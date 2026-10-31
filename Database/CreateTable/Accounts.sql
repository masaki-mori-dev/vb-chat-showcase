-- ============================================
-- Accountsテーブル（2026年9月20日、A-1）
-- サイトのアカウント。1行＝アカウント1つ
--
-- 【アカウントは、管理人と一般ユーザー】（2026年9月29日から一般ユーザーの登録もある）
-- 入れ物は、ロールを足しても変えずに使える形で作ってある。
--
-- 【チャットの利用者とは、まったく別の話である】
-- チャットは今までどおり Cookie ユーザーのまま。この表はチャットの身元
-- （ChatBrowserId・名前の札）とは1つも繋がっていない。
-- ChatMessages.UserId は、この回でも NULL のままである（アカウントで発言する仕組みは作っていない）
--
-- 【流す順番】Roles.sql のあと（RoleKey が外部キーでそちらを指す）
--
-- 【最初の管理人は、SQLでは作れない】
-- パスワードがハッシュ（PBKDF2）なので、SSMS からは INSERT できない。
-- Account/Setup.aspx（「最初の管理人を作る画面」）で作る。この表は空のまま本番へ流すのが正しい
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'Accounts', N'U') IS NULL
BEGIN

    CREATE TABLE Accounts (

        AccountId INT IDENTITY(1,1) NOT NULL,

        -- ログインに使う名前
        --
        -- 【「@」を入れさせない】ログインの欄は1つで、「@ が在ればメールアドレス」で振り分ける
        --   （決まりはコード側 Account/AccountStore.vb の NormalizeLoginId と、Account/AccountPassword.vb）。
        -- 【URLにそのまま出る】記事のURL（/articles/{user}/{id}）の {user} は、この値である。
        --   そのためコード側で、英数字とハイフン・アンダースコアだけに絞ってある
        -- 【重複させない】下の一意索引。SQL Server の既定の照合順序は大文字・小文字を区別しないので、
        --   'taro' と 'Taro' は同じ名前として弾かれる（それが狙いどおり）
        UserName NVARCHAR(30) NOT NULL,

        -- 記事に出す名前（日本語でよい。ログインには使わない）
        -- 最初の管理人を作るときは、ユーザー名と同じ値を入れておく（あとから変えられる）
        DisplayName NVARCHAR(50) NOT NULL,

        -- メールアドレス
        --
        -- 【必須】パスワードの再設定に要る（再設定そのものは、この回では作っていない）。
        -- 【重複させない】ユーザー名と同じ理由（どちらで引いても1人に決まるように）
        Email NVARCHAR(256) NOT NULL,

        -- パスワードのハッシュ（PBKDF2-SHA256。Account/AccountPassword.vb）
        --
        -- 形は部屋のパスワード（Chat/RoomPassword.vb）と同じ「方式$反復回数$ソルト$ハッシュ」の1本の文字列。
        -- 反復回数を文字列の中に持つので、あとで回数を上げても古いパスワードはそのまま照合できる。
        -- **生のパスワードは、どこにも残さない**（部屋のパスワードと違い、見せる仕組みも作らない）
        PasswordHash NVARCHAR(256) NOT NULL,

        -- どのロールか（Roles.RoleKey）
        RoleKey NVARCHAR(20) NOT NULL,

        -- 鍵の世代
        --
        -- 【何のための列か】ログインの Cookie は、サーバーが署名しただけの文字列である
        -- （サーバー側に「誰がログイン中か」の記録を持たない＝寝るサーバーでも壊れない形）。
        -- そのままだと、**盗まれた Cookie を無効にする手段が無い**。
        -- そこでこの整数を Cookie の中にも入れて、毎回突き合わせる。
        -- **パスワードを変えたら1つ進める**＝それまでに配った Cookie が全部その場で無効になる。
        -- 名前の札（Chat/NameToken.vb）が同じ形を ChatBrowserId でやっているのと同じ考え方だが、
        -- あちらは無効にする必要が無かったので、世代を持っていない
        TokenGeneration INT NOT NULL CONSTRAINT DF_Accounts_TokenGeneration DEFAULT 1,

        -- 有効か（0＝止めている）。**行は消さない**
        -- ChatRooms.IsActive と同じ考え方（記事の書き手としてあとから引くので、行が消えると記事の主が分からなくなる）
        IsActive BIT NOT NULL CONSTRAINT DF_Accounts_IsActive DEFAULT 1,

        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Accounts_CreatedAt DEFAULT SYSDATETIME(),

        -- 最後にログインした時刻（記録。コードの判断には使わない）
        LastLoginAt DATETIME2(3) NULL,

        -- 利用規約に同意した時刻。規約が変わったときに「いつの版に同意したか」が要るため、残す。
        -- 値は仮登録の行（AccountRegistrations.TermsAgreedAt）から、本登録のときに写す。
        -- 【NULL を許す】最初のサイト管理者（/account/setup で作ったアカウント）には同意のチェックが無い。
        -- NOT NULL ＋ 既定値にすると「同意していない人にも、同意した時刻が入っている」という嘘の記録になる。
        -- 【この列を判断に使わないこと（今は）】記録のための列。「同意していない人を弾く」処理を足すなら、
        -- 最初のサイト管理者が弾かれる形にならないかを先に確かめること
        TermsAgreedAt DATETIME2(3) NULL,

        -- サイト全体の右の枠の「新しいメンバー」に、その人を出さない印（1＝出さない）。
        -- プロフィールのページ・ブログは今までどおり見える。今は SSMS で立てる
        -- （例：UPDATE Accounts SET ExcludeFromNewMembers = 1 WHERE UserName = N'〇〇';）。
        -- 枠に反映されるまで最大10分（控えの寿命。Accounts/NewMembers.vb）。
        -- 【NOT NULL ＋ 既定 0】既にいる人も、これから登録する人も「出す」から始まる
        ExcludeFromNewMembers BIT NOT NULL CONSTRAINT DF_Accounts_ExcludeFromNewMembers DEFAULT 0,

        CONSTRAINT PK_Accounts PRIMARY KEY (AccountId),

        -- 【外部キーを付ける理由】Roles.sql の先頭のコメントにある
        CONSTRAINT FK_Accounts_Roles FOREIGN KEY (RoleKey) REFERENCES Roles (RoleKey)

    );

    -- ログインは「ユーザー名かメールアドレス」の1つの欄で受けるので、どちらでも1行で引けるようにする。
    -- 一意にしてあるのは、重複を DB の側でも止めるため
    -- （コードの確認と INSERT の間に、別の人が同じ名前で入れられないように）
    CREATE UNIQUE INDEX UX_Accounts_UserName ON Accounts (UserName);
    CREATE UNIQUE INDEX UX_Accounts_Email ON Accounts (Email);

END
