/*
    記事の本文を「見たまま」書く画面（ボタン・HTMLモード・包んで送る、を足した）

    読み込むページは Articles/Edit.aspx だけ。
    道具は Quill 2.0.3（BSD-3-Clause。Scripts/lib/quill/ と Content/lib/quill/ に置いてある）。

    ============================================================
    【この画面は、守りではない】
    ============================================================
    ここで何を制限しても、開発者ツールから外せる。
    **本文の唯一の砦は、保存するときの HtmlSanitizer（Articles/ArticleHtml.vb）である。**
    ここでやっているのは「書いたのに、保存したら消えた」を起こさないための親切だけ。

    ============================================================
    【ツールバーで作れるもの ＝ HtmlSanitizer が許すもの】（いちばん大事な決まり）
    ============================================================
    画面で付けられる書式が、保存のときに取り除かれると、利用者には
    「書いたのに消えた」としか見えない。そこで3か所をそろえてある。

      1. ツールバーのボタン（Articles/Edit.aspx の #articleToolbar と色の小窓）
      2. 下の FORMATS（Quill が受け付ける書式。貼り付けたものも、これ以外は落とす）
      3. Articles/ArticleHtml.vb の AllowedTags ／ AllowedAttributes ／ AllowedClasses

    **書式を1つ足すときは、3か所とも足すこと。**
    字寄せは class（ql-align-〇〇）で出させ、サニタイザーは決まった名前の class しか通さない。
    色は style で出させるが（自由な色を選べるようにしたため）、サニタイザーが通すのは字の飾りの要素
    （span・strong・em・u・s・a）の color と background-color の2つ・値は #RRGGBB だけ
    （「何でも通る style」にしない。ArticleHtml.vb の NormalizeStyle・StyleElements）。

    ============================================================
    【送り方は、今までと同じ口。ただし送る直前に「包む」】
    ============================================================
    本文は、今までどおり <textarea>（BodyBox）でサーバーへ送る。
    この JS は、Quill の中身が変わるたびに、その HTML を textarea へ書き写す。
    **送る直前に、textarea の値を "vbenc1:" ＋ Base64（UTF-8）に包む。**
    本番の手前の WAF が、説明の文に書いた「<script> タグは…」のような文字だけで保存を 403 にするため
    （テストで起きた）。サーバー（Core/BodyTransport.vb）が戻してから、
    必ず HtmlSanitizer を通して保存する。**守りの本体はサニタイザーのまま**である。
    **JS か Quill が読み込めなかったときは、textarea がそのまま残る**（今までの形で書ける。そのときは包まれない）。
*/
(function () {

    'use strict';

    var source = document.querySelector('textarea[data-article-editor]');

    if (!source) {
        return;
    }

    /* 【Quill が読み込めなかったら、何もしない】textarea のまま書ける */
    if (typeof window.Quill === 'undefined') {
        if (window.console) {
            console.warn('[記事の編集] Quill が読み込めなかったので、素の入力欄のまま使います。');
        }
        return;
    }

    var Quill = window.Quill;

    var wrapper = document.getElementById('articleEditor');
    var toolbar = document.getElementById('articleToolbar');
    var editorEl = document.getElementById('articleEditorBody');
    var htmlSource = document.getElementById('articleHtmlSource');
    var htmlHint = document.getElementById('articleHtmlHint');
    var htmlToggle = document.getElementById('articleHtmlToggle');
    var notice = document.getElementById('articleEditorNotice');

    if (!wrapper || !toolbar || !editorEl) {
        return;
    }

    /* 包んだ本文の先頭の印（サーバーの BodyTransport.EncodedPrefix と同じ。markup から受け取る） */
    var ENCODED_PREFIX = wrapper.getAttribute('data-encoded-prefix') || 'vbenc1:';

    /* 保存のときに使える書き方の一覧（サーバーの ArticleHtml が持つものを、Edit.aspx.vb が JSON で渡している） */
    var ALLOWED = readAllowed();

    /* Quill が受け付ける書式（上の「3か所をそろえてある」の2）。
       【ここに無い書式は、貼り付けても落ちる】
       Word やよそのページから貼ったときに、決まった色以外の色・表などが入り込まないのは、これのおかげである。
       【画像（image）は 足した】ただし**記事の画像の置き場所のものだけ**（下の clipboard の「画像」の matcher と、
       サーバーの ArticleHtml.vb の NormalizeImage が同じ形で絞る）。よそのサイトの画像・data: の画像は、貼った時点で落とす */
    var FORMATS = [
        'header', 'bold', 'italic', 'underline', 'strike',
        'color', 'background', 'align',
        'list', 'blockquote', 'code-block', 'divider',
        'link', 'image'
    ];

    /* 記事の画像の src の形（サーバーの ArticleImages.SrcPatternText を ArticleHtml.AllowedJson の imageSrc で受け取る） */
    var IMAGE_SRC_PATTERN = ALLOWED.imageSrc ? new RegExp(ALLOWED.imageSrc) : null;

    /* 見出しの段。本文の見出しは h2 と h3 だけ（h1 は記事のタイトル。ArticleHtml.vb の「h1 を入れていない」） */
    var HEADER_LARGE = 2;
    var HEADER_SMALL = 3;


    /* ============================================================
       Quill に、このサイトの決まりを教える（new Quill より前にやること）
       ============================================================ */

    var Parchment = Quill.import('parchment');

    /* リンク先に許す種類を、サニタイザー（ArticleHtml.vb の AllowedSchemes）とそろえる。
       Quill の既定は http・https・mailto・tel・sms で、tel と sms はサニタイザーが落とす
       （画面ではリンクなのに、保存したらリンクが消えた、になる）。
       ここに無い種類のリンクは、Quill が about:blank に置き換え、保存のときにサニタイザーが行き先を外す */
    Quill.import('formats/link').PROTOCOL_WHITELIST = ALLOWED.schemes.length ? ALLOWED.schemes : ['http', 'https', 'mailto'];

    /* 字寄せは、決まった名前の class で出させる。
       字寄せの既定は class だが「両端そろえ」も入る。決まった名前（ArticleHtml.vb から受け取った一覧）だけにする */
    /* 【文字の色・背景の色は、style の #RRGGBB だけ】
       決まった数色の class（ql-color-〇〇）だったが「その他の色…」（カラーピッカー）を足したので、
       自由な色を表せる style に1本化した。**サニタイザー（ArticleHtml.vb の NormalizeStyle）と同じ決まり**にそろえてある——
       値は #RRGGBB だけ（Quill の既定の色の部品は rgb(…) を #RRGGBB に直して返すので、それを元にしている）。
       名前の色（red など）・var(…)・半透明などは、Quill が付けずに捨てる（貼り付けたものも） */
    var ColorStyleClass = Quill.import('attributors/style/color').constructor;

    class HexColorAttributor extends ColorStyleClass {

        add(node, value) {
            var hex = toHexColor(value);
            return hex ? super.add(node, hex) : false;
        }

        value(node) {
            return toHexColor(super.value(node)) || '';
        }

    }

    Quill.register({
        'formats/align': new Parchment.ClassAttributor('align', 'ql-align', {
            scope: Parchment.Scope.BLOCK,
            whitelist: namesWithPrefix('ql-align-')
        }),
        'formats/color': new HexColorAttributor('color', 'color', {
            scope: Parchment.Scope.INLINE
        }),
        'formats/background': new HexColorAttributor('background', 'background-color', {
            scope: Parchment.Scope.INLINE
        })
    }, true);

    /* 区切り線（<hr>）。Quill には無いので、1つ足す（中身を持たない、行まるごとの部品） */
    var BlockEmbed = Quill.import('blots/block/embed');

    class DividerBlot extends BlockEmbed { }
    DividerBlot.blotName = 'divider';
    DividerBlot.tagName = 'hr';

    Quill.register(DividerBlot, true);

    /* 画像。【説明が空（alt=""）でも、属性を残す】
       Quill の既定の画像は、空の説明を「属性なし」にしてしまう（format で値が空なら removeAttribute）。
       alt が無い画像は、読み上げソフトがファイル名を読むことがある。**空の alt は「飾りの画像なので飛ばしてよい」という意味**なので、
       作るときに alt="" を付け、空の値でも属性として残す（開発機で、HTMLモードの往復・開き直しで消えていたのを見つけて直した） */
    var BaseImage = Quill.import('formats/image');

    /* 【画像の大きさと枠】本文の中では名前の付いた class で持つ：
         大きさ … article-img-s（小）・article-img-m（中）・article-img-l（大）。付いていなければ大（貼った画像）
         枠     … article-img-noframe が付いていれば枠なし。付いていなければ枠あり
       Quill の中では、画像の書式 imgsize（'s'・'m'・'l'）と imgframe（'off' なら枠なし）として持ち、この部品が class に書き換える。
       **名前はサーバー（ArticleHtml.vb の AllowedJson の imageSizeClasses・imageNoFrameClass）から受け取る**（JS に書き写さない。
       3か所をそろえる決まりの「ArticleHtml の許す class」と、同じ一覧から作るため） */
    var IMAGE_SIZE_CLASSES = {};

    (ALLOWED.imageSizeClasses || []).forEach(function (name) {
        IMAGE_SIZE_CLASSES[name.charAt(name.length - 1)] = name;
    });

    var IMAGE_NOFRAME_CLASS = ALLOWED.imageNoFrameClass || '';

    /* 【どの画面に出すか】両方（class なし）・PCだけ・スマホだけ。
       名前はサーバー（ArticleHtml.vb の imageDeviceClasses）から受け取る。並びは [PCだけ, スマホだけ] */
    var IMAGE_DEVICE_CLASSES = {
        pc: (ALLOWED.imageDeviceClasses || [])[0] || '',
        sp: (ALLOWED.imageDeviceClasses || [])[1] || ''
    };

    function imageSizeOf(node) {
        for (var key in IMAGE_SIZE_CLASSES) {
            if (node.classList.contains(IMAGE_SIZE_CLASSES[key])) {
                return key;
            }
        }
        return null;
    }

    /* 付いていなければ 'both'（両方に出る） */
    function imageDeviceOf(node) {
        for (var key in IMAGE_DEVICE_CLASSES) {
            if (IMAGE_DEVICE_CLASSES[key] && node.classList.contains(IMAGE_DEVICE_CLASSES[key])) {
                return key;
            }
        }
        return 'both';
    }

    class ArticleImageBlot extends BaseImage {

        static create(value) {
            var node = super.create(value);
            node.setAttribute('alt', '');
            return node;
        }

        static formats(domNode) {

            var formats = super.formats(domNode);
            var size = imageSizeOf(domNode);

            if (size) {
                formats.imgsize = size;
            }

            if (IMAGE_NOFRAME_CLASS && domNode.classList.contains(IMAGE_NOFRAME_CLASS)) {
                formats.imgframe = 'off';
            }

            var device = imageDeviceOf(domNode);

            if (device !== 'both') {
                formats.imgdevice = device;
            }

            return formats;

        }

        format(name, value) {

            if (name === 'alt') {
                this.domNode.setAttribute('alt', value || '');
                return;
            }

            if (name === 'imgsize') {

                for (var key in IMAGE_SIZE_CLASSES) {
                    this.domNode.classList.remove(IMAGE_SIZE_CLASSES[key]);
                }

                if (IMAGE_SIZE_CLASSES[value]) {
                    this.domNode.classList.add(IMAGE_SIZE_CLASSES[value]);
                }

                this.cleanClass();
                return;

            }

            if (name === 'imgframe') {

                if (IMAGE_NOFRAME_CLASS) {
                    this.domNode.classList.toggle(IMAGE_NOFRAME_CLASS, value === 'off');
                }

                this.cleanClass();
                return;

            }

            /* どの画面に出すか。'both' なら class を付けない */
            if (name === 'imgdevice') {

                for (var dk in IMAGE_DEVICE_CLASSES) {
                    if (IMAGE_DEVICE_CLASSES[dk]) {
                        this.domNode.classList.remove(IMAGE_DEVICE_CLASSES[dk]);
                    }
                }

                if (IMAGE_DEVICE_CLASSES[value]) {
                    this.domNode.classList.add(IMAGE_DEVICE_CLASSES[value]);
                }

                this.cleanClass();
                return;

            }

            super.format(name, value);

        }

        /* class が空になったら、属性ごと外す（class="" を本文に残さない） */
        cleanClass() {
            if (this.domNode.classList.length === 0) {
                this.domNode.removeAttribute('class');
            }
        }

    }

    Quill.register(ArticleImageBlot, true);


    var quill = new Quill(editorEl, {
        theme: 'snow',
        formats: FORMATS,
        /* 何も書いていないときの案内。文言は markup（Edit.aspx の data-placeholder）に書いてある */
        placeholder: editorEl.getAttribute('data-placeholder') || '',
        modules: {
            /* ツールバーは markup に書いたものを使う（ボタンの名前を markup に日本語で書くため） */
            toolbar: {
                container: toolbar,
                handlers: {
                    undo: function () { quill.history.undo(); },
                    redo: function () { quill.history.redo(); },
                    divider: insertDivider,
                    /* 【画像】Quill の既定（選んだファイルを data: で本文に入れる）は使わない。受け口へ送ってから入れる */
                    image: function () { ArticleImage.choose(); }
                }
            },
            /* 【写真のファイルを貼り付け・落とし込みしたときも、同じ道を通す】
               Quill の既定の uploader は、ファイルを data: の画像として本文に入れる（保存でサニタイザーに消される）。
               受け取る形式は、画像のボタンと同じ6つ（GIF 以外は、送る前に JPEG か PNG に描き直す） */
            uploader: {
                mimetypes: ['image/png', 'image/jpeg', 'image/gif', 'image/bmp', 'image/x-icon', 'image/vnd.microsoft.icon', 'image/webp'],
                handler: function (range, files) {
                    if (files && files[0]) {
                        ArticleImage.fromFile(files[0], range);
                    }
                }
            }
        }
    });

    var editorRoot = quill.root;


    /* ============================================================
       キーボードだけの人が、編集の欄から出られるようにする
       ============================================================
       Quill は既定で Tab を「字下げ」「タブ文字を入れる」に使う。
       それだと、キーボードだけで操作している人が**編集の欄から出られなくなる**
       （Tab を押すたびに、本文にタブが入るだけ）。

       そこで Tab と Shift+Tab に結び付いた動きを、全部外している。
       外すと Quill は何もせず、ブラウザの既定（次の・前の部品へ移る）になる。
       【コードの囲みの中でも同じ】字下げは空白で打つ。

       【Quill の中の持ち方に寄りかかっている】bindings は「キーの名前 → 動きの配列」。
       Quill の版を上げたら、Tab で外へ出られるかを必ず確かめること */
    quill.keyboard.bindings.Tab = [];
    quill.keyboard.bindings[9] = [];


    /* ============================================================
       行の頭の「[ ] 」「[x] 」で、チェックボックスの箇条書きにしない
       ============================================================
       Quill 2.0.3 の打ち方の近道（list autofill）は、行の頭に「[ ] 」「[x] 」と打つと
       チェックボックスの箇条書き（list: unchecked／checked）にする。書く画面ではチェックボックスに見えるが、
       保存でサーバー（ArticleHtml）が data-list を落とすので、**ふつうの箇条書きに変わる**（「書いたのに変わった」）。
       近道の「- 」「* 」（箇条書き）「1. 」（番号付き）は、どちらも保存で残るので、そのまま使えるようにしてある。
       【Quill の中の持ち方に寄りかかっている】近道の「どの頭で始まったら」（prefix の正規表現）を、チェックボックスの2つを外した形に差し替える。
       Quill の版を上げたら、「[ ] 」と打ってチェックボックスにならないかを必ず確かめること */
    Object.keys(quill.keyboard.bindings).forEach(function (key) {

        (quill.keyboard.bindings[key] || []).forEach(function (binding) {

            if (binding && binding.prefix instanceof RegExp && binding.prefix.source.indexOf('\\[x\\]') >= 0) {
                binding.prefix = /^\s*?(\d+\.|-|\*)$/;
            }

        });

    });


    /* ============================================================
       見出しの段を、h2 と h3 にそろえる（貼り付けたもの・読み込んだもの）
       ============================================================
       ツールバーで付けられるのは h2 と h3 だけだが、よそから貼ると h1 や h4 が入ってくる。
       h1 はサニタイザーが落とす（タイトルの h1 とぶつかるため）ので、放っておくと
       「見出しが、ふつうの文に化けた」になる。貼った時点で近いほうへ寄せる */
    quill.clipboard.addMatcher(Node.ELEMENT_NODE, function (node, delta) {

        delta.ops.forEach(function (op) {

            if (!op.attributes) {
                return;
            }

            if (op.attributes.header) {
                op.attributes.header = (op.attributes.header <= HEADER_LARGE) ? HEADER_LARGE : HEADER_SMALL;
            }

            /* 【チェックボックスの箇条書きは、ふつうの箇条書きへ】貼り付けた Quill の HTML（data-list="checked"）で入ってきても、
               保存でサーバーが落とすので、貼った時点でそろえる（上の「近道で作らせない」と同じ理由） */
            if (op.attributes.list === 'checked' || op.attributes.list === 'unchecked') {
                op.attributes.list = 'bullet';
            }

            /* 【色は #RRGGBB に直し、直せないものは外す】（開発機で踏んだ）
               Quill は、貼り付けた style の色を自分の既定の部品でも読むので、「red」のような名前の色が
               そのまま書式として届く。こちらの色の部品（HexColorAttributor）はそれを断るが、**断っただけだと、
               すぐ前の色付きの字の色を、そのまま受け継いでしまう**（Quill は「前の字の書式との差」だけを付けるため。
               HTMLモードで「ok（#123456）」の直後に「名前（red）」を書いたら、「名前」まで #123456 になった）。
               ここで外しておけば「色なし」として届き、前の色は受け継がれない */
            ['color', 'background'].forEach(function (name) {

                if (!(name in op.attributes)) {
                    return;
                }

                var hex = toHexColor(op.attributes[name]);

                if (hex) {
                    op.attributes[name] = hex;
                } else {
                    delete op.attributes[name];
                }

            });

        });

        return delta;

    });

    /* 【画像は、記事の画像の置き場所のものだけ】よそのサイトの画像（記事を開いた人の情報がよそへ渡る）・
       data: の画像（DB に絵をそのまま入れることになる）・部屋の画像（90日で消える）は、貼った時点で落とす。
       保存のときに消える（「書いたのに消えた」）より、貼った時点で入らないほうが分かりやすい */
    quill.clipboard.addMatcher('IMG', function (node, delta) {

        var src = node.getAttribute('src') || '';

        if (IMAGE_SRC_PATTERN && IMAGE_SRC_PATTERN.test(src)) {
            return delta;
        }

        return new (Quill.import('delta'))();

    });


    /* ============================================================
       ボタンの絵（Quill に無いもの・Quill が受け持たないボタン）
       ============================================================ */
    (function fillIcons() {

        var icons = Quill.import('ui/icons');

        var colorBtn = document.getElementById('articleColorBtn');
        var bgBtn = document.getElementById('articleBackgroundBtn');

        /* 【innerHTML で入れ替えないこと】（テストで踏んだ）
           ボタンの中には、読み上げ用の見えない文字（.visually-hidden。Edit.aspx）を置いてある。
           カラーピッカー（jscolor）は、付けたボタンが「文字を持たない」と空のボタンとみなし、**中身を全部消す**
           （jscolor の isButtonEmpty → removeChildren）。絵（SVG）だけのボタンだと、ピッカーを初めて開いた瞬間に
           絵が消えて、ツールバーの「文字の色」の場所が空になった。見えない文字を残したまま、絵をその前に足す */
        if (colorBtn && icons.color) {
            colorBtn.insertAdjacentHTML('afterbegin', icons.color);
        }

        if (bgBtn && icons.background) {
            bgBtn.insertAdjacentHTML('afterbegin', icons.background);
        }

        /* 絵は飾り。名前は aria-label が持つ */
        Array.prototype.forEach.call(toolbar.querySelectorAll('svg'), function (svg) {
            svg.setAttribute('aria-hidden', 'true');
            svg.setAttribute('focusable', 'false');
        });

    })();


    /* ============================================================
       保存されている本文を読み込む
       ============================================================
       保存されているのは、サニタイザーを通ったあとの HTML。
       【包まれたまま戻ってくることがある】保存が断られて画面が戻ってきたときに、サーバーが包みを戻せなかった場合。
       戻せればそのまま読み込む。
       【'silent' で入れる】入れた瞬間を「書き換えた」と数えないため（下の「保存していない変更」） */
    (function loadInitial() {

        /* 【保存されている本文は、包んだ形（data-initial-body）から読む】
           サーバーは、同じ本文を textarea（生の HTML）と、書く欄の箱の data-initial-body（包んだ形）の両方に出している。
           JS が動くときは包んだほうを使う＝**ページの応答の途中で、生の HTML の一部（style など）が書き換えられても、
           書く欄に読み込む本文は変わらない**（送るときに包むのと同じ考え方を、受け取る側にも当てた）。
           textarea の生の HTML は、JS が動かないときの素の入力欄のためだけに残している */
        var value = wrapper.getAttribute('data-initial-body') || source.value;

        if (value.indexOf(ENCODED_PREFIX) === 0) {

            var once = decodeBody(value);

            /* 保存が断られて画面が戻ってきたとき、textarea の値そのものが包んだ形のままのことがある。
               それをさらに包んで返しているので、2回戻す */
            if (once !== null && once.indexOf(ENCODED_PREFIX) === 0) {
                value = once;
            }

        }

        if (!value) {
            return;
        }

        if (value.indexOf(ENCODED_PREFIX) === 0) {

            var decoded = decodeBody(value);

            if (decoded === null) {
                showNotice('前に送った本文を読み取れませんでした。お手数ですが、書き直してください。');
                return;
            }

            value = decoded;

        }

        quill.setContents(quill.clipboard.convert({ html: value }), 'silent');

    })();

    /* 読み込んだ状態を「やり直し」の起点にする（Ctrl+Z で空に戻らないように） */
    quill.history.clear();


    /* ============================================================
       書き写す（Quill → textarea）
       ============================================================ */

    /* 本文が空か。
       【文字数ではなく、中身で見る】空白だけの本文を「書いた」と数えないため。
       文字でないもの（区切り線・画像など）が1つでもあれば、空ではない */
    function isBlank() {

        return quill.getContents().ops.every(function (op) {
            return typeof op.insert === 'string' && op.insert.trim() === '';
        });

    }

    /* Quill の中身を、保存する HTML にする。
       【root.innerHTML ではなく getSemanticHTML を使う】
       innerHTML は Quill の内側の形で、箇条書きも番号付きも <ol><li data-list="…"> で表す。
       data-* はサニタイザーが落とすので、そのまま送ると**箇条書きが全部、番号付きに化ける**。
       getSemanticHTML は、ふつうの <ul> / <ol> にして返す。

       【&nbsp; を、ふつうの空白へ戻す】（開発機で確かめた）
       Quill 2.0.3 の getSemanticHTML は、空白を**全部** &nbsp; にして返す
       （連続した空白を残すための作り）。そのまま保存すると、英語の文が単語の切れ目で
       折り返さなくなり、狭い画面ではみ出す。空白が2つ以上続くところだけ &nbsp; を残す */
    function semanticHtml() {

        if (isBlank()) {
            return '';
        }

        var html = quill.getSemanticHTML();

        /* 【空の行を <p><br></p> に戻す】getSemanticHTML は空の行を <p></p> にして返す。
           中身の無い <p> は高さを持たないので、書いた人が空けた1行が、記事では消えて詰まる */
        html = html.replace(/<p><\/p>/g, '<p><br></p>');

        /* 【コードの囲みの data-language を外す】Quill は <pre data-language="plain"> で出す。
           data-* はサニタイザーが落とし、そのとき「使えない書き方を取り除きました」と知らせることになる。
           こちらで外しておけば、ボタンで作った本文では、その知らせは出ない */
        html = html.replace(/<pre data-language="[^"]*">/g, '<pre>');

        /* 【色を #rrggbb の形にそろえてから送る】ブラウザは色を rgb(29, 78, 216) の形で書き出す。
           サーバーでも直すが、送る形を最初から保存する形（#RRGGBB）にそろえておく。
           【span だけでなく、style の付いた要素すべて】太字などは <strong style="…"> になる（ArticleHtml.vb の StyleElements）。
           直すのはタグの中の style="…" だけ（本文の文字は触らない。本文の < は &lt; になっているので、タグと取り違えない） */
        html = html.replace(/(<[a-z0-9]+\b[^>]*?\sstyle=")([^"]*)(")/g, function (whole, head, style, tail) {

            var fixed = style.replace(/rgba?\([^)]*\)/g, function (color) {
                return toHexColor(color) || color;
            });

            return head + fixed + tail;

        });

        /* 【行き先を外されたリンクの about:blank を消す】Quill は、許していない種類のリンク（javascript: など）を
           about:blank に置き換える。そのまま送ると、サニタイザーが行き先を落として「取り除きました」と知らせることになり、
           HTMLモードでも「about: のリンク」と出て紛らわしい。ここで行き先ごと外しておく（文字だけが残る） */
        html = html.replace(/ href="about:blank"/g, '');

        return html.replace(/(&nbsp;)+/g, function (run) {

            var count = run.length / 6;

            if (count === 1) {
                return ' ';
            }

            /* 2つ以上続くところは、最後の1つだけふつうの空白にする（そこで折り返せるように） */
            return new Array(count).join('&nbsp;') + ' ';

        });

    }

    /* いま「送るべき本文」（HTMLモードなら、その欄の中身そのまま） */
    function currentHtml() {
        return htmlMode ? htmlSource.value : semanticHtml();
    }

    function copyToSource() {
        source.value = currentHtml();
    }


    /* ============================================================
       包む・戻す（Base64。暗号ではない）
       ============================================================
       【UTF-8 のバイト列にしてから Base64】btoa は日本語をそのまま受け取れない（1バイトの字しか扱えない）。
       【細切れにして変換する】大きな本文を1回で String.fromCharCode に渡すと、引数が多すぎて止まる */
    function encodeBody(html) {

        if (!html) {
            return '';
        }

        var bytes = new TextEncoder().encode(html);
        var binary = '';
        var CHUNK = 0x8000;

        for (var i = 0; i < bytes.length; i += CHUNK) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
        }

        return ENCODED_PREFIX + window.btoa(binary);

    }

    /* 戻せなければ null（壊れた UTF-8 も、例外にして null。化けた字で読み込まない） */
    function decodeBody(value) {

        try {

            var binary = window.atob(value.substring(ENCODED_PREFIX.length));
            var bytes = new Uint8Array(binary.length);

            for (var i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }

            return new TextDecoder('utf-8', { fatal: true }).decode(bytes);

        } catch (e) {
            return null;
        }

    }


    /* ============================================================
       お知らせの1行（ツールバーの下）
       ============================================================
       【いったん空にしてから入れる】同じ文を続けて出したときも、読み上げソフトがもう一度読むように */
    var noticeTimer = null;

    function showNotice(text) {

        if (!notice) {
            return;
        }

        notice.textContent = '';

        window.clearTimeout(noticeTimer);

        noticeTimer = window.setTimeout(function () {
            notice.textContent = text;
        }, 50);

    }

    function clearNotice() {

        if (notice) {
            window.clearTimeout(noticeTimer);
            notice.textContent = '';
        }

    }


    /* ============================================================
       区切り線を入れる
       ============================================================
       カーソルのある行の下に1本入れて、カーソルを線の次へ移す */
    function insertDivider() {

        var range = quill.getSelection(true);
        var index = range ? range.index + range.length : quill.getLength();

        quill.insertEmbed(index, 'divider', true, 'user');
        quill.setSelection(index + 1, 0, 'user');

    }


    /* ============================================================
       文字の色・背景の色（よく使う色の小窓 ＋ 「その他の色…」）
       ============================================================
       【Quill の色の部品（snow の picker）を使っていない】span で作った部品で、キーボードと読み上げの扱いが弱い。
       ボタンの小窓にして、Esc で閉じてボタンへ戻る形にした（部屋の「見た目」のアイコンの小窓と同じ考え方）。

       【よく使う色】小窓の見本のボタン（Edit.aspx）。data-value の名前から theme.css の --article-color-〇〇 ／
       --article-bg-〇〇 を読み、その #RRGGBB を付ける（class から style に変えた。上の HexColorAttributor）。
       【その他の色…】部屋の設定のアイコンの色と同じカラーピッカー（jscolor。Scripts/chat-room.js の見た目の節と同じ使い方）。
       jscolor（62KB）は、初めて押したときに取りに行く（場所は markup の data-jscolor-url）。取れなくても、よく使う色は使える。

       【読みにくい色でも、注意は出さない】（開発者の判断でやめた＝色が薄いと出る注意がわずらわしい）。
、文字と背景の明るさの差が 4.5:1 未満だと「読みにくい色かもしれません」と出していた。どの色を選んでも黙って付く */

    (function setupColorMenus() {

        var menuButtons = toolbar.querySelectorAll('.article-menu-btn');
        var openButton = null;

        /* 「その他の色…」のカラーピッカー（ボタンごとに1つ。初めて押したときに作る） */
        var pickers = {};
        var pickerState = 'idle';   /* idle → loading → ready ／ failed */
        var pickRange = null;       /* ピッカーを開いたときに選んでいた範囲 */
        var PICKER_PALETTE = [
            '#FF0000', '#FF6B6B', '#FF9900', '#FFE66D',
            '#22B14B', '#4ECDC4', '#00A1E7', '#3F47CC',
            '#A349A4', '#95E1D3', '#7d7d7d', '#000000'
        ];

        function panelOf(button) {
            return document.getElementById(button.getAttribute('data-panel'));
        }

        function close(returnFocus) {

            if (!openButton) {
                return;
            }

            var panel = panelOf(openButton);

            if (panel) {
                panel.hidden = true;
            }

            openButton.setAttribute('aria-expanded', 'false');

            var button = openButton;
            openButton = null;

            if (returnFocus) {
                button.focus();
            }

        }

        function open(button) {

            close(false);

            var panel = panelOf(button);

            if (!panel) {
                return;
            }

            panel.hidden = false;
            button.setAttribute('aria-expanded', 'true');
            openButton = button;

            var first = panel.querySelector('button');

            if (first) {
                first.focus();
            }

        }

        /* 色を付ける（value が空なら外す） */
        function applyColor(format, hex, range) {

            if (range) {
                quill.setSelection(range.index, range.length, 'silent');
            } else {
                quill.getSelection(true);
            }

            quill.format(format, hex ? hex : false, 'user');

            quill.focus();

        }

        /* よく使う色の名前 → theme.css の #RRGGBB */
        function themeColor(format, name) {

            var variable = (format === 'color' ? '--article-color-' : '--article-bg-') + name;
            var value = window.getComputedStyle(document.documentElement).getPropertyValue(variable);

            return toHexColor(value);

        }

        /* === 「その他の色…」 === */
        function openPicker(button, format) {

            pickRange = quill.selection.savedRange || quill.getSelection();

            if (pickerState === 'ready') {
                showPicker(button, format);
                return;
            }

            if (pickerState === 'loading') {
                return;
            }

            pickerState = 'loading';
            showNotice('色を選ぶ道具を読み込んでいます…');

            var script = document.createElement('script');
            var done = false;

            function finish(ok) {

                if (done) {
                    return;
                }

                done = true;
                window.clearTimeout(timer);

                if (ok && window.JSColor) {
                    pickerState = 'ready';
                    clearNotice();
                    showPicker(button, format);
                    return;
                }

                pickerState = 'failed';

                if (script.parentNode) {
                    script.parentNode.removeChild(script);
                }

                showNotice('色を選ぶ道具を読み込めませんでした。もう一度押すと、読み込み直します（よく使う色は選べます）。');

            }

            var timer = window.setTimeout(function () { finish(false); }, 15000);

            script.src = wrapper.getAttribute('data-jscolor-url') || '';
            script.async = true;
            script.onload = function () { finish(true); };
            script.onerror = function () { finish(false); };
            document.head.appendChild(script);

        }

        function showPicker(button, format) {

            if (!pickers[format]) {

                var start = toHexColor(quill.getFormat()[format]) ||
                    (format === 'color' ? baseTextColor() : '#FFFFFF');

                /* 【ツールバーの色のボタンに付ける】小窓の中の「その他の色…」に付けると、小窓を閉じた瞬間に
                   位置の手がかりが無くなる。showOnClick は切ってある（部屋と同じ。開くのはこちらの show()） */
                pickers[format] = new window.JSColor(button, {
                    value: start,
                    format: 'hex',
                    alphaChannel: false,
                    previewElement: null,
                    valueElement: null,
                    showOnClick: false,
                    palette: PICKER_PALETTE,
                    paletteCols: 6,
                    width: 181,
                    height: 101,
                    closeButton: true,
                    closeText: '閉じる',

                    /* 選び終えた：その色を付ける */
                    onChange: function () {
                        applyColor(format, toHexColor(this.toHEXString()), pickRange);
                    }
                });

            } else {

                var current = toHexColor(quill.getFormat()[format]);

                if (current) {
                    pickers[format].fromString(current);
                }

            }

            pickers[format].show();

        }

        Array.prototype.forEach.call(menuButtons, function (button) {

            button.addEventListener('click', function () {

                if (htmlMode) {
                    return;
                }

                if (openButton === button) {
                    close(true);
                } else {
                    open(button);
                }

            });

            var panel = panelOf(button);

            if (!panel) {
                return;
            }

            var format = panel.getAttribute('data-format');

            Array.prototype.forEach.call(panel.querySelectorAll('button'), function (swatch) {

                /* 【押したときに、書く欄から目（カーソル）を奪わない】（下の preventFocusSteal と同じ理由） */
                swatch.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                });

                swatch.addEventListener('click', function () {

                    /* 「その他の色…」 */
                    if (swatch.hasAttribute('data-more')) {
                        close(false);
                        openPicker(button, format);
                        return;
                    }

                    var name = swatch.getAttribute('data-value');

                    applyColor(format, name ? themeColor(format, name) : null, null);
                    close(false);

                });

            });

            panel.addEventListener('keydown', function (e) {

                if (e.key === 'Escape') {
                    e.preventDefault();
                    close(true);
                }

            });

        });

        /* 小窓の外を押したら閉じる（カラーピッカーの中は「外」に数えない） */
        document.addEventListener('mousedown', function (e) {

            if (!openButton) {
                return;
            }

            var panel = panelOf(openButton);

            if (panel && (panel.contains(e.target) || openButton.contains(e.target))) {
                return;
            }

            if (e.target.closest && e.target.closest('.jscolor-wrap')) {
                return;
            }

            close(false);

        });

    })();


    /* ============================================================
       ツールバーのボタンを押しても、書く欄からカーソルを奪わない
       ============================================================
       【変換中に B／I が効かなかった本当の理由】（テストで、Galaxy で起きた）
       ボタンを押すと、ブラウザはカーソル（focus）をボタンへ移す。書く欄からカーソルが抜けると、日本語入力は
       **その場で変換を確定してしまう**（compositionend）。そのあとで click が届くので、Quill が書式を付けるときには
       もう「変換中」ではなく、しかも確定した字は選ばれていない＝カーソルの位置にだけ付いて、確定した字は太字にならない。
       手当て（変換中に押されたボタンを数える）は、click の時点で「変換中」を見ていたので、一度も数えられていなかった。

       【直し方】ボタンの mousedown の既定の動き（カーソルを移す）を止める。カーソルが書く欄に残るので、
       変換は確定されず、click の時点でまだ「変換中」になる（下の setupCompositionFormats が数えられる）。
       ついでに、スマホではボタンを押してもキーボードが閉じなくなる。
       タッチでも、指を離したあとの mousedown（互換のため、ブラウザが作る）で止められる。
       キーボードの人は Tab でボタンへ来るので、関係ない */
    (function preventFocusSteal() {

        Array.prototype.forEach.call(toolbar.querySelectorAll('button'), function (button) {
            button.addEventListener('mousedown', function (e) {
                e.preventDefault();
            });
        });

    })();


    /* ============================================================
       変換中に押した「太字」などを、確定した文字に付ける（作り、直した）
       ============================================================
       変換が始まった位置（compositionstart）を覚えておき、変換中にツールバーで押された文字の飾り（太字・斜体・下線・取り消し線）を控え、
       変換が終わったら（compositionend）、その範囲（始まった位置 〜 今のカーソル）に付ける。
       【押した回数が奇数なら、始まった位置の状態の逆にする】太字の中で B を押したら、外す。

       【変換中は、Quill にボタンの処理をさせない】
       変換中の字のまわりの中身を Quill が書き換えると、日本語入力が壊れる（字が二重になる・変換が切れる）ことがある。
       押されたことを数え、ボタンの見た目だけ「押された」にして、Quill のボタンの処理には届けない（捕捉の段階で止める）。
       付けるのは、変換が確定したあとに1回だけ。

       【開発機では、合成したイベントでしか確かめられない】（ブラウザのペインでは日本語入力が使えない）。実機で確かめる */
    (function setupCompositionFormats() {

        var INLINE = ['bold', 'italic', 'underline', 'strike'];
        var startIndex = null;
        var pressed = {};

        editorRoot.addEventListener('compositionstart', function () {

            var range = quill.getSelection();

            startIndex = range ? range.index : null;
            pressed = {};

            /* 【変換が始まったら、案内の文字（プレースホルダー）を消す】（開発者と決めた形）
               Quill は、変換中の字をまだ中身に入れていないので「空の欄」のまま＝案内の文字が、変換中の字と重なって出続けた。
               ふつうの入力欄（トップページの名前の欄）は打ち始めた瞬間に消える。それに合わせる。
               変換を取り消して空に戻ったら、下の compositionend で印を外すので、また出る（Content/Site.css の is-composing） */
            editorRoot.classList.add('is-composing');

        });

        editorRoot.addEventListener('compositionend', function () {
            editorRoot.classList.remove('is-composing');
        });

        function isComposing() {
            return !!(quill.composition && quill.composition.isComposing);
        }

        /* 捕捉の段階で数える（Quill のボタンの処理より先に） */
        toolbar.addEventListener('click', function (e) {

            if (!isComposing() || startIndex === null) {
                return;
            }

            var button = e.target.closest ? e.target.closest('button') : null;

            if (!button) {
                return;
            }

            var name = null;

            INLINE.forEach(function (n) {
                if (button.classList.contains('ql-' + n)) {
                    name = n;
                }
            });

            if (!name) {
                return;
            }

            pressed[name] = (pressed[name] || 0) + 1;

            /* 見た目だけ先に切り替える（Quill の処理は止めるので、自分で） */
            var on = button.classList.toggle('ql-active');
            button.setAttribute('aria-pressed', on ? 'true' : 'false');

            e.preventDefault();
            e.stopPropagation();

        }, true);

        quill.on('composition-end', function () {

            var from = startIndex;
            var counts = pressed;

            startIndex = null;
            pressed = {};

            if (from === null || Object.keys(counts).length === 0) {
                return;
            }

            /* Quill が確定した字を中身に入れ終わるのを待ってから付ける */
            window.setTimeout(function () {

                var range = quill.getSelection();

                if (!range || range.index <= from) {
                    return;
                }

                var before = quill.getFormat(from, 0);

                Object.keys(counts).forEach(function (name) {

                    if (counts[name] % 2 === 0) {
                        return;
                    }

                    var turnOn = !before[name];

                    quill.formatText(from, range.index - from, name, turnOn, 'user');

                    /* このあと打つ字にも同じ書式を（ボタンを押したときの意味どおり） */
                    quill.format(name, turnOn, 'user');

                });

            }, 0);

        });

    })();


    /* ============================================================
       HTMLモード（書く画面 ⇔ HTML を直接見る・直す画面）
       ============================================================
。**使える書き方は、ボタンで作れるものと同じ範囲**。
       範囲の外（iframe・style・決まっていない class など）は、書く画面へ戻したとき（Quill が落とす）と、
       保存するとき（サニタイザーが落とす）に取り除かれる。**黙って消さない**——戻すときに、何を取り除いたかを知らせる。

       【見分ける一覧は ArticleHtml.vb が持つ】（data-allowed で受け取っている）
       【HTMLモードの欄は送らない】name を付けていない。送るのは BodyBox だけで、包むのもそちら */
    var htmlMode = false;

    /* HTML を読みやすくするため、塊の終わりで改行を入れる（コードの囲みの中身には触らない＝タグが無いため） */
    function prettyHtml(html) {
        return html
            .replace(/(<\/(p|h2|h3|blockquote|pre|li|ul|ol)>|<hr>)/g, '$1\n')
            .replace(/(<(ul|ol)>)/g, '$1\n');
    }

    /* 使えない書き方を拾い出す（戻り値は、知らせに出す短い名前の一覧。重なりは除く） */
    function findDisallowed(html) {

        var found = [];

        function add(label) {
            if (found.indexOf(label) < 0) {
                found.push(label);
            }
        }

        var doc = new DOMParser().parseFromString('<body>' + html + '</body>', 'text/html');

        Array.prototype.forEach.call(doc.body.querySelectorAll('*'), function (el) {

            var tag = el.tagName.toLowerCase();

            if (ALLOWED.tags.indexOf(tag) < 0) {
                add('<' + tag + '>');
                return;
            }

            Array.prototype.forEach.call(el.attributes, function (attr) {

                var name = attr.name.toLowerCase();

                if (ALLOWED.attributes.indexOf(name) < 0) {
                    add(name + ' の指定');
                    return;
                }

                /* 【画像の4つ（src・alt・width・height）は、img の上でだけ】（ArticleHtml.vb の NormalizeImage と同じ決まり） */
                if ((ALLOWED.imageAttributes || []).indexOf(name) >= 0) {

                    if (tag !== 'img') {
                        add('<' + tag + '> の ' + name + ' の指定');
                        return;
                    }

                    if (name === 'src' && !(IMAGE_SRC_PATTERN && IMAGE_SRC_PATTERN.test(attr.value))) {
                        add('外部サイトなどの画像（画像のボタンで貼った画像だけが使えます）');
                    }

                    if ((name === 'width' || name === 'height') && !/^[1-9][0-9]{0,3}$/.test(attr.value)) {
                        add('<img> の ' + name + '="' + attr.value + '"');
                    }

                    return;

                }

                /* 【style は、字の飾りの要素（span・strong・em・u・s・a）の色の2つ・#RRGGBB だけ】
                   （ArticleHtml.vb の NormalizeStyle・StyleElements と同じ決まり。一覧はサーバーから受け取る） */
                if (name === 'style') {

                    if (ALLOWED.styleElements.indexOf(tag) < 0) {
                        add('<' + tag + '> の style の指定');
                        return;
                    }

                    attr.value.split(';').forEach(function (declaration) {

                        if (!declaration.trim()) {
                            return;
                        }

                        var colon = declaration.indexOf(':');
                        var prop = colon > 0 ? declaration.substring(0, colon).trim().toLowerCase() : declaration.trim();
                        var val = colon > 0 ? declaration.substring(colon + 1).trim() : '';

                        if (ALLOWED.styles.indexOf(prop) < 0 || !toHexColor(val)) {
                            add('style の「' + declaration.trim() + '」');
                        }

                    });

                    return;

                }

                if (name === 'class') {

                    attr.value.split(/\s+/).forEach(function (cls) {
                        if (cls && ALLOWED.classes.indexOf(cls) < 0) {
                            add('class="' + cls + '"');
                        }
                        /* 【画像の大きさ・枠・出す画面の class は、img の上でだけ】（ArticleHtml.vb の NormalizeImage と同じ決まり） */
                        if (cls && tag !== 'img' &&
                            ((ALLOWED.imageSizeClasses || []).indexOf(cls) >= 0 ||
                             cls === ALLOWED.imageNoFrameClass ||
                             (ALLOWED.imageDeviceClasses || []).indexOf(cls) >= 0)) {
                            add('<' + tag + '> の class="' + cls + '"');
                        }
                    });

                }

                if (name === 'href') {

                    var m = /^\s*([a-z][a-z0-9+.\-]*):/i.exec(attr.value);

                    if (m && ALLOWED.schemes.indexOf(m[1].toLowerCase()) < 0) {
                        add(m[1].toLowerCase() + ': のリンク');
                    }

                }

            });

        });

        return found;

    }

    /* HTMLモードの欄の中身を、書く画面へ戻す（戻り値＝取り除いたものの一覧） */
    function applyHtmlSource() {

        var html = htmlSource.value;
        var removed = findDisallowed(html);

        quill.setContents(quill.clipboard.convert({ html: html }), 'user');

        return removed;

    }

    function removedMessage(removed) {
        return '使えない書き方を取り除きました（' + removed.join('、') + '）。';
    }

    function setHtmlMode(on) {

        if (on === htmlMode) {
            return;
        }

        if (on) {

            htmlSource.value = prettyHtml(semanticHtml());

        } else {

            var removed = applyHtmlSource();

            if (removed.length) {
                showNotice(removedMessage(removed));
            } else {
                clearNotice();
            }

        }

        htmlMode = on;

        /* 見せる欄を入れ替える（Quill の欄は、枠ごと隠す） */
        quill.container.hidden = on;
        htmlSource.hidden = !on;

        if (htmlHint) {
            htmlHint.hidden = !on;
        }

        htmlToggle.setAttribute('aria-pressed', on ? 'true' : 'false');
        htmlToggle.classList.toggle('ql-active', on);

        /* HTMLモードの間は、ほかのボタンは使えない（押しても、見えていない Quill の中身が変わるだけ） */
        Array.prototype.forEach.call(toolbar.querySelectorAll('button'), function (button) {
            if (button !== htmlToggle) {
                button.disabled = on;
            }
        });

        copyToSource();

        if (on) {
            htmlSource.focus();
        } else {
            quill.focus();
        }

    }

    if (htmlToggle && htmlSource) {

        htmlToggle.addEventListener('click', function () {
            setHtmlMode(!htmlMode);
        });

        htmlSource.addEventListener('input', function () {
            copyToSource();
        });

    }


    /* ============================================================
       記事の画像
       ============================================================
       画像のボタン・写真の貼り付け・落とし込みの3つの入口から、同じ道を通る：
         選ぶ → 縮める（GIF はそのまま）→ 確かめの小窓（縮めた実物・説明の欄）→ 「貼る」で受け口へ送る → 本文に入れる

       【縮め方は、部屋の画像（Scripts/chat-room.js の ImageUpload）の写し】
       部屋のページとこのページは読み込むファイルが別なので、写して持っている。**片方を直したら、もう片方も見ること**
       （GIF はアニメが止まるので縮めない／それ以外は canvas で長辺を縮めて JPEG か PNG／透明があれば PNG／上限に収まるまで5段階）。
       【canvas で描き直すと、撮った場所（GPS）などの EXIF は入らない】（開発機の Chrome で確かめた）。
       それでも受け口（Articles/UploadImage.aspx）がもう一度取り除く——ここは画面の親切であって、守りではない。

       【送り方は multipart】本番の手前の検査（WAF）が、生のバイト列を1割ほど 403 で弾く（部屋の画像で踏んだ）。

       【本文に入れるのは、受け口が返した src だけ】/Uploads/articles/番号/GUID.拡張子。
       サニタイザー（ArticleHtml.vb の NormalizeImage）も、この形の画像だけを残す。
       width・height も入れる（画像が届く前に場所を取る。CSS の max-width: 100% ＋ height: auto で枠に収まる）。
       説明（alt）は空でもよい（空なら読み上げソフトは飛ばす） */
    var ArticleImage = (function () {

        var UPLOAD_URL = wrapper.getAttribute('data-image-upload-url') || '';
        var MAX_BYTES = parseInt(wrapper.getAttribute('data-image-max-bytes'), 10) || 0;
        var MAX_EDGE = parseInt(wrapper.getAttribute('data-image-max-edge'), 10) || 1280;
        var JPEG_QUALITY = parseFloat(wrapper.getAttribute('data-image-quality')) || 0.85;

        var ACCEPT = 'image/png,image/jpeg,image/gif,image/bmp,image/x-icon,image/vnd.microsoft.icon,image/webp';
        var TYPE_GIF = 'image/gif';

        /* 収まらなかったときに、順に試す縮め方（部屋の画像と同じ） */
        var RETRY_STEPS = [
            { edgeScale: 1.0, quality: 1.0 },
            { edgeScale: 1.0, quality: 0.75 },
            { edgeScale: 0.75, quality: 0.7 },
            { edgeScale: 0.5, quality: 0.65 },
            { edgeScale: 0.35, quality: 0.6 }
        ];

        /* 読み込むことすら試さない大きさ（端末を守る安全弁。部屋の画像と同じ） */
        var MAX_SOURCE_BYTES = 32 * 1024 * 1024;

        var dialog = document.getElementById('articleImageDialog');
        var title = document.getElementById('articleImageDialogTitle');
        var preview = document.getElementById('articleImagePreview');
        var sizeText = document.getElementById('articleImageSize');
        var altInput = document.getElementById('articleImageAlt');
        var status = document.getElementById('articleImageStatus');
        var okBtn = document.getElementById('articleImageOk');
        var rechooseBtn = document.getElementById('articleImageRechoose');
        var cancelBtn = document.getElementById('articleImageCancel');

        /* 大きさ（小・中・大）と枠、出す画面 */
        var sizeRadios = dialog ? dialog.querySelectorAll('input[name="articleImageSizeChoice"]') : [];
        var deviceRadios = dialog ? dialog.querySelectorAll('input[name="articleImageDeviceChoice"]') : [];
        var frameToggle = document.getElementById('articleImageFrame');
        var previewBox = preview ? preview.parentNode : null;

        function chosenSize() {
            for (var i = 0; i < sizeRadios.length; i++) {
                if (sizeRadios[i].checked) {
                    return sizeRadios[i].value;
                }
            }
            return 'l';
        }

        /* 'both'（両方）・'pc'（PCだけ）・'sp'（スマホだけ） */
        function chosenDevice() {
            for (var i = 0; i < deviceRadios.length; i++) {
                if (deviceRadios[i].checked) {
                    return deviceRadios[i].value;
                }
            }
            return 'both';
        }

        function setChoices(size, framed, device) {

            Array.prototype.forEach.call(sizeRadios, function (radio) {
                radio.checked = (radio.value === (size || 'l'));
            });

            Array.prototype.forEach.call(deviceRadios, function (radio) {
                radio.checked = (radio.value === (device || 'both'));
            });

            if (frameToggle) {
                frameToggle.checked = framed;
            }

            syncPreviewFrame();

        }

        function isFramed() {
            return !frameToggle || frameToggle.checked;
        }

        /* 見本に、枠のオン／オフをその場で映す */
        function syncPreviewFrame() {
            if (previewBox) {
                previewBox.classList.toggle('is-noframe', !isFramed());
            }
        }

        if (frameToggle) {
            frameToggle.addEventListener('change', syncPreviewFrame);
        }

        var input = null;

        /* 今の小窓の中身。mode＝'insert'（貼る）か 'edit'（説明を直す）
           pending … 貼る前の画像（{ blob, width, height }）
           range   … 貼る位置（選んだときのカーソル）
           target  … 説明を直す画像の <img>
           generation … 用意の途中で「やめる」「選び直す」が押されたら、古い結果を捨てるための番号 */
        var state = { mode: null, pending: null, range: null, target: null, previewUrl: null, uploading: false, generation: 0, opener: null };

        if (!UPLOAD_URL || !dialog || MAX_BYTES <= 0) {
            /* 小窓が無い・貼れないアカウント：ボタンを押したら理由だけ出す */
            return {
                choose: function () { showNotice('このアカウントでは、画像を貼れません。'); },
                fromFile: function () { showNotice('このアカウントでは、画像を貼れません。'); },
                editAlt: function () { }
            };
        }

        function mb(bytes) {
            return Math.round(bytes / 1048576 * 10) / 10 + 'MB';
        }

        function say(text, isError) {
            status.textContent = text || '';
            status.classList.toggle('is-error', !!text && !!isError);
        }

        function ensureInput() {

            if (input) {
                return input;
            }

            input = document.createElement('input');
            input.type = 'file';
            input.accept = ACCEPT;
            input.style.display = 'none';

            input.addEventListener('change', function () {

                var file = input.files && input.files[0];

                /* 同じファイルをもう一度選んでも change が飛ぶように、空へ戻す */
                input.value = '';

                if (file) {
                    fromFile(file, state.range);
                }

            });

            document.body.appendChild(input);

            return input;

        }

        /* 画像のボタン：今のカーソルの位置を覚えてから、写真を選ぶ画面を開く */
        function choose() {

            if (htmlMode) {
                showNotice('HTMLモードの間は、画像を貼れません。書く画面に戻してから押してください。');
                return;
            }

            state.range = quill.getSelection(true);
            state.opener = document.activeElement;

            ensureInput().click();

        }

        function readImage(file) {

            return new Promise(function (resolve, reject) {

                var url = URL.createObjectURL(file);
                var img = new Image();

                img.onload = function () { URL.revokeObjectURL(url); resolve(img); };
                img.onerror = function () { URL.revokeObjectURL(url); reject(); };

                img.src = url;

            });

        }

        function toBlob(img, maxEdge, quality) {

            return new Promise(function (resolve, reject) {

                var scale = Math.min(1, maxEdge / Math.max(img.naturalWidth, img.naturalHeight));
                var width = Math.max(1, Math.round(img.naturalWidth * scale));
                var height = Math.max(1, Math.round(img.naturalHeight * scale));

                var canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;

                var ctx = canvas.getContext('2d');

                if (!ctx) {
                    reject();
                    return;
                }

                ctx.drawImage(img, 0, 0, width, height);

                var hasAlpha = false;

                try {
                    var data = ctx.getImageData(0, 0, width, height).data;
                    for (var i = 3; i < data.length; i += 4) {
                        if (data[i] < 255) { hasAlpha = true; break; }
                    }
                } catch (e) {
                    hasAlpha = true;
                }

                canvas.toBlob(function (blob) {
                    if (!blob) { reject(); return; }
                    resolve({ blob: blob, width: width, height: height });
                }, hasAlpha ? 'image/png' : 'image/jpeg', quality);

            });

        }

        /* 選ばれた（貼られた・落とされた）1枚を、確かめの小窓まで連れていく */
        function fromFile(file, range) {

            if (state.uploading) {
                return;
            }

            if (htmlMode) {
                showNotice('HTMLモードの間は、画像を貼れません。書く画面に戻してから貼ってください。');
                return;
            }

            state.range = range || quill.getSelection(true);

            var gen = ++state.generation;

            openDialog('insert');
            say('画像を用意しています…');

            if (file.type === TYPE_GIF) {

                if (file.size > MAX_BYTES) {
                    say('アニメーションGIFは、そのままの大きさで送るため、' + mb(MAX_BYTES) + 'までです。', true);
                    return;
                }

            } else if (file.size > MAX_SOURCE_BYTES) {

                say('元の画像が大きすぎて、開けませんでした（' + mb(MAX_SOURCE_BYTES) + 'まで）。', true);
                return;

            }

            readImage(file).then(function (img) {

                if (file.type === TYPE_GIF) {
                    prepared(gen, file, img.naturalWidth, img.naturalHeight);
                    return;
                }

                shrink(img, 0, gen);

            }, function () {

                if (gen === state.generation) {
                    say('この形式の画像は貼れません。対応しているのは PNG / JPG / GIF / BMP / ICO / WebP です。', true);
                }

            });

        }

        function shrink(img, step, gen) {

            if (step >= RETRY_STEPS.length) {
                if (gen === state.generation) {
                    say('画像が大きすぎます（' + mb(MAX_BYTES) + 'まで）。', true);
                }
                return;
            }

            var s = RETRY_STEPS[step];

            toBlob(img, Math.round(MAX_EDGE * s.edgeScale), JPEG_QUALITY * s.quality).then(function (result) {

                if (result.blob.size > MAX_BYTES) {
                    shrink(img, step + 1, gen);
                    return;
                }

                prepared(gen, result.blob, result.width, result.height);

            }, function () {

                if (gen === state.generation) {
                    say('この画像は貼れませんでした。別の画像でお試しください。', true);
                }

            });

        }

        function prepared(gen, blob, width, height) {

            if (gen !== state.generation || state.mode !== 'insert') {
                return;
            }

            clearPreviewUrl();

            state.pending = { blob: blob, width: width, height: height };
            state.previewUrl = URL.createObjectURL(blob);

            preview.src = state.previewUrl;
            preview.hidden = false;
            sizeText.textContent = width + ' × ' + height + '（' + Math.max(1, Math.round(blob.size / 1024)) + 'KB）';

            okBtn.disabled = false;
            say('');

            altInput.focus();

        }

        function clearPreviewUrl() {
            if (state.previewUrl) {
                URL.revokeObjectURL(state.previewUrl);
                state.previewUrl = null;
            }
        }

        /* 小窓を開く（mode＝'insert' か 'edit'） */
        function openDialog(mode) {

            state.mode = mode;

            if (!state.opener) {
                state.opener = document.activeElement;
            }

            title.textContent = (mode === 'edit') ? '画像を直す' : '画像を貼る';
            okBtn.textContent = (mode === 'edit') ? '決める' : '貼る';
            rechooseBtn.hidden = (mode === 'edit');

            if (mode === 'insert') {
                state.pending = null;
                clearPreviewUrl();
                preview.removeAttribute('src');
                preview.hidden = true;
                sizeText.textContent = '';
                altInput.value = '';
                okBtn.disabled = true;

                /* 貼るときは「大・枠あり・両方に出す」 */
                setChoices('l', true, 'both');
            }

            dialog.hidden = false;

        }

        function closeDialog() {

            state.generation++;
            state.mode = null;
            state.pending = null;
            state.target = null;
            clearPreviewUrl();

            dialog.hidden = true;
            say('');

            var back = state.opener;
            state.opener = null;

            /*
                開いたところへ戻す（画像のボタン・書く欄）。

                【焦点を戻すときに、ページを動かさないこと】
                書く欄（.ql-editor）は contenteditable なので、focus() すると
                **ブラウザが「今の選択位置」までスクロールする**。画像を押して小窓を開いた場合、
                Quill が覚えている選択位置は本文の先頭のままなので、
                **閉じた瞬間にページのいちばん上まで戻っていた**
                （開発機で再現＝スクロール 13774px → 533px。13241px 飛んでいた）。
                小窓は画面に覆いかぶさるだけでページを動かさないので、
                **動かさずに閉じれば、押した画像がそのまま目の前にある**。

                preventScroll を見ないブラウザのために、位置を控えて戻す形も重ねてある
                （どちらか片方が効けばよい）。
            */
            var keepX = window.pageXOffset;
            var keepY = window.pageYOffset;

            if (back && typeof back.focus === 'function' && document.contains(back)) {
                try {
                    back.focus({ preventScroll: true });
                } catch (err) {
                    back.focus();
                }
            } else {
                quill.focus();
            }

            if (window.pageXOffset !== keepX || window.pageYOffset !== keepY) {
                window.scrollTo(keepX, keepY);
            }

        }

        /* 書く欄の中の画像を押した：説明を直す */
        function editAlt(img) {

            if (state.uploading || htmlMode) {
                return;
            }

            state.target = img;
            state.opener = document.activeElement;

            openDialog('edit');

            preview.src = img.getAttribute('src');
            preview.hidden = false;
            sizeText.textContent = '';
            altInput.value = img.getAttribute('alt') || '';
            okBtn.disabled = false;

            /* 今の形で開く（class が無い古い画像は「大・枠あり・両方に出す」） */
            setChoices(imageSizeOf(img) || 'l',
                       !(IMAGE_NOFRAME_CLASS && img.classList.contains(IMAGE_NOFRAME_CLASS)),
                       imageDeviceOf(img));

            altInput.focus();
            altInput.select();

        }

        /* 「貼る」（か「決める」） */
        function ok() {

            if (okBtn.disabled || state.uploading) {
                return;
            }

            var alt = altInput.value.trim();

            if (state.mode === 'edit') {

                var blot = state.target ? Quill.find(state.target) : null;

                if (blot && blot !== quill) {
                    /* 空の説明も属性として残る（上の ArticleImageBlot）。大きさ・枠・出す画面も一緒に */
                    quill.formatText(quill.getIndex(blot), 1,
                        { alt: alt, imgsize: chosenSize(), imgframe: isFramed() ? 'on' : 'off',
                          imgdevice: chosenDevice() }, 'user');
                    copyToSource();
                    markDirty();
                }

                closeDialog();
                return;

            }

            if (!state.pending) {
                return;
            }

            upload(state.pending, alt, chosenSize(), isFramed(), chosenDevice());

        }

        function upload(pending, alt, size, framed, device) {

            state.uploading = true;
            okBtn.disabled = true;
            rechooseBtn.disabled = true;
            say('送っています…');

            var form = new FormData();
            form.append('image', pending.blob, 'image');

            window.fetch(UPLOAD_URL, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'X-Image-Width': String(pending.width),
                    'X-Image-Height': String(pending.height)
                },
                body: form
            }).then(function (res) {

                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }

                return res.json();

            }).then(function (reply) {

                state.uploading = false;
                rechooseBtn.disabled = false;

                if (!reply || !reply.ok) {
                    okBtn.disabled = false;
                    say((reply && reply.reason) || '画像を貼れませんでした。もう一度お試しください。', true);
                    return;
                }

                insertImage(reply.src, reply.width, reply.height, alt, size, framed, device);
                closeDialog();

            }).catch(function (err) {

                state.uploading = false;
                okBtn.disabled = false;
                rechooseBtn.disabled = false;

                if (window.console) {
                    console.warn('[記事の編集] 画像を送れませんでした。', err);
                }

                say('画像を送れませんでした（通信が途切れたかもしれません）。もう一度「貼る」を押してください。', true);

            });

        }

        /* その位置の1文字が改行か（画像などの埋め込みは改行ではない。getText は埋め込みを数えないので、getContents で見る） */
        function isLineBreakAt(pos) {
            var ops = quill.getContents(pos, 1).ops;
            return ops.length > 0 && ops[0].insert === '\n';
        }

        /* 本文に入れる（覚えておいたカーソルの位置。無ければ末尾） */
        function insertImage(src, width, height, alt, size, framed, device) {

            var range = state.range;
            var index = range ? range.index : Math.max(0, quill.getLength() - 1);

            /* 【画像は、1枚ずつ自分の行（段落）に置く】（テスト）
               Quill の画像は文字と同じ「行の中の1つ」なので、続けて貼ると同じ行に横並びでくっついた。
               前に文字（や画像）があれば、先に改行を入れてから貼り、後ろにも改行が無ければ入れる。
               上下のすき間は CSS（Content/Site.css の「画像」）の余白で空ける */
            if (index > 0 && !isLineBreakAt(index - 1)) {
                quill.insertText(index, '\n', 'user');
                index += 1;
            }

            quill.insertEmbed(index, 'image', src, 'user');

            if (!isLineBreakAt(index + 1)) {
                quill.insertText(index + 1, '\n', 'user');
            }

            /* 【新しく貼る画像は、大きさの class を必ず付ける】（「大」も article-img-l と書く。付いていない＝古い画像、と読めるように） */
            quill.formatText(index, 1, {
                alt: alt, width: String(width), height: String(height),
                imgsize: size || 'l', imgframe: framed === false ? 'off' : 'on',
                imgdevice: device || 'both'
            }, 'user');

            quill.setSelection(index + 1, 0, 'silent');

            copyToSource();
            markDirty();

        }

        /* === 小窓の中の操作 === */
        okBtn.addEventListener('click', ok);

        cancelBtn.addEventListener('click', function () {
            if (!state.uploading) {
                closeDialog();
            }
        });

        rechooseBtn.addEventListener('click', function () {
            if (!state.uploading) {
                ensureInput().click();
            }
        });

        /* 説明の欄で Enter を押しても、form（保存）を送らせない。かわりに「貼る」「決める」 */
        altInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
                e.preventDefault();
                ok();
            }
        });

        /* Esc で閉じる。Tab は小窓の中で回す（aria-modal の約束） */
        dialog.addEventListener('keydown', function (e) {

            if (e.key === 'Escape') {
                e.preventDefault();
                if (!state.uploading) {
                    closeDialog();
                }
                return;
            }

            if (e.key !== 'Tab') {
                return;
            }

            var items = Array.prototype.filter.call(
                dialog.querySelectorAll('input, button'),
                function (el) { return !el.hidden && !el.disabled; });

            if (!items.length) {
                return;
            }

            var first = items[0];
            var last = items[items.length - 1];

            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }

        });

        /* 書く欄の中の画像を押したら、説明を直す */
        editorRoot.addEventListener('click', function (e) {
            if (e.target && e.target.tagName === 'IMG') {
                editAlt(e.target);
            }
        });

        return { choose: choose, fromFile: fromFile, editAlt: editAlt };

    })();

    /* ============================================================
       保存していない変更があるときに、ページを離れようとしたら確かめる
       ============================================================
       書いたものが一瞬で消える事故を防ぐため（リンクを押した・戻るを押した・タブを閉じた）。

       【ブラウザの確かめの窓（beforeunload）を使う】
       このサイトは確かめの窓を自前（vbChatConfirm）で出す決まりだが、ここは例外にした。
       タブを閉じる・戻る・アドレスを打ち替える、は自前の窓では止められない。
       ブラウザの窓なら全部止まり、文言もブラウザが決まった形で出す（こちらからは変えられない）。

       【保存・削除のボタンで送るときは、確かめない】
       submit の合図で「保存していない変更」を消す。
       ただし、**画面の検査（Validator）で送信が止められたときは消さない**——
       WebForms は form の onsubmit で false を返して止めるので、その合図は defaultPrevented で分かる。

       【保存に失敗して画面が戻ってきたときは、最初から「保存していない」】
       サーバーが textarea に data-unsaved="1" を付けて返す（Edit.aspx.vb の OnPreRender）。
       画面には打ったとおりに出ているが、DB には入っていないため */
    var dirty = source.getAttribute('data-unsaved') === '1';

    /* 【「公開ページを見る」「プレビュー」の横の1行】
       リンクの先に出るのは保存した内容なので、保存していない変更があるときだけ
       「保存していない変更は、まだ反映されていません。」を出す（Articles/Edit.aspx の #articleViewUnsaved）。
       新しい記事の画面にはリンクが無いので、この要素も無い（null なら何もしない） */
    var viewUnsaved = document.getElementById('articleViewUnsaved');

    /* 【保存の帯の「保存しました 16:02」】
       中身は、サーバーが ?done=saved で開いた画面に書いて返す（Articles/Edit.aspx.vb の ShowDoneNotice）。
       ここでやるのは2つだけ——①読み上げソフトに伝わるように入れ直す（下）②打ち直したら消す（clearSavedStatus） */
    var savedStatus = document.getElementById('articleSavedStatus');

    /* 【打ち直したら消す】出たままだと、そのあとの変更まで「保存した」ように見える。
       自動保存の1行（articleAutosaveStatus）とは別の要素なので、こちらを明示的に消す必要がある */
    function clearSavedStatus() {

        if (savedStatus) {
            savedStatus.textContent = '';
        }

    }

    /* 【公開中の記事で、編集中の控えがあるか】控えに自動保存した内容は「保存していない」ではないが、
       まだ公開ページには出ていない。

       【それを伝えるのは、自動保存の1行だけ】（1行へまとめた）
       前は、この1行（#articleViewUnsaved）にも「編集中の内容は、まだ公開ページに反映していません（「保存する」で反映します）。」と
       出していて、すぐ下の自動保存の1行「自動保存しました 20:53（公開ページには、まだ反映していません）」と**同じことを2回**言っていた。
       しかも「保存する」というボタンは、もう無い（公開中の記事のボタンは［更新する］）。
、未反映であることは帯の頭の黄色いボタン「未反映の変更をプレビュー」が伝え、自動保存の1行は「未反映の変更を自動保存しました 時刻」（下の autosaveSavedText） */
    var hasDraftNow = !!document.getElementById('articleDraftNotice');


    /* 自動保存できたときの1行（何を保存したかまで書くようにした。基本ルール X）
         公開中（控えへ）　　　　　 「未反映の変更を自動保存しました 10:19」（黄色い［未反映の変更をプレビュー］と言葉をそろえる）
         公開中（公開と同じに戻った）「公開中と同じ内容に戻ったので、未反映の変更を破棄しました 10:19」（控えは消える）
         下書き（新しい記事の最初も）「下書きを自動保存しました 10:19」
         非公開　　　　　　　　　　 「非公開のまま自動保存しました 10:19」
       状態は、保存してある状態（帯の印の data-status。Edit.aspx.vb の StatusKey）。自動保存は状態を変えないので、開いたときの値のままでよい */
    var statusMark = document.getElementById('articleStatusMark');
    var savedStatusKey = statusMark ? (statusMark.getAttribute('data-status') || 'draft') : 'draft';

    function autosaveSavedText(target, hasDraft, hadDraft, savedAt) {

        if (target === 'draft') {
            if (hasDraft) {
                return '未反映の変更を自動保存しました ' + savedAt;
            }
            /* 控えがもともと無かった（ほかの画面で消えていた、など）なら、何も破棄していない */
            return hadDraft
                ? '公開中と同じ内容に戻ったので、未反映の変更を破棄しました ' + savedAt
                : '公開中と同じ内容です（未反映の変更はありません）';
        }

        if (savedStatusKey === 'hidden') {
            return '非公開のまま自動保存しました ' + savedAt;
        }

        return '下書きを自動保存しました ' + savedAt;

    }

    function syncViewUnsaved() {

        if (!viewUnsaved) {
            return;
        }

        /* 【リンクがまだ出ていない間（新しい記事で、まだ保存していない）は出さない】
           何の「反映」の話か分からない1行で、帯が1行ぶん高くなるだけになるため */
        var viewLink = document.querySelector('#articleSaveBar [data-view-link]');
        var linkShown = !viewLink || !viewLink.hidden;

        if (dirty && linkShown) {
            viewUnsaved.textContent = '保存していない変更は、まだ反映されていません。';
            viewUnsaved.hidden = false;
        } else {
            viewUnsaved.hidden = true;
        }

    }

    /* 【変わった回数を数える】自動保存が送った「あと」に打った分があるなら、返事が来ても「保存していない」を消さない */
    var changeSeq = 0;

    function markDirty() {
        dirty = true;
        changeSeq++;
        clearSavedStatus();
        syncViewUnsaved();
        scheduleAutosave();
    }

    syncViewUnsaved();

    /* 【「保存しました」を、読み上げソフトにも伝える】
       role="status" の中身は、**ページが開いた時点から入っていると読まれない**（変わったときに読む決まり）。
       そこで、いったん空にしてから入れ直す（下の showSaveStatus・Scripts/site.js の「6」と同じ手）。
       【自動保存の1行には、これをやらない】数秒おきに時刻が変わるので、打つたびに読まれてしまう
       （あちらは aria-live も付けていない。決まり）。
       【Quill が読み込めなかったときは、ここまで来ない】文は画面には出ているので、読まれないだけである */
    if (savedStatus && savedStatus.textContent) {

        (function (message) {

            savedStatus.textContent = '';

            window.setTimeout(function () {
                savedStatus.textContent = message;
            }, 50);

        })(savedStatus.textContent);

    }

    copyToSource();

    quill.on('text-change', function () {
        copyToSource();
        markDirty();
        scheduleEditorToc();
    });

    /* ============================================================
       本文の右の、目次の列
       ============================================================
       開発者の判断：書く人の道具。**目次の3択に関係なく、見出しがあれば出す**。

       【打つたびに組み立て直さない】text-change は1文字ごとに飛ぶ。
       打つのが止まって少ししてから作り直す（自動保存と同じ考え方）。
       そのうえ**見出しの並びが変わっていなければ、DOM は触らない**——
       本文を打っているだけのときは、ここで何も起きない。

       【押すと、本文のその見出しへ飛ぶ】記事のページの目次と違って、
       書く画面には id が無い（id はサーバーが表示のときに付けるもの）ので、
       **要素そのものを覚えておいて**そこへスクロールする。

       【狭い画面では、CSS が消している】本文の幅がそのぶん狭くなるほうが困る。
       **かわりに、記事のページと同じ「丸いボタン → 下からせり上がる目次」を出す**（基本ルール P）。
       部品は Scripts/article-toc.js の window.VbTocSheet（記事のページと同じもの。新しく作らない）。
       どちらを出すかは、**右の列が CSS で消えているかを見て決める**（幅の数字を JS に写さない＝境目がずれない） */
    var tocAside = document.createElement('aside');
    tocAside.className = 'article-editor-toc';
    tocAside.setAttribute('aria-label', '本文の目次');

    var tocHeading = document.createElement('h2');
    tocHeading.className = 'article-toc-heading';
    tocHeading.textContent = '目次';
    tocAside.appendChild(tocHeading);

    var tocBody = document.createElement('div');
    tocAside.appendChild(tocBody);

    wrapper.parentNode.insertBefore(tocAside, wrapper.nextSibling);

    var tocTimer = null;
    var tocSignature = null;

    function scheduleEditorToc() {
        window.clearTimeout(tocTimer);
        tocTimer = window.setTimeout(buildEditorToc, 400);
    }

    function editorHeadings() {

        var found = [];
        var nodes = quill.root.querySelectorAll('h2, h3');

        for (var i = 0; i < nodes.length; i++) {

            var text = (nodes[i].textContent || '').replace(/\s+/g, ' ').trim();

            /* 中身が文字でない見出しは出さない（記事のページの目次と同じ決まり） */
            if (text.length === 0) {
                continue;
            }

            found.push({
                element: nodes[i],
                text: text,
                isSub: (nodes[i].tagName.toLowerCase() === 'h3')
            });

        }

        return found;
    }

    /* 【上に貼り付くツールバーのぶんだけ、余分に上げる】飛んだ先がツールバーの裏に隠れないように */
    function stickyOffset() {

        var head = wrapper.querySelector('.article-editor-head');

        return (head ? head.offsetHeight : 0) + 12;
    }

    function buildEditorToc() {

        var list = editorHeadings();

        var signature = list.map(function (one) {
            return (one.isSub ? '3:' : '2:') + one.text;
        }).join('\n');

        /* 並びも文字も変わっていなければ、触らない */
        if (signature === tocSignature) {
            return;
        }

        tocSignature = signature;

        while (tocBody.firstChild) {
            tocBody.removeChild(tocBody.firstChild);
        }

        if (list.length === 0) {

            var empty = document.createElement('p');
            empty.className = 'article-toc-empty';
            empty.textContent = '大見出し・小見出しを付けると、ここに出ます。';
            tocBody.appendChild(empty);

            if (tocSheet) {
                tocSheet.setList(emptyTocNote());
            }

            return;
        }

        tocBody.appendChild(makeTocList(list));

        if (tocSheet) {
            tocSheet.setList(makeTocList(list));
        }

    }

    /* 目次の一覧を1つ作る（右の列と、狭い画面の板の両方に、同じものを1つずつ） */
    function makeTocList(list) {

        var ol = document.createElement('ol');
        ol.className = 'article-toc-list';

        list.forEach(function (one) {

            var item = document.createElement('li');
            item.className = 'article-toc-item' + (one.isSub ? ' article-toc-item--sub' : '');

            /* 【<a> ではなく <button>】書く画面の見出しには id が無いので、行き先の URL を作れない */
            var jump = document.createElement('button');
            jump.type = 'button';
            jump.className = 'article-toc-link';
            jump.textContent = one.text;

            jump.addEventListener('click', function () {

                var top = one.element.getBoundingClientRect().top + window.pageYOffset - stickyOffset();

                window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });

            });

            item.appendChild(jump);
            ol.appendChild(item);

        });

        return ol;

    }

    /* 【狭い画面の目次】見出しが無いうちは、板に「付けるとここに出ます」の1行を入れる */
    function emptyTocNote() {
        var empty = document.createElement('p');
        empty.className = 'article-toc-empty';
        empty.textContent = '大見出し・小見出しを付けると、ここに出ます。';
        return empty;
    }

    var tocSheet = (window.VbTocSheet && window.VbTocSheet.create)
        ? window.VbTocSheet.create({ id: 'articleEditorTocSheet', modifier: 'editor', storeKey: 'vbchat_editor_toc_fab', list: emptyTocNote() })
        : null;

    /* 【右の列が出ていない幅でだけ、丸いボタンを出す】右の列の display を見て決める（CSS の @container と同じ境目になる）。
       【キーボードが出ている間は、ボタンを隠す】見えている範囲が狭く、ツールバーと保存の帯でもう埋まっているため。
       閉じたら（見えている範囲が戻ったら）また出る */
    function syncTocSheet() {

        if (!tocSheet) {
            return;
        }

        var wide = window.getComputedStyle(tocAside).display !== 'none';
        var vv = window.visualViewport;
        var keyboard = !!vv && (window.innerHeight - vv.height - vv.offsetTop) > 120;
        var available = !wide && !keyboard;

        tocSheet.fab.classList.toggle('is-available', available);

        if (!available) {
            tocSheet.close();
        }

    }

    window.addEventListener('resize', syncTocSheet);

    if (window.visualViewport) {
        window.visualViewport.addEventListener('resize', syncTocSheet);
    }

    buildEditorToc();
    syncTocSheet();

    var form = source.form;

    if (form) {

        /* タイトル・カテゴリ・並び順・HTMLモードの欄も見る。Quill の欄の input は上の text-change で見ているので、ここでは除く */
        /* 【画像の小窓の中の欄は数えない】説明・大きさ・枠は、「決める」「貼る」で本文に入ったときに text-change で数える */
        var imageDialogEl = document.getElementById('articleImageDialog');

        function isOutsideEditorParts(target) {
            return !editorEl.contains(target) && !(imageDialogEl && imageDialogEl.contains(target));
        }

        form.addEventListener('input', function (e) {
            if (isOutsideEditorParts(e.target)) {
                markDirty();
            }
        });

        form.addEventListener('change', function (e) {
            if (isOutsideEditorParts(e.target)) {
                markDirty();
            }
        });

        /* ============================================================
           送る直前（包む）
           ============================================================
           【画面の検査で止められた送信では、何もしない】（defaultPrevented）
           WebForms の onsubmit はこの関数より先に走る（markup に書かれた分が先）。

           【HTMLモードのまま送るとき】使えない書き方が入っていたら、送るのをやめて書く画面へ戻し、何を取り除いたかを見せる。
           「確かめてから、もう一度押してください」と頼む（黙って取り除いて保存しない）。
           入っていなければ、書く画面へ静かに戻してから送る（送るのは、いつも Quill の出す形）。 */
        form.addEventListener('submit', function (e) {

            if (e.defaultPrevented) {
                return;
            }

            /* 【自動保存が途中なら、終わるのを待ってから送る】
               待たないと、手で押した保存のあとに、古い中身の自動保存が遅れて届き、新しい中身を上書きしうる。
               新しい記事なら、自動保存が作った記事の番号を受け取る前に送ると、記事が2つになる */
            if (autosave.inFlight) {

                e.preventDefault();

                autosave.waitingSubmitter = e.submitter || null;
                autosave.waitingSubmit = true;

                setAutosaveText('自動保存が終わるのを待っています…');

                return;

            }

            if (htmlMode) {

                var removed = findDisallowed(htmlSource.value);

                if (removed.length) {

                    e.preventDefault();
                    setHtmlMode(false);

                    var message = removedMessage(removed) +
                        '内容を確かめてから、もう一度「保存する」を押してください。';

                    showNotice(message);
                    showSaveStatus(message);

                    return;

                }

                setHtmlMode(false);

            }

            source.value = encodeBody(semanticHtml());

            dirty = false;

            /* ページを離れるので、この先の自動保存はしない（送る中身と、自動保存の中身が入れ違わないように） */
            stopAutosave();

        });

    }


    /* ============================================================
       自動保存
       ============================================================
       打つのが止まって数秒たったら、サーバー（Articles/Edit.aspx.vb の WebMethod AutoSave）へ送る。
         ・下書き・非公開 … 本体へ（プレビューにもすぐ出る）
         ・公開中       … 編集中の控えへ（公開ページは変わらない。「保存する」で反映する）
         ・新しい記事   … 最初の1回で下書きとして作り、番号を受け取る（以後はその記事を更新）
       指摘：ブラウザの戻るボタンなどを間違って押して、書いた内容が消えるほうがずっと怖い。

       【送る中身は、手で押す保存と同じ】本文は Quill の出す形を包んだもの（encodeBody）。
       サーバーで戻してから、必ず HtmlSanitizer を通す（ArticleStore.AutoSave）。**状態は送らない**（自動保存では変えない。変えるのは保存の帯のボタンだけ）。

       【一度に1本だけ】送っている間に打った分は、返事のあとにもう一度送る。
       【HTMLモードの間は送らない】その欄には、まだ使えない書き方が入っていうる。書く画面へ戻したときに送る。
       【これは守りではない】ログイン・自分の記事か・書けるロールかは、サーバーが毎回確かめる */
    var AUTOSAVE_URL = wrapper.getAttribute('data-autosave-url') || '';

    /* 打つのが止まってから送るまで */
    /* 二重送信を防ぐ目印を運ぶヘッダーの名前（Core/FormOnce.vb の HeaderName と同じ文字列にすること） */
    var FORM_ONCE_HEADER = 'X-VbChat-FormOnce';

    var AUTOSAVE_IDLE_MS = 3000;

    /* 打ち続けていても、最初の変更からこれだけたったら一度送る（長い文を打っている最中に、何も保存されないままにしない） */
    var AUTOSAVE_MAX_WAIT_MS = 30000;

    /* 通信に失敗したときに、もう一度試すまで */
    var AUTOSAVE_RETRY_MS = 15000;

    var autosaveStatus = document.getElementById('articleAutosaveStatus');
    var autoSavedIdInput = document.getElementById('articleAutoSavedId');
    var draftNotice = document.getElementById('articleDraftNotice');
    var draftPreviewLink = document.querySelector('[data-draft-preview]');
    /* 保存の帯の［破棄する］。黄色いボタンと同じときに出し、同じときに消す */
    var draftDiscardButton = document.querySelector('[data-draft-discard]');

    var autosave = {
        articleId: parseInt(wrapper.getAttribute('data-article-id'), 10) || 0,
        timer: null,
        firstChangeAt: 0,
        pending: false,
        inFlight: false,
        stopped: false,
        waitingSubmit: false,
        waitingSubmitter: null
    };

    function autosaveField(name) {
        return form ? form.querySelector('[data-autosave-field="' + name + '"]') : null;
    }

    function autosaveFieldValue(name) {
        var el = autosaveField(name);
        return el ? el.value : '';
    }

    function setAutosaveText(text) {
        if (autosaveStatus) {
            autosaveStatus.textContent = text;
        }
    }

    function stopAutosave() {
        autosave.stopped = true;
        window.clearTimeout(autosave.timer);
        autosave.timer = null;
    }

    function scheduleAutosave() {

        /* 【初期化の途中で呼ばれても落ちない】（autosave はこの下で作る。var の巻き上げで、それまでは undefined） */
        if (!autosave || !AUTOSAVE_URL || autosave.stopped) {
            return;
        }

        autosave.pending = true;

        var now = Date.now();

        if (!autosave.firstChangeAt) {
            autosave.firstChangeAt = now;
        }

        window.clearTimeout(autosave.timer);

        var wait = Math.min(AUTOSAVE_IDLE_MS, Math.max(0, autosave.firstChangeAt + AUTOSAVE_MAX_WAIT_MS - now));

        autosave.timer = window.setTimeout(runAutosave, wait);

    }

    /* ============================================================
       中身が変わっていないなら、送らない
       ============================================================
       【起きたこと】公開中の「使い方」で、状態を選び直しただけ（本文は1文字も変えていない）で、
       一覧に「未反映の編集あり」が付いた。欄のどれかが動くと「変わった」とみなして自動保存を送り、
       サーバーは「公開中の中身と同じなら控えを作らない」と比べるが、**本文を1文字ずつ比べる**ので、
       書く欄（Quill）を一度通っただけの本文が、保存してある本文と少しでも違う形だと「違う」になって、控えができた。

       【直し方】**送る前に、ここで比べる。** 比べる相手は「保存されていると分かっている中身」の写し（下の2つ）で、
       どちらも**同じ書く欄から、同じ手順で作った文字列**なので、形の違いで食い違うことが無い。

         lastSavedSnapshot  … いま保存されている中身（開いたとき＝DB の中身。自動保存が通るたびに、送った中身へ進める）
                                同じなら、**何も送らない**（控えも作られない）
         publishedSnapshot  … 開いたときの、公開中の中身（控えの無い状態で開いたときだけ持つ）
                                この画面で控えができたあと、ここへ戻ったら、**控えを捨てる**（「未反映の編集あり」を残さない）

       【保存が断られて戻ってきた画面（dirty で始まる）では、どちらも持たない】
       画面の中身は DB に入っていないので、比べる相手が無い。今までどおり、そのまま送る。

       【サーバーも、今までどおり比べている】（ArticleStore.AutoSave の SameAsArticle）。こちらは、その手前の1枚である */
    function autosaveFields() {

        return {
            title: autosaveFieldValue('title'),
            slug: autosaveFieldValue('slug'),
            categoryId: parseInt(autosaveFieldValue('category'), 10) || 0,
            sortOrder: autosaveFieldValue('sortOrder'),
            pinOrder: autosaveFieldValue('pinOrder'),
            /* 目次の3択。読めないときは 10＝入れない（サーバーも NormalizeTocMode で丸める） */
            tocMode: parseInt(autosaveFieldValue('tocMode'), 10) || 10,
            /* 見出しの番号の2択。読めないときは 10＝付けない */
            headingNumberMode: parseInt(autosaveFieldValue('headingNumberMode'), 10) || 10,
            html: semanticHtml()
        };

    }

    var lastSavedSnapshot = dirty ? null : JSON.stringify(autosaveFields());
    var publishedSnapshot = (!dirty && !hasDraftNow) ? lastSavedSnapshot : null;

    /* 控えを捨てる口（Articles/Edit.aspx.vb の WebMethod DiscardDraft）。自動保存の口と同じ場所にある */
    var DISCARD_URL = AUTOSAVE_URL.replace(/AutoSave$/, 'DiscardDraft');

    function runAutosave() {

        autosave.timer = null;

        if (!autosave.pending || autosave.stopped || autosave.inFlight || htmlMode) {
            return;
        }

        /* 【変換中（未確定）の字があるときは、まだ送らない】
           Quill は変換中の字を中身に入れていないので、今送ると、その字の無い本文が保存される。
           確定すると Quill の中身が変わって（text-change）、また送る予定が立つ。それまで少し待って見直す */
        if (editorComposing()) {
            autosave.timer = window.setTimeout(runAutosave, 500);
            return;
        }

        autosave.pending = false;
        autosave.firstChangeAt = 0;

        var sentSeq = changeSeq;
        var fields = autosaveFields();
        var snapshot = JSON.stringify(fields);

        /* 【保存されている中身と同じ】欄を触っただけ・打って消した、など。送らない */
        if (lastSavedSnapshot !== null && snapshot === lastSavedSnapshot) {

            dirty = false;
            syncViewUnsaved();

            return;

        }

        /* 【この画面でできた控えを、公開中の中身へ戻した】控えを捨てる（本文そのものは送らない） */
        var discard = hasDraftNow && publishedSnapshot !== null && snapshot === publishedSnapshot &&
                      autosave.articleId > 0 && DISCARD_URL !== AUTOSAVE_URL;

        autosave.inFlight = true;

        var payload = discard ? { articleId: autosave.articleId } : {
            articleId: autosave.articleId,
            title: fields.title,
            slug: fields.slug,
            categoryId: fields.categoryId,
            sortOrder: fields.sortOrder,
            pinOrder: fields.pinOrder,
            tocMode: fields.tocMode,
            headingNumberMode: fields.headingNumberMode,
            body: encodeBody(fields.html)
        };

        setAutosaveText('自動保存しています…');

        /* 【二重送信を防ぐ目印を、ヘッダーで運ぶ】（Core/FormOnce.vb の HeaderName と同じ文字列）
           新しい記事を自動保存が「足す」ときに、サーバーが確かめる（ArticleStore.Insert）。
           保存したあと、戻るボタンで「新しく書く」画面へ戻ると、ブラウザが欄の中身と目印を戻す
           （目印は site.js の「11」）。そこで打つと、自動保存が同じ記事をもう1本作っていた。
           引数（payload）に足さないこと＝WebMethod は引数が増えると、古い画面の自動保存を500で断る */
        var requestHeaders = { 'Content-Type': 'application/json; charset=utf-8' };
        var onceInput = document.querySelector('input[name="vbFormOnce"]');

        if (onceInput && onceInput.value) {
            requestHeaders[FORM_ONCE_HEADER] = onceInput.value;
        }

        window.fetch(discard ? DISCARD_URL : AUTOSAVE_URL, {
            method: 'POST',
            credentials: 'same-origin',
            headers: requestHeaders,
            body: JSON.stringify(payload)
        }).then(function (res) {

            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }

            return res.json();

        }).then(function (json) {

            handleAutosaveReply(json && json.d, sentSeq, snapshot);

        }).catch(function (err) {

            if (window.console) {
                console.warn('[記事の編集] 自動保存に失敗しました。', err);
            }

            /* 【通信の失敗は、少し待ってからもう一度】打った分は、まだ保存されていない */
            autosave.pending = true;

            failAutosave('自動保存できませんでした（通信が途切れたかもしれません）。少したったら、もう一度試します。');

            if (!autosave.stopped) {
                window.clearTimeout(autosave.timer);
                autosave.timer = window.setTimeout(runAutosave, AUTOSAVE_RETRY_MS);
            }

        }).then(function () {

            autosave.inFlight = false;

            /* 【手で押した保存が待っていたら、今送る】requestSubmit なら click の処理（確かめの窓など）を2回走らせない */
            if (autosave.waitingSubmit) {

                var submitter = autosave.waitingSubmitter;

                autosave.waitingSubmit = false;
                autosave.waitingSubmitter = null;

                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit(submitter || undefined);
                } else if (submitter) {
                    submitter.click();
                } else {
                    form.submit();
                }

                return;

            }

            if (autosave.pending && !autosave.timer) {
                scheduleAutosave();
            }

        });

    }

    function failAutosave(message) {
        setAutosaveText(message);
        showNotice(message);
    }

    /* snapshot … 送った中身の写し（通ったら、「いま保存されている中身」として覚える。上の lastSavedSnapshot） */
    function handleAutosaveReply(reply, sentSeq, snapshot) {

        if (!reply) {
            autosave.pending = true;
            failAutosave('自動保存できませんでした。少したったら、もう一度試します。');
            return;
        }

        if (!reply.ok) {

            /* 【ログインが切れた】それ以上は送らない（何度送っても断られる）。打った分は画面に残っている */
            if (reply.loggedOut) {
                stopAutosave();
            }

            /* 【二重送信だった】この画面の記事は、もう保存してある（戻るボタンで戻ってきた）。
               同じ理由を打つたびに出し続けないように、自動保存を止めて、理由をそのまま出す */
            if (reply.alreadySaved) {
                stopAutosave();
                failAutosave(reply.message);
                return;
            }

            /* 【中身で断られた（URL の名前が重なった・長すぎる、など）】次に何か変えたら、もう一度送る */
            failAutosave('自動保存できませんでした：' + reply.message);

            return;

        }

        /* 【通った】この中身が、いま保存されている（次に同じ中身なら、もう送らない） */
        lastSavedSnapshot = snapshot;

        /* 【前に断られたときの赤い知らせを、直った分だけ消す】
           空のまま［公開する］で断られたあと、タイトルを入れて自動保存が通っても、帯の
           「保存できませんでした。タイトルを入力してください。本文を入力してください。」が残ったまま、その下に「自動保存しました」が出ていた */
        clearFixedSaveErrors();

        if (reply.target === 'none') {
            setAutosaveText('');
            return;
        }

        /* 【新しい記事が作られた】番号を覚える。「保存する」を押したら、この記事を保存する（Edit.aspx.vb の AutoSavedIdField） */
        if (!autosave.articleId && reply.articleId) {

            autosave.articleId = reply.articleId;

            if (autoSavedIdInput) {
                autoSavedIdInput.value = String(reply.articleId);
            }

            /* 開き直しても同じ記事になるように、アドレスにも番号を付ける（ページは読み込み直さない） */
            var editUrl = '/account/articles/edit?id=' + encodeURIComponent(String(reply.articleId));

            try {
                window.history.replaceState(null, '', editUrl);
            } catch (e) {
                /* 付けられなくても、送る先（form の action）と番号の欄で足りる */
            }

            /* 【［プレビュー］を出す】［下書き保存］を押したときと同じように、
               自動保存で下書きができた時点で、記事のページ（持ち主だけのプレビュー）へのリンクを出す。
               リンクは Edit.aspx.vb の LoadArticle が新しい記事のときも描いておき（hidden）、行き先の頭（data-preview-base）を持たせてある。
               番号の URL で開く（記事のアドレスを付けていれば、記事のページが名前の URL へ移り直す） */
            var newViewLink = document.querySelector('#articleSaveBar [data-view-link]');

            if (newViewLink) {
                newViewLink.href = (newViewLink.getAttribute('data-preview-base') || '') + encodeURIComponent(String(reply.articleId));
                newViewLink.hidden = false;
            }

        }

        /* 送ったあとに打った分が無ければ、「保存していない」を消す（ページを離れても確かめない） */
        if (changeSeq === sentSeq) {
            dirty = false;
        }

        if (reply.target === 'draft') {

            var hadDraft = hasDraftNow;
            hasDraftNow = !!reply.hasDraft;

            if (draftPreviewLink) {
                draftPreviewLink.hidden = !hasDraftNow;
            }

            if (draftDiscardButton) {
                draftDiscardButton.hidden = !hasDraftNow;
            }

            if (draftNotice && !hasDraftNow) {
                draftNotice.hidden = true;
            }

            setAutosaveText(autosaveSavedText('draft', hasDraftNow, hadDraft, reply.savedAt));

        } else {

            setAutosaveText(autosaveSavedText(reply.target, false, false, reply.savedAt));

        }

        syncViewUnsaved();

        if (reply.removedUnsafe) {
            showNotice('使えない書き方が入っていたので、取り除いてから自動保存しました。');
        }

    }

    /* 【保存が断られて戻ってきた画面】打った中身は DB に入っていないので、開いた時点で一度自動保存する
       （状態の切り替えは送らない。断られた理由が名前の重なりなら、自動保存も同じ理由で断られ、そう出る） */
    if (dirty) {
        changeSeq++;
        scheduleAutosave();
    }

    window.addEventListener('beforeunload', function (e) {

        if (!dirty) {
            return;
        }

        e.preventDefault();
        e.returnValue = '';

    });

    /* 【自動保存が通ったら、直った欄の理由を消し、帯の理由を残りだけにする】
       ・タイトル・本文（要るのは公開になる保存だけ）… 中身が入っていれば直った
       ・そのほか（記事のアドレス・並び順・トップに固定）… 自動保存が通った＝その値で受け付けられた＝直った
       ・残った理由があれば、帯の1行はそれだけにする（無ければ空。画面の上の1行も空）
       理由を欄の上に出しているのは Scripts/site.js の「8」（vbFieldError）。消すのも、その口を通す */
    function clearFixedSaveErrors() {

        if (!form || !window.vbFieldError) {
            return;
        }

        Array.prototype.forEach.call(form.querySelectorAll('[data-error-id]'), function (field) {

            var isTitle = /TitleBox$/.test(field.id || '') && field.getAttribute('data-autosave-field') === 'title';
            var isBody = field === source;

            if ((isTitle || isBody) && !(field.value || '').trim()) {
                return;
            }

            window.vbFieldError.clear(field);

        });

        var left = Array.prototype.map.call(form.querySelectorAll('.field-error'), function (el) {
            return (el.textContent || '').trim();
        }).filter(function (t) { return t; });

        var box = document.querySelector('[data-validation-status~="article"]');

        if (box) {
            box.textContent = left.length
                ? (box.getAttribute('data-validation-status-prefix') || '') + left.join('')
                : '';
        }

        if (!left.length) {
            var top = document.querySelector('.admin-box > .account-error');
            if (top) {
                top.textContent = '';
            }
        }

    }

    /* 保存の帯の、理由の1行（Edit.aspx の data-validation-status。組の名前を2つ書いてある＝article articlePublish） */
    function showSaveStatus(text) {

        var box = document.querySelector('[data-validation-status~="article"]');

        if (!box) {
            return;
        }

        box.textContent = '';

        window.setTimeout(function () {
            box.textContent = text;
        }, 50);

    }


    /* ============================================================
       スマホでキーボードを出しても、保存の帯を押せるようにする
       ============================================================
       【作り替えた】（指摘：スマホこそ下の帯のボタンが要る）
       それまでは「打っている間は帯の貼り付けをやめ（is-typing）、かわりにツールバーの右端に『保存』を出す」形だった。
       今は **打っている間も、保存の帯をキーボードのすぐ上に出す**。仕組み（見えている範囲の下端に付き直す --vv-bottom ／
       打っている行が帯の裏に隠れたら送る）は、プロフィールの画面と同じ部品にまとめて Scripts/site.js の「13」へ移した。
       ツールバーを見えている範囲の上に付き直す --vv-top も、そちらが入れる。

       【ツールバーの右端の「保存」はやめた】2つあると「どっちを押すか」になる。そのうえ、あの「保存」は
       押してもカーソルを奪わない作り（mousedown を止める）だったので、**変換中の字を確定させないまま保存していた**。
       （Galaxy で：未確定の字が保存されない／本文が全部未確定だと「本文を入力してください」と断られ、
       本文の欄へカーソルを戻す動きが変換中の欄とぶつかって、画面が上下にカクカク動いた。開発機でも
       CDP の Input.imeSetComposition で作って、同じことが起きるのを確かめた） */


    /* ============================================================
       変換中（未確定）の字があるときに保存を押したら、確定させてから保存する
       ============================================================
       【なぜ要るのか】Quill は、変換中の字を、確定する（compositionend）まで自分の中身に入れない。
       保存の直前に本文を書き写す（semanticHtml）と、変換中の字は入らない。
       ふつうはボタンを押した瞬間にカーソルがボタンへ移り、日本語入力が確定するが、
       ・カーソルを奪わないボタン（前のツールバーの「保存」）
       ・カーソルが移るより先に送ってしまう端末
       では、確定しないまま送られる。
       【やり方】送るボタンの click を、いちばん先（window の捕捉の段階）で受け取る。変換中なら止めて、
       本文の欄からカーソルを外し（＝日本語入力が確定する）、Quill が確定した字を中身に入れ終わるのを待ってから、
       **同じボタンをもう一度押す**。2度目は素通りさせる（印を付けておく）。
       Scripts/site.js の「5」（押す前に確かめる）も click の捕捉で動くが、こちらは window・あちらは document なので、こちらが先に動く
       ＝確かめの窓は、確定したあとの2度目の click で出る。
       【待つ上限は 600ms】確定の合図が来ない端末でも、送れなくならないように */
    function editorComposing() {
        return !!(quill.composition && quill.composition.isComposing) || editorRoot.classList.contains('is-composing');
    }

    (function setupCommitBeforeSave() {

        var PASS = 'vbAfterCommit';

        window.addEventListener('click', function (e) {

            var button = e.target && e.target.closest ? e.target.closest('input[type="submit"], button[type="submit"]') : null;

            if (!button || button.form !== form) {
                return;
            }

            if (button[PASS]) {
                button[PASS] = false;
                return;
            }

            if (!editorComposing()) {
                return;
            }

            e.preventDefault();
            e.stopImmediatePropagation();

            var done = false;

            var finish = function () {

                if (done) {
                    return;
                }

                done = true;

                /* Quill が確定した字を中身に入れ終わってから（composition-end の直後は、まだ入っていないことがある） */
                window.setTimeout(function () {
                    button[PASS] = true;
                    button.click();
                }, 60);

            };

            quill.once('composition-end', finish);
            window.setTimeout(finish, 600);

            /* カーソルを外す＝日本語入力が確定する */
            try {
                editorRoot.blur();
            } catch (err) {
                /* 外せなくても、上限の時間で送る */
            }

        }, true);

    })();


    /* ============================================================
       ツールバーが画面の上に貼り付いたことを、見た目で分かるようにする
       ============================================================
       テストで「微妙についてきてた」＝付いてきているのが分かりにくかった。
       貼り付いている間だけ、影を付ける（is-stuck）。見張りの1px の目印が画面の上から出たら、貼り付いている */
    (function setupStuckShadow() {

        var head = wrapper.querySelector('.article-editor-head');

        if (!head || typeof window.IntersectionObserver === 'undefined') {
            return;
        }

        var sentinel = document.createElement('div');
        sentinel.className = 'article-editor-sentinel';
        sentinel.setAttribute('aria-hidden', 'true');
        head.parentNode.insertBefore(sentinel, head);

        new IntersectionObserver(function (entries) {

            entries.forEach(function (entry) {
                head.classList.toggle('is-stuck', !entry.isIntersecting && entry.boundingClientRect.top < 0);
            });

        }, { threshold: 0 }).observe(sentinel);

    })();


    /* ============================================================
       読み上げソフト・キーボードのための手当て
       ============================================================ */

    var label = document.querySelector('label[for="' + source.id + '"]');

    /* 編集の欄に名前を付ける（項目名「本文」をそのまま使う）。
       【role="textbox" と aria-multiline】contenteditable の div は、読み上げソフトによっては
       入力欄として名乗らないので、こちらで名乗っておく */
    editorRoot.setAttribute('role', 'textbox');
    editorRoot.setAttribute('aria-multiline', 'true');

    if (label) {

        if (!label.id) {
            label.id = source.id + '_label';
        }

        editorRoot.setAttribute('aria-labelledby', label.id);

        /* 項目名を押したら、編集の欄へ入る（label の for は、隠した textarea を指したままなので） */
        label.addEventListener('click', function (e) {
            e.preventDefault();
            focusBody();
        });

    }

    /* 下の説明の1行を、編集の欄の説明として結び付ける */
    var hint = document.getElementById('articleBodyHint');

    if (hint) {
        editorRoot.setAttribute('aria-describedby', hint.id);
    }

    /* 本文の欄へカーソルを置く（HTMLモードなら、その欄へ）。見える位置までスクロールする */
    function focusBody() {

        var target = htmlMode ? htmlSource : editorRoot;

        target.scrollIntoView({ block: 'center' });

        if (htmlMode) {
            htmlSource.focus({ preventScroll: true });
        } else {
            quill.focus();
        }

    }

    /* 【画面の検査で「本文が空」と止められたとき、ここへカーソルを置く】
       Scripts/site.js の「6」は、検査に引っかかった欄の vbFocus があればそれを呼ぶ。
       本当の欄（textarea）は隠してあるので、そのままだとカーソルを置けない */
    source.vbFocus = focusBody;

    /* 【断られた理由は、Quill の欄の上に出す】本当の欄（textarea）は隠してあるので、
       理由を差し込む場所を Quill の箱にする（Scripts/site.js の「8」の data-error-anchor。サーバーで断られたときの「9」も同じ） */
    source.setAttribute('data-error-anchor', wrapper.id);


    /* ============================================================
       この画面の中で、カテゴリを作る（開発者が採用）
       ============================================================
       「＋ 新しいカテゴリ」→ その場に名前の欄 →「追加」で、サーバー（Edit.aspx.vb の WebMethod AddCategory）が作り、
       選択肢に足して、そのカテゴリを選んだ状態にする。**ページを読み込み直さない**（書きかけを失う不安を作らない）。
       決まり（同じ名前・空の名前を断る）はカテゴリの画面と同じ1本の道。理由は名前の欄の上に出す（site.js の「8」）。
       【連打】送っている間は「追加」を押せなくする（2回届いても、同じ名前はサーバーが断る）。
       【選んだら、自動保存にも乗る】選択肢を変えたあと change を投げる（ふつうに選び直したのと同じ扱い）。
       【名前は textContent で入れる】利用者が付けた名前なので、HTML として入れない */
    (function setupCategoryAdd() {

        var select = form.querySelector('select[data-autosave-field="category"]');
        var openBtn = document.getElementById('articleCategoryAddBtn');
        var box = document.getElementById('articleCategoryAdd');
        var nameInput = document.getElementById('articleCategoryNewName');
        var okBtn = document.getElementById('articleCategoryAddOk');
        var cancelBtn = document.getElementById('articleCategoryAddCancel');
        var url = box ? box.getAttribute('data-add-url') : '';

        if (!select || !openBtn || !box || !nameInput || !okBtn || !cancelBtn || !url || !window.fetch) {
            return;
        }

        openBtn.hidden = false;

        var sending = false;

        function setOpen(on) {

            box.hidden = !on;
            openBtn.setAttribute('aria-expanded', on ? 'true' : 'false');

            if (window.vbFieldError) {
                window.vbFieldError.clear(nameInput);
            }

            if (on) {
                nameInput.value = '';
                nameInput.focus();
            } else {
                openBtn.focus();
            }

        }

        function fail(message) {

            if (window.vbFieldError) {
                window.vbFieldError.show(nameInput, message);
                window.vbFieldError.focus(nameInput);
            } else {
                nameInput.focus();
            }

        }

        function add() {

            if (sending) {
                return;
            }

            var name = nameInput.value.trim();

            /* 【空はここで止める】文はサーバー（ArticleRules.ValidateCategoryName）と同じ */
            if (!name) {
                fail('カテゴリの名前を入力してください。');
                return;
            }

            sending = true;
            okBtn.disabled = true;

            window.fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json; charset=utf-8' },
                body: JSON.stringify({ name: name })
            }).then(function (res) {

                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }

                return res.json();

            }).then(function (json) {

                var reply = json && json.d;

                if (!reply || !reply.ok) {
                    fail((reply && reply.message) || 'カテゴリを追加できませんでした。少し待ってから、もう一度押してください。');
                    return;
                }

                var option = document.createElement('option');
                option.value = String(reply.categoryId);
                option.textContent = reply.name;
                select.appendChild(option);
                select.value = option.value;

                select.dispatchEvent(new Event('change', { bubbles: true }));

                box.hidden = true;
                openBtn.setAttribute('aria-expanded', 'false');

                /* 選んだ結果が読み上げで分かるように、選択肢へ戻る */
                select.focus();

            }).catch(function () {

                fail('カテゴリを追加できませんでした（通信が途切れたかもしれません）。もう一度押してください。');

            }).then(function () {

                sending = false;
                okBtn.disabled = false;

            });

        }

        openBtn.addEventListener('click', function () {
            setOpen(box.hidden);
        });

        cancelBtn.addEventListener('click', function () {
            setOpen(false);
        });

        okBtn.addEventListener('click', add);

        /* 【名前の欄で Enter】保存（ページの送信）にしない。追加にする。Esc でやめる */
        nameInput.addEventListener('keydown', function (e) {

            if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
                e.preventDefault();
                add();
            } else if (e.key === 'Escape') {
                e.preventDefault();
                setOpen(false);
            }

        });

    })();

    /* ボタンの「押されているか」を、見た目だけでなく読み上げにも伝える。
       Quill 2 は ql-active の class を付け外しするので、それを aria-pressed に写す。
       【押しっぱなしにならないボタン（data-momentary）には付けない】元に戻す・やり直す・書式を消す・区切り線、と、
       小窓を開くボタン（色。こちらは aria-expanded）。付けると「押されていません」と読まれて紛らわしい */
    function syncPressed() {

        Array.prototype.forEach.call(toolbar.querySelectorAll('button[class*="ql-"]'), function (button) {

            if (button.hasAttribute('data-momentary')) {
                button.removeAttribute('aria-pressed');
                return;
            }

            button.setAttribute('aria-pressed', button.classList.contains('ql-active') ? 'true' : 'false');

        });

    }

    quill.on('editor-change', function () {
        /* Quill がツールバーを更新し終わってから写す */
        window.setTimeout(syncPressed, 0);
    });

    syncPressed();


    /* ============================================================
       リンクの入力（Quill の snow のツールチップ）
       ============================================================
       文言（「リンク先を入力：」「決定」「外す」など）は Content/Site.css で日本語に差し替えてある
       （Quill は CSS の content で出しているため）。ここでは、キーボードと読み上げのための手当てをする */
    var tooltip = wrapper.querySelector('.ql-tooltip');

    if (tooltip) {

        var linkInput = tooltip.querySelector('input[type="text"]');

        if (linkInput) {

            linkInput.setAttribute('aria-label', 'リンク先のURL');
            linkInput.setAttribute('data-link', 'https://example.com');

            /* 【「https://」を付け忘れたURLに、付けてから渡す】
               付けないと Quill は「このサイトの中の場所」として扱い、
               /account/articles/example.com のような行き先になる。
               Quill が Enter と「決定」を受け取るより先（捕捉の段階）に直しておく */
            var fixScheme = function () {

                var value = linkInput.value.trim();

                if (value === '') {
                    return;
                }

                /* すでに種類（https: mailto: など）が付いている・サイトの中の場所を書いた */
                if (/^[a-z][a-z0-9+.\-]*:/i.test(value) || value.charAt(0) === '/' || value.charAt(0) === '#') {
                    return;
                }

                linkInput.value = 'https://' + value;

            };

            linkInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    fixScheme();
                }
            }, true);

            var action = tooltip.querySelector('a.ql-action');

            if (action) {
                action.addEventListener('click', fixScheme, true);
            }

        }

        /* 「編集」「決定」「外す」は、href の無い <a> なので、Tab で止まらない・Enter で押せない。
           ボタンとして名乗らせ、Enter と Space で押せるようにする */
        Array.prototype.forEach.call(tooltip.querySelectorAll('a.ql-action, a.ql-remove'), function (a) {

            a.setAttribute('role', 'button');
            a.setAttribute('tabindex', '0');

            a.addEventListener('keydown', function (e) {

                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    a.click();
                }

            });

        });

    }


    /* ============================================================
       中で使うもの
       ============================================================ */

    /* data-allowed の JSON を読む（読めなければ、何も許さない一覧＝HTMLモードで全部を「使えない」と知らせる側に倒す） */
    function readAllowed() {

        var empty = { tags: [], attributes: [], classes: [], styles: [], styleElements: [], schemes: [], imageAttributes: [], imageSrc: '' };

        try {

            var parsed = JSON.parse(wrapper.getAttribute('data-allowed') || '{}');

            return {
                tags: parsed.tags || [],
                attributes: parsed.attributes || [],
                classes: parsed.classes || [],
                styles: parsed.styles || [],
                styleElements: parsed.styleElements || [],
                schemes: parsed.schemes || [],
                imageAttributes: parsed.imageAttributes || [],
                imageSrc: parsed.imageSrc || '',
                imageSizeClasses: parsed.imageSizeClasses || [],
                imageNoFrameClass: parsed.imageNoFrameClass || '',
                imageDeviceClasses: parsed.imageDeviceClasses || []
            };

        } catch (e) {
            return empty;
        }

    }

    /* 色の値を #rrggbb（小文字）に直す。直せなければ null。
       **サーバーの ArticleHtml.ToHexColor と同じ決まり**（#RRGGBB・#RGB・rgb(r, g, b)・rgba(r, g, b, 1) だけ）。
       名前の色（red など）・var(…)・半透明は null＝付けない */
    function toHexColor(value) {

        if (!value) {
            return null;
        }

        var v = String(value).trim();
        var m = /^#([0-9a-f]{6})$/i.exec(v);

        if (m) {
            return '#' + m[1].toLowerCase();
        }

        m = /^#([0-9a-f])([0-9a-f])([0-9a-f])$/i.exec(v);

        if (m) {
            return ('#' + m[1] + m[1] + m[2] + m[2] + m[3] + m[3]).toLowerCase();
        }

        m = /^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(1|1\.0+)\s*)?\)$/i.exec(v);

        if (m) {

            var hex = '#';

            for (var i = 1; i <= 3; i++) {

                var n = parseInt(m[i], 10);

                if (n > 255) {
                    return null;
                }

                hex += ('0' + n.toString(16)).slice(-2);

            }

            return hex;

        }

        return null;

    }

    /* 書く欄のふつうの文字の色（CSS から読む。読めなければ #333333）。カラーピッカーを初めて開いたときの色に使う */
    function baseTextColor() {
        return toHexColor(window.getComputedStyle(editorRoot).color) || '#333333';
    }

    /* 「ql-align-center」のような class の一覧から、名前（center）だけを取り出す */
    function namesWithPrefix(prefix) {

        return ALLOWED.classes
            .filter(function (cls) { return cls.indexOf(prefix) === 0; })
            .map(function (cls) { return cls.substring(prefix.length); });

    }


    /* ============================================================
       最後に、入れ替える
       ============================================================
       【ここまで来てから textarea を隠す】途中で失敗したら、textarea のまま残る */
    source.hidden = true;
    wrapper.hidden = false;

})();

