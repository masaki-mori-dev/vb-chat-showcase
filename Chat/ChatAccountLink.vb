' チャットの身元と、アカウントの結び付き（チャットとアカウントの紐づけ）
'
' 【何をするものか】
' 「この人は今、アカウントID を表示して参加しているか。しているなら、どのアカウントか」を決める、ただ1つの場所。
' 部屋（ChatHub.JoinRoom）・ラウンジ（ChatHub.JoinLounge・Lounge.aspx・RoomList.ascx）・トリップの付け外し
' （Lounge.aspx の SetTripKey）が、みんなここを呼ぶ。**同じ判定を、よそに書き写さないこと。**
' 【付け外し】①の札を書き直すのは Chat/ChatLogin.vb だけ（名前を決めるとき＝SignIn、
' ラウンジのスイッチ＝ChangeAccountIdShown（ChatBrowserId を変えない＝別人にならない））
'
' 【アカウントID を出してよいのは、2つの札が両方そろっているときだけ】
'   ① 名前の札（ChatNameToken）に、アカウントの番号が入っている
'        ＝名前を決めたときに、本人が「アカウントID を表示する」を選んだ（Chat/NameToken.vb の v2）
'   ② ログインの札（AccountAuth）が、**同じアカウント**のもので、今も有効である
'        ＝署名・期限・DB の3つ（行が在る／止めていない／鍵の世代が合う）。AccountSession.ResolveByCookieValue が確かめる
'
'   ①だけ … 出さない（ログアウトした・7日で切れた・ほかの端末でパスワードを変えた・退会した）
'   ②だけ … 出さない（ログインはしているが、名乗らずに入ると決めた人＝**名乗らずに入る自由**）
'   番号が食い違う … 出さない（同じブラウザで、別のアカウントにログインし直した）
'
' 【画面やブラウザから送られた値を、1つも信じていない】
' どちらの札もサーバーが署名したもので、HttpOnly。「表示する」のチェックは名前を決めるときに1回だけ受け取り、
' そのときもアカウントの番号はログインの札から読む（Chat/ChatLogin.vb）。
' ChatBrowserId は本人の証明ではない（Cookie を写せば名乗れる）ので、これだけでアカウントを引き当てることはしない。
'
' 【DB を1行引く。だから、鍵（SyncLock）の中から呼ばないこと】
' 引くのは①が入っている人だけ（名乗らずに入っている人・アカウントの無い人は、DB に触らない）。
' ChatHub の鍵の中から呼ぶと、DB が詰まっている間、全部屋のチャットが止まる（SiteSettings と同じ罠）。
'
' 【読めなければ、出さない（フェイルクローズ）】
' DB が読めないときに「たぶん本人」で出すと、止めたアカウントの ID が、DB の詰まっている間だけ生き返る。
' 出ないだけで、入室もラウンジも今までどおり動く（トリップのソルトが読めないときと同じ形）。
'
' 【トリップとは、どちらか一方】
' アカウントID を出している間は、トリップを出さない・付けさせない。
' その切り替えも、ここが返す結果で決める（呼ぶ側が自分で Cookie を見て決めないこと）。
Module ChatAccountLink


    ' 今の状態
    Public Enum LinkState

        ' 結び付けていない（今までどおりの参加者。トリップを使える）
        None = 0

        ' アカウントID を表示している
        Showing = 1

        ' 名前を決めたときは「表示する」にしたが、今はログインしていない（か、別のアカウントでログインしている）。
        ' アカウントID は出さない。ラウンジが「ログインし直すと、また出ます」と案内するためだけに、None と分けてある
        NeedsLogin = 2

    End Enum


    ' 判定の結果
    Public Class LinkResult

        Public Property State As LinkState = LinkState.None

        ' アカウントの番号（Showing のときだけ 1 以上。ログの UserId に入れる）
        Public Property AccountId As Integer

        ' アカウントのユーザー名（Showing のときだけ。画面に「@ユーザー名」と出し、公開のプロフィールへつなぐ）
        '
        ' 【名前（表示名）ではなく、ユーザー名を出す】表示名は本人がいつでも変えられ、ほかの人と重ねることもできる。
        ' ユーザー名は重ならず・変えられず・公開のプロフィールの住所そのものなので、「この人はこのアカウント」の印になる
        Public Property UserName As String

        Public ReadOnly Property IsShowing As Boolean
            Get
                Return State = LinkState.Showing
            End Get
        End Property

    End Class


    Private ReadOnly NoLink As New LinkResult()


    ' 3つの Cookie の値から決める（ハブ用。ハブには HttpContext が無いので、値だけで呼べる形にしてある）
    '
    ' browserIdRaw … ChatBrowserId の生の値
    ' nameTokenRaw … ChatNameToken の生の値
    ' accountAuthRaw … AccountAuth の生の値（無ければ Nothing）
    Public Function Resolve(browserIdRaw As String, nameTokenRaw As String, accountAuthRaw As String) As LinkResult

        ' ① 名前の札に、アカウントの番号が入っているか（署名も、この身元のものかも、NameToken が確かめる）
        Dim wantedAccountId As Integer = NameToken.ReadAccountId(browserIdRaw, nameTokenRaw)

        If wantedAccountId <= 0 Then
            Return NoLink
        End If

        ' ② ログインの札が、同じアカウントのもので、今も有効か
        Dim account As AccountStore.AccountRecord = Nothing

        Try
            account = AccountSession.ResolveByCookieValue(accountAuthRaw)
        Catch ex As Exception

            ' ResolveByCookieValue は自分で握って Nothing を返す作りだが、念のため（ここから例外を漏らすと、入室そのものが落ちる）
            ErrorReport.Report(ex, "ChatAccountLink.Resolve")
            account = Nothing

        End Try

        If account Is Nothing OrElse account.AccountId <> wantedAccountId Then
            Return New LinkResult With {.State = LinkState.NeedsLogin}
        End If

        Return New LinkResult With {
            .State = LinkState.Showing,
            .AccountId = account.AccountId,
            .UserName = account.UserName
        }

    End Function


    ' 1回の要求の中で、同じ判定を何度も DB から引かないための置き場所
    Private Const ItemKey As String = "vbchat.ChatAccountLink"


    ' ページの要求から決める（ラウンジのページ・部屋の一覧のコントロール・WebMethod 用）
    '
    ' 【1回の要求の中では、1回しか引かない】HttpContext.Items に控える（AccountSession.Current と同じ形）。
    ' ラウンジのページは、プロフィールの枠（Lounge.aspx.vb）と顔の一覧（RoomList.ascx.vb）の2か所から呼ぶ。
    ' 【HttpContext.Current を使わず、引数で受け取る】ChatLogin・ChatLogout と同じ理由（移った先に Current は無い）
    Public Function Resolve(context As HttpContext) As LinkResult

        If context Is Nothing Then
            Return NoLink
        End If

        If context.Items.Contains(ItemKey) Then
            Return DirectCast(context.Items(ItemKey), LinkResult)
        End If

        Dim request As HttpRequest = context.Request

        Dim result As LinkResult = Resolve(CookieValue(request, "ChatBrowserId"),
                                           CookieValue(request, NameToken.CookieName),
                                           CookieValue(request, AccountAuth.CookieName))

        context.Items(ItemKey) = result

        Return result

    End Function


    ' 画面に出す形（「@ユーザー名」）。出すものが無ければ Nothing
    '
    ' 【ここで HTML にしない】返すのは文字だけ。出口（.aspx・JS）が、それぞれエスケープして出す
    Public Function DisplayText(userName As String) As String

        If String.IsNullOrEmpty(userName) Then
            Return Nothing
        End If

        Return "@" & userName

    End Function


    ' アカウントID を押したときの行き先（公開のプロフィール）。URL を組み立てるのは AccountProfileRules の1か所
    Public Function ProfileUrl(userName As String) As String

        If String.IsNullOrEmpty(userName) Then
            Return Nothing
        End If

        Return AccountProfileRules.PublicUrl(userName)

    End Function


    Private Function CookieValue(request As HttpRequest, name As String) As String

        Dim cookie As HttpCookie = request.Cookies(name)

        If cookie Is Nothing Then
            Return Nothing
        End If

        Return cookie.Value

    End Function


End Module
