-- ============================================
-- ArticleCategoriesテーブル（2026年9月21日、A-2a）
-- 記事のカテゴリ。1行＝カテゴリ1つ
--
-- 【カテゴリは、アカウントごとに持つ】
-- 将来、一般ユーザーが各自のブログを持つため。
-- サイト共通のカテゴリにすると、一般ユーザーを足した日に
-- 「他人のカテゴリが自分の記事の選択肢に出る」ことになり、作り直しになる。
--
-- 【流す順番】Accounts.sql のあと、Articles.sql の前
--   ・AccountId が外部キーで Accounts を指す
--   ・Articles.CategoryId が外部キーでこの表を指す
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ArticleCategories', N'U') IS NULL
BEGIN

    CREATE TABLE ArticleCategories (

        CategoryId INT IDENTITY(1,1) NOT NULL,

        -- どのアカウントのカテゴリか
        AccountId INT NOT NULL,

        -- カテゴリの名前（日本語でよい）
        --
        -- 【同じアカウントの中では重複させない】下の一意索引。
        -- カテゴリは一覧のメニューに並ぶので、同じ名前が2つあると利用者が選び分けられない。
        -- （部屋名は重複を許しているが、あちらは「今そこに居る人」で見分けられる。
        --   集計メールで同じ部屋名が並んで見分けられない、という不便は実際に出ている）
        Name NVARCHAR(50) NOT NULL,

        -- このカテゴリの記事を、どの順で並べるか
        --
        --   10 … 指定した順（Articles.SortOrder。番号の無い記事は、番号付きのあとに日付順）
        --   20 … 新しい順（Articles.FirstPublishedAt の降順）
        --   30 … 古い順　（同じく昇順）
        --
        -- 【真偽値の組み合わせにしない】
        -- 「指定した順か」「新しい順か」の2つの列にすると、両方立った行・両方とも寝た行が作れてしまう。
        -- **3択は3択として、名前の付いた1つの列で持つ。** Articles.Status と同じ考え方である。
        --
        -- 【10刻みで採番してある】あとから「見た数の多い順」などを間に差し込めるように
        -- （ChatMessages.MessageType と同じ決まり）
        SortMode TINYINT NOT NULL CONSTRAINT DF_ArticleCategories_SortMode DEFAULT 10,

        -- カテゴリ自体の並び順（小さいほど先。同じ数ならCategoryId順）
        --
        -- 【記事の並び順（Articles.SortOrder）とは別物である】
        -- あちらは「カテゴリの中で、記事をどう並べるか」、こちらは「カテゴリをどう並べるか」。
        -- あちらは NULL を許す（＝番号なし）が、こちらは必ず値が入る
        -- ――カテゴリは数が少なく、「番号なし」を作る意味が無いため
        SortOrder INT NOT NULL CONSTRAINT DF_ArticleCategories_SortOrder DEFAULT 0,

        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ArticleCategories_CreatedAt DEFAULT SYSDATETIME(),
        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ArticleCategories_UpdatedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_ArticleCategories PRIMARY KEY (CategoryId),

        CONSTRAINT FK_ArticleCategories_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId),

        -- 【DBの側でも、3択の外の値を止める】
        -- コード（Articles/ArticleRules.vb）でも丸めているが、
        -- SSMS から手で入れた値まではコードが守れない。
        -- 知らない値が入ると、並べ方が黙って「指定した順」に落ちるだけで、誰も気づけない
        CONSTRAINT CK_ArticleCategories_SortMode CHECK (SortMode IN (10, 20, 30))

    );

    -- 同じアカウントの中で、名前を重複させない（上の Name のコメント）
    CREATE UNIQUE INDEX UX_ArticleCategories_Account_Name
        ON ArticleCategories (AccountId, Name);

    -- 「このアカウントのカテゴリを、並び順で出す」ための索引
    CREATE INDEX IX_ArticleCategories_Account_Sort
        ON ArticleCategories (AccountId, SortOrder, CategoryId);

END
