/* ============================================================
   プロフィールの編集の画面
   （ブログ型への作り替えの①。同じ日の夕方に、実機テストの注文で作り直した）
   ============================================================

   読むのは Accounts/Profile.aspx だけ。この1本で4つのことをしている。

     1. 知らせを、画面の下の「保存の帯」に1本化する
     2. 選ばれた画像を、canvas で縮めてから送る（アイコンは、そのとき真ん中を正方形に切る）
     3. スマホでキーボードが出たとき、帯が見えている範囲についてくる
     4. 保存のあと、読んでいた場所に戻る

   【1. 知らせを1本化した理由】（開発者の報告）
   直す前は、画像を差し替えた知らせだけが枠の下に薄い字で出て、
   画像を外した知らせは画面の上に青い帯で出ていた——指摘：ちぐはぐに見える。
   **保存した・画像を差し替えた・画像を外した・断った理由の4つとも、保存の帯に出す。**
   帯は画面の下に貼り付いていて、どこまでスクロールしても見えるので、置き場所としていちばんよい。

   【2. canvas を通す理由は、2つある】
     ① スマホで撮った写真は、そのままだと上限（2MB）を超える
     ② **canvas で描き直すと、撮った場所（GPS）などの EXIF が入らない**
        （サーバー側でも取り除いているので、これは二重の守り。Core/ImageMetadata.vb）

   【画像は、選んだ時点で差し替わる】
   「保存する」を押すまで待たない。入れ先（プロフィールのアイコン／カバー）が
   最初から決まっているので、その場で差し替えてしまえば**宙に浮いた画像が1枚も生まれない**
   （記事の画像は「どの記事のものか」が保存まで決まらないので、そちらは片付ける仕事が要る）。

   【このファイルが読み込めなくても、画面は使える】
   「画像を選ぶ」が効かなくなるだけで、ほかの項目の入力と保存は、そのまま動く
   （知らせも、サーバーが入れた文がそのまま帯に出る）。

   【記事の編集画面（Scripts/article-editor.js）と、縮め方・帯の仕掛けをそろえてある】
   **わざと写してある**——あちらは Quill と一体で、こちらは単独のページで動く。
   片方を直したときに、もう片方が黙って道連れにならないようにするため。
   **縮め方・帯の付き方を変えるときは、両方を見ること** */
