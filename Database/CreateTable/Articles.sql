-- ============================================
-- Articlesテーブル（2026年9月21日、A-2a）
-- 記事。1行＝記事1つ
--
-- 【VB版で書くのは、管理人だけ】
-- 一般ユーザーの投稿は MVC版（2世代目）で作る。それでも入れ物は将来と同じ形にしてある
-- （AccountId を持ち、カテゴリもアカウントごと）。
--
-- 【流す順番】Accounts.sql → ArticleCategories.sql → この表
--   AccountId と CategoryId が、外部キーでそれぞれを指している
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'Articles', N'U') IS NULL
BEGIN

    CREATE TABLE Articles (

        ArticleId INT IDENTITY(1,1) NOT NULL,

        -- どのアカウントの記事か
        --
        -- 【記事のURL（/articles/{user}/{id}）の {user} は、ここから引く】
        -- Accounts.UserName を焼き込まないのは、ユーザー名を変えたときに
        -- 記事のほうだけ古い名前で残らないようにするため。
        -- **ログの「ニックネームを焼き込む」とは、意図して逆にしてある**
        -- ――あちらは「その時どう名乗っていたか」が意味を持つ記録、
        -- こちらは「今このアカウントのものである」という所属だからである
        AccountId INT NOT NULL,

        -- どのカテゴリか（NULL＝カテゴリなし）
        --
        -- 【NULL を許す理由】カテゴリを1つも作らないまま記事を書けるようにするため。
        -- カテゴリを必須にすると、最初の記事を書く前にカテゴリを作らされる
        CategoryId INT NULL,

        -- 題
        --
        -- 【本文ではないので、出口でエスケープする】（サイト全体の決まり）
        -- 生のHTMLで出してよいのは BodyHtml だけである
        Title NVARCHAR(200) NOT NULL,

        -- 本文（HTML）
        --
        -- 【この列だけは、出口で生のHTMLとして出す】
        -- 見たまま編集（A-2b の Quill）の結果がHTMLなので、エスケープすると記事にならない。
        -- **唯一の砦は、保存するときに通す HtmlSanitizer（Articles/ArticleHtml.vb）である。**
        -- 保存を通る道を増やしたら、そこにも必ず通すこと。
        --
        -- 【NVARCHAR(MAX)】長さの上限をDBで決めない。
        -- 実際の上限は Articles/ArticleRules.vb が持つ（文字数で数える）
        BodyHtml NVARCHAR(MAX) NOT NULL,

        -- 状態
        --
        --   10 … 下書き（自分だけ見られる）
        --   20 … 公開　（誰でも見られる）
        --   30 … 非公開（公開したものを、消さずに引っ込める）
        --
        -- 【真偽値2つの組み合わせにしない】
        -- 「公開か」「下書きか」の2つの列にすると、両方立った行・両方とも寝た行が作れてしまう。
        -- **「書きかけ」と「引っ込めた」は別物**なので、3つの状態として1つの列で持つ。
        --
        -- 【10刻みで採番してある】あとから「予約投稿」などを間に差し込めるように
        -- （ChatMessages.MessageType と同じ決まり）。
        -- ただし**予約投稿は入れないと決めてある**（要るようになったら列を1本足す）
        Status TINYINT NOT NULL CONSTRAINT DF_Articles_Status DEFAULT 10,

        -- カテゴリの中での並び順（「指定した順」のときだけ使う。NULL＝番号なし）
        --
        -- 【NULL を許す】「指定した順」で番号の無い記事は、番号付きのあとに日付順で並べる
        -- 0 を「番号なし」にすると、本当に0番を指定したい人が困る
        SortOrder INT NULL,

        -- 固定表示（アカウントのトップページに固定で出す）の順（NULL＝固定しない）
        --
        -- 【カテゴリとは別の軸である】
        -- どのカテゴリに入れた記事でも、固定表示にできる。
        --
        -- 【IsPinned（真偽値）と PinOrder の2列にしなかった】（2026年9月21日）
        -- 2列にすると「立っているのに順番が無い」「寝ているのに順番だけ在る」という、
        -- 食い違った行が作れてしまう。**1つの列なら、その食い違いが構造として起きない。**
        -- すぐ上の Status を「真偽値2つの組み合わせにしない」と決めたのと、まったく同じ理由である。
        -- 失うのは「固定を外しても、前の順番を覚えておく」ことだけで、
        -- 固定する記事は数個なので、付け直すほうが安い。
        --
        -- 【「公開の記事だけ固定表示に出す」は、ここでは縛らない】
        -- 下書きに固定の番号が付いていてもよく、出すときに Status で絞る（A-3）。
        -- 「公開してから固定する」順にしか操作できない形にすると、かえって使いにくい
        PinOrder INT NULL,

        -- 記事の「URL の名前」（2026年9月22日、A-3b。/articles/{user}/guide のように引く）
        -- 英小文字で始まり、英小文字・数字・ハイフンだけ。空（NULL）なら番号の URL のまま。
        -- 【同じアカウントの中で重複させないが、一意の索引は張っていない】理由：NULL の行が何本もあるので、ふつうの一意の索引は使えない。フィルター付き索引にすると、
        -- 手で流す SQL に SET QUOTED_IDENTIFIER ON が要るようになる。重複させない守りは、保存の文の中（Articles/ArticleStore.vb の Save）に置いてある
        Slug NVARCHAR(40) NULL,

        -- 記事の「目次」の3択（2026年9月28日）
        --
        --   10 … 入れない（既定）
        --   20 … 入れる（開いた状態で）
        --   30 … 入れる（閉じた状態で）
        --
        -- 【真偽値2つの組み合わせにしない】すぐ上の Status・PinOrder と、まったく同じ理由である。
        -- 「入れるか」と「開いているか」を2列にすると、**「入れない＋開く」という意味の無い行**が作れてしまう。
        --
        -- 【目次そのものは BodyHtml に書き込まない】出すたびに、その時点の H2・H3 から組み立てる
        -- （Articles/ArticleToc.vb）。見出しを直せば目次もついてくるし、
        -- 目次の形を変えたい日に、既にある記事を書き直さずに済む
        TocMode TINYINT NOT NULL CONSTRAINT DF_Articles_TocMode DEFAULT 10,

        -- 見出しの番号の2択（2026年9月28日）
        --
        --   10 … 付けない（既定）
        --   20 … 付ける（本文の見出しと目次の両方に。H2＝「1.」、H3＝「1-1」）
        --
        -- 【目次の3択（TocMode）とは別の列】目次を入れずに番号だけ振る記事もありうるため。
        -- 1つの欄に2つの意味を持たせない
        -- 【番号は BodyHtml に書き込まない】表示のときに CSS のカウンターで出す
        HeadingNumberMode TINYINT NOT NULL CONSTRAINT DF_Articles_HeadingNumberMode DEFAULT 10,

        -- 【ゴミ箱】（3列）
        DeletedAt DATETIME2(3) NULL,
        TrashedStatus TINYINT NULL,
        TrashedSlug NVARCHAR(40) NULL,

        -- 最初に公開した日（まだ一度も公開していなければ NULL）
        --
        -- 【カテゴリの「新しい順／古い順」は、この列で並べる】
        -- **下書き→公開になった瞬間に一度だけ入れて、以後は書き換えない。**
        -- そのため、非公開にして戻しても、順番は元の位置のままになる。
        --
        -- 【却下した2案】
        --   ・作成日（CreatedAt）… 下書きで寝かせた記事が、公開した日にはもう古い扱いになる
        --   ・更新日（UpdatedAt）… 誤字を1文字直しただけで、記事の順番が入れ替わる
        FirstPublishedAt DATETIME2(3) NULL,

        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Articles_CreatedAt DEFAULT SYSDATETIME(),

        -- 最後に保存した時刻（記録と、編集画面の表示のため。並べ方には使わない）
        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Articles_UpdatedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_Articles PRIMARY KEY (ArticleId),

        CONSTRAINT FK_Articles_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId),

        -- 【カテゴリを消すときは、先に記事のカテゴリを外すこと】
        -- ON DELETE SET NULL にしていないのは、**黙って外れるより、止まって気づくほうがよい**ため。
        -- 外す処理は Articles/ArticleCategoryStore.vb の Delete が、同じトランザクションでやっている
        CONSTRAINT FK_Articles_Categories
            FOREIGN KEY (CategoryId) REFERENCES ArticleCategories (CategoryId),

        -- 【DBの側でも、3択の外の値を止める】理由は ArticleCategories.SortMode と同じ
        CONSTRAINT CK_Articles_Status CHECK (Status IN (10, 20, 30)),

        -- 目次の3択も、同じくDBの側で止める
        CONSTRAINT CK_Articles_TocMode CHECK (TocMode IN (10, 20, 30)),

        -- 見出しの番号の2択も、同じくDBの側で止める
        CONSTRAINT CK_Articles_HeadingNumberMode CHECK (HeadingNumberMode IN (10, 20)),
        CONSTRAINT CK_Articles_TrashedStatus CHECK (TrashedStatus IS NULL OR TrashedStatus IN (10, 20, 30)),

        -- 【公開なら、最初に公開した日が必ず入っている】
        -- ここを守っておかないと、A-3 の「新しい順／古い順」に NULL が混ざって、
        -- **並び順が環境任せになる**。入れ忘れをコードだけで守らない
        CONSTRAINT CK_Articles_FirstPublished
            CHECK (Status <> 20 OR FirstPublishedAt IS NOT NULL)

    );

    -- 管理画面の一覧（このアカウントの記事を、新しい順に出す）
    CREATE INDEX IX_Articles_Account_Status ON Articles (AccountId, Status, ArticleId);

    -- カテゴリの一覧（A-3。3つの並べ方は、どれもこの索引の列で並ぶ）
    CREATE INDEX IX_Articles_Category_Sort
        ON Articles (CategoryId, SortOrder, FirstPublishedAt, ArticleId);

    -- 固定表示（A-3。アカウントのトップページ）
    --
    -- 【フィルター付き索引（WHERE PinOrder IS NOT NULL）にしていない】
    -- そうすると、この表への INSERT／UPDATE／DELETE を sqlcmd から手で流すときに
    -- SET QUOTED_IDENTIFIER ON が要るようになる（ChatMessages.ImageName で踏んだ罠）。
    -- 固定表示の記事は数個なので、絞り込みで得られるものより、罠を1つ増やさないほうが大きい
    CREATE INDEX IX_Articles_Account_Pin ON Articles (AccountId, PinOrder, ArticleId);

    -- 名前で引く（A-3b。一意ではない＝重複させない守りは Articles/ArticleStore.vb の保存の文の中）
    CREATE INDEX IX_Articles_Account_Slug ON Articles (AccountId, Slug);

END
