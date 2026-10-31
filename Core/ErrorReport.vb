Imports System.Collections.Concurrent
Imports System.Data.SqlClient
Imports System.Diagnostics
Imports System.Reflection
Imports System.Text
Imports System.Threading

' エラーを知らせる、ただ1つの窓口（追加。実装の優先順位の13番）
'
' 【何のためにあるか】
' このサイトは「チャットを止めない」ために、あちこちで例外を握りつぶしている
' （ログの書き込み・設定の読み込み・過去ログの読み出し…）。握りつぶすこと自体は正しいが、
' **握りつぶした先に通知の口が無いと、壊れていることに誰も気づけない**。
' 13番の目的は、その口をここ1つに揃えることである。
'
' 【呼ぶ側は、これを呼んだらすぐ戻る】
' Report / ReportProblem は「覚えて」と渡すだけ（ConcurrentQueue に積んで、裏の作業を起こす）。
' ここでは SMTP を待たない・ロックを長く握らない・DB に触らない・**SiteSettings も読まない**。
'
' 【なぜ SiteSettings を読まないのか（読むと、アプリごと落ちる）】
' SiteSettings は、最初の読み込みを鍵（CacheLock）の中でやっていて、失敗するとここへ報告する。
' もしここで SiteSettings を読むと、同じスレッドなので鍵を素通りし、
' まだ出来上がっていないのでまた読みに行き、また失敗して報告し…を無限に繰り返して
' スタックが溢れる（＝プロセスごと落ちる）。
' そこで「1時間」「10通」「届け先」は、**裏の作業（Process・SiteMail）が読む**。
' 別のスレッドなので、鍵が空くのを待つだけで済む
'
' 【同じエラーは、1時間に1通】（SiteSettings の ErrorMailQuietSeconds）
' 「同じ」は、**起きた場所＋エラーの種類**で決める。エラーの文章は判定に入れない
' （文章にはIDや値が混ざるので、毎回別物になって止まらない）。
' DB のエラー（SqlException）だけは番号も入れる（タイムアウト -2 と、デッドロック 1205 と、
' 「長すぎる」2628 は、別の問題なので）。
' 止めている間の回数は数えておき、止める時間が過ぎた時点で1回以上あれば、続報を1通送る
' （「次のメールに書き添える」だけだと、エラーがそこで止んだとき、数えた回数が誰にも届かない）
'
' 【全体でも、1時間に10通まで】（SiteSettings の ErrorMailMaxPerHour）
' 続報も、まとめのメールも、この10通に数える。
' 枠がいっぱいのときに起きた新しい種類のエラーは、まだ1通も送っていない「控え」として覚えておき、
' 枠が空いた時点でまとめて1通にする。
' 「1時間」の枠は、設定の ErrorMailQuietSeconds とは別の、決まった60分（1時間あたりの数、という意味だから）
'
' 【数えた記録はメモリに置き、DBには書かない】
' DB が壊れているときこそ知らせが要る。そこで DB に書きに行く作りだと、肝心なときに知らせが道連れになる。
' 代わりに、アプリが起き直すたびに数え直しになる（起動のたびに起きるエラーは、起動のたびに1通来る）。
' それは承知のうえ
'
' 【送らないもの】（IsNoise）
' 4xx（存在しないページ・ボットの変なURL・大きすぎる送信）、相手が接続を切っただけのもの
' （番号3つ＋「番号 0x80070057 かつ投げた場所が RaiseCommunicationError」の組み合わせ1つ）、
' Response.Redirect が中で使う ThreadAbortException。
' これらを送ると、1時間10通の枠がボットに埋められて、本物のエラーが止められる
Public NotInheritable Class ErrorReport

    Private Sub New()
    End Sub

    ' 件名の先頭の印（メールソフトで振り分けるため）。
    ' info@ は将来の MVC 版とも共用なので、サービスの名前を必ず入れる
    Friend Const SubjectPrefix As String = "[vb-chat エラー] "

    ' 全体の上限を数える枠（1時間あたり、の「1時間」）
    Private Shared ReadOnly GlobalWindow As TimeSpan = TimeSpan.FromHours(1)

    ' 続報・控えのまとめを送る時刻が来たかを見に行く間隔
    Private Shared ReadOnly TickInterval As TimeSpan = TimeSpan.FromSeconds(30)

    ' === 覚えておく量の上限（どれも、壊れ方がひどいときにメモリを食い続けないための歯止め） ===

    ' まだ裏の作業が片付けていない報告の数
    Private Const MaxIncoming As Integer = 1000

    ' 覚えておくエラーの種類の数（満員拒否の記録の「覚える上限」と同じ考え方）
    Private Const MaxTrackedKinds As Integer = 200

    ' 1件の全文の長さの上限（文字）
    Private Const MaxFullTextLength As Integer = 20000

    ' 控えのまとめに載せる、1種類あたりの抜粋の長さ（文字）
    Private Const HeldExcerptLength As Integer = 3000

    ' 相手が接続を切っただけのときに、ASP.NET が投げる HttpException の ErrorCode
    '   0x800704CD … リモート ホストが接続を閉じました
    '   0x80070040 … 指定されたネットワーク名は利用できません
    '   0x800703E3 … I/O 操作は中止されました
    '
    ' 【この3つは、番号だけで「相手が切った」と決めてよい】
    ' どれも通信そのものの番号で、ほかの場面では出てこない
    Private Shared ReadOnly ClientGoneErrorCodes As Integer() = {
        &H800704CD, &H80070040, &H800703E3
    }

    ' === 0x80070057 だけは、番号だけで決めてはいけない ===
    '
    ' 【何が起きたか】/signalr/negotiate から1通来た。
    ' 握手の返事を書き出す途中（HttpResponse.Flush → IIS7WorkerRequest.ExplicitFlush）で
    ' ブラウザが離れただけで、害は無い。ところが上の3つに 0x80070057 が無いので、
    ' 「相手が切っただけ」と見なされずメールになった。
    '
    ' 【番号だけで捨てると、本物まで黙る】0x80070057 は E_INVALIDARG（引数が正しくない）で、
    ' 通信と関係のない不具合でも出うる、ありふれた番号である。
    ' そこで **「番号」と「投げた場所」の2つがそろったときだけ** ノイズとして扱う。
    ' 投げた場所＝IIS が「相手が切れた」と気づいて例外にする、ただ1つのメソッド。
    '
    ' 【StackTrace の文字列を探さない】TargetSite なら、例外が投げられたメソッドそのものが取れる。
    ' 文字列を組み立てて探すより安く、AndAlso で短絡もできる（番号が違えば、そもそも見に行かない）。
    '
    ' 【Response.IsClientConnected では決めない（こちらより弱い）】
    ' IIS が切断に気づくのは次の入出力のときなので、**本物のエラーが起きたあとに相手が離れた場合も
    ' False になる**＝本物を黙らせる側に外す。そのうえ、控えのまとめのように
    ' Application_Error の外から呼ぶ道では、そもそも見られない
    Private Const ClientGoneInvalidArg As Integer = &H80070057
    Private Const ClientGoneMethodName As String = "RaiseCommunicationError"

    Private Shared ReadOnly Incoming As New ConcurrentQueue(Of Occurrence)
    Private Shared ReadOnly Runner As New SerialRunner(AddressOf Process, "ErrorReport")

    ' 以下は StateLock の中でだけ触る
    Private Shared ReadOnly StateLock As New Object()
    Private Shared ReadOnly Kinds As New Dictionary(Of String, KindState)(StringComparer.Ordinal)
    Private Shared ReadOnly SentTimes As New Queue(Of DateTime)
    Private Shared _droppedKinds As Integer

    ' Interlocked で触る
    Private Shared _droppedIncoming As Integer
    Private Shared _trackedKindCount As Integer
    Private Shared _tickStarted As Integer

    ' 続報・控えのまとめのための見回り（最初の報告が来たときに作る。エラーが無ければ作らない）
    Private Shared _tickTimer As Timer


    ' ============================================================
    '  呼ぶ側の入口
    ' ============================================================

    ' 例外を知らせる（握りつぶしている Catch の中から呼ぶ）
    '
    ' place  … 起きた場所。"ChatLog.Write" のように、決まった文字列で渡す
    '          （「同じエラー」の判定に使うので、値や時刻を混ぜないこと）。
    '          Nothing なら、例外のスタックの中でいちばん上にある、このアプリのメソッド名を使う
    ' detail … 補足（部屋の番号・種別など）。**発言の本文・Cookie の値を入れないこと**
    '          （メールに載る。こっそりの本文や、本人証明そのものである ChatBrowserId が漏れる）
    Public Shared Sub Report(ex As Exception, place As String, Optional detail As String = Nothing)
        ReportFrom(ex, place, "握りつぶしている箇所から報告", detail)
    End Sub


    ' Report の中身（入口の書き方だけを変えたいとき用。GuardWebMethod が使う）
    Private Shared Sub ReportFrom(ex As Exception, place As String, entry As String, detail As String)

        Try

            If ex Is Nothing OrElse IsNoise(ex) Then
                Return
            End If

            Dim resolvedPlace As String = place

            If String.IsNullOrWhiteSpace(resolvedPlace) Then
                resolvedPlace = If(FindOwnFrame(ex), "（場所不明）")
            End If

            Enqueue(Occurrence.FromException(ex, resolvedPlace, entry, CurrentPath(), detail))

        Catch inner As Exception
            SerialRunner.SafeTrace("ErrorReport.Report が失敗しました： " & inner.ToString())
        End Try

    End Sub


    ' 例外は無いが、おかしなことを知らせる（設定の値が読めない、ソルトのファイルが無い、など）
    '
    ' place は Report と同じく、決まった文字列で渡す。
    ' message は、何がどうおかしいか（こちらはメールの本文に載るだけで、判定には使わない）
    Public Shared Sub ReportProblem(place As String, message As String)

        Try
            Enqueue(Occurrence.FromProblem(If(place, "（場所不明）"), message, CurrentPath()))
        Catch inner As Exception
            SerialRunner.SafeTrace("ErrorReport.ReportProblem が失敗しました： " & inner.ToString())
        End Try

    End Sub


    ' 未処理のエラーを知らせる（Application_Error・ハブのエラーの口・見回りのタイマーから）
    '
    ' entry         … どの口から来たか（「ページ」「SignalR のハブ」など。メールに載る）
    ' path          … URL のパス（クエリ文字列は含めない）
    ' fallbackPlace … このアプリのコードがスタックに1つも無かったときの場所
    '                 （markup のコンパイルエラーなど、枠組みの中だけで起きたもの）
    Friend Shared Sub ReportUnhandled(ex As Exception, entry As String, path As String, fallbackPlace As String)

        Try

            If ex Is Nothing OrElse IsNoise(ex) Then
                Return
            End If

            Dim place As String = If(FindOwnFrame(ex), If(fallbackPlace, "（場所不明）"))

            Enqueue(Occurrence.FromException(ex, place, entry, path, Nothing))

        Catch inner As Exception
            SerialRunner.SafeTrace("ErrorReport.ReportUnhandled が失敗しました： " & inner.ToString())
        End Try

    End Sub


    ' WebMethod（$.ajax で呼ぶもの）の中身を包む
    '
    ' 【なぜ要るのか】WebMethod で漏れた例外は、Application_Error に来ない。
    ' ASP.NET（ASP.NET AJAX の受け口）が自分で捕まえて、JSON の500で返すだけで終わる。
    '
    ' 【動きは変えない】知らせたあと、同じ例外をそのまま投げ直す（Throw）。
    ' 画面の側から見た振る舞い（呼び出しが失敗として返る）は、今までと同じ。
    '
    ' 使い方：Return ErrorReport.GuardWebMethod("Lounge.EnterRoom", Function() EnterRoomCore(roomId))
    Public Shared Function GuardWebMethod(Of T)(place As String, body As Func(Of T)) As T

        Try
            Return body()
        Catch ex As Exception
            ReportFrom(ex, place, "WebMethod（$.ajax。ASP.NET が JSON の500で返す）", Nothing)
            Throw
        End Try

    End Function


    ' アプリが止まる直前のエラーを、その場で送る（AppDomain.UnhandledException から）
    '
    ' 【ここだけは、並べずにその場で送る】
    ' 並べても、裏の作業が走る前にプロセスが消える。届けば幸運、という最後の手段である。
    ' 1時間の枠も見ない（プロセスが止まる＝この先の枠を使う者がいない）。
    ' ここは Report の経路ではないので、SiteSettings を読んでよい
    Friend Shared Sub ReportFatal(ex As Exception)

        Try

            If ex Is Nothing Then
                Return
            End If

            SerialRunner.SafeTrace("アプリが止まる直前のエラー： " & ex.ToString())

            If Not SiteSettings.MailEnabled Then
                Return
            End If

            Dim occ As Occurrence = Occurrence.FromException(
                ex, If(FindOwnFrame(ex), "（場所不明）"), "アプリ全体（AppDomain.UnhandledException）", Nothing, Nothing)

            Dim body As New StringBuilder()
            body.AppendLine("vb-chat で、アプリが止まる直前のエラーが起きました。")
            body.AppendLine("このメールのあと、アプリは再起動しているはずです（その時点の在室者は全員切断されています）。")
            body.AppendLine()
            AppendOccurrence(body, occ)

            SiteMail.SendNow(SiteSettings.MailTo,
                             SubjectPrefix & "【アプリが止まる直前】" & occ.KindShort & " ＠ " & occ.Place,
                             body.ToString(), 10000)

        Catch inner As Exception
            SerialRunner.SafeTrace("止まる直前のエラーを送れませんでした： " & inner.ToString())
        End Try

    End Sub


    ' ============================================================
    '  送らないもの
    ' ============================================================

    ' このエラーは知らせなくてよいか
    '
    ' 【4xx は送らない】存在しないページ・危険な文字列の混ざったURL・大きすぎる送信は、
    ' ほぼボットか、利用者側の都合である。こちらの不具合ではない
    Friend Shared Function IsNoise(ex As Exception) As Boolean

        If ex Is Nothing Then
            Return True
        End If

        Dim baseEx As Exception = ex.GetBaseException()

        For Each candidate As Exception In {ex, baseEx}

            ' Response.Redirect（endResponse = True）が中で使う。エラーではなく、ただの流れ
            If TypeOf candidate Is ThreadAbortException Then
                Return True
            End If

            ' 「危険な値が含まれています」＝要求の検証で断ったもの（ボットがほとんど）
            If TypeOf candidate Is HttpRequestValidationException Then
                Return True
            End If

            Dim http As HttpException = TryCast(candidate, HttpException)

            If http IsNot Nothing Then

                Dim code As Integer = http.GetHttpCode()

                If code >= 400 AndAlso code < 500 Then
                    Return True
                End If

                ' 相手が接続を切っただけ
                If Array.IndexOf(ClientGoneErrorCodes, http.ErrorCode) >= 0 Then
                    Return True
                End If

                ' 相手が接続を切っただけ（その2）＝番号と「投げた場所」の2つで見る。
                ' 番号だけで捨ててはいけない理由は、ClientGoneInvalidArg の宣言のところ
                If http.ErrorCode = ClientGoneInvalidArg AndAlso ThrownByClientGone(candidate) Then
                    Return True
                End If

                ' 送られてきた本文が maxRequestLength を超えた（大きすぎる画像など）
                If http.WebEventCode = System.Web.Management.WebEventCodes.RuntimeErrorPostTooLarge Then
                    Return True
                End If

            End If

        Next

        Return False

    End Function


    ' この例外は、IIS が「相手が切れた」と気づいた場所から投げられたものか
    '
    ' 【TargetSite は、例外が投げられたメソッドそのもの】
    ' 名前が RaiseCommunicationError なら、IIS7WorkerRequest が書き出しに失敗して
    ' 「通信が切れた」と判断した、その1か所である。
    '
    ' 【Try で包んである】TargetSite は、権限の足りない環境では例外になりうる。
    ' ここで落ちると**エラーの報告そのものが止まる**ので、分からなければ False
    ' （＝ノイズ扱いにしない＝送る側へ倒す）
    Private Shared Function ThrownByClientGone(ex As Exception) As Boolean

        Dim site As MethodBase = Nothing

        Try
            site = ex.TargetSite
        Catch
            Return False
        End Try

        Return site IsNot Nothing AndAlso
               String.Equals(site.Name, ClientGoneMethodName, StringComparison.Ordinal)

    End Function


    ' ============================================================
    '  積む（呼ぶ側のスレッド）
    ' ============================================================

    Private Shared Sub Enqueue(occ As Occurrence)

        ' Trace にも残す（あとで Trace のリスナーを足したときに、そちらにも出るように）
        SerialRunner.SafeTrace("[ErrorReport] " & occ.Place & " / " & occ.KindText & " / " & occ.FirstLine)

        If Incoming.Count >= MaxIncoming Then
            Interlocked.Increment(_droppedIncoming)
            Return
        End If

        Incoming.Enqueue(occ)
        Runner.Kick()
        EnsureTicking()

    End Sub


    ' 今の要求の URL のパス（無ければ Nothing）
    '
    ' 【クエリ文字列は含めない】合言葉（MailTest の key）などが混ざりうる
    Private Shared Function CurrentPath() As String

        Try
            Dim context As HttpContext = HttpContext.Current
            If context IsNot Nothing Then
                Return context.Request.Path
            End If
        Catch
            ' Application_Start の中などでは Request が使えない
        End Try

        Return Nothing

    End Function


    ' 見回りを始める（最初の1回だけ）
    Private Shared Sub EnsureTicking()

        If Interlocked.CompareExchange(_tickStarted, 1, 0) <> 0 Then
            Return
        End If

        Try
            _tickTimer = New Timer(AddressOf OnTick, Nothing, TickInterval, TickInterval)
        Catch ex As Exception
            Interlocked.Exchange(_tickStarted, 0)
            SerialRunner.SafeTrace("ErrorReport の見回りを始められませんでした： " & ex.ToString())
        End Try

    End Sub


    ' 見回り（30秒おき）。覚えている種類が1つでもあれば、裏の作業を起こす
    '
    ' 【例外を外へ出さないこと】タイマーの中で漏れた例外は、プロセスごと落とす
    Private Shared Sub OnTick(state As Object)

        Try
            If Interlocked.CompareExchange(_trackedKindCount, 0, 0) > 0 OrElse Not Incoming.IsEmpty Then
                Runner.Kick()
            End If
        Catch ex As Exception
            SerialRunner.SafeTrace("ErrorReport の見回りで例外： " & ex.ToString())
        End Try

    End Sub


    ' ============================================================
    '  数えて、送るメールを決める（裏の作業）
    ' ============================================================

    Private Shared Sub Process()

        ' 設定はここで読む（このファイルの先頭の【なぜ SiteSettings を読まないのか】）。
        ' 鍵（StateLock）を取る前に読むこと
        Dim quiet As TimeSpan = SiteSettings.ErrorMailQuietPeriod
        Dim maxPerHour As Integer = SiteSettings.ErrorMailMaxPerHour

        Dim mails As New List(Of KeyValuePair(Of String, String))

        SyncLock StateLock

            Dim occ As Occurrence = Nothing

            While Incoming.TryDequeue(occ)
                Absorb(occ, quiet, maxPerHour, mails)
            End While

            Sweep(DateTime.Now, quiet, maxPerHour, mails)

            Interlocked.Exchange(_trackedKindCount, Kinds.Count)

        End SyncLock

        ' 送るのは鍵の外で（SiteMail.Enqueue は並べるだけだが、念のため）
        '
        ' 【罠】ループの変数を mail にしないこと。VB は大文字・小文字を区別しないので、
        ' System.Web.Mail（名前空間）とぶつかってビルドが止まる
        For Each outgoing In mails
            SiteMail.Enqueue(outgoing.Key, outgoing.Value)
        Next

    End Sub


    ' 報告1件を、覚えている種類へ足す
    Private Shared Sub Absorb(occ As Occurrence, quiet As TimeSpan, maxPerHour As Integer,
                              mails As List(Of KeyValuePair(Of String, String)))

        Dim state As KindState = Nothing

        If Kinds.TryGetValue(occ.Key, state) Then

            ' 既に知っている種類。数えるだけ（送るかどうかは Sweep が決める）
            state.SinceMail += 1
            state.Latest = occ
            Return

        End If

        If Kinds.Count >= MaxTrackedKinds Then
            ' 覚えきれない。数だけ数えて、次に送るまとめに書き添える
            _droppedKinds += 1
            Return
        End If

        state = New KindState(occ)
        Kinds.Add(occ.Key, state)

        If TryTakeSlot(occ.At, maxPerHour) Then
            mails.Add(BuildFirstMail(state, quiet, heldCount:=0))
            state.MailedAt = occ.At
            state.SinceMail = 0
        End If

        ' 枠が無ければ、MailedAt が Nothing のまま＝控え。Sweep が枠の空いたときにまとめて送る

    End Sub


    ' 時間の過ぎた種類の続報と、控えのまとめ
    Private Shared Sub Sweep(now As DateTime, quiet As TimeSpan, maxPerHour As Integer,
                             mails As List(Of KeyValuePair(Of String, String)))

        ' --- 1) 止めていた時間が過ぎた種類 ---
        For Each entry In Kinds.ToList()

            Dim state As KindState = entry.Value

            If Not state.MailedAt.HasValue Then
                Continue For
            End If

            If now - state.MailedAt.Value < quiet Then
                Continue For
            End If

            If state.SinceMail = 0 Then
                ' 止めている間に1回も起きなかった。忘れてよい（次に起きたら、また1通目から）
                Kinds.Remove(entry.Key)
                Continue For
            End If

            If Not TryTakeSlot(now, maxPerHour) Then
                ' 枠が無い。次の見回りでまた試す（数えた回数は残っている）
                Continue For
            End If

            mails.Add(BuildFollowUpMail(state, quiet))
            state.MailedAt = now
            state.SinceMail = 0

        Next

        ' --- 2) 枠で止めていた（まだ1通も送っていない）種類 ---
        Dim held As List(Of KindState) = Kinds.Values.Where(Function(s) Not s.MailedAt.HasValue).ToList()
        Dim droppedIncoming As Integer = Interlocked.CompareExchange(_droppedIncoming, 0, 0)

        If held.Count = 0 AndAlso _droppedKinds = 0 AndAlso droppedIncoming = 0 Then
            Return
        End If

        If Not TryTakeSlot(now, maxPerHour) Then
            Return
        End If

        If held.Count = 1 AndAlso _droppedKinds = 0 AndAlso droppedIncoming = 0 Then
            ' 1種類だけなら、ふつうの1通目と同じ形で送る（読みやすいので）
            mails.Add(BuildFirstMail(held(0), quiet, heldCount:=held(0).SinceMail))
        Else
            mails.Add(BuildHeldSummaryMail(held, _droppedKinds, droppedIncoming, quiet))
        End If

        For Each state In held
            state.MailedAt = now
            state.SinceMail = 0
        Next

        _droppedKinds = 0
        Interlocked.Add(_droppedIncoming, -droppedIncoming)

    End Sub


    ' 全体の枠（1時間あたり maxPerHour 通）から1通ぶん取る。取れなければ False
    Private Shared Function TryTakeSlot(now As DateTime, maxPerHour As Integer) As Boolean

        While SentTimes.Count > 0 AndAlso now - SentTimes.Peek() >= GlobalWindow
            SentTimes.Dequeue()
        End While

        If SentTimes.Count >= maxPerHour Then
            Return False
        End If

        SentTimes.Enqueue(now)
        Return True

    End Function


    ' ============================================================
    '  メールの中身
    ' ============================================================

    ' 1通目（heldCount … 枠で止めていた間に起きた回数。0なら止めていない）
    Private Shared Function BuildFirstMail(state As KindState, quiet As TimeSpan,
                                           heldCount As Integer) As KeyValuePair(Of String, String)

        Dim occ As Occurrence = state.First
        Dim body As New StringBuilder()

        body.AppendLine("vb-chat でエラーが起きました。")

        If heldCount > 1 Then
            body.AppendLine("（1時間あたりの上限で止めていたため、送るのが遅れました。止めていた間に " & heldCount &
                            " 回起きています。最後は " & FormatTime(state.Latest.At) & "）")
        End If

        body.AppendLine()
        AppendOccurrence(body, occ)
        AppendAftercare(body, quiet)

        Return New KeyValuePair(Of String, String)(
            SubjectPrefix & occ.KindShort & " ＠ " & occ.Place, body.ToString())

    End Function


    ' 続報（止めていた時間が過ぎて、その間に1回以上起きていたとき）
    Private Shared Function BuildFollowUpMail(state As KindState, quiet As TimeSpan) As KeyValuePair(Of String, String)

        Dim body As New StringBuilder()

        body.AppendLine("前回のメール（" & FormatTime(state.MailedAt) & "）のあと、同じエラー（場所と種類が同じもの）が、")
        body.AppendLine("さらに " & state.SinceMail & " 回起きました。最後に起きたのは " & FormatTime(state.Latest.At) & " です。")
        body.AppendLine()
        body.AppendLine("--- 最後の1回 ---")
        AppendOccurrence(body, state.Latest)
        AppendAftercare(body, quiet)

        Return New KeyValuePair(Of String, String)(
            SubjectPrefix & "続報：" & state.First.KindShort & " ＠ " & state.First.Place &
            "（さらに" & state.SinceMail & "回）",
            body.ToString())

    End Function


    ' 控えのまとめ（1時間あたりの上限で止めていた、別々の種類）
    Private Shared Function BuildHeldSummaryMail(held As List(Of KindState), droppedKinds As Integer,
                                                 droppedIncoming As Integer,
                                                 quiet As TimeSpan) As KeyValuePair(Of String, String)

        Dim body As New StringBuilder()

        body.AppendLine("1時間あたりの上限に達していたため、送らずに止めていたエラーがあります。")
        body.AppendLine("枠が空いたので、まとめて送ります。")
        body.AppendLine()

        Dim index As Integer = 0

        For Each state In held

            index += 1

            body.AppendLine("=== " & index & ". " & state.First.KindShort & " ＠ " & state.First.Place & " ===")
            body.AppendLine("回数　　　：" & state.SinceMail & " 回（最初 " & FormatTime(state.First.At) &
                            "／最後 " & FormatTime(state.Latest.At) & "）")
            AppendOccurrence(body, state.First, HeldExcerptLength)
            body.AppendLine()

        Next

        If droppedKinds > 0 Then
            body.AppendLine("このほかに、覚えきれなかったエラーが " & droppedKinds & " 件あります（覚える種類の上限 " &
                            MaxTrackedKinds & "）。")
        End If

        If droppedIncoming > 0 Then
            body.AppendLine("このほかに、報告が多すぎて数えきれなかったものが " & droppedIncoming & " 件あります（上限 " &
                            MaxIncoming & "）。")
        End If

        AppendAftercare(body, quiet)

        Dim subject As String =
            If(held.Count > 0,
               SubjectPrefix & "止めていたエラー " & held.Count & " 種類のまとめ",
               SubjectPrefix & "数えきれなかったエラーがあります")

        Return New KeyValuePair(Of String, String)(subject, body.ToString())

    End Function


    ' 1件ぶんの中身
    '
    ' 【入れないもの】Cookie（ChatBrowserId は本人証明、ChatTripKey は合言葉そのもの）、
    ' 発言の本文、クエリ文字列、要求の中身。どれも Occurrence が最初から持っていない
    Private Shared Sub AppendOccurrence(body As StringBuilder, occ As Occurrence,
                                        Optional maxLength As Integer = MaxFullTextLength)

        body.AppendLine("起きた時刻　：" & FormatTime(occ.At))
        body.AppendLine("起きた場所　：" & occ.Place)
        body.AppendLine("エラーの種類：" & occ.KindText)
        body.AppendLine("入口　　　　：" & occ.Entry)
        body.AppendLine("URLのパス　 ：" & If(occ.Path, "（なし）"))

        If Not String.IsNullOrEmpty(occ.Detail) Then
            body.AppendLine("補足　　　　：" & occ.Detail)
        End If

        body.AppendLine("ビルドの目印：" & SafeBuildId())
        body.AppendLine("サーバー　　：" & SafeMachineName())
        body.AppendLine()
        body.AppendLine("--- 全文 ---")
        body.AppendLine(Truncate(occ.FullText, maxLength))

    End Sub


    Private Shared Sub AppendAftercare(body As StringBuilder, quiet As TimeSpan)

        Dim minutes As Integer = CInt(Math.Round(quiet.TotalMinutes))

        body.AppendLine()
        body.AppendLine("--- このあとの扱い ---")
        body.AppendLine("同じエラー（場所と種類が同じもの）は、これから " & minutes & " 分は送りません。")
        body.AppendLine("その間に起きた回数は数えておき、" & minutes & " 分が過ぎた時点で1回以上あれば、続報を1通送ります。")
        body.AppendLine("エラーのメールは、全体でも1時間に決まった数までしか送りません（SiteSettings の ErrorMailMaxPerHour）。")
        body.AppendLine("この数え方はメモリにだけあるので、アプリが再起動すると最初から数え直します。")

    End Sub


    ' ============================================================
    '  小さな道具
    ' ============================================================

    ' スタックの中でいちばん上にある、このアプリのメソッド（"ChatHub.JoinRoom" の形）。無ければ Nothing
    '
    ' 【行番号は入れない】発行のたびに変わりうるうえ、同じメソッドの中の別の行を別物にしても嬉しくない
    Friend Shared Function FindOwnFrame(ex As Exception) As String

        Try

            Dim ownAssembly As Assembly = GetType(ErrorReport).Assembly

            For Each candidate As Exception In {ex.GetBaseException(), ex}

                Dim frames As StackFrame() = New StackTrace(candidate, False).GetFrames()

                If frames Is Nothing Then
                    Continue For
                End If

                For Each frame As StackFrame In frames

                    Dim method As MethodBase = frame.GetMethod()

                    If method Is Nothing OrElse method.DeclaringType Is Nothing Then
                        Continue For
                    End If

                    If method.DeclaringType.Assembly IsNot ownAssembly Then
                        Continue For
                    End If

                    Return FriendlyTypeName(method.DeclaringType) & "." & method.Name

                Next

            Next

        Catch
            ' 場所が分からないだけ。呼ぶ側が既定の場所を使う
        End Try

        Return Nothing

    End Function


    ' VB が作るクロージャや状態機械の型（"_Closure$__12-0" など）は、外側の型の名前にする
    Private Shared Function FriendlyTypeName(type As Type) As String

        Dim current As Type = type

        While current.Name.Contains("$") AndAlso current.DeclaringType IsNot Nothing
            current = current.DeclaringType
        End While

        Return current.Name

    End Function


    Private Shared Function Truncate(text As String, maxLength As Integer) As String

        If text Is Nothing Then
            Return String.Empty
        End If

        If text.Length <= maxLength Then
            Return text
        End If

        Return text.Substring(0, maxLength) & vbCrLf & "…（" & (text.Length - maxLength) & " 文字を省きました）"

    End Function


    Private Shared Function FormatTime(value As DateTime?) As String

        If Not value.HasValue Then
            Return "（なし）"
        End If

        Return value.Value.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture)

    End Function


    Private Shared Function SafeBuildId() As String
        Try
            Return common.BuildId()
        Catch
            Return "（読めません）"
        End Try
    End Function


    Private Shared Function SafeMachineName() As String
        Try
            Return Environment.MachineName
        Catch
            Return "（読めません）"
        End Try
    End Function


    ' ============================================================
    '  状態の確認（Admin/MailTest.aspx の status から呼ぶ）
    ' ============================================================

    Friend Shared Function DescribeStatus() As String

        Dim sb As New StringBuilder()
        Dim now As DateTime = DateTime.Now

        SyncLock StateLock

            Dim sentInWindow As Integer = SentTimes.Where(Function(t) now - t < GlobalWindow).Count()

            sb.AppendLine("この1時間に送ったエラーのメール：" & sentInWindow & " 通")
            sb.AppendLine("覚えているエラーの種類：" & Kinds.Count & " 種類")

            For Each state In Kinds.Values
                sb.AppendLine("  ・" & state.First.KindShort & " ＠ " & state.First.Place &
                              "（" & If(state.MailedAt.HasValue,
                                        "最後に送った " & FormatTime(state.MailedAt) & "／そのあと " & state.SinceMail & " 回",
                                        "枠で止めている／" & state.SinceMail & " 回") & "）")
            Next

        End SyncLock

        sb.AppendLine("まだ数えていない報告：" & Incoming.Count & " 件")
        sb.AppendLine("（この記録はアプリが再起動すると0に戻ります）")

        Return sb.ToString()

    End Function


    ' ============================================================
    '  入れ物
    ' ============================================================

    ' 報告1件（呼ばれた瞬間に、要るものを全部写し取っておく。あとで HttpContext は使えないため）
    Private NotInheritable Class Occurrence

        Public ReadOnly At As DateTime
        Public ReadOnly Place As String
        Public ReadOnly KindKey As String      ' 「同じ」の判定に使う（型の完全名＋DBのエラー番号）
        Public ReadOnly KindText As String     ' メールに出す
        Public ReadOnly KindShort As String    ' 件名に出す
        Public ReadOnly Entry As String
        Public ReadOnly Path As String
        Public ReadOnly Detail As String
        Public ReadOnly FullText As String
        Public ReadOnly FirstLine As String

        Private Sub New(place As String, kindKey As String, kindText As String, kindShort As String,
                        entry As String, path As String, detail As String, fullText As String, firstLine As String)

            Me.At = DateTime.Now
            Me.Place = place
            Me.KindKey = kindKey
            Me.KindText = kindText
            Me.KindShort = kindShort
            Me.Entry = entry
            Me.Path = path
            Me.Detail = detail
            Me.FullText = Truncate(fullText, MaxFullTextLength)
            Me.FirstLine = firstLine

        End Sub

        Public ReadOnly Property Key As String
            Get
                Return Place & "|" & KindKey
            End Get
        End Property

        Public Shared Function FromException(ex As Exception, place As String, entry As String,
                                             path As String, detail As String) As Occurrence

            Dim baseEx As Exception = ex.GetBaseException()
            Dim typeName As String = baseEx.GetType().FullName
            Dim shortName As String = baseEx.GetType().Name

            Dim kindKey As String = typeName
            Dim kindText As String = typeName
            Dim kindShort As String = shortName

            ' DB のエラーは番号も「種類」に入れる
            Dim sql As SqlException = FindSqlException(ex)

            If sql IsNot Nothing Then
                kindKey &= "#" & sql.Number
                kindText &= "（DBのエラー番号 " & sql.Number & "）"
                kindShort &= "#" & sql.Number
            End If

            Dim firstLine As String = If(baseEx.Message, String.Empty)
            Dim newline As Integer = firstLine.IndexOfAny({ChrW(13), ChrW(10)})
            If newline >= 0 Then
                firstLine = firstLine.Substring(0, newline)
            End If

            Return New Occurrence(place, kindKey, kindText, kindShort, entry, path, detail, ex.ToString(), firstLine)

        End Function

        Public Shared Function FromProblem(place As String, message As String, path As String) As Occurrence

            Dim text As String = If(message, "（説明なし）")

            Return New Occurrence(place, "（例外なし）", "例外ではない異常（設定の値・ファイルなど）", "異常",
                                  "握りつぶしている箇所から報告", path, Nothing, text, text)

        End Function

        ' 例外の連なりの中から、SqlException を探す
        Private Shared Function FindSqlException(ex As Exception) As SqlException

            Dim current As Exception = ex

            While current IsNot Nothing

                Dim sql As SqlException = TryCast(current, SqlException)

                If sql IsNot Nothing Then
                    Return sql
                End If

                current = current.InnerException

            End While

            Return Nothing

        End Function

    End Class


    ' 1種類ぶんの記録
    Private NotInheritable Class KindState

        Public ReadOnly First As Occurrence
        Public Latest As Occurrence

        ' 最後にメールを送った時刻。Nothing＝まだ1通も送っていない（枠で止めている控え）
        Public MailedAt As DateTime?

        ' 最後にメールを送ってから起きた回数（控えのときは、最初の1回を含めた回数）
        Public SinceMail As Integer

        Public Sub New(first As Occurrence)
            Me.First = first
            Me.Latest = first
            Me.MailedAt = Nothing
            Me.SinceMail = 1
        End Sub

    End Class

End Class
