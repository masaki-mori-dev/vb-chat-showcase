/*
    記事のページの「目次の道具」

    ============================================================
    【何を作るか】2つ
    ============================================================
      1. PC … 右の枠（更新履歴のある列）に、**ついてくる目次**（position: sticky）
      2. スマホ … 右下に浮かぶ「目次」ボタンと、下からせり上がる目次
                  ボタンは**指で動かせる**（置いた場所は localStorage が覚える）

    どちらも「今読んでいる見出し」に色が付く（IntersectionObserver）。

    ============================================================
    【中身は、出来上がった本文から作る】
    ============================================================
    目次の HTML をサーバーがもう1回組み立てる形にしなかった。
    **見出しと id を作っているのはサーバー（Articles/ArticleToc.vb）だけ**で、
    ここはその結果（h2[id] / h3[id]）を読むだけにしてある。
    作る場所を2つに増やすと、片方だけ直したときに目次と本文がずれる。

    【この台本が読み込まれるのは、目次を「入れる」記事だけ】
    （Articles/ArticleDetail.aspx.vb の TocScript.Visible）。
    読み込まれなくても、**本文の頭の目次はそのまま使える**（あちらは JS を使っていない）。

    ============================================================
    【PC とスマホの出し分けは、CSS にまかせている】
    ============================================================
    両方を作っておいて、`display: none` で片方を消す（境目は「右の枠が本文の横にいるか」。2026年10月7日に 1100px から変えた＝右の袖は @container side、丸いボタンと板は <html> の vb-side-below。Content/Site.css）。
    JS で matchMedia を見て作り分けると、幅をまたいだときの作り直しが要る——
    こっそりの窓で踏んだ「狭い⇔広いをまたいだら、置き場所を決め直すこと」と同じ形の罠になる。
    `display: none` なら、キーボードの Tab の順番からも外れる。
*/

