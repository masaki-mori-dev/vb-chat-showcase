' ブログの設定（URL は /account/blog/settings）
'
' 【何を置くか】ブログのタイトルと説明だけ。プロフィールの画面から移した。
' プロフィールに残るのは、アイコン・自己紹介（誕生日などの基本情報）・SNS。
'
' 【保存は別の道】AccountProfileStore.SaveBlog（この2列だけを書く）。プロフィールの保存（AccountProfileStore.Save）は
' この2列に触らない＝**どちらで保存しても、もう片方で決めたものは消えない・空で上書きされない**。
'
' 【記事を書けないアカウントは、記事一覧へ返す】カテゴリの画面と同じ（ブログの切り替えも、書けないアカウントには出ない）。
'
' 【PRG】うまくいったら ?done=saved で開き直す（基本ルールの⑥）。断ったときはその場で、悪い欄の上に理由（⑧⑨。Core/FormErrors.vb）
Public Class BlogSettingsPage
    Inherits AccountPageBase

    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"

    ' 説明をほどいた結果（ポストバックのときだけ入る）
    Private _descriptionDecode As BodyTransport.DecodeResult


    ' ブログの切り替え（記事一覧｜カテゴリ｜ブログの設定）。決まった文字列だけ（Articles/BlogTabs.vb）
    Protected ReadOnly Property BlogTabsHtml As String
        Get
            Return BlogTabs.Html(CurrentAccount.RoleKey, Page.AppRelativeVirtualPath)
        End Get
    End Property

    ' タイトルが空のときの見出し（「〇〇の記事」）。決め方は AccountProfileRules.BlogTitleOf の1か所
    Protected ReadOnly Property DefaultTitleText As String
        Get
            Return AccountProfileRules.BlogTitleOf(Nothing, CurrentAccount.DisplayName)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【包む仕掛けを、画面へつなぐ】（Scripts/site.js の「7」）。属性にコードブロックは書けないので、ここで入れる
        DescriptionBox.Attributes("data-encode-on-submit") = DescriptionEncoded.ClientID
        DescriptionBox.Attributes("data-encoded-prefix") = BodyTransport.EncodedPrefix

        ViewLink.NavigateUrl = ArticleRules.AccountUrl(CurrentAccount.UserName)

        If IsPostBack Then

            FormErrors.Clear(TitleBox, DescriptionBox)

            ' 【ほどくのは、ここ1か所だけ】（Accounts/Profile.aspx.vb の DecodeWrapped と同じ理由）
            If Not String.IsNullOrEmpty(DescriptionEncoded.Value) Then

                _descriptionDecode = BodyTransport.Decode(DescriptionEncoded.Value,
                                                          AccountProfileRules.BlogDescriptionMaxLength,
                                                          "ブログの説明", "保存していません。")

                DescriptionBox.Text = If(_descriptionDecode.Ok, _descriptionDecode.Html, DescriptionEncoded.Value)

            End If

            Return

        End If

        If Not RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles) Then

            ' 【この Redirect を Try/Catch の中へ移さないこと】ThreadAbortException を投げる
            Response.Redirect(BlogTabs.ListUrl)

        End If

        If Request.QueryString("done") = "saved" Then
            DoneText.Text = HttpUtility.HtmlEncode("ブログの設定を保存しました。")
        End If

        Try

            Dim p As AccountProfileStore.ProfileRecord = AccountProfileStore.Find(CurrentAccount.AccountId)

            TitleBox.Text = If(p.BlogTitle, "")
            DescriptionBox.Text = If(p.BlogDescription, "")

        Catch ex As Exception

            ErrorReport.Report(ex, "BlogSettingsPage.Load")

            ' 【読めなかったら、欄を出さない】空の欄のまま保存されると、決めてあったタイトルと説明が消えるため
            ErrorText.Text = HttpUtility.HtmlEncode(TroubleMessage)
            SettingsPanel.Visible = False

        End Try

    End Sub


    Protected Sub SaveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles SaveButton.Click

        If Not RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles) Then
            ShowError("このアカウントでは、ブログを書けません。")
            Return
        End If

        If _descriptionDecode IsNot Nothing AndAlso Not _descriptionDecode.Ok Then
            ShowError(_descriptionDecode.Message, DescriptionBox)
            DescriptionEncoded.Value = ""
            Return
        End If

        Dim title As String = TitleBox.Text
        Dim description As String = DescriptionBox.Text
        Dim fieldKey As String = Nothing

        Dim reason As String = AccountProfileRules.PrepareBlog(title, description, fieldKey)

        If reason IsNot Nothing Then
            ShowError(reason, If(fieldKey = AccountProfileRules.FieldBlogDescription, CType(DescriptionBox, WebControl), TitleBox))
            DescriptionEncoded.Value = ""
            Return
        End If

        Try
            AccountProfileStore.SaveBlog(CurrentAccount.AccountId, title, description)
        Catch ex As Exception

            ErrorReport.Report(ex, "BlogSettingsPage.Save")
            ShowError(TroubleMessage)
            DescriptionEncoded.Value = ""
            Return

        End Try

        ' 【サイトの記事の控え（メニュー・更新履歴）にブログのタイトルは入っていない】ので、捨てる控えは無い。
        ' ブログのページ（トップ・右の枠）は開くたびに AccountProfiles を読む

        ' Try/Catch の外（Redirect は ThreadAbortException を投げる）
        Response.Redirect(BlogTabs.SettingsUrl & "?done=saved")

    End Sub


    ' 断った理由を、悪い欄の上に出す（Scripts/site.js の「9」が、data-error-jump の文を印の付いた欄の上へ移す）
    Private Sub ShowError(message As String, Optional field As WebControl = Nothing)

        ErrorText.Text = HttpUtility.HtmlEncode(message)
        DoneText.Text = ""

        If field IsNot Nothing Then
            FormErrors.MarkField(field)
        End If

    End Sub

End Class
