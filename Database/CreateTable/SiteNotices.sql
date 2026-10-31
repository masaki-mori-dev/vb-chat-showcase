-- ============================================
-- SiteNoticesテーブル（2026年9月29日、ブログ型の⑤「管理人からのお知らせ」）
-- 1行＝お知らせ1つ（日付・一言・リンク先は任意・出す／出さない）
--
-- 【なぜ記事（Articles）と別の表なのか】
-- お知らせは「日付＋一行」で、本文も・カテゴリも・目次も・URL の名前も持たない。
-- 記事の表に混ぜると、
--   ・記事の一覧・ブログのトップ・更新履歴・カテゴリの一覧の全部に
--     「お知らせは除く」という条件が要る（1か所忘れると、お知らせが記事として出る）
--   ・記事の側の決まり（本文が必須・サニタイザー・画像の台帳）が、全部お知らせにも掛かる
-- ことになる。**小さい表を1本増やすほうが安い。**
--
-- 【書けるのはサイト管理者だけ】
-- 一般会員には、管理画面のメニューにも出さず、URL を直に打ってもサーバー側で断る
-- （Accounts/AccountPageBase.vb の RequireAdministrator）。
-- **そのため、この表は「誰が書いたか」の列を持たない**――書き手はサイト管理者に決まっている。
-- 将来、書ける人を増やすなら、そのときに AccountId の列を足す。
--
-- 【本文は、生のHTMLにしない】一言（プレーンテキスト）だけを持つ。
-- 出すときに、出口でエスケープする（記事の本文だけが例外で、こちらは例外にしない）。
-- リンクは「行き先のアドレス」を別の列で持ち、こちらが <a> を組み立てる。
--
-- 【流す順番】どこでもよい（外部キーを1本も持たない）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'SiteNotices', N'U') IS NULL
BEGIN

    CREATE TABLE SiteNotices (

        NoticeId INT IDENTITY(1,1) NOT NULL,

        -- お知らせの日付（画面に出す日付。書いた日とは別に、手で決められる）
        --
        -- 【DATE にしてある】時刻は出さないし、並べ替えにも使わない
        -- （同じ日のものは NoticeId の降順で並ぶ）
        NoticeDate DATE NOT NULL,

        -- 一言（プレーンテキスト。HTML ではない）
        --
        -- 【列幅と、コードの上限を同じ数にすること】（SiteNoticeRules.BodyMaxLength）
        -- 広いほうを画面に許すと、その1件だけが保存で SQL 例外になる
        -- （NicknameMaxLength と ChatMessages.Nickname で踏んだ罠と、同じ形）
        Body NVARCHAR(200) NOT NULL,

        -- 行き先（任意。空なら、リンクにしない）
        --
        -- 【このサイトの中だけ】「/」で始まる形しか受け付けない（SiteNoticeRules.ValidateLink）。
        -- よそのサイトを許すと、管理画面を乗っ取られたときに
        -- サイトの全ページの右の枠から、好きな所へ飛ばせることになる
        LinkUrl NVARCHAR(400) NULL,

        -- 出すか（0＝下書き。書いておいて、あとで出す）
        --
        -- 【行は消さずに、これを寝かせる】記事の「非公開」と同じ考え方
        IsVisible BIT NOT NULL CONSTRAINT DF_SiteNotices_IsVisible DEFAULT 1,

        CreatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_SiteNotices_CreatedAt DEFAULT SYSDATETIME(),

        UpdatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_SiteNotices_UpdatedAt DEFAULT SYSDATETIME(),

        CONSTRAINT PK_SiteNotices PRIMARY KEY (NoticeId)

    );

    -- 画面に出すときの並び（日付の降順）。数十行の表なので、これ1本で足りる
    CREATE INDEX IX_SiteNotices_NoticeDate ON SiteNotices (NoticeDate DESC, NoticeId DESC);

END
