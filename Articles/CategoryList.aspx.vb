Imports System.Text.RegularExpressions

' カテゴリの一覧（/articles/{user}/category/{番号}）。A-3b
'
' 【公開の記事だけを、カテゴリの並べ方で全部】並べ方は ArticleRules.OrderByClause の1か所（管理画面の一覧と同じ順）。
' 【404 にするもの】無い・止めているアカウント／無い番号・他人のカテゴリ／**公開の記事が1本も無いカテゴリ**
' （下書きだけのカテゴリの名前を外に出さないため）。どれも同じ画面・同じステータス。
' 【{user} の大文字小文字違い・番号の書き方違い（01）は、正しい URL へ 301】
' 【AccountPageBase を継承しない】ログインしていない人にも見せるページなので。
Public Class ArticleCategoryList
    Inherits System.Web.UI.Page

    Private Shared ReadOnly IdPattern As New Regex("^[0-9]{1,9}$", RegexOptions.Compiled)


    ' === マークアップから参照する値（ListHtml だけが、組み立て済みの HTML） ===
    Protected Property CategoryName As String = ""
    Protected Property AuthorName As String = ""
    Protected Property AuthorUrl As String = ""
    Protected Property ArticleCount As Integer
    Protected Property ListHtml As String = ""


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        Dim rawUser As String = TryCast(RouteData.Values("user"), String)
        Dim rawId As String = TryCast(RouteData.Values("categoryId"), String)

        If String.IsNullOrEmpty(rawUser) OrElse rawUser <> rawUser.Trim() OrElse
           AccountStore.ValidateUserName(rawUser) IsNot Nothing OrElse
           String.IsNullOrEmpty(rawId) OrElse Not IdPattern.IsMatch(rawId) Then

            ShowNotFound()
            Return

        End If

        Dim categoryId As Integer = Integer.Parse(rawId, Globalization.CultureInfo.InvariantCulture)

        If categoryId <= 0 Then
            ShowNotFound()
            Return
        End If

        Dim account As AccountStore.AccountRecord
        Dim category As ArticleCategoryStore.CategoryRecord
        Dim articles As List(Of ArticleStore.ArticleRecord)

        Try

            account = AccountStore.FindActiveByUserName(rawUser)

            If account Is Nothing Then
                ShowNotFound()
                Return
            End If

            ' 【他人のカテゴリは Nothing】Find が AccountId で絞っている
            category = ArticleCategoryStore.Find(account.AccountId, categoryId)

            If category Is Nothing Then
                ShowNotFound()
                Return
            End If

            articles = ArticleStore.GetPublishedInCategory(account.AccountId, categoryId, category.SortMode, Nothing)

            ' 【転送の判定より先に置くこと】公開の記事が無いカテゴリを正しい URL へ転送すると、在ることが分かってしまう
            If articles.Count = 0 Then
                ShowNotFound()
                Return
            End If

            If Not String.Equals(rawUser, account.UserName, StringComparison.Ordinal) OrElse
               Not String.Equals(rawId, categoryId.ToString(Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) Then

                Response.RedirectPermanent(ArticleRules.CategoryUrl(account.UserName, categoryId))

            End If

        Catch ex As Threading.ThreadAbortException
            ' RedirectPermanent（endResponse = True）が投げる。エラーとして扱わない
            Throw
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleCategoryList.Page_Load")

            PageResponse.MarkTrouble(Response)
            Page.Title = "表示できませんでした"
            TroubleView.Visible = True
            Return

        End Try

        ' 【右の枠を、このブログのものに差し替える】（ブログ型の③）
        BlogSidebar.Install(Master, account.AccountId, account.UserName, account.DisplayName)

        Page.Title = category.Name

        CategoryName = category.Name
        AuthorName = account.DisplayName
        AuthorUrl = ArticleRules.AccountUrl(account.UserName)
        ArticleCount = articles.Count
        ListHtml = ArticleDisplay.ListHtml(articles, account.UserName)

        CategoryView.Visible = True

    End Sub


    Private Sub ShowNotFound()

        PageResponse.MarkNotFound(Response)

        Page.Title = "ページが見つかりません"
        NotFoundView.Visible = True

    End Sub

End Class
