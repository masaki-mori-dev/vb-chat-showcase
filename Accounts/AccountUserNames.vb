' 使わせないユーザー名（一般ユーザーの登録）
'
' 【何のための一覧か】ユーザー名は、そのまま2つのアドレスに出る。
'
'     /articles/{ユーザー名}    … ブログのトップ
'     /profile/{ユーザー名}     … 公開のプロフィール
'
' そのため、次の2つを断る必要がある。
'   ① **管理人のふりに使える名前**（kanrinin・admin・support など）
'      ＝「/profile/admin」を持った人は、事情を知らない人には運営に見える
'   ② **サイトのアドレスとぶつかる名前**（account・articles・chat など）
'      ＝今はぶつからなくても、1区切りの URL を足した日にぶつかる
'
' 【この一覧は、コードの定数で持つ】
' DB（SiteSettings）に置くと、**管理画面から弱められる**——
' 「admin を許す」と1行変えるだけで、なりすましの口が開く。
' AccountPassword.MinLength・ContactRules の上限を定数にしてあるのと、まったく同じ判断である。
'
' 【退会した人の名前は、ここには入らない】あちらは DB の表（RetiredUserNames）で、
' 退会のたびに増える。確かめるのは AccountStore.CheckUserName の1か所で、
' 「もう誰かが使っている」と「退会した人の名前」を、そこでまとめて見る。
'
' 【大文字・小文字は区別しない】HashSet の比較子で寝かせてある。
' DB 側（Accounts.UserName の一意索引）も既定の照合順序で区別しないので、向きがそろう。
'
' 【最初のサイト管理者を作る画面（/account/setup）は、この検査を通らない】
' あちらは開発者が1回だけ使う画面で、**本番のサイト管理者の名前は「kanrinin」**である
' （＝この一覧に入っている）。**Setup.aspx.vb に、この検査を足さないこと。**
' 足すと、新しいサーバーで本番の管理人が作れなくなる
Public NotInheritable Class AccountUserNames

    Private Sub New()
    End Sub


    ' === 使わせない名前 ===
    '
    ' 【足すのは自由。消すときは考えること】
    ' 一度公開したあとに1つ消すと、その名前は誰でも取れるようになる。
    ' 逆に足すのは安全である（まだ誰も持っていない名前なら、誰も困らない）。
    '
    ' 【罠】この初期化子（{ … }）の中に、コメント行を置かないこと。
    ' VB は From 初期化子の中のコメント行でビルドが止まる
    ' （「VB の初期化子 From の中にコメント行を置くとビルドが止まる」の落とし穴。
    '、実際にここで踏んだ）。
    ' そのため、何がどの組なのかは**下の3つの配列に分けて**表してある。

    ' ① 管理人・運営のふりに使える名前
    Private Shared ReadOnly LikeStaff As String() = {
        "admin", "admins", "administrator", "administrators",
        "kanrinin", "kanri", "unei", "staff", "official", "system", "sysop",
        "root", "owner", "operator", "manager", "master", "webmaster", "postmaster",
        "moderator", "mod", "support", "help", "helpdesk", "service", "info",
        "security", "abuse", "billing", "payment", "noreply", "no-reply",
        "vbchat", "vb-chat"}

    ' ②-1 サイトのアドレスの1区切り目に在るもの
    '
    ' **1区切りの URL を足す日に、ここへも足すこと**
    ' （今在るのは account・contact・profile・chat・articles）
    Private Shared ReadOnly LikeSiteRoot As String() = {
        "account", "accounts", "articles", "article", "profile", "profiles",
        "chat", "contact", "contacts", "uploads", "content",
        "scripts", "bundles", "signalr", "database", "bin", "app_data",
        "favicon", "robots", "sitemap", "feed", "rss", "api"}

    ' ②-2 アカウントの画面の2区切り目
    '
    ' /account/login のような名前を、そのまま人の名前にさせない
    ' （/profile/login のような、紛らわしいアドレスを作らせないため）
    ' 【notices・notice は足した】/account/notices（お知らせ）の足し忘れ。
    ' 実機テストで開発者が見つけた。**アカウントの画面を足す日に、ここへも足すこと**
    Private Shared ReadOnly LikeAccountPage As String() = {
        "login", "logout", "setup", "settings", "register", "verify",
        "forgot", "reset", "recover", "categories", "category", "edit",
        "manage", "new", "me", "you", "test", "null", "undefined",
        "notices", "notice"}


    ' 上の3つを1つにまとめたもの（確かめるのは、これ）
    Private Shared ReadOnly Reserved As HashSet(Of String) = BuildReserved()

    Private Shared Function BuildReserved() As HashSet(Of String)

        Dim all As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        all.UnionWith(LikeStaff)
        all.UnionWith(LikeSiteRoot)
        all.UnionWith(LikeAccountPage)

        Return all

    End Function


    ' 使わせない名前か
    Public Shared Function IsReserved(userName As String) As Boolean

        If String.IsNullOrEmpty(userName) Then
            Return False
        End If

        Return Reserved.Contains(userName.Trim())

    End Function


    ' 断る理由の文言（予約語だったとき、退会した人の名前だったとき）
    '
    ' 【「使えません」だけを言い、理由は言わない】
    ' 「管理人のふりに使える名前だから」「退会した人が使っていたから」を言っても、
    ' 利用者には何の役にも立たず、**一覧の中身と、誰が退会したかを推測させるだけ**である
    Public Const ReservedMessage As String =
        "そのアカウントIDは使えません。別のIDにしてください。"

End Class
