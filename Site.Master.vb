Public Class SiteMaster
    Inherits MasterPage

    ' === マークアップ（Site.Master）から参照する値 ===
    '
    ' 【なぜ、マークアップの中で SiteSettings を直接呼ばないのか】
    ' マスターページのインラインコード（<%: %>）は、コードビハインドとは別の
    ' コンパイル単位になり、プロジェクトのルート名前空間（vb_chat）が
    ' 自動では効かない。そのまま書くと
    ' 「'SiteSettings' is not declared」で**ページが500になる**
    ' （実際に踏んだ）。
    '
    ' vb_chat.SiteSettings と完全な名前で書くか、<%@ Import %> を足せば動くが、
    ' ここではプロパティとして出す形を採った。マークアップにロジックを書かないという
    ' 方針（「ロジックは必ず別クラスへ出す」）にも沿う

    ' サイト名（ヘッダーのロゴの横に出る）
    Protected ReadOnly Property SiteName As String
        Get
            Return SiteSettings.SiteName
        End Get
    End Property

    ' 著作権表示の名前（フッターに出る）
    Protected ReadOnly Property CopyrightName As String
        Get
            Return SiteSettings.CopyrightName
        End Get
    End Property

    ' 一般の人向けのもの（右上のメニューとハンバーガー・右の枠の更新履歴）を出すか
    '
    '
    ' 【既定は出す】消すのは管理人の画面（Accounts/AdminMaster.Master）だけで、
    ' あちらが自分の Init で False にする。ほかのページは何もしなくてよい。
    '
    ' 【なぜ、別のマスターページを丸ごと作らなかったのか】
    ' ヘッダーのロゴ・フッター・<head> の中身（theme.css・Site.css・?v=・ビルドの目印）は、
    ' 管理人の画面でも同じである。丸ごと写すと、そこを直すたびに2か所になる
    ' （部屋の vbChatConfirm が写しになっているのと同じ負担を、わざわざ作ることになる）。
    ' 違うのは「一般向けの2か所を出さない」ことだけなので、スイッチ1つにした
    Public Property ShowPublicChrome As Boolean = True

    ' === 右上のメニューの1行 ===
    '
    ' 行き先は記事の「URL の名前」で決まる（Articles/SiteArticles.vb）。
    ' **その名前の記事が公開されていなければ、行ごと出さない**（押しても「見つかりません」になるリンクを置かない）
    Protected Function NavItemHtml(slug As String, text As String) As String

        Dim link As String = SiteArticles.LinkHtml(slug, text)

        If link.Length = 0 Then
            Return ""
        End If

        Return "<li>" & link & "</li>"

    End Function


    ' === 右上の角＝自分のアカウント ===
    '
    ' 理由：ページの最上部にアカウントが出ていれば、ログインしていると直感で分かる（Google などと同じ形）。
    ' それまでは、右上のメニューに「ブログ／管理画面／ログアウト」がほかの行と同じ並びで混ざっているだけで、
    ' **誰でログインしているかが、どこにも出ていなかった**。
    '
    '   ログインしている　 … 自分のアイコン（＋広い画面では表示名と ▼）。押すと小さなメニュー
    '                         （見出し＝アイコン・表示名・ロール、中身＝@ユーザー名・ブログ・管理画面（＋中のページを字下げで）・ログアウト）
    '   ログインしていない … 「ログイン」のボタンと「新規登録」（狭い画面では「新規登録」を出さない＝ログインの画面に「新規登録はこちら」がある）
    '
    ' 【開け閉めは Scripts/site.js の「12」】ボタンの aria-expanded・Esc・外を押す・閉じたらボタンへ戻す。
    ' **メニューの中身は、最初から HTML に入っている（hidden）**。【承知のうえ】site.js が届かないと開かない
    ' （ハンバーガーのメニューと同じ。ログインの要る画面は URL を打てば開ける）
    '
    ' 【DB は増やしていない】ログインの確かめ（AccountSession.Current）は、もともとこのヘッダーが呼んでいた（1回の要求で1回）。
    ' アイコン（写真・文字・色）は AccountAvatar.ForAccount の控え、ロールの名前は AccountSession.CurrentRoleName の控え。
    ' **ログインしていない人は、DB を1回も引かない**（AccountAuth の Cookie を持っていないので、Current が DB を見ずに戻る）
    '
    ' 【このページは、ブラウザに覚えさせない】ログインしている人のときだけ（Page_Load の SetNoStore）。理由はそこに書いた
    '
    ' 【戻すのは、組み立て済みの HTML】<%= %> で出す。名前・ユーザー名・ロールの名前は、ここでエスケープする
    Protected Function AccountCornerHtml() As String

        Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

        If account Is Nothing Then

            ' 【ログインの戻り先】（基本ルールC）ラウンジとトップから押したときだけ、そこへ戻す。
            ' 戻り先に許すのは「/account の下」とラウンジ・トップの完全一致だけ（オープンリダイレクト避け。Accounts/AccountReturnUrl.vb）なので、
            ' ほかの公開のページでは LoginUrlFor が戻り先を付けない（ログインの画面 → /account のまま）
            ' 【人の形の絵は、狭い画面でだけ出る】（Content/Site.css）。広い画面は「ログイン」の文字のボタン。
            ' 狭い画面は、ロゴと ☰ の横に文字のボタンが入りきらない（360px で 11px 足りなかった）ので、
            ' 絵の下に小さく「ログイン」と書く形にした（スマホのアプリの下のタブと同じ見せ方）。文字は消さない
            Return "<div class=""account-corner account-corner--guest"">" &
                   "<a class=""account-login-btn"" href=""" & HttpUtility.HtmlAttributeEncode(AccountReturnUrl.LoginUrlFor(Request.RawUrl)) & """>" &
                   "<svg class=""account-login-icon"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"">" &
                   "<circle cx=""12"" cy=""8"" r=""4"" fill=""none"" stroke=""currentColor"" stroke-width=""1.8""/>" &
                   "<path d=""M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"" fill=""none"" stroke=""currentColor"" stroke-width=""1.8"" stroke-linecap=""round""/></svg>" &
                   "<span class=""account-login-text"">ログイン</span></a>" &
                   "<a class=""account-register-link"" href=""/account/register"">新規登録</a>" &
                   "</div>"

        End If

        Dim look As AccountAvatar.Look = AccountAvatar.ForAccount(account)

        Dim name As String = HttpUtility.HtmlEncode(account.DisplayName)
        Dim user As String = HttpUtility.HtmlEncode(account.UserName)
        Dim role As String = HttpUtility.HtmlEncode(AccountSession.CurrentRoleName(Context))

        Dim sb As New Text.StringBuilder()

        sb.Append("<div class=""account-corner account-menu"">")

        ' --- ボタン ---
        ' 【名前は aria-label】「〇〇 のアカウントのメニュー」。中のアイコン・名前・▼は、この名前に置き換わる
        sb.Append("<button type=""button"" class=""account-menu-btn"" id=""accountMenuBtn""")
        sb.Append(" aria-expanded=""false"" aria-controls=""accountMenu""")
        sb.Append(" aria-label=""").Append(HttpUtility.HtmlAttributeEncode(account.DisplayName)).Append(" のアカウントのメニュー"">")
        sb.Append(AccountAvatar.Html(look, "sm"))
        sb.Append("<span class=""account-menu-name"">").Append(name).Append("</span>")
        sb.Append("<svg class=""account-menu-caret"" viewBox=""0 0 10 6"" aria-hidden=""true"" focusable=""false"">")
        sb.Append("<path d=""M1 1l4 4 4-4"" fill=""none"" stroke=""currentColor"" stroke-width=""1.6"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>")
        sb.Append("</button>")

        ' --- メニュー（hidden。開くと出る）---
        sb.Append("<div class=""account-menu-pop"" id=""accountMenu"" hidden=""hidden"">")

        ' 見出し：今だれでログインしているか（アイコン・表示名・ロール）
        sb.Append("<div class=""account-menu-head"">")
        sb.Append(AccountAvatar.Html(look, "md"))
        sb.Append("<div class=""account-menu-who"">")
        sb.Append("<p class=""account-menu-who-name"">").Append(name).Append("</p>")
        sb.Append("<p class=""account-menu-who-role"">").Append(role).Append("</p>")
        sb.Append("</div></div>")

        ' 中身。@ユーザー名＝公開のプロフィール（チャットのアカウントID と同じ見せ方・同じ行き先）
        sb.Append("<ul class=""account-menu-list"">")
        sb.Append("<li><a class=""account-menu-id"" href=""").Append(HttpUtility.HtmlAttributeEncode(AccountProfileRules.PublicUrl(account.UserName))).Append(""">")
        sb.Append("<span class=""visually-hidden"">プロフィール </span>@").Append(user).Append("</a></li>")
        ' 【「ブログを見る」】（「ブログ」から変えた）管理画面の中の項目が「記事」から「ブログ」になり、
        ' 同じメニューに「ブログ」が2つ並んで、どちらが書く画面か分からなくなったため。こちらは公開のブログのトップ
        sb.Append("<li><a href=""").Append(HttpUtility.HtmlAttributeEncode(ArticleRules.AccountUrl(account.UserName))).Append(""">ブログを見る</a></li>")

        ' 【管理画面と、その中のページ】（要望：チャット履歴だけでなく、全部か、もう少し多く出す）
        ' 「管理画面」の li の中に ul を入れ子にして、中の5つ（サイト管理者はお知らせも入れて6つ）を字下げして並べる。
        ' 入れ子のリストなので、読み上げソフトにも「管理画面の中の一覧」と伝わる（見出しや区切りの役の要素を足さなくてよい）。
        ' **項目の一覧と並びは Accounts/AdminMenu.vb の1か所**（管理画面の管理メニューと同じ）。ここに li を書き足さないこと。
        ' 今いる画面には、管理メニューと同じ aria-current を付ける
        Dim here As String = Page.AppRelativeVirtualPath

        sb.Append("<li class=""account-menu-group""><a href=""").Append(AdminMenu.Home.Url).Append("""")
        sb.Append(AdminMenu.CurrentMark(AdminMenu.Home, here)).Append(">管理画面</a>")
        sb.Append("<ul class=""account-menu-sub"">")

        For Each entry As AdminMenu.Item In AdminMenu.PagesFor(account)
            sb.Append(AdminMenu.LinkItemHtml(entry, here))
        Next

        sb.Append("</ul></li>")
        ' 【ログアウトは、区切り線の下】ページを移る項目ではなく「操作」なので、ほかと分ける
        ' 行き先は /account/logout（チャットの「チャットを出る」＝/chat/logout とは別物）
        sb.Append("<li class=""account-menu-logout""><a href=""/account/logout"">ログアウト</a></li>")
        sb.Append("</ul>")

        sb.Append("</div></div>")

        Return sb.ToString()

    End Function


    ' 右の枠の一覧の下の「すべて見る」（基本ルール T）。ブログの右の枠（Articles/BlogSidebar.vb）も同じ形
    Public Shared Function SidebarMoreLink(url As String, text As String) As String
        Return "<p class=""sidebar-more""><a href=""" & HttpUtility.HtmlAttributeEncode(url) & """>" & HttpUtility.HtmlEncode(text) & "</a></p>"
    End Function


    ' === 右の枠の「管理人からのお知らせ」（ブログ型の⑤） ===
    '
    ' 【1つも無ければ、見出しごと出さない】
    ' 更新履歴は「まだありません」と出すが、**お知らせは無いのがふつう**なので、
    ' わざわざ場所を取らせない（開発者に毎日お知らせを書かせる作りにはしない）。
    '
    ' 【中身の組み立ては Notices/SiteNotices.vb】毎回 DB を引かないよう、あちらがメモリに控えている。
    ' **ブログのページの右の枠（③）も、同じものを呼んでいる**（Articles/BlogSidebar.vb）
    ' 「新しいメンバー」の枠（中身と控えは Accounts/NewMembers.vb）
    '
    ' 【戻すのはエスケープ済みの HTML】<%= %> で出す。名前と画像の行き先は、あちらでエスケープしてある
    Protected Function NewMembersHtml() As String
        Return NewMembers.Html()
    End Function


    ' 【右の枠のお知らせを出さないページ】お知らせを全部並べるページ（/news。Notices/NoticeList.aspx.vb）では、
    ' 本文がお知らせの一覧そのものなので、右の枠（狭い画面では本文の下）に同じものをもう一度出さない。ほかの欄はそのまま
    Public Property HideSidebarNotices As Boolean

    Protected Function NoticeAreaHtml() As String

        If HideSidebarNotices Then
            Return ""
        End If

        Dim list As String = SiteNotices.ListHtml()

        If list.Length = 0 Then
            Return ""
        End If

        ' 【すべて見る】（基本ルール T）右の枠には決まった数だけ。全部は /news で
        Return "<div class=""notice-area""><h3>管理人からのお知らせ</h3>" & list &
               SidebarMoreLink("/news", "お知らせをすべて見る") & "</div>"

    End Function


    ' === 右の枠の中身を、ブログのページのものに差し替える（ブログ型の③） ===
    '
    ' 呼ぶのはブログの3ページ（記事のページ・ブログのトップ・カテゴリの一覧）の Page_Load。
    ' **中身は Articles/BlogSidebar.vb が組み立てる**（ここは入れ替えるだけ）。
    '
    ' 【サイト全体のぶん（更新履歴・お知らせ）は、出さなくなる】
    '＝ブログのページでは「その人の最新の記事」に回す。
    ' **1つの枠に両方出すと、縦に長くなりすぎる**（プロフィール＋最新の記事＋カテゴリ＋お知らせ＋袖の目次で、
    ' もう十分に長い）。
    '
    ' 【袖の目次は、ここには関係しない】あれは Scripts/article-toc.js が、
    ' 画面が出たあとに .right-sidebar のいちばん下へ足す。
    ' **どちらの中身を出していても、目次はその下に付く**
    '
    ' 【html は、呼ぶ側が組み立てたものをそのまま出す】
    ' 利用者の文字（自己紹介・記事のタイトル・カテゴリ名）は、
    ' **組み立てる側（BlogSidebar）がエスケープする決まり**にしてある
    Public Sub ShowBlogSidebar(html As String)

        BlogSidebarHtml.Text = html
        BlogSidebar.Visible = True
        SiteSidebar.Visible = False

    End Sub


    ' マークアップから、静的ファイルのURLを組み立てるときに呼ぶ
    '
    ' 中身は common.AssetUrl をそのまま呼ぶだけだが、ここを経由させている。
    ' 理由は上の SiteName と同じ——マスターページのインラインコードは
    ' コードビハインドとは別のコンパイル単位で、ルート名前空間（vb_chat）が
    ' 効かない。マークアップから common を直接呼ぶと
    ' 「'common' is not declared」でページが500になる
    ' （SiteSettings で実際に踏んだ罠と、同じもの）
    Protected Function Asset(ByVal virtualPath As String) As String
        Return common.AssetUrl(virtualPath)
    End Function

    ' DLLを見分ける印を、応答のヘッダー（X-VbChat-Build）として出す
    '
    ' 発行後の確かめの道具が、これと手元の bin\vb-chat.dll を突き合わせて、
    ' 「発行したDLLが本当に本番へ届いたか」を確かめる。
    '
    ' 【なぜ、マークアップ（Site.Master）に書かないのか】
    ' 書くと、**マークアップとDLLの両方が届かないとページが表示できなくなる。**
    ' 片方だけ発行された瞬間に「'BuildId' is not declared」で全ページが500になる——
    ' 発行漏れを見つけるために入れたものが、発行漏れでサイトを落とすことになる。
    '
    ' ヘッダーならDLLだけで完結する。古いDLLのときはヘッダーが出ないだけで、
    ' 発行後の確かめの道具が「目印が出ていない」と教えてくれる。**安全側に転ぶ**
    '
    ' 【<meta> を Page.Header.Controls.Add で足す形は、使えない】
    ' 実際に試して、全ページが500になった。
    '
    '     コントロールにコード ブロック (<% ... %>) が含まれているため、
    '     コントロールのコレクションを変更できません。
    '
    ' Site.Master の <head runat="server"> には <%: Page.Title %> と
    ' <%: Asset(...) %> が入っている。**コードブロックを含むコンテナは、
    ' 実行時に子を足せない**（ASP.NETの仕様）。
    ' あそこの <%: %> は消せないので、この道は塞がっている
    '
    ' 【部屋（Chat/ChatRoom.aspx）には出ていない】
    ' あちらは Site.Master を使わない独立ページのため。
    ' 確認にはトップとラウンジで足りるので、今は足していない
    ' （必要になったら、あちらの Page_Load に同じ1行を書けばよい）
    Private Sub AddBuildMarker()

        Try
            Response.AddHeader("X-VbChat-Build", common.BuildId())

        Catch
            ' 【何があっても画面は止めない】
            ' これは確認のための値であって、利用者には何の関係も無い
        End Try

    End Sub

    ' 【PreRender で当てている】
    ' 入れ子のマスターページ（AdminMaster）が値を決めるのは自分の Init なので、
    ' それより後ならどこでもよいが、ページの Load で誰かが変える余地も残しておく
    ' === 二重送信を防ぐ、使い捨ての目印（Core/FormOnce.vb）===
    '
    ' 【マークアップから FormOnce を直接呼ばない】マスターページのインラインコードは、
    ' コードビハインドとは別のコンパイル単位で、ビルドは通るのに実行すると500になることがある
    ' 。プロパティ経由にしてある
    Protected ReadOnly Property FormOnceFieldName As String
        Get
            Return FormOnce.FieldName
        End Get
    End Property

    ' 【描くたびに、新しい目印】読むたびに作るので、マークアップから読むのは1回だけにすること
    Protected ReadOnly Property FormOnceToken As String
        Get
            Return FormOnce.NewToken()
        End Get
    End Property


    Protected Sub Page_PreRender(ByVal sender As Object, ByVal e As EventArgs) Handles Me.PreRender

        PublicNav.Visible = ShowPublicChrome
        PublicSidebar.Visible = ShowPublicChrome

    End Sub

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        ' 【IsPostBack の外に置くこと】
        ' ポストバックでも印は出したい。中に入れると、
        ' 「ポストバックしたページだけ目印が消える」という分かりにくい形になる
        AddBuildMarker()

        ' === ログインしている人のページは、ブラウザ・途中の仕組みに覚えさせない ===
        '
        ' 右上の角に、その人の名前・ユーザー名・ロールが出るようになった。公開のページでも、**ログインしている人に返す1枚は
        ' 「その人だけのページ」**である。
        '   ・ASP.NET の既定は Cache-Control: private（途中の共用の仕組みは写しを取らない）。それでも、ブラウザには残る
        '     ＝共用のパソコンでログアウトしたあと、戻るボタンで前の人の名前の出たページが見える
        '   ・そこで、ログインの要る画面と同じ no-store にそろえる（基本ルールの7。PageResponse.SetNoStore の1か所を呼ぶだけ）
        ' **ログインしていない人の公開のページには付けない**（軽さのため。基本ルールの7のまま）。
        ' 【DB は増えない】AccountSession.Current は、ヘッダー（AccountCornerHtml）がどのみち呼ぶ（1回の要求で1回）
        If AccountSession.Current(Context) IsNot Nothing Then
            PageResponse.SetNoStore(Response)
        End If








        ' === ページのタイトルに、サイト名を足す ===
        '
        ' サイト名は SiteSettings（＝DBのSiteSettingsテーブル）が持つ
        ' （Web.config の appSettings から移した）
        '
        ' 【、If Not IsPostBack の外へ出した】
        ' それまでは中に入れていたため、**ポストバックするとタイトルからサイト名が消えていた**。
        ' 今まで誰も踏まなかったのは、ポストバックするページが1つも無かったからで、
        ' ログインの画面（Account/Login.aspx）が最初のポストバックするページになった。
        '
        ' 【二重に足さないための守り】
        ' Page.Title は、中身が HtmlTitle コントロールの ViewState に載りうる。
        ' そのまま足すと、ポストバックのたびに「ログイン - サイト名 - サイト名」と伸びかねない。
        ' **既に足してあるなら、何もしない**形にしてある（何度走らせても同じ結果になる）
        Dim siteName As String = SiteSettings.SiteName

        If String.IsNullOrEmpty(Page.Title) Then

            Page.Title = siteName

        ElseIf Page.Title <> siteName AndAlso
               Not Page.Title.EndsWith(" - " & siteName, StringComparison.Ordinal) Then

            Page.Title = Page.Title & " - " & siteName

        End If







    End Sub
End Class