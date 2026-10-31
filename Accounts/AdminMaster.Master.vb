' 管理人の画面の、入れ子のマスターページ
'
' 中身の説明は AdminMaster.Master の先頭のコメントにある。
' ここが持つのは「一般向けのものを消す」「メニューを出すか」「今いる画面はどれか」の3つだけ
Public Class AdminMaster
    Inherits System.Web.UI.MasterPage


    ' === 一般の人向けのものを出さない ===
    '
    ' 【Init で決める】Site.Master はこの値を自分の PreRender で当てるので、それより前ならよい。
    ' Init は内側（このマスター）から外側（Site.Master）の順に走るので、ここがいちばん早い
    Protected Sub Page_Init(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Init

        Dim site As SiteMaster = TryCast(Master, SiteMaster)

        If site IsNot Nothing Then
            site.ShowPublicChrome = False
        End If

    End Sub


    ' === メニューは、ログインしているときだけ ===
    '
    ' 【AccountSession.Current は、1回の要求の中では DB を1回しか引かない】
    ' 各ページの AccountPageBase もこれを呼んでいるので、ここで呼んでも問い合わせは増えない
    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        AdminHeader.Visible = (AccountSession.Current(Context) IsNot Nothing)

    End Sub


    ' 【ヘッダーの「表示名（ロール）」（WhoText・WhoName・WhoRole）は、消した】
    ' サイト全体の「右上の角」（Site.Master.vb の AccountCornerHtml）が、名前・ロール・ログアウトを出すようになったため

    ' === 管理メニューの項目（Accounts/AdminMenu.vb の1か所へ寄せた） ===
    '
    ' 【お知らせはサイト管理者だけ】（ブログ型の⑤）AdminMenu.PagesFor が抜く。
    ' ロールを見るので DB を1行引くが、1回の要求の中では AccountSession.Current が1回しか引かない。
    ' **出さないだけでは守りにならない**ので、画面の側でも断っている（Notices/Manage.aspx.vb の RequireAdministrator）
    '
    ' 【戻すのは決まった文字列だけ】<%= %>（エスケープしない形）で出す
    Protected Function AdminNavItemsHtml() As String

        Dim here As String = Page.AppRelativeVirtualPath
        Dim sb As New Text.StringBuilder(AdminMenu.LinkItemHtml(AdminMenu.Home, here))

        For Each entry As AdminMenu.Item In AdminMenu.PagesFor(AccountSession.Current(Context))
            sb.Append(AdminMenu.LinkItemHtml(entry, here))
        Next

        Return sb.ToString()

    End Function


    ' 【使っている量と上限は、Articles/ArticleUsage.vb へ移した】
    ' 管理メニューの行の右端にはもう出さない（理由：ブログはこのサイトの主な機能ではないため）。
    ' 出すのは記事一覧の上と、記事を書く画面の見出しの横だけ


    ' 【今いる画面の印（CurrentMark）は、Accounts/AdminMenu.vb へ移した】
    ' 項目ごとに「どの画面で印を付けるか」を一覧と一緒に持つため（右上の角のメニューも同じ印を使う）

End Class
