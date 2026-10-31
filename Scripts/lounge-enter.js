/*
 * ラウンジの入室ボタンと、パスワードの小窓
 *
 * 15-2a より前は、入室ボタンの処理が Chat/Lounge.aspx の中（インライン）にあった。
 * パスワードの小窓と一緒に置くため、ここへ出した（ページの中に書くと、開くたびに毎回配信される）。
 *
 * 【流れ】
 *   1. 入室ボタンを押す → WebMethod（Lounge.aspx/EnterRoom）を password: null で呼ぶ
 *   2. 返事が ok        → /chat/room へ移る
 *      needPassword     → 小窓を出して、パスワードを聞く（待たせている間なら、残りの秒数も出す）
 *      それ以外         → 理由を警告（showWarning）で出す
 *   3. 小窓で「入室する」→ 同じ WebMethod を、打った言葉で呼び直す
 *
 * 【どの部屋に鍵があるかを、画面で決めない】
 * 一覧の鍵の印（.room-lock-icon）を見て小窓を出す形にはしていない。
 * 聞くかどうかは、いつもサーバーの返事（needPassword）で決める。
 * 印は見た目のためのもので、古い一覧（F5 していない画面）でも、正しく小窓が出る。
 *
 * 【ここの照合は親切であって、防御ではない】最後に断るのは ChatHub.JoinRoom（ChatHub.RoomKeys.vb）
 *
 * 【パスワードをどこにも残さない】打った言葉は、この小窓の入力欄と、1回の POST にしか乗らない。
 * sessionStorage にも Cookie にも置かない（部屋の画面は、サーバーの「鍵を通った印」で入れる）
 *
 * 【小窓の見た目は、確認のダイアログ（Scripts/site.js の vbChatConfirm）の部品を使い回す】
 * .custom-confirm-* の class をそのまま使い、足りない部分（入力欄・理由の1行・残りの秒数）だけ
 * Content/Site.css の「パスワードの小窓」で足してある。
 * 開き方・閉じ方の作法（Tab を中で回す・Esc で閉じる・閉じたら押したボタンへ戻す）も同じにしてある
 *
 * 【、ノックの入口と「入室が許可されました」の小窓も持つ】
 *   ・「ノック」のボタン（data-mode="knock"）を押す → lounge-live.js の VbChatLoungeLive.knock
 *   ・管理人が許可した → lounge-live.js が openKnockApproved を呼ぶ → 小窓（30秒の数え下げ）
 *   ・小窓で「入室する」→ いつもの入室（startEnter）。パスワード付きの部屋なら、ここでパスワードの小窓も通る
 *     （管理人が許すのは「席を空ける」ことで、「鍵を開ける」ことではない）
 *
 * 読み込む順番：jQuery → site.js（showWarning）→ このファイル。Chat/Lounge.aspx のいちばん下
 */
