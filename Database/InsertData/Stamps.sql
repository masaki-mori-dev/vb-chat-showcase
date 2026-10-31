-- スタンプ1枚ごとの目次
-- このファイルは素材から機械で書き出したもの。手で書き換えない。
--
-- StampCode は発言に焼き込まれるので、一度決めたら二度と変えられない。
-- FileName を別に持ってあるのは、ファイル名だけなら後から変えられるようにするため。
-- IsActive は、既にある行では書き換えない（上と同じ理由）。

SET NOCOUNT ON;

MERGE INTO Stamps AS T
USING (VALUES
    (N'frog-01', N'frog', N'01-konnichiwa.svg', N'こんにちは', 10),
    (N'frog-02', N'frog', N'02-ohayou.svg', N'おはよう', 20),
    (N'frog-03', N'frog', N'03-matane.svg', N'またね', 30),
    (N'frog-04', N'frog', N'04-arigatou.svg', N'ありがとう', 40),
    (N'frog-05', N'frog', N'05-otsukaresama.svg', N'おつかれさま', 50),
    (N'frog-06', N'frog', N'06-ryoukai.svg', N'りょうかい', 60),
    (N'frog-07', N'frog', N'07-gomenne.svg', N'ごめんね', 70),
    (N'frog-08', N'frog', N'08-iine.svg', N'いいね', 80),
    (N'frog-09', N'frog', N'09-www.svg', N'www', 90),
    (N'frog-10', N'frog', N'10-nande.svg', N'なんで？', 100),
    (N'frog-11', N'frog', N'11-yoroshiku.svg', N'よろしく', 110),
    (N'frog-12', N'frog', N'12-oyasumi.svg', N'おやすみ', 120),
    (N'frog-13', N'frog', N'13-ganbare.svg', N'がんばれ', 130),
    (N'frog-14', N'frog', N'14-kanashii.svg', N'かなしい', 140),
    (N'frog-15', N'frog', N'15-omedetou.svg', N'おめでとう', 150),
    (N'chick-01', N'chick', N'01-konnichiwa.svg', N'こんにちは', 10),
    (N'chick-02', N'chick', N'02-ohayou.svg', N'おはよう', 20),
    (N'chick-03', N'chick', N'03-matane.svg', N'またね', 30),
    (N'chick-04', N'chick', N'04-arigatou.svg', N'ありがとう', 40),
    (N'chick-05', N'chick', N'05-otsukaresama.svg', N'おつかれさま', 50),
    (N'chick-06', N'chick', N'06-ryoukai.svg', N'りょうかい', 60),
    (N'chick-07', N'chick', N'07-gomenne.svg', N'ごめんね', 70),
    (N'chick-08', N'chick', N'08-iine.svg', N'いいね', 80),
    (N'chick-09', N'chick', N'09-www.svg', N'www', 90),
    (N'chick-10', N'chick', N'10-nande.svg', N'なんで？', 100),
    (N'chick-11', N'chick', N'11-yoroshiku.svg', N'よろしく', 110),
    (N'chick-12', N'chick', N'12-oyasumi.svg', N'おやすみ', 120),
    (N'chick-13', N'chick', N'13-ganbare.svg', N'がんばれ', 130),
    (N'chick-14', N'chick', N'14-kanashii.svg', N'かなしい', 140),
    (N'chick-15', N'chick', N'15-omedetou.svg', N'おめでとう', 150)
) AS S (StampCode, SetCode, FileName, Label, SortOrder)
ON T.StampCode = S.StampCode
WHEN MATCHED THEN
    UPDATE SET T.SetCode = S.SetCode, T.FileName = S.FileName,
               T.Label = S.Label, T.SortOrder = S.SortOrder
WHEN NOT MATCHED THEN
    INSERT (StampCode, SetCode, FileName, Label, SortOrder, IsActive)
    VALUES (S.StampCode, S.SetCode, S.FileName, S.Label, S.SortOrder, 1);

SELECT StampCode, SetCode, FileName, Label, SortOrder, IsActive FROM Stamps ORDER BY SetCode, SortOrder;
