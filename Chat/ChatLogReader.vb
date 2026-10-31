Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.Linq
Imports Newtonsoft.Json

' 過去ログ1件分を、クライアントへ渡すためのクラス
' ChatMessagesテーブルの1行に対応するが、クライアントに渡してよい情報だけを持つ
' （IpAddress・UserId・BrowserIdなど、サーバー内部でしか使わない情報は含めない）
'
' 各プロパティに<JsonProperty>でJS側と同じキャメルケースのキー名を個別指定している
' （ParticipantInfo・JoinRoomResultと同じ方針。グローバルなシリアライザ設定は
'   SignalR自身の内部プロトコルまで巻き込んで壊してしまうため、採用しない）
Public Class ChatLogEntry

    ' ログの種別（10=発言、20=入室、30番台=退室系）
    ' クライアントは、この値を見て「吹き出し」と「イベント行」を描き分ける
    <JsonProperty("messageType")>
    Public Property MessageType As Integer

    ' 発言した当時のLocalId
    ' 【重要】これを「自分の発言かどうか」の判定に使ってはいけない（IsSelfの説明を参照）
    <JsonProperty("localId")>
    Public Property LocalId As Integer

    ' 発言した当時のニックネーム・アイコン（ログに焼き込まれたスナップショット）
    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    ' 発言の本文。入退室などのイベント行ではNothing
    <JsonProperty("message")>
    Public Property Message As String

    ' 入退室などのイベント行に表示する文言（発言の行ではNothing）
    '
    ' 文言はサーバー側（ChatEventText）で組み立てて渡す。
    ' クライアント側で組み立てると、リアルタイム受信ぶんとで同じ日本語を2か所で
    ' 管理することになり、片方だけ直して食い違う事故が起きるため
    <JsonProperty("eventText")>
    Public Property EventText As String

    ' 発言した当時の時刻を "HH:mm" に整形したもの
    ' 【罠】クライアント側で new Date() から作り直してはいけない。
    ' 過去の発言が、すべて「ページを開いた時刻」になってしまうため
    <JsonProperty("time")>
    Public Property Time As String

    ' 発言した当時の日付を "" の形にしたもの
    ' 日付が変わる位置に区切り行を入れるために使い、そのまま区切り行の文字としても表示する
    '
    ' 【罠】クライアント側で new Date() から作り直してはいけない。
    ' 時刻とまったく同じ理由で、過去の行がすべて「今日」になり、
    ' 区切り行が1本も入らなくなってしまう
    <JsonProperty("date")>
    Public Property [Date] As String

    ' 自分自身の発言かどうか（吹き出しを左右どちらに寄せるかの判定に使う）
    '
    ' 【罠】LocalIdで判定してはいけない。LocalIdは入室のたびに新しい番号が
    ' 発行される（欠番方式）ため、昨日の自分の発言が相手側の吹き出しになってしまう。
    ' ChatMessages.SenderBrowserIdとCookieのChatBrowserIdを突き合わせ、
    ' サーバー側で確定させた値をそのまま使う
    <JsonProperty("isSelf")>
    Public Property IsSelf As Boolean

    ' 「同じ人かどうか」だけが分かる、使い捨ての印（AnonIdクラスを参照）
    '
    ' 無視機能で、クライアントが「この行はあの人の行か」を判定するために使う。
    ' リアルタイムで届く発言・入退室にも、まったく同じ値が付いている
    ' （そうでないと「今の発言は消えるのに、過去ログだけ消えない」という形で壊れる）。
    '
    ' 【BrowserIdそのものは、絶対にクライアントへ渡さない】
    ' あれは本人証明であり、他人の値を知ればなりすませる
    <JsonProperty("anonId")>
    Public Property AnonId As String

    ' こっそりの発言（種別11）の、宛先の匿名ID。それ以外の行では常にNothing
    '
    ' 【なぜ、送り主の印だけでは足りないのか】
    ' クライアントは「誰との窓か」で行を振り分ける。相手から来た行は
    ' 送り主（AnonId）が相手だが、**自分が送った行は送り主が自分**なので、
    ' AnonId だけでは、どの窓に入れるのかが分からない。
    ' 窓のキーは「相手の匿名ID」＝ IsSelf なら RecipientAnonId、そうでなければ AnonId
    '
    ' 【ここにも生のBrowserIdは入らない】AnonId とまったく同じ理由である
    <JsonProperty("recipientAnonId")>
    Public Property RecipientAnonId As String

    ' その行を書いた時点の、書いた人のトリップ（16進10桁。無ければ Nothing）
    '
    ' 【ログに焼き込まれた値をそのまま渡す。今の値を引き直さない】
    ' 引き直すと、過去ログが全部今の値に化け、「同じ人だった」の証明にならない。
    '
    ' 【トリップ機能が止まっているときは、焼き込まれていても渡さない】
    ' 「止まっているときは一切渡さない」と決めてある（リアルタイムの配信にも出ないので、
    ' 過去ログにだけ出ると食い違う）。
    '
    ' 【無いときは、キーごと送らない】（NullValueHandling.Ignore）
    ' 200行の過去ログで "trip":null を並べる必要が無い。
    ' 入退室の行にも載るが、画面に出すのは発言の行だけ
    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

    ' その行を書いた人のアカウントのユーザー名（アカウントID を表示して書いた行だけ。無ければキーごと送らない）
    '
    ' 画面は「@ユーザー名」と出し、押すと公開のプロフィールが開く。トリップとは、どちらか一方。
    '
    ' 【名前・トリップと違って、焼き込んでいない。読むたびに Accounts から引く】
    ' ログの行が持っているのはアカウントの番号（ChatMessages.UserId）だけで、ユーザー名は読むときに引く。
    '   ・ユーザー名は変えられない（記事の URL に使うため）ので、「その時点の値」を焼き込む意味が無い
    '   ・**退会した・止めたアカウントは、その場で出なくなる**（リンク先の無い ID を、過去ログに残さない）。
    '     焼き込むと、退会した人のユーザー名が、ログの保存期間のあいだ残り続ける
    '   ・列を足さずに済む（UserId は最初から在った列。アカウントができる日のために空けてあった）
    <JsonProperty("accountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property AccountName As String

End Class


' ChatMessagesテーブルから、過去ログを読み出すためのクラス
'
' 呼び出し元が2か所（ChatRoom.aspx.vbのPage_Loadと、表示件数の変更用WebMethod）に
' なるため、最初から共通化してある。
' 将来「もっと過去を読む」ボタンを足す際にも、ここを使い回す
Public Class ChatLogReader

    ' 表示行数として許可する値の一覧
    ' 想定外の値がそのままSQLのTOP句へ渡らないよう、白リストで丸めるために使う
    '
    ' 【SiteSettingsへは移していない。理由は3つ】
    '  1. これは「値」ではなく**白リスト＝防御**である。SiteSettingsの決まりごとで、
    '     丸めの範囲はコード側に持つと決めてある。DBへ出すと管理画面から広げられ、
    '     極端な行数がそのままSQLのTOP句へ渡る道ができてしまう
    '  2. 一覧を変えることは、セレクトボックスの選択肢を変えることと同義であり、
    '     運用の設定というより画面の作りの話である
    '  3. 下のDefaultMessageCountとの噛み合わせを、コード側で守れなくなる
    '     （既定値が一覧に無いと、どの選択肢とも一致しない状態になる）
    '
    ' "30,50,100,200" のような文字列でDBに入れて読む側で分ける形も検討したが、
    ' 上の3つに見合う利点が無かったため採らなかった
    Public Shared ReadOnly AllowedMessageCounts As Integer() = {30, 50, 100, 200}

    ' 一覧にも設定にも頼れなくなったときの、最後の受け皿
    ' （AllowedMessageCountsに必ず含まれている値にしておくこと）
    Private Const FallbackMessageCount As Integer = 50


    ' 表示行数の既定値。Cookie ChatLogCount が無ければ、この値を使う
    ' （数えるのは「画面に出る行」。発言も入退室のお知らせ行も、まとめてこの数に入る）
    '
    ' 【SiteSettingsへ移した】
    ' 値はSiteSettings.DefaultMessageCount（既定50）が持つ。
    '
    ' 【罠】既定値が一覧（AllowedMessageCounts）に無いと、白リストが無力になる
    ' NormalizeMessageCountは「一覧に無ければ既定値を返す」という作りである。
    ' 既定値そのものが一覧に無いと、一覧に無い値を渡されたときに
    ' 「一覧に無い既定値」を返すことになり、丸めた意味が消える。
    ' そこでここで一覧との突き合わせを行い、外れていたら受け皿へ落とす。
    ' 突き合わせを一覧の在り処（このクラス）で行うのは、一覧を持っているのが
    ' ここだからである（SiteSettings側からは一覧が見えない）
    Public Shared ReadOnly Property DefaultMessageCount As Integer
        Get
            Dim configured As Integer = SiteSettings.DefaultMessageCount

            If AllowedMessageCounts.Contains(configured) Then
                Return configured
            End If

            ErrorReport.ReportProblem("SiteSettings.DefaultMessageCount",
                "SiteSettings の DefaultMessageCount（" & configured &
                "）が、選べる行数の一覧にありません。" & FallbackMessageCount & " を使います。")

            Return FallbackMessageCount

        End Get
    End Property

    ' DBから何行取ってくるかの倍率（表示したい行数の何倍を取るか）
    '
    ' 【なぜ、多めに取ってから絞るのか】
    ' 種別33（同一ブラウザの置き換え）とその直後の入室(20)は、DBには残るが画面には出さない。
    ' そのため単純に「直近N行」を取ると、その見えない行が枠を食ってしまい、
    ' 「30行を選んだのに、20行しか出ない」という分かりにくい状態になる。
    ' 余裕を持って多めに取り、隠す行を落としたあとでN行に切り詰めることで、
    ' 「画面に出る行が、ちょうどN行」になる
    '
    ' SiteSettings.LogFetchRatio（既定2）へ移した
    Private Shared ReadOnly Property FetchRatio As Integer
        Get
            Return SiteSettings.LogFetchRatio
        End Get
    End Property


    ' 渡された表示件数を、許可された値に丸める
    ' （画面のセレクトボックス以外の経路から極端な値を送られても、困らないようにするため）
    Public Shared Function NormalizeMessageCount(messageCount As Integer) As Integer

        If AllowedMessageCounts.Contains(messageCount) Then
            Return messageCount
        End If

        Return DefaultMessageCount

    End Function


    ' 過去ログを読み出す（DB接続を自前で開く版）
    ' 表示件数の変更用WebMethodのように、他にDBを触る用事が無い呼び出し元から使う
    Public Shared Function GetRecentMessages(roomId As Integer,
                                             messageCount As Integer,
                                             myBrowserId As String) As List(Of ChatLogEntry)

        Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        Using conn As New SqlConnection(connStr)
            conn.Open()
            Return GetRecentMessages(conn, roomId, messageCount, myBrowserId)
        End Using

    End Function


    ' 過去ログを読み出す（既に開いてあるDB接続を使い回す版）
    ' ChatRoom.aspx.vbのPage_Loadから、部屋名の取得で開いた接続に相乗りさせるために使う
    ' （1回のページ読み込みで、DBへ何度も繋ぎ直さないため）
    '
    ' roomId       … どの部屋のログを読むか
    ' messageCount … 画面に表示する「行数」。発言も、入退室のお知らせ行も、まとめてこの数に数える
    ' myBrowserId  … 見ている人のChatBrowserId Cookieの値。IsSelfの判定に使う
    Public Shared Function GetRecentMessages(conn As SqlConnection,
                                             roomId As Integer,
                                             messageCount As Integer,
                                             myBrowserId As String) As List(Of ChatLogEntry)

        Dim result As New List(Of ChatLogEntry)

        ' 各行のBrowserId（resultと同じ並び順）
        ' ChatLogEntryには持たせない（クライアントへ渡してよい情報ではないため）が、
        ' 「置き換え(33)の直後の入室(20)を隠す」判定に必要なため、ここで別に控えておく
        Dim browserIds As New List(Of Guid?)

        ' 想定外の件数がSQLへ渡らないよう、必ず白リストで丸めてから使う
        messageCount = NormalizeMessageCount(messageCount)

        ' 自分の発言かどうかの判定に使う、自分のBrowserId
        ' Cookieが無い・壊れている場合は判定できないため、全件が「相手の発言」になる
        ' （吹き出しが左寄せになるだけで、チャット自体は問題なく使える）
        Dim myBrowserGuid As Guid
        Dim hasMyBrowserGuid As Boolean = Guid.TryParse(myBrowserId, myBrowserGuid)

        ' トリップを画面へ渡してよいか（止まっているときは、焼き込まれていても渡さない）
        Dim isTripEnabled As Boolean = TripCode.IsEnabled

        ' ============================================
        ' ① 直近の行を、種別を問わずまとめて取る
        '
        ' 表示したい行数（messageCount）のFetchRatio倍を取っておき、
        ' 画面に出ない行を落としたあとで、N行に切り詰める
        ' （多めに取る理由は、FetchRatioの宣言のところに書いてある）
        '
        ' 並び順にCreatedAtではなくMessageIdを使うのは、同一時刻の発言が複数あっても
        ' 順序が一意に定まるため（ChatMessagesテーブルの設計方針どおり）。
        ' 内側で新しい側からTOPを取り、外側で昇順（＝古い順）に並べ替えている
        ' ============================================
        ' 【必須】こっそりの発言（種別11）は、ここでは絶対に読まない。
        '
        ' 部屋の過去ログは、その部屋に居る全員へ配る。当事者でない人にも配る。
        ' 種別で外さないと、**他人のこっそりの本文が、そのまま部屋のログに並ぶ**。
        '
        ' 【無視機能とは、向きが逆である】
        ' あちらは「サーバーは無視リストを知らない。クライアント側で隠す」。
        ' こっそりは**そもそも送ってはいけない**ので、サーバー側で外す。
        ' クライアントに隠させる形にすると、F12を開けば全部読める
        '
        ' 【自分が当事者のこっそりも、ここには混ぜない】
        ' あちらは窓に出すものであって、部屋のログに出すものではない。
        ' 読むのは GetRecentPrivateMessages のほうである
        '
        ' 【40番台（入室できなかった記録・ノック）も、ここで外す】
        ' どれも画面に出ない行（ChatEventText.Build が Nothing）なので、取っても捨てるだけである。
        ' 取ると「多めに取る」枠（FetchRatio）を食い、指定した行数に届かなくなる
        ' （満員の部屋へのノックが続いた部屋ほど、過去ログが短く見える）。
        ' こっそりと違って、読んでも害は無い（出さないだけ）。外しているのは、枠を食わせないためである
        '
        ' 【アカウントのユーザー名は、外側で Accounts から引く】
        ' 行が持っているのは番号（UserId）だけ。止めていないアカウント（IsActive = 1）のユーザー名だけを付ける。
        ' 退会した人は行そのものが無いので、付かない（＝過去ログからアカウントID が消える。ChatLogEntry.AccountName）。
        ' 引くのは主キー（AccountId）で、UserId が入っている行のぶんだけ。ほとんどの行は NULL なので、何も引かない
        Dim sql As String =
            "SELECT t.*, a.UserName AS AccountName FROM (" &
            "    SELECT TOP (@FetchCount) MessageId, MessageType, LocalId, SenderBrowserId, Nickname, " &
            "           IconType, IconColor, Trip, MessageText, CreatedAt, UserId " &
            "    FROM ChatMessages " &
            "    WHERE RoomId = @RoomId " &
            "      AND MessageType <> @PrivateType " &
            "      AND (MessageType < 40 OR MessageType >= 50) " &
            "    ORDER BY MessageId DESC" &
            ") AS t " &
            "LEFT JOIN Accounts a ON a.AccountId = t.UserId AND a.IsActive = 1 " &
            "ORDER BY t.MessageId"

        Using cmd As New SqlCommand(sql, conn)

            cmd.Parameters.AddWithValue("@RoomId", roomId)
            cmd.Parameters.AddWithValue("@PrivateType", CByte(ChatMessageType.PrivateMessage))
            cmd.Parameters.AddWithValue("@FetchCount", messageCount * FetchRatio)

            Using reader As SqlDataReader = cmd.ExecuteReader()

                While reader.Read()

                    Dim entry As New ChatLogEntry()

                    entry.MessageType = Convert.ToInt32(reader("MessageType"))

                    ' LocalIdはNULL許容列のため、値の有無を確認してから取り出す
                    If Not Convert.IsDBNull(reader("LocalId")) Then
                        entry.LocalId = Convert.ToInt32(reader("LocalId"))
                    End If

                    entry.Nickname = reader("Nickname").ToString()
                    entry.IconType = NullableString(reader("IconType"))
                    entry.IconColor = NullableString(reader("IconColor"))
                    entry.Message = NullableString(reader("MessageText"))

                    ' トリップ（ログに焼き込まれた、その時点の値）
                    If isTripEnabled Then
                        entry.Trip = NullableString(reader("Trip"))
                    End If

                    ' アカウントのユーザー名（今も在る・止めていないアカウントのものだけ。上の SQL の説明）。
                    ' トリップとは、どちらか一方（書くときに守っているが、読むときにも片方だけにする）
                    entry.AccountName = NullableString(reader("AccountName"))

                    If entry.AccountName IsNot Nothing Then
                        entry.Trip = Nothing
                    End If

                    ' 時刻は、ここで "HH:mm" に整形して渡す
                    ' （クライアント側で作り直すと、過去の発言が全部「今の時刻」になってしまう）
                    '
                    ' InvariantCultureを明示しているのは、サーバーのカルチャ設定によって
                    ' 整形結果が変わらないようにするため（共用サーバーの設定は、こちらで
                    ' 決められない。和暦などのカレンダー設定が入っていても影響を受けない）
                    Dim createdAt As DateTime = CType(reader("CreatedAt"), DateTime)

                    entry.Time = createdAt.ToString("HH:mm", CultureInfo.InvariantCulture)

                    ' 日付は "" の形にして渡す（日付の区切り行に使う）
                    '
                    ' ToString("yyyy年M月d日")のような書式指定は使わず、年・月・日を
                    ' 自分で連結している。サーバーのカルチャ設定に和暦のカレンダーが
                    ' 入っていると、書式指定では年が「8年」のような和暦になってしまうため
                    ' （共用サーバーの設定は、こちらでは決められない）
                    entry.Date = createdAt.Year & "年" &
                                 createdAt.Month & "月" &
                                 createdAt.Day & "日"

                    ' 発言以外（入退室などのイベント行）は、表示する文言をここで組み立てる。
                    ' ニックネームは、ログに焼き込まれた当時の値を渡すこと
                    ' （今のCookieの値を使うと、過去ログの表示が後から変わってしまう）
                    If entry.MessageType = CInt(ChatMessageType.RoomSettingsChanged) Then

                        ' 部屋の設定の変更（61）は、何を変えたかが本文の列に入っている。
                        ' 本文は画面へ渡さない（発言の吹き出しと取り違えないため）。文言の中にだけ入れる
                        entry.EventText = ChatEventText.BuildRoomSettingsChanged(entry.Message)
                        entry.Message = Nothing

                    ElseIf entry.MessageType <> CInt(ChatMessageType.Message) Then
                        entry.EventText = ChatEventText.Build(
                            CType(entry.MessageType, ChatMessageType), entry.Nickname)
                    End If

                    ' その行を書いた人のBrowserId（無い場合はNothing。列名は SenderBrowserId）
                    ' クライアントへは渡さないが、下の「置き換えの行を隠す」処理で必要になるため、
                    ' resultと同じ並び順の別リストに控えておく
                    Dim rowBrowserId As Guid? = Nothing
                    If Not Convert.IsDBNull(reader("SenderBrowserId")) Then
                        rowBrowserId = CType(reader("SenderBrowserId"), Guid)
                    End If
                    browserIds.Add(rowBrowserId)

                    ' 自分の発言かどうかは、BrowserIdの一致で判定する（LocalIdでは判定しない）
                    entry.IsSelf = False
                    If hasMyBrowserGuid AndAlso rowBrowserId.HasValue Then
                        entry.IsSelf = (rowBrowserId.Value = myBrowserGuid)
                    End If

                    ' 無視機能で使う、使い捨ての印（AnonIdクラスを参照）
                    '
                    ' 【Guid型のまま渡していることが大事】
                    ' ハブ側は Cookie の生の文字列から計算しており、こちらはDBの Guid 型。
                    ' AnonId 側が ToString("N") で書式を揃えるので、両方の経路で同じ値になる。
                    ' ここで文字列に直してから渡すと、表記の揺れで**同じ人の印が2つできる**
                    If rowBrowserId.HasValue Then
                        entry.AnonId = AnonId.Compute(roomId, rowBrowserId.Value)
                    End If

                    result.Add(entry)

                End While

            End Using

        End Using

        ' ============================================
        ' ② 置き換え（33）の直後に続く入室（20）の行を、表示しないようにする
        '
        ' 【順番が大事】この処理は、下の絞り込みより先に行うこと。
        ' 「33の隣が20か」を隣り合わせで判定しているため、
        ' 先に行を捨ててしまうと、隣り合わせの関係が崩れて判定できなくなる
        ' ============================================
        HideReplacementJoinRows(result, browserIds)

        ' ============================================
        ' ③ 「画面に出る行」だけを残す
        '
        '   ・発言（種別10） … 必ず残す
        '   ・それ以外       … 文言（EventText）があるものだけ残す
        '
        ' 文言が空になるのは、ChatEventText.Buildが種別33にNothingを返す場合と、
        ' 直前の②が置き換え直後の入室行を空にした場合。どちらもクライアントは
        ' 行を作らないため、ここで数に入れてはいけない
        ' （表示するかどうかの判断は、クライアントと同じく「文言の有無」に一本化してある）
        ' ============================================
        Dim visible As New List(Of ChatLogEntry)

        For Each entry As ChatLogEntry In result

            If entry.MessageType = CInt(ChatMessageType.Message) Then
                visible.Add(entry)
            ElseIf Not String.IsNullOrEmpty(entry.EventText) Then
                visible.Add(entry)
            End If

        Next

        ' ============================================
        ' ④ N行を超えていたら、新しい側からN行だけ残す（並びは昇順のまま）
        '
        ' 【割り切り】隠される行が極端に多い部屋では、FetchRatio倍取っても
        ' N行に届かないことがある。その場合はN行より少なく表示されるだけで、
        ' 表示そのものは正しく行われるため、ここは追わない
        ' ============================================
        If visible.Count > messageCount Then
            visible = visible.GetRange(visible.Count - messageCount, messageCount)
        End If

        Return visible

    End Function


    ' ============================================
    ' こっそりの過去ログを読み出す
    ' ============================================
    '
    ' 部屋のログ（GetRecentMessages）とは、別の関数に分けてある。
    ' 中身も、返す相手も、絞り方も違うためである。
    '
    '   ・部屋のログ … その部屋の全員へ配る。だから種別11を外す
    '   ・こっそり   … 当事者2人にしか配らない。だから自分が当事者の行だけを取る
    '
    ' 【絞り込みは、必ずサーバー側でやること】
    ' 「全部返して、クライアント側で自分のぶんだけ出す」形にしてはいけない。
    ' F12を開けば全部読める。**無視機能とは、ここが逆である**
    ' （あちらはサーバーが無視リストを知らないので、クライアントで隠すしかない）。
    '
    ' 【1つの窓につきN行を返す】
    ' 開発者の指定：窓ごとに、本体の表示行数の設定をそのまま使う。
    ' 全体でN行にすると、よく話している相手の窓が、他の窓の行を食ってしまう。
    ' ROW_NUMBER() を「相手ごと」に振り直すことで、1回の問い合わせで実現している
    '
    ' 【FetchRatio（多めに取る）は要らない】
    ' 部屋のログで倍取っているのは、DBには在るのに画面に出ない行
    ' （種別33と、その直後の入室）があるためである。
    ' こっそりは全行が吹き出しとして出るので、落ちる行がそもそも無い
    '
    ' roomId       … どの部屋のこっそりか（部屋をまたいで持ち出さないため、必ず絞る）
    ' messageCount … 1つの窓に表示する行数
    ' myBrowserId  … 見ている人のChatBrowserId Cookieの値
    Public Shared Function GetRecentPrivateMessages(conn As SqlConnection,
                                                    roomId As Integer,
                                                    messageCount As Integer,
                                                    myBrowserId As String) As List(Of ChatLogEntry)

        Dim result As New List(Of ChatLogEntry)

        messageCount = NormalizeMessageCount(messageCount)

        ' 【自分が誰か分からなければ、1行も返さない】
        '
        ' 部屋のログ側は、読めなくても「全部が相手の発言」になるだけで実害が無い。
        ' こちらは違う。自分を特定できないまま返すと、**誰のものか分からないまま
        ' こっそりを配ることになる**。分からないときは、何も返さないほうへ倒す
        Dim myBrowserGuid As Guid

        If Not Guid.TryParse(myBrowserId, myBrowserGuid) Then
            Return result
        End If

        ' トリップを画面へ渡してよいか（部屋のログと同じ判断）
        Dim isTripEnabled As Boolean = TripCode.IsEnabled

        ' 【PARTITION BY の中身が、そのまま「窓のキー」である】
        ' 自分が送った行なら相手は宛先、もらった行なら相手は送り主。
        ' その「相手」ごとに新しい順で番号を振り直し、N番までを残す
        '
        ' 【既知の費用】この問い合わせは、その部屋の行を走査する
        ' （IX_ChatMessages_RoomId_MessageId で RoomId までは絞れるが、
        '   そこから先は種別と当事者での絞り込みになる）。
        ' 部屋あたりの行数が増えたときに効いてくる。宿題15と同じ性質の話で、
        ' 実際に重くなったら、そのとき索引を足して測ること
        '
        ' 【アカウントのユーザー名は、部屋のログと同じく外側で Accounts から引く】
        Dim sql As String =
            "SELECT t.MessageId, t.MessageType, t.LocalId, t.SenderBrowserId, t.RecipientBrowserId, " &
            "       t.Nickname, t.IconType, t.IconColor, t.Trip, t.MessageText, t.CreatedAt, " &
            "       a.UserName AS AccountName " &
            "FROM (" &
            "    SELECT MessageId, MessageType, LocalId, SenderBrowserId, RecipientBrowserId, " &
            "           Nickname, IconType, IconColor, Trip, MessageText, CreatedAt, UserId, " &
            "           ROW_NUMBER() OVER (" &
            "               PARTITION BY CASE WHEN SenderBrowserId = @MyBrowserId " &
            "                                 THEN RecipientBrowserId ELSE SenderBrowserId END " &
            "               ORDER BY MessageId DESC) AS RowNo " &
            "    FROM ChatMessages " &
            "    WHERE RoomId = @RoomId " &
            "      AND MessageType = @PrivateType " &
            "      AND SenderBrowserId IS NOT NULL " &
            "      AND RecipientBrowserId IS NOT NULL " &
            "      AND (SenderBrowserId = @MyBrowserId OR RecipientBrowserId = @MyBrowserId) " &
            ") AS t " &
            "LEFT JOIN Accounts a ON a.AccountId = t.UserId AND a.IsActive = 1 " &
            "WHERE t.RowNo <= @PerWindow " &
            "ORDER BY t.MessageId"

        Using cmd As New SqlCommand(sql, conn)

            cmd.Parameters.AddWithValue("@RoomId", roomId)
            cmd.Parameters.AddWithValue("@PrivateType", CByte(ChatMessageType.PrivateMessage))
            cmd.Parameters.AddWithValue("@MyBrowserId", myBrowserGuid)
            cmd.Parameters.AddWithValue("@PerWindow", messageCount)

            Using reader As SqlDataReader = cmd.ExecuteReader()

                While reader.Read()

                    Dim senderId As Guid = CType(reader("SenderBrowserId"), Guid)
                    Dim recipientId As Guid = CType(reader("RecipientBrowserId"), Guid)

                    Dim entry As New ChatLogEntry()

                    entry.MessageType = Convert.ToInt32(reader("MessageType"))

                    If Not Convert.IsDBNull(reader("LocalId")) Then
                        entry.LocalId = Convert.ToInt32(reader("LocalId"))
                    End If

                    entry.Nickname = reader("Nickname").ToString()
                    entry.IconType = NullableString(reader("IconType"))
                    entry.IconColor = NullableString(reader("IconColor"))
                    entry.Message = NullableString(reader("MessageText"))

                    ' 送り主のトリップ（部屋のログと同じく、焼き込まれた値をそのまま）
                    If isTripEnabled Then
                        entry.Trip = NullableString(reader("Trip"))
                    End If

                    ' 送り主のアカウントのユーザー名（部屋のログと同じ。トリップとは、どちらか一方）
                    entry.AccountName = NullableString(reader("AccountName"))

                    If entry.AccountName IsNot Nothing Then
                        entry.Trip = Nothing
                    End If

                    ' 時刻・日付の作り方は、部屋のログとまったく同じにしてある
                    ' （書式指定を使わずに連結しているのは、サーバーのカルチャ設定に
                    '   和暦のカレンダーが入っていても影響を受けないようにするため。
                    '   詳しい理由は GetRecentMessages 側のコメントにある）
                    Dim createdAt As DateTime = CType(reader("CreatedAt"), DateTime)

                    entry.Time = createdAt.ToString("HH:mm", CultureInfo.InvariantCulture)
                    entry.Date = createdAt.Year & "年" &
                                 createdAt.Month & "月" &
                                 createdAt.Day & "日"

                    ' 自分の発言かどうかは、部屋のログとまったく同じ判定
                    ' （LocalIdでは判定しない。入室のたびに番号が変わるため）
                    entry.IsSelf = (senderId = myBrowserGuid)

                    ' 【必ず両方に印を付けること】
                    ' 送り主だけだと、自分が送った行がどの窓のものか分からなくなる
                    entry.AnonId = AnonId.Compute(roomId, senderId)
                    entry.RecipientAnonId = AnonId.Compute(roomId, recipientId)

                    result.Add(entry)

                End While

            End Using

        End Using

        Return result

    End Function


    ' こっそりの過去ログを読み出す（DB接続を自前で開く版）
    ' 呼び出し元が接続を持っていないときに使う
    Public Shared Function GetRecentPrivateMessages(roomId As Integer,
                                                    messageCount As Integer,
                                                    myBrowserId As String) As List(Of ChatLogEntry)

        Dim connStr As String =
            ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        Using conn As New SqlConnection(connStr)
            conn.Open()
            Return GetRecentPrivateMessages(conn, roomId, messageCount, myBrowserId)
        End Using

    End Function


    ' 種別33（同一ブラウザの置き換え）の直後に続く、同じ人の入室(20)の行を隠す
    '
    ' 【なぜ隠すのか】
    ' 別のタブを開いたのは本人の都合であって、同じ部屋にいる他の人にとっては
    ' 何の意味も無い。外から見れば、その人はずっとそこにいる。
    ' 33の行自体はChatEventText.Buildが既にNothingを返して消えているため、
    ' ここで20の行も消さないと、「入室しました」だけが唐突に残ってしまう。
    '
    ' リアルタイム側でも同じことをしている（ChatHub.JoinRoomのisSameRoomReplacement）。
    ' 過去ログとリアルタイムで見え方が食い違わないよう、両方に入れてある。
    '
    ' entries    … 読み出した過去ログ（MessageIdの昇順に並んでいること）
    ' browserIds … entriesと同じ並び順の、各行のBrowserId
    '
    ' 【割り切り】判定は「すぐ隣の行」だけを見ている。
    ' 33と20は、同じJoinRoomの処理の中で数ミリ秒の間に書き込まれるため、通常は隣り合う。
    ' ただし、ちょうどその隙間に他の人の発言が入ると隣り合わなくなり、
    ' その回だけ「入室しました」の行が1つ残る。実害が小さいため、そこまでは追わない
    Private Shared Sub HideReplacementJoinRows(entries As List(Of ChatLogEntry),
                                               browserIds As List(Of Guid?))

        ' 最後の行の「次の行」は無いため、1つ手前まで見る
        For i As Integer = 0 To entries.Count - 2

            ' 置き換え（33）の行を探す
            If entries(i).MessageType <> CInt(ChatMessageType.Replaced) Then
                Continue For
            End If

            ' その直後が入室(20)でなければ、対象外
            If entries(i + 1).MessageType <> CInt(ChatMessageType.Join) Then
                Continue For
            End If

            ' 同じ人（同じブラウザ）でなければ、たまたま並んだ別人の入室なので、消してはいけない
            ' （どちらかのBrowserIdが記録されていない場合も、同一とは断定できないため対象外）
            If Not browserIds(i).HasValue OrElse Not browserIds(i + 1).HasValue Then
                Continue For
            End If

            If browserIds(i).Value <> browserIds(i + 1).Value Then
                Continue For
            End If

            ' 文言を空にする。クライアントは文言が空なら行を作らない
            ' （行そのものをリストから消さないのは、他の情報は残しておきたいのと、
            '   表示するかどうかの判断を、文言の有無に一本化しておきたいため）
            entries(i + 1).EventText = Nothing

        Next

    End Sub


    ' 読み出した過去ログを、HTMLの<script>内へそのまま埋め込める形のJSONにする
    '
    ' 【罠】JSONを<%= %>で生のまま出力するため、発言本文に "</script>" と書かれると、
    ' そこでscriptタグが閉じてしまい、ページが壊れる。
    ' JSON文字列の中の "<" を "\u003c"（JSONのユニコードエスケープ）に置き換えておけば、
    ' JavaScriptが受け取る文字列の中身は "<" のままで、
    ' HTMLパーサーからはタグの開始に見えなくなる
    Public Shared Function ToScriptJson(entries As List(Of ChatLogEntry)) As String

        Dim json As String = JsonConvert.SerializeObject(entries)

        ' VBの文字列にはエスケープ記法が無いため、"\u003c" はそのまま
        ' 「バックスラッシュ・u・0・0・3・c」の6文字として扱われる（これが狙いどおり）
        Return json.Replace("<", "\u003c")

    End Function


    ' DBのNULLを、Nothingに変換するための小さなヘルパー
    Private Shared Function NullableString(value As Object) As String

        If value Is Nothing OrElse Convert.IsDBNull(value) Then
            Return Nothing
        End If

        Return value.ToString()

    End Function



    ' その部屋から、そのブラウザが直前に抜けたときの種別を1件だけ返す
    '
    ' 戻り値 … ChatMessageType の値（30番台）。見つからない・引けない場合は 0
    '
    ' 【何に使うか】切断中に落とされた人へ、ラウンジで理由を見せるため
    ' （ChatHub.BuildSeatLostReason）。Kicked が届かなかった相手の埋め合わせである
    '
    ' 【サーバー側に覚えておくものを増やさないための形】
    ' 「誰をいつ落としたか」をメモリに持てば引かずに済むが、
    ' いつ捨てるか・いくつまで持つかという問題が付いてくる
    ' （RejectLogTrackingLimit と同じ手当てが要る）。
    ' 理由は既に ChatMessages に書いてあるので、必要になった時に1件だけ引く
    '
    ' 【30番台に限っていること】在室が終わった記録だけを見る。
    ' 20（入室）や10（発言）を拾うと、「抜けた理由」ではなくなる。
    ' 40番台（入室拒否）も在室の記録ではないため、範囲に入れない
    '
    ' 【フェイルオープン】引けなければ 0 を返す。呼び出し側は理由なしの文言に落とす。
    ' 断ること自体は、理由が分からなくても正しく行われなければならない
    Public Shared Function GetLastLeaveType(roomId As Integer, browserId As String) As Integer

        Dim id As Guid

        If Not Guid.TryParse(browserId, id) Then
            Return 0
        End If

        Try

            Dim connStr As String =
                ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)

                ' 並び順は MessageId。CreatedAt ではない
                ' （同じ時刻の行が複数あっても、順序が一意に定まるため。
                '   過去ログの読み出しと同じ理由）
                Using cmd As New SqlCommand(
                    "SELECT TOP (1) MessageType FROM ChatMessages " &
                    "WHERE RoomId = @RoomId AND SenderBrowserId = @SenderBrowserId " &
                    "  AND MessageType >= 30 AND MessageType < 40 " &
                    "ORDER BY MessageId DESC", conn)

                    cmd.Parameters.AddWithValue("@RoomId", roomId)
                    cmd.Parameters.AddWithValue("@SenderBrowserId", id)

                    conn.Open()

                    Dim result As Object = cmd.ExecuteScalar()

                    If result Is Nothing OrElse Convert.IsDBNull(result) Then
                        Return 0
                    End If

                    Return Convert.ToInt32(result)

                End Using
            End Using

        Catch ex As Exception

            ' 0（理由なし）で通すことは変えないが、黙っては通さない。
            ' それまでは Debug.WriteLine で、本番の Release ビルドでは呼び出しごと消えていた
            ErrorReport.Report(ex, "ChatLogReader.GetLastLeaveType")
            Return 0

        End Try

    End Function

End Class
