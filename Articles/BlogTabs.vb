' ブログの画面の上の切り替え「記事一覧｜カテゴリ｜ブログの設定」
'
' 【なぜ作ったか】指摘：「記事」では何のことか分からないので「ブログ」と呼ぶ。説明が無くても直感で分かる形を徹底する。
' 管理メニューは「ブログ」の1つにまとめ、ブログの中の3つの画面は、この切り替えで行き来する
' （それまでは管理メニューに「記事」「カテゴリ」が並び、ブログのタイトルと説明はプロフィールの画面にあった）。
'
' 【置く画面は3つ】記事一覧（Articles/Manage.aspx）・カテゴリ（Articles/Categories.aspx）・ブログの設定（Articles/BlogSettings.aspx）。
' 記事を書く画面（Articles/Edit.aspx）には置かない（「記事一覧へ戻る」で戻る。書いている途中に別の画面へ移る入口を増やさない）。
'
' 【記事を書けないアカウントには出さない】カテゴリとブログの設定は、記事を書く人のためのもの（どちらの画面も、書けないアカウントは記事一覧へ返す）。
' 1つしか無い切り替えは、切り替えではないので、記事一覧の画面でも出さない。
'
' 【戻すのは決まった文字列だけ】呼ぶ側は <%= %>（エスケープしない形）で出すので、ここに外から来た値を混ぜないこと。
' 【今いる画面の印】aria-current="page"（管理メニューと同じ。見た目は Content/Site.css の .blog-tabs）
Public NotInheritable Class BlogTabs

    Private Sub New()
    End Sub

    ' 記事一覧の URL（管理メニューの「ブログ」の行き先と同じ）
    Public Const ListUrl As String = "/account/articles"

    ' カテゴリの画面の URL
    Public Const CategoriesUrl As String = "/account/categories"

    ' ブログの設定の画面の URL（App_Start/RouteConfig.vb の BlogSettings）
    Public Const SettingsUrl As String = "/account/blog/settings"


    ' 切り替えの HTML（書けないアカウントには空文字）。here は Page.AppRelativeVirtualPath
    Public Shared Function Html(roleKey As String, here As String) As String

        If Not RoleLimits.GetBoolean(roleKey, RoleLimits.KeyCanWriteArticles) Then
            Return ""
        End If

        Dim path As String = If(here, "").ToLowerInvariant()

        Dim sb As New Text.StringBuilder()

        sb.Append("<nav class=""blog-tabs"" aria-label=""ブログの切り替え""><ul>")
        AppendTab(sb, "記事一覧", ListUrl, path = "~/articles/manage.aspx")
        AppendTab(sb, "カテゴリ", CategoriesUrl, path = "~/articles/categories.aspx")
        AppendTab(sb, "ブログの設定", SettingsUrl, path = "~/articles/blogsettings.aspx")
        sb.Append("</ul></nav>")

        Return sb.ToString()

    End Function


    Private Shared Sub AppendTab(sb As Text.StringBuilder, text As String, url As String, current As Boolean)

        sb.Append("<li><a href=""").Append(url).Append("""")

        If current Then
            sb.Append(" aria-current=""page""")
        End If

        sb.Append(">").Append(HttpUtility.HtmlEncode(text)).Append("</a></li>")

    End Sub

End Class
