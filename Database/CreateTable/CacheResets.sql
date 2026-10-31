-- ============================================
-- CacheResets テーブル：DB を直接書き換えたあと、アプリにメモリの控えを捨てさせる合図（2026年10月4日）
--
-- 【何のための表か】1行だけの合図。
--   DB を直接書き換える道具が、流し終わったら RequestedAt を今の時刻にする。
--   アプリ（Core/CacheReset.vb）は30秒に1回この行を読み、RequestedAt が AppliedAt より新しければ、
--   全部の控え（新しいメンバー・アイコン・メニュー・お知らせ・設定・ロールごとの上限・スタンプの目次・ロールの名前）を捨てて、AppliedAt を書く。
--   道具は AppliedAt を見て「捨て終わった」を確かめる。
--
-- 【既にある表に入れなかった理由】（意味と寿命が違うものは、同じ表に混ぜない）
--   SiteSettings は、それ自体が控えに載る（読むのが起き直すまで1回だけ）ので、合図に使えない。
--   DailyJobs は「この日の分まで済んだ」を覚える表で、意味と寿命が違う。
--
-- 【何度流しても安全な形で書いてある】表が無いときだけ作り、行が無いときだけ入れる
-- ============================================

IF OBJECT_ID(N'CacheResets', N'U') IS NULL
BEGIN
    CREATE TABLE CacheResets (
        Id TINYINT NOT NULL CONSTRAINT PK_CacheResets PRIMARY KEY,
        RequestedAt DATETIME2(3) NOT NULL,
        AppliedAt DATETIME2(3) NOT NULL,
        CONSTRAINT CK_CacheResets_OneRow CHECK (Id = 1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM CacheResets WHERE Id = 1)
BEGIN
    INSERT INTO CacheResets (Id, RequestedAt, AppliedAt) VALUES (1, SYSDATETIME(), SYSDATETIME());
END
GO
