/* ============================================
   vb-chat プロジェクト、site.js
   ============================================ */


/* ============================================
   1. ハンバーガーメニュー
   ============================================ */

// ハンバーガーアイコンを、押したら、メニューを、開閉する
document.addEventListener("DOMContentLoaded", function () {
    const btn = document.getElementById("hamburgerBtn");
    const nav = document.getElementById("mainNav");

    // 【管理人の画面には、ハンバーガーが無い】
    // Site.Master が出さないので、ここで null になる。
    // 見ずに addEventListener を呼ぶと例外で止まり、この下の DOMContentLoaded の処理
    // （警告の帯・「?」の小窓・確認のボタン）まで巻き込まれはしないが、コンソールが汚れる
    if (!btn || !nav) {
        return;
    }

    btn.addEventListener("click", function () {
        // メニューの開閉に合わせて、押したボタン自身の見た目も切り替える
        const isOpen = nav.classList.toggle("is-open");

        // クラス名が is-open ではなく is-active なのは、このボタンが部屋の中の
        // 人数ボタン・歯車ボタンと同じ役目のため（そちらの名前に揃えている）
        btn.classList.toggle("is-active", isOpen);

        // 「押すと何かが開くボタン」であることを、読み上げソフトにも伝える
        btn.setAttribute("aria-expanded", isOpen ? "true" : "false");
    });
});


/* ============================================
   2. 共通の、警告メッセージ表示（alertの代わり）
   ============================================ */

// 画面上部に、"独自デザインの、警告メッセージ"を、しばらく表示する
// 使い方：showWarning("表示したい、メッセージ");
//
// 指摘：赤い知らせが短すぎて読めない。
//   ・×で閉じられる（本命。スマホにはマウスを乗せる操作が無いため）
//   ・自動で消えるまでの時間＝最低8秒＋文字数に応じて延ばす（上限20秒）。warningDuration
//   ・マウスが乗っている間・焦点が中にある間は消えない（離れたら、残りの時間から数え直す）
//   ・いくつ出ても重ならない（上から縦に積む。#warningStack）
//   ・読み上げソフトにも伝える（積む箱が aria-live）。それまではノックだけが、
//     読み上げ用の1行（#knockAnnounce）に同じ文言を入れていた。この箱に1本化した
//
// DB の WarningDisplaySeconds（既定3秒）は「最低これだけは出す」の意味になった。
// 8秒より短くしても8秒は出す（8秒はコードに持つ。短すぎて読めなかったのが直す理由なので）。
//
// 【このファイルは、設定を渡していないページからも読まれる】
// site.js は Site.Master が全ページで読み込む静的ファイルであり、
// vbChatSettings を出しているのは3ページ（トップ・ラウンジ・部屋）で、
// site.js を読むのは、そのうち Site.Master 配下の2ページだけ。
// 説明・利用規約・問い合わせ・ブログを足した日に、また効くようになる。
// **ここを vbChatSettings.warningDisplayMs だけにしてはいけない**
// （設定を渡していないページで、警告が一瞬も消えなくなる）
function showWarning(message) {

    var text = String(message == null ? '' : message);
    var stack = warningStack();

    // 同じ文言がもう出ていたら、古いほうを消して出し直す（時間も数え直す。読み上げもされ直す）
    Array.prototype.slice.call(stack.children).forEach(function (el) {
        if (el.getAttribute('data-text') === text && typeof el.vbDismiss === 'function') {
            el.vbDismiss(false);
        }
    });

    var box = document.createElement('div');
    box.className = 'custom-warning';
    box.setAttribute('data-text', text);

    // 文言は textContent で入れる（呼ぶ側が、サーバーの文言や部屋名を渡すため）
    var body = document.createElement('span');
    body.className = 'custom-warning-text';
    body.textContent = text;
    box.appendChild(body);

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'custom-warning-close';
    close.setAttribute('aria-label', 'この知らせを閉じる');
    close.textContent = '×';
    box.appendChild(close);

    var timer = null;
    var remaining = warningDuration(text);
    var startedAt = 0;
    var hovering = false;
    var focused = false;

    function start() {
        if (timer !== null || hovering || focused) {
            return;
        }
        startedAt = Date.now();
        timer = setTimeout(function () {
            dismiss(false);
        }, remaining);
    }

    function pause() {
        if (timer === null) {
            return;
        }
        clearTimeout(timer);
        timer = null;
        remaining = Math.max(1000, remaining - (Date.now() - startedAt));
    }

    // byUser … ×で閉じた。焦点がこの中にあったら、行き先を決めてから消す
    function dismiss(byUser) {

        pause();

        if (!box.parentNode) {
            return;
        }

        var hadFocus = box.contains(document.activeElement);

        box.parentNode.removeChild(box);

        if (hadFocus) {
            // ほかの知らせが残っていれば、その×へ。無ければ焦点を外す（ページの先頭から読み直しになる）
            var next = stack.querySelector('.custom-warning-close');
            if (next) {
                next.focus();
            } else if (document.activeElement && document.activeElement.blur) {
                document.activeElement.blur();
            }
        }
    }

    box.vbDismiss = dismiss;

    close.addEventListener('click', function () {
        dismiss(true);
    });

    box.addEventListener('mouseenter', function () { hovering = true; pause(); });
    box.addEventListener('mouseleave', function () { hovering = false; start(); });
    box.addEventListener('focusin', function () { focused = true; pause(); });
    box.addEventListener('focusout', function (e) {
        if (!box.contains(e.relatedTarget)) {
            focused = false;
            start();
        }
    });

    stack.appendChild(box);
    start();
}

// 自動で消えるまでの時間（ミリ秒）
//
// 最低8秒（DB の WarningDisplaySeconds がそれより長ければ、そちら）＋1文字につき0.15秒、上限20秒
// （DB の値が20秒より長ければ、上限もその値。DB で決めた「最低これだけ」を上限で削らないため）。
// 30文字なら12.5秒、60文字で17秒。上限は、×を押さなくても画面が片付くように。
//
// 【同じ計算が、部屋にもある】部屋は site.js を読まないので、chat-room.js の noticeDurationMs に写しがある
// （ノックの小窓が消えた理由を出す時間）。**片方を変えたら、もう片方も直すこと**
function warningDuration(text) {

    var base = 8000;

    if (typeof vbChatSettings !== 'undefined' && vbChatSettings.warningDisplayMs > base) {
        base = vbChatSettings.warningDisplayMs;
    }

    return Math.min(Math.max(20000, base), base + String(text || '').length * 150);
}

// 警告を積む箱（無ければ作る）
//
// 【aria-live は箱に付ける】読み上げソフトは、中身が足されたときに読む。
// ただし、作ったばかりの箱へすぐ足すと読まれないことがあるので、ページを開いたときに先に作っておく（下）
function warningStack() {

    var stack = document.getElementById('warningStack');

    if (!stack) {
        stack = document.createElement('div');
        stack.id = 'warningStack';
        stack.className = 'custom-warning-stack';
        stack.setAttribute('aria-live', 'polite');
        stack.setAttribute('aria-relevant', 'additions');
        document.body.appendChild(stack);
    }

    return stack;
}

document.addEventListener('DOMContentLoaded', function () {
    warningStack();
});

/* ============================================
   3. 共通の、確認ダイアログ（confirmの代わり）
   ============================================ */

/*
    取り返しのつかない操作の前に、確認を出す。
    使い方：

        vbChatConfirm({
            title:      'ログアウトしますか？',
            lines:      ['1行目', '2行目'],
            okText:     'ログアウトする',
            cancelText: 'やめる',
            onOk:       function () { ... }
        });

    【なぜ、ブラウザ標準の confirm を使わないのか】
    見た目がブラウザ任せになる。外の人に中を見せる前提のサイトなので、そこは自前で持ちたい。
    加えて Chrome は、背面のタブから出されるダイアログを抑制する。
    このサイトの自作コードに confirm も alert も1件も無いのは、その方針のため。

    【使い回す前提で作ってある】
    この先で必ず要る——部屋作成の回の「この部屋を閉じますか」「強制退室させますか」
    「入室禁止にしますか」、記事の回の「削除しますか」。
    見た目は Content/Site.css の「確認ダイアログ」の節が持つ。

    【部屋の中にも写しがある（写した）】Scripts/chat-room.js の vbChatConfirm。
    部屋（Chat/ChatRoom.aspx）は Site.Master を使わないので、site.js を読み込んでいない。
    CSSも Content/ChatRoom.css に写してある。**片方だけ直さないこと**
    （トグルスイッチが同じ事情で2か所に写しになっているのと同じ話）。
    部屋の写しだけの違い＝Esc を部屋の側へ渡さない（stopPropagation）・焦点を戻す先が消えていたら戻さない

    【文言は、必ず textContent で入れること】
    innerHTML にすると、呼び出し側が渡した文字列がHTMLとして解釈される。
    今は定数しか渡していないが、いずれ「〇〇さんを強制退室させますか」のように
    **利用者が決めた名前**を混ぜたくなる。そのときここが穴になる
    （「入口の検査だけでは足りない。出口でもエスケープする」と同じ話）
*/
function vbChatConfirm(options) {

    /* 【二重に開かないこと】
       開いている間は下が押せないので普通は起きないが、
       キーボードやプログラムからは連続で呼べてしまう。
       2枚重なると、閉じても下の1枚が残って操作できなくなる */
    if (document.querySelector('.custom-confirm-overlay')) {
        return;
    }

    var opts = options || {};

    /* 閉じたあとにフォーカスを戻す先。
       戻さないと、押した瞬間にボタンが消えて、キーボードや読み上げソフトで
       使っている人が居場所を見失う（パネルの閉じるボタンと同じ手当て） */
    var previousFocus = document.activeElement;

    var overlay = document.createElement('div');
    overlay.className = 'custom-confirm-overlay';

    var box = document.createElement('div');
    box.className = 'custom-confirm';

    /* role="alertdialog" は「今すぐ読んでほしい確認」という意味。
       aria-modal で「後ろは操作できない」ことも伝える */
    box.setAttribute('role', 'alertdialog');
    box.setAttribute('aria-modal', 'true');
    box.setAttribute('aria-labelledby', 'customConfirmTitle');
    box.setAttribute('aria-describedby', 'customConfirmBody');

    var title = document.createElement('p');
    title.className = 'custom-confirm-title';
    title.id = 'customConfirmTitle';
    title.textContent = opts.title || '確認';
    box.appendChild(title);

    var body = document.createElement('div');
    body.className = 'custom-confirm-body';
    body.id = 'customConfirmBody';

    /* 【大きく見せる1行 ＋ その下に添える小さい1行】（オートリンクで足した）

       部屋のリンクの確認（外部サイトへ移動します）で、行き先のドメインを大きく出すために作った。
       **使っているのは今のところ部屋（Scripts/chat-room.js の写し）だけ**だが、
       2つの写しが食い違わないよう、こちらにも同じ口を置いてある
       （記事やラウンジでもリンクの確認が要るようになったら、そのまま使える）。
       見た目は Content/Site.css の .custom-confirm-strong / -sub */
    if (opts.strongText) {
        var strong = document.createElement('p');
        strong.className = 'custom-confirm-strong';
        strong.textContent = opts.strongText;
        body.appendChild(strong);
    }

    if (opts.subText) {
        var sub = document.createElement('p');
        sub.className = 'custom-confirm-sub';
        sub.textContent = opts.subText;
        body.appendChild(sub);
    }

    var lines = opts.lines || [];

    for (var i = 0; i < lines.length; i++) {
        var line = document.createElement('p');
        line.textContent = lines[i];
        body.appendChild(line);
    }

    box.appendChild(body);

    var actions = document.createElement('div');
    actions.className = 'custom-confirm-actions';

    /* 【安全な側を先に置く】
       左が「やめる」、右が「実行する」。Windowsの並びに合わせてある */
    var cancelBtn = document.createElement('button');
    cancelBtn.type = 'button';
    cancelBtn.className = 'custom-confirm-btn custom-confirm-btn--cancel';
    cancelBtn.textContent = opts.cancelText || 'やめる';

    /* 【okHref を渡すと、実行する側が <a> になる】（オートリンク）

       確認したあとに window.open を呼ぶと、ブラウザが「勝手に開いた窓」とみなして
       止めることがある（とくにスマホの Safari）。押したもの自身がリンクなら、
       利用者が押して開いたことがブラウザにも分かるので、止められない。
       **こちらも、部屋の写しと食い違わないように置いてある**（使うのは今のところ部屋だけ） */
    var okBtn;

    if (opts.okHref) {
        okBtn = document.createElement('a');
        okBtn.href = opts.okHref;
        okBtn.target = '_blank';
        okBtn.rel = 'noopener noreferrer';
    } else {
        okBtn = document.createElement('button');
        okBtn.type = 'button';
    }

    okBtn.className = 'custom-confirm-btn custom-confirm-btn--ok';
    okBtn.textContent = opts.okText || 'はい';

    actions.appendChild(cancelBtn);
    actions.appendChild(okBtn);
    box.appendChild(actions);
    overlay.appendChild(box);
    document.body.appendChild(overlay);

    /* 【最初のフォーカスは「やめる」】
       Enterを空押ししたときに、取り返しのつかない側が実行されないようにするため */
    cancelBtn.focus();

    function close() {
        document.removeEventListener('keydown', onKeyDown, true);
        overlay.parentNode.removeChild(overlay);

        if (previousFocus && previousFocus.focus) {
            previousFocus.focus();
        }
    }

    function onKeyDown(e) {

        if (e.key === 'Escape') {
            e.preventDefault();
            close();
            return;
        }

        /* 【Tabを、この2つの中だけで回す】
           回さないと、後ろのページのボタンへフォーカスが抜ける。
           見た目は「後ろは押せない」のに、キーボードでは押せてしまう */
        if (e.key !== 'Tab') {
            return;
        }

        e.preventDefault();

        if (document.activeElement === cancelBtn) {
            okBtn.focus();
        } else {
            cancelBtn.focus();
        }
    }

    /* capture で拾うのは、後ろのページのキー操作より先に受け取るため */
    document.addEventListener('keydown', onKeyDown, true);

    /* 下敷きそのものを押したときだけ閉じる（中の箱を押しても閉じない）。
       外を押して閉じるのは、安全な側（やめる）に倒れるので入れてよい */
    overlay.addEventListener('click', function (e) {
        if (e.target === overlay) {
            close();
        }
    });

    cancelBtn.addEventListener('click', close);

    okBtn.addEventListener('click', function () {
        /* 先に閉じる。onOk が画面遷移する場合、開いたままだと
           移動の直前に一瞬だけダイアログが残って見える */
        close();

        if (typeof opts.onOk === 'function') {
            opts.onOk();
        }
    });
}


