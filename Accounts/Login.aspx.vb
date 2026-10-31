' ログインの画面
'
' 【AccountPageBase は継承しない】
' あちらは「ログインしていなければ、この画面へ返す」ので、継承すると自分自身へ転送し続ける。
' Chat/Logout.aspx が ChatPageBase を継承していないのと、同じ形の話である。
'
' 【ポストバックで動かしている】
' 管理画面まわりは Web Forms をあえて使い倒す、という方針どおり
' （「管理画面とブログCMSでは、Web Forms を『あえて』使い倒す」）。
' $.ajax + WebMethod にしていないのは、
'   ・このページは jQuery を読み込んでいない（Site.Master は読まない）
'   ・失敗したら画面を出し直すだけで、部分更新の必要が無い
' ため。**判断そのものは Accounts/AccountSession.vb が持っていて、ここは呼ぶだけ**である
Public Class AccountLoginPage
    Inherits System.Web.UI.Page

    ' パスワードを変えたあとに戻ってくるときの合図（Accounts/Reset.aspx が付ける。文言はこちらが持つ）
    Public Const ResetDoneQuery As String = "reset=done"

    ' 退会したあとに戻ってくるときの合図（Accounts/Settings.aspx が付ける。文言はこちらが持つ）
    '
    ' 【クエリ文字列で、文言そのものを運ばない】運ぶと、リンクを送りつけるだけで
    ' 好きな文をこの画面に出せる（決めた決まり）
    Public Const RetiredQuery As String = "retired=done"


    ' 「新規登録はこちら」の行き先（ログイン・新規登録・忘れたときの画面の間で、戻り先を持ち回す）
    ' 【受け取った値をそのまま付けない】AccountReturnUrl.WithReturn が Normalize を通す（使えない値なら戻り先を付けない）
    Protected ReadOnly Property RegisterLinkUrl As String
        Get
            Return AccountReturnUrl.WithReturn("/account/register", Request.QueryString(AccountReturnUrl.QueryName))
        End Get
    End Property


    ' 「パスワードを忘れたとき」の行き先（ログイン・新規登録・忘れたときの画面の間で、戻り先を持ち回す）
    ' 【受け取った値をそのまま付けない】AccountReturnUrl.WithReturn が Normalize を通す（使えない値なら戻り先を付けない）
    Protected ReadOnly Property ForgotLinkUrl As String
        Get
            Return AccountReturnUrl.WithReturn("/account/forgot", Request.QueryString(AccountReturnUrl.QueryName))
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【キャッシュさせない】
        ' ログインの画面が戻るボタンで出てくると、打ったユーザー名が残ったり、
        ' ログアウトしたあとに「ログイン中」の画面が見えたりする。
        ' Chat/Logout.aspx と同じ理由
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        If Not IsPostBack Then

            If Request.QueryString("reset") = "done" Then
                DoneText.Text = HttpUtility.HtmlEncode("パスワードを変えました。新しいパスワードでログインしてください。")
            ElseIf Request.QueryString("retired") = "done" Then

                ' 【ログインの欄は出さず、「退会しました」だけの画面にする】
                ' 退会の手続きの中でログアウトも済んでいるので、下の「ログイン済みなら転送」には当たらない
                LoginView.Visible = False
                RetiredView.Visible = True
                Page.Title = "退会しました"
                Return

            End If

            ' === もうログインしている人は、アカウントのページへ ===
            '
            ' トップページが「Cookieがある人はラウンジへ」と返しているのと、同じ形の導線。
            '
            ' 【この Redirect を Try/Catch の中へ移さないこと】
            ' 既定（endResponse = True）は ThreadAbortException を投げるため、
            ' Catch が走って嘘のエラー記録が出る
            If AccountSession.Current(Context) IsNot Nothing Then
                Response.Redirect(ReturnTarget())
            End If

        End If

    End Sub


    Protected Sub SignInButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles SignInButton.Click

        Dim result As AccountSession.SignInResult =
            AccountSession.SignIn(Context, LoginIdBox.Text, PasswordBox.Text)

        If result.Ok Then

            ' 飛ばされてきた元のページへ戻す
            Response.Redirect(ReturnTarget())

        End If

        ' === 断られた ===
        '
        ' 【打たれたパスワードは、画面に残さない】
        ' TextMode="Password" の asp:TextBox は、そもそも値を描き直さない（ASP.NET の仕様）。
        ' ユーザー名のほうは残す（打ち直させないため）
        ErrorText.Text = HttpUtility.HtmlEncode(result.Message)

    End Sub


    ' ログインしたあとに行く先
    '
    ' 【必ず AccountReturnUrl.Normalize を通すこと】
    ' ?returnUrl= は URL を作る人が自由に書ける。そのまま Redirect すると、
    ' 本物のログインの画面を通したあとで偽物のサイトへ連れて行ける（オープンリダイレクト）。
    ' 使えない値なら /account が返る。
    '
    ' 【ポストバックでも、クエリ文字列は残っている】
    ' フォームの送り先（action）は、今の URL をクエリ文字列ごと使うため
    ' （開発機で確かめた）
    Private Function ReturnTarget() As String
        Return AccountReturnUrl.Normalize(Request.QueryString(AccountReturnUrl.QueryName))
    End Function

End Class
