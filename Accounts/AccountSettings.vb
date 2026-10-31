Imports System.Threading

' アカウント設定（表示名・メールアドレス・パスワードを変える）の判断
'
' 【なぜ .aspx ではなく、ここに置くのか】AccountSession と同じ理由。
' C#／ASP.NET Core MVC へ移るときに .aspx は捨てるが、「何を、どの順で確かめるか」は持っていく。
' 画面（Accounts/Settings.aspx）は、ここを呼んで結果を出すだけ。
' HttpContext.Current を使わず引数で受け取るのも、同じ理由。
'
' 【ユーザー名は変えられない】記事の URL（/articles/{ユーザー名}/…）に使うので、変えると記事の URL が全部変わる。
' 変える口は、ここにも AccountStore にも作っていない。
'
' 【名前（表示名）は、設定の画面とプロフィールの画面の両方から変えられる】
' 決定：名前はどちらの画面で変えられてもよい。ただし、どちらも「名前（表示名）」の呼び方にそろえる。
' **書き換える道は、下の ChangeDisplayName の1本だけ**（検査が食い違わないようにするため）。
'
' 【パスワードは、どこにも書かない】ログにも、エラー報告の補足にも、お知らせのメールにも入れない。
Module AccountSettings


    ' 変えた結果
    Public Class ChangeResult

        ' 受け付けたか
        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す（出口でエスケープする）
        Public Property Message As String

        ' 直してほしい欄（画面が aria-invalid を付けて、カーソルをそこへ置く＝site.js の「9」）。
        ' 分からないときは Nothing（画面の側が、その区切りの空の欄を選ぶ）
        Public Property Field As String

    End Class

    ' 直してほしい欄の名前（ChangeResult.Field）
    Public Const FieldNewEmail As String = "new-email"
    Public Const FieldCurrentPassword As String = "current-password"
    Public Const FieldNewPassword As String = "new-password"


    ' DB が読めなかったときの文言（理由は利用者に見せない。エラー報告メールで届く）
    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"

    ' 別の端末で先に変えられていたとき（パスワードの世代が合わなかった）
    Private Const ChangedElsewhereMessage As String =
        "ほかの画面で先に変更されました。この画面を開き直してから、もう一度お試しください。"


    ' === 名前（表示名）を変える ===
    '
    ' 【今のパスワードは求めない】乗っ取られても失うものが無い（見える名前が変わるだけで、本人が直せる）
    '
    ' 【変えたあと、捨てる控えは無い】表示名はどの画面も開くたびに DB から読んでいる
    ' 
    '
    ' 【ここが「名前（表示名）を書き換える唯一の道」になった】
    ' それまでは、設定の画面がここを通り、プロフィールの画面が `AccountStore.UpdateDisplayName` を
    ' 直接呼んでいた。**2つの画面から変えられるので、入力の検査が食い違わないよう1本に寄せてある。**
    ' **プロフィールの画面から `AccountStore.UpdateDisplayName` を直接呼ばないこと。**
    '
    ' 【今と同じ名前でも、断らない】（同じ日に変えた）
    ' プロフィールの画面は、名前もほかの項目も**まとめて1回で保存する**。
    ' 名前を触っていないときに「今の名前と同じです。」で断ると、**ほかの項目まで保存できなくなる**。
    ' そこで、同じときは**DB を触らずに成功として返す**（書き込みが1回減るだけで、利用者から見た結果は同じ）
    Public Function ChangeDisplayName(account As AccountStore.AccountRecord, displayName As String) As ChangeResult

        If account Is Nothing Then
            Return Rejected(TroubleMessage)
        End If

        Dim name As String = If(displayName, "").Trim()

        If name.Length = 0 Then
            Return Rejected("名前（表示名）を入力してください。")
        End If

        If name.Length > AccountStore.DisplayNameMaxLength Then
            Return Rejected("名前（表示名）は" & AccountStore.DisplayNameMaxLength & "文字以内にしてください。")
        End If

        ' 今と同じなら、DB を触らずに成功（上のコメント）
        If String.Equals(name, account.DisplayName, StringComparison.Ordinal) Then
            Return Accepted()
        End If

        Try

            If Not AccountStore.UpdateDisplayName(account.AccountId, name) Then
                Return Rejected(TroubleMessage)
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountSettings.ChangeDisplayName")
            Return Rejected(TroubleMessage)
        End Try

        ' 【「新しいメンバー」の枠の控えを捨てる】名前がそこにも出るため
        NewMembers.Invalidate()

        Return Accepted()

    End Function


    ' === メールアドレスを変える ===
    '
    ' 【今のパスワードを求める】ログインの Cookie を盗まれたときに、
    ' パスワードの再設定先（将来作る）を乗っ取られないため
    Public Function ChangeEmail(context As HttpContext, account As AccountStore.AccountRecord,
                                currentPassword As String, newEmail As String) As ChangeResult

        If context Is Nothing OrElse account Is Nothing Then
            Return Rejected(TroubleMessage)
        End If

        Dim email As String = If(newEmail, "").Trim()

        ' 形の検査は、パスワードの計算より前（打ち間違いで、歯止めの数を増やさないため）
        Dim reason As String = AccountStore.ValidateEmail(email)

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldNewEmail)
        End If

        If String.Equals(email, account.Email, StringComparison.OrdinalIgnoreCase) Then
            Return Rejected("今のメールアドレスと同じです。", FieldNewEmail)
        End If

        reason = CheckCurrentPassword(context, account, currentPassword)

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldCurrentPassword)
        End If

        Dim result As AccountStore.EmailUpdateResult

        Try
            result = AccountStore.UpdateEmail(account.AccountId, email)
        Catch ex As Exception
            ErrorReport.Report(ex, "AccountSettings.ChangeEmail")
            Return Rejected(TroubleMessage)
        End Try

        Select Case result

            Case AccountStore.EmailUpdateResult.Updated

                ' 【前のアドレスへ知らせる】（info@ から直した）乗っ取った人が変えても、本人の前のアドレスに届く
                AccountMail.NotifyEmailChanged(account, account.Email, email)

                Return Accepted()

            Case AccountStore.EmailUpdateResult.Taken
                Return Rejected("そのメールアドレスは、ほかのアカウントで使われています。", FieldNewEmail)

            Case Else
                Return Rejected(TroubleMessage)

        End Select

    End Function


    ' === パスワードを変える ===
    '
    ' 【鍵の世代を進める】ほかの端末のログインは、その場で全部切れる（仕組み）。
    ' 【この端末には、新しい世代でログインの札を出し直す】ここまで切れると不便なため
    Public Function ChangePassword(context As HttpContext, account As AccountStore.AccountRecord,
                                   currentPassword As String, newPassword As String,
                                   confirmPassword As String) As ChangeResult

        If context Is Nothing OrElse account Is Nothing Then
            Return Rejected(TroubleMessage)
        End If

        Dim secret As String = AccountPassword.Normalize(newPassword)
        Dim confirm As String = AccountPassword.Normalize(confirmPassword)

        ' 新しいパスワードの検査は、今のパスワードの計算より前（打ち間違いで、歯止めの数を増やさないため）
        Dim reason As String = AccountPassword.Validate(secret)

        If reason Is Nothing AndAlso Not String.Equals(secret, confirm, StringComparison.Ordinal) Then
            reason = "新しいパスワードと、確認のパスワードが違います。"
        End If

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldNewPassword)
        End If

        reason = CheckCurrentPassword(context, account, currentPassword)

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldCurrentPassword)
        End If

        ' 【今と同じパスワードは断る】今のパスワードが合ったあとでしか分からない（ハッシュなので）
        If String.Equals(AccountPassword.Normalize(currentPassword), secret, StringComparison.Ordinal) Then
            Return Rejected("今のパスワードと同じです。別のパスワードにしてください。", FieldNewPassword)
        End If

        Dim lifetime As TimeSpan = SiteSettings.AccountLoginCookieLifetime
        Dim newGeneration As Integer

        Try

            ' 【ハッシュの計算に0.4秒ほどかかる】（Accounts/AccountPassword.vb の反復回数）
            newGeneration = AccountStore.UpdatePassword(account.AccountId, account.TokenGeneration,
                                                        AccountPassword.Hash(secret))

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountSettings.ChangePassword")
            Return Rejected(TroubleMessage)
        End Try

        If newGeneration < 0 Then
            Return Rejected(ChangedElsewhereMessage)
        End If

        ' === この端末の札を、新しい世代で出し直す ===
        '
        ' 【出し直せなかったら】（鍵が手に入らない。ふつう起きない。AccountAuth が知らせている）
        ' パスワードはもう変わっているので、断らない。この端末もログアウトになるだけ（安全な側）
        Dim token As String = AccountAuth.Issue(account.AccountId, newGeneration, lifetime)

        If Not String.IsNullOrEmpty(token) Then
            AccountAuth.WriteCookie(context.Response, token, lifetime)
        End If

        ' この要求の中で、古い世代の控えを使わせない（次の要求で DB から読み直される）
        AccountSession.Forget(context)

        ' 【そのアカウントのアドレスへ知らせる】（info@ から直した）
        AccountMail.NotifyPasswordChanged(account, "設定の画面で")

        Return Accepted()

    End Function


    ' === 今のパスワードを確かめる（断る理由を返す。合っていれば Nothing） ===
    '
    ' 【ログインの画面と同じ歯止めを通す】パスワードを変える画面を、パスワードを当てる入口にしないため。
    '   ・IP … 受け付けない状態になる（既定5回で60秒）
    '   ・アカウント … 遅らせるだけ（ロックにすると本人が締め出される。A-1 と同じ理由）
    ' 【数えるキーは、そのアカウントのユーザー名】ログインの画面でユーザー名を打って間違えた数と、同じ数に足される
    ' （入口を2つにしても、試せる回数が2倍にならない）
    Friend Function CheckCurrentPassword(context As HttpContext, account As AccountStore.AccountRecord,
                                          currentPassword As String) As String

        Dim secret As String = AccountPassword.Normalize(currentPassword)

        If secret.Length = 0 Then
            Return "今のパスワードを入力してください。"
        End If

        ' 長すぎる入力は、計算する前に断る（ログインの画面と同じ）
        If secret.Length > AccountPassword.MaxLength Then
            Return "今のパスワードが違います。"
        End If

        Dim ip As String = Nothing

        Try
            ip = context.Request.UserHostAddress
        Catch ex As Exception
            ip = Nothing
        End Try

        Dim locked As Integer = AccountPassword.GetLockoutSeconds(ip)

        If locked > 0 Then
            Return "パスワードの間違いが続いたため、あと約" & locked &
                   "秒は受け付けません。少し待ってから、もう一度お試しください。"
        End If

        Dim key As String = account.UserName
        Dim delay As TimeSpan = AccountPassword.GetDelay(key)

        If delay > TimeSpan.Zero Then
            Thread.Sleep(delay)
        End If

        ' 設定は、鍵の外でまとめて読んでおく（RecordFailure の中は鍵の中）
        Dim maxFailures As Integer = SiteSettings.AccountLoginMaxFailures
        Dim lockout As TimeSpan = SiteSettings.AccountLoginLockout

        If Not AccountPassword.Verify(secret, account.PasswordHash, account.AccountId) Then

            AccountPassword.RecordFailure(key, ip, maxFailures, lockout)

            Return "今のパスワードが違います。"

        End If

        AccountPassword.RecordSuccess(key)

        Return Nothing

    End Function


    Private Function Accepted() As ChangeResult
        Return New ChangeResult With {.Ok = True}
    End Function

    Private Function Rejected(message As String, Optional field As String = Nothing) As ChangeResult
        Return New ChangeResult With {.Ok = False, .Message = message, .Field = field}
    End Function


End Module
