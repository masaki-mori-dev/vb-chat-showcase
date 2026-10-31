# Scripts/lib/emoji/ — 絵文字ピッカーのファイル

> ## 【版を上げる前に】
> **CLAUDE.md の「Emoji Mart を新しくする日に、見る場所」を、先に読むこと。**
>
> 上げた瞬間には何も起きず、**あとから静かに効いてくる**ものが3つある。
> エラーも出ず、画面も普通に見えるので、知らないと拾えない。
> このファイルにある「作り直し方」だけでは足りない。

このフォルダには、**外から持ってきたもの**と、**それを材料に作った生成物**だけを置く。
自分たちが書いたコード（ピッカーの組み立て・検索・入力欄への挿入）は
`Scripts/chat-room.js` の `EmojiPicker` にあり、ここには置かない。

| ファイル | 何か | 出どころ |
|---|---|---|
| `emoji-mart.js` | ピッカー本体。**無改造** | npm `emoji-mart@5.6.0` の `dist/browser.js`（MIT） |
| `emoji-ja.js` | 絵文字データ。**生成物。中身はJSON**（下記） | 下記の3つを突き合わせて作る |

---

## 【重要】`emoji-ja.js` は、名前は `.js` だが中身は JSON である

**「拡張子が間違っている」と判断して `.json` へ直さないこと。**
わざとこの名前にしてある。

**IIS は既定で `application/json` を圧縮しない。**
圧縮の対象（`httpCompression/staticTypes`）に入っているのは、この6つだけである。

```
text/*   message/*   application/javascript
application/atom+xml   application/xaml+xml   image/svg+xml
```

`.json` のままだと、**673KB が無圧縮のまま流れる**（本番で実測）。
拡張子を `.js` にすると `Content-Type` が `application/javascript` になり、
圧縮の対象に入る。

**実測（まったく同じ中身で比較）**

| 名前 | Content-Type | Content-Encoding | 転送 |
|---|---|---|---|
| `emoji-ja.json` | `application/json` | **無し** | **673,372 B** |
| `emoji-ja.js` | `application/javascript` | **gzip** | **139,686 B** |

**読み方は変えていない。** `fetch` して `Response.json()` で読む。
**あれは `Content-Type` を見ない**ので、名前が `.js` でも JSON として読める。

**却下した2案と、その理由は CLAUDE.md にある。**

---

## emoji-ja.js の材料

| パッケージ | 版 | ライセンス | 使うファイル |
|---|---|---|---|
| `@emoji-mart/data` | 1.2.1 | MIT | `sets/15/native.json` |
| `cldr-annotations-full` | 48.2.0 | Unicode-3.0 | `annotations/ja/annotations.json` |
| `cldr-annotations-derived-full` | 48.2.0 | Unicode-3.0 | `annotationsDerived/ja/annotations.json` |

**これに加えて、リポジトリの中の2本を使う**（npmから落とすものではない）。

| ファイル | 中身 |
|---|---|
| `Tools/yomi-auto.json` | 機械（`pykakasi`）が振った読みがな。2,985語 |
| `Tools/yomi-fix.json` | そのうち間違っていた語を、人が直した表。151語 |

**`fix`にある語は、`auto`の読みを使わない。** 規則と理由は
CLAUDE.md の「読みがな（漢字→ひらがな）は、表で持つ」にある。

## 作り直し方

```
powershell -ExecutionPolicy Bypass -File Tools\build-emoji-data.ps1
```

node も npm も要らない（この開発機には入っていない）。管理者権限も要らない。
走らせるのは、**版を上げるとき**と、**読みがなのデータを足すとき**だけ。
普段の開発では走らせなくてよい。

### 出るはずの数字（2026年9月7日の実測）

```
CLDR 基本の語数      : 1,966
CLDR 基本＋派生      : 4,342
そのまま一致        : 1,526 件
U+FE0F を外して一致 : 344 件
どちらでも不一致    : 0 件
日本語が付いた合計  : 1,870 件 / 1,870 件
読みがな（機械）    : 6,765 件
読みがな（人が修正）: 311 件
出来上がったJSON    : 673,372 B
```

（読みがなを入れる前は 573,719 B だった）

**ずれていたら、そこで止めて理由を突き止めること。**
とくに「不一致」が 0 でないときは、`U+FE0F` の2段引きか、
派生（`annotationsDerived`）の読み込みが抜けている。**どちらも、抜けてもエラーは出ない。**

---

## 【重要】`icon-sprite.svg` とは、扱いが逆になっている

どちらも「生成物」だが、**作る場所が違うので、扱いも逆になる。**

| | `Content/icon-sprite.svg` | `Scripts/lib/emoji/emoji-ja.js` |
|---|---|---|
| 誰が作るか | **本番サーバー上**で `GenerateIconSprite.aspx` が作る | **開発機**で `build-emoji-data.ps1` が作る |
| `.vbproj` | **登録しない** | **`Content` で登録する** |
| 発行 | **されない** | **される** |
| git | 入っている | 入っている |

`icon-sprite.svg` を発行しないのは、**本番で管理画面からアイコンを足した直後に発行すると、
開発機の古い版が本番を上書きしてしまう**ため。
こちらは本番で作られることが無いので、その心配が無い。**普通に発行してよい。**

`emoji-mart.js` のほうも、本番で作られるものではないので同じく `Content` で登録する。

## 【必須】読み込むときは `common.AssetUrl` を通すこと

`Web.Release.config` の `<clientCache>` は**静的ファイル全部に掛かる**（30日）。
`?v=` が付かないと、データを差し替えても30日間ブラウザに届かない。

## ピッカーのUIの日本語（`i18n`）は、ここには無い

`@emoji-mart/data` の `i18n/ja.json`（850 B）に相当するものは、
**`Scripts/chat-room.js` の中に直接書いてある。**

- 画面に出す文言なので、UIのコードと同じ場所にあるほうが直しやすい
- 850 B のために、初回の取得をもう1本増やしたくない
- 発行し忘れる的を1つ増やしたくない（CLAUDE.md の「発行後チェック」）

**文言を変えたくなったら、`chat-room.js` の `EMOJI_I18N` を直す。**
