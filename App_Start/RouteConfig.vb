Imports System.Web.Routing

' URLの定義は、この MapPageRoute だけで行っている。
'
' 【FriendlyUrls は使っていない（参照ごと外した）】
' Visual Studio のテンプレートが Microsoft.AspNet.FriendlyUrls を入れており、
' このファイルにも Imports だけが残っていたが、routes.EnableFriendlyUrls() は
' 一度も呼ばれていなかった＝機能そのものが動いていなかった。
' 実測でも /About は404で、/About.aspx だけが表示された
' （FriendlyUrls が効いていれば、拡張子なしのURLが解決される）。
' 実際に API を使っていたのは ViewSwitcher.ascx だけで、そちらも同日に削除した。
'
' 【注意】「chat/lounge・chat/room に末尾スラッシュを付けない」という決まりは、
' FriendlyUrls とは無関係に、この MapPageRoute の挙動から来ている。
' FriendlyUrls を外したことでは変わらないので、決まりはそのまま守ること
Public Module RouteConfig
    Sub RegisterRoutes(ByVal routes As RouteCollection)

        routes.MapPageRoute("ArticleDetail", "articles/{user}/{id}", "~/Articles/ArticleDetail.aspx")

        ' アカウントのトップページとカテゴリの一覧
        '
        ' 【2区切り（articles/{user}）なので、下の AccountHome のコメントの罠（403.14）には当たらない】
        ' 罠は「URL の1区切り目と同じ名前のフォルダ」で起きる。/articles だけの1区切りのルートは、今も作らないこと
        ' （Articles\ フォルダが在るので、/articles は 403 になる）。
        ' ユーザー名に「.」は使えないので、Articles\Manage.aspx のような実在のファイルとも重ならない。
        '
        ' 【category は記事の「URL の名前」に使えない】（ArticleRules.ValidateSlug）ので、
        ' /articles/{user}/category は記事と取り違えない（区切りの数も違う）
        routes.MapPageRoute("ArticleAccountTop", "articles/{user}", "~/Articles/AccountTop.aspx")
        routes.MapPageRoute("ArticleCategoryList", "articles/{user}/category/{categoryId}", "~/Articles/CategoryList.aspx")

        ' ============================================
        ' 問い合わせ
        ' ============================================
        '
        ' 【罠】URLの1つ目の区切りと、同じ名前のフォルダを作らないこと（下の AccountHome のコメント）
        '
        ' "contact" は1区切りだけのルートなので、**contact\ というフォルダを作ると 403.14 になる**
        ' （IIS が「フォルダだ」と判断して、一覧を拒む）。
        ' そこで、置き場所のフォルダは Contacts\（複数形）にしてある。URLは contact のまま——
        ' Accounts\ と /account の関係と、まったく同じ形である。
        '
        ' 【末尾にスラッシュを付けない】ほかのルートと同じ決まり（「URL構成」）
        routes.MapPageRoute("Contact", "contact", "~/Contacts/Contact.aspx")

        routes.MapPageRoute("Lounge", "chat/lounge", "~/Chat/Lounge.aspx")

        routes.MapPageRoute("ChatRoom", "chat/room", "~/Chat/ChatRoom.aspx")

        ' ログアウト（Cookie・Session を捨てて、トップページへ返すだけの受け口）。
        '
        ' 【ルーティングしてある理由】
        ' C#／MVC へ移すときに .aspx は捨てるが、URLは変えたくない。
        ' 物理パス（/Chat/Logout.aspx）をラウンジのボタンに書いてしまうと、
        ' 移った日に外向きのURLごと変わる
        routes.MapPageRoute("Logout", "chat/logout", "~/Chat/Logout.aspx")

        ' こっそりの画像の取り出し口（Eの回で追加）
        '
        '     /chat/private-image?n=2026/09/10/(32桁).jpg
        '
        ' 【ルーティングしてある理由】
        ' このURLは <img> の src に出るので、**利用者の目に触れる**
        ' （右クリックで画像のアドレスをコピーできる）。
        ' 物理パス（/Chat/PrivateImage.aspx）がそのまま見えるより、こちらのほうがよい。
        ' C#／MVC へ移したときにURLが変わらない、という理由もログアウトと同じ。
        '
        ' 【アップロードの受け口（Chat/UploadImage.aspx）は、ルーティングしていない】
        ' あちらは画面のJavaScriptからしか呼ばれず、利用者の目に触れない。
        ' KeepAlive・LeaveBeacon と同じく、物理パスのまま呼ぶ
        '
        ' 【部屋あての画像は、ここを通らない】
        ' /Uploads/images/... の静的ファイルとして、IISがそのまま返す。
        ' 守る必要が無いものに、アプリを通す手間を払わない
        routes.MapPageRoute("PrivateImage", "chat/private-image", "~/Chat/PrivateImage.aspx")

        ' ============================================
        ' アカウント
        ' ============================================
        '
        ' 【なぜ Admin ではなく account なのか】
        ' 将来の一般ユーザーも、同じログインの仕組みを使う（アカウント機能は MVC版で作るが、
        ' 入れ物は今から同じ形にしておく、という決定）。
        ' 「管理人専用」という名前を付けると、一般ユーザーを足す日にURLごと変えることになる。
        '
        ' 【罠】URLの1つ目の区切りと、同じ名前のフォルダを作らないこと（踏んだ）
        '
        ' 最初、ページの置き場所を Account\ にして "account" をルートに登録したところ、
        ' /account が **403.14（ディレクトリの一覧は出せません）** になった。
        '   ・ASP.NET のルーティングは、既定では**実在するファイル・フォルダに当たるURLを扱わない**
        '     （RouteCollection.RouteExistingFiles が False）
        '   ・そのため /account はルートに入らず、IIS が「フォルダだ」と判断して
        '     /account/ へ転送し、一覧を拒んで403を返す
        ' **/chat も同じ理由で403になる**（Chat\ フォルダが在るため）。
        ' 今まで気づかなかったのは、1つの区切りだけのルート（"account" のような形）が
        ' 1本も無かったからである。
        '
        ' **そこで、置き場所のフォルダを Accounts\（複数形）にしてある。** URLは account のまま。
        '
        ' 【却下】routes.RouteExistingFiles = True にする
        ' 1行で直るが、**サイト全体のルーティングの振る舞いが変わる**。
        ' 以後ルートを1本足すたびに「実在するファイルを隠していないか」を確かめる必要が出る。
        ' フォルダの名前を1文字変えるほうが、影響の及ぶ範囲が小さい
        '
        ' 【末尾にスラッシュを付けないこと】
        ' chat/lounge・chat/room と同じ決まりにそろえてある（「URL構成」）。
        ' 今のアカウントのページはポストバックで動いていて WebMethod を1つも使っていないので、
        ' あの罠（末尾スラッシュ付きのURLから WebMethod を呼ぶと404）には当たらないが、
        ' **サイトの中で決まりを2つにしない**
        routes.MapPageRoute("AccountHome", "account", "~/Accounts/Home.aspx")
        routes.MapPageRoute("AccountLogin", "account/login", "~/Accounts/Login.aspx")
        routes.MapPageRoute("AccountLogout", "account/logout", "~/Accounts/Logout.aspx")

        ' アカウント設定（表示名・メールアドレス・パスワード）
        routes.MapPageRoute("AccountSettings", "account/settings", "~/Accounts/Settings.aspx")

        ' プロフィールの編集（ブログ型の①）
        '
        ' 【画像の受け口（Accounts/UploadProfileImage.aspx）は、ルーティングしていない】
        ' あちらは画面の JavaScript からしか呼ばれず、利用者の目に触れない
        ' （Chat/UploadImage.aspx・Articles/UploadImage.aspx と同じ扱い）。
        ' 置かれた画像そのものは /Uploads/profiles/... の静的ファイルとして、IIS がそのまま返す
        routes.MapPageRoute("AccountProfile", "account/profile", "~/Accounts/Profile.aspx")

        ' 公開のプロフィールページ（ブログ型の②）
        '
        ' 【なぜ /profile/{user} なのか】3つ考えて、これにした。
        '   ・/articles/{user}/profile … **記事の「URL の名前」に profile を付けた記事とぶつかる**。
        '     予約語（ArticleRules.ReservedSlugs）を1つ増やせば避けられるが、
        '     **プロフィールは記事の下ではない**（ブログと、将来のアバターが、プロフィールにぶら下がる）
        '   ・/{user} … いちばん短いが、contact・account・chat・articles と同じ場所に人の名前が並ぶ。
        '     以後、1区切りの URL を足すたびに「その名前の人が居たらどうなるか」を考えることになる
        '   ・**/profile/{user}**（これ）… **人が2区切り目に来る形が、/articles/{user} とそろう**。
        '     将来 /pig/{user} のようなものが増えても、同じ形で並ぶ。MVC版でもそのまま使える
        '
        ' 【403.14 の罠には当たらない】2区切りなので、実在するフォルダとぶつからない
        ' （「URL構成」）。**ただし「profile」という名前のフォルダを作らないこと**——
        ' 作ると /profile（1区切り）を足せなくなる。今は1区切りのルートを足す予定が無い
        '
        ' 【ページは Accounts\ に置いてある】プロフィールのコード（AccountProfileStore・
        ' AccountProfileRules・AccountProfileImages）が全部あそこにあるため。
        ' **URL と置き場所は一致していなくてよい**（記事の管理画面が /account/articles なのと同じ）
        routes.MapPageRoute("PublicProfile", "profile/{user}", "~/Accounts/PublicProfile.aspx")

        ' 一般ユーザーの登録。register＝申し込み、verify＝確認のメールのリンクの行き先
        '
        ' 【verify は、ログインしていない人が開く画面である】
        ' リンクはメールから開かれるので、その端末ではまずログインしていない。
        ' **AccountPageBase を継承させないこと**（継承すると、ログインの画面へ返されて、
        ' 確認のリンクが永久に使えなくなる）。Login.aspx・Forgot.aspx・Reset.aspx と同じ扱いである
        routes.MapPageRoute("AccountRegister", "account/register", "~/Accounts/Register.aspx")
        routes.MapPageRoute("AccountVerify", "account/verify", "~/Accounts/Verify.aspx")

        ' パスワードを忘れたとき。forgot＝依頼、reset＝メールのリンクの行き先、
        ' recover＝非常口（ふだんは404。App_Data の合言葉のファイルが在る間だけ開く）
        routes.MapPageRoute("AccountForgot", "account/forgot", "~/Accounts/Forgot.aspx")
        routes.MapPageRoute("AccountReset", "account/reset", "~/Accounts/Reset.aspx")
        routes.MapPageRoute("AccountRecover", "account/recover", "~/Accounts/Recover.aspx")

        ' 最初の管理人を作る画面。
        ' **ふだんは404である**（アカウントが0件で、かつ合言葉のファイルが在るときだけ開く）。
        ' ルーティングしてあるのは、ほかのページと同じくURLを変えずに MVC版へ移すため
        routes.MapPageRoute("AccountSetup", "account/setup", "~/Accounts/Setup.aspx")

        ' ============================================
        ' 記事
        ' ============================================
        '
        ' 【URLは account/…、置き場所は Articles\】
        ' URLは「ログインの向こう側」、置き場所は「機能ごと」（Feature Folders）で決めている。
        ' 作る公開のページ（articles/{user}/{id}）と同じフォルダに置いてあるので、
        ' 記事に関わるものは1か所で見渡せる。
        '
        ' 【1つ目の区切りが account なので、403 の罠には当たらない】
        ' 罠に当たるのは「実在するフォルダと同じ名前の、1区切りのURL」だけである
        ' （上の AccountHome のコメント）。account\ というフォルダは無い（Accounts\ である）。
        ' **articles だけの1区切りのルートを足すなら、Articles\ フォルダと同じ名前になるので、
        '   そのときは罠に当たる**（「アカウントのトップページ」を作るときに注意すること）
        '
        ' 【末尾にスラッシュを付けないこと】ほかのルートと同じ決まり。
        ' 編集の画面は ?id=12 のクエリ文字列で記事を指す（新規は番号なし）
        routes.MapPageRoute("ArticleManage", "account/articles", "~/Articles/Manage.aspx")
        routes.MapPageRoute("ArticleEdit", "account/articles/edit", "~/Articles/Edit.aspx")
        routes.MapPageRoute("ArticleCategories", "account/categories", "~/Articles/Categories.aspx")

        ' 【ブログの設定】ブログのタイトルと説明（プロフィールの画面から移した）。
        ' 【URL は変えずに足しただけ】記事一覧（/account/articles）・カテゴリ（/account/categories）は、管理メニューの名前を
        ' 「ブログ」にしても URL はそのまま（開いたままのタブ・ブックマーク・確かめの道具を壊さないため。画面の言葉だけを変えた）。
        ' 新しく作るこの画面だけ「ブログの中」の形の URL にした。並べ方の行き先は Articles/BlogTabs.vb の3つの定数
        routes.MapPageRoute("BlogSettings", "account/blog/settings", "~/Articles/BlogSettings.aspx")

        ' ============================================
        ' 管理人からのお知らせ（ブログ型の⑤）
        ' ============================================
        '
        ' 【置き場所は Notices\（新しいフォルダ）】お知らせは記事ではない
        ' （理由は Notices/SiteNoticeRules.vb の先頭）。
        ' 【URL は2区切りなので、403.14 の罠には当たらない】
        ' **notices だけの1区切りのルートを足すなら、そのときは罠に当たる**
        ' （Notices\ フォルダと同じ名前になる）。
        ' 【サイト管理者だけの画面】守りは Notices/Manage.aspx.vb の RequireAdministrator
        routes.MapPageRoute("SiteNotices", "account/notices", "~/Notices/Manage.aspx")

        ' お知らせを全部並べる公開のページ（基本ルール T）。
        ' 【URL は「news」】1区切りの「notices」は、Notices\ フォルダと同じ名前になって 403.14（上の罠）
        routes.MapPageRoute("SiteNoticeList", "news", "~/Notices/NoticeList.aspx")

        ' サイトの表示の設定（右の枠に出す数。サイト管理者だけ）
        routes.MapPageRoute("SiteDisplay", "account/display", "~/Accounts/SiteDisplay.aspx")

        ' ============================================
        ' 自分が参加したチャット履歴（チャットとアカウントの紐づけ）
        ' ============================================
        '
        ' 【置き場所は Chat\】読むのがチャットのログ（ChatMessages）なので。URL は「ログインの向こう側」の /account の下。
        ' URL と置き場所は、一致していなくてよい（記事の管理画面が Articles\ に在るのと同じ）。
        ' 【2区切り・3区切りなので、403.14 の罠には当たらない】
        ' 【ログインが要る】どちらも AccountPageBase を継承している。誰の記録かは、ログインしているアカウントで決まる
        ' （一覧は番号を受け取らない。中身は ?s= の番号を、Chat/AccountChatLog.vb の FindSession が持ち主かどうか確かめる）
        routes.MapPageRoute("AccountChatLogs", "account/chatlogs", "~/Chat/MyChatLogs.aspx")
        routes.MapPageRoute("AccountChatLogView", "account/chatlogs/view", "~/Chat/MyChatLogView.aspx")

        ' ============================================
        ' ログを調べる道具（サイト管理者だけ）
        ' ============================================
        '
        ' 通報や警察からの依頼があったときに、部屋のログを探す・再生する・CSV に書き出す。
        ' 【置き場所は Admin\】サイトを運営するための道具なので。URL は管理画面の /account の下（ログインの画面から戻ってこられる）。
        ' 【2区切り・3区切りなので、403.14 の罠には当たらない】
        ' 【守り】3枚とも Admin/LogToolPageBase.vb を継承（サイト管理者だけ・no-store）
        routes.MapPageRoute("AdminLogSearch", "account/logs", "~/Admin/LogSearch.aspx")
        routes.MapPageRoute("AdminLogReplay", "account/logs/view", "~/Admin/LogReplay.aspx")
        routes.MapPageRoute("AdminLogExport", "account/logs/csv", "~/Admin/LogExport.aspx")
        ' 見た記録（探した・開いた・書き出したことの一覧）
        routes.MapPageRoute("AdminLogAudit", "account/logs/audit", "~/Admin/LogAudit.aspx")

    End Sub
End Module
