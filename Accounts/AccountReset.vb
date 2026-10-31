Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' パスワードを忘れたとき
'
' 【何を、どの順で確かめるか】をここに持つ（AccountSession・AccountSettings と同じ立ち位置）。
' 画面（Accounts/Forgot.aspx・Reset.aspx・Recover.aspx）は、ここを呼んで結果を出すだけ。
' C#／ASP.NET Core MVC へ移るときに持っていく。HttpContext.Current を使わず引数で受け取るのも同じ理由。
'
Module AccountReset


    ' 結果
    Public Class ResetResult

        Public Property Ok As Boolean

        ' 画面に出す文言（出口でエスケープする）
        Public Property Message As String

        ' リンクがもう使えない（画面は欄を隠して「送り直す」へ案内する）
        Public Property LinkDead As Boolean

        ' 断ったとき、どの欄が悪いか（下の Field〇〇。どの欄とも言えなければ Nothing）。
        ' 画面がその欄に印を付け、理由がその欄の上に出る（Core/FormErrors.vb）
        Public Property Field As String

    End Class


    ' === どの欄が悪いか ===
    Public Const FieldLoginId As String = "loginid"
    Public Const FieldNewPassword As String = "newpassword"
    Public Const FieldConfirm As String = "confirm"


    ' === 決まり（防御なので SiteSettings に入れない。AccountPassword.MinLength と同じ判断）===

    ' 合言葉の期限
    Public ReadOnly TokenLifetime As TimeSpan = TimeSpan.FromMinutes(30)

    ' 同じアカウントへ送る間隔（これより短い間に2回頼まれても、2通目は送らない。答えは同じ）
    Private ReadOnly MinResendInterval As TimeSpan = TimeSpan.FromMinutes(5)

    ' 同じ IP からの依頼（15分に5回まで）
    Private ReadOnly RequestLimiter As New RateCounter(TimeSpan.FromMinutes(15), 5)

    ' リンクの画面で、合言葉が合わなかった回数（同じ IP。10分に10回まで）
    Private ReadOnly TokenMissLimiter As New RateCounter(TimeSpan.FromMinutes(10), 10)

    ' 合言葉の長さ（32バイトの乱数 → Base64URL で43文字）
    Private Const TokenBytes As Integer = 32
    Private Const TokenLength As Integer = 43

    ' リンクの場所と、クエリの名前（メールの本文と画面が使う）
    Public Const ResetPath As String = "/account/reset"
    Public Const TokenQueryName As String = "t"

    ' 合言葉を一時的に預ける Cookie（アドレスバー・履歴・Referer に合言葉を残さないため）
    Public Const TokenCookieName As String = "AccountResetToken"

    ' どのアカウントか分からない答え（在っても無くても、これだけを返す）
    Public Const SentMessage As String =
        "そのメールアドレス（またはアカウントID）のアカウントがあれば、パスワードの再設定のリンクをメールで送りました。" &
        "届かないときは、迷惑メールのフォルダも見てください。リンクは30分だけ使えます。"

    Private Const InvalidLinkMessage As String =
        "このリンクは使えません（期限が切れたか、もう使われたか、新しいリンクを送り直したため）。" &
        "もう一度「パスワードを忘れたとき」から、リンクを送ってください。"

    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"


    ' ============================================================
    ' ① 依頼を受け付ける（/account/forgot）
    ' ============================================================
    '
    ' 【在っても無くても、同じ答え・同じ速さ】アカウントを引くところから、裏の作業でやる。
    ' 画面は引く前に答えを返すので、かかる時間でも見分けられない
    Public Function RequestReset(context As HttpContext, loginId As String) As ResetResult

        If context Is Nothing Then
            Return Fail(TroubleMessage)
        End If

        Dim id As String = AccountStore.NormalizeLoginId(loginId)

        If id.Length = 0 Then
            Return Fail("メールアドレスかアカウントIDを入力してください。", FieldLoginId)
        End If

        ' 【長すぎるものは断る】形の話なので、アカウントの有無は漏れない
        If id.Length > AccountStore.EmailMaxLength Then
            Return Fail("メールアドレスかアカウントIDが長すぎます。", FieldLoginId)
        End If

        Dim ip As String = ClientIp(context)

        If Not RequestLimiter.TryCount(ip) Then
            Return Fail("依頼が続いたため、しばらく受け付けません。15分ほど待ってから、もう一度お試しください。")
        End If

        ' 【リンクの頭は、要求の中で決める】裏の作業には要求が無い（AccountMail.PublicBaseUrl のホスト名の話）
        Dim baseUrl As String = AccountMail.PublicBaseUrl(context.Request)

        Try
            HostingEnvironment.QueueBackgroundWorkItem(Sub(ct) SendResetInBackground(id, baseUrl))
        Catch ex As Exception
            ErrorReport.Report(ex, "AccountReset.RequestReset（裏の作業を並べる）")
            Return Fail(TroubleMessage)
        End Try

        Return New ResetResult With {.Ok = True, .Message = SentMessage}

    End Function


    ' 裏の作業：アカウントを引き、合言葉を出し、メールを並べる
    '
    ' 【ここで起きたことは、画面には何も返らない】うまくいかなかったら ErrorReport で知らせる。
    ' **打たれた文字列（メールアドレス・ユーザー名）は、知らせに入れない**
    Private Sub SendResetInBackground(loginId As String, baseUrl As String)

        Try

            Dim account As AccountStore.AccountRecord = AccountStore.FindForLogin(loginId)

            ' 無い・止めている → 何もしない（画面の答えは、もう同じ文言で返してある）
            If account Is Nothing OrElse Not account.IsActive Then
                Return
            End If

            Dim token As String = NewToken()

            ' 【同じアカウントへは5分に1通まで】確かめと書き込みは1つの文（AccountStore.SaveResetToken）
            If Not AccountStore.SaveResetToken(account.AccountId, HashToken(token), TokenLifetime, MinResendInterval) Then
                Return
            End If

            AccountMail.SendResetLink(account, baseUrl, token, TokenLifetime)

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountReset.SendResetInBackground")
        End Try

    End Sub


    ' ============================================================
    ' ② リンクを開いたとき（/account/reset）
    ' ============================================================

    ' 合言葉の形か（長さと文字だけを見る。DB は引かない）
    Public Function LooksLikeToken(token As String) As Boolean

        If token Is Nothing OrElse token.Length <> TokenLength Then
            Return False
        End If

        For Each ch As Char In token
            If Not (Char.IsLetterOrDigit(ch) AndAlso AscW(ch) < 128) AndAlso ch <> "-"c AndAlso ch <> "_"c Then
                Return False
            End If
        Next

        Return True

    End Function


    ' 合言葉が今も使えるか（画面の出し分け。消さない）
    '
    ' 戻り値 … 使えるなら Nothing、使えないなら画面に出す文言
    Public Function CheckToken(context As HttpContext, token As String) As String

        Dim ip As String = ClientIp(context)

        If TokenMissLimiter.IsOver(ip) Then
            Return "リンクの確かめが続いたため、しばらく受け付けません。10分ほど待ってから、もう一度お試しください。"
        End If

        If Not LooksLikeToken(token) Then
            TokenMissLimiter.TryCount(ip)
            Return InvalidLinkMessage
        End If

        Try

            If AccountStore.FindResetAccountId(HashToken(token)) = 0 Then
                TokenMissLimiter.TryCount(ip)
                Return InvalidLinkMessage
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountReset.CheckToken")
            Return TroubleMessage
        End Try

        Return Nothing

    End Function


    ' 新しいパスワードに変える
    '
    ' 【順番】新しいパスワードの形を見る（DB を引かない）→ 合言葉を使う（取り出して消す＝1回だけ）→ パスワードを書き換える
    Public Function CompleteReset(context As HttpContext, token As String,
                                  newPassword As String, confirmPassword As String) As ResetResult

        Dim ip As String = ClientIp(context)

        If TokenMissLimiter.IsOver(ip) Then
            Return Fail("リンクの確かめが続いたため、しばらく受け付けません。10分ほど待ってから、もう一度お試しください。")
        End If

        Dim secret As String = AccountPassword.Normalize(newPassword)
        Dim field As String = FieldNewPassword
        Dim reason As String = AccountPassword.Validate(secret)

        If reason Is Nothing AndAlso Not String.Equals(secret, AccountPassword.Normalize(confirmPassword), StringComparison.Ordinal) Then
            field = FieldConfirm
            reason = "新しいパスワードと、確認のパスワードが違います。"
        End If

        If reason IsNot Nothing Then
            Return Fail(reason, field)
        End If

        If Not LooksLikeToken(token) Then
            TokenMissLimiter.TryCount(ip)
            Return Dead(InvalidLinkMessage)
        End If

        Dim account As AccountStore.AccountRecord

        Try

            Dim accountId As Integer = AccountStore.ConsumeResetToken(HashToken(token))

            If accountId = 0 Then
                TokenMissLimiter.TryCount(ip)
                Return Dead(InvalidLinkMessage)
            End If

            account = AccountStore.FindById(accountId)

            If account Is Nothing OrElse Not account.IsActive Then
                Return Dead(InvalidLinkMessage)
            End If

            ' 【ハッシュの計算に0.4秒ほどかかる】世代を進める＝ほかの端末のログインは、その場で全部切れる
            If AccountStore.UpdatePassword(account.AccountId, account.TokenGeneration,
                                           AccountPassword.Hash(secret)) < 0 Then
                ' 合言葉はもう使ってしまったので、送り直してもらう
                Return Dead("ほかの画面で先にパスワードが変わりました。もう一度「パスワードを忘れたとき」から、リンクを送ってください。")
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountReset.CompleteReset")
            Return Fail(TroubleMessage)
        End Try

        ' ログインの間違いの数を0に戻す（本人がパスワードを決め直したので）
        AccountPassword.RecordSuccess(account.UserName)
        AccountPassword.RecordSuccess(account.Email)

        AccountMail.NotifyPasswordChanged(account, "再設定のリンクで")

        Return New ResetResult With {.Ok = True}

    End Function


    ' ============================================================
    ' ③ 非常口（/account/recover）：サイト管理者のパスワードを作り直す
    ' ============================================================
    '
    ' 【合言葉のファイルを持っている人だけが来る画面】なので、ここでは区別を隠さない
    ' （「作り直せるのは、サイト管理者のアカウントだけです」）
    Public Function Recover(loginId As String, newPassword As String, confirmPassword As String) As ResetResult

        Dim id As String = AccountStore.NormalizeLoginId(loginId)
        Dim secret As String = AccountPassword.Normalize(newPassword)

        If id.Length = 0 OrElse id.Length > AccountStore.EmailMaxLength Then
            Return Fail("アカウントID（またはメールアドレス）を入力してください。", FieldLoginId)
        End If

        Dim field As String = FieldNewPassword
        Dim reason As String = AccountPassword.Validate(secret)

        If reason Is Nothing AndAlso Not String.Equals(secret, AccountPassword.Normalize(confirmPassword), StringComparison.Ordinal) Then
            field = FieldConfirm
            reason = "新しいパスワードと、確認のパスワードが違います。"
        End If

        If reason IsNot Nothing Then
            Return Fail(reason, field)
        End If

        Dim account As AccountStore.AccountRecord

        Try

            account = AccountStore.FindForLogin(id)

            If account Is Nothing OrElse Not account.IsActive OrElse
               Not String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal) Then
                Return Fail("作り直せるのは、有効なサイト管理者のアカウントだけです。")
            End If

            If AccountStore.UpdatePassword(account.AccountId, account.TokenGeneration,
                                           AccountPassword.Hash(secret)) < 0 Then
                Return Fail("ほかの画面で先にパスワードが変わりました。もう一度お試しください。")
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountReset.Recover")
            Return Fail(TroubleMessage)
        End Try

        AccountPassword.RecordSuccess(account.UserName)
        AccountPassword.RecordSuccess(account.Email)

        ' アカウントのアドレスと、info@ の両方へ知らせる
        ' （メールが送れない日に使う画面なので、送れるようになったときに気づけるように info@ にも残す）
        AccountMail.NotifyPasswordChanged(account, "非常口の画面で")

        SiteMail.Enqueue("[vb-chat お知らせ] 非常口でパスワードを作り直しました",
                         "非常口の画面（/account/recover）で、サイト管理者のパスワードを作り直しました。" & vbCrLf &
                         "アカウント：" & account.AccountId & "（" & account.DisplayName & "）" & vbCrLf &
                         "時刻：" & DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture) & vbCrLf & vbCrLf &
                         "App_Data\account-recover.txt を FTP で消したかを確かめてください。")

        Return New ResetResult With {.Ok = True}

    End Function


    ' ============================================================
    ' 合言葉の Cookie（リンクを開いたら、合言葉を URL から Cookie へ移す）
    ' ============================================================

    Public Sub WriteTokenCookie(response As HttpResponse, token As String)

        Dim cookie As New HttpCookie(TokenCookieName, token) With {
            .HttpOnly = True,
            .Secure = True,
            .Path = ResetPath,
            .Expires = DateTime.Now.Add(TokenLifetime)
        }
        cookie.SameSite = SameSiteMode.Lax

        response.Cookies.Add(cookie)

    End Sub

    Public Function ReadTokenCookie(request As HttpRequest) As String

        Dim cookie As HttpCookie = request.Cookies(TokenCookieName)
        Return If(cookie Is Nothing, Nothing, cookie.Value)

    End Function

    ' 【path をそろえて消す】そろえないと、消したつもりで別の Cookie を1つ作って終わる
    Public Sub ClearTokenCookie(response As HttpResponse)

        Dim cookie As New HttpCookie(TokenCookieName, "") With {
            .HttpOnly = True,
            .Secure = True,
            .Path = ResetPath,
            .Expires = DateTime.Now.AddDays(-1)
        }
        cookie.SameSite = SameSiteMode.Lax

        response.Cookies.Add(cookie)

    End Sub


    ' ============================================================
    ' 下回り
    ' ============================================================

    ' 32バイトの乱数を Base64URL（=・+・/ を使わない形）にする
    Private Function NewToken() As String

        Dim bytes(TokenBytes - 1) As Byte

        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using

        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)

    End Function

    ' DB に置くのは SHA-256 だけ
    Private Function HashToken(token As String) As Byte()

        Using sha As SHA256 = SHA256.Create()
            Return sha.ComputeHash(Encoding.UTF8.GetBytes(token))
        End Using

    End Function

    Private Function ClientIp(context As HttpContext) As String

        Try
            Return If(context.Request.UserHostAddress, "")
        Catch ex As Exception
            Return ""
        End Try

    End Function

    Private Function Fail(message As String, Optional field As String = Nothing) As ResetResult
        Return New ResetResult With {.Ok = False, .Message = message, .Field = field}
    End Function

    Private Function Dead(message As String) As ResetResult
        Return New ResetResult With {.Ok = False, .Message = message, .LinkDead = True}
    End Function


    ' ============================================================
    ' 数える道具は Core/RateCounter.vb へ出した（問い合わせページ）
    ' ============================================================
    '
    ' それまでこのモジュールの中に Private のクラスとして持っていたが、問い合わせページでも
    ' 同じものが要るようになったため、Core へ移した（**2つ以上の機能から使うものは Core/**）。
    ' **中身は1行も変えていない。** VB はフォルダが名前空間にならないので、上の
    ' RequestLimiter・TokenMissLimiter の書き方も変わっていない。
    ' 【DB の表を作らなかった理由】外から叩くだけで行を増やせる口になる（AccountPassword の歯止めと同じ）


End Module
