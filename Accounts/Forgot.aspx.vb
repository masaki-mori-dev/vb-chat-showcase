' パスワードを忘れたときの画面
'
' 【判断は Accounts/AccountReset.vb】ここは入力を渡して、答えを出すだけ。
' ログインしていなくても開ける（ログインできない人のための画面なので）
Public Class AccountForgotPage
    Inherits System.Web.UI.Page


    ' 「ログインの画面へ戻る」の行き先（ログイン・新規登録・忘れたときの画面の間で、戻り先を持ち回す）
    ' 【受け取った値をそのまま付けない】AccountReturnUrl.WithReturn が Normalize を通す（使えない値なら戻り先を付けない）
    Protected ReadOnly Property LoginLinkUrl As String
        Get
            Return AccountReturnUrl.WithReturn("/account/login", Request.QueryString(AccountReturnUrl.QueryName))
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)
        Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")

        ' 【受け付けたあとの画面】（PRG）開き直した先で、答えだけを出す。
        ' 文言はこちらが持つ（クエリ文字列で文言そのものを運ばない＝決まり）
        If Not IsPostBack AndAlso Request.QueryString("done") = "sent" Then
            DoneText.Text = HttpUtility.HtmlEncode(AccountReset.SentMessage)
            FormArea.Visible = False

            ' 【上の説明も出さない】残るのは、結果の文と「ログインの画面へ戻る」だけ
            LeadArea.Visible = False
        End If

    End Sub


    Protected Sub SendButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles SendButton.Click

        ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るため
        FormErrors.Clear(LoginIdBox, SendButton)

        ' 【二重送信を防ぐ】（Core/FormOnce.vb）受け付けたあと、戻るボタンで戻って
        ' もう一度押したときは、ここで止まる（新規登録の画面と同じ形。検査は AccountReset.RequestReset の中にある）。
        ' 【前からの歯止めは、そのまま残してある】同じアカウントへは5分に1通まで（MinResendInterval。DB が決める）・
        ' 同じ IP からは15分に5回まで。あちらは**わざと繰り返す人**を止めるもので、こちらは**押し間違い**を止めるもの
        If Not FormOnce.TryUse(Request) Then
            ErrorText.Text = HttpUtility.HtmlEncode(FormOnce.AlreadySentMessage)
            FormErrors.MarkHere(SendButton)
            Return
        End If

        Dim result As AccountReset.ResetResult = AccountReset.RequestReset(Context, LoginIdBox.Text)

        If result.Ok Then

            ' 【受け付けたら開き直す（PRG）】（開発者が決めた「毎回の基本」）
            ' 「戻る」や F5 で、もう一度送られないように。開き直した画面は欄ごと出さない（上の Page_Load）。
            ' 【答えはいつも同じ】在っても無くても同じ文言なので、開き直してもアカウントの有無は漏れない
            ' Try/Catch の外。Redirect は ThreadAbortException を投げる
            Response.Redirect("/account/forgot?done=sent")

        End If

        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)

        ' 【どの欄が悪いかの印】理由が、その欄の上へ移る（Scripts/site.js の「9」・Core/FormErrors.vb）
        If result.Field = AccountReset.FieldLoginId Then
            FormErrors.MarkField(LoginIdBox)
        End If

    End Sub


End Class