/* ============================================
   4. 共通の、「?」の説明の小窓
   ============================================
   【Scripts/help.js へ切り出した】部屋（Chat/ChatRoom.aspx）も読めるように。
   「入りきらないときだけ『?』」（.fit-hint）も、そちらにある。文は Core/HelpTexts.vb の表 */


/* ============================================================
   8. 理由は、入力欄の上に、サイトが自分で出す（作り直した）
   ============================================================
   【決まり】（：これを基本ルールにする。「毎回の基本」の5）
     ・断られた理由は、**入力欄の上**に出す（項目名 → 理由 → 入力欄）
     ・**ブラウザが自分で出す吹き出しは使わない**（required・type="email"・pattern・setCustomValidity）。
       画面の側で止めたときも、サーバーで断ったときも、**サイトが同じ場所・同じ見た目で出す**

   【きっかけ】スマホで、カーソルは欄に合ったのに、キーボードが開いて**欄の下の理由が隠れた**。
   メールアドレスの欄では、ブラウザの吹き出しが欄の下に出て、やはり切れた。
   理由が欄の上にあれば、欄が見えているかぎり理由も見えている。

   【この節にあるもの】
     A. 部品 window.vbFieldError …… 理由を欄の上に出す／消す／そこへカーソルを置く。下の「6」「9」もこれを使う
     B. ブラウザの確かめを、サイトの出し方に置き換える
          ・form に novalidate を付けて、ブラウザの吹き出しを止める
          ・送信のボタンが押されたら、**こちらが** checkValidity() で確かめ、理由を A で出す
          ・確かめる範囲は、前からの決まりのまま（下の【区切りごとの確かめ】）

   【区切りごとの確かめ】（入れた決まり。そのまま）
   ASP.NET Web Forms は、ページに form が1つしか置けない。設定の画面で「退会する」を押しても何も起きなかったのは、
   別の区切りの「新しいメールアドレス」の形の確かめが、退会の送信まで止めていたためだった。
       <asp:Panel ... data-validate-scope="1">
           <asp:Button ... formnovalidate="formnovalidate" />
     ・formnovalidate ＋ 区切りの箱（data-validate-scope）…… **その区切りの欄だけ**を確かめる
     ・formnovalidate だけ（箱が無い）…… 何も確かめずに送る（削除のボタンなど）
     ・どちらも無い送信のボタン …… form 全体を確かめる（ブラウザの元の動きと同じ）

   【5（押す前に確かめる）より前に置いてある】どちらも document の捕捉の段階で聞いており、
   **登録した順に呼ばれる**。こちらが先でないと、確認の窓で「退会する」を押したあとに、
   欄の確かめで止められることになる。**この節を5より後ろへ動かさないこと。**

   【site.js が届かなかったとき】novalidate は JavaScript で付けているので、付かない＝ブラウザの吹き出しに戻る。
   markup に novalidate を書かなかったのは、そのため（何も確かめない画面になるより、吹き出しのほうがまし）。

   【これは親切であって、防御ではない】本当の検査はサーバーにある
*/
(function () {

    var FIELD_SELECTOR = 'input, select, textarea';
    var counter = 0;

    /* ---------- どこに出すか ---------- */

    /* 理由を差し込む場所（この要素の直前に入れる）。
       ・data-error-anchor="id" …… その要素の前（欄そのものが隠れているとき。記事の本文＝Quill）
       ・チェックボックス・ラジオ …… その行（項目名ごと）の前。小さな四角の真上では読みにくい
       ・それ以外 …… 欄そのものの前 */
    function anchorOf(field) {

        var id = field.getAttribute('data-error-anchor');
        var byId = id ? document.getElementById(id) : null;

        if (byId) {
            return byId;
        }

        if (field.type === 'checkbox' || field.type === 'radio') {
            return field.closest('.account-terms, .form-row') || field.parentNode;
        }

        return field;
    }

    function reasonOf(field) {
        var id = field.getAttribute('data-error-id');
        return id ? document.getElementById(id) : null;
    }

    /* 【項目名が欄の左にあるとき（広い画面）】理由を1行目に回して、欄の左端にそろえる。
           　　　　　　理由
           項目名　　［入力欄］
       【項目名が欄の上にあるとき（狭い画面）】そのまま、項目名 → 理由 → 入力欄。
       どちらになるかは**幅の数字では決めない**。今の並びを測って決める
       （同じ行が、置かれる場所によって横にも縦にもなる＝イントリンシック・デザインの決まり） */
    function layout(reason, anchor) {

        var row = reason.parentNode;

        if (!row || !window.getComputedStyle) {
            return;
        }

        reason.classList.remove('field-error--side');
        reason.style.paddingLeft = '';

        var style = window.getComputedStyle(row);
        var isFlexRow = style.display.indexOf('flex') >= 0 && style.flexDirection.indexOf('row') === 0;

        row.classList.toggle('has-field-error', isFlexRow);

        if (!isFlexRow) {
            return;
        }

        /* 理由が無いものとして、欄がどこから始まるかを測る */
        reason.hidden = true;

        var rowRect = row.getBoundingClientRect();
        var left = anchor.getBoundingClientRect().left - rowRect.left - (parseFloat(style.paddingLeft) || 0);

        reason.hidden = false;

        if (left > 8) {
            reason.classList.add('field-error--side');
            reason.style.paddingLeft = Math.round(left) + 'px';
        }

    }

    function relayoutAll() {

        Array.prototype.forEach.call(document.querySelectorAll('.field-error[data-error-of]'), function (reason) {

            var field = document.getElementById(reason.getAttribute('data-error-of'));

            if (field) {
                layout(reason, anchorOf(field));
            }

        });

    }

    /* ---------- 出す・消す ---------- */

    /* 理由を、欄の上に出す。
       options.alert === false のときは role="alert" を付けない（同じ理由を、別の aria-live の要素が読むとき） */
    function show(field, message, options) {

        if (!field || !message) {
            return null;
        }

        var reason = reasonOf(field);
        var anchor = anchorOf(field);

        if (!reason) {

            if (!field.id) {
                counter++;
                field.id = 'vbField' + counter;
            }

            counter++;

            reason = document.createElement('p');
            reason.className = 'field-error';
            reason.id = 'vbFieldError' + counter;
            reason.setAttribute('data-error-of', field.id);

            if (!options || options.alert !== false) {
                reason.setAttribute('role', 'alert');
            }

            anchor.parentNode.insertBefore(reason, anchor);

            field.setAttribute('data-error-id', reason.id);

            /* 【欄に入ったときに、理由も読まれるように】 */
            var described = (field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);

            described.push(reason.id);
            field.setAttribute('aria-describedby', described.join(' '));

            /* 【直しはじめたら消す】出たままだと、直したあとも「まだ悪い」ように見える */
            var onEdit = function () {
                field.removeEventListener('input', onEdit);
                field.removeEventListener('change', onEdit);
                clear(field);
            };

            field.addEventListener('input', onEdit);
            field.addEventListener('change', onEdit);

        }

        /* 【文字は textContent で入れる】理由の文には、打たれた値が混ざることがある */
        reason.textContent = message;
        field.setAttribute('aria-invalid', 'true');

        layout(reason, anchor);

        return reason;
    }

    function clear(field) {

        if (!field) {
            return;
        }

        var reason = reasonOf(field);

        field.removeAttribute('aria-invalid');

        if (!reason) {
            return;
        }

        var described = (field.getAttribute('aria-describedby') || '').split(/\s+/)
            .filter(function (id) { return id && id !== reason.id; });

        if (described.length) {
            field.setAttribute('aria-describedby', described.join(' '));
        } else {
            field.removeAttribute('aria-describedby');
        }

        field.removeAttribute('data-error-id');
        field.removeAttribute('data-error-from');

        var row = reason.parentNode;

        row.removeChild(reason);

        if (row.classList && !row.querySelector(':scope > .field-error')) {
            row.classList.remove('has-field-error');
        }

    }

    /* 欄ではないもの（表の行・ボタンの行）のそばに、理由を出す（サーバーが「ここ」と印を付けたとき。下の「9」）。
       表の行なら、その行のすぐ上に1行足す（<tr> の間に <p> は置けないため） */
    function showNear(target, message) {

        counter++;

        var reason = document.createElement('p');

        reason.className = 'field-error field-error--block';
        reason.id = 'vbFieldError' + counter;
        reason.setAttribute('role', 'alert');
        reason.textContent = message;

        if (target.tagName === 'TR') {

            var tr = document.createElement('tr');
            var td = document.createElement('td');

            tr.className = 'field-error-row';
            td.colSpan = Math.max(1, target.cells.length);
            td.appendChild(reason);
            tr.appendChild(td);

            target.parentNode.insertBefore(tr, target);

            return reason;
        }

        var anchor = target.closest('.admin-actions, .account-actions, .contact-actions, .form-row') || target;

        anchor.parentNode.insertBefore(reason, anchor);

        return reason;
    }

    /* ---------- 見える所まで動かして、カーソルを置く ---------- */

    function isCoarse() {
        return !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
    }

    /* 画面の上に貼り付いているもの（記事を書く画面のツールバーなど）の下端。無ければ 0 */
    function stuckTop(viewTop) {

        if (!document.elementsFromPoint) {
            return 0;
        }

        var bottom = 0;

        document.elementsFromPoint(window.innerWidth / 2, viewTop + 2).forEach(function (el) {

            var position = window.getComputedStyle(el).position;

            if (position === 'fixed' || position === 'sticky') {
                bottom = Math.max(bottom, el.getBoundingClientRect().bottom - viewTop);
            }

        });

        return bottom;
    }

    /* 理由（と、その行の項目名）から欄までが、見えている範囲に入るように動かす。
       【指で触る端末】このあとキーボードが開いて、見える範囲が上半分ほどになる。
       だから**上のほうへ寄せておく**（理由がいちばん上、その下に欄）。
       【マウスの端末】もう全部見えているなら、動かさない（見えているものを動かされるほうが困る） */
    function reveal(reason, field) {

        if (!reason) {
            return;
        }

        var vv = window.visualViewport;
        var viewTop = vv ? vv.offsetTop : 0;
        var viewHeight = vv ? vv.height : window.innerHeight;

        var row = reason.parentNode && reason.parentNode.classList &&
                  reason.parentNode.classList.contains('has-field-error') ? reason.parentNode : reason;

        var top = Math.min(row.getBoundingClientRect().top, reason.getBoundingClientRect().top);
        var bottomEl = field ? anchorOf(field) : reason;
        var bottom = Math.max(bottomEl.getBoundingClientRect().bottom, reason.getBoundingClientRect().bottom);

        var margin = 12 + stuckTop(viewTop);
        var delta = 0;

        if (isCoarse()) {

            if (top < viewTop + margin || bottom > viewTop + viewHeight * 0.45) {
                delta = top - (viewTop + margin);
            }

        } else if (top < viewTop + margin || bottom > viewTop + viewHeight - 12) {

            delta = top - (viewTop + Math.max(margin, viewHeight * 0.25));

        }

        if (Math.abs(delta) > 1) {
            window.scrollBy(0, delta);
        }

    }

    function focus(field, reason) {

        if (!field) {
            reveal(reason, null);
            return;
        }

        reason = reason || reasonOf(field);

        /* 【隠してある欄（記事の本文の textarea）】その欄に vbFocus が付いていれば、それを呼ぶ
           （Scripts/article-editor.js が、Quill の欄へカーソルを置く関数を付けている） */
        if (typeof field.vbFocus === 'function') {
            field.vbFocus();
            return;
        }

        try {
            field.focus({ preventScroll: true });
        } catch (e) {
            field.focus();
        }

        reveal(reason, field);

        /* 【キーボードが開いたあとに、もう一度】開くと見えている範囲が縮み、ブラウザが欄だけを見える所へ送る。
           そのとき理由が上へ切れることがあるので、縮んだあとにそろえ直す。
           人が自分で動かしはじめたら、もう触らない */
        var vv = window.visualViewport;

        if (!vv || !isCoarse()) {
            return;
        }

        var again = function () {
            if (document.activeElement === field) {
                reveal(reason, field);
            }
        };

        var stop = function () {
            vv.removeEventListener('resize', again);
            window.removeEventListener('touchstart', stop);
        };

        vv.addEventListener('resize', again);
        window.addEventListener('touchstart', stop, { passive: true });
        window.setTimeout(stop, 1500);

    }

    /* ---------- ブラウザの確かめを、サイトの言葉で出す ---------- */

    function labelOf(field) {

        var given = field.getAttribute('data-error-label');

        if (given) {
            return given;
        }

        var label = field.id ? document.querySelector('label[for="' + field.id + '"]') : null;
        var text = '';

        if (label) {

            /* 見えない字（「（任意）」など）は、理由の文には入れない */
            var copy = label.cloneNode(true);

            Array.prototype.forEach.call(copy.querySelectorAll('.visually-hidden'), function (el) {
                el.parentNode.removeChild(el);
            });

            text = copy.textContent;

        } else if (field.getAttribute('aria-label')) {

            text = field.getAttribute('aria-label');

        } else if (field.getAttribute('aria-labelledby')) {

            var by = document.getElementById(field.getAttribute('aria-labelledby').split(/\s+/)[0]);

            text = by ? by.textContent : '';

        }

        return text.replace(/\s+/g, '').trim();
    }

    /* 【文は、サーバーが断るときの文とそろえてある】（「〇〇を入力してください。」「メールアドレスの形が正しくありません。」）
       画面の側で止めたときと、サーバーで断ったときで、言い方が変わらないように。
       欄ごとに変えたいときは data-error-required ／ data-error-pattern を書く */
    function messageFor(field) {

        var v = field.validity;
        var label = labelOf(field);
        var named = label ? '「' + label + '」' : 'この欄';

        if (v.customError) {
            return field.validationMessage;
        }

        if (v.valueMissing) {

            if (field.getAttribute('data-error-required')) {
                return field.getAttribute('data-error-required');
            }

            if (field.type === 'checkbox' || field.type === 'radio') {
                return named + 'にチェックを入れてください。';
            }

            if (field.tagName === 'SELECT') {
                return (label || 'この欄') + 'を選んでください。';
            }

            return (label || 'この欄') + 'を入力してください。';
        }

        if (v.typeMismatch && field.type === 'email') {
            return 'メールアドレスの形が正しくありません。';
        }

        if (v.patternMismatch && field.getAttribute('data-error-pattern')) {
            return field.getAttribute('data-error-pattern');
        }

        if (v.tooShort) {
            return (label || 'この欄') + 'は' + field.minLength + '文字以上にしてください。';
        }

        return (label || 'この欄') + 'の形が正しくありません。';
    }

    function isShown(field) {
        var r = field.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }

    /* 範囲の中の欄を確かめる。悪い欄があれば、理由を欄の上に出して、いちばん上の欄へカーソルを置く。
       戻り値 … 全部よければ true */
    function validateScope(scope) {

        var fields = scope.querySelectorAll(FIELD_SELECTOR);
        var firstBad = null;

        for (var i = 0; i < fields.length; i++) {

            var field = fields[i];

            if (!field.willValidate || !isShown(field)) {
                continue;
            }

            if (field.checkValidity()) {

                /* 前にこちらが出した理由だけを消す（サーバーが出した理由は、直しはじめるまで残す） */
                if (field.getAttribute('data-error-from') === 'browser') {
                    field.removeAttribute('data-error-from');
                    clear(field);
                }

                continue;
            }

            /* 【読み上げは、いちばん上の1つだけ】全部に role="alert" を付けると、理由が続けて読まれて聞き取れない。
               残りは、その欄に入ったときに読まれる（aria-describedby） */
            show(field, messageFor(field), { alert: firstBad === null });
            field.setAttribute('data-error-from', 'browser');

            if (firstBad === null) {
                firstBad = field;
            }

        }

        if (firstBad) {
            focus(firstBad);
            return false;
        }

        return true;
    }

    document.addEventListener('click', function (e) {

        var target = e.target;

        if (!target || !target.closest) {
            return;
        }

        var button = target.closest('input[type="submit"], button[type="submit"], button:not([type])');

        if (!button || !button.form) {
            return;
        }

        var scope = button.form;

        if (button.hasAttribute('formnovalidate')) {

            scope = button.closest('[data-validate-scope]');

            if (!scope) {
                return;
            }

        }

        if (!validateScope(scope)) {
            e.preventDefault();
            e.stopImmediatePropagation();
        }

    }, true);

    function install() {

        /* 【Validator の小さな赤字を隠す合図】理由は欄の上に出すので、欄の横のほうは出さない（Content/Site.css）。
           JavaScript が動かないときは、この class が付かず、今までどおり欄の横に出る */
        document.documentElement.classList.add('vb-field-errors');

        Array.prototype.forEach.call(document.forms, function (form) {
            form.noValidate = true;
        });

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

    /* 幅が変わると、項目名が欄の左から上へ回ることがある。出ている理由の置き方を決め直す */
    window.addEventListener('resize', relayoutAll);

    window.vbFieldError = {
        show: show,
        showNear: showNear,
        clear: clear,
        focus: focus,
        reveal: reveal
    };

})();


/* ============================================
   5. 共通の、「押す前に確かめる」ボタン
   ============================================ */

/*
    取り返しのつかないボタン（記事を消す・カテゴリを消す）に、確認のダイアログを挟む。
    使い方は、markup に属性を書くだけ：

        <asp:Button ... data-confirm="この記事を消します。よろしいですか？"
                        data-confirm-ok="消す" />

    【JavaScript は文言を1つも持たない】
    「?」の説明の小窓（上の4）と同じ決まり。文は、それを出したいものの隣に書く。

    【なぜ、ブラウザ標準の confirm を使わないのか】
    上の3（vbChatConfirm）のコメントにある。このサイトの自作コードに confirm も alert も1件も無い。

    【どうやってポストバックを止めているか】
    ボタンの click を捕まえて、いったん既定の動き（送信）を止める。
    「はい」が押されたら、**同じボタンに印を付けてから押し直す**。
    2度目はこの関数が印を見て、印を外して素通りさせる（そこで本当に送信される）。
    捕捉の段階（第3引数の true）で聞いているのは、ほかの click ハンドラより先に止めるため。

    【確かめの窓より先に、画面の検査を通したいとき】（記事の保存の帯の「公開する」）
        <asp:Button ... ValidationGroup="article" data-confirm-group="article" data-confirm="…" />
    data-confirm-group にグループ名を書くと、窓を出す前に WebForms の検査（Page_ClientValidate）を走らせ、
    落ちたら窓を出さずに止める（理由は、下の「6」が欄の上に出す）。
    書かないと、「公開します。よろしいですか？」に「公開する」と答えたあとで「タイトルを入力してください」と止められる。

    【部屋（Chat/ChatRoom.aspx）には写していない】
    部屋は Site.Master を使わず site.js を読み込んでいないので、あちらは vbChatConfirm を
    直接呼んでいる（Scripts/chat-room.js）。部屋で同じ形が要るようになったら、ここを写すこと
*/
(function () {

    document.addEventListener('click', function (e) {

        var target = e.target;

        if (!target || !target.closest) {
            return;
        }

        var button = target.closest('[data-confirm]');

        if (!button) {
            return;
        }

        /* 2度目（「はい」のあとに押し直された）。印を外して、そのまま送信させる */
        if (button.getAttribute('data-confirming') === '1') {
            button.removeAttribute('data-confirming');
            return;
        }

        e.preventDefault();

        /* 窓より先に、画面の検査（上のコメントの data-confirm-group）。
           【stopImmediatePropagation はしない】止めるのは既定の動き（送信）だけで足りる。
           ボタンの onclick（WebForms の検査）は、既定の動きが止まっていても走るが、もう結果は同じである */
        var group = button.getAttribute('data-confirm-group');

        if (group !== null && typeof window.Page_ClientValidate === 'function' &&
            !window.Page_ClientValidate(group)) {
            return;
        }

        vbChatConfirm({
            title: button.getAttribute('data-confirm'),
            okText: button.getAttribute('data-confirm-ok') || 'はい',
            cancelText: 'やめる',
            onOk: function () {
                button.setAttribute('data-confirming', '1');
                button.click();
            }
        });

    }, true);

})();

/* ============================================================
   6. WebForms の検査（Validator）の理由も、入力欄の上に出す（出す場所を変えた）
   ============================================================
   【きっかけ】記事を書く画面で、タイトルを入れずに保存を押したら、タイトルの欄の横に小さく赤字が出ただけで、
   いちばん下の保存のボタンのそばには何も出ず、「保存のボタンが無反応」に見えた
   （指摘：これでは分かるはずがない）。

   【やっていること】WebForms の画面の検査（Validator）が止めたときに——
     ・**問題の欄の上に、理由を出す**（上の「8」の vbFieldError。ブラウザの確かめ・サーバーで断ったときと、同じ場所・同じ見た目）
     ・いちばん上の問題の欄へ、カーソルを置く（理由と欄が見える所まで動かす）
     ・Validator が自分で出す欄の横の小さな赤字は、隠してある（Content/Site.css の .vb-field-errors）

   【保存の帯のある画面（記事を書く画面）だけ、帯にも出す】
   markup に data-validation-status="グループ名" の1行があれば、そこにも理由を全部出す
   （帯はいつも見えているので、「保存のボタンが無反応」に見えない）。
       <p class="admin-form-status" data-validation-status="グループ名"
          data-validation-status-prefix="保存できませんでした。" aria-live="assertive"></p>
   そのときは、欄の上の理由に role="alert" を付けない（同じ文が2回読まれるため。帯のほうが aria-live で読む）。
   **ほかの画面には、この1行を置かない**（理由の場所を2つにしない）。

   【仕組み】WebForms の Page_ClientValidate（検査の本体）を、包んで差し替えている。
   ボタンを押したときの WebForm_DoPostBackWithOptions が、名前で Page_ClientValidate を呼ぶので、差し替えたものが呼ばれる。
   【Page_IsValid を後から見る形にしなかった】検査しないボタン（CausesValidation="False"）を押したときは
   前の結果が残っていて、「止められた」と取り違える。

   【サーバーの側で検査に落ちて、画面が返ってきたとき】（JavaScript の検査を通り抜けた場合）
   Validator は「落ちた」という印を付けて返ってくるので、読み込みのあとに同じ出し方で出す（下の showServerSide）。

   【これは親切であって、防御ではない】本当の検査は、サーバー側（Page.IsValid と保存の口）にある
*/
(function () {

    function install() {

        if (typeof window.Page_ClientValidate !== 'function' || window.Page_ClientValidate.vbWrapped) {
            return;
        }

        var original = window.Page_ClientValidate;

        var wrapped = function (group) {

            var ok = original.apply(this, arguments);

            try {
                report(group || '', ok, true);
            } catch (e) {
                /* 知らせに失敗しても、検査そのものの結果は変えない */
            }

            return ok;

        };

        wrapped.vbWrapped = true;
        window.Page_ClientValidate = wrapped;

    }

    function inGroup(validator, group) {
        return (validator.validationGroup || '') === group;
    }

    /* group が null なら、グループを問わず全部見る（読み込みのあとの showServerSide） */
    function report(group, ok, moveFocus) {

        var validators = window.Page_Validators || [];
        var order = [];
        var firstMessage = {};
        var seen = {};

        for (var i = 0; i < validators.length; i++) {

            var v = validators[i];

            if (group !== null && !inGroup(v, group)) {
                continue;
            }

            var id = v.controltovalidate;

            if (!(id in seen)) {
                seen[id] = true;
                order.push(id);
            }

            if (v.isvalid === false && !(id in firstMessage)) {
                firstMessage[id] = v.errormessage || '';
            }

        }

        /* 【~=】data-validation-status には組の名前を空白区切りで複数書ける（記事を書く画面＝article articlePublish） */
        var box = group === null || group === '' ? null : document.querySelector('[data-validation-status~="' + group + '"]');
        var firstBad = null;
        var messages = [];

        order.forEach(function (id) {

            var control = document.getElementById(id);

            if (!control) {
                return;
            }

            if (!(id in firstMessage)) {

                /* 直った欄からは、検査が出した理由だけを消す */
                if (control.getAttribute('data-error-from') === 'validator') {
                    window.vbFieldError.clear(control);
                }

                return;
            }

            var text = firstMessage[id];

            window.vbFieldError.show(control, text, { alert: !box && firstBad === null });
            control.setAttribute('data-error-from', 'validator');

            if (firstBad === null) {
                firstBad = control;
            }

            if (text && messages.indexOf(text) < 0) {
                messages.push(text);
            }

        });

        if (box) {

            if (ok || firstBad === null) {
                box.textContent = '';
            } else {

                var prefix = box.getAttribute('data-validation-status-prefix') || '';

                /* 【いったん空にしてから入れる】同じ理由で続けて止められたときも、読み上げソフトがもう一度読むように */
                box.textContent = '';

                window.setTimeout(function () {
                    box.textContent = prefix + messages.join(' ');
                }, 50);

            }

        }

        if (firstBad !== null && moveFocus) {
            window.vbFieldError.focus(firstBad);
        }

        return firstBad;
    }

    /* サーバーの側で検査に落ちて返ってきた画面（下の「9」が呼ぶ）。落ちた欄があれば、いちばん上の欄を返す */
    window.vbShowServerValidators = function () {

        if (!window.Page_Validators || !window.Page_Validators.length) {
            return null;
        }

        return report(null, false, false);
    };

    /* WebForms の検査のスクリプトは、このファイルより前に読まれている（form の先頭）。
       念のため、読み終わりにももう一度見る */
    install();
    document.addEventListener('DOMContentLoaded', install);

})();


/* ============================================================
   7. 共通の、「送る直前に包む」欄（問い合わせページ）
   ============================================================
   【何のためか】本番サーバーの手前には、ホスティング会社の WAF（全員向けの粗い網）が立っていて、
   「<script>」や「onclick=」を含む POST を、**アプリに届く前に 403 で止める**。
   問い合わせの「内容」は、不具合の報告にタグやコードを書く人がいちばん多い欄なので、
   そのままだと「コードの話を書いた人だけ送れない」ことになる（記事の本文で実際に起きた）。

   そこで、送る直前にこの欄の値を "vbenc1:" ＋ Base64（UTF-8）に包む。**暗号ではない**（元に戻せる変換）。
   サーバーは Core/BodyTransport.vb で戻し、そのあと今までどおりの守り（長さの確かめ・出口のエスケープ）を通す。

   ============================================================
   見えている欄は、絶対に書き換えない
   ============================================================
   直す前は、送る直前に**見えている欄の値そのもの**を "vbenc1:…" に書き換えていた。
   ブラウザは「戻る」のときに、**離れた瞬間の値**を欄へ戻す。つまり戻ると欄に "vbenc1:…" が出た
   （本番の実機テストで開発者が踏んだ）。しかもその状態でもう一度送ると**さらに包まれ**、
   サーバーは1回しかほどかないので、**本文が "vbenc1:…" のまま DB に保存される**
   ——エラーも出ずに、中身だけが壊れる。いちばん気づけない壊れ方である。

   直したあとは、こうなっている。

     ・見えている欄（textarea）の value は、**1バイトも書き換えない**
       → 「戻る」で戻ったとき、書いた文がそのまま残る
     ・包んだ値は、**別の見えない欄（hidden）** に入れて送る
     ・見えている欄は、**送る瞬間だけ disabled にして POST に乗せない**
       （乗せると、生の "<script>" が WAF に止められる）。戻ってきたら pageshow で元に戻す
     ・**もう "vbenc1:" で始まる値は、二度と包まない**（万一のときの保険）
     ・読み込みのときに、欄が包まれた値になっていたら**ほどいて見せる**。
       直す前の版で包まれた値が「戻る」で残っていても、これで読める形に戻る（何重でもほどく）

   【使い方】markup に、次の2つの属性を書く。**hidden の欄もセットで要る。**

       <textarea data-encode-on-submit="（包んだ値を入れる hidden の id）"
                 data-encoded-prefix="vbenc1:"></textarea>
       <input type="hidden" id="（その id）" name="..." />

   先頭の印は、サーバー（BodyTransport.EncodedPrefix）から markup 経由で受け取る（1か所で持つため）。
   **サーバー側は「hidden に値があればそれを使い、無ければ見えている欄を使う」**という読み方にすること
   （見えている欄のほうは、JavaScript が動かない環境のための道である）。

   【WebForms の検査で止められた送信では、何もしない】（defaultPrevented）
   markup に書かれた onsubmit（WebForms の分）が、この関数より先に走る。

   【これは asp:Button（input type="submit"）が前提】
   ふつうのボタンを押した送信はブラウザ自身が行うので、submit の合図が飛ぶ。
   JavaScript から theForm.submit() を呼ぶ形（__doPostBack）では合図が飛ばないので、
   そういうボタンを足すときは、この仕組みが効かないことに注意する。

   【Scripts/article-editor.js にも、同じ形の包む・戻すがある】
   あちらは Quill の中身を書く欄へ写す処理と一体になっているので、**わざと別に持っている**。
   **先頭の印（vbenc1:）と、包み方を変えるときは、両方直すこと**
   （あちらの textarea は、そもそも画面に出ていない＝人が見る欄ではないので、この直しは要らない） */
(function () {

    /* 何重に包まれていても、ほどききるための上限（壊れた値で無限に回らないため） */
    var MAX_UNWRAP = 5;

    /* 【UTF-8 のバイト列にしてから Base64】btoa は日本語をそのまま受け取れない（1バイトの字しか扱えない）。
       【細切れにして変換する】大きな値を1回で String.fromCharCode に渡すと、引数が多すぎて止まる
       【もう包まれている値は、そのまま返す】二重に包まないための保険
       （サーバー側の BodyTransport.Encode も、同じ守りを持っている） */
    function encode(prefix, text) {

        if (!text) {
            return '';
        }

        if (text.indexOf(prefix) === 0) {
            return text;
        }

        var bytes = new TextEncoder().encode(text);
        var binary = '';
        var CHUNK = 0x8000;

        for (var i = 0; i < bytes.length; i += CHUNK) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
        }

        return prefix + window.btoa(binary);

    }

    /* 戻せなければ null（壊れた UTF-8 も、例外にして null。化けた字で見せない） */
    function decode(prefix, value) {

        try {

            var binary = window.atob(value.substring(prefix.length));
            var bytes = new Uint8Array(binary.length);

            for (var i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }

            return new TextDecoder('utf-8', { fatal: true }).decode(bytes);

        } catch (e) {
            return null;
        }

    }

    function setUp(box) {

        var prefix = box.getAttribute('data-encoded-prefix') || 'vbenc1:';
        var hidden = document.getElementById(box.getAttribute('data-encode-on-submit') || '');
        var form = box.form;

        if (!hidden || !form) {
            /* 【hidden が無ければ、何もしない】
               ここで「今までどおり見えている欄を書き換える」形へ落とすと、
               直したはずの壊れ方が、置き忘れた画面だけで静かに復活する */
            return;
        }

        /* 包まれた値が欄に残っていたら、ほどいて見せる（直す前の版が「戻る」で残っていた場合） */
        function unwrapBox() {

            for (var i = 0; i < MAX_UNWRAP; i++) {

                if (box.value.indexOf(prefix) !== 0) {
                    return;
                }

                var once = decode(prefix, box.value);

                if (once === null) {
                    return;
                }

                box.value = once;

            }

        }

        /* 画面が（戻るなどで）出てきたときの後始末 */
        function restore() {

            /* 送る瞬間に止めた欄を、使える状態へ戻す。
               「戻る」でページごと保存されていた場合、disabled のまま出てくるため */
            box.disabled = false;

            /* 【包んだ値は、必ず捨てる】戻ってきた画面の hidden に古い値が残っていると、
               利用者が書き直したのに、古い中身が送られる。次の送信で入れ直すので、消してよい */
            hidden.value = '';

            unwrapBox();

        }

        form.addEventListener('submit', function (e) {

            if (e.defaultPrevented) {
                return;
            }

            /* 【空のときは包まない】包むと "" ではなくなり、サーバー側の「入力してください」が
               「読み取れませんでした」に変わって、理由が的外れになる */
            if (!box.value) {
                hidden.value = '';
                return;
            }

            hidden.value = encode(prefix, box.value);

            /* 【見えている欄は書き換えない。POST に乗せないために、止めるだけ】
               disabled の入力欄は送られない（readonly では送られてしまう）。
               value はそのままなので、「戻る」で戻ったときに書いた文が残る */
            box.disabled = true;

        });

        window.addEventListener('pageshow', restore);
        restore();

    }

    function install() {

        Array.prototype.forEach.call(
            document.querySelectorAll('[data-encode-on-submit]'), setUp);

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();


/* ============================================================
   9. サーバーで断られたら、理由を「操作したところ」へ（同じ日の夕方に、出す場所を変えた）
   ============================================================
   指摘：毎回同じことを言っている。徹底する。

   【きっかけ】①設定の画面で退会のパスワードを間違えたら、ページが開き直って画面のいちばん上に戻り、
   いちばん下の区切りに出た理由が見えなかった ②カテゴリの画面は、理由の場所がページの上に1か所しかなく、
   ずっと下の「追加」で断られると上へ飛んでいた ③スマホでは、欄の下の理由がキーボードに隠れた。

   【やっていること】サーバーで断って画面が返ってきたとき（ポストバック）、理由の要素（data-error-jump）に文字があれば——

     ア. サーバーが「この欄が悪い」と印を付けている（aria-invalid="true"）
         → **理由を、その欄の上へ移す**（上の「8」の vbFieldError）。そこへカーソルを置く
     イ. 欄ではないもの（表の行・ボタン）に「ここ」と印が付いている（data-error-here）
         → 理由を、その行・そのボタンの行のすぐ上へ移す
     ウ. どちらの印も無い（「混み合っています」「ユーザー名かパスワードが違います」など、どの欄とも言えない理由）
         → 理由は、サーバーが置いた場所のまま（**各画面は、理由の要素を、その区切りの欄より上に置くこと**）。
            そこが見える所まで動かし、区切りの中の空の欄（無ければ最初の欄）にカーソルを置く

   【使い方】（サーバーの側）
       <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
       ' 悪い欄が分かるとき　　　　　　　　FormErrors.MarkField(PasswordBox)
       ' 欄ではなく、行やボタンのとき　　　FormErrors.MarkHere(AddButton)   ／ row.Attributes("data-error-here") = "1"
   印は Core/FormErrors.vb が付ける（ポストバックのはじめに、前の回の印を消すのも同じクラス）。

   【探す範囲】理由の要素の区切り（data-validate-scope・.account-section・.admin-box・.account-box・.contact-box のいちばん近いもの）の中。

   【サーバーの側の Validator が落ちたとき】も、ここで出す（上の「6」の vbShowServerValidators）。

   【window の load のあとで動く】Web Forms の MaintainScrollPositionOnPostback（元の位置へ戻す）は
   load で動くので、それより後に動かないと、戻されてしまう。
   **記事を書く画面にも、付けた**（それまでは付けていなかった）。サーバーが「どの欄の話か」を返すようになったので、
   タイトル・本文・URL の名前・カテゴリの上へ理由を移す（保存の帯にも同じ理由が出る）。
   article-editor.js の位置の戻しは「?done=saved」のときだけなので、断られた画面ではぶつからない。

   【人が先に動かしたら、画面は動かさない】読み込みの途中でスクロールやキーを押した人を、引っ張らない
   （理由を欄の上へ移すところまではやる。動かすのとカーソルだけをやめる） */
(function () {

    var SCOPES = '[data-validate-scope], .account-section, .admin-box, .account-box, .contact-box, form';
    var FIELDS = 'input:not([type=hidden]):not([type=submit]):not([type=button]):not([type=checkbox]):not([type=radio]):not([disabled]):not([readonly]), select:not([disabled]), textarea:not([disabled])';
    var MARKED = 'input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"]';

    var touched = false;

    function markTouched() {
        touched = true;
    }

    ['wheel', 'touchstart', 'keydown', 'mousedown'].forEach(function (name) {
        window.addEventListener(name, markTouched, { passive: true, once: true });
    });

    function isUsable(el) {
        if (el.classList.contains('visually-hidden') || el.getAttribute('aria-hidden') === 'true') {
            return false;
        }
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }

    function usableFields(scope) {
        return Array.prototype.filter.call(scope.querySelectorAll(FIELDS), isUsable);
    }

    /* どの欄とも言えない理由のとき、カーソルを置く欄（空の欄 → 最初の欄） */
    function pickField(scope) {

        var all = usableFields(scope);

        for (var j = 0; j < all.length; j++) {
            if (!all[j].value) { return all[j]; }
        }

        return all.length > 0 ? all[0] : null;
    }

    function run() {

        var api = window.vbFieldError;

        if (!api) {
            return;
        }

        /* サーバーの側の Validator が落ちていたら、その欄の上に出す */
        var validatorField = window.vbShowServerValidators ? window.vbShowServerValidators() : null;

        var errors = Array.prototype.filter.call(
            document.querySelectorAll('[data-error-jump]'),
            function (el) { return el.textContent.trim().length > 0; });

        if (errors.length === 0) {

            if (validatorField && !touched) {
                api.focus(validatorField);
            }

            return;
        }

        var error = errors[0];
        var text = error.textContent.trim();
        var scope = error.closest(SCOPES) || document.body;

        /* ア. 悪い欄が分かっている → 欄の上へ移す */
        var marked = Array.prototype.filter.call(scope.querySelectorAll(MARKED), isUsable)[0] ||
                     scope.querySelector('[data-error-anchor][aria-invalid="true"]');

        if (marked) {

            error.textContent = '';
            api.show(marked, text);

            if (!touched) {
                api.focus(marked);
            }

            return;
        }

        /* イ. 行・ボタンに印 → そのすぐ上へ移す */
        var here = scope.querySelector('[data-error-here]');

        if (here) {

            error.textContent = '';

            var moved = api.showNear(here, text);

            if (!touched) {

                var inside = here.tagName === 'TR' ? pickField(here) : null;

                if (inside) {
                    inside.setAttribute('aria-describedby', moved.id);
                }

                api.focus(inside, moved);

            }

            return;
        }

        /* ウ. どの欄とも言えない → 理由はそのまま。見える所まで動かして、空の欄へ */
        if (touched) {
            return;
        }

        if (!error.id) {
            error.id = 'vbErrorJump1';
        }

        var field = pickField(scope);

        if (field) {

            var described = (field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);

            if (described.indexOf(error.id) < 0) {
                described.push(error.id);
                field.setAttribute('aria-describedby', described.join(' '));
            }

            /* 【理由と欄が離れすぎているときは、カーソルを置かない】
               置くと、スマホではキーボードが開いて欄だけが見える所へ送られ、理由が画面の外へ出る */
            var gap = field.getBoundingClientRect().bottom - error.getBoundingClientRect().top;
            var room = (window.visualViewport ? window.visualViewport.height : window.innerHeight) * 0.45;

            /* 【理由が欄より下にあるときも、カーソルを置かない】（理由が保存の帯に出る画面＝プロフィールの編集）
               帯はいつも見えているので、そこから離れた上の欄へ飛ばすと、かえって理由を見失う */
            var fieldIsAfter = !!(error.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING);

            if (gap > room || !fieldIsAfter) {
                field = null;
            }

        }

        api.focus(field, error);

    }

    function afterLoad() {
        window.setTimeout(run, 0);
    }

    if (document.readyState === 'complete') {
        afterLoad();
    } else {
        window.addEventListener('load', afterLoad);
    }

})();


/* ============================================================
   10. 行き先（URL）の欄で、使えない形を先に止める（お知らせの行き先）
   ============================================================
   【きっかけ】お知らせの行き先に「javascript:」を入れると、本番の WAF がアプリに届く前に 403 の画面を返していた
   （理由が何も出ない）。**画面の側でも、送る前に「その行き先は使えません」と止める。**
   **本体はサーバー**（Notices/SiteNoticeRules.vb の ValidateLink）——JS を切られても、そこで断る。
   欄の値は「送る直前に包む」（上の「7」）ので、JS が動いていれば WAF にも止められない。

   【使い方】欄に data-url-field を書くだけ。
   【仕組み】setCustomValidity（ブラウザの確かめ）に理由を入れる。**吹き出しは出ない**——
   送るときに上の「8」がその欄を確かめ、この理由を**欄の上に**出す（吹き出しから置き換えた）。
   受け付けるかの細かい決まり（http・https・「/」）は**サーバーだけが持つ**。ここで止めるのは、
   明らかに危ない形（javascript: など、http・https 以外の「英字:」で始まるもの）だけ */
(function () {

    var MESSAGE = 'その行き先は使えません（http:// か https:// で始まるアドレスか、「/」で始まるこのサイトのアドレスにしてください）。';

    function check(field) {

        var value = (field.value || '').trim();
        var scheme = /^([a-z][a-z0-9+.\-]*):/i.exec(value);
        var bad = scheme !== null && !/^https?$/i.test(scheme[1]);

        field.setCustomValidity(bad ? MESSAGE : '');

    }

    function install() {

        Array.prototype.forEach.call(document.querySelectorAll('[data-url-field]'), function (field) {
            check(field);
            field.addEventListener('input', function () { check(field); });
        });

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();


/* ============================================================
   11. 二重送信を防ぐ、使い捨ての目印（仕組みの本体は Core/FormOnce.vb）
   ============================================================
   【サーバーの側】画面を描くたびに、フォームの見えない欄（vbFormOnce）に新しい目印を1つ入れる。
   送信を受け付けるときに「この目印は使った」と覚え、同じ目印の2度目は「もう送信済みです」と断る。

   【ここでやること＝戻ってきた画面に、前と同じ目印を入れ直す】
   送ったあと、戻るボタンで入力の画面に戻ると、ブラウザは画面を——
     ・取っておいたものを、そのまま出す（bfcache）→ 目印も前のまま。何もしなくてよい
     ・サーバーから取り直す（このサイトのアカウントの画面は no-store なので、こちらになる）
       → 打った文字はブラウザが欄へ戻してくれるが、**目印は新しいものに変わっている**＝2件目が通ってしまう
   そこで、**履歴の1件ごとに、目印を覚えさせておく**（history.state）。
   履歴の1件は「画面を1回開いたこと」そのものなので、
     ・戻る・進むで来た　　　 → 覚えていた目印を、欄へ入れ直す（同じ画面に戻ってきた）
     ・ふつうに開いた・F5 　　→ サーバーが入れた新しい目印を、そのまま使って覚える（画面を開き直した）
   になる。「戻るボタンは壊さない。開き直せば、わざと2件目を送れる」という決まりどおり。

   【history.state の、ほかの中身は消さない】いまある中身に足す形で書く。
   【JavaScript が動かないとき】サーバーが入れた目印のまま送られる（戻って取り直した画面からの2件目は防げない。
   取っておいた画面をそのまま出すブラウザでは防げる）。これは親切であって、防御ではない */
(function () {

    var FIELD_ID = 'vbFormOnce';
    var STATE_KEY = 'vbFormOnce';

    function navigationType() {

        try {
            var entries = window.performance.getEntriesByType('navigation');
            return entries && entries.length ? entries[0].type : '';
        } catch (e) {
            return '';
        }

    }

    function install() {

        var field = document.getElementById(FIELD_ID);

        if (!field || !window.history || typeof window.history.replaceState !== 'function') {
            return;
        }

        var state = window.history.state;
        var remembered = state && typeof state === 'object' ? state[STATE_KEY] : null;

        /* 戻る・進むで来て、この履歴の1件が目印を覚えている → それを使う */
        if (navigationType() === 'back_forward' && typeof remembered === 'string' && /^[0-9a-f]{32}$/.test(remembered)) {
            field.value = remembered;
            return;
        }

        /* ふつうに開いた・開き直した → サーバーの目印を、この履歴の1件に覚えさせる */
        var next = {};

        if (state && typeof state === 'object') {
            Object.keys(state).forEach(function (key) { next[key] = state[key]; });
        }

        next[STATE_KEY] = field.value;

        try {
            window.history.replaceState(next, '');
        } catch (e) {
            /* 覚えさせられなくても、画面は動く（戻ってからの2件目を防げないだけ） */
        }

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();


/* ============================================================
   12. 右上の角＝自分のアカウントのメニュー／アイコンの文字の色
   ============================================================
   組み立ては Site.Master.vb の AccountCornerHtml。中身は最初から HTML に入っていて（hidden）、ここは開け閉めだけ。

   【「?」の小窓（4）と同じ、ディスクロージャの形】ボタンの aria-expanded ＋ aria-controls。
   role="menu" にはしていない（あれは矢印キーで項目を移る作りの名乗りで、中身はふつうのリンクの並び。
   名乗りと中身が食い違うと、読み上げソフトによっては Tab で中へ入れなくなる）。

     開く  … ボタンを押す（キーボードの Enter・Space もここへ来る）
     閉じる … もう一度押す／Esc（焦点はボタンへ戻す）／外を押す／焦点がメニューの外へ出る（Tab で抜けた）

   【4（「?」の小窓）の部品に乗せなかった理由】あちらはマウスを乗せると開く・position: fixed で画面の端に寄せる、
   という「短い説明」のための作りで、メニューには要らない動きばかりだった。開け閉めの決まり（Esc・外・焦点を戻す）は同じにしてある。

   【window.vbAvatarTextColor】アイコンの丸に乗せる文字の色（白で 3:1 あれば白、なければ黒っぽい色）。
   サーバー（Accounts/AccountAvatar.vb の TextColorFor）と**同じ計算**。プロフィールの編集の見本が使う。
   **片方だけ直さないこと**（見本の色と、保存したあとの色が食い違う）
*/
(function () {

    /* --- アイコンの文字の色（WCAG の相対輝度）--- */
    function channel(hex) {
        var v = parseInt(hex, 16) / 255;
        return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    }

    window.vbAvatarTextColor = function (color) {

        if (typeof color !== 'string' || !/^#[0-9a-fA-F]{6}$/.test(color)) {
            return '#FFFFFF';
        }

        var luminance = 0.2126 * channel(color.substr(1, 2)) +
                        0.7152 * channel(color.substr(3, 2)) +
                        0.0722 * channel(color.substr(5, 2));

        /* 白で 3:1（WCAG の大きい文字の基準）あれば白、なければ黒っぽい色。理由は AccountAvatar.vb の TextColorFor */
        var withWhite = 1.05 / (luminance + 0.05);

        return withWhite >= 3.0 ? '#FFFFFF' : '#1A1A1A';
    };


    /* --- メニューの開け閉め --- */
    function install() {

        var btn = document.getElementById('accountMenuBtn');
        var pop = document.getElementById('accountMenu');

        /* ログインしていない人のページ・部屋には無い */
        if (!btn || !pop) {
            return;
        }

        var corner = btn.parentNode;

        function isOpen() {
            return btn.getAttribute('aria-expanded') === 'true';
        }

        function open() {
            pop.hidden = false;
            btn.setAttribute('aria-expanded', 'true');
        }

        function close(returnFocus) {

            if (!isOpen()) {
                return;
            }

            pop.hidden = true;
            btn.setAttribute('aria-expanded', 'false');

            if (returnFocus) {
                btn.focus();
            }
        }

        btn.addEventListener('click', function () {
            if (isOpen()) {
                close(false);
            } else {
                open();
            }
        });

        /* Esc。メニューの中にいても、ボタンにいても閉じて、焦点はボタンへ */
        document.addEventListener('keydown', function (e) {
            if ((e.key === 'Escape' || e.key === 'Esc') && isOpen()) {
                e.preventDefault();
                close(true);
            }
        });

        /* 外を押したら閉じる（焦点は動かさない＝押した場所へ行く） */
        document.addEventListener('click', function (e) {
            if (isOpen() && !corner.contains(e.target)) {
                close(false);
            }
        });

        /* Tab でメニューの外へ抜けたら閉じる（開いたまま取り残さない） */
        corner.addEventListener('focusout', function (e) {
            if (isOpen() && e.relatedTarget && !corner.contains(e.relatedTarget)) {
                close(false);
            }
        });

        /* 【戻るボタンで戻ってきたときに、開いたまま出ないように】（bfcache） */
        window.addEventListener('pageshow', function () {
            close(false);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();


/* ============================================================
   13. 保存の帯を、スマホのキーボードのすぐ上に出す（2つの画面から1か所へまとめた）
   ============================================================
   使うのは、画面の下に貼り付く保存の帯（.admin-savebar）を持つ画面＝記事を書く画面とプロフィールの編集の画面。
   それまでは Scripts/profile-editor.js と Scripts/article-editor.js が、それぞれ同じことを書いていた（基本ルールの⑫）。

   【なぜ要るのか】Android の Chrome は、キーボードを出しても「ページの表示領域」を縮めず、**見えている範囲だけ**を縮める。
   position: sticky の bottom: 0 は「ページの表示領域」の下端に付くので、帯がキーボードの裏へ行く。

   【やっていること】
     (a) 見えている範囲の上端・下端のずれを CSS の変数（--vv-top ／ --vv-bottom）に入れる。
         帯は bottom: var(--vv-bottom)、記事を書く画面のツールバーは top: var(--vv-top) で付き直す（Content/Site.css）
     (b) 帯の高さ＋8px を --savebar-pad に入れる。Content/Site.css が :root の scroll-padding-bottom に使う
         ＝ブラウザが欄を画面へ送るとき、帯のぶんだけ余白を残してくれる
     (c) それでも打っている所が帯の裏に隠れていたら、足りないぶんだけ画面を送る（keepVisible）。
         (b) はブラウザが自分で送るときにしか効かない。キーボードが出て「あとから」隠れることがあるので、その受け皿。
         **本文の欄（Quill。contenteditable）は背が高いので、欄ではなくカーソルの位置で見る**

   【打っている間も、帯は出したまま】（指摘：スマホこそ下の帯のボタンが要る）
   前の記事を書く画面は、打っている間だけ帯の貼り付けをやめていた（is-typing）。今はどの画面もやめない。

   【開発機では確かめられない】ブラウザのペインにはキーボードが無く、visualViewport も縮まない。実機で見る */
(function () {

    function install() {

        var bar = document.querySelector('.admin-savebar');

        if (!bar) {
            return;
        }

        var root = document.documentElement;
        var vv = window.visualViewport;

        function updateBarHeight() {
            /* 8px は、欄と帯がぴったりくっつかないようにするための、ほんの少しのすき間 */
            root.style.setProperty('--savebar-pad', (Math.round(bar.getBoundingClientRect().height) + 8) + 'px');
        }

        function updateViewport() {

            if (!vv) {
                return;
            }

            var top = Math.max(0, vv.offsetTop);
            var bottom = Math.max(0, window.innerHeight - vv.height - vv.offsetTop);

            root.style.setProperty('--vv-top', Math.round(top) + 'px');
            root.style.setProperty('--vv-bottom', Math.round(bottom) + 'px');

        }

        function isTextEntry(el) {

            if (!el) {
                return false;
            }

            if (el.isContentEditable) {
                return true;
            }

            var tag = el.tagName;

            if (tag === 'TEXTAREA') {
                return true;
            }

            return tag === 'INPUT' && /^(text|search|url|email|number|tel|password|date)$/i.test(el.type || 'text');

        }

        /* 打っている所の位置。本文の欄（contenteditable）はカーソルの位置、ふつうの欄は欄そのもの */
        function caretRect(el) {

            if (el.isContentEditable && window.getSelection) {

                var sel = window.getSelection();

                if (sel.rangeCount > 0) {

                    var range = sel.getRangeAt(0).cloneRange();
                    var rects = range.getClientRects();
                    var r = rects.length ? rects[rects.length - 1] : range.getBoundingClientRect();

                    /* 空の行では幅も高さも0になることがある。そのときは欄の上端だけを使う */
                    if (r && (r.height > 0 || r.top > 0)) {
                        return { top: r.top, bottom: r.bottom || r.top + 20 };
                    }

                }

            }

            return el.getBoundingClientRect();

        }

        /* 【送りすぎないこと】打っている所の上端が画面の外へ出るまで送ってはいけない（上端が 8px のところまで） */
        function keepVisible() {

            var el = document.activeElement;

            if (!isTextEntry(el)) {
                return;
            }

            var limit = bar.getBoundingClientRect().top - 8;
            var rect = caretRect(el);

            if (rect.bottom <= limit) {
                return;
            }

            var delta = Math.min(rect.bottom - limit, Math.max(0, rect.top - 8));

            if (delta > 1) {
                window.scrollBy(0, delta);
            }

        }

        if (vv) {

            vv.addEventListener('resize', function () {
                updateViewport();
                updateBarHeight();
                /* キーボードが出た「あと」に、隠れていないかを見る */
                keepVisible();
            });

            vv.addEventListener('scroll', updateViewport);
            updateViewport();

        }

        window.addEventListener('resize', updateBarHeight);
        updateBarHeight();

        /* 帯の高さは、知らせの文が出た・消えたで変わる */
        if (window.ResizeObserver) {
            new window.ResizeObserver(updateBarHeight).observe(bar);
        }

        /* 【何度か見に行く】キーボードは、すぐには出そろわない（出るまでの動きがある）。
           同じことを何度やっても、隠れていなければ何もしないので害が無い */
        document.addEventListener('focusin', function (e) {

            if (!isTextEntry(e.target)) {
                return;
            }

            window.setTimeout(keepVisible, 0);
            window.setTimeout(keepVisible, 150);
            window.setTimeout(keepVisible, 400);

        });

        /* 【打つたびに】本文を打ち進めると、カーソルが帯の裏へ下りていくため（変換中の字も含む） */
        document.addEventListener('input', function () {
            window.setTimeout(keepVisible, 0);
        });

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();


/* ============================================================
   14. 管理メニューは、狭い画面では1行のまま横にすべらせる
   ============================================================
   前は折り返して2行になり、記事を書く画面では本文が遠くなっていた。見た目は Content/Site.css の .admin-nav（中身任せ：入らなければ横にすべる）。
   ここでは、**今いるページの項目が見える位置まで**、メニューの中を横に送っておく（左の端に隠れていると、どこにいるか分からない） */
(function () {

    function install() {

        var nav = document.querySelector('.admin-nav');
        var current = nav ? nav.querySelector('a[aria-current]') : null;

        if (!nav || !current || nav.scrollWidth <= nav.clientWidth) {
            return;
        }

        var navRect = nav.getBoundingClientRect();
        var itemRect = current.getBoundingClientRect();

        /* 項目の真ん中が、メニューの真ん中に来るように（端なら、行ける所まで） */
        nav.scrollLeft += (itemRect.left + itemRect.width / 2) - (navRect.left + navRect.width / 2);

    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }

})();

/* ============================================================
   15. 管理画面の項目名の列の幅を、中身から決める
   ============================================================
   設定の画面で「新しい／メールアドレス」と折り返し、「新しいパスワー／ド／（確認）」は言葉の途中で割れていた
   （列が 112px のまま固定で、入力欄の右には大きな余りがあった）。
   **いちばん長い項目名が1行で入る幅**を、その箱（.admin-box）の項目名を全部測って決め、CSS の変数 --admin-label-w に入れる。
   列は箱の中で1つの幅にそろう（入力欄の左端がそろう）。見た目の決まりは Content/Site.css の .admin-box .form-row .account-label の節。
   ・狭くて列の上限（箱の 45%）に当たるときは折り返すが、**言葉の途中では割らない**（CSS の word-break: keep-all ＋ markup の <wbr />）
   ・本文の列が狭い（項目名が上の行へ回る）画面では、この幅は使われない（CSS がそちらを優先する）
   ・測るのは、開いたときと、書体が届いたとき（文字の幅は画面の幅では変わらない）。JS が動かないときは 112px のまま */
(function () {

    var SELECTOR = '.form-row:not(.form-row--body) > .account-label, .form-row:not(.form-row--body) > .admin-label';

    function fit() {

        Array.prototype.forEach.call(document.querySelectorAll('.admin-box'), function (box) {

            var labels = box.querySelectorAll(SELECTOR);
            var widest = 0;

            Array.prototype.forEach.call(labels, function (label) {

                if (label.getClientRects().length === 0) {
                    return;
                }

                var oldWidth = label.style.width;
                var oldMax = label.style.maxWidth;
                var oldWhite = label.style.whiteSpace;

                label.style.width = 'max-content';
                label.style.maxWidth = 'none';
                label.style.whiteSpace = 'nowrap';

                widest = Math.max(widest, label.getBoundingClientRect().width);

                label.style.width = oldWidth;
                label.style.maxWidth = oldMax;
                label.style.whiteSpace = oldWhite;
            });

            if (widest > 0) {
                box.style.setProperty('--admin-label-w', Math.ceil(widest) + 'px');
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', fit);
    } else {
        fit();
    }

    window.addEventListener('load', fit);

    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(fit);
    }

})();


/* ============================================================
   16. 数字の入力欄を整える（指摘：数字を全角で打つと通らない。すべての数字の入力欄で直す）
   ============================================================
   置き方：ふつうの文字の欄（type="text"）に data-number-field="1" と、
     data-number-default（数字と認識できないときの既定。空＝空欄に戻す）・data-number-min・data-number-max を書く。
     スマホで数字のキーボードを出すために inputmode="numeric" も付ける。
   整え方（欄から離れたとき・送る直前）：
     ①前後の空白（全角の空白も）を省く ②全角の数字・マイナスを半角にする（Unicode の NFKC 正規化）
     ③数字と認識できなければ既定の数字 ④範囲の外なら、範囲の端へ寄せる（例 99999 → 9999、-5 → 0）
   【type="number" をやめた理由】全角で打つと、ブラウザが値を「空」として扱い、こちらには何も届かない（整えようが無い）。
   【サーバーにも同じ整え方がある】Core/NumberInput.vb（最後の守り。片方だけ直さないこと）。
   【送る直前は、捕捉の段階で】ボタンの onclick（WebForms の検査）より先に整える＝全角のままで「数で入力してください」と断られない */
(function () {

    function normalize(text) {

        var s = String(text || '');

        if (s.normalize) {
            s = s.normalize('NFKC');
        }

        return s.replace(/[−‐‑]/g, '-').trim();
    }

    function fix(el) {

        var raw = el.value;
        var s = normalize(raw);
        var def = el.getAttribute('data-number-default') || '';

        if (s.length > 0) {

            if (!/^-?\d+$/.test(s)) {
                s = def;
            } else {

                var n = parseInt(s, 10);
                var min = el.getAttribute('data-number-min');
                var max = el.getAttribute('data-number-max');

                if (min !== null && min !== '' && n < Number(min)) {
                    n = Number(min);
                }

                if (max !== null && max !== '' && n > Number(max)) {
                    n = Number(max);
                }

                s = String(n);
            }
        }

        if (s !== raw) {
            el.value = s;
        }
    }

    function fixAll(root) {
        Array.prototype.forEach.call((root || document).querySelectorAll('input[data-number-field]'), fix);
    }

    document.addEventListener('change', function (e) {
        if (e.target && e.target.matches && e.target.matches('input[data-number-field]')) {
            fix(e.target);
        }
    }, true);

    document.addEventListener('focusout', function (e) {
        if (e.target && e.target.matches && e.target.matches('input[data-number-field]')) {
            fix(e.target);
        }
    }, true);

    /* 送る直前（ボタンの click の捕捉＝WebForms の検査より先。Enter で送ったときもブラウザは既定のボタンの click を起こす） */
    window.addEventListener('click', function (e) {
        var b = e.target && e.target.closest ? e.target.closest('button, input[type="submit"], a') : null;
        if (b) {
            fixAll();
        }
    }, true);

    document.addEventListener('submit', function () { fixAll(); }, true);

    /* 外から呼べるように（今は誰も呼んでいない。自動保存はサーバーの Core/NumberInput.vb が整える） */
    window.vbNumberFieldsFix = fixAll;

})();


/* ============================================================
   17. 押したあと、画面の位置を飛ばさない（基本ルール U）
   ============================================================
   ボタンを押すと、Web Forms はページを送って作り直す（うまくいったら「?done=…」へ移り直す＝PRG）。
   どちらもブラウザから見れば別の読み込みなので、**画面がいちばん上から始まる**（お知らせの管理で「編集」を押すと上へ飛んだ）。

   【やっていること】
     (a) 送る直前に「どこを見ていたか（スクロールの位置）と、押したボタンの id」を覚える
         （submit と、LinkButton が使う __doPostBack の両方。__doPostBack は submit の合図を出さないため）
     (b) 同じ住所を 20 秒以内に開き直したら、戻す：
         ・サーバーが「ここへ戻して」と印を付けた要素（data-return-here）があれば、そこを画面の真ん中へ。
           さらに data-focus-here の欄があれば、カーソルを置く（お知らせ・カテゴリの「編集」）
         ・印が無ければ、覚えた位置へ戻す
         ・**断られて理由が出ているときは、何もしない**（理由の場所へ移すのは「9」の仕事。そちらが先）
     (c) 「保存しました」のような知らせ（.admin-done）が画面の外にあるときは、画面の下に数秒だけ同じ文を出す（結果が見えるように）

   【自分で同じことをしている画面は除く】記事を書く画面・プロフィールの画面（window.vbOwnReturnPoint を立てている）。
   いつか、そちらもこの部品へまとめる（基本ルール 8）

   【置き場所は sessionStorage】その場限りの「状態」（「ブラウザに何かを覚えさせるときの、置き場所」） */
(function () {

    'use strict';

    var KEY = 'vbchat_return_point';

    function remember(id) {
        try {
            window.sessionStorage.setItem(KEY, JSON.stringify({
                path: location.pathname.toLowerCase(),
                y: Math.round(window.pageYOffset || document.documentElement.scrollTop || 0),
                id: id || '',
                at: Date.now()
            }));
        } catch (e) {
            /* 覚えられなくても、送ること自体は止めない */
        }
    }

    document.addEventListener('submit', function (e) {
        var sub = e.submitter || document.activeElement;
        remember(sub && sub.id ? sub.id : '');
    }, true);

    function wrapPostBack() {
        if (typeof window.__doPostBack !== 'function' || window.__doPostBack.vbWrapped) {
            return;
        }
        var original = window.__doPostBack;
        var wrapped = function (target, argument) {
            var a = document.activeElement;
            remember(a && a.id ? a.id : '');
            return original.apply(this, arguments);
        };
        wrapped.vbWrapped = true;
        window.__doPostBack = wrapped;
    }

    function hasErrorShown() {
        var shown = false;
        Array.prototype.forEach.call(document.querySelectorAll('[data-error-jump]'), function (el) {
            if ((el.textContent || '').trim()) {
                shown = true;
            }
        });
        return shown || !!document.querySelector('[aria-invalid="true"]');
    }

    function inView(el) {
        var r = el.getBoundingClientRect();
        return r.bottom > 0 && r.top < window.innerHeight;
    }

    function toast(text) {
        var t = document.createElement('div');
        t.className = 'vb-return-toast';
        /* 読み上げは、もとの知らせ（role="status"）に任せる。同じ文を2回読ませない */
        t.setAttribute('aria-hidden', 'true');
        t.textContent = text;
        document.body.appendChild(t);
        window.setTimeout(function () { t.classList.add('is-leaving'); }, 4000);
        window.setTimeout(function () { if (t.parentNode) { t.parentNode.removeChild(t); } }, 4600);
    }

    function restore() {

        wrapPostBack();

        if (window.vbOwnReturnPoint) {
            return;
        }

        var saved = null;

        try {
            saved = JSON.parse(window.sessionStorage.getItem(KEY) || 'null');
            window.sessionStorage.removeItem(KEY);
        } catch (e) {
            saved = null;
        }

        var target = document.querySelector('[data-return-here]');

        if (!target && (!saved || saved.path !== location.pathname.toLowerCase() || Date.now() - saved.at > 20000)) {
            return;
        }

        if (hasErrorShown()) {
            return;
        }

        if (target) {
            target.scrollIntoView({ block: 'center' });
            /* 色を付けて、約1.8秒で外す（外すときの戻り方は CSS。「アニメーションを減らす」の人は、いきなり戻る） */
            target.classList.add('is-returned');
            window.setTimeout(function () {
                target.classList.add('is-return-fading');
                target.classList.remove('is-returned');
                window.setTimeout(function () { target.classList.remove('is-return-fading'); }, 1400);
            }, 1800);
            var f = document.querySelector('[data-focus-here]');
            if (f) {
                try { f.focus({ preventScroll: true }); } catch (e) { f.focus(); }
                if (typeof f.setSelectionRange === 'function' && typeof f.value === 'string') {
                    f.setSelectionRange(f.value.length, f.value.length);
                }
            }
        } else {
            window.scrollTo(0, saved.y);
        }

        var done = document.querySelector('.admin-done');
        if (done && (done.textContent || '').trim() && !inView(done)) {
            toast(done.textContent.trim());
        }
    }

    if (document.readyState === 'complete') {
        restore();
    } else {
        window.addEventListener('load', restore);
    }

})();


/* ============================================================
   18. 中身に合わせて伸びる、複数行の欄（お知らせの文）
   ============================================================
   <textarea data-autogrow> と書くと、打った量に合わせて高さが伸び縮みする（下限は rows の行数）。
   【field-sizing: content は使わない】Chrome だけで、Safari・Firefox では効かない（2026年10月の時点）。
   【改行で送らない欄にだけ使う】部屋の発言欄（Enter で送る）とは別物 */
(function () {

    'use strict';

    function grow(el) {
        el.style.height = 'auto';
        el.style.height = (el.scrollHeight + 2) + 'px';
    }

    function all() {
        Array.prototype.forEach.call(document.querySelectorAll('textarea[data-autogrow]'), grow);
    }

    document.addEventListener('input', function (e) {
        if (e.target && e.target.matches && e.target.matches('textarea[data-autogrow]')) {
            grow(e.target);
        }
    }, true);

    window.addEventListener('resize', all);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', all);
    } else {
        all();
    }

})();


/* ============================================================
   19. 右の枠が下へ回ったかの合図と、右の枠を右からすべり出させるボタン（基本ルール P）
   ============================================================
   要望：Claude のアプリの画面と同じように、右の枠が下へ回っても、出したいときに出せるようにする。
   見た目と決めたことは Content/Site.css の「右の枠を、画面の右からすべり出させる」。

   【合図】<html> に vb-side-below を付け外しする。**本文の列が行いっぱいなら、右の枠は下にいる**（同じ行にいれば、右の枠と
   あいだのぶん本文が狭い）。境目の数字を持たない＝記事のページ（40字＋右の枠）も、ほかのページ（1100px）も、同じ見方で分かる。
   使うのは、右の枠の外にあるもの（このボタン・目次の丸いボタンと板）。右の枠の中のものは @container side で自分で決める。
   ページの終わりで読み込まれるので、すぐに1回決める（DOMContentLoaded を待つと、目次のボタンが一瞬遅れて出る）。

   【中身は1つだけ】開いている間だけ、右の枠（aside）そのものを枠の中へ移し、閉じたら戻す（写しを作らない＝id が2つにならない・
   片方だけ直す事故が起きない）。元の場所には同じ高さの空の箱を置く＝ページの高さが変わらず、読んでいる位置が飛ばない（基本ルール U）。
   閉じ方は ×・外側（下地）・Esc。右の枠が横に並ぶ幅になったら、自分で閉じる。

   【ボタンはヘッダーの中】（最初は画面の右の端の縦書きの札だったが、本文の写真にかぶった。
   要望：その中に右の枠を開く札を入れる）。右上の角のすぐ後ろ（いちばん右）に入れる。
   絵は「横に枠のある四角」（☰＝サイトのメニューと見分けがつくように）。名前は「サイドメニュー」（中身はページで違う＝プロフィール・お知らせ など）。
   出るのは右の枠が下にいる間だけ（CSS の html.vb-side-below）。出入りでヘッダーの幅が変わるので、Scripts/header-fit.js に測り直してもらう */
(function () {

    'use strict';

    var layout = document.querySelector('.content-layout');
    var main = layout && layout.querySelector('.main-content');
    var sidebar = layout && layout.querySelector('.right-sidebar');
    // 管理人の画面には右の枠が無い（Site.Master の PublicSidebar）。合図もボタンも要らない
    if (!layout || !main || !sidebar) {
        return;
    }

    var root = document.documentElement;
    var tab = null;   // ヘッダーの中の「サイドメニュー」のボタン
    var LABEL = 'サイドメニュー';
    var drawer = null;
    var backdrop = null;
    var body = null;
    var closeBtn = null;
    var spacer = null;
    var opener = null;

    function isBelow() {
        // 1px の余裕＝小数の丸め
        return main.getBoundingClientRect().width >= layout.clientWidth - 1;
    }

    function isOpen() {
        return !!drawer && drawer.classList.contains('is-open');
    }

    // 右の枠に見出しが1つも無ければ、出すものが無いのでボタンも作らない
    function hasContent() {
        return !!sidebar.querySelector('h2, h3');
    }

    function build(header) {
        tab = document.createElement('button');
        tab.type = 'button';
        tab.className = 'side-drawer-btn';
        tab.id = 'sideDrawerBtn';
        tab.setAttribute('aria-label', LABEL);
        tab.setAttribute('aria-expanded', 'false');
        tab.setAttribute('aria-controls', 'sideDrawer');
        // 絵＝四角の右側に枠（右の枠が出てくる形）。色は currentColor（☰ と同じ）
        tab.innerHTML = '<svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true" focusable="false">' +
            '<rect x="3" y="4.5" width="18" height="15" rx="2" fill="none" stroke="currentColor" stroke-width="2"/>' +
            '<rect x="14" y="4.5" width="7" height="15" rx="1" fill="currentColor"/></svg>';

        backdrop = document.createElement('div');
        backdrop.className = 'side-drawer-backdrop';
        backdrop.hidden = true;

        drawer = document.createElement('div');
        drawer.className = 'side-drawer';
        drawer.id = 'sideDrawer';
        drawer.setAttribute('role', 'dialog');
        drawer.setAttribute('aria-modal', 'true');
        // 【見える題は置かない】中身のいちばん上に見出し（プロフィール・お知らせ など）があるので。名前は読み上げ用だけ
        drawer.setAttribute('aria-label', LABEL);
        drawer.inert = true;

        var head = document.createElement('div');
        head.className = 'side-drawer-head';
        closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'side-drawer-close';
        closeBtn.setAttribute('aria-label', '閉じる');
        closeBtn.textContent = '×';
        head.appendChild(closeBtn);

        body = document.createElement('div');
        body.className = 'side-drawer-body';

        drawer.appendChild(head);
        drawer.appendChild(body);
        // 【いちばん右＝右上の角のすぐ後ろ】（入れ替えた。要望：ログインと右の枠の札を左右逆にする。
        // 枠が右からすべり出るので、ボタンも右の端）。広い形＝メニュー［右上の角］［このボタン］／狭い形＝☰［右上の角］［このボタン］
        var corner = header.querySelector('.account-corner');
        header.insertBefore(tab, corner ? corner.nextSibling : null);
        document.body.appendChild(backdrop);
        document.body.appendChild(drawer);

        tab.addEventListener('click', function () {
            if (isOpen()) {
                close(true);
            } else {
                open();
            }
        });
        closeBtn.addEventListener('click', function () { close(true); });
        backdrop.addEventListener('click', function () { close(true); });

        drawer.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                e.preventDefault();
                close(true);
                return;
            }
            // 開いている間は、Tab を枠の中で回す（aria-modal の約束）
            if (e.key === 'Tab') {
                var items = drawer.querySelectorAll('a[href], button:not([disabled]), summary, input, select, textarea, [tabindex]:not([tabindex="-1"])');
                var list = Array.prototype.filter.call(items, function (el) { return el.offsetParent !== null; });
                if (!list.length) { return; }
                var first = list[0];
                var last = list[list.length - 1];
                if (e.shiftKey && document.activeElement === first) {
                    e.preventDefault();
                    last.focus();
                } else if (!e.shiftKey && document.activeElement === last) {
                    e.preventDefault();
                    first.focus();
                }
            }
        });
    }

    function open() {
        if (isOpen()) { return; }
        opener = document.activeElement;
        // 元の場所に、同じ高さ・同じ並び方の空の箱を置いてから移す（ページの高さを変えない）
        spacer = document.createElement('div');
        spacer.className = 'right-sidebar-spacer';
        spacer.setAttribute('aria-hidden', 'true');
        // 【並び方は右の枠から写す】（直した不具合）前は '1 1 200px' と決め打ちで、記事以外のページ（右の枠を
        // flex-basis: 100% で下へ押し出している）では、空の箱が本文の横に並んでしまい、「右の枠は横にいる」と判断して開いた直後に閉じていた
        var cs = window.getComputedStyle(sidebar);
        spacer.style.flex = cs.flexGrow + ' ' + cs.flexShrink + ' ' + cs.flexBasis;
        spacer.style.height = sidebar.offsetHeight + 'px';
        layout.insertBefore(spacer, sidebar);
        body.appendChild(sidebar);

        backdrop.hidden = false;
        drawer.inert = false;
        drawer.classList.add('is-open');
        tab.setAttribute('aria-expanded', 'true');
        closeBtn.focus({ preventScroll: true });
    }

    function close(returnFocus) {
        if (!isOpen()) { return; }
        drawer.classList.remove('is-open');
        drawer.inert = true;
        backdrop.hidden = true;
        tab.setAttribute('aria-expanded', 'false');
        if (spacer && spacer.parentNode) {
            spacer.parentNode.replaceChild(sidebar, spacer);
        }
        spacer = null;
        if (returnFocus) {
            var back = (opener && document.contains(opener) && opener !== document.body) ? opener : tab;
            if (back && back.focus) { back.focus({ preventScroll: true }); }
        }
    }

    function update() {
        var below = isBelow();
        var was = root.classList.contains('vb-side-below');
        root.classList.toggle('vb-side-below', below);
        if (!below) {
            close(false);
        }
        // ボタンが出入りすると、ヘッダーに入るかが変わる（Scripts/header-fit.js）
        if (was !== below && tab && window.vbHeaderFit) {
            window.vbHeaderFit();
        }
    }

    var header = document.querySelector('.page-wrapper > header');
    if (header && hasContent()) {
        build(header);
    }

    update();
    if (window.ResizeObserver) {
        // 本文の列も見る（右の枠が回ると、列の幅だけが大きく変わる）
        var ro = new window.ResizeObserver(update);
        ro.observe(layout);
        ro.observe(main);
    }
    window.addEventListener('resize', update);

})();
