-- ============================================
-- AccountProfiles テーブル（2026年9月29日、ブログ型の①）
-- プロフィール。1行がアカウント1つ（1アカウントに1行まで）
--
-- 【なぜ Accounts に列を足さず、別の表にしたのか】
-- Accounts には、パスワードのハッシュ・鍵の世代のような「守る情報」がある。
-- プロフィールは、その逆で「誰にでも見せる情報」である。
-- 混ぜると、公開ページ（誰でも開ける）を出すたびに、守る情報の入った行を読みに行くことになる。
-- 分けておけば、公開ページに渡す読み出しから、守る列を1つも読まないようにできる。
-- 3世代目（C#アバターチャット(仮)）のアバターも、この表にぶら下げる予定
--
-- 【名前（表示名）は、この表に無い】Accounts.DisplayName が正のまま。
-- 表示名は「公開ページに出す名前」であると同時に、管理画面のヘッダー・記事の署名にも出る
-- （＝ログインの向こう側でも使う）。移すと、ヘッダーを描くたびに表を2本読むことになる。
-- プロフィールの編集画面の「名前」の欄は、Accounts.DisplayName を直接読み書きしている
--
-- 【流す順番】Accounts.sql の あと（AccountId を外部キーで指している）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'AccountProfiles', N'U') IS NULL
BEGIN

    CREATE TABLE AccountProfiles (

        -- 【主キーが AccountId そのもの】1アカウントに1行しか無いので、別の番号を振っていない
        -- （ArticleDrafts が ArticleId を主キーにしているのと同じ形）
        AccountId INT NOT NULL,

        -- ============================================
        -- 画像（2枚）
        -- ============================================
        --
        -- 中身は「2/0f3c8a1b2d4e5f60718293a4b5c6d7e8.jpg」（アカウントの番号／GUID／拡張子）。
        -- 置き場所は Uploads/profiles/ で、**記事の画像（Uploads/articles/）とは根を分けてある**
        -- ――記事の画像は1日1回の片付けが「どの記事にも使われていない画像」を消しにいくので、
        -- 同じフォルダに置くと、プロフィールの画像が翌日に全部消える。
        --
        -- 【NULL が「置いていない」】空文字は入れない
        -- 【差し替えると、名前ごと変わる】古いファイルは、差し替えが済んだあとに消す
        -- （Accounts/AccountProfileImages.vb）。名前を使い回さないのは、
        -- ブラウザが前の絵をキャッシュしていて「差し替えたのに古い絵が出る」のを避けるため
        AvatarImageName NVARCHAR(60) NULL,
        CoverImageName NVARCHAR(60) NULL,

        -- 【文字のアイコン】
        -- 写真を置いていない人の「色の付いた丸に1文字」。**自分で変えたときだけ入る。NULL が「最初のまま」**
        -- （最初の文字＝名前の1文字目、最初の色＝アカウントの番号から。Accounts/AccountAvatar.vb）
        AvatarLetter NVARCHAR(16) NULL,
        AvatarColor CHAR(7) NULL,

        -- ============================================
        -- 「値」と「見せるか」を、別の列で持つ
        -- ============================================
        --
        -- 【空欄（入れていない）と、非公開（入れたけど見せない）は別物である】
        -- 1つの列にまとめて「非公開なら空にする」形にすると、非公開にした瞬間に中身が消えて、
        -- 公開に戻すときにもう一度書くことになる。
        --
        -- 【見せるかは、真偽値ではなく名前の付いた値にしてある】
        --   10 … 公開
        --   20 … 非公開
        -- 10刻みなのは、MVC版で「フォロワーだけ」のような3つ目が来たときに、間へ入れられるようにするため
        -- （Articles.Status・ArticleCategories.SortMode と同じ決まり）。
        --
        -- 【名前・画像・ブログ名・ブログの説明には、見せるかの列を付けていない】
        -- そこは「プロフィールのページそのものの見た目」で、隠すなら空にすればよい。
        -- 要るようになったら、列を1本足すだけで済む

        -- --- 自己紹介（プレーンテキスト。HTML ではない）---
        --
        -- 【HTML にしない】記事の本文と違い、③の右の枠では「出だしだけを切って …」と出す。
        -- HTML を途中で切るとタグが壊れるので、最初からプレーンテキストで持つ。
        -- 改行はそのまま入る（出すときに white-space: pre-wrap で見せる）。
        -- 出すときは、ふつうにエスケープする（記事の本文のような「生の HTML」ではない）
        Introduction NVARCHAR(1000) NULL,
        IntroductionVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_IntroVis DEFAULT 10,

        -- --- 性別（10 男性／20 女性／30 その他／40 回答しない。NULL＝選んでいない）---
        --
        -- 【「その他」「回答しない」を入れてある】
        -- 「回答しない」と「非公開」と「空欄」は、それぞれ違うことを意味する――
        -- 「答えない」と決めたのか、「入れたけど今は見せない」のか、「まだ何も触っていない」のか
        Gender TINYINT NULL,
        GenderVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_GenderVis DEFAULT 10,

        -- --- 生年月日 ---
        --
        -- 【ここだけ、見せるかが2択ではなく3択である】
        --   10 … 出さない
        --   20 … 月日だけ
        --   30 … 年月日
        -- 「年は見せたくないが、誕生日は見せたい」が、ふつうにあるため。
        -- **よくあるブログのサービスにある「設定後は変更不可」は真似しない**（あとから何度でも変えられる）
        BirthDate DATE NULL,
        BirthDateMode TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_BirthMode DEFAULT 30,
        -- 公開・非公開は、ほかの項目と同じ列に分けた（10 公開／20 非公開）。
        -- 非公開にしても、BirthDateMode（20 月日だけ／30 年月日）は覚えておく。10（出さない）は古い値で、
        -- アプリは「非公開＋年月日」として読む。
        -- 今は 10 を外してある（CHECK は 20・30、既定は 30）
        BirthDateVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_BirthVis DEFAULT 10,

        -- --- 血液型（10 A／20 B／30 O／40 AB。NULL＝選んでいない）---
        BloodType TINYINT NULL,
        BloodTypeVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_BloodVis DEFAULT 10,

        -- --- 出身地・居住地 ---
        --
        -- 【都道府県は、JIS の番号（北海道=1 … 沖縄=47）をそのまま入れる。48 は「海外」】
        -- ここだけ10刻みにしていないのは、**都道府県がこちらの決めた選択肢ではない**ため。
        -- 10刻みは「あとから間に足せるように」だが、都道府県の間には何も足されない。
        -- 48（海外）だけがこちらで決めた値で、49以降は空けてある
        Hometown TINYINT NULL,
        HometownVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_HomeVis DEFAULT 10,
        Residence TINYINT NULL,
        ResidenceVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_ResVis DEFAULT 10,

        -- --- ステータス（10 独身／20 恋人あり／30 婚約中／40 既婚／50 その他。NULL＝選んでいない）---
        RelationshipStatus TINYINT NULL,
        RelationshipStatusVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_RelVis DEFAULT 10,

        -- --- 職業（選ぶ ＋ 自由記入。よくあるブログのサービスと同じ形）---
        --
        --   10 会社員／20 公務員／30 会社役員／40 自営業／50 自由業／
        --   60 専業主婦・主夫／70 パート・アルバイト／80 学生／90 無職／100 その他
        --
        -- OccupationText は、選んだものに添える一言（「Web エンジニア」など）。
        -- **選ばずに自由記入だけ、も許す**（当てはまるものが無い人を止めないため）
        Occupation TINYINT NULL,
        OccupationText NVARCHAR(50) NULL,
        OccupationVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_OccVis DEFAULT 10,

        -- ============================================
        -- SNS
        -- ============================================
        --
        -- 【URL ではなく、利用者名（ID）だけを持つ】
        -- 要望：そのサービスのアドレスしか受け付けない（javascript: などを仕込まれないように）。
        -- URL を保存すると、**出すたびに「本当にそのサービスの URL か」を確かめ続ける**ことになる。
        -- 利用者名なら、入れるときに文字の形だけを見ればよく（英数字と一部の記号だけ）、
        -- **URL はこちらが組み立てる**ので、javascript: が入る隙が原理的に無い。
        -- 画面の欄は URL を貼っても受け取り、そこから利用者名を取り出す（Accounts/AccountProfileRules.vb）。
        --
        -- 【列の名前を XName にしないこと】この VB プロジェクトは System.Xml.Linq を
        -- 既定で Imports しており、XName という型が既にある（VB は大文字小文字を区別しない）
        XUserName NVARCHAR(15) NULL,
        XVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_XVis DEFAULT 10,
        FacebookUserName NVARCHAR(50) NULL,
        FacebookVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_FbVis DEFAULT 10,
        InstagramUserName NVARCHAR(30) NULL,
        InstagramVisibility TINYINT NOT NULL CONSTRAINT DF_AccountProfiles_IgVis DEFAULT 10,

        -- ============================================
        -- ブログ（④でブログのトップに出す）
        -- ============================================
        --
        -- 空なら、これまでどおり表示名から組み立てた見出し（「〇〇の記事」）を出す
        BlogTitle NVARCHAR(60) NULL,
        BlogDescription NVARCHAR(200) NULL,

        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_AccountProfiles_UpdatedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_AccountProfiles PRIMARY KEY (AccountId),

        -- 【ON DELETE CASCADE】プロフィールは、アカウントの一部である。
        -- アカウントが無くなったあとに残っても、誰のものか分からない
        -- （ArticleDrafts が記事に付いて消えるのと同じ考え方。
        --   なお Accounts の行は、今の運用では消さない＝IsActive を 0 にするだけ）
        CONSTRAINT FK_AccountProfiles_Accounts
            FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId) ON DELETE CASCADE,

        -- 【DB の側でも、決めた値の外を止める】
        -- コードが守っていても、SSMS から手で入れた値までは守れない
        -- （Articles.Status と同じ決まり）
        CONSTRAINT CK_AccountProfiles_IntroVis CHECK (IntroductionVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_GenderVis CHECK (GenderVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_BloodVis CHECK (BloodTypeVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_HomeVis CHECK (HometownVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_ResVis CHECK (ResidenceVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_RelVis CHECK (RelationshipStatusVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_OccVis CHECK (OccupationVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_XVis CHECK (XVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_FbVis CHECK (FacebookVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_IgVis CHECK (InstagramVisibility IN (10, 20)),

        CONSTRAINT CK_AccountProfiles_BirthMode CHECK (BirthDateMode IN (20, 30)),
        CONSTRAINT CK_AccountProfiles_BirthVis CHECK (BirthDateVisibility IN (10, 20)),
        CONSTRAINT CK_AccountProfiles_Gender CHECK (Gender IS NULL OR Gender IN (10, 20, 30, 40)),
        CONSTRAINT CK_AccountProfiles_Blood CHECK (BloodType IS NULL OR BloodType IN (10, 20, 30, 40)),
        CONSTRAINT CK_AccountProfiles_Rel
            CHECK (RelationshipStatus IS NULL OR RelationshipStatus IN (10, 20, 30, 40, 50)),
        CONSTRAINT CK_AccountProfiles_Occ
            CHECK (Occupation IS NULL OR Occupation IN (10, 20, 30, 40, 50, 60, 70, 80, 90, 100)),

        -- 都道府県（1～47）と、海外（48）
        CONSTRAINT CK_AccountProfiles_Home
            CHECK (Hometown IS NULL OR (Hometown BETWEEN 1 AND 48)),
        CONSTRAINT CK_AccountProfiles_Res
            CHECK (Residence IS NULL OR (Residence BETWEEN 1 AND 48)),

        -- 文字のアイコンの色は #RRGGBB の形だけ
        CONSTRAINT CK_AccountProfiles_AvatarColor
            CHECK (AvatarColor IS NULL OR AvatarColor LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')

    );

    -- 【索引は、主キーだけ】
    -- 引き方は「AccountId で1行」しかない（公開ページは、先に Accounts をユーザー名で引いて番号を得る）。
    -- 使われない索引は、書き込みのたびに更新されるだけである（宿題16と同じ考え方）

END