/* ============================================================
   保存したあと、押す前に見ていた場所へ戻す
   ============================================================
   指摘：まだ書いている途中なのに一覧へ戻される。保存しても一覧へ戻らなくなった（Articles/Edit.aspx.vb）が、
   **同じ画面へ「移り直す」形**（302。F5 で2回送られないため）なので、ブラウザから見れば別の読み込みで、
   画面はいちばん上から始まる。それでは「途中の場所を見失う」のが直らないので、書いていた場所へ戻す。

   【置き場所は sessionStorage】その場限りの「状態」だから
   （「ブラウザに何かを覚えさせるときの、置き場所」の表。ログアウトの全消しに巻き込まれる側）。

   【この囲みを、上の囲みの外に置いてある理由】
   上の囲みは、Quill が読み込めなかったら何もせずに戻る。**素の入力欄で書いているときも、場所は戻したい。**

   【?done=saved で開いたときだけ戻す】ふつうに開いたとき・番号を打ち替えて開いたときに動かさない。
   'saved-cleaned' も 'saved' で始まるので、同じ判定で拾える。

   【断られた保存（ポストバックで画面が戻ってくる場合）は、ここではない】
   そちらは Web Forms の MaintainScrollPositionOnPostback が戻す（Articles/Edit.aspx の @Page）。 */
