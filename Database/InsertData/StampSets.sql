-- スタンプのセット（目次）
-- このファイルは素材から機械で書き出したもの。手で書き換えない。
-- 絵を足したら、スクリプトを走らせ直すと、絵と目次が同時に更新される。
--
-- 何度流しても安全な形にしてある。IsActive は、既にある行では書き換えない
--（管理画面から非公開にしたセットが、流し直しで復活しないようにするため）。

SET NOCOUNT ON;

MERGE INTO StampSets AS T
USING (VALUES
    (N'frog', N'カエル', 10),
    (N'chick', N'ヒヨコ', 20)
) AS S (SetCode, DisplayName, SortOrder)
ON T.SetCode = S.SetCode
WHEN MATCHED THEN
    UPDATE SET T.DisplayName = S.DisplayName, T.SortOrder = S.SortOrder
WHEN NOT MATCHED THEN
    INSERT (SetCode, DisplayName, SortOrder, IsActive)
    VALUES (S.SetCode, S.DisplayName, S.SortOrder, 1);

SELECT SetCode, DisplayName, SortOrder, IsActive FROM StampSets ORDER BY SortOrder;