(function () {
    'use strict';

    var LABEL = '目次';

    /* ============================================================
       狭い画面：浮かぶボタンと、下からせり上がる目次（共通の部品）
       ============================================================
       【、部品にした】記事を書く画面（Scripts/article-editor.js）も、狭い画面では
       これと同じボタンと板で目次を出す（基本ルール P＝広い画面だけの部品を狭い画面で消すなら、代わりの出し方を用意する）。
       **同じ見た目・同じ動き（指で動かす・外を押すと閉じる・Esc）を2か所に書かないため**、ここで作る1つを両方が使う。

       opts.list     … 板に入れる目次の一覧（<ol class="article-toc-list">。中の .article-toc-link を押すと板が閉じる）
       opts.id       … 板の id（既定 articleTocSheet）
       opts.modifier … ボタンと板に足す class の後ろ（'editor' → article-toc-fab--editor ／ article-toc-sheet--editor）
       opts.storeKey … 動かした場所を覚える localStorage の名前（記事のページと書く画面で分ける＝既定の場所が違うため）
       戻り値        … { fab, sheet, setList(新しい一覧), close() } */
    function createTocSheet(opts) {

        opts = opts || {};

        var sheetId = opts.id || 'articleTocSheet';

        var fab = document.createElement('button');
        fab.type = 'button';
        fab.className = 'article-toc-fab';
        fab.textContent = LABEL;
        fab.setAttribute('aria-expanded', 'false');
        fab.setAttribute('aria-controls', sheetId);

        var sheet = document.createElement('nav');
        sheet.className = 'article-toc-sheet';
        sheet.id = sheetId;
        sheet.setAttribute('aria-label', LABEL);
        /* 【閉じている間は、読み上げソフトからも消す】画面から外しているだけだと、Tab で中へ入れてしまう */
        sheet.setAttribute('aria-hidden', 'true');
        /* 【閉じている間は、Tab でも入れない】aria-hidden だけでは、中の項目に Tab で焦点が入る */
        sheet.inert = true;

        var head = document.createElement('div');
        head.className = 'article-toc-sheet-head';

        var sheetHeading = document.createElement('h2');
        sheetHeading.className = 'article-toc-heading';
        sheetHeading.textContent = LABEL;

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'article-toc-sheet-close';
        closeBtn.setAttribute('aria-label', '目次を閉じる');
        closeBtn.textContent = '×';

        head.appendChild(sheetHeading);
        head.appendChild(closeBtn);

        if (opts.modifier) {
            fab.classList.add('article-toc-fab--' + opts.modifier);
            sheet.classList.add('article-toc-sheet--' + opts.modifier);
        }

        sheet.appendChild(head);

        /* 【中の一覧は、あとから差し替えられる】書く画面は、見出しを打ち直すたびに作り直す */
        var currentList = null;

        function setList(list) {
            if (currentList && currentList.parentNode === sheet) {
                sheet.removeChild(currentList);
            }
            currentList = list || null;
            if (currentList) {
                sheet.appendChild(currentList);
            }
        }

        setList(opts.list);

        document.body.appendChild(fab);
        document.body.appendChild(sheet);

        /* === 開け閉め === */
        function openSheet() {
            sheet.classList.add('is-open');
            sheet.setAttribute('aria-hidden', 'false');
            sheet.inert = false;
            fab.setAttribute('aria-expanded', 'true');

            /* 【開いたら、中へ焦点を移す】キーボード・読み上げソフトで使えるように */
            var first = sheet.querySelector('.article-toc-link');
            if (first) {
                first.focus();
            }
        }

        function closeSheet(focusBack) {
            sheet.classList.remove('is-open');
            sheet.setAttribute('aria-hidden', 'true');
            sheet.inert = true;
            fab.setAttribute('aria-expanded', 'false');

            /* 【閉じたら、押したボタンへ焦点を戻す】サイトのパネルと同じ決まり（居場所を見失わせない） */
            if (focusBack) {
                fab.focus();
            }
        }

        function isOpen() {
            return sheet.classList.contains('is-open');
        }

        closeBtn.addEventListener('click', function () {
            closeSheet(true);
        });

        /* 項目を押したら、その見出しへ飛んで、目次は引っ込む（開発者の指定）。
           一覧は差し替わるので、項目ごとではなく板で受ける（項目の飛ぶ処理が先に走り、そのあとここで閉じる） */
        sheet.addEventListener('click', function (e) {
            if (e.target.closest && e.target.closest('.article-toc-link')) {
                closeSheet(false);
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen()) {
                closeSheet(true);
            }
        });

        /* 外を押したら閉じる（サイトのパネルと同じ閉じ方） */
        document.addEventListener('pointerdown', function (e) {
            if (isOpen() && !sheet.contains(e.target) && e.target !== fab) {
                closeSheet(false);
            }
        });

        /* ============================================================
           ボタンを指で動かす
           ============================================================
           【押したのか、動かしたのかを、動いた量で分ける】
           少しでも動いたら押したことにしない、にすると、指では押せない（必ず数px動く）。
           逆に動きを見ないと、動かしたつもりで目次が開く。**5px を境目**にしてある。

           【touch-action: none が要る】（CSS 側）
           これが無いと、押したまま動かした瞬間にブラウザがページをスクロールして、
           ボタンには pointermove が届かなくなる。

           【必ず画面の中へ丸めてから置く】（こっそりの窓と同じ罠）
           はみ出すと掴めなくなり、押すことも動かすこともできなくなる */
        var STORE_KEY = opts.storeKey || 'vbchat_article_toc_fab';
        var MOVE_THRESHOLD = 5;

        var drag = null;

        function clamp(value, min, max) {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        /* 【置き場所は「右下からどれだけ離れているか」で持つ】
           ============================================================
           決定：**まずはウィンドウの右下に出す。幅を大きく変えたりして
           消えない限りは、動かした位置を覚えておく程度でよい**。

           前は「左上から何px」で持っていた。そうすると、**すごく狭い窓で置いた位置を、
           広い窓に戻したときにそのまま使う**ので、画面の真ん中あたりに出ていた
           （指摘：大きく狭めてから広げて消えたあと、もう一度広げると真ん中に出る）。

           右下からの距離で持つと、**窓の大きさが変わっても、右下との位置関係が保たれる**。
           CSS の既定（右下から16px）と、同じものさしになる。

           【今どこに居るかは、この変数が持つ】null なら「まだ動かしていない＝CSS の既定のまま」 */
        var placed = null;

        /* 出ているか（右の枠が本文の横にいる間は display: none）。
           【offsetParent で見てはいけない】position: fixed なので、見えていても null になる */
        function isShown() {
            var rect = fab.getBoundingClientRect();
            return !(rect.width === 0 && rect.height === 0);
        }

        /* 右下からの距離を、そのまま当てる（画面の中へ丸めてから） */
        function applyPlace(pos) {

            var w = fab.offsetWidth || 52;
            var h = fab.offsetHeight || 52;
            var margin = 8;

            var right = clamp(pos.right, margin, Math.max(margin, window.innerWidth - w - margin));
            var bottom = clamp(pos.bottom, margin, Math.max(margin, window.innerHeight - h - margin));

            /* 【left / top は auto に戻す】CSS は left/top が auto のとき right/bottom で置く */
            fab.style.setProperty('--toc-fab-x', 'auto');
            fab.style.setProperty('--toc-fab-y', 'auto');
            fab.style.setProperty('--toc-fab-right', right + 'px');
            fab.style.setProperty('--toc-fab-bottom', bottom + 'px');

            placed = { right: right, bottom: bottom };

            return placed;
        }

        /* 動かしている間に使う（指の位置＝左上の座標が来る）。
           ここで「右下からの距離」に直してから当てる */
        function placeAt(x, y) {

            var w = fab.offsetWidth || 52;
            var h = fab.offsetHeight || 52;

            return applyPlace({
                right: window.innerWidth - (x + w),
                bottom: window.innerHeight - (y + h)
            });
        }

        function remember(pos) {
            /* 【使えないことがある】端末の設定で localStorage が触れないと例外になる。
               そのときは「覚えない」だけで、動かすことはできる */
            try {
                window.localStorage.setItem(STORE_KEY, JSON.stringify(pos));
            } catch (err) {
                /* 覚えられなくても、何も止めない */
            }
        }

        /* 【覚えた場所を捨てて、CSS の既定（右下）に戻す】
           ボタンが消える幅になったときに呼ぶ。次に出るときは、いつも右下から始まる */
        function forget() {

            placed = null;

            fab.style.removeProperty('--toc-fab-x');
            fab.style.removeProperty('--toc-fab-y');
            fab.style.removeProperty('--toc-fab-right');
            fab.style.removeProperty('--toc-fab-bottom');

            try {
                window.localStorage.removeItem(STORE_KEY);
            } catch (err) {
                /* 消せなくても、何も止めない */
            }
        }

        function restore() {

            var raw = null;

            try {
                raw = window.localStorage.getItem(STORE_KEY);
            } catch (err) {
                return;
            }

            if (!raw) {
                return;
            }

            try {

                var pos = JSON.parse(raw);

                if (pos && typeof pos.right === 'number' && typeof pos.bottom === 'number') {
                    applyPlace(pos);
                    return;
                }

                /* 【古い形（左上からの距離）が残っていたら、捨てる】
                   より前に動かした人の localStorage に入っている。
                   読み替えずに捨てるのは、そのまま当てると**この回で直した不具合が再現する**ため */
                forget();

            } catch (err) {
                /* 壊れていたら、既定の場所のまま */
                forget();
            }
        }

        fab.addEventListener('pointerdown', function (e) {

            var rect = fab.getBoundingClientRect();

            drag = {
                id: e.pointerId,
                dx: e.clientX - rect.left,
                dy: e.clientY - rect.top,
                moved: false
            };

            fab.setPointerCapture(e.pointerId);
        });

        fab.addEventListener('pointermove', function (e) {

            if (!drag || drag.id !== e.pointerId) {
                return;
            }

            var x = e.clientX - drag.dx;
            var y = e.clientY - drag.dy;

            if (!drag.moved) {

                var rect = fab.getBoundingClientRect();

                if (Math.abs(x - rect.left) < MOVE_THRESHOLD && Math.abs(y - rect.top) < MOVE_THRESHOLD) {
                    return;
                }

                drag.moved = true;
            }

            drag.last = placeAt(x, y);
        });

        function endDrag(e) {

            if (!drag || drag.id !== e.pointerId) {
                return;
            }

            var moved = drag.moved;
            var last = drag.last;

            drag = null;

            try {
                fab.releasePointerCapture(e.pointerId);
            } catch (err) {
                /* 既に外れていることがある */
            }

            if (moved) {
                if (last) {
                    remember(last);
                }
                return;
            }

            /* 動いていなければ、押したということ */
            if (isOpen()) {
                closeSheet(false);
            } else {
                openSheet();
            }
        }

        fab.addEventListener('pointerup', endDrag);
        fab.addEventListener('pointercancel', endDrag);

        /* 【画面の大きさが変わったとき】（この形にした）

             ・**ボタンが消える幅になったら、覚えた場所を捨てる**（localStorage からも）。
               次に出るときは、いつも窓の右下から始まる
             ・出ている間は、**右下からの距離を保ったまま**、画面の中へ丸め直す
               （横に倒した・キーボードが出た、で画面の外に出ると掴めなくなるため）
             ・まだ動かしていない（placed が null）なら、何もしない
               ——CSS の既定（右下から16px）が、そのまま効いている

           【なぜ「捨てる」のか】ボタンが消えている間の窓の大きさは、
           置き場所の手がかりにならない。持ち越すと、次に出たときに変な場所に出る。
           **スマホでは消えることがない**ので、動かした場所はそのまま覚えている */
        window.addEventListener('resize', function () {

            if (!isShown()) {
                forget();
                return;
            }

            if (placed) {
                applyPlace(placed);
            }

        });

        /* 【開いた時点で出ていないなら、覚えた場所は捨てる】
           PC で開いた人の localStorage に、前に触った位置が残っていても持ち越さない */
        if (isShown()) {
            restore();
        } else {
            forget();
        }

        /* ============================================================
           下へ読み進めている間は、ボタンを薄くする
           ============================================================
           理由：読みたい所の邪魔になりうるため。
           止まったとき・上へ戻ったときは、元の濃さに戻す */
        var lastY = window.pageYOffset;
        var dimTimer = null;

        window.addEventListener('scroll', function () {

            var y = window.pageYOffset;

            if (y > lastY + 4) {
                fab.classList.add('is-dim');
            } else if (y < lastY - 4) {
                fab.classList.remove('is-dim');
            }

            lastY = y;

            window.clearTimeout(dimTimer);
            dimTimer = window.setTimeout(function () {
                fab.classList.remove('is-dim');
            }, 700);

        }, { passive: true });


        return {
            fab: fab,
            sheet: sheet,
            setList: setList,
            close: function () {
                if (isOpen()) {
                    closeSheet(false);
                }
            }
        };

    }

    window.VbTocSheet = { create: createTocSheet };


    /* ============================================================
       ここから下は、記事のページ（Articles/ArticleDetail.aspx）だけ
       ============================================================ */
    var body = document.querySelector('.article-body');

    /* 【目次を入れない記事では、何もしない】本文の頭に目次が無ければ、それが合図 */
    if (!body || !body.querySelector('.article-toc')) {
        return;
    }

    var headings = [];
    var nodes = body.querySelectorAll('h2[id], h3[id]');
    var i;

    for (i = 0; i < nodes.length; i++) {
        headings.push(nodes[i]);
    }

    if (headings.length === 0) {
        return;
    }

    /* 【番号の印を <html> へ写す】
       見出しの番号は CSS のカウンターで出していて、印（article-numbered）は本文の箱に付いている。
       右の枠・スマホの小窓は本文の外にあるので、そのままでは番号が出ない。
       <html> に写せば、Content/Site.css の同じ規則が3つ全部に当たる */
    if (body.classList.contains('article-numbered')) {
        document.documentElement.classList.add('article-numbered');
    }

    /* ============================================================
       目次の一覧を1つ作る
       ============================================================
       本文の頭の目次（サーバーが作ったもの）と、同じ class・同じ並びにする。
       **番号は CSS が出す**ので、ここでは文字だけを入れる */
    function buildList() {

        var list = document.createElement('ol');
        list.className = 'article-toc-list';

        headings.forEach(function (heading) {

            var item = document.createElement('li');
            var isSub = (heading.tagName.toLowerCase() === 'h3');

            item.className = 'article-toc-item'
                + (isSub ? ' article-toc-item--sub' : '')
                /* H2 より前の H3（サーバーが印を付けている）は、目次でも番号を出さない */
                + (heading.classList.contains('article-heading-nonum') ? ' article-toc-item--nonum' : '');

            var link = document.createElement('a');
            link.className = 'article-toc-link';
            link.href = '#' + heading.id;
            link.textContent = (heading.textContent || '').replace(/\s+/g, ' ').trim();

            item.appendChild(link);
            list.appendChild(item);

        });

        return list;
    }

    /* ============================================================
       PC：右の枠の「ついてくる目次」（作り直した）
       ============================================================
       【作り直した理由】実機を見た 3 つ。
         ①本文に大きな目次が出ているのに、右にも目次が出るのは不自然
            → **本文の頭の目次が見えている間は、出さない**
         ②いちばん上に固定されすぎていて、更新履歴や右の枠のほかの項目の
            邪魔になる
            → **右の枠のいちばん下**（広告の枠・更新履歴の後ろ）に入れる
         ③広い場所があるのに、スマホとまったく同じ形では工夫が無い
            → 畳める・ついて来る・今読んでいる見出しが太くなる

       【出し入れは display: none でやる】隠れている間は、読み上げソフトからも
       Tab の順番からも消える＝**目次が2つ読まれない**（開発者の指定）。
       `visibility` や画面外への移動だと、読み上げソフトには残ってしまう。

       【ちらつきと、ほかの部品のずれ】
       ・**いちばん下に入れてあるので、出ても引っ込んでも、上の広告の枠と更新履歴は1pxも動かない**
       ・本文の頭の目次は、ふつうに読んでいれば画面を1回通り過ぎるだけなので、
         出入りも1回ずつ。境目でわずかに往復しないよう、**下へ 24px の余裕（rootMargin）**を付けてある */
    var sideLinks = [];
    var sidebar = document.querySelector('.right-sidebar');
    var inlineToc = body.querySelector('.article-toc');
    var side = null;
    var SIDE_OPEN_KEY = 'vbchat_article_toc_side';

    if (sidebar) {

        side = document.createElement('nav');
        side.className = 'article-toc-side';
        side.setAttribute('aria-label', LABEL);

        /* 【本文の頭の目次と、まったく同じ作りにする】<details> なら、畳む仕掛けを
           自分で書かなくても、キーボードでも読み上げソフトでも正しく開け閉めできる */
        var sideDetails = document.createElement('details');
        sideDetails.className = 'article-toc-details';
        sideDetails.open = true;

        var sideSummary = document.createElement('summary');
        sideSummary.className = 'article-toc-summary';
        sideSummary.textContent = LABEL;

        sideDetails.appendChild(sideSummary);
        sideDetails.appendChild(buildList());
        side.appendChild(sideDetails);

        /* 【いちばん下に入れる】広告の枠（.banner-area）と更新履歴の後ろ。
           右の枠は本文と同じ高さまで伸びる箱なので、**中身は上から順に流れる**＝
           ここへ足しても「ページのいちばん下」へは行かず、更新履歴のすぐ下に出る */
        sidebar.appendChild(side);

        sideLinks = side.querySelectorAll('.article-toc-link');

        /* 畳んだかどうかは、その端末が覚えておく（サーバーは知らなくてよい＝Cookie にしない。
           丸いボタンの置き場所と同じ決まり） */
        try {
            if (window.localStorage.getItem(SIDE_OPEN_KEY) === '0') {
                sideDetails.open = false;
            }
        } catch (err) {
            /* 読めなくても、開いたまま使える */
        }

        sideDetails.addEventListener('toggle', function () {
            try {
                window.localStorage.setItem(SIDE_OPEN_KEY, sideDetails.open ? '1' : '0');
            } catch (err) {
                /* 覚えられなくても、何も止めない */
            }
        });

        /* === 本文の頭の目次が見えなくなったら出す／また見えたら引っ込める === */
        if (inlineToc && window.IntersectionObserver) {

            new window.IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    side.classList.toggle('is-shown', !entry.isIntersecting);
                });
            }, {
                /* 下へ 24px ぶん見送ってから出す（境目での往復を減らすため） */
                rootMargin: '0px 0px 24px 0px',
                threshold: 0
            }).observe(inlineToc);

        } else if (inlineToc) {
            /* 【IntersectionObserver が無い古いブラウザ】出しっぱなしにする。
               「2つ出る」ほうが「使えない」より軽い壊れ方なので、そちらへ倒す */
            side.classList.add('is-shown');
        }
    }

    /* 狭い画面：浮かぶボタンと、下からせり上がる目次（上の共通の部品） */
    var tocSheet = createTocSheet({ list: buildList() });
    var sheetLinks = tocSheet.sheet.querySelectorAll('.article-toc-link');

    /* ============================================================
       今読んでいる見出しに色を付ける
       ============================================================
       【IntersectionObserver で見る】スクロールのたびに全部の見出しの位置を測る形にすると、
       長い記事で指が重くなる（開発者の指定でもある）。

       【いちばん上に見えている見出しを「今」とする】画面に2つ以上入ることがあるため。
       **古いブラウザで IntersectionObserver が無いときは、色を付けないだけ**（目次は使える） */
    if (!window.IntersectionObserver) {
        return;
    }

    var visible = [];

    function markCurrent(id) {

        function apply(links) {
            for (var k = 0; k < links.length; k++) {
                var on = (links[k].getAttribute('href') === '#' + id);
                links[k].classList.toggle('is-current', on);
                if (on) {
                    links[k].setAttribute('aria-current', 'true');
                } else {
                    links[k].removeAttribute('aria-current');
                }
            }
        }

        apply(sideLinks);
        apply(sheetLinks);
    }

    var observer = new window.IntersectionObserver(function (entries) {

        entries.forEach(function (entry) {

            var id = entry.target.id;
            var at = visible.indexOf(id);

            if (entry.isIntersecting) {
                if (at < 0) { visible.push(id); }
            } else if (at >= 0) {
                visible.splice(at, 1);
            }

        });

        if (visible.length === 0) {
            return;
        }

        /* 本文に出てくる順に並べ直して、いちばん上のものを「今」とする */
        var order = [];

        headings.forEach(function (heading) {
            if (visible.indexOf(heading.id) >= 0) {
                order.push(heading.id);
            }
        });

        if (order.length > 0) {
            markCurrent(order[0]);
        }

    }, {
        /* 【画面の上のほうに入った時点で「今」にする】下端に出ただけで切り替わると、先の見出しが選ばれる */
        rootMargin: '0px 0px -70% 0px',
        threshold: 0
    });

    headings.forEach(function (heading) {
        observer.observe(heading);
    });

}());
