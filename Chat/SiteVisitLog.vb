' サイトへの出入りの記録（種別50〜52）
'
'   50 ログイン        … トップで名前を決めて、ラウンジに来た
'   51 ログアウト      … ラウンジのログアウトのボタン（部屋に席があれば、その部屋には今までどおり37も書く）
'   52 ラウンジに来た  … ラウンジのページを開いた（1つの身元につき1日1回）
'
' 【何のためにあるか】1日1回の集計メールの材料。
' それまでは「ログイン」も「ラウンジだけ見て帰った人」も、どこにも残っていなかった。
' ログインは Cookie を書くのもトップの JS なので、サーバーは次のラウンジの要求まで何も知らない。
' ログアウトは、部屋に席が残っていたときだけ37が書かれていた。
'
' 【書く先は ChatMessages。RoomId は 0】（NoRoom）
' 表を分けないのは、1本の時系列で「ログイン → ラウンジ → 入室 → 発言 → 退室 → ログアウト」を
' 並べて読めるようにするため（発言と入退室を1つの表にしたのと同じ理由）。
' 部屋のログを読むところは、どれも RoomId か種別で絞っているので、0番の行は部屋に出ない
'
' 【ログインの見分け方】
' トップの決定ボタンが、寿命60秒の Cookie（JustLoggedInCookieName）を1つ書いてから移動する。
' ラウンジの Page_Load がそれを見て50を書き、**同じ応答で消す**（RecordLoungeArrival）。
'   ・URL に印を付けない … F5 で二重に書かれない。印の付いた URL を人に送られることも無い
'   ・受け口を新しく作らない … 叩ける口を増やさない
'   ・ハブの引数を増やさない … 引数を増やすのは、古いページとの破壊的変更になる
'
' 【歯止め：満員拒否（40）と同じ2段構え】（ShouldRecord）
' どれも、外から Cookie を作って叩けば行を増やせる。
'   1. 同じ種類・同じ身元（ChatBrowserId）は、1日1回だけ書く
'   2. その「今日もう書いた身元」の表が上限（SiteSettings.VisitLogDailyLimit）に達したら、
'      その日は新しい身元を書かない
' 2が要るのは、1だけだと ChatBrowserId を毎回変えられた瞬間に無力になるため
' （ChatHub の ShouldLogJoinReject のコメントと同じ理由）。
' **書かないだけで、ログインもラウンジも普通に使える**。記録が止まるだけである
'
' 【覚えておくのはメモリ】アプリが起き直すと表は空になり、同じ日にもう1回書くことがある
' （共用サーバーは、アクセスが無いとアプリを寝かせる）。数えるときは SenderBrowserId の DISTINCT で数えること。
' DB に「今日もう書いたか」を問い合わせる形にしなかったのは、ラウンジを開くたびに
' 問い合わせが1回増えるため（ラウンジは今、開くたびに2回。増やさない）
'
' 【書かないもの】
'   ・ChatBrowserId が GUID として読めない要求 … 身元が無いので1日1回が効かない
'   ・名前が空の要求 … JoinRoom が名前の無い人を入れないのと同じ
'   ・発行後の確かめの道具からの要求 … 下の ProbeUserAgent
'
' 【、道具の見分け方を変えた】
' それまでは「発行後の確かめの道具のダミーの ChatBrowserId を、わざと GUID でない値にしておく」
' ことで、道具を走らせても本番の記録が汚れないようにしていた。
' 名前の札（ChatNameToken）が要るようになり、道具も本物と同じ手順でログインするようになったので、
' ダミーの身元は使えなくなった（札は GUID の身元にしか出ない）。
' かわりに、道具が名乗る User-Agent（ProbeUserAgent）を見て、記録だけを飛ばしている。
'
' 【失敗しても、呼んだ側を止めない】DB への書き込みの失敗は ChatLog.Write が ErrorReport へ知らせる。
' それ以外の思わぬ例外も、ここで ErrorReport へ知らせて飲み込む（ラウンジが開かない・ログアウトできない、にしない）
'
' 【HttpContext.Current を使わず、引数で受け取る】ChatLogout と同じ（移った先の MVC に Current は無い）
Public NotInheritable Class SiteVisitLog

    Private Sub New()
    End Sub

    ' 部屋ではない行の RoomId（ChatRooms の RoomId は IDENTITY で1から振られるので、0 と重ならない）
    Public Const NoRoom As Integer = 0

    ' ログインの目印の Cookie の名前
    '
    ' 【Default.aspx の決定ボタンの JS と、同じ名前にしておくこと】
    ' あちらが書き、ラウンジ（ここ）が読んで消す。片方だけ変えると、ログインが1件も記録されなくなる
    ' （エラーは出ない）。ログアウトの掃除は、名前を並べずに全部消すので、何も足さなくてよい
    Public Const JustLoggedInCookieName As String = "ChatJustLoggedIn"

    ' 発行後の確かめの道具が名乗る User-Agent
    '
    ' 発行後の確かめの道具が、この文字列を User-Agent に入れて叩く。
    ' 一致した要求は、**出入りの記録だけを飛ばす**（ページの中身も、ほかの動きも1つも変えない）。
    '
    ' 【偽れることは承知のうえ】User-Agent は誰でも名乗れる。
    ' それでも困らないのは、**偽ってできるのが「自分を数えさせない」ことだけ**だからである
    ' （集計メールに載らなくなるだけで、入れる部屋も、書ける発言も、1つも変わらない）。
    ' 監視・点検の通信を集計から外すのは、ふつうにやることでもある。
    '
    ' 【道具の側の文字列と、同じにしておくこと】片方だけ変えると、道具を走らせるたびに
    ' 本番の集計に1行ずつ足されていく（エラーは出ない）
    Public Const ProbeUserAgent As String = "vb-chat-probe"

    ' 以下は Lock の中でだけ触る
    Private Shared ReadOnly Lock As New Object()

    ' 表がどの日の分か（アプリの時計の日付。DB と同じ日本時間）
    Private Shared _recordedDay As Date = Date.MinValue

    ' 種類 → 今日もう書いた身元
    Private Shared ReadOnly RecordedToday As New Dictionary(Of ChatMessageType, HashSet(Of Guid))


    ' ラウンジを開いたとき（Chat/Lounge.aspx.vb の Page_Load から）
    '
    ' ログインの目印があれば、50を書いて目印を消す。続けて52（1日1回）を書く。
    ' 順番は 50 → 52（時系列で「ログインしてから、ラウンジに来た」の順に並べる）
    Public Shared Sub RecordLoungeArrival(context As HttpContext)

        Try

            If context Is Nothing Then
                Return
            End If

            Dim marker As HttpCookie = context.Request.Cookies(JustLoggedInCookieName)

            If marker IsNot Nothing Then

                ' 【目印は、書けたかどうかに関係なく必ず消す】
                ' 残すと、次にラウンジを開いたときにまたログインとして数えられる
                ' （寿命60秒のうちに F5 した、など。1日1回の歯止めでも止まるが、それに頼らない）
                ExpireCookie(context, JustLoggedInCookieName)

                Record(context, ChatMessageType.Login)

            End If

            Record(context, ChatMessageType.LoungeVisit)

        Catch ex As Exception
            ErrorReport.Report(ex, "SiteVisitLog.RecordLoungeArrival")
        End Try

    End Sub


    ' ラウンジのログアウトのボタンが押されたとき（Chat/ChatLogout.vb の SignOut から）
    '
    ' 【Cookie を消す「前」に呼ぶこと】誰のログアウトかは ChatBrowserId で決まる。
    ' SignOut は届いた Cookie を全部消すので、あとからでは名乗る値が無くなる
    Public Shared Sub RecordLogout(context As HttpContext)

        Try

            If context Is Nothing Then
                Return
            End If

            Record(context, ChatMessageType.LoungeLogout)

        Catch ex As Exception
            ErrorReport.Report(ex, "SiteVisitLog.RecordLogout")
        End Try

    End Sub


    ' 1行書く（歯止めを通ったときだけ）
    Private Shared Sub Record(context As HttpContext, messageType As ChatMessageType)

        Dim request As HttpRequest = context.Request

        ' 発行後の確かめの道具は数えない（上の ProbeUserAgent）
        If IsProbe(request) Then
            Return
        End If

        Dim browserIdCookie As HttpCookie = request.Cookies("ChatBrowserId")

        If browserIdCookie Is Nothing Then
            Return
        End If

        ' 【Guid に直してから覚えること】Cookie の生の文字列は、大文字・小文字や波かっこで表記が揺れうる。
        ' 文字列のまま覚えると、同じ身元を別人として2回書く（ChatHub.LeaveByBrowserId と同じ理由）
        Dim browserId As Guid

        If Not Guid.TryParse(browserIdCookie.Value, browserId) Then
            Return
        End If

        ' 名前は、入室（JoinRoom）・ラウンジの顔（JoinLounge）と同じ出どころ＝**名前の札**から読む
        ' （名前の Cookie から移した。あれは JS から書き換えられる）。
        ' 検査も同じものを通してから焼き込む。検査しない値を書くと、列の幅（Nickname は NVARCHAR(50)）を
        ' 超えた値で INSERT が落ち、そのたびにエラーのメールが来る（外から誰でも起こせる）
        '
        ' 【ここは鍵の外】NormalizeNickname は名前の長さの上限を SiteSettings から読む
        Dim nickname As String = ChatProfileValidator.NormalizeNickname(NameToken.ReadNickname(request))

        If String.IsNullOrEmpty(nickname) Then
            Return
        End If

        ' 上限は鍵を取る前に読む（SiteSettings はキャッシュが空のとき DB を待つ。鍵の中で待たない）
        Dim dailyLimit As Integer = SiteSettings.VisitLogDailyLimit

        If Not ShouldRecord(messageType, browserId, dailyLimit) Then
            Return
        End If

        Dim iconTypeCookie As HttpCookie = request.Cookies("ChatIconType")
        Dim iconColorCookie As HttpCookie = request.Cookies("ChatIconColor")

        ' 【アカウントID を表示している人には、トリップを焼き込まない】（どちらか一方）
        ' 合言葉の Cookie が残っていても、その人は今トリップを出していない。部屋の行（JoinRoom）と同じ決め方にそろえる
        ' （決めるのは Chat/ChatAccountLink.vb。1回の要求の中では DB は1回。ここへ来るのは、1つの身元につき1日1回だけ）。
        ' **アカウントの番号（UserId）は、ここでは入れない**——出入りの記録は「ブラウザの数」を数えるためのもので、
        ' 「自分が参加したチャット履歴」（部屋の入室〜退室）とは別の話である
        '
        ' ChatLog.Write は Participant を受け取る（名前・アイコン・トリップ・IP を、その時点の値で焼き込む）。
        ' 部屋の番号（LocalId）は無いので 0 のまま＝NULL で書かれる（満員拒否の40と同じ）
        Dim visitor As New Participant With {
            .Nickname = nickname,
            .IconType = ChatProfileValidator.NormalizeIconType(
                If(iconTypeCookie Is Nothing, Nothing, iconTypeCookie.Value)),
            .IconColor = ChatProfileValidator.NormalizeIconColor(
                If(iconColorCookie Is Nothing, Nothing, iconColorCookie.Value)),
            .Trip = If(ChatAccountLink.Resolve(context).IsShowing, Nothing, TripCode.FromRequest(request)),
            .BrowserId = browserIdCookie.Value,
            .IpAddress = ReadIpAddress(request),
            .RemotePort = ClientEndpoint.ReadPort(request)
        }

        ChatLog.Write(NoRoom, messageType, visitor)

    End Sub


    ' この種類・この身元を、今日書いてよいか（書いてよければ、書いたものとして覚える）
    '
    ' dailyLimit … SiteSettings.VisitLogDailyLimit。呼ぶ側が鍵の手前で読んで渡す
    '
    ' 【覚えてから書く】書き込み（DB）は鍵の外で、このあとに行う。
    ' 書き込みに失敗しても、その日はもう書かない（ChatLog.Write が ErrorReport へ知らせる）。
    ' 失敗したら覚え直す形にすると、DB が落ちている間、ラウンジを開くたびに書きに行くことになる
    Private Shared Function ShouldRecord(messageType As ChatMessageType,
                                         browserId As Guid,
                                         dailyLimit As Integer) As Boolean

        SyncLock Lock

            ' 日付が変わったら、表を丸ごと空にする（掃除のタイマーは要らない。次に書くときに気づけば足りる）
            Dim today As Date = Date.Today

            If today <> _recordedDay Then
                RecordedToday.Clear()
                _recordedDay = today
            End If

            Dim recorded As HashSet(Of Guid) = Nothing

            If Not RecordedToday.TryGetValue(messageType, recorded) Then
                recorded = New HashSet(Of Guid)()
                RecordedToday(messageType) = recorded
            End If

            ' 1段目：今日もう書いた身元
            If recorded.Contains(browserId) Then
                Return False
            End If

            ' 2段目：表が埋まっている日は、新しい身元を書かない
            If recorded.Count >= dailyLimit Then
                Return False
            End If

            recorded.Add(browserId)
            Return True

        End SyncLock

    End Function


    ' Cookie を消す（ChatLogout の掃除と同じ形。path を揃えないと、消したつもりで別の Cookie を作って終わる）
    Private Shared Sub ExpireCookie(context As HttpContext, name As String)

        Dim expired As New HttpCookie(name, String.Empty) With {
            .Path = "/",
            .Expires = New DateTime(1999, 1, 1),
            .Secure = True,
            .SameSite = SameSiteMode.Lax
        }

        ' Add ではなく Set（同じ名前で2本出さない）
        context.Response.Cookies.Set(expired)

    End Sub


    ' 発行後の確かめの道具からの要求か（上の ProbeUserAgent）
    '
    ' 【見るのは User-Agent だけ。ページの中身は1つも変えない】
    ' ここで変わるのは「記録するかどうか」だけで、道具が見ているもの（ステータス・転送先・
    ' 本文・Set-Cookie・ビルドの目印）は本物の利用者とまったく同じである。
    ' **道具にだけ都合のいい画面を返す形にしないこと**——それをやると、道具が見ているのは
    ' 本番の姿ではなくなり、検査そのものが嘘になる
    Private Shared Function IsProbe(request As HttpRequest) As Boolean

        Dim agent As String = Nothing

        Try
            agent = request.UserAgent
        Catch ex As Exception
            Return False
        End Try

        Return Not String.IsNullOrEmpty(agent) AndAlso
               agent.IndexOf(ProbeUserAgent, StringComparison.OrdinalIgnoreCase) >= 0

    End Function


    ' IP アドレス（ログ用。取れなくても止めない＝NULL で書かれるだけ。JoinRoom と同じ）
    Private Shared Function ReadIpAddress(request As HttpRequest) As String

        Try
            Return request.UserHostAddress
        Catch ex As Exception
            ' 取れなかったときは、ログの IpAddress 列が NULL になるだけ（知らせない。JoinRoom と同じ）
            Return Nothing
        End Try

    End Function

End Class
