# Scripts/lib/ — 外から持ってきたものだけを置く

**2026年9月24日に分けた**（フォルダの並べ直し・その2）。
リポジトリを開いた人が「**この人が書いたのはどれか**」を一目で見分けられるようにするため。

| どこ | 何が入っているか |
|---|---|
| **`Scripts/` の直下** | **自分たちが書いた7本**（`chat-room.js`・`lounge-live.js`・`lounge-enter.js`・`site.js`・`sounds.js`・`icon-sprite.js`・`article-editor.js`） |
| **`Scripts/lib/`（ここ）** | **外から持ってきたもの。原則として無改造** |
| `Content/lib/` | 同じ線引きの、CSS 版（今は `quill/quill.snow.css` の1本だけ） |

## 中身

| | 版 | 何か | 出どころ |
|---|---|---|---|
| `jquery-3.7.1.js` / `.min.js` / `.intellisense.js` | 3.7.1 | jQuery | NuGet（`.js` と `.intellisense.js` は発行しない＝`.vbproj` の `None`） |
| `jquery.signalR-2.4.3.js` / `.min.js` | 2.4.3 | SignalR のクライアント | NuGet |
| `jscolor.min.js` | — | 色を選ぶ道具。部屋の「見た目」とトップページで使う | 配布元から |
| `quill/` | 2.0.3 | 記事を「見たまま」書く道具。**詳しくは `quill/README.md`** | npm |
| `emoji/` | — | 絵文字ピッカー（Emoji Mart）と、その日本語データ。**詳しくは `emoji/README.md`** | npm ＋ Unicode CLDR |
| `WebForms/` | — | **ASP.NET Web Forms に同梱されているもの**（Visual Studio のテンプレートが置いた） | Microsoft |

## 触るときの決まり

- **中身を書き換えないこと。** 直したいことがあれば、自分たちのファイル（`Scripts/` の直下）の側でやる。
  Quill のツールバーの設定・保存する HTML の組み立ては `Scripts/article-editor.js` にある
- **版を上げたら、それぞれの `README.md` の「版を上げる日に、見る場所」を読むこと**
- **npm から持ってきたファイルは、更新時刻が1985年10月26日に固定されている。**
  そのまま置くと `common.AssetUrl` の `?v=` が変わらず、**本番の30日キャッシュで新しい版が届かない**。
  置いたら今の時刻にすること
- **CDN からは読まない**（自サイトに置く）。よそのサーバーが止まったり、中身が差し替わったりしても巻き込まれないため

## `WebForms/` について

**今はどこからも配られていない。** `App_Start/BundleConfig.vb` に束（`~/bundles/WebFormsJs`・
`~/bundles/MsAjaxJs`）として登録してあるが、`Scripts.Render` している画面が1つも無い。

**それでも消していないのは、管理画面で Validator を使う回に関わる可能性があるため**
（CLAUDE.md の「宿題リスト」の宿題5。非侵入型検証は `"jquery"` という名前の
`ScriptResourceMapping` を要求し、それは `Global.asax.vb` が登録している）。