(function () {

    'use strict';

    var KEY = 'vbchat_article_scroll';

    var source = document.querySelector('textarea[data-article-editor]');
    var form = source ? source.form : null;

    if (!form) {
        return;
    }

    /* 【サイト共通の「押したあと位置を飛ばさない」（Scripts/site.js の「17」）には、この画面を任せない】同じことを2回しないため */
    window.vbOwnReturnPoint = true;

    /* 【読み書きは try で囲む】端末の設定で sessionStorage が使えないことがある。
       使えなければ、この機能だけ働かない（保存そのものには関係しない） */
    function forget() {

        try {
            window.sessionStorage.removeItem(KEY);
        } catch (e) {
        }

    }

    /* 【送るときに覚える】
       画面の検査で止められた送信でも覚えてしまうが、戻すのは ?done=saved で開いたときだけなので害は無い
       （次に送ったときに上書きされる） */
    form.addEventListener('submit', function () {

        try {
            window.sessionStorage.setItem(KEY,
                String(Math.round(window.pageYOffset || document.documentElement.scrollTop || 0)));
        } catch (e) {
        }

    });

    var saved = null;

    try {
        saved = window.sessionStorage.getItem(KEY);
    } catch (e) {
        saved = null;
    }

    if (saved === null) {
        return;
    }

    /* 【読んだら必ず忘れる】残すと、次に ?done=saved で開いたときに古い場所へ飛ぶ */
    forget();

    var y = parseInt(saved, 10);

    if (!(y > 0) || !/[?&]done=saved/.test(window.location.search)) {
        return;
    }

    /* 【人が動かしたあとは、動かさない】
       読んでいる途中で画面を引っ張られるほうが、少しずれているより困る。
       自分の scrollTo でも scroll の合図は飛ぶので、**人の操作のほう**（指・ホイール・キー）を見ている */
    var touched = false;

    function markTouched() {
        touched = true;
    }

    window.addEventListener('wheel', markTouched, { passive: true });
    window.addEventListener('touchstart', markTouched, { passive: true });
    window.addEventListener('pointerdown', markTouched);
    window.addEventListener('keydown', markTouched);

    /* 1度目＝本文が入ったあとの今（この囲みは、上の囲みが Quill に本文を入れ終わってから走る） */
    window.scrollTo(0, y);

    /* 2度目＝画像や書体が遅れて届いて高さが変わったとき（load）。合っていれば何もしない */
    window.addEventListener('load', function () {

        if (touched) {
            return;
        }

        if (Math.abs((window.pageYOffset || 0) - y) > 2) {
            window.scrollTo(0, y);
        }

    });

})();

/* 【保存のボタンの言葉を、選んだ状態に合わせる囲みは、消した】
   保存の帯の「状態」を選ぶ欄をやめて、ボタン2つ（そのまま保存／状態を変える）にしたため。
   ボタンの言葉は、画面を描くときにサーバーが入れる（Articles/ArticleRules.vb の「保存の帯」の節）。
   **画面の側に、言葉の写しは無い** */
