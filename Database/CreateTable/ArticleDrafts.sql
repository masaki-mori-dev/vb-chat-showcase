-- ============================================
-- ArticleDraftsテーブル（2026年9月22日、A-2b-4）
-- 公開中の記事の「編集中の控え」。1行＝記事1つぶんの控え（記事1つにつき、控えは1つまで）
--
-- 【何のためにあるか】
-- 記事の画面は、打つのが止まって数秒たったら自動で保存する。
--   ・下書き・非公開の記事 … 本体（Articles）へそのまま書く（誰にも見えていないので、分ける意味が無い）
--   ・公開中の記事       … **本体には書かず、この表に書く**。公開ページは、書きかけの途中で変わらない
-- 「反映する（保存）」を押したら本体へ書き、この行を消す。「この編集を捨てる」を押したら、この行だけを消す。
--
-- 【Articles に列を足す形にしなかった理由】
--   ・「公開中の本物」と「編集中の控え」が1つの行に混ざると、読む SQL がどちらを読むかを毎回決めることになる。
--     公開ページ・一覧・メニューの控え（SiteArticles）は、今までどおり Articles だけを読めばよい
--   ・捨てるときは、この行を消すだけで済む（Articles の列を空に戻す、という書き方の間違いが起きない）
--
-- 【状態（Status）は持たない】状態の切り替えは「保存する」を押したときだけ（自動保存では変えない）。
-- 控えを開いたときも、状態の欄は本体の状態のまま出る。
--
-- 【流す順番】Articles.sql のあと（ArticleId が外部キーで Articles を指す）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ArticleDrafts', N'U') IS NULL
BEGIN

    CREATE TABLE ArticleDrafts (

        -- どの記事の控えか（記事1つにつき1行まで＝主キー）
        ArticleId INT NOT NULL,

        -- どのアカウントの記事か
        --
        -- 【Articles にも同じ値があるのに持つ理由】どの SQL も AccountId で絞る、という決まりを
        -- この表でも守るため（ArticleId だけで UPDATE・DELETE しない。番号を打ち替えるだけで他人の控えを触れないように）
        AccountId INT NOT NULL,

        -- 以下は、画面の欄と同じもの（Articles の同じ名前の列と同じ型）
        --
        -- 【カテゴリに外部キーを付けていない】カテゴリを消すときは、Articles/ArticleCategoryStore.vb の Delete が
        -- この表の CategoryId も同じトランザクションで NULL にする。控えを本体へ反映するときは、保存の口（ArticleStore.Save）が
        -- 「自分のカテゴリか」を確かめ直す
        CategoryId INT NULL,

        -- タイトル（自動保存では空でもよい。「反映する（保存）」のときは必須）
        Title NVARCHAR(200) NOT NULL,

        -- 本文（HTML）
        --
        -- 【本体と同じく、HtmlSanitizer を通してから入る】自動保存の口（ArticleStore.AutoSave）も、
        -- 手で押す保存と同じ道（包みを戻す → Sanitize）を通る。
        -- 【プレビューで出すときも、表示の前にもう一度通す】（ArticleHtml.SanitizeDraftForDisplay）
        BodyHtml NVARCHAR(MAX) NOT NULL,

        SortOrder INT NULL,
        PinOrder INT NULL,

        -- URL の名前（反映するときに、ほかの記事と重なっていないかを確かめる。控えの段階では形だけを見る）
        Slug NVARCHAR(40) NULL,

        -- 記事の「目次」の3択（2026年9月28日）。本体（Articles.TocMode）と同じ意味・同じ値
        --
        -- 【控えにも持たせる理由】自動保存の控えで開き直したときに、選んだ3択が消えないようにするため。
        -- 「編集中の内容をプレビュー」も、控えの側の値で出す
        TocMode TINYINT NOT NULL CONSTRAINT DF_ArticleDrafts_TocMode DEFAULT 10,

        -- 見出しの番号の2択（2026年9月28日）。本体（Articles.HeadingNumberMode）と同じ意味・同じ値
        HeadingNumberMode TINYINT NOT NULL CONSTRAINT DF_ArticleDrafts_HeadingNumberMode DEFAULT 10,

        -- 最後に自動保存した時刻（画面に「自動保存しました 21:45」と出す・プレビューの控えの鍵にする）
        SavedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ArticleDrafts_SavedAt DEFAULT SYSDATETIME(),

        CONSTRAINT CK_ArticleDrafts_TocMode CHECK (TocMode IN (10, 20, 30)),
        CONSTRAINT CK_ArticleDrafts_HeadingNumberMode CHECK (HeadingNumberMode IN (10, 20)),

        CONSTRAINT PK_ArticleDrafts PRIMARY KEY (ArticleId),

        -- 【ON DELETE CASCADE にしてある】記事を消したら、控えも一緒に消える。
        -- カテゴリ（Articles.CategoryId）では「黙って外れるより、止まって気づくほうがよい」として付けなかったが、
        -- 控えは記事の一部で、記事なしに残る意味が無い（止めても、直し方は「控えも消す」しかない）
        CONSTRAINT FK_ArticleDrafts_Articles
            FOREIGN KEY (ArticleId) REFERENCES Articles (ArticleId) ON DELETE CASCADE,

        CONSTRAINT FK_ArticleDrafts_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId)

    );

END
