/* ============================================================
   ヘッダーのメニューが1行に入るかで、ハンバーガーに切り替える
   ============================================================
   指摘：上のメニューがハンバーガーに変わるのが早すぎる。
   前は画面の幅 1100px（@media）で切り替えていた。今は**ロゴ・メニュー・（右の枠のボタン）・右上の角が1行に入らなくなったら**
   <html> に vb-nav-compact を付ける（イントリンシック・デザインの①。メニューの項目が増えても、名前が長くなっても、ここは直さなくてよい）。
   付いている間は、ハンバーガーと低いヘッダー（小さいロゴ）になる。見た目は Content/Site.css の「狭い画面のヘッダー」。

   【測り方】いったん vb-nav-compact を外して広い形にし、メニューか右上の角がロゴの行から次の行へ落ちたかを見て、付け直す。
   読む・書くを1回の処理の中で続けてやるので、広い形が画面に描かれることは無い（描かれるのは、決まったほうだけ）。
   広い形でも header は flex-wrap: wrap なので、入らなければ縮まずに次の行へ落ちる＝それが合図。

   【このファイルだけ、ヘッダーのすぐ後ろで読み込む】（Site.Master）。site.js（ページの終わり）で決めると、
   広い形のヘッダーが一瞬見えてからハンバーガーに変わることがある。ほかの仕事はここに足さないこと（ページの表示を止めるため）。
   【JS が動かないとき】vb-nav-compact は付かず、メニューが次の行へ落ちた形のまま（リンクは全部押せる）

   【低い形のときは、ヘッダーを画面の上に固定する】（決定：ヘッダーの高さを低くする代わりに、
   ヘッダーは画面の上に固定して出したままにする）。<html> に vb-header-sticky も付け、ヘッダーの高さ（☰ のメニューを閉じた形）を
   CSS の変数 --sticky-header-h に入れる。目次などで飛んだ先が隠れないように（:root の scroll-padding-top）・右の袖の目次の貼り付く位置に使う。

   【管理人の画面】メニューもハンバーガーも無いので、合図は「管理メニュー（.admin-nav）が1行に入らず、横にすべる形になったか」
   （要望：管理画面も、スマホの幅のときはヘッダーを同じ方式にする。そうすると編集する部分の高さをもっと確保できる）。
   付くのは低い形（vb-nav-compact）だけで、**上には固定しない**（vb-header-sticky を付けない）——書く画面はツールバーと保存の帯が
   もう貼り付いていて、キーボードが出ると見えている範囲がその2つで埋まる。ヘッダーまで固定すると、編集できる高さが減るだけ */
(function () {

    'use strict';

    var root = document.documentElement;
    var header = document.querySelector('.page-wrapper > header');
    var nav = document.getElementById('mainNav');
    var adminNav = header && header.querySelector('.admin-nav');
    // 管理人の画面にはメニューもハンバーガーも無い（Site.Master の PublicNav）。かわりに管理メニューを見る
    if (!header || (!nav && !adminNav)) {
        return;
    }
    var logo = header.querySelector('.site-title');

    function fits() {
        var line = logo.getBoundingClientRect().bottom;
        // ロゴの行の下端より下から始まっていたら、次の行へ落ちている（hidden のものは 0 の箱なので外す）
        var parts = [nav, header.querySelector('.account-corner'), header.querySelector('.side-drawer-btn')];
        for (var i = 0; i < parts.length; i++) {
            var el = parts[i];
            if (!el || el.offsetWidth === 0) { continue; }
            if (el.getBoundingClientRect().top >= line - 1) {
                return false;
            }
        }
        return true;
    }

    // 管理人の画面：管理メニューが入りきらず、横にすべる形になっているか（低い形でも管理メニューの幅は変わらないので、行き来しない）
    function adminFits() {
        return adminNav.scrollWidth <= adminNav.clientWidth + 1;
    }

    function update() {
        if (!nav) {
            root.classList.toggle('vb-nav-compact', !adminFits());
            return;
        }
        var was = root.classList.contains('vb-nav-compact');
        root.classList.remove('vb-nav-compact');
        var compact = !fits();
        root.classList.toggle('vb-nav-compact', compact);
        root.classList.toggle('vb-header-sticky', compact);
        if (compact) {
            // ☰ のメニューを開いていたら、そのぶんは除く（閉じた形の高さ）
            var h = header.getBoundingClientRect().height - (nav.classList.contains('is-open') ? nav.getBoundingClientRect().height : 0);
            root.style.setProperty('--sticky-header-h', Math.round(h) + 'px');
        } else {
            root.style.removeProperty('--sticky-header-h');
        }
        if (was !== compact && !compact) {
            // 広い形に戻ったら、開いていたハンバーガーのメニューも閉じた形に戻す（次に狭くなったとき、開いたまま出ないように）
            nav.classList.remove('is-open');
            var btn = document.getElementById('hamburgerBtn');
            if (btn) {
                btn.classList.remove('is-active');
                btn.setAttribute('aria-expanded', 'false');
            }
        }
    }

    var pending = false;
    function schedule() {
        if (pending) { return; }
        pending = true;
        window.requestAnimationFrame(function () {
            pending = false;
            update();
        });
    }

    update();
    window.addEventListener('resize', schedule);
    window.addEventListener('load', schedule);
    // 字の形（Web フォントではないが、届いた字の幅）が決まったあとにも1回
    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(schedule);
    }
    // ヘッダーの中身が変わったとき（右の枠のボタンが出入りした・アイコンが届いた）に、site.js の「19」などが呼ぶ
    window.vbHeaderFit = schedule;

})();
