-- ============================================
-- Stampsテーブル
-- スタンプ1枚ごとの目次。絵そのものは持たない（絵はファイル）
--
-- 【StampSets.sql のあとに流すこと】
-- SetCode が StampSets.SetCode を外部キーで参照しているため
--
-- 【何度流しても安全な形にしてある】
-- 既にテーブルがあれば、何もしない（中身も消さない）
--
-- 【中身（行）は、Database/InsertData/Stamps.sql が入れる】
-- あちらは素材から機械で書き出すファイルで、手では書き換えない
--
-- 【なぜ IconMaster と別の表にしたのか】
-- IconMaster は SVG の中身そのものを DB に持っているが、スタンプは
-- 絵をファイルで持ち、DB には目次だけを持つ（1個1ファイルの <img> で出すため。
-- PNG や手描きの絵も混ぜられる）。同じ表に入れると
-- 「アイコンの行には SVG があり、スタンプの行は空」という、
-- 1つの表に2つの意味が乗る形になる
--
-- 【viewBox の列は作らない】
-- <img> で出すので、viewBox はファイル自身が持っている。DB に持っても誰も読まない
-- ============================================

IF OBJECT_ID(N'Stamps', N'U') IS NULL
BEGIN

    CREATE TABLE Stamps (

        -- スタンプの名前（英小文字・数字・ハイフン）。例: 'frog-01'
        --
        -- 【一度決めたら、二度と変えられない】
        -- 発言に [stamp:frog-01] の形で焼き込まれ、過去ログに永久に残る。
        -- 変えると、過去の発言がその絵を引けなくなる
        StampCode NVARCHAR(40) NOT NULL PRIMARY KEY,

        -- どのセットに属するか（StampSets.SetCode）
        --
        -- 【外部キーを付けてある】
        -- 存在しないセットを指すスタンプは、どのタブにも出ず、
        -- しかもエラーにならないので気づけない。そういう行を、そもそも作らせない。
        -- ChatMessages の RoomId に外部キーを付けていないのは、書き込みの多いログで
        -- 削除の順序に縛られたくないためで、事情が違う（こちらは数十行の目次）
        SetCode NVARCHAR(20) NOT NULL
            CONSTRAINT FK_Stamps_StampSets REFERENCES StampSets (SetCode),

        -- 絵のファイル名。例: '01-konnichiwa.svg'
        -- 置き場所は Content/Stamps/<SetCode>/<FileName>
        --
        -- 【StampCode と別に持つ理由】
        -- StampCode は発言に焼き込まれるので変えられないが、
        -- ファイル名だけなら後から変えられるようにしておくため
        -- （絵を描き直して形式が変わる、名前の付け方を揃え直す、など）
        FileName NVARCHAR(100) NOT NULL,

        -- 読み上げ用の文言（<img> の alt）。例: 'こんにちは'
        -- スタンプに書かれている文字と同じにしてある
        Label NVARCHAR(50) NOT NULL,

        -- セットの中での並び順
        -- 10刻みで空けてある（StampSets.SortOrder と同じ考え方）
        SortOrder INT NOT NULL,

        -- 1 = パネルに出す、0 = これからは出さない
        --
        -- 【0にしても、過去ログの絵は消えない】
        -- 意味は「これから選べなくする」だけ（StampSets.IsActive と同じ）。
        -- 行を消すと過去ログがその絵を引けなくなるので、消さずに0にすること
        IsActive BIT NOT NULL DEFAULT 1

    );

END
GO


-- ============================================
-- 索引（インデックス）について
-- ============================================
-- StampCode が PRIMARY KEY のため、既にクラスタ化索引が作られている。
-- StampCatalog は表を丸ごと1回で読むだけで、行数も数十～数百行にしかならないため、
-- 追加の索引は作らない（SetCode の外部キーにも索引は付けない）
