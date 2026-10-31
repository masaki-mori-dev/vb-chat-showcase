Imports System.Threading

' 「ログインする」「ログアウトする」「今、誰としてログインしているか」を決める処理
'
' 【なぜ .aspx ではなく、クラスに置くのか】
' Chat/ChatLogin.vb・Chat/ChatLogout.vb とまったく同じ理由。
' 近い将来 C#／ASP.NET Core MVC へ移るときに .aspx は捨てるが、
' 「何を、どの順で確かめるか」はそのまま持っていきたい。
' 受け口（Accounts/Login.aspx・Accounts/Logout.aspx）は、ここを呼んで画面を移すだけにしてある。
' HttpContext.Current を使わず引数で受け取るのも、同じ理由（移った先に Current は無い）。
'
' 【ChatLogin／ChatLogout とは、別の身元である】
' あちらはチャットの Cookie ユーザー（名前の札）、こちらはアカウント。
'
' 【、2つは「結び付けられる」ようになった】（チャットとアカウントの紐づけ）
' ログインしている人がトップページで「アカウントID を表示する」を入れたまま名前を決めると、
' チャットの身元に、このアカウントの番号が結び付く（Chat/NameToken.vb の v2・Chat/ChatAccountLink.vb）。
' それでも**身元そのものは別のまま**である——名前の札だけでも、ログインの札だけでも、アカウントID は出ない。
'
' 【チャットを出ても、アカウントのログインは残る】（同じ日に変えた）
' それまでは ChatLogout が「届いた Cookie を全部消す」ので、AccountAuth も一緒に消えていた（承知のうえだった）。
' 紐づけができると、「チャットを出て、名前を決め直す（アカウントID を出す／出さないを選び直す）」たびに
' ログインし直すことになる。ChatLogout の側で、ログインの札だけを残すようにした（決め方と理由は ChatLogout.vb）。
'
' 逆向き（アカウントのログアウトで、チャットの Cookie も消す）は、今も**しない**。
' アカウントのログアウトが消すのは AccountAuth 1つだけである。
' **それで足りる**——ログインの札が無くなれば、チャットの側のアカウントID は次に繋いだときから出なくなる
' （ChatAccountLink が、毎回ログインの札を確かめているため）
Module AccountSession


    ' ログインの結果
    Public Class SignInResult

        ' 受け付けたか
        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す
        Public Property Message As String

    End Class


    ' 断るときの文言（ひとつだけ）
    '
    ' 【「どちらが違うか」を言わない】（記事の章のログインの決まり③）
    ' 「そのユーザー名は登録されていません」と出すと、**アカウントが在るかどうかを外から確かめられる**。
    ' 止めているアカウント（IsActive = 0）のときも、同じ文言で断る
    Private Const BadCredentialsMessage As String =
        "アカウントID（またはメールアドレス）か、パスワードが違います。"

    ' DB が読めなかったときの文言
    '
    ' 【理由を利用者に見せない】中で何が起きたかは、エラー報告メールで開発者に届く
    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"

    ' 1回の要求の中で、同じ人を何度も DB から引かないための置き場所
    Private Const CurrentItemKey As String = "vbchat.CurrentAccount"

    ' 「在るアカウント」と「無いアカウント」で、かかる時間を揃えるための、捨てハッシュ
    '
    ' 【なぜ要るのか】アカウントが無いときにパスワードの計算を飛ばすと、
    ' **返事が速い＝そのユーザー名は存在しない**と分かってしまう（文言を揃えた意味が無くなる）。
    ' 無いときも同じだけ計算する。作るのは1回だけ（初めて必要になったとき）
    Private ReadOnly DummyHash As New Lazy(Of String)(
        Function() AccountPassword.Hash("timing-placeholder-not-a-real-password"))


    ' === ログインする ===
    '
    ' loginId  … ユーザー名またはメールアドレス（欄は1つ）
    ' password … パスワード
    Public Function SignIn(context As HttpContext, loginId As String, password As String) As SignInResult

        If context Is Nothing Then
            Return Rejected(TroubleMessage)
        End If

        Dim id As String = AccountStore.NormalizeLoginId(loginId)
        Dim secret As String = AccountPassword.Normalize(password)

        If id.Length = 0 OrElse secret.Length = 0 Then
            Return Rejected(BadCredentialsMessage)
        End If

        ' 長すぎる入力は、計算する前に断る（PBKDF2 に長い文字列を渡させないため）。
        ' 文言は同じ——「長さが違う」ことすら教えない
        If id.Length > AccountStore.EmailMaxLength OrElse secret.Length > AccountPassword.MaxLength Then
            Return Rejected(BadCredentialsMessage)
        End If

        Dim ip As String = Nothing

        Try
            ip = context.Request.UserHostAddress
        Catch ex As Exception
            ip = Nothing
        End Try

        ' === 歯止め（1）このIPは、受け付けない状態か ===
        '
        ' 【パスワードの計算より先に見る】受け付けない間は、CPU を使う計算まで行かない
        ' （部屋のパスワードと同じ順番）
        Dim locked As Integer = AccountPassword.GetLockoutSeconds(ip)

        If locked > 0 Then
            Return Rejected("ログインの失敗が続いたため、あと約" & locked &
                            "秒は受け付けません。少し待ってから、もう一度お試しください。")
        End If

        ' === 歯止め（2）このログインIDは、間違いが続いているか ===
        '
        ' 【ロックではなく、遅らせるだけ】
        ' ロックにすると、管理人のユーザー名（記事のURLに出るので公開されている）を
        ' わざと間違えるだけで、本人を締め出せてしまう。
        ' 【DB を引く前に待つ】そのアカウントが在るかどうかで、待ち時間が変わらないようにするため
        Dim delay As TimeSpan = AccountPassword.GetDelay(id)

        If delay > TimeSpan.Zero Then
            Thread.Sleep(delay)
        End If

        ' 設定は、この先の処理に入る前にまとめて読んでおく
        Dim maxFailures As Integer = SiteSettings.AccountLoginMaxFailures
        Dim lockout As TimeSpan = SiteSettings.AccountLoginLockout
        Dim lifetime As TimeSpan = SiteSettings.AccountLoginCookieLifetime

        Dim account As AccountStore.AccountRecord = Nothing

        Try
            account = AccountStore.FindForLogin(id)
        Catch ex As Exception

            ' 【DB が読めないときは、通さない（フェイルクローズ）】
            ' SiteSettings は既定値で通すが、ログインで同じことをすると、そのまま素通りになる
            ErrorReport.Report(ex, "AccountSession.SignIn（アカウントを引く）")

            Return Rejected(TroubleMessage)

        End Try

        ' === パスワードを確かめる ===
        '
        ' 【アカウントが無いときも、同じだけ計算する】上の DummyHash の理由
        Dim ok As Boolean

        If account Is Nothing Then
            AccountPassword.Verify(secret, DummyHash.Value, 0)
            ok = False
        Else
            ok = AccountPassword.Verify(secret, account.PasswordHash, account.AccountId)
        End If

        ' 【止めているアカウントも、同じ文言で断る】上の BadCredentialsMessage の理由
        If Not ok OrElse Not account.IsActive Then

            AccountPassword.RecordFailure(id, ip, maxFailures, lockout)

            Return Rejected(BadCredentialsMessage)

        End If

        ' === 札を作って配る ===
        Dim token As String = AccountAuth.Issue(account.AccountId, account.TokenGeneration, lifetime)

        If String.IsNullOrEmpty(token) Then

            ' 鍵がどうしても手に入らなかった（ふつう起きない。AccountAuth が知らせのメールを出している）。
            ' ここで通しても、次のページで札が読めずログイン画面へ戻されるだけなので、断る
            Return Rejected(TroubleMessage)

        End If

        AccountAuth.WriteCookie(context.Response, token, lifetime)

        AccountPassword.RecordSuccess(id)
        AccountStore.TouchLastLogin(account.AccountId)

        ' この要求の中では、もう DB を引かなくてよい
        context.Items(CurrentItemKey) = account

        Return New SignInResult With {.Ok = True}

    End Function


    ' === 登録した直後の人を、そのままログインさせる（一般ユーザーの登録） ===
    '
    ' 【パスワードを確かめない。だから、呼べる場所を限る】
    ' 呼んでよいのは **Accounts/AccountRegistration.vb の Complete だけ**である。
    ' あそこは「確認のメールのリンクを開けた＝そのアドレスの持ち主」まで確かめ終わっていて、
    ' さらに Accounts の行を作った直後なので、**身元はもう確かである**。
    '
    ' 【なぜ SignIn を使い回さないのか】あちらは「打たれたパスワードを照合する」処理である。
    ' 登録の直後にパスワードをもう一度打たせるのは、利用者にとって意味の無い手間だし、
    ' こちらで生のパスワードを持ち回すことになる（仮登録の行にはハッシュしか無い）。
    '
    ' 【新しい口を足したくなったら、まずここを読むこと】
    ' 「パスワード無しでログインさせる」処理は、1つ増やすたびに乗っ取りの入口が1つ増える。
    ' 足すなら、**その前に身元がどう確かめられているか**を、必ず書き残すこと
    Friend Function SignInNewAccount(context As HttpContext, account As AccountStore.AccountRecord) As Boolean

        If context Is Nothing OrElse account Is Nothing Then
            Return False
        End If

        Dim lifetime As TimeSpan = SiteSettings.AccountLoginCookieLifetime

        Dim token As String = AccountAuth.Issue(account.AccountId, account.TokenGeneration, lifetime)

        If String.IsNullOrEmpty(token) Then

            ' 鍵がどうしても手に入らなかった（ふつう起きない。AccountAuth が知らせのメールを出している）。
            ' **アカウントはもう作られている**ので、ログインの画面から入ってもらえばよい
            Return False

        End If

        AccountAuth.WriteCookie(context.Response, token, lifetime)

        AccountStore.TouchLastLogin(account.AccountId)

        ' この要求の中では、もう DB を引かなくてよい
        context.Items(CurrentItemKey) = account

        Return True

    End Function
    ' === ログアウトする ===
    '
    ' 【消すのは、ログインの札1つだけ】
    ' チャットの Cookie（名前・アイコン・通知音など）には触らない。
    ' チャットのログアウト（ChatLogout.SignOut）が「全部消す」なのと、意図して逆にしてある——
    ' あちらは「別人になる」操作、こちらは「管理人の画面から出る」操作で、重さが違う
    Public Sub SignOut(context As HttpContext)

        If context Is Nothing Then
            Return
        End If

        AccountAuth.ClearCookie(context.Response)

        context.Items.Remove(CurrentItemKey)

    End Sub


    ' === 今、誰としてログインしているか（ログインしていなければ Nothing） ===
    '
    ' 【札だけでは足りない】札は「このアカウントIDで、この世代で、この期限まで」を証明するだけで、
    ' そのアカウントが今も有効かは分からない。毎回 DB を1行引いて、
    '   ・行が在るか
    '   ・IsActive が立っているか
    '   ・TokenGeneration が、札の中の世代と同じか（パスワードを変えたら進む＝古い札は無効）
    ' の3つを確かめる。**この3つを飛ばすと、止めたアカウントが7日間生き続ける**
    '
    ' 【1回の要求の中では、1回しか引かない】HttpContext.Items に控える
    Public Function Current(context As HttpContext) As AccountStore.AccountRecord

        If context Is Nothing Then
            Return Nothing
        End If

        If context.Items.Contains(CurrentItemKey) Then
            Return TryCast(context.Items(CurrentItemKey), AccountStore.AccountRecord)
        End If

        Dim account As AccountStore.AccountRecord = Resolve(context)

        ' 見つからなかったこと（Nothing）も控える。控えないと、
        ' ログインしていない人のページで、DB を何度も引くことになる
        context.Items(CurrentItemKey) = account

        Return account

    End Function


    ' === この要求の中の控えを捨てる ===
    '
    ' 表示名・メールアドレス・パスワード（鍵の世代）を変えたあとに呼ぶ。
    ' 捨てないと、同じ要求の中で古い値（古い世代）の控えを使い続ける
    Public Sub Forget(context As HttpContext)

        If context Is Nothing Then
            Return
        End If

        context.Items.Remove(CurrentItemKey)
        context.Items.Remove(RoleNameItemKey)

    End Sub


    Private Function Resolve(context As HttpContext) As AccountStore.AccountRecord

        Dim cookie As HttpCookie = context.Request.Cookies(AccountAuth.CookieName)

        If cookie Is Nothing Then
            Return Nothing
        End If

        Return ResolveByCookieValue(cookie.Value)

    End Function


    ' === ログインの札の「値」から、アカウントを確かめる（Resolve から切り出した） ===
    '
    ' 【なぜ切り出したか】チャットのハブ（SignalR）には HttpContext が無い（WebSocket では Current が Nothing になりうる）。
    ' ハブが読めるのは Cookie の値だけなので、**「札 → DB の3つの確かめ」を、値だけで呼べる形にして1か所に置いた**。
    ' ページ（上の Resolve）も、ハブ・ラウンジ（Chat/ChatAccountLink.vb）も、必ずここを通る。
    ' **同じ確かめを、よそに書き写さないこと**（写すと、確かめを1つ足した日に片方だけ古くなる）。
    '
    ' 【控えない】1回の要求の中の控え（HttpContext.Items）は、呼ぶ側（Current）の仕事。ここは毎回 DB を1行引く
    Friend Function ResolveByCookieValue(cookieValue As String) As AccountStore.AccountRecord

        If String.IsNullOrEmpty(cookieValue) Then
            Return Nothing
        End If

        Dim ticket As AccountAuth.Ticket = AccountAuth.Read(cookieValue)

        If ticket Is Nothing Then
            Return Nothing
        End If

        Dim account As AccountStore.AccountRecord

        Try
            account = AccountStore.FindById(ticket.AccountId)
        Catch ex As Exception

            ' 【ここもフェイルクローズ】DB が読めないなら、ログインしていないものとして扱う。
            ' 通すと、止めたアカウントが DB の詰まっている間だけ生き返ることになる
            ErrorReport.Report(ex, "AccountSession.Current（アカウントを引く）")

            Return Nothing

        End Try

        If account Is Nothing OrElse Not account.IsActive Then
            Return Nothing
        End If

        ' 鍵の世代が合っているか（パスワードを変えたら、それまでの札は全部ここで落ちる）
        If account.TokenGeneration <> ticket.Generation Then
            Return Nothing
        End If

        Return account

    End Function


    ' === 今のアカウントのロールの日本語名（「管理人」など） ===
    '
    ' 【DB の Roles.DisplayName から引く】（Home.aspx.vb から移した）
    ' ホーム（/account）とヘッダーの「表示名（ロール）」の2か所が使うので、ここに1つだけ置く。
    ' 1回の要求の中では1回しか引かない（HttpContext.Items に控える）。
    '
    ' 【引けなければ、ロールの名前（英字）をそのまま返す＝フェイルオープン】
    ' 見せるだけの値なので、DB が一瞬詰まっただけで画面を止めない
    ' （ロールごとの上限 RoleLimits は、逆にフェイルクローズ。あちらは「できること」を決める値）
    Private Const RoleNameItemKey As String = "vbchat.CurrentRoleName"

    ' ロールの名前の控え（ロールのキー → 日本語名）
    Private ReadOnly RoleNameLifetime As TimeSpan = TimeSpan.FromMinutes(10)
    Private ReadOnly RoleNameCacheLock As New Object()
    Private ReadOnly _roleNames As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private _roleNamesExpireAt As DateTime = DateTime.MinValue

    ' ロールの名前の控えを捨てる（DB を直接書き換えたあと。Core/CacheReset.vb）
    Public Sub InvalidateRoleNames()
        SyncLock RoleNameCacheLock
            _roleNames.Clear()
            _roleNamesExpireAt = DateTime.MinValue
        End SyncLock
    End Sub

    Public Function CurrentRoleName(context As HttpContext) As String

        Dim account As AccountStore.AccountRecord = Current(context)

        If account Is Nothing Then
            Return ""
        End If

        If context.Items.Contains(RoleNameItemKey) Then
            Return CStr(context.Items(RoleNameItemKey))
        End If

        Dim name As String = account.RoleKey

        ' 【メモリの控え（10分）を先に見る】
        ' 右上の角のメニュー（Site.Master）がロールの名前を出すようになり、**ログインしている人のすべてのページ**で呼ばれる。
        ' ロールの名前は SSMS で Roles を書き換えたときにしか変わらない（5つ・管理画面から変える口は無い）ので、
        ' 毎回 DB を引かず、ロールごとに控える。書き換えたときは、最大10分で反映される
        Dim cached As String = Nothing

        SyncLock RoleNameCacheLock
            If DateTime.UtcNow < _roleNamesExpireAt Then
                _roleNames.TryGetValue(account.RoleKey, cached)
            Else
                _roleNames.Clear()
                _roleNamesExpireAt = DateTime.UtcNow + RoleNameLifetime
            End If
        End SyncLock

        If cached IsNot Nothing Then
            name = cached
        Else

            Try

                Dim found As String = AccountStore.FindRoleDisplayName(account.RoleKey)

                If Not String.IsNullOrEmpty(found) Then
                    name = found

                    SyncLock RoleNameCacheLock
                        _roleNames(account.RoleKey) = found
                    End SyncLock
                End If

            Catch ex As Exception
                ErrorReport.Report(ex, "AccountSession.CurrentRoleName")
            End Try

        End If

        context.Items(RoleNameItemKey) = name

        Return name

    End Function


    Private Function Rejected(message As String) As SignInResult
        Return New SignInResult With {.Ok = False, .Message = message}
    End Function


End Module
