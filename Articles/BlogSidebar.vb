' ブログのページの右の枠（ブログ型の③）
'
' 【何を出すか】（開発者が決めた順）
'   ① プロフィールの簡易版（アイコン・名前・自己紹介の出だし・「プロフィールを見る」）
'   ② その人の最新の記事
'   ③ カテゴリ
'   ④ 管理人からのお知らせ（⑤で作った Notices/SiteNotices.vb を、そのまま呼ぶ）
' そのあと**袖の目次**が付くが、あれはここでは組み立てない
' ——Scripts/article-toc.js が、画面が出たあとに .right-sidebar のいちばん下へ足す。
'
' 【出す場所は3ページ】記事のページ／ブログのトップ／カテゴリの一覧。
' 呼び方は `BlogSidebar.Install(Master, 番号, ユーザー名, 名前)` の1行だけにしてある
' （3か所に同じ手順を書かない）。
'
' 【サイト全体の右の枠（お知らせ・新しいメンバー。更新履歴はやめた）は、出さなくなる】
'＝ブログのページでは「その人の最新の記事」に回す。
' 入れ替えるのは Site.Master.vb の ShowBlogSidebar。
'
' 【段組みは、今の2段のまま】右の枠の場所も幅も変えていない。
' **この回で変えたのは「枠の中身」だけである。**
'
' 【出口でエスケープする】自己紹介・記事のタイトル・カテゴリの名前は、全部利用者が決めた文字である。
' **生の HTML を出してよいのは記事の本文だけ**、という決まりはここでも変わらない。
'
' 【うまく組み立てられなかったら、空文字を返す（フェイルオープン）】
' 呼ぶ側は、空なら差し替えない＝**サイト全体の右の枠がそのまま出る。**
' 右の枠のために、記事のページを落とす筋合いが無い
Public NotInheritable Class BlogSidebar

    Private Sub New()
    End Sub


    ' 最新の記事を出す本数
    '
    ' 【、管理画面の設定】（SiteSettings.BlogRecentCount。既定5）。
    ' 前はサイト全体の更新履歴の数とそろえる定数だった（更新履歴はやめた）。管理画面で変えられる（基本ルール T）
    Public Shared ReadOnly Property RecentCount As Integer
        Get
            Return SiteSettings.BlogRecentCount
        End Get
    End Property

    ' 自己紹介の出だしを、何文字で切るか
    '
    ' 【右の枠は 200px ほどしかない】80文字で、だいたい4〜5行になる。
    ' これより長いと、下のカテゴリとお知らせが画面の外へ押し出される
    Public Const IntroExcerptLength As Integer = 80


    ' === 右の枠を、ブログのものに差し替える ===
    '
    ' master      … ページの Master（Site.Master 以外なら何もしない）
    ' accountId   … そのブログの持ち主の番号
    ' userName    … 同じ人のユーザー名（URL に使う）
    ' displayName … 同じ人の名前（表示名）
    '
    ' 【AccountStore.AccountRecord を受け取らない理由】
    ' 記事のページ（Articles/ArticleDetail.aspx.vb）が持っているのは記事の行で、
    ' アカウントの行ではない（番号・ユーザー名・表示名は、その行に JOIN で乗っている）。
    ' **アカウントの行を渡す形にすると、記事のページが DB をもう1回引くことになる。**
    '
    ' 【3ページから、この1行だけを呼ぶ】
    ' 【中で何が起きても、呼ぶ側は止まらない】組み立てに失敗したら差し替えないだけ
    ' knownProfile … 呼ぶ側がもう読んである行（無ければ Nothing で、こちらが読む）。
    '                ブログのトップ（④）は、上の帯にブログ名と説明を出すために、もう読んである
    '                ——**渡さないと、同じ行を1ページで2回読むことになる**
    Public Shared Sub Install(master As MasterPage, accountId As Integer,
                              userName As String, displayName As String,
                              Optional knownProfile As AccountProfileStore.ProfileRecord = Nothing)

        Dim site As SiteMaster = TryCast(master, SiteMaster)

        If site Is Nothing OrElse accountId <= 0 OrElse String.IsNullOrEmpty(userName) Then
            Return
        End If

        Dim html As String = BuildHtml(accountId, userName, displayName, knownProfile)

        If html.Length = 0 Then
            Return
        End If

        site.ShowBlogSidebar(html)

    End Sub


    ' === 中身を組み立てる（失敗したら空文字） ===
    '
    ' 【DB を引くのは2〜3回】①プロフィール（呼ぶ側が渡してくれたら0回）②最新の記事 ③カテゴリと数。
    ' お知らせは Notices/SiteNotices.vb がメモリに控えているので0回。
    '
    ' 【控えを持っていない】サイト全体のメニューの行き先（SiteArticles）は10分の控えを持つが、こちらは持たない。
    ' 理由＝**人ごとに違う中身**なので、控えると「誰のぶんを持っているか」の管理が要る
    ' （アカウントが増えれば、その数だけ控えが要る）。
    ' ブログのページは、開くたびに記事も引いているので、2回増えても割に合う。
    ' **人が増えて重くなったら、そのときに人ごとの控えを足すこと**
    Public Shared Function BuildHtml(accountId As Integer, userName As String, displayName As String,
                                     Optional knownProfile As AccountProfileStore.ProfileRecord = Nothing) As String

        If accountId <= 0 OrElse String.IsNullOrEmpty(userName) Then
            Return ""
        End If

        Dim profile As AccountProfileStore.ProfileRecord
        Dim recent As List(Of ArticleStore.ArticleRecord)
        Dim categories As List(Of ArticleCategoryStore.CategoryRecord)
        Dim counts As Dictionary(Of Integer, Integer)

        Try

            profile = If(knownProfile, AccountProfileStore.Find(accountId))
            recent = ArticleStore.GetPublishedRecent(accountId, RecentCount)

            ' 【公開記事の無いカテゴリは出さない】下書きだけのカテゴリの名前を、外に出さないため
            ' （ブログのトップ＝Articles/AccountTop.aspx.vb と、まったく同じ決まり）
            counts = ArticleStore.CountPublishedByCategory(accountId)
            categories = ArticleCategoryStore.GetAll(accountId)

        Catch ex As Exception

            ' 【知らせるが、止めない】右の枠が入れ替わらないだけで、記事は読める
            ErrorReport.Report(ex, "BlogSidebar.BuildHtml", "アカウント " & accountId)

            Return ""

        End Try

        Dim sb As New Text.StringBuilder()

        sb.Append(ProfileHtml(accountId, userName, displayName, profile))
        sb.Append(RecentHtml(userName, recent))
        sb.Append(CategoriesHtml(userName, categories, counts))

        ' ④ 管理人からのお知らせ（⑤で作ったものを、そのまま呼ぶ）
        '
        ' 【サイト全体のページと、同じ中身・同じ見た目】
        ' 「管理人からのお知らせ」は、そのブログの持ち主のお知らせではなく**サイトの運営のお知らせ**なので、
        ' 誰のブログを見ていても同じものが出る
        Dim notices As String = SiteNotices.ListHtml()

        If notices.Length > 0 Then
            sb.Append("<div class=""notice-area""><h3>管理人からのお知らせ</h3>").Append(notices)
            sb.Append(SiteMaster.SidebarMoreLink("/news", "お知らせをすべて見る")).Append("</div>")
        End If

        Return sb.ToString()

    End Function


    ' ============================================================
    ' ① プロフィールの簡易版
    ' ============================================================
    '
    ' 【自己紹介が「非公開」なら、出さない】公開のプロフィールページと、同じ判断
    ' （AccountProfileRules.Excerpt に渡す前に落とす）。
    '
    ' 【「プロフィールを見る」は、いつも出す】自己紹介が空でも・非公開でも、
    ' プロフィールのページには基本情報が出うるため。
    ' **アイコンと名前も、いつも出す**（画像が無ければ灰色の丸。公開のプロフィールと同じ理由＝
    ' 置かないと、枠の形が人によって変わる）
    Private Shared Function ProfileHtml(accountId As Integer, userName As String, displayName As String,
                                        profile As AccountProfileStore.ProfileRecord) As String

        Dim sb As New Text.StringBuilder("<div class=""blog-side-profile"">")

        sb.Append("<h3>プロフィール</h3>")

        sb.Append("<div class=""blog-side-head"">")

        ' 【アイコンは共通の部品】写真が無ければ「色の付いた丸に1文字」（Accounts/AccountAvatar.vb）。
        ' alt は空（すぐ隣に名前が文字で出ている＝読み上げソフトが2回言わないように）
        sb.Append(AccountAvatar.Html(
            AccountAvatar.Resolve(accountId, userName, displayName,
                                  If(profile Is Nothing, Nothing, profile.AvatarImageName),
                                  If(profile Is Nothing, Nothing, profile.AvatarLetter),
                                  If(profile Is Nothing, Nothing, profile.AvatarColor)),
            "md", "blog-side-avatar"))

        sb.Append("<p class=""blog-side-name"">")
        sb.Append(HttpUtility.HtmlEncode(If(displayName, ""))).Append("</p>")

        sb.Append("</div>")

        ' 自己紹介の出だし
        If profile IsNot Nothing AndAlso
           Not ProfileChoices.IsPrivate(profile.IntroductionVisibility) Then

            Dim intro As String = AccountProfileRules.Excerpt(profile.Introduction, IntroExcerptLength)

            If intro.Length > 0 Then
                sb.Append("<p class=""blog-side-intro"">").Append(HttpUtility.HtmlEncode(intro)).Append("</p>")
            End If

        End If

        ' 【行き先は AccountProfileRules.PublicUrl の1か所】文字列を繋がないこと
        sb.Append("<p class=""blog-side-more""><a href=""")
        sb.Append(HttpUtility.HtmlAttributeEncode(AccountProfileRules.PublicUrl(userName)))
        sb.Append(""">プロフィールを見る</a></p>")

        sb.Append("</div>")

        Return sb.ToString()

    End Function


    ' ============================================================
    ' ② その人の最新の記事
    ' ============================================================
    '
    ' 【1本も無ければ、見出しごと出さない】記事が0本のブログは、ふつう誰も見に来ない
    ' （見に来るのは持ち主だけで、その人は自分に記事が無いことを知っている）。
    ' **（前のサイト全体の更新履歴は「まだ記事はありません」と出していた。わざと違えてあった）**
    ' ——あちらはサイトのトップに出るので、空っぽの枠より文が在ったほうがよい。
    '
    ' 【見た目は、前のサイト全体の更新履歴と同じ形】（.history-area。更新履歴はやめたが、この形はここで使っている）
    ' 右の枠の同じ場所に出るので、CSS を使い回している
    Private Shared Function RecentHtml(userName As String,
                                       recent As List(Of ArticleStore.ArticleRecord)) As String

        If recent Is Nothing OrElse recent.Count = 0 Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder("<div class=""history-area""><h3>最新の記事</h3><ul>")

        For Each a As ArticleStore.ArticleRecord In recent

            sb.Append("<li>")

            ' 【日付の形は ArticleDisplay に任せる】記事のページ・更新履歴と1文字も違わない形にするため
            sb.Append(ArticleDisplay.TimeHtml(a.UpdatedAt, "history-date"))

            sb.Append("<a href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(ArticleRules.PageUrl(userName, a.ArticleId, a.Slug)))
            sb.Append(""">").Append(HttpUtility.HtmlEncode(ArticleRules.DisplayTitle(a.Title))).Append("</a>")

            sb.Append("</li>")

        Next

        sb.Append("</ul>")

        ' 【すべて見る】（基本ルール T）行き先は、その人のブログのトップ（新しい順・ページ送り）
        sb.Append(SiteMaster.SidebarMoreLink(ArticleRules.AccountUrl(userName), "記事をすべて見る"))
        sb.Append("</div>")

        Return sb.ToString()

    End Function


    ' ============================================================
    ' ③ カテゴリ
    ' ============================================================
    '
    ' 【公開記事のあるカテゴリだけ】数も、公開記事の数だけを出す
    ' （下書きの数を外に出さない。ブログのトップと同じ決まり）。
    '
    ' 【1つも無ければ、見出しごと出さない】カテゴリを1つも作っていない人が居るため
    Private Shared Function CategoriesHtml(userName As String,
                                           categories As List(Of ArticleCategoryStore.CategoryRecord),
                                           counts As Dictionary(Of Integer, Integer)) As String

        If categories Is Nothing OrElse counts Is Nothing Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder()

        For Each c As ArticleCategoryStore.CategoryRecord In categories

            Dim count As Integer = 0

            If Not counts.TryGetValue(c.CategoryId, count) OrElse count = 0 Then
                Continue For
            End If

            sb.Append("<li><a href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(ArticleRules.CategoryUrl(userName, c.CategoryId)))
            sb.Append(""">").Append(HttpUtility.HtmlEncode(c.Name)).Append("</a>")

            ' 【数は、読み上げソフトにも意味が通る形で出す】
            ' 「(3)」だけだと、耳では何の数か分からない
            sb.Append("<span class=""blog-side-count"">")
            sb.Append("<span class=""visually-hidden"">記事 </span>")
            sb.Append(count.ToString(Globalization.CultureInfo.InvariantCulture))
            sb.Append("<span class=""visually-hidden""> 件</span>")
            sb.Append("</span></li>")

        Next

        If sb.Length = 0 Then
            Return ""
        End If

        Return "<div class=""blog-side-categories""><h3>カテゴリ</h3><ul>" & sb.ToString() & "</ul></div>"

    End Function

End Class