(function () {
    'use strict';

    var ENTER_URL = '/Chat/Lounge.aspx/EnterRoom';

    // パスワードの文字数の上限（サーバーの RoomPassword.MaxLength。画面に数字を書かない）
    var maxLengthField = document.getElementById('roomPasswordMaxLength');
    var PASSWORD_MAX_LENGTH = maxLengthField ? (parseInt(maxLengthField.value, 10) || 32) : 32;

    // 入室の問い合わせを投げている間は、次を投げない（ボタンの連打で2回移動しないため）
    var entering = false;


    // WebMethod を呼ぶ。返事は JSON の文字列（response.d）で来るので、ここで読む。
    // 読めなかったときは null を渡す（呼び出し側が「通信エラー」と同じに扱う）
    function requestEnter(roomId, password) {

        return $.ajax({
            type: 'POST',
            url: ENTER_URL,
            data: JSON.stringify({ roomId: roomId, password: password }),
            contentType: 'application/json; charset=utf-8',
            dataType: 'json'
        }).then(function (response) {

            var result = null;

            try {
                result = JSON.parse(response.d);
            } catch (parseError) {
                // 下で null として扱う
            }

            return result;
        });
    }


    function goToRoom() {
        window.location.href = '/chat/room';
    }


    // ============================================
    //  入室ボタン
    // ============================================
    //
    // btn-enter のボタン全部に、まとめて登録する（document に委ねる）。
    // 一覧は Repeater が作るうえ、誰かが部屋を作ると lounge-live.js があとから足すので、
    // 個別に登録すると、あとから足した部屋のボタンが効かない
    //
    // 【ボタンの形は4つ】（形を決めるのは lounge-live.js の applyEnterButton）
    //   入室（ふつう）… startEnter
    //   入室（灰色）  … 満員の知らせだけ
    //   ノック        … data-mode="knock"。ノックする
    //   返事待ち      … data-mode="waiting"（灰色）。待ってもらう
    //   入室中        … data-mode="mine"。自分が別のタブで入っている部屋。startEnter へ
    //                   （サーバーの EnterRoom はパスワードを聞き直さず、部屋の JoinRoom は同じ部屋の2窓目として置き換える）
    $(document).on('click', '.btn-enter', function (e) {

        // ブラウザ標準の「#へ移動する」動作を止める
        e.preventDefault();

        var button = this;
        var roomId = parseInt(button.getAttribute('data-room-id'), 10);
        var mode = button.getAttribute('data-mode');

        if (mode === 'waiting') {
            showWarning('ノックしてあります。管理人の返事を待ってください。');
            return;
        }

        if (mode === 'knock') {

            // ノックは SignalR で送る（返事をその場で受け取るため）。
            // lounge-live.js が読めていない・繋がる仕組みが無いときは、ノックできない
            if (window.VbChatLoungeLive) {
                window.VbChatLoungeLive.knock(roomId, button);
            } else {
                showWarning('今はノックできません。ページを読み込み直してから、もう一度お試しください。');
            }
            return;
        }

        // 満室（灰色）なら入らない。
        // 灰色にするのは RoomList.ascx.vb と lounge-live.js で、届いた人数で判定しているだけ。
        // 【これは親切であって、防御ではない】本当に断るのはハブの JoinRoom の定員の判定。
        // ここを通り抜けても（数字が古かった、など）、部屋の側で「満員です」と断られてラウンジへ戻る
        if (button.getAttribute('aria-disabled') === 'true') {
            showWarning('この部屋は満員です。空くまでお待ちください。');
            return;
        }

        startEnter(roomId, button);
    });


    // 入室する（入室ボタンと、ノックを許可されたあとの「入室する」で共通）
    //
    // opener … 押されたボタン（パスワードの小窓を閉じたら、ここへフォーカスを戻す。無ければ null）
    function startEnter(roomId, opener) {

        if (entering) {
            return;
        }

        var button = opener;

        entering = true;

        // Friendly URL（/chat/lounge）経由だと PageMethods の自動URL生成が
        // 404になってしまうため、物理パスを直接指定して $.ajax で呼び出す
        requestEnter(roomId, null).done(function (result) {

            entering = false;

            if (result && result.ok) {
                entering = true;   // 移動するまで、ほかのボタンを効かせない
                goToRoom();
                return;
            }

            if (result && result.needPassword) {
                openPasswordDialog(roomId, result, button);
                return;
            }

            showWarning((result && result.message) || '入室できませんでした。');

        }).fail(function () {
            entering = false;
            showWarning('通信エラーが発生しました。もう一度お試しください。');
        });
    }


    // ============================================
    //  パスワードの小窓
    // ============================================
    //
    // roomId      … 入ろうとしている部屋
    // firstResult … 最初の問い合わせの返事（部屋名・待たせている秒数が入っている）
    // opener      … 押された入室ボタン（閉じたら、ここへフォーカスを戻す）
    //
    //   ┌───────────────────────┐
    //   │ パスワードを入れてください   │
    //   │ 「部屋名」は、パスワード付きの部屋です。│
    //   │ （断られた理由。赤）            │ ← 入力欄の「上」（スマホのキーボードに隠れないため。教訓）
    //   │ （あと N 秒）                   │
    //   │ [ パスワード          ]        │
    //   │             [やめる] [入室する] │
    //   └───────────────────────┘
    function openPasswordDialog(roomId, firstResult, opener) {

        // 二重に開かない（確認のダイアログと同じ）
        if (document.querySelector('.custom-confirm-overlay')) {
            return;
        }

        var overlay = document.createElement('div');
        overlay.className = 'custom-confirm-overlay';

        var box = document.createElement('div');
        box.className = 'custom-confirm room-password-dialog';

        // 入力を求める小窓なので alertdialog ではなく dialog。aria-modal で「後ろは操作できない」
        box.setAttribute('role', 'dialog');
        box.setAttribute('aria-modal', 'true');
        box.setAttribute('aria-labelledby', 'roomPasswordDialogTitle');
        box.setAttribute('aria-describedby', 'roomPasswordDialogBody');

        var title = document.createElement('p');
        title.className = 'custom-confirm-title';
        title.id = 'roomPasswordDialogTitle';
        title.textContent = 'パスワードを入れてください';
        box.appendChild(title);

        // 部屋名は、サーバーの返事から textContent で入れる（HTML として解釈させない）
        var body = document.createElement('div');
        body.className = 'custom-confirm-body';
        body.id = 'roomPasswordDialogBody';

        var line = document.createElement('p');
        line.textContent = '「' + ((firstResult && firstResult.roomName) || 'この部屋') + '」は、パスワード付きの部屋です。';
        body.appendChild(line);
        box.appendChild(body);

        // 断られた理由（パスワードが違う・待ってほしい）。変わった瞬間に読み上げる。
        // 空のときは高さを持たない（CSS の :empty）
        var error = document.createElement('p');
        error.className = 'room-password-error';
        error.setAttribute('aria-live', 'polite');
        box.appendChild(error);

        // 残りの秒数。**aria-live を付けない**（1秒ごとに読み上げられて邪魔になる）。
        // 待たせ始めた・待ち終わったは、上の理由の1行で1回ずつ伝える
        var countdown = document.createElement('p');
        countdown.className = 'room-password-countdown';
        countdown.hidden = true;
        box.appendChild(countdown);

        var label = document.createElement('label');
        label.className = 'visually-hidden';
        label.setAttribute('for', 'roomPasswordDialogInput');
        label.textContent = 'パスワード';
        box.appendChild(label);

        // type="text" にしてある理由は、部屋を作るエリアの入力欄と同じ（Chat/Lounge.aspx のコメント）
        var input = document.createElement('input');
        input.type = 'text';
        input.id = 'roomPasswordDialogInput';
        input.className = 'room-password-input room-password-dialog-input';
        input.maxLength = PASSWORD_MAX_LENGTH;
        input.placeholder = 'パスワード';
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('autocapitalize', 'off');
        input.setAttribute('autocorrect', 'off');
        input.setAttribute('spellcheck', 'false');
        box.appendChild(input);

        var actions = document.createElement('div');
        actions.className = 'custom-confirm-actions';

        // 安全な側を先に（左）。確認のダイアログと同じ並び
        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'custom-confirm-btn custom-confirm-btn--cancel';
        cancelBtn.textContent = 'やめる';

        var okBtn = document.createElement('button');
        okBtn.type = 'button';
        okBtn.className = 'custom-confirm-btn custom-confirm-btn--ok';
        okBtn.textContent = '入室する';

        actions.appendChild(cancelBtn);
        actions.appendChild(okBtn);
        box.appendChild(actions);
        overlay.appendChild(box);
        document.body.appendChild(overlay);

        var busy = false;
        var closed = false;
        var countdownTimer = null;
        var waitUntil = 0;

        // Tab を回す相手（この順番で回る）
        var focusables = [input, cancelBtn, okBtn];

        // 最初のフォーカスは入力欄（確認のダイアログと違って、取り返しのつかない操作ではない。
        // 開いた目的が「打つこと」なので、すぐ打てるようにする）
        input.focus();

        function close() {

            if (closed) {
                return;
            }

            closed = true;
            stopCountdown();
            document.removeEventListener('keydown', onKeyDown, true);
            overlay.parentNode.removeChild(overlay);

            // 押したボタンへ戻す（一覧が描き直されて、もう文書に無ければ戻さない）
            if (opener && document.body.contains(opener) && opener.focus) {
                opener.focus();
            }
        }

        function setError(text) {
            error.textContent = text || '';
        }

        function updateOkButton() {
            okBtn.disabled = busy || waitUntil > Date.now();
        }

        function stopCountdown() {
            if (countdownTimer !== null) {
                clearInterval(countdownTimer);
                countdownTimer = null;
            }
        }

        // 待たせる（seconds 秒）。サーバーの文言は理由の1行へ1回だけ、残りの秒数は別の行で数え下げる
        function startCountdown(seconds) {

            stopCountdown();

            waitUntil = Date.now() + seconds * 1000;

            var tick = function () {

                var remaining = Math.ceil((waitUntil - Date.now()) / 1000);

                if (remaining <= 0) {
                    stopCountdown();
                    waitUntil = 0;
                    countdown.hidden = true;
                    countdown.textContent = '';
                    setError('もう一度入れられます。');
                    updateOkButton();
                    return;
                }

                countdown.hidden = false;
                countdown.textContent = 'あと ' + remaining + ' 秒';
            };

            tick();
            countdownTimer = setInterval(tick, 1000);
            updateOkButton();
        }

        // 返事（needPassword のもの）を小窓に反映する
        function applyResult(result) {

            if (result.message) {
                setError(result.message);
            }

            if (result.retryAfter > 0) {
                startCountdown(result.retryAfter);
            }
        }

        function submit() {

            if (busy || waitUntil > Date.now()) {
                return;
            }

            // 空のときは、サーバーへ行かずにここで知らせる（サーバーも同じ判定をする。数えない）
            if (!input.value || !input.value.trim()) {
                setError('パスワードを入れてください。');
                input.focus();
                return;
            }

            busy = true;
            updateOkButton();

            // 前の理由を、送る前に一度消す。
            // 【消さないと、同じ「パスワードが違います。」が続いたとき、2度目から読み上げられない】
            // （aria-live は中身が変わったときにだけ読む）
            setError('');

            requestEnter(roomId, input.value).done(function (result) {

                if (closed) {
                    return;
                }

                if (result && result.ok) {
                    // 移動するまでボタンは戻さない（その一瞬に、もう一度押せてしまうため）
                    goToRoom();
                    return;
                }

                busy = false;

                if (result && result.needPassword) {

                    applyResult(result);
                    updateOkButton();

                    // 打ち直しやすいように、入力欄の文字をまとめて選んでおく
                    input.focus();
                    input.select();
                    return;
                }

                // パスワードの話ではない理由（部屋が閉じた、など）。小窓を閉じて、ページの警告で出す
                close();
                showWarning((result && result.message) || '入室できませんでした。');

            }).fail(function () {

                if (closed) {
                    return;
                }

                busy = false;
                updateOkButton();
                setError('通信エラーが発生しました。もう一度お試しください。');
            });
        }

        function onKeyDown(e) {

            if (e.key === 'Escape') {
                e.preventDefault();
                close();
                return;
            }

            // 入力欄で Enter を押したら、入室する。
            // 【日本語入力の変換を確定する Enter は除く】（落とし穴）
            if (e.key === 'Enter' && e.target === input) {

                if (e.isComposing || e.keyCode === 229) {
                    return;
                }

                e.preventDefault();
                submit();
                return;
            }

            // 【Tab を、この小窓の中だけで回す】回さないと、後ろのページのボタンへ抜ける
            if (e.key !== 'Tab') {
                return;
            }

            e.preventDefault();

            // 押せないボタン（待っている間の「入室する」）は飛ばす
            var usable = focusables.filter(function (el) { return !el.disabled; });
            var index = usable.indexOf(document.activeElement);
            var next = e.shiftKey
                ? (index <= 0 ? usable.length - 1 : index - 1)
                : (index + 1) % usable.length;

            usable[next].focus();
        }

        // capture で拾うのは、後ろのページのキー操作より先に受け取るため
        document.addEventListener('keydown', onKeyDown, true);

        // 下敷きそのものを押したときだけ閉じる（中の箱を押しても閉じない）
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                close();
            }
        });

        cancelBtn.addEventListener('click', close);
        okBtn.addEventListener('click', submit);

        // 開いた時点で待たせている（前に間違えすぎた）なら、それも出す
        applyResult(firstResult || {});
    }


    // ============================================
    //  「入室が許可されました」の小窓
    // ============================================
    //
    //   ┌──────────────────────────┐
    //   │ 入室が許可されました                  │
    //   │ 「部屋名」の管理人が、入室を許可しました。│
    //   │ 席を取り置いています（あと 30 秒）      │ ← aria-live を付けない（1秒ごとに読ませない）
    //   │                  [やめる] [入室する]  │
    //   └──────────────────────────┘
    //
    // 【alertdialog にしてある】ノックした人が別のことをしていても、許可されたことに気づけるように。
    // 開いた瞬間に、題と本文が読み上げられる（aria-labelledby ／ aria-describedby）。
    // 【最初のフォーカスは「入室する」】確認のダイアログ（取り返しのつかない操作）と違い、ここの目的は入ること。
    // 【やめる・Esc・下敷き】席を手放す（管理人の側の取り置きが、その場で空く）。
    // 【時間切れ】小窓を閉じる。取り置きはサーバーが外し、「外れました」を知らせてくる
    //
    // 【ほかの小窓（パスワード・ログアウトの確認）が開いているとき】重ねない。
    // 警告だけ出す（ボタンは「ノック」のままなので、押せばこの小窓が出し直される）
    var approvedDialog = null;

    function openKnockApproved(info, opener) {

        if (!info) {
            return;
        }

        var roomId = info.roomId;
        var seconds = Math.max(1, info.seconds || 0);
        var roomName = info.roomName || 'この部屋';

        // 同じ部屋の小窓が開いていれば、閉じてから開き直す（秒数を新しくする）
        if (approvedDialog) {
            approvedDialog.close(false);
        }

        if (document.querySelector('.custom-confirm-overlay')) {
            showWarning('「' + roomName + '」への入室が許可されました。ノックのボタンを押すと、入室できます。');
            return;
        }

        // 閉じたら焦点を戻す先。サーバーからの知らせで開いたとき（opener が無い）は、その部屋のボタンへ
        // （開く前の焦点がページのどこにも無い＝body のことが多く、そこへ戻すと居場所を見失う）
        var roomButton = document.querySelector('.room-item[data-room-id="' + roomId + '"] .btn-enter');
        var previousFocus = opener || roomButton || document.activeElement;

        var overlay = document.createElement('div');
        overlay.className = 'custom-confirm-overlay';

        var box = document.createElement('div');
        box.className = 'custom-confirm knock-approved-dialog';
        box.setAttribute('role', 'alertdialog');
        box.setAttribute('aria-modal', 'true');
        box.setAttribute('aria-labelledby', 'knockApprovedTitle');
        box.setAttribute('aria-describedby', 'knockApprovedBody');

        var title = document.createElement('p');
        title.className = 'custom-confirm-title';
        title.id = 'knockApprovedTitle';
        title.textContent = '入室が許可されました';
        box.appendChild(title);

        var body = document.createElement('div');
        body.className = 'custom-confirm-body';
        body.id = 'knockApprovedBody';

        var line = document.createElement('p');
        line.textContent = '「' + roomName + '」の管理人が、入室を許可しました。';
        body.appendChild(line);
        box.appendChild(body);

        // 残りの秒数（読み上げさせない。上の本文だけで用が足りる）
        var countdown = document.createElement('p');
        countdown.className = 'room-password-countdown';
        box.appendChild(countdown);

        var actions = document.createElement('div');
        actions.className = 'custom-confirm-actions';

        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'custom-confirm-btn custom-confirm-btn--cancel';
        cancelBtn.textContent = 'やめる';

        var okBtn = document.createElement('button');
        okBtn.type = 'button';
        okBtn.className = 'custom-confirm-btn custom-confirm-btn--ok';
        okBtn.textContent = '入室する';

        actions.appendChild(cancelBtn);
        actions.appendChild(okBtn);
        box.appendChild(actions);
        overlay.appendChild(box);
        document.body.appendChild(overlay);

        var closed = false;
        var until = Date.now() + seconds * 1000;
        var timer = null;

        // release … 席を手放すか（やめる・Esc・下敷き＝true。入室する・時間切れ・サーバーの知らせ＝false）
        function close(release) {

            if (closed) {
                return;
            }

            closed = true;

            if (timer !== null) {
                clearInterval(timer);
                timer = null;
            }

            document.removeEventListener('keydown', onKeyDown, true);
            overlay.parentNode.removeChild(overlay);

            if (approvedDialog && approvedDialog.roomId === roomId) {
                approvedDialog = null;
            }

            if (release && window.VbChatLoungeLive) {
                window.VbChatLoungeLive.releaseKnockSeat(roomId);
            }

            if (previousFocus && document.body.contains(previousFocus) && previousFocus.focus) {
                previousFocus.focus();
            }
        }

        function tick() {

            var remaining = Math.ceil((until - Date.now()) / 1000);

            if (remaining <= 0) {
                close(false);
                return;
            }

            countdown.textContent = '席を取り置いています（あと ' + remaining + ' 秒）';
        }

        function onKeyDown(e) {

            if (e.key === 'Escape') {
                e.preventDefault();
                close(true);
                return;
            }

            if (e.key !== 'Tab') {
                return;
            }

            // 【Tab を、この小窓の中だけで回す】（ボタン2つ）
            e.preventDefault();

            if (document.activeElement === okBtn) {
                cancelBtn.focus();
            } else {
                okBtn.focus();
            }
        }

        document.addEventListener('keydown', onKeyDown, true);

        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                close(true);
            }
        });

        cancelBtn.addEventListener('click', function () {
            close(true);
        });

        okBtn.addEventListener('click', function () {

            // 閉じてから入る（パスワード付きなら、このあとパスワードの小窓が開く。重ねないため）。
            // フォーカスは戻さない（このあと移動するか、パスワードの小窓へ移る）
            previousFocus = null;
            close(false);
            startEnter(roomId, opener);
        });

        approvedDialog = { roomId: roomId, close: close };

        tick();
        timer = setInterval(tick, 1000);

        okBtn.focus();
    }


    // サーバーから「取り置きが外れた・取り消された」が届いた（lounge-live.js から）。
    // 開いていれば、席は手放さずに閉じる（もうサーバー側で外れている）
    function closeKnockApproved(roomId) {

        if (approvedDialog && approvedDialog.roomId === roomId) {
            approvedDialog.close(false);
        }
    }


    // lounge-live.js（ノックの返事を受け取る側）から呼ぶ口
    window.VbChatLoungeEnter = {
        startEnter: startEnter,
        openKnockApproved: openKnockApproved,
        closeKnockApproved: closeKnockApproved
    };

})();
