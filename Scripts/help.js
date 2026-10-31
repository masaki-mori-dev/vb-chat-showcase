/*
 * 「?」の説明の小窓（Scripts/site.js の「4」から、この1本へ切り出した）
 *
 * 【読むページ】Site.Master（サイト全体）と Chat/ChatRoom.aspx（部屋）の両方。見た目は Content/help.css。
 *   それまで部屋は site.js・Site.css を読まないので「?」が置けなかった（置くなら写す、と書いてあった）。
 *   写すのをやめ、部屋もこの1本を読む（毎回の基本の8＝共通の仕組みは1か所）。
 *
 * 【文は Core/HelpTexts.vb の表の1か所】
 *   markup には <%= HelpButton("鍵") %> の1行だけを書く（ページの土台＝ChatPageBase・AccountPageBase と RoomList.ascx に、呼ぶ1行がある）。
 *   この JS は文章を1つも持たない（前からの決まり）。サーバーが組み立てる形は、下の見本と同じ。
 *
 *       <span class="help">
 *           <button type="button" class="help-btn" data-help-target="helpXxx"
 *                   aria-label="〇〇の説明" aria-expanded="false" aria-controls="helpXxx">?</button>
 *           <div class="help-popover" id="helpXxx" hidden="hidden">
 *               <p class="help-popover-title">〇〇とは</p>
 *               <p>1行目。</p>
 *               <p class="help-popover-fig"><span class="help-fig" aria-hidden="true">（本物の部品を小さく）</span><span>その意味の文。</span></p>
 *               <p class="help-popover-more"><a href="/articles/…#article-heading-N" target="_blank" rel="noopener">さらに詳しく</a></p>
 *           </div>
 *       </span>
 *
 *   ・data-help-target と id と aria-controls は同じ名前（HelpTexts が書く）
 *   ・小窓に role="tooltip" を書かない（ボタンで開け閉めし、中にリンクも置けるディスクロージャ。aria-expanded が役を果たす。
 *     名乗りと中身が食い違うと、読み上げソフトによっては小窓の文を本文として読まない）
 *   ・【絵（.help-fig）】画面で使っている本物の部品（鍵の印・「管理人」の札・［ノック］など）を、同じ class で小さく出す。
 *     読み上げでは絵を飛ばし（aria-hidden）、意味は隣の文で伝える。**絵を押しても何も起きない**（下の「絵の中の押下は、外へ流さない」）
 *
 * 【あとから足された「?」でも動く】押す・乗せる・焦点が外れる、を document で受ける（ボタンごとに登録しない）。
 *   部屋の中で SignalR があとから描く部分に置いても、何もしなくてよい。
 *
 * 【開け閉め】PC（hover: hover）はマウスを乗せる／どの環境でも押す（Enter・Space も）。閉じ方＝外れる・外を押す・Esc（焦点はボタンへ）。
 *   同時に開くのは1つだけ。**Esc は「?」の小窓を先に閉じ、ほかの Esc（部屋のパネルを閉じる、など）へは流さない**
 *
 * 【なぜ vbChatConfirm を土台にしなかったか】あれは下を操作できなくする確認の窓。説明は読みながら操作を続けられるべきもので、別の部品にしてある。
 */
