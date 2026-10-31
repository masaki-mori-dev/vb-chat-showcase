-- ============================================
-- Rolesテーブル（2026年9月20日、A-1）
-- アカウントの「種類」の一覧。1行＝ロール1つ
--
-- 【上下（段階）を作らない】
-- 5つは「種類」であって、強さの順ではない。「moderator は premium の上だから
-- premium の権限も全部持つ」という**暗黙の継承**を作ると、後から機能を足したときに、
-- 足した人が気づかないまま上位のロールへ権限が流れ込む。
-- できること・上限は、すべて RoleLimits の「ロールごとの行」でだけ決める。
-- **この表に「強さ」を表す列は置かない。SortOrder は管理画面での並び順である**
-- （SiteSettings.SortOrder と同じ役割。SiteSettings のあれも、順に意味は無い）。
--
-- 【VB版で実際に使うのは administrator だけ】
-- 残りの4つは行だけ作って、誰にも割り当てない。一般ユーザーの登録画面は MVC版（2世代目）で作る。
-- それでも今5行入れておくのは、**上限の表（RoleLimits）がロールを外部キーで指すため**、
-- あとから足すより、最初から器が5つある形のほうが、管理画面（16番）を作るときに迷わないから
--
-- 【流す順番】この表を先に作ること（Accounts と RoleLimits が、外部キーでここを指す）。
--   Database\CreateTable\Roles.sql → Accounts.sql → RoleLimits.sql
--   Database\InsertData\Roles.sql → RoleLimits.sql
--
-- 【外部キーを付ける理由】ChatMessages に外部キーを付けなかったのは、
-- 書き込みが多いログの表で、定期削除の順番に縛られたくなかったからである。
-- こちらは5行しかない master の表で、行を消すこともない。
-- 綴りを間違えたロールがアカウントに入ると、そのアカウントは上限を1つも引けなくなり、
-- **フェイルクローズで何もできない人が静かに出来上がる**。DBで止めるほうがよい
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'Roles', N'U') IS NULL
BEGIN

    CREATE TABLE Roles (

        -- ロールの名前（コードが読む値。英小文字）
        -- 'general' / 'premium' / 'moderator' / 'viewer' / 'administrator'
        RoleKey NVARCHAR(20) NOT NULL,

        -- 管理画面に出す日本語の名前（そのまま画面へ出せる形で持つ。SiteSettings.DisplayName と同じ考え方）
        DisplayName NVARCHAR(50) NOT NULL,

        -- 管理画面の注意書き
        Description NVARCHAR(500) NULL,

        -- 管理画面での並び順。**強さの順ではない**（上のコメント）。10刻みで空けてある
        SortOrder INT NOT NULL,

        CONSTRAINT PK_Roles PRIMARY KEY (RoleKey)

    );

END
