' アカウント設定の画面
'
' 【判断は Accounts/AccountSettings.vb】ここは入力を渡して、結果を出すだけ。
' 【AccountPageBase を継承している】ログインしていなければ、ログインの画面へ返される（戻り先つき）。
'
' 【うまくいったら、同じ画面へ移り直す】（?done=name|email|password）
' F5 で同じ変更が2回送られないように。文言はこちらが持つ（クエリ文字列で文言そのものを運ばない＝決まり）
Public Class AccountSettingsPage
    Inherits AccountPageBase

    Private Const SelfUrl As String = "/account/settings"


    ' 画面に出すユーザー名（markup の <%: %> がエスケープする）
    Protected ReadOnly Property UserNameText As String
        Get
            Return CurrentAccount.UserName
        End Get
    End Property

    ' 画面に出す今のメールアドレス（同上）
    Protected ReadOnly Property EmailText As String
        Get
            Return CurrentAccount.Email
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        DoneText.Text = ""

        ' 【退会の区切りは、サイト管理者には出さない】
        ' 出さないだけでは守りにならないので、AccountRetire.Retire も必ず断る
        ' （IsAdministrator・RequireAdministrator は AccountPageBase）。
        ' 【IsPostBack の外に置くこと】中に入れると、ポストバックのたびに区切りが出てしまう
        RetirePanel.Visible = Not IsAdministrator

        ' 【前の回に付けた aria-invalid を消す】TextBox.Attributes は ViewState に残るので、
        ' 消さないと、別の区切りで押したときまで前の欄が赤いまま残る（プロフィールの ClearInvalidMarks と同じ罠）
        FormErrors.Clear(DisplayNameBox, NewEmailBox, EmailPasswordBox,
                         CurrentPasswordBox, NewPasswordBox, ConfirmPasswordBox, RetirePasswordBox)

        If Not IsPostBack Then

            DisplayNameBox.Text = CurrentAccount.DisplayName

            Select Case Request.QueryString("done")
                Case "name"
                    DoneText.Text = HttpUtility.HtmlEncode("名前（表示名）を保存しました。")
                Case "email"
                    DoneText.Text = HttpUtility.HtmlEncode("メールアドレスを変えました。")
                Case "password"
                    DoneText.Text = HttpUtility.HtmlEncode(
                        "パスワードを変えました。ほかの端末のログインは切れています（この端末はログインしたままです）。")
            End Select

        End If

    End Sub


    Protected Sub DisplayNameButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles DisplayNameButton.Click

        Dim result As AccountSettings.ChangeResult =
            AccountSettings.ChangeDisplayName(CurrentAccount, DisplayNameBox.Text)

        If result.Ok Then
            ' 【Try/Catch の中に置かない】Redirect は ThreadAbortException を投げる
            Response.Redirect(SelfUrl & "?done=name")
        End If

        DisplayNameError.Text = HttpUtility.HtmlEncode(result.Message)

        ' 【この区切りの欄は1つだけ】理由は、その欄の上に出す（Scripts/site.js の「9」）
        FormErrors.MarkField(DisplayNameBox)

    End Sub


    Protected Sub EmailButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles EmailButton.Click

        Dim result As AccountSettings.ChangeResult =
            AccountSettings.ChangeEmail(Context, CurrentAccount, EmailPasswordBox.Text, NewEmailBox.Text)

        If result.Ok Then
            Response.Redirect(SelfUrl & "?done=email")
        End If

        EmailError.Text = HttpUtility.HtmlEncode(result.Message)
        MarkInvalid(result.Field)

    End Sub


    Protected Sub PasswordButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles PasswordButton.Click

        Dim result As AccountSettings.ChangeResult =
            AccountSettings.ChangePassword(Context, CurrentAccount, CurrentPasswordBox.Text,
                                           NewPasswordBox.Text, ConfirmPasswordBox.Text)

        If result.Ok Then
            ' この応答で、新しい世代のログインの札（Set-Cookie）も一緒に届く
            ' （Response.Redirect しても Set-Cookie は捨てられない）
            Response.Redirect(SelfUrl & "?done=password")
        End If

        PasswordError.Text = HttpUtility.HtmlEncode(result.Message)
        MarkInvalid(result.Field)

    End Sub

    ' 直してほしい欄に aria-invalid を付ける（「断られたらその場所へ」）
    '
    ' 【付けるだけ】スクロールとカーソルは Scripts/site.js の「9」がやる
    ' （理由の出ている区切りへスクロールし、aria-invalid の付いた欄にカーソルを置く）。
    ' 【次のポストバックで消す】TextBox.Attributes は ViewState に残るので、Page_Load の先頭で消している
    Private Sub MarkInvalid(field As String)

        Dim box As TextBox = Nothing

        Select Case field
            Case AccountSettings.FieldNewEmail
                box = NewEmailBox
            Case AccountSettings.FieldCurrentPassword
                ' メールアドレスの区切りとパスワードの区切りの、どちらで押されたかで決める
                box = If(EmailError.Text.Length > 0, EmailPasswordBox, CurrentPasswordBox)
            Case AccountSettings.FieldNewPassword
                box = NewPasswordBox
        End Select

        FormErrors.MarkField(box)

    End Sub


    ' === 退会 ===
    '
    ' 【判断は Accounts/AccountRetire.vb】ここは押されたことを伝えて、結果を出すだけ。
    ' 【うまくいったら、トップページへ】アカウントはもう無いので、管理の画面へは戻せない
    ' （ログアウトも AccountRetire の中で済んでいる）
    Protected Sub RetireButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles RetireButton.Click

        ' 【念のため、ここでも断る】画面から区切りを消してあるが、
        ' ポストバックは開発者ツールから組み立てられる（画面の出し分けは防御ではない）
        If IsAdministrator Then
            RetireError.Text = HttpUtility.HtmlEncode("サイト管理者のアカウントは、退会できません。")
            Return
        End If

        Dim result As AccountRetire.RetireResult = AccountRetire.Retire(Context, RetirePasswordBox.Text)

        If result.Ok Then
            ' 【行き先はログインの画面】アカウントはもう無いので、管理の画面には戻れない。
            ' トップページにしないのは、**「退会しました」と伝える場所が無い**ため
            ' （トップは、チャットの Cookie を持っている人をラウンジへ返す）。
            ' 文言は向こうが持つ（AccountLoginPage.RetiredQuery）
            ' 【Try/Catch の中に置かない】Redirect は ThreadAbortException を投げる
            Response.Redirect(AccountPageBase.LoginUrl & "?" & AccountLoginPage.RetiredQuery)
        End If

        RetireError.Text = HttpUtility.HtmlEncode(result.Message)

        ' 【パスワードで断ったときは、その欄の上に】それ以外（混み合っています、など）は、区切りの上の今の場所に出る
        If result.PasswordRejected Then
            FormErrors.MarkField(RetirePasswordBox)
        End If

    End Sub

End Class
