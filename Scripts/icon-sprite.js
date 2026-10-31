/* ============================================
   アイコンスプライトを、1回だけ取ってきて、ページへ差し込む
   ============================================

   【なぜ、この形なのか】
   以前は GetIconSpriteHtml() がサーバー側でファイルを読み、86KBのSVGを
   HTMLへそのまま貼り付けていた。.aspx はキャッシュされないため、
   トップ・ラウンジ・部屋のどれを開いても、毎回まるごと送り直していた
   （実測で、部屋のページは gzip 後の約7割がこれだった）。

   ファイルとして取りに行けば、ブラウザが30日キャッシュしてくれる
   （本番の .svg には Cache-Control: max-age=2592000 が効いている。実測）。

   【外部参照の <use href="file.svg#id"> は使えない】
   Chrome では、ファイルは取りに行くのに何も描画されない（実測）。
   ネットワークだけ見ると成功しているように見えるので、いちばん紛らわしい。
   取ってきた中身を DOM へ差し込むしかない。

   【差し込んだあと、既にある <use> は自動で描き直される】
   まだ存在しない id を指している <use> は 0×0 のままだが、
   あとから symbol が現れると自動的に解決される（実測）。
   色（currentColor）もそのまま効く。
   つまり **<use> を書き換える処理は要らない**。あとから増える行（発言・入室）も同じ。

   【URLは、必ずサーバーから受け取ること】
   common.AssetUrl が付ける ?v=（ファイルの最終更新時刻）が要る。
   これを付け忘れると、管理画面でアイコンを足しても
   30日間ブラウザに届かない。
   受け渡しは隠しフィールド（#iconSpriteUrl）で、
   部屋の効果音のURLと同じ形にしてある
   （<script> の中に <%: %> を直接書かないため。宿題7で踏んだ罠）
*/
window.vbChatIconSprite = (function () {

    'use strict';

    /* 取りに行くのは1回だけ。2度目以降は同じ約束（Promise）を返す */
    var promise = null;

    /* 失敗したときの待ち時間（ミリ秒）。使い切ったらあきらめる。
       電波の悪い端末で一度きりの失敗に当たっても、次で拾えるようにするため */
    var RETRY_DELAYS_MS = [400, 1500];

    /* 差し込み済みかどうか（同じページで二重に差し込まないため） */
    var injected = false;

    function inject(text) {

        if (injected) {
            return;
        }

        injected = true;

        var holder = document.createElement('div');

        /* 画面には出さない。読み上げソフトからも隠す
           （中身は <symbol> の入れ物で、それ自体は誰にも読ませるものではない） */
        holder.id = 'iconSpriteHolder';
        holder.setAttribute('aria-hidden', 'true');
        holder.style.display = 'none';
        holder.innerHTML = text;

        document.body.appendChild(holder);
    }

    function attempt(url, tryIndex) {

        return fetch(url, { credentials: 'same-origin' })
            .then(function (res) {

                /* 【罠】fetch は 404 でも reject しない。res.ok を自分で見ること
                   （部屋の効果音の読み込みで踏んだのと、同じ罠） */
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }

                return res.text();
            })
            .then(function (text) {
                inject(text);
                return true;
            })
            .catch(function (e) {

                if (tryIndex >= RETRY_DELAYS_MS.length) {
                    throw e;
                }

                return new Promise(function (resolve) {
                    setTimeout(resolve, RETRY_DELAYS_MS[tryIndex]);
                }).then(function () {
                    return attempt(url, tryIndex + 1);
                });
            });
    }

    /* 取ってくる（何回呼んでも、実際に取りに行くのは1回だけ）

       戻り値は Promise。
       アイコンが出そろってからでないと困るページ（トップページのアイコン選択）は、
       これを待ってから組み立てる。
       それ以外のページは待たなくてよい——<use> があとから解決されるため */
    function ready() {

        if (promise) {
            return promise;
        }

        var field = document.getElementById('iconSpriteUrl');
        var url = field ? field.value : '';

        if (!url) {
            /* 隠しフィールドを置き忘れたページ。
               ここで throw しておかないと、待っている側が永久に待つ */
            promise = Promise.reject(new Error('iconSpriteUrl が見つかりません'));
        } else {
            promise = attempt(url, 0);
        }

        promise.catch(function (e) {

            /* 【フェイルオープン】取れなくても、ページは止めない。
               アイコンが出ないだけで、名前を決めることも、
               部屋で発言することもできる状態を保つ。
               呼び出し側は、この前提で書くこと */
            console.warn('[vb-chat] アイコンを読み込めませんでした： ' + e.message);
        });

        return promise;
    }

    /* 読み込まれた時点で、すぐ取りに行く。
       待つかどうかは、ページごとに ready() で決める */
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ready);
    } else {
        ready();
    }

    return { ready: ready };

})();
