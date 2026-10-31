-- ============================================
-- IconMasterテーブル
-- 利用者が選ぶアイコン（今40個）の実体。
-- SVGの中身そのものを持ち、Admin/GenerateIconSprite.aspx.vb が
-- ここから Content/icon-sprite.svg を組み立てる
--
-- 【何度流しても安全な形にしてある】
-- 既にテーブルがあれば、何もしない（中身も消さない）
--
-- 【中身（行）は、Database/InsertData/IconMaster.sql が入れる】
-- あちらは素材から機械で書き出すファイルで、手では書き換えない。
-- **素材（ICOOON MONO）そのものが入るので、git には載せていない**
-- （規約で素材の再配布は禁止されている）。
-- 
--
-- 【流す順番】
--   このファイル
--     → InsertData/IconMaster.sql
--     → そのあと /Admin/GenerateIconSprite.aspx を叩いてスプライトを作る
-- ============================================

IF OBJECT_ID(N'IconMaster', N'U') IS NULL
BEGIN

    CREATE TABLE IconMaster (

        Id INT IDENTITY(1,1) NOT NULL,

        -- アイコンの名前（英小文字・数字・アンダースコア）。例: 'young_male'
        --
        -- 【一度出したら、二度と変えられない】
        -- スプライトの id（icon-young_male）になり、ChatNickname などと同じく
        -- Cookie（ChatIconType）と過去ログ（ChatMessages.IconType）に焼き込まれる。
        -- 変えると、その人の過去ログのアイコンが黙って消える
        -- （スタンプの StampCode と、まったく同じ話）
        --
        -- 【一意にしてある】
        -- 同じ名前の行が2つあると <symbol id> が重複し、片方が黙って負ける
        Name NVARCHAR(50) NOT NULL,

        -- SVGの中身そのもの。<svg ...> ～ </svg> の形で入れること
        --
        -- 【裸の中身を入れてはいけない】
        -- GenerateIconSprite.aspx.vb の ExtractSvgInnerContent が
        -- 「最初の > から 最後の </svg> まで」を切り出すため、
        -- <g>…</g> だけを入れると、切り出しに失敗して**空の symbol**になる
        --
        -- 【色は <g fill="currentColor"> で持つ】
        -- あちらは <symbol id viewBox> しか書かない（fill は書かない）ので、
        -- 中身の側で currentColor にしておかないと、本番のアイコンが真っ黒になる
        SvgData NVARCHAR(MAX) NOT NULL,

        -- 分類（'human' / 'fantasy' / 'animal'）。
        -- 今はコードのどこからも読んでいない。管理画面や、将来のタブ分け用
        Category NVARCHAR(50) NULL,

        -- 1: スプライトに出す　0: 出さない
        --
        -- 【0 にすると、その絵の過去ログが空白になる】
        -- 生成の SELECT が IsActive = 1 だけを取るため。
        -- 「選ばせないが、過去ログには出す」にしたいときは、
        -- スプライトには残して、画面の選択肢から外す形へ直すこと（16番）
        IsActive BIT NOT NULL DEFAULT 1,

        -- 並び順（トップページと、部屋の「見た目」の選択肢の並び）。
        -- 10刻みで空けてある。後から間に1つ挿したいとき、前後を振り直さずに済むため
        -- （書き出す道具が、素材の目次の行番号×10 を入れる）
        DisplayOrder INT NOT NULL DEFAULT 0,

        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),

        -- その絵が、何マスの方眼紙に描かれているか。例: '0 0 512 512'
        --
        -- 【素材のセットごとに値が違う】
        -- Twemoji=36 / Noto Emoji=128 / Bootstrap Icons=16。
        -- 36マスの絵を512マスの紙に貼ると、左上に豆粒で表示される（エラーは出ない）
        --
        -- 【SvgData の中の viewBox は使われない】
        -- ExtractSvgInnerContent が <svg ...> タグごと捨てるため。正はこの列のほう
        --
        -- 【正方形の中の寄せ方を調整する欄でもある】
        -- 枠を狭めれば絵は大きく、広げれば小さくなる（例: '64 0 384 384'）
        ViewBox NVARCHAR(50) NOT NULL DEFAULT N'0 0 512 512',

        -- 日本語の名前。例: 'キツネ'
        --
        -- 読み上げソフト向け（画面の aria-label が「アイコン7」のままだと、
        -- 何個目かしか分からない）。NULL でも動く
        Label NVARCHAR(50) NULL,

        CONSTRAINT PK_IconMaster PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_IconMaster_Name UNIQUE (Name)
    );

END
GO
