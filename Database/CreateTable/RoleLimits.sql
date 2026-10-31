-- ============================================
-- RoleLimitsテーブル（2026年9月20日、A-1）
-- 「ロールごとに、何ができて、どこまでか」を持つ表。1行＝ロール1つ × 項目1つ
--
-- 【なぜ SiteSettings に入れないのか】
-- SiteSettings は「サイト全体で1つの値」の置き場所で、**ロールという軸を持つ値は入らない**
-- （ChatRooms.MaxUsers を SiteSettings に入れなかったのと同じ理由）。
--
-- 【形は SiteSettings に揃えてある＝「ロール × キー × 値」の行で持ち、列を増やさない】
-- 上限を1つ足すたびに ALTER TABLE するのは、管理画面（16番）から編集する形とも噛み合わない。
-- 将来の会員区分ごとの中身を、**コードを直さずに値で変えられる**ようにするためである
--
-- 【ここだけ SiteSettings と逆にする：読めなかったらフェイルクローズ】
-- SiteSettings は DB が読めなければコードの既定値で通す（フェイルオープン）。
-- 同じことをこの表でやると、「上限が読めない＝無制限」になりかねない。
-- 読めなければ**上げさせない・書かせない**側へ倒す（Account/RoleLimits.vb）。
--
-- 【値の読み方（コード側の決まり）】
--   ・オン/オフは '1' / '0'
--   ・数は10進の整数。**-1 は「無制限」**（administrator だけに入れる値）
--   ・読めない値（空・数字でない・-1 より小さい）は 0 として扱う＝させない
--
-- 【VB版で埋めるのは administrator の行だけ】
-- 残り4つのロールには、1行も入れない。入っていない＝フェイルクローズで「できない」になる
-- （一般ユーザーの登録画面そのものが MVC版なので、それでよい）
--
-- 【流す順番】Roles.sql のあと（RoleKey が外部キーでそちらを指す）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'RoleLimits', N'U') IS NULL
BEGIN

    CREATE TABLE RoleLimits (

        -- どのロールの話か（Roles.RoleKey）
        RoleKey NVARCHAR(20) NOT NULL,

        -- 項目の名前（コードが読む値）。例: 'CanWriteArticles' / 'ArticleMaxCount'
        LimitKey NVARCHAR(50) NOT NULL,

        -- 値（コードが読む値）。上の「値の読み方」のとおり
        --
        -- 【管理画面で編集できるのは、この列だけ】
        -- SiteSettings.SettingValue とまったく同じ線引きである。
        -- 残りの列は、初期データのスクリプトが持ち主なので、流すたびに上書きしてよい
        LimitValue NVARCHAR(200) NOT NULL,

        -- 以下は管理画面のための列。コードは読まない（SiteSettings と同じ）
        DisplayName NVARCHAR(50) NOT NULL,
        Description NVARCHAR(500) NULL,
        Category NVARCHAR(30) NOT NULL,
        SortOrder INT NOT NULL,

        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoleLimits_UpdatedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_RoleLimits PRIMARY KEY (RoleKey, LimitKey),

        -- 【外部キーを付ける理由】Roles.sql の先頭のコメントにある
        CONSTRAINT FK_RoleLimits_Roles FOREIGN KEY (RoleKey) REFERENCES Roles (RoleKey)

    );

END
