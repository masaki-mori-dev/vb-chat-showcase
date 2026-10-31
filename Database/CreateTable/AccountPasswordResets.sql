-- ============================================
-- AccountPasswordResetsテーブル（2026年9月22日、A-2e）
-- パスワードを忘れたときの「再設定のリンク」の合言葉。1行＝アカウント1つぶん（1アカウントに1行まで）
--
-- 【合言葉そのものは置かない。SHA-256 だけを置く】
-- DB が漏れても、そこから再設定のリンクは作れない。
-- 合言葉は32バイトの乱数なので、PBKDF2 のような遅いハッシュは要らない（総当たりの心配が無い）。
--
-- 【1アカウントに1行（主キー＝AccountId）】
-- 新しく出したら同じ行を上書きする＝前に出したリンクは、自然に使えなくなる。
--
-- 【使ったら行を消す】取り出すのと消すのを1つの文でやる（DELETE ... OUTPUT）。2回押されても通るのは1回だけ。
-- パスワードかメールアドレスが変わったときも、Accounts/AccountStore.vb がこの行を消す
-- （メールアドレスを変えたら、前のアドレスへ送ったリンクは使えなくなる）。
--
-- 【流す順番】Accounts.sql のあと（AccountId が外部キーで Accounts を指す）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'AccountPasswordResets', N'U') IS NULL
BEGIN

    CREATE TABLE AccountPasswordResets (

        -- どのアカウントの合言葉か（1アカウントに1行まで＝主キー）
        AccountId INT NOT NULL,

        -- 合言葉の SHA-256（32バイト）。リンクを開いたときは、これで引く（下の一意索引）
        TokenHash BINARY(32) NOT NULL,

        -- 出した時刻。同じアカウントへは5分に1通まで、を決めるのに使う
        RequestedAt DATETIME2(3) NOT NULL CONSTRAINT DF_AccountPasswordResets_RequestedAt DEFAULT SYSDATETIME(),

        -- 期限（出してから30分）。過ぎたものは、行が残っていても使えない
        ExpiresAt DATETIME2(3) NOT NULL,

        CONSTRAINT PK_AccountPasswordResets PRIMARY KEY (AccountId),

        -- 【ON DELETE CASCADE】アカウントを消したら、合言葉も一緒に消える（アカウントなしに残る意味が無い）
        CONSTRAINT FK_AccountPasswordResets_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId) ON DELETE CASCADE

    );

    -- リンクを開いたとき、合言葉のハッシュで1行を引く。
    -- 【一意】32バイトの乱数のハッシュなので、ふつう重ならない。万一重なったら INSERT が止まる（別の人のリンクで入れてしまうより安全）。
    -- 【フィルターを付けていない】フィルター付き索引は、sqlcmd から DML を流すときに QUOTED_IDENTIFIER の罠を作るため
    CREATE UNIQUE INDEX IX_AccountPasswordResets_TokenHash ON AccountPasswordResets (TokenHash);

END
