-- ============================================
-- Rolesテーブルの初期データ（2026年9月20日、A-1）
--
-- 【5つは「種類」であって、上下ではない】
-- 並び（SortOrder）は管理画面に出す順であって、強さの順ではない。
-- 何ができるかは、この表ではなく RoleLimits の行だけが決める（Database\CreateTable\Roles.sql の先頭）
--
-- 【VB版で実際に使うのは administrator だけ】
-- 残り4つは行だけ作って、誰にも割り当てない
--
-- 【流す順番】Database\CreateTable\Roles.sql のあと、
--            Database\InsertData\RoleLimits.sql の前
--
-- 【何度実行しても、結果は変わらない】
-- まだ無い行だけを入れ、既にある行は表示のための列だけを揃える
-- （SiteSettings の初期データとまったく同じ形。理由はあちらの末尾のコメント）
--
-- 【日本語名】「管理人」ではなく「サイト管理者」など。
-- 「部屋の管理人」（15番）と紛れるため。
-- （このファイルを流し直しても、下の UPDATE で同じ名前に揃う）
-- ============================================

DECLARE @SeedRole TABLE (
    RoleKey     NVARCHAR(20),
    DisplayName NVARCHAR(50),
    SortOrder   INT,
    Description NVARCHAR(500)
);

INSERT INTO @SeedRole (RoleKey, DisplayName, SortOrder, Description) VALUES

(N'general', N'一般会員', 10,
 N'無料で登録した人。VB版では使いません（一般ユーザーの登録画面はMVC版で作ります）。'),

(N'premium', N'プレミアム会員', 20,
 N'上位の会員区分（今は使っていない）。VB版では使いません。何ができるか・どこまでかは、RoleLimitsの行で決めます（コードは直しません）。'),

(N'moderator', N'見回り係', 30,
 N'荒らし対応などを手伝ってもらう人。VB版では使いません。※「一般より上」ではありません。ロールに上下は作っていないので、できることはこのロールの行にだけ書きます。'),

(N'viewer', N'閲覧のみ', 40,
 N'読むことしかできない人。VB版では使いません。止めたい人をここへ移す使い方も想定しています。'),

(N'administrator', N'サイト管理者', 50,
 N'サイトの管理者。VB版で実際に使うのは、このロールだけです。記事は全部このロールのアカウントで書きます。');


-- まだ無い行だけを入れる
INSERT INTO Roles (RoleKey, DisplayName, SortOrder, Description)
SELECT s.RoleKey, s.DisplayName, s.SortOrder, s.Description
FROM @SeedRole AS s
WHERE NOT EXISTS (SELECT 1 FROM Roles AS t WHERE t.RoleKey = s.RoleKey);


-- 既にある行は、管理画面のための列だけを最新に揃える
-- （この表にはコードが読む値が RoleKey しか無いので、全部こちらが持ち主である）
UPDATE t
SET t.DisplayName = s.DisplayName,
    t.SortOrder   = s.SortOrder,
    t.Description = s.Description
FROM Roles AS t
INNER JOIN @SeedRole AS s ON s.RoleKey = t.RoleKey;
