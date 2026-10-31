' 新規登録の画面（一般ユーザーの登録）
'
' 【AccountPageBase は継承しない】
' あちらは「ログインしていなければログインの画面へ返す」ので、継承すると
' **登録できるのはログインしている人だけ**という、意味の通らない形になる。
' Login.aspx・Forgot.aspx と同じ扱いである。
'
' 【ポストバックで動かしている】管理画面まわりの方針どおり（Login.aspx の先頭のコメント）。
' 判断そのものは Accounts/AccountRegistration.vb が持っていて、ここは呼ぶだけである
Public Class AccountRegisterPage
    Inherits System.Web.UI.Page


    ' 「ログインはこちら」の行き先（ログイン・新規登録・忘れたときの画面の間で、戻り先を持ち回す）
    ' 【受け取った値をそのまま付けない】AccountReturnUrl.WithReturn が Normalize を通す（使えない値なら戻り先を付けない）
    Protected ReadOnly Property LoginLinkUrl As String
        Get
            Return AccountReturnUrl.WithReturn("/account/login", Request.QueryString(AccountReturnUrl.QueryName))
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【キャッシュさせない】戻るボタンで、打ったメールアドレスやパスワードが残らないように
        ' （ほかのアカウントの画面とそろえてある）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        ' 【検索エンジンには載せない】サイト全体に付いているので（Global.asax.vb）、ここでは何もしない

        ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るので、消さないと直したあとも赤いまま
        If IsPostBack Then
            FormErrors.Clear(UserNameBox, DisplayNameBox, EmailBox, PasswordBox, ConfirmBox, AgreeBox, RegisterButton)
        End If

        ' 【同意のチェックも、ほかの欄と同じく画面の側で確かめる】（本番の実機テストで見つかった）
        ' ほかの欄には required があり、送る前に欄の上へ理由が出る（Scripts/site.js の「8」）。このチェックにだけ無く、
        ' ほかの欄で止まった回には、サーバーまで届かないので理由が出なかった（わざとではなかった）。
        ' 【markup ではなく、ここで付ける】asp:CheckBox に書いた属性は、まわりの span に付いてしまう。
        ' InputAttributes は ViewState に残らないので、ポストバックでも毎回付ける
        AgreeBox.InputAttributes("required") = "required"
        AgreeBox.InputAttributes("data-error-required") = AccountRegistration.AgreeRequiredMessage

        If Not IsPostBack Then

            ' === もうログインしている人は、アカウントのページへ ===
            '
            ' 【この Redirect を Try/Catch の中へ移さないこと】
            ' 既定（endResponse = True）は ThreadAbortException を投げるため、
            ' Catch が走って嘘のエラー記録が出る
            If AccountSession.Current(Context) IsNot Nothing Then
                Response.Redirect(AccountPageBase.HomeUrl)
            End If

            ' 【受け付けたあとの画面】（PRG）欄ごと出さず、答えだけを出す
            If Request.QueryString("done") = "sent" Then
                DoneText.Text = HttpUtility.HtmlEncode(AccountRegistration.SentMessage)
                FormArea.Visible = False
                Return
            End If

            ' 【パスワードの決まりは、コードから出す】（画面に数字を書かない）
            PasswordHintText.Text = HttpUtility.HtmlEncode(
                AccountPassword.MinLength & "文字以上" & AccountPassword.MaxLength & "文字以内。")

            ' 【利用規約のリンク】その名前（terms）の記事が公開されていなければ、リンクごと出さない
            ' （押しても「見つかりません」になるリンクを置かない、という決まり）。
            ' **同意のチェックの文（「利用規約に同意します」）は、markup 側に素の文で在る**ので、
            ' リンクが出なくても意味は通る
            Dim link As String = SiteArticles.LinkHtml("terms", "利用規約を読む")

            TermsLinkText.Text = If(link.Length > 0, "（" & link & "）", "")

        End If

    End Sub


    Protected Sub RegisterButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles RegisterButton.Click

        ' 【二重送信を防ぐ】（Core/FormOnce.vb）受け付けたあと、戻るボタンで戻って
        ' もう一度押したときは、ここで止まる。
        ' 【検査より先に見ている】この画面の検査は AccountRegistration.Begin の中にあり、外から「検査は通ったか」だけを
        ' 聞く口が無い。先に見ても害は無い——入力の間違いで断った画面には、新しい目印が入っている
        ' （目印を使ってしまうのは、この1回の送信だけである）。
        ' 【アドレスが登録済みかは、ここからも漏れない】答えは、アドレスを引く前に決まる
        If Not FormOnce.TryUse(Request) Then
            ErrorText.Text = HttpUtility.HtmlEncode(FormOnce.AlreadySentMessage)
            DoneText.Text = ""
            FormErrors.MarkHere(RegisterButton)
            Return
        End If

        Dim result As AccountRegistration.RegisterResult =
            AccountRegistration.Begin(Context,
                                      UserNameBox.Text, DisplayNameBox.Text, EmailBox.Text,
                                      PasswordBox.Text, ConfirmBox.Text,
                                      AgreeBox.Checked, TrapBox.Text)

        If result.Ok Then

            ' === 受け付けた → 開き直す（PRG。開発者が決めた「毎回の基本」）===
            '
            ' 「戻る」や F5 で、もう一度申し込まれないように。開き直した画面は欄ごと出さない（Page_Load）。
            ' 【答えはいつも同じ】登録済みのアドレスでも同じ文言なので、開き直しても有無は漏れない。
            ' Try/Catch の外。Redirect は ThreadAbortException を投げる
            Response.Redirect("/account/register?done=sent")

        End If

        ' === 断られた ===
        '
        ' 【打たれたパスワードは、画面に残さない】
        ' TextMode="Password" の asp:TextBox は、そもそも値を描き直さない（ASP.NET の仕様）。
        ' ほかの欄と規約のチェックは残す（打ち直させないため）
        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)
        DoneText.Text = ""

        ' 【どの欄が悪いかの印】理由が、その欄の上へ移る（Scripts/site.js の「9」・Core/FormErrors.vb）。
        ' どの欄とも言えない理由（混み合っています・回数の歯止め）は、印を付けない＝欄の上の、いまの場所に出る
        Select Case result.Field
            Case AccountRegistration.FieldUserName : FormErrors.MarkField(UserNameBox)
            Case AccountRegistration.FieldDisplayName : FormErrors.MarkField(DisplayNameBox)
            Case AccountRegistration.FieldEmail : FormErrors.MarkField(EmailBox)
            Case AccountRegistration.FieldPassword : FormErrors.MarkField(PasswordBox)
            Case AccountRegistration.FieldConfirm : FormErrors.MarkField(ConfirmBox)
            Case AccountRegistration.FieldAgree : FormErrors.MarkField(AgreeBox)
        End Select

    End Sub

End Class
