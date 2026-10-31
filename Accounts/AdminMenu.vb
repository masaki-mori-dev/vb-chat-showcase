' 管理画面の中のページの一覧
'
' 【1か所で持つ】使うのは2つ（基本ルールの12）。
'   ・管理画面の管理メニュー（Accounts/AdminMaster.Master の AdminNavHtml）＝「ホーム」＋下の項目
'   ・右上の角のメニュー（Site.Master.vb の AccountCornerHtml）＝「管理画面」の下に、下の項目を字下げして並べる
' **項目を足す・並べ替える・名前を変えるときは、ここだけ直せば両方に効く。**
'
' 【お知らせはサイト管理者だけ】管理メニューと同じく、右上の角のメニューにもサイト管理者にだけ出す
' （出さないだけでは守りにならない。画面の側でも RequireAdministrator が断る）。
'
' 【今いる画面の印も、ここで決める】ページ（.aspx の置き場所）を項目ごとに持っている。
'   aria-current="page" … まさにこの画面
'   aria-current="true" … その項目の中の画面（記事の編集・チャット履歴の中身）
'
' 【戻すのは決まった文字列だけ】呼ぶ側は <%= %>（エスケープしない形）で出すので、ここに外から来た値を混ぜないこと
Public NotInheritable Class AdminMenu

    Private Sub New()
    End Sub


    Public NotInheritable Class Item

        Public ReadOnly Property Text As String
        Public ReadOnly Property Url As String
        Public ReadOnly Property AdministratorOnly As Boolean

        ' この画面にいるとき aria-current="page"
        Public ReadOnly Property Pages As String()

        ' この画面にいるとき aria-current="true"
        Public ReadOnly Property SubPages As String()

        Public Sub New(text As String, url As String, administratorOnly As Boolean,
                       pages As String(), subPages As String())
            Me.Text = text
            Me.Url = url
            Me.AdministratorOnly = administratorOnly
            Me.Pages = pages
            Me.SubPages = subPages
        End Sub

    End Class


    ' 管理画面のホーム（右上の角のメニューでは「管理画面」という名前で出す）
    Public Shared ReadOnly Home As New Item("ホーム", "/account", False,
                                            {"~/accounts/home.aspx"}, {})

    ' 管理画面の中のページ（この順で並ぶ。チャット履歴が先＝チャットがこのサイトの主な機能のため）
    ' 「ログを調べる」（サイト管理者だけ）を、チャット履歴の次に（同じチャットのログの道具なので並べた）。
    ' 「記事」を「ブログ」にし、「カテゴリ」は外した（ブログの中の切り替え＝Articles/BlogTabs.vb へ）。
    ' 指摘：「記事」では何のことか分からないので「ブログ」と呼ぶ。カテゴリ・ブログの設定・記事を書く画面は、
    ' ブログの中の画面として aria-current="true" の側に入れてある
    Public Shared ReadOnly Pages As Item() = {
        New Item("チャット履歴", "/account/chatlogs", False, {"~/chat/mychatlogs.aspx"}, {"~/chat/mychatlogview.aspx"}),
        New Item("ログを調べる", "/account/logs", True, {"~/admin/logsearch.aspx"}, {"~/admin/logreplay.aspx", "~/admin/logaudit.aspx"}),
        New Item("ブログ", "/account/articles", False, {"~/articles/manage.aspx"},
                 {"~/articles/edit.aspx", "~/articles/categories.aspx", "~/articles/blogsettings.aspx"}),
        New Item("お知らせ", "/account/notices", True, {"~/notices/manage.aspx"}, {}),
        New Item("表示", "/account/display", True, {"~/accounts/sitedisplay.aspx"}, {}),
        New Item("プロフィール", "/account/profile", False, {"~/accounts/profile.aspx"}, {}),
        New Item("設定", "/account/settings", False, {"~/accounts/settings.aspx"}, {})}


    ' その人に見せる項目（サイト管理者でなければ、お知らせを抜く）
    Public Shared Function PagesFor(account As AccountStore.AccountRecord) As List(Of Item)

        Dim isAdmin As Boolean = account IsNot Nothing AndAlso
            String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal)

        Dim list As New List(Of Item)()

        For Each entry As Item In Pages
            If Not entry.AdministratorOnly OrElse isAdmin Then
                list.Add(entry)
            End If
        Next

        Return list

    End Function


    ' 今いる画面の印（無ければ空）。here は Page.AppRelativeVirtualPath
    Public Shared Function CurrentMark(entry As Item, here As String) As String

        Dim path As String = If(here, "").ToLowerInvariant()

        If Array.IndexOf(entry.Pages, path) >= 0 Then
            Return " aria-current=""page"""
        End If

        If Array.IndexOf(entry.SubPages, path) >= 0 Then
            Return " aria-current=""true"""
        End If

        Return ""

    End Function


    ' 1項目の <li><a …>…</a></li>（text を渡すと、表示名だけ差し替える＝右上の角の「管理画面」）
    Public Shared Function LinkItemHtml(entry As Item, here As String, Optional text As String = Nothing) As String

        Return "<li><a href=""" & entry.Url & """" & CurrentMark(entry, here) & ">" &
               HttpUtility.HtmlEncode(If(text, entry.Text)) & "</a></li>"

    End Function

End Class
