-- ============================================
-- ArticleImageUsesテーブル（2026年9月27日。どの記事／控えが、どの画像を使っているか）
-- 1行＝「この記事（か控え）の本文に、この画像が出てくる」
--
-- 【何のためにあるか】
-- これがあると、1日1回の片付けが「どこからも使われていない画像」を**索引1本で引ける**。
-- それまでは、全記事・全控えの本文（BodyHtml）を丸ごと読んで突き合わせていた。
--
-- 【いつ書き換えるか】
-- 記事と控えを保存したとき（自動保存も含む）に、**本文の保存と同じトランザクション**で。
-- **差分だけ書く**（消えた名前を DELETE、増えた名前を INSERT）ので、本文の画像が変わっていなければ
-- 1行も書かれない。自動保存は打つのが止まって3秒ごとに走るため、ここが効く。
--
-- 【OwnerKind ＝ 本体か、控えか】
-- 記事1つに、本文は最大2つある（Articles.BodyHtml と ArticleDrafts.BodyHtml）。
-- 真偽値（IsDraft）にせず、名前の付いた10刻みの値にしてある（Articles.Status・ChatMessageType と同じ決まり）。
--   10 … 記事の本体（Articles）
--   20 … 編集中の控え（ArticleDrafts）
-- 30以降は、あとで増えたときのために空けてある。
--
-- 【本文に在っても、台帳に無い名前は書かない】
-- HTMLモードで存在しない画像の URL を手で書くと、形だけは正しい名前が本文に入りうる。
-- そのまま INSERT すると外部キーで落ちて、**記事の保存そのものが失敗する**。
-- INSERT 側で「台帳に在る名前だけ」に絞ってある（守りは外部キーがそのまま残る）。
--
-- 【記事を消したら、その記事の行は一緒に消える】外部キーの ON DELETE CASCADE。
-- 控えだけを消すとき（「この編集を捨てる」「反映する（保存）」）は、OwnerKind = 20 の行を消す
--
-- 【フィルター付きの索引を作っていない】理由は ArticleImageFiles.sql と同じ
--
-- 【流す順番】Articles.sql と ArticleImageFiles.sql のあと（両方を外部キーで指している）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ArticleImageUses', N'U') IS NULL
BEGIN

    CREATE TABLE ArticleImageUses (

        -- どの記事か（控えの行も、記事の番号で持つ＝ArticleDrafts の主キーも ArticleId のため）
        ArticleId INT NOT NULL,

        -- 本体（10）か、編集中の控え（20）か
        OwnerKind TINYINT NOT NULL,

        -- 使っている画像の名前（ArticleImageFiles.ImageName）
        ImageName NVARCHAR(64) NOT NULL,

        CONSTRAINT PK_ArticleImageUses PRIMARY KEY (ArticleId, OwnerKind, ImageName),

        -- SSMS から手で入れた値まではコードが守れないので、DB 側でも3択の外を止める
        CONSTRAINT CK_ArticleImageUses_OwnerKind CHECK (OwnerKind IN (10, 20)),

        CONSTRAINT FK_ArticleImageUses_Articles
            FOREIGN KEY (ArticleId) REFERENCES Articles (ArticleId) ON DELETE CASCADE,

        CONSTRAINT FK_ArticleImageUses_Files
            FOREIGN KEY (ImageName) REFERENCES ArticleImageFiles (ImageName)

    );

    -- 【この索引が、この表の目的そのもの】「この画像は、どこかで使われているか」を1回の seek で答える。
    -- 1日1回の片付けが、画像1枚につき1回これを引く
    CREATE INDEX IX_ArticleImageUses_ImageName
        ON ArticleImageUses (ImageName);

END
