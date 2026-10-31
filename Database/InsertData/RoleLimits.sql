-- ============================================
-- RoleLimitsテーブルの初期データ（2026年9月20日、A-1）
--
-- 【埋めてあるのは administrator と general の2つ】
-- （2026年9月29日に general を足した。それまでは administrator の行だけだった）。
-- 残り3つのロール（premium・moderator・viewer）には、1行も入れない。
-- 入っていない項目は、読む側（Accounts/RoleLimits.vb）が
-- **フェイルクローズで「できない・0」として扱う**。
-- **一般ユーザーの登録は、VB版で作った**
--
-- 【-1 は「無制限」】administrator だけに入れる値。
-- 読む側は、読めない値（空・数字でない・-1 より小さい）を 0 として扱う
--
-- 【流す順番】Database\CreateTable\RoleLimits.sql と Database\InsertData\Roles.sql のあと
--
-- 【何度実行しても、結果は変わらない】
-- まだ無い行だけを入れ、既にある行は表示のための列だけを揃える（LimitValue は絶対に触らない）。
-- LimitValue を上書きしないのは、SiteSettings.SettingValue と同じ理由
-- 。あれは運用者が管理画面から変える値で、流し直しで初期値へ戻してはいけない
-- ============================================

DECLARE @SeedLimit TABLE (
    RoleKey     NVARCHAR(20),
    LimitKey    NVARCHAR(50),
    LimitValue  NVARCHAR(200),
    DisplayName NVARCHAR(50),
    Category    NVARCHAR(30),
    SortOrder   INT,
    Description NVARCHAR(500)
);

INSERT INTO @SeedLimit (RoleKey, LimitKey, LimitValue, DisplayName, Category, SortOrder, Description) VALUES

-- === 記事（A-2・A-3 で使う） ===

(N'administrator', N'CanWriteArticles', N'1', N'記事を書けるか', N'記事', 110,
 N'1で書ける、0で書けません。行が無いロールは「書けない」になります（読めないときは、できない側へ倒す決まりのため）。'),

(N'administrator', N'ArticleMaxCount', N'-1', N'記事の数の上限（件）', N'記事', 120,
 N'そのアカウントが持てる記事の数。-1で無制限です。0以上の数を入れると、その数までになります。読めない値（空・数字でない）は0として扱われ、1件も書けなくなります。'),

-- === 画像（A-2 で使う） ===

(N'administrator', N'ArticleImageMaxBytes', N'3145728', N'記事の画像1枚の上限（バイト）', N'画像', 210,
 N'記事に貼る画像1枚の大きさの上限。既定は3145728バイト＝3MB。※3MBより大きくするときは、Web.configのmaxRequestLengthも一緒に上げてください（片方だけ上げても効きません。チャットの画像と同じ事情です）。'),

(N'administrator', N'ArticleStorageMaxBytes', N'-1', N'記事の画像の合計の上限（バイト）', N'画像', 220,
 N'そのアカウントが持てる記事の画像の合計。-1で無制限です。※記事の画像は自動削除（90日）の対象外なので、増え続けます。歯止めはこの上限だけです。'),


-- === 一般会員（2026年9月29日、一般ユーザーの登録の回で足した） ===
--
-- 【ここまで、一般会員の行は1行も無かった】
-- 読む側（Accounts/RoleLimits.vb）はフェイルクローズなので、行が無ければ
-- 「記事を書けない・画像は0バイトまで」だった。**登録できるようにした以上、
-- この4行が無いと、登録した人が何もできない。**
--
-- 【値は「最初は小さめ」】あとから管理画面で上げられる。
-- 逆（広く出して、あとから絞る）はやらない――もう上げてしまった人の画像を、消すことになる。
--
-- 【1枚の上限は、サイト管理者と同じ 3MB のまま】
-- ここを下げても Web.config の maxRequestLength（4096KB）は変わらないので、
-- サーバーが楽になるわけではない。困るのは利用者だけである。
-- **歯止めは、合計のほう（下の 50MB）でかける**

(N'general', N'CanWriteArticles', N'1', N'記事を書けるか', N'記事', 110,
 N'1で書ける、0で書けません。行の無いロールは「書けない」になります（読めないときは、できない側へ倒すためです）。'),

(N'general', N'ArticleMaxCount', N'200', N'記事の数の上限（件）', N'記事', 120,
 N'そのアカウントが持てる記事の数。-1で無制限です。0以上の数を入れると、その数までになります。読めない値（空・数字でない）は0として扱い、1件も書けなくなります。'),

(N'general', N'ArticleImageMaxBytes', N'3145728', N'記事の画像1枚の上限（バイト）', N'画像', 210,
 N'記事に貼る画像1枚の大きさの上限。既定は3145728バイト＝3MB。※3MBより大きくするときは、Web.configのmaxRequestLengthも一緒に上げてください（片方だけ上げても通りません。チャットの画像と同じ制限です）。'),

(N'general', N'ArticleStorageMaxBytes', N'52428800', N'記事の画像の合計の上限（バイト）', N'画像', 220,
 N'そのアカウントが持てる記事の画像の合計。既定は52428800バイト＝50MB。-1で無制限です。※記事の画像は自動削除（90日）の対象外なので、増え続けます。歯止めはこの上限だけです。');


-- まだ無い行だけを入れる
INSERT INTO RoleLimits (RoleKey, LimitKey, LimitValue, DisplayName, Category, SortOrder, Description)
SELECT s.RoleKey, s.LimitKey, s.LimitValue, s.DisplayName, s.Category, s.SortOrder, s.Description
FROM @SeedLimit AS s
WHERE NOT EXISTS (
    SELECT 1 FROM RoleLimits AS t
     WHERE t.RoleKey = s.RoleKey AND t.LimitKey = s.LimitKey
);


-- 既にある行は、管理画面のための列だけを最新に揃える
--
-- 【LimitValue は絶対に触らない】上のコメントの理由
UPDATE t
SET t.DisplayName = s.DisplayName,
    t.Category    = s.Category,
    t.SortOrder   = s.SortOrder,
    t.Description = s.Description
FROM RoleLimits AS t
INNER JOIN @SeedLimit AS s
        ON s.RoleKey = t.RoleKey AND s.LimitKey = t.LimitKey;
