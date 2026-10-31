<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="Edit.aspx.vb" Inherits="vb_chat.ArticleEditPage" Title="記事を書く" ValidateRequest="false" MaintainScrollPositionOnPostback="true" EnableEventValidation="false" %>
<%-- 【EnableEventValidation="false"】この画面の中で作ったカテゴリは、描いたときの選択肢に無い値として送られる。
     ASP.NET の「送られてきた値の検査」（EventValidation）は、それを例外で止める（保存のたびに500になる）。
     切ってよい理由＝送られてくる値は、どれもサーバーで確かめ直している（カテゴリは自分のものか＝ArticleStore.Save、
     目次・見出しの番号は NormalizeTocMode／NormalizeHeadingNumberMode、隠しているボタン＝削除は番号が0なら何もしない） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         発行の前に、【発行前チェック】の grep を走らせること。

         【ValidateRequest="false" にしてある】（A-2a）
         ASP.NET は既定で、「< が入っている入力」をページに届く前に例外で止める。
         **記事の本文は HTML なので、この画面だけはそれを切らないと1文字も保存できない**
         （開発機で実際に踏んだ。止まるのは Page_Load より前なので、こちらのコードは走らない）。

         【この1ページだけである。Web.config で site 全体を切らないこと】
         切ると、チャットの発言も部屋名も検証を通らなくなる。
         このページを切ってよい理由は3つ——
           ・ログインした管理人しか開けない（Articles/Edit.aspx.vb は AccountPageBase を継承）
           ・本文は、保存のときに必ず HtmlSanitizer を通る（Articles/ArticleHtml.vb）
           ・タイトルは本文ではないので、今までどおり出口で <%: %> を通してエスケープする

         【Quill に差し替えても、この行は要る】
         見たまま編集は、結果を HTML として送ってくるためである

         【Validator を使っている】
         入れなかったが、入力の多いフォームになったので、この回で入れた。
         非侵入型検証は "jquery" という名前の ScriptResourceMapping を要求するので、
         Global.asax.vb の Application_Start で登録してある。
         **登録していないと、このページが500で止まる**（
         「管理画面を作る回に、必ず戻ってくる場所」）。

         【Validator は親切であって、防御ではない】
         開発者ツールから外せる。本当の検査は Articles/ArticleRules.vb と
         Articles/ArticleStore.vb にあり、保存の口で必ず通る --%>

    <%-- 【Quill の見た目（snow）】この1ページでしか使わないので、Site.Master の <head> には足さずに、ここで読む。
         <link rel="stylesheet"> は body の中に置いてもよい（HTML の決まりで許されている）。
         **本文の欄より前に置くこと**——後ろに置くと、見た目の付いていない編集の欄が一瞬出る。
         【Asset（common.AssetUrl）を必ず通す】通し忘れると、版を上げても30日間ブラウザに届かない。
         Quill の上書き（日本語の文言・ボタンの大きさなど）は Content/Site.css にあり、あちらはあとに読まれる必要は無い
         （セレクタを Quill より細かくしてある） --%>
    <link href="<%: Asset("~/Content/lib/quill/quill.snow.css") %>" rel="stylesheet" type="text/css" />

    <div class="admin-box">

        <%-- 【見出しの横に、使っている量】（要望：記事の画面にこそ必要）
             例「記事 3/200　画像 0/50MB」。記事一覧の上の件数の行と、この画面の2か所だけに出す（Articles/ArticleUsage.vb）。
             入りきらなければ見出しの下へ回る（中身任せの折り返し。幅の数字は書いていない） --%>
        <div class="admin-heading-row">
            <h1><asp:Literal ID="HeadingText" runat="server" /></h1>
            <%= HeadingUsageHtml %>
        </div>

        <%-- 【記事のページへのリンクは、保存の帯へ移した】（指摘：スクロールの手間が大きい）
             保存すると元のスクロール位置に戻るので、ここ（いちばん上）にあったリンクは画面の外になっていた。
             今は下の保存の帯の中、「保存する」の隣にある --%>

        <%-- 【data-error-jump】サーバーで断った理由が「どの欄の話か」分かるときは、その欄の上へ移る
             （Scripts/site.js の「9」。印は Edit.aspx.vb の ShowError が FormErrors.MarkField で付ける）。
             保存の帯にも同じ理由が出る（帯はいつも見えている） --%>
        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- 【編集中の控えがあるときの知らせ】
             公開中の記事を書きかけのまま離れたあと、開き直したときに出る。画面の中身は控え（Edit.aspx.vb の LoadArticle）。
             「反映する（保存）」＝ふつうの保存と同じ（保存の検査も通る）。「未反映の変更を破棄する」＝控えだけを消して開き直す
             （「この編集を捨てる」から改名。保存の帯の［破棄する］と同じもの＝同じ名前・同じ確かめの文・同じ処理）。
             【どちらも POST】取り返しのつかない操作なので、GET のリンクにしない（一覧の削除と同じ理由）。
             捨てるほうだけ、押す前に確かめる（data-confirm。Scripts/site.js の「5」） --%>
        <asp:PlaceHolder ID="DraftNotice" runat="server" Visible="False">
            <div id="articleDraftNotice" class="admin-draft-notice" role="status">
                <p><asp:Literal ID="DraftNoticeText" runat="server" /></p>
                <p class="admin-draft-notice-actions">
                    <asp:Button ID="ApplyDraftButton" runat="server" Text="反映する（保存）"
                                CssClass="account-submit" ValidationGroup="articlePublish" />
                    <asp:Button ID="DiscardDraftButton" runat="server" formnovalidate="formnovalidate" Text="未反映の変更を破棄する"
                                CssClass="admin-delete-btn" CausesValidation="False"
                                data-confirm="未反映の変更を破棄します。よろしいですか？"
                                data-confirm-ok="破棄する" />
                </p>
            </div>
        </asp:PlaceHolder>

        <div class="form-row">
            <asp:Label ID="TitleLabel" runat="server" AssociatedControlID="TitleBox"
                       CssClass="admin-label article-title-label" Text="タイトル" />
            <asp:TextBox ID="TitleBox" runat="server" CssClass="account-input article-title-input" MaxLength="200" placeholder="タイトル" data-autosave-field="title" />
            <%-- 【タイトルと本文が要るのは、公開する保存だけ】（決定：下書きの保存には本文は要らない。タイトルも要らない）
                 なので、この2つは articlePublish の組にだけ置いてある（下書き保存・非公開の保存のボタンは article の組） --%>
            <asp:RequiredFieldValidator ID="TitleRequired" runat="server"
                                        ControlToValidate="TitleBox"
                                        ValidationGroup="articlePublish"
                                        Display="Dynamic"
                                        CssClass="admin-validator"
                                        ErrorMessage="タイトルを入力してください。" />
        </div>

        <div class="form-row form-row--body">
            <asp:Label ID="BodyLabel" runat="server" AssociatedControlID="BodyBox"
                       CssClass="admin-label article-body-label" Text="本文" />
            <%-- 【本文は「見たまま」書く（Quill）。ボタンと HTMLモードを足した】
                 動きは Scripts/article-editor.js。見た目は Content/Site.css の「記事の本文を書く画面」。

                 【送る口は、今までどおりこの textarea（BodyBox）】
                 JS が Quill の中身（HTML）を、書き換えるたびにここへ書き写す。
                 **送る直前に、JS がこの欄の値を「包む」（Base64）**——本番の WAF が、説明の文に書いた
                 「script」のような文字だけで保存を 403 にするため。サーバー（Edit.aspx.vb の DecodeBody）が戻してから
                 HtmlSanitizer を通す。仕組みと理由は Core/BodyTransport.vb。
                 **JS か Quill が読み込めなかったときは、この textarea がそのまま見えて、今までの形で書ける**
                 （そのときは包まれないので、WAF に止められうる）。

                 【data-article-editor】JS がこの textarea を見つけるための印。
                 【TextMode="MultiLine" は <textarea> になる】ASP.NET が中身をエスケープして描き直すので、
                 保存されている HTML が壊れることはない --%>
            <asp:TextBox ID="BodyBox" runat="server" CssClass="admin-body-input"
                         TextMode="MultiLine" Rows="18" data-article-editor="1" />

            <%-- 【Quill の置き場所。JS が動くまで隠してある】
                 動かなければ出てこない（押しても何も起きないボタンを見せないため）。

                 【data-allowed】保存のときに使える書き方の一覧（JSON）。Articles/ArticleHtml.vb が持つものを
                 Edit.aspx.vb が渡している。HTMLモードで「使えない書き方」を見分けて知らせるのに使う。
                 【data-encoded-prefix】包んだ本文の先頭の印（Core/BodyTransport.vb と同じ値）
                 【data-initial-body】保存されている本文を包んだ形（Core/BodyTransport.vb の Encode）。JS はこちらから読み込む
                 （ページの応答の途中で生の HTML の一部が書き換えられても、書く欄の本文が変わらないように）
                 【data-jscolor-url】「その他の色…」のカラーピッカー（jscolor。62KB）の場所。初めて押したときに取りに行く
                 （部屋のアイコンの色と同じ道具・同じ読み込み方。Asset＝common.AssetUrl を通す）

                 【ツールバーのボタンを markup に書いている】名前（aria-label）と、
                 マウスを乗せたときの説明（title）を日本語で持たせるため。
                 ql-〇〇 の class のボタンは、Quill（snow）が中の絵を入れて、押したときの動きも受け持つ。
                 ql- の付かないボタン（色・HTML）は、article-editor.js が受け持つ。

                 【type="button" を必ず書くこと】書かないと、form の中のボタンは「送信」になる。
                 押すたびにページが保存されてしまう。

                 【ボタンを足すときは、3か所をそろえる】ここ／Scripts/article-editor.js の FORMATS／
                 Articles/ArticleHtml.vb の許すタグ・属性・class。画面で付けられる書式が保存で消えると、
                 利用者には「書いたのに消えた」としか見えない。

                 【data-momentary】押しっぱなしにならないボタン（元に戻す・やり直す・書式を消す・区切り線）。
                 読み上げに「押されている／いない」を伝えない（article-editor.js の syncPressed） --%>
            <div id="articleEditor" class="article-editor" hidden="hidden"
                 data-allowed="<%: EditorAllowedJson %>"
                 data-encoded-prefix="<%: BodyEncodedPrefix %>"
                 data-article-id="<%: ArticleIdText %>"
                 data-image-upload-url="/Articles/UploadImage.aspx"
                 data-image-max-bytes="<%: ImageMaxBytesText %>"
                 data-image-max-edge="<%: ImageMaxEdgeText %>"
                 data-image-quality="<%: ImageQualityText %>"
                 data-autosave-url="<%: AutoSaveUrl %>"
                 data-jscolor-url="<%: Asset("~/Scripts/lib/jscolor.min.js") %>"
                 data-initial-body="<%: InitialBodyEncoded %>">

                <%-- 【ツールバーと色の小窓は、1つの箱ごと画面の上に残す】（position: sticky は、この箱に付けてある）
                     色の小窓をツールバーの中に置かないのは、狭い画面でツールバーを横にスクロールさせているため
                     （中に置くと、はみ出した小窓が切られる） --%>
                <div class="article-editor-head">

                    <div id="articleToolbar" class="article-editor-toolbar" role="toolbar" aria-label="本文の書式">
                        <span class="ql-formats">
                            <button type="button" class="ql-undo" data-momentary="1" aria-label="元に戻す" title="元に戻す（Ctrl+Z）">
                                <svg viewBox="0 0 18 18" aria-hidden="true" focusable="false"><path class="ql-stroke" d="M5 8h7a3.5 3.5 0 0 1 0 7H8"></path><polyline class="ql-stroke" points="8 5 5 8 8 11"></polyline></svg>
                            </button>
                            <button type="button" class="ql-redo" data-momentary="1" aria-label="やり直す" title="やり直す（Ctrl+Y）">
                                <svg viewBox="0 0 18 18" aria-hidden="true" focusable="false"><path class="ql-stroke" d="M13 8H6a3.5 3.5 0 0 0 0 7h4"></path><polyline class="ql-stroke" points="10 5 13 8 10 11"></polyline></svg>
                            </button>
                        </span>
                        <span class="ql-formats">
                            <button type="button" class="ql-header" value="2" aria-label="大見出し" title="大見出し"></button>
                            <button type="button" class="ql-header" value="3" aria-label="小見出し" title="小見出し"></button>
                        </span>
                        <span class="ql-formats">
                            <button type="button" class="ql-bold" aria-label="太字" title="太字（Ctrl+B）"></button>
                            <%-- 【日本語は、書体によって斜めにならないことがある】Content/Site.css の「斜体」 --%>
                            <button type="button" class="ql-italic" aria-label="斜体" title="斜体（Ctrl+I）。日本語は、端末の書体によっては斜めにならないことがあります"></button>
                            <button type="button" class="ql-underline" aria-label="下線" title="下線（Ctrl+U）"></button>
                            <button type="button" class="ql-strike" aria-label="取り消し線" title="取り消し線"></button>
                        </span>
                        <%-- 【色のボタンの中の、見えない文字を消さないこと】
                             カラーピッカー（jscolor）は、文字を持たないボタンを「空」とみなして中身を全部消す（絵が消えた）。
                             見えない文字（.visually-hidden）があれば空とみなされない。絵は JS がこの文字の前に足す --%>
                        <span class="ql-formats">
                            <button type="button" id="articleColorBtn" class="article-menu-btn" data-panel="articleColorPanel"
                                    aria-label="文字の色" title="文字の色" aria-expanded="false" aria-controls="articleColorPanel"><span class="visually-hidden">文字の色</span></button>
                            <button type="button" id="articleBackgroundBtn" class="article-menu-btn" data-panel="articleBackgroundPanel"
                                    aria-label="背景の色" title="背景の色（マーカー）" aria-expanded="false" aria-controls="articleBackgroundPanel"><span class="visually-hidden">背景の色</span></button>
                        </span>
                        <span class="ql-formats">
                            <button type="button" class="ql-align" value="" aria-label="左寄せ" title="左寄せ"></button>
                            <button type="button" class="ql-align" value="center" aria-label="中央寄せ" title="中央寄せ"></button>
                            <button type="button" class="ql-align" value="right" aria-label="右寄せ" title="右寄せ"></button>
                        </span>
                        <span class="ql-formats">
                            <button type="button" class="ql-list" value="bullet" aria-label="箇条書き" title="箇条書き"></button>
                            <button type="button" class="ql-list" value="ordered" aria-label="番号付きの箇条書き" title="番号付きの箇条書き"></button>
                            <button type="button" class="ql-blockquote" aria-label="引用" title="引用"></button>
                            <button type="button" class="ql-code-block" aria-label="コードの囲み" title="コードの囲み（プログラムなどを、そのままの形で見せる）"></button>
                            <button type="button" class="ql-divider" data-momentary="1" aria-label="区切り線を入れる" title="区切り線を入れる">
                                <svg viewBox="0 0 18 18" aria-hidden="true" focusable="false"><line class="ql-stroke" x1="2" x2="16" y1="9" y2="9"></line><line class="ql-stroke ql-thin" x1="5" x2="13" y1="4" y2="4"></line><line class="ql-stroke ql-thin" x1="5" x2="13" y1="14" y2="14"></line></svg>
                            </button>
                        </span>
                        <span class="ql-formats">
                            <button type="button" class="ql-link" aria-label="リンク" title="リンク（Ctrl+K）"></button>
                            <%-- 【画像】押すと写真を選ぶ画面が開き、貼る前に確かめの小窓（#articleImageDialog）が出る。
                                 動きは Scripts/article-editor.js の「記事の画像」（Quill の既定の画像の動き＝data: で本文に入れる、は使わない） --%>
                            <button type="button" class="ql-image" data-momentary="1" aria-label="画像を貼る" title="画像を貼る（写真などを選ぶ。貼る前に確かめられます）"></button>
                            <%-- 【「元に戻す」と取り違えられた】（テスト）名前を「書式を消す」にし、説明にも書いた --%>
                            <button type="button" class="ql-clean" data-momentary="1" aria-label="書式を消す" title="書式を消す（選んだ文字を、ふつうの文字に戻す。1つ前に戻すのは ↶ のボタン）"></button>
                        </span>
                        <span class="ql-formats">
                            <%-- 【HTMLモード】書く画面と、HTML を直接見る・直す画面を切り替える。押しっぱなしのボタン（aria-pressed） --%>
                            <button type="button" id="articleHtmlToggle" class="article-html-toggle" aria-pressed="false"
                                    aria-label="HTMLで見る・直す" title="HTMLで見る・直す（もう一度押すと、書く画面に戻ります）">HTML</button>
                        </span>
                    </div>

                    <%-- 【ツールバーの右端の「保存」は、やめた】
                         下の保存の帯を、打っている間もキーボードのすぐ上に出すようにしたため（指摘：スマホこそ下の帯のボタンが要る）。
                         2つあると「どっちを押すか」になる。そのうえ、この「保存」は押してもカーソルを奪わない作りだったので、
                         **変換中の字を確定させないまま保存していた**（未確定の字が消える・本文が空と断られる。Galaxy で起きた） --%>

                    <%-- 【よく使う色 ＋ その他の色…】（決まった数色、「その他の色…」を足した）
                         data-value の名前から、JS が Content/theme.css の --article-color-〇〇 ／ --article-bg-〇〇 の色（#RRGGBB）を読んで付ける。
                         **名前を足したら、theme.css にも足すこと**（見本の色は Content/Site.css の .swatch-color-〇〇 ／ .swatch-bg-〇〇）。
                         空の data-value は「色を外す」。
                         data-more の「その他の色…」は、部屋のアイコンの色と同じカラーピッカー（jscolor）を開く。
                         保存のときにサニタイザーが通すのは #RRGGBB だけ（Articles/ArticleHtml.vb の NormalizeStyle） --%>
                    <div id="articleColorPanel" class="article-color-panel" role="group" aria-label="文字の色を選ぶ" hidden="hidden" data-format="color">
                        <button type="button" data-value="" aria-label="色なし（ふつうの文字）" title="色なし"><span class="article-swatch">あ</span></button>
                        <button type="button" data-value="red" aria-label="赤" title="赤"><span class="article-swatch swatch-color-red">あ</span></button>
                        <button type="button" data-value="orange" aria-label="だいだい" title="だいだい"><span class="article-swatch swatch-color-orange">あ</span></button>
                        <button type="button" data-value="green" aria-label="緑" title="緑"><span class="article-swatch swatch-color-green">あ</span></button>
                        <button type="button" data-value="blue" aria-label="青" title="青"><span class="article-swatch swatch-color-blue">あ</span></button>
                        <button type="button" data-value="purple" aria-label="紫" title="紫"><span class="article-swatch swatch-color-purple">あ</span></button>
                        <button type="button" data-value="gray" aria-label="灰色" title="灰色"><span class="article-swatch swatch-color-gray">あ</span></button>
                        <button type="button" class="article-color-more" data-more="1" aria-label="その他の色（色を選ぶ道具を開く）" title="その他の色">その他の色…</button>
                    </div>
                    <div id="articleBackgroundPanel" class="article-color-panel" role="group" aria-label="背景の色を選ぶ" hidden="hidden" data-format="background">
                        <button type="button" data-value="" aria-label="背景の色なし" title="背景の色なし"><span class="article-swatch">あ</span></button>
                        <button type="button" data-value="yellow" aria-label="黄色" title="黄色"><span class="article-swatch swatch-bg-yellow">あ</span></button>
                        <button type="button" data-value="pink" aria-label="ピンク" title="ピンク"><span class="article-swatch swatch-bg-pink">あ</span></button>
                        <button type="button" data-value="green" aria-label="緑" title="緑"><span class="article-swatch swatch-bg-green">あ</span></button>
                        <button type="button" data-value="blue" aria-label="青" title="青"><span class="article-swatch swatch-bg-blue">あ</span></button>
                        <button type="button" data-value="gray" aria-label="灰色" title="灰色"><span class="article-swatch swatch-bg-gray">あ</span></button>
                        <button type="button" class="article-color-more" data-more="1" aria-label="その他の背景の色（色を選ぶ道具を開く）" title="その他の色">その他の色…</button>
                    </div>

                    <%-- 書く画面からのお知らせ（HTMLモードから戻したときに「使えない書き方を取り除きました」など）。
                         空のときは高さを持たない。読み上げにも伝える（role="status"） --%>
                    <p id="articleEditorNotice" class="article-editor-notice" role="status" aria-live="polite"></p>

                </div>

                <div id="articleEditorBody" class="article-editor-body" data-placeholder="ここに本文を書きます。"></div>

                <%-- 【HTMLモードの欄】name を付けていない＝送られない（送るのは BodyBox だけ。包むのもそちら）。
                     使える書き方は、ボタンで作れるものと同じ範囲。範囲の外（iframe・style など）は、
                     書く画面へ戻したとき・保存するときに取り除かれ、そのことを知らせる --%>
                <textarea id="articleHtmlSource" class="article-html-source" rows="18" hidden="hidden"
                          spellcheck="false" aria-label="本文のHTML" aria-describedby="articleHtmlHint"></textarea>
                <%-- 【古い文を直した】「決まった色」→ 色は #RRGGBB ならどの色でも通る（Articles/ArticleHtml.vb の NormalizeStyle）。
                     【見えている1行だけにして、くわしくは「?」へ】 --%>
                <div id="articleHtmlHint" class="admin-hint admin-hint--body" hidden="hidden">
                    ボタンで作れる書き方だけが残ります
                    <%= HelpButton("article.html") %>
                </div>

            </div>

            <asp:RequiredFieldValidator ID="BodyRequired" runat="server"
                                        ControlToValidate="BodyBox"
                                        ValidationGroup="articlePublish"
                                        Display="Dynamic"
                                        CssClass="admin-validator"
                                        ErrorMessage="本文を入力してください。" />
        </div>

        <%-- 【古い文を直した】「決まった色以外の色」→ 色は #RRGGBB ならどの色でも通る。
             【見えている1行だけにして、くわしくは「?」へ】この1行は、本文の欄の aria-describedby（article-editor.js がつなぐ） --%>
        <div id="articleBodyHint" class="admin-hint admin-hint--body">
            使えない書式は、自動で取り除かれます
            <%= HelpButton("article.body") %>
        </div>

        <%-- ============================================================
             【タイトルと本文のあと＝それ以外の設定】（要望：タイトルと本文をいちばん上にし、それ以外を本文の下にする）
             それまでは、タイトル → URL の名前 → カテゴリ → 並び順 → 固定 → 目次 → 見出しの番号 → 本文、の順で、
             スマホでは本文にたどり着くまでに画面2枚ぶんかかっていた。PC も同じ並び。開け閉めの箱にはしていない。

             【説明は、入りきるなら全文を見せ、入りきらないときだけ「?」】
             横幅があれば入力欄の横、無ければ下（中身任せの折り返し）。2行に入りきらないときだけ「…」で切って「?」を出し、
             「?」の中には同じ文の全文が入る。部品は Scripts/site.js の「4」の後半（.fit-hint）。**文を書くのは .fit-hint-text の1か所だけ**
             （「?」の中の文は、JS がここから写す＝同じ文を2か所に書かない）。
             欄の aria-describedby は .fit-hint-text の id を指す（見た目は切れても、読み上げには全文が届く）。

             【.form-row-main】入力欄と説明をひとまとめにする箱。説明が折り返したとき、項目名の下ではなく入力欄の左端から始まるように。
             【data-error-anchor】断った理由を、この箱の前（＝項目名の右）に出すための印（Scripts/site.js の「8」）
             ============================================================ --%>
        <%-- 【お試し】（開発者の判断：効果が分からないので、まず試す）
             「そのほかの設定」を見出し2つに分けた＝「置き場所」（記事のアドレス・カテゴリ・カテゴリの中の順番・トップに固定）と
             「本文の見た目」（目次・見出しの番号）。**戻すときは、この h2 の文を「そのほかの設定」に戻し、下の「本文の見た目」の h2 を消すだけ** --%>
        <%-- 【まとまりの箱 .admin-group】スマホ幅でだけ、まとまりごとに地の色を少し変える
             （どこからどこまでが一つのまとまりか分かるように）。PC の見た目は変えていない。Content/Site.css の .admin-group --%>
        <div class="admin-group">
        <h2 class="admin-subhead">置き場所</h2>

        <%-- 【記事のアドレス】（「URL の名前」から名前を変えた）
             付けると /articles/ユーザー名/guide のような URL になる。
             メニューの「使い方」などの行き先は、この名前で決まる（名前の一覧は Articles/SiteArticles.vb）。
             【検査の本命はサーバー】ArticleRules.ValidateSlug（小文字にしてから見る）。重なりは ArticleStore の保存の文の中。
             決まり（英小文字で始める・ほかの記事と重ならない・サイトのメニューの名前）は説明に書かず、外れたら断る理由で伝える。
             下の RegularExpressionValidator は親切（大文字は通す＝サーバーで小文字にする）。
             【検査の2つの組】article（そのまま保存）と articlePublish（公開する保存）。同じ検査を両方に置いてある
             （1つのボタンは1つの組しか検査しないため。Edit.aspx.vb の OnPreRender が、ボタンごとに組を決める） --%>
        <div class="form-row">
            <asp:Label ID="SlugLabel" runat="server" AssociatedControlID="SlugBox"
                       CssClass="admin-label" Text="記事のアドレス" />
            <div class="form-row-main" id="articleSlugMain">
                <asp:TextBox ID="SlugBox" runat="server" CssClass="account-input admin-slug-input" MaxLength="40" data-autosave-field="slug"
                             autocomplete="off" spellcheck="false" autocapitalize="none" aria-describedby="articleSlugHint"
                             data-error-anchor="articleSlugMain" />
                <asp:RegularExpressionValidator ID="SlugFormat" runat="server"
                                                ControlToValidate="SlugBox"
                                                ValidationGroup="article"
                                                Display="Dynamic"
                                                CssClass="admin-validator"
                                                ValidationExpression="[A-Za-z][A-Za-z0-9\-]{0,39}"
                                                ErrorMessage="記事のアドレスは、英字で始めて、英字・数字・ハイフン（-）だけで入力してください（例 guide）。" />
                <asp:RegularExpressionValidator ID="SlugFormatPublish" runat="server"
                                                ControlToValidate="SlugBox"
                                                ValidationGroup="articlePublish"
                                                Display="Dynamic"
                                                CssClass="admin-validator"
                                                ValidationExpression="[A-Za-z][A-Za-z0-9\-]{0,39}"
                                                ErrorMessage="記事のアドレスは、英字で始めて、英字・数字・ハイフン（-）だけで入力してください（例 guide）。" />
                <div class="fit-hint"><span class="fit-hint-text" id="articleSlugHint">英小文字・数字・ハイフン（-）だけ。空欄なら番号になります。入力すると記事のアドレスに使われます。例：guide と入力 → <%: SlugExampleUrl %></span></div>
            </div>
        </div>

        <%-- 【カテゴリは、この画面の中で作れる】（開発者が採用）
             「＋ 新しいカテゴリ」を押すと、その場に名前の欄が出る。追加すると、すぐそのカテゴリが選ばれる。
             書く画面からは離れない（ページを読み込み直さない＝WebMethod。Edit.aspx.vb の AddCategory）。
             並べ方は「新しい順」で作る（あとでカテゴリの画面で変えられる）。
             同じ名前・空の名前の断り方は、カテゴリの画面と同じ（ArticleCategoryStore.Create の1本の道）。理由は名前の欄の上に出す。
             【ボタンは JS が動いてから出す】動かなければ押しても何も起きないので、hidden のまま。
             【カテゴリを整理する】（「＋ 新しいカテゴリ」の横へ）名前・並べ方・削除はカテゴリの画面で。
             **新しいタブで開く**（書きかけを失わないため）。入りきらなければ下へ回る（中身任せの折り返し） --%>
        <div class="form-row">
            <asp:Label ID="CategoryLabel" runat="server" AssociatedControlID="CategoryList"
                       CssClass="admin-label" Text="カテゴリ" />
            <div class="form-row-main article-category-pick" id="articleCategoryMain">
                <asp:DropDownList ID="CategoryList" runat="server" CssClass="admin-select" data-autosave-field="category"
                                  data-error-anchor="articleCategoryMain" />
                <button type="button" id="articleCategoryAddBtn" class="admin-link-btn" hidden="hidden"
                        aria-expanded="false" aria-controls="articleCategoryAdd">＋ 新しいカテゴリ</button>
                <a href="/account/categories" class="admin-link-btn" target="_blank" rel="noopener"
                   aria-label="カテゴリを整理する（新しいタブで開きます）">カテゴリを整理する</a>
            </div>
        </div>

        <div id="articleCategoryAdd" class="article-category-add" hidden="hidden" data-validate-scope="1"
             data-add-url="/Articles/Edit.aspx/AddCategory">
            <label for="articleCategoryNewName" class="admin-label">新しいカテゴリの名前</label>
            <input type="text" id="articleCategoryNewName" class="account-input" maxlength="50" autocomplete="off"
                   data-error-label="カテゴリの名前" />
            <span class="article-category-add-actions">
                <button type="button" id="articleCategoryAddOk" class="account-submit">追加</button>
                <button type="button" id="articleCategoryAddCancel" class="admin-link-btn">やめる</button>
            </span>
        </div>

        <%-- 【状態の選択は、保存の帯へ移した】
             今は、保存の帯に「今の状態の印」とボタン2つが並ぶ（ArticleRules の保存の帯の節） --%>

        <div class="form-row">
            <asp:Label ID="SortOrderLabel" runat="server" AssociatedControlID="SortOrderBox"
                       CssClass="admin-label" Text="カテゴリの中の順番" />
            <div class="form-row-main" id="articleSortOrderMain">
                <asp:TextBox ID="SortOrderBox" runat="server" CssClass="admin-num-input" data-autosave-field="sortOrder"
                             MaxLength="10" inputmode="numeric" data-number-field="1" data-number-min="0" data-number-max="9999" data-number-default="" aria-describedby="articleSortOrderHint"
                             data-error-anchor="articleSortOrderMain" />
                <asp:RangeValidator ID="SortOrderRange" runat="server"
                                    ControlToValidate="SortOrderBox"
                                    ValidationGroup="article"
                                    Display="Dynamic"
                                    CssClass="admin-validator"
                                    Type="Integer" MinimumValue="0" MaximumValue="9999"
                                    ErrorMessage="カテゴリの中の順番は0〜9999の数で入力してください。" />
                <asp:RangeValidator ID="SortOrderRangePublish" runat="server"
                                    ControlToValidate="SortOrderBox"
                                    ValidationGroup="articlePublish"
                                    Display="Dynamic"
                                    CssClass="admin-validator"
                                    Type="Integer" MinimumValue="0" MaximumValue="9999"
                                    ErrorMessage="カテゴリの中の順番は0〜9999の数で入力してください。" />
                <div class="fit-hint"><span class="fit-hint-text" id="articleSortOrderHint">カテゴリを「指定した順」にしたときの順番です。空欄の記事は、番号の付いた記事のあとに新しい順で並びます。</span></div>
            </div>
        </div>

        <%-- 【トップに固定】（「固定表示」から名前を変えた。中身は Articles.PinOrder のまま） --%>
        <div class="form-row">
            <asp:Label ID="PinOrderLabel" runat="server" AssociatedControlID="PinOrderBox"
                       CssClass="admin-label" Text="トップに固定" />
            <div class="form-row-main" id="articlePinOrderMain">
                <asp:TextBox ID="PinOrderBox" runat="server" CssClass="admin-num-input" data-autosave-field="pinOrder"
                             MaxLength="10" inputmode="numeric" data-number-field="1" data-number-min="0" data-number-max="9999" data-number-default="" aria-describedby="articlePinOrderHint"
                             data-error-anchor="articlePinOrderMain" />
                <asp:RangeValidator ID="PinOrderRange" runat="server"
                                    ControlToValidate="PinOrderBox"
                                    ValidationGroup="article"
                                    Display="Dynamic"
                                    CssClass="admin-validator"
                                    Type="Integer" MinimumValue="0" MaximumValue="9999"
                                    ErrorMessage="トップに固定する順は0〜9999の数で入力してください。" />
                <asp:RangeValidator ID="PinOrderRangePublish" runat="server"
                                    ControlToValidate="PinOrderBox"
                                    ValidationGroup="articlePublish"
                                    Display="Dynamic"
                                    CssClass="admin-validator"
                                    Type="Integer" MinimumValue="0" MaximumValue="9999"
                                    ErrorMessage="トップに固定する順は0〜9999の数で入力してください。" />
                <div class="fit-hint"><span class="fit-hint-text" id="articlePinOrderHint">ブログのトップに固定する順です。空欄なら固定しません</span></div>
            </div>
        </div>
        </div>

        <div class="admin-group">
        <h2 class="admin-subhead">本文の見た目</h2>

        <%-- 【目次】。3択を1つの欄で選ばせる（Articles/ArticleRules.vb の ArticleTocMode）。
             「入れる」と「開く」の2つのスイッチにすると、「入れない＋開く」という意味の無い組み合わせができる。
             【data-autosave-field】自動保存にも乗せる（Scripts/article-editor.js が値を拾う）。
             【説明に位置を書かない】前は「右の枠」「右下」と書いていた（基本ルールの②違反） --%>
        <div class="form-row">
            <asp:Label ID="TocLabel" runat="server" AssociatedControlID="TocList"
                       CssClass="admin-label" Text="目次" />
            <div class="form-row-main">
                <asp:DropDownList ID="TocList" runat="server" CssClass="admin-select" data-autosave-field="tocMode"
                                  aria-describedby="articleTocHint" />
                <div class="fit-hint"><span class="fit-hint-text" id="articleTocHint">見出しから目次を自動で作ります。見出しが無い記事には出ません。小見出しは1段下げて出ます。</span></div>
            </div>
        </div>

        <%-- 【見出しの番号】。目次の3択とは**別の欄**にしてある
             （目次を入れずに番号だけ振る記事もありうる。1つの欄に2つの意味を持たせない） --%>
        <div class="form-row">
            <asp:Label ID="HeadingNumberLabel" runat="server" AssociatedControlID="HeadingNumberList"
                       CssClass="admin-label" Text="見出しの番号" />
            <div class="form-row-main">
                <asp:DropDownList ID="HeadingNumberList" runat="server" CssClass="admin-select" data-autosave-field="headingNumberMode"
                                  aria-describedby="articleHeadingNumberHint" />
                <div class="fit-hint"><span class="fit-hint-text" id="articleHeadingNumberHint">見出しと目次に「1.」「1-1」の番号を付けます。表示するときに番号を付けるだけなので、「付けない」に戻せば消えます。</span></div>
            </div>
        </div>
        </div>

        <%-- 【保存の帯。画面の下に貼り付いて、スクロールしても常に見える】
