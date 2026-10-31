' サイト管理者のパスワードを作り直す画面＝非常口
'
' 【ふだんは404】App_Data\account-recover.txt（8文字以上）が置いてあって、?key= がその中身と合う間だけ開く。
' 判定は GateFile.IsOpen（MailTest・Setup と同じ1か所）。
'
' 【閉じているときは1バイトも書かない】Accounts/Setup.aspx.vb とまったく同じ作り
' （Site.Master を使う画面なので、CompleteRequest では止まらない。Render を差し替えている）
'
' 【判断は Accounts/AccountReset.vb の Recover】
Public Class AccountRecoverPage
    Inherits System.Web.UI.Page

    ' 合言葉のファイル（本番にしか無い使い捨て。.gitignore と発行プロファイルで除外、.vbproj に登録しない）
    Private Const GateRelativePath As String = "~/App_Data/account-recover.txt"

    ' この画面は開いているか（OnInit で1回だけ決める）
    Private isOpen As Boolean


    Protected Overrides Sub OnInit(e As EventArgs)

        isOpen = GateFile.IsOpen(GateRelativePath, Request.QueryString("key"))

        If Not isOpen Then

            ' 閉じているときの404（Setup.aspx と同じ形。本番の IIS に差し替えさせない）
            Response.Clear()
            Response.StatusCode = 404
            Response.ContentType = "text/plain"
            Response.TrySkipIisCustomErrors = True

        End If

        MyBase.OnInit(e)

    End Sub


    Protected Overrides Sub Render(writer As System.Web.UI.HtmlTextWriter)

        If Not isOpen Then
            Return
        End If

        MyBase.Render(writer)

    End Sub


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)
        Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")
        Response.AppendHeader("Referrer-Policy", "no-referrer")

    End Sub


    Protected Sub RecoverButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles RecoverButton.Click

        ' 【閉じているときは、何もしない】画面を出さないことと、処理をしないことは別（Setup.aspx と同じ）
        If Not isOpen Then
            Return
        End If

        ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るため
        FormErrors.Clear(LoginIdBox, NewPasswordBox, ConfirmPasswordBox)

        Dim result As AccountReset.ResetResult =
            AccountReset.Recover(LoginIdBox.Text, NewPasswordBox.Text, ConfirmPasswordBox.Text)

        If result.Ok Then

            DoneText.Text = HttpUtility.HtmlEncode(
                "パスワードを作り直しました。ログインの画面から、新しいパスワードで入ってください。" &
                "まだ App_Data\account-recover.txt を消していなければ、今すぐ FTP で消してください。")
            FormArea.Visible = False
            LeadArea.Visible = False
            Return

        End If

        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)

        ' 【どの欄が悪いかの印】理由が、その欄の上へ移る（Scripts/site.js の「9」・Core/FormErrors.vb）
        Select Case result.Field
            Case AccountReset.FieldLoginId : FormErrors.MarkField(LoginIdBox)
            Case AccountReset.FieldNewPassword : FormErrors.MarkField(NewPasswordBox)
            Case AccountReset.FieldConfirm : FormErrors.MarkField(ConfirmPasswordBox)
        End Select

    End Sub


End Class
