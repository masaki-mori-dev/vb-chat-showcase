-- ============================================
-- ChatRoomsテーブル
-- チャットの「部屋」情報を管理するテーブル
-- 固定部屋・ユーザー作成の一時部屋・将来のアカウント永続部屋、
-- すべてこのテーブル1つで管理する（RoomType列で区別）
-- ============================================

CREATE TABLE ChatRooms (

    -- 部屋を一意に識別するID（自動採番）
    RoomId INT IDENTITY(1,1) PRIMARY KEY,

    -- 部屋名（例：「雑談部屋」「作業しながら喋る部屋」等）
    RoomName NVARCHAR(100) NOT NULL,

    -- この部屋の定員（何人まで入れるか）
    MaxUsers INT NOT NULL,

    -- ノック機能を受け付けるかどうか（満員時、入室希望を通知する機能）
    -- 0 = 受け付けない、1 = 受け付ける
    IsKnockEnabled BIT NOT NULL DEFAULT 0,

    -- 鍵付き部屋のパスワード（ハッシュ化して保存）
    -- 鍵なしの部屋はNULL
    PasswordHash NVARCHAR(256) NULL,

    -- 鍵付き部屋のパスワードを、管理人に見せるための暗号文（2026年9月18日、15-3a-3）
    -- 照合には使わない（照合は上の PasswordHash だけ）。鍵はDBの外（App_Data\room-password-key.txt）。
    -- 鍵なしの部屋・この列より前に作られた部屋は NULL（管理人の画面に「表示できません」と出る）。
    -- 詳しくは Chat/RoomPasswordSecret.vb
    PasswordCipher NVARCHAR(400) NULL,

    -- 部屋の種別
    -- 'Fixed'      = 開発者が手動管理する固定部屋
    -- 'Temporary'  = ゲストが作る一時的な部屋（IIS再起動で消えてよい）
    -- 'Persistent' = 将来のアカウントユーザーが持つ永続部屋
    RoomType NVARCHAR(20) NOT NULL,

    -- 今のオーナー（部屋の管理人）のUserId（アカウント機能ができてから使う列）
    -- RoomType = 'Fixed'      のとき → 常にNULL（アプリ側で「オーナーは開発者」と決め打ち）
    -- RoomType = 'Temporary'  のとき → 常にNULL（Cookieユーザーの部屋。管理人は下のOwnerBrowserId）
    -- RoomType = 'Persistent' のとき → 部屋の持ち主のUserId（将来）
    --
    -- 【2026年9月16日に、コメントを直した（宿題19）】
    -- ここには「Temporaryのとき → その時点のオーナーのLocalId」と書いてあったが、
    -- 2026年8月29日にOwnerBrowserIdへ変更する決定が出ていた。
    -- LocalIdは入室のたびに振り直されるので、オーナーがF5した瞬間に権限を失う。
    -- 決定から2週間以上、この行だけが古いまま残っていた
    OwnerId INT NULL,

    -- 今のオーナー（部屋の管理人）のブラウザ（ChatBrowserId Cookieの値）
    -- RoomType = 'Temporary' のときだけ入る。Fixed・Persistentでは NULL
    --
    -- 2026年9月16日、15-1（部屋を作る土台）で追加した。
    -- 設計そのものは2026年8月29日に決まっていたが、列は流されていなかった（宿題19）。
    --
    -- 【LocalIdではなくCookieのGUIDで持つ理由】
    -- LocalIdは入室のたびに新しい番号が振られるため、オーナーがF5しただけで
    -- 自分の部屋のオーナー権を失う。このCookieは1年有効なGUIDなので、
    -- F5でもブラウザを閉じて戻ってきても、同じ人のままでいられる。
    -- 権限の移譲も、この値の差し替えで済む
    --
    -- 【割り切り】Cookieを消されたらオーナー権は失われる。
    -- 一時部屋（誰も居なくなれば閉じる部屋）なので、そこは許容する
    OwnerBrowserId UNIQUEIDENTIFIER NULL,

    -- 部屋が作成された日時
    CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),

    -- この部屋が今も有効かどうか（論理削除フラグ）
    -- 1 = 有効（ラウンジの一覧に表示する）
    -- 0 = 終了済み（一覧には出さないが、ログ表示のために行だけ残す）
    -- 一時部屋を物理削除してしまうと、ChatMessagesに残ったRoomIdから部屋名を
    -- 引けなくなり、過去ログが読めなくなる。そのため物理削除ではなく、
    -- このフラグによる論理削除にしている
    IsActive BIT NOT NULL DEFAULT 1,

    -- この部屋で、画像をアップロードできるか
    -- 0 = できない、1 = できる
    --
    -- 2026年9月7日に先行して追加した列で、コードからの参照はまだ1件も無い。
    -- 使うのは E（画像アップロード）の回。まだ誰も読まないので、既定は安全側の0
    IsImageUploadEnabled BIT NOT NULL DEFAULT 0,

    -- この部屋で、AIが話題を振ってよいか
    -- 0 = 振らない、1 = 振る
    --
    -- 部屋に人が居るのにN分発言が無いとき、AIが話題を提供する機能のための列。
    -- こちらも2026年9月7日の先行追加で、参照はまだ無い。
    -- AIのAPIは従量課金なので、「気づいたら全部屋で有効だった」が起きない向きに
    -- 倒して、既定は0にしてある
    IsAiTopicEnabled BIT NOT NULL DEFAULT 0

);
