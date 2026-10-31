-- ============================================
-- ChatRoomBansテーブル（2026年9月19日、15-3b）
-- 部屋の管理人が「入室禁止」にした人の一覧。1行＝部屋1つ×ブラウザ1つ
--
-- 【何のための表か】
-- 入室禁止は「その部屋では継続的に」効く
-- 一時部屋はアプリの再起動をまたいで残る（ChatRooms の行が残り、人は繋ぎ直しに来る）ので、
-- メモリだけに持つと、**再起動で禁止が黙って消える**。だからDBに置く。
-- メモリにも控えは持つ（ChatHub.Bans.vb。DBが読めないときの受け皿）が、正はこちら
--
-- 【目印は ChatBrowserId】名前を変えただけ（＝ログアウトせずに別の名前を名乗る）では逃げられない。
-- ログアウトすれば逃げられるが、それは「定員を今の人数より下げて締め出す」で補う
--
-- 【却下した形：ChatMessages の記録（38＝入室禁止にした／63＝解除した）から、今の禁止を組み立てる】
-- 表は増えないが、①入室のたびに部屋の記録を後ろから探すことになる（主キーで1行引くより重い）
-- ②記録は保存期間（既定90日）で消える（状態を、消えていく記録に預けることになる）
-- ③記録の書き込み（ChatLog.Write）は失敗を握りつぶす作りなので、禁止が書けたかを管理人に返せない
--
-- 【Nickname などは、禁止にした時点の見た目】「部屋の設定」の「入室禁止にした人」の一覧に出すため。
-- **ChatBrowserId そのものは、画面へ渡さない**（本人証明そのもの。一覧は匿名ID＝AnonId で指す）
--
-- 【部屋が閉じたら、その部屋の行は消す】（ChatHub.CloseRoomInDatabase。閉じた部屋には誰も入れないので、残しても害は無いが溜まる）
-- 【外部キーは付けない】ChatMessages と同じ（部屋の行は物理削除しない作りで、消す順番に縛られたくない）
--
-- 【何度流しても安全な形にしてある】既にテーブルがあれば、何もしない（中身も消さない）
-- ============================================

IF OBJECT_ID(N'ChatRoomBans', N'U') IS NULL
BEGIN

    CREATE TABLE ChatRoomBans (

        -- どの部屋の禁止か（ChatRooms.RoomId）
        RoomId INT NOT NULL,

        -- 誰を禁止にしたか（ChatBrowserId Cookie の値）
        BrowserId UNIQUEIDENTIFIER NOT NULL,

        -- 禁止にした時点の名前・アイコン・色・トリップ（一覧に出すため。列の幅は ChatMessages と揃えてある）
        Nickname NVARCHAR(50) NOT NULL,
        IconType NVARCHAR(30) NULL,
        IconColor NVARCHAR(10) NULL,
        Trip NVARCHAR(10) NULL,

        -- 禁止にした管理人（記録。コードの判断には使わない）
        BannedByBrowserId UNIQUEIDENTIFIER NULL,

        -- 禁止にした時刻（一覧の並び順）
        BannedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ChatRoomBans_BannedAt DEFAULT SYSDATETIME(),

        -- 入室のたびに「この部屋 × このブラウザ」で1行引くので、それを主キーにする
        CONSTRAINT PK_ChatRoomBans PRIMARY KEY (RoomId, BrowserId)

    );

END
