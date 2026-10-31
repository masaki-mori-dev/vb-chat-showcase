' 登録の確認の画面（確認のメールのリンクの行き先。一般ユーザーの登録）
'
' 【判断は Accounts/AccountRegistration.vb】ここは合言葉の出し入れと、結果を出すだけ。
' **Accounts/Reset.aspx.vb と、同じ形にそろえてある**（片方を直したら、もう片方も見ること）。
'
' 【ログインしていなくても開ける】メールから開かれる画面なので、そちらが普通である。
' ログインしている人が開いても同じように動く——本登録が済んだ時点で、
' AccountSession.SignInNewAccount が**新しいアカウントの札に差し替える**（前のログインは、そこで切れる）
Public Class AccountVerifyPage
    Inherits System.Web.UI.Page


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【検索エンジンに載せない・キャッシュさせない・よそへ URL を漏らさない】
        ' 302（下の移り直し）にも付くように、いちばん先に付ける
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)
        Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")
        Response.AppendHeader("Referrer-Policy", "no-referrer")

        If Not IsPostBack Then

            ' === メールのリンク（?t=合言葉）から来たとき ===
            '
            ' 【合言葉を Cookie へ移して、クエリの無い URL へ移り直す】
            ' アドレスバー・閲覧の履歴・Referer に合言葉を残さないため。
            ' 形が合言葉でないものは Cookie にしない（移り直した先で「このリンクは使えません」になる）。
            ' 【この Redirect は Try/Catch の外】ThreadAbortException を投げる
            Dim fromLink As String = Request.QueryString(AccountRegistration.TokenQueryName)

            If fromLink IsNot Nothing Then

                If AccountRegistration.LooksLikeToken(fromLink) Then
                    AccountRegistration.WriteTokenCookie(Response, fromLink)
                Else
                    AccountRegistration.ClearTokenCookie(Response)
                End If

                Response.Redirect(AccountRegistration.VerifyPath)

            End If

            ' === 画面を出す：合言葉が今も使えるか ===
            Dim problem As String = Nothing

            Dim pending As AccountStore.RegistrationRecord =
                AccountRegistration.CheckToken(Context, AccountRegistration.ReadTokenCookie(Request), problem)

            If pending Is Nothing Then
                ShowProblem(problem)
                Return
            End If

            ' 【出口でエスケープする】名前は利用者が決めた文字である
            UserNameText.Text = HttpUtility.HtmlEncode(pending.UserName)
            DisplayNameText.Text = HttpUtility.HtmlEncode(pending.DisplayName)
            EmailText.Text = HttpUtility.HtmlEncode(pending.Email)

        End If

    End Sub


    Protected Sub VerifyButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles VerifyButton.Click

        Dim result As AccountRegistration.RegisterResult =
            AccountRegistration.Complete(Context, AccountRegistration.ReadTokenCookie(Request))

        If result.Ok Then

            ' 合言葉はもう使った（DB から消えている）。Cookie も消す
            '
            ' 【そのままログインしている】（AccountRegistration.Complete の中で札を配っている）
            ' 【行き先はプロフィールの編集の画面】登録した直後の人が、いちばん先に埋めたいところ
            AccountRegistration.ClearTokenCookie(Response)
            Response.Redirect(AccountRegistration.AfterVerifyUrl)

        End If

        If result.LinkDead Then
            ' 使えなかった（期限切れ・もう使われた・ユーザー名が取られた、など）。
            ' ボタンを出したままだと、何度押しても通らない
            AccountRegistration.ClearTokenCookie(Response)
            ShowProblem(result.Message)
            Return
        End If

        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)

    End Sub


    ' 使えないリンク：理由を出し、ボタンを隠して「もう一度登録する」へ案内する
    Private Sub ShowProblem(message As String)

        ErrorText.Text = HttpUtility.HtmlEncode(message)
        FormArea.Visible = False
        RetryArea.Visible = True

    End Sub

End Class
