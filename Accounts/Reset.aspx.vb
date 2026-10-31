' 新しいパスワードを決める画面（再設定のリンクの行き先）
'
' 【判断は Accounts/AccountReset.vb】ここは合言葉の出し入れと、結果を出すだけ。
' ログインしていなくても開ける。ログインしていても同じように動く（ログインの札には触らない。
' パスワードを変えると世代が進むので、その札も切れる）
Public Class AccountResetPage
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
            ' 形が合言葉でないものは Cookie にしない（移り直した先で「このリンクは使えません」になる）。
            ' 【この Redirect は Try/Catch の外】ThreadAbortException を投げる
            Dim fromLink As String = Request.QueryString(AccountReset.TokenQueryName)

            If fromLink IsNot Nothing Then

                If AccountReset.LooksLikeToken(fromLink) Then
                    AccountReset.WriteTokenCookie(Response, fromLink)
                Else
                    AccountReset.ClearTokenCookie(Response)
                End If

                Response.Redirect(AccountReset.ResetPath)

            End If

            ' === 画面を出す：合言葉が今も使えるか ===
            Dim problem As String = AccountReset.CheckToken(Context, AccountReset.ReadTokenCookie(Request))

            If problem IsNot Nothing Then
                ShowProblem(problem)
            End If

        End If

    End Sub


    Protected Sub SaveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles SaveButton.Click

        ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るため
        FormErrors.Clear(NewPasswordBox, ConfirmPasswordBox)

        Dim result As AccountReset.ResetResult =
            AccountReset.CompleteReset(Context, AccountReset.ReadTokenCookie(Request),
                                       NewPasswordBox.Text, ConfirmPasswordBox.Text)

        If result.Ok Then

            ' 合言葉はもう使った（DB から消えている）。Cookie も消して、ログインの画面へ
            ' 【自動でログインさせない】打ち間違いに、いちばん早く気づけるように（最初の管理人を作る画面と同じ理由）
            AccountReset.ClearTokenCookie(Response)
            Response.Redirect(AccountPageBase.LoginUrl & "?" & AccountLoginPage.ResetDoneQuery)

        End If

        If result.LinkDead Then
            ' 合言葉が使えなかった（期限切れ・もう使われた、など）。欄を出したままだと、何度押しても通らない
            AccountReset.ClearTokenCookie(Response)
            ShowProblem(result.Message)
            Return
        End If

        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)

        ' 【どの欄が悪いかの印】理由が、その欄の上へ移る（Scripts/site.js の「9」・Core/FormErrors.vb）
        ' （前の回の印は、この上で消している）
        Select Case result.Field
            Case AccountReset.FieldNewPassword : FormErrors.MarkField(NewPasswordBox)
            Case AccountReset.FieldConfirm : FormErrors.MarkField(ConfirmPasswordBox)
        End Select

    End Sub


    ' 使えないリンク：理由を出し、欄を隠して「送り直す」へ案内する
    Private Sub ShowProblem(message As String)

        ErrorText.Text = HttpUtility.HtmlEncode(message)
        FormArea.Visible = False
        AgainArea.Visible = True

    End Sub


End Class