(function () {

    /* 画面の端との最小のすき間（小窓がはみ出さないように寄せる） */
    var EDGE_MARGIN = 8;

    /* ボタンと小窓の間のすき間（px）。
       【0 でないこと】ここに隙間があると、マウスが小窓へ移る途中で「外へ出た」ことになり、
       せっかく開いた小窓が閉じる。**見た目のすき間は、小窓の側の padding で作ってある** */
    var GAP = 0;

    /* 今開いている小窓（同時に開くのは1つだけ） */
    var openEntry = null;

    /* マウスで開ける環境か（PC）。タッチだけの端末では、hover は使わない
       （タッチ端末の「hover」は押したときに1回だけ起きるので、開いた直後に閉じるような動きになる） */
    function canHover() {
        return window.matchMedia && window.matchMedia('(hover: hover)').matches;
    }

    /* 小窓を、ボタンのすぐ下に置く（画面からはみ出すときは、内側へ寄せる）

       【position: fixed にしてある理由】
       ラウンジのプロフィールの枠は `overflow: hidden` の箱（.current-profile-clip）の中にある。
       absolute だと、その箱に切り落とされて小窓が出ない。
       fixed の基準は画面そのものなので、`overflow: hidden` では切られない
       （**祖先に transform / filter / will-change があると、そちらが基準になって切られる**。
         「position: fixed の基準」の落とし穴。置く場所を増やすときは確かめること） */
    function place(entry) {

        var pop = entry.popover;
        var rect = entry.button.getBoundingClientRect();

        /* いったん左上に置いて、実際の大きさを測る */
        pop.style.left = '0px';
        pop.style.top = '0px';

        var width = pop.offsetWidth;
        var height = pop.offsetHeight;

        var left = rect.left;
        var maxLeft = window.innerWidth - width - EDGE_MARGIN;

        if (left > maxLeft) {
            left = maxLeft;
        }
        if (left < EDGE_MARGIN) {
            left = EDGE_MARGIN;
        }

        /* ふつうは下。下に入らなければ上へ出す */
        var top = rect.bottom + GAP;

        if (top + height > window.innerHeight - EDGE_MARGIN && rect.top - GAP - height > EDGE_MARGIN) {
            top = rect.top - GAP - height;
        }

        pop.style.left = Math.round(left) + 'px';
        pop.style.top = Math.round(top) + 'px';
    }

    function isOpen(entry) {
        return !!openEntry && !!entry && openEntry.button === entry.button;
    }

    function open(entry) {

        if (openEntry && openEntry.button !== entry.button) {
            close(openEntry);
        }

        entry.popover.hidden = false;
        entry.button.setAttribute('aria-expanded', 'true');
        openEntry = entry;

        place(entry);
    }

    /* 【マウスを乗せて開いた小窓は、押すと「留める」】前は、乗せて開いた直後に押すと閉じていた
       （乗せる→開く、押す→開いていたので閉じる）。押した人は「開きたい」ので、留めて開いたままにする。
       留めた小窓は、マウスが外れても閉じない（外を押す・Esc・もう一度押す、で閉じる） */
    function pin(entry) {
        entry.byHover = false;
    }

    function close(entry, moveFocus) {

        if (!entry || !isOpen(entry)) {
            return;
        }

        entry.popover.hidden = true;
        entry.button.setAttribute('aria-expanded', 'false');
        openEntry = null;

        if (moveFocus) {
            entry.button.focus();
        }
    }

    /* ボタン → その小窓（ボタンの data-help-target の id）。見つからなければ null */
    function entryOf(button) {

        if (!button) {
            return null;
        }

        var id = button.getAttribute('data-help-target');
        var popover = id ? document.getElementById(id) : null;

        if (!popover) {
            return null;
        }

        return { button: button, popover: popover, wrap: button.parentNode };
    }

    /* ある要素が入っている「?」の箱（.help）の、ボタンの entry */
    function entryInside(node) {

        var wrap = node && node.closest ? node.closest('.help') : null;

        if (!wrap) {
            return null;
        }

        return entryOf(wrap.querySelector('.help-btn[data-help-target]'));
    }

    /* ------------------------------------------------------------
       【入りきらないときだけ「?」】（記事を書く画面から）
       ------------------------------------------------------------
       説明は、**入力欄の横に置けて、2行に収まるときだけ**全文を見せる。それ以外（下へ回る・2行に入りきらない）は
       文を隠して「?」だけを出す（の決め直し＝画面の高さを低くするため。下には出さない）。
       「?」の中には、同じ文の全文が入る。**置くだけで、どの画面でも使える**：

           <div class="fit-hint"><span class="fit-hint-text" id="xxxHint">説明の文。</span></div>

       ・**文を書くのは .fit-hint-text の1か所だけ**。「?」の中の文は、ここで同じ文から写す（同じ文を2か所に書かない）
       ・欄の aria-describedby は .fit-hint-text の id を指す。見た目が2行で切れても、要素には全文が入っているので、
         読み上げには全文が届く（切っているのは CSS の line-clamp だけ）
       ・「?」のボタンと小窓は、上の部品（.help）をそのまま作って足すので、開け閉め・Esc・外を押す、は上と同じ
       ・ボタンの名前（aria-label）は「（項目名）の説明」。項目名は、同じ行（.form-row）の label から取る
       ・切れたかどうかは、**大きさが変わるたびに測り直す**（ResizeObserver。無いブラウザは、画面の大きさが変わったとき）。
         測るときは「?」をいったん隠してから測る（出したまま測ると、「?」のぶん狭くなって、いつまでも切れて見える）
       ・置き場所の決め方（横か下か）は CSS（.fit-hint の flex-basis）。JS は「切れたか」しか見ない
       ・畳んだ「?」は欄の横から落とさない（.form-row-main に has-collapsed-hint を付けて折り返させない）。
         横いっぱいの欄（.form-row--body）では、.fit-hint を項目名のすぐ後ろに置く＝項目名の横に1行、入らなければ項目名の横に「?」
         （Content/Site.css。決定：欄の下に説明や「?」を落とす形は、やめる）
       ・ここまでは「何度も使う画面」の決まり。たまにしか使わない画面は .fit-hint--show（畳まずに全文）を足す */
    function setupFitHints() {

        var hints = Array.prototype.slice.call(document.querySelectorAll('.fit-hint'));

        hints.forEach(function (box, index) {

            var text = box.querySelector('.fit-hint-text');

            if (!text || box.querySelector('.help')) {
                return;
            }

            /* 【畳まない説明】（基本ルールB）たまにしか使わない画面
               （プロフィール・設定・ブログの設定など）は、高さより意味がすぐ分かるほうを取る。
               .fit-hint--show は「?」を作らず、入らなければ次の行へ回して全文を見せる（並びは CSS） */
            if (box.classList.contains('fit-hint--show')) {
                return;
            }

            var baseId = text.id || ('fitHint' + index);
            var popId = baseId + 'Help';

            var row = box.closest('.form-row');
            var main = box.parentElement && box.parentElement.classList.contains('form-row-main') ? box.parentElement : null;
            var label = row ? row.querySelector('label') : null;
            var name = label ? label.textContent.replace(/\s+/g, ' ').trim() : '';

            var wrap = document.createElement('span');
            wrap.className = 'help';
            wrap.hidden = true;

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'help-btn';
            button.setAttribute('data-help-target', popId);
            button.setAttribute('aria-label', name ? name + 'の説明' : '説明');
            button.setAttribute('aria-expanded', 'false');
            button.setAttribute('aria-controls', popId);
            button.textContent = '?';

            var pop = document.createElement('div');
            pop.className = 'help-popover';
            pop.id = popId;
            pop.hidden = true;

            var p = document.createElement('p');
            p.textContent = text.textContent;
            pop.appendChild(p);

            wrap.appendChild(button);
            wrap.appendChild(pop);
            box.appendChild(wrap);

            /* 【横に並んでいるか】すぐ前の部品（入力欄）と、上下で重なっていれば横。次の行へ回っていれば下。
               前に部品が無い置き方（説明だけの行）は、横に並んでいるのと同じに扱う */
            function isBeside() {

                var prev = box.previousElementSibling;

                while (prev && prev.offsetParent === null && prev.getClientRects().length === 0) {
                    prev = prev.previousElementSibling;
                }

                if (!prev) {
                    return true;
                }

                return box.getBoundingClientRect().top < prev.getBoundingClientRect().bottom - 1;
            }

            function measure() {

                var wasOpen = !wrap.hidden && !pop.hidden;

                /* 測るときは、全文を出した形に戻してから（畳んだまま測ると、いつまでも畳んだままに見える） */
                box.classList.remove('is-collapsed');
                if (main) {
                    main.classList.remove('has-collapsed-hint');
                }
                wrap.hidden = true;

                var cut = text.scrollHeight > text.clientHeight + 1;

                /* 【の決め直し】横に置けて2行に収まるときだけ全文。それ以外は「?」だけ（下には出さない）。
                   目的は画面の高さを低くすること（指摘：全体の高さを低くするための形なのに、それでは意味が無い）。
                   畳んでも文は要素に残る（見た目だけ隠す）ので、欄の aria-describedby は今どおり全文を読む */
                var collapse = cut || !isBeside();

                box.classList.toggle('is-collapsed', collapse);
                wrap.hidden = !collapse;

                /* 【畳んだ「?」は欄の横から落とさない】入力欄と同じ箱（.form-row-main）なら、
                   その箱を折り返さない形にする（欄のほうが「?」のぶん縮む。Content/Site.css の .has-collapsed-hint） */
                if (main) {
                    main.classList.toggle('has-collapsed-hint', collapse);
                }

                /* 「?」が引っ込むときに小窓が開いていたら、閉じた形にそろえる */
                if (!collapse && wasOpen) {
                    pop.hidden = true;
                    button.setAttribute('aria-expanded', 'false');
                    if (openEntry && openEntry.button === button) {
                        openEntry = null;
                    }
                }
            }

            measure();

            if (window.ResizeObserver) {

                var pending = false;

                new ResizeObserver(function () {
                    if (pending) {
                        return;
                    }
                    pending = true;
                    window.requestAnimationFrame(function () {
                        pending = false;
                        measure();
                    });
                }).observe(box);

            } else {
                window.addEventListener('resize', measure);
            }

            /* 書体が遅れて届くと、行の数が変わる */
            window.addEventListener('load', measure);
        });
    }

    /* ------------------------------------------------------------
       document で受ける（あとから足された「?」も、そのまま動く）
       ------------------------------------------------------------ */

    /* 押す（マウス・タッチ・キーボードの Enter / Space、どれもここへ来る）／外を押したら閉じる */
    document.addEventListener('click', function (e) {

        var button = e.target.closest ? e.target.closest('.help-btn[data-help-target]') : null;

        if (button) {

            e.preventDefault();

            var entry = entryOf(button);

            if (!entry) {
                return;
            }

            if (isOpen(entry)) {
                if (openEntry.byHover) {
                    pin(openEntry);
                } else {
                    close(openEntry);
                }
            } else {
                open(entry);
            }

            return;
        }

        if (!openEntry) {
            return;
        }

        /* 小窓の中は「外」ではない */
        if (openEntry.popover.contains(e.target)) {
            return;
        }

        close(openEntry);
    });

    /* 【絵の中の押下は、外へ流さない】絵は本物の部品の class を使っている（.btn-enter など）。
       ページの「その class を押したら入室・ノック」の処理（document で受けている。Scripts/lounge-enter.js）まで届くと、
       絵を押しただけで動いてしまう。捕まえる段（window）で止める */
    window.addEventListener('click', function (e) {
        if (e.target.closest && e.target.closest('.help-fig')) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);

    /* マウスを乗せたら開き、外れたら閉じる（PCだけ）

       【閉じない理由＝小窓は .help の中の子である】マウスが小窓の上へ移っても、まだ .help の中にいる
       （position: fixed で見た目は外に出ていても、DOM の親子は変わらない）。
       だから文を選んだり、リンクを押したりできる。mouseover / mouseout は「どこから・どこへ」で判断する */
    document.addEventListener('mouseover', function (e) {

        if (!canHover()) {
            return;
        }

        var entry = entryInside(e.target);

        if (!entry || isOpen(entry)) {
            return;
        }

        var from = e.relatedTarget;

        if (from && entry.wrap.contains(from)) {
            return;
        }

        entry.byHover = true;
        open(entry);
    });

    document.addEventListener('mouseout', function (e) {

        if (!canHover() || !openEntry || !openEntry.byHover) {
            return;
        }

        var wrap = openEntry.wrap;

        if (!wrap.contains(e.target)) {
            return;
        }

        var to = e.relatedTarget;

        if (to && wrap.contains(to)) {
            return;
        }

        /* キーボードで開いた人の小窓を、たまたま通りかかったマウスで閉じない */
        var active = document.activeElement;

        if (active === openEntry.button || (active && openEntry.popover.contains(active))) {
            return;
        }

        close(openEntry);
    });

    /* 焦点が小窓の外へ出たら閉じる（Tab で先へ進んだとき） */
    document.addEventListener('focusout', function () {
        window.setTimeout(function () {

            if (!openEntry) {
                return;
            }

            var active = document.activeElement;

            if (active && active !== document.body && active !== openEntry.button && !openEntry.popover.contains(active)) {
                close(openEntry);
            }
        }, 0);
    });

    /* Esc で閉じて、焦点をボタンへ戻す（押した場所を見失わないため）。
       【いちばん先に受けて、ほかへ流さない】部屋の Esc（パネルを閉じる）より先に。1回の Esc で閉じるのは「?」だけ */
    window.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && openEntry) {
            e.stopPropagation();
            close(openEntry, true);
        }
    }, true);

    /* 画面が動いたら置き直す（fixed なので、放っておくと取り残される） */
    function reposition() {
        if (openEntry) {
            place(openEntry);
        }
    }

    window.addEventListener('scroll', reposition, true);
    window.addEventListener('resize', reposition);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupFitHints);
    } else {
        setupFitHints();
    }

})();
