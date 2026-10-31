Imports System.Configuration
Imports System.Data.SqlClient
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Web.Hosting
Imports Newtonsoft.Json

' スタンプの目次（どのセットに、どのスタンプがあるか）を保持するクラス
'
' 【真実はDB側にあり、メモリはただのキャッシュ】
' SiteSettings・IconCatalog とまったく同じ考え方で作ってある。
' 目次は StampSets / Stamps テーブルが持ち、このクラスは読んだ結果を抱えているだけ。
' Reload() で捨てて、次に読まれたときに読み直す。
' **利用者がスタンプを何回押しても、DBは見に行かない**（読むのはアプリが起きて最初の1回）
'
' 【絵そのものは持たない】
' 絵は Content/Stamps/<SetCode>/<FileName> のファイルで、ブラウザが <img> で直接取りに行く
' （IISが静的ファイルとして返すので、アプリを1行も通らない）。
' このクラスが知っているのは「どのファイルが、どのスタンプか」だけ
'
' 【なぜ、Application_Start で読まないのか（遅延読み込み）】
' 共用サーバーはアクセスが無いとアプリを寝かせ、次のアクセスで起こす。
' 起動のタイミングをこちらで選べないため、たまたまDBが詰まっていると
' アプリごと起動できなくなる。遅延なら、次に読まれたときにやり直せる（SiteSettings と同じ）
'
' 【DBが読めなくても、止まらない（フェイルオープン）】
' 読めなければ「スタンプが1つも無い目次」で動き続ける。部屋は普通に開き、
' スタンプのタブが空になるだけである。1分後（RetryAfterFailure）に読み直す。
' テーブルがまだ無いDB（本番へ流す前）でも、同じ経路に落ちる
'
' 【罠】ChatHub の SyncLock の中から呼ばないこと
' キャッシュが空のときはDBへ問い合わせに行く。鍵を握ったままDBを待つと、
' その間、全部屋のチャットが止まる（SiteSettings と同じ注意）
Public Class StampCatalog

    ' 接続文字列の名前（SiteSettings・ChatLog など、他の箇所と揃えている）
    Private Const ConnectionName As String = "VbChatConnection"

    ' 絵のファイルを置く場所。この下に <SetCode>/<FileName> で置く
    Private Const StampsVirtualRoot As String = "~/Content/Stamps/"

    ' DBが読めなかったあと、次に読み直しを試みるまでの間隔（SiteSettings と同じ1分）
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)

    ' 画面へ渡すJSONを、作り直すまでの間隔
    '
    ' 【なぜ作り直すのか】
    ' JSONには、絵のURLに ?v=（ファイルの最終更新時刻）を付けて入れている。
    ' 本番は静的ファイルを30日ためさせるので、印が無いと、絵を描き直して発行しても
    ' 30日間ブラウザに届かない（common.AssetUrl と同じ理由）。
    ' ところが **Content の下のファイルだけを発行しても、アプリは再起動しない**。
    ' DBの目次は変わらないので、ここでファイルの時刻を見直さないと、古い印を配り続ける。
    '
    ' 【なぜ毎回ではなく、1分おきなのか】
    ' AssetUrl は1ページに多くて4回だが、ここはスタンプの数だけ時刻を読むことになる
    ' （今は30個、セットが増えれば何百にもなる）。部屋を開くたびにそれをやらず、
    ' 作ったJSONを1分だけ使い回す。描き直した絵は、発行から1分以内に印が変わる
    Private Shared ReadOnly ClientJsonLifetime As TimeSpan = TimeSpan.FromMinutes(1)

    ' SetCode・StampCode として認める形（英小文字・数字・ハイフン。例: frog / frog-01）
    '
    ' 【なぜ確かめるのか】
    ' SetCode はURLのフォルダ名に、StampCode は発言の [stamp:frog-01] にそのまま入る。
    ' 行はこちらのスクリプト（素材から Database/InsertData/Stamps.sql を書き出す道具）が入れるが、
    ' 「入口の検査だけでは足りない。出口でも確かめる」の決まりどおり、読む側でも見る。
    ' 外れた行は使わず、Trace に理由を残す（黙って直すと、誤りに永久に気づけない）
    '
    ' 【末尾は $ ではなく \z】
    ' $ は「末尾の改行の手前」にも当てはまるため、"frog" & 改行 が通ってしまう
    Private Shared ReadOnly CodePattern As New Regex(
        "^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.Compiled)

    ' FileName として認める形（例: 01-konnichiwa.svg）
    '
    ' 英数字・ハイフン・下線と、拡張子1つだけ。
    ' 「..」も「/」も「\」も、この形には当てはまらない＝フォルダの外を指せない。
    ' 拡張子は、<img> で出せる画像の形式に限る（SVGのほか、PNGや手描きの絵も混ぜられるように）
    Private Shared ReadOnly FileNamePattern As New Regex(
        "^[A-Za-z0-9][A-Za-z0-9_-]*\.(svg|png|gif|jpg|jpeg|webp)\z",
        RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    ' 読み上げ用の文言が空だったときに使う文言
    Private Const FallbackLabel As String = "スタンプ"


    ' === キャッシュ ===
    '
    ' 【値をひとつのオブジェクト（Snapshot）にまとめている】
    ' SiteSettings と同じ。完全に組み立ててから1回の代入で公開するので、
    ' 鍵の外から読んだ他のスレッドは「古い目次」か「完成した新しい目次」しか見ない
    Private Shared Cached As Snapshot = Nothing

    ' 画面へ渡すJSONと、それをいつ・どの目次から作ったか
    '
    ' 【Snapshot の中に持たない理由】
    ' Snapshot は「DBの中身」で、Reload() まで変わらない。
    ' JSON は「DBの中身＋ファイルの時刻」で、1分おきに作り直す。
    ' 寿命が違うものを1つにまとめると、Snapshot を書き換えることになる
    '
    ' 鍵は取らない。2つのスレッドが同時に作り直しても、同じものが2回できるだけで害が無い
    ' （中身を書き換えない小さなオブジェクトを、1回の代入で差し替えている）
    Private Shared CachedJson As ClientJsonEntry = Nothing

    ' キャッシュを触るときの排他制御用
    Private Shared ReadOnly CacheLock As New Object()


    ' === 公開するもの ===

    ' 画面（JavaScript）へ渡す目次のJSON
    '
    ' 【形】
    '   {
    '     "sets":   [ { "code": "frog", "name": "カエル", "stamps": ["frog-01", ...] }, ... ],
    '     "stamps": { "frog-01": { "src": "/Content/Stamps/frog/01-konnichiwa.svg?v=...",
    '                              "label": "こんにちは" }, ... }
    '   }
    '
    '   sets   … パネルに並べるもの。IsActive が 1 のセットの、IsActive が 1 のスタンプだけ。
    '            並び順は、サーバーが並べた配列の順そのもの（並び順の数字は渡さない。
    '            画面側でもう一度並べ直すと、並びを決める場所が2つになる）
    '   stamps … 絵を引くための表。**IsActive が 0 のものも入れる**。
    '            IsActive = 0 は「これから選べなくする」だけで、過去ログに残っている
    '            [stamp:xxx] は、そのまま絵として出す約束だから。
    '            ここから外すと、過去ログの絵が引けなくなる
    '
    ' 【絵のファイルが無いスタンプは、どちらにも入れない】
    ' 目次（DB）には在るのにファイルが無い＝発行し忘れた、など。
    ' パネルに出すと、押しても壊れた絵が全員に届く。Trace に理由を残して外す
    '
    ' 【URL は、サーバーが組み立てて渡す】
    ' ファイル名だけ渡して画面で組み立てると、置き場所の決まりを2か所で持つことになる。
    ' ?v= の印もサーバーでしか作れない
    Public Shared ReadOnly Property ClientJson As String
        Get
            Dim snapshot As Snapshot = GetSnapshot()
            Dim entry As ClientJsonEntry = CachedJson

            If entry IsNot Nothing AndAlso entry.IsFreshFor(snapshot) Then
                Return entry.Json
            End If

            entry = New ClientJsonEntry(snapshot, BuildClientJson(snapshot))
            CachedJson = entry
            Return entry.Json
        End Get
    End Property


    ' そのスタンプを、今パネルから送ってよいか
    '
    ' 目次に在り、スタンプもセットも IsActive が 1 のときだけ True。
    ' 発言に [stamp:xxx] を受け付けるかどうかを、サーバー側で決めるために使う
    ' （[stamp:xxx] は利用者が手で打てるので、画面に出していないことは防御にならない）。
    '
    ' 【絵のファイルの有無は見ない】
    ' ここは発言のたびに呼ばれうるので、ディスクを見に行かない。
    ' ファイルが無いスタンプは、そもそもパネルに出ていない（上の ClientJson を参照）
    Public Shared Function IsSendable(stampCode As String) As Boolean

        If String.IsNullOrEmpty(stampCode) Then
            Return False
        End If

        Dim stamp As StampEntry = Nothing
        Return GetSnapshot().StampsByCode.TryGetValue(stampCode, stamp) AndAlso stamp.IsSendable

    End Function


    ' ============================================================
    ' 発言の本文に入る印
    ' ============================================================
    '
    '     [stamp:frog-01]
    '
    ' 【スタンプの発言は、本文がこの印だけである】
    ' 画像の印（SiteImage の [img:…]）と、まったく同じ考え方で作ってある。
    ' 種別（10＝部屋、11＝こっそり）は「誰に向けた発言か」を表すので、
    ' スタンプのために種別を増やさない。印だけにしておけば、宛先の列も、
    ' こっそりの窓への配信も、過去ログの絞り込みも、1つも足さずにそのまま効く
    '
    ' 【印は、利用者に組み立てさせない。サーバーが作る】
    ' ChatHub.SendStamp がスタンプの名前だけを受け取り、ここで組み立てる。
    ' そのうえで、普通の発言が「[stamp:」を含んでいたら断る（下の ContainsToken）
    '
    ' 【画面側にも、同じ形がある】
    ' Scripts/chat-room.js の STAMP_TOKEN_PATTERN。形を変えるときは両方を直すこと
    Private Const TokenPrefix As String = "[stamp:"

    ' 印を組み立てる（サーバーだけが呼ぶ）
    '
    ' 【必ず IsSendable を通した名前を渡すこと】
    ' 名前の形（英小文字・数字・ハイフン）は、目次に入る時点で CodePattern が確かめている。
    ' 目次に在る名前しか渡らなければ、印の形が崩れることは無い
    Public Shared Function BuildToken(stampCode As String) As String
        Return TokenPrefix & stampCode & "]"
    End Function

    ' 本文のどこかに、スタンプの印らしきものが混ざっているか
    '
    ' 【何のためにあるのか】
    ' これが無いと、誰でも「[stamp:frog-01]」と打つだけで、パネルを通らずに
    ' スタンプを送れる。止めたスタンプ（IsActive = 0）も、手で打てば送れてしまう。
    '
    ' 【前後に何かあっても捕まえること】
    ' 「これは [stamp:…] です」のように文章へ紛れ込ませる形も断る
    ' （SiteImage.ContainsToken と、まったく同じ形）
    Public Shared Function ContainsToken(text As String) As Boolean

        If String.IsNullOrEmpty(text) Then
            Return False
        End If

        Return text.IndexOf(TokenPrefix, StringComparison.OrdinalIgnoreCase) >= 0

    End Function


    ' キャッシュを捨てる（次に読まれたときに、DBから読み直される）
    '
    ' 【必ず呼ぶこと】将来、管理画面からスタンプを足す・止める処理を書いたら、
    ' 保存の直後にこれを呼ぶ。今は管理画面が無いので、
    ' DBの目次を変えたら、アプリを再起動するまで効かない（SiteSettings と同じ）
    Public Shared Sub Reload()

        SyncLock CacheLock
            Cached = Nothing
            CachedJson = Nothing
        End SyncLock

    End Sub


    ' キャッシュを返す。まだ無ければ、DBから読んで作る
    '
    ' 【SiteSettings.GetSnapshot と、まったく同じ形】
    ' 最初の1回は鍵が空くまで待つ（返せるものが何も無いため）。
    ' 読み直し（前回失敗して、1分経った）は TryEnter にして、鍵が取れなければ待たずに
    ' 今持っている空の目次を返す。
    '
    ' 【罠】読み直しで SyncLock を使ってはいけない。
    ' DBが落ちていると、接続のタイムアウト（既定15秒）まで返ってこない。
    ' SyncLock にすると、その間に部屋を開こうとした人が全員この鍵の前に並ぶ
    Private Shared Function GetSnapshot() As Snapshot

        Dim snapshot As Snapshot = Cached

        If snapshot IsNot Nothing AndAlso Not snapshot.ShouldRetry() Then
            Return snapshot
        End If

        If snapshot Is Nothing Then

            SyncLock CacheLock

                If Cached IsNot Nothing Then
                    Return Cached
                End If

                Cached = Load()
                Return Cached

            End SyncLock

        End If

        If Not Monitor.TryEnter(CacheLock) Then
            Return snapshot
        End If

        Try

            If Cached IsNot Nothing AndAlso Not Cached.ShouldRetry() Then
                Return Cached
            End If

            Cached = Load()
            Return Cached

        Finally

            ' 【必ず Finally で解放すること】TryEnter は自動では解放してくれない
            Monitor.Exit(CacheLock)

        End Try

    End Function


    ' DBから目次を読んで、Snapshot を組み立てる
    '
    ' 【必ず SyncLock の中から呼ぶこと】
    Private Shared Function Load() As Snapshot

        Try
            Return BuildSnapshot(ReadFromDatabase(), isFailed:=False)

        Catch ex As Exception

            ' 【ここで例外を投げないこと】投げると、部屋のページがそのまま500になる。
            ' エラー報告メールへ繋いだ。鍵の中だが、ErrorReport は積むだけ
            ErrorReport.Report(ex, "StampCatalog.Load", "スタンプの無い目次で動作します")

            Return BuildSnapshot(New List(Of StampRow), isFailed:=True)

        End Try

    End Function


    ' DBから読んだ1行（確かめる前の、生の値）
    Private NotInheritable Class StampRow
        Public StampCode As String
        Public SetCode As String
        Public FileName As String
        Public Label As String
        Public IsActive As Boolean
        Public SetDisplayName As String
        Public SetIsActive As Boolean
    End Class


    ' 目次を丸ごと1回で読む
    '
    ' 行数は数十〜数百行にしかならないため、必要なものを1件ずつ引かず、まとめて1回で取る。
    ' 並び順はSQLで決める（セットの順 → セットの中の順）。
    ' 同じ数字が並んだときの順番も決まるように、名前を2つ目のキーにしてある
    '
    ' スタンプが1枚も無いセットは、JOIN で落ちる（空のタブを出さない）
    Private Shared Function ReadFromDatabase() As List(Of StampRow)

        Const sql As String =
            "SELECT s.StampCode, s.SetCode, s.FileName, s.Label, s.IsActive, " &
            "       t.DisplayName, t.IsActive " &
            "FROM Stamps AS s " &
            "INNER JOIN StampSets AS t ON t.SetCode = s.SetCode " &
            "ORDER BY t.SortOrder, t.SetCode, s.SortOrder, s.StampCode"

        Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
        Dim rows As New List(Of StampRow)

        Using conn As New SqlConnection(connStr)
            Using cmd As New SqlCommand(sql, conn)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        rows.Add(New StampRow With {
                            .StampCode = reader.GetString(0),
                            .SetCode = reader.GetString(1),
                            .FileName = reader.GetString(2),
                            .Label = reader.GetString(3),
                            .IsActive = reader.GetBoolean(4),
                            .SetDisplayName = reader.GetString(5),
                            .SetIsActive = reader.GetBoolean(6)
                        })

                    End While
                End Using

            End Using
        End Using

        Return rows

    End Function


    ' 読んだ行を確かめて、Snapshot を組み立てる
    '
    ' 形の外れた行は使わない（CodePattern・FileNamePattern のコメントを参照）。
    ' 1行が外れても、他の行は普通に使える（スタンプは1枚ずつ別のファイルなので、
    ' アイコンのスプライトのように「1行壊れると全部消える」ことはない）
    Private Shared Function BuildSnapshot(rows As List(Of StampRow), isFailed As Boolean) As Snapshot

        Dim stamps As New List(Of StampEntry)
        Dim sets As New List(Of StampSetEntry)
        Dim setsByCode As New Dictionary(Of String, StampSetEntry)(StringComparer.Ordinal)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

        For Each row As StampRow In rows

            If Not CodePattern.IsMatch(row.StampCode) OrElse
               Not CodePattern.IsMatch(row.SetCode) OrElse
               Not FileNamePattern.IsMatch(row.FileName) Then

                ErrorReport.ReportProblem("StampCatalog.BuildSnapshot（行の形）",
                    "Stamps の行の形が正しくないため、使いません：" &
                    row.StampCode & " / " & row.SetCode & " / " & row.FileName)
                Continue For

            End If

            ' 主キーなので重なることは無いはずだが、念のため
            ' （DBの照合順序は大文字・小文字を区別しないが、上の形で小文字に限っている）
            If Not seen.Add(row.StampCode) Then
                Continue For
            End If

            Dim label As String = If(row.Label, String.Empty).Trim()
            If label.Length = 0 Then
                label = FallbackLabel
            End If

            Dim stampSet As StampSetEntry = Nothing
            If Not setsByCode.TryGetValue(row.SetCode, stampSet) Then

                Dim setName As String = If(row.SetDisplayName, String.Empty).Trim()
                If setName.Length = 0 Then
                    setName = row.SetCode
                End If

                stampSet = New StampSetEntry(row.SetCode, setName, row.SetIsActive)
                setsByCode.Add(row.SetCode, stampSet)
                sets.Add(stampSet)

            End If

            Dim stamp As New StampEntry(
                code:=row.StampCode,
                setCode:=row.SetCode,
                virtualPath:=StampsVirtualRoot & row.SetCode & "/" & row.FileName,
                label:=label,
                isSendable:=row.IsActive AndAlso row.SetIsActive)

            stamps.Add(stamp)

            If stamp.IsSendable Then
                stampSet.StampCodes.Add(stamp.Code)
            End If

        Next

        Return New Snapshot(stamps, sets, isFailed)

    End Function


    ' 画面へ渡すJSONを組み立てる（形は ClientJson のコメントを参照）
    '
    ' ここだけ、スタンプの数だけファイルの時刻を読む。
    ' 呼ばれるのは ClientJsonLifetime（1分）に1回まで
    Private Shared Function BuildClientJson(snapshot As Snapshot) As String

        ' 絵のファイルが在ったスタンプの src と label
        Dim stamps As New Dictionary(Of String, Object)(StringComparer.Ordinal)

        For Each stamp As StampEntry In snapshot.Stamps

            If Not FileExists(stamp.VirtualPath) Then

                ErrorReport.ReportProblem("StampCatalog.BuildClientJson（絵のファイル）",
                    "スタンプの絵のファイルが見つからないため、目次から外します：" & stamp.VirtualPath)
                Continue For

            End If

            ' ?v= の印の付け方は、common.AssetUrl の1か所に任せる（書式を2か所で持たない）
            stamps.Add(stamp.Code, New With {
                .src = AssetUrl(stamp.VirtualPath),
                .label = stamp.Label
            })

        Next

        ' パネルに並べるもの。絵の在るものだけ。1枚も残らないセットは出さない
        Dim sets As New List(Of Object)

        For Each stampSet As StampSetEntry In snapshot.Sets

            If Not stampSet.IsActive Then
                Continue For
            End If

            Dim codes As New List(Of String)
            For Each code As String In stampSet.StampCodes
                If stamps.ContainsKey(code) Then
                    codes.Add(code)
                End If
            Next

            If codes.Count = 0 Then
                Continue For
            End If

            sets.Add(New With {
                .code = stampSet.Code,
                .name = stampSet.DisplayName,
                .stamps = codes
            })

        Next

        Dim json As String = JsonConvert.SerializeObject(New With {
            .sets = sets,
            .stamps = stamps
        })

        ' <script> の中へ <%= %> でそのまま埋めるため、"</script>" で閉じられないようにする
        ' （ChatLogReader.ToScriptJson と同じ手当て）
        Return json.Replace("<", "\u003c")

    End Function


    ' 絵のファイルが在るか
    '
    ' 【物理パスの解決に HttpContext を使わない】IconCatalog と同じ理由
    ' （HostingEnvironment なら、どこから呼ばれても解決できる）
    Private Shared Function FileExists(virtualPath As String) As Boolean

        Try
            Dim path As String = HostingEnvironment.MapPath(virtualPath)
            Return path IsNot Nothing AndAlso File.Exists(path)
        Catch
            Return False
        End Try

    End Function


    ' スタンプ1枚（確かめ終わった値）
    Private NotInheritable Class StampEntry

        Public ReadOnly Code As String
        Public ReadOnly SetCode As String
        Public ReadOnly VirtualPath As String
        Public ReadOnly Label As String

        ' 今パネルから送ってよいか（スタンプもセットも IsActive が 1）
        Public ReadOnly IsSendable As Boolean

        Public Sub New(code As String, setCode As String, virtualPath As String,
                       label As String, isSendable As Boolean)
            Me.Code = code
            Me.SetCode = setCode
            Me.VirtualPath = virtualPath
            Me.Label = label
            Me.IsSendable = isSendable
        End Sub

    End Class


    ' セット1つ（確かめ終わった値）
    '
    ' StampCodes は組み立ての途中でだけ足す。Snapshot に入ったあとは書き換えない
    Private NotInheritable Class StampSetEntry

        Public ReadOnly Code As String
        Public ReadOnly DisplayName As String
        Public ReadOnly IsActive As Boolean

        ' パネルに並べるスタンプ（IsActive が 1 のもの）を、並び順どおりに
        Public ReadOnly StampCodes As New List(Of String)

        Public Sub New(code As String, displayName As String, isActive As Boolean)
            Me.Code = code
            Me.DisplayName = displayName
            Me.IsActive = isActive
        End Sub

    End Class


    ' 読んだ目次（中身は書き換えない。変えるときは Reload() で捨てて作り直す）
    Private NotInheritable Class Snapshot

        ' すべてのスタンプ（IsActive が 0 のものも）。並び順どおり
        Public ReadOnly Stamps As List(Of StampEntry)

        ' 同じものを、名前で引けるようにしたもの
        Public ReadOnly StampsByCode As Dictionary(Of String, StampEntry)

        ' すべてのセット（IsActive が 0 のものも）。並び順どおり
        Public ReadOnly Sets As List(Of StampSetEntry)

        ' このSnapshotが「DBを読めなかった結果」であるかどうか
        Private ReadOnly IsFailed As Boolean

        ' 組み立てた時刻（読み直しを試みる判断に使う）
        Private ReadOnly CreatedAt As DateTime

        Public Sub New(stamps As List(Of StampEntry), sets As List(Of StampSetEntry), isFailed As Boolean)

            Me.Stamps = stamps
            Me.Sets = sets
            Me.StampsByCode = New Dictionary(Of String, StampEntry)(StringComparer.Ordinal)
            For Each stamp As StampEntry In stamps
                Me.StampsByCode(stamp.Code) = stamp
            Next

            Me.IsFailed = isFailed
            Me.CreatedAt = DateTime.Now

        End Sub

        ' 読めなかったSnapshotだけ、しばらく経ったら読み直しを試みる
        Public Function ShouldRetry() As Boolean

            If Not IsFailed Then
                Return False
            End If

            Return DateTime.Now - CreatedAt >= RetryAfterFailure

        End Function

    End Class


    ' 作ったJSONと、それをいつ・どの目次から作ったか（中身は書き換えない）
    Private NotInheritable Class ClientJsonEntry

        Public ReadOnly Json As String
        Private ReadOnly Source As Snapshot
        Private ReadOnly BuiltAt As DateTime

        Public Sub New(source As Snapshot, json As String)
            Me.Source = source
            Me.Json = json
            Me.BuiltAt = DateTime.Now
        End Sub

        ' 同じ目次から作っていて、作ってから1分以内なら、そのまま使ってよい
        Public Function IsFreshFor(snapshot As Snapshot) As Boolean
            Return Source Is snapshot AndAlso DateTime.Now - BuiltAt < ClientJsonLifetime
        End Function

    End Class

End Class
