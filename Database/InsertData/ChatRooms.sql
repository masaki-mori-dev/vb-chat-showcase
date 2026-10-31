-- ============================================
-- 固定部屋、初期データの投入
-- 開発者が手動で管理する、常設の3部屋
-- ============================================

-- === IsImageUploadEnabled（画像を送れるか）について ===
--
-- 2026年9月10日、E（画像アップロード）の回で値を入れた。
--
--   初心者歓迎！ … 0（送れない）
--   雑談１・雑談２ … 1（送れる）
--
-- 初心者部屋だけ不許可にする。
--
-- 【この値は、必ずこの列で決まる。部屋名では決まらない】
-- コード側で「部屋名が『初心者歓迎！』なら」と書いてはいけない。
-- 部屋名も設定も、あとから変わりうる（部屋名や設定は今後
-- 変えるかもしれない）。**判定は ChatRooms.IsImageUploadEnabled ただ1つ**である。
--
-- ここが名前を書いているのは、**どの行に入れるかを決めているだけ**で、
-- 判定しているわけではない（このファイルは、新しいサーバーを立てるときに
-- 1回だけ流す初期データである）。

INSERT INTO ChatRooms (RoomName, MaxUsers, IsKnockEnabled, PasswordHash, RoomType, OwnerId, IsImageUploadEnabled)
VALUES ('初心者歓迎！', 20, 0, NULL, 'Fixed', NULL, 0);

INSERT INTO ChatRooms (RoomName, MaxUsers, IsKnockEnabled, PasswordHash, RoomType, OwnerId, IsImageUploadEnabled)
VALUES ('雑談１', 20, 0, NULL, 'Fixed', NULL, 1);

INSERT INTO ChatRooms (RoomName, MaxUsers, IsKnockEnabled, PasswordHash, RoomType, OwnerId, IsImageUploadEnabled)
VALUES ('雑談２', 20, 0, NULL, 'Fixed', NULL, 1);
