-- ============================================
-- ContactMessagesテーブル（2026年9月24日、問い合わせページ）
-- サイトの「お問い合わせ」から届いた1件＝1行
--
-- 【なぜメールだけにしないのか】
--   ・メールが送れなかった日に、1件も失わないため（DBが正、メールは知らせ）
--   ・MVC版の管理画面で「届いた問い合わせの一覧」に使うため
--
-- 【個人情報が入る表である】お名前・返信先のメールアドレス・IPアドレス。
-- そのため **保存期間を設けてある**（SiteSettings の ContactRetentionSeconds。既定365日）。
-- 過ぎた行は、1日1回の仕事 LogCleanup が消す（古いログの削除に相乗り）。
-- 控えを取るときは、この表も本番DBの .bacpac に入る＝置き場所に気をつけること
-- （ChatMessages がIPアドレスを持つのと同じ扱い）。
--
-- 【アカウントとは結び付けない】問い合わせはログインしていない人が送るもので、
-- Accounts への外部キーは無い。将来ログイン中の人からの問い合わせを分けたくなったら、
-- そのとき AccountId の列を足す（ChatMessages.UserId と同じ形になる）。
--
-- 【流す順番】どの表にも依存していないので、いつでもよい（外部キーが1つも無い）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ContactMessages', N'U') IS NULL
BEGIN

    CREATE TABLE ContactMessages (

        ContactId INT IDENTITY(1,1) NOT NULL,

        -- お名前（任意。空で送れる）
        --
        -- 【NULL と空文字を混ぜない】入れていなければ NULL にする（Contacts/ContactStore.vb）。
        -- 幅は、チャットのニックネーム（ChatMessages.Nickname＝50）にそろえてある
        SenderName NVARCHAR(50) NULL,

        -- 返信先のメールアドレス（任意。返事がほしい人だけ入れる）
        --
        -- 【幅は Accounts.Email（256）と同じ】
        -- 【この値をメールの宛先にしない】送るのは info@ だけで、ここは本文と Reply-To に入るだけ。
        -- 宛先にすると、他人のアドレスを書いて送るだけで、このサイトが迷惑メールの踏み台になる
        -- （Core/SiteMail.vb の EnqueueToAccount の先頭のコメントと同じ話）
        ReplyEmail NVARCHAR(256) NULL,

        -- 問い合わせの種類
        --
        --   10 … 不具合の報告
        --   20 … ご意見・ご要望
        --   30 … その他
        --
        -- 【10刻みで採番してある】あとから間に差し込めるように
        -- （ChatMessages.MessageType・ArticleCategories.SortMode と同じ決まり）。
        -- 【真偽値の組み合わせにしない】3択は、名前の付いた1つの列で持つ
        Kind TINYINT NOT NULL,

        -- 内容（必須）
        --
        -- 【幅 2000 と、コードの上限 2000 は同じ数にしてある】（Contacts/ContactRules.vb の BodyMaxLength）
        -- ここを狭くすると、画面が通した長さで SQL 例外になり、その1件が丸ごと失われる
        -- （NicknameMaxLength と ChatMessages.Nickname で踏んだ罠と、同じ形）
        Body NVARCHAR(2000) NOT NULL,

        -- 送ってきた人のIPアドレス
        --
        -- 【残す理由】連打・荒らしの見極めと、将来のIP遮断のため（ChatMessages がIPを残すのと同じ判断）。
        -- 保存期間で消える。IPv6 は最大45文字
        IpAddress NVARCHAR(45) NULL,

        -- ブラウザの名乗り（User-Agent）
        --
        -- 【残す理由】不具合の報告では、ブラウザとOSがいちばん役に立つ材料だから。
        -- 本人に「何をお使いですか」と聞き返す往復を、1回減らせる。
        -- 長いものは、コード側が256文字で切って入れる（切っても種類は読み取れる）
        UserAgent NVARCHAR(256) NULL,

        -- 届いた時刻
        --
        -- 【DBの時計で入れる】既定値のまま書き、あとから書き換えないこと。
        -- 保存期間の削除（LogCleanup）と、一覧の並び順が、この列で決まる
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ContactMessages_CreatedAt DEFAULT SYSDATETIME(),

        -- 対応が済んだ時刻（まだなら NULL）
        --
        -- 【今はどこからも書き込まない】MVC版の管理画面で「対応済み」を押すための列。
        -- 今のうちに用意しておくのは、あとで足すと表の形を変える SQL が1本増えるだけで、
        -- 得るものが何も無いから（ChatMessages.UserId と同じ考え方）
        HandledAt DATETIME2(3) NULL,

        CONSTRAINT PK_ContactMessages PRIMARY KEY (ContactId),

        -- 【DBの側でも、3択の外の値を止める】
        -- コード（Contacts/ContactRules.vb）でも丸めているが、SSMS から手で入れた値までは守れない。
        -- 知らない値が入ると、一覧の種類が黙って空欄になるだけで、誰も気づけない
        -- （ArticleCategories.SortMode と、まったく同じ判断）
        CONSTRAINT CK_ContactMessages_Kind CHECK (Kind IN (10, 20, 30))

    );

    -- 古い行を消す（LogCleanup）ときと、管理画面で新しい順に並べるときの索引
    --
    -- 【フィルター付きにしていない】そうすると、この表への INSERT／DELETE を sqlcmd から
    -- 手で流すときに SET QUOTED_IDENTIFIER ON が要るようになる
    -- （ChatMessages.ImageName で踏んだ罠。Articles.PinOrder と同じ判断で、罠を1つ増やさない）
    CREATE INDEX IX_ContactMessages_CreatedAt
        ON ContactMessages (CreatedAt);

END