(function () {

    'use strict';

    /* ============================================================
       1. 知らせ（保存の帯の中の2行）
       ============================================================ */

    var savedBox = document.getElementById('profileSavedStatus');
    var errorBox = document.getElementById('profileErrorStatus');

    function setText(box, text) {

        if (box) {
            box.textContent = text || '';
        }

    }

    /* うまくいった知らせ（断った理由は消す。2つ並ぶと、どちらが今の話か分からない） */
    function showDone(text) {
        setText(errorBox, '');
        setText(savedBox, text);
    }

    /* 断った理由（うまくいった知らせは消す） */
    function showError(text) {
        setText(savedBox, '');
        setText(errorBox, text);
    }

    /* 【開いた時点から入っている文は、読み上げソフトが読まない】（role="status" の決まり）
       サーバーが入れた文（「プロフィールを保存しました。」など）を、いったん空にしてから入れ直す。
       記事の編集画面（showSaveStatus）・Scripts/site.js の「6」と同じ手である */
    (function announceOnLoad() {

        if (!savedBox) {
            return;
        }

        var text = savedBox.textContent;

        if (!text) {
            return;
        }

        savedBox.textContent = '';

        window.setTimeout(function () {
            savedBox.textContent = text;
        }, 60);

    })();

    /* 【打ち直したら、うまくいった知らせを消す】
       出たままだと、そのあとの変更まで保存済みに見える（記事の編集画面の clearSavedStatus と同じ）。
       【画像の欄（type="file"）は数えない】画像を選ぶと、すぐ下でこちらが知らせを入れるため */
    (function clearOnEdit() {

        function onEdit(e) {

            var el = e.target;

            if (el && el.tagName === 'INPUT' && (el.type || '').toLowerCase() === 'file') {
                return;
            }

            if (savedBox && savedBox.textContent) {
                savedBox.textContent = '';
            }

        }

        document.addEventListener('input', onEdit);
        document.addEventListener('change', onEdit);

    })();


    /* ============================================================
       2. 画像（アイコン・カバー）を送る
       ============================================================ */

    (function setupImages() {

        var config = document.getElementById('profileImageConfig');

        if (!config) {
            return;
        }

        var UPLOAD_URL = config.getAttribute('data-upload-url') || '';
        var MAX_BYTES = parseInt(config.getAttribute('data-max-bytes'), 10) || 0;

        var EDGE = {
            avatar: parseInt(config.getAttribute('data-avatar-edge'), 10) || 512,
            cover: parseInt(config.getAttribute('data-cover-edge'), 10) || 1600
        };

        /* 画面の、どの部分を書き換えるか */
        var TARGET = {
            avatar: { frame: 'profileAvatarFrame', img: 'profileAvatarImg', label: 'アイコン画像' },
            cover: { frame: 'profileCoverFrame', img: 'profileCoverImg', label: 'カバー画像' }
        };

        /* 収まらなかったときに、順に試す縮め方（記事の画像・部屋の画像と同じ） */
        var RETRY_STEPS = [
            { edgeScale: 1.0, quality: 1.0 },
            { edgeScale: 1.0, quality: 0.75 },
            { edgeScale: 0.75, quality: 0.7 },
            { edgeScale: 0.5, quality: 0.65 },
            { edgeScale: 0.35, quality: 0.6 }
        ];

        /* 読み込むことすら試さない大きさ（端末を守る安全弁。記事の画像と同じ） */
        var MAX_SOURCE_BYTES = 32 * 1024 * 1024;

        if (!UPLOAD_URL || !MAX_BYTES || !window.fetch || !window.FormData) {
            return;
        }


        /* 上限を、人に読める形で（1MB 未満は KB。サーバー側の MbText と同じ考え方） */
        function mb(bytes) {

            if (bytes < 1048576) {
                return Math.max(1, Math.round(bytes / 1024)) + 'KB';
            }

            return (Math.round(bytes / 1048576 * 10) / 10) + 'MB';

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


        /* canvas へ描いて Blob にする

           square が true なら、**元の画像の真ん中を正方形に切ってから**縮める
           （アイコンは出す場所が多いので、1回切っておけば、出すところは全部同じでよい） */
        function toBlob(img, maxEdge, quality, square) {

            return new Promise(function (resolve, reject) {

                var sx = 0;
                var sy = 0;
                var sw = img.naturalWidth;
                var sh = img.naturalHeight;

                if (square) {

                    var side = Math.min(sw, sh);

                    sx = Math.round((sw - side) / 2);
                    sy = Math.round((sh - side) / 2);
                    sw = side;
                    sh = side;

                }

                var scale = Math.min(1, maxEdge / Math.max(sw, sh));
                var width = Math.max(1, Math.round(sw * scale));
                var height = Math.max(1, Math.round(sh * scale));

                var canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;

                var ctx = canvas.getContext('2d');

                if (!ctx) {
                    reject();
                    return;
                }

                ctx.drawImage(img, sx, sy, sw, sh, 0, 0, width, height);

                /* 透明なところがあれば PNG（背景を白で潰さないため）。記事の画像と同じ判定 */
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


        /* 上限に収まるまで、段階を下げながら試す */
        function shrink(img, kind, step) {

            if (step >= RETRY_STEPS.length) {
                return Promise.reject({ reason: '画像が大きすぎます（' + mb(MAX_BYTES) + 'まで）。' });
            }

            var s = RETRY_STEPS[step];
            var square = (kind === 'avatar');

            return toBlob(img, Math.round(EDGE[kind] * s.edgeScale), s.quality, square).then(function (result) {

                if (result.blob.size > MAX_BYTES) {
                    return shrink(img, kind, step + 1);
                }

                return result;

            });

        }


        function upload(kind, prepared) {

            var form = new FormData();
            form.append('image', prepared.blob, 'image');

            return window.fetch(UPLOAD_URL, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'X-Image-Kind': kind,
                    'X-Image-Width': String(prepared.width),
                    'X-Image-Height': String(prepared.height)
                },
                body: form
            }).then(function (res) {

                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }

                return res.json();

            }).then(function (reply) {

                if (!reply || !reply.ok) {
                    throw { reason: (reply && reply.reason) || '画像を送れませんでした。もう一度お試しください。' };
                }

                show(kind, reply.src);
                showDone(TARGET[kind].label + 'を差し替えました。');

            });

        }


        /* プレビューを書き換える（画面は読み込み直さない） */
        function show(kind, src) {

            var target = TARGET[kind];

            if (!target) {
                return;
            }

            var img = document.getElementById(target.img);
            var frame = document.getElementById(target.frame);

            if (img) {
                img.src = src;
            }

            if (frame) {
                frame.setAttribute('data-empty', '0');
            }

        }


        function handle(input) {

            var kind = input.getAttribute('data-profile-image');

            if (!EDGE[kind]) {
                return;
            }

            var file = input.files && input.files[0];

            if (!file) {
                return;
            }

            /* 【同じファイルをもう一度選べるようにする】
               value を空に戻さないと、同じファイルを選び直しても change が飛ばない。
               先に控えてから空にする */
            input.value = '';

            if (file.size > MAX_SOURCE_BYTES) {
                showError('元の画像が大きすぎて、開けませんでした（' + mb(MAX_SOURCE_BYTES) + 'まで）。');
                return;
            }

            input.disabled = true;
            showDone(TARGET[kind].label + 'を用意しています…');

            readImage(file).then(function (img) {

                showDone(TARGET[kind].label + 'を送っています…');

                return shrink(img, kind, 0).then(function (prepared) {
                    return upload(kind, prepared);
                });

            }).catch(function (err) {

                if (err && err.reason) {
                    showError(err.reason);
                } else {

                    if (window.console) {
                        console.warn('[プロフィール] 画像を送れませんでした。', err);
                    }

                    showError('画像を送れませんでした（通信が途切れたか、この形式の画像は使えません）。');

                }

            }).then(function () {
                input.disabled = false;
            }, function () {
                input.disabled = false;
            });

        }


        Array.prototype.forEach.call(
            document.querySelectorAll('input[type="file"][data-profile-image]'),
            function (input) {
                input.addEventListener('change', function () { handle(input); });
            });

    })();


    /* ============================================================
       3. スマホでキーボードが出ても、帯を見えたままにする
       ============================================================
       【、Scripts/site.js の「13」へ移した】記事を書く画面も同じ形（打っている間も帯を出す）に
       そろえたので、2つの画面で同じことを書かないように、1か所へまとめた（基本ルールの⑫）。
       この画面の帯（#profileSaveBar）にも .admin-savebar が付いているので、何もしなくても効く */


    /* ============================================================
       4. 保存のあと、読んでいた場所に戻る
       ============================================================
       指摘：保存ボタンを押したあと、画面の位置が上に戻ってしまう。その場にとどまるほうがよい。

       【2つの道がある】
         ・保存が**断られた**とき … ポストバックで画面が戻ってくる。
           そちらは .aspx の MaintainScrollPositionOnPostback="true" が戻す（サーバー側の仕掛け）
         ・保存が**うまくいった**とき … 302 で移り直す（PRG。F5 でもう一度送られないように）。
           移り直すとブラウザから見れば別の読み込みなので、ここで場所を覚えて戻す

       記事の編集画面と、まったく同じ形である（キーだけ別にしてある）。

       【読み書きは try で囲む】端末の設定で sessionStorage が使えないことがある。
       使えなければ、この機能だけ働かない（保存そのものには関係しない） */
    (function setupScrollRestore() {

        var KEY = 'vbchat_profile_scroll';

        /* 【form は1つだけ】Web Forms は、ページに form を1つしか置けない */
        var form = document.forms[0];

        if (!form) {
            return;
        }

        /* 【サイト共通の「押したあと位置を飛ばさない」（Scripts/site.js の「17」）には、この画面を任せない】同じことを2回しないため */
        window.vbOwnReturnPoint = true;

        function forget() {

            try {
                window.sessionStorage.removeItem(KEY);
            } catch (e) {
            }

        }

        /* 【送るときに覚える】
           断られた送信でも覚えてしまうが、戻すのは ?done= で開いたときだけなので害は無い
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

        /* 【読んだら必ず忘れる】残すと、次に ?done= で開いたときに古い場所へ飛ぶ */
        forget();

        var y = parseInt(saved, 10);

        /* 戻すのは「保存できた」「画像を外した」で移り直してきたときだけ */
        if (!(y > 0) || !/[?&]done=(saved|image)/.test(window.location.search)) {
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


    /* ============================================================
       5. 文字のアイコンの見本
       ============================================================
       「文字」を打つ・「色」を選ぶと、その場で丸い見本が変わる（要望：変え方が直感で分かるようにする）。
       保存は「保存する」。確かめ（1文字か・色の形）の本物は、サーバー（Accounts/AccountAvatar.vb）。

       【1文字の欄】2文字目を打ったら、**新しく打ったほうの1文字だけ**を残す
       （入っている「T」の後ろに「R」と打ったら「R」。前の文字を消してから打たなくても、入れ替えられる）。
       変換中（日本語入力）は触らない（確定してから決める）。
       【「1文字」は見た目の1文字】Intl.Segmenter で数える（絵文字の 👨‍👩‍👧 も 🇯🇵 も1文字）。
       無い古いブラウザでは、コードポイントで数える（家族の絵文字は分かれるが、サーバーが断るので壊れはしない）。
       【空にしたら、最初の文字】見本の data-default-letter。名前を打ち変えると、最初の文字も付いて変わる
       （サーバーと同じ決め方＝名前の1文字目が文字か数字ならそれ、でなければユーザー名の頭文字。英字は大文字）。
       【文字の色（白か黒）】window.vbAvatarTextColor（Scripts/site.js の「12」）＝サーバーと同じ計算 */
    (function () {

        var preview = document.getElementById('avatarLetterPreview');
        var letterBox = document.querySelector('.avatar-letter-input');
        var colorBox = document.querySelector('.avatar-color-input');
        var resetBtn = document.getElementById('avatarColorReset');
        var nameBox = document.querySelector('input[aria-labelledby="profileNameHeading"]');

        if (!preview || !letterBox || !colorBox) {
            return;
        }

        /* 【丸は2つある】アイコンの枠の中（写真が無いときに見える）と、写真を置いている間の小さな見本。
           どちらも同じ文字・色にそろえる（アイコンの区切りを組み直した） */
        var circles = Array.prototype.slice.call(preview.querySelectorAll('.js-letter-preview .account-avatar'));
        var defaultColor = preview.getAttribute('data-default-color') || '#546E7A';
        var userName = preview.getAttribute('data-user-name') || '';

        var segmenter = (typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function')
            ? new Intl.Segmenter('ja', { granularity: 'grapheme' })
            : null;

        function graphemes(text) {

            if (!text) {
                return [];
            }

            if (segmenter) {
                return Array.from(segmenter.segment(text), function (s) { return s.segment; });
            }

            return Array.from(text);
        }

        /* 最初の文字（Accounts/AccountAvatar.vb の DefaultLetter と同じ決め方） */
        function defaultLetter() {

            var name = nameBox ? nameBox.value.trim() : '';
            var first = Array.from(name)[0] || '';

            if (first && /[\p{L}\p{N}]/u.test(first)) {
                return first.toUpperCase();
            }

            var m = userName.match(/[A-Za-z0-9]/);

            return m ? m[0].toUpperCase() : '?';
        }

        function currentColor() {
            var v = (colorBox.value || '').trim();
            return /^#[0-9a-fA-F]{6}$/.test(v) ? v.toUpperCase() : defaultColor;
        }

        function render() {

            var letter = graphemes(letterBox.value.trim())[0] || defaultLetter();
            var color = currentColor();
            var text = window.vbAvatarTextColor ? window.vbAvatarTextColor(color) : '#FFFFFF';

            circles.forEach(function (circle) {
                circle.textContent = letter;
                circle.style.backgroundColor = color;
                circle.style.color = text;
            });

            /* 欄の中のグレーの字も、名前に付いていく（空にすると、これに戻ることが見て分かる） */
            letterBox.setAttribute('placeholder', defaultLetter());
        }

        /* --- 文字の欄：1文字に保つ --- */
        var composing = false;
        var accepted = letterBox.value.trim();

        function keepOne() {

            var parts = graphemes(letterBox.value.trim());

            if (parts.length > 1) {

                /* 新しく打ったほう＝前に入っていた1文字と違うほう（どちらにも無ければ、最後の1文字） */
                var picked = parts[parts.length - 1];

                for (var i = 0; i < parts.length; i++) {
                    if (parts[i] !== accepted) {
                        picked = parts[i];
                        break;
                    }
                }

                letterBox.value = picked;
            }

            accepted = letterBox.value.trim();
            render();
        }

        letterBox.addEventListener('compositionstart', function () { composing = true; });
        letterBox.addEventListener('compositionend', function () { composing = false; keepOne(); });
        letterBox.addEventListener('input', function () {
            if (composing) {
                render();
                return;
            }
            keepOne();
        });

        /* --- 色：選んでいる途中（input）も、閉じたとき（change）も --- */
        colorBox.addEventListener('input', render);
        colorBox.addEventListener('change', render);

        if (resetBtn) {
            resetBtn.addEventListener('click', function () {

                if (colorBox.jscolor && typeof colorBox.jscolor.fromString === 'function') {
                    colorBox.jscolor.fromString(defaultColor);
                } else {
                    colorBox.value = defaultColor;
                }

                render();
            });
        }

        if (nameBox) {
            nameBox.addEventListener('input', render);
        }

        render();

    })();

})();
