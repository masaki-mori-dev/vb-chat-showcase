# Scripts/quill/ — 記事の本文を「見たまま」書く道具（Quill）

このフォルダには、**外から持ってきたものだけ**を置く（`Scripts/emoji/` と同じ決まり）。
自分たちが書いたコード（ツールバーの設定・Tab の扱い・保存する HTML の組み立て）は
`Scripts/article-editor.js` にあり、ここには置かない。

| ファイル | 何か | 出どころ |
|---|---|---|
| `quill.js` | 本体。**無改造** | npm `quill@2.0.3` の `dist/quill.js` |
| `LICENSE.txt` | ライセンス（BSD-3-Clause）の全文 | 同じパッケージの `LICENSE` |
| `../../Content/quill/quill.snow.css` | 見た目（snow）。**無改造** | 同じパッケージの `dist/quill.snow.css` |

- **ライセンス**＝BSD-3-Clause（Copyright Slab / Jason Chen / salesforce.com）。
  `quill.js` と `quill.snow.css` の先頭にも著作権表示が入っている。**消さないこと**（BSD の条件）。
  中に束ねられている依存（lodash-es・parchment・quill-delta・eventemitter3）は MIT か BSD
- **取り寄せた日**＝2026年9月21日（A-2b）。そのときの最新版（2.0.3。2024年11月30日公開）
- **確かめ方**＝npm の登録簿が持つ `integrity`（sha512）と、落とした tgz の sha512 が一致することを見た
  （`xEYQBqfYx/sfb33VJiKnSJp8ehloavImQ2A6564GAbqG55PGw1dAWUn1MUbQB62t0azawUS2CZZhWCjO8gRvTw==`）
- **CDN から読まない**（自サイトに置く）。よそのサーバーが止まったり中身が差し替わったりしても、書く画面が巻き込まれないため

## 【版を上げる日に、見る場所】

1. **ファイルの更新時刻**。npm から持ってきたファイルは、**更新時刻が1985年10月26日に固定されている**。
   そのまま置くと `common.AssetUrl` の `?v=` が変わらず、**本番の30日キャッシュで、新しい版が届かない**。
   置いたら `touch` などで今の時刻にすること（2.0.3 を置いたときもそうした）
2. **Tab で編集の欄から出られるか**。`Scripts/article-editor.js` は、Quill の中の持ち方
   （`quill.keyboard.bindings.Tab`）を直接空にしている。版が変わると効かなくなりうる
3. **`getSemanticHTML` が空白を `&nbsp;` にするか**。2.0.3 は全部の空白を `&nbsp;` にする
   （`article-editor.js` の `semanticHtml` が戻している）。直った版なら、その手当ては要らなくなる
4. **保存される HTML が、`Articles/ArticleHtml.vb` の許すタグ・属性に収まっているか**。
   箇条書きの形（`<ul>` か `<ol data-list>` か）が変わると、保存で箇条書きが番号付きに化ける
5. **末尾の `sourceMappingURL`**。`.map` は置いていないので、開発者ツールを開いたときだけ
   404 の警告が1行出る。**害は無い**（本体を無改造に保つため、行を消していない）
