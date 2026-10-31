/*
    ログを調べる道具（/account/logs・/account/logs/view。サイト管理者だけ）

    探す画面（Admin/LogSearch.aspx）と再生の画面（Admin/LogReplay.aspx）が読む。
    どちらの画面かは、要素があるかで見分ける（無ければ、その部分は何もしない）。

    1. 探す画面の「▼」（詳しい条件を開け閉めする。Gmail の検索の▼と同じ）
    2. 再生の画面
       2-1. 探していた行を、ログの枠の真ん中へ
       2-2. こっそりも混ぜる（スイッチ）＝住所を変えて開き直す
       2-3. もっと前／もっと後（WebMethod の LoadMore。ページは読み込み直さない＝LINE でさかのぼるのと同じ）
       2-4. 吹き出しを押すと小窓（IP・ポート番号・トリップ・アカウントID・ブラウザの印・ミリ秒までの時刻）
       2-5. 在室者（狭い画面ではボタンで開く）・探す画面へ戻る

    【文字は textContent で入れる】小窓の値は、行の data-* に入っている（サーバーが属性としてエスケープしたもの）。
    innerHTML に混ぜないこと（名前・本文は利用者が決めた文字）。
    「もっと前／もっと後」の HTML だけは innerHTML で入れるが、それはサーバー（Admin/LogToolView.vb）が値を1つずつエスケープして組み立てたもの。

    【このファイルは配信される】まだ塞いでいない穴のことは書かない。
*/
(function () {
    'use strict';

    var SEARCH_URL = '/account/logs';
    var REPLAY_URL = '/account/logs/view';


    /* ============================================================
       1. 探す画面の「▼」
       ============================================================ */
    (function setupSearchToggle() {

        var toggle = document.getElementById('logsearchToggle');
        var detail = document.getElementById('logsearchDetail');

        if (!toggle || !detail) {
            return;
        }

        toggle.addEventListener('click', function () {

            var open = toggle.getAttribute('aria-expanded') !== 'true';

            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');

            if (open) {
                detail.removeAttribute('hidden');

                /* 開いたら、最初の欄へ（続けて打てるように＝基本ルール U） */
                var first = detail.querySelector('input, select');
                if (first) {
                    first.focus();
                }
            } else {
                detail.setAttribute('hidden', 'hidden');
            }
        });
    })();


    /* ============================================================
       2. 再生の画面
       ============================================================ */
    var rowsEl = document.getElementById('logviewRows');
    var logEl = document.getElementById('logviewLog');

    if (!rowsEl || !logEl) {
        return;
    }

    var centerId = rowsEl.getAttribute('data-center') || '0';
    var includePrivate = rowsEl.getAttribute('data-private') === '1';
    var pair = rowsEl.getAttribute('data-pair') || '';
    var moreUrl = rowsEl.getAttribute('data-more-url') || '';
    var exportBase = rowsEl.getAttribute('data-export-url') || '';
    var statusEl = document.getElementById('logviewStatus');
    var csvLink = document.getElementById('logviewCsv');


    function setStatus(text) {
        if (statusEl) {
            statusEl.textContent = text || '';
        }
    }


    /* ------------------------------------------------------------
       2-1. 探していた行を、ログの枠の真ん中へ
       ------------------------------------------------------------ */
    function centerRow() {

        var row = rowsEl.querySelector('.logview-row.is-center');

        if (!row) {
            return;
        }

        /* 枠（.logview-log。position: relative）からの位置で計算する */
        var top = row.offsetTop - (logEl.clientHeight / 2) + (row.offsetHeight / 2);
        logEl.scrollTop = Math.max(0, top);
    }

    centerRow();

    /* アイコンの絵（icon-sprite.js があとから差し込む）で行の高さが変わることがあるので、読み終わったらもう一度 */
    window.addEventListener('load', centerRow);


    /* ------------------------------------------------------------
       2-2. こっそりも混ぜる
       ------------------------------------------------------------ */
    var privateSwitch = document.getElementById('logviewPrivate');

    if (privateSwitch) {
        privateSwitch.addEventListener('change', function () {

            /* 切ったら「この2人だけ」も外れる（2人だけ＝こっそりを混ぜた形の、さらに絞ったもの） */
            window.location.href = REPLAY_URL + '?m=' + encodeURIComponent(centerId) + (privateSwitch.checked ? '&p=1' : '');
        });
    }


    /* ------------------------------------------------------------
       2-3. もっと前／もっと後
       ------------------------------------------------------------ */
    function updateExportLink() {

        if (!csvLink || !exportBase) {
            return;
        }

        csvLink.href = exportBase +
            '&from=' + encodeURIComponent(rowsEl.getAttribute('data-first') || centerId) +
            '&to=' + encodeURIComponent(rowsEl.getAttribute('data-last') || centerId);
    }

    /* 今出ている行のうち、いちばん後ろの日付（もっと後で、同じ日付の区切りを2回出さないため） */
    function lastDateText() {

        var dates = rowsEl.querySelectorAll('.logview-date');

        return dates.length ? (dates[dates.length - 1].getAttribute('data-date') || '') : '';
    }

    function loadMore(button) {

        var older = button.getAttribute('data-dir') === 'older';
        var edge = rowsEl.getAttribute(older ? 'data-first' : 'data-last') || centerId;

        if (!moreUrl || button.disabled) {
            return;
        }

        button.disabled = true;
        setStatus('読み込んでいます…');

        window.fetch(moreUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({
                m: Number(centerId),
                older: older,
                edge: Number(edge),
                p: includePrivate,
                pair: pair,
                lastDate: older ? '' : lastDateText()
            })
        }).then(function (res) {

            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }

            return res.json();

        }).then(function (json) {

            var reply = json && json.d;

            if (!reply || !reply.ok) {
                setStatus((reply && reply.message) || '読み込めませんでした。');
                button.disabled = false;
                return;
            }

            if (older) {
                prependRows(reply.html || '');
                rowsEl.setAttribute('data-first', reply.edge);
            } else {
                rowsEl.insertAdjacentHTML('beforeend', reply.html || '');
                rowsEl.setAttribute('data-last', reply.edge);
            }

            updateExportLink();

            setStatus(reply.count > 0 ? (reply.count + '件を読み込みました。') : 'これより' + (older ? '前' : '後') + 'の記録はありません。');

            if (reply.hasMore) {
                button.disabled = false;
            } else {
                /* もう無い。押せる場所を残さない（ボタンにカーソルがあったら、ログの枠へ） */
                if (document.activeElement === button) {
                    logEl.setAttribute('tabindex', '-1');
                    logEl.focus();
                }
                button.hidden = true;
            }

        }).catch(function (err) {

            if (window.console) {
                console.warn('[ログを調べる] 読み込めませんでした。', err);
            }

            setStatus('読み込めませんでした（通信が途切れたかもしれません）。もう一度押してください。');
            button.disabled = false;
        });
    }

    /* 上に足す（読んでいる所を動かさない＝今いちばん上にある行が、足す前と同じ高さに見えるよう、スクロールの位置を下げる）

       【scrollHeight の差では合わせない】行が少なくて枠に余白があるうちは、scrollHeight は枠の高さのままなので、
       差が足した高さより小さく出て、読んでいた行が下へずれる（6行の部屋で 195px ずれた）。
       足す前からある行（アンカー）の、画面の上での位置を前後で比べる */
    function prependRows(html) {

        var oldFirst = rowsEl.firstElementChild;
        var anchor = rowsEl.querySelector('.logview-row');
        var anchorTop = anchor ? anchor.getBoundingClientRect().top : 0;

        rowsEl.insertAdjacentHTML('afterbegin', html);

        /* 足した行の最後の日付と、もとの頭の日付の区切りが同じなら、もとの頭の区切りを消す（同じ日付が2回並ばないように） */
        if (oldFirst && oldFirst.classList.contains('logview-date')) {

            var prev = oldFirst.previousElementSibling;

            while (prev && !prev.classList.contains('logview-date')) {
                prev = prev.previousElementSibling;
            }

            if (prev && prev.getAttribute('data-date') === oldFirst.getAttribute('data-date')) {
                oldFirst.parentNode.removeChild(oldFirst);
            }
        }

        if (anchor) {
            logEl.scrollTop += (anchor.getBoundingClientRect().top - anchorTop);
        }
    }

    Array.prototype.forEach.call(document.querySelectorAll('.logview-more'), function (button) {
        button.addEventListener('click', function () {
            loadMore(button);
        });
    });


    /* ------------------------------------------------------------
       2-4. 吹き出しを押すと小窓
       ------------------------------------------------------------ */
    var pop = document.getElementById('logviewPop');
    var popList = document.getElementById('logviewPopList');
    var popLinks = document.getElementById('logviewPopLinks');
    var popClose = document.getElementById('logviewPopClose');
    var popOwner = null;

    /* 小窓に出す項目（data-* の名前・項目名・等幅で出すか） */
    var POP_FIELDS = [
        ['kind', '種類', false],
        ['time', '日時', true],
        ['room', '部屋', false],
        ['name', '名前', false],
        ['to-name', '宛先', false],
        ['trip', 'トリップ', true],
        ['account', 'アカウントID', false],
        ['ip', 'IPアドレス', true],
        ['port', 'ポート番号', true],
        ['browser', 'ブラウザの印', true],
        ['to-browser', '宛先のブラウザの印', true],
        ['text', '本文', false]
    ];

    function data(el, name) {
        return el.getAttribute('data-' + name) || '';
    }

    function addLink(text, href, newTab) {

        var a = document.createElement('a');
        a.textContent = text;
        a.href = href;

        if (newTab) {
            a.target = '_blank';
            a.rel = 'noopener';
        }

        popLinks.appendChild(a);
    }

    function openPop(hit) {

        if (!pop) {
            return;
        }

        popList.textContent = '';
        popLinks.textContent = '';

        var lines = [];

        POP_FIELDS.forEach(function (field) {

            var value = data(hit, field[0]);

            if (!value && field[0] !== 'ip' && field[0] !== 'port') {
                return;
            }

            var dt = document.createElement('dt');
            var dd = document.createElement('dd');

            dt.textContent = field[1];
            dd.textContent = value || '（記録なし）';

            if (field[2]) {
                dd.className = 'is-code';
            }

            popList.appendChild(dt);
            popList.appendChild(dd);

            lines.push(field[1] + '：' + (value || '（記録なし）'));
        });

        /* 調べ続けるためのリンク（どれも探す画面へ。期間は付けない＝保存期間の全部から） */
        var browser = data(hit, 'browser');
        var ip = data(hit, 'ip');
        var trip = data(hit, 'trip');
        var accountName = data(hit, 'account-name');
        var toBrowser = data(hit, 'to-browser');
        var image = data(hit, 'image');

        if (browser) {
            addLink('この人（ブラウザの印）の記録', SEARCH_URL + '?sender=' + encodeURIComponent(browser));
        }
        if (ip) {
            addLink('このIPの記録', SEARCH_URL + '?ip=' + encodeURIComponent(ip));
        }
        if (trip) {
            addLink('このトリップの記録', SEARCH_URL + '?trip=' + encodeURIComponent(trip));
        }
        if (accountName) {
            addLink('このアカウントIDの記録', SEARCH_URL + '?acct=' + encodeURIComponent(accountName));
        }
        if (browser && toBrowser) {
            addLink('この2人のこっそりだけ',
                REPLAY_URL + '?m=' + encodeURIComponent(centerId) + '&p=1&pair=' + encodeURIComponent(browser + ',' + toBrowser));
        }
        if (image) {
            addLink('画像を開く', image, true);
        }

        /* まとめてコピー（問い合わせに答えるとき、そのまま貼れるように） */
        var copy = document.createElement('button');
        copy.type = 'button';
        copy.textContent = 'まとめてコピー';
        copy.addEventListener('click', function () {
            copyText(lines.join('\n'), copy);
        });
        popLinks.appendChild(copy);

        popOwner = hit;
        pop.hidden = false;
        placePop(hit);

        pop.setAttribute('tabindex', '-1');
        pop.focus();
    }

    /* 押した行のそば（下。入らなければ上）。画面の端からはみ出さないように */
    function placePop(hit) {

        var rect = hit.getBoundingClientRect();
        var w = pop.offsetWidth;
        var h = pop.offsetHeight;
        var margin = 8;

        var top = rect.bottom + 6;

        if (top + h > window.innerHeight - margin) {
            top = rect.top - h - 6;
        }

        top = Math.max(margin, Math.min(top, window.innerHeight - h - margin));

        var left = Math.max(margin, Math.min(rect.left + 40, window.innerWidth - w - margin));

        pop.style.top = top + 'px';
        pop.style.left = left + 'px';
    }

    function closePop(returnFocus) {

        if (!pop || pop.hidden) {
            return;
        }

        pop.hidden = true;

        if (returnFocus && popOwner) {
            popOwner.focus();
        }

        popOwner = null;
    }

    function copyText(text, button) {

        var done = function () {
            button.textContent = 'コピーしました';
            window.setTimeout(function () {
                button.textContent = 'まとめてコピー';
            }, 2000);
        };

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, function () {
                fallbackCopy(text);
                done();
            });
        } else {
            fallbackCopy(text);
            done();
        }
    }

    function fallbackCopy(text) {

        var area = document.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', 'readonly');
        area.style.position = 'fixed';
        area.style.top = '-1000px';
        document.body.appendChild(area);
        area.select();

        try {
            document.execCommand('copy');
        } catch (e) {
            /* 写せなくても、画面に値は出ている */
        }

        document.body.removeChild(area);
    }

    rowsEl.addEventListener('click', function (e) {

        var hit = e.target.closest ? e.target.closest('.logview-hit') : null;

        if (!hit) {
            return;
        }

        if (popOwner === hit && !pop.hidden) {
            closePop(true);
            return;
        }

        openPop(hit);
    });

    if (popClose) {
        popClose.addEventListener('click', function () {
            closePop(true);
        });
    }

    document.addEventListener('keydown', function (e) {
        if ((e.key === 'Escape' || e.key === 'Esc') && pop && !pop.hidden) {
            closePop(true);
        }
    });

    /* 小窓の外を押したら閉じる（行を押したときは、上の click が開き直す） */
    document.addEventListener('mousedown', function (e) {

        if (!pop || pop.hidden) {
            return;
        }

        if (pop.contains(e.target) || (e.target.closest && e.target.closest('.logview-hit'))) {
            return;
        }

        closePop(false);
    });

    /* ログの枠を動かしたら、押した行から離れるので閉じる */
    logEl.addEventListener('scroll', function () {
        closePop(false);
    });

    window.addEventListener('resize', function () {
        if (popOwner && pop && !pop.hidden) {
            placePop(popOwner);
        }
    });


    /* ------------------------------------------------------------
       2-5. 在室者（狭い画面のボタン）・探す画面へ戻る
       ------------------------------------------------------------ */
    var peopleBtn = document.getElementById('logviewPeopleBtn');
    var people = document.getElementById('logviewPeople');

    if (peopleBtn && people) {
        peopleBtn.addEventListener('click', function () {

            var open = !people.classList.contains('is-open');

            people.classList.toggle('is-open', open);
            peopleBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }

    /* 探す画面から来たなら「戻る」にする（条件とページを保ったまま戻れる） */
    var back = document.getElementById('logviewBack');

    if (back && document.referrer) {

        var from = '';

        try {
            from = new URL(document.referrer).pathname;
        } catch (e) {
            from = '';
        }

        if (document.referrer.indexOf(window.location.origin + '/') === 0 && from.toLowerCase() === SEARCH_URL) {
            back.addEventListener('click', function (e) {
                e.preventDefault();
                window.history.back();
            });
        }
    }

})();
