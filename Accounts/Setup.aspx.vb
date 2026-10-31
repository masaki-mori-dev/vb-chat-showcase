Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 最初のサイト管理者を作る画面（画面の言葉は「サイト管理者」にそろえた＝「部屋の管理人」と紛れないように）
'
' 【ふだんは404】
' 開くのは、次の2つが**両方**そろっている間だけ。
'   ① アカウントが1つも無い
'   ② App_Data\account-setup.txt が置かれていて、その中身（合言葉）と ?key= が一致する
' どちらか欠けたら、存在しないページとまったく同じ404を返す。
'
' 【なぜ「0件か」だけでは足りないのか】
' 発行してから最初の管理人を作るまでの間に、**他人に取られる**。
' その隙間は、本番へ上げてから開発者が画面を開くまでの数分だが、URLは決まっているので、
' 待ち構えていれば取れてしまう。合言葉は、その数分を塞ぐためだけにある。
'
' 【なぜ SQL で直接 INSERT しないのか】
' パスワードがハッシュ（PBKDF2）なので、SSMS からは作れない。
'
' 【なぜ、確かめたあとも画面を残すのか】
' 閉じている間は、ほかの存在しないページとまったく同じ404を返すだけで、何もしない。
' サーバーを移した日・2世代目のDBを作った日に、また同じ手順が要る
' （Admin/MailTest.aspx を残してあるのと、同じ判断）。
' 閉じていることは発行後の確かめの道具が毎回確かめる。
'
' 【Admin/MailTest.aspx と違うところ】
' あちらは markup を持たないページなので、Response.Flush ＋ CompleteRequest で打ち切れた。
' こちらは Site.Master を使う画面なので、**それだけでは止まらない**
' （CompleteRequest が飛ばすのはパイプラインの後続イベントで、実行中の Page は最後まで走る。
'   「Response.Redirect は、既定で呼ぶ」の落とし穴に、同じ話が書いてある）。
' そこで Render を差し替えて、閉じているときは1バイトも書かない形にしてある。
' **Response.End は使っていない**（ThreadAbortException を投げるため）
Public Class AccountSetupPage
    Inherits System.Web.UI.Page

    ' 合言葉のファイル
    Private Const GateRelativePath As String = "~/App_Data/account-setup.txt"


    ' この画面は開いているか（OnInit で1回だけ決める）
    Private isOpen As Boolean


    Protected Overrides Sub OnInit(e As EventArgs)

        isOpen = IsGateOpen(Request.QueryString("key")) AndAlso NoAccountYet()

        If Not isOpen Then

            ' 閉じているときの404（Chat/PrivateImage.aspx・Admin/MailTest.aspx と同じ形）
            '
            ' 【本番のIISに、エラー画面へ差し替えさせない】
            ' 差し替えられると、開発機と本番で応答が変わり、
            ' 発行後の確かめの道具が「このページが届いているか」を見分けられなくなる
            ' （IIS が 404 の本文を差し替える落とし穴）
            Response.Clear()
            Response.StatusCode = 404
            Response.ContentType = "text/plain"
            Response.TrySkipIisCustomErrors = True

        End If

        MyBase.OnInit(e)

    End Sub


    ' 閉じているときは、1バイトも書かない
    '
    ' 【Response.SuppressContent ではなく、ここで止める理由】
    ' あちらは「送らない」だけで、マスターページの組み立て自体は走る。
    ' 走らせる意味が無いうえ、途中で例外が出れば、閉じているはずの画面が500を返すことになる
    Protected Overrides Sub Render(writer As System.Web.UI.HtmlTextWriter)

        If Not isOpen Then
            Return
        End If

        MyBase.Render(writer)

    End Sub


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' キャッシュさせない（叩くたびに、今の状態で判断したい）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

    End Sub


    Protected Sub CreateButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles CreateButton.Click

        ' 【閉じているときは、何もしない】
        ' Render は止めてあるが、ポストバックのイベントは走りうる
        ' （閉じたあとに、開いていた頃の画面から送られてくることがある）。
        ' **画面を出さないことと、処理をしないことは別である**
        If Not isOpen Then
            Return
        End If

        Dim userName As String = If(UserNameBox.Text, "").Trim()
        Dim displayName As String = If(DisplayNameBox.Text, "").Trim()
        Dim email As String = If(EmailBox.Text, "").Trim()
        Dim password As String = AccountPassword.Normalize(PasswordBox.Text)
        Dim confirm As String = AccountPassword.Normalize(PasswordConfirmBox.Text)

        ' === 検査 ===
        '
        ' 【ここが本当の検査である】画面の required・maxlength は親切であって、防御ではない
        ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るため
        FormErrors.Clear(UserNameBox, DisplayNameBox, EmailBox, PasswordBox, PasswordConfirmBox)

        ' 【どの欄で断ったかも覚える】理由を、その欄の上に出すため（Core/FormErrors.vb）
        Dim badBox As WebControl = UserNameBox
        Dim reason As String = AccountStore.ValidateUserName(userName)

        If reason Is Nothing Then
            badBox = EmailBox
            reason = AccountStore.ValidateEmail(email)
        End If

        If reason Is Nothing Then
            badBox = PasswordBox
            reason = AccountPassword.Validate(password)
        End If

        If reason Is Nothing AndAlso Not String.Equals(password, confirm, StringComparison.Ordinal) Then
            badBox = PasswordConfirmBox
            reason = "パスワードと、確認のパスワードが違います。"
        End If

        ' 表示名は、空ならユーザー名と同じにする
        If displayName.Length = 0 Then
            displayName = userName
        ElseIf displayName.Length > AccountStore.DisplayNameMaxLength Then
            badBox = DisplayNameBox
            reason = "名前（表示名）は" & AccountStore.DisplayNameMaxLength & "文字以内にしてください。"
        End If

        If reason IsNot Nothing Then
            ErrorText.Text = HttpUtility.HtmlEncode(reason)
            FormErrors.MarkField(badBox)
            Return
        End If

        ' === 作る ===
        Dim created As Boolean

        Try

            ' 【ハッシュの計算に0.4秒ほどかかる】
            ' 反復回数を10万回にしてあるため（Accounts/AccountPassword.vb）。この画面では一度きりである
            created = AccountStore.CreateFirstAdministrator(
                userName, displayName, email, AccountPassword.Hash(password))

        Catch ex As Exception

            ErrorReport.Report(ex, "AccountSetupPage.Create")

            ' 【理由を利用者に見せない】中で何が起きたかは、エラー報告メールで届く。
            ' 一意索引に当たった（同じユーザー名・メールが既に在る）場合も、ここへ来る
            ErrorText.Text = "作れませんでした。時間をおいて、もう一度お試しください。"
            Return

        End Try

        If Not created Then

            ' 「0件か」の確認と INSERT は1つの文なので、ここへ来るのは
            ' **ほぼ同時に2回押された**か、この画面を開いている間に別の経路で作られたとき
            ErrorText.Text = "すでにアカウントがあります。この画面はもう使えません。"
            Return

        End If

        ' === ログインの画面へ ===
        '
        ' 【ここで自動的にログインさせない】
        ' 作ったパスワードで本当に入れるかを、その場で確かめてもらうため
        ' （打ち間違いに、いちばん早く気づける）。
        '
        ' 【この Redirect を Try/Catch の中へ移さないこと】ThreadAbortException を投げる
        Response.Redirect(AccountPageBase.LoginUrl)

    End Sub


    ' アカウントが1つも無いか
    '
    ' 【DBが読めないときは「無い」としない（フェイルクローズ）】
    ' 読めないことを「0件」として扱うと、DBが詰まっている間、この画面が誰にでも開く。
    ' 合言葉もそろっていなければ開かないので二重の守りではあるが、
    ' **読めない＝開かない**にしておくほうが素直である
    Private Shared Function NoAccountYet() As Boolean

        Try
            Return Not AccountStore.AnyAccountExists()

        Catch ex As Exception

            ErrorReport.Report(ex, "AccountSetupPage.NoAccountYet")
            Return False

        End Try

    End Function


    ' 合言葉が合っているか（判定は Core/GateFile.vb の1か所。まとめた）
    Private Shared Function IsGateOpen(givenKey As String) As Boolean
        Return GateFile.IsOpen(GateRelativePath, givenKey)
    End Function

End Class
