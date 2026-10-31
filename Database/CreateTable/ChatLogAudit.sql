-- ============================================
-- ChatLogAudit テーブル：管理人が「ログを調べる道具」で、探した・開いた・書き出した記録（2026年10月11日）
--
-- 【何のための表か】サイト管理者が、利用者の発言・IP アドレスなどを見たことの記録（見た記録）。
--   いつ・誰が・何をした・どんな条件で、を1回ごとに1行。道具（Admin/LogTool.vb の WriteAudit）だけが書く。
--   通報や警察からの依頼で調べるのは正当な使い方だが、「調べた」という事実は残す（あとから説明できるように）。
--
-- 【既にある表に入れなかった理由】（意味と寿命が違うものは、同じ表に混ぜない）
--   ChatMessages は「チャットで起きたこと」の表で、管理人の操作は意味が違う。
--   しかも ChatMessages は保存期間（既定90日）で消える。見た記録は、見た相手のログが消えたあとも残したい。
--   ContactMessages（問い合わせ）・DailyJobs も、意味と寿命が違う。
--
-- 【消さない】1日1回の古いログの削除（LogCleanup）の対象にしていない。1回の操作で1行なので、増え方はわずか
--
-- 【書けなかったら、見せない】道具は、この表に書けたあとでだけ結果を出す（見た記録が残らない閲覧を作らない）
--
-- 列：
--   AuditId    … 番号
--   AccountId  … 操作した管理人（Accounts.AccountId。外部キーは張らない＝ログの表と同じ考え方）
--   UserName   … そのときのアカウントID（焼き込み。あとでアカウントが消えても、誰だったか読めるように）
--   Action     … search（探した）／view（再生の画面を開いた）／more（再生の画面でさかのぼった・先を読んだ）／export（CSV を書き出した）
--   Detail     … 条件（探した言葉・期間・部屋・IP など。再生なら、どの発言のまわりか・どの範囲か）。道具が組み立てた文
--   TargetMessageId … 再生・書き出しの、もとになった発言の番号（探したときは NULL）
--   ResultCount … 画面に出した・書き出した行の数
--   IpAddress  … 管理人の、そのときの IP アドレス
--   OpenPath   … もう一度開くための住所（/account/logs で始まる形だけ）
--   CreatedAt  … 記録した日時
--
-- 【何度流しても安全な形で書いてある】表が無いときだけ作る
-- ============================================

IF OBJECT_ID(N'ChatLogAudit', N'U') IS NULL
BEGIN
    CREATE TABLE ChatLogAudit (
        AuditId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatLogAudit PRIMARY KEY,
        AccountId INT NOT NULL,
        UserName NVARCHAR(50) NOT NULL,
        Action VARCHAR(10) NOT NULL,
        Detail NVARCHAR(2000) NULL,
        TargetMessageId BIGINT NULL,
        ResultCount INT NULL,
        IpAddress NVARCHAR(45) NULL,
        OpenPath NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ChatLogAudit_CreatedAt DEFAULT SYSDATETIME()
    );

    -- 新しい順に眺めるための索引
    CREATE INDEX IX_ChatLogAudit_CreatedAt ON ChatLogAudit (CreatedAt DESC);
END
GO
