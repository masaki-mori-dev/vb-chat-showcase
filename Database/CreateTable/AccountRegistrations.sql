-- ============================================
-- AccountRegistrationsテーブル（2026年9月29日、一般ユーザーの登録）
-- 仮登録。1行＝「確認のメールを送ったが、まだ本登録していない申し込み」1つ
--
-- 【なぜ Accounts へ直接 INSERT しないのか（ダブルオプトイン）】
-- メールアドレスが本人のものだと確かめる前に Accounts へ入れると、
--   ・他人のアドレスで登録して、そのアドレスを塞げる（本人が登録できなくなる）
--   ・届かないアドレスのアカウントが溜まる（パスワードの再設定ができない＝直せない行になる）
-- ことになる。**確認のメールのリンクを押した時点で、はじめて Accounts へ移す。**
--
-- 【ユーザー名は、ここでは押さえない（わざと）】
-- この表に UserName の一意索引を張れば「先に申し込んだ人のもの」にできるが、
-- **メールを1通も受け取らずに、好きなユーザー名を期限のあいだ塞げる口**になる。
-- 本登録のときに Accounts へ「まだ空いていれば入れる」形（1つの文の中で確かめる）に
-- してあるので、先に取られていたら、そのときに断る（めったに起きない）。
--
-- 【メールアドレスは1つにつき1行まで】下の一意索引。
-- 2度目の申し込みは同じ行を上書きする（前のリンクは使えなくなる）＝
-- AccountPasswordResets が「1アカウントに1行」なのと、まったく同じ形である。
-- 同じアドレスへ何通も送らせないための歯止めも、この1行の RequestedAt で決める。
--
-- 【合言葉そのものは置かない】SHA-256 だけ（AccountPasswordResets と同じ）。
-- DB が漏れても、そこから本登録のリンクは作れない。
--
-- 【パスワードのハッシュは、ここで持つ】
-- 仮登録の時点でハッシュにしてしまう（生のパスワードは、どこにも残さない）。
-- 本登録では、この文字列をそのまま Accounts へ写す（もう一度打たせない）。
--
-- 【流す順番】Roles.sql → Accounts.sql のあと。
-- この表は外部キーを1本も持たない（まだアカウントではないため）。
--
-- 【期限が切れた行は、1日1回の片付け（Core/LogCleanup.vb）が消す】
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'AccountRegistrations', N'U') IS NULL
BEGIN

    CREATE TABLE AccountRegistrations (

        RegistrationId INT IDENTITY(1,1) NOT NULL,

        -- 申し込まれたユーザー名（本登録で Accounts.UserName になる）
        --
        -- 【この列に一意索引は張らない】上の【ユーザー名は、ここでは押さえない】の理由
        UserName NVARCHAR(30) NOT NULL,

        -- 名前（表示名）。本登録で Accounts.DisplayName になる
        DisplayName NVARCHAR(50) NOT NULL,

        -- メールアドレス（確認のメールの宛先）
        --
        -- 【1アドレスにつき1行】同じアドレスで2回申し込んだら、この行を上書きする
        Email NVARCHAR(256) NOT NULL,

        -- パスワードのハッシュ（PBKDF2-SHA256。Accounts.PasswordHash と同じ形）
        PasswordHash NVARCHAR(256) NOT NULL,

        -- 確認のリンクの合言葉の SHA-256（32バイト）
        --
        -- 【合言葉そのものは置かない】AccountPasswordResets.TokenHash と同じ考え方
        TokenHash BINARY(32) NOT NULL,

        -- 利用規約に同意した時刻
        --
        -- 【残す理由】規約が変わったときに「いつの版に同意したか」が要る
        -- 本登録のときに Accounts へは写さず、**この時刻を Accounts.TermsAgreedAt へ入れる**
        TermsAgreedAt DATETIME2(3) NOT NULL,

        -- 申し込んだ人の IP（記録だけ。歯止めそのものはメモリで数えている）
        IpAddress NVARCHAR(45) NULL,

        RequestedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_AccountRegistrations_RequestedAt DEFAULT SYSDATETIME(),

        -- 期限（出してから 24時間。Accounts/AccountRegistration.vb の TokenLifetime）
        ExpiresAt DATETIME2(3) NOT NULL,

        CONSTRAINT PK_AccountRegistrations PRIMARY KEY (RegistrationId)

    );

    -- 1アドレスにつき1行（上書きのため）
    CREATE UNIQUE INDEX UX_AccountRegistrations_Email ON AccountRegistrations (Email);

    -- 合言葉から1行引く。一意にしてあるのは、万一ぶつかったら DB で止めるため
    CREATE UNIQUE INDEX UX_AccountRegistrations_TokenHash ON AccountRegistrations (TokenHash);

    -- 期限切れの片付け（1日1回）のため
    CREATE INDEX IX_AccountRegistrations_ExpiresAt ON AccountRegistrations (ExpiresAt);

END