＝上にも置くか、動いていつも押しやすい位置にあるようにする。長い記事を書いていても、
             下までスクロールせずに保存できる。position: sticky（bottom: 0）は Content/Site.css の .admin-savebar。

             【断った理由を、ボタンのすぐ横にも出す】（指摘：保存ボタンが「無反応」に見えた）
             data-validation-status … 画面の検査（Validator）で止めたとき、Scripts/site.js の「6」が理由をここへ入れ、
             問題の欄までスクロールしてカーソルを置く。サーバーで断ったとき（重複・上限・読み取れない本文）は
             Edit.aspx.vb の ShowError がここと画面の上の2か所に入れる。
             aria-live="assertive"＝読み上げソフトにもその場で伝える。

             【スマホで文字を打っている間も、キーボードのすぐ上に出す】（前は打っている間だけ貼り付けをやめていた）
             打っている行が帯の裏に隠れないようにする仕組みは、Scripts/site.js の「13」（プロフィールの画面と同じ部品）。
             【検査の組を2つ持つ】data-validation-status に両方の名前を書いてある（article＝そのまま保存／articlePublish＝公開する保存） --%>
        <div id="articleSaveBar" class="admin-savebar">
            <%-- 【保存のボタンと、記事のページへのリンクを1つの組にしてある】（Q）
                 幅があれば横に並び、無ければ組の中で折り返す（中身任せ。幅の数字は書いていない）。

                 【記事のページへのリンク】編集のときだけ出す（新しい記事は、まだページが無い）。
                 公開中なら「公開ページを見る」、下書き・非公開なら「プレビュー」。文言と URL は Edit.aspx.vb の LoadArticle が決める
                 （**保存してある状態**で決める）。
                 【新しいタブで開く】同じタブで移ると、保存していない変更の確かめ（beforeunload）を踏ませるため。
                 【保存していない変更があるときだけ、「まだ反映されていません」が出る】見せるのは保存した内容なので。
                 出し入れは Scripts/article-editor.js（「保存していない」と判断したとき）。
                 【「一覧へ戻る」「削除」は帯に入れない】削除をいつも見える所に置くと押し間違えるため（帯の下のまま）。
                 ただし未反映の変更の［破棄する］だけは帯に置く（確かめの窓があり、未反映があるときだけ出て、消えるのは控えだけのため。下の自動保存の1行の注） --%>
            <div class="admin-savebar-main">
            <%-- 【選ぶ欄をやめて、ボタンを2つ並べた】
                 指摘：ドロップダウンリストが余計だった。選んだ時点で反映したのかどうかが、直感で分からない。
                 今は、**押したことが、そのまま起きること**である。言葉と決まりは ArticleRules（保存の帯の節）が持つ。

                   下書き（新しい記事も）　印「下書き」　［下書き保存］［公開する］
                   公開中　　　　　　　　　印「公開中」　［更新する］［非公開にする］
                   非公開　　　　　　　　　印「非公開」　［保存］［もう一度公開する］

                 【今の状態の印】保存してある状態で決める。公開中だけ緑。**文字で書いてある**（色だけで伝えない）。
                 【色の付いたボタン＝そのまま保存】いつも左。状態を変えるほうは枠だけで、押す前に確かめの窓が出る
                 （data-confirm。文は Edit.aspx.vb の OnPreRender が入れる）。
                 【data-confirm-group】確かめの窓より先に、画面の検査（タイトル・本文）を通す。
                 「公開します」と確かめたあとで「タイトルを入力してください」と止められないように（Scripts/site.js の「5」） --%>
            <%-- 【公開中の記事で、公開ページに未反映の変更（編集中の控え）があるときだけ見える】
                 見られるのは持ち主だけ（ほかの人には、?draft=1 は無いものとして公開中の記事が出る）。
                 控えができた・消えたときは、Scripts/article-editor.js が hidden を付け外しする。
                 【帯のいちばん左（「公開中」の印より左）へ移し、ほんのり黄色のボタンにした】（基本ルール W）
                 前は帯の右端の地味なリンク「編集中の内容をプレビュー」で、未反映があることに気づけなかった。
                 色が無い＝全部反映済み、とひと目で分かる。［更新する］で反映すると控えが消えて、この印も消える --%>
            <asp:HyperLink ID="DraftPreviewLink" runat="server" CssClass="admin-unreflected-btn" Visible="False"
                           data-draft-preview="1"
                           Target="_blank" rel="noopener" Text="未反映の変更をプレビュー"
                           aria-label="未反映の変更をプレビュー（新しいタブで開きます）" />
            <span id="articleStatusMark" class="status-mark <%: StatusMarkClass %>" data-status="<%: StatusKey %>"><%: StatusMarkText %></span>
            <%= HelpButton("article.status") %>
            <%-- 【data-save-keep】「状態を変えない保存」の目印。Scripts/article-editor.js が、
                 打っている間に出す右上の［保存］の押し先と、自動保存の1行に入れるボタンの名前を、これで探す
                 （並び順で探すと、並びを変えた日に［公開する］を掴みうる）。
                 右上の［保存］（やめた）も、自動保存の1行のボタンの名前（短くした）も無くなり、今は誰も読んでいない。印だけ残してある --%>
            <asp:Button ID="SaveButton" runat="server" Text="下書き保存"
                        CssClass="account-submit" ValidationGroup="article" data-save-keep="1" />
            <asp:Button ID="SwitchButton" runat="server" Text="公開する"
                        CssClass="admin-switch-btn" ValidationGroup="article"
                        data-confirm-group="article" />
            <asp:PlaceHolder ID="ViewLinkRow" runat="server" Visible="False">
                <asp:HyperLink ID="ViewLink" runat="server" CssClass="admin-link-btn"
                               Target="_blank" rel="noopener" />
                <span id="articleViewUnsaved" class="admin-view-unsaved" hidden="hidden">保存していない変更は、まだ反映されていません。</span>
            </asp:PlaceHolder>
            </div>
            <p class="admin-form-status" data-validation-status="article articlePublish"
               data-validation-status-prefix="保存できませんでした。" aria-live="assertive"><asp:Literal ID="SaveStatusText" runat="server" /></p>
            <%-- 【保存できたことの知らせ】「保存しました 16:02」（公開中の記事なら「（公開ページに反映しました）」も添える）。
                 保存しても一覧へ戻らなくなったので、うまくいったことは、この帯で伝える（Edit.aspx.vb の ShowDoneNotice が入れる）。
                 【自動保存の1行（下）とは、別の要素にしてある】同じ要素にすると、次の自動保存がこの文を上書きしてしまう。
                 【role="status"＝読み上げソフトにも伝える】ただし「開いた時点から入っている文」は読まれない決まりなので、
                 Scripts/article-editor.js が、いったん空にしてから入れ直している。
                 【打ち直したら消える】article-editor.js の clearSavedStatus。出たままだと、そのあとの変更まで保存済みに見える --%>
            <p id="articleSavedStatus" class="admin-saved-status" role="status"><asp:Literal ID="SavedStatusText" runat="server" /></p>
            <%-- 【自動保存の知らせ】「下書きを自動保存しました 21:45」など。Scripts/article-editor.js が入れる。
                 【何を保存したかまで書く】（基本ルール X）文は article-editor.js の autosaveSavedText。
                 【aria-live を付けていない】数秒おきに時刻が変わるので、付けると読み上げソフトが打つたびに読む。
                 自動保存が断られたときだけ、ツールバーの下のお知らせの1行（role="status"）でも伝える
                 【［破棄する］】公開中の記事に未反映の変更（編集中の控え）があるときだけ、1行のすぐあとに出る。
                 中身は画面の上の［未反映の変更を破棄する］と同じ（Edit.aspx.vb の DiscardDraftButton_Click を2つのボタンで共有）。
                 控えができた・消えたときは、黄色いボタンと一緒に article-editor.js が hidden を付け外しする。
                 【帯には削除を入れない決まりの例外】上の「『一覧へ戻る』『削除』は帯に入れない」とは別に考えた。
                 押す前に確かめの窓が出る・未反映があるときだけ出る・消えるのは控えだけ（公開中の記事は残る）ので、帯に置いてよいとした。
                 黄色いボタンの隣ではなくここなのは、帯の横幅が足りないため（開発者の判断） --%>
            <div class="admin-autosave-row">
                <p id="articleAutosaveStatus" class="admin-autosave-status"></p>
                <asp:Button ID="DiscardDraftBarButton" runat="server" formnovalidate="formnovalidate" Text="破棄する"
                            CssClass="admin-autosave-discard" CausesValidation="False" Visible="False"
                            data-draft-discard="1"
                            aria-label="未反映の変更を破棄する"
                            data-confirm="未反映の変更を破棄します。よろしいですか？"
                            data-confirm-ok="破棄する" />
            </div>
            <%-- 【新しい記事を自動保存が作ったら、その番号を JS がここへ入れる】Edit.aspx.vb の AutoSavedIdField。
                 「保存する」を押したときに、同じ記事を保存するため（入れないと、記事が2つになる） --%>
            <input type="hidden" id="<%: AutoSavedIdField %>" name="<%: AutoSavedIdField %>" value="" />
        </div>

        <%-- 【右寄せ】（「ボタンと、枠・小窓のいちばん下のリンクは右寄せ」） --%>
        <div class="admin-actions admin-actions--end">

            <%-- 【「やめて」を外した】保存しても、この画面に留まるようになったため。
                 保存し終わってから押すことがあるので、「やめて一覧へ戻る」だと嘘になる --%>
            <a href="/account/articles" class="admin-link-btn">記事一覧へ戻る</a>

            <%-- 【削除のボタンは、編集のときだけ出す】
                 【保存の帯には入れていない】いつも見えている帯に置くと、押し間違いやすくなる。
                 【CausesValidation="False"】削除するときに、タイトルや本文の検査を走らせない。
                 【data-confirm】押す前に確認のダイアログを出す（Scripts/site.js の「5」）。
                 ブラウザ標準の confirm は、このサイトでは使わない決まりである
                 （見た目がブラウザ任せになるうえ、背面のタブでは Chrome が抑制する） --%>
            <asp:Button ID="DeleteButton" runat="server" formnovalidate="formnovalidate" Text="この記事を削除"
                        CssClass="admin-delete-btn" CausesValidation="False"
                        data-confirm="この記事をゴミ箱へ移します。30 日のうちなら元に戻せます。"
                        data-confirm-ok="ゴミ箱へ移す" />

        </div>

        <p class="admin-note"><asp:Literal ID="MetaText" runat="server" /></p>

        <%-- 【「最初に公開した日」の説明は消した】（開発者の判断＝名前で分かるので要らない） --%>

        <%-- 【画像を貼る前の確かめの小窓】
             縮めた実物・説明（alt）の欄・「貼る」「選び直す」「やめる」。部屋の画像の G-2c（送る前に中止できる）と同じ考え方。
             貼った画像を押したときは、同じ小窓が「説明を直す」で開く（プレビューとボタンの文言を JS が変える）。
             【ボタンはどれも type="button"】form の中なので、書かないと保存が走る。説明の欄で Enter を押しても保存させない（JS が止める）。
             【説明は空でもよい】空なら alt="" で、読み上げソフトは飛ばす（飾りの画像）。「画像」と勝手に入れない --%>
        <div id="articleImageDialog" class="article-image-dialog" role="dialog" aria-modal="true"
             aria-labelledby="articleImageDialogTitle" hidden="hidden">
            <div class="article-image-dialog-box">
                <h2 id="articleImageDialogTitle" class="article-image-dialog-title">画像を貼る</h2>
                <div class="article-image-dialog-preview"><img id="articleImagePreview" alt="" /></div>
                <p id="articleImageSize" class="admin-hint"></p>
                <%-- 【1枚の上限を一言】（K。前の回で決めた「あ」のついで）
                     数はコードから出す（ArticleUploadImage.MaxImageBytes＝RoleLimits と 3MB の天井の小さいほう）。
                     画面に数字を書くと、上限を変えた日にここだけ古いまま残る --%>
                <p class="admin-hint">1枚<%: ImageMaxMbText %>まで</p>
                <label for="articleImageAlt" class="article-image-dialog-label">画像の説明</label>
                <input type="text" id="articleImageAlt" class="account-input" maxlength="200" autocomplete="off"
                       aria-describedby="articleImageAltHint" />
                <p id="articleImageAltHint" class="admin-hint">何が写っているかを短く。飾りの画像なら空欄でかまいません。</p>
                <%-- 【大きさと枠】本文の中の class（article-img-s・m・l と article-img-noframe）になる。
                     貼るときは「大・枠あり」、直すときはその画像の今の形で開く（Scripts/article-editor.js の「記事の画像」）。
                     【px の数字で選ばせない】本文の幅が PC とスマホで違うので、本文の幅に対する割合で決める（Content/Site.css）。
                     【name を付けたラジオボタン】form の中なので保存のときに一緒に送られるが、サーバーは読まない（本文の class が正本） --%>
                <fieldset class="article-image-dialog-size">
                    <legend>大きさ</legend>
                    <label><input type="radio" name="articleImageSizeChoice" value="s" /> 小（本文の幅の約3分の1）</label>
                    <label><input type="radio" name="articleImageSizeChoice" value="m" /> 中（約半分）</label>
                    <label><input type="radio" name="articleImageSizeChoice" value="l" checked="checked" /> 大（本文の幅いっぱい）</label>
                </fieldset>
                <%-- 【どの画面に出すか】本文の中の class（article-img-pconly／article-img-sponly）になる。
                     付けなければ両方に出る（今までの画像は全部これ）。境目は 700px（Content/Site.css）。
                     【何のために要るのか】使い方の記事に、スマホの細長い画面写真だけを貼ると、
                     PC で読む人には自分の画面と違って見える。同じ場面の写真を2枚貼って、見ている画面のほうだけを出す。
                     【隠すほうは display: none】読み上げソフトからも消えるので、同じ説明が2回読まれない --%>
                <fieldset class="article-image-dialog-size">
                    <legend>出す画面</legend>
                    <label><input type="radio" name="articleImageDeviceChoice" value="both" checked="checked" /> 両方（PCでもスマホでも出す）</label>
                    <label><input type="radio" name="articleImageDeviceChoice" value="pc" /> PCだけ（横幅701px以上）</label>
                    <label><input type="radio" name="articleImageDeviceChoice" value="sp" /> スマホだけ（横幅700px以下）</label>
                </fieldset>
                <div class="article-image-dialog-frame">
                    <label for="articleImageFrame">枠を付ける</label>
                    <label class="toggle-switch">
                        <input type="checkbox" id="articleImageFrame" checked="checked" aria-describedby="articleImageFrameHint" />
                        <span class="toggle-slider"></span>
                    </label>
                    <span id="articleImageFrameHint" class="admin-hint">薄いグレーの線。透明な背景のロゴなど、要らないときだけ外します。</span>
                </div>
                <p id="articleImageStatus" class="article-image-dialog-status" role="status" aria-live="polite"></p>
                <p class="article-image-dialog-actions">
                    <button type="button" id="articleImageOk" class="account-submit">貼る</button>
                    <button type="button" id="articleImageRechoose" class="admin-link-btn">選び直す</button>
                    <button type="button" id="articleImageCancel" class="admin-link-btn">やめる</button>
                </p>
            </div>
        </div>

    </div>

</asp:Content>

<asp:Content ID="ScriptsContent" ContentPlaceHolderID="AdminScripts" runat="server">

    <%-- 【読む順番】quill.js → article-editor.js（あとのほうが window.Quill を使う）。
         defer も async も付けない（付けると順番が保証されない）。
         【Quill が届かなかったときは、article-editor.js が何もしない】本文は素の textarea のまま書ける。
         article-toc.js は、狭い画面の目次（丸いボタンと板）の部品 window.VbTocSheet を作るため（記事のページと同じもの）。
         このページには記事の本文（.article-body）が無いので、部品を作るところだけが動く。届かなくても、狭い画面に目次が出ないだけ --%>
    <script src="<%: Asset("~/Scripts/lib/quill/quill.js") %>"></script>
    <script src="<%: Asset("~/Scripts/article-toc.js") %>"></script>
    <script src="<%: Asset("~/Scripts/article-editor.js") %>"></script>

</asp:Content>
