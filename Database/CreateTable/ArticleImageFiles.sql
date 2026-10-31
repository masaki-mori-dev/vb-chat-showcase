-- ============================================
-- ArticleImageFilesテーブル（2026年9月27日。記事の画像の「台帳」）
-- 1行＝アップロードした画像のファイル1つ
--
-- 【何のためにあるか】
-- それまでは、記事の画像について何かを知りたいたびに**ディスクとBodyHtmlを丸ごと読んでいた**。
--   ・そのアカウントが置いている量（ロールごとの上限 ArticleStorageMaxBytes）… フォルダの全ファイルを数える
--   ・使われていない画像の片付け（1日1回）              … フォルダの全ファイル＋全記事・全控えの本文
-- **記事と画像が増えるほど、毎晩の仕事と、画像を1枚上げるたびの待ち時間が、そのまま増える形だった**
-- （2026年9月26日の判断：利用者が増えてからでは大変になる）。
-- そこで、事実を書いておく表を2本作り、**読むのは索引だけ**にした。
--   ・この表          … どんな画像が在るか（名前・持ち主・大きさ・上げた時刻）
--   ・ArticleImageUses … どの記事／控えが、どの画像を使っているか
--
-- 【鍵は画像の名前そのもの（番号を振っていない）】
-- 名前は「2/0f3c….jpg」（アカウントの番号／GUID／拡張子）で、**本文の <img src> から取れる値そのもの**。
-- 番号（IDENTITY）を振ると、本文から取り出した名前を毎回番号に引き直すことになる。
-- 46文字までの短い文字列なので、そのまま鍵にしている。
--
-- 【この表に行があること＝ファイルが在ること、を保つ】
--   ・上げるとき … ファイルを保存してから、この行を書く。行が書けなければ、ファイルを消して断る
--   ・消すとき   … ファイルを消してから、この行を消す
--   ・ずれたとき … 1日1回の片付け（LogCleanup）が、
--                  「行は在るのにファイルが無い」は行を消し、「ファイルが在るのに行が無い」は**消さずに知らせる**
--                  （記事の画像は消えたら戻せないので、機械に任せない）。
--                  直すのは /Admin/RebuildArticleImageLedger.aspx（全体の突き合わせ）
--
-- 【フィルター付きの索引を作っていない】作ると、この表への INSERT/UPDATE/DELETE を sqlcmd から
-- 手で流すときに SET QUOTED_IDENTIFIER ON が要るようになる（ChatMessages.ImageName で踏んだ罠）
--
-- 【流す順番】Accounts.sql のあと（AccountId が外部キーで Accounts を指す）。
--             そのあと ArticleImageUses.sql
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ArticleImageFiles', N'U') IS NULL
BEGIN

    CREATE TABLE ArticleImageFiles (

        -- 画像の名前。「2/0f3c8a1b….jpg」＝アカウントの番号／GUID／拡張子
        -- （Core/SiteImage.vb の ArticleNamePattern と同じ形。本文の src は、この頭に /Uploads/articles/ が付く）
        ImageName NVARCHAR(64) NOT NULL,

        -- 持ち主（名前の頭の番号と同じ。索引で「そのアカウントの合計」を出すために列でも持つ）
        AccountId INT NOT NULL,

        -- ファイルの大きさ（バイト）。ロールごとの上限 ArticleStorageMaxBytes は、この列の合計で見る
        ByteSize BIGINT NOT NULL,

        -- 上げたときの寸法（画面が送ってくる値。分からなければ NULL）
        Width INT NULL,
        Height INT NULL,

        -- 上げた時刻。**「上げて1日以上たったもの」だけを片付ける**ための列
        -- （貼ったばかりで、まだ保存されていない画像を消さないための安全弁）
        UploadedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_ArticleImageFiles_UploadedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_ArticleImageFiles PRIMARY KEY (ImageName),

        CONSTRAINT FK_ArticleImageFiles_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId)

    );

    -- そのアカウントの合計を出すため（ByteSize を載せてあるので、この索引だけで足し算が済む）
    CREATE INDEX IX_ArticleImageFiles_Account
        ON ArticleImageFiles (AccountId) INCLUDE (ByteSize);

    -- 1日1回の片付けが「上げて1日以上たったもの」を引くため
    CREATE INDEX IX_ArticleImageFiles_UploadedAt
        ON ArticleImageFiles (UploadedAt);

END
