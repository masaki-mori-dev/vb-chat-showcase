/*
    部屋（Chat/ChatRoom.aspx）の画面を動かすJavaScript

    【なぜ、このファイルに分けたのか】
    以前は .aspx の中に直接書いていた。.aspx はブラウザにキャッシュされないため、
    部屋を開くたびに、このコードが丸ごと毎回配信されていた（gzip後で約10KB）。
    外部ファイルにすれば ?v= が効いて、2度目からは1バイトも流れない。

    【読み込む位置は、決まっている】
    ページ側で、次の順に読み込むこと。順番を入れ替えると壊れる。

        1. jquery                  … このファイル全体が $ を使う
        2. jquery.signalR          … $.connection を使う
        3. /signalr/hubs           … $.connection.chatHub は、ここで生える
        4. ページ側のインライン     … 下の4つのグローバル変数を受け取る
        5. このファイル

    icon-sprite.js は、このファイルと関係が無い（どちらが先でもよい）。
    まだ存在しないidを指す <use> は、symbol があとから現れた時点で描き直されるため。

    【サーバーの値は、ここには埋められない】
    .js は ASP.NET が中身を見ないので、<% %> を書いてもただの文字列になる。
    サーバーが決めた値は、すべてページ側でグローバル変数として受け取ること。
    現在うけとっているのは次の4つ。

        vbChatInitialLog  … 部屋の過去ログ
        vbChatPrivateLog  … 自分が当事者のこっそりの過去ログ
        vbChatSettings    … 設定（SiteSettings のうち、画面が使う値だけ）
        vbChatStamps      … スタンプの目次（StampCatalog.ClientJson）

    【コメントは、発行のときに外す】
    発行の道具が、送る写しからコメントを外す。
    ただしソースは GitHub で読まれるので、**外から見れば分かることは書いてよい。まだ塞いでいない穴は書かない。**

    【発行したら、本当に届いたかを確かめること】
    このファイルだけが本番へ届かず、
    **同じ回の CSS だけが新しい、という状態が実機で起きた。**
    新しいCSSと古いこのファイルが噛み合うと、
    **どちらの版にも無い挙動**（窓が全部左上に出る、など）が生まれる。

    確かめ方は発行後の確かめの道具（手元と本番のSHA-256を突き合わせる）。
    「ページのソースに <script src="chat-room.js?v="> の行が在る」だけでは足りない。
    **その行は在ったのに、その先のファイルが古かった。**
*/
$(function () {

    /*
        接続状態の変化・キープアライブ・自動再接続の様子を、時刻つきでコンソールへ出す
        （自動再接続が正しく動いているかを、実機で確認するために残している）
    */
    function debugLog(text) {
        console.log('[vb-chat] ' + new Date().toLocaleTimeString() + ' ' + text);
    }

    /*
        === Cookieから、自分のプロフィール情報を読み込む ===
        （ChatPageBaseのRequireChatProfile()により、この3つのCookieは必ず揃っている前提）
    */
    function getCookie(name) {
        var match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
        return match ? decodeURIComponent(match[1]) : null;
    }

    var myNickname = getCookie('ChatNickname');
    var myIconType = getCookie('ChatIconType');
    var myIconColor = getCookie('ChatIconColor');

    /*
        同じブラウザからの多重入室を検知するための識別ID（ChatBrowserId Cookie）は、
        ここでは読まない。サーバー側（ChatHub.JoinRoom）が、自分でCookieから読む。

        【なぜ、こちらから送らないのか】
        この値は「同じブラウザかどうか」の判定に使われ、一致した相手は部屋から
        追い出される。送れる形にしておくと、他人のBrowserIdを送りつけるだけで、
        その人を部屋から追い出せてしまう。「誰であるか」を名乗らせてはいけない

        退室ビーコン（LeaveBeacon.aspx）も同じ考え方で、Cookieはブラウザが
        勝手に付けて送るものだけを使い、本人確認はサーバー側で行っている
    */

    /* Page_Loadでチェック済みのSession("CurrentRoomId")の値（部屋ID） */
    var roomId = parseInt($('#roomId').val(), 10);

    /* 部屋の定員（人数表示の分母。現在人数は参加者一覧の件数をそのまま使う） */
    var maxUsers = parseInt($('#maxUsers').val(), 10);

    /*
        この部屋で、画像を送れるか（ChatRooms.IsImageUploadEnabled）。

        使うのは、こっそりの窓に画像のボタンを出すかどうかの1か所だけ。
        部屋あての入口（発言に入れるものを選ぶパネルの「画像」タブ）のほうは、
        ページ側の <% If %> が出し分けている——**判断の元は、どちらも同じ列である**。

        【出さないことは、防御ではない】
        受け口（Chat/UploadImage.aspx）が、同じ列をもう一度自分で確かめている
    */
    var isImageUploadEnabled = ($('#imageUploadEnabled').val() === '1');

    /*
        サーバーから渡された設定は、ページ側（Chat/ChatRoom.aspx）が
        グローバル変数 vbChatSettings として書き出している。

        【なぜ、このファイルの中で受け取らないのか】
        このファイルは静的な .js で、ASP.NET は中身をまったく見ない。
        サーバーの値を埋め込めるのは .aspx 側だけなので、
        「サーバーが決めた値」はすべてページ側で受け取り、
        ここではその変数を読むだけにしてある。

        なぜ数字をここへべた書きしてはいけないのか、中に何が入っているのかは、
        ページ側のサーバー側コメント（配信されないほう）に書いてある
    */

    /* 発言1件あたりの文字数の上限（残り文字数の表示と、送信ボタンの無効化に使う） */
    var MAX_MESSAGE_LENGTH = vbChatSettings.maxMessageLength;

    /* 自分のLocalId（JoinRoomの戻り値で確定するまではnull） */
    var myLocalId = null;

    /*
        参加者一覧（{localId, nickname, iconType, iconColor}の配列）
        JoinRoomの戻り値（既存の参加者）＋UserJoined/UserLeftの受信により更新する
    */
    var participants = [];

    /*
        今の管理人の、この部屋の中の番号（居ない・固定部屋なら 0）。
        入室の戻り値（ownerLocalId）で決まり、ownerChanged の配信で付け替わる。
        参加者一覧の「管理人」の札と、自分のプロフィールバーの札に使う
    */
    var ownerLocalId = 0;

    /*
        自分の意思でこの部屋を離れようとしているかどうか
        退室ボタン・kicked・ラウンジに戻るボタンでは、これをtrueにしてから遷移する。
        正常な離脱であり、切断の警告を出す必要がないため
    */
    var isLeavingIntentionally = false;

    /*
        === この部屋から出ていくと決めた瞬間に、必ずここを通す ===

        印を立てるだけでなく、**こっそりの窓の記録も一緒に捨てる。**
        入り直したときに窓の残像が一瞬見える件を、ここで断っている
        （詳細は PrivateTalk の forgetOpenedWindows）。

        【1か所にまとめてある理由】
        出ていく経路は5つある（退室ボタン／kicked／ラウンジに戻るボタン／
        入室を断られた／ログの取り直しで断られた）。
        **それぞれに2行ずつ書くと、次に経路が増えた日に片方だけ忘れる**——
        送信ボタンの有効・無効を updateSendButtonState の1か所で決めているのと、
        接続状態バーのボタンを showConnectionBar の1か所で決めているのと、同じ形である。

        【F5では呼ばれない】
        F5は部屋に留まるので、どの経路にも当たらない。
        **開いている窓がF5後も開いたままなのは、開発者の望む仕様である**
    */
    function markLeavingRoom() {
        isLeavingIntentionally = true;
        PrivateTalk.forgetOpenedWindows();
    }

    /*
        自分のSignalR接続ID（退室ビーコンで、サーバーへ「どの接続が抜けるか」を伝えるため）

        【罠】ビーコンを送る瞬間に $.connection.hub.id を読んではいけない。

        SignalRは自前のunloadハンドラを持っており、その中で stop() を呼ぶ。
        SignalR 2.x の stop() は connection.id を null にする。
        そのハンドラが登録されるのは $.connection.hub.start() を呼んだ時点で、
        これはこちらの beforeunload / pagehide の登録より前にある。
        先に登録されたハンドラが先に発火するため、sendLeaveBeacon() が走る頃には
        hub.id が既に null になっている。

        その結果、ビーコンが1本も飛ばなくなる。しかもエラーも警告も出ないため、
        画面上は何の異常も起きず、誰も気づけない
        （実際に、F5のたびに33（置き換え）＋20（入室）がログに残り続ける状態になっていた）。

        そのため、接続が確立した時点で、ここへ控えておく。
        SignalRが stop() で id を消しても、この控えは残る
    */
    var myConnectionId = null;

    /* 自分のプロフィールバーの表示を、Cookieの値で更新する */
    $('#myNickname').text(myNickname);
    $('#myAvatar svg').css('color', myIconColor);
    $('#myAvatar svg use').attr('href', '#icon-' + myIconType);


    /*
        === トリップ（名前の下に出す、16進10桁の印） ===

        【見た目で名前と分ける＝名前の文字列では原理的に作れない形にする】
        名前の下の行に、等幅の書体・ひとまわり小さい文字・トリップ自身から決まる色で出す。
        名前は利用者が1つの欄に打った1つの文字列で、その色・書体・置き場所は全部こちらが
        決めている。**名前と違う色・書体・置き場所で出せば、名前欄に何を打っても真似できない。**
        （「なまえ [a1b2c3d4e5]」のような角括弧の飾りは、名前欄に打てば作れてしまうので却下された）

        【色はCSSが決める】ここでは data-trip 属性にトリップを入れるだけ。
        1桁目で8色に振る対応表は Content/theme.css の末尾にある（ラウンジも同じ表を読む）。

        【出す場所】相手の発言の行・参加者一覧・自分のプロフィールバー・こっそりの窓の見出しの4か所。
        自分の発言の行（アイコンも名前も出していない）・入退室のお知らせ行（薄いグレー1行に
        10桁を混ぜると読みにくい）・こっそりの窓の各行には出さない。記録はするが表示はしない、は種別33と同じ形。
        （こっそりの窓は 出さず、相手の行ごとに出し、見出しの1か所にした。
          理由は PrivateTalk の buildWindowHtml の見出しの項）

        【値は、ログに焼き込まれたもの／届いたものをそのまま使う】
        今のトリップを引き直さない。遡ったとき全部が今の値に化けると、
        「同じ人だった」という証明そのものが成り立たなくなる。

        【判定に使わないこと】表示するだけの値である（無視・こっそりの宛先などは匿名IDで決まる）

        【読み上げ】前に「トリップ」を読み上げ専用で添える。10桁が名前の続きに聞こえないように、
        また聞き飛ばしたい人がどこからどこまでか分かるようにするため
        （自分の発言の行の「自分の発言」と同じ形）
    */
    var TRIP_PATTERN = /^[0-9a-f]{10}$/;

    function isTripValue(trip) {
        return typeof trip === 'string' && TRIP_PATTERN.test(trip);
    }

    /*
        === アカウントID（名前の下に出す「@ユーザー名」。チャットとアカウントの紐づけ） ===

        ログインしている人が「アカウントID を表示する」で入ると、トリップと同じ場所（名前の下の行）に
        「@ユーザー名」が出る。押すと、その人の公開のプロフィールが新しいタブで開く（部屋から離れないため）。

        【トリップとは、どちらか一方】アカウントID を出している人に、トリップは出ない。
        決めているのはサーバー（ChatHub.JoinRoom → Chat/ChatAccountLink.vb）で、画面は届いたものを出すだけ。

        【「名前の下の印」という1つの値にまとめて運ぶ】
        どちらか一方なので、画面の中では
            トリップ … 'a1b2c3d4e5'（16進10桁）
            アカウントID … '@taro'（先頭が @）
        のどちらか（か null）を、今までトリップを運んでいた引数・プロパティ（trip）にそのまま乗せている。
        サーバーからは trip と accountName が別々に届くので、**受け取った所で idMarkOf が1つにまとめる**
        （receiveMessage・receivePrivateMessage・userJoined・過去ログ・JoinRoom の戻り値・ノック）。
        分けたまま運ぶと、行・一覧・窓の見出し・バーの全部の関数に引数が1つずつ増え、片方だけ渡し忘れる場所ができる。

        【名前欄では、真似できない】トリップと同じ考え方。名前の「下の行」に、リンクの色で出る**本物のリンク**なので、
        名前欄に「@taro」と打っても同じ形にはならない（名前は名前の色・名前の行にしか出ない）。

        【ユーザー名の形を、ここでも確かめる】半角の英数字とハイフン・アンダースコアの3〜30文字
        （サーバーの AccountStore のユーザー名の決まりと同じ）。形を確かめてあるので、エスケープの要らない文字しか来ない。
        **この確かめを外すなら、escapeAttr／escapeHtml を通すこと**（過去ログの値は、何度でもここへ来る）

        【行き先】公開のプロフィール（/profile/ユーザー名）。サーバー側で URL を組み立てているのは
        AccountProfileRules.PublicUrl の1か所で、ここはその写し（URL の形を変える日は、両方を直す）。
        サイトの中なので、確認の小窓は挟まない（発言の中の外のリンクとは違う）
    */
    var ACCOUNT_NAME_PATTERN = /^[A-Za-z0-9_-]{3,30}$/;
    var ACCOUNT_PROFILE_URL_PREFIX = '/profile/';

    function isAccountName(name) {
        return typeof name === 'string' && ACCOUNT_NAME_PATTERN.test(name);
    }

    /* サーバーから別々に届く trip と accountName を、「名前の下の印」1つにまとめる（無ければ null） */
    function idMarkOf(trip, accountName) {

        if (isAccountName(accountName)) {
            return '@' + accountName;
        }

        return isTripValue(trip) ? trip : null;
    }

    /* 印がアカウントID なら、そのユーザー名（@ を除いたもの）。そうでなければ null */
    function accountNameOfMark(mark) {

        if (typeof mark !== 'string' || mark.charAt(0) !== '@') {
            return null;
        }

        var name = mark.substring(1);

        return isAccountName(name) ? name : null;
    }

    /* 形の合っている印だけを通す（合わなければ null）。覚えておく値を作るときに使う */
    function normalizeIdMark(mark) {
        return (isTripValue(mark) || accountNameOfMark(mark)) ? mark : null;
    }

    /* 読み上げ用の名前に添える文字（「（トリップ a1b2c3d4e5）」か「（アカウント @taro）」。無ければ空文字） */
    function idMarkLabel(mark) {

        var accountName = accountNameOfMark(mark);

        if (accountName) {
            return '（アカウント @' + accountName + '）';
        }

        return isTripValue(mark) ? '（トリップ ' + mark + '）' : '';
    }

    /* アカウントID の行き先（公開のプロフィール） */
    function accountProfileUrl(accountName) {
        return ACCOUNT_PROFILE_URL_PREFIX + encodeURIComponent(accountName);
    }

    /*
        名前の下の印（トリップかアカウントID）を組み立てる（無い・形が崩れているときは空文字）。
        形を確かめてあるので、エスケープの要らない文字しか来ない
        （それでも「形を確かめる」を外したら、escapeAttr／escapeHtml を通すこと）

        linkMode … アカウントID のときの形（トリップには関係ない）
            'link'  … ふつうのリンク（Tab で止まる）。こっそりの窓の見出し
            'quiet' … 押せるが、Tab では止まらないリンク。**発言の行**
                      （同じ人の行が何十行も並ぶので、1行ごとに Tab が止まると、キーボードの人がログを抜けられなくなる。
                        キーボードからは、参加者一覧で相手を選んで「プロフィール」を押す）
            'text'  … リンクにしない。**参加者一覧**（1人ぶんがボタンで、ボタンの中にリンクは置けない）
        省略したら 'link'
    */
    function buildTripHtml(trip, className, linkMode) {

        var accountName = accountNameOfMark(trip);

        if (accountName) {

            var inner = '<span class="visually-hidden">アカウント </span>@' + accountName;

            if (linkMode === 'text') {
                return '<span class="account-id ' + className + '">' + inner + '</span>';
            }

            return '<a class="account-id ' + className + '" href="' + accountProfileUrl(accountName) + '"' +
                ' target="_blank" rel="noopener"' +
                (linkMode === 'quiet' ? ' tabindex="-1"' : '') +
                ' title="@' + accountName + ' のプロフィール（新しいタブで開きます）">' +
                inner + '<span class="visually-hidden">（新しいタブで開きます）</span></a>';
        }

        if (!isTripValue(trip)) {
            return '';
        }

        return '<span class="trip-code ' + className + '" data-trip="' + trip + '">' +
            '<span class="visually-hidden">トリップ </span>' + trip + '</span>';
    }

    /*
        自分のプロフィールバーのトリップ。JoinRoom の戻り値（myTrip）から入れる。

        【Cookie からは読めない】合言葉の Cookie は HttpOnly なので、JS では何もできない。
        ソルトもサーバーにしか無いので、自分のトリップもサーバーから教わるしかない
    */
    function setMyTrip(trip) {

        var el = document.getElementById('myTrip');

        if (!el) {
            return;
        }

        /*
            アカウントID を出している人は、トリップの代わりに「@ユーザー名」。
            どちらか一方なので、同じ場所（名前の下の行）に、どちらかだけが出る。
            値は JoinRoom の戻り値（myAccountName）＝サーバーがログインの札から確かめたもの
        */
        var accountEl = document.getElementById('myAccount');
        var accountName = accountNameOfMark(trip);

        if (accountEl) {
            if (accountName) {
                accountEl.setAttribute('href', accountProfileUrl(accountName));
                document.getElementById('myAccountText').textContent = '@' + accountName;
                accountEl.hidden = false;
            } else {
                accountEl.removeAttribute('href');
                document.getElementById('myAccountText').textContent = '';
                accountEl.hidden = true;
            }
        }

        if (isTripValue(trip)) {
            el.setAttribute('data-trip', trip);
            document.getElementById('myTripText').textContent = trip;
            el.hidden = false;
        } else {
            el.removeAttribute('data-trip');
            document.getElementById('myTripText').textContent = '';
            el.hidden = true;
        }
    }


    /* === HTMLエスケープ（他人のニックネーム・発言内容は信用できないため、必ず通す） === */
    function escapeHtml(text) {
        return $('<div>').text(text).html();
    }

    /*
        === HTML属性の中に入れる文字列のエスケープ ===

        【罠】escapeHtmlだけでは、属性値に使うには足りない。
        上のescapeHtmlは < > & は変換するが、ダブルクォートは変換しない
        （textContentに入れてinnerHTMLで取り出す仕組みのため）。
        title="..." のような属性値にそのまま入れると、ダブルクォートを含む
        ニックネームで属性が途中で閉じ、タグを壊されてしまう。
        属性の中に入れるときは、必ずこちらを通すこと
    */
    function escapeAttr(text) {
        return escapeHtml(text).replace(/"/g, '&quot;');
    }


    /*
        ============================================================
        === 発言の中の URL を、リンクにする（オートリンク）===
        ============================================================

        足した。**画面側だけの変更で、サーバーは何も知らない。**

        【いちばん大事な決まり】
        **エスケープ済みのHTMLに正規表現をかけない。**
        生の本文を「URLの部分」と「それ以外」に分けてから、それぞれをエスケープして組み立てる。
        エスケープ後にかけると、
          ・「&amp;」の途中で URL が切れる（&amp; を含むクエリ文字列は珍しくない）
          ・組み立てた <a href="..."> の中に、もう一度正規表現が当たる
        といった壊れ方をする。**順番が守り**である。

        【どこまでを URL とみなすか】
          ・`http://` `https://` で始まるものだけ
          ・**空白か、ASCII 以外の文字（日本語など）が出たところで終わり**
            → 「https://example.comです」の「です」は混ざらない
          ・**末尾の句読点は URL に含めない**（`)` `.` `,` `!` `?` `]` など）
            → 「（https://example.com）」「https://example.com。」が自然に切れる
          ・ただし**丸かっこは釣り合いを見る**——URL の中に `(` が多ければ `)` を戻す
            （Wikipedia の「…_(曖昧さ回避)」のような URL を切らないため）

        【行き先は new URL() で読めたものだけ】
        読めない文字列や、`javascript:` のような別の仕組みは**リンクにしない**（ただの文字として出す）。
        `http:` `https:` 以外は通さない。
        **href に入れるのは new URL() が正規化したほう**で、画面に出す文字は打たれたまま。
        ずれるのは正規化のぶんだけだが、**確認の小窓が見せるドメインは href のほう**＝本当に行く先である
    */

    /*
        ASCII の見える文字が続く限り。**空白か、ASCII 以外（日本語など）が来たら、そこで終わる。**

        `<` `>` `"` とバッククォートも外してある——URL の決まり（RFC 3986）で
        「URL の中に素で書いてはいけない」とされている文字で、
        入れておくと「https://example.com/&lt;b&gt;x」のような、
        タグの書きかけまでリンクに巻き込む
    */
    var AUTOLINK_PATTERN = /https?:\/\/[^\s"<>`-￿]+/gi;

    /* URL の末尾から外す句読点。**ASCII だけ**（それ以外は、そもそも上の形に入らない） */
    var AUTOLINK_TAIL_PATTERN = /[)\]}>,.;:!?'"]+$/;

    /* 丸かっこの釣り合いを見て、末尾の ) を戻す */
    function trimUrlTail(url) {

        var kept = url;

        for (;;) {
            var m = AUTOLINK_TAIL_PATTERN.exec(kept);
            if (!m) {
                break;
            }
            kept = kept.slice(0, m.index);
        }

        /*
            URL の中で「(」のほうが多いなら、落とした「)」を戻す。
            例：https://ja.wikipedia.org/wiki/…_(曖昧さ回避) ← ASCII の範囲では「_(」まで
        */
        for (;;) {
            var opens = (kept.match(/\(/g) || []).length;
            var closes = (kept.match(/\)/g) || []).length;
            if (opens <= closes || url.charAt(kept.length) !== ')') {
                break;
            }
            kept += ')';
        }

        return kept;
    }

    /* http / https として読めたら、正規化した文字列を返す。読めなければ null */
    function safeHttpUrl(url) {

        if (typeof URL !== 'function') {
            /* 古いブラウザ。リンクにせず、ただの文字として出す（何も壊れない） */
            return null;
        }

        try {
            var u = new URL(url);

            if (u.protocol !== 'http:' && u.protocol !== 'https:') {
                return null;
            }

            if (!u.hostname) {
                return null;
            }

            return u.href;

        } catch (e) {
            return null;
        }
    }

    /*
        リンク1つぶんの HTML。

        【href を持たせたまま、押したときだけ横取りする】
        持たせておくと、
          ・マウスを乗せるとブラウザが行き先を出す
          ・Ctrl（⌘）＋クリックや中クリックで、そのまま新しいタブへ
          ・読み上げソフトが「リンク」として読み、リンクの一覧にも並ぶ
          ・万一 JavaScript が死んでいても、リンクとしては生きている
        ふつうの左クリックだけを横取りして、確認の小窓を出す（下の onLinkClick）
    */
    function buildLinkHtml(href, text) {
        return '<a class="message-link" href="' + escapeAttr(href) + '"' +
            ' target="_blank" rel="noopener noreferrer">' + escapeHtml(text) + '</a>';
    }

    /*
        発言の本文を、表示用のHTMLにする。

        **文章の発言は、必ずここを通すこと**（部屋の吹き出しも、こっそりの窓も）。
        画像・スタンプの発言は本文がサーバーの組み立てた印なので、ここは通らない
    */
    function buildMessageTextHtml(text) {

        if (!text) {
            return escapeHtml(text || '');
        }

        var out = '';
        var last = 0;
        var m;

        AUTOLINK_PATTERN.lastIndex = 0;

        while ((m = AUTOLINK_PATTERN.exec(text)) !== null) {

            var trimmed = trimUrlTail(m[0]);
            var href = safeHttpUrl(trimmed);

            if (!href) {
                /* リンクにしない。地の文として、あとでまとめてエスケープされる */
                continue;
            }

            out += escapeHtml(text.slice(last, m.index));
            out += buildLinkHtml(href, trimmed);

            last = m.index + trimmed.length;

            /* 末尾の句読点を落としたぶん、次の探し始めを戻す */
            AUTOLINK_PATTERN.lastIndex = last;
        }

        out += escapeHtml(text.slice(last));

        return out;
    }

    /*
        ============================================================
        === 画像の発言 ===
        ============================================================

        E（画像アップロード）の回で足した。

        画像の発言は、**本文がサーバーの組み立てた印だけ**である。

            [img:2026/09/10/0f3c….jpg:1280x960]
              名前（日付のフォルダ＋GUID＋拡張子）  幅x高さ

        文章と一緒には送れない（決定：LINE などと同じ形にする）。

        【なぜ、種別を新しく作らなかったのか】
        種別（10＝部屋、11＝こっそり）が表しているのは「誰に向けた発言か」である。
        そこへ「何を送ったか」を混ぜると2つの軸の掛け算になり、
        こっそりの画像のために13番が要ることになる。スタンプが増えれば、また倍になる。
        印だけにしておけば、Dで作った宛先の列も、窓への配信も、過去ログの絞り込みも、
        **1つも足さずにそのまま効く**。

        【なぜ、寸法が印に入っているのか】
        画像が届く前に場所を確保して、**画面を跳ねさせない**ため。
        アイコンをファイルにしたときに1188px跳ねたのと、同じ形の穴を先に塞いである
    */

    /*
        印の形。サーバー側（Core/SiteImage.vb の TokenPattern）と、必ず同じにすること。

        【前後に何も許さない形にしてあること】
        本文がまるごとこの形のときだけ、画像として描く。
        文章に紛れ込ませた「これは [img:…] です」は、ただの文章として出る
        （そもそもサーバーが、そういう発言を受け付けない）
    */
    var IMAGE_TOKEN_PATTERN =
        /^\[img:(\d{4}\/\d{2}\/\d{2}\/[0-9a-f]{32}\.(?:jpg|png|gif)):(\d{1,4})x(\d{1,4})\]$/;

    /*
        画像を取りに行くURLの頭。
        サーバー側（Core/SiteImage.vb の PublicUrlPrefix / PrivateUrlPath）にも
        同じ文字列がある。置き場所を変えるときは、両方を直すこと
    */
    var IMAGE_URL_PREFIX = '/Uploads/images/';
    var PRIVATE_IMAGE_URL = '/chat/private-image';

    /*
        画像のボタンに入れる絵。

        【1か所で持っていること】
        使う場所が2つある——発言に入れるものを選ぶパネルの「画像を選ぶ」ボタンと、
        こっそりの窓の小さなボタン。**同じ絵を2か所に書かない**
        （歯車と笑顔がそれぞれ1か所にしか無いのと、同じ扱い）。

        大きさは付けない（CSS側が className ごとに決める）。
        色も指定しない（currentColor で、ボタン側の color を拾う）

        【絵文字はボタンに使わない、という決まりのまま】
        🖼 のような絵文字は、環境ごとにフォントが違い、
        字面の余白も見た目の大きさも変わる（
        😀 のボタンをSVGへ作り替えたときの理由と、まったく同じ）
    */
    function imageIconSvg(className) {

        return '<svg class="' + className + '" viewBox="0 0 24 24" aria-hidden="true"' +
            ' fill="none" stroke="currentColor" stroke-width="1.6">' +
            /* 額縁 */
            '<rect x="3" y="4" width="18" height="16" rx="2"></rect>' +
            /* 空に浮かぶもの（塗りつぶしなので、線の指定を打ち消しておく） */
            '<circle cx="8.5" cy="9.5" r="1.8" fill="currentColor" stroke="none"></circle>' +
            /* 山なみ */
            '<path d="M4 17.5 L9.5 12 L13.5 16 L16.5 13.5 L20 17"' +
            ' stroke-linecap="round" stroke-linejoin="round"></path>' +
            '</svg>';
    }

    /*
        「発言に入れるもの」を選ぶボタンの絵（笑顔）。

        【持ち主は、本体の丸いボタン（#insertBtn）の markup である】
        こっそりの窓にも同じボタンを出すが、同じ絵を2か所に書かないよう、
        あちらの <svg> を写して使う。class だけを付け替える
        （本体の .insert-btn-icon は、44pxの円に合わせた大きさを持っているため）
    */
    function insertIconHtml(className) {

        var $svg = $('#insertBtn svg').first().clone();

        if ($svg.length === 0) {
            return '';
        }

        $svg.attr('class', className);

        return $svg.prop('outerHTML');
    }

    /* 本文が画像の印なら {name, width, height} を返す。違えば null */
    function parseImageToken(message) {

        if (!message) {
            return null;
        }

        var m = IMAGE_TOKEN_PATTERN.exec(message);

        if (!m) {
            return null;
        }

        return {
            name: m[1],
            width: parseInt(m[2], 10),
            height: parseInt(m[3], 10)
        };
    }

    /*
        画像1枚のHTML。

        isPrivate … こっそりの窓の中なら true。取りに行く先が変わる

        【こっそりの画像だけ、.aspx を通す】
        静的ファイルのままだと、URLをコピーして外の人へ渡せば、部屋に居ない人でも
        開ける。あちらは毎回「この人は当事者か」を確かめてから返す。
        部屋あての画像は、これまでどおり静的ファイル——守る必要が無いものに、
        アプリを通す手間を払わない

        【幅と高さを、必ず属性で書くこと】
        これがあると、ブラウザは画像が届く前に場所を空けておける。
        書かないと、届いた瞬間にその行が伸びて、**読んでいるところが飛ぶ**。

        【属性には「枠に収めたあとの寸法」を入れる】（直した）
        それまでは元の寸法をそのまま入れ、CSS の max-width と max-height で縮めていた。
        **幅は max-width で縮み、高さは height:auto で比から出るが、そこへ max-height が掛かると
        高さだけが切られ、幅はそのまま残る**——縦長の画像がつぶれていた
        （実測＝720×1280が 240×320（3:4）で描かれていた。横長は無事）。
        最初から枠に収まる寸法を渡せば、CSS は何も切らない。
        **届く前に取る場所と、届いたあとの大きさが1pxも違わない**ので、場所取りも前より正確になる。
        枠の大きさは CSS の :root が持っている（imageBoxOf）

        【loading="lazy" にしてあること】
        過去ログを200行描くとき、画面の外にある画像まで取りに行かない
        （開発者の指定：過去ログでは、見えている画像だけ取りに行く）。
        知らないブラウザは、この属性を読み飛ばすだけで害は無い

        【読み上げソフトへは「画像」とだけ伝える】
        誰が送ったかは、隣にアイコンと名前が出ている（開発者の指定）

        【属性へ入れる値は escapeAttr を通すこと】
        名前は上の正規表現を通っているので危険な文字は入りようがないが、
        **出口では必ずエスケープする**というこの画面の決まりに合わせてある
        （「入口の検査だけでは足りない」の裏返しで、両方やる）
    */
    function buildImageHtml(image, isPrivate) {

        var url = isPrivate
            ? PRIVATE_IMAGE_URL + '?n=' + encodeURIComponent(image.name)
            : IMAGE_URL_PREFIX + image.name;

        var size = fitImageSize(image, imageBoxOf(isPrivate));

        /* tabindex="-1" と aria-describedby は、ログの Tab を1回にするため（LogKeyNav） */
        return '<a class="message-image-link" href="' + escapeAttr(url) + '"' +
            ' target="_blank" rel="noopener noreferrer"' +
            ' tabindex="-1" aria-describedby="logKeyHint">' +
            '<img class="message-image" src="' + escapeAttr(url) + '"' +
            ' width="' + size.width + '" height="' + size.height + '"' +
            ' loading="lazy" alt="画像" />' +
            '</a>';
    }

    /*
        画像の枠（幅と高さの上限）。部屋の中と、こっそりの窓の中の2つ。

        【数字はここに書かない。CSS の :root が持っている】
        --message-image-max-width / -height と --private-image-max-width / -height の4つ。
        実機で見て決める値なので、あちらの1か所を直せば、ここも付いてくる。
        読むのは、はじめて画像を描くときの1回だけ（誰も書き換えない値なので、読み直さない）。
        読めなかったときの数字は、読めていた頃と同じ値にしてある
    */
    var imageBoxes = null;

    function imageBoxOf(isPrivate) {

        if (!imageBoxes) {

            var style = getComputedStyle(document.documentElement);

            var px = function (name, fallback) {
                var n = parseFloat(style.getPropertyValue(name));
                return (n > 0) ? n : fallback;
            };

            imageBoxes = {
                room: { w: px('--message-image-max-width', 240), h: px('--message-image-max-height', 320) },
                priv: { w: px('--private-image-max-width', 180), h: px('--private-image-max-height', 200) }
            };
        }

        return isPrivate ? imageBoxes.priv : imageBoxes.room;
    }

    /*
        元の比を保ったまま、枠に収まる寸法にする。

        【大きくはしない】枠より小さい画像は、元の大きさのまま（今までと同じ）。
        【幅と高さの両方で見る】どちらか厳しいほうに合わせて、同じ倍率で縮める
    */
    function fitImageSize(image, box) {

        var scale = Math.min(1, box.w / image.width, box.h / image.height);

        return {
            width: Math.max(1, Math.round(image.width * scale)),
            height: Math.max(1, Math.round(image.height * scale))
        };
    }


    /*
        ============================================================
        === スタンプの発言 ===
        ============================================================

        スタンプの発言も、画像とまったく同じ形である——**本文がサーバーの組み立てた印だけ**。

            [stamp:frog-01]

        種別（10＝部屋、11＝こっそり）は変えない。印の考え方と、種別を増やさない理由は
        上の「画像の発言」の節と同じ。

        【印は、利用者に組み立てさせない】
        パネルから押すと、スタンプの名前だけを ChatHub.SendStamp へ渡し、
        本文はサーバーが作る。普通の発言に「[stamp:」が混ざっていたら、サーバーが断る
    */

    /*
        目次。ページ側（Chat/ChatRoom.aspx）が vbChatStamps として書き出している。

          sets   … パネルに並べるもの（今送ってよいものだけ。配列の順が並び順）
          stamps … 絵を引く表（止めたスタンプも入る。過去ログの絵を出すため）

        【無くても止まらないこと】
        .aspx だけ古い（発行漏れ）と、この変数そのものが無い。
        そのまま読むと ReferenceError で、**部屋の画面が丸ごと動かなくなる**。
        スタンプが出ないだけで済むよう、空の目次へ落とす
    */
    var STAMPS = (typeof vbChatStamps !== 'undefined' && vbChatStamps && vbChatStamps.stamps)
        ? vbChatStamps
        : { sets: [], stamps: {} };

    /*
        印の形。サーバー側（Chat/StampCatalog.vb の BuildToken と、名前の形 CodePattern）と、
        必ず同じにすること。本文がまるごとこの形のときだけ、スタンプとして描く
    */
    var STAMP_TOKEN_PATTERN = /^\[stamp:([a-z0-9]+(?:-[a-z0-9]+)*)\]$/;

    /*
        今パネルから送ってよいスタンプの一覧（名前 → セットの名前）。

        【過去ログの絵を押せるかどうかは、ここで決まる】
        止めたセットのスタンプは、絵は出すが押せない（押しても送れないため）
    */
    var SENDABLE_STAMP_SET = {};

    STAMPS.sets.forEach(function (set) {
        set.stamps.forEach(function (code) {
            SENDABLE_STAMP_SET[code] = set.code;
        });
    });

    /*
        目次からスタンプ1つを引く。無ければ null。

        【罠】STAMPS.stamps[code] をそのまま読まないこと。
        名前の形は英小文字・数字・ハイフンなので、「constructor」も「toString」も通る。
        素のオブジェクトはそれらを親から受け継いでいるので、**目次に無い名前でも値が返る**。
        必ず、自分の持ち物かを確かめてから読む
    */
    function lookupStamp(code) {

        if (!Object.prototype.hasOwnProperty.call(STAMPS.stamps, code)) {
            return null;
        }

        return STAMPS.stamps[code];
    }

    /*
        本文がスタンプの印なら {code, src, label, setCode} を返す。違えば null。

        【目次に無い名前は null を返す（＝ただの文章として出る）】
        目次から行ごと消された・絵のファイルが無い、といった場合。
        絵が出せないのに「スタンプ」とだけ出すより、書かれていた文字をそのまま見せるほうが、
        何が起きたかが分かる（行は消さない決まりなので、普段は起こらない）
    */
    function parseStampToken(message) {

        if (!message) {
            return null;
        }

        var m = STAMP_TOKEN_PATTERN.exec(message);

        if (!m) {
            return null;
        }

        var entry = lookupStamp(m[1]);

        if (!entry) {
            return null;
        }

        return {
            code: m[1],
            src: entry.src,
            label: entry.label,

            /* 今送れるなら、そのセットの名前。送れなければ null */
            setCode: Object.prototype.hasOwnProperty.call(SENDABLE_STAMP_SET, m[1])
                ? SENDABLE_STAMP_SET[m[1]]
                : null
        };
    }

    /*
        スタンプ1つのHTML（部屋の中でも、こっそりの窓の中でも同じ）。

        【押すと、スタンプの一覧が開いて、そのスタンプが目立った状態になる】
        その状態でもう一度押せば送れる（開発者の指定）。
        だから <button> で包む。キーボードでも読み上げソフトでも届くようにするため。

        【絵を新しいタブで開く動線は、作らないこと】
        <img> で読み込んだSVGは、中にスクリプトがあっても何も実行しない（実測済み）。
        **安全なのはファイルではなく、<img> という読み込み方のほうである**——
        同じファイルを直接開けば、中のスクリプトは vb-chat のサイトの中で動く。
        画像の発言は押すと新しいタブで開くが、スタンプは <a> で包まない。

        【止めたセットのスタンプは、押せない】
        絵はそのまま出す（過去ログの約束）が、送れないものを選ばせない。

        【大きさは、ここでは決めない】
        CSS の --stamp-size の1か所が持つ（width / height 属性も書かない）。
        大きさを固定で持っているので、絵が届く前から場所が確保され、画面は跳ねない。
        loading="lazy" は、過去ログの画面の外にある絵を取りに行かないため
        （画像の発言と同じ。一度取れば、以後はブラウザのキャッシュから出る）
    */
    function buildStampHtml(stamp) {

        var imgHtml = '<img class="message-stamp" src="' + escapeAttr(stamp.src) + '"' +
            ' loading="lazy" alt="' + (stamp.setCode ? '' : escapeAttr('スタンプ「' + stamp.label + '」')) + '" />';

        if (!stamp.setCode) {
            return imgHtml;
        }

        /* tabindex="-1" と aria-describedby は、ログの Tab を1回にするため（LogKeyNav） */
        return '<button type="button" class="message-stamp-btn"' +
            ' tabindex="-1" aria-describedby="logKeyHint"' +
            ' data-stamp-code="' + escapeAttr(stamp.code) + '"' +
            ' title="' + escapeAttr(stamp.label) + '"' +
            ' aria-label="' + escapeAttr('スタンプ「' + stamp.label + '」。押すと、スタンプの一覧で選べます') + '">' +
            imgHtml +
            '</button>';
    }


    /*
        ============================================================
        === ログの中のスタンプ・画像は、Tab 1回で入り、中は上下の矢印で移る ===
        ============================================================

。

        【なぜ要るのか】
        スタンプと画像（E）は押せるので、1つずつ Tab で止まっていた。
        ログが長いと、キーボードで入力欄まで行くのに Tab を何十回も押すことになる。
        **押せることは残したまま、移動の回数を減らす**（開発者の指定）。

        【形＝いわゆる「ロービング tabindex」】
        ログ（部屋のログと、こっそりの窓のログ。それぞれ別の組）の中で、
        **Tab で止まるのは、いつも1つだけ**（tabindex="0"）。残りは tabindex="-1"。
        そこへ入ったら ↑↓ で前後へ、Home／End で最初と最後へ移る。もう一度 Tab で抜ける。

        【どれに止まるか——読んでいた位置を動かさない】（開発者が確かめたかったこと）
          1. 前に焦点が乗っていたもの（覚えている）が、今画面に見えていれば、それ
          2. そうでなければ、**今画面に見えているもののうち、いちばん下のもの**
          3. 見えているものが1つも無ければ、**ログの枠そのもの**（↑↓は今までどおりスクロール）
        スクロールするたびに選び直す。**ただし焦点がログの中にある間は選び直さない**
        ——新しい発言が届いても、覚えている位置は最新へ動かない。
        どれを選んでも「今見えているもの」なので、Tab で入った瞬間に読んでいた位置が飛ばない。

        【読み上げソフトの利用者にとって不便にならないこと】
        tabindex は「Tab の順番」を決めるだけで、読み上げの対象からは外れない。
        読み上げモード（NVDA・JAWS の矢印での読み進め、TalkBack のスワイプ）では今までどおり全部に届き、押せる。
        焦点が乗ったときには、aria-describedby で「上下の矢印キーで移れる」ことを読み上げる（#logKeyHint）
    */
    var LogKeyNav = (function () {

        var ITEM = '.message-stamp-btn, .message-image-link';
        var CONTAINER = '#chatLog, .private-talk-log';

        /* 組（ログ）ごとに、前に焦点が乗っていたものを覚えておく */
        var remembered = new WeakMap();

        var pending = [];
        var scheduled = false;

        function itemsOf(container) {
            return Array.prototype.slice.call(container.querySelectorAll(ITEM));
        }

        /* ログの見えている範囲に、まるごと入っているか */
        function isVisibleIn(el, container) {
            var r = el.getBoundingClientRect();
            var c = container.getBoundingClientRect();
            return r.height > 0 && r.top >= c.top - 1 && r.bottom <= c.bottom + 1;
        }

        function focusIsInside(container) {
            var a = document.activeElement;
            return !!a && a !== container && container.contains(a);
        }

        /* Tab で止まる1つを選び直す（上の「どれに止まるか」） */
        function refresh(container) {

            if (!container || !container.isConnected || focusIsInside(container)) {
                return;
            }

            var items = itemsOf(container);
            var target = null;
            var mem = remembered.get(container);

            if (mem && mem.isConnected && container.contains(mem) && isVisibleIn(mem, container)) {
                target = mem;
            }

            for (var i = items.length - 1; !target && i >= 0; i--) {
                if (isVisibleIn(items[i], container)) {
                    target = items[i];
                }
            }

            items.forEach(function (el) {
                el.tabIndex = (el === target) ? 0 : -1;
            });

            /*
                見えているものが無ければ、枠が止まる場所になる（中が見えている間は、枠は止まらない）。
                部屋のログの枠は、もともと tabindex="-1"（「新着あり」を押したあとの焦点の置き場）。
                -1 でも JS からの focus() は今までどおり効く
            */
            container.tabIndex = target ? -1 : 0;
        }

        /*
            スクロールのたびに呼ばれるので、まとめて1回にする。

            【requestAnimationFrame にしないこと】
            画面を描いていない間（裏のタブ、開発機のブラウザのペインが隠れているとき）は、
            rAF の呼び戻しが1回も来ない（実際に、開発機で一度も選ばれなかった）。
            setTimeout なら、どの状態でも走る（
            「どの合図にも寄りかからない」と同じ考え方）
        */
        function schedule(container) {

            if (pending.indexOf(container) < 0) {
                pending.push(container);
            }

            if (scheduled) {
                return;
            }

            scheduled = true;

            setTimeout(function () {
                var list = pending;
                pending = [];
                scheduled = false;
                list.forEach(refresh);
            }, 0);
        }

        /* スクロールはバブリングしないので、捕まえる側（capture）で拾う */
        document.addEventListener('scroll', function (e) {
            var c = e.target;
            if (c && c.nodeType === 1 && c.matches(CONTAINER)) {
                schedule(c);
            }
        }, true);

        $(window).on('resize', function () {
            $(CONTAINER).each(function () { schedule(this); });
        });

        /* 焦点が乗ったものを覚え、それを「止まる1つ」にする（マウスで押したときも同じ） */
        $(document).on('focusin', ITEM, function () {

            var container = $(this).closest(CONTAINER)[0];

            if (!container) {
                return;
            }

            remembered.set(container, this);

            var self = this;

            itemsOf(container).forEach(function (el) {
                el.tabIndex = (el === self) ? 0 : -1;
            });

            container.tabIndex = -1;
        });

        /*
            ↑↓・Home・End で前後へ。
            【焦点がスタンプ・画像に乗っている間だけ】ログの枠に焦点があるときは
            何もしない（↑↓はブラウザのスクロールのまま）
        */
        $(document).on('keydown', ITEM, function (e) {

            var container = $(this).closest(CONTAINER)[0];

            if (!container) {
                return;
            }

            var items = itemsOf(container);
            var index = items.indexOf(this);
            var next;

            if (e.key === 'ArrowUp') {
                next = Math.max(0, index - 1);
            } else if (e.key === 'ArrowDown') {
                next = Math.min(items.length - 1, index + 1);
            } else if (e.key === 'Home') {
                next = 0;
            } else if (e.key === 'End') {
                next = items.length - 1;
            } else {
                return;
            }

            e.preventDefault();

            if (items[next] && items[next] !== this) {
                items[next].focus();   /* 見えていなければ、ブラウザが見える位置まで送る */
            }
        });

        return {
            /* 行を足した・描き直した・窓を開いたあとに呼ぶ */
            refresh: function (container) {
                if (container) {
                    schedule(container);
                }
            }
        };
    })();


    /* === 現在時刻を HH:mm 形式にする === */
    function formatTime(date) {
        var hh = ('0' + date.getHours()).slice(-2);
        var mm = ('0' + date.getMinutes()).slice(-2);
        return hh + ':' + mm;
    }

    /*
        === 日付を "2026年8月29日" 形式にする ===

        サーバー側（ChatLogReader）が過去ログに載せてくる日付と、まったく同じ形に
        すること。この2つは文字列として比較され、違っていれば区切り行が入るため、
        形が食い違うと、リアルタイム受信ぶんの先頭で毎回よけいな区切り行が出る

        【罠】過去ログの日付を、この関数で作ってはいけない。
        時刻とまったく同じ理由で、過去の行がすべて「今日」になってしまう。
        過去ログは、サーバーが確定させた m.date をそのまま使うこと。
        この関数を使ってよいのは、「今」であることが確実な、
        リアルタイム受信ぶんと自分の入室行だけ
    */
    function formatDate(date) {
        return date.getFullYear() + '年' +
               (date.getMonth() + 1) + '月' +
               date.getDate() + '日';
    }

    /* 今日の日付（リアルタイムで追加する行に使う） */
    function todayDateText() {
        return formatDate(new Date());
    }

    /*
        === 最後に描画した行の日付（"2026年8月29日"）===
        日付が変わる位置に区切り行を1本だけ入れるために、直前の行の日付を覚えておく
    */
    var lastRenderedDate = null;


    /* ========================================================
       新着の追従（LINE・Discordと同じ動き）

       直す前は、行が増えるたびに必ず最下部へ飛ばしていた。そのため
       過去ログを遡って読んでいる最中に他人が発言すると、引きずり降ろされた。

       ここから下の3つの値は、あとから変えることになる。
       画面やJSのあちこちに書き散らさず、必ずここだけを直せば済む形にしてある
       ======================================================== */

    /*
        【実機で調整する値】チャットログの下端から何px以内なら「下端に居る」とみなすか。

        0にすると、指1本ぶんスクロールしただけで追従が切れて使いにくい。
        大きくしすぎると、読んでいる途中でも引きずり降ろされる。
        80pxにしてあるのは、発言1行の高さ（約47px）＋行間（14px）より少し広い、
        つまり「最後の1行が見えている間は、下端に居るとみなす」という考え方から。
        スマホの実機で触って、追従が切れるのが早すぎる／遅すぎると感じたら、
        この数字だけを変えること
    */
    var SCROLL_FOLLOW_THRESHOLD_PX = 80;

    /* 「新着あり」ボタンの文言（目で見る人向け） */
    var NEW_MESSAGE_LABEL = '新着あり';

    /*
        「新着あり」が出たことを、読み上げソフトにだけ知らせる文言。
        ボタンの文言そのものを読ませるのではなく、何が起きたかと、
        どう操作すればよいかまで含めて伝える
    */
    var NEW_MESSAGE_ANNOUNCE_TEXT = '新しい発言があります。「新着あり」ボタンで最新の発言へ移動できます。';

    /*
        行を足すときの、スクロールのしかた。

        【なぜ真偽値（skipScroll）をやめたのか】
        「飛ばす／飛ばさない」の2択では、新しく必要になった
        「どこを読んでいても必ず飛ばす（自分の発言）」が表せない。
        真偽値をもう1つ足すと、呼び出し側が true, false と並ぶだけの読めない形になる。
        名前の付いた選択肢にしておけば、呼び出し側を見ただけで意図が分かる

        skip       … スクロールしない（過去ログの一括描画。最後に一度だけ呼ぶ）
        auto       … 下端に居るときだけ追従する。居なければ「新着あり」を出す（他人の発言）
        auto-quiet … 下端に居るときだけ追従する。居なくても**何も知らせない**（入退室のお知らせ）
        force      … どこを読んでいても必ず最下部へ（自分の発言・自分の入室）
    */
    var SCROLL_SKIP = 'skip';
    var SCROLL_AUTO = 'auto';
    var SCROLL_AUTO_QUIET = 'auto-quiet';
    var SCROLL_FORCE = 'force';

    /*
        直前まで下端に張り付いていたか。

        【これは、画面の高さが変わったときのためだけに持っている控えである】
        行を足すときの判定には使わない。あちらは、足す前にその場で測る
        （measureしてから足せば、必ず正しい。控えが古くなる余地が無い）。

        控えが要るのは、ソフトキーボードが出てチャットログが縮んだときだけ。
        その瞬間は、利用者が指を動かしていないのに下端からの距離が広がるため、
        「縮む前はどうだったか」を、スクロールした時点の記録から知る必要がある
    */
    var isFollowingBottom = true;

    /* 下端付近に居るか（実際に測る） */
    function isChatLogNearBottom() {
        var el = $('#chatLog')[0];
        return (el.scrollHeight - el.scrollTop - el.clientHeight) <= SCROLL_FOLLOW_THRESHOLD_PX;
    }

    /*
        === こっそりの窓が全面の間は、部屋の「新着あり」を出さない===

        理由：押しても、その下の部屋は窓に隠れていて見えないため。
        【出さないだけで、忘れはしない】全面の間に出すはずだったことを控えておき（newMessageBadgeWaiting）、
        窓を閉じて部屋が見えるようになった時点で、まだ下端に戻っていなければ出す（syncNewMessageBadge）。
        全面に入った時点で既に出ていたものも、いったん引っ込めて控えに回す。

        【止めるときは、この1行】false にすると、全面の間も今までどおり出る
        （実機で「出ていたほうが分かりやすい」となったときのため。開発者の指定）
    */
    var HIDE_NEW_MESSAGE_BADGE_WHILE_PRIVATE_FULL = true;

    var newMessageBadgeWaiting = false;

    /* 今は「新着あり」を出さずに控えておくときか */
    function isNewMessageBadgeHeldBack() {

        /* PrivateTalk はこの下で作られる。起動の途中で呼ばれても止まらないように、在るかを見てから聞く */
        return HIDE_NEW_MESSAGE_BADGE_WHILE_PRIVATE_FULL &&
            typeof PrivateTalk !== 'undefined' && !!PrivateTalk && PrivateTalk.isFullScreen();
    }

    /* 「新着あり」を出す */
    function showNewMessageBadge() {

        /*
            既に出ているなら、何もしない。
            ここで抜けないと、新着が来るたびに読み上げソフトが同じ文言を読み直し、
            会話が続いている間ずっと喋り続けることになる
        */
        if ($('#newMessageBtn').hasClass('is-visible')) {
            return;
        }

        /* 全面の間は、出さずに控えるだけ（読み上げの文言も入れない） */
        if (isNewMessageBadgeHeldBack()) {
            newMessageBadgeWaiting = true;
            return;
        }

        $('#newMessageBtn').addClass('is-visible');
        $('#newMessageAnnounce').text(NEW_MESSAGE_ANNOUNCE_TEXT);
    }

    /*
        全面に入った／出たときに呼ぶ（PrivateTalk.syncFullScreen）。

          入った … 出ていたら引っ込めて、控えに回す
          出た   … 控えがあって、まだ下端に戻っていなければ出す
                   （全面の間に下端まで追従していた人には、もう知らせることが無い）
    */
    function syncNewMessageBadge() {

        if (isNewMessageBadgeHeldBack()) {
            if ($('#newMessageBtn').hasClass('is-visible')) {
                hideNewMessageBadge();
                newMessageBadgeWaiting = true;
            }
            return;
        }

        if (newMessageBadgeWaiting) {
            newMessageBadgeWaiting = false;
            if (!isChatLogNearBottom()) {
                showNewMessageBadge();
            }
        }
    }

    /* 「新着あり」を消す */
    function hideNewMessageBadge() {

        /* 下端へ戻った・押された、のどちらでも、控えももう要らない */
        newMessageBadgeWaiting = false;

        if (!$('#newMessageBtn').hasClass('is-visible')) {
            return;
        }

        $('#newMessageBtn').removeClass('is-visible');

        /*
            【必須】読み上げ用の文言も空に戻す。
            aria-liveは「中身が変わったとき」に読むため、文言を残したままだと
            次に新着が来ても同じ文字列のままで、一言も読まれなくなる
        */
        $('#newMessageAnnounce').text('');
    }

    /*
        ボタンの文言は、マークアップに直接書かずにここで入れる。
        上の定数1か所を直せば、画面の表示も読み上げの案内も一緒に変わる
    */
    $('#newMessageBtnText').text(NEW_MESSAGE_LABEL);

    /*
        === 行をchatLogの末尾に追加する（すべての行が、必ずここを通る）===

        rowHtml  … 追加する行のHTML（吹き出し・イベント行のどちらも）
        dateText … その行の日付（"2026年8月29日"）。
                   過去ログはサーバーから受け取った値、リアルタイムぶんは todayDateText()
        scrollMode … 末尾へのスクロールをどうするか。上のSCROLL_*のいずれか。
                   省略した場合はSCROLL_AUTO（下端に居るときだけ追従する）

        【なぜ、1つの関数に集約するのか】
        日付の区切り行を、過去ログとリアルタイムで別々に判定すると、
        その境目で区切りが二重に出たり、逆に抜けたりする。
        「最後に描画した行の日付」を1つだけ持ち、行を足すときに必ずここを通せば、
        どちらの経路から来た行でも、区切りは正しく1本だけ入る。
        追従するかどうかの判断も、同じ理由でここ1か所に集めてある
    */
    function appendRowHtml(rowHtml, dateText, scrollMode) {

        /*
            【重要】追従するかどうかは、行を足す「前」に、その場で測って決める。

            足したあとに測ると、scrollHeightが伸びたあとの値になるため、
            下端に張り付いて読んでいた人まで「下端ではない」と判定されてしまう。

            scrollイベントで更新している控え（isFollowingBottom）を読む形にもできるが、
            ここでは採らない。控えは「イベントが必ず届いていること」が前提になり、
            届かなかった一度が、追従が二度と効かない状態として残る。
            まだ足していない今この瞬間のDOMは、利用者が見ているそのものなので、
            測れば必ず正しい
        */
        var wasFollowing = isChatLogNearBottom();

        /*
            日付が変わっていれば、先に区切り行を差し込む
            （dateTextが無い場合は、判定できないので区切りを入れない）
        */
        if (dateText && dateText !== lastRenderedDate) {
            $('#chatLog').append(
                '<div class="date-separator"><span>' + escapeHtml(dateText) + '</span></div>');
            lastRenderedDate = dateText;
        }

        $('#chatLog').append(rowHtml);

        /*
            Tab で止まるスタンプ・画像を選び直す（次のコマで。下のスクロールが済んだあとになる）。
            焦点がログの中にある人の位置は動かさない（LogKeyNav の決まり）
        */
        LogKeyNav.refresh(document.getElementById('chatLog'));

        /* 過去ログの一括描画。1件ごとにスクロールせず、呼び出し元が最後に一度だけ行う */
        if (scrollMode === SCROLL_SKIP) {
            return;
        }

        /*
            自分の発言・自分の入室（SCROLL_FORCE）は、どこを読んでいても必ず最下部へ。
            自分が打った発言が画面の外に出るチャットアプリは存在しない。
            それ以外は、下端に張り付いて読んでいた人だけを連れていく
        */
        if (scrollMode === SCROLL_FORCE || wasFollowing) {
            scrollChatLogToBottom();
            return;
        }

        /*
            === ここから下は、上のほうを読んでいる最中だった場合 ===

            【「新着あり」は、発言のときだけ出す】
            以前は入退室のお知らせでも出していた。押しても入退室の行しか無く、
            **読み逃したら困るもの＝発言**に対して出す知らせなので、そこと合っていなかった。

            追従のほうは残してある（上の wasFollowing の分岐）。
            下端で読んでいる人は、入退室の行が来ても下端のまま付いていくのが自然なため。
            **変えたのは「知らせるかどうか」だけで、「連れていくかどうか」ではない**
        */
        if (scrollMode === SCROLL_AUTO_QUIET) {
            return;
        }

        /* 引きずり降ろさず、発言が来たことだけ知らせる */
        showNewMessageBadge();
    }

    /*
        === 入退室などのお知らせ1件分を、chatLogの末尾に追加する ===

        eventText … 表示する文言。サーバー側（ChatEventText）が組み立てたものを、
                    そのまま表示する。クライアント側で文言を作らないこと
        dateText  … その行の日付（区切り行の判定に使う）
        scrollMode … 上のSCROLL_*のいずれか。省略時はSCROLL_AUTO
                     （入退室のお知らせは SCROLL_AUTO_QUIET を渡すこと。
                       「新着あり」は発言のときだけ出す、という決まりのため）

        吹き出しではなく、中央寄せの小さいグレー1行として表示する。
        自分の入退室行も、他人のものとまったく同じ見た目にする（左右の寄せは無し）
    */
    function appendSystemMessageRow(eventText, dateText, scrollMode) {

        /*
            文言が無ければ、行を作らない
            （サーバーが「イベント行として表示すべきでない」と判断した種別）
        */
        if (!eventText) {
            return;
        }

        var rowHtml = '<div class="system-message">' + escapeHtml(eventText) + '</div>';

        appendRowHtml(rowHtml, dateText, scrollMode);
    }

    /*
        === 発言1件分の吹き出しHTMLを組み立てて、chatLogの末尾に追加する ===
        time     … 表示する時刻（"HH:mm"）。省略時は現在時刻を使う。
                    過去ログの描画時は、サーバー側が整形した発言当時の時刻を渡す
        dateText … その行の日付（区切り行の判定に使う）
        scrollMode … 上のSCROLL_*のいずれか。省略時はSCROLL_AUTO。
                    過去ログの描画ではSCROLL_SKIPを渡し、1件ごとにスクロールが
                    走るのを避ける（最後に一度だけまとめて行う）。
                    自分の発言はSCROLL_FORCE（下端に居なくても、必ず最下部へ）
    */
    /* ============================================================
       絵文字だけの発言は、大きく出す

       LINEの実機で確かめた挙動に合わせてある——**3個までは大きく、4個以上は普通。**
       絵文字を1つだけ送ったときに、豆粒で出るのは寂しい、という話である。

       【吹き出しは消さない】
       LINEは吹き出しの背景ごと消すが、vb-chatは**吹き出しの縁の色で発言者を示す**
       設計なので、そこを消すと「誰の発言か」の手がかりが減る。
       変えるのは文字の大きさだけにしてある
       ============================================================ */

    /* ここまでなら大きく出す。4個以上は普通の大きさ */
    var EMOJI_ONLY_MAX_COUNT = 3;

    /*
        吹き出しのしっぽ（SVGへ作り替えた）

        【なぜSVGなのか】
        それまではCSSの三角を2枚重ねて、はみ出したぶんを線に見せていた。
        あの作りだと、**斜めの線の太さが「2枚の横のずれ ÷ √2」で決まる。**
        等倍の画面ではそのずれを整数（＝画面の点）にしか置けないので、
        見え幅は 0.71px 刻みでしか選べず、**本体2pxに釣り合う値が存在しなかった**
        （1.41pxは細すぎ、2.12pxは太すぎ。どちらも実機で指摘された）。
        SVGの線は引いた太さがそのまま一様に効くので、この問題自体が消える。

        【色は、ここでは渡していない】
        吹き出しに付けるインラインの --bubble-border-color を、SVGが受け継ぐ
        （CSS変数は子へ届く）。**JS側で色を書かないこと**——
        書くと、同じ色を2か所で持つことになる。

        【aria-hidden】しっぽは飾りなので、読み上げソフトには読ませない。
        focusable="false" は、古いブラウザがSVGをタブ順に入れてしまうのを防ぐため。

        【左右対称の三角。上下とも同じ角度の斜め】
、それまでの「上辺が水平・下辺が斜め」から作り替えた。
        **同じ太さで引いても、斜めの線は水平な線より太く見える**
        （水平は画面の点の並びに乗るが、斜めは乗らないので輪郭が広がる）。
        上下とも斜めなら2本が同じ性質になり、見え方が揃う。

        【3つに分けてある理由。順番も含めて意味がある】
        - fill  … 塗り。**輪郭より先に描く**（あとだと輪郭の内側半分を塗りつぶす）
        - line  … 輪郭。**目盛り17まで伸ばして、吹き出しの枠線の帯を完全にまたぐ**
        - cover … 白い覆い。**輪郭より後に描く。** 目盛り16（＝吹き出しの白い内側の端）から
          右を白く塗りつぶし、内側へはみ出した輪郭を隠す

        【なぜ輪郭を伸ばしたか】
        以前は輪郭を目盛り15で止め、**届かない15〜16は吹き出しの枠線に輪郭を続けさせていた。**
        これが「ちょうど接する」の仲間で、**ChromeOSの実機でしっぽが吹き出しから
        離れて見えた**（ウィンドウズのズーム1.25でも再現した）。
        受け渡しをやめ、**枠線の帯の上に輪郭を重ねる**形にした。

        座標と、18・16という数字の意味は Content/ChatRoom.css の「6-2」にある。
        **d と viewBox は、あちらの width/height と対になっている。片方だけ変えないこと**
    */
    var BUBBLE_TAIL_SVG =
        '<svg class="message-tail" width="18" height="16" viewBox="0 0 18 16"' +
        ' aria-hidden="true" focusable="false">' +
        '<path class="message-tail-fill" d="M4 8 L18 1 L18 15 Z" />' +
        '<path class="message-tail-line" d="M17 1.5 L4 8 L17 14.5" />' +
        '<rect class="message-tail-cover" x="16" y="0" width="2" height="16" />' +
        '</svg>';

    /*
        【重要】ここだけ、文字数の数え方と、わざと揃えていない

        発言の文字数（500文字の判定・残り文字数の表示）は、UTF-16の単位で数えている。
        JSの .length、VBの String.Length、DBの NVARCHAR、SQLの LEN() の4つを
        揃えるためで、決めたことである。

        **こちらは書記素（人が「1文字」と感じる単位）で数える。** 役割が違うため。

          文字数制限   … 「どれだけ保存できるか」の話   → UTF-16単位（DBの列幅と揃える）
          絵文字の個数 … 「見た目に何個見えるか」の話   → 書記素単位（人が数える単位）

        UTF-16で数えると、こうなってしまう（実測）。

          👨‍👩‍👧   UTF-16=8  書記素=1
          👋🏻     UTF-16=4  書記素=1
          ❤️      UTF-16=2  書記素=1

        つまり 👨‍👩‍👧 を1個だけ送っても「8個」と数えられ、大きくならない。

        **「揃っていない」と思って直しに来ないこと。** 揃えたら壊れる
    */
    var emojiGraphemeSegmenter =
        (typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function')
            ? new Intl.Segmenter('ja', { granularity: 'grapheme' })
            : null;

    /*
        その書記素が、絵文字かどうか

        【\p{Emoji} を使ってはいけない】実測すると、こうなる。

          "1"  \p{Emoji}=true   \p{Extended_Pictographic}=false
          "#"  \p{Emoji}=true   \p{Extended_Pictographic}=false
          "*"  \p{Emoji}=true   \p{Extended_Pictographic}=false
          "😀" \p{Emoji}=true   \p{Extended_Pictographic}=true

        \p{Emoji} は「絵文字として表示されうる文字」で、数字や # まで含む。
        「123」が大きく表示されることになるので、Extended_Pictographic のほうを使う。

        【ただし Extended_Pictographic だけでは、2種類の絵文字が漏れる】（実測で発見）

          🇯🇵 （旗）      … 中身は地域表示記号2つ。Extended_Pictographic=false
          1️⃣ （キーキャップ）… 中身は「1」＋U+FE0F＋U+20E3。同じく false

        どちらもピッカーに載っている普通の絵文字なので、拾えるようにしてある。
        「1」だけなら U+20E3 が付かないので、これで数字が紛れ込むことはない。

        【割り切り】© ® ™ は Extended_Pictographic=true なので、
        これらだけの発言は大きくなる。実害が無いので、そのままにしてある
        （開発者の判断）
    */
    var EMOJI_PICTOGRAPHIC = /\p{Extended_Pictographic}/u;
    var EMOJI_REGIONAL_INDICATOR = /[\u{1F1E6}-\u{1F1FF}]/u; /* 旗 */
    /* U+20E3 は目に見えない結合文字なので、必ずエスケープで書くこと。
       そのまま貼ると、ソースの上では「消えた」ようにしか見えない */
    var EMOJI_KEYCAP = /\u20E3/;

    function isEmojiGrapheme(grapheme) {
        return EMOJI_PICTOGRAPHIC.test(grapheme) ||
               EMOJI_REGIONAL_INDICATOR.test(grapheme) ||
               EMOJI_KEYCAP.test(grapheme);
    }

    /*
        絵文字だけの発言なら、その個数（書記素の数）を返す。
        そうでなければ 0 を返す

        【0 の意味は、ひとつだけ】「絵文字だけの発言ではない」。
        上限を超えたときは 0 ではなく EMOJI_ONLY_MAX_COUNT + 1 を返す。
        どちらも「大きくしない」という同じ結果になるが、
        0 に混ぜてしまうと、あとで数を見て調べたいときに区別が付かなくなる

        空白（半角・全角）は取り除いてから見る。「😀 😀」は2個の絵文字である

        【Intl.Segmenter が無ければ、あきらめて 0 を返す】
        Chrome・Safari は問題ないが、Firefox の対応は2024年と新しいため、
        古い環境では存在しないことがある。
        **無いときは、大きくしないだけ。何も壊れない。**
        自前で書記素を切り分ける処理を書き起こす価値は無い
        （絵文字の結合の規則は毎年増える。追い続けることになる）
    */
    function countEmojiOnlyMessage(text) {

        if (!emojiGraphemeSegmenter || !text) {
            return 0;
        }

        /* 半角スペース・タブ・改行と、全角スペース（U+3000）を落とす */
        var trimmed = text.replace(/[\s　]/g, '');

        if (trimmed === '') {
            return 0;
        }

        var count = 0;
        var segments = emojiGraphemeSegmenter.segment(trimmed);

        /*
            1つでも絵文字でないものが混ざっていたら、そこで打ち切る。
            「こんにちは😀」は、絵文字だけの発言ではない
        */
        for (var iterator = segments[Symbol.iterator](), step = iterator.next();
             !step.done;
             step = iterator.next()) {

            if (!isEmojiGrapheme(step.value.segment)) {
                return 0;
            }

            count++;

            /*
                上限を超えた時点で、もう数える意味が無い。
                絵文字を何百個も貼られたときに、全部数えないための足切り
                （返すのは「上限より多い」という事実だけで、正確な個数ではない）
            */
            if (count > EMOJI_ONLY_MAX_COUNT) {
                return count;
            }
        }

        return count;
    }


    /*
        trip … 送り主のトリップ（無ければ null／undefined）。相手の発言の行にだけ出る
               （自分の発言には、そもそも送信者の欄が無い）。
               末尾に足したのは、呼び出し2か所（receiveMessage と renderLogEntries）以外に
               影響を出さないため
    */
    function appendMessageRow(nickname, message, iconType, iconColor, isSelf, time, dateText, scrollMode, trip) {

        var safeNickname = escapeHtml(nickname);

        /*
            本文。**URL はリンクになる**（上の「オートリンク」）。
            エスケープは buildMessageTextHtml の中で、URL の部分と地の文に分けてから掛かる
        */
        var bodyTextHtml = buildMessageTextHtml(message);

        /*
            アイコン色・アイコン種別も、属性の中へ入れる以上は必ずescapeAttrを通す。

            【役割が、サーバー側の検査とは別物であること】
            ChatHub.JoinRoomでも、この2つはサーバー側で検査している（入口の防御）。
            ただしそれで止められるのは、これから入ってくる新しい値だけで、
            既にChatMessagesへ保存されている値は、過去ログとして何度でもここへ来る。
            既存の値を無害化できるのは、この出口側しかない。
            「サーバーで検査したから、ここは素通しでよい」としないこと
        */
        var safeIconColor = escapeAttr(iconColor);
        var safeIconType = escapeAttr(iconType);

        if (!time) {
            time = formatTime(new Date());
        }

        /*
            送信者（アイコンが上、名前が下の縦並び）。相手の発言にだけ出す（下の rowHtml）。
            名前はCSS側で2行に打ち切られるため、省略されても全文が分かるよう
            title属性にも入れておく。属性の中はescapeAttr（ダブルクォートも変換する）

            data-nickname … 押すと入力欄へ「〇〇：」を入れるための名前（
            ReplyAddress の上に理由がある）。title は省略されたときの全文を見せる役目なので、
            役目の違う値として別に持つ
        */
        var senderHtml =
            '<div class="message-sender" data-nickname="' + escapeAttr(nickname) + '">' +
            '<div class="message-avatar">' +
            '<svg viewBox="0 0 512 512" style="color: ' + safeIconColor + ';">' +
            '<use href="#icon-' + safeIconType + '"></use>' +
            '</svg>' +
            '</div>' +
            '<span class="message-nickname" title="' + escapeAttr(nickname) + '">' + safeNickname + '</span>' +

            /* 名前の下の行にトリップかアカウントID（どちらも無い人は空文字＝行は増えない）。
               アカウントID は押せるが Tab では止まらない（buildTripHtml の 'quiet'） */
            buildTripHtml(trip, 'message-trip', 'quiet') +
            '</div>';

        /*
            絵文字だけの発言（3個まで）は、文字を大きくする。

            【判定を、ここに置いている理由】
            appendMessageRow は、リアルタイムの受信も過去ログの描き直しも、
            必ず通る1か所である（呼び出しは receiveMessage と renderLogEntries の2つだけ）。
            片方にだけ書くと、「送った直後は大きいのに、F5すると普通に戻る」ことになる
        */
        var emojiOnlyCount = countEmojiOnlyMessage(message);
        var isEmojiOnly = (emojiOnlyCount >= 1 && emojiOnlyCount <= EMOJI_ONLY_MAX_COUNT);

        /*
            画像の発言（本文が、サーバーの組み立てた印そのもの）。

            【判定を、ここに置いている理由】
            絵文字だけの発言の判定と、まったく同じである——
            appendMessageRow は、リアルタイムの受信も過去ログの描き直しも
            必ず通る1か所なので、片方にだけ書くと
            「送った直後は出るのに、F5すると印の文字列が出る」ことになる
        */
        var image = parseImageToken(message);

        /* スタンプの発言（判定を置く場所の理由は、上の画像とまったく同じ） */
        var stamp = image ? null : parseStampToken(message);

        /*
            吹き出しの中身。

            【画像・スタンプのときは safeMessage を出さないこと】
            出すと、絵の下に「[img:…]」「[stamp:…]」という文字列が並ぶ
        */
        var bodyHtml = image
            ? buildImageHtml(image, false)
            : (stamp ? buildStampHtml(stamp) : bodyTextHtml);

        var bubbleHtml =
            '<div class="message-bubble message-bubble--' + (isSelf ? 'self' : 'other') +
            (isEmojiOnly ? ' message-bubble--emoji-only' : '') +
            (image ? ' message-bubble--image' : '') +
            (stamp ? ' message-bubble--stamp' : '') + '"' +
            ' style="border-color: ' + safeIconColor + '; --bubble-border-color: ' + safeIconColor + ';">' +
            bodyHtml +

            /* しっぽ。絵文字だけ・画像・スタンプの発言では、CSS側が display: none で消す
               （ここで出し分けないのは、消す条件を1か所＝CSSに寄せておくため） */
            BUBBLE_TAIL_SVG +

            '</div>';

        var timeHtml = '<span class="message-timestamp">' + time + '</span>';

        /*
            時刻・吹き出し・送信者を、1行に横並びにする。
                自分  … [時刻] [吹き出し]
                相手  … [送信者] [吹き出し] [時刻]

            【自分の発言には、アイコンと名前を出さない】（LINEと同じ形）
            右に寄っていること自体が「自分の発言」の印になる。
            **ただし左右の寄せは、目の見えない人には伝わらない**ので、
            読み上げ専用の「自分の発言」を行の頭に置いてある（.visually-hidden。
            position: absolute なので、行の並び・すき間には関わらない）。
            こっそりの窓の中は左右に分けていない作りなので、あちらは今までどおり名前を出す

            【画像・スタンプの行には message-row--media を付ける】
、CSS はこの印を使っていない。狭い画面でも、画像・スタンプの行は
            文章の行とまったく同じ並び（[送信者] [絵] [時刻]）になった。
            印は、並びを変えたくなったときの足場として残してある（Content/ChatRoom.css の「6. 相手の発言」）
        */
        var mediaClass = (image || stamp) ? ' message-row--media' : '';

        var rowHtml = isSelf
            ? '<div class="message-row message-row--self' + mediaClass + '">' +
                '<span class="visually-hidden">自分の発言</span>' + timeHtml + bubbleHtml + '</div>'
            : '<div class="message-row message-row--other' + mediaClass + '">' +
                senderHtml + bubbleHtml + timeHtml + '</div>';

        /* 追加・日付の区切り・スクロールは、共通の関数に任せる */
        appendRowHtml(rowHtml, dateText, scrollMode);
    }

    /*
        === チャットログを最下部までスクロールする ===

        最下部へ行けば「下端に張り付いている」状態に戻り、
        未読の知らせも用済みになる。3つは必ず一緒に動くので、ここでまとめて面倒を見る
        （呼び出し側にそれぞれ書かせると、いつか片方だけ書き忘れる）
    */
    function scrollChatLogToBottom() {
        var chatLogEl = $('#chatLog')[0];
        chatLogEl.scrollTop = chatLogEl.scrollHeight;
        isFollowingBottom = true;
        hideNewMessageBadge();
    }

    /*
        利用者がチャットログをスクロールしたときにすること
          1. 画面の高さが変わったとき用の控えを取り直す
          2. 自分で下まで戻ってきたなら、新着の知らせを消す

        passive: true は「このハンドラは preventDefault を呼ばない」という宣言で、
        スクロールの滑らかさを損なわないために付けている（中でやるのは測定だけ）
    */
    $('#chatLog')[0].addEventListener('scroll', function () {

        isFollowingBottom = isChatLogNearBottom();

        if (isFollowingBottom) {
            hideNewMessageBadge();
        }
    }, { passive: true });

    /* 「新着あり」を押したら、最新の発言まで移動する */
    $('#newMessageBtn').on('click', function () {

        /* 「新着あり」の表示を消すのも、この中でやっている */
        scrollChatLogToBottom();

        /*
            【フォーカスの行き先】
            押した瞬間にボタン自身が消えるため、フォーカスの置き場所が無くなる。
            何もしないと、キーボードや読み上げソフトで使っている人が、
            ページの先頭までフォーカスを飛ばされて読んでいた場所を見失う。

            入力欄へ移さないのは、スマホでソフトキーボードが勝手に開いてしまうため
            （読みに戻っただけの人にとっては邪魔でしかない）。
            preventScroll は、フォーカスを当てた拍子に位置がずれるのを防ぐ
            （この指定を知らない古いブラウザは、引数を無視するだけで害はない）
        */
        $('#chatLog')[0].focus({ preventScroll: true });
    });

    /*
        画面の高さが変わったとき（ソフトキーボードの開閉・画面の回転）

        【必要な理由】
        viewportに interactive-widget=resizes-content を指定したため、
        キーボードが出るとページそのものが縮み、チャットログの高さも縮む。
        このとき scrollTop は動かないので、下端に張り付いていたはずの人が、
        キーボードの高さのぶんだけ下端から離れた位置に取り残される。
        追従していた人は、そのまま下端へ連れ直す
    */
    $(window).on('resize', function () {
        if (isFollowingBottom) {
            scrollChatLogToBottom();
        }
    });


    /*
        === 過去ログの描画（DBから読み出したぶん） ===

        会話ログはDBに一本化している。以前はsessionStorageへ保存・復元していたが、
        次の3つの理由で廃止した。
          1. 入退室のイベント行も、同じ経路で時系列に並べたいため
          2. 「後から入室した人にも、既存のログを見せる」という要件そのものが
             DBからの読み出しであるため
          3. 二重管理による、件数・並び順のバグを避けるため

        【重要】sessionStorage自体をやめたわけではない。
        退室の理由の受け渡し（vbchat_leave_reason）では今も使っている。
        「sessionStorageを全部消す」ような実装には、絶対にしないこと

        vbChatInitialLogは、Page_Loadが読み出した過去ログ。
        このファイルの下のほう（scriptタグの直前）で、サーバーから埋め込んでいる
    */

    /*
        ログの種別（サーバー側のChatMessageType Enumと対応している）
        10 … 発言
    */
    var MESSAGE_TYPE_MESSAGE = 10;

    /*
        過去ログを画面へ描画する

        SignalRの接続を開始する前に、これを済ませておく。
        以降はリアルタイム受信ぶんだけを追記するため、
        過去ログとリアルタイムぶんが二重に表示されることは原理的に起きない

        ※割り切り：Page_Loadでのログ取得から、SignalRの接続完了までの
        　数百ミリ秒の間に他人が発言すると、その1件は抜ける可能性がある
    */
    function renderInitialLog() {
        renderLogEntries(vbChatInitialLog);

        /*
            こっそりの窓も、ここで作り直す。

            【F5しても、それまでのやりとりが残るのは、この一行のおかげである】
            窓の中身はブラウザ側には残っていない。サーバーが
            「自分が当事者の行」だけを返してくるので、そこから作り直している。
            宛先の印（RecipientBrowserId）をログに残した理由が、まさにこれである

            【参加者一覧より先に走る】
            そのため、この時点では窓の名前が分からないものがある
            （自分が送った行しか無い窓）。JoinRoom の結果が届いたあとに
            refreshNicknames / refreshPresence を呼び直して揃える
        */
        PrivateTalk.renderPastLog(vbChatPrivateLog);
    }

    /*
        === 描き直しかたの3択 ===

        【真偽値にしないこと】
        以前は preserveReadingPosition という真偽値1つだった。そこへ
        「新着ありを出すかどうか」が加わったので、真偽値をもう1つ足すと
        呼び出し側が `true, false` と並ぶだけの読めない形になる。
        appendRowHtml の SCROLL_SKIP / SCROLL_AUTO / SCROLL_AUTO_QUIET / SCROLL_FORCE と
        まったく同じで、**名前が付いていれば、呼び出し側を見ただけで意図が分かる**
    */

    /* 最下部へ飛ばす（初回の描画・表示行数の変更。利用者自身の操作が起点で、
       最新の発言が見えている状態から始まるのが自然なため） */
    var REDRAW_TO_BOTTOM = 'to-bottom';

    /* 読んでいた位置を保ち、下端の行が入れ替わっていれば「新着あり」を出す（再接続）。
       再接続は利用者が起こしたことではなく、読んでいる途中に横から起きた出来事なので、
       他人の発言が届いたのとまったく同じ扱いにする */
    var REDRAW_KEEP_POSITION = 'keep-position';

    /* 読んでいた位置は保つが、「新着あり」は出さない（無視の切り替え）。

       【なぜ、新着ありを出してはいけないのか】
       増えた（減った）行は新着ではなく、**利用者が自分で「見せて」「隠して」と
       押した結果**である。押した本人に「新着があります」と知らせるのはおかしい。

       【出てしまう条件は、実測で確かめてある】
       「無視した相手の行が、その時点でログのいちばん最後にあるとき」だけ出ていた。
       下端の行が入れ替わったことを新着と判定していたため。
       相手の行が途中にあるだけなら最後の行は変わらないので、出なかった
       ——**端末の違いではなく、そのときログの最後に誰の行があったかの違いだった** */
    var REDRAW_KEEP_POSITION_QUIET = 'keep-position-quiet';


    /*
        過去ログの配列を、チャットログへ丸ごと描画する

        呼ばれるのは4か所。どれも「今あるものを消して、渡されたぶんを描く」
        という同じ動きのため、1つの関数にまとめてある
          1. ページを開いたときの初回描画            … REDRAW_TO_BOTTOM
          2. 表示行数を変えたとき                    … REDRAW_TO_BOTTOM
          3. 再接続に成功したとき                    … REDRAW_KEEP_POSITION
          4. 無視の設定を切り替えたとき              … REDRAW_KEEP_POSITION_QUIET

        redrawMode … 上の3つの定数のどれか（省略すると REDRAW_TO_BOTTOM と同じ）
    */
    function renderLogEntries(entries, redrawMode) {

        /* 読んでいた位置を保つか（保たないなら、最下部へ飛ばす） */
        var keepPosition = (redrawMode === REDRAW_KEEP_POSITION ||

                            redrawMode === REDRAW_KEEP_POSITION_QUIET);
        var chatLogEl = $('#chatLog')[0];

        /*
            【必ず、消す前に測ること】
            empty() したあとでは scrollHeight も scrollTop も別物になり、
            「さっきまで下端に居たか」を知る手段が無くなる
            （行を足す前に測る、という appendRowHtml の決まりと同じ理由）
        */
        var wasAtBottom = isChatLogNearBottom();
        var prevScrollTop = chatLogEl.scrollTop;
        var prevLastRowText = $('#chatLog').children().last().text();

        /* 今の表示を、いったん全部消す */
        $('#chatLog').empty();

        /*
            【罠】日付の記憶も、必ずここでリセットすること。
            lastRenderedDateは「最後に描画した行の日付」を覚えているため、
            消さずに描き直すと、先頭の行の日付が「もう描画済み」と判定され、
            日付の区切り行が1本も出なくなる
        */
        lastRenderedDate = null;

        /*
            【罠】hasRenderedMyJoinRow は、ここでリセットしてはいけない。
            描き直しは入室ではないため、戻してしまうと、次に再接続したときに
            自分の入室行が二重に出てしまう
        */

        (entries || []).forEach(function (m) {

            /*
                === 無視している相手の行は、過去ログでも作らない ===

                【ここで返してよい】日付の区切り行は appendRowHtml の中で入るので、
                弾いた行のぶんだけ区切りが取り残されることはない。

                【表示行数が、指定より少なくなる】
                サーバーは無視リストを知らない（＝知らせない設計）ので、
                N行返ってきたあとにここで減らす形になる。
                50行のうち20行がその人の発言なら、30行しか出ない。
                **クライアント側で隠す方式を選んだ以上、原理的にそうなる**
                （開発者が了承済み）
            */
            if (IgnoreList.isIgnored(m.anonId)) {
                return;
            }

            if (m.messageType === MESSAGE_TYPE_MESSAGE) {

                /*
                    時刻・日付・isSelfは、サーバー側が確定させた値をそのまま使う。
                    クライアント側で判定し直してはいけない
                      時刻・日付 … new Date()から作ると、全部「今」になってしまう
                      isSelf     … LocalIdは入室のたびに新しい番号が発行されるため、
                                    判定し直すと自分の過去の発言が相手側の吹き出しになる
                */
                appendMessageRow(m.nickname, m.message, m.iconType, m.iconColor,
                                 m.isSelf, m.time, m.date, SCROLL_SKIP,

                                 /* ログに焼き込まれた、その時点のトリップ（引き直さない）。
                                    アカウントID を出して発言した行なら、そちら（サーバーが今もそのアカウントが在るかを見て付ける） */
                                 idMarkOf(m.trip, m.accountName));

            } else {

                /*
                    発言以外（入退室などのイベント行）。
                    文言はサーバー側（ChatEventText）が組み立て済みのため、そのまま渡す
                */
                appendSystemMessageRow(m.eventText, m.date, SCROLL_SKIP);
            }
        });

        /* 1件ごとではなく、最後に一度だけまとめてスクロールする */

        /*
            初回描画と、表示行数の変更。
            どちらも利用者自身の操作が起点で、最新の発言が見えている状態から
            始まるのが自然なので、今までどおり必ず最下部へ飛ばす。
            下端に張り付いていた人も、同じくそのまま最下部へ

            【この中で「新着あり」も消える】ので、描き直しの前に出ていた知らせは残らない
        */
        if (!keepPosition || wasAtBottom) {
            scrollChatLogToBottom();
            return;
        }

        /*
            ここから下は、再接続の直後に、過去ログを読み返している最中だった場合。

            【なぜ引きずり降ろさないのか】
            再接続は利用者が起こしたことではない。読んでいる途中に横から起きた
            出来事という点で、他人の発言が届いたのとまったく同じ性質である。
            ここで最下部へ飛ばすと、「読んでいる最中に引きずり降ろされる」という、
            スクロール追従（11番のA）で直したはずの症状が、別の入口から戻ってくる

            【割り切り】位置は完全には戻らない。
            表示行数には上限があるので、新しい発言がk行増えると、古いほうがk行落ちる。
            つまり読んでいた行は、k行ぶん上へずれる。
            ここでは scrollTop をそのまま戻しており、増えた行が0のときは正確、
            数行なら数行ぶんずれる。完全に合わせるには行に印が要るので、そこは追わない
        */
        chatLogEl.scrollTop = prevScrollTop;
        isFollowingBottom = false;

        /*
            下端の行が入れ替わっていたら、その人にとっては新着である。
            出ていなければ出す（既に出ていれば showNewMessageBadge の中で何もしない）

            【無視の切り替えでは出さない】
            増えた（減った）行は新着ではなく、**利用者が自分で「見せて」「隠して」と
            押した結果**である。押した本人に「新着があります」と知らせるのはおかしい。

            出ていた条件は実測で確かめてある——
            「無視した相手の行が、その時点でログのいちばん最後にあるとき」だけだった。
            相手の行が途中にあるだけなら、最後の行は変わらないので出ない。
            **端末の違いではなく、そのときログの最後に誰の行があったかの違いだった**
        */
        if (redrawMode === REDRAW_KEEP_POSITION &&
            $('#chatLog').children().last().text() !== prevLastRowText) {
            showNewMessageBadge();
        }
    }

    /*
        === 表示行数（過去ログを何行表示するか）===

        数えるのは「画面に出る行」。発言も、入退室のお知らせ行も、まとめてこの数に入る
        （画面に出さない行＝種別33とその直後の入室は、サーバー側で除いたうえで
        　ちょうどこの行数になるよう調整されている。ChatLogReaderを参照）
    */

    /*
        Cookieを書き込む
        有効期間はサーバーが決める（DB側は ProfileCookieDays。既定365日）。
        Default.aspxでプロフィールのCookieを書いているのと、同じ値・同じ属性を使っている

        【SameSite=Lax】他のサイトに埋め込まれた形での送信を防ぐ。
        Laxにしてあるのは、Strictだと外部リンクから来た1度目のアクセスで
        Cookieが送られず、名前を決めてあるのにトップページへ戻されるため

        【Secure】httpsでしか送らせない。本番はhttps強制なので費用ゼロ。
        開発機のIIS Expressも、localhost はブラウザが安全な場所として扱うため
        http:// のまま受け付ける（実測で確認済み）

        【HttpOnly は付けられない】この画面のJavaScriptが、自分でCookieを
        読んでいるため（上の getCookie。名前・アイコン・色を画面に出すのに使う）。
        付けるとJSから読めなくなり、プロフィールが表示できなくなる。
        「検討したが、この作りでは無理」であって、忘れているのではない

        【罠】ここで encodeURIComponent を掛けてはいけない。値のエンコードは
        呼び出し側の責任という約束を、Default.aspx の setCookie と揃えてある。
        同じ名前の関数が、ページによって違う約束で動いていると、
        片方の書き方をもう片方へ持ち込んだ日に壊れる
        （あちらは、素で書かれた "#2563EB" を掛けてしまうと壊れる）。
        この関数が書くのは ChatLogCount（数字だけ）なので、掛けても掛けなくても
        結果は同じだが、約束のほうを揃えておく
    */
    function setCookie(name, value) {
        document.cookie = name + '=' + value +
                          '; max-age=' + vbChatSettings.cookieMaxAgeSeconds +
                          '; path=/; SameSite=Lax; Secure';
    }

    /*
        === ログを取り直して、描き直す ===

        呼ばれるのは2か所。表示行数を変えたときと、再接続に成功したとき。
        どちらも「サーバーから今のログをもらって、描き直す」というまったく同じ動きなので、
        新しい仕組みは作らず、この1つにまとめてある

          count  … 取り直す行数
          reason … コンソールと計測パネルに出す、何のための取り直しかの説明
          redrawMode … 描き直しかたの3択（renderLogEntries の手前にある定数）

        【なぜ、ページを読み込み直さないのか】
        読み込み直すとSignalRの接続が繋ぎ直しになり、そのぶん会話が途切れる。
        ログだけをAjaxで取り直して描き直せば、接続はそのまま維持できる

        【割り切り】描き直した時点でまだDBに入っていない、ごく直前の発言は、
        一瞬だけ画面から消えることがある。ログの書き込みは配信のあとに行っている
        （表示速度を優先しているため）ので、ほんの短い間だけ差が出る。
        実害が小さいため、ここは追わない
    */
    function fetchAndRenderLog(count, reason, redrawMode) {

        /*
            Friendly URL経由だと問題が出るため、LeaveRoom・KeepAliveの呼び出しと同様、
            物理パスを直接指定する
        */
        return $.ajax({
            type: 'POST',
            url: '/Chat/ChatRoom.aspx/GetLogEntries',
            contentType: 'application/json; charset=utf-8',
            dataType: 'json',
            data: JSON.stringify({ messageCount: count })
        }).done(function (res) {
            handleLogFetchResult(res, reason, redrawMode);
        }).fail(function () {

            /*
                取り直しに失敗した場合は、今出ているログをそのまま残す。
                中途半端に消すより、古いままでも見えているほうがましなため
            */
            debugLog(reason + '：ログの取り直しに失敗');
        });
    }

    /*
        取り直しの結果を、3つに分けて扱う

          成功         … 描き直す
          一時的な失敗 … 今の表示をそのまま残す（DBの詰まりなど。次に呼べば直る）
          恒久的な失敗 … 理由を添えて移動する（ここに居続けても直らない）

        【なぜ分ける必要があるのか】
        以前はどの失敗も「今の表示を残す」で扱っていた。
        一時的な失敗に対しては正しいが、恒久的な失敗に対しては最悪で、
        二度と回復しないのに利用者へ何も知らせないことになる。
        実際、Sessionが切れたときに「再接続もできる・発言もできる・
        しかし切断中の発言だけが永久に出ない」という状態が起きていた
    */
    function handleLogFetchResult(res, reason, redrawMode) {

        var result = null;

        try {
            result = JSON.parse(res.d);
        } catch (e) {
            result = null;
        }

        /*
            形が読めない場合は、一時的な失敗として扱う。
            分からないときに利用者を部屋から追い出す側へ倒さない
        */
        if (!result || typeof result !== 'object') {
            debugLog(reason + '：ログの取り直しに失敗（戻り値が読めない）');
            return;
        }

        /* 恒久的な失敗。理由を持たせて移動する */
        if (result.rejectReason) {

            debugLog(reason + '：' + result.rejectReason);

            /* 正常な離脱のため、このあとのdisconnectedで切断の警告を出さない */
            markLeavingRoom();

            /*
                理由はsessionStorageへ預けて、移動先で帯として出す
                （kicked・入室拒否と、まったく同じ運び方）。

                【サーバー側のSessionでは運べない】
                Sessionが切れていること自体が、断る理由の1つだからである
            */
            saveLeaveReason(result.rejectReason);

            /* 戻る先はサーバーが決めて返す。万一届かなければラウンジ */
            window.location.href = result.rejectRedirectUrl || '/chat/lounge';
            return;
        }

        /*
            【重要】配列のときだけ描き直すこと。
            一時的な失敗（entriesがnull）を「成功して0件」と取り違えて
            描き直すと、DBが一瞬詰まっただけで画面の会話が全部消える。
            空の配列は「まだ発言が1件も無い部屋」という正当な状態なので、
            描き直してよい（結果として画面が空になるのが正しい）
        */
        if (!Array.isArray(result.entries)) {
            debugLog(reason + '：ログの取り直しに失敗（サーバーが取得に失敗）');
            return;
        }

        renderLogEntries(result.entries, redrawMode);

        /*
            こっそりの窓も、同じ結果から描き直す。

            【配列のときだけにすること】部屋のログとまったく同じ理由である。
            一時的な失敗（null）を「成功して0件」と取り違えて描き直すと、
            DBが一瞬詰まっただけで、開いていた窓の中身が全部消える
        */
        if (Array.isArray(result.privateEntries)) {
            PrivateTalk.renderPastLog(result.privateEntries);
        }

        debugLog(reason + '：ログを ' + result.entries.length + ' 行で描き直した');
    }

    /* セレクトボックスで行数が変わったら、その場でログを取り直して描き直す */
    $('#logCountSelect').on('change', function () {

        var count = parseInt($(this).val(), 10);

        if (!count) {
            return;
        }

        /* 次に入室したときも同じ行数になるよう、Cookieに残す */
        setCookie('ChatLogCount', count);

        /*
            【必須】まだ入室が済んでいなければ、取り直しに行かない。

            サーバーは「どの部屋のログか」をハブの参加者リストから引くため、
            JoinRoomが終わるまでは、こちらを知らない。
            その間に呼ぶと「部屋に入っていない」と断られ、
            正常に部屋を開いた人がラウンジへ追い出される。

            起きるのはページを開いた直後の数百ミリ秒だけだが、
            設定パネルを開いてセレクトを操作するのが間に合わないとは限らない。
            Cookieには残してあるので、次にこの部屋を開いたときには効く
        */
        if (myLocalId === null) {
            debugLog('表示行数を ' + count + ' 行に変更（入室がまだ済んでいないため、取り直しは次回から）');
            return;
        }

        /* 利用者自身の操作なので、最下部へ飛ばしてよい */
        fetchAndRenderLog(count, '表示行数を ' + count + ' 行に変更', REDRAW_TO_BOTTOM);
    });

    /*
        === 再接続に成功したら、ログを取り直す ===

        【これが無いと、切れている間の発言が永久に画面へ出ない】
        過去ログを描いているのはページを開いたときの1回だけ（renderInitialLog）で、
        joinRoomAndRender は参加者一覧を作り直すだけでログには触っていない。
        そのため、切断中に他の人が発言すると、その分は放送も届かず、
        描き直しも起きないまま、F5するまで出てこない。

        にある「ログ取得から接続完了までの数百ミリ秒の1件は抜ける」という
        割り切りは、切断が数百ミリ秒で終わることを前提にしていた。
        凍結からの復帰では、そこが数分になる

        【往復を抑える仕掛けは、入れていない】
        増えるのは再接続1回につきDBへの問い合わせ1回で、再接続の頻度は
        SignalR自身の reconnectDelay（2秒）と、こちらのbackoff（3・10・30秒）で
        もともと上限が決まっている。
        それより、間引いた1回がそのまま「発言が出ないまま残る」ことのほうが重い
        ——電波が悪くて何度も切れる端末は、いちばん取りこぼしている端末でもある。
        直そうとしている不具合を、条件付きで復活させることになる
    */
    function refreshLogAfterReconnect(reason) {

        var count = parseInt($('#logCountSelect').val(), 10);

        if (!count) {
            return;
        }

        /*
            再接続は利用者が起こしたことではないので、
            読み返している最中なら、その位置を尊重する
        */
        fetchAndRenderLog(count, reason, REDRAW_KEEP_POSITION);
    }


    /*
        無視の設定を変えたあと、ログを描き直す

        【なぜ、取り直すのか】
        既に画面に出ている行を1つずつ消し歩く形にすると、
        **無視を解除したときに、消えていた過去ログが戻ってこない**
        （消した行は、もう手元に無い）。取り直せば、隠すのも戻すのも同じ1本の道になる。

        【読んでいた位置は保つ】
        利用者自身の操作ではあるが、変えたのは「誰を見せるか」であって、
        「どこを読んでいるか」ではない。最下部へ飛ばす理由が無い
    */
    function refreshLogAfterIgnoreChange() {

        var count = parseInt($('#logCountSelect').val(), 10);

        if (!count) {
            return;
        }

        fetchAndRenderLog(count, '無視の設定を変更', REDRAW_KEEP_POSITION_QUIET);
    }


    /*
        === 通知音 ===

        【鳴らす口は、この Sounds ひとつだけ】
        呼ぶ側が書くのは Sounds.enter() / Sounds.leave() / Sounds.message() の一行だけ。
        オン/オフの判断も、音量も、その中に閉じてある。

        【中身は Scripts/sounds.js にある】（外へ出した）
        ラウンジとトップページでも鳴らすことになり、同じものを写さないため。
        仕様・決めた理由・罠（過去ログでは鳴らさない、など）も、あちらの冒頭にある。

        【最大の落とし穴：過去ログでは鳴らさない】
        鳴らす場所を「ハブから届いたときだけ」に限ることで、構造的に起きないようにしてある。
        【罠】音を appendRowHtml の側へ移してはいけない（部屋へ入るたび過去ログの行数ぶん鳴る）

        【sounds.js が読めなかったとき】鳴らない Sounds に落とす。
        音が鳴らないだけで、部屋はそのまま使える（ここで止まると、部屋ごと動かなくなる）
    */
    var Sounds = window.VbChatSounds
        ? VbChatSounds.create({
            enterUrl: $('#soundEnterUrl').val(),
            leaveUrl: $('#soundLeaveUrl').val(),
            knockUrl: $('#soundKnockUrl').val(),
            log: debugLog
        })
        : (function () {
            debugLog('Scripts/sounds.js を読み込めなかったため、通知音は鳴りません');
            var noop = function () { };
            return {
                enter: noop,
                leave: noop,
                message: noop,
                knock: noop,
                leaveThenRun: function (done) { done(); },
                enterThenRun: function (done) { done(); },
                setEnabled: noop,
                setVolumeLevel: function () { return 'normal'; },
                isKnownLevel: function () { return false; },
                unlock: noop
            };
        })();

    /*
        === 無視機能（11番のC）===

        「この人は無視されているか」の判定を、この窓口ひとつに閉じてある
        （Sounds とまったく同じ形）。呼ぶ側が書くのは一行だけにする。

        将来 D の「こっそり話す」から使うときも、永続の無視に作り替えるときも、
        **中の印を差し替えるだけで済む**。

        【印は匿名ID（AnonId）。LocalId ではない】
        LocalId はアプリが再起動すると1番から振り直されるため、
        「昨日のログの5番」と「今いる5番」が別人になり、過去ログの行に印を付けられない。
        匿名IDはサーバーが BrowserId から計算して配る値で、
        参加者一覧・発言・入退室・過去ログの各行に、同じものが付いている。

        【生の BrowserId は、ここへは来ない】
        あれは本人証明そのもので、他人の値を知ればなりすませる。
        サーバーは絶対に渡してこないし、こちらも要らない。

        【サーバーへは一切送らない】
        誰を無視しているかは、この画面の中だけの話である。
        サーバー側で配信先を選別すると、発言のたびに部屋の人数ぶんの選別が入るため、
        **クライアント側で隠す**方式を選んである（サーバー負荷ゼロ）。
        F12 を開けば無視した相手の発言は見えるが、開発者の判断で許容
    */
    var IgnoreList = (function () {

        /*
            置き場所は sessionStorage。

            【なぜ localStorage ではないのか】
            無視が効くのは「自分がその部屋に居る間」だけ、と決めてある。
            タブを閉じれば消えるのが、ちょうどその範囲になる。

            【キーに部屋IDを含める】
            別の部屋へ移ったらリセットする、という仕様をこれで満たす。
            サーバーが配る匿名IDにも roomId が混ぜてあるので、
            仮に別の部屋のリストが残っていても、誰にも当たらない
        */
        var storageKey = 'vbchat_ignored_' + roomId;

        /* 無視している相手の匿名IDの配列 */
        var ignored = [];

        /*
            自分の匿名ID（JoinRoom の戻り値で確定するまでは null）
            自分を無視できないようにするためだけに持っている
        */
        var myAnonId = null;

        /*
            読み書きは必ず try/catch で囲む。
            ブラウザの設定や容量超過で例外を投げることがあり、
            そこで落とすと**部屋そのものが動かなくなる**
            （退室理由の受け渡しで sessionStorage を使っているのと、同じ用心）
        */
        function load() {
            try {
                var raw = sessionStorage.getItem(storageKey);
                var parsed = raw ? JSON.parse(raw) : null;
                ignored = Array.isArray(parsed) ? parsed : [];
            } catch (e) {
                ignored = [];
            }
        }

        function save() {
            try {
                sessionStorage.setItem(storageKey, JSON.stringify(ignored));
            } catch (e) {
                /* 保存できなくても、この画面を開いている間は動き続ける */
            }
        }

        /*
            === ここで先に読んでおくことが必要である ===

            【なぜ、JoinRoom の結果を待たないのか】
            過去ログの描画は、SignalRの接続を開始する**前**に済ませる作りになっている
            （そうしないと、描画中に届いた発言と二重に表示されるため）。
            つまり applyJoinResult が呼ばれるより先に、ログが1回描かれる。

            待つ形にすると、**F5した直後だけ、無視した相手の過去ログが戻ってしまう**
            （実際にそうなった。実測で発見した）。
            F5はいちばん多い経路なので、そちらを既定にして先に読む。

            読んだうえで捨てるべきだったとき（＝新規入室）は、
            applyJoinResult が true を返すので、呼び出し側が描き直す
        */
        load();

        return {

            /*
                入室の結果を受け取って、無視リストを引き継ぐか捨てるかを決める

                【この判定は、サーバーにしかできない】
                F5 も内部的には入室処理を通るため、素直に「入室したらリセット」に
                すると、F5 のたびに無視が外れる（指摘：F5 して無視が外れたら
                意味がない）。isContinuation は
                「このブラウザは、直前までこの部屋に居たか」をサーバーが答えたもの

                【戻り値】ログを描き直す必要があるかどうか。
                捨てた場合だけ true になる（先に読んだせいで、隠さなくてよい行を
                隠したまま描いてしまっているため）
            */
            applyJoinResult: function (result) {

                myAnonId = result.myAnonId || null;

                if (result.isContinuation) {
                    /* 引き継ぐ。上で読んだものが、そのまま正しい */
                    return false;
                }

                var hadAny = (ignored.length > 0);

                ignored = [];
                save();

                return hadAny;
            },

            /*
                その匿名IDの相手を、隠すべきか

                【印が無い行は、隠さない】
                anonId が空で届くことは通常は無い（入室時に Cookie を検査しているため）が、
                欠けても**画面が壊れないほう**へ倒してある
            */
            isIgnored: function (anonId) {
                return !!anonId && ignored.indexOf(anonId) !== -1;
            },

            /* 自分自身か（自分は無視できない） */
            isSelf: function (anonId) {
                return !!anonId && !!myAnonId && anonId === myAnonId;
            },

            add: function (anonId) {
                if (!anonId || this.isSelf(anonId) || this.isIgnored(anonId)) {
                    return;
                }
                ignored.push(anonId);
                save();
            },

            remove: function (anonId) {
                var at = ignored.indexOf(anonId);
                if (at === -1) {
                    return;
                }
                ignored.splice(at, 1);
                save();
            },

            count: function () {
                return ignored.length;
            }
        };
    })();


    /*
        === こっそり話す（秘密トーク）の窓 ===

        【呼ぶ側が書くのは一行だけ、という形にしてある】
        Sounds・IgnoreList とまったく同じ考え方である。
        「誰の窓か」「窓を作るか」「無視している相手か」「退室したか」は、
        すべてこの中で決まる。外から talks を直接触らないこと。

        【窓の鍵は、相手の匿名ID】
        LocalId は入室のたびに変わるので、過去ログの行と結び付けられない。
        生の BrowserId はクライアントへ来ない（来たらなりすませる）。
        匿名IDだけが、参加者一覧にも・リアルタイムの行にも・過去ログの行にも
        同じ値で付いている。

        【自分が送った行は、送り主が自分である】
        だから anonId だけでは窓を決められない。サーバーは行に
        recipientAnonId も付けてくるので、
            窓の鍵 ＝ isSelf ? recipientAnonId : anonId
        となる。この式は windowKeyOf ひとつに閉じてある。

        === やりとりの中身と、窓は、別物である ===

        talks[匿名ID].rows が**やりとりの中身**（並び順もここが持つ）で、
        画面に出ている窓は、それを写しただけの**見た目**である。
        窓を閉じても rows は消えない。だからどの経路で開き直しても履歴が入る。

        === 画面の作りは、広さで2通りある ===

          広いとき（PC） … 窓を好きな位置へ置ける。ドラッグで動かせる。
                            2枚目以降は少しずつずらして重ねる
          狭いとき（スマホ）… 覆いかぶさる形。一度に1つだけ見えるので、
                            上に相手の名前をタブで並べて切り替える

        どちらになるかは CSS のメディアクエリが決め、JSは同じ問い合わせを
        matchMedia で見ている。**幅の数字は PT_NARROW_MEDIA 1か所だけ**
    */
    var PrivateTalk = (function () {

        /*
            匿名ID → 1人ぶんのやりとり
            { anonId, nickname, present, rows, unread, x, y, $el, $log, $input, $send, $reject }
            $el が null なら「窓を開いていない」
        */
        var talks = {};

        /* 狭いとき、今どの窓を見せているか（広いときは使わない） */
        var activeAnonId = null;

        /*
            === 窓のことを覚える（sessionStorage）===

            覚えるのは4つ。**F5では全部そのまま、入り直したら全部クリア**
            （開発者が決めた）。

              ・どの窓が開いているか
              ・窓の位置
              ・窓の大きさ
              ・透け具合

            【未読は別の箱】F5ではそのまま、**入り直しても残す**。
            寿命が違うので、キーを分けてある（storedUnread の項）

            【「閉じた窓」ではなく「開いている窓」を覚えている】
            最初は閉じたほうを覚えていたが、それだと
            **部屋を出て入り直しても窓が開いたまま**になる（一度も閉じていないため）。
            開いているほうを覚えれば、
              ・F5・再接続 … そのまま復元する
              ・入り直し   … 記録ごと捨てるので、どれも開かない
            の2つが、1つの記録で決まる。

            【置き場所は sessionStorage】
            置き場所の表に当てると、これは設定でも履歴でもなく
            **その場限りの状態**である。無視リストとまったく同じ軸なので、
            作りも揃えてある（部屋IDをキーに含める／try/catch で囲む／
            過去ログを描くより先に読む／isContinuation で引き継ぐか捨てるかを決める）

            【透け具合も、ここへ移した（それまでは Cookie）】
            **「入り直したらクリアする」と決めた時点で、Cookie に置く理由が消える**——
            あちらは「部屋を出ても残す」ための箱だからである。
            Cookie は毎リクエスト送られるので、そのぶんの無駄も無くなる。

            【4つを1つの記録にまとめてある】
            キーを4本に分けると、捨てるときに4回書くことになり、
            **1本だけ消し忘れても誰も気づけない**。
            まとめておけば、捨てるのは forgetAllOpened の1か所で済む
        */
        var STATE_KEY = 'vbchat_pt_' + roomId;

        /* 開いている窓（並び順は、そのまま開き直す順でもある） */
        var opened = [];

        /* 匿名ID → { x, y, w, h }。閉じた窓のぶんは持たない（下の closeWindow） */
        var layout = {};

        /* 0〜100。null は「まだ動かしていない」＝ theme.css の既定を使う */
        var storedSeeThrough = null;

        /*
            まだ見ていない発言がある相手（匿名ID。未読になった順）。

            【5つ目を足した】
            「窓が1枚でも開いている間は、別の相手の窓を自動で開かない。タブに［未読］を出すだけ」
            になった。未読の印は画面の上の状態でしかなかったので、**F5すると印ごと消え、
            窓を持っていない相手のこっそりは、どこにも知らせが出ないまま埋もれる**
            （それまでは、自動で開いた窓が「開いている窓」として覚えられていたので、F5でも戻っていた）。
            通信が切れて繋ぎ直したとき（過去ログの取り直し）も、同じく消えていた。

            === 【別の箱へ分けた】入り直しても消さない ===

            要望：こっそりの窓は閉じたままでよいが、未読があれば赤い札を出す。
            名前を変えない人にとっては、そのほうがよいため。
            **未読は「画面の状態」ではなく「まだ読んでいない人がいる」という事実である。**
            窓の位置や大きさと同じ箱に入れていたのが混ざっていた、という整理。

              窓・位置・大きさ・透け具合 … STATE_KEY。入り直したら消す（今までどおり）
              未読                       … UNREAD_KEY。**入り直しても残す**。ログアウトで消える
                                             （ログアウトは sessionStorage を丸ごと消すので、何も書き足さずに消える）

            【同じ記録の中で「未読だけ残す」にしなかった理由】
            消すのは forgetAllOpened の1か所、という形がそのまま使えるため。
            1つの記録の中で「これだけは消さない」を作ると、消す側の書き方を1つ間違えただけで黙って消える。
            **寿命が違うものは、箱を分ける**（Cookie から透け具合を移したのと、同じ考え方の逆向き）
        */
        var UNREAD_KEY = 'vbchat_pt_unread_' + roomId;
        var storedUnread = [];

        /*
            【自分で書いたものでも、読むときは形を確かめる】
            通知音の音量が「一覧に無い値は既定へ」としているのと同じ規則。
            壊れた記録で画面が動かなくなるほうが、覚えていないことより重い
        */
        function loadState() {

            var parsed = null;

            try {
                var raw = sessionStorage.getItem(STATE_KEY);
                parsed = raw ? JSON.parse(raw) : null;
            } catch (e) {
                parsed = null;
            }

            if (!parsed || typeof parsed !== 'object') {
                parsed = {};
            }

            opened = Array.isArray(parsed.opened) ? parsed.opened : [];

            layout = (parsed.layout && typeof parsed.layout === 'object')
                ? parsed.layout : {};

            storedSeeThrough =
                (typeof parsed.seeThrough === 'number' &&
                 parsed.seeThrough >= 0 && parsed.seeThrough <= 100)
                    ? parsed.seeThrough
                    : null;

            /* 未読は別の箱（UNREAD_KEY）から読む。読めない・形が違うなら「未読なし」 */
            var unreadParsed = null;

            try {
                var unreadRaw = sessionStorage.getItem(UNREAD_KEY);
                unreadParsed = unreadRaw ? JSON.parse(unreadRaw) : null;
            } catch (e) {
                unreadParsed = null;
            }

            storedUnread = Array.isArray(unreadParsed)
                ? unreadParsed.filter(function (id) { return typeof id === 'string'; })
                : [];
        }

        function saveState() {
            try {
                sessionStorage.setItem(STATE_KEY, JSON.stringify({
                    opened: opened,
                    layout: layout,
                    seeThrough: storedSeeThrough
                }));
            } catch (e) {
                /* 覚えられなくても、この画面を開いている間は動き続ける */
            }
        }

        function saveUnread() {
            try {
                sessionStorage.setItem(UNREAD_KEY, JSON.stringify(storedUnread));
            } catch (e) {
                /* 覚えられなくても、この画面を開いている間は印は出ている */
            }
        }

        /*
            === 過去ログを描くより先に読んでおくこと ===

            無視リストとまったく同じ事情である。過去ログの描画は
            SignalRの接続を開始する前に済ませる作りなので、
            待つ形にすると**F5した直後だけ、窓の開き具合がずれる**
        */
        loadState();

        function rememberOpened(anonId) {
            if (opened.indexOf(anonId) === -1) {
                opened.push(anonId);
                saveState();
            }
        }

        function forgetOpened(anonId) {
            var at = opened.indexOf(anonId);
            if (at !== -1) {
                opened.splice(at, 1);
                saveState();
            }
        }

        /*
            === 4つとも捨てる（部屋を出るとき・入り直したとき）===
            【未読は捨てない】別の箱（UNREAD_KEY）にあり、入り直しても残す（storedUnread の項）
        */
        function forgetAllOpened() {
            opened = [];
            layout = {};
            storedSeeThrough = null;
            saveState();
        }

        /*
            今の未読を、覚えている形へ写す（未読になった順）。
            markUnread／clearUnread／やりとりを捨てたとき（無視）／過去ログを描き直したあと、から呼ぶ
        */
        function rememberUnread() {
            storedUnread = Object.keys(talks)
                .map(function (id) { return talks[id]; })
                .filter(function (talk) { return talk.unread; })
                .sort(function (a, b) { return a.unreadSeq - b.unreadSeq; })
                .map(function (talk) { return talk.anonId; });
            saveUnread();
        }

        /*
            === 透け具合を、既定へ戻して描き直す ===

            【捨てるのと、画面へ戻すのを分けてある】
            どちらも「入り直し」では要るが、**「部屋を出るとき」には要らない。**

            出ていく経路では、記録を捨てたあと**すぐには画面が変わらない**——
            退室の音が鳴り終わるのを待つ形にしてあるので、
            通知音をオンにしている人は、**そこから0.7秒だけ部屋を見ている**
            （実測＝オンで750ms、オフで23ms）。
            そこで一緒に描き直すと、**押した瞬間に窓の透け方が既定へ戻る**のが見える。
            退室ボタンを押した反応として、意味の無い変化を混ぜない。

            **入り直したときは、逆に必ず要る。**
            :root に書いたインラインの値は F5 をまたがない代わりに、
            同じページのまま入り直した場合はそのまま残っているので、
            戻さないと**次に開く窓が、前の部屋の透け具合で出る**
        */
        function resetSeeThrough() {
            seeThrough = DEFAULT_SEE_THROUGH;
            applySeeThrough();
        }

        /*
            === 送られてきたら自動で開くか ===

            【既定はオン】（開発者の指定）
            置き場所は Cookie。置き場所の表に当てると、これは
            **利用者の設定**で、通知音・音量・表示行数とまったく同じ軸である。
            「その場限りの状態」である上の opened とは、軸が違う

            【効くのは「窓が1枚も開いていないとき」だけ】
            窓が1枚でも開いている間は、別の相手の窓を自動で開かない・手前に出さない。
            狭い画面はタブの帯に［未読］、広い画面は裏に開く（receive の項）
        */
        var AUTO_OPEN_COOKIE = 'ChatPrivateAutoOpen';
        var autoOpen = (getCookie(AUTO_OPEN_COOKIE) !== '0');

        /*
            === 窓の透け具合 ===

            **つまみは1つだけ。部位ごとの配り方は theme.css の係数が持つ。**
            ここは「0〜100を預かって、:root へ渡す」だけにしてある
            （色の計算をこちらへ持ち込まない、という theme.css の決まりのため）。

            【:root へ書くこと。層ではない】
            カスタムプロパティは**宣言された要素で var() が解決される**ので、
            :root で組み立てている色は :root の値しか見ない。
            層に書いても、色は既定のまま動かない（理由は theme.css 側にも書いてある）。

            **その代わり、開いている窓は全部まとめて変わる**——
            つまみは1つ、という指示にちょうど合う。

            【保存は sessionStorage（Cookie から移した）】
            **入り直したらクリアする、と決めた時点で Cookie の箱ではなくなった。**
            上の「窓のことを覚える」に、他の3つと一緒に入れてある
        */
        var SEE_THROUGH_VAR = '--pt-see-through';

        /*
            === 既定値は theme.css が持っている。ここには書かない ===

            【必ず、1回だけ・いちばん先に読むこと】
            applySeeThrough が :root へインラインで書き込むと、
            **それ以降 getComputedStyle が返すのは「今の値」であって、既定ではない。**
            あとから読み直す形にすると、入り直したときに
            「既定へ戻したつもりで、今の値をそのまま入れ直す」ことになる
        */
        var DEFAULT_SEE_THROUGH = (function () {
            var n = parseFloat(getComputedStyle(document.documentElement)
                .getPropertyValue(SEE_THROUGH_VAR));
            return isNaN(n) ? 30 : Math.round(n * 100);
        })();

        /* 覚えていなければ既定。値の検査は loadState が済ませてある */
        var seeThrough = (storedSeeThrough === null)
            ? DEFAULT_SEE_THROUGH
            : storedSeeThrough;

        /*
            === 使っていた Cookie は、ここで消す ===

            置き場所を sessionStorage へ移したので、残っていると
            **1年のあいだ毎リクエスト送られるだけの、誰も読まない値**になる。
            移した理由の1つがその無駄を無くすことなので、
            **消すところまでが1組**である（既に発行して本番で配ってしまっている）。

            消し方は、同じ名前・同じ path で max-age を0にするだけ。
            **path を揃えないと消えない**（別のCookieを1つ作って終わる）
        */
        if (getCookie('ChatPrivateSeeThrough') !== null) {
            document.cookie =
                'ChatPrivateSeeThrough=; max-age=0; path=/; SameSite=Lax; Secure';
        }

        /* 画面へ反映する。開いている窓のつまみも、まとめて揃える */
        function applySeeThrough() {

            document.documentElement.style.setProperty(
                SEE_THROUGH_VAR, (seeThrough / 100));

            $('.private-talk-seethrough').val(seeThrough);
        }

        applySeeThrough();

        /* 狭いか（スマホ）。幅の数字は、CSS側のメディアクエリと必ず同じにすること */
        var PT_NARROW_MEDIA = '(max-width: 700px)';

        /*
            動かせるか。

            【タッチの端末では動かさない】（開発者の指定）
            スマホは覆いかぶさる形のままにする。
            この問い合わせは、CSSの :hover の効かせ方とまったく同じ形にしてある
        */
        var PT_DRAG_MEDIA = '(hover: hover) and (pointer: fine)';

        function isNarrow() {
            return window.matchMedia(PT_NARROW_MEDIA).matches;
        }

        function canDrag() {
            return !isNarrow() && window.matchMedia(PT_DRAG_MEDIA).matches;
        }

        /* 2枚目以降を、少しずつずらして重ねる幅 */
        var CASCADE_STEP_PX = 24;

        /* 窓と、層のふちとの間 */
        var EDGE_MARGIN_PX = 8;

        /*
            その行が、どの窓のものか

            【相手が誰かで決める】自分が送った行なら宛先、
            もらった行なら送り主。それが「相手」である
        */
        function windowKeyOf(entry) {
            return entry.isSelf ? entry.recipientAnonId : entry.anonId;
        }

        /*
            窓の見出しの文言。

            【「こっそり話す」と、そのまま書いてある】
            窓を暗くしたが、**「秘密っぽさ」を色だけで伝えないこと。**
            色の見分けがつきにくい人にも、ここが普通の部屋とは別物だと分かる必要がある。
            参加者一覧の入口のボタンとも、同じ言葉に揃えてある
        */
        function titleFor(talk) {
            return talk.nickname + 'さんと こっそり話す';
        }

        /*
            窓の読み上げ用の名前（見出しの文言 ＋ 相手のトリップ）。
            見出しの10桁は読み上げ専用の「トリップ」付きで読まれるが、窓（dialog）の名前にも入れておく
            （窓に入った瞬間に「誰との窓か」が、同じ名前の人と区別できる形で伝わるように）
        */
        function labelFor(talk) {
            return titleFor(talk) + idMarkLabel(talk.trip);
        }

        /*
            相手のトリップを覚え直す。窓の見出しに1か所だけ出す。

            【値の出どころは3つ。あとから来たほうが勝つ】
              ・過去ログの相手の行（焼き込まれた値。いちばん新しい行のもの）… renderPastLog
              ・リアルタイムで届いた相手の発言 … receive
              ・参加者一覧（相手が今この部屋に居れば、今の値）… refreshNicknames ／ notifyJoined
            見出しが示すのは「今この窓の相手は誰か」なので、分かる範囲でいちばん新しい値を出す。
            相手がラウンジで外した・変えたあとに入り直せば、ここも変わる（匿名IDは変わらないので、窓はそのまま続く）
        */
        function setTalkTrip(talk, trip) {
            /* trip は「名前の下の印」（トリップかアカウントID。idMarkOf がまとめたもの） */
            var next = normalizeIdMark(trip);
            if (talk.trip !== next) {
                talk.trip = next;
                applyTitle(talk);
            }
        }

        function layerEl() {
            return document.getElementById('privateTalkLayer');
        }

        /* 今開いている窓を、開いた順に返す */
        function openTalks() {
            return Object.keys(talks)
                .filter(function (id) { return !!talks[id].$el; })
                .map(function (id) { return talks[id]; });
        }

        /* やりとりの入れ物を用意する（窓は作らない） */
        function ensureTalk(anonId, nickname) {

            if (!anonId) {
                return null;
            }

            var talk = talks[anonId];

            if (!talk) {
                talk = {
                    anonId: anonId,
                    nickname: nickname || '相手',

                    /* 相手のトリップ（無ければ null）。窓の見出しに出す（setTalkTrip の項） */
                    trip: null,

                    /*
                        相手が今この部屋に居るか。
                        居ないときは送信できない（窓は残す。閉じると、
                        それまでのやりとりが読めなくなるため）
                    */
                    present: true,

                    /* やりとりの中身。並び順はこの配列が持つ */
                    rows: [],

                    /* まだ見ていない発言があるか（参加者一覧に印を出す） */
                    unread: false,

                    /*
                        ログのいちばん下（最新）を見ていたか（のあと）。
                        窓が見えている間のスクロールでだけ書き換える（showLatestIfFollowing の項）
                    */
                    followBottom: true,

                    /* 窓の位置（広いときだけ使う。null なら、開くときに決める） */
                    x: null,
                    y: null,

                    $el: null
                };
                talks[anonId] = talk;
            }

            if (nickname) {
                talk.nickname = nickname;
                applyTitle(talk);
            }

            return talk;
        }

        function applyTitle(talk) {
            if (talk.$el) {
                talk.$el.find('.private-talk-title').text(titleFor(talk));
                talk.$el.find('.private-talk-head-trip-slot').html(buildTripHtml(talk.trip, 'private-talk-head-trip'));
                talk.$el.attr('aria-label', labelFor(talk));
            }
            renderTabs();
        }

        /*
            窓のHTML。

            【属性へ入れる値は escapeAttr を通すこと】
            escapeHtml ではダブルクォートが変換されず、属性が途中で閉じてしまう
            （appendMessageRow と同じ理由）
        */
        function buildWindowHtml(talk) {

            var safeAnonId = escapeAttr(talk.anonId);

            return '<div class="private-talk-window" data-anon-id="' + safeAnonId + '"' +
                ' role="dialog" aria-label="' + escapeAttr(labelFor(talk)) + '">' +

                '<div class="private-talk-head">' +

                /*
                    見出しの文言と、その下の行の相手のトリップ。

                    【こっそりの窓のトリップは、ここに1か所だけ】
                    相手の発言の行ごとに出していたが改めた——
                    「ここは二人だけの世界なので、各ログにトリップいらない。
                    上の相手の名前の所に一か所だけかけばいいでしょ」。
                    元の要望（同じ名前の人が2人いると、どちらからか分からない）は、見出しに1回出れば満たされる。
                    行ごとに出していたときの「相手の行だけ本文の書き出しが20pxずれる」も無くなった。

                    【名前の横ではなく、下の行】見出しの帯は、題・自動で開く・閉じるで既に詰まっていて
                    （「自動で開く」は窓の下の行へ移した。今の帯は題と閉じるだけ）、
                    320px の窓では題が「花子さんと こ…」まで削れた前例がある。
                    横に10桁（約66px）を足すと、いちばん大事な相手の名前のほうが削れる。
                    下の行なら、付けている相手の窓だけ帯が1段（約14px）高くなるだけで済む
                */
                '<span class="private-talk-heading">' +
                '<span class="private-talk-title">' + escapeHtml(titleFor(talk)) + '</span>' +
                '<span class="private-talk-head-trip-slot">' + buildTripHtml(talk.trip, 'private-talk-head-trip') + '</span>' +
                '</span>' +

                /*
                    「自動で開く」は、見出しから窓の下の行（背景を透かすの右）へ移した。
                    見出しに残るのは、相手の名前（とその下の印）と × だけ＝名前を長く出せる
                */

                /*
                    閉じるボタン。名前はスマホ（全面）と PC で違う（applyCloseLabels の項）。
                    この窓が開けば、狭い画面では必ず全面になるので、isNarrow() で決まる
                */
                '<button type="button" class="private-talk-close"' +
                ' aria-label="' + (isNarrow() ? CLOSE_LABEL_ALL : CLOSE_LABEL_ONE) + '"' +
                ' title="' + (isNarrow() ? CLOSE_TITLE_ALL : CLOSE_TITLE_ONE) + '">×</button>' +
                '</div>' +

                '<div class="private-talk-log"></div>' +

                /*
                    === 窓の足もと（透け具合のつまみ ＋ 断られた理由） ===

                    【1つの行を分け合っている】
                    本体の入力欄の下が「改行の案内 ＋ 文字数」で1行を分け合い、
                    **断られた理由が出たら案内が引っ込む**のと、まったく同じ形。

                    理由は2つある。
                      ・**帯には入らなかった。** つまみを帯へ置くと、
                        題が 145px → 74px に潰れて「花子さんと こ…」になった（実測）
                      ・**行がいつも在ると、画面が跳ねない。** それまで理由の行は
                        中身が空のとき高さ0で、理由が出た瞬間に窓の中が押し上げられていた

                    【設定パネルではなく、窓の中に置いてある】
（設定パネルには置かない）。
                    見ながら動かして決める値なので、動かす対象のすぐ横にあるのが素直

                    【形はスライダー】
                    音量をセレクトにしたのは「スマホで摘まみにくい」「段階で足りる」
                    という別の理由だった。こちらは**見ながら合わせる値**なので、
                    連続して動かせるほうが合う

                    【名前を、画面にも出してある】
                    要望：つまみには説明を付ける。
                    **画面に出す言葉は「背景を透かす」**。

                    「見える文字を置くと、そのぶん理由の幅が減る」として
                    aria-label だけにしていたが、**理由が出るときは
                    つまみごと引っ込む**ので、実際には食い合っていなかった。
                    引っ込める先を「つまみ」から「名前＋つまみ」に広げてある。

                    【読み上げソフト向けの名前は、画面の文字より長くてよい】
                    見えている「背景を透かす」だけでは、
                    どちらへ動かせばどうなるのかが分からない。
                    aria-label のほうには、その向きまで入れてある

                    【端に「こい ⇄ すける」を添える案は、実測して見送った】
                    320pxの窓では、名前・つまみ・断られた理由で既にいっぱいで、
                    添えると理由の幅が二桁pxまで削られる。
                    **向きは aria-label と title が伝える**
                */
                '<div class="private-talk-foot">' +
                '<span class="private-talk-seethrough-box">' +
                '<span class="private-talk-seethrough-label">背景を透かす</span>' +
                '<input type="range" class="private-talk-seethrough"' +
                ' min="0" max="100" step="5"' +
                ' value="' + seeThrough + '"' +
                ' aria-label="窓の背景を透かす具合。0で濃く、100でいちばん透ける"' +
                ' title="右へ動かすほど、窓の背景が透けます">' +

                /*
                    自動で開くの切り替え（見出しの帯からここへ移した）。

                    【つまみと同じ箱に入れてある】断られた理由が出たら、箱ごと引っ込む（showReject。今までのつまみと同じ形）。
                    320px の窓で入りきらないときは、箱の中で次の行へ折り返す（CSS の flex-wrap）。

                    【文字で状態を出していることに意味がある】
                    絵や名前だけのボタンにすると、何が起きるのか伝わらない
                    （指摘：「チェンジ」という名前では何が起きるか分からない）。
                    見出しがそのまま説明になる形にしてある
                */
                '<button type="button" class="private-talk-autoopen"' +
                ' aria-pressed="' + (autoOpen ? 'true' : 'false') + '">' +
                autoOpenLabel() + '</button>' +
                '</span>' +
                '<div class="private-talk-reject" aria-live="polite"></div>' +
                '</div>' +

                '<div class="private-talk-input-row">' +

                /*
                    === 発言に入れるもの（絵文字・スタンプ・画像）を選ぶボタン ===

                    【入力欄の「左」に置いてある】（開発者の指定）
                    LINEのスマホ版と同じ並びになるのと、
                    **送信ボタンから離れて、押し間違いが減る**のが理由である。
                    それまでは送信ボタンのすぐ隣（右）に、画像専用のボタンを置いていた。

                    【押すと、本体と同じパネルが開く】
                    要望：部屋の本体の入力欄の絵文字などと同じやり方に
                    する。窓の中にパネルは作らない
                    （窓は既定260pxで、パネルの面だけで346pxあるので入らない）。
                    **本体のパネルをそのまま開き、「今どこへ入れるか」だけを
                    この窓へ切り替える**（InsertPanel.toggleFor）。

                    【いつでも出す】
                    画像専用だった頃は、画像が許されている部屋でしか出していなかった。
                    今は絵文字とスタンプも入るので、どの部屋でも出す
                    （画像のタブのほうが、ページ側の <% If %> で出し分けられている）。

                    【入ることは実測してある】320pxの窓で ボタン34 ／ 入力欄184 ／ 送信60。
                    縮められる下限の240pxでも、入力欄104で収まる（ボタンの幅は画像のときと同じ）

                    【絵は本体の丸いボタンから写す】同じ笑顔を2か所に書かないため
                    （insertIconHtml）
                */
                '<button type="button" class="private-talk-insert-btn"' +
                ' aria-label="絵文字・スタンプ・画像を入れる" title="絵文字・スタンプ・画像を入れる"' +
                ' aria-expanded="false" aria-controls="insertPanel">' +
                insertIconHtml('private-talk-insert-icon') +
                '</button>' +

                '<textarea class="private-talk-input" rows="2"' +
                ' aria-label="こっそり送る内容"' +
                ' placeholder="こっそり送る"></textarea>' +

                '<button type="button" class="private-talk-send">送信</button>' +
                '</div>' +

                '</div>';
        }

        function autoOpenLabel() {
            return '自動で開く：' + (autoOpen ? 'オン' : 'オフ');
        }

        /*
            === 暗い窓の上でも読める色に、持ち上げる ===

            【なぜ要るのか】
            窓の中では、名前とアイコンに**その人が自分で選んだ色**が付く。
            地を暗くしたので、**濃い色を選んでいる人の名前が読めなくなる**
            （実測＝#DC2626 の赤は、暗い地の上で 3.35:1。基準の4.5:1を割る）。

            【色そのものは変えず、明るさだけ足す】
            白のほうへ少しずつ寄せて、基準を満たした時点で止める。
            **色味（赤い人は赤いまま）が残るので、誰の発言かは見分けられる。**
            寄せ切れば白なので、どんな色でも必ず止まる

            【基準の地は、実際に描かれている色から取る。ここに色コードを書かない】
            ログの地は**半透明**なので、見えている色は
            「地の色 ＋ 下のチャット」が混ざったものである。
            そこで `getComputedStyle` で今の地（rgba）を読み、
            **いちばん明るくなる場合＝白の上に重ねたとき**の色を基準にする。
            透け具合を変えれば、次に窓を開いたときから自動で追いかける。

            **数字をこちらに写さないこと。** 透け具合の係数は theme.css が持っており、
            ここへ書き写すと、あちらを変えた日に片方だけ古くなる

            【アイコンにも同じ色を使う】
            名前とアイコンで明るさが違うと、同じ人の色に見えない。
            暗い地の上で絵が沈むのも、これで一緒に防げる
        */
        var NAME_MIN_CONTRAST = 4.5;

        /*
            === 持ち上げる上限（足した）===

            【これが無いと、透かしたときに全員まっ白になる】
            透けるほどログの地は白へ寄る。白い吹き出しの上では
            **つまみ100で地が rgb(114,121,137) まで明るくなり、
            そこでは「白そのもの」ですら 4.36:1 しか出ない**（実測）。
            基準の4.5に届かないので、上の繰り返しが最後まで回りきり、
            **どの色も #FFFFFF が返っていた**——赤い人も青い人も、まっ白である
            （指摘：アイコン・名前・文字・時刻が全部白になる）。

            入れたときの狙いは「**色味が残るので、誰の発言かは見分けられる**」
            だった。**届かないときに白へ塗り潰すのは、その狙いの正反対である。**

            【0.65 は実測から決めた】
            既定（つまみ30）で必要になる持ち上げ量は、
            **どの色でも最大 0.65**（いちばん要るのは黒。216色を総当たりして確認）。
            そこで上限を 0.65 にすれば、**既定の見え方は1ピクセルも変わらない**まま、
            透かしたときの塗り潰しだけが止まる。

            【透けすぎて読めなくなるのは、止めない】
            決定：ここは作り込まず、利用者の好みに任せる。
            ここで守っているのは読みやすさではなく、**誰の色かが分かること**である
        */
        var NAME_MAX_LIFT = 0.65;

        /* 名前の色を決めるときの、地の色（窓を開くたびに測り直す） */
        var privateBgRgb = [20, 32, 58];

        /*
            今そこに描かれている地の色を測って、控える。

            【白の上に重ねた場合で見る】
            窓の下にあるのはチャットログで、吹き出しは白である。
            **いちばん明るくなるのは白の上**なので、そこで基準を満たしていれば
            他のどこでも満たす
        */
        function refreshPrivateBg(logEl) {

            var rgba = toRgba(getComputedStyle(logEl).backgroundColor);

            if (!rgba) {
                return;
            }

            var a = rgba[3];

            privateBgRgb = [
                Math.round(rgba[0] * a + 255 * (1 - a)),
                Math.round(rgba[1] * a + 255 * (1 - a)),
                Math.round(rgba[2] * a + 255 * (1 - a))
            ];
        }

        /* rgb() / rgba() を [r, g, b, a] にする（a が無ければ 1） */
        function toRgba(color) {
            var nums = String(color).match(/[\d.]+/g);
            if (!nums || nums.length < 3) {
                return null;
            }
            return [Number(nums[0]), Number(nums[1]), Number(nums[2]),
                    nums.length > 3 ? Number(nums[3]) : 1];
        }

        function toRgb(color) {

            var hex = String(color).trim();
            var m = /^#([0-9a-f]{6})$/i.exec(hex);

            if (m) {
                var n = parseInt(m[1], 16);
                return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
            }

            var nums = hex.match(/[\d.]+/g);
            return nums && nums.length >= 3
                ? [Number(nums[0]), Number(nums[1]), Number(nums[2])]
                : null;
        }

        function relativeLuminance(rgb) {
            var a = rgb.map(function (v) {
                v = v / 255;
                return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
            });
            return 0.2126 * a[0] + 0.7152 * a[1] + 0.0722 * a[2];
        }

        function contrastRatio(a, b) {
            var l1 = relativeLuminance(a);
            var l2 = relativeLuminance(b);
            var hi = Math.max(l1, l2);
            var lo = Math.min(l1, l2);
            return (hi + 0.05) / (lo + 0.05);
        }

        function readableOnPrivateBg(color) {

            var rgb = toRgb(color);

            if (!rgb) {
                return color;
            }

            var best = rgb;
            var bestRatio = -1;

            for (var t = 0; t <= NAME_MAX_LIFT + 0.0001; t += 0.05) {

                var mixed = rgb.map(function (v) {
                    return Math.round(v + (255 - v) * t);
                });

                var ratio = contrastRatio(mixed, privateBgRgb);

                /* 足りたら、そこで止める（**必要以上に持ち上げない**） */
                if (ratio >= NAME_MIN_CONTRAST) {
                    return 'rgb(' + mixed[0] + ', ' + mixed[1] + ', ' + mixed[2] + ')';
                }

                if (ratio > bestRatio) {
                    bestRatio = ratio;
                    best = mixed;
                }
            }

            /*
                【届かないときは、上限の中でいちばんましなところ】
                白へ塗り潰さない。**濃くしたほうがましな色もある**ので
                （まっ黒は、明るい地の上では持ち上げないほうが読める）、
                「上限まで持ち上げる」ではなく「いちばん比の大きかった値」を返す
            */
            return 'rgb(' + best[0] + ', ' + best[1] + ', ' + best[2] + ')';
        }

        /*
            1行ぶんのHTML。

            【時刻も出す】（判断を覆した）
            「窓が狭いので時刻は出さず、title 属性に残す」としたが、
            実機を見た表示する形にした。
            **本文の下に、右寄せで小さく置いてある**——名前の横に並べると、
            名前が長い人のところで折り返しが1行増えるため。
            title 属性は、そのまま残してある（読み上げや、指を乗せたときのため）

            【時刻はサーバーが決めたものを使う】
            クライアントで new Date() から作ると、**過去の行が全部「今」になる**
            （本体の過去ログとまったく同じ決まり）。
            リアルタイムのぶんだけは、届いた時刻＝今なので、こちらで付けてよい

            【名前にも、その人の色を付ける】
            本体では色を持つのはアイコンだけで、名前は灰色である。
            ここは狭い窓に1行ずつ並ぶ形なので、名前にも色を付ける
            （指摘：色を選ばせているのに、こちらで色を固定するのはおかしい）

            【入口の検査だけでは足りない。出口でもエスケープする】
            iconColor は style の中、iconType は href の中へ入る
        */
        function buildRowHtml(row) {

            if (row.kind === 'date') {
                return '<div class="private-talk-date">' + escapeHtml(row.text) + '</div>';
            }

            /* 【出口のエスケープは、持ち上げたあとに掛けること】
               持ち上げる関数は色を作り直すが、読めない値はそのまま返すため */
            var safeColor = escapeAttr(readableOnPrivateBg(row.iconColor));
            var safeType = escapeAttr(row.iconType);

            /* 画像の発言なら {name, width, height}。文章なら null */
            var image = parseImageToken(row.message);

            /* スタンプの発言なら {code, src, label, setCode}。それ以外は null */
            var stamp = image ? null : parseStampToken(row.message);

            var textClass = image ? ' private-talk-text--image'
                : (stamp ? ' private-talk-text--stamp' : '');

            /* 文章なら、URL をリンクにしてから出す（部屋の吹き出しとまったく同じ関数を通す） */
            var bodyHtml = image ? buildImageHtml(image, true)
                : (stamp ? buildStampHtml(stamp) : buildMessageTextHtml(row.message));

            return '<div class="private-talk-row private-talk-row--' +
                (row.isSelf ? 'self' : 'other') + '"' +
                ' title="' + escapeAttr(row.time) + '">' +

                '<div class="private-talk-avatar">' +
                '<svg viewBox="0 0 512 512" style="color: ' + safeColor + ';">' +
                '<use href="#icon-' + safeType + '"></use>' +
                '</svg>' +
                '</div>' +

                /*
                    【行ごとには、トリップを出さない】
                    相手のトリップは、窓の見出しに1か所だけ出す（buildWindowHtml の見出しの項）。
                    ここ（相手の行の名前の下）に出していたが、2 人だけの窓なので
                    行ごとのトリップは要らない（開発者の判断）。相手の行だけ本文の書き出しが20pxずれる件も、これで消えた
                */
                '<span class="private-talk-name" style="color: ' + safeColor + ';">' +
                escapeHtml(row.nickname) + '</span>' +

                /*
                    画像の発言なら、絵をそのまま出す。

                    【取りに行く先が、部屋あてとは違う】
                    こっそりの画像は App_Data の下にあり、URLを直に叩いても取れない。
                    Chat/PrivateImage.aspx が、毎回「この人は当事者か」を確かめてから返す

                    スタンプの絵は、部屋あてと同じ静的ファイル（共有の素材で、秘密ではない）。
                    押すと、パネルがこの窓の相手あてに開く
                */
                '<span class="private-talk-text' + textClass + '">' +
                bodyHtml +
                '</span>' +

                '<span class="private-talk-time">' + escapeHtml(row.time || '') + '</span>' +

                '</div>';
        }

        /*
            やりとりに1行足す（画面に出ているときは、そこへも足す）

            【日付の区切りも、ここで rows へ入れる】
            描くときに入れると、窓を開き直すたびに判定をやり直すことになり、
            経路によって出たり出なかったりする
        */
        function pushRow(talk, row) {

            if (row.date) {

                var lastDate = null;

                for (var i = talk.rows.length - 1; i >= 0; i--) {
                    if (talk.rows[i].kind === 'message') {
                        lastDate = talk.rows[i].date;
                        break;
                    }
                }

                if (row.date !== lastDate) {
                    appendOne(talk, { kind: 'date', text: row.date });
                }
            }

            appendOne(talk, row);
        }

        function appendOne(talk, row) {

            talk.rows.push(row);

            if (talk.$el) {
                talk.$log.append(buildRowHtml(row));
                scrollToBottom(talk);

                /* Tab で止まるスタンプ・画像を選び直す（部屋のログと同じ。LogKeyNav） */
                LogKeyNav.refresh(talk.$log[0]);
            }
        }

        function scrollToBottom(talk) {
            if (!talk.$el) {
                return;
            }
            var el = talk.$log[0];

            /* 隠れている窓（狭い画面の裏のタブ）は、高さが0でスクロール位置を入れられない。
               見えるようになったときに showLatestIfFollowing が送る */
            if (el.clientHeight === 0) {
                return;
            }

            el.scrollTop = el.scrollHeight;
            talk.followBottom = true;
        }

        /*
            === 窓が見えるようになったとき・大きさが変わったとき、いちばん下を見ていたなら最新へ ===
            （で出た不具合）

            【症状】狭い画面で、タブを切り替えて窓を表に出すと、隠れている間に届いた行が下に隠れていて
            スクロールしないと見えなかった。参加者一覧から窓を開くと、下のほうが見えない状態で出た。

            【原因】行が届いたときの scrollToBottom は、**隠れている窓（display: none。高さ0）には効かない。**
            表に出したとき（setActive）には何もしていなかったので、前の位置のまま出ていた。
            参加者一覧から開くほうは、開いた瞬間は最新が見えていて（開発機で実測＝下端から0px）、
            そのあと入力欄に焦点が入って**ソフトキーボードが出て窓が縮み、最新が下へはみ出していた**
            （前から残っていた「キーボードが出た瞬間、こっそりのログの最新が少し隠れうる」と同じ根）。

            **今日の変更で出たものではない。** 前の版でも、自動で開くオンで
            裏の窓に1通届くと 24px、オフで3通届いてタブを押すと 96px、下に隠れていた（開発機で実測）。
            「手前に出さない」にしたぶん、隠れたまま行が届く場面が増えて、目に付くようになった。

            【読んでいた位置は飛ばさない】
            いちばん下を見ていた窓（followBottom）だけ最新へ送る。上のほうを読んでいた窓は、そのまま。
            隠れる前の位置は、ブラウザが覚えていて表に出すと戻す（開発機で実測＝200 → 200）。
            followBottom を書き換えるのは、**窓が見えている間のスクロールだけ**（下の scroll の受け口）。
            隠れている間に届いた行では書き換えない——ここで「最新ではなくなった」と見なすと、送る先を失う
        */
        function showLatestIfFollowing(talk) {

            if (!talk || !talk.$el || !talk.followBottom) {
                return;
            }

            scrollToBottom(talk);
        }

        /* 窓のログがスクロールされたら、いちばん下を見ているかを控える（見えている間だけ） */
        document.addEventListener('scroll', function (e) {

            var el = e.target;

            if (!el || el.nodeType !== 1 || !el.classList.contains('private-talk-log') || el.clientHeight === 0) {
                return;
            }

            var talk = talkOf(el);

            if (talk) {
                talk.followBottom =
                    (el.scrollHeight - el.scrollTop - el.clientHeight) <= SCROLL_FOLLOW_THRESHOLD_PX;
            }
        }, true);

        /*
            === はじめて開くときの置き場所を決める（広い画面だけ） ===

            【高さは下寄せ】チャットログの下端に、窓の下端を揃える。
            2枚目以降は、上へ少しずつずらして重ねる。

            **【下寄せははない】**（訂正）
            報告は「こっそりのログの流れる向きが、本体と逆」だった。
            調べると並び順は正しく、再現しなかった。そこで「窓が上のほうに出るせいでは」と
            こちらで当たりを付けて下寄せにした——**こちらの解釈による対処**である
            （：こっそりの窓の既定の位置を本体の下の方にする、という指示は出していない）。
            **下寄せにして実機の見え方が良くなったこと自体は残っている**ので、高さはそのままにしてある

            【横は、2通り】

              1. チャットの右の余白に、窓がまるごと入るなら … **余白に置く**
                 （1366px以上の画面なら入る。チャットの中の何も隠さない。
                   入るかどうかは「窓の幅 ＋ チャットとのすき間8px」で見る。
                   画面の右端にすき間は取らない——1366pxでは余白が333pxしかなく、
                   右にもすき間を取ると3px足りずに、いちばん多いノートPCの幅で外れる）
              2. 入らないなら                               … **チャットログの左下**

            **それまではチャットログの右下だった。** 発言に入れるものを選ぶパネルは
            入力行の上・右寄せ（340×420）に出るので、**窓の既定の置き場所とパネルが
            ちょうど重なっていた**（1024pxで実測）。窓のボタンからパネルを開くと、
            その窓がパネルの下に隠れて、絵文字が入ったのかが見えなかった。
            パネルは入力行の上に出るのが自然な位置なので、動かすのは窓のほう
            （指摘：最初に窓を出す場所が悪い）。

            - **余白に置くのを先にしてあるのは、何も隠さないから**。
              右下のときは自分の発言を、左下のときは相手の発言を隠す。
              開発者が本当に欲しかった「本体と秘密窓を並べる」形にも、いちばん近い
            - **左下にするときの重なりは測ってある**：チャットは最大700px。
              窓（8〜328px）とパネル（344〜684px）のあいだが16px空く
            - **2枚目以降のずらし方は、パネルから離れる向き**：
              余白に置くときは右上へ、左下に置くときは左上へ
            - どちらの場合も、最後は setPosition が表示領域の中へ丸める

            【層は画面いっぱいなので、座標はそのまま画面座標として使える】
        */
        function placeWindow(talk, index) {

            var log = document.querySelector('.chat-log-wrapper').getBoundingClientRect();
            var room = document.querySelector('.chat-room').getBoundingClientRect();
            var ww = talk.$el[0].offsetWidth;
            var wh = talk.$el[0].offsetHeight;

            var rightSpace = layerEl().getBoundingClientRect().right - room.right;
            var fitsBeside = rightSpace >= ww + EDGE_MARGIN_PX;

            var x = fitsBeside
                ? room.right + EDGE_MARGIN_PX + (index * CASCADE_STEP_PX)
                : log.left + EDGE_MARGIN_PX - (index * CASCADE_STEP_PX);

            var y = log.bottom - wh - EDGE_MARGIN_PX - (index * CASCADE_STEP_PX);

            setPosition(talk, x, y);
        }

        /*
            位置を入れる。**必ずブラウザの表示領域の中へ丸めてから入れる。**

            === 【、丸めを戻した】画面の外へは出さない ===

            「画面の外へも出せる」形にしたが、実機を見た開発者の判断で
            戻した——指摘：「**ウィンドウの外に出ようとすると消える・
            切れる**」。出せること自体ではなく、**切り取られて見えなくなるのが
            望みではなかった**、という話である。

            【止める基準は、チャットログの枠ではなく表示領域】
            層は広い画面では `position: fixed` で表示領域そのものなので、
            **層の中へ丸めることが、そのまま「ブラウザの外へ出さない」になる。**
            薄いブルー（チャットログ）の外＝部屋の余白側へは、これまでどおり出せる

            【狭い画面（スマホ）には効かない】
            あちらは CSS 側が left/top ごと上書きして覆いかぶさる形にしているので、
            ここで入れた値はそもそも使われない

            【却下】デスクトップに、本体と秘密窓を別々のウィンドウとして並べる案。
            開発者が本当に欲しかったのはこの形だった（昔のほかのチャットが
            そうだった）。**検討したうえで、やらないと決めている**——
              ① 今の作りからの作り直しが大きい
              ② 別ウィンドウは**利用者の操作の直後にしか開けない**ので、
                 作った「送られてきたら自動で開く」が原理的に成り立たなくなる
              ③ スマホには別ウィンドウの概念が無いので、PC用とスマホ用で2本立てになる
            **将来また話が出たら、この3つを先に読むこと。**
        */
        function setPosition(talk, x, y) {

            var layer = layerEl();
            var el = talk.$el[0];
            var maxX = Math.max(0, layer.clientWidth - el.offsetWidth);
            var maxY = Math.max(0, layer.clientHeight - el.offsetHeight);

            talk.x = Math.round(Math.min(Math.max(0, x), maxX));
            talk.y = Math.round(Math.min(Math.max(0, y), maxY));

            el.style.setProperty('--pt-x', talk.x + 'px');
            el.style.setProperty('--pt-y', talk.y + 'px');

            applySizeLimit(talk);
        }

        /*
            === 大きくできる上限を、「今いる場所から表示領域の端まで」にする ===

            【なぜ、これで解けるのか】
            窓の大きさを変えているのはブラウザ自身（CSSの resize）なので、
            **こちらは途中の合図を1つも受け取れない。**
            合図を待つ形にしている限り、
              ・引きずっている最中は、はみ出したままになる
              ・その合図が飛ばない環境では、はみ出したまま戻らない
            のどちらかが必ず残る。

            **上限そのものを詰めておけば、ブラウザが端より先へ広げない。**
            合図を待つ必要が無くなる＝**確かめられない仕組みに寄りかからない。**

            【反省】
            あのとき `ResizeObserver` を「開発機で飛ばないから」と却下して
            `pointerup` を採ったが、**採ったほうも飛ばなかった**（角を掴む操作は
            ブラウザ自身が処理していて、こちらのハンドラまで来ない）。
            **却下の基準を、採る側にも同じように当てること。**
            いちばん良いのは、どちらの合図にも寄りかからない形である

            【窓は、動かしても縮まない】
            上の丸めで x は「表示領域の幅 − 窓の幅」までしか進めないので、
            ここで出す上限は必ず今の幅以上になる。**右へ寄せたら勝手に細くなる、
            という嫌な動きにはならない**（2つの丸めが、同じ式から出ているため）

            【狭い画面では外すこと】
            あちらは覆いかぶさる形で、高さはCSS（--private-talk-height-narrow。全面）が持っている。
            ここで書き込むと、その指定を上書きして窓の高さが変わってしまう
        */
        function applySizeLimit(talk) {

            var el = talk.$el[0];

            if (isNarrow()) {
                el.style.removeProperty('max-width');
                el.style.removeProperty('max-height');
                return;
            }

            var layer = layerEl();

            el.style.maxWidth = Math.max(0, layer.clientWidth - talk.x) + 'px';
            el.style.maxHeight = Math.max(0, layer.clientHeight - talk.y) + 'px';
        }

        /*
            === 覚えていた大きさを入れる／狭いときは外す===

            【狭い画面には入れないこと】
            あちらは CSS が `width: auto` と高さ（--private-talk-height-narrow。G-2e から全面）で覆いかぶさる形にしている。
            **インラインの width / height は、その指定より強い。**
            入れると狭い画面のレイアウトが崩れる
            （applySizeLimit が狭いときに max-* を外しているのと、まったく同じ理由）。

            【層に収まるところまで詰めてから入れる】
            広い画面で大きくしてから、小さい画面でF5されることがある。
            **覚えていた値を、そのまま信じて入れない**
        */
        function applyStoredSize(talk) {

            var el = talk.$el[0];

            if (isNarrow()) {
                el.style.removeProperty('width');
                el.style.removeProperty('height');
                return;
            }

            var saved = layout[talk.anonId];

            if (!saved || !saved.w || !saved.h) {
                return;
            }

            var layer = layerEl();

            el.style.width = Math.min(saved.w, layer.clientWidth) + 'px';
            el.style.height = Math.min(saved.h, layer.clientHeight) + 'px';
        }

        /*
            === 窓の位置と大きさを控える===

            【大きさにだけ、合図が無い】
            位置はこちらが動かしているので `setPosition` という関所があるが、
            **大きさを変えているのはブラウザ自身**（CSSの resize）で、
            こちらには合図が1つも来ない——D-4bで `ResizeObserver` も `pointerup` も
            飛ばないことを実測している。

            そこで**「離れる直前に1回だけ見に行く」**形にしてある。
            **採る側にも同じ基準を当てた**（教訓）——F5のときに
            `beforeunload` と `pagehide` が飛び、そこからの sessionStorage への
            書き込みが読み込み後も残ることを、実測して確かめてある。
            ドラッグの終わりと窓の開け閉めでも呼んでおくので、
            仮にその合図の来ない環境があっても、位置のほうは最後の値が残る。

            【狭いときは、何も控えないこと】
            覆いかぶさる形の窓は画面いっぱいなので、その大きさを控えると
            **次に広い画面で開いたときに、画面いっぱいの窓が復元される。**
            そもそも狭い画面では動かせも広げられもしないので、控えるものが無い
        */
        function saveLayout() {

            if (isNarrow()) {
                return;
            }

            openTalks().forEach(function (talk) {

                var el = talk.$el[0];

                layout[talk.anonId] = {
                    x: talk.x,
                    y: talk.y,
                    w: el.offsetWidth,
                    h: el.offsetHeight
                };
            });

            saveState();
        }

        /*
            表示領域が小さくなったら、はみ出した窓を引き戻す

            === 【、復活させた】 ===

            「画面の外へ出せる」ようにしたときに外したものである
            （外へ追いやった窓が、大きさを変えただけで戻ってくるのは変だったため）。
            **外に置かない仕様へ戻した以上、これが無いと、窓を縮めたときに
            表示領域の外へ取り残される。**

            【狭い⇔広いをまたいだときの並べ直し（onModeChange）とは、役目が違う】
            あちらは「狭かったころの座標がそのまま効く」のを防ぐためのもので、
            置き場所を**決め直す**。こちらは今の場所を**中へ寄せるだけ**である
        */
        function reclampAll() {
            openTalks().forEach(function (talk) {
                if (talk.x !== null) {
                    setPosition(talk, talk.x, talk.y);
                }
            });
        }

        /*
            狭いときのタブ（相手の名前を並べて切り替える）

            【広いときは、そもそも出さない】CSS側で消している

            【窓を開いていない相手の未読も、タブとして並べる】
            全面の間は、バーの上の「こっそり未読」を出さない（タブの帯と重なるため。
            updateOutsideUnreadMark）。その代わりに、**窓を開いていない相手の未読を、
            開いている窓のタブの後ろへ「未読」付きで並べる**。押せばその窓が開く（下のタブのクリック）。
            並びは、未読が新しい順（最後に来た相手が、開いている窓のすぐ後ろ）。
            **窓は開かない**——開くかどうかは本人が決める（「自動で開く：オフ」の意味を変えない）

            【タブの印は、その相手の窓を見たら消える】setActive が clearUnread を呼ぶ
        */
        function renderTabs() {

            var $tabs = $('#privateTalkTabs');
            var list = openTalks();

            if (list.length === 0) {
                $tabs.empty().prop('hidden', true);
                syncFullScreen();
                return;
            }

            list = list.concat(pendingUnread());

            $tabs.prop('hidden', false).html(list.map(function (talk) {

                var isActive = (talk.anonId === activeAnonId);

                return '<button type="button" class="private-talk-tab"' +
                    ' data-anon-id="' + escapeAttr(talk.anonId) + '"' +
                    ' role="tab" aria-selected="' + (isActive ? 'true' : 'false') + '">' +
                    escapeHtml(talk.nickname) +
                    (talk.unread ? '<span class="private-talk-tab-unread">未読</span>' : '') +
                    '</button>';
            }).join(''));

            syncFullScreen();
        }

        /*
            窓を開いていない相手のうち、まだ見ていない発言があるもの（未読が新しい順）。
            バーの上の「こっそり未読」が知らせているのは、この人たちである
            （窓を開いている相手の未読は、狭い画面ならタブに出ている。広い画面では、
              開いている窓はそもそも未読にならない＝receive を参照）
        */
        function pendingUnread() {
            return Object.keys(talks)
                .map(function (id) { return talks[id]; })
                .filter(function (talk) { return talk.unread && !talk.$el; })
                .sort(function (a, b) { return b.unreadSeq - a.unreadSeq; });
        }

        /*
            === 全面か（狭い画面で、窓を1枚以上開いているか）===

：スマホでこっそりの窓を開いたら全面にする。上のバーだけ残し、本体の入力欄などは消す。
            見た目は CSS が .chat-room.is-private-full で決める（窓の高さ・本体の入力欄を隠す）。
            ここは印を付け外しして、全面に入った／出たときの後始末をまとめて呼ぶだけ。

            【印の出し入れは、ここ1か所】
            呼ばれるのは、開いている窓の数が変わったとき（renderTabs）と、
            狭い⇔広いをまたいだとき（onModeChange・resize）。
            それまでの is-private-typing（こっそりの入力欄に焦点がある間）は、窓を開いている間に
            必ず含まれるので、この印に置き換えて消した
        */
        var wasFullScreen = false;

        function isFullScreen() {
            return isNarrow() && openTalks().length > 0;
        }

        function syncFullScreen() {

            var full = isFullScreen();

            if (full === wasFullScreen) {
                return;
            }

            wasFullScreen = full;

            /*
                【本体のログがいちばん下だったかは、印を変える「前」に測る】
                本体の入力欄が隠れる／戻ると、本体のログの高さが変わる。
                窓を閉じて入力欄が戻ると、ログが縮んで**最新の行が入力欄の裏へ隠れる**ので、
                いちばん下を見ていた人だけ、いちばん下へ送り直す（行を足す前に測るのと同じ決まり）
            */
            var wasAtBottom = isChatLogNearBottom();

            $('.chat-room').toggleClass('is-private-full', full);

            /* 窓の × の名前も替える（全面なら「すべて閉じて、部屋にもどる」） */
            applyCloseLabels(full);

            if (wasAtBottom) {
                scrollChatLogToBottom();
            }

            /* 部屋の「新着あり」は、全面の間は出さない（syncNewMessageBadge の項） */
            syncNewMessageBadge();

            /* バーの上の「こっそり未読」も、全面の間は出さない（タブに並ぶため） */
            updateOutsideUnreadMark();

            /*
                入れるもののパネルが開いていたら、置き場所と押し上げを測り直す
                （部屋あてに開いていたら閉じる。本体の入力欄が消えるため）。
                【InsertPanel が在るかを確かめてから呼ぶ】今は窓の復元（renderInitialLog）より先に
                InsertPanel ができているが、起動の順番が変わった日に、部屋の画面ごと止まらないように
            */
            if (typeof InsertPanel !== 'undefined' && InsertPanel) {
                InsertPanel.onFullScreenChanged(full);
            }

            /*
                【広い画面へ出たら、開いている窓の未読を消す】
                狭い画面では、裏に回った窓（タブ）に届いたぶんを未読にしている。
                広い画面では窓が全部並んで見えるので、その印を残すと
                **見えている窓なのに「未読」が残る**（参加者一覧の印と、読み上げ）
            */
            if (!full && !isNarrow()) {
                openTalks().forEach(clearUnread);
            }
        }

        /*
            狭いときに、どの窓を見せるかを決める

            【is-active は、広いときも付けておく】
            見た目に使うのは狭いときだけだが、状態を2通りに分けると
            切り替えたときに食い違う。付けるのは常に1つ、で統一してある
        */
        function setActive(anonId) {

            if (anonId && talks[anonId] && talks[anonId].$el) {
                activeAnonId = anonId;
            } else {
                var list = openTalks();
                activeAnonId = list.length ? list[list.length - 1].anonId : null;
            }

            openTalks().forEach(function (talk) {
                talk.$el.toggleClass('is-active', talk.anonId === activeAnonId);
            });

            /*
                選ばれた窓を、いちばん手前へ。**上の is-active を付けるだけで済んでいる。**

                【前後は z-index で決める。DOMは動かさない】
                CSS の .private-talk-window.is-active が z-index を持つ。
                層の中は「ふつう」と「手前」の2つの値しか無いので、
                **窓が何枚になっても値を配らずに済む**（DOMの順を選んだ理由は、そのまま満たしている）。
                手前でない窓どうしの前後は、開いた順（DOMの順）のまま。

                【なぜ、DOMの並べ直しをやめたのか】
                それまでは選ばれた窓を層の末尾へ移していた（appendTo）。
                押し始めた瞬間（pointerdown）にそれが走るので、**押している最中の要素が
                いったん外れて入り直し、そのボタンの click が届かなかった**
                （本番の実機で確定。裏の窓のスタンプも送信ボタンも、1度目は窓が手前に出るだけだった）。
                入れ直すと中のスクロール位置も0へ戻るので、控えて戻す手当ても要っていた。
                **並べ直しそのものをやめたので、どちらも原因ごと消えた。**

                【キーボードと読み上げの移動順は、開いた順のまま動かない】
                以前は手前に出した窓が移動順でも最後になっていたが、今は順番が変わらない
            */

            /* 表に出た窓は、見たことにする */
            if (activeAnonId && talks[activeAnonId]) {

                /*
                    隠れている間に届いた行を見せる（いちばん下を見ていた窓だけ。showLatestIfFollowing の項）。
                    is-active を付けたあとなので、狭い画面でも窓はもう見えている
                */
                showLatestIfFollowing(talks[activeAnonId]);

                clearUnread(talks[activeAnonId]);
            }

            renderTabs();
        }

        /*
            === 未読の知らせを、パネルの外にも出す ===

            参加者一覧の中の印は**パネルを開かないと見えない。**
            「自動で開く」をオフにしていると窓も開かないので、
            **パネルを開くまで、届いたこと自体に気づけない**
            （指摘：作った本人でも何の音か分からず、不具合かと思った）。

            そこで人数ボタンのあたりにも「こっそり未読」と出す。
            **参加者一覧の中の印は残す**——誰から来たのかは、そちらでしか分からない

            【読み上げソフトへの伝え方は、#newMessageAnnounce と同じ形にしてある】
            出た瞬間に1回だけ文言を入れ、消えるときに空へ戻す。
            **既に出ているときは何もしない**（届くたびに読み上げ続けるのを防ぐ）
        */
        var UNREAD_LABEL = 'こっそり未読';
        var UNREAD_ANNOUNCE_TEXT = 'こっそりの未読があります';

        function anyUnread() {
            return Object.keys(talks).some(function (id) { return talks[id].unread; });
        }

        /*
            【目で見る印と、読み上げとで、出す条件が違う】（分けた）

              読み上げ（#privateUnreadAnnounce） … これまでどおり「どこかに未読があるか」
              バーの上の印（#privateUnreadMark）  … 「窓を開いていない相手の未読があるか」かつ「全面でない」

            印は押すと窓を開くので、**開く窓が無い未読（＝窓を開いている相手の未読）では出さない**。
            その未読は、狭い画面ではタブに出ている。
            全面の間は、印がタブの帯とちょうど重なって下のタブへの指を横取りするので出さない。
            その間の未読は、タブの帯に「未読」付きで並ぶ（renderTabs）。
            読み上げのほうは、見た目の置き場所に関係なく、未読が来たことを1回だけ伝える
        */
        function updateOutsideUnreadMark() {

            var $mark = $('#privateUnreadMark');
            var $announce = $('#privateUnreadAnnounce');

            var shouldAnnounce = anyUnread();
            var announced = ($announce.text() !== '');

            /* 【罠】既に入っている／既に空なら、触らない。
               触ると、届くたびに読み上げ用の文言が入り直して読まれ続ける。
               【罠】消えるときは、必ず空へ戻すこと。
               aria-live は中身が変わったときに読むので、残したままだと次に出ても一言も読まれない */
            if (announced !== shouldAnnounce) {
                $announce.text(shouldAnnounce ? UNREAD_ANNOUNCE_TEXT : '');
            }

            var shouldShow = pendingUnread().length > 0 && !isFullScreen();

            /*
                【印に焦点があるまま消すときは、焦点の行き先を先に決めておくこと】
                押して消えるときは openLatestUnread が窓のログへ焦点を移している。
                それ以外で消える（相手を無視した・入り直した）ときは、人数ボタンへ返す
                （パネルの閉じるボタンと同じく、キーボードで使っている人が居場所を見失わないため）
            */
            if (!shouldShow && document.activeElement === $mark[0]) {
                $('#participantCountBtn').trigger('focus');
            }

            $mark.prop('hidden', !shouldShow);
        }

        /*
            未読になった順番。「最後に来た相手」を決めるために使う（Date.now() だと同じミリ秒で並びうる）
        */
        var unreadSeq = 0;

        function markUnread(talk) {

            /*
                【もう未読でも、順番は付け直す】
                「最後に来た相手」は、最後に発言が届いた相手である。
                未読の2通目が届いたら、その相手がいちばん新しくなる
            */
            talk.unreadSeq = ++unreadSeq;

            if (!talk.unread) {
                talk.unread = true;
                renderTabs();
                renderParticipantList();
                updateOutsideUnreadMark();
            } else if (!talk.$el) {
                /* 並び（タブの帯の未読の順）だけ変わる */
                renderTabs();
            }

            /* F5でも消えないように覚える（storedUnread の項） */
            rememberUnread();
        }

        function clearUnread(talk) {
            if (talk.unread) {
                talk.unread = false;
                renderTabs();
                renderParticipantList();
                updateOutsideUnreadMark();
                rememberUnread();
            }
        }

        /*
            窓を開く（やりとりの中身から、まるごと描き直す）

            【どの経路から呼んでも、履歴が入る】
            ボタンから開いても、相手の発言で開いても、F5で復元しても、
            描くのは必ずこの関数である

            inBack … true なら、手前に出さない。
                      PC で別の相手の窓を読んでいる最中に届いたこっそりを、その裏へ開くため。
                      今手前にある窓はそのまま手前に残る（読んでいる窓を裏に回さない）
        */
        function openWindow(talk, inBack) {

            if (talk.$el) {

                /* 手前に出さないなら、何もしない（窓はもう在る） */
                if (inBack) {
                    return talk;
                }

                /*
                    既に開いている窓を、もう一度「こっそり話す」で呼んだとき。

                    **前面に出すだけでよい。** 位置を引き戻す処理は要らなくなった——
                    setPosition が必ず表示領域の中へ丸めるので、
                    そもそも見えない場所に窓が置かれることが無いためである
                    （あった bringIntoView は、この丸めを戻した時点で
                      何もしない関数になったので消した）
                */
                setActive(talk.anonId);
                return talk;
            }

            /* ずらす枚数は、今開いている窓の数で決める */
            var index = openTalks().length;

            talk.$el = $(buildWindowHtml(talk));
            talk.$log = talk.$el.find('.private-talk-log');
            talk.$input = talk.$el.find('.private-talk-input');
            talk.$send = talk.$el.find('.private-talk-send');
            talk.$reject = talk.$el.find('.private-talk-reject');

            $('#privateTalkLayer').append(talk.$el);

            /*
                【行を描くより先に、地の色を測ること】
                名前の色は「暗い地の上で読めるか」で決まるので、
                今どれくらい透けているかが分かっていないと決められない。
                DOMへ入れたあとでないと測れないので、この順番になる
            */
            refreshPrivateBg(talk.$log[0]);

            /*
                【rows の順に、上から並べる】
                リアルタイムで足したぶんも、過去ログから来たぶんも、
                この1か所で同じ向きに描かれる
            */
            talk.$log.html(talk.rows.map(buildRowHtml).join(''));

            /* Tab で止まるスタンプ・画像を決める（LogKeyNav。下端へ送ったあとのコマで選ばれる） */
            LogKeyNav.refresh(talk.$log[0]);

            /*
                === 覚えていた大きさと位置を戻す===

                【大きさが先。位置はそのあと】
                位置の丸めは窓の今の大きさ（offsetWidth）から上限を出すので、
                先に大きさを入れておかないと**古い大きさで丸めた位置**になる。

                【必ず丸め直すこと】
                小さい画面でF5すると、大きすぎる窓や画面の外の位置が戻りうる。
                applyStoredSize が層に収まるところまで詰め、
                そのあと setPosition が表示領域の中へ丸める
            */
            applyStoredSize(talk);

            var saved = layout[talk.anonId];

            if (talk.x === null && saved && typeof saved.x === 'number') {
                talk.x = saved.x;
                talk.y = saved.y;
            }

            /* 位置は、DOMへ入れてからでないと大きさが測れない */
            if (talk.x === null) {
                placeWindow(talk, index);
            } else {
                setPosition(talk, talk.x, talk.y);
            }

            updateSendState(talk);

            rememberOpened(talk.anonId);

            /*
                【裏へ開くときは、今の手前の窓を手前のまま選び直す】
                setActive は選ばれた窓以外から is-active を外すので、
                新しい窓は「ふつう」の段に入り、手前の窓の後ろになる（z-index の段）
            */
            setActive((inBack && activeAnonId) ? activeAnonId : talk.anonId);

            /*
                【いちばん下（最新）を見せるのは、setActive のあと】
                setActive は、手前へ出すときに窓をDOMごと動かす。
                動かすとスクロール位置が0へ戻るので、**先に送っても無駄になる**
                （実測で確定。818 → 0 に戻っていた）
            */
            scrollToBottom(talk);

            return talk;
        }

        /*
            窓を閉じる（やりとりの中身は消さない）

            deferActive … true なら、手前の窓を選び直さない（呼んだ側が最後に1回だけ setActive(null) を呼ぶ）。
                          何枚もまとめて閉じるとき用（closeAllWindows）。1枚閉じるたびに選び直すと、
                          そのとき手前に来た窓の未読が「見たこと」になって消える（見ていないのに）
        */
        function closeWindow(anonId, remember, deferActive) {

            var talk = anonId ? talks[anonId] : null;

            if (!talk || !talk.$el) {
                return;
            }

            /*
                パネルがこの窓あてに開いていたら、閉じる。
                入れる先の入力欄が、これから消えるため
            */
            InsertPanel.onTargetGone(anonId);

            talk.$el.remove();
            talk.$el = null;
            talk.$log = null;
            talk.$input = null;
            talk.$send = null;
            talk.$reject = null;

            /*
                位置も大きさも忘れる。次に開いたときは、また下寄せから並べ直す
                （閉じた場所を覚えていると、画面の大きさが変わったあとに
                  変なところへ出てくる）

                【も、ここは変えていない】
                F5で覚えておくことと、**閉じて開き直したら忘れることは別の話**である
。覚えた記録もここで一緒に捨てる——
                残すと「自分で閉じたのに、前の大きさで開き直る」ことになる
            */
            talk.x = null;
            talk.y = null;
            delete layout[anonId];
            saveState();

            if (remember) {
                forgetOpened(anonId);
            }

            if (!deferActive) {
                setActive(null);
            }
        }

        /*
            開いている窓を、全部閉じる（スマホで全面のときの ×）

            【閉じたあと、手前を選び直すのは最後の1回だけ】closeWindow の deferActive の項。
            見ないまま閉じた窓の未読は残る——窓を持たない未読になるので、
            全面が解けたあと、バーの上の「こっそり未読」が知らせる
        */
        function closeAllWindows() {
            openTalks().forEach(function (talk) {
                closeWindow(talk.anonId, true, true);
            });
            setActive(null);
        }

        /*
            === 窓の × の名前（読み上げソフトと、指を乗せたときの説明）===

            スマホで全面のときは、× を押すと**開いている窓が全部閉じる**（部屋に戻る）。
            PC では今までどおり、その窓だけ閉じる。**同じ × が、画面の幅で意味を変える。**
            目で見る人は、全面かどうかが画面で分かる。読み上げソフトには分からないので、
            **名前（aria-label）を全面のときだけ「すべて閉じて、部屋にもどる」に替えて、押す前に伝える**。
            見た目は × のまま（開発者の判断：今のままでよい）。

            【名前を決めるのは、この1か所】
            窓を作るとき（buildWindowHtml）と、全面に入った／出たとき（syncFullScreen）の2か所から呼ぶ。
            窓を作る時点では、その窓はもう開く前提なので「狭い画面なら全面」で決まる
        */
        var CLOSE_LABEL_ONE = 'こっそりの窓を閉じる';
        var CLOSE_TITLE_ONE = '閉じる';
        var CLOSE_LABEL_ALL = 'こっそりの窓をすべて閉じて、部屋にもどる';
        var CLOSE_TITLE_ALL = 'すべて閉じる';

        function applyCloseLabels(full) {
            $('.private-talk-close')
                .attr('aria-label', full ? CLOSE_LABEL_ALL : CLOSE_LABEL_ONE)
                .attr('title', full ? CLOSE_TITLE_ALL : CLOSE_TITLE_ONE);
        }

        /*
            相手が居るかどうかで、送信できる／できないを決める

            【決めるのは、この1か所だけにしてある】
            あちこちから prop('disabled', ...) を書くと、あとから起きたほうが
            もう一方の判断を黙って上書きする（本体の updateSendButtonState と同じ罠）
        */
        function updateSendState(talk) {

            if (!talk.$el) {
                return;
            }

            talk.$input.prop('disabled', !talk.present);
            talk.$send.prop('disabled', !talk.present);
            talk.$el.find('.private-talk-insert-btn').prop('disabled', !talk.present);

            /*
                パネルがこの窓あてに開いていたら、閉じる。
                送れない相手へ、絵文字を入れ続けられてしまうため
            */
            if (!talk.present) {
                InsertPanel.onTargetGone(talk.anonId);
            }
            talk.$input.attr('placeholder',
                talk.present ? 'こっそり送る' : '相手が退室したため、送信できません');
        }

        function showReject(talk, reason) {

            if (!talk.$el) {
                return;
            }

            talk.$reject.text(reason || '');

            /*
                【理由が出たら、つまみは名前ごと引っ込める】
                同じ行を分け合っているので、両方出すと狭い窓で両方潰れる。
                本体の「改行の案内」が、断られた理由に場所を譲るのと同じ扱い。
                理由は次に送信が通れば消えるので、つまみもそこで戻る。

                【名前とつまみは、1つの箱にまとめて出し入れする】
                別々に hidden を付け外しすると、いつか片方だけ忘れる
            */
            talk.$el.find('.private-talk-seethrough-box').prop('hidden', !!reason);

            /*
                【理由が出ている間だけ、足もとにログと同じ地を敷く】
                枠の上では赤が 3.33:1 しか出ないため（ログの上なら 5.90:1）。
                **色は1つも変えず、地のほうを変えている**——
                赤を薄くすると「エラーだ」と色から伝わらなくなるためである
                （開発者の判断）。見た目の理由は CSS 側に書いてある。

                【出し入れは、必ずここでまとめて行うこと】
                つまみを引っ込めるのと、地を敷くのは**同じ合図**である。
                別々の場所に書くと、いつか片方だけ忘れる
            */
            talk.$el.find('.private-talk-foot')
                .toggleClass('is-showing-reject', !!reason);
        }

        /* 実際に送る。受け付けられたときだけ入力欄を消す（本体と同じ判断） */
        function send(talk) {

            if (!talk.$el || !talk.present) {
                return;
            }

            if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                showReject(talk, 'サーバーとの接続が切れています。');
                return;
            }

            /*
                ここでの正規化は「防御」ではなく「親切」である。
                まったく同じ処理をサーバー側でも必ず行う（本体の sendMessage と同じ）
            */
            var message = talk.$input.val().trim();
            message = message.replace(/\r\n/g, '\n').replace(/\n{3,}/g, '\n\n');

            if (message === '') {
                return;
            }

            chatHub.server.sendPrivateMessage(talk.anonId, message).done(function (result) {

                if (!result || (!result.accepted && !result.rejectReason)) {
                    debugLog('サーバー側がこっそりを受け付けなかった（理由なし）');
                    showServerTroubleBar();
                    return;
                }

                if (result.accepted) {
                    /*
                        入力欄を消すのは、受け付けられたときだけ。
                        呼んだ直後に消すと、断られたときに打った文章が失われる
                    */
                    talk.$input.val('');
                    showReject(talk, '');

                    /*
                        パネルがこの窓あてに開いていたら、閉じる。
                        本体の発言を送れたときに閉じるのと、同じ判断
                        （開いたままだと、送った発言がパネルの下に隠れる）
                    */
                    InsertPanel.closeIfTarget(talk.anonId);
                    return;
                }

                showReject(talk, result.rejectReason);

            }).fail(function () {
                showReject(talk, '送信できませんでした。');
            });
        }

        /* イベントの起きた要素から、そのやりとりを引く */
        function talkOf(el) {
            return talks[$(el).closest('.private-talk-window').attr('data-anon-id')];
        }

        /* === 窓の中の操作。窓は動的に増えるので、委譲で受ける === */

        /*
            === 【必須】送信ボタンは、入力欄から焦点を奪わないこと ===

            開発者の実機（Android）で、**こっそりの窓だけ、日本語を確定しないまま
            送信ボタンを押すと送れない**という報告が出た（本体の入力欄は問題なし）。

            押した瞬間に何が起きるかを順に追うと、こうなる。

              1. 入力欄から焦点が外れる
              2. IMEが変換を確定する → **変換候補の帯が消える**
              3. キーボードが低くなる → viewport の interactive-widget=resizes-content で
                 ページ全体が広がる
              4. **窓が動く**（狭い画面の窓は、チャットの領域を基準に置いてあるため）
              5. 指の下からボタンが逃げるので、**click が届かない**

            **本体の入力欄が無事なのは、あちらが画面の下端に貼り付いていて、
            ページが広がっても送信ボタンの位置が変わらないからである。**

            `pointerdown` で `preventDefault()` すると、焦点がそのまま入力欄に残る。
            1が起きなければ2〜5も起きないので、この連鎖ごと断てる。
            **`click` は、これをしても今までどおり飛ぶ**（抑えられるのは焦点の移動と、
            マウス互換イベントだけ）。

            【この症状は開発機では再現できない】
            Androidの日本語入力そのものが要る。ここは**実機で確かめる項目**である
        */
        $(document).on('pointerdown', '.private-talk-send', function (e) {
            e.preventDefault();
        });

        $(document).on('click', '.private-talk-send', function () {

            var talk = talkOf(this);

            if (!talk) {
                return;
            }

            send(talk);

            /*
                === 【必須】送ったら、自分から焦点を外す===

                開発者の実機（Android）で、**こっそりの窓だけ、送信しても
                キーボードが閉じない**という報告が出た（本体の入力欄は閉じる）。

                原因は、すぐ上の pointerdown の preventDefault である。
                **押しても焦点が入力欄に残るので、キーボードも閉じない。**
                本体は同じ手当てをしていないため、押した時点で焦点が外れて閉じる——
                「秘密窓だけ」という切り分けと、ぴったり合う。

                【は、壊れない】
                あちらが守っているのは**押した瞬間**の連鎖（焦点が外れる →
                IMEが確定する → キーボードが低くなる → 窓が動く → click が届かない）で、
                ここは**その click を受け取ったあと**である。
                届いたものを取り消すことはできないので、順番として噛み合わない。

                【Enterで送ったときは、外さないこと】
                この窓は Enter でも送れる。キーボードの Enter で送るのは
                「続けて打つ」場面なので、そこで焦点を外すと打ち直しになる。
                **だから外すのは、送信ボタンを押したこの経路だけ**にしてある
                （keydown 側には書かない。send の中にも書かない）
            */
            talk.$input.blur();
        });

        /*
            === こっそりの窓の、入れるものを選ぶボタン ===

            【pointerdown で焦点を奪わないこと】
            送信ボタンとまったく同じ手当てである。押した瞬間に入力欄から焦点が外れると、
            IMEの確定 → キーボードが低くなる → ページが広がる → 窓が動く、と連鎖して、
            **指の下からボタンが逃げる**（踏んだ）。
        */
        $(document).on('pointerdown', '.private-talk-insert-btn', function (e) {
            e.preventDefault();
        });

        $(document).on('click', '.private-talk-insert-btn', function () {

            var talk = talkOf(this);

            if (!talk || !talk.present) {
                return;
            }

            /* 本体のパネルを、この窓あてに開く（既にこの窓あてなら閉じる） */
            InsertPanel.toggleFor(talk.anonId);

            /*
                【click を受け取ったあとで、焦点を外す】
                パネルが開くので、ソフトキーボードは閉じたほうがよい
                （開いたままだと、パネルの入る場所がほとんど残らない）。
                送信ボタンで やったのと、同じ順番である——
                押した瞬間の連鎖は上の preventDefault が止めていて、
                ここはその click が届いたあとなので、噛み合わない
            */
            talk.$input.blur();
        });

        /*
            === 窓の × ===

            【スマホで全面のときは、開いている窓を全部閉じる】
            要望：タブの切り替えがあるのだから、閉じるボタンを押したら
            全部まとめて閉じる。
            閉じてもやりとりの中身は残り、狭い画面では窓の大きさも覚えていないので、失うものは無い。
            開き直すのは、参加者一覧の「こっそり話す」か、未読ならバーの上の「こっそり未読」から。
            PC は今までどおり、その窓だけ閉じる（窓を並べて置けるので、1枚ずつ閉じたい）。

            【閉じたあと、焦点を部屋のログへ移す】
            押した × が消えるので、移さないとキーボードや読み上げで使っている人が居場所を見失う。
            PC で窓がまだ残っていれば、手前になった窓のログへ。
            **本体の入力欄へは移さない**（スマホでソフトキーボードが開いてしまう）
        */
        $(document).on('click', '.private-talk-close', function () {

            if (isFullScreen()) {
                closeAllWindows();
            } else {
                closeWindow($(this).closest('.private-talk-window').attr('data-anon-id'), true);
            }

            var next = (activeAnonId && talks[activeAnonId] && talks[activeAnonId].$el)
                ? talks[activeAnonId].$log[0]
                : document.getElementById('chatLog');

            if (!next.hasAttribute('tabindex')) {
                next.tabIndex = -1;
            }

            next.focus({ preventScroll: true });
        });

        /*
            透け具合のつまみを動かした。

            【input で拾うこと】change だと、指を離すまで変わらない。
            見ながら合わせる値なので、動かしている最中に変わってほしい

            【つまみは帯の上にある＝ドラッグの掴む場所と重なる】
            見出しの帯を掴んで動かす処理は、ボタンの上から始まったときは掴まない
            形にしてある（closest('button')）。**つまみは button ではないので、
            そこに引っかからない。** input も除くようにしてある
        */
        $(document).on('input', '.private-talk-seethrough', function () {
            seeThrough = parseInt(this.value, 10);
            storedSeeThrough = seeThrough;
            saveState();
            applySeeThrough();
        });

        /* 自動で開くの切り替え。窓からでも設定パネルからでも、同じところへ入る */
        $(document).on('click', '.private-talk-autoopen', function () {
            PrivateTalk.setAutoOpen(!autoOpen);
        });

        /*
            窓のどこかを触ったら、その窓を前に出す（狭いときは、その窓に切り替わる）

            【キーボードで窓の中へ入ったときも、前に出す】
            Tab で裏の窓のボタンへ来ると、そのボタンが手前の窓の下に隠れていて見えなかった。
            それまでは、前に出す＝DOMを並べ直すことだったので、焦点が入った瞬間に
            並べ直すと焦点そのものが外れてしまい、入れられなかった。
            **今は並べ直さない（z-index だけ）ので、焦点が入ったときに前へ出してよい**
        */
        $(document).on('pointerdown focusin', '.private-talk-window', function () {
            var talk = talkOf(this);
            if (talk && talk.anonId !== activeAnonId) {
                setActive(talk.anonId);
            }
        });

        /*
            狭いときのタブ

            【まだ窓を開いていない相手のタブもある】（renderTabs の項）
            そのタブを押したら、窓を開く（開けば手前に出て、未読の印も消える）
        */
        $(document).on('click', '.private-talk-tab', function () {

            var talk = talks[$(this).attr('data-anon-id')];

            if (!talk) {
                return;
            }

            if (talk.$el) {
                setActive(talk.anonId);
            } else {
                openWindow(talk);
            }
        });

        /*
            Enterで送る（本体の入力欄と、まったく同じ規則にしてある）

            【罠】変換中のEnterでは送らないこと。
            日本語入力で変換を確定しただけなのに、送信されてしまう
        */
        $(document).on('keydown', '.private-talk-input', function (e) {

            if (e.key !== 'Enter' || e.shiftKey) {
                return;
            }

            if (e.originalEvent && e.originalEvent.isComposing) {
                return;
            }

            e.preventDefault();

            var talk = talkOf(this);
            if (talk) {
                send(talk);
            }
        });

        /*
            === 見出しの帯を掴んで動かす（広い画面だけ） ===

            【キーボードだけの人が詰まらないこと】
            動かせるのは「あると便利」であって、無くても使える。
            送信・閉じる・タブ・自動で開くは、すべてボタンなのでTabで届く。
            帯そのものはフォーカスを取らないので、Tabの順番も増えない

            【ボタンの上から始めない】
            閉じるボタンを押したつもりが、ドラッグの開始になってしまう
        */
        var dragging = null;

        $(document).on('pointerdown', '.private-talk-head', function (e) {

            if (!canDrag() || e.button !== 0) {
                return;
            }

            /* ボタンや、つまみ（スライダー）の上から始まったときは掴まない。
               掴むと、閉じるつもりが窓ごと動く／つまみが動かせなくなる */
            if ($(e.target).closest('button, input, a').length) {
                return;
            }

            var talk = talkOf(this);

            if (!talk || !talk.$el) {
                return;
            }

            dragging = {
                talk: talk,
                startX: e.clientX,
                startY: e.clientY,
                originX: talk.x,
                originY: talk.y
            };

            this.setPointerCapture(e.pointerId);
            e.preventDefault();
        });

        $(document).on('pointermove', '.private-talk-head', function (e) {

            if (!dragging) {
                return;
            }

            setPosition(dragging.talk,
                dragging.originX + (e.clientX - dragging.startX),
                dragging.originY + (e.clientY - dragging.startY));
        });

        $(document).on('pointerup pointercancel', '.private-talk-head', function (e) {

            if (!dragging) {
                return;
            }

            try {
                this.releasePointerCapture(e.pointerId);
            } catch (err) {
                /* 既に離れていることがある。掴み直せるので、黙って進む */
            }

            dragging = null;

            /* 動かし終わった場所を控える（F5で戻すため） */
            saveLayout();
        });

        /*
            ブラウザの大きさが変わったら、はみ出した窓を引き戻す（reclampAll の項を参照）。
            狭い⇔広いをまたいでいたら、全面の印も付け直す（下の onModeChange でも呼ぶ。
            どちらか片方の合図しか来ない環境でも付け直せるように、両方から呼んである。2度目は何もしない）
        */
        $(window).on('resize', function () {
            reclampAll();
            syncFullScreen();

            /*
                窓が縮んだ（スマホでソフトキーボードが出た）ら、いちばん下を見ていた窓は最新へ送り直す。
                縮んでもスクロール位置は変わらないので、送り直さないと最新が下へはみ出す
                （showLatestIfFollowing の項。followBottom は縮む前の値のまま残っている）
            */
            openTalks().forEach(showLatestIfFollowing);
        });

        /*
            === 【やめた】大きさを変えたあとに、位置を丸め直す形 ===

            D-4bでは `pointerup`（掴んでいた指を離した時点）で丸め直していたが、
            **その合図がこちらまで来なかった**（角を掴んで大きさを変える操作は、
            ブラウザ自身が処理しているため）。結果、
            **広げるとはみ出したまま戻らない**という形で本番に出た。

            **今は、そもそも端より先へ広がれないようにしてある**（applySizeLimit）。
            合図を待たないので、引きずっている最中もはみ出さない
        */

        /*
            === 狭い⇔広いをまたいだら、置き場所を決め直す ===

            【なぜ、引き戻すだけでは足りないのか】
            狭いときの窓は、CSS側が画面いっぱいに広げていて、
            --pt-x / --pt-y は使われていない。そのまま広い画面へ移ると、
            **狭かったころの座標がそのまま効いて、変なところに出てくる。**
            実測＝スマホ幅で開いた窓を900pxへ広げると、
            右下から 380,262 という中途半端な位置に残った（はみ出してはいない）。

            またぐたびに下寄せから並べ直せば、どちらの向きでも整う。
            スマホを横向きにしたときにも、同じ道を通る
        */
        (function () {

            var narrowQuery = window.matchMedia(PT_NARROW_MEDIA);

            function onModeChange() {
                openTalks().forEach(function (talk, index) {

                    /*
                        【置き場所だけでなく、大きさも切り替えること】
                        狭い側ではインラインの width / height を外す。
                        外さないと、覆いかぶさるはずの窓が
                        広い画面のときの大きさのまま出る
                    */
                    applyStoredSize(talk);

                    placeWindow(talk, index);
                });

                /* 全面の印も付け直す（狭い側へ来たら全面、広い側へ出たら解除） */
                syncFullScreen();
            }

            /* 【罠】古いブラウザは addEventListener を持たない。
               付けられなくても、窓は開くし動く（並べ直しが起きないだけ） */
            if (narrowQuery.addEventListener) {
                narrowQuery.addEventListener('change', onModeChange);
            } else if (narrowQuery.addListener) {
                narrowQuery.addListener(onModeChange);
            }
        })();

        return {

            /*
                === この部屋から出ていく直前に、開いている窓の記録を捨てる ===

                指摘：「**1度目の再入室のとき、こっそりの窓の残像が一瞬見えて消える。
                2度目は全く出ない**」。直し方＝退室時にこっそりの窓を消しておけば、
                再入室時に一瞬表示されることも無くなる。

                【なぜ一瞬見えるのか】
                部屋を開くと、**JoinRoom の結果が返るより先に過去ログを描く**
                （そうしないと、描いている最中に届いた発言と二重になるため）。
                そのとき sessionStorage の記録を見て窓を開けてしまい、
                そのあと applyJoinResult が「入り直しだから」と閉じる。
                **開いて閉じるまでの一瞬が、残像として見えていた。**

                【出ていく時点で捨てれば、開く理由そのものが無くなる】
                2度目に出なかったのは、1度目の applyJoinResult が記録を空にしたあとだから。

                【F5では絶対に呼ばないこと】
                F5は部屋に留まるので、**開いている窓がそのまま開き直すのが正しい**
                （開発者の望む仕様で、今そうなっている）。
                ここを閉じる側に寄せると、その仕様が壊れる

                【applyJoinResult の後始末は、残してある】
                意味は重なるが、**受け持つ場面が違う**——こちらは「自分で出ていった」
                ときだけ通る。ブラウザを閉じた・電波が切れた・アプリが再起動した、
                といった経路では通らないので、あちらが最後の砦として要る
            */
            forgetOpenedWindows: function () {
                forgetAllOpened();
            },

            /*
                === この画面を離れる直前に、窓の位置と大きさを控える ===

                **大きさは、ここでしか拾えない**（変えているのはブラウザ自身で、
                合図が1つも来ない。詳しくは saveLayout の項）。
                呼ぶのは beforeunload と pagehide の2か所で、
                **自分から出ていくときは呼ばない**（記録は既に捨ててあるため）
            */
            saveLayout: function () {
                saveLayout();
            },

            /*
                入室の結果を受け取って、開いていた窓を引き継ぐか捨てるかを決める

                【無視リストと、まったく同じ軸である】
                F5や再接続では引き継ぐ。新しく入り直したときは捨てる
                （＝どの窓も開かない。ログは残っているので、開けば履歴は出る）。
                判定はサーバーが返した isContinuation を見るだけで、
                クライアント側だけでは F5 と新規入室の区別が付かない
            */
            applyJoinResult: function (result) {

                if (result && result.isContinuation) {
                    return;
                }

                /*
                    === 【罠】記録を空にするだけでは足りない。窓も閉じること ===

                    **「入り直したら開いていない状態にする」は、これが抜けていて
                    効いていなかった**（本番の実機で発覚）。

                    原因は順番である。

                      1. 部屋を開くと、まず過去ログを描く。このとき sessionStorage の
                         記録を見て、窓を**開けてしまう**（renderPastLog）
                      2. そのあと JoinRoom の結果が返り、ここが呼ばれる
                      3. ところが opened を空にするだけだったので、
                         **既に開いた窓はそのまま残っていた**

                    **無視機能で踏んだのと、まったく同じ形の穴である。**
                    あちらは「過去ログを描くほうが先に走る」に気づいて描き直す形にしたのに、
                    こちらは半分しか当てていなかった。

                    【一般化】「入室の結果を受け取ってから決める」と書いた処理は、
                    **それより前に描画が走っていないかを必ず確かめること。**
                    この画面は「接続の前にログを描く」と決めてあり、その決まりが先に効く
                */
                forgetAllOpened();

                /* 【入り直しでは、透け具合も既定へ戻すこと】（resetSeeThrough の項） */
                resetSeeThrough();

                /*
                    【未読の印は残す】（のあと。決定：入り直しても赤い札は出す）
                    過去ログの描画のときに、別の箱（UNREAD_KEY）から戻してある。窓は閉じるので、
                    未読は「窓を持たない未読」になり、バーの上の「こっそり未読」が知らせる。

                    【閉じるときに、手前を選び直さないこと】（deferActive）
                    1枚閉じるたびに選び直すと、そのとき手前に来た窓の未読が「見たこと」になって消える
                */
                Object.keys(talks).forEach(function (anonId) {
                    closeWindow(anonId, false, true);
                });

                /* 狭い画面のタブも、選択ごと片付ける */
                activeAnonId = null;
                renderTabs();
                renderParticipantList();
                updateOutsideUnreadMark();
            },

            /* 送られてきたら自動で開くか（設定パネルと窓の、両方から呼ばれる） */
            setAutoOpen: function (value) {

                autoOpen = !!value;

                /* 利用者の設定なので Cookie。通知音・表示行数と同じ軸 */
                setCookie(AUTO_OPEN_COOKIE, autoOpen ? '1' : '0');

                /* 開いている窓のボタンと、設定パネルを、まとめて揃える */
                $('.private-talk-autoopen')
                    .text(autoOpenLabel())
                    .attr('aria-pressed', autoOpen ? 'true' : 'false');

                $('#privateAutoOpenToggle').prop('checked', autoOpen);
            },

            isAutoOpen: function () {
                return autoOpen;
            },

            /* その相手に、まだ見ていない発言があるか（参加者一覧の印に使う） */
            /*
                その窓の足もとに、断られた理由を出す（空文字なら消す）

                【外へ出しているのは、画像の送信のためである】
                こっそりの画像は、ハブではなく Chat/UploadImage.aspx を通るので、
                断られたことを窓へ伝える口がここにしか無い。
                文章を送るときと**同じ場所・同じ見た目**にしてある——
                断られた理由の置き場所を、2つに増やさない

                【窓が開いていなければ、何も起きない】
                中の showReject が、そこを見てから戻る
            */
            /*
                その窓の入力欄（開いていない・相手が退室した・無い なら null）

                【外へ出しているのは、入れるものを選ぶパネルのためである】
                パネルは1つしか無く、本体の入力欄へも、こっそりの窓へも入れる。
                どこへ入れるかは InsertPanel が決め、入れる先の要素だけをここから借りる
            */
            inputOf: function (anonId) {

                var talk = talks[anonId];

                if (!talk || !talk.$el || !talk.present) {
                    return null;
                }

                return talk.$input[0];
            },

            /* その窓の相手の名前（パネルに「◯◯さんへ」と出すため） */
            nicknameOf: function (anonId) {
                var talk = talks[anonId];
                return talk ? talk.nickname : '';
            },

            /*
                その窓の要素（開いていなければ null）。
                入れるものを選ぶパネルを、この窓の隣に出すために使う
            */
            windowOf: function (anonId) {
                var talk = talks[anonId];
                return (talk && talk.$el) ? talk.$el[0] : null;
            },

            /*
                狭い画面（窓が覆いかぶさる形）か。
                【境目は PT_NARROW_MEDIA の1つだけ】パネルの出し方も、ここに合わせる
            */
            isNarrow: function () {
                return isNarrow();
            },

            /*
                全面か（狭い画面で、窓を1枚以上開いているか）。
                部屋の「新着あり」を出すか、パネルが窓のログを押し上げるか、の判断に使う
            */
            isFullScreen: function () {
                return isFullScreen();
            },

            /*
                その窓のログの要素（開いていなければ null）。
                入れるもののパネルが、全面の窓のログを押し上げるために使う（InsertPanel.updateLogPush）
            */
            logOf: function (anonId) {
                var talk = talks[anonId];
                return (talk && talk.$el) ? talk.$log[0] : null;
            },

            /*
                === バーの上の「こっそり未読」を押したとき===

                要望：赤い未読の印を押したら、その相手の窓が直接開く
                「複数の相手が未読なら、最後に来た相手を優先して開く（そこは適当でよい）」。

                【開くのは1人だけ】最後に発言が届いた相手（pendingUnread の先頭）。
                ほかの相手の未読は残る——狭い画面では開いた瞬間に全面になり、
                タブの帯に「未読」付きで並ぶ（renderTabs）。広い画面では印が残るので、もう一度押せば次の人。

                【焦点は、開いた窓のログへ】（入力欄ではない）
                ・印は押すと消えるので、どこかへ移さないと、キーボードや読み上げで使っている人が居場所を見失う
                ・入力欄へ移すと、スマホではソフトキーボードが開く。**未読を押した人がまずしたいのは読むこと**
                  （参加者一覧の「こっそり話す」は話すための入口なので、あちらは入力欄へ移す）
                ・ログの枠は tabindex を持っていない瞬間がある（LogKeyNav が付けるのは次のコマ）ので、
                  無ければ -1 を付けてから移す（-1 なら Tab の順番は増えない）
            */
            openLatestUnread: function () {

                var list = pendingUnread();

                if (list.length === 0) {
                    return;
                }

                var talk = list[0];

                openWindow(talk);

                var log = talk.$log[0];

                if (!log.hasAttribute('tabindex')) {
                    log.tabIndex = -1;
                }

                log.focus({ preventScroll: true });
            },

            /* その窓の、入れるものを選ぶボタン（パネルを閉じたあと、焦点を戻す先） */
            insertButtonOf: function (anonId) {
                var talk = talks[anonId];
                return (talk && talk.$el) ? talk.$el.find('.private-talk-insert-btn')[0] : null;
            },

            showReject: function (anonId, text) {

                var talk = talks[anonId];

                if (talk) {
                    showReject(talk, text || '');
                }
            },

            hasUnread: function (anonId) {
                return !!anonId && !!talks[anonId] && talks[anonId].unread;
            },

            /*
                窓を開く（参加者一覧の「こっそり話す」から呼ばれる）

                【履歴は openWindow が入れる】
                やりとりの中身は talks[].rows にあり、窓を閉じても消えていない
            */
            open: function (anonId, nickname) {

                if (!anonId || IgnoreList.isSelf(anonId) || IgnoreList.isIgnored(anonId)) {
                    return;
                }

                var talk = ensureTalk(anonId, nickname);

                if (!talk) {
                    return;
                }

                openWindow(talk);
                talk.$input.trigger('focus');
            },

            /*
                こっそりが1件届いた（リアルタイム）

                【無視している相手なら、窓も出さない】
                「行と音は、同じ判断に乗せる」に、窓も乗せる。
                無視しているのに窓が立ち上がったら、無視の意味が無い。

                戻り値 … 行を出したかどうか（呼び出し側が、音を鳴らすかの判断に使う）
            */
            receive: function (localId, nickname, message, iconType, iconColor,
                               anonId, recipientAnonId, recipientNickname, trip) {

                var isSelf = (localId === myLocalId);

                /*
                    相手が誰かは、自分が送ったかどうかで変わる。
                    windowKeyOf と同じ式を、ここでも使っている
                */
                var otherAnonId = isSelf ? recipientAnonId : anonId;
                var otherNickname = isSelf ? recipientNickname : nickname;

                if (!otherAnonId) {
                    return false;
                }

                /* 自分が送ったぶんは、無視の判定に掛けない（自分の発言のため） */
                if (!isSelf && IgnoreList.isIgnored(anonId)) {
                    return false;
                }

                var talk = ensureTalk(otherAnonId, otherNickname);

                if (!talk) {
                    return false;
                }

                pushRow(talk, {
                    kind: 'message',
                    nickname: nickname,
                    message: message,
                    isSelf: isSelf,
                    iconType: iconType,
                    iconColor: iconColor,
                    time: formatTime(new Date()),
                    date: todayDateText()
                });

                /* 相手の発言なら、その時点のトリップで窓の見出しを揃える（自分の発言では触らない） */
                if (!isSelf) {
                    setTalkTrip(talk, trip);
                }

                /*
                    === 窓を開くか、手前に出すか、未読にするか ===（組み直した）

                    指摘：スマホでこっそりの窓に入力している最中に、別の相手からこっそりが届いて、そちらが開くのはわずらわしい。
                    入力中に限らず、窓が開いている間はずっと（読んでいる最中も同じ）。

                    **こっそりの窓が1枚でも開いている間は、別の相手の窓を自動で開かない・手前に出さない。**
                    「自動で開く」の意味は「窓が1枚も開いていないときに、最初の1通で開く」になり、
                    そこから先はタブ（狭い画面）と、裏に開く窓（広い画面）が引き受ける。

                      自分が送った                      … 窓が閉じていれば開く（手前へ）。開いていれば何もしない
                      相手から・その窓がもう開いている  … 手前に出さない（狭い画面で裏のタブなら［未読］）
                      相手から・窓が1枚も開いていない    … 「自動で開く」がオンなら開く（手前へ）。オフなら未読
                      相手から・別の相手の窓が開いている … 狭い画面＝タブの帯に［未読］（窓は作らない）
                                                           広い画面＝「自動で開く」がオンなら裏に開く。オフなら未読

                    【「開いている」の判定は、狭い画面も広い画面も同じ】＝ 窓が1枚以上あるか。
                    違うのは「開かない」の形だけ——狭い画面は1枚ずつしか見えないので、窓を作ってもタブが増えるだけで
                    ［未読］と同じことになる。広い画面は裏に置いても見出しの帯がずれて見えるので、窓として開いてよい
                    （決定：PC では窓を開いてよいが、手前に出さない）。

                    【判定は、この発言で窓を作る前に数える】
                */
                var anyWindowOpen = openTalks().length > 0;

                if (isSelf) {

                    /*
                        【自分が送ったぶんは、窓が閉じていれば必ず開く】
                        自分で打った窓が閉じたままになるほうがおかしい。
                        開いているときは手前に出し直さない——画像を送っている間に別の窓へ移った人を、
                        送り終わった瞬間に引き戻さないため
                    */
                    if (!talk.$el) {
                        openWindow(talk);
                    }

                } else if (talk.$el) {

                    /* その相手の窓はもう在る。手前に出さない（読んでいる窓を裏に回さない） */

                } else if (autoOpen && !anyWindowOpen) {

                    openWindow(talk);

                } else if (autoOpen && !isNarrow()) {

                    openWindow(talk, true);

                } else {

                    /*
                        開かないと決めたときは、代わりに未読の印を出す
                        （狭い画面で窓が開いていればタブの帯に［未読］、そうでなければバーの上の「こっそり未読」）。

                        【これが無いと、届いたことに気づけない】
                        通知音は既定がオフなので、音でも気づけない
                    */
                    markUnread(talk);
                }

                /*
                    狭いときは、見えていない窓（裏のタブ）に届いたぶんも未読にする。
                    【自分が送ったぶんは除く】自分の発言で自分の窓が未読になるのはおかしい
                */
                if (!isSelf && talk.$el && talk.anonId !== activeAnonId && isNarrow()) {
                    markUnread(talk);
                }

                return true;
            },

            /*
                こっそりの過去ログから、やりとりの中身を作り直す

                【窓ごとに振り分けるのは、ここ1か所】
                サーバーは「自分が当事者の行」だけを、窓ごとにN行ずつ返してくる。
                当事者でない行は1件も来ない（絞り込みはサーバー側）
            */
            renderPastLog: function (entries) {

                /*
                    【作り直す前に、覚えている未読と、手前の窓を控えておく】
                    下で窓を開き直すと setActive が未読を消し、そのたびに覚えている未読も書き換わる。
                    手前の窓は、通信を繋ぎ直したときの取り直しで「最後に開いた窓」へ入れ替わっていた——
                    読んでいる窓が勝手に変わるのは、止めたことと同じ種類の不便である（F5では元から null）
                */
                var unreadToRestore = storedUnread.slice();
                var activeToRestore = activeAnonId;

                /* 今出ている窓は、いったん全部しまう（記録には残さない。手前も選び直さない＝deferActive） */
                Object.keys(talks).forEach(function (anonId) {
                    closeWindow(anonId, false, true);
                });

                talks = {};
                activeAnonId = null;

                (entries || []).forEach(function (m) {

                    var otherAnonId = windowKeyOf(m);

                    if (!otherAnonId) {
                        return;
                    }

                    /*
                        無視している相手のこっそりは、過去ログでも出さない。
                        自分が送ったぶんも同じ窓に入るので、
                        判定は必ず「相手の匿名ID」で行う
                    */
                    if (IgnoreList.isIgnored(otherAnonId)) {
                        return;
                    }

                    /*
                        窓の名前は、相手の名前にしたい。
                        自分が送った行にあるのは自分の名前なので、そこからは取れない。
                        相手からもらった行の nickname だけを使い、
                        1件も無ければ参加者一覧から拾う（下の refreshNicknames）
                    */
                    var talk = ensureTalk(otherAnonId, m.isSelf ? null : m.nickname);

                    if (!talk) {
                        return;
                    }

                    pushRow(talk, {
                        kind: 'message',
                        nickname: m.nickname,
                        message: m.message,
                        isSelf: m.isSelf,
                        iconType: m.iconType,
                        iconColor: m.iconColor,
                        time: m.time,
                        date: m.date
                    });

                    /* 相手の行なら、焼き込まれたトリップで窓の見出しを揃える（いちばん新しい行が勝つ） */
                    if (!m.isSelf) {
                        setTalkTrip(talk, idMarkOf(m.trip, m.accountName));
                    }
                });

                /*
                    中身が揃ってから、開いていたものだけを開き直す。

                    【opened に入っているものだけ】
                    履歴があるから開く、にしてはいけない。それだと
                    部屋を出て入り直しても、窓が開いたままになる
                */
                opened.slice().forEach(function (anonId) {
                    if (talks[anonId]) {
                        openWindow(talks[anonId]);
                    }
                });

                if (activeToRestore && talks[activeToRestore] && talks[activeToRestore].$el) {
                    setActive(activeToRestore);
                }

                /*
                    === 覚えていた未読を戻す===

                    窓を開き直したあとで戻す（先に戻すと、開き直すたびに setActive が消してしまう）。
                    そのうえで、**今見えている窓のぶんは消す**——狭い画面では手前の1枚、
                    広い画面では開いている窓すべて（広い画面の窓は並んで見えるので、未読を持たせない決まり）。
                    過去ログに行が無い相手（無視した・行数からあふれた）は、戻す先が無いので捨てる
                */
                unreadToRestore.forEach(function (anonId) {
                    var talk = talks[anonId];
                    if (talk) {
                        talk.unread = true;
                        talk.unreadSeq = ++unreadSeq;
                    }
                });

                openTalks().forEach(function (talk) {
                    if (!isNarrow() || talk.anonId === activeAnonId) {
                        talk.unread = false;
                    }
                });

                rememberUnread();
                renderTabs();
                renderParticipantList();

                this.refreshNicknames();
                this.refreshPresence();
                updateOutsideUnreadMark();
            },

            /*
                窓の名前を、参加者一覧の今の名前で揃える

                【なぜ要るのか】
                自分が送った行しか無い窓は、相手の名前が分からない。
                相手が在室していれば、一覧に本当の名前がある
            */
            refreshNicknames: function () {
                participants.forEach(function (p) {
                    var talk = p.anonId ? talks[p.anonId] : null;
                    if (talk) {
                        talk.nickname = p.nickname;

                        /* 相手が今この部屋に居れば、今のトリップで見出しを揃える */
                        talk.trip = normalizeIdMark(p.trip);
                        applyTitle(talk);
                    }
                });
                renderTabs();
            },

            /*
                窓ごとに「相手が今この部屋に居るか」を取り直して、送信の可否へ反映する

                【F5のあとに要る】
                窓は過去ログから作られるので、相手が既に退室していることがある。
                その場合、開いた瞬間から送信できない状態が正しい
            */
            refreshPresence: function () {
                Object.keys(talks).forEach(function (anonId) {

                    var talk = talks[anonId];

                    var isHere = participants.some(function (p) {
                        return p.anonId === anonId;
                    });

                    if (talk.present !== isHere) {
                        talk.present = isHere;
                        updateSendState(talk);
                    }
                });
            },

            /*
                相手が退室した／入ってきた

                【行は出さない。入力欄のグレーアウトと文言だけで伝える】
                以前はここでお知らせの行を足していたが、**相手側にだけ、
                F5のたびに増えていく**という壊れ方をした。

                原因は、置き換え(a)の配信である。同じ部屋で置き換えが起きると、
                サーバーは UserLeft と UserJoined を**文言を空にして**配信する
                （部屋のログには出さないが、参加者一覧は正しく保つ必要があるため）。
                こちらはその文言の判定の外で呼ばれるので、毎回2行ずつ積み上がっていた。
                しかも積み上がるのは相手側だけで、その人がF5するまで消えない。

                【教訓】同じ配信でも、「状態を置き換えるもの」（present や参加者一覧）は
                何度来ても平気だが、「足すもの」（行）は積み上がる。
                **繰り返し届く配信から、足すものを作らないこと**
            */
            notifyLeft: function (anonId) {

                var talk = anonId ? talks[anonId] : null;

                if (!talk || !talk.present) {
                    return;
                }

                talk.present = false;
                updateSendState(talk);
            },

            notifyJoined: function (anonId, nickname, trip) {

                var talk = anonId ? talks[anonId] : null;

                if (!talk) {
                    return;
                }

                if (nickname) {
                    talk.nickname = nickname;
                    applyTitle(talk);
                }

                /* 入り直した相手の、今のトリップ（ラウンジで外した・変えたあとなら、ここで変わる） */
                setTalkTrip(talk, trip);

                if (!talk.present) {
                    talk.present = true;
                    updateSendState(talk);
                }
            },

            /* その相手の窓が開いているか */
            has: function (anonId) {
                return !!anonId && !!talks[anonId] && !!talks[anonId].$el;
            },

            /*
                その相手のやりとりを、まるごと捨てる（無視にしたときに呼ぶ）

                【中身ごと消すこと】
                行も音も出さないと決めた相手のやりとりが残っていると、
                窓を開き直したときに過去のやりとりが見えてしまう。
                解除したときは、サーバーから取り直して復元される
            */
            close: function (anonId) {
                closeWindow(anonId, true);
                if (anonId) {
                    delete talks[anonId];
                    rememberUnread();   /* 覚えている未読からも外す（F5で戻さない） */
                    renderTabs();
                    updateOutsideUnreadMark();
                }
            }
        };
    })();




    /*
        【自動再生の制限をほどく】

        ページを読み込んだ直後は、まだ誰もこのページを操作していないため、
        ブラウザは音を出させない（AudioContextがsuspendedのまま作られる）。

        設定パネルでオンにしたときは、その操作自体がきっかけになるので問題ない。
        困るのは「オンのままF5した」場合で、そのままだと何か操作するまで無音になる。
        そこで、最初の操作（クリック・キー・タッチ）を1回だけ捕まえて、そこでほどく。

        capture:true にしてあるのは、他のハンドラが止めても必ず届くようにするため。
        once:true なので、ほどけたあとは残らない

        【click も拾う】
        スマホの指では、pointerdown・touchstart の時点ではまだ「操作があった」ことにならない（指を離したときに付く）。
、操作があるまで AudioContext を作らない（sounds.js の mayStartAudio）ので、
        指を離したあとの click でもう一度試す。拾い損ねても、次に鳴らすときに作られるので鳴らなくはならない
    */
    ['pointerdown', 'keydown', 'touchstart', 'click'].forEach(function (eventName) {
        document.addEventListener(eventName, function () {
            Sounds.unlock();
        }, { once: true, capture: true });
    });


    /*
        === 通知音のオン/オフ ===

        【保存先はCookie】表示行数（ChatLogCount）と同じ setCookie に揃えてある。
        localStorageのほうが通信は減るが、同じ「利用者の設定」で保存先が2種類あると、
        次に設定を足す人が毎回どちらかを迷うことになる。そちらのほうが高くつく。

        === 【重要】上の話は「設定」の中での話である（追記） ===

        この画面には保存先が3つある。**迷わないための線は、こう引いてある。**

          Cookie         … 利用者の「設定」
                           名前・アイコン・通知音・音量・表示行数。
                           サーバーも知る必要があるものが混ざっており、
                           **毎回のリクエストに乗って送られる**

          sessionStorage … その場限りの「状態」
                           無視リスト・退室理由。タブを閉じたら消えてよいもの

          localStorage   … その端末に溜まる「使用履歴」
                           よく使う絵文字（emoji-mart が持つ）。
                           **サーバーは知らなくてよく、知る必要も無い**

        **「よく使う絵文字」を Cookie に入れてはいけない。**
        毎回のリクエストに乗ってサーバーへ送られることになり、
        「サーバー側の負荷を減らす」という常設の観点に真っ向から反する。

        つまり上の「保存先が2種類あると迷う」は、**設定を足すときの話**であって、
        「設定」と「履歴」を同じ場所に入れろ、という意味ではない。
        **設定なのか、状態なのか、履歴なのかで決める**
    */
    var SOUND_COOKIE_NAME = 'ChatSoundEnabled';

    /*
        まだ一度も選んでいない人には、サーバーが決めた既定値を使う。

        【罠】「Cookieが無い」と「Cookieに '0' が入っている」は、別物である。
        両方をまとめてオフ扱いにすると、「自分でオフにした人」と
        「まだ選んでいない人」が見分けられなくなる。将来、既定をオンに変えた日に、
        自分でオフにした人の設定まで黙って覆ることになる
    */
    var soundCookie = getCookie(SOUND_COOKIE_NAME);
    var soundEnabled = (soundCookie === null)
                     ? (vbChatSettings.notificationSoundDefault === true)
                     : (soundCookie === '1');

    /*
        === 通知音のオン/オフは、この1か所で決める ===

        切り替える場所が2つある。
          ・部屋の上のバーの、スピーカーのボタン（#soundBtn）
          ・設定パネルのスイッチ（#soundToggle）

        **どちらから触っても、ここへ入る。** ここが Cookie と Sounds と、
        2つの見た目をまとめて揃える。
        こっそりの「自動で開く」が、窓と設定パネルの両方から
        PrivateTalk.setAutoOpen ひとつへ入るのと、まったく同じ形である。
        **状態を2か所で持たないこと**——持つと、片方だけ古いままになる

        save … Cookie に残すか（ページを開いたときの初期化では残さない）
    */
    function setSoundEnabled(isOn, save) {

        soundEnabled = !!isOn;

        if (save) {
            /* 次に入室したときも同じ状態になるよう、Cookieに残す */
            setCookie(SOUND_COOKIE_NAME, soundEnabled ? '1' : '0');
        }

        Sounds.setEnabled(soundEnabled);

        $('#soundToggle').prop('checked', soundEnabled);

        /*
            バーのボタン。どちらの絵を出すかは aria-pressed が決める（CSS側）。
            title には「今の状態」と「押すとどうなるか」の両方を書く——
            スピーカーの絵だけでは、押すと鳴るようになるのか止まるのか、
            初めての人には分からない
        */
        $('#soundBtn')
            .attr('aria-pressed', soundEnabled ? 'true' : 'false')
            .attr('title', soundEnabled
                ? '通知音：オン（押すとオフ）'
                : '通知音：オフ（押すとオン）');
    }

    setSoundEnabled(soundEnabled, false);

    $('#soundToggle').on('change', function () {

        var isOn = $(this).is(':checked');

        setSoundEnabled(isOn, true);
        debugLog('通知音を' + (isOn ? 'オンに' : 'オフに') + 'しました（設定パネル）');
    });

    /*
        === 部屋の上のバーの、スピーカーのボタン ===

        【オンにした瞬間に、小さく1回鳴らす】（開発者の提案）
        ・オンにした人が「鳴った」で、効いたことを確かめられる
        ・**同じ一押しで、ブラウザの自動再生の制限も解ける**。
          押したこと自体が「利用者の操作」なので、その中で鳴らせば許される。
          既定はオフなので、初めての人は必ずこのボタンか設定パネルを通る

        鳴らすのは発言の音（ポン）。いちばん短く、入退室の音と取り違えない。

        【オフにしたときは鳴らさない】止めたい場面（人が来た・電車に乗った）で、
        止めたとたんに音が出たら本末転倒である

        【設定パネルのスイッチでは鳴らしていない】
        あちらは前からある動きなので、変えていない。
        入れるなら、この関数の中の1行を setSoundEnabled 側へ移すだけでよい
    */
    $('#soundBtn').on('click', function () {

        var isOn = !soundEnabled;

        setSoundEnabled(isOn, true);

        if (isOn) {
            Sounds.message();
        }

        debugLog('通知音を' + (isOn ? 'オンに' : 'オフに') + 'しました（バーのボタン）');
    });


    /*
        === こっそりが送られてきたら、自動で窓を開くか ===

        既定はオン。保存先は Cookie（利用者の設定なので、通知音・表示行数と同じ軸）。

        【設定パネルと窓の、両方から切り替えられる】
        どちらから触っても PrivateTalk.setAutoOpen ひとつへ入り、
        そこが Cookie と、開いている全部の窓のボタンと、このスイッチを揃える。
        **状態を2か所で持たないこと**——持つと、片方だけ古いままになる
    */
    $('#privateAutoOpenToggle').prop('checked', PrivateTalk.isAutoOpen());

    $('#privateAutoOpenToggle').on('change', function () {
        PrivateTalk.setAutoOpen($(this).is(':checked'));
        debugLog('こっそりの自動で開くを' +
                 (PrivateTalk.isAutoOpen() ? 'オンに' : 'オフに') + 'しました');
    });


    /*
        === 【消した】狭い画面で、こっそりに打っている間だけ、本体の入力欄を隠す ===

        入れた is-private-typing（こっそりの入力欄の focusin／focusout で付け外し）は、
        **「窓を開いている間ずっと」の is-private-full へ置き換えた**
        （PrivateTalk.syncFullScreen）。打っている間は必ず窓が開いているので、新しい印に含まれる。
        入力欄を隠す理由・隠し方の罠（行は残して中身だけ）は、CSS 側のコメントに残してある
    */

    /*
        === バーの上の「こっそり未読」を押したら、最後に来た相手の窓を開く===

        それまでは印を押しても何も起きず、人数 → 一覧で相手を選ぶ、の2段だった。
        どの相手を開くか・焦点をどこへ移すかは、PrivateTalk.openLatestUnread が決める
    */
    $('#privateUnreadMark').on('click', function () {
        PrivateTalk.openLatestUnread();
    });


    /*
        === 通知音の大きさ（小・中・大）===

        保存するのは倍率ではなく、段階の名前（'low' / 'normal' / 'high'）。
        倍率はSoundsの中にしかないので、あとで「大」の倍率を変えれば、
        既に「大」を選んでいる人にもそのまま効く
    */
    var SOUND_VOLUME_COOKIE_NAME = 'ChatSoundVolume';

    /*
        Cookieが無い（まだ選んでいない）／知らない値が入っている場合は、
        サーバーが決めた既定値を使う。
        サーバー側も白リストで丸めているので、ここへ来るのは必ず3つのどれかである
    */
    var savedVolumeLevel = getCookie(SOUND_VOLUME_COOKIE_NAME);

    if (!Sounds.isKnownLevel(savedVolumeLevel)) {
        savedVolumeLevel = vbChatSettings.notificationSoundVolumeDefault;
    }

    /*
        【罠】セレクトには、setVolumeLevelが返した値を入れること。
        渡した値をそのまま入れると、一覧に無い名前だったときに
        「何も選択されていない」状態になり、倍率（中）と画面が食い違う
    */
    $('#soundVolumeSelect').val(Sounds.setVolumeLevel(savedVolumeLevel));

    $('#soundVolumeSelect').on('change', function () {

        var level = $(this).val();

        setCookie(SOUND_VOLUME_COOKIE_NAME, level);
        Sounds.setVolumeLevel(level);

        debugLog('通知音の音量を ' + level + ' にしました');
    });


    /*
        退室の理由を、ラウンジへ持ち越すためのキー

        【なぜ、この画面で表示しないのか】
        以前はalertで知らせていたが、Chromeは背面のタブから出されるダイアログを
        抑制するため、表示が握り潰されていた（2窓の置き換えテストで判明）。
        無言タイムアウトでも同じことが起き、しかも放置している人ほどタブは背面に
        あるため、「一番理由を知りたい人に、一番届かない」状態になっていた。
        そこで理由を持ったままラウンジへ移動し、ラウンジ側で帯として表示する。
        sessionStorageはタブ単位で保持されるため、同じタブでの移動なら値が残る
    */
    var LEAVE_REASON_STORAGE_KEY = 'vbchat_leave_reason';

    /* 退室の理由を、ラウンジへ持ち越すために保存する */
    function saveLeaveReason(reason) {
        try {
            sessionStorage.setItem(LEAVE_REASON_STORAGE_KEY, reason);
        } catch (e) {
            /* 保存できなくても、理由が表示されないだけで、退室そのものは正常に行われる */
        }
    }


    /* === 参加者一覧・人数表示 === */

    /* 人数表示（現在人数/定員）を、participants配列の件数から更新する */
    function updateParticipantCount() {

        $('#participantCountText').text('(' + participants.length + '/' + maxUsers + ')');

        /*
            読み上げソフトへは、別の言い方で渡す（読み上げの点検の回）。

            見える文字（「(3/20)」と「▼」）は aria-hidden にしてあるので、
            ボタンの名前はこの aria-label だけが決める。
            **ラウンジの .room-count と同じ言い方に揃えてある**（「3人、定員20人」）。
            開いているかどうかは aria-expanded（setOpenPanel が付け替える）が受け持つので、
            ここには入れない（同じことを2か所で持たない）
        */
        $('#participantCountBtn').attr(
            'aria-label',
            '参加者 ' + participants.length + '人、定員' + maxUsers + '人');
    }

    /* participants配列の中身をもとに、参加者一覧パネルの表示を丸ごと組み立て直す */
    /*
        === 参加者一覧の描き直し ===

        【無視している相手も、一覧からは消さない】
        開発者の明示＝一覧には残す（＝居ることは分かる）。人数表示も減らさない。
        隠すのは「発言・入退室の行・音」だけである。
        そして**一覧が、無視を解除する唯一の入口**でもある。

        【罠】この関数は一覧を丸ごと作り直す。
        誰かが入退室するたびに選択状態が消えるので、選んでいた相手（selectedAnonId）を
        覚えておいて、描き直したあとに選び直している
    */
    function renderParticipantList() {
        var itemsHtml = participants.map(function (p) {
            var safeNickname = escapeHtml(p.nickname);

            /*
                属性の中へ入れる値は、必ずescapeAttrを通す
                （理由はappendMessageRowのコメントを参照）
            */
            var safeIconColor = escapeAttr(p.iconColor);
            var safeIconType = escapeAttr(p.iconType);
            var safeAnonId = escapeAttr(p.anonId || '');

            var isIgnored = IgnoreList.isIgnored(p.anonId);
            var isSelected = (p.anonId && p.anonId === selectedAnonId);

            var classes = 'participant-item';
            if (isIgnored) {
                classes += ' participant-item--ignored';
            }
            if (isSelected) {
                classes += ' participant-item--selected';
            }

            /*
                「非表示中」の印は、目で見て分かる形（アイコンを薄く＋文字を添える）と、
                読み上げソフト向けの両方で出す。
                読み上げへの対応は aria-label 程度にとどめる方針
                （開発者の判断：そこに時間をかけない）
            */
            var ignoredMark = isIgnored
                ? '<span class="participant-ignored-mark">非表示中</span>'
                : '';

            /*
                こっそりの未読の印。

                【なぜ要るのか】
                「送られてきたら自動で開く」をオフにすると、窓が開かなくなる。
                通知音は既定がオフなので、**音でも気づけない**。
                届いたことを知る手立てが、これしか無くなる。

                オンのときは窓がすぐ開くので、この印はほとんど出ない
            */
            var unreadMark = PrivateTalk.hasUnread(p.anonId)
                ? '<span class="participant-unread-mark">こっそり未読</span>'
                : '';

            /*
                【ボタンの aria-label が中身の読み上げを置き換える】
                この一覧の1人ぶんはボタンで、aria-label を付けてあるので、
                中の <span>（トリップの読み上げ用の「トリップ」も含めて）は読まれない。
                トリップはここにも足しておく
            */
            var isOwner = (ownerLocalId !== 0 && p.localId === ownerLocalId);

            var ariaLabel = escapeAttr(
                p.nickname +
                (isOwner ? '（管理人）' : '') +
                idMarkLabel(p.trip) +
                (isIgnored ? '（非表示中）' : '') +
                (PrivateTalk.hasUnread(p.anonId) ? '（こっそりの未読あり）' : ''));

            return '<li class="' + classes + '">' +
                '<button type="button" class="participant-select-btn"' +
                ' data-anon-id="' + safeAnonId + '"' +
                ' aria-pressed="' + (isSelected ? 'true' : 'false') + '"' +
                ' aria-label="' + ariaLabel + '">' +
                '<div class="participant-avatar">' +
                '<svg viewBox="0 0 512 512" style="color: ' + safeIconColor + ';">' +
                '<use href="#icon-' + safeIconType + '"></use>' +
                '</svg>' +
                '</div>' +
                '<span class="participant-nickname">' + safeNickname + '</span>' +

                /* 名前の下の行にトリップかアカウントID（どちらも無い人は空文字）。
                   【ここではリンクにしない】1人ぶんがボタンなので、中にリンクは置けない（'text'）。
                   プロフィールへは、選んでから上の「プロフィール」を押す（updateIgnoreButton） */
                buildTripHtml(p.trip, 'participant-trip', 'text') +

                /* 管理人の札。名前（とトリップ）の下。読み上げは上の aria-label が受け持つ */
                (isOwner ? '<span class="owner-badge participant-owner-badge" aria-hidden="true">管理人</span>' : '') +
                ignoredMark +
                unreadMark +
                '</button>' +
                '</li>';
        }).join('');

        /*
            【焦点を、同じ人のところへ戻す】（読み上げの点検の回）

            この関数は一覧を丸ごと作り直すので、押したボタンごと文書から外れる。
            そのままだと焦点が body へ落ち、**キーボードで相手を選んだ瞬間に居場所を失う**
            （次の Tab はページの先頭から。管理人の道具＝選んでから押す、が使えなくなる）。
            誰かが入退室しただけのときも同じことが起きる。

            誰を指していたかは data-anon-id で分かるので、作り直したあとに探して戻す。
            **押されていた相手が居なくなっていたら、何もしない**（退室させた直後など。
            そこで別の人へ勝手に移すと、押すつもりの無い相手が選ばれてしまう）
        */
        var focusedAnonId = null;
        var active = document.activeElement;

        if (active && $(active).hasClass('participant-select-btn') &&
            $.contains(document.getElementById('participantList'), active)) {
            focusedAnonId = active.getAttribute('data-anon-id') || null;
        }

        $('#participantList').html(itemsHtml);

        if (focusedAnonId) {
            var back = document.querySelector(
                '#participantList .participant-select-btn[data-anon-id="' +
                (window.CSS && CSS.escape ? CSS.escape(focusedAnonId) : focusedAnonId) + '"]');
            if (back) {
                back.focus();
            }
        }

        updateIgnoreButton();
    }

    /*
        参加者を1人追加する（初期表示・UserJoined受信の両方から呼ばれる）

        trip … その人のトリップ（無ければ null／undefined）。入室の時点でサーバーが計算した値
        joinSeq … 入室した順の番号（サーバーの Participant.JoinSeq）。
                  一覧はこの番号の順に並べる。F5 や、同じ部屋を別のタブで開き直しても変わらない。
                  番号が来ない（発行の直後の古いサーバー）ときは、今までどおり末尾へ足す
    */
    function addParticipant(localId, nickname, iconType, iconColor, anonId, trip, joinSeq) {

        /*
            保険として、同じ人が二重に並ばないようにしておく。
            現在の作りでは二重登録は起きない想定だが、将来メッセージの順番が
            変わった場合などに、一覧へ同じ人が2人並ぶのを防ぐため
        */
        var isAlreadyListed = participants.some(function (p) {
            return p.localId === localId;
        });
        if (isAlreadyListed) {
            return;
        }

        var entry = {
            localId: localId,
            nickname: nickname,
            iconType: iconType,
            iconColor: iconColor,
            anonId: anonId,
            trip: trip,
            joinSeq: (typeof joinSeq === 'number') ? joinSeq : null
        };

        /*
            【入室した順の位置へ入れる】
            要望：人数のパネルをその順番で並べる。F5 などで順番が変わるのはよくない。
            それまでは、入り直した自分がいつも末尾に足されていた（JoinRoom の戻り値の一覧のあとに、自分を足すため）。
            番号の無い人（古いサーバーから来た）は、末尾に置く
        */
        var insertAt = participants.length;

        if (entry.joinSeq !== null) {
            for (var i = 0; i < participants.length; i++) {
                var other = participants[i].joinSeq;
                if (other !== null && other > entry.joinSeq) {
                    insertAt = i;
                    break;
                }
            }
        }

        participants.splice(insertAt, 0, entry);
        renderParticipantList();
        updateParticipantCount();
    }


    /*
        === 参加者一覧の「非表示にする／やめる」 ===

        【ボタンは1組だけ、パネルの上部に置く】
        各参加者ごとにボタンを並べるのではない。定員20人なら40個になってしまう。
        「一覧で相手を選ぶ → ボタンを押す」という2段の形にしてある。

        【ボタンは1つのまま、文言が変わる】
        既に非表示の人を選んだら「非表示をやめる」になる。
        **これが無いと、間違えて押した人は部屋を出るまで戻せない**
        （当初の設計に抜けていて）。

        【「こっそり話す」（Dの秘密トーク）は、まだ出さない】
        いずれ同じ場所に並ぶが、押しても何も起きないボタンを本番に置かないため、
        Cの回では置かない。場所だけ空けてある
    */

    /* 今選ばれている相手の匿名ID（誰も選んでいなければ null） */
    var selectedAnonId = null;

    function updateIgnoreButton() {

        var $btn = $('#ignoreToggleBtn');

        /* 誰も選んでいない、または自分を選んでいる間は、押せない */
        var canPress = !!selectedAnonId && !IgnoreList.isSelf(selectedAnonId);

        $btn.prop('disabled', !canPress);

        /*
            「こっそり話す」も、押せる条件はまったく同じ
            （誰かを選んでいて、それが自分でないこと）。

            【無視している相手には、こっそりも送れない】
            行も音も窓も出さないと決めた相手へ送れてしまうと、
            返事が来ても自分には見えない、という噛み合わない状態になる。
            解除すれば、また押せるようになる
        */
        $('#privateTalkBtn').prop(
            'disabled', !canPress || IgnoreList.isIgnored(selectedAnonId));

        /*
            「プロフィール」。選んだ相手がアカウントID を出しているときだけ出すリンク。
            押すと、公開のプロフィールが新しいタブで開く（部屋から離れない）。

            【なぜ、ここに置くのか】一覧の1人ぶんはボタンで、中にリンクを置けない。
            「選んでから押す」は、無視・こっそり・管理人の道具と同じ形で、キーボードでも届く
            （発言の行のアカウントID は Tab で止まらないので、キーボードの人の入口はここ）。
            【自分を選んでいても出す】自分のプロフィールを見るのは、誰の害にもならない。
            【無ければ、場所ごと消す】（hidden）。アカウントID を出していない人には、押せるものが何も無いため
        */
        var profileLink = document.getElementById('participantProfileLink');

        if (profileLink) {

            var selectedAccountName = null;

            if (selectedAnonId) {
                participants.forEach(function (p) {
                    if (p.anonId === selectedAnonId) {
                        selectedAccountName = accountNameOfMark(p.trip);
                    }
                });
            }

            if (selectedAccountName) {
                profileLink.setAttribute('href', accountProfileUrl(selectedAccountName));
                profileLink.setAttribute('aria-label',
                    '@' + selectedAccountName + ' のプロフィール（新しいタブで開きます）');
                profileLink.hidden = false;
            } else {
                profileLink.removeAttribute('href');
                profileLink.hidden = true;
            }
        }

        /*
            管理人の道具も、同じ時に押せる・押せないを決め直す。
            【無視している相手にも使える】無視は自分の画面の話で、退室させる・入室禁止は部屋の話（荒らしを無視したうえで出す、がふつうの使い方）。
            OwnerTools はこの関数より後ろで作られるので、最初の描画（作られる前）では呼ばない
        */
        if (typeof OwnerTools !== 'undefined') {
            OwnerTools.refresh();
        }

        if (!canPress) {
            $btn.text('非表示にする');
            return;
        }

        $btn.text(IgnoreList.isIgnored(selectedAnonId)
            ? '非表示をやめる'
            : '非表示にする');
    }

    /*
        一覧の中の誰かを選ぶ（もう一度押すと選択を外す）

        【自分も選べる。ただしボタンは押せない】
        選ぶこと自体を禁じると「押しても何も起きない」ように見えて、
        壊れているのか仕様なのか分からない。選べたうえでボタンが無効になるほうが、
        「自分は非表示にできない」と伝わる
    */
    $(document).on('click', '.participant-select-btn', function () {

        var anonId = $(this).attr('data-anon-id') || null;

        selectedAnonId = (anonId && anonId === selectedAnonId) ? null : anonId;

        renderParticipantList();
    });

    $('#ignoreToggleBtn').on('click', function () {

        if (!selectedAnonId || IgnoreList.isSelf(selectedAnonId)) {
            return;
        }

        if (IgnoreList.isIgnored(selectedAnonId)) {
            IgnoreList.remove(selectedAnonId);
        } else {
            IgnoreList.add(selectedAnonId);

            /*
                【窓も閉じること】
                行も音も出さないと決めた相手の窓だけが残っていると、
                そこに過去のやりとりが見えたままになる。
                「行と音は、同じ判断に乗せる」に、窓も乗せてある。

                解除したときは、下の refreshLogAfterIgnoreChange が
                こっそりの過去ログごと取り直すので、窓もそこで戻る
            */
            PrivateTalk.close(selectedAnonId);
        }

        /*
            【隠す・戻すは、ログを描き直して反映させる】
            既に画面に出ている行を1つずつ消し歩くのではなく、
            サーバーから取り直して描き直す。無視を解除したときに
            「消えていた過去ログが戻る」のも、同じ経路でそのまま実現できる。

            読んでいた位置は保つが、「新着あり」は出さない
            （REDRAW_KEEP_POSITION_QUIET。あの定数のコメントに理由がある）。
            利用者が起こした操作ではあるが、**読んでいる場所は変えたくない**
        */
        renderParticipantList();
        refreshLogAfterIgnoreChange();
    });

    /*
        === 「こっそり話す」を押した ===

        窓を開くだけ。開いたあとのことは、すべて PrivateTalk の中で決まる
        （Sounds・IgnoreList と同じで、呼ぶ側は一行しか書かない）
    */
    $('#privateTalkBtn').on('click', function () {

        if (!selectedAnonId || IgnoreList.isSelf(selectedAnonId) ||
            IgnoreList.isIgnored(selectedAnonId)) {
            return;
        }

        var target = participants.filter(function (p) {
            return p.anonId === selectedAnonId;
        })[0];

        PrivateTalk.open(selectedAnonId, target ? target.nickname : null);

        /*
            パネルは閉じる。開いたままだと、**開いた窓が下に隠れて見えない**
            （参加者一覧も窓も、どちらもチャットログの上へ覆いかぶさるため）
        */
        setOpenPanel(null);
    });


    /*
        === 管理人の道具===

        参加者一覧で相手を選んで押す、3つのボタン（Chat/ChatRoom.aspx の #ownerTools。無視・こっそりの行の下）。
          ・管理人をゆずる   → chatHub.server.handOverOwner(匿名ID)
          ・退室させる       → chatHub.server.kickParticipant(匿名ID)   … その時限り。すぐ入り直せる
          ・入室禁止にする   → chatHub.server.banParticipant(匿名ID)    … 退室させたうえで、この部屋にはもう入れない
        どれも押す前に確認を1回出す（vbChatConfirm。ゆずったら自分では取り戻せない・退室は取り消せない）。

        【管理人の画面にだけ出す】行ごと hidden（renderMyOwnerBadge と同じ条件・同じ時に refresh）。
        **出さないことは防御ではない**。管理人か・相手が同じ部屋に居るか・自分ではないかは、ハブが必ず確かめる。

        【押せる条件は、無視・こっそりと同じ】誰かを選んでいて、それが自分ではないこと（updateIgnoreButton から refresh）。
        ゆずるは、相手が今つながっていないと断られる（ハブが理由を返す。画面は相手の接続の状態を知らない）。

        【相手は匿名ID で指す】一覧の選択（selectedAnonId）と同じ値。ブラウザの識別子は画面に無い。
        【名前は確認の文言に入る】vbChatConfirm は textContent で入れるので、利用者が決めた名前でも安全
    */
    var OwnerTools = (function () {

        var row = document.getElementById('ownerTools');

        if (!row) {
            return { refresh: function () { } };
        }

        var handOverBtn = document.getElementById('ownerHandOverBtn');
        var kickBtn = document.getElementById('ownerKickBtn');
        var banBtn = document.getElementById('ownerBanBtn');
        var statusEl = document.getElementById('ownerToolsStatus');

        /* 返事を待っている間は、どれも押せない（2回押されて2回確認が出る・2回送る、を防ぐ） */
        var busy = false;

        function isOwnerNow() {
            return ownerLocalId !== 0 && myLocalId !== null && ownerLocalId === myLocalId;
        }

        function selectedTarget() {
            if (!selectedAnonId || IgnoreList.isSelf(selectedAnonId)) {
                return null;
            }
            return participants.filter(function (p) {
                return p.anonId === selectedAnonId;
            })[0] || null;
        }

        function setStatus(text) {
            statusEl.textContent = text || '';
        }

        function refresh() {

            var show = isOwnerNow();

            if (row.hidden === show) {
                row.hidden = !show;
                /* 管理人になった・外れたときは、前の理由の1行を消す */
                setStatus('');
            }

            var canPress = show && !busy && !!selectedTarget();

            handOverBtn.disabled = !canPress;
            kickBtn.disabled = !canPress;
            banBtn.disabled = !canPress;
        }

        /*
            確認を出してから、ハブを呼ぶ
            call … function (anonId) { return chatHub.server.xxx(anonId); }
        */
        function confirmAndRun(target, confirmOptions, call) {

            setStatus('');

            confirmOptions.onOk = function () {

                busy = true;
                refresh();

                call(target.anonId)
                    .done(function (result) {
                        if (result && result.ok) {
                            /*
                                退室させた・禁止にした相手は、配信（userLeft）で一覧から消え、選択も外れる。
                                ゆずったときは、配信（ownerChanged）でこのボタンの行が消える
                            */
                            return;
                        }
                        setStatus((result && result.message) || 'できませんでした。');
                    })
                    .fail(function () {
                        setStatus('できませんでした。接続が戻ってから、もう一度お試しください。');
                    })
                    .always(function () {
                        busy = false;
                        refresh();
                    });
            };

            vbChatConfirm(confirmOptions);
        }

        handOverBtn.addEventListener('click', function () {

            var target = selectedTarget();

            if (!target || !isOwnerNow()) {
                return;
            }

            confirmAndRun(target, {
                title: target.nickname + 'さんに、管理人をゆずりますか？',
                lines: [
                    'ゆずると、自分では取り戻せません（取り戻すには、' + target.nickname + 'さんにゆずってもらいます）。',
                    '部屋の設定・ノックの返事・退室させる・入室禁止は、' + target.nickname + 'さんが行うようになります。'
                ],
                okText: 'ゆずる',
                cancelText: 'やめる'
            }, function (anonId) {
                return chatHub.server.handOverOwner(anonId);
            });
        });

        kickBtn.addEventListener('click', function () {

            var target = selectedTarget();

            if (!target || !isOwnerNow()) {
                return;
            }

            confirmAndRun(target, {
                title: target.nickname + 'さんを、退室させますか？',
                lines: [
                    target.nickname + 'さんはラウンジへ戻されます。部屋の全員に「管理人により退室させられました」と出ます。',
                    'この部屋には、すぐに入り直せます（入れないようにするときは「入室禁止にする」）。'
                ],
                okText: '退室させる',
                cancelText: 'やめる'
            }, function (anonId) {
                return chatHub.server.kickParticipant(anonId);
            });
        });

        banBtn.addEventListener('click', function () {

            var target = selectedTarget();

            if (!target || !isOwnerNow()) {
                return;
            }

            confirmAndRun(target, {
                title: target.nickname + 'さんを、入室禁止にしますか？',
                lines: [
                    target.nickname + 'さんはラウンジへ戻され、この部屋には入れなくなります（パスワードを知っていても、ノックしても入れません）。',
                    '間違えたときは、設定の「部屋の設定」にある「入室禁止」の一覧から解除できます。'
                ],
                okText: '入室禁止にする',
                cancelText: 'やめる'
            }, function (anonId) {
                return chatHub.server.banParticipant(anonId);
            });
        });

        refresh();

        return { refresh: refresh };
    })();


    /*
        === 確認のダイアログ（Scripts/site.js の vbChatConfirm の写し）===

        部屋（Chat/ChatRoom.aspx）は Site.Master を使わないので、site.js を読み込んでいない。
        管理人の道具（上の OwnerTools）で要るようになったので、写した（site.js と Site.css の「確認ダイアログ」に書いてあった決まりどおり）。
        見た目は Content/ChatRoom.css の「確認ダイアログ（Site.css の写し）」。

        【片方だけ直さないこと】作法（最初の焦点は「やめる」・Tab は2つの中で回す・Esc と下敷きで閉じる・
        閉じたら開いたボタンへ焦点を戻す・文言は textContent）は、site.js と同じにしてある。

        【写しにした理由（共通のファイルに出さなかった理由）】
        ①ラウンジ・トップの読み込み（Site.Master）と、ラウンジのパスワード・ノックの小窓（lounge-enter.js。同じ部品を使っている）を
          この回で触らずに済む ②「実行する」ボタンの色は、Site.css ではページ全体の button の決まりから来ていて、
          部屋にはその決まりが無い＝CSS はどのみち部屋の側に書くことになる。トグルスイッチが2か所に写しになっているのと同じ事情

        【部屋だけで足した2つ】
          ・Esc を部屋の側（パネルを閉じる Esc）へ渡さない（stopPropagation）。渡すと、確認を閉じたつもりで参加者一覧まで閉じる
          ・パネルの「外を押したら閉じる」が、このダイアログの中を外と取り違えない（document の click の見張りで除いてある）
    */
    function vbChatConfirm(options) {

        /* 【二重に開かないこと】（site.js と同じ） */
        if (document.querySelector('.custom-confirm-overlay')) {
            return;
        }

        var opts = options || {};

        var previousFocus = document.activeElement;

        var overlay = document.createElement('div');
        overlay.className = 'custom-confirm-overlay';

        var box = document.createElement('div');
        box.className = 'custom-confirm';
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

        /*
            【大きく見せる1行】（オートリンクで足した）
            外のサイトへ移動するときに、**行き先のドメインだけを大きく**出す。
            news.yahoo.co.jp.あやしい.com のような偽物は、全体を並べると見落とすが、
            ドメインだけを大きく出せば「あやしい.com」が目に入る。
            下の subText（URL の全体）は、小さく折り返して添える
        */
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

        (opts.lines || []).forEach(function (text) {
            var line = document.createElement('p');
            line.textContent = text;
            body.appendChild(line);
        });

        box.appendChild(body);

        var actions = document.createElement('div');
        actions.className = 'custom-confirm-actions';

        /* 安全な側を先（左）に置く */
        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'custom-confirm-btn custom-confirm-btn--cancel';
        cancelBtn.textContent = opts.cancelText || 'やめる';

        /*
            【okHref を渡すと、実行する側が <a> になる】（オートリンク）

            確認したあとに window.open を呼ぶと、**ブラウザが「勝手に開いた窓」とみなして
            止めることがある**（とくにスマホの Safari）。押したもの自身がリンクなら、
            利用者が押して開いたことがブラウザにも分かるので、止められない。

            rel="noopener noreferrer" … 開いた先から window.opener でこちらを触られない・
            どこから来たかを渡さない（リンクを新しいタブで開くときの決まり）
        */
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

        /* 最初の焦点は「やめる」（Enter の空押しで、取り返しのつかない側が走らないように） */
        cancelBtn.focus();

        function close() {
            document.removeEventListener('keydown', onKeyDown, true);
            if (overlay.parentNode) {
                overlay.parentNode.removeChild(overlay);
            }
            if (previousFocus && previousFocus.focus && document.contains(previousFocus)) {
                previousFocus.focus();
            }
        }

        function onKeyDown(e) {

            if (e.key === 'Escape') {
                e.preventDefault();
                /* 部屋の Esc（パネルを閉じる）へ渡さない */
                e.stopPropagation();
                close();
                return;
            }

            if (e.key !== 'Tab') {
                return;
            }

            /* Tab を2つのボタンの中だけで回す */
            e.preventDefault();

            if (document.activeElement === cancelBtn) {
                okBtn.focus();
            } else {
                cancelBtn.focus();
            }
        }

        document.addEventListener('keydown', onKeyDown, true);

        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                close();
            }
        });

        cancelBtn.addEventListener('click', close);

        okBtn.addEventListener('click', function () {

            /*
                【<a> のときは、既定の動き（新しいタブで開く）を止めないこと】
                close() は小窓を消して焦点を戻すだけなので、リンクの移動とはぶつからない
            */
            close();

            if (typeof opts.onOk === 'function') {
                opts.onOk();
            }
        });
    }


    /*
        === 発言の中のリンクを押したとき ===

        **ふつうの左クリックだけを横取りして、確認の小窓を出す。**
        Ctrl（⌘）＋クリック・中クリック・Shift ＋クリックは、そのままブラウザに任せる——
        「新しいタブで開く」と分かっていて押している操作なので、そこへ確認を挟む意味が無い。

        【パネルが開いているときは、ここへ届かない】
        参加者一覧・設定が開いている間に部屋のログを押すと、下の「外を押したら閉じる」の
        見張りが閉じるだけで止める（画像・スタンプ・相手の名前と、まったく同じ扱い）。
        **閉じるつもりの一押しで、外のサイトへ行こうとする小窓が出ないため**である
    */
    $(document).on('click', 'a.message-link', function (e) {

        if (e.which && e.which !== 1) {
            return;
        }

        if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) {
            return;
        }

        e.preventDefault();

        var href = this.href;
        var host = href;

        try {
            host = new URL(href).hostname || href;
        } catch (err) {
            /* href は組み立てるときに new URL() を通しているので、ここへは来ないはず */
        }

        vbChatConfirm({
            title: '外部サイトへ移動します',
            strongText: host,
            subText: href,
            lines: [
                'このリンクは、発言した人が書いたものです。',
                '知らないサイトでは、パスワードやカードの番号を入れないでください。'
            ],
            okText: '開く',
            okHref: href,
            cancelText: 'やめる'
        });
    });

    /* 参加者を1人削除する（UserLeft受信時に呼ばれる） */
    function removeParticipant(localId) {

        var leaving = participants.filter(function (p) {
            return p.localId === localId;
        })[0];

        participants = participants.filter(function (p) {
            return p.localId !== localId;
        });

        /*
            === 選んでいた相手が居なくなったら、選択を解除する ===

            解除しないと、**一覧からその人が消えたのに、ボタンだけが
            「非表示をやめる」のまま残る**（開発者が発見）。
            誰も選んでいないのだから、ボタンは「非表示にする」・押せない、が正しい。

            【その人の非表示は続けること】消すのは選択だけである。
            また入ってきたときに「非表示中」が付いたままでなければ、
            「退室して入り直せば無視が外れる」ことになってしまう
            （匿名IDは同じ値なので、IgnoreList を触らなければ自然にそうなる）

            【設計の抜け】
            「一覧を作り直したら、選んでいた相手を選び直す」とは書いてあったが、
            **その相手が居なくなった場合**を書いていなかった
        */
        if (leaving && leaving.anonId && leaving.anonId === selectedAnonId) {
            selectedAnonId = null;
        }

        renderParticipantList();
        updateParticipantCount();
    }

    /*
        === パネル（参加者一覧・設定）の開閉 ===

        【必須】両方を同時に開いてはいけない。
        どちらもチャットログの上へ覆いかぶさる形で、まったく同じ場所に出るため、
        重なって表示され、下になったほうが操作できなくなる。
        そのため「開くのはどれか1つだけ」という形に、最初から統一してある。

        パネルを増やすときは、この配列へ1行足すこと
        （個別に開閉を書くと、必ずどこかで排他の掛け忘れが起きる）
    */
    var panels = [

        /*
            closeOnOutside … 外を押す（click）か Esc でも閉じるパネル。
            中身は下の「外を押す・Esc で閉じる」にある。
            **入れるものを選ぶパネルには付けないこと**——あれは入力欄のすぐ上に出て、
            入力欄と一緒に使う。付けると、絵文字を選びながら文字を打とうとした瞬間に閉じる
            （あちらには、入力欄を触ったら閉じる専用の手当てが別にある）
        */
        { panel: '#participantPanel', button: '#participantCountBtn', closeOnOutside: true },
        /*
            設定パネルを閉じたら、「見た目」の色を選ぶ道具も閉じる。
            道具は body の直下に出るので、パネルと一緒には消えない。
            **アイコンの小窓も同じ**（position: fixed なので、パネルを隠しても残る）。
            Appearance はこの配列より後ろで作られるが、呼ばれるのは閉じたとき（＝画面が動き出したあと）
        */
        {
            panel: '#settingsPanel',
            button: '#settingsBtn',
            closeOnOutside: true,
            onOpen: function () { RoomSettings.onPanelOpen(); },
            onClose: function () {
                Appearance.hidePicker();
                Appearance.hideIconPanel(false);
                RoomSettings.onPanelClose();
            }
        },

        /*
            発言に入れるもの（絵文字・スタンプ）を選ぶパネル。
            出る場所は入力欄の上で、上の2つ（チャットログの上）とは違うが、
            チャットログの下のほうと重なりうるので、同じ排他に乗せてある。

            onOpen は、この配列に足したパネルだけが持つ「開いたときの一手間」。
            絵文字はピッカー本体もデータも押されるまで読まないので、
            ここで初回の読み込みを始める。

            【なぜ、ボタンに別のクリックハンドラを足さないのか】
            足しても動くが、そうすると「排他はここ、読み込みはあちら」と離れる。
            開いたときの処理は、開くことを決めている側が持つほうが追いやすい。

            【EmojiPicker ではなく InsertPanel を呼んでいる理由】
            このパネルにはタブが2つある。絵文字のデータを取りに行ってよいのは、
            **絵文字のタブが表に出たときだけ**である。
            前回スタンプのタブで閉じた人が開き直したときに読み込むと、
            見えてもいないものに 200KB 以上を使うことになる。
            その判断は、タブを持っている InsertPanel の側にある
        */
        {
            panel: '#insertPanel',
            button: '#insertBtn',
            onOpen: function () { InsertPanel.onPanelOpened(); },
            onClose: function () { InsertPanel.onPanelClosed(); },

            /*
                【このパネルだけ、ボタンの開閉を下の共通の手当てに任せない】
                入れる先が2種類（本体の入力欄／こっそりの窓）あるので、
                「開いていれば閉じる」だけでは足りない——
                **こっそりの窓あてに開いているときに本体の丸いボタンを押したら、
                閉じずに本体あてへ切り替える**のが自然である。
                その判断は宛先を持っている InsertPanel.toggleFor がする
            */
            customToggle: true
        }
    ];

    /*
        指定したパネルだけを開き、他はすべて閉じる
        openPanelSelector に null を渡すと、すべて閉じる
    */
    function setOpenPanel(openPanelSelector) {

        panels.forEach(function (p) {

            var isOpen = (p.panel === openPanelSelector);

            /*
                【必ず、書き換える前に読むこと】
                下の onClose は「今このとき閉じられた」ときだけ呼びたい。
                toggleClass のあとで読むと、読めるのは新しい状態のほうなので、
                wasOpen と isOpen が必ず同じ値になり、**onClose が一度も呼ばれない**。
                （行を足す前に測る、というこの画面の決まりと同じ形）
            */
            var wasOpen = $(p.panel).hasClass('is-open');

            $(p.panel).toggleClass('is-open', isOpen);

            /* ボタン側にも開閉状態を持たせる（押されている見た目にするため） */
            $(p.button).toggleClass('is-active', isOpen);

            /*
                読み上げソフトへも、開閉を伝える。
                aria-expanded を書いていないボタンには、属性が増えるだけで害はない
            */
            $(p.button).attr('aria-expanded', isOpen ? 'true' : 'false');

            /*
                開いたとき・閉じたときの一手間は、この配列に持たせてある
                （持っていないパネルは、何も起きない）。

                【閉じた側だけを呼ぶこと】
                「開いていない」ことと「今このとき閉じられた」ことは別である。
                wasOpen を見ずに呼ぶと、他のパネルを開くたびに、
                閉じたままのパネルの後片付けが毎回走る
            */
            if (isOpen && p.onOpen) {
                p.onOpen();
            } else if (!isOpen && wasOpen && p.onClose) {
                p.onClose();
            }
        });

        /* 人数表示の矢印は、参加者一覧の開閉にあわせて▼／▲を切り替える */
        $('#participantToggleIcon').text(
            openPanelSelector === '#participantPanel' ? '▲' : '▼');

        /*
            設定パネルを開いている間の印。
            要望：スマホで部屋名を打とうとしたときは、発言欄を閉じる。
            狭い画面だけ、この印が付いている間は本体の発言欄を隠す（ChatRoom.css の
            「狭い画面で、…本体の入力欄を隠す」。こっそりの窓・スタンプのタブと同じ隠し方）。
            【設定パネルだけ】打ち込む場所があるのは設定パネルだけ。入れるもののパネルは発言欄と一緒に使い、
            参加者一覧は相手を選んで発言に入れる使い方がある。
            印は幅に関係なく付け、隠すかどうかは CSS のメディアクエリだけが決める（PC 幅では何も変わらない）
        */
        $('.chat-room').toggleClass('is-settings-open', openPanelSelector === '#settingsPanel');
    }

    /*
        各ボタンは、自分のパネルの開閉をトグルする
        （既に開いていれば閉じ、閉じていれば「そこだけを」開く）
    */
    panels.forEach(function (p) {

        if (p.customToggle) {
            return;
        }

        $(p.button).on('click', function () {
            var isOpen = $(p.panel).hasClass('is-open');
            setOpenPanel(isOpen ? null : p.panel);
        });
    });

    /* 本体の丸いボタン＝本体の入力欄あて（宛先 null） */
    $('#insertBtn').on('click', function () {
        InsertPanel.toggleFor(null);
    });

    /*
        === パネルの「閉じる」ボタン（3つとも共通） ===

        【1か所にまとめてある理由】
        3つのパネルに、それぞれ閉じる処理を書いてはいけない。
        排他は setOpenPanel が一手に引き受けており、
        個別に閉じる処理を書くと、どこかで掛け忘れが起きる
        （「個別に開閉を書くと、必ずどこかで排他の掛け忘れが起きる」）。

        同時に開くのは1つだけなので、押されたら setOpenPanel(null) でよい。

        【フォーカスを、開いたボタンへ戻すこと】
        押した瞬間にボタン自身が消えるので、戻さないと、キーボードや
        読み上げソフトで使っている人が居場所を見失う。
        どのパネルの閉じるボタンかは data-close-panel に書いてあるので、
        そこから panels の並びを引いて、対になるボタンへ返す
        （「新着あり」を押したあとと、同じ手当て）
    */
    $(document).on('click', '.panel-close-btn', function () {

        var panelSelector = $(this).attr('data-close-panel');

        /*
            【入れるものを選ぶパネルだけ、戻す先が2通りある】
            閉じると宛先が消えるので、閉じる「前」に聞いておく
        */
        var returnTo = (panelSelector === '#insertPanel') ? InsertPanel.returnFocusEl() : null;

        setOpenPanel(null);

        if (returnTo) {
            returnTo.focus();
            return;
        }

        var entry = null;

        panels.forEach(function (p) {
            if (p.panel === panelSelector) {
                entry = p;
            }
        });

        if (entry) {
            $(entry.button).trigger('focus');
        }
    });


    /*
        === 参加者一覧・設定は、外を押すか Esc でも閉じる ===

        × のボタンは残す（「お年寄りには × があるほうが優しい」で足したもの）。
        外を押す・Esc は、それに足す閉じ方である。
        対象は panels の closeOnOutside が付いたパネルだけ（入れるものを選ぶパネルは外してある）。

        【click で拾う。pointerdown にしないこと】
        click は「押して、その場で離した」ときにしか起きないので、ログを指で払った
        （スクロールした）だけでは閉じない。pointerdown で拾うと、払った瞬間に閉じる。
        （入れるものを選ぶパネルが pointerdown なのは、入力欄を触った瞬間に閉じたいから。
        目的が違う）

        【捕捉の段階（第3引数 true）で拾う】理由は2つ。
        1. パネルの中の操作が、押された要素を作り直すことがある。
           参加者を押すと renderParticipantList が一覧を丸ごと作り直すので、
           ふつうの段階で拾うと、押された要素はもう文書から外れていて
           「パネルの外を押した」と取り違え、**相手を選んだだけでパネルが閉じる**。
           捕捉の段階なら、どの処理よりも先に、押されたままの形で判定できる
        2. ログの上で押したときは、閉じるだけにして、その先へは届けない（下の 3）

        【判定】
        1. 開いているパネルの中、または、そのパネルを開いたボタン → 何もしない
           （ボタンは自分でトグルするので、ここで閉じると開き直すことになる）
        2. それ以外 → 閉じる。ほかのパネルのボタン・入れるもののボタンを押したときは、
           閉じたあと、そのボタンが自分のパネルを開く（排他は setOpenPanel が持っている）
        3. **部屋のログ（#chatLog）の上で押したときは、閉じるだけ。その先の動きは起こさない。**
           パネルはログの上に覆いかぶさっているので、「閉じるつもりで触る」のは
           たいていログの見えている部分である。そこには押すと動くものが並んでいる——
           画像（新しいタブで開く）・スタンプ（入れるもののパネルが開く）・
           相手の名前（入力欄に宛先が入り、スマホではキーボードが開く）。
           閉じるつもりの一押しで、それらが起きると驚かせる。
           入力欄・プロフィールのバー・こっそりの窓などは、狙って押すものなので、そのまま届ける
           （こっそりの窓の見えている部分を触ったときも、閉じてよい）

        【焦点】外を押して閉じたときは、何もしない（焦点は押した先へ行く）。
        Esc で閉じたときだけ、焦点がパネルの中にあったら、開いたボタンへ戻す
        （× で閉じたときと同じ。パネルが消えると焦点の置き場所が無くなるため）。
        パネルの外（入力欄など）に焦点があったなら、そのまま動かさない——
        打っている途中で Esc を押した人の焦点を、ボタンへ持っていかない
    */
    function openOutsideClosablePanel() {

        var found = null;

        panels.forEach(function (p) {
            if (p.closeOnOutside && $(p.panel).hasClass('is-open')) {
                found = p;
            }
        });

        return found;
    }

    document.addEventListener('click', function (e) {

        var open = openOutsideClosablePanel();

        if (!open) {
            return;
        }

        /*
            【色を選ぶ道具（jscolor）の中も「パネルの中」とみなす】
            設定パネルの「見た目」から開く道具は、パネルの中ではなく body の直下に出る。
            これを外と取り違えると、色を1つ押しただけで設定パネルが閉じる
        */
        /*
            【確認のダイアログ（vbChatConfirm）の中も「パネルの中」とみなす】
            管理人の道具の確認は body の直下に出る。外と取り違えると、「退室させる」を押した瞬間に参加者一覧が閉じ、
            閉じたあと焦点を戻す先（押したボタン）が消える
        */
        /*
            【アイコンの小窓の外を押したら、小窓だけを閉じる】
            小窓は設定パネルの中の子なので、下の判定では「パネルの中」になる＝設定パネルは閉じない。
            そのままだと、小窓の外（パネルの中）を押しても小窓が閉じないので、ここで閉じておく
        */
        if (Appearance.isIconPanelOpen() &&
            $(e.target).closest('#appearanceIconPanel, #appearanceIconBtn').length === 0) {
            Appearance.hideIconPanel(false);
        }

        if ($(e.target).closest(open.panel + ', ' + open.button + ', .jscolor-wrap, .custom-confirm-overlay').length > 0) {
            return;
        }

        setOpenPanel(null);

        if ($(e.target).closest('#chatLog').length > 0) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);

    $(document).on('keydown', function (e) {

        if (e.key !== 'Escape') {
            return;
        }

        var open = openOutsideClosablePanel();

        if (!open) {
            return;
        }

        /*
            色を選ぶ道具が開いていれば、Esc はそれだけを閉じる（設定パネルは開いたまま）。
            焦点は色のボタンへ戻す（道具の中に焦点は入らないが、念のため）
        */
        if (Appearance.isPickerOpen()) {
            Appearance.hidePicker();
            $('#appearanceColorBtn').trigger('focus');
            return;
        }

        /*
            アイコンの小窓も同じ。Esc はこれだけを閉じて、
            焦点をアイコンのボタンへ戻す（設定パネルは開いたまま。2度目の Esc でパネルが閉じる）
        */
        if (Appearance.isIconPanelOpen()) {
            Appearance.hideIconPanel(true);
            return;
        }

        var active = document.activeElement;
        var focusWasInside = !active || active === document.body ||
            $(active).closest(open.panel).length > 0;

        setOpenPanel(null);

        if (focusWasInside) {
            $(open.button).trigger('focus');
        }
    });


    /* === 接続状態バーの表示制御 === */

    /*
        帯を表示する

        isError  … trueなら完全に切断された状態（赤）、falseなら再接続中（黄）
        canRetry … 「再接続」ボタンを出すかどうか。省略すると、これまで通り
                   isErrorに従う（赤ならボタン2つ、黄なら両方とも隠す）

        【なぜ、再接続ボタンだけ別に消せるようにしたのか】
        接続は生きているのに、サーバー側の都合で発言だけが通らない状態がある
        （下のshowServerTroubleBar）。そこで再接続ボタンを出しても、
        reconnect()が「もう繋がっている」と判断して即座に戻るため、
        押しても何も起きないボタンになる。出さないほうが正しい

        ボタンの出し入れは、必ずこの1か所で決めること。
        呼び出し側で個別に $('#reconnectBtn').hide() を足すと、次にこの関数が
        呼ばれた瞬間に戻ってしまい、原因の分かりにくい食い違いになる
    */
    function showConnectionBar(text, isError, canRetry) {

        if (typeof canRetry === 'undefined') {
            canRetry = isError;
        }

        /*
            【順番が重要】先に帯を出してから、文字を入れること。

            .connection-bar は display: none で、is-visible が付くまで
            画面に存在しない扱いになる。読み上げソフトは、隠れている間の変更を
            拾わないため、「文字を入れる → 表示する」の順にすると、
            aria-live を付けていても一言も読まれない。
            しかも画面には帯がちゃんと出るので、目で見ている限りは直った気になる。

            同じ関数の中で続けて行うため、目で見ている人の見え方は変わらない
        */
        $('#connectionBar')
            .toggleClass('connection-bar--error', isError)
            .toggleClass('connection-bar--warning', !isError)
            .addClass('is-visible');

        $('#connectionBarText').text(text);

        $('#reconnectBtn').toggle(isError && canRetry);
        $('#backToLoungeBtn').toggle(isError);
    }

    /* 帯を隠す */
    function hideConnectionBar() {

        $('#connectionBar').removeClass('is-visible');

        /*
            隠したら文字も空にする。
            同じ文言で2度目に出たとき、文字が変わらないと読み上げが起きないため
            （例：切断 → 復帰 → また切断。2度目の「接続が切れました…」が読まれない）。

            隠したあとに空にしているので、この行で読み上げが起きることはない。
            帯は display: none で、その時点では読み上げの対象外になっている
        */
        $('#connectionBarText').text('');
    }

    /*
        サーバー側の都合で、発言が通らなくなったことを知らせる

        【いつ、この状態になるのか】
        実質、アプリケーション（IIS）が再起動したときだけである。
        再起動でChatHubのShared変数が全部消えるため、SignalRの接続は生きたまま、
        サーバー側からは「そんな参加者は居ない」状態になる。
        2窓目による置き換えと無言タイムアウトはkickedでラウンジへ飛ばしており、
        退室ボタンは自分で移動するので、ここへは来ない。

        呼ばれる経路は2つあるが、利用者から見た結果は同じなので、表示も1つにまとめる。
          ・SendMessageが accepted = false・rejectReason 無しを返した
          ・ハブの呼び出しそのものが失敗した（.fail）
            ＝発行でハブのメソッド署名が変わった直後、古いページが古い引数で呼び、
              SignalRがメソッドを解決できないケース

        【文言について】
        部屋がどうなっているかには、意図的に一切触れていない。
        「部屋はそのまま残っています」と書くと、ユーザーが部屋を作れるように
        なったあと（一時部屋は本当に消えていることがある）、無い部屋を
        ラウンジで探させることになる。
        「この発言は届いていません」も書かない。打った文章が入力欄に残るため、
        そちらで伝わる

        【入力欄を落とすこと】
        落とさないと、送信を押すたびに同じ帯が出るだけになる
    */
    function showServerTroubleBar() {

        showConnectionBar(
            'サーバー側の都合（再起動など）で、一時的に発言できない状態になりました。申し訳ありません。',
            /*
                第2引数 true  … 赤（利用者の操作が必要）
                第3引数 false … 「再接続」ボタンは出さない（接続は生きているため）
            */
            true,
            false);

        setInputEnabled(false);
    }


    /*
        接続の状態から見て、入力してよい状態かどうか
        （送信ボタンは、これと「文字数が上限内か」の両方を満たすときだけ押せる）
    */
    var isInputEnabledByConnection = true;

    /*
        入力欄と送信ボタンの、使用可否を切り替える
        接続が切れている間は、送信しても届かないため操作させない
    */
    function setInputEnabled(enabled) {
        isInputEnabledByConnection = enabled;
        $('#messageInput').prop('disabled', !enabled);
        updateSendButtonState();
    }

    /*
        送信ボタンの有効／無効を決める

        【なぜ、1か所にまとめるのか】
        送信ボタンを押せなくする理由は2つある（接続が切れている／文字数が上限超え）。
        それぞれの場所から prop('disabled', ...) を書くと、あとから起きたほうが
        もう一方の判断を黙って上書きしてしまう。
        たとえば500文字を超えたまま接続が復帰すると、上限を超えているのに
        送信ボタンだけが押せる状態に戻る。
        決めるのは必ずここ1か所にして、両方の条件をまとめて見ること
    */
    function updateSendButtonState() {
        var isTooLong = $('#messageInput').val().length > MAX_MESSAGE_LENGTH;
        $('#sendMessageBtn').prop('disabled', !isInputEnabledByConnection || isTooLong);

        /*
            入れるものを選ぶボタンは、接続の状態だけで決める。文字数は見ない。

            【なぜ、送信ボタンと条件が違うのか】
            上限を超えているときに送れないのは当然だが、そのときこのボタンまで
            押せなくすると、「消してから入れ直す」ことすらできなくなる。
            入れれば必ず超えるので押させない、という考え方もあるが、
            入力欄そのものは打てるままなのだから、揃わないほうが不自然になる。

            接続が切れているときに落とすのは、入力欄・送信ボタンと揃えるため
        */
        $('#insertBtn').prop('disabled', !isInputEnabledByConnection);

        /* 押せなくなったのに開いたまま、という状態を残さない */
        if (!isInputEnabledByConnection && $('#insertPanel').hasClass('is-open')) {
            setOpenPanel(null);
        }
    }

    /* === 文字数の表示 === */

    /*
        「残りがこれ以下になったら、表示を濃くする」という線引き

        【なぜ、50とべた書きしないのか】
        上限（MAX_MESSAGE_LENGTH）はサーバー側が決めており、将来は管理画面から
        変えられるようにする予定がある。ここに数字を固定で書くと、上限を変えた日に
        ここだけ古いまま残る（Kickedの文言で踏んだ罠と、まったく同じ形）。
        上限からの割合で出しておけば、上限が変わっても自動で付いてくる。
        1割にしているのは、今の上限500文字でちょうど「残り50」になるため
        （DB側は CounterWarnPercent として％で持ち、渡すときに小数へ直している）
    */
    var COUNTER_WARN_RATIO = vbChatSettings.counterWarnRatio;
    var COUNTER_WARN_REMAINING = Math.max(1, Math.round(MAX_MESSAGE_LENGTH * COUNTER_WARN_RATIO));

    /*
        今どの段階にいるか（'normal' / 'near' / 'over'）。
        段階が変わった瞬間だけ読み上げるための、前回の値の控え
    */
    var counterStage = null;

    /*
        文字数の表示を更新する

        【「残り」ではなく「現在 / 上限」で出す理由】
        残りだけを出すと、数字が減っていくのを見て初めて「何が50なのか」を
        考えることになる。上限を並べて出しておけば、説明を書かなくても意味が通る。
        超えたときも「501 / 500 文字」になり、マイナス表示のように
        読み手が引き算をしなくて済む

        【数えるのは、正規化する前の生の長さ（.length）】
        サーバー側は正規化した後の長さで判定し、正規化は文字を減らす方向にしか
        働かないため、サーバーが数える長さは必ずこれ以下になる。
        つまり「画面では収まっていたのに、サーバーに弾かれる」は起こらない向きになる

        JavaScriptの .length、VBの String.Length、DBのNVARCHARは、いずれも
        UTF-16の単位で数える（絵文字1つは2文字）。3か所で数え方が揃っている
    */
    function updateMessageCounter() {

        var length = $('#messageInput').val().length;
        var remaining = MAX_MESSAGE_LENGTH - length;

        /* 上限は、サーバーから渡された値をそのまま出す（画面に数字を書かない） */
        $('#messageCounter').text(length + ' / ' + MAX_MESSAGE_LENGTH + ' 文字');

        /*
            目立たせ方は3段階。
            打っている間ずっと目立つのは邪魔なので、近づいたときだけ強くする
              normal … 薄いグレー（あるのは分かるが、気にならない）
              near   … 濃くする（まだエラーではないので、色は変えない）
              over   … 赤（送信ボタンも無効になる）
        */
        var stage = 'normal';

        if (remaining < 0) {
            stage = 'over';
        } else if (remaining <= COUNTER_WARN_REMAINING) {
            stage = 'near';
        }

        $('#messageCounter')
            .toggleClass('message-counter--near', stage === 'near')
            .toggleClass('message-counter--over', stage === 'over');

        announceCounterStage(stage, remaining);

        updateSendButtonState();
    }

    /*
        上限に近づいたことを、読み上げソフトにだけ知らせる

        【段階が変わった瞬間だけ知らせる】
        目で見えている人は、色が変わったことに一瞬で気づける。
        音声で使っている人には、その変化が届かない。
        かといって数字そのものを読み上げ対象にすると、1文字打つたびに
        「123 / 500 文字」と読まれ、会話にならない。
        そこで、段階（normal → near → over）が変わった時だけ文言を入れ、
        普段は空にしておく（空の要素は読み上げられない）

        【文言に入れる数字も、その場の実測値を使う】
        「残り50文字です」と固定にすると、貼り付けで一気に飛び越えたときに
        嘘になる（残り12文字なのに「残り50文字」と読まれる）
    */
    function announceCounterStage(stage, remaining) {

        if (stage === counterStage) {
            return;
        }

        counterStage = stage;

        var text = '';

        if (stage === 'near') {
            text = '残り' + remaining + '文字です';
        } else if (stage === 'over') {
            text = '上限の' + MAX_MESSAGE_LENGTH + '文字を超えました。短くしてください';
        }

        $('#counterAnnounce').text(text);
    }

    /*
        送信できなかった理由を、入力欄の上に出す

        文言はサーバーが決めて、戻り値で返してくる。ここには書かないこと
        （同じ日本語を2か所で管理すると、必ず片方だけ直して食い違う）
    */
    function showSendReject(text) {
        $('#sendRejectText').text(text);

        /*
            理由と改行の案内は、同じ場所を使う。理由のほうが今すぐ必要な情報なので、
            出ている間は案内を引っ込める（横に並べると、狭い画面で両方が潰れる）
        */
        $('#inputHint').hide();
    }

    /* 理由の表示を消す（次の送信が受け付けられたときに呼ぶ） */
    function hideSendReject() {
        $('#sendRejectText').text('');
        $('#inputHint').show();
    }


    /* === 改行のしかたの案内 === */

    /*
        タッチ操作が主体の端末（スマホ・タブレット）かどうかを判定する
        これらの端末のキーボードにはShiftが無いため、
        「Shift+Enterで改行」という操作自体が存在しない

        端末名（ユーザーエージェント）では判定しない。
        新しい端末が出るたびに判定が漏れるため、「タッチ操作が主体かどうか」で見る

        【この宣言を、下へ動かさないこと】
        使う場所はEnterキーの判定（ずっと下）と、すぐ下の案内の文言の2か所ある。
        varは巻き上げられるため、宣言が後ろにあっても構文エラーにはならず、
        案内の文言のほうだけが undefined を見て、
        「スマホなのにShift+Enterと案内する」という形で静かに間違える
    */
    var isTouchDevice = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;

    /*
        文言は、端末によって変える。キー操作の仕様そのものが違うため
        （PCはEnterで送信・Shift+Enterで改行、タッチ端末はEnterで改行・送信はボタンのみ）。
        この2つの文言は、下の isTouchDevice の判定と対になっている。
        キー操作の仕様を変えるときは、必ずここも一緒に直すこと
    */
    var INPUT_HINT_PC = 'Shift+Enter で改行';
    var INPUT_HINT_TOUCH = 'Enter で改行';

    /*
        案内の文言を入れる

        【なぜ必要になったか】
        入力欄を1行から始める形にしたため、見ただけでは「複数行も打てる」ことが
        分からなくなった。3行ぶんの高さがあった頃は、その高さ自体が案内になっていた。
        目で見る人にはこの1行で、音声で使う人にはaria-describedby経由で、
        どちらにも同じことが伝わるようにしてある
    */
    function applyInputHint() {
        $('#inputHint').text(isTouchDevice ? INPUT_HINT_TOUCH : INPUT_HINT_PC);
    }


    /* ============================================================
       絵文字ピッカー

       【絵文字は、機種依存でいく】
       画像もSVGも1枚も持たない。Unicodeの文字をそのまま発言に流し、
       絵は閲覧している端末のフォントが描く。
       だから、発言の表示のためにサーバーもクライアントも1バイトも転送しない。
       端末によって絵が違うのは、不具合ではなく機種依存の定義そのものである。

       ここで読み込む670KBは「探すため」の道具であって、「見せるため」ではない。
       だから、押されるまで読まない。

       【Sounds・IgnoreList と同じ形にしてある】
       外から呼ぶのは onPanelOpened() だけ。読み込み・検索・挿入は、この中に閉じている
       ============================================================ */

    /*
        検索語と、データの両方に掛ける正規化

        【最重要】絵文字のデータ（Scripts/lib/emoji）を作るときに掛けた Normalize() と、1文字も違えてはいけない。
        あちらはデータを作るときに、ここは利用者が打ったときに掛ける。
        片側だけでは一致しない。順番も含めて、次の3段でなければならない。

          1) NFKC 正規化   … 半角カナ→全角カナ、全角英数→半角英数
          2) 小文字化
          3) カタカナ→ひらがな

        3) は U+30A1〜U+30F6 から 0x60 を引くだけ。
        長音「ー」(U+30FC) はこの範囲の外なので、そのまま残るのが正しい
        （「らーめん」と「ラーメン」が、どちらも「らーめん」になる）。

        【表記ゆれを、データで持たなかった理由】
        ひらがな版もデータに持つ案を却下した。
        データが1バイトも増えず、後から揺れの種類を足せば全絵文字へ一斉に効くため。
        揺れを足したくなったら、ここと C# の両方へ、同じものを足すこと

        【まだ効かないもの】漢字とひらがな。
        「わらう」で「笑う」は引けない。文字の変換ではなく読みの知識が要るため、
        正規化では消せない。読みがなをデータへ足す回で対応する
    */
    function normalizeSearchText(text) {

        if (!text) {
            return '';
        }

        var normalized = text.normalize('NFKC').toLowerCase();
        var result = '';

        for (var i = 0; i < normalized.length; i++) {

            var code = normalized.charCodeAt(i);

            if (code >= 0x30A1 && code <= 0x30F6) {
                result += String.fromCharCode(code - 0x60);
            } else {
                result += normalized.charAt(i);
            }
        }

        return result.trim();
    }


    /*
        ピッカーのUIに出る日本語。

        【なぜ、データと同じ場所に置かないのか】
        @emoji-mart/data には i18n/ja.json（850B）が入っているが、
        あれを別ファイルで配ると、初回の取得が1本増え、発行し忘れる的も1つ増える。
        画面に出す文言なので、画面のコードと同じ場所にあるほうが直しやすい。
        文言を変えたくなったら、ここを直せばよい
    */
    var EMOJI_I18N = {
        search: '検索',
        search_no_results_1: '見つかりませんでした',
        search_no_results_2: '別のことばで探してみてください',
        pick: '絵文字を選ぶ',
        add_custom: '絵文字を追加する',
        categories: {
            activity: 'アクティビティ',
            custom: 'カスタム',
            flags: '旗',
            foods: '食べもの・飲みもの',
            frequent: 'よく使う',
            nature: '動物・自然',
            objects: 'もの',
            people: '顔・人',
            places: '場所・乗りもの',
            search: '検索結果',
            symbols: '記号'
        },
        skins: {
            choose: '肌の色を選ぶ',
            1: '標準',
            2: '明るい肌色',
            3: 'やや明るい肌色',
            4: '肌色',
            5: 'やや濃い肌色',
            6: '濃い肌色'
        }
    };


    var EmojiPicker = (function () {

        /* 検索結果を、いちどに何件まで出すか */
        var MAX_SEARCH_RESULTS = 60;

        /*
            'idle'（まだ読んでいない）／'loading'（読んでいる最中）
            ／'ready'（使える）／'error'（読めなかった）
        */
        var loadState = 'idle';

        /*
            検索の結果が、順番どおりに返ってくるとは限らない。
            「あ」→「あい」と続けて打つと、先に投げた「あ」のほうが
            あとから返ってくることがあり、そのまま描くと1文字前の結果が残る。
            いちばん新しい呼び出しの番号だけを覚えておいて、それ以外は捨てる
        */
        var searchToken = 0;

        /*
            日本語入力の変換中かどうか。

            **検索を走らせるかどうかの判断には、使っていない**（下の「検索窓の見張り」の
            コメントを参照）。今のところ、この値を読んでいる場所は無い。
            それでも持っているのは、変換中かどうかを見たくなったときに、
            この2つのイベントを二重に張らずに済むようにするため
        */
        var isComposing = false;

        /*
            最後の入力から、何ミリ秒待ってから検索するか（デバウンス）。

            打つたびに検索すると、「ねこ」の3打で3回走る。
            **打ち終わるのを待ってから1回だけ走らせる**ことで、
            変換中の途中の状態（「ｎ」「ね」「ねｋ」）で無駄に走るのを抑える。

            150ms は「打っている最中は走らせず、指が止まったらすぐ出す」あたりの値。
            短くすると回数が増え、長くすると出るのが遅く感じる
        */
        var SEARCH_DEBOUNCE_MS = 150;

        var searchTimer = null;

        /*
            最後に実際に検索した語（正規化したあと）。
            同じ語で描き直さないための控え。null は「まだ検索していない」
        */
        var lastQuery = null;

        /* 検索を予約する（既に予約があれば、打ち直す） */
        function scheduleSearch() {

            if (searchTimer !== null) {
                clearTimeout(searchTimer);
            }

            searchTimer = setTimeout(function () {
                searchTimer = null;
                runSearch();
            }, SEARCH_DEBOUNCE_MS);
        }

        /* 予約を取り消す（パネルを閉じたときなど） */
        function cancelScheduledSearch() {

            if (searchTimer !== null) {
                clearTimeout(searchTimer);
                searchTimer = null;
            }
        }


        function setStatus(text, isError) {
            $('#emojiTabStatus')
                .text(text || '')
                .toggleClass('is-error', !!isError);
        }


        /*
            ピッカー本体（emoji-mart.js）を読む

            【なぜ $.getScript を使わないのか】
            あれは既定でキャッシュを無効にし、URLへ勝手に _=<時刻> を足す。
            そうなると毎回98KBを取り直すことになり、AssetUrl の ?v= で
            「変わったときだけ取り直す」ようにしてある意味が消える
        */
        function loadLibrary() {

            return $.Deferred(function (task) {

                if (window.EmojiMart) {
                    task.resolve();
                    return;
                }

                var url = $('#emojiLibUrl').val();

                if (!url) {
                    task.reject('emojiLibUrl が空です');
                    return;
                }

                var script = document.createElement('script');
                script.src = url;
                script.onload = function () {

                    /* 読めたのに中身が無い、という壊れ方も拾っておく */
                    if (window.EmojiMart) {
                        task.resolve();
                    } else {
                        task.reject('EmojiMart が見つかりません');
                    }
                };
                script.onerror = function () {
                    task.reject('ピッカー本体を読み込めませんでした');
                };

                document.head.appendChild(script);

            }).promise();
        }


        /*
            日本語データ（emoji-ja.js。中身はJSON）を読む

            【罠】fetch は404でもrejectしない。res.ok を自分で見ること。
            見ないと、404のHTMLをJSONとして読もうとして、
            分かりにくいところで落ちる（効果音の読み込みで踏んだのと同じ罠）
        */
        function loadData() {

            var url = $('#emojiDataUrl').val();

            if (!url) {
                return $.Deferred().reject('emojiDataUrl が空です').promise();
            }

            return $.Deferred(function (task) {

                fetch(url).then(function (res) {

                    if (!res.ok) {
                        throw new Error('HTTP ' + res.status);
                    }

                    return res.json();

                }).then(function (data) {
                    task.resolve(data);
                }).catch(function (err) {
                    task.reject('絵文字データを読み込めませんでした（' + err.message + '）');
                });

            }).promise();
        }


        /* 絵文字のタブが表に出たときに呼ばれる（パネルを開いた・タブを切り替えた）。

           【呼ぶかどうかを決めているのは InsertPanel の側】
           スタンプのタブで開いたときは、ここは呼ばれない
           （見えてもいないもののために、200KB以上を取りに行かないため） */
        function onPanelOpened() {

            /*
                【必ず、検索窓と検索結果の両方を空に戻す】

                片方だけだと、**窓は空なのに前回の結果が残っている**状態になる
                （実機で見つかった）。
                前に探した語をそのまま出しておく作りにはしない——
                絵文字は「そのとき言いたいこと」で探すもので、前回の続きではない
            */
            clearSearch();

            if (loadState === 'ready') {
                focusSearchBox();
                return;
            }

            if (loadState === 'loading') {
                return;
            }

            /*
                一度失敗していても、もう一度だけ試させる。
                電波が悪かっただけ、ということが普通にあるため
                （閉じて開き直せば、また取りに行く）
            */
            loadState = 'loading';
            setStatus('絵文字を読み込んでいます…', false);

            $.when(loadLibrary(), loadData())
                .done(function (ignored, data) {
                    try {
                        buildPicker(data);
                        loadState = 'ready';
                        setStatus('', false);
                        focusSearchBox();
                    } catch (e) {
                        loadState = 'error';
                        setStatus('絵文字を用意できませんでした。開き直すと、もう一度試します。', true);
                        debugLog('絵文字ピッカーの組み立てに失敗： ' + e.message);
                    }
                })
                .fail(function (reason) {
                    loadState = 'error';
                    setStatus('絵文字を読み込めませんでした。開き直すと、もう一度試します。', true);
                    debugLog('絵文字の読み込みに失敗： ' + reason);
                });
        }


        /*
            検索の状態を、まっさらに戻す

            窓の中身・検索結果・読み上げ用の件数・予約中の検索の、4つを片付ける。
            **1か所にまとめてあるのは、片付け忘れを作らないため**
            （窓だけ空にして結果を残す、が実際に起きた壊れ方だった）

            searchToken を進めているのは、**片付けたあとに、
            片付ける前に投げた検索の結果が返ってきて描かれるのを止めるため**
        */
        function clearSearch() {

            cancelScheduledSearch();
            searchToken++;
            lastQuery = null; /* 戻し忘れると、次に同じ語を打ったときに描かれない */

            $('#emojiSearchInput').val('');
            showResults(null);
        }


        function buildPicker(data) {

            /*
                SearchIndex.search() を単体で使うために、init は自分で呼ぶ。
                （Picker を作ると内部でも呼ばれるが、こちらが先に呼んでおけば、
                  ピッカーの組み立てに失敗しても検索だけは動く）
            */
            window.EmojiMart.init({ data: data, i18n: EMOJI_I18N });

            var picker = new window.EmojiMart.Picker({
                data: data,
                i18n: EMOJI_I18N,

                /* 絵は端末のフォントに任せる。画像セットは使わない */
                set: 'native',

                /* 内蔵の検索窓は消す。自前の窓から SearchIndex を呼ぶため */
                searchPosition: 'none',

                /* 下のプレビュー欄は、狭い画面では場所の無駄なので出さない */
                previewPosition: 'none',

                /* 画面の明るさは、このサイトの配色に合わせて固定する
                   （OSがダークモードだと、明るいページの上に暗い箱が出てしまう） */
                theme: 'light',

                /* 箱の幅に合わせて、1行に並ぶ数を変える */
                dynamicWidth: true,

                autoFocus: false,

                onEmojiSelect: function (emoji) {
                    insertEmoji(emoji.native);
                }
            });

            $('#emojiPickerHost').empty().append(picker);
        }


        function focusSearchBox() {

            /*
                タッチ端末では、検索窓へ勝手にカーソルを入れない。
                入れるとソフトキーボードが開いて、絵文字の並ぶ場所を覆ってしまう。
                「新着あり」を押したときにチャットログへフォーカスを移していて、
                入力欄へは移していないのと、同じ理由
            */
            if (isTouchDevice) {
                return;
            }

            $('#emojiSearchInput').trigger('focus');
        }


        /*
            選ばれた絵文字を、「よく使う」へ記録する

            === 【重要】これは Emoji Mart の公式APIではない ===

            README が公開APIとして案内しているのは
            Picker / init / SearchIndex / getEmojiDataFromNative の4つだけで、
            FrequentlyUsed はそこに書かれていない。
            **バンドル（dist/browser.js）のエクスポートには含まれている**ので、
            内部へ手を突っ込んでいるわけではないが、**約束されてもいない。**

            版が上がったときに、予告なく消えることがありうる。
            **消えても絵文字は普通に入る**（記録が黙って止まるだけ）ので、
            存在を確かめてから呼び、例外も飲む形にしてある。

            開発者の判断：公式ではない入口だが、部品の更新が 2 年止まっていて、
            自前で置いて版を固定しているので、実際の危険は小さい。
            **その理解の上で承認された。**

            【なぜ、自分で localStorage を書かないのか】
            キーも保存形式も Emoji Mart の Store が持っている。
            そこへ直接書くと、**保存形式が変わった日に静かに壊れる**。
            add() を通せば、形式のことはあちらが面倒を見てくれる。

            【なぜ、この経路にだけ要るのか】
            ピッカー本体から選んだときは、emoji-mart が中で記録している。
            検索結果は**自前のボタン**なので、そこを通らない。
            足さないと、**検索して選ぶ人（＝多数派のはず）の履歴が一切溜まらず、
            「よく使う」がいつまでも既定の15個のままになる。**

            【版を上げる日に見る場所】
            「Emoji Mart を新しくする日に、見る場所」に、
            同じ性質のものが他にもまとめてある。**そちらを先に読むこと**
        */
        function rememberAsFrequentlyUsed(id) {

            if (!id) {
                return;
            }

            try {

                var em = window.EmojiMart;

                if (em && em.FrequentlyUsed && typeof em.FrequentlyUsed.add === 'function') {
                    em.FrequentlyUsed.add({ id: id });
                }

            } catch (e) {
                /*
                    記録できなくても、絵文字は入る。
                    ここで止めると、「よく使う」のために絵文字が入らなくなる——
                    本末転倒なので、握りつぶしてよい場所である
                    （何が起きたかは、コンソールには残す）
                */
                debugLog('「よく使う」への記録に失敗： ' + e.message);
            }
        }


        /*
            選ばれた絵文字を、入力欄の「カーソルのある場所」へ入れる

            末尾へ足すのではない。文章の途中に入れたいことが普通にあるため
        */
        function insertEmoji(text) {

            if (!text) {
                return;
            }

            /*
                【入れる先は、パネルを開いたボタンで決まる】
                本体の丸いボタンなら本体の入力欄、こっそりの窓のボタンならその窓の入力欄
                （InsertPanel が持っている）
            */
            var input = InsertPanel.targetInput();

            if (!input || input.disabled) {
                return;
            }

            var start = input.selectionStart;
            var end = input.selectionEnd;
            var value = input.value;

            /*
                選択している範囲があれば、置き換える（打ったときと同じ動き）。
                selectionStart が読めない古い環境では、末尾へ足す
            */
            if (typeof start !== 'number' || typeof end !== 'number') {
                start = value.length;
                end = value.length;
            }

            input.value = value.slice(0, start) + text + value.slice(end);

            var caret = start + text.length;
            input.setSelectionRange(caret, caret);

            /*
                【必須】文字数の表示と送信ボタンの状態を、必ず更新すること。
                値をJSから書き換えたときは input イベントが起きないため、
                これを忘れると「500文字を超えているのに送信ボタンが押せる」ことになる

                絵文字は1つでUTF-16の2単位を使うので、カウンタは2つ進む。
                これは3か所（JS・VB・DB）で数え方を揃えてある結果であって、不具合ではない
            */
            /*
                文字数の表示は本体の入力欄にしか無い。こっそりの窓は、
                サーバーが同じ規則で断り、理由を窓の足もとへ出す
            */
            if (InsertPanel.target() === null) {
                updateMessageCounter();
            }

            /* パネルの「◯◯さんへ」の横に出している、入れた文の見本を更新する */
            InsertPanel.refreshPreview();

            /*
                続けて打てるように、入力欄へ戻す。
                パネルは開いたままにしておく（絵文字を続けて入れることが多いため）

                === 【必読】タッチ端末では、焦点を戻さない（本番の実機で出た） ===

                スマホで、絵文字が**1回おきにしか入らなかった**。

                    1度目 … 入る。ここで焦点を戻すので**ソフトキーボードが開き**、ページが縮む
                    2度目 … 次のタップで入力欄から焦点が外れ、**キーボードが閉じ**、ページが広がる。
                            パネルも窓も動くので、**指の下から絵文字が逃げて click が届かない**
                    3度目 … キーボードが閉じた状態なので、また入る（以下くり返し）

                踏んだ連鎖（焦点が外れる → キーボードが閉じる → 窓が動く →
                指の下からボタンが逃げる）とまったく同じ形である。
                **違うのは引き金で、あちらは「押した瞬間に焦点が外れる」、
                こちらは「こちらのコードが焦点を戻して、キーボードを開けている」。**
                だから手当ても逆向きで、**そもそもキーボードを開けない**。

                - **本体の入力欄あてでも、同じ関数を通るので同じことが起きていた**
                  （報告はこっそりの窓あてだったが、形は同じ）
                - **カーソルの位置は失わない。** 入力欄は焦点が外れていても
                  selectionStart／selectionEnd を覚えているので、次の絵文字も続きに入る
                - **続けて文字を打つときは、入力欄を触ればよい**（そのときパネルは閉じる）。
                  LINE も、絵文字を選んでいる間はソフトキーボードを出さない
                - パソコンでは今までどおり戻す（キーボードが開閉しないので、何も動かない）。
                  判定は isTouchDevice（pointer: coarse）で、改行の案内と同じもの
            */
            if (!isTouchDevice) {
                input.focus();
            }
        }


        /* 自前の検索。正規化してから SearchIndex に渡す */
        function runSearch() {

            if (loadState !== 'ready') {
                return;
            }

            var raw = $('#emojiSearchInput').val();
            var query = normalizeSearchText(raw);

            /* 空になったら、検索結果を畳んでカテゴリのピッカーへ戻す */
            if (query === '') {
                lastQuery = null;
                showResults(null);
                return;
            }

            /*
                【前と同じ語なら、やり直さない】

                「ねこ」と打って結果が出たあと、変換を確定すると compositionend が来て、
                もう一度この関数が呼ばれる。語は同じなので結果も同じだが、
                **描き直すとボタンが全部作り直しになり、
                結果を下までスクロールして見ていた位置が先頭へ戻る。**

                正規化したあとの語で比べているので、
                「ネコ」を確定して「ねこ」になっても、同じ語として扱われる
            */
            if (query === lastQuery) {
                return;
            }

            lastQuery = query;

            var token = ++searchToken;

            window.EmojiMart.SearchIndex
                .search(query, { maxResults: MAX_SEARCH_RESULTS })
                .then(function (results) {

                    /* 打っている間に追い越された結果は、捨てる */
                    if (token !== searchToken) {
                        return;
                    }

                    showResults(results || []);
                })
                .catch(function (err) {
                    if (token === searchToken) {
                        debugLog('絵文字の検索に失敗： ' + err.message);
                    }
                });
        }


        /*
            検索結果を並べる。null を渡すと、検索結果を閉じてピッカーへ戻す

            【1件が <button>😀</button> で済む】
            機種依存でいくと決めたので、画像もSVGも要らない。
            自前で描くことになったのは、Emoji Mart のピッカーに
            「外から検索結果を流し込む」公開APIが無いためである
        */
        function showResults(results) {

            var $box = $('#emojiSearchResults');

            if (results === null) {
                $box.empty().prop('hidden', true);
                $('#emojiPickerHost').prop('hidden', false);
                $('#emojiSearchCount').text('');
                return;
            }

            $('#emojiPickerHost').prop('hidden', true);
            $box.prop('hidden', false).empty();

            if (results.length === 0) {
                $box.append(
                    $('<p>').addClass('emoji-no-result')
                            .text('見つかりませんでした。別のことばで探してみてください。'));
                $('#emojiSearchCount').text('見つかりませんでした');
                return;
            }

            /*
                【テキストは text() で入れること】
                データは自分たちで作ったものだが、出口でエスケープを通すのは
                「入口の検査だけでは足りない」という、この画面の決まりである。
                jQuery の text()／attr() は、その役目をそのまま果たす
            */
            results.forEach(function (emoji) {

                var native = emoji.skins && emoji.skins.length ? emoji.skins[0].native : null;

                if (!native) {
                    return;
                }

                var $btn = $('<button>')
                    .attr('type', 'button')
                    .addClass('emoji-result-btn')
                    .attr('title', emoji.name)
                    .attr('aria-label', emoji.name)
                    .text(native);

                $btn.on('click', function () {
                    rememberAsFrequentlyUsed(emoji.id);
                    insertEmoji(native);
                });

                $box.append($btn);
            });

            $('#emojiSearchCount').text(results.length + '件見つかりました');
        }


        /* --- 検索窓の見張り --- */

        /*
            === 【重要】ここで isComposing を見てはいけない ===

            初めはEnterキーの判定と同じ形にして、変換中（compositionstart〜
            compositionend の間）は検索を走らせないようにしていた。**これは誤りだった。**

            **改行と検索では、変換中をどう扱うかが正反対である。**

              改行（Enter） … 変換確定のEnterで誤送信するのを防ぐ ＝ 走らせたくない
              検索          … 打ちながら候補を出したい           ＝ 走らせたい

            「ねこ」と打った時点で🐈が出てほしいのに、確定するまで何も出なかった。

            **除外するのではなく、間引くのが正しい形。**
            変換中の「ｎ」「ね」「ねｋ」といった途中の状態でも検索は走らせ、
            デバウンス（最後の入力から SEARCH_DEBOUNCE_MS 待つ）で回数を抑える。

            【compositionend でも呼んでいる理由】
            変換中に input イベントを出さないIMEがあるかもしれないため。
            その場合でも、確定した時点で1回は走る（＝直す前と同じ動きに落ちる）。
            **どちらのIMEでも動く形にしてあるので、実機の挙動に依存しない。**
            なお isComposing は、この経路では読んでいない
        */
        $('#emojiSearchInput').on('compositionstart', function () {
            isComposing = true;
        });

        $('#emojiSearchInput').on('compositionend', function () {
            isComposing = false;
            scheduleSearch();
        });

        $('#emojiSearchInput').on('input', function () {
            scheduleSearch();
        });

        /*
            検索窓での Enter は、何もしない。
            この画面にフォームは無いので送信は起きないが、
            「押したら1件目が入る」と期待されうるので、明示的に止めておく
            （どれが1件目かは見た目で分かりにくく、事故のもとになる）
        */
        $('#emojiSearchInput').on('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
            }
        });


        /*
            パネルが閉じられたときの後片付け。

            開いたときに clearSearch() を呼ぶので、これが無くても表示は正しくなる。
            それでも置いてあるのは、**予約中の検索を、閉じた時点で止めるため**。
            止めないと、閉じたあとに1回だけ、見えていないパネルへ結果を描きに行く
        */
        function onPanelClosed() {
            cancelScheduledSearch();
        }


        return {
            onPanelOpened: onPanelOpened,
            onPanelClosed: onPanelClosed
        };
    })();


    /*
        === 発言に入れるものを選ぶパネルの、タブ（絵文字／スタンプ） ===

        【追加】
        入口のボタンを増やさず、パネルの中で切り替える形にした（LINE と同じ）。
        縦にボタンを3つ積むと、狭い画面で入力欄が潰れるため。

        【この中に閉じてあること】
        - どちらのタブが選ばれているか（見た目・aria・面の出し分け）
        - 最後に選んだタブを覚えること
        - 絵文字のデータを、いつ取りに行くか

        呼ぶ側（panels 配列）が書くのは onPanelOpened() / onPanelClosed() の1行だけで、
        中で何が起きるかを知らなくてよい（Sounds・IgnoreList と同じ形）
    */
    /*
        ============================================================
        === 画像を送る ===
        ============================================================

        E（画像アップロード）の回で作った。

        入口は2つある。どちらもここへ来る。

            ・発言に入れるものを選ぶパネルの「画像」タブ  … 部屋あて
            ・こっそりの窓の、送信ボタンの隣のボタン       … その相手へ

        【Sounds・IgnoreList・EmojiPicker と同じ形にしてある】
        呼ぶ側が書くのは ImageUpload.pick(宛先) の一行だけで、
        選ばせる・縮める・変換する・送る・断られたことを伝える、は全部この中にある。

        【流れ】

            1. ファイルを選んでもらう（<input type="file">）
            2. 大きすぎないか、その場で見る（読み込む前に断る）
            3. 絵として読めるか試す（読めない形式は、ここで分かる）
            4. GIF以外は、長辺を縮めて JPEG か PNG に変換する
            5. まだ大きければ、収まるまでもう一段小さくする
            6. サーバーへ送る
            7. サーバーが、発言として配信する（＝自分の画面にも普通に届く）

        【2〜5は「親切」であって、防御ではない】
        受け口（Chat/UploadImage.aspx）は開発者ツールから直接叩ける。
        大きさも形式も、サーバー側が別に見ている。
        ここでやっているのは「送る前に小さくして、通信を軽くする」ことだけである
    */
    var ImageUpload = (function () {

        /* 画像1枚の上限（バイト）。サーバー側の最後の砦と、同じ設定から来ている */
        var MAX_BYTES = vbChatSettings.imageMaxBytes;

        /* 縮小後の長辺（ピクセル） */
        var MAX_EDGE = vbChatSettings.imageMaxEdge;

        /* JPEGに変換するときの画質（0〜1） */
        var JPEG_QUALITY = vbChatSettings.imageJpegQuality;

        /*
            受け付ける形式。

            【サーバーが受け取る3つとは、わざと違う】
            ここは「利用者が選んでよい形式」で、GIF以外はすべて
            JPEG か PNG に変換してから送る。**BMP・ICO・WebP はサーバーまで届かない。**

            他社のチャットが「PNG, JPG, GIF, PNG, BMP, ICO 形式のみ対応しています」と
            出すのを見て、決定：それより多くの形式を受け付ける。
            BMPは無圧縮で巨大だが、変換すれば数百KBになる——
            **受け付けるだけでなく、向こうより軽く配れる**

            【accept は、ダイアログの初期の絞り込みでしかない】
            利用者は「すべてのファイル」に切り替えられるので、
            ここに書いた並びは検査ではない。検査は下の readImage（絵として読めるか）と、
            サーバー側の先頭バイトの判定である
        */
        var ACCEPT = 'image/png,image/jpeg,image/gif,image/bmp,image/x-icon,image/vnd.microsoft.icon,image/webp';

        /*
            アニメーションGIFだけは、縮小しない。

            【縮めると、アニメが1コマ目の静止画になる】
            canvas は「今そこに描かれている1コマ」しか写し取れない。
            動くものを送ったつもりが止まった絵になるのは、直しようのない裏切りである。
            そのぶん上限（MAX_BYTES）がそのまま効く
        */
        var TYPE_GIF = 'image/gif';

        /*
            収まらなかったときに、順に試す縮め方。

            【なぜ、決め打ちの1回では足りないのか】
            長辺と画質は、それぞれ別の設定である。運用者が長辺を4096・画質を100に
            したら、縮めた結果が上限を超えることがありうる。
            **超えたときに黙って断るのではなく、収まるまで小さくする**ほうが、
            利用者にとって親切である（設定どうしの噛み合わせを、
            利用者に押し付けない）
        */
        var RETRY_STEPS = [
            { edgeScale: 1.0, quality: 1.0 },
            { edgeScale: 1.0, quality: 0.75 },
            { edgeScale: 0.75, quality: 0.7 },
            { edgeScale: 0.5, quality: 0.65 },
            { edgeScale: 0.35, quality: 0.6 }
        ];

        /*
            === 「読み込むことすら試さない」大きさ ===

            上限（MAX_BYTES）とは、別の話である。

            【なぜ、上限をそのまま使ってはいけないのか】
            **BMPを受け付けている意味が、そこで消えるからである。**
            BMPは無圧縮なので、2000×1500の写真で 8.8MB になる。
            変換すれば数百KBに収まる——それが「受け付けるだけでなく、
            向こうより軽く配れる」という、この形式を入れた理由そのものだった。
            元のファイルの大きさで断ると、**変換すれば収まるものまで断ることになる**
            （実際にそうなっていたのを実測で見つけた）。

            【それでも、青天井にはしないこと】
            読み込むと、その端末は「幅×高さ×4バイト」を確保する。
            4000×3000なら48MBで、古いスマホでは固まりうる。
            **ここは「これ以上は、試すこと自体をやめる」という線**であって、
            送れる大きさの話ではない。だから文言も別にしてある

            【設定にしていない理由】
            運用者が調整したい値ではなく、端末を守るための安全弁である。
            上限（ImageMaxBytes）を10MBまで上げても、この線は動かなくてよい
        */
        var MAX_SOURCE_BYTES = 32 * 1024 * 1024;

        /* 送っている最中は、二重に送らせない */
        var busy = false;

        /*
            今どこへ送ろうとしているか。
            null なら部屋あて、文字列ならこっそりの相手の匿名ID
        */
        var target = null;

        /* ファイルを選ぶための <input>。1つだけ作って使い回す */
        var input = null;

        /*
            選んだ画像の確認

            「選んだら即送信」をやめ、**選んだあと、縮めた実物を面いっぱいに出してから送る**。
            要件の本体は「送る前に中止できること」。画像を大きく出すのは、
            選ぶ画面のサムネイルでは間違った写真かどうか分からないため。

              pending    … 確認を待っている画像（{ blob, width, height }）。無ければ null
              previewUrl … 確認に出している blob: のURL（片付けで必ず解放する）
              generation … 用意している途中で「やめる」「選び直す」が押されたら、
                           古い用意の結果を捨てるための番号
              uploading  … 「送る」を押して、サーバーへ送っている最中か（この間は取り消せない）
        */
        var pending = null;
        var previewUrl = null;
        var generation = 0;
        var uploading = false;

        /*
            断られた理由・待っていることを、正しい場所へ出す。

            【パネルが開いている間は、画像の面の中（#imageStatus）に出す】
            確認を出すようになって、断られたあともパネルは開いたままになった。
            入力欄の上の1行はパネルの下に隠れるので、見ている面の中に出す
            （スタンプの面が #stampStatus に出すのと同じ）。

            【パネルが閉じていたら、今までの場所】
            部屋あては入力欄の上の1行、こっそりはその窓の足もと
        */
        function say(text, isError) {

            if ($('#insertPanel').hasClass('is-open')) {
                $('#imageStatus')
                    .text(text || '')
                    .toggleClass('is-error', !!text && !!isError);
                return;
            }

            if (target === null) {
                if (text) {
                    showSendReject(text);
                } else {
                    hideSendReject();
                }
                return;
            }

            PrivateTalk.showReject(target, text);
        }

        /*
            <input type="file"> を用意する。

            【1つを使い回すこと】
            押すたびに作ると、古いものがDOMに残り続ける。

            【value を空に戻すこと】
            同じファイルをもう一度選んでも change が飛ぶようにするため。
            戻さないと「1回失敗したあと、同じ写真を選び直しても何も起きない」
        */
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

                input.value = '';

                if (file) {
                    handle(file);
                }
            });

            document.body.appendChild(input);

            return input;
        }

        /*
            ファイルを、絵として読む。

            【読めるかどうかが、受け付けるかどうかそのものである】
            読めなければ、その端末では描けない形式ということなので、
            送っても**誰の画面にも出ない**。ここで断るのが正しい。

            【blob: のURLは、必ず後片付けすること】
            解放しないと、選ぶたびにメモリへ溜まっていく
        */
        function readImage(file) {

            return new Promise(function (resolve, reject) {

                var url = URL.createObjectURL(file);
                var img = new Image();

                img.onload = function () {
                    URL.revokeObjectURL(url);
                    resolve(img);
                };

                img.onerror = function () {
                    URL.revokeObjectURL(url);
                    reject();
                };

                img.src = url;
            });
        }

        /*
            canvas へ描いて、JPEG か PNG のバイト列にする。

            【透明があれば PNG、無ければ JPEG】
            透明を JPEG にすると、抜けていたところが真っ黒になる。
            逆に、写真を PNG にすると何倍にも膨らむ。
            **実際に描いたものを見て決める**のがいちばん確かなので、
            透明な点が1つでもあるかを数えている

            【canvas が使えないときは、あきらめて断る】
            プライバシー保護の拡張機能が canvas を塞いでいることがある。
            そのときは「この画像は送れませんでした」で終わる——
            **黙って元のファイルを送る形にはしない**
            （縮んでいないものが、上限まるごと流れることになるため）
        */
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
                        if (data[i] < 255) {
                            hasAlpha = true;
                            break;
                        }
                    }
                } catch (e) {
                    /* 読めない（canvas を塞がれている）。安全側の PNG にしておく */
                    hasAlpha = true;
                }

                canvas.toBlob(function (blob) {

                    if (!blob) {
                        reject();
                        return;
                    }

                    resolve({ blob: blob, width: width, height: height });

                }, hasAlpha ? 'image/png' : 'image/jpeg', quality);
            });
        }

        /* 上限の文言。数字は設定から組み立てる（画面にべた書きしない） */
        function tooLargeText() {
            return '画像が大きすぎます（' +
                Math.round(MAX_BYTES / 1048576 * 10) / 10 + 'MBまで）。';
        }

        /* 選ばれた1枚を、送るところまで面倒を見る */
        function handle(file) {

            if (uploading) {
                return;
            }

            /*
                新しく用意し始める。前に出していた確認は片付ける
                （「選び直す」から来たとき。古いほうを送ってしまわないように）
            */
            clearConfirm();

            var gen = ++generation;

            busy = true;
            say('画像を用意しています…');

            /*
                === 読み込む前の、大きさの検査 ===

                【GIFと、それ以外で線が違う】
                GIFは**縮めずにそのまま送る**ので、元の大きさがそのまま上限に当たる。
                それ以外は変換されるので、当たるのは変換後の大きさである——
                ここで元の大きさを上限で見ると、**BMPを受け付けている意味が消える**
                （無圧縮なので、変換すれば収まるものでも元は数MBある）。

                それ以外に掛かるのは、上の MAX_SOURCE_BYTES
                （端末を守るための安全弁で、送れる大きさの話ではない）
            */
            if (file.type === TYPE_GIF) {

                if (file.size > MAX_BYTES) {
                    finish('アニメーションGIFは、そのままの大きさで送るため、' +
                        Math.round(MAX_BYTES / 1048576 * 10) / 10 + 'MBまでです。');
                    return;
                }

            } else if (file.size > MAX_SOURCE_BYTES) {

                finish('元の画像が大きすぎて、開けませんでした（' +
                    Math.round(MAX_SOURCE_BYTES / 1048576) + 'MBまで）。');
                return;
            }

            readImage(file).then(function (img) {

                /*
                    === アニメーションGIFは、そのまま送る ===

                    【縮めると、アニメが1コマ目の静止画になる】
                    canvas は「今そこに描かれている1コマ」しか写し取れない。
                    寸法だけは、読み込んだ絵から取れる
                */
                if (file.type === TYPE_GIF) {
                    prepared(gen, file, img.naturalWidth, img.naturalHeight);
                    return;
                }

                shrinkUntilFits(img, 0, gen);

            }, function () {

                finishFor(gen, 'この形式の画像は送れません。対応しているのは ' +
                    'PNG / JPG / GIF / BMP / ICO / WebP です。');
            });
        }

        /* 上限に収まるまで、順に小さくしていく */
        function shrinkUntilFits(img, step, gen) {

            if (step >= RETRY_STEPS.length) {
                finishFor(gen, tooLargeText());
                return;
            }

            var s = RETRY_STEPS[step];

            toBlob(img, Math.round(MAX_EDGE * s.edgeScale), JPEG_QUALITY * s.quality)
                .then(function (result) {

                    if (result.blob.size > MAX_BYTES) {
                        shrinkUntilFits(img, step + 1, gen);
                        return;
                    }

                    prepared(gen, result.blob, result.width, result.height);

                }, function () {

                    finishFor(gen, 'この画像は送れませんでした。別の画像でお試しください。');
                });
        }

        /*
            用意ができた。すぐには送らず、確認を出す

            【用意の途中で「やめる」「選び直す」が押されていたら、捨てる】
            縮めるのは非同期なので、結果が届く前に利用者が次の操作をしていることがある。
            番号（generation）が今のものと違えば、それは古い用意の結果である
        */
        function prepared(gen, blob, width, height) {

            if (gen !== generation) {
                return;
            }

            busy = false;
            pending = { blob: blob, width: width, height: height };
            showConfirm();
        }

        /* 用意の途中で断るとき。古い用意の結果なら、何も出さない */
        function finishFor(gen, text) {
            if (gen === generation) {
                finish(text);
            }
        }

        /*
            確認を出す。「画像を選ぶ」と案内の1行は隠し、送る実物を面いっぱいに出す。

            【出したことを、読み上げソフトにも伝える】（開発者の指定）
            見えている人には大きな絵で分かるが、それだけでは伝わらない。

            【焦点は「やめる」へ】
            確認ダイアログ（vbChatConfirm）と同じ決まり——最初の焦点は安全な側に置く。
            Enter を空押ししても送られない
        */
        function showConfirm() {

            if (previewUrl) {
                URL.revokeObjectURL(previewUrl);
            }

            previewUrl = URL.createObjectURL(pending.blob);

            $('#imageConfirmImg').attr('src', previewUrl);
            $('#imagePickBtn, #imagePickNotice').prop('hidden', true);
            $('#imageConfirm').prop('hidden', false).removeClass('is-sending');

            say('');

            /* 【先に出してから文言を入れる】aria-live は隠れている間の変更を拾わない */
            $('#imageConfirmAnnounce').text(
                '画像を選びました。送る画像を大きく表示しています。' +
                '「送る」で送れます。「選び直す」「やめる」もあります。');

            var cancelBtn = document.getElementById('imageConfirmCancel');
            if (cancelBtn) {
                cancelBtn.focus({ preventScroll: true });
            }
        }

        /*
            確認を片付けて、「画像を選ぶ」の状態へ戻す。

            【blob: のURLは、必ず解放すること】（readImage と同じ決まり）
        */
        function clearConfirm() {

            pending = null;

            if (previewUrl) {
                URL.revokeObjectURL(previewUrl);
                previewUrl = null;
            }

            $('#imageConfirm').prop('hidden', true).removeClass('is-sending');
            $('#imageConfirmImg').removeAttr('src');
            $('#imagePickBtn, #imagePickNotice').prop('hidden', false);
            $('#imageConfirmAnnounce').text('');
        }

        /*
            サーバーへ送る。

            【画像は multipart（FormData）で送る。寸法と宛先はヘッダーで渡す】

            【必読】生のバイト列で送ってはいけない。
            初版は画像のバイト列を本文へそのまま入れていたが、
            **本番のサーバーの手前にいる検査が、それを文字として採点して、
            1割ほどの画像を403で弾いていた**（開発機では1枚も弾かれない）。
            multipart のファイルの部分は、その採点から外れる
            （実測＝同じ40枚で、生のバイト列は4枚弾かれ、multipart は0枚）。
            詳しくは Chat/UploadImage.aspx.vb の先頭のコメント

            【返ってきた結果を、画面へ描かないこと】
            配信はサーバーがやる。自分の画面にも、他の人とまったく同じ経路で届く。
            ここで描くと、**送った本人だけ時刻の作り方が違う**ことになる
            （こっそりを自分にも配信している理由と、同じ話）

            【呼ぶのは、確認で「送る」が押されたときだけ】（confirmSend）
        */
        function upload(blob, width, height) {

            var headers = {
                'X-Image-Width': String(width),
                'X-Image-Height': String(height)
            };

            if (target !== null) {
                headers['X-Image-Recipient'] = target;
            }

            /*
                ファイルの部分の名前は "image"（受け口が Request.Files("image") で読む）。
                ファイル名はサーバーが使わないので、形式が分かる程度の仮の名前でよい
                （元のファイル名は、どこにも送らない）
            */
            var form = new FormData();
            form.append('image', blob, 'image.' + (EXTENSION_OF[blob.type] || 'jpg'));

            $.ajax({
                url: '/Chat/UploadImage.aspx',
                type: 'POST',
                data: form,
                headers: headers,

                /* 【2つとも false にすること】
                   processData を true のままだと、jQuery が FormData を文字列にしようとして壊す。
                   contentType を指定すると、ブラウザが付けるはずの
                   境界（boundary=…）が消えて、サーバーが中身を読めなくなる */
                processData: false,
                contentType: false,
                dataType: 'json'
            }).done(function (result) {

                if (result && result.ok) {

                    uploading = false;
                    finish('');
                    clearConfirm();

                    /*
                        入れるものを選ぶパネルが開いていたら、閉じる。
                        送った画像が、パネルの下に隠れてしまうため
                        （文章を送ったときと、まったく同じ手当て）
                    */
                    /*
                        【どちらあてでも閉じる】画像はパネルから選んで送るので、
                        送れた時点でパネルの用は済んでいる
                    */
                    if ($('#insertPanel').hasClass('is-open')) {
                        setOpenPanel(null);
                    }

                    return;
                }

                /*
                    断られた。理由が無いのは、サーバー側の都合
                    （部屋に居ない、など）で、利用者に直せることは無い
                */
                finish((result && result.reason) || '画像を送れませんでした。');

            }).fail(function (xhr) {

                var status = (xhr && xhr.status) || 0;

                debugLog('画像の送信に失敗しました（応答コード ' + status + '）');

                /*
                    【「通信の状態を」と言ってよいのは、本当に届かなかったときだけ】
                    応答コード 0 は、サーバーから何も返ってこなかった
                    （電波が切れた・つながらなかった）という意味である。

                    それ以外（403・500 など）は、**サーバーには届いたうえで断られている**。
                    ここを一緒くたにしていたせいで、本番の手前の検査に
                    弾かれていたことが「通信の問題」に見え、原因の見当が外れた
                    （実機テスト）。
                    応答コードも文言に添えておく——次に何か起きたとき、
                    画面の文字だけで、どちらの経路か見分けられるように
                */
                if (status === 0) {
                    finish('画像を送れませんでした。通信の状態をお確かめください。');
                } else {
                    finish('サーバーに受け付けてもらえませんでした（' + status + '）。' +
                        '別の画像でお試しください。');
                }
            });
        }

        /*
            multipart に付ける仮のファイル名の拡張子。
            送るのは、変換したあとの JPEG か PNG か、そのままの GIF だけである
        */
        var EXTENSION_OF = {
            'image/jpeg': 'jpg',
            'image/png': 'png',
            'image/gif': 'gif'
        };

        /*
            終わり。文言が空なら、待っていた表示を消すだけ。

            送っている最中に断られたときもここへ来る（upload の失敗）。
            【確認は残す】断られても、そのまま「送る」をやり直すか、「やめる」を選べる
        */
        function finish(text) {
            busy = false;
            uploading = false;
            $('#imageConfirm').removeClass('is-sending');
            say(text, !!text);
        }

        /* 確認で「送る」が押された */
        function confirmSend() {

            if (!pending || busy || uploading) {
                return;
            }

            busy = true;
            uploading = true;

            /* 送っている間は、3つのボタンを効かなくする（見た目は CSS の is-sending） */
            $('#imageConfirm').addClass('is-sending');
            say('画像を送っています…');

            upload(pending.blob, pending.width, pending.height);
        }

        /*
            やめる（確認を片付けて、「画像を選ぶ」の状態へ戻す）。

            【送っている最中は、取り消せない】もうサーバーへ向かっているため。
            【用意している最中なら、その結果を捨てる】番号を1つ進めれば、
            届いた結果は prepared が「古い」と判断して捨てる
        */
        function cancel() {

            if (uploading) {
                return;
            }

            generation++;
            busy = false;
            clearConfirm();
            say('');
        }

        /*
            確認の3つのボタン。
            【効かないときは、押されても何もしない】送っている最中（is-sending）
        */
        $('#imageConfirmSend').on('click', function () {
            confirmSend();
        });

        /* 選び直す。ファイルを選ぶ画面をすぐ開く（宛先はそのまま） */
        $('#imageConfirmRepick').on('click', function () {
            if (!uploading) {
                ensureInput().click();
            }
        });

        /* やめる。焦点は、また見えるようになった「画像を選ぶ」へ戻す */
        $('#imageConfirmCancel').on('click', function () {
            if (uploading) {
                return;
            }
            cancel();
            $('#imagePickBtn').trigger('focus');
        });

        return {

            /*
                画像を選んでもらう（送るのは、確認で「送る」が押されてから）

                recipientAnonId … 省略（または null）なら部屋あて。
                                  文字列なら、こっそりのその相手へ
            */
            pick: function (recipientAnonId) {

                if (busy) {
                    return;
                }

                target = recipientAnonId || null;

                ensureInput().click();
            },

            /* 送っている最中か（ボタンを押せなくするために使う） */
            isBusy: function () {
                return busy;
            },

            /* 確認を出しているか、用意している最中か（Esc を1段ずつ効かせるため） */
            hasConfirm: function () {
                return pending !== null || (busy && !uploading);
            },

            /* やめる（パネルを閉じた・タブを移った・宛先が変わった、から呼ばれる） */
            cancel: cancel
        };
    })();


    /*
        ============================================================
        === スタンプの一覧（発言に入れるものを選ぶパネルの「スタンプ」タブ） ===
        ============================================================

。

        【形】面の中に、セットのタブ（カエル／ヒヨコ）とグリッドを置く。
        入口のボタンは今までどおり1つ（決めた「入口のボタンは1つ」）。

        【押すと「選ばれた」状態になり、確認の帯から送る。入力欄には入れない】（変えた。
        G-2b は押したら即送信だった。中身は下の「選ばれた状態」のコメント）
        宛先はパネルを開いたボタンで決まる（InsertPanel.target）。
        本体の丸いボタンなら部屋あて、こっそりの窓のボタンならその相手。

        【送れたあとも、パネルは開いたまま】（開発者の判断）
        続けて送れるのが自然なため。

        【送った直後は、しばらく「送る」を休ませる】（開発者の指定）
        押しっぱなしの連打を、断られる前に止めて、見た目でも分かるようにする。
        休ませる長さは、サーバーの連投の間隔（SiteSettings の MessageIntervalSeconds）と
        同じにしてある——**同じならば、休みが明けたあとに押して断られることが無い**。
        なお「直前と同じ内容」の判定は、スタンプには掛けていない（サーバー側の判断）

        【サーバーにもDBにも、都度は聞かない】
        目次はページを開いたときに vbChatStamps で受け取ったものを使う。
        絵は ?v= 付きの静的ファイルなので、1回取ればブラウザのキャッシュから出る。
        **グリッドは、そのセットが初めて表に出たときに作る**（見ていないセットの絵は取らない）
    */
    var StampPicker = (function () {

        /*
            最後に選んだセットの覚え先（入れるものを選ぶタブの vbchat_insert_tab と同じ扱い。
            **設定でも状態でもなく、その端末に溜まる履歴**なので localStorage）
        */
        var STORAGE_KEY = 'vbchat_stamp_set';

        /*
            押したあと、ボタンを休ませる長さ（ミリ秒）。

            サーバーの連投の間隔と同じにする（理由は上）。
            ただし運用者が間隔を0（制限なし）にしても、押しっぱなしの連打だけは止めたいので、
            下限を持たせてある
        */
        var COOLDOWN_MIN_MS = 500;
        var COOLDOWN_MS = Math.max(
            (typeof vbChatSettings.messageIntervalMs === 'number') ? vbChatSettings.messageIntervalMs : 3000,
            COOLDOWN_MIN_MS);

        var SETS = STAMPS.sets;

        /* 今表に出ているセット（null＝まだ決めていない） */
        var currentSet = null;

        /* 作り終えたグリッド（セットの名前 → true） */
        var built = {};

        /* 休ませている最中か */
        var cooling = false;

        function isKnownSet(code) {
            for (var i = 0; i < SETS.length; i++) {
                if (SETS[i].code === code) {
                    return true;
                }
            }
            return false;
        }

        /* localStorage は読むだけでも例外を投げることがある（InsertPanel と同じ用心） */
        function readSavedSet() {
            try {
                var saved = window.localStorage.getItem(STORAGE_KEY);
                return isKnownSet(saved) ? saved : null;
            } catch (e) {
                return null;
            }
        }

        function saveSet(code) {
            try {
                window.localStorage.setItem(STORAGE_KEY, code);
            } catch (e) {
                /* 覚えられなくても、今の操作は成り立つ */
            }
        }

        /*
            断られた理由などを、この面の中の1行に出す（空文字なら消す）。

            【入力欄の上の1行（#sendRejectText）には出さない】
            パネルが入力行の上に覆いかぶさっているので、あの行は見えない。
            送ったあともパネルは開いたままなので、利用者が見ているここに出す
        */
        function say(text, isError) {
            $('#stampStatus')
                .text(text || '')
                .toggleClass('is-error', !!text && !!isError);
        }

        /*
            セットのタブを作る（1回だけ）。
            並びは目次の配列の順そのもの（＝StampSets.SortOrder。サーバーが並べてある）
        */
        var tabsBuilt = false;

        function buildTabs() {

            if (tabsBuilt) {
                return;
            }

            tabsBuilt = true;

            var $tabs = $('#stampSetTabs');

            if (SETS.length === 0) {

                /*
                    目次が空。DBが読めなかった（1分後にサーバーが読み直す）か、
                    まだ1つもセットが無い。タブもグリッドも出さず、1行だけ出す
                */
                $tabs.prop('hidden', true);
                say('スタンプを読み込めませんでした。しばらくしてから、ページを読み込み直してください。', true);
                return;
            }

            SETS.forEach(function (set) {

                /* 名前は英小文字・数字・ハイフンなので、そのまま id に使える（サーバーで確かめてある） */
                $('<button type="button" class="stamp-set-tab" role="tab">')
                    .attr('id', 'stampSetTab-' + set.code)
                    .attr('data-stamp-set', set.code)
                    .attr('aria-controls', 'stampGrid-' + set.code)
                    .attr('aria-selected', 'false')
                    .text(set.name)
                    .appendTo($tabs);
            });
        }

        /*
            そのセットのグリッドを作る（1回だけ）。

            【ここで初めて <img> ができる＝絵を取りに行く】
            だから、タブが表に出たときに呼ぶ。
            loading="lazy" は付けない——パネルの中はスクロールの外側が
            隠れていることが多く、遅延にすると開いた瞬間に空欄が並ぶため
            （15個で約37KB。一度取れば、以後はキャッシュから出る）
        */
        function buildGrid(set) {

            if (built[set.code]) {
                return;
            }

            built[set.code] = true;

            var html = '<div class="stamp-grid" role="tabpanel"' +
                ' id="stampGrid-' + escapeAttr(set.code) + '"' +
                ' aria-labelledby="stampSetTab-' + escapeAttr(set.code) + '"' +
                ' data-stamp-set="' + escapeAttr(set.code) + '" hidden>';

            set.stamps.forEach(function (code) {

                var entry = lookupStamp(code);

                if (!entry) {
                    return;
                }

                html += '<button type="button" class="stamp-choice"' +
                    ' data-stamp-code="' + escapeAttr(code) + '"' +
                    ' title="' + escapeAttr(entry.label) + '"' +
                    ' aria-label="' + escapeAttr(entry.label) + '">' +
                    '<img class="stamp-choice-img" src="' + escapeAttr(entry.src) + '" alt="" />' +
                    '</button>';
            });

            html += '</div>';

            $('#stampGridHost').append(html);
        }

        function findSet(code) {
            for (var i = 0; i < SETS.length; i++) {
                if (SETS[i].code === code) {
                    return SETS[i];
                }
            }
            return null;
        }

        /*
            セットを選ぶ（タブの見た目・読み上げ・グリッドの出し分けを、まとめて行う）

            【覚えるのは、ここではない】
            覚えるのは利用者がタブを押したときだけ。受け取ったスタンプを押して
            開いたときにもここを通るが、そちらは利用者がセットを選んだわけではない
        */
        function selectSet(code) {

            var set = findSet(code) || SETS[0];

            if (!set) {
                return;
            }

            currentSet = set.code;
            buildGrid(set);

            $('.stamp-set-tab').each(function () {
                $(this).attr('aria-selected',
                    $(this).attr('data-stamp-set') === set.code ? 'true' : 'false');
            });

            $('.stamp-grid').each(function () {
                $(this).prop('hidden', $(this).attr('data-stamp-set') !== set.code);
            });
        }

        /*
            ============================================================
            選ばれた状態
            ============================================================

            「押したら即送信」をやめ、**1回押すと「選ばれた」状態になる**。
            面の下に確認の帯（大きめの絵・名前・「送る」「やめる」）が出て、
            「送る」か、選んだスタンプをもう一度押すと送れる。
            受け取ったスタンプを押して開いたときも、同じ「選ばれた」状態になる
            （「太枠で光る → もう一度押すと送れる」と同じ2段に寄せた）。

            【要件の本体は「送る前に中止できること」】（開発者が実機を見て、即送信を覆した）

            【選ばれた状態が解除される出口】（開発者と決めた。**選んだまま忘れて送る、を起こさない**）
              ・「やめる」
              ・送れたとき
              ・セットのタブを切り替えたとき（別のセットへ移ったら、選んでいたものは消える）
              ・絵文字・画像のタブへ移ったとき
              ・パネルを閉じたとき（×・外を押す・Esc）
              ・宛先が変わったとき（部屋あて ⇔ こっそりの窓あて。違う相手へ送らないため）
              ・Esc は1段ずつ——選ばれていればまず解除だけ、もう一度 Esc でパネルを閉じる
            **断られたとき（連投の間隔など）は解除しない**（そのまま「送る」をやり直せる）
        */
        var selectedCode = null;

        /*
            === こっそりの窓あてにスタンプを送れたあと、パネルを開いたままにするか===

            true  … 全面の窓（ログを押し上げている）なら、閉じない。部屋あてと同じく続けて送れる
            false … 形に戻る（こっそりの窓あてで、パネルが窓に重なっていたら、1個送るたびに閉じる）

            【止めるときは、この1行】実機で「閉じたほうがよい」となったときのため（開発者の指定）。
            HIDE_INPUT_ON_MEDIA_TABS と同じやり方。どちらでも、PC で窓の隣に入りきらずに重なったときは閉じる
            （sendSelected の項）
        */
        var KEEP_PANEL_OPEN_AFTER_PRIVATE_STAMP = true;

        /* 選んでいるスタンプが無い状態へ戻す（上の出口は、すべてここを通る） */
        function clearSelection() {

            selectedCode = null;

            $('#stampGridHost .stamp-choice.is-highlighted')
                .removeClass('is-highlighted')
                .attr('aria-pressed', 'false');

            $('#stampConfirm').prop('hidden', true);

            /* 読み上げの文言も空に戻す（次に選んだとき、同じ文言でも必ず読まれるように） */
            $('#stampConfirmAnnounce').text('');
        }

        /* グリッドの中だけをスクロールして、そのボタンを見える位置へ */
        function scrollChoiceIntoView($btn) {

            /*
                【scrollIntoView は使わない】
                あれはグリッドの外側（チャットログやページそのもの）まで
                動かしうる。パネルは浮いているので、外側が動くと読んでいた位置が飛ぶ。

                【offsetTop は、グリッドの入れ物からの距離である】
                入れ物（.stamp-grid-host）を position: relative にしてあるので、
                ボタンの offsetParent は入れ物になる（CSS側にも同じ注意を書いてある）
            */
            var host = document.getElementById('stampGridHost');
            var btnTop = $btn[0].offsetTop;
            var btnBottom = btnTop + $btn[0].offsetHeight;

            if (btnTop < host.scrollTop) {
                host.scrollTop = btnTop;
            } else if (btnBottom > host.scrollTop + host.clientHeight) {
                host.scrollTop = btnBottom - host.clientHeight;
            }
        }

        /*
            そのスタンプを「選ばれた」状態にして、確認の帯を出す。

            【帯が出てから、見える位置へスクロールする】
            帯が出るとグリッドの入れ物が縮むので、先にスクロールすると
            選んだスタンプが帯の下に隠れることがある

            【読み上げソフトにも、選んだことを伝える】（開発者の指定）
            見えている人には帯と大きな絵で分かるが、それだけでは伝わらない。
            焦点はグリッドのそのスタンプに残す——Enter をもう一度押せば送れる
        */
        function select(code) {

            var entry = lookupStamp(code);
            var $btn = choiceOf(code);

            if (!entry || $btn.length === 0) {
                return;
            }

            clearSelection();

            selectedCode = code;
            $btn.addClass('is-highlighted').attr('aria-pressed', 'true');

            $('#stampConfirmImg').attr('src', entry.src);
            $('#stampConfirmLabel').text(entry.label);
            $('#stampConfirm').prop('hidden', false);

            scrollChoiceIntoView($btn);

            /* 【先に帯を出してから文言を入れる】aria-live は隠れている間の変更を拾わない */
            $('#stampConfirmAnnounce').text(
                '「' + entry.label + '」を選びました。「送る」を押すか、もう一度押すと送れます。' +
                '「やめる」で取り消せます。');

            say('');
        }

        /*
            ボタンを休ませる／起こす。

            【休ませるのは「送る」ことだけ】（変えた）
            グリッドごと休ませていたが、押しても送られなくなったので、
            次のスタンプを「選ぶ」ことまで止める理由が無くなった。
            休ませている間は「送る」と「選んだものをもう一度押す」だけが効かない。

            【disabled は使わない】
            disabled にすると、キーボードで選んでいたボタンから焦点が外れて
            居場所を見失う。aria-disabled と見た目だけを変え、押されても無視する
        */
        function setCooling(on) {
            cooling = on;
            $('#insertPaneStamp').toggleClass('is-cooling', on);
            $('#stampConfirmSend').attr('aria-disabled', on ? 'true' : 'false');
        }

        /*
            送る（選ばれているスタンプを）。

            【休みが明けるのは、「時間が過ぎた」と「返事が来た」の両方がそろったとき】
            返事より先に時間が過ぎても、返事が来るまでは押させない
            （二重に送らないため）。返事が先でも、時間が過ぎるまでは押させない
            （サーバーの連投の間隔に先回りするため）
        */
        function sendSelected() {

            var code = selectedCode;

            if (!code || cooling) {
                return;
            }

            if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                say('サーバーとの接続が切れています。', true);
                return;
            }

            var to = InsertPanel.target();

            /* こっそりの相手が退室していたら、送れない（窓の送信ボタンと同じ判断） */
            if (to !== null && !PrivateTalk.inputOf(to)) {
                say('相手が退室したため、送信できません。', true);
                return;
            }

            var waitingTime = true;
            var waitingReply = true;

            function wake() {
                if (!waitingTime && !waitingReply) {
                    setCooling(false);
                }
            }

            setCooling(true);

            setTimeout(function () {
                waitingTime = false;
                wake();
            }, COOLDOWN_MS);

            chatHub.server.sendStamp(to, code).done(function (result) {

                /* 理由の無い「受け付けない」は、サーバー側の都合（本体の発言と同じ扱い） */
                if (!result || (!result.accepted && !result.rejectReason)) {
                    debugLog('サーバー側がスタンプを受け付けなかった（理由なし）');
                    showServerTroubleBar();
                    return;
                }

                if (result.accepted) {

                    /*
                        送れた。パネルは閉じない（開発者の判断）。選ばれた状態だけ解除する。

                        【焦点を、グリッドのそのスタンプへ戻す】
                        「送る」を押した人は、帯が消えると焦点の置き場所を失う
                    */
                    var focusWasInConfirm = $.contains(document.getElementById('stampConfirm'),
                        document.activeElement);

                    say('');
                    clearSelection();

                    /*
                        【こっそりの窓あてで、パネルがその窓に重なっていて、しかも押し上げていないときだけ閉じる】

                        G-2dは「重なっていたら閉じる」だった。閉じる理由は
                        「こっそりの窓あては押し上げないので、送ったものがパネルの下に隠れて見えない」。
、狭い画面の窓は全面になり、パネルのぶん窓のログを押し上げるように
                        なった（InsertPanel.updateLogPush）。**送ったスタンプはパネルの上に見えるので、
                        閉じずに続けて送れる**——部屋あてと同じ動きに揃う（開発者の指定）。

                        【まだ閉じるのは、PC で窓の隣に入りきらずに重なったときだけ】
                        窓（既定260px）がパネル（約420px）の裏にまるごと隠れるので、押し上げても見えない。
                        PC で窓の隣に出ているときは見えているので、今までどおり開いたまま。
                        焦点は、パネルを開いた窓のボタンへ戻す（入力欄へ戻すと、スマホでキーボードが開く）
                    */
                    if (to !== null && InsertPanel.coversTargetWindow() &&
                        !(KEEP_PANEL_OPEN_AFTER_PRIVATE_STAMP && InsertPanel.pushesTargetLog())) {
                        var returnTo = InsertPanel.returnFocusEl();
                        InsertPanel.closeIfTarget(to);
                        if (returnTo) {
                            returnTo.focus({ preventScroll: true });
                        }
                        return;
                    }

                    if (focusWasInConfirm) {
                        var $btn = choiceOf(code);
                        if ($btn.length > 0) {
                            $btn[0].focus({ preventScroll: true });
                        }
                    }
                    return;
                }

                /* 断られた。選ばれた状態は残す（そのまま「送る」をやり直せる） */
                say(result.rejectReason, true);

            }).fail(function () {

                /* 送っている最中に接続が切れたなら、切断ハンドラに任せる（本体の発言と同じ） */
                if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                    return;
                }

                debugLog('スタンプの送信に失敗しました');
                say('送信できませんでした。', true);

            }).always(function () {
                waitingReply = false;
                wake();
            });
        }

        /* グリッドの中で、そのスタンプのボタン */
        function choiceOf(code) {
            return $('#stampGridHost .stamp-choice').filter(function () {
                return $(this).attr('data-stamp-code') === code;
            }).first();
        }

        /*
            スタンプを押した。
            選ばれていなければ選ぶ。**選ばれているものをもう一度押したら送る**
        */
        $(document).on('click', '.stamp-choice', function () {

            var code = $(this).attr('data-stamp-code');

            if (code === selectedCode) {
                sendSelected();
                return;
            }

            select(code);
        });

        $(document).on('click', '#stampConfirmSend', function () {
            sendSelected();
        });

        /* やめる。焦点は、選んでいたスタンプへ戻す（帯と一緒にボタンが消えるため） */
        $(document).on('click', '#stampConfirmCancel', function () {

            var $btn = selectedCode ? choiceOf(selectedCode) : $();

            clearSelection();
            say('');

            if ($btn.length > 0) {
                $btn[0].focus({ preventScroll: true });
            }
        });

        /* セットのタブを押した。ここだけが「覚える」。選んでいたものは解除する */
        $(document).on('click', '.stamp-set-tab', function () {

            var code = $(this).attr('data-stamp-set');

            if (code === currentSet) {
                return;
            }

            clearSelection();
            selectSet(code);
            saveSet(code);
        });

        /* 左右の矢印キーでも移れる（入れるもののタブと同じ。role="tablist" と名乗った以上） */
        $(document).on('keydown', '.stamp-set-tab', function (e) {

            if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') {
                return;
            }

            e.preventDefault();

            var tabs = $('.stamp-set-tab');
            var index = tabs.index(this);
            var next = (e.key === 'ArrowRight')
                ? (index + 1) % tabs.length
                : (index - 1 + tabs.length) % tabs.length;

            clearSelection();
            selectSet(tabs.eq(next).attr('data-stamp-set'));
            saveSet(currentSet);
            tabs.eq(next).trigger('focus');
        });

        return {

            /* スタンプの面が表に出た（パネルを開いたとき・タブを切り替えたとき） */
            onPaneShown: function () {
                buildTabs();
                if (SETS.length > 0) {
                    selectSet(currentSet || readSavedSet() || SETS[0].code);
                }
            },

            /* パネルが閉じた・別のタブへ移った。選ばれた状態と、出していた理由を片付ける */
            onPaneHidden: function () {
                clearSelection();
                if (SETS.length > 0) {
                    say('');
                }
            },

            /* 宛先が変わった（違う相手へ送らないよう、選ばれた状態を解除する） */
            onTargetChanged: function () {
                clearSelection();
            },

            /* 選ばれているスタンプがあるか（Esc を1段ずつ効かせるため） */
            hasSelection: function () {
                return selectedCode !== null;
            },

            /* Esc で解除したとき。「やめる」と同じ（焦点は選んでいたスタンプへ戻す） */
            cancelSelection: function () {
                $('#stampConfirmCancel').trigger('click');
            },

            /*
                受け取ったスタンプを押したとき。そのスタンプのセットを出し、「選ばれた」状態にする。

                【呼ぶ前に、スタンプの面を表に出しておくこと】（InsertPanel.showStamp が面倒を見る）

                【焦点も、そのスタンプへ移す】
                キーボードで来た人が、そのまま Enter で送れるようにするため。
                スマホでは、入力欄から焦点が外れてソフトキーボードが閉じる
                （パネルの入る場所が空く。こっそりの窓のボタンが blur するのと同じ結果）
            */
            reveal: function (code) {

                var setCode = Object.prototype.hasOwnProperty.call(SENDABLE_STAMP_SET, code)
                    ? SENDABLE_STAMP_SET[code]
                    : null;

                if (!setCode) {
                    return;
                }

                clearSelection();
                selectSet(setCode);
                select(code);

                var $btn = choiceOf(code);

                if ($btn.length > 0) {
                    $btn[0].focus({ preventScroll: true });
                }
            },

            /* そのスタンプを、今パネルから送れるか（受け取ったスタンプを押せるか） */
            isSendable: function (code) {
                return Object.prototype.hasOwnProperty.call(SENDABLE_STAMP_SET, code);
            }
        };
    })();


    var InsertPanel = (function () {

        /*
            最後に選んだタブの覚え先。

            【なぜ localStorage なのか（Cookie でも sessionStorage でもなく）】
            「ブラウザに何かを覚えさせるときの、置き場所」の表に当てると、
            これは**設定でも状態でもなく、その端末に溜まる履歴**である。
            Emoji Mart の「よく使う絵文字」と同じ軸なので、同じ場所に置く。
            **サーバーは知る必要が無い**ので、Cookie に入れると毎回送るぶんが無駄になる。

            【キー名】emoji-mart.frequently / emoji-mart.last とぶつからないよう、
            このサイトの他のキー（vbchat_leave_reason など）と同じ付け方にしてある
        */
        var STORAGE_KEY = 'vbchat_insert_tab';

        /* 覚えていた値が読めないときの行き先。「読めない値は既定へ」の決まりどおり */
        var DEFAULT_TAB = 'emoji';

        /*
            白リスト。ここに無い名前は、すべて既定へ落とす。

            【DOMから作っていること】
            「画像」のタブは、その部屋で画像が許されているときだけ出る
            （ページ側が <% If IsImageUploadEnabled Then %> で出し分けている）。
            並びをここへ書き写すと、許されていない部屋で
            **覚えていた「image」がそのまま通り、面の無いタブが選ばれる**。

            DOMから拾えば、出ていないタブは白リストにも入らないので、
            覚えていた値は自然に既定（絵文字）へ落ちる。
            **これは利用者の言い値ではなく、サーバーが出したHTMLである**ので、
            白リストとしての値打ちは変わらない
        */
        /*
            【、読むたびに拾う】「画像」のタブが、部屋に居ながら出たり隠れたりするようになった
            （管理人が部屋の設定で切り替える）。**隠れているタブは入れない**＝隠れた「画像」を覚えていても既定へ落ちる
        */
        function tabNames() {
            return $('.insert-tab:not([hidden])').map(function () {
                return $(this).attr('data-insert-tab');
            }).get();
        }

        /* 今どちらが選ばれているか。null は「まだ一度も決めていない」 */
        var currentTab = null;

        /*
            === 今どこへ入れるか ===

            null   … 本体の入力欄（部屋あて）
            文字列 … こっそりの窓の相手の匿名ID

            【パネルは1つしか無い】
            本体の丸いボタンからも、こっそりの窓のボタンからも、同じパネルが開く
            （窓は既定260pxで、パネルの面は346pxあるので、窓の中には作れない）。
            **開いたボタンが、入れる先を決める。**
            絵文字は insertEmoji が、画像は ImageUpload.pick が、ここを見る

            【閉じたら、本体あてへ戻す】
            次に本体の丸いボタンで開いたとき、前のこっそりの宛先が残っていると困るため
        */
        var target = null;

        /*
            見本に出す、入れた文の末尾の長さの上限（書記素ではなく、コードポイントで数える）。
            実際に出るのは、帯の幅に収まるところまで（refreshPreview が頭を削って詰める）。
            名前を外して帯をまるごと使えるようになったので、16 から広げた
        */
        var PREVIEW_MAX_CHARS = 40;

        /*
            打ちかけの文が空のときに、帯に出す文言。
            窓の入力欄の placeholder と同じにしてある（帯が「窓の入力欄の写し」だと伝わるように）
        */
        var PREVIEW_EMPTY_TEXT = 'こっそり送る';

        function isOpen() {
            return $('#insertPanel').hasClass('is-open');
        }

        /*
            === 「◯◯さんへ こっそり」の帯 ===

            【なぜ要るのか】パネルは1つしか無いので、開いた人が
            **「今どっち宛てか」を見て分からないといけない**。

            【こっそりのときだけ出す】本体あてが「いつもの形」なので、
            そちらに帯を出しても情報が増えない。
            **いつもと違うときだけ、いつもと違う見た目にする。**

            【色だけで伝えない】窓と同じ暗い地にしてあるが、
            「◯◯さんへ こっそり」と**言葉で**書いてある

            【入れた文の見本も出す】
            パネルは入力行の上に覆いかぶさるので、**こっそりの窓がパネルの下に
            隠れることが多い**（広い画面では窓の既定の置き場所がちょうど重なり、
            狭い画面では窓が画面の下半分を占める）。
            絵文字を入れても、それが入ったのかが見えない。
            そこで、窓の入力欄の**末尾だけ**をここに写す。
            本体あてのときは、入力欄がパネルのすぐ下に見えているので要らない
        */
        function renderTarget() {

            var $row = $('#insertTargetRow');

            if (target === null) {
                $row.prop('hidden', true);
                $('#insertPanel').attr('aria-label', '絵文字・スタンプを選ぶ');
                positionPanel();

                /* 開いた・閉じた・宛先が部屋に変わった、のどれでもここを通る */
                updateLogPush();
                return;
            }

            var name = PrivateTalk.nicknameOf(target) || '相手';

            /*
                【帯に宛先の名前は出さない】（実機を見て外した）
                理由：その人とのこっそりの窓から開いた絵文字などのパネルなので。
                窓のボタンから開いている以上、宛先は自明だった。
                帯に残すのは、**打ちかけの文の見本だけ**である

                【読み上げソフトには、宛先を伝えたままにしてある】
                目で見ている人は「どの窓のボタンを押したか」を見ているが、
                読み上げでは、パネルが開いた瞬間に名前が読まれるのが手がかりになる
            */
            $row.prop('hidden', false);

            $('#insertPanel').attr('aria-label', '絵文字・スタンプを選ぶ（' + name + 'さんへ こっそり）');

            refreshPreview();

            /* 帯の有無で高さが変わるので、中身を入れ終わってから置く */
            positionPanel();

            /* こっそりの窓あてに切り替わったら、押し上げる先を測り直す（部屋のぶんは戻し、全面なら窓を押し上げる） */
            updateLogPush();
        }

        /* 窓の入力欄の末尾を、帯の中へ写す */
        function refreshPreview() {

            if (target === null) {
                return;
            }

            var input = PrivateTalk.inputOf(target);
            var text = input ? input.value.replace(/\s+/g, ' ') : '';

            /*
                【コードポイントで切ること】
                文字列の slice（UTF-16の単位）で切ると、絵文字の真ん中で割れて
                「�」が出る。Array.from はサロゲートペアを1つとして数える
                （ZWJ結合の絵文字は分かれうるが、見本なので割り切る）
            */
            var chars = Array.from(text);
            var el = document.getElementById('insertTargetPreview');

            /* 空なら、窓の入力欄と同じ placeholder を薄く出す（帯の高さも変わらない） */
            if (chars.length === 0) {
                el.textContent = PREVIEW_EMPTY_TEXT;
                el.classList.add('is-empty');
                return;
            }

            el.classList.remove('is-empty');

            /*
                【見せるのは「末尾」。頭のほうを削って収める】（実測で直した）

                最初は CSS の text-overflow: ellipsis に任せていたが、あれは**末尾を**「…」で切る。
                絵文字はたいてい末尾に入るので、**今入れた絵文字だけが見えない**
                （375pxで「今日は雨ですね、傘ありま…」となり、入れた🐈が隠れた）。

                そこで、実際の幅に収まるまで頭から1文字ずつ削る。
                長さの上限（PREVIEW_MAX_CHARS）から始めるので、回るのは多くても十数回である。
                帯が見えていない（hidden）ときは幅が測れないので、上限で切るだけにする
            */
            var n = Math.min(chars.length, PREVIEW_MAX_CHARS);

            for (; n >= 0; n--) {

                el.textContent = (n < chars.length ? '…' : '') + chars.slice(chars.length - n).join('');

                if (el.clientWidth === 0 || el.scrollWidth <= el.clientWidth) {
                    break;
                }
            }
        }

        /*
            === こっそりの窓から開いたときは、その窓の隣に出す（広い画面だけ） ===

            指摘：こっそりの窓の絵文字のボタンを押したのに、本体の方の絵文字のパネルが
            開いていた（以前からの不具合）。

            【パネルは1つのまま。「どこに出すか」だけを変える】
            本体あてのときは、今までどおり本体の入力行の上。
            こっそりの窓あてのときだけ is-beside-window を付けて position: fixed にし、
            窓の横の座標を --ip-x / --ip-y に入れる（窓の --pt-x / --pt-y と同じやり方）。

            【横は、右が先・入らなければ左】
            窓の右にパネルがまるごと入るなら右、入らなければ左。
            **どちらにも入らなければ、広いほうの端へ寄せる**（そのときだけ窓と少し重なる）。
            窓の位置を表示領域へ丸めるのと同じ考え方で、**画面の外へは出さない**

            【縦は、窓の下端に揃える】
            パネル（面346px ＋ タブ ＋ 余白で約420px）は窓（既定260px）より背が高い。
            窓は下寄せで出るので、下端を揃えると上へ伸びる形になる。
            **上下どちらかにはみ出すなら、表示領域の中へ押し戻す**

            【狭い画面では、今までどおり】窓が覆いかぶさる形なので、隣という場所が無い。
            本体の入力行の上に出し、打ちかけの文の見本の帯で中身を見せる

            【窓を動かしたときに、追いかけない】
            窓を掴んだ時点で「パネルの外を押した」ことになり、パネルは閉じる。
            追いかける先が無いので、開いたとき・宛先を切り替えたとき・画面の大きさが
            変わったときだけ置き直せば足りる
        */
        var BESIDE_GAP_PX = 8;
        var VIEW_MARGIN_PX = 8;

        function positionPanel() {

            var panel = document.getElementById('insertPanel');

            var win = (isOpen() && target !== null && !PrivateTalk.isNarrow())
                ? PrivateTalk.windowOf(target)
                : null;

            if (!win) {
                panel.classList.remove('is-beside-window');
                panel.classList.remove('is-covering-window');
                panel.style.removeProperty('--ip-x');
                panel.style.removeProperty('--ip-y');
                return;
            }

            /* 先に切り替えてから測る（fixed になったあとの大きさが要るため） */
            panel.classList.add('is-beside-window');

            var w = win.getBoundingClientRect();
            var pw = panel.offsetWidth;
            var ph = panel.offsetHeight;
            var vw = document.documentElement.clientWidth;
            var vh = document.documentElement.clientHeight;

            var rightX = w.right + BESIDE_GAP_PX;
            var leftX = w.left - BESIDE_GAP_PX - pw;
            var x;

            if (rightX + pw <= vw - VIEW_MARGIN_PX) {
                x = rightX;
            } else if (leftX >= VIEW_MARGIN_PX) {
                x = leftX;
            } else {
                /* どちらにも入らない。広いほうの端へ寄せる（窓と少し重なる） */
                x = (vw - w.right >= w.left) ? vw - VIEW_MARGIN_PX - pw : VIEW_MARGIN_PX;
            }

            /*
                【左右どちらにも入らず、窓に重なってしまうときだけ、見本の帯を出す】
                たとえば 720px の画面で窓を真ん中へ動かすと、パネルは窓の入力欄の上に
                かかる（上下にも、420px のパネルが入る場所は無い）。
                帯は「パネルが窓の入力欄を覆う」ときのための手当てなので、
                条件をそれに揃えてある（狭い画面では、いつも覆うので常に出る）。
                帯の分だけ背が伸びるので、付けてから高さを測り直す
            */
            var covering = (x < w.right) && (x + pw > w.left);
            panel.classList.toggle('is-covering-window', covering);

            if (covering) {
                ph = panel.offsetHeight;
            }

            var y = w.bottom - ph;
            y = Math.min(y, vh - VIEW_MARGIN_PX - ph);
            y = Math.max(y, VIEW_MARGIN_PX);

            panel.style.setProperty('--ip-x', Math.round(x) + 'px');
            panel.style.setProperty('--ip-y', Math.round(y) + 'px');

            /* 帯が出たなら、中身（打ちかけの文）を幅に合わせて詰め直す */
            if (covering) {
                refreshPreview();
            }
        }

        /*
            === 部屋あてに開いている間は、ログをパネルのぶん押し上げる ===

            理由：パネルが自分の発言の上に重なるので、スタンプを続けて送ると
            **自分が何を送ったか見えない**。「パネルを左に出す」案は相手の発言が隠れるので、押し上げにした。

            【どのタブでも同じ】パネルは1枚で高さも固定なので、タブを切り替えるたびに
            ログが上下するのを避けるため（開発者の判断）。

            【こっそりの窓あてのときは、全面の窓のログを押し上げる】
            狭い画面では、窓を開いている間は全面になり、パネルが窓の下半分（入力行を含む）を覆う。
            そこで**部屋あてとまったく同じ仕組みで、窓のログを押し上げる**（開発者の指定）。
            送ったスタンプが窓のログのいちばん下に出て、パネルの上に見える。
            窓の入力行はパネルの下に隠れるが、打ちかけの文はパネルの見本の帯に写っている（refreshPreview）。
            広い画面では押し上げない——パネルは窓の隣に出てログに重ならず、入りきらずに重なるときは
            窓（既定260px）がパネル（約420px）の裏にまるごと隠れるので、押し上げても見えない。

            【やり方】ログの枠の大きさは変えず、下の余白だけを
            「パネルがログに重なっている高さ」ぶん伸ばす。いちばん下の発言が
            パネルの上に来るところまでスクロールできるようになる。
            余白の変数は、部屋が --log-push（.chat-log-wrapper に入れる）、窓が --pt-log-push（窓のログに直接入れる）。
            **名前を分けてあるのは、部屋の変数が窓へ受け継がれてしまうため**（CSS 側の .private-talk-log に理由）

            【いちばん下を見ていた人だけ、いちばん下へ送る】
            開いた瞬間も閉じた瞬間も、それまでいちばん下（最新）を見ていたなら最新が見えたまま。
            過去を読み返していた人は、スクロール位置を動かさない（読んでいた場所が飛ばない）。
            測るのは余白を変える「前」（行を足す前に測るのと、同じ決まり）
        */

        /* 今どこを押し上げているか（{ holder, prop, log, room }。押し上げていなければ null） */
        var pushed = null;

        function updateLogPush() {

            var panel = document.getElementById('insertPanel');

            /* 前回縮めたぶんは、いったん戻してから測り直す（画面の大きさが変わったときのため） */
            panel.style.removeProperty('--insert-pane-height');
            panel.classList.remove('is-shrunk');

            /* 押し上げる先を決める */
            var next = null;

            if (isOpen() && target === null) {

                var roomLog = document.getElementById('chatLog');

                next = { holder: document.querySelector('.chat-log-wrapper'),
                         prop: '--log-push', log: roomLog, room: true };

            } else if (isOpen() && target !== null && PrivateTalk.isFullScreen()) {

                var windowLog = PrivateTalk.logOf(target);

                if (windowLog) {
                    next = { holder: windowLog, prop: '--pt-log-push', log: windowLog, room: false };
                }
            }

            var push = 0;

            if (next) {

                fitPaneToLeaveLog(panel, next.log);

                var panelTop = panel.getBoundingClientRect().top;
                push = Math.max(0, Math.round(next.log.getBoundingClientRect().bottom - panelTop));
            }

            /* 押し上げる先が変わったら、前の先は戻す（宛先を部屋⇔こっそりで切り替えたとき） */
            if (pushed && (!next || pushed.holder !== next.holder)) {
                setLogPush(pushed, 0);
            }

            if (next) {
                setLogPush(next, push);
            }

            pushed = (next && push > 0) ? next : null;
        }

        /* 1つのログに、押し上げの余白を入れる（0 なら外す） */
        function setLogPush(p, push) {

            var current = parseInt(p.holder.style.getPropertyValue(p.prop), 10) || 0;

            if (push === current) {
                return;
            }

            var log = p.log;
            var wasAtBottom = p.room
                ? isChatLogNearBottom()
                : (log.scrollHeight - log.scrollTop - log.clientHeight) <= SCROLL_FOLLOW_THRESHOLD_PX;

            if (push > 0) {
                p.holder.style.setProperty(p.prop, push + 'px');
            } else {
                p.holder.style.removeProperty(p.prop);
            }

            if (!wasAtBottom) {
                return;
            }

            if (p.room) {
                scrollChatLogToBottom();   /* 部屋のほうは「新着あり」の片付けも一緒に */
            } else {
                log.scrollTop = log.scrollHeight;
            }
        }

        /*
            === 画面が低いときは、パネルのほうを縮めて、ログを最低これだけ残す ===

            押し上げの目的は「自分が送ったスタンプが見えること」。ところが実測で、
            360×640 では押し上げたログが **39px** しか残らず、スタンプ1つ（120px）も入らなかった
            （1366×657 のノートPCでも 85px）。開発者の判断：一覧が窮屈になるほうを取る。

            【残す高さ＝ --stamp-size ＋ 30px】スタンプ1つと、その上下のすき間。
            スタンプの大きさを変えれば、ここも付いてくる（数字を2か所に持たない）。

            【縮めるのは面（.insert-pane）の高さ】パネルの中の3つの面が同じ変数から高さを取るので、
            どのタブでも同じだけ縮む＝タブを切り替えても跳ねない。中はそれぞれスクロールする。
            縮めているあいだは is-shrunk を付け、一覧のマスを小さくする（CSS 側）。

            【縮めすぎない】面が PANE_MIN_PX を下回るほど低い画面では、そこで止める
            （そのときはログのほうが少なくなるが、パネルが使えなくなるよりはよい）
        */
        var LOG_KEEP_EXTRA_PX = 30;
        var PANE_MIN_PX = 150;

        function fitPaneToLeaveLog(panel, log) {

            var stampSize = parseFloat(getComputedStyle(document.documentElement)
                .getPropertyValue('--stamp-size')) || 120;
            var keep = stampSize + LOG_KEEP_EXTRA_PX;

            var visible = panel.getBoundingClientRect().top - log.getBoundingClientRect().top;

            if (visible >= keep) {
                return;
            }

            var pane = panel.querySelector('.insert-pane:not([hidden])');

            if (!pane) {
                return;
            }

            var paneHeight = pane.getBoundingClientRect().height;
            var fitted = Math.max(PANE_MIN_PX, Math.floor(paneHeight - (keep - visible)));

            if (fitted >= paneHeight) {
                return;
            }

            panel.style.setProperty('--insert-pane-height', fitted + 'px');
            panel.classList.add('is-shrunk');
        }

        /* 画面の大きさが変わったら置き直す（狭い⇔広いをまたいだときも、ここで戻る） */
        $(window).on('resize', function () {
            if (isOpen()) {
                positionPanel();
            }

            /* 狭い⇔広いをまたいだら、入力欄を隠す印も付け直す（中で置き直しもする） */
            syncMediaTabInput();
            updateLogPush();
        });

        /*
            ボタンの「押されている」見た目と aria-expanded を、宛先に合わせて揃える。

            【setOpenPanel だけでは足りない】
            あちらは #insertBtn を「パネルが開いているか」だけで塗る。
            こっそりの窓あてに開いているときに、本体の丸いボタンが
            押されている見た目になっていると、宛先を取り違えさせる
        */
        function syncButtons() {

            var open = isOpen();

            $('#insertBtn')
                .toggleClass('is-active', open && target === null)
                .attr('aria-expanded', (open && target === null) ? 'true' : 'false');

            $('.private-talk-insert-btn').each(function () {
                var anonId = $(this).closest('.private-talk-window').attr('data-anon-id');
                var mine = open && target !== null && anonId === target;
                $(this).toggleClass('is-active', mine)
                       .attr('aria-expanded', mine ? 'true' : 'false');
            });
        }

        function isKnownTab(name) {
            return tabNames().indexOf(name) >= 0;
        }

        /*
            覚えていたタブを読む。

            【必ず try/catch で囲むこと】localStorage は、ブラウザの設定や
            プライベートウィンドウで、読むだけでも例外を投げることがある。
            読めなかったら既定へ落とすだけで、パネルは普通に開く
        */
        function readSavedTab() {
            try {
                var saved = window.localStorage.getItem(STORAGE_KEY);
                return isKnownTab(saved) ? saved : null;
            } catch (e) {
                return null;
            }
        }

        function saveTab(name) {
            try {
                window.localStorage.setItem(STORAGE_KEY, name);
            } catch (e) {
                /* 覚えられなくても、今の操作は成り立つ。黙って諦めてよい */
            }
        }

        /*
            タブを選ぶ（見た目・読み上げ・面の出し分け・絵文字の読み込みを、まとめて行う）

            【覚えるのは、ここではない】
            覚えるのは「利用者が自分で選んだとき」だけにしてある（下のクリックと矢印キー）。
            パネルを開いたときの復元でも呼ばれるので、ここで覚えると、
            読んだ値をそのまま書き戻すだけの無駄が毎回走る
        */
        function selectTab(name) {

            if (!isKnownTab(name)) {
                name = DEFAULT_TAB;
            }

            currentTab = name;

            /*
                見た目も読み上げも、aria-selected 1つで決まるようにしてある
                （CSS が .insert-tab[aria-selected="true"] を見ている）。
                クラスと aria を別々に付け外しすると、いつか片方だけ忘れる
            */
            $('.insert-tab').each(function () {
                var isSelected = ($(this).attr('data-insert-tab') === name);
                $(this).attr('aria-selected', isSelected ? 'true' : 'false');
            });

            /*
                面の出し分け。

                【1つずつ書かず、まとめて回していること】
                タブが増えるたびに1行足す形にすると、いつか足し忘れる
                （そして「タブは切り替わるのに、面が両方出る」という形で表に出る）。
                aria-controls が、そのタブの面を指している
            */
            $('.insert-tab').each(function () {
                var paneId = $(this).attr('aria-controls');
                $('#' + paneId).prop('hidden', $(this).attr('data-insert-tab') !== name);
            });

            /*
                絵文字のデータを取りに行くのは、絵文字の面が表に出たときだけ。
                閉じるときと同じ後片付けを、面が引っ込むときにも行う
                （予約中の検索を残したまま面を隠すと、
                  見えていないところへ結果を描きに行くことになる）
            */
            if (name === 'emoji') {
                EmojiPicker.onPanelOpened();
            } else {
                EmojiPicker.onPanelClosed();
            }

            /*
                スタンプも同じ。グリッドは、スタンプの面が表に出たときに初めて作る
                （作った時点で <img> が絵を取りに行くため）
            */
            if (name === 'stamp') {
                StampPicker.onPaneShown();
            } else {
                StampPicker.onPaneHidden();
            }

            /*
                画像の面から離れたら、確認は取り消す。
                スタンプの「選ばれた状態」を別のタブで解除するのと、同じ決まり
                （選んだまま忘れて、あとで送ってしまうのを防ぐ）
            */
            if (name !== 'image') {
                ImageUpload.cancel();
            }

            syncMediaTabInput();
        }

        /*
            === 狭い画面で、スタンプ・画像のタブの間は本体の入力欄を隠す ===

：縦の余裕を一覧に回す。絵文字のタブは文字を打つので出したまま。
            隠すのは CSS（狭い画面の .chat-room.is-insert-media）で、ここは印を付け外しするだけ。

            【止めるときは、この1行】false にすると印が付かなくなり、今までどおりに戻る。
            絵文字⇔スタンプを切り替えるたびにパネルが入力欄のぶん（約140px）上下するので、
            実機で「跳ねるほうが気になる」となったときのため（開発者の指定）

            【印を変えたら、置き直す】入力欄が消える／戻ると、パネルの位置が変わる。
            ログの押し上げ（と、面の縮め）を測り直す
        */
        var HIDE_INPUT_ON_MEDIA_TABS = true;

        function syncMediaTabInput() {

            /*
                【狭い画面だけ】境目はこっそりの窓と同じ 700px（PrivateTalk.isNarrow）。
                CSS の入力欄を隠す指定も狭い画面の中にあるが、印そのものを狭いときにしか付けないのは、
                この印で一覧のマスも小さくしている（CSS）ため——PC幅では何も変えない
            */
            var on = HIDE_INPUT_ON_MEDIA_TABS && isOpen() && PrivateTalk.isNarrow() &&
                (currentTab === 'stamp' || currentTab === 'image');

            var $room = $('.chat-room');

            if ($room.hasClass('is-insert-media') === on) {
                return;
            }

            $room.toggleClass('is-insert-media', on);

            if (isOpen()) {
                positionPanel();
                updateLogPush();
            }
        }

        /* パネルが開かれたとき。前回のタブを思い出して、そこから始める */
        function onPanelOpened() {
            selectTab(currentTab || readSavedTab() || DEFAULT_TAB);
            renderTarget();
            syncButtons();
        }

        function onPanelClosed() {
            EmojiPicker.onPanelClosed();
            StampPicker.onPaneHidden();
            ImageUpload.cancel();

            /* 閉じたら入力欄を戻す（isOpen() はもう false なので、印が外れる） */
            syncMediaTabInput();

            /* 閉じたら本体あてへ戻す（次に丸いボタンで開いたときのため） */
            target = null;
            renderTarget();
            syncButtons();
        }

        /*
            宛先を変える。**変わったときは、選んでいたスタンプと画像の確認を取り消す**。
            部屋あてに選んだものを、そのままこっそりの相手へ送ってしまう（またはその逆）を防ぐ
        */
        function changeTarget(newTarget) {

            if (newTarget !== target) {
                StampPicker.onTargetChanged();
                ImageUpload.cancel();
            }

            target = newTarget;
        }

        /*
            そのボタンから開く／閉じる

            newTarget … null なら本体あて、文字列ならこっそりの相手

            【同じ宛先で開いているなら閉じる。違う宛先なら、閉じずに切り替える】
            こっそりの窓あてに開いているところで本体の丸いボタンを押したら、
            閉じてから開き直すのではなく、そのまま本体あてになるのが自然である
        */
        function toggleFor(newTarget) {

            if (isOpen() && target === newTarget) {
                setOpenPanel(null);
                return;
            }

            changeTarget(newTarget);

            if (isOpen()) {
                renderTarget();
                syncButtons();
                return;
            }

            /* 開く。onPanelOpened が帯とボタンを揃える */
            setOpenPanel('#insertPanel');
        }

        /*
            受け取ったスタンプを押したとき。スタンプの面で、そのスタンプを目立たせて開く。

            newTarget … 押したスタンプが部屋のログにあれば null、こっそりの窓の中なら
                        その窓の相手。**押した場所が、送る先を決める**
                        （パネルを開いたボタンで宛先が決まるのと、同じ考え方）

            【toggleFor と違って、閉じない】
            既に同じ宛先で開いていても、閉じずにそのスタンプへ移る。
            利用者が押したのは「開閉のボタン」ではなく「このスタンプ」だから

            【localStorage には覚えさせない（saveTab を呼ばない）】
            利用者がタブを選んだわけではないため。
            同じページの中で次に開いたときは、今いるスタンプの面から始まる
            （currentTab のほうは書き換わる。タブを押して選んだときと同じ）
        */
        function showStamp(newTarget, code) {

            changeTarget(newTarget);

            if (isOpen()) {
                selectTab('stamp');
                renderTarget();
                syncButtons();
            } else {
                /* 開く前に決めておけば、onPanelOpened がスタンプの面から始める */
                currentTab = 'stamp';
                setOpenPanel('#insertPanel');
            }

            StampPicker.reveal(code);
        }

        /* タブを押したとき。ここだけが「覚える」 */
        $(document).on('click', '.insert-tab', function () {
            var name = $(this).attr('data-insert-tab');
            if (name === currentTab) {
                return; /* 同じタブを押し直しただけ。絵文字の面を作り直さない */
            }
            selectTab(name);
            saveTab(currentTab);
        });

        /*
            左右の矢印キーでも移れるようにする。
            role="tablist" と名乗った以上、読み上げソフトの利用者はこれを期待する。
            Tab キーでも両方に届くようにしてある（どちらのタブも tabindex を触っていない）
        */
        $(document).on('keydown', '.insert-tab', function (e) {

            if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') {
                return;
            }

            e.preventDefault();

            /* 隠れているタブ（画像を送れない部屋の「画像」）には移らない */
            var tabs = $('.insert-tab:not([hidden])');
            var index = tabs.index(this);
            var next = (e.key === 'ArrowRight')
                ? (index + 1) % tabs.length
                : (index - 1 + tabs.length) % tabs.length;

            var name = tabs.eq(next).attr('data-insert-tab');
            selectTab(name);
            saveTab(currentTab);

            /* 移った先へフォーカスも移す（矢印で選んだのに、
               キーボードの居場所が前のタブに残ると、次の矢印がおかしくなる） */
            tabs.eq(next).trigger('focus');
        });

        /*
            「画像」のタブを出し入れする（管理人が部屋の設定で画像の可否を変えたとき）。
            隠すときに「画像」の面が表に出ていたら、既定のタブ（絵文字）へ移す（選んでいた画像の確認も面ごと隠れる）。
            【貼られた画像は消さない】ログの行には触らない
        */
        function setImageEnabled(enabled) {

            /*
                【焦点がそのタブにあったら、行き先を決めてから隠す】（読み上げの点検の回）
                管理人が画像を止めた瞬間に、たまたま「画像」のタブへ焦点を置いていた人の焦点が消える
                （ノックの小窓を消すときと同じ手当て）。移る先は、表に出る既定のタブ
            */
            var hadFocus = (document.activeElement === document.getElementById('insertTabImage'));

            $('#insertTabImage').prop('hidden', !enabled);

            if (!enabled && currentTab === 'image') {
                selectTab(DEFAULT_TAB);
            }

            if (!enabled && hadFocus) {
                var next = document.querySelector('.insert-tab:not([hidden])');
                if (next) {
                    next.focus();
                }
            }
        }

        return {
            onPanelOpened: onPanelOpened,
            onPanelClosed: onPanelClosed,
            toggleFor: toggleFor,
            setImageEnabled: setImageEnabled,
            showStamp: showStamp,
            refreshPreview: refreshPreview,

            /* 今の宛先（null＝本体あて、文字列＝こっそりの相手） */
            target: function () {
                return target;
            },

            /* 今の宛先の入力欄。こっそりの窓が消えていれば null */
            targetInput: function () {
                return (target === null)
                    ? $('#messageInput')[0]
                    : PrivateTalk.inputOf(target);
            },

            /* 閉じたあと、焦点を戻す先（開いたボタン） */
            returnFocusEl: function () {
                return (target === null)
                    ? $('#insertBtn')[0]
                    : PrivateTalk.insertButtonOf(target);
            },

            /*
                こっそりの窓あてに開いていて、パネルがその窓に重なっているか

                【判定のしかた】
                  ・狭い画面 … 画面幅で決める（窓が覆いかぶさる形なので、パネルは必ず窓に重なる）
                  ・広い画面 … 実際の位置で決める。positionPanel が窓の横に入りきらずに重ねたとき
                               is-covering-window を付けている（その計算をそのまま使う。新しく測らない）
            */
            coversTargetWindow: function () {
                return isOpen() && target !== null &&
                    (PrivateTalk.isNarrow() ||
                     document.getElementById('insertPanel').classList.contains('is-covering-window'));
            },

            /*
                こっそりの窓あてに開いていて、その窓のログを押し上げているか。
                押し上げていれば、パネルが窓に重なっていても、送ったスタンプはパネルの上に見える
                （狭い画面の全面の窓。updateLogPush の項）
            */
            pushesTargetLog: function () {
                return isOpen() && target !== null && PrivateTalk.isFullScreen();
            },

            /*
                全面に入った／出たとき（PrivateTalk.syncFullScreen から呼ばれる）

                【部屋あてに開いていたら、閉じる】全面になると本体の入力欄が消えるので、
                絵文字を入れる先が見えなくなる（たとえば部屋あてに開いている最中に、
                こっそりが届いて窓が自動で開いたとき）。
                【こっそりの窓あてなら、置き場所と押し上げを測り直す】
                狭い⇔広いをまたいだときは、窓の隣に出すか・入力行の上に出すかも変わる
            */
            onFullScreenChanged: function (full) {

                if (!isOpen()) {
                    return;
                }

                if (full && target === null) {
                    setOpenPanel(null);
                    return;
                }

                positionPanel();
                updateLogPush();
            },

            /* その宛先で開いているときだけ閉じる（送れたとき用） */
            closeIfTarget: function (someTarget) {
                if (isOpen() && target === someTarget) {
                    setOpenPanel(null);
                }
            },

            /* その窓が閉じられた／相手が退室した */
            onTargetGone: function (anonId) {
                if (isOpen() && target !== null && target === anonId) {
                    setOpenPanel(null);
                }
            }
        };
    })();


    /*
        === 「画像を選ぶ」ボタンと、その下の案内 ===

        【この2つは、画像が許されている部屋にしか存在しない】
        ページ側の <% If %> が出し分けているので、
        許されていない部屋では jQuery の結果が0件になり、何も起きない
        （存在しない要素を触っても、何も起きないのが jQuery である）
    */
    $('#imagePickBtn')
        .prepend(imageIconSvg('image-pick-icon'))
        .on('click', function () {

            /*
                宛先は、パネルを開いたボタンで決まる
                （本体の丸いボタンなら null＝部屋あて、こっそりの窓のボタンならその相手）
            */
            ImageUpload.pick(InsertPanel.target());
        });

    /*
        案内の文言は、設定から組み立てる。

        【画面に数字をべた書きしないこと】
        決めているのは SiteSettings（＝DBの ImageMaxBytes）である。
        書き写すと、値を変えたときに案内だけが嘘になる
        （文字数カウンタの上限を MAX_MESSAGE_LENGTH から出しているのと、同じ話）

        【受け付ける形式は、サーバーが保存する3つとは違う】
        GIF以外は、送る前に JPEG か PNG へ変換している。
        利用者から見えるのは「選べる形式」なので、こちらを書く
    */
    $('#imagePickNotice').text(
        'PNG / JPG / GIF / BMP / ICO / WebP が送れます（' +
        Math.round(vbChatSettings.imageMaxBytes / 1048576 * 10) / 10 +
        'MBまで／大きい画像は自動で縮めます）。');


    /*
        入れるものを選ぶパネルは、外を押すか Esc で閉じる

        【なぜ、このパネルだけ閉じ方が多いのか】
        参加者一覧・設定は、開けている間もそのボタンが目の前にあり、
        もう一度押せば閉じられる。
        こちらは入力欄の上に覆いかぶさるので、「打ちたいのに邪魔」という
        状態になりやすい。入力欄を触れば閉じる、が自然な動きになる
    */
    $(document).on('pointerdown', function (e) {

        if (!$('#insertPanel').hasClass('is-open')) {
            return;
        }

        /*
            パネルの中と、開閉ボタン自身は、押しても閉じない。

            【こっそりの窓のボタンも、開閉ボタンである】
            ここで閉じると、そのあとの click で開き直し、
            **「押しても何も起きない」**ように見える
        */
        /*
            【ログの中のスタンプも、開閉ボタンの仲間である】
            押すと、パネルがそのスタンプを目立たせて開く（または、そちらへ移る）。
            ここで閉じると、そのあとの click で開き直すことになり、画面がちらつく
        */
        if ($(e.target).closest('#insertPanel, #insertBtn, .private-talk-insert-btn, .message-stamp-btn').length > 0) {
            return;
        }

        /*
            【ログの相手（名前・アイコン・トリップ）も、ここでは閉じない】
            押すと入力欄へ宛先が入り、そのとき閉じる（ReplyAddress.insert）。
            押した瞬間に閉じると、ログの下の余白（--log-push）が縮んで行が動き、
            **指の下から名前が逃げて click が届かない**ことがある。閉じるのは click のあとに回す
        */
        if ($(e.target).closest('#chatLog .message-sender').length > 0) {
            return;
        }

        setOpenPanel(null);
    });


    /*
        === 宛先つき発言：ログの相手を押すと、入力欄に「〇〇：」が入る ===

        10人を超えると、誰が誰に話しかけているのか分からなくなるため。

        【押したら、入力欄に文字が入るだけ。サーバーは何も解析しない】
        「誰あてか読んで分かる」という目的は、文字だけで果たせる。
        宛先を仕組みとして持つ（あて先の人にだけ印を付ける、など）のは、あとから育てられる。

        【入口は、部屋のログの相手の行だけ】
        ・参加者一覧は使わない。
          一覧で相手を押す操作は、もう「選ぶ」（無視する／こっそり話す、の相手）に使われていて、
          そこへ宛先を乗せると、1回のタップに意味が2つ乗る
        ・こっそりの窓の中には入れない（1対1なので、宛先が自明）。
          窓は #chatLog の外（#privateTalkLayer）にあるので、下のセレクタが自然に外している
        ・自分の発言には、そもそも送信者の欄が無い

        【マウス・指では押せて、Tab では止まらない】
        .message-sender は div のまま（button にしない）。
、ログの中で Tab が止まる場所を1つ（LogKeyNav）に減らしたばかりなので、
        発言の数だけ Tab の止まる場所を増やす形にはしない

        【決めたこと（2つ）】
        1. 入れる場所は、いつも**先頭**。カーソルの位置ではない。
           「〇〇：」は呼びかけで、文の頭にしか意味が無い。
           スマホでは入力欄に焦点が無いことが多く、そのときのカーソルの位置は
           利用者に見えていない（文の途中に宛先が割り込む）。
           カーソルは、それまで打っていた文の同じ位置に保つ（焦点が無かったときは末尾）
        2. 同じ人を続けて押しても、**二重には入れない**（先頭が既にその宛先なら、何もしない）。
           **別の人を押したら、先頭の宛先を入れ替える**（押し間違いを、押し直すだけで直せる）。
           入れ替えるのは「こちらが入れた宛先が、先頭にそのまま残っているとき」だけ。
           利用者が書き換えた文や、自分で打った文には手を付けない（その場合は先頭に足す）

        【pointerdown で焦点を奪わないこと】ログのスタンプと、まったく同じ手当て。
        入力欄から焦点が外れると、スマホではソフトキーボードが閉じてページが広がり、
        **指の下から名前が逃げて click が届かない**（踏んだ連鎖）。
        preventDefault しても、指で払ったときのスクロールは止まらない（止めるのは焦点の移動と
        マウス互換のイベントだけ）。副作用は、PC で名前をドラッグして選択できなくなることだけ

        【タッチ端末でも、入れたあと入力欄へ焦点を移す】
        絵文字（insertEmoji）ではタッチ端末で焦点を戻さないが、あちらは「続けて何度も押す」操作で、
        押すたびにキーボードが開閉して指の下からボタンが逃げたため。
        こちらは押したら次は文を打つので、キーボードが開くのが自然である（LINE の返信と同じ）。
        続けて別の人を押し直しても、上の preventDefault で焦点は入力欄に残るので、開閉は起きない
    */
    var ReplyAddress = (function () {

        /* こちらが最後に入れた宛先（「太郎： 」）。入れ替えてよいかの判定に使う */
        var lastPrefix = null;

        function insert(nickname) {

            var input = $('#messageInput')[0];

            if (!nickname || !input || input.disabled) {
                return;
            }

            /*
                コロンのあとに半角スペースを1つ（実機を見て決めた）。
                宛先と本文の間にひと息あるほうが読みやすい。
                宛先だけで送っても、サーバーが前後の空白を落とすので、末尾の空白は残らない
            */
            /*
                「〇〇さんへ： 」から「〇〇： 」（名前＋全角のコロン＋半角の空白）に変えた。
                「@名前」の形は使わない——アカウントID（@ユーザー名）と紛れるため
            */
            var prefix = nickname + '： ';
            var value = input.value;

            /*
                カーソルは、入力欄に焦点があるときだけ「それまでの位置」を保つ。
                焦点が無いときの selectionStart は、利用者に見えていない位置なので末尾へ置く
            */
            var hadFocus = (document.activeElement === input);
            var caret = (hadFocus && typeof input.selectionStart === 'number')
                ? input.selectionStart
                : null;

            if (value.indexOf(prefix) === 0) {

                /* 既にその人あて。二重には入れない（カーソルもそのまま） */

            } else {

                var removedLength = 0;

                if (lastPrefix && value.indexOf(lastPrefix) === 0) {
                    removedLength = lastPrefix.length;
                }

                input.value = prefix + value.slice(removedLength);

                if (caret !== null) {
                    /* 入れ替えた宛先の中にカーソルがあったなら、新しい宛先の直後へ */
                    caret = Math.max(caret - removedLength, 0) + prefix.length;
                }

                /*
                    【必須】文字数の表示と送信ボタンの状態を更新する。
                    JS から値を書き換えたときは input イベントが起きない
                    （insertEmoji と同じ手当て。忘れると上限を超えても送信ボタンが押せる）
                */
                updateMessageCounter();
            }

            lastPrefix = prefix;

            /*
                入れるものを選ぶパネルが開いていたら閉じる（入力欄を触ったのと同じ扱い）。
                狭い画面のスタンプ・画像のタブでは本体の入力欄が隠れているので、閉じないと
                入れた宛先が見えない。閉じるのを pointerdown ではなくここにしている理由は、
                上の「外を押したら閉じる」の中に書いた
            */
            if ($('#insertPanel').hasClass('is-open')) {
                setOpenPanel(null);
            }

            input.focus();

            if (caret === null) {
                caret = input.value.length;
            }

            input.setSelectionRange(caret, caret);
        }

        return {
            insert: insert
        };
    })();

    $(document).on('pointerdown', '#chatLog .message-sender', function (e) {
        e.preventDefault();
    });

    $(document).on('click', '#chatLog .message-sender', function (e) {

        /* アカウントID（名前の下のリンク）を押したときは、宛先を入れない。プロフィールが新しいタブで開くだけ
           （リンクは送信者の欄の中にあるので、ここで見分ける） */
        if ($(e.target).closest('a.account-id').length > 0) {
            return;
        }

        /* 接続が切れている間は、入力欄が使えないので何もしない */
        if (!isInputEnabledByConnection) {
            return;
        }

        ReplyAddress.insert($(this).attr('data-nickname'));
    });


    /*
        === 受け取ったスタンプを押したとき ===

        スタンプの一覧が開き、そのスタンプのセットが表に出て、そのスタンプが目立つ。
        その状態でもう一度押せば送れる（開発者の指定）。

        【送る先は、押した場所で決まる】
        部屋のログのスタンプなら部屋あて、こっそりの窓の中のスタンプならその相手。

        【絵を新しいタブで開く動線は作らない】
        押して起きるのは「一覧で選べる状態にする」ことだけである（理由は buildStampHtml）

        【pointerdown で焦点を奪わないこと】
        こっそりの窓の送信ボタン・入れるもののボタンと、まったく同じ手当て。
        入力欄から焦点が外れると、スマホではソフトキーボードが閉じてページが広がり、
        **指の下からスタンプが逃げて click が届かない**（踏んだ連鎖）。
        焦点は click のあとで、パネルの中のそのスタンプへ移す（StampPicker.reveal）
    */
    $(document).on('pointerdown', '.message-stamp-btn', function (e) {
        e.preventDefault();
    });

    $(document).on('click', '.message-stamp-btn', function () {

        var code = $(this).attr('data-stamp-code');

        /* 接続が切れている間は、入れるもののボタンと同じく何もしない */
        if (!isInputEnabledByConnection || !StampPicker.isSendable(code)) {
            return;
        }

        var $win = $(this).closest('.private-talk-window');
        var newTarget = null;

        if ($win.length > 0) {

            newTarget = $win.attr('data-anon-id');

            /* 相手が退室した窓からは、送れないので開かない（窓の入れるもののボタンと同じ） */
            if (!PrivateTalk.inputOf(newTarget)) {
                return;
            }
        }

        InsertPanel.showStamp(newTarget, code);
    });

    $(document).on('keydown', function (e) {

        if (e.key === 'Escape' && $('#insertPanel').hasClass('is-open')) {

            /*
                【Esc は1段ずつ】（開発者と決めた）
                スタンプが選ばれている／画像の確認が出ているなら、まずそれだけを取り消す。
                パネルを閉じるのは、もう一度 Esc を押したとき
            */
            if (StampPicker.hasSelection()) {
                StampPicker.cancelSelection();
                return;
            }

            if (ImageUpload.hasConfirm()) {
                ImageUpload.cancel();
                $('#imagePickBtn').trigger('focus');
                return;
            }

            /* 閉じると宛先が消えるので、戻す先は閉じる「前」に聞く */
            var input = InsertPanel.targetInput();

            setOpenPanel(null);

            /*
                閉じたあと、フォーカスの行き先を決めておく。
                決めないと、消えた要素の上にフォーカスが残って、
                キーボードや読み上げソフトで使っている人が居場所を見失う
                （「新着あり」を押したあとと、同じ手当て）。
                こっそりの窓あてに開いていたなら、その窓の入力欄へ戻す
            */
            $(input || '#messageInput').trigger('focus');
        }
    });


    /*
        入力のたびに、文字数と送信ボタンの状態を更新する
        （貼り付け・切り取り・IMEでの確定も、inputイベントで拾える）

        ここで入力欄の高さも書き換える形（自動伸縮）を一度入れたが、戻した（Content/ChatRoom.css の .message-input の説明）。
    */
    $('#messageInput').on('input', updateMessageCounter);

    /* 最初の1回。まだ何も入力していない状態（＝「0 / 500 文字」）を表示しておく */
    updateMessageCounter();
    applyInputHint();


    /*
        === ノックの返事待ち（部屋の管理人だけに出る）===

        満室の部屋に誰かがノックすると、管理人の画面にだけ、許可を聞く小窓が出る
        （サーバーの配信 knockRequested。仕組みは Chat/Hubs/ChatHub.Knock.vb）。

          ┌────────────────────────────┐
          │ [顔] 〇〇さんがノックしています        │
          │      a1b2c3d4e5（トリップ）              │
          │ あと 58 秒                              │ ← aria-live を付けない（1秒ごとに読ませない）
          │ （断られた理由。赤）                    │
          │                       [断る] [入れる]  │
          └────────────────────────────┘

        【並べて出す】同時に何人来ても、1人ずつ順番にはしない。
        サーバーが1部屋10件までに抑えているので、並びきらなければ、この箱の中でスクロールする。

        【会話を止めない】ほかの小窓（ログアウトの確認など）と違い、下敷きを敷かない・焦点を奪わない。
        管理人は話している最中かもしれないため。
        届いたことは、音（部屋の全員に鳴る。knockReceived）と、読み上げ用の1行（#knockAnnounce）で知らせる。

        【重なり順は 25】パネル（20）の手前、「新着あり」（30）の奥。
        パネルを開いていても、ノックは見える位置に出る（60秒で消えるので、気づけないと困る）。
        こっそりの窓（10）より手前（スマホで窓を全面に開いていても見える）

        【無視している相手からのノックは、出さない】無視機能の決まり（行も音も出さない）に乗せる。
        その人のノックは、返事が無いまま時間切れになる

        【消える場面】入れる・断る を押した／時間切れ（ここで数え下げる。サーバーからも knockWithdrawn が来る）／
        その人が普通に入ってきた（knockWithdrawn）／管理人でなくなった（入り直したときの一覧が空）
    */
    /*
        知らせを自動で消すまでの時間（ミリ秒）

        最低8秒（DB の WarningDisplaySeconds がそれより長ければ、そちら）＋1文字につき0.15秒、上限20秒
        （DB の値が20秒より長ければ、上限もその値）。
        【同じ計算が site.js の warningDuration にある】部屋は site.js を読まないので写してある。
        **片方を変えたら、もう片方も直すこと**
    */
    function noticeDurationMs(text) {

        var base = 8000;

        if (window.vbChatSettings && vbChatSettings.warningDisplayMs > base) {
            base = vbChatSettings.warningDisplayMs;
        }

        return Math.min(Math.max(20000, base), base + String(text || '').length * 150);
    }

    var KnockRequests = (function () {

        var $box = $('#knockRequests');
        var announce = document.getElementById('knockAnnounce');

        /* knockId → { el, timer, until } */
        var items = {};

        var SVG_NS = 'http://www.w3.org/2000/svg';

        function count() {
            return Object.keys(items).length;
        }

        function updateBox() {
            if ($box.length) {
                $box[0].hidden = (count() === 0);
            }
        }

        /*
            読み上げ用の1行。同じ文言が続いても読ませるため、いったん空にしてから入れる
            （aria-live の落とし穴）
        */
        function tell(text) {

            if (!announce) {
                return;
            }

            announce.textContent = '';

            setTimeout(function () {
                announce.textContent = text;
            }, 50);
        }

        function remove(knockId) {

            var item = items[knockId];

            if (!item) {
                return;
            }

            clearInterval(item.timer);
            clearTimeout(item.hideTimer);
            delete items[knockId];

            /*
                【焦点がこの箱の中にあったら、行き先を決めてから消す】
                消した瞬間に焦点が迷子になり、キーボードや読み上げソフトで使っている人が居場所を見失う。
                ほかの小窓が残っていれば、その「入れる」へ。無ければ入力欄へ
                （スマホでキーボードが勝手に開かないよう、タッチ端末では入力欄へは移さない）
            */
            var hadFocus = item.el.contains(document.activeElement);

            $(item.el).remove();
            updateBox();

            if (!hadFocus) {
                return;
            }

            var next = $box.find('.knock-request-approve').get(0);

            if (next) {
                next.focus();
            } else if (!isTouchDevice) {
                $('#messageInput').trigger('focus');
            }
        }

        function clear() {
            Object.keys(items).forEach(function (knockId) {
                remove(knockId);
            });
        }

        /*
            === 小窓の中身を、消えた理由に差し替える（⑥）===

            指摘：管理人には、なぜ小窓が消えたのかが分からない。ログではなく小窓で知らせる。
            ボタンを消して理由だけにし、少し置いてから消す（×でも閉じられる）。部屋のログには残さない。
            同時に何枚も出ているときは、その1枚だけが変わる。

              ┌────────────────────────────┐
              │ 〇〇さんのノックは、時間切れで取り消されました  [×] │
              └────────────────────────────┘

            【消えるまでの時間】noticeDurationMs（ラウンジの赤い警告と同じ計算）。
            マウスが乗っている間・焦点が中にある間は消えない（離れたら、残りから数え直す）。
            スマホにはマウスを乗せる操作が無いので、×が本命
        */
        function showNotice(knockId, text) {

            var item = items[knockId];

            if (!item || !text) {
                return;
            }

            /* もう差し替えてある（手元の時計とサーバーの両方から「時間切れ」が来た、など）。最初の文言のまま */
            if (item.notice) {
                return;
            }

            item.notice = true;
            item.busy = true;
            clearInterval(item.timer);

            var el = item.el;
            var hadFocus = el.contains(document.activeElement);

            el.textContent = '';
            el.classList.add('knock-request--notice');

            var noticeId = 'knockNotice' + knockId;

            var p = document.createElement('p');
            p.className = 'knock-request-notice';
            p.id = noticeId;
            p.textContent = text;
            el.appendChild(p);

            var close = document.createElement('button');
            close.type = 'button';
            close.className = 'knock-request-close';
            close.setAttribute('aria-label', 'この知らせを閉じる');
            close.textContent = '×';
            el.appendChild(close);

            el.setAttribute('aria-labelledby', noticeId);

            close.addEventListener('click', function () {
                remove(knockId);
            });

            /* 焦点が「入れる」などにあった＝押そうとしていた。×へ移す（居場所を失わせない） */
            if (hadFocus) {
                close.focus();
            }

            var remaining = noticeDurationMs(text);
            var startedAt = 0;
            var hovering = false;
            var focused = hadFocus;

            var start = function () {
                if (item.hideTimer || hovering || focused || !items[knockId]) {
                    return;
                }
                startedAt = Date.now();
                item.hideTimer = setTimeout(function () {
                    item.hideTimer = null;
                    remove(knockId);
                }, remaining);
            };

            var pause = function () {
                if (!item.hideTimer) {
                    return;
                }
                clearTimeout(item.hideTimer);
                item.hideTimer = null;
                remaining = Math.max(1000, remaining - (Date.now() - startedAt));
            };

            el.addEventListener('mouseenter', function () { hovering = true; pause(); });
            el.addEventListener('mouseleave', function () { hovering = false; start(); });
            el.addEventListener('focusin', function () { focused = true; pause(); });
            el.addEventListener('focusout', function (e) {
                if (!el.contains(e.relatedTarget)) {
                    focused = false;
                    start();
                }
            });

            start();
            tell(text);
        }

        /*
            サーバーの knockWithdrawn（理由つき）

            小窓があれば、理由があれば差し替え・無ければ消す。
            小窓が無ければ、standalone（許可したあとの取り置きが外れた）のときだけ、知らせだけの小窓を作る
        */
        function withdraw(knockIdValue, text, anonId, standalone) {

            var knockId = String(knockIdValue);

            if (items[knockId]) {
                if (text) {
                    showNotice(knockId, text);
                } else {
                    remove(knockId);
                }
                return;
            }

            if (!text || !standalone || !$box.length) {
                return;
            }

            /* 無視している相手のことは出さない（無視機能の決まり＝行も音も出さない） */
            if (anonId && IgnoreList.isIgnored(anonId)) {
                return;
            }

            var el = document.createElement('div');
            el.className = 'knock-request';
            el.setAttribute('role', 'group');
            el.setAttribute('data-knock-id', knockId);
            $box.append(el);

            items[knockId] = {
                el: el,
                busy: true,
                notice: false,
                timer: null,
                hideTimer: null
            };

            updateBox();
            showNotice(knockId, text);
        }

        function answer(knockId, approve) {

            var item = items[knockId];

            if (!item || item.busy) {
                return;
            }

            item.busy = true;
            item.setButtonsDisabled(true);
            item.setError('');

            chatHub.server.answerKnock(parseInt(knockId, 10), approve)
                .done(function (res) {

                    if (!items[knockId]) {
                        return;
                    }

                    if (res && res.ok) {
                        remove(knockId);
                        return;
                    }

                    item.busy = false;

                    var message = (res && res.message) || '返事を送れませんでした。';

                    if (res && res.keep) {
                        /* 臨時の席がいっぱい、など。小窓は残す（あとで断れるように） */
                        item.setButtonsDisabled(false);
                        item.setError(message);
                        return;
                    }

                    /*
                        もう返事ができない（時間切れ・取り消し・管理人ではない）。
、小窓の中身を理由に差し替えて、少し置いてから消す（×でも閉じられる）
                    */
                    showNotice(knockId, item.nickname + 'さんのノックには、もう返事ができません（時間切れか、取り消されました）');
                })
                .fail(function () {

                    if (!items[knockId]) {
                        return;
                    }

                    item.busy = false;
                    item.setButtonsDisabled(false);
                    item.setError('返事を送れませんでした。もう一度押してください。');
                });
        }

        /*
            1つぶん足す。**文字は textContent で入れる**（名前を HTML として解釈させない）。
            アイコンの種類と色は、部屋の参加者一覧と同じくサーバーが検査した値だが、
            出口でも属性として入れる（setAttribute なので、引用符で属性が閉じることは無い）
        */
        function add(info) {

            if (!info || !$box.length) {
                return;
            }

            if (IgnoreList.isIgnored(info.anonId)) {
                return;
            }

            var knockId = String(info.knockId);
            var seconds = Math.max(1, info.seconds || 0);
            var nickname = info.nickname || '名無し';

            /* 同じノックが2回届いた（入り直したときの一覧と、配信が重なった）。秒数だけ新しくする */
            if (items[knockId]) {
                items[knockId].until = Date.now() + seconds * 1000;
                return;
            }

            var titleId = 'knockRequestTitle' + knockId;

            var el = document.createElement('div');
            el.className = 'knock-request';
            el.setAttribute('role', 'group');
            el.setAttribute('aria-labelledby', titleId);
            el.setAttribute('data-knock-id', knockId);

            var head = document.createElement('div');
            head.className = 'knock-request-head';

            var icon = document.createElement('span');
            icon.className = 'knock-request-icon';

            var svg = document.createElementNS(SVG_NS, 'svg');
            svg.setAttribute('viewBox', '0 0 512 512');
            svg.setAttribute('aria-hidden', 'true');
            svg.style.color = info.iconColor || '';

            var use = document.createElementNS(SVG_NS, 'use');
            use.setAttribute('href', '#icon-' + (info.iconType || ''));
            svg.appendChild(use);
            icon.appendChild(svg);
            head.appendChild(icon);

            var text = document.createElement('div');
            text.className = 'knock-request-text';

            var title = document.createElement('p');
            title.className = 'knock-request-title';
            title.id = titleId;
            title.textContent = nickname + 'さんがノックしています';
            text.appendChild(title);

            /* ノックした人のトリップかアカウントID（どちらか一方。アカウントID を足した）。
               形は buildTripHtml が確かめてから組み立てるので、innerHTML に入れてよい（エスケープの要らない文字しか来ない） */
            var knockMarkHtml = buildTripHtml(idMarkOf(info.trip, info.accountName), 'knock-request-trip');

            if (knockMarkHtml) {
                var knockMark = document.createElement('span');
                knockMark.className = 'knock-request-mark';
                knockMark.innerHTML = knockMarkHtml;
                text.appendChild(knockMark);
            }

            var time = document.createElement('p');
            time.className = 'knock-request-time';
            text.appendChild(time);

            head.appendChild(text);
            el.appendChild(head);

            var error = document.createElement('p');
            error.className = 'knock-request-error';
            error.setAttribute('aria-live', 'polite');
            el.appendChild(error);

            var actions = document.createElement('div');
            actions.className = 'knock-request-actions';

            /* 読み上げでは、誰についての操作かが分かるように名前を添える */
            var deny = document.createElement('button');
            deny.type = 'button';
            deny.className = 'knock-request-btn knock-request-deny';
            deny.textContent = '断る';
            deny.setAttribute('aria-label', nickname + 'さんを断る');

            var approve = document.createElement('button');
            approve.type = 'button';
            approve.className = 'knock-request-btn knock-request-approve';
            approve.textContent = '入れる';
            approve.setAttribute('aria-label', nickname + 'さんを入れる');

            actions.appendChild(deny);
            actions.appendChild(approve);
            el.appendChild(actions);

            $box.append(el);

            var item = {
                el: el,
                busy: false,
                notice: false,
                nickname: nickname,
                until: Date.now() + seconds * 1000,
                timer: null,
                hideTimer: null,
                setButtonsDisabled: function (disabled) {
                    deny.disabled = disabled;
                    approve.disabled = disabled;
                },
                setError: function (message) {
                    error.textContent = message || '';
                }
            };

            items[knockId] = item;

            var tick = function () {

                var remaining = Math.ceil((item.until - Date.now()) / 1000);

                if (remaining <= 0) {
                    /*
                        時間切れ。サーバーからも同じ理由が届くが、手元の時計のほうが先に来ることが多いので、
                        ここでも同じ文言に差し替える（2度目は showNotice が無視する）
                    */
                    showNotice(knockId, nickname + 'さんのノックは、時間切れで取り消されました');
                    return;
                }

                time.textContent = 'あと ' + remaining + ' 秒';
            };

            tick();
            item.timer = setInterval(tick, 1000);

            deny.addEventListener('click', function () {
                answer(knockId, false);
            });

            approve.addEventListener('click', function () {
                answer(knockId, true);
            });

            updateBox();

            tell(nickname + 'さんがノックしました。入れるかどうか、' + seconds + '秒以内に選んでください。');
        }

        /* 入り直したとき（JoinRoom の戻り値）。サーバーが持っている一覧に揃える */
        function replaceAll(list) {

            var keep = {};

            (list || []).forEach(function (info) {
                keep[String(info.knockId)] = true;
            });

            Object.keys(items).forEach(function (knockId) {
                /* 理由に差し替えた小窓は、サーバーの一覧には無いが残す（自分で消えるのを待つ） */
                if (!keep[knockId] && !items[knockId].notice) {
                    remove(knockId);
                }
            });

            (list || []).forEach(add);
        }

        updateBox();

        return {
            add: add,
            remove: function (knockId) { remove(String(knockId)); },
            withdraw: withdraw,
            replaceAll: replaceAll,
            clear: clear
        };
    })();


    /* === SignalR接続の準備 === */
    var chatHub = $.connection.chatHub;

    /*
        === ハブを呼ぶときの共通の守り===

        【なぜ、これが要るのか】
        つながっていないときにハブを呼ぶと、SignalR は2通りに壊れる。
          ・切れている（disconnected / connecting）… その場で例外を投げる。
            呼んだ側の .fail も .always も走らず、押したボタンが押せないまま残る
          ・再接続中（reconnecting）… 死んだソケットへ送ろうとして、返事が永久に来ないことがある
        どちらも「押しても何も起きない」になる。スマホを放っておいてから戻ったときに、
        退室ボタンがこれで固まった（本番。退室がサーバーへ届かず、席が1時間残った）。

        そこで、ハブの呼び出しは全部ここを通す（/signalr/hubs が作る chatHub.server.〇〇 は、
        どれも chatHub.invoke を呼ぶので、invoke を1か所包めば全部に効く＝毎回の基本の8）。
          ・つながっていなければ、送らずにすぐ失敗を返す（呼んだ側の .fail に、いつもどおり入る）
          ・送ったあと HUB_CALL_TIMEOUT_MS 待っても返事が無ければ、失敗を返す
        失敗を受けた側の振る舞いは、今までの .fail のまま（たいていは「接続の話なので、帯に任せる」）

        【待つ時間を長めにしてある理由】正常なときに切ることが無いように。
        ここで切っても、サーバーはあとから処理しうる（送信なら「直前と同じ内容」の検査が二重を止める）
    */
    var HUB_CALL_TIMEOUT_MS = 20000;
    (function () {
        var rawInvoke = chatHub.invoke;
        chatHub.invoke = function () {
            var d = $.Deferred();
            var methodName = arguments[0];

            if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                debugLog('つながっていないため、' + methodName + ' を送らずに失敗にした');
                return d.reject(new Error('not connected')).promise();
            }

            var timer = setTimeout(function () {
                debugLog(methodName + ' の返事が ' + HUB_CALL_TIMEOUT_MS + 'ms 来ないため、失敗にした');
                d.reject(new Error('timeout'));
            }, HUB_CALL_TIMEOUT_MS);

            try {
                rawInvoke.apply(this, arguments).then(function (result) {
                    clearTimeout(timer);
                    d.resolve(result);
                }, function (err) {
                    clearTimeout(timer);
                    d.reject(err);
                });
            } catch (e) {
                clearTimeout(timer);
                debugLog(methodName + ' を送れなかった：' + e.message);
                d.reject(e);
            }
            return d.promise();
        };
    })();

    /* サーバーから届くイベントのハンドラを、接続開始前に登録しておく */

    /* trip … 送り主のトリップ（サーバーが末尾に足した。無ければ null） */
    /* accountName … 送り主のアカウントのユーザー名（サーバーが末尾に足した。アカウントID を出していない人は null）。
                     trip とは、どちらか一方。受け取ったここで「名前の下の印」1つにまとめる（idMarkOf） */
    chatHub.client.receiveMessage = function (localId, nickname, message, iconType, iconColor, anonId, trip, accountName) {

        trip = idMarkOf(trip, accountName);

        /*
            === 無視している相手の発言は、行そのものを作らない ===

            display: none にはしない（：そこに間を空けない）。

            【ここで返してよい理由】発言の行は、参加者一覧にも人数にも関わらない。
            日付の区切り行も appendMessageRow の中で入るので、ここで抜ければ
            区切りだけが取り残されることもない。

            【「新着あり」も出ない】行を作らないのに知らせだけ出すと、
            押しても何も無いところへ飛ぶ。ここで返せば、それも自然に満たされる。

            【通知音も鳴らない】下の Sounds.message() ごと飛ばしている。
            「行と音は、同じ判断に乗せる」（教訓）
        */
        if (IgnoreList.isIgnored(anonId)) {
            return;
        }

        /*
            リアルタイムで受け取った発言は、今この瞬間の接続で届いたものなので、
            LocalIdで自分かどうかを判定してよい
            （過去ログの場合は、これをしてはいけない。renderInitialLogの説明を参照）
        */
        var isSelf = (localId === myLocalId);

        /*
            リアルタイム受信ぶんは「今」であることが確実なため、
            時刻・日付はクライアント側で作ってよい

            【自分の発言だけは、必ず最下部へ】
            自分が送った発言も、他の人と同じようにここへ返ってくる。
            過去ログを遡って読んでいる最中に発言した場合、追従の判定に任せると
            自分の発言が画面の外に出たままになる。そういうチャットアプリは無い
        */
        appendMessageRow(nickname, message, iconType, iconColor, isSelf,
                         formatTime(new Date()), todayDateText(),
                         isSelf ? SCROLL_FORCE : SCROLL_AUTO, trip);

        /*
            通知音。自分の発言でも鳴らす（自分の発言も、ここへ返ってくる）。
            【ここで鳴らすことに意味がある】ハブから届いたときにだけ鳴らすので、
            過去ログを描き直しても鳴らない（Soundsの説明を参照）
        */
        Sounds.message();

        /*
            発言の保存はDB側（ChatHub → ChatLog.Write）で行われるため、
            ここでクライアント側に保存する処理は不要
        */
    };

    /*
        こっそりの発言が届いたときに呼ばれる（当事者2人にしか届かない）

        【自分が送ったぶんも、ここへ返ってくる】
        部屋あての発言が Clients.Group で自分にも返ってくるのと、同じ形に揃えてある。
        こうしておくと「画面に出す」処理が1本で済み、時刻の作り方も1通りになる
    */
    /*
        trip … 送り主のトリップ（サーバーが末尾に足した。無ければ null）。
        【受け取っている】「1対1で相手が自明」として
        わざと受け取っていなかったが、覆した。、窓の見出しに1か所だけ出す
        （理由は PrivateTalk の buildWindowHtml の見出しの項）
    */
    chatHub.client.receivePrivateMessage = function (localId, nickname, message,
                                                     iconType, iconColor,
                                                     anonId, recipientAnonId,
                                                     recipientNickname, trip, accountName) {

        /* 送り主のトリップかアカウントID（どちらか一方）を、1つの印にまとめる（receiveMessage と同じ） */
        trip = idMarkOf(trip, accountName);

        /*
            【行と音は、同じ判断に乗せる。窓も同じ判断に乗せる】
            無視している相手からのこっそりなら、PrivateTalk.receive が false を返し、
            窓も作られない。その場合、音も鳴らさない。
            無視しているのに窓が立ち上がったら、無視の意味が無い
        */
        var shown = PrivateTalk.receive(localId, nickname, message,
                                        iconType, iconColor,
                                        anonId, recipientAnonId, recipientNickname, trip);

        if (shown) {
            /* 通知音は、本体の発言と同じ音（開発者の指定） */
            Sounds.message();
        }
    };

    /*
        他の人が入室したときに呼ばれる

        このイベントは、自分自身の入室時には届かない（サーバーがOthersInGroupで
        配信しているため）。自分の入室行は、joinRoomAndRenderの中で描画している

        eventTextは「○○さんが入室しました」。文言はサーバーが組み立て済みのため、
        クライアント側では作らず、受け取ったものをそのまま表示する
    */
    /* trip … 入ってきた人のトリップ（サーバーが末尾に足した。無ければ null） */
    /* joinSeq … 入室した順の番号（サーバーが末尾に足した） */
    /* accountName … 入ってきた人のアカウントのユーザー名（末尾に足した。無ければ null） */
    chatHub.client.userJoined = function (localId, nickname, iconType, iconColor, eventText, anonId, trip, joinSeq, accountName) {

        trip = idMarkOf(trip, accountName);

        /*
            参加者一覧の更新は、文言の有無と関係なく必ず行う。
            ここで抜けると一覧に入らなくなり、人数表示までずれる
        */
        addParticipant(localId, nickname, iconType, iconColor, anonId, trip, joinSeq);

        /*
            === 無視している相手の入室 ===

            【一覧への追加は、必ず先に済ませてある】
            参加者一覧は隠さないと決めた（開発者の明示＝一覧には残す。
            居ることは分かる）。ここで早く返すと**一覧に入らなくなり、
            人数表示までずれる**。飛ばしてよいのは「行と音」だけである。

            【行と音は、同じ判断に乗せる】
            種別33（置き換え）の直後の入室など、サーバーが「行として表示すべき
            でない」と判断した場合、eventTextは空で届く。音をこのifの外に置くと、
            行が出ないのに音だけが鳴る（「行を出さない」ことと「配信しない」ことは
            別、というサーバー側の作りが、そのままここへ効いてくる）。
            無視した相手も、まったく同じ形でこの1つの判断に乗せてある
        */
        if (eventText && !IgnoreList.isIgnored(anonId)) {
            appendSystemMessageRow(eventText, todayDateText(), SCROLL_AUTO_QUIET);
            Sounds.enter();
        }

        /*
            その人の窓が開いていたら、また送れるようにする。
            窓が無ければ何も起きない（PrivateTalk の中で判断している）

            【文言の有無とは関係なく呼ぶ】
            置き換え(33)の直後の入室は eventText が空で届くが、
            その人が部屋に居ることに変わりはない。
            上の if の中に入れると、そのとき窓が「退室したまま」で固まる
        */
        PrivateTalk.notifyJoined(anonId, nickname, trip);
    };

    /*
        誰かが退室したときに呼ばれる

        eventTextは「○○さんが退室しました（…）」。退室の理由によって文言が変わるが、
        その組み立てもサーバー側（ChatEventText）が行っている
    */
    chatHub.client.userLeft = function (localId, eventText, anonId) {

        /*
            【名前は、消す前に控えておくこと】
            こっそりの窓へ「◯◯さんが退室しました」と出すのに要る。
            removeParticipant のあとでは、もう一覧に居ない
        */
        var leaving = participants.filter(function (p) {
            return p.localId === localId;
        })[0];

        var leftNickname = leaving ? leaving.nickname : null;

        /*
            参加者一覧の更新は、文言の有無と関係なく必ず行う。
            ここで抜けると一覧から消えなくなり、人数表示までずれる
        */
        removeParticipant(localId);

        /*
            【行と音は、同じ判断に乗せる】
            種別33（置き換え）の退室は、サーバーが「行として表示すべきでない」と
            判断して、空の文言で配信してくる。音をこのifの外に置くと、行が出ないのに
            音だけが鳴る（再接続が(a)置き換えに落ちたときに、他の人の画面で
            「退室音だけ鳴る／行は出ない／人数も変わらない」という形で表面化していた）

            【自分の退室では鳴らない】
            このイベントは他人の退室でしか届かない（自分は退室ボタンを押した時点で
            ラウンジへ移動してしまい、鳴らす場所そのものが無い）。
            移動先のラウンジで鳴らすことは既存のsessionStorageの仕組みでできるが、
            自分で押した直後に移動先で鳴るのは変なので、やらないと決めてある
            （に理由を残してある）

            【無視している相手の退室も、行と音だけを飛ばす】
            一覧からの削除（removeParticipant）は、上で必ず済ませてある。
            ここで早く返すと**一覧から消えなくなり、人数表示までずれる**
        */
        if (eventText && !IgnoreList.isIgnored(anonId)) {
            appendSystemMessageRow(eventText, todayDateText(), SCROLL_AUTO_QUIET);
            Sounds.leave();
        }

        /*
            === 窓には、文言の有無と関係なく知らせる ===

            【なぜ if の外なのか】
            部屋のログでは、置き換え(33)の退室を出さないと決めてある
            （内部の都合を会話に漏らさないため）。しかし窓の側では、
            相手が居なくなったのに送信できたままになるほうが困る。
            **「部屋のログに出すか」と「送信できるか」は、別の判断である**

            【窓は閉じない】
            閉じると、それまでのやりとりが読めなくなる（開発者の指定）。
            送信だけできなくして、その場でお知らせを1行出す
        */
        PrivateTalk.notifyLeft(anonId, leftNickname);
    };

    /*
        サーバー側から強制的に退室させられた時に呼ばれる
        （他のタブ・ウィンドウによる置き換え、無言タイムアウトなど）
        理由はサーバーが文言で渡してくるため、そのままラウンジへ持ち越す
    */
    chatHub.client.kicked = function (message) {

        /* 正常な離脱のため、このあとのdisconnectedで切断の警告を出さないようにする */
        markLeavingRoom();

        /*
            ここではalertを出さない。背面のタブでは表示が抑制されてしまうため、
            理由を保存してラウンジへ持ち越し、移動先で帯として表示する
        */
        saveLeaveReason(message);

        /*
            【罠】ここで Session("CurrentRoomId") をクリアしてはいけない。

            以前はここで /Chat/ChatRoom.aspx/LeaveRoom を呼んでSessionを消していた。
            しかしSessionは、タブごとではなく「ブラウザ単位」で共有されている。
            蹴られた古いタブが消すSessionは、生き残った新しいタブのものでもある。

            置き換え（別のタブで同じ部屋を開いた）の場合、新しいタブは正当に
            その部屋にいる。にもかかわらず、死んでいく古いタブがSessionを
            消してしまうため、新しいタブでF5を押すとPage_Loadが
            「Sessionが無い」と判定し、ラウンジへ飛ばしていた。

            これは、LeaveBeacon.aspx.vbでSessionのクリアを取り除いたのと
            まったく同じ形の不具合である（死んでいくタブが、共有のSessionを
            消してから死ぬ）。何も書かないと、将来また同じ行が足される。

            消さないことで失うのは「蹴られた人がURL直打ちで部屋へ戻れてしまう」
            ことだけ。そもそもSessionを消すことは定員の防御にはならない。
            定員はChatHub.JoinRoomがサーバー側で判定しており、部屋のページを
            開けたかどうかとは関係なく、満員なら入室そのものが断られる
            （この点もビーコン側とまったく同じ）
        */
        window.location.href = '/chat/lounge';
    };

    /*
        === ノック===

        knockReceived  … 誰かが満室のこの部屋にノックした（部屋の全員に届く）。お知らせの行と音
        knockRequested … 自分が管理人のときだけ届く。許可を聞く小窓（KnockRequests）
        knockWithdrawn … その小窓が要らなくなった（時間切れ・その人が普通に入ってきた、など）

        【行と音は、同じ判断に乗せる】無視している相手のノックなら、行も音も出さない（入退室と同じ形）。
        ノックした人は部屋に居ないが、同じブラウザなら部屋に居たときと同じ匿名IDになるので、無視が効く

        【過去ログには出ない】ノックはその場限りの頼みごとなので、この配信でだけ出す（ChatEventText のコメント）
    */
    chatHub.client.knockReceived = function (eventText, anonId) {

        if (eventText && !IgnoreList.isIgnored(anonId)) {
            appendSystemMessageRow(eventText, todayDateText(), SCROLL_AUTO_QUIET);
            Sounds.knock();
        }
    };

    chatHub.client.knockRequested = function (info) {
        KnockRequests.add(info);
    };

    /*
        ノックの小窓を消す／理由に差し替える（⑥）

        text       … 消える理由（「〇〇さんのノックは、時間切れで取り消されました」など。サーバーが作る）。
                     空なら黙って消す（その人が空いた席から普通に入ってきた）
        anonId     … ノックした人の匿名ID（無視している相手なら、理由も出さない）
        standalone … 小窓がもう無くても、知らせだけ出すか（許可したあとの取り置きが外れた）
    */
    chatHub.client.knockWithdrawn = function (knockId, text, anonId, standalone) {
        KnockRequests.withdraw(knockId, text, anonId, standalone);
    };

    /*
        === 見た目（部屋に居ながら、自分のアイコンと色を変える）===

        置き場所は設定パネルの「見た目」の行（Chat/ChatRoom.aspx）。サーバーは ChatHub.ChangeAppearance。

        【名前は変えられない】名前は身元そのもの。色とアイコンは見た目だけ。
        変えても部屋のログにお知らせ行は出さない。過去ログの色とアイコンも変わらない（発言の行に焼き込まれている）。
        変わるのは、参加者一覧・自分のバー・ラウンジの顔（他の人の画面でも）と、このあとの発言。

        【受け付けられてから Cookie を書く】サーバーと食い違わせないため。
        受け付けられる前の見た目は、このパネルの中（色の見本・選んだアイコン）にしか出さない。
        再接続の JoinRoom は myIconType / myIconColor を送るので、それも受け付けられてから書き換える。

        【送る回数を抑える】
        ・アイコンを押す・色を選び終える（jscolor の change）たびに、少し待ってから（SEND_DELAY_MS）送る。
          続けて押したら、最後の1つだけが送られる
        ・送っている最中は、次を送らない（返事が来てから、そのとき選んでいるものを送る）
        ・サーバーが連打で断ったら（retryAfterMs）、その時間だけ待って、そのとき選んでいるものを送り直す
        ・今と同じなら送らない

        【jscolor の input と change】（jscolor 2.x の中身で確かめた）
        input … つまみを動かしている間、何度も起きる → パネルの中の見本にだけ使う（送らない）
        change … 手を離したとき・パレットの色を1つ押したときに1回。**ピッカーを閉じたときではない**
                 （閉じる合図は jscolor に無い）→ これで送る。何度か選び直しても、上の「少し待って最後の1つ」で1回にまとまる

        【色を選ぶ道具（jscolor 62KB）は、色のボタンを初めて押したときに取りに行く】
        取りに行っている間はボタンを「読み込み中…」にし、取れなかったら理由を出して、もう一度押せるようにする。
        **取れなくても部屋は止まらない**（アイコンの並びは jscolor を使わない。アイコンのスプライトと同じフェイルオープン）
    */
    var Appearance = (function () {

        var SEND_DELAY_MS = 500;
        var LOAD_TIMEOUT_MS = 15000;

        /* 色を選ぶ道具の色見本（トップページ Default.aspx の jscolor.presets.default と同じ12色） */
        var PALETTE = [
            '#FF0000', '#FF6B6B', '#FF9900', '#FFE66D',
            '#22B14B', '#4ECDC4', '#00A1E7', '#3F47CC',
            '#A349A4', '#95E1D3', '#7d7d7d', '#000000'
        ];

        var colorBtn = document.getElementById('appearanceColorBtn');
        var colorSwatch = document.getElementById('appearanceColorSwatch');
        var colorText = document.getElementById('appearanceColorText');
        var iconGroup = document.getElementById('appearanceIcons');
        var statusEl = document.getElementById('appearanceStatus');

        /* アイコンの小窓 */
        var iconBtn = document.getElementById('appearanceIconBtn');
        var iconBtnUse = document.getElementById('appearanceIconBtnUse');
        var iconPanel = document.getElementById('appearanceIconPanel');

        /* サーバーが受け付けている見た目（＝Cookie・自分のバー） */
        var current = { type: myIconType, color: myIconColor };

        /* 今この画面で選んでいる見た目（まだ受け付けられていないかもしれない） */
        var desired = { type: myIconType, color: myIconColor };

        var sendTimer = null;
        var retryTimer = null;
        var isSending = false;

        /* 色を選ぶ道具の状態：'idle'（まだ取りに行っていない）／'loading'／'ready'／'failed' */
        var pickerState = 'idle';
        var picker = null;

        function sameColor(a, b) {
            return String(a || '').toUpperCase() === String(b || '').toUpperCase();
        }

        function sameAppearance(a, b) {
            return a.type === b.type && sameColor(a.color, b.color);
        }

        function choices() {
            return iconGroup ? Array.prototype.slice.call(iconGroup.querySelectorAll('.appearance-icon-choice')) : [];
        }

        function setStatus(text, isError) {
            if (!statusEl) {
                return;
            }
            statusEl.textContent = text || '';
            statusEl.classList.toggle('is-error', !!(text && isError));
        }

        /* パネルの中を、今選んでいる見た目で描き直す（色の見本と、アイコンの並び） */
        function renderPanel(previewColor) {

            var color = previewColor || desired.color;

            if (colorSwatch) {
                colorSwatch.style.backgroundColor = color || '';
            }

            /* 「見た目」の行のアイコンのボタン（今のアイコンと色） */
            if (iconBtnUse) {
                iconBtnUse.setAttribute('href', '#icon-' + desired.type);
            }
            if (iconBtn) {
                iconBtn.style.color = color || '';
            }

            var list = choices();
            var hasChecked = false;

            list.forEach(function (el) {
                var isChecked = (el.getAttribute('data-icon') === desired.type);
                hasChecked = hasChecked || isChecked;
                el.setAttribute('aria-checked', isChecked ? 'true' : 'false');
                el.tabIndex = isChecked ? 0 : -1;
                el.style.color = isChecked ? (color || '') : '';
                el.style.borderColor = isChecked ? (color || '') : '';
            });

            /* 今のアイコンが一覧に無い（止めたアイコン、など）ときも、Tab で並びへ入れるようにする */
            if (!hasChecked && list.length > 0) {
                list[0].tabIndex = 0;
            }
        }

        /* 自分のプロフィールバー（入室したときに Cookie の値で描いているもの） */
        function renderMyAvatar() {
            $('#myAvatar svg').css('color', myIconColor);
            $('#myAvatar svg use').attr('href', '#icon-' + myIconType);
        }

        /* 参加者一覧の1人ぶんを置き換える（自分・他の人の共通）。変わったときだけ描き直す */
        function applyToParticipant(localId, iconType, iconColor) {

            var changed = false;

            participants.forEach(function (p) {
                if (p.localId === localId && (p.iconType !== iconType || p.iconColor !== iconColor)) {
                    p.iconType = iconType;
                    p.iconColor = iconColor;
                    changed = true;
                }
            });

            if (changed) {
                renderParticipantList();
            }
        }

        /* サーバーが受け付けた。Cookie・自分のバー・一覧の自分を書き換える */
        function applyAccepted(sent, iconType, iconColor) {

            current = { type: iconType, color: iconColor };

            myIconType = iconType;
            myIconColor = iconColor;
            setCookie('ChatIconType', iconType);
            setCookie('ChatIconColor', iconColor);

            renderMyAvatar();

            if (myLocalId !== null) {
                applyToParticipant(myLocalId, iconType, iconColor);
            }

            /*
                送った値のまま（返事を待つ間に選び直していない）なら、受け付けられた値に揃える
                （サーバーが一覧に無いアイコン・形の違う色を既定へ落としたときも、画面がそれに合う）
            */
            if (sameAppearance(desired, sent)) {
                desired = { type: iconType, color: iconColor };
                renderPanel();
                if (picker) {
                    picker.fromString(iconColor);
                }
            }
        }

        /* 送れなかった。パネルの中を、受け付けられている見た目へ戻す */
        function revert(message) {
            desired = { type: current.type, color: current.color };
            renderPanel();
            if (picker) {
                picker.fromString(current.color);
            }
            setStatus(message, true);
        }

        function schedule() {
            if (sendTimer) {
                clearTimeout(sendTimer);
            }
            sendTimer = setTimeout(function () {
                sendTimer = null;
                send();
            }, SEND_DELAY_MS);
        }

        function send() {

            if (isSending || retryTimer) {
                return;
            }

            if (sameAppearance(desired, current)) {
                return;
            }

            var sent = { type: desired.type, color: desired.color };

            isSending = true;
            setStatus('');

            chatHub.server.changeAppearance(sent.type, sent.color).done(function (result) {

                isSending = false;

                if (result && result.accepted) {
                    applyAccepted(sent, result.iconType || sent.type, result.iconColor || sent.color);
                    /* 返事を待つ間に選び直していたら、そちらを送る */
                    send();
                    return;
                }

                if (result && result.retryAfterMs > 0) {
                    /* 続けて変えすぎた。その時間だけ待って、そのとき選んでいるものを送り直す */
                    retryTimer = setTimeout(function () {
                        retryTimer = null;
                        send();
                    }, result.retryAfterMs + 50);
                    return;
                }

                /* 部屋に居ない（再接続の途中、など）。画面の操作では起こらない */
                revert('見た目を変えられませんでした。少し待ってから、もう一度お試しください。');

            }).fail(function () {
                isSending = false;
                revert('見た目を変えられませんでした。接続が戻ってから、もう一度お試しください。');
            });
        }

        /* === アイコンの小窓 ===

            それまでは20個を「見た目」の行の下に並べていた。要望：
            アイコンはこれからも増えるので、色と同じく、押したら小窓が開く形にする。

            【1つ選んだら、その場で変えて小窓を閉じる】色と違って、ドラッグで探るものではないため。

            【小窓の中の選び方＝矢印で移る → Enter / Space（かクリック）で決める】
            ふつうのラジオボタンは「矢印で移った時点で選ばれる」が、ここでは**選んだ瞬間に小窓が閉じる**ので、
            それだと矢印を1回押しただけで閉じてしまい、見比べられない。
            そのため矢印は焦点を動かすだけにして、決めるのは Enter / Space / クリックにしてある
            （aria-checked が動くのも、決めたときだけ。ARIA も「選ぶことに結果が伴う場合」はこの形を認めている）。
            Tab で止まるのは、今選んでいる1つだけ（roving tabindex）＝そこは今までどおり。

            【置き場所（position: fixed）】設定パネルは中がスクロールするので、absolute だと切り落とされる。
            見た目は Content/ChatRoom.css の .appearance-icon-panel
        */
        var ICON_PANEL_EDGE = 8;

        function isIconPanelOpen() {
            return !!(iconPanel && !iconPanel.hidden);
        }

        /* ボタンのすぐ下に置く（画面からはみ出すときは、内側へ寄せる） */
        function placeIconPanel() {

            if (!iconPanel || !iconBtn) {
                return;
            }

            var rect = iconBtn.getBoundingClientRect();

            iconPanel.style.left = '0px';
            iconPanel.style.top = '0px';

            var width = iconPanel.offsetWidth;
            var height = iconPanel.offsetHeight;

            var left = Math.min(rect.left, window.innerWidth - width - ICON_PANEL_EDGE);
            left = Math.max(left, ICON_PANEL_EDGE);

            var top = rect.bottom + 4;

            /* 下に入らなければ上へ。上にも入らなければ、画面の中へ押し込む */
            if (top + height > window.innerHeight - ICON_PANEL_EDGE) {
                if (rect.top - 4 - height > ICON_PANEL_EDGE) {
                    top = rect.top - 4 - height;
                } else {
                    top = Math.max(ICON_PANEL_EDGE, window.innerHeight - height - ICON_PANEL_EDGE);
                }
            }

            iconPanel.style.left = Math.round(left) + 'px';
            iconPanel.style.top = Math.round(top) + 'px';
        }

        function openIconPanel() {

            if (!iconPanel || isIconPanelOpen()) {
                return;
            }

            /* 【開くたびに描き直す】前に開いたとき、決めずに矢印で動かしたままなら、
               Tab で止まる場所（tabIndex 0）が選んでいるものからずれている。ここで戻す */
            renderPanel();

            iconPanel.hidden = false;
            iconBtn.setAttribute('aria-expanded', 'true');
            placeIconPanel();

            /* 今選んでいるものへ焦点を置く（そこから矢印で動かせる） */
            var checked = iconGroup ? iconGroup.querySelector('.appearance-icon-choice[aria-checked="true"]') : null;
            var first = checked || (choices()[0] || null);

            if (first) {
                first.focus();
            }
        }

        function closeIconPanel(moveFocus) {

            if (!isIconPanelOpen()) {
                return;
            }

            iconPanel.hidden = true;
            iconBtn.setAttribute('aria-expanded', 'false');

            if (moveFocus && iconBtn) {
                iconBtn.focus();
            }
        }

        if (iconBtn) {
            iconBtn.addEventListener('click', function () {
                if (isIconPanelOpen()) {
                    closeIconPanel(true);
                } else {
                    openIconPanel();
                }
            });
        }

        /* 決める（クリック・Enter・Space）。選んで、送って、小窓を閉じる */
        function commitIcon(el) {

            desired.type = el.getAttribute('data-icon');
            renderPanel();
            schedule();
            closeIconPanel(true);
        }

        if (iconGroup) {

            iconGroup.addEventListener('click', function (e) {
                var el = e.target.closest ? e.target.closest('.appearance-icon-choice') : null;
                if (el) {
                    commitIcon(el);
                }
            });

            iconGroup.addEventListener('keydown', function (e) {

                var list = choices();
                var index = list.indexOf(document.activeElement);

                if (index < 0) {
                    return;
                }

                /* 決める */
                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    commitIcon(list[index]);
                    return;
                }

                /* 移るだけ（ここでは選ばない。上の「小窓の中の選び方」） */
                var next = null;

                if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
                    next = list[(index + 1) % list.length];
                } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
                    next = list[(index - 1 + list.length) % list.length];
                } else if (e.key === 'Home') {
                    next = list[0];
                } else if (e.key === 'End') {
                    next = list[list.length - 1];
                }

                if (next) {
                    e.preventDefault();
                    /* 【Tab で止まる場所は、いつも1つだけ】移った先を 0 にして、ほかは -1 に戻す
                       （roving tabindex）。aria-checked は動かさない＝決めたときだけ動く */
                    list.forEach(function (el) { el.tabIndex = (el === next) ? 0 : -1; });
                    next.focus();
                }
            });
        }

        /* 画面が動いたら置き直す（fixed なので、放っておくと取り残される） */
        window.addEventListener('resize', function () {
            if (isIconPanelOpen()) {
                placeIconPanel();
            }
        });

        /* === 色を選ぶ道具（初めて押したときに取りに行く） === */
        function setButtonLoading(isLoading) {
            if (!colorBtn) {
                return;
            }
            if (isLoading) {
                colorBtn.setAttribute('aria-busy', 'true');
                colorText.textContent = '読み込み中…';
            } else {
                colorBtn.removeAttribute('aria-busy');
                colorText.textContent = '色';
            }
        }

        function createPicker() {

            picker = new window.JSColor(colorBtn, {
                value: desired.color,
                format: 'hex',
                alphaChannel: false,

                /* 見本はこちらで描く（jscolor に任せると、ボタンの背景に色の帯を描き込む） */
                previewElement: null,
                valueElement: null,

                /*
                    【開くのは、こちらの click で行う】
                    jscolor 自身の「押したら開く」は、mousedown（タッチなら touchstart）で
                    **押された要素そのもの（e.target）が、jscolor を付けた要素のときだけ**開く。親はたどらない。
                    このボタンの中には色見本と「色」の2つの span があり、そこを押すと e.target が span になって開かない
                    （開いている間なら、逆に閉じる）。ボタンの縁を押したときだけ開いていた＝「開いたり開かなかったりする」。
                    トップページで起きないのは、あちらの jscolor が子を持たない <input> に付いているから。
                    だから jscolor には開かせず、下の click で picker.show() を呼ぶ（click はボタンのどこを押しても、
                    キーボードの Enter・Space でも来る。それまではキーボードでは2度目から開けなかった）
                */
                showOnClick: false,

                palette: PALETTE,
                paletteCols: 6,
                width: 181,
                height: 101,
                closeButton: true,
                closeText: '閉じる',

                /* 動かしている間：パネルの中の見本だけ */
                onInput: function () {
                    renderPanel(this.toHEXString());
                },

                /* 選び終えた：少し待ってから送る */
                onChange: function () {
                    desired.color = this.toHEXString();
                    renderPanel();
                    schedule();
                }
            });
        }

        function loadPicker() {

            pickerState = 'loading';
            setButtonLoading(true);
            setStatus('色を選ぶ道具を読み込んでいます…');

            var script = document.createElement('script');
            var finished = false;

            function finish(ok) {

                if (finished) {
                    return;
                }
                finished = true;
                clearTimeout(timer);
                setButtonLoading(false);

                if (ok && window.JSColor) {
                    try {
                        createPicker();
                        pickerState = 'ready';
                        setStatus('');
                        /* 取りに行っている間に設定パネルを閉じていたら、開かない */
                        if ($('#settingsPanel').hasClass('is-open')) {
                            picker.show();
                        }
                        return;
                    } catch (ex) {
                        picker = null;
                    }
                }

                /* 取れなかった。もう一度押せば取りに行き直す（部屋はそのまま使える） */
                pickerState = 'failed';
                if (script.parentNode) {
                    script.parentNode.removeChild(script);
                }
                setStatus('色を選ぶ道具を読み込めませんでした。もう一度押すと、読み込み直します（アイコンは選べます）。', true);
            }

            var timer = setTimeout(function () { finish(false); }, LOAD_TIMEOUT_MS);

            script.src = $('#jscolorUrl').val();
            script.async = true;
            script.onload = function () { finish(true); };
            script.onerror = function () { finish(false); };
            document.head.appendChild(script);
        }

        if (colorBtn) {
            colorBtn.addEventListener('click', function () {
                /*
                    取れたあとも、開くのはここ（jscolor の showOnClick は切ってある。上の createPicker の理由）。
                    開いているときに押しても、開いたまま（閉じるのは「閉じる」・外を押す・Esc）
                */
                if (pickerState === 'ready' && picker) {
                    picker.show();
                } else if (pickerState === 'idle' || pickerState === 'failed') {
                    loadPicker();
                }
            });
        }

        renderPanel();

        return {

            /* 配信：誰か（自分も含む）が見た目を変えた。参加者一覧のその人を置き換える */
            onAppearanceChanged: function (localId, iconType, iconColor) {
                applyToParticipant(localId, iconType, iconColor);
            },

            /* 色を選ぶ道具が開いているか（Esc で、設定パネルより先にこちらを閉じるため） */
            isPickerOpen: function () {
                return !!document.querySelector('.jscolor-wrap');
            },

            hidePicker: function () {
                if (picker) {
                    picker.hide();
                }
            },

            /* アイコンの小窓。色の道具とまったく同じ扱いにしてある
               ——Esc は設定パネルより先にこちらを閉じ、設定パネルを閉じるときは一緒に閉じる */
            isIconPanelOpen: isIconPanelOpen,

            hideIconPanel: function (moveFocus) {
                closeIconPanel(moveFocus);
            }
        };
    })();

    /*
        === 部屋の設定（管理人だけ。同じ日の夜に 作り替えた）===

        置き場所は設定パネルのいちばん下（Chat/ChatRoom.aspx の #roomSettings）。サーバーは ChatHub.ChangeRoomSettings。

        【管理人にだけ出す】自分の番号（myLocalId）が管理人の番号（ownerLocalId）と同じときだけ hidden を外す
        （renderMyOwnerBadge と同じ条件・同じ時に呼ぶ＝管理人が替わったら、その場で出し入れ）。
        **出さないことは防御ではない**。管理人かどうかは、ハブが必ず確かめる。

        【その場で反映する】（保存ボタンをやめた）
        指摘：ほかの場所はトグルを動かしただけで変更が反映されるのに、
        管理人の設定だけ保存ボタンを押さないと反映されないのは、そろっていない。
          ・定員 … 選んだとき
          ・ノック・画像 … スイッチを動かしたとき
          ・鍵 … 切ったときに外す。入れたときは、パスワードを入れて「かける」（か Enter）を押したときにかける
          ・部屋名 … 「変える」（か Enter）を押したとき（打っている途中で送らない）
        ハブの形は5つまとめて受け取るまま。**今の値に、変えた項目だけを差し替えて送る**（サーバーは今の値と比べて、変わった項目だけを扱う）。

        【送るのは1本ずつ】返事が来る前に別の項目を触ったら、待たせてまとめて送る（queue）。
        同じ項目を続けて変えると、サーバーが「少し待って」（retryAfterMs）と返すので、その時間だけ待ってから送り直す
        （見た目の変更と同じやり方）。断られたら、スイッチ・定員を今の値へ戻して理由を出す。

        【部屋のログの1行は、サーバーがまとめる】変更が止まってから数秒後に1行（roomSettingsChanged の eventText だけが届く）。

        【今の設定は、配信（roomSettingsChanged）で揃える】部屋の全員に届く（管理人でない人も、管理人になったときに
        今の値で欄が出るように持っておく）。変えた管理人の画面も、同じ配信と戻り値で揃う（どちらも「置き換える」だけ）。
        **ただし、打っている途中の部屋名・新しいパスワードの欄は、配信で書き換えない**（ほかの項目を変えたときに消えないように）。

        【今のパスワードは、管理人には見える】最初は伏せ字。「見る」を押したときだけハブ（getRoomPassword）から
        1回もらう（部屋の全員への配信には載らない）。パネルを閉じたとき・管理人でなくなったときは、また伏せる
    */
    var RoomSettings = (function () {

        var box = document.getElementById('roomSettings');

        if (!box) {
            return {
                refreshVisibility: function () { },
                apply: function () { },
                onPanelOpen: function () { },
                onPanelClose: function () { }
            };
        }

        var nameInput = document.getElementById('roomSettingName');
        var nameApplyBtn = document.getElementById('roomSettingNameApply');
        var maxSelect = document.getElementById('roomSettingMaxUsers');
        var knockToggle = document.getElementById('roomSettingKnock');
        var imageToggle = document.getElementById('roomSettingImage');
        var lockToggle = document.getElementById('roomSettingLock');
        var currentRow = document.getElementById('roomSettingPasswordCurrent');
        var currentValue = document.getElementById('roomSettingPasswordValue');
        var revealBtn = document.getElementById('roomSettingPasswordReveal');
        var passwordInput = document.getElementById('roomSettingPassword');
        var passwordApplyBtn = document.getElementById('roomSettingPasswordApply');
        var statusEl = document.getElementById('roomSettingsStatus');

        var MASKED_TEXT = '●●●●●●';

        /* サーバーが受け付けている今の設定（最初はページが DB から読んだ値） */
        var current = {
            roomName: box.getAttribute('data-room-name') || '',
            maxUsers: parseInt(box.getAttribute('data-max-users'), 10) || 0,
            knockEnabled: box.getAttribute('data-knock') === '1',
            imageEnabled: box.getAttribute('data-image') === '1',
            hasPassword: box.getAttribute('data-has-password') === '1'
        };

        /* まだ送っていない変更（項目名 → 値）。送っている最中・待っている最中に触った分が溜まる */
        var queue = null;
        var inFlight = false;
        var retryTimer = null;

        /* 今のパスワードを見せているか（見せている間は、パスワードそのものが画面にある） */
        var revealed = false;
        var revealing = false;

        /*
            === 入室禁止にした人===

            「部屋の設定」のいちばん下に一覧（#roomBanList）。1人ずつ［アイコン 名前（トリップ）］［解除］。
            要望：間違えて押したときに戻せるように（作りは一任）。

            【一覧は、設定パネルを開いたとき・管理人になったときに取りに行く】（chatHub.server.getRoomBans）。
            配信は無い（変えられるのは管理人だけなので、管理人の画面が開くたびに取れば足りる）。
            入室禁止にしたあとに設定パネルを開けば、そこで新しい一覧になる（参加者一覧と設定パネルは同時に開かないため）。
            【解除は確認なしで、その場で】取り消しの操作なので、確認を挟まない（また禁止にしたければ、その人が居るときに押せばよい）。
            【ブラウザの識別子は無い】1人ぶんは匿名ID（anonId）で指す。名前は textContent で入れる
        */
        var banListEl = document.getElementById('roomBanList');
        var banEmptyEl = document.getElementById('roomBanEmpty');
        var banStatusEl = document.getElementById('roomBansStatus');
        var bansLoading = false;
        var liftingAnonId = null;

        function setBanStatus(text, isError) {
            banStatusEl.textContent = text || '';
            banStatusEl.classList.toggle('is-error', !!(text && isError));
        }

        function renderBans(bans) {

            var list = bans || [];

            while (banListEl.firstChild) {
                banListEl.removeChild(banListEl.firstChild);
            }

            list.forEach(function (ban) {

                var li = document.createElement('li');
                li.className = 'room-ban-item';

                var avatar = document.createElement('span');
                avatar.className = 'room-ban-avatar';
                avatar.setAttribute('aria-hidden', 'true');
                avatar.innerHTML = '<svg viewBox="0 0 512 512" style="color: ' + escapeAttr(ban.iconColor || '') + ';">' +
                    '<use href="#icon-' + escapeAttr(ban.iconType || '') + '"></use></svg>';

                var name = document.createElement('span');
                name.className = 'room-ban-name';
                name.textContent = ban.nickname || '';

                li.appendChild(avatar);
                li.appendChild(name);

                /* トリップは、ほかの場所と同じ形（.trip-code ＋ data-trip で1桁目の色。読み上げは「トリップ 」を添える） */
                if (isTripValue(ban.trip)) {
                    var trip = document.createElement('span');
                    trip.className = 'trip-code room-ban-trip';
                    trip.setAttribute('data-trip', ban.trip);
                    var tripLabel = document.createElement('span');
                    tripLabel.className = 'visually-hidden';
                    tripLabel.textContent = 'トリップ ';
                    trip.appendChild(tripLabel);
                    trip.appendChild(document.createTextNode(ban.trip));
                    li.appendChild(trip);
                }

                var liftBtn = document.createElement('button');
                liftBtn.type = 'button';
                liftBtn.className = 'room-setting-btn room-setting-btn--quiet room-ban-lift-btn';
                liftBtn.textContent = '解除';
                liftBtn.setAttribute('aria-label', (ban.nickname || '') + 'さんの入室禁止を解除');
                liftBtn.setAttribute('data-anon-id', ban.anonId || '');
                liftBtn.disabled = (liftingAnonId !== null);

                li.appendChild(liftBtn);
                banListEl.appendChild(li);
            });

            banListEl.hidden = (list.length === 0);
            banEmptyEl.hidden = (list.length !== 0);
        }

        function loadBans() {

            if (bansLoading || box.hidden) {
                return;
            }

            bansLoading = true;

            chatHub.server.getRoomBans()
                .done(function (result) {
                    if (result && result.ok) {
                        renderBans(result.bans);
                        setBanStatus('');
                    } else {
                        setBanStatus((result && result.message) || '一覧を読めませんでした。', true);
                    }
                })
                .fail(function () {
                    setBanStatus('一覧を読めませんでした。接続が戻ってから、設定を開き直してください。', true);
                })
                .always(function () {
                    bansLoading = false;
                });
        }

        banListEl.addEventListener('click', function (e) {

            var btn = e.target.closest ? e.target.closest('.room-ban-lift-btn') : null;

            if (!btn || liftingAnonId !== null) {
                return;
            }

            liftingAnonId = btn.getAttribute('data-anon-id') || '';

            var nameEl = btn.parentNode ? btn.parentNode.querySelector('.room-ban-name') : null;
            var liftedName = nameEl ? nameEl.textContent : '';

            Array.prototype.forEach.call(banListEl.querySelectorAll('.room-ban-lift-btn'), function (b) {
                b.disabled = true;
            });

            chatHub.server.liftRoomBan(liftingAnonId)
                .done(function (result) {
                    liftingAnonId = null;
                    if (result && result.ok) {
                        renderBans(result.bans);
                        setBanStatus(liftedName ? liftedName + 'さんの入室禁止を解除しました。' : '解除しました。');
                        /*
                            焦点を戻す先（押したボタンは消えている）。残っていれば次の人の「解除」、
                            誰も居なくなったら「いません」の1行（tabindex="-1"）。どちらも無くすと焦点が迷子になる
                        */
                        var nextBtn = banListEl.querySelector('.room-ban-lift-btn');
                        if (nextBtn) {
                            nextBtn.focus();
                        } else {
                            banEmptyEl.focus();
                        }
                    } else {
                        renderBans(null);
                        setBanStatus((result && result.message) || '解除できませんでした。', true);
                        loadBans();
                    }
                })
                .fail(function () {
                    liftingAnonId = null;
                    setBanStatus('解除できませんでした。接続が戻ってから、もう一度お試しください。', true);
                    loadBans();
                });
        });

        function setStatus(text, isError) {
            statusEl.textContent = text || '';
            statusEl.classList.toggle('is-error', !!(text && isError));
        }

        function merge(base, add) {
            var out = {};
            var key;
            for (key in base) {
                if (Object.prototype.hasOwnProperty.call(base, key)) { out[key] = base[key]; }
            }
            for (key in add) {
                if (Object.prototype.hasOwnProperty.call(add, key)) { out[key] = add[key]; }
            }
            return out;
        }

        function has(obj, key) {
            return !!obj && Object.prototype.hasOwnProperty.call(obj, key);
        }

        /* === 今のパスワード（伏せ字・見る・隠す） === */

        function hidePassword() {
            revealed = false;

            /*
                【●を6つ読ませない】（読み上げの点検の回）
                この欄は aria-live なので、伏せ字に戻した瞬間にも読まれる。
                そのまま入れると「黒丸 黒丸 黒丸…」になるだけなので、
                目には●、読み上げには「伏せています」を渡す。
                中身は決まった文字列だけ（利用者の文字は混ぜない）
            */
            currentValue.textContent = '';

            var dots = document.createElement('span');
            dots.setAttribute('aria-hidden', 'true');
            dots.textContent = MASKED_TEXT;
            currentValue.appendChild(dots);

            var said = document.createElement('span');
            said.className = 'visually-hidden';
            said.textContent = '伏せています';
            currentValue.appendChild(said);

            currentValue.classList.remove('is-message');
            revealBtn.textContent = '見る';
            revealBtn.setAttribute('aria-pressed', 'false');
        }

        function showPassword(text) {
            revealed = true;
            currentValue.textContent = text;
            currentValue.classList.remove('is-message');
            revealBtn.textContent = '隠す';
            revealBtn.setAttribute('aria-pressed', 'true');
        }

        /* 見せられない理由（「表示できません」など）。伏せ字の場所に出す */
        function showPasswordMessage(text) {
            revealed = false;
            currentValue.textContent = text;
            currentValue.classList.add('is-message');
            revealBtn.textContent = '見る';
            revealBtn.setAttribute('aria-pressed', 'false');
        }

        revealBtn.addEventListener('click', function () {

            if (revealed) {
                hidePassword();
                return;
            }

            if (revealing) {
                return;
            }

            revealing = true;
            revealBtn.disabled = true;

            chatHub.server.getRoomPassword()
                .done(function (result) {
                    if (result && result.ok) {
                        showPassword(result.password || '');
                    } else {
                        showPasswordMessage((result && result.message) || '今は表示できません。');
                    }
                })
                .fail(function () {
                    showPasswordMessage('今は表示できません。接続が戻ってから、もう一度お試しください。');
                })
                .always(function () {
                    revealing = false;
                    revealBtn.disabled = false;
                });
        });

        /* === 欄を今の設定で揃える === */

        /* パスワードの欄の使える・使えない、案内、ボタンの文字（鍵のスイッチと、今かかっているかで変わる） */
        function syncPasswordField() {
            var on = lockToggle.checked;
            passwordInput.disabled = !on;
            passwordApplyBtn.disabled = !on;
            passwordApplyBtn.textContent = current.hasPassword ? 'かけ直す' : 'かける';
            passwordInput.placeholder = !on
                ? '鍵をかけるときに入れます'
                : (current.hasPassword
                    ? 'かけ直すときだけ入れる'
                    : 'パスワードを入れて「かける」');
            currentRow.hidden = !current.hasPassword;
        }

        /*
            スイッチ・定員を今の値へ（まだ送っていない変更がある項目は、触らない）。
            部屋名の欄は、打ちかけでなければ（＝前の今の値のままなら）書き換える
        */
        function syncControls(previousName) {

            if (!has(queue, 'maxUsers')) {
                maxSelect.value = String(current.maxUsers);
            }
            if (!has(queue, 'knockEnabled')) {
                knockToggle.checked = current.knockEnabled;
            }
            if (!has(queue, 'imageEnabled')) {
                imageToggle.checked = current.imageEnabled;
            }
            /* 鍵のスイッチ：入れてパスワードを入れる途中（かかっていないのにオン）は、そのままにする */
            if (!has(queue, 'lock') && !(lockToggle.checked && !current.hasPassword)) {
                lockToggle.checked = current.hasPassword;
            }
            if (typeof previousName === 'string' && nameInput.value === previousName) {
                nameInput.value = current.roomName;
            }

            syncPasswordField();
        }

        /* すべての欄を今の値に戻す（書きかけも捨てる）。管理人になった・外れたとき */
        function resetForm() {
            queue = null;
            nameInput.value = current.roomName;
            passwordInput.value = '';
            maxSelect.value = String(current.maxUsers);
            knockToggle.checked = current.knockEnabled;
            imageToggle.checked = current.imageEnabled;
            lockToggle.checked = current.hasPassword;
            hidePassword();
            syncPasswordField();
        }

        /* 断られたときに、その項目の欄を今の値へ戻す（打った部屋名・パスワードの文字は残す＝直して押し直せる） */
        function revertControls(change) {
            if (has(change, 'maxUsers')) { maxSelect.value = String(current.maxUsers); }
            if (has(change, 'knockEnabled')) { knockToggle.checked = current.knockEnabled; }
            if (has(change, 'imageEnabled')) { imageToggle.checked = current.imageEnabled; }
            if (has(change, 'lock') && !change.lock) { lockToggle.checked = current.hasPassword; }
            syncPasswordField();
        }

        /* === 送る（1本ずつ） === */

        function send(change) {
            queue = merge(queue || {}, change);
            pump();
        }

        function pump() {

            if (inFlight || retryTimer || !queue) {
                return;
            }

            var change = queue;
            queue = null;

            var wanted = {
                roomName: has(change, 'roomName') ? change.roomName : current.roomName,
                maxUsers: has(change, 'maxUsers') ? change.maxUsers : current.maxUsers,
                knockEnabled: has(change, 'knockEnabled') ? change.knockEnabled : current.knockEnabled,
                imageEnabled: has(change, 'imageEnabled') ? change.imageEnabled : current.imageEnabled,
                lock: has(change, 'lock') ? change.lock : current.hasPassword,
                password: has(change, 'password') ? change.password : ''
            };

            inFlight = true;
            setStatus('変えています…');

            chatHub.server.changeRoomSettings(wanted.roomName, wanted.maxUsers, wanted.knockEnabled,
                                              wanted.imageEnabled, wanted.lock, wanted.password)
                .done(function (result) {

                    if (result && result.ok) {

                        if (result.settings) {
                            apply(result.settings, has(change, 'roomName'));
                        }

                        /* かけた・かけ直したパスワードは、自分で打ったもの。見せている最中なら、新しいほうに差し替える */
                        if (has(change, 'password') && change.password) {
                            passwordInput.value = '';
                            if (revealed) {
                                showPassword(change.password.trim());
                            }
                        }

                        setStatus(queue ? '' : (result.changed ? '変えました。' : '変わったところはありません。'));
                        return;
                    }

                    /* 同じ項目を続けて変えた＝少し待ってから送り直す（そのあとに触った分が上書きする） */
                    if (result && result.retryAfterMs > 0) {
                        queue = merge(change, queue || {});
                        setStatus('少し待ってから反映します…');
                        retryTimer = setTimeout(function () {
                            retryTimer = null;
                            pump();
                        }, result.retryAfterMs + 50);
                        return;
                    }

                    setStatus((result && result.message) || '変えられませんでした。', true);
                    revertControls(change);
                })
                .fail(function () {
                    setStatus('変えられませんでした。接続が戻ってから、もう一度お試しください。', true);
                    revertControls(change);
                })
                .always(function () {
                    inFlight = false;
                    pump();
                });
        }

        /* === 操作 === */

        function applyName() {

            if (nameInput.value.trim() === current.roomName) {
                setStatus('部屋名は変わっていません。');
                return;
            }

            send({ roomName: nameInput.value });
        }

        function applyPassword() {

            var typed = passwordInput.value;

            if (!lockToggle.checked) {
                return;
            }

            if (typed.trim().length === 0) {
                setStatus('パスワードを入れてください。', true);
                passwordInput.focus();
                return;
            }

            send({ lock: true, password: typed });
        }

        /* Enter で確定（変換中の Enter では送らない＝本体の入力欄と同じ規則） */
        function onEnter(handler) {
            return function (e) {
                if (e.key !== 'Enter' || e.isComposing || e.keyCode === 229) {
                    return;
                }
                e.preventDefault();
                handler();
            };
        }

        nameApplyBtn.addEventListener('click', applyName);
        nameInput.addEventListener('keydown', onEnter(applyName));
        passwordApplyBtn.addEventListener('click', applyPassword);
        passwordInput.addEventListener('keydown', onEnter(applyPassword));

        maxSelect.addEventListener('change', function () {
            send({ maxUsers: parseInt(maxSelect.value, 10) });
        });

        knockToggle.addEventListener('change', function () {
            send({ knockEnabled: knockToggle.checked });
        });

        imageToggle.addEventListener('change', function () {
            send({ imageEnabled: imageToggle.checked });
        });

        lockToggle.addEventListener('change', function () {

            if (lockToggle.checked) {

                /* かけるのは、パスワードを入れて「かける」を押したとき。ここでは欄を使えるようにするだけ */
                if (queue && has(queue, 'lock') && !queue.lock) {
                    /* まだ送っていない「外す」を取り消した */
                    delete queue.lock;
                }
                syncPasswordField();

                if (!current.hasPassword) {
                    setStatus('パスワードを入れて「かける」を押してください。');
                    passwordInput.focus();
                }
                return;
            }

            passwordInput.value = '';
            syncPasswordField();

            if (current.hasPassword) {
                send({ lock: false });
            } else {
                setStatus('');
            }
        });

        /*
            今の設定を置き換えて、画面の中の関係するところを揃える（配信・戻り値の両方から呼ぶ。何度呼んでも同じ結果）
              ・上のバーの部屋名
              ・人数の分母（定員）＝在室者の画面の (3/10) の「10」。ページを開いたときに焼き込んだ値なので、ここで置き換える
              ・「画像」のタブの出し入れ（オフにしても、貼られた画像は消さない）
              ・ノックを受け付けなくなったら、管理人の画面のノックの小窓を消す（サーバーは返事待ちのノックを取り消している）
              ・欄（管理人でない人の分も。管理人になったときに今の値で出るように）
            nameFromSelf … 自分が部屋名を変えた返事なら true（打った欄を、整えられた名前で置き換える）
        */
        function apply(settings, nameFromSelf) {

            if (!settings) {
                return;
            }

            var knockTurnedOff = current.knockEnabled && !settings.knockEnabled;
            var previousName = nameFromSelf ? nameInput.value : current.roomName;

            current = {
                roomName: settings.roomName || '',
                maxUsers: settings.maxUsers || current.maxUsers,
                knockEnabled: !!settings.knockEnabled,
                imageEnabled: !!settings.imageEnabled,
                hasPassword: !!settings.hasPassword
            };

            $('.my-profile-bar .room-name').text(current.roomName).attr('title', current.roomName);

            maxUsers = current.maxUsers;
            updateParticipantCount();

            InsertPanel.setImageEnabled(current.imageEnabled);

            if (knockTurnedOff) {
                KnockRequests.replaceAll([]);
            }

            if (!current.hasPassword) {
                hidePassword();
            }

            syncControls(previousName);
        }

        resetForm();

        return {

            /* 管理人のときだけ欄を出す（renderMyOwnerBadge から。管理人が替わった・自分の番号が決まったとき） */
            refreshVisibility: function () {

                var show = isOwnerNow();

                if (box.hidden === !show) {
                    return;
                }

                box.hidden = !show;

                /* 管理人になった・外れたときは、書きかけを捨てて今の値に戻す（見せていたパスワードも伏せる） */
                resetForm();
                setStatus('');

                /* 入室禁止の一覧。管理人になったら取りに行き、外れたら中身を捨てる（管理人でない人の画面に残さない） */
                setBanStatus('');
                if (show) {
                    loadBans();
                } else {
                    renderBans(null);
                }
            },

            apply: function (settings) {
                apply(settings, false);
            },

            /* 設定パネルを開いたら、入室禁止の一覧を取り直す（管理人のときだけ） */
            onPanelOpen: function () {
                if (!box.hidden) {
                    loadBans();
                }
            },

            /* 設定パネルを閉じたら、見せていたパスワードを伏せる（肩越しに見られないように） */
            onPanelClose: function () {
                hidePassword();
            }
        };

        function isOwnerNow() {
            return ownerLocalId !== 0 && myLocalId !== null && ownerLocalId === myLocalId;
        }
    })();

    /*
        管理人が部屋の設定を変えた。部屋の全員に届く（変えた管理人にも）。
        eventText … 「管理人が部屋の設定を変えました（部屋名「…」・定員 3人）」（＝5つとも出す）。
        設定の置き換えは何度届いても同じ結果。行は1回の配信につき1行（過去ログにも同じ行が出る＝記録 61）。
        【、2つに分かれて届く】変えたその場は settings だけ（eventText は空）。
        変更が止まってから数秒後に、まとめた1行が eventText だけで届く（settings は null）
        【、行と一緒に発言の音（ポン）を鳴らす】要望：音を鳴らす。
        部屋の決まりが変わった（定員・鍵・画像など）ことに、画面を見ていない人も気づけるように。
        ・音は発言と同じ合成音を使い回す（mp3 を足すと、FTP で手置きするファイルが1つ増えるため）
        ・変えた管理人の画面でも鳴らす（自分の発言でも鳴るのと同じ。約5秒後に「記録された」ことが分かる）
        ・無視とは関係なく鳴らす（行も無視に関係なく出す＝部屋そのものの出来事。管理人の交代の行と同じ扱い）
        ・過去ログでは鳴らない（音はここ＝ハブから届いたときだけ。「過去ログでは鳴らさない」は場所で解いてある）
        ・管理人が部屋を出るときは「退室しました」の直後にこの行が届くが、40ms 以内なら Sounds の間引きで1つにまとまる
        ・行と音は同じ判断（eventText があるとき）に乗せる。設定だけの配信（eventText が空）では鳴らさない
    */
    chatHub.client.roomSettingsChanged = function (settings, eventText) {

        RoomSettings.apply(settings);

        if (eventText) {
            appendSystemMessageRow(eventText, todayDateText(), SCROLL_AUTO_QUIET);
            Sounds.message();
        }
    };

    /*
        誰か（自分も含む）が見た目を変えた。
        変えた本人の Cookie と自分のバーは、配信ではなく ChangeAppearance の戻り値で書き換える
        （放送は戻り値より先に届くことがある。一覧の置き換えは何度届いても同じ結果になる）
    */
    chatHub.client.appearanceChanged = function (localId, iconType, iconColor) {
        Appearance.onAppearanceChanged(localId, iconType, iconColor);
    };

    /*
        === 管理人が替わった===

        localId   … 新しい管理人の、この部屋の中の番号（ブラウザの識別子ではない）
        eventText … 「管理人が〇〇さんに替わりました」（文言はサーバーが作る。過去ログにも同じ行が出る）

        決定：「管理人が〇〇さんに替わりました」は全員に出す。入退室のお知らせ行と同じ見た目。
        【無視している相手が新しい管理人でも、出す】部屋そのものの出来事で、誰がノックの返事をするかに関わるため
        （その人の発言や入退室の行とは性質が違う）。音は鳴らさない（直前に退室の音が鳴っている）

        designated … 管理人が参加者一覧から相手を選んでゆずった（指名。足した3つ目の引数）。
        【指名のときだけ「ポン」を鳴らす】要望：音は普通に出す。自動の引き継ぎ（退室したので替わった）は
        今のまま鳴らさない（直前に退室のドアの音が鳴るので、音が重なる）。行と音は同じ判断（eventText があるとき）に乗せる。
        発言の音を使い回す（部屋の設定の1行と同じ。mp3 を足さない）
    */
    chatHub.client.ownerChanged = function (localId, eventText, designated) {

        /*
            札の付け替え（⑤）。文言が空で届くこともある
            （管理人が部屋を作って入った・同じ部屋を開き直した、など。番号が変わっただけで、交代ではない）
        */
        setOwnerLocalId(localId);

        if (eventText) {
            appendSystemMessageRow(eventText, todayDateText(), SCROLL_AUTO_QUIET);

            if (designated === true) {
                Sounds.message();
            }
        }
    };

    /* 管理人の番号を置き換えて、札を描き直す（参加者一覧と、自分のプロフィールバー） */
    function setOwnerLocalId(localId) {

        var next = (typeof localId === 'number') ? localId : 0;

        if (next === ownerLocalId) {
            return;
        }

        var wasOwner = (ownerLocalId !== 0 && myLocalId !== null && ownerLocalId === myLocalId);

        ownerLocalId = next;
        renderParticipantList();
        renderMyOwnerBadge();

        /*
            【管理人でなくなったら、ノックの小窓を消す】
            指名でゆずると、自分は部屋に居たまま管理人でなくなる。返事待ちのノックは新しい管理人の画面へ移る
            （ChatHub.HandOverOwner）ので、こちらに残った小窓を押しても「管理人ではない」と断られるだけになる。
            （自動の引き継ぎでは、前の管理人はもう部屋に居ないので、ここへは来ない）
        */
        if (wasOwner && !(ownerLocalId !== 0 && myLocalId !== null && ownerLocalId === myLocalId)) {
            KnockRequests.replaceAll([]);
        }
    }

    function renderMyOwnerBadge() {

        var badge = document.getElementById('myOwnerBadge');

        if (badge) {
            badge.hidden = !(ownerLocalId !== 0 && myLocalId !== null && ownerLocalId === myLocalId);
        }

        /* 部屋の設定の欄も、同じ条件で出し入れする */
        RoomSettings.refreshVisibility();

        /* 管理人の道具のボタン（参加者一覧）も、同じ条件で出し入れする */
        OwnerTools.refresh();
    }


    /* === 入室処理 === */

    /*
        自分の「入室しました」の行を、もう描画したかどうか

        【罠】joinRoomAndRender()は、再接続のたびに呼ばれる。
        このフラグが無いと、自動再接続が起きるたびに「入室しました」が
        自分の画面に出てしまう（他の人の画面には何も出ないため、自分だけが
        見えている、という食い違った状態になる）

        サーバー側も、引き継ぎ(b)ならmyJoinEventTextをNothingで返すため、
        通常の再接続では、そもそも文言が届かない。このフラグはその二重の保険
    */
    var hasRenderedMyJoinRow = false;

    /*
        初回の接続時だけでなく、再接続したあとにも使い回すため、関数として切り出している

        サーバー側のJoinRoomは、同じChatBrowserIdを持つ古い接続を全部屋から自動的に
        取り除く。そのため、再接続時にこれを呼んでも、自分が二重に登録されることはない
    */
    /*
        isReconnect … 「これは再接続としての呼び出しか」をサーバーへ伝える。

        【必ず、呼ぶ側が明示すること。既定値を付けない】
        取り違えると、片方は静かに壊れる。
          ・初回なのに true …… 普通の入室が**全部**断られる
          ・再接続なのに false … 席を失った人が新規入室として通ってしまい、
                                  今回直した不具合がそのまま戻る
        既定値があると「書き忘れ」がどちらかへ倒れるので、引数を必須にしてある
    */
    function joinRoomAndRender(isReconnect) {
        return chatHub.server.joinRoom(roomId, myNickname, myIconType, myIconColor, isReconnect)
            .done(function (result) {

                /*
                    サーバー側に入室を断られた場合
                    （満員、終了した部屋、名前が無い、Cookieが壊れている）。

                    【必ず、いちばん先に見ること】断られたときは参加者リストにも
                    追加されておらず、他のプロパティは意味を持たない。
                    先に一覧を作り直したりすると、入れていない部屋の画面を
                    組み立ててしまう
                */
                if (result.rejectReason) {

                    /* 正常な離脱のため、このあとのdisconnectedで切断の警告を出さない */
                    markLeavingRoom();

                    /*
                        理由はalertでは出さない（背面のタブでは表示が抑制されるため）。
                        kickedと同じく、sessionStorageへ預けて移動先で帯として表示する
                    */
                    saveLeaveReason(result.rejectReason);

                    /*
                        【罠】ここで Session("CurrentRoomId") を消してはいけない。
                        Sessionはタブごとではなくブラウザ単位で共有されているため、
                        同じブラウザの別のタブが正当に在室している部屋を巻き添えにする
                        （kickedの処理に書いてある罠と、まったく同じもの）

                        戻る先は、サーバーが理由に応じて決めて返してくる
                        （満員・終了した部屋ならラウンジ、名前が無いならトップページ）。
                        万一届かなかった場合に備え、ラウンジを既定にしておく
                    */
                    window.location.href = result.rejectRedirectUrl || '/chat/lounge';
                    return;
                }
                myLocalId = result.myLocalId;

                /*
                    === 無視リストを、引き継ぐか捨てるか ===

                    【必ず、一覧を作り直す前にやること】
                    下の renderParticipantList が「非表示中」の印を描くため、
                    そのときには無視リストが確定していなければならない。

                    判定はサーバーが返した isContinuation を見るだけ
                    （クライアント側だけでは、F5と新規入室の区別が付かない）。

                    【true が返ったら、ログを描き直す】
                    IgnoreList は、この時点より前（ページを開いた直後）に
                    sessionStorage を読んでいる。過去ログの描画のほうが先に走るためで、
                    そうしないとF5のたびに無視した相手の過去ログが戻ってしまう。
                    そのぶん、捨てるべきだった場合には「隠さなくてよい行を隠したまま」
                    描き終わっているので、ここで描き直す
                */
                var needsLogRedraw = IgnoreList.applyJoinResult(result);

                /*
                    閉じたこっそりの窓も、まったく同じ軸で引き継ぐ／捨てる。
                    F5・再接続なら閉じたまま、新しく入り直したなら開き直す
                    （どちらも sessionStorage で、判定は同じ isContinuation）
                */
                PrivateTalk.applyJoinResult(result);

                /* 管理人の番号。一覧を作る前に置く（作るときに札を付けるため） */
                ownerLocalId = (typeof result.ownerLocalId === 'number') ? result.ownerLocalId : 0;

                /* 再接続時は、古い参加者一覧が残っているため、一度空にしてから作り直す */
                participants = [];
                result.existingParticipants.forEach(function (p) {
                    addParticipant(p.localId, p.nickname, p.iconType, p.iconColor, p.anonId,
                                   idMarkOf(p.trip, p.accountName), p.joinSeq);
                });

                /* 自分の「名前の下の印」（トリップかアカウントID）。サーバーが決めた値をまとめる */
                var myIdMark = idMarkOf(result.myTrip, result.myAccountName);

                /*
                    自分自身も、ここで一覧へ入れる。

                    【重要】サーバーからの入室通知（UserJoined）を待ってはいけない。
                    SignalRでは、Hubメソッドの中で送った放送のほうが、そのHubメソッドの
                    戻り値よりも先に届く。以前は自分にも放送が届く作りだったため、
                    自分を一覧に追加した直後に、上のリセットで自分が消えていた。
                    自分の情報は放送に頼らず戻り値から組み立てることで、
                    メッセージの到着順に左右されなくなる
                    （サーバー側も、新規入室の放送はClients.OthersInGroupで自分を除いている）
                */
                addParticipant(result.myLocalId, myNickname, myIconType, myIconColor, result.myAnonId,
                               myIdMark, result.myJoinSeq);

                /*
                    自分のトリップは、プロフィールバーにも出す。
                    【毎回入れ直す】再接続（引き継ぎ）でも、サーバーはその時点の Cookie から
                    計算し直している。ラウンジで外した・変えたあとに入り直せば、ここで変わる
                */
                setMyTrip(myIdMark);

                /* 自分が管理人なら、名前の横に札 */
                renderMyOwnerBadge();

                /*
                    自分の入室行も、同じ理由でここで描画する（放送は自分には届かない）。
                    文言はサーバーが組み立てたものを使い、クライアント側では作らない。

                    初回だけ描画する。再接続では出さない
                    （引き継ぎ(b)ならmyJoinEventText自体がnullで届く）

                    自分の行なのでSCROLL_FORCE。直前に過去ログを描いて最下部に居るため
                    実際の動きは変わらないが、「自分の行は必ず最下部へ」という
                    決まりから外れた例外を1つも作らないために、明示して渡している
                */
                if (!hasRenderedMyJoinRow && result.myJoinEventText) {
                    hasRenderedMyJoinRow = true;
                    appendSystemMessageRow(result.myJoinEventText, todayDateText(), SCROLL_FORCE);

                    /*
                        自分の入室音。入室の通知はOthersInGroupで配信されていて
                        自分には届かないため、ここでこちらから鳴らす。

                        【再接続では鳴らさない】このifの中に置いてあるので、
                        入室行を出すときと必ず同じ回数だけ鳴る。
                        外に出すと、自動再接続のたびに鳴るようになる

                        【割り切り】ページを開いた直後は、まだこのページを
                        一度も操作していないため、ブラウザの自動再生制限で
                        鳴らないことが多い（実測。「実測した結果」を参照）
                    */
                    Sounds.enter();
                }

                /*
                    無視リストを捨てた場合だけ、ログを描き直す
                    （上の applyJoinResult のコメントを参照）。

                    描き直しは、いつもの経路（サーバーから取り直す）を通す。
                    ここで vbChatInitialLog を描き直すと、接続してから今までの
                    数百ミリ秒に届いた発言が落ちてしまう
                */
                if (needsLogRedraw) {
                    refreshLogAfterIgnoreChange();
                }

                /*
                    === こっそりの窓を、今の参加者一覧に合わせる ===

                    【必ず、一覧を作り直したあとに呼ぶこと】
                    窓は過去ログから作られるので、この2つが分からないまま出来ている。

                      名前     … 自分が送った行しか無い窓は、相手の名前が取れない
                      居るか   … 相手が既に退室していれば、送信できない状態が正しい

                    どちらも参加者一覧を見れば分かるが、その一覧は
                    JoinRoom の戻り値が届いたこの時点で、はじめて揃う

                    【再接続のたびに呼んでよい】
                    どちらも「今の一覧に合わせる」だけで、行を足したり消したりしない
                */
                PrivateTalk.refreshNicknames();
                PrivateTalk.refreshPresence();

                /*
                    ノックの返事待ち。自分が管理人なら、サーバーが一覧をくれる
                    （F5・繋ぎ直しの間に届いたノックも、ここで出し直す）。
                    管理人でなければ null ＝ 出ている小窓は全部片付ける（その間に管理人が替わった）
                */
                if (Array.isArray(result.pendingKnocks)) {
                    KnockRequests.replaceAll(result.pendingKnocks);
                } else {
                    KnockRequests.clear();
                }
            })
            .fail(function () {

                /*
                    入室（JoinRoom）の呼び出しそのものが失敗した。

                    【ここに受け皿が要る理由】
                    reconnect()は hideConnectionBar() と setInputEnabled(true) を
                    joinRoomAndRender().done() の中でしか呼んでいない。
                    ここで失敗すると .done が走らないため、受け皿が無いと
                    「接続の帯が出たまま・入力欄が無効のまま」で固まり、
                    しかも自動では二度と戻らない
                */
                debugLog('入室（JoinRoom）の呼び出しに失敗しました');

                /*
                    自分から離れようとしている最中（退室ボタン・kicked）なら、
                    何も出さない。このあと画面が移動するため
                */
                if (isLeavingIntentionally) {
                    return;
                }

                /*
                    呼んでいる最中に接続が切れた場合も、ここへ来る。
                    それは「接続の話」なので、既存の切断ハンドラ
                    （reconnecting / disconnected）に任せて、ここでは何もしない。
                    上書きすると、再接続を試みている最中なのに
                    「サーバー側の都合」と表示され、原因を取り違えさせてしまう
                    （sendMessageの.failと、まったく同じ考え方）
                */
                if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                    return;
                }

                /*
                    接続は生きているのに入室できない＝利用者に直せることは何も無い。
                    「サーバー側の都合」の帯へ合流させる
                    （赤い帯＋ラウンジに戻るボタン。接続は生きているので、
                      押しても何も起きない「再接続」ボタンは出ない）
                */
                showServerTroubleBar();
            });
    }


    /*
        === 再接続処理 ===

        【背景】切断の原因は、Chromeが「裏に回ったタブ」を凍結することだと特定できた。
        凍結されている間はJavaScriptが1行も動かないため、凍結そのものを防ぐ手立ては無い。
        打てる手は「タブが前面に戻ってきた瞬間に、自動で繋ぎ直す」ことだけになる
    */

    /* 再接続の処理中かどうか（多重にstart()を呼ぶと不正な状態になるため、フラグで防ぐ） */
    var isReconnecting = false;

    /* 自動での再接続を、続けて何回試したか */
    var reconnectAttempts = 0;

    /*
        自動再接続の、待ち時間（ミリ秒）。だんだん間隔を空けていく。
        サーバーが本当に落ちている場合に、短い間隔で叩き続けないようにするため
        （DB側は ReconnectDelaysSeconds に "3,10,30" の形で持っている。
          並びの長さが、そのまま試行回数になる）
    */
    var RECONNECT_DELAYS_MS = vbChatSettings.reconnectDelaysMs;

    /* 再接続の見張り（reconnect() の中のコメント） */
    var RECONNECT_WATCHDOG_MS = 15000;
    var reconnectAttemptSeq = 0;

    /*
        つなぎ直すために、今の接続を止める（2か所の同じ3行を1つにした）

        【必ず名乗ってから止めること】stop() は disconnected を（同期で）発火させる。
        名乗らないと、そのハンドラが「予期しない切断」と勘違いして自動再接続を予約する。
        【戻すのは finally で】戻し忘れると、以降の本物の切断で自動再接続が一度も走らなくなる。
        notifyServer は false（相手はもう居ないので、伝えようとしても待たされるだけ）
    */
    function stopToReconnect() {
        isStoppingToReconnect = true;
        try {
            $.connection.hub.stop(false, false);
        } finally {
            isStoppingToReconnect = false;
        }
    }

    /* 再接続を1回試みる（自動・手動の両方から呼ばれる） */
    function reconnect() {

        /* 既に接続済み、または接続処理中なら何もしない */
        if (isReconnecting || $.connection.hub.state === $.signalR.connectionState.connected) {
            return;
        }

        /*
            【start() を呼んでよいのは、切れている（disconnected）ときだけ】
            SignalR の start() は、状態が reconnecting（や connecting）のとき、
            **つなぎ直さずに「済んだ」をすぐ返す**（jquery.signalR の start の先頭の判定）。
            それを成功と思って先へ進むと、死んだ接続で JoinRoom を呼び、黄色い帯のまま止まっていた
            （スマホを放っておいてから戻ったとき。本番）。
            reconnecting から繋ぎ直したいときは、呼ぶ側が先に stopToReconnect() で切ってから来る
        */
        if ($.connection.hub.state !== $.signalR.connectionState.disconnected) {
            debugLog('再接続：状態が ' + $.connection.hub.state + ' のため start() を呼ばない');
            return;
        }

        isReconnecting = true;
        showConnectionBar('再接続しています…', false);
        setInputEnabled(false);

        /*
            【見張り】つなぎ直し（start → JoinRoom）が RECONNECT_WATCHDOG_MS たっても終わらなければ、
            あきらめて赤い帯（再接続・ラウンジに戻るのボタンつき）に変える。
            黄色い帯のまま、押せるものが何も無い状態で止まらないようにするため
        */
        var attemptId = ++reconnectAttemptSeq;
        var watchdog = setTimeout(function () {
            if (attemptId !== reconnectAttemptSeq || !isReconnecting || isLeavingIntentionally) {
                return;
            }
            debugLog('再接続が ' + RECONNECT_WATCHDOG_MS + 'ms 終わらないため、あきらめて赤い帯にする');
            isReconnecting = false;
            stopToReconnect();
            showConnectionBar('サーバーとの接続が切れました。', true);
            setInputEnabled(false);
        }, RECONNECT_WATCHDOG_MS);

        $.connection.hub.start().done(function () {
            clearTimeout(watchdog);
            if (attemptId !== reconnectAttemptSeq) {
                return;
            }
            isReconnecting = false;
            /* 繋がったので、試行回数を仕切り直す */
            reconnectAttempts = 0;

            /*
                【重要】ここでも必ず控え直すこと。
                start()で繋ぎ直すと接続IDは新しくなるため、忘れると再接続後の
                ビーコンが古い接続IDを送り、サーバー側で該当なしとなって空振りする
                （そのぶん退室が確定せず、1時間の無言タイムアウトまで在室扱いで残る）

                なお $.connection.hub.reconnected（SignalR自身の再接続）では
                接続IDは変わらないため、そちらでの控え直しは不要
            */
            myConnectionId = $.connection.hub.id;

            debugLog('再接続に成功');

            /*
                【true を渡すこと】これは再接続である。
                席が既に無ければ（無言タイムアウトで退室させられていた・
                別のタブに取られた・サーバーが再起動した）、
                新規入室として通さず、理由を添えてラウンジへ返してもらう
            */
            joinRoomAndRender(true).done(function () {

                /*
                    【必須】入室を断られていたら、ここから先は何もしない。

                    joinRoomAndRender の中の .done は「断られたら理由を預けて移動する」
                    処理をしているが、**この外側の .done も同じ約束に登録されているため、
                    断られた場合でも続けて走る**（jQueryのPromiseは、
                    登録した順に全部呼ばれる）。

                    止めないと、下の refreshLogAfterReconnect が動いてしまい、
                    GetLogEntries が「部屋に入っていない」と答えて、
                    **せっかく預けた理由を上書きする**。
                    実際、無言タイムアウトで落とされた人が
                    「1分発言がなかったため…」ではなく
                    「部屋に入っていない状態になりました」を見せられていた
                    （実測で発見）。

                    断られたときは isLeavingIntentionally が立っているので、それを見る
                */
                if (isLeavingIntentionally) {
                    return;
                }

                hideConnectionBar();
                setInputEnabled(true);

                /*
                    切れている間に流れた発言を拾い直す。
                    【入室のやり直しが済んでから呼ぶこと】先に呼ぶと、
                    入室に失敗した（＝この部屋にいない）場合でも、
                    ログだけを描き直してしまう
                */
                refreshLogAfterReconnect('再接続');
            }).fail(function () {
                /*
                    【受け皿】JoinRoom が失敗した・返ってこなかった（共通の守りが20秒で失敗にする）。
                    joinRoomAndRender の中の .fail は、接続が切れていれば帯を切断のハンドラに任せて何もしない。
                    つながっているのに入れなかった、なら赤い帯（サーバー側の都合）を出す。
                    どちらにも当たらず黄色い「再接続しています…」が残る場合だけ、ここで押せる帯に変える
                */
                if (isLeavingIntentionally) {
                    return;
                }
                if ($('#connectionBarText').text() === '再接続しています…') {
                    showConnectionBar('サーバーとの接続が切れました。', true);
                    setInputEnabled(false);
                }
            });
        }).fail(function () {
            clearTimeout(watchdog);
            if (attemptId !== reconnectAttemptSeq) {
                return;
            }
            isReconnecting = false;
            debugLog('再接続に失敗（' + reconnectAttempts + '度目）');

            /*
                【罠】ここで scheduleAutoReconnect() を呼んではいけない。
                start()が失敗すると、SignalR自身がdisconnectedイベントも発火させるため、
                そちらのハンドラからscheduleAutoReconnect()が呼ばれる。
                ここでも呼ぶと、1回の失敗で予約が2回走る
                （最初のhub.start()の側には、同じ罠のコメントが最初から入っている。
                  こちらだけ抜けていた）。

                【実測（サーバーを115秒落として確認）】
                直す前は、reconnectAttempts が1回の失敗で2つ進むため、
                  ・3,10,30 と3回ぶん設定してあるのに、自動再接続は2回しか走らない
                  ・打ち切ったあと、余った30秒のタイマーが1回だけ発火し、
                    赤い帯を黄色い「再接続しています…」へ塗り替えて、
                    再接続ボタンとラウンジに戻るボタンを2.3秒間消してしまう
                という2つが起きていた
            */
        });
    }

    /*
        自動での再接続を、次の待ち時間で予約する
        3回とも失敗したら、はじめて赤い帯とボタンを表示する
    */
    function scheduleAutoReconnect() {

        /* 意図的な離脱なら、そもそも繋ぎ直さない */
        if (isLeavingIntentionally) {
            return;
        }

        if (reconnectAttempts >= RECONNECT_DELAYS_MS.length) {
            debugLog('自動再接続を打ち切り（手動に切り替え）');
            showConnectionBar('サーバーとの接続が切れました。', true);
            setInputEnabled(false);
            return;
        }

        var delay = RECONNECT_DELAYS_MS[reconnectAttempts];
        reconnectAttempts++;

        showConnectionBar('接続が切れました。再接続を試みています…', false);
        setInputEnabled(false);

        setTimeout(function () {
            debugLog('自動再接続を試行（' + reconnectAttempts + '度目）');
            reconnect();
        }, delay);
    }

    /* 利用者が戻ってきた（タブの復帰・ネットワークの復帰）ときの、共通の処理 */
    function reconnectOnResume(reason) {

        /* 意図的な離脱なら、繋ぎ直す必要はない */
        if (isLeavingIntentionally) {
            return;
        }

        /*
            今まさに繋ぎに行っている最中なら、何もしない

            【罠】connecting を必ず見ること。connected だけを見ていると、
            ページの読み込み中（最初のhub.start()が走っている最中）に
            visibilitychangeが発火した場合に素通りしてしまい、
            hub.start()が二重に走る。その結果、同じ接続からJoinRoomが2回呼ばれ、
            新しく開いたページが自分で自分をキックする状態になっていた
            （URLをアドレスバーに打ち込んで確定した直後は、候補リストが閉じて
              ページへ焦点が移るため、このタイミングで実際に起こりうる）

            【connectedと違い、こちらは中身を疑わない】
            「今つなぎに行っている」は、その処理自身が結果を出す。
            横から二重にstart()を掛けるほうが、はるかに害が大きい
        */
        if ($.connection.hub.state === $.signalR.connectionState.connecting) {
            debugLog(reason + 'が、connectingのため繋ぎ直さずに帰る');
            return;
        }

        /*
            === 繋がっているように見えるとき、それを鵜呑みにしない ===

            【なぜ疑うのか】凍結が解けた直後は、ソケットが既に死んでいても
            state は connected のままである（実測で2.4〜3.0秒。SignalRの
            webSocketsトランスポートが reconnectDelay の2000msを待ってから
            reconnecting へ移るため）。
            ここを素通りさせると、本命として書いたこの経路が、
            本命の場面（凍結からの復帰）でだけ空振りする。
            Android実機の計測で、実際にそうなっていることを確認した。

            【何で生死を見るか】「最後にサーバーから何か届いてから、どれだけ経ったか」。
            サーバーは誰も発言していなくても約10秒おきに keepalive を送ってくるので、
            生きていれば必ず新しい（下の millisecondsSinceLastServerMessage を参照）
        */
        if ($.connection.hub.state === $.signalR.connectionState.connected) {

            var silenceMs = millisecondsSinceLastServerMessage();

            /*
                【フェイルセーフ】測れない（null）ときは、今までどおり信じて帰る。
                測れないことを理由に繋ぎ直しを始めると、SignalRを上げて
                内部の値が消えた日に、全員が復帰のたびに繋ぎ直す形になる
            */
            if (silenceMs === null || silenceMs < RESUME_STALE_MS) {
                debugLog(reason + 'が、connectedのため繋ぎ直さずに帰る' +
                         '（最後の受信から ' +
                         (silenceMs === null ? '不明' : silenceMs + 'ms') + '）');
                return;
            }

            debugLog(reason + 'が、' + silenceMs +
                     'ms 何も届いていないため、繋がっていないものとして扱う');

            /*
                【必要】いったん明示的に切ってから繋ぎ直す。
                これを挟まないと、reconnect() の中の
                「既に接続済みなら何もしない」に自分で弾かれてしまう。

                notifyServer は false。相手はもう居ないので、切断を伝えようとしても
                届かず、そのぶん待たされるだけになる

                【必ず名乗ってから止めること】
                この stop() は disconnected を（同期で）発火させる。
                名乗らないと、そのハンドラが「予期しない切断」と勘違いして
                自動再接続を予約し、繋ぎ直しに成功したあとに、
                用済みのタイマーがもう1回だけ走ることになる（実機で確認）

                【罠】戻すのは finally で。戻し忘れると、
                以降の本物の切断で自動再接続が一度も走らなくなる
            */
            stopToReconnect();
        }

        /*
            === 「再接続中」のまま戻ってきたとき===

            SignalR 自身の再接続は、凍結されている間は1行も動かず、
            戻ったあとも死んだソケットを待ったまま終わらないことがある（スマホを放っておいたとき。本番）。
            利用者が戻ってきたこの瞬間は、こちらで切って、新しくつなぎ直す。
            （ほんの数秒前に切れたばかりでも、つなぎ直しは軽く、席は JoinRoom の引き継ぎで残る）
            reconnect() は disconnected からしか start() しないので、ここで切っておかないと何も起きない
        */
        if ($.connection.hub.state === $.signalR.connectionState.reconnecting) {
            debugLog(reason + 'が、reconnecting のまま止まっているかもしれないため、切ってつなぎ直す');
            stopToReconnect();
        }

        debugLog(reason + 'ため、再接続を試みます');

        /*
            試行回数を0に戻してから繋ぎ直す。
            利用者が戻ってきたこの瞬間は「やり直す価値のある新しい機会」であり、
            一度3回失敗していても、また3回試せるようにするため
        */
        reconnectAttempts = 0;
        reconnect();
    }

    /*
        こちらの意思で接続を止めている最中かどうか

        【なぜ、こんなフラグが要るのか】
        disconnected は「予期しない切断」専用のイベントではない。
        こちらから stop() を呼んだときも、まったく同じ形で発火する。
        ハンドラ側からは区別が付かないため、止めた側が名乗るしかない。

        既にある isLeavingIntentionally（退室ボタン・kicked）と、役割は同じ。
        あちらが「この部屋から出ていく」なのに対し、こちらは
        「この部屋には居続けるが、接続だけを張り直す」という違いだけである
    */
    var isStoppingToReconnect = false;

    /*
        === 復帰したときに、接続の生死を疑うためのしきい値 ===

        「最後にサーバーから何か届いてから、これ以上経っていたら、
          state が connected でも繋がっていないものとして扱う」という線引き。

        【この数字の根拠（実測）】
        誰も発言していない部屋で45秒間、値を2秒おきに見たところ、
        最後の受信からの経過は 0〜10秒 を繰り返していた。
        ＝サーバーが約10秒おきに keepalive を送ってきている
        （KeepAliveTimeout 20秒の半分。本番サーバーも同じ20秒）。
        20000ms は「keepalive を2回続けて取りこぼした」状態であり、
        これは SignalR 自身がタイムアウトと見なす長さでもある。

        【短くしすぎないこと】10秒より短くすると、
        「ただ画面を消していただけで、接続は健全」という人まで繋ぎ直しに行く。
        無駄な往復が増えるだけでなく、JoinRoom をやり直すことで
        (a)置き換えの経路を毎回通ることになる
    */
    var RESUME_STALE_MS = 20000;

    /*
        最後にサーバーから何か届いてから、何ミリ秒経ったか（測れなければ null）

        【なぜ received イベントを使わないのか】
        $.connection.hub.received は、keepalive では発火しない
        （静かな部屋で45秒間測って0回だった）。
        あれで測ると「誰も発言していない部屋」が20秒で死んだことになり、
        静かな部屋にいる人ほど繋ぎ直される、逆立ちした挙動になる。

        かわりに、SignalR自身が keepalive の監視に使っている値を読む。
        こちらは keepalive を含む「届いたもの全部」で更新される

        【罠】_ で始まる内部の値なので、SignalRを上げたときに消える可能性がある。
        読めなければ null を返し、呼び出し側は「今までどおりの動き」へ落ちる。
        黙って繋ぎ直しを連発する形には、絶対にしないこと
    */
    function millisecondsSinceLastServerMessage() {

        var internals = $.connection.hub._;

        if (!internals) {
            return null;
        }

        var lastAt = +internals.lastMessageAt;

        if (!lastAt) {
            return null;
        }

        return Date.now() - lastAt;
    }


    /* === 接続状態のハンドラ === */

    /* 接続が切れ、SignalRが自動で再接続を試みている最中 */
    $.connection.hub.reconnecting(function () {
        debugLog('reconnecting');
        showConnectionBar('接続が切れました。再接続を試みています…', false);
        setInputEnabled(false);

        /*
            【見張り】SignalR 自身の再接続が RECONNECTING_STUCK_MS たっても終わらなければ、
            こちらで切って、自分の再接続（3回まで・だめなら赤い帯）へ引き継ぐ。
            SignalR は本来、時間切れで disconnected へ移るが、その時計が動かず
            黄色い帯のまま止まることがあった（スマホを放っておいたとき）
        */
        clearTimeout(reconnectingWatchdog);
        reconnectingWatchdog = setTimeout(function () {
            if (isLeavingIntentionally ||
                $.connection.hub.state !== $.signalR.connectionState.reconnecting) {
                return;
            }
            debugLog('reconnecting が ' + RECONNECTING_STUCK_MS + 'ms 終わらないため、切ってつなぎ直す');
            stopToReconnect();
            scheduleAutoReconnect();
        }, RECONNECTING_STUCK_MS);
    });

    /* SignalR 自身の再接続の見張り（上の reconnecting のハンドラ） */
    var RECONNECTING_STUCK_MS = 45000;
    var reconnectingWatchdog = null;

    /*
        SignalR自身の再接続に成功した

        【ここで joinRoomAndRender() を呼んで、部屋へ入り直さない】
        サーバー（IIS）が再起動していた場合、ChatHubのShared変数が全部消えるため、
        接続だけが戻っても、サーバー側から見ると誰も部屋にいない状態になる。
        ここで入り直せば自動で復帰できる──実際、その下地は揃っている
        （joinRoomAndRenderは再接続のたびに呼ばれる前提で書いてあり、
          hasRenderedMyJoinRowが入室行の二重表示を防ぎ、サーバー側のJoinRoomが
          同じChatBrowserIdの古い接続を外すので、二重登録にもならない）。

        **それでも、あえてやらない。** サーバーが再起動したときは
        「一回リセット」とし、その時いた人はラウンジへ出す方針である
。自動復帰は、部屋の状態が消えていることを
        利用者に気づかせないまま話を続けさせることになり、
        一時部屋（ユーザーが作った部屋）が実装されると、なおさら実態と食い違う。
        発言しようとした時点でshowServerTroubleBarが理由を伝え、
        ラウンジへ戻ってもらう
    */
    $.connection.hub.reconnected(function () {

        debugLog('reconnected');
        clearTimeout(reconnectingWatchdog);
        reconnectAttempts = 0;
        hideConnectionBar();
        setInputEnabled(true);

        /*
            切れている間に流れた発言を拾い直す。

            【上の「部屋へ入り直さない」方針とは、矛盾しない】
            あちらが禁じているのは joinRoomAndRender()——つまり
            「サーバー側の部屋の状態が消えているのに、入り直して、
              何も無かったかのように会話を続けさせる」こと。
            こちらはログを読み直すだけで、入室のやり直しは一切しない。
            呼ぶのもハブではなくWebMethod（GetLogEntries）なので、
            参加者が増えることも、誰かに通知が飛ぶこともない。

            そして、サーバーが再起動していた場合はSessionも消えているため、
            GetLogEntries は "null" を返して今の表示をそのまま残す。
            方針がいちばん気にしている場面では、そもそも何も起こらない
        */
        refreshLogAfterReconnect('SignalR自身の再接続');
    });

    /* 完全に切断された（SignalR自身の再接続もあきらめた状態） */
    $.connection.hub.disconnected(function () {

        debugLog('disconnected');

        /* 退室ボタンやkickedによる正常な離脱では、警告を出さない */
        if (isLeavingIntentionally) {
            return;
        }

        /*
            復帰時に「もう繋がっていない」と判断して、こちらから止めた場合。
            呼んだ側が、この直後に自分で reconnect() する。
            ここで予約すると、繋ぎ直しに成功したあとに用済みのタイマーが走る
        */
        if (isStoppingToReconnect) {
            debugLog('こちらの意思で切ったため、自動再接続は予約しない');
            return;
        }

        /*
            こちら側で、間隔を空けながら自動的に繋ぎ直しにいく

            【予約するのは、必ずここ1か所だけ】
            start() が失敗したときも、こちらから stop() したときも、
            最後には必ずこのイベントを通る。呼び出し元の側でも予約すると、
            1回の切断で2回走る（2件、同じ形で踏んでいる）
        */
        scheduleAutoReconnect();
    });


    /* === 復帰のきっかけを拾う === */

    /*
        タブが前面に戻ってきた時（今回の本命）
        凍結されている間はJavaScriptが動かないため、
        復帰したこの瞬間に接続状態を確認し、切れていれば繋ぎ直す
    */
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState !== 'visible') {
            return;
        }
        reconnectOnResume('タブが前面に復帰した');
    });

    /* ネットワークが復帰した時（Wi-Fiが切れて復活した場合など） */
    window.addEventListener('online', function () {
        reconnectOnResume('ネットワークが復帰した');
    });


    /* === 接続状態バーのボタン === */

    /* 手動で接続をやり直す（自動で復帰できなかった場合の、最後の手段） */
    $('#reconnectBtn').on('click', function () {
        reconnectAttempts = 0;
        reconnect();
    });

    /* あきらめてラウンジへ戻る */
    $('#backToLoungeBtn').on('click', function () {

        /* 自分の意思での離脱のため、切断の警告は出さない */
        markLeavingRoom();

        /*
            【席も外してから移る】以前は移るだけで、席が無言タイムアウトの1時間まで残り、
            ラウンジに自分の「入室中」が出たままになっていた。退室ボタンと同じ処理を通す
        */
        leaveRoomAndGoToLounge();
    });

    /*
        接続確立後、自分の部屋ID・ニックネーム・アイコン情報・ブラウザIDでJoinRoomを呼ぶ
        JoinRoomの戻り値（JoinRoomResult）には、自分のLocalIdと、
        既にその部屋にいた参加者一覧の両方が入っている
        （自分自身は、放送を待たず、この戻り値から一覧へ入れる）

        SignalRの接続を始める前に、DBから読み出した過去ログを描画しておく。
        先に描画しておくことで、接続が確立するまでの間も、利用者にはログが見えている状態になる。
        また、この境目があるため、リアルタイム受信ぶんと二重に表示されることもない
    */
    renderInitialLog();

    /*
        【罠】最初のstart()でも isReconnecting を立てること。
        このフラグは reconnect() が「多重にstart()を呼ばない」ための門番だが、
        最初のstart()が立てていないと、この接続処理が終わる前に
        復帰イベント（visibilitychange・online）が来た場合に素通りされ、
        start()が二重に走ってしまう。
        その結果、同じ接続からJoinRoomが2回呼ばれ、サーバー側で
        自分自身が「別のタブからの接続」と判定される状態になっていた

        成功・失敗のどちらでも、必ずフラグを下ろすこと
    */
    isReconnecting = true;

    $.connection.hub.start().done(function () {
        isReconnecting = false;

        /*
            退室ビーコン用に、接続IDを控えておく
            （送信の瞬間には読めないため。理由は myConnectionId の宣言にあるコメント）
        */
        myConnectionId = $.connection.hub.id;

        debugLog('connected');

        /*
            【false を渡すこと】これはページを開いたときの初回の入室である。
            ここで true を渡すと、ラウンジから普通に入ってきた人が
            「席が無い」と判断されて全員断られる
        */
        joinRoomAndRender(false);
    }).fail(function () {
        isReconnecting = false;
        debugLog('最初の接続に失敗');

        /*
            ここで自動再接続を予約しないこと。
            start()が失敗すると、SignalR自身がdisconnectedイベントも発火させるため、
            そちらのハンドラからscheduleAutoReconnect()が呼ばれる。
            ここでも呼ぶと、待ち時間のタイマーが二重に走ってしまう
        */
    });


    /*
        === キープアライブ ===
        Sessionのスライド式タイムアウト（既定20分）を延長するため、定期的に軽い
        リクエストを送る

        以前はここでサーバーのPingも呼び、「接続がまだ生きている」ことを伝えていたが、
        接続の生死で退室を判断しなくなったため、その呼び出しは削除した
        （退室の判断は、最終発言時刻の1本だけで行う）
        （DB側は KeepAliveIntervalSeconds。Sessionのタイムアウト（既定20分）より
          短くしておく必要があるため、サーバー側で上限を15分に丸めてある）
    */
    var KEEPALIVE_INTERVAL_MS = vbChatSettings.keepAliveIntervalMs;

    setInterval(function () {

        $.ajax({
            /*
                Friendly URL経由だと問題が出るため、LeaveRoomの呼び出しと同様、
                物理パスを直接指定する
            */
            url: '/Chat/KeepAlive.aspx',
            cache: false
        }).done(function () {
            debugLog('keepalive 成功');
        }).fail(function () {
            debugLog('keepalive 失敗');
        });
    }, KEEPALIVE_INTERVAL_MS);


    /*
        === 閉じられた時の退室通知 ===

        ページが閉じられる瞬間に、サーバーへ退室を伝える

        SignalRは「閉じます」の通知にunloadイベントを使っているが、
        Chromeがunloadを制限しており（コンソールにViolationの警告が出る）、
        ウィンドウごと閉じた場合などに届かないことが分かった。
        そのため、閉じられる瞬間の通知用に作られたpagehideとsendBeaconを使う
    */

    /*
        退室のビーコンを、既に送ったかどうか
        beforeunloadとpagehideの両方が発生するため、これが無いと二重に送ってしまう
    */
    var leaveBeaconSent = false;

    /*
        === こっそりの窓の位置と大きさを、離れる直前に控える===

        F5では**開いている窓も、位置も、大きさも、透け具合もそのまま**、
        というのが開発者の決めた形である。位置は動かした時点で控えられるが、
        **大きさだけは合図が無い**ので、ここで1回だけ見に行く
        （実測＝F5で beforeunload・pagehide の両方が飛び、
          そこからの sessionStorage への書き込みは読み込み後も残る）。

        【自分から出ていくときは、控えないこと】
        markLeavingRoom が記録を捨てたあとにここが走るので、
        書き戻すと**入り直したときに窓が復活する**。
        退室のビーコンを送らない条件と、まったく同じ理由・同じ判定である
    */
    function savePrivateTalkLayout() {

        if (isLeavingIntentionally) {
            return;
        }

        PrivateTalk.saveLayout();
    }

    /* 退室のビーコンを1回だけ送る（beforeunload・pagehideの両方から呼ばれる） */
    function sendLeaveBeacon() {

        /*
            二重に送らないようにする
            （beforeunloadとpagehideの両方が発生した場合、後から来たほうは何もしない）
        */
        if (leaveBeaconSent) {
            return;
        }

        /* 退室ボタンやkickedによる離脱では、既にサーバー側で退室処理が済んでいるため送らない */
        if (isLeavingIntentionally) {
            return;
        }

        /*
            sendBeaconが使えない古いブラウザでは、何も送らない。
            その場合も、最終発言から1時間の無言タイムアウトが保険として働く
        */
        if (!navigator.sendBeacon) {
            return;
        }

        /*
            このページのSignalR接続ID。サーバーに「どの接続が抜けるのか」を伝える

            【なぜ、部屋IDとCookieだけでは足りないのか】
            BrowserIdだけを伝えると、F5の際に「新しいページのJoinRoom」が
            このビーコンを追い越したとき、サーバー側が入れ替わったあとの
            別の参加者を掴んで、生きている接続を退室させてしまう。
            しかも本人の画面には何の変化も出ないため、誰も気づけない
            （詳しい順序は、サーバー側のChatHub.LeaveByConnectionIdのコメントを参照）

            【罠】ここで $.connection.hub.id を読んではいけない。
            この時点では、SignalR自身のunloadハンドラが先に stop() を呼んでおり、
            hub.id は既に null になっている（詳細は myConnectionId のコメント）。
            接続が確立した時点で控えておいた、こちらの値を使うこと
        */
        var connectionId = myConnectionId;

        /*
            控えが空なのは、接続が確立する前にページを離れた場合。
            そもそも入室できていないため、何も送らずに終わる。
            空のまま送ると、サーバー側で誰の話か分からなくなる
        */
        if (!connectionId) {
            return;
        }

        leaveBeaconSent = true;

        /*
            Friendly URL経由だと問題が出るため、LeaveRoom・KeepAliveの呼び出しと同様、
            物理パスを直接指定する
        */
        navigator.sendBeacon('/Chat/LeaveBeacon.aspx?roomId=' + roomId +
                             '&connectionId=' + encodeURIComponent(connectionId));
    }

    /*
        ウィンドウごと閉じられた場合、pagehideでは送信が間に合わないことがあるため、
        より早いタイミングで発生するbeforeunloadでも送る。
        beforeunloadはスマホでは基本的に発生しないため、
        「画面を消しただけで退室扱いになる」ことにはならない

        【重要】確認ダイアログ（「このページを離れますか？」）を出してはいけない。
        戻り値を返したり、preventDefaultを呼んだりしないこと
    */
    window.addEventListener('beforeunload', function () {
        savePrivateTalkLayout();
        sendLeaveBeacon();
    });

    window.addEventListener('pagehide', function (e) {

        /* 控えるほうは、persisted を問わず行う（隠れただけでも損はしない） */
        savePrivateTalkLayout();

        /*
            persistedがtrueの場合は、完全に閉じられたのではなく、
            一時的に隠されただけ（スマホで画面を消した場合など）。
            この場合は在室のままにしたいので、何も送らない

            【重要】この判定は、スマホの画面消灯を退室扱いにしないために必須。
            絶対に外さないこと
        */
        if (e.persisted) {
            return;
        }

        sendLeaveBeacon();
    });


    /* === メッセージ送信処理 === */
    function sendMessage() {

        /*
            接続が切れている状態で送信すると、SignalRが
            「Connection must be started before data can be sent」という例外を投げる。
            利用者から見ると「文字が残ったまま、何も起きない」状態になるため、
            送信前に接続状態を確かめ、切れていることをはっきり知らせる
        */
        if ($.connection.hub.state !== $.signalR.connectionState.connected) {
            showConnectionBar('サーバーとの接続が切れています。', true);
            setInputEnabled(false);
            return;
        }

        /*
            === ここでの正規化は「防御」ではなく「親切」である ===

            まったく同じ正規化を、サーバー側（ChatHub.NormalizeMessage）でも行っている。
            ハブは開発者ツールから直接呼べるため、この行は1つも通らずに発言を
            送り込める。信用の境界は、サーバー側にしか置けない。

            それでもここに残しているのは、残り文字数の表示と数え方を揃えて、
            「画面では収まっていたのに、サーバーに弾かれた」という分かりにくい
            断られ方をなくすため。
            サーバー側は、これより多くの改行コード（\r 単体）まで畳むので、
            サーバーが数える長さは、必ずここでの長さ以下になる
        */
        var message = $('#messageInput').val().trim();
        message = message.replace(/\r\n/g, '\n').replace(/\n{3,}/g, '\n\n');

        if (message === '') {
            return;
        }

        /*
            部屋IDは渡さない。サーバーが、接続IDから自分で引く
            （渡せる形にしておくと、入室していない部屋のグループへ
              発言を投げ込めてしまう）

            【入力欄は、ここでは消さない】
            送信が受け付けられたことを確かめてから消す。
            呼んだ直後に消してしまうと、連投や文字数超過で断られたときに、
            打った文章がそのまま失われる
        */
        chatHub.server.sendMessage(message).done(function (result) {

            /*
                戻り値が無い、または「受け付けなかったが、理由も無い」。
                どちらもサーバー側の都合であって、利用者に直せることは何も無い
                （実質、アプリケーションが再起動した場合。
                  詳細はshowServerTroubleBarのコメント）
            */
            if (!result || (!result.accepted && !result.rejectReason)) {
                debugLog('サーバー側が発言を受け付けなかった（理由なし）');
                showServerTroubleBar();
                return;
            }

            if (result.accepted) {
                $('#messageInput').val('');
                updateMessageCounter();
                hideSendReject();

                /*
                    入れるものを選ぶパネルが開いていたら、閉じる。

                    【なぜ、ここで閉じるのか】
                    パネルは入力行の上へ最大420px（または画面の60%）で覆いかぶさる。
                    開いたままだと、**送った発言が見えない。**

                    【入力欄のクリアと、まったく同じ判断に乗せてある】
                    受け付けられたときだけ閉じる。弾かれたとき（500文字超・連投・
                    直前と同じ）は開いたままにする——打ち直すかもしれないので、
                    そこで閉じると、絵文字を入れ直すのにもう一度開くことになる。

                    【このパネルが開いているときだけ、閉じること】
                    setOpenPanel(null) は「どれも開いていない」状態にするので、
                    確かめずに呼ぶと、**参加者一覧や設定を開いたまま発言した人の
                    パネルまで、勝手に閉じてしまう。**
                    あの2つは入力行に覆いかぶさらないので、閉じる理由が無い。

                    閉じること自体は setOpenPanel に任せる（排他も aria も onClose も、
                    あちらがまとめて面倒を見ている）。ここで決めるのは「閉じるかどうか」だけ
                */
                /*
                    【本体あてに開いているときだけ】
                    パネルがこっそりの窓あてに開いているなら、本体の発言とは関係が無い。
                    閉じると、窓へ絵文字を入れている途中の人の手元が消える
                */
                InsertPanel.closeIfTarget(null);

                return;
            }

            /*
                断られたが、利用者が直せる理由がある（文字数・連投・同じ内容）。
                こちらは接続の話ではないので、入力欄の上の1行に出す
            */
            showSendReject(result.rejectReason);

        }).fail(function () {

            /*
                呼び出しそのものが失敗した。入力欄は消さない。

                ここへ来る主な理由は、発行でハブのメソッド署名が変わった直後に、
                古いまま開かれていたページが古い引数で呼んだ場合である。
                SignalRがメソッドを解決できず、呼び出しが失敗する。
                利用者から見た結果は上の「理由なしで受け付けられなかった」と
                同じなので、同じ帯に合流させる
            */
            debugLog('発言の送信に失敗しました');

            /*
                ただし、送っている最中に接続が切れた場合も、ここへ来る。
                それは「接続の話」なので、既存の切断ハンドラ
                （reconnecting / disconnected）に任せて、ここでは何もしない。
                上書きすると、再接続を試みている最中なのに
                「サーバー側の都合」と表示され、原因を取り違えさせてしまう
            */
            if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                return;
            }

            showServerTroubleBar();
        });
    }

    $('#sendMessageBtn').on('click', function () {
        sendMessage();
    });

    /*
        textarea内でのEnterキー
          PC     … Enter＝送信、Shift+Enter＝改行
          スマホ … Enter＝改行（送信は送信ボタンだけに任せる）
    */
    $('#messageInput').on('keydown', function (e) {

        /*
            日本語入力の変換中に押されたEnterは、変換候補を確定するためのもので、
            送信の意図ではない。ここで抜けないと、変換を確定しただけで送信されてしまう
        */
        if (e.originalEvent && e.originalEvent.isComposing) {
            return;
        }
        /* isComposingが無い、古いブラウザ向けの保険 */
        if (e.which === 229) {
            return;
        }

        /* タッチ端末では、Enterは常に改行として扱う */
        if (isTouchDevice) {
            return;
        }

        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });


    /* === 退室処理 === */

    /*
        === この部屋を出て、ラウンジへ移る（退室ボタン・帯の「ラウンジに戻る」の両方から）===

        【必ずラウンジへ移る。つながっていても、いなくても】
        以前は、ハブの leaveRoom の返事を待ってから移動していた。
        スマホを放っておいてから戻ると SignalR が「再接続中」のまま止まっていることがあり、
        その返事が来ない（例外で止まる）ので、**押しても何も起きなかった**
        （本番。退室がサーバーへ届かず、席が無言タイムアウトの1時間まで残った）。

        【順番】
          1. つながっていれば、ハブの leaveRoom（種別30で外す・グループから外す・残りの人へ知らせる）。
             待つのは LEAVE_HUB_WAIT_MS まで
          2. HTTP の WebMethod LeaveRoom（Session を消す・**席も外す**＝ChatHub.LeaveRoomByBrowserId）。
             1 が済んでいれば席はもう無いので、何も起きない。1 が届かなかったときの本命。
             待つのは LEAVE_HTTP_WAIT_MS まで
          3. ラウンジへ移る。上の2つが返ってこなくても、LEAVE_MAX_WAIT_MS で必ず移る

        【markLeavingRoom は呼ぶ側で】退室ボタンは音を鳴らす前に立てる（鳴っている間に
        切断の警告が出ないように）。ここでは立っている前提で進む
    */
    var LEAVE_HUB_WAIT_MS = 1500;
    var LEAVE_HTTP_WAIT_MS = 2500;
    var LEAVE_MAX_WAIT_MS = 4500;

    function leaveRoomAndGoToLounge() {

        var moved = false;
        function goToLounge() {
            if (moved) {
                return;
            }
            moved = true;
            window.location.href = '/chat/lounge';
        }

        /* 最後の歯止め。何が返ってこなくても、これで必ず移る */
        setTimeout(goToLounge, LEAVE_MAX_WAIT_MS);

        function leaveByHttp() {
            /*
                Friendly URL経由だとPageMethodsの自動URL生成が404になるため、
                Lounge.aspxのEnterRoomと同様、物理パスを直接指定して呼び出す
            */
            $.ajax({
                type: 'POST',
                url: '/Chat/ChatRoom.aspx/LeaveRoom',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                timeout: LEAVE_HTTP_WAIT_MS
            }).always(goToLounge);
        }

        if ($.connection.hub.state !== $.signalR.connectionState.connected) {
            debugLog('退室：つながっていないため、ハブを待たずに HTTP で退室する');
            leaveByHttp();
            return;
        }

        var httpStarted = false;
        function thenHttp() {
            if (httpStarted) {
                return;
            }
            httpStarted = true;
            leaveByHttp();
        }

        setTimeout(function () {
            if (!httpStarted) {
                debugLog('退室：ハブの返事が ' + LEAVE_HUB_WAIT_MS + 'ms 来ないため、HTTP で退室する');
            }
            thenHttp();
        }, LEAVE_HUB_WAIT_MS);

        /* 失敗しても（.always）HTTP へ進む。ハブの失敗で止めない */
        chatHub.server.leaveRoom(roomId).always(thenHttp);
    }

    $('#leaveRoomBtn').on('click', function () {

        var $btn = $(this);

        /*
            【二度押しを止める。あわせて「効いている」ことを見せる】
            音を鳴らし終わるまで移動を待たせるので、押した直後に
            **何も起きない時間が数百ミリ秒できる。**
            そのままだと「押しても反応しないボタン」に見えて、もう一度押される
        */
        if ($btn.prop('disabled')) {
            return;
        }

        $btn.prop('disabled', true);

        /* 0. 自分の意思での離脱のため、このあとのdisconnectedで警告を出さないようにする */
        markLeavingRoom();

        /*
            === 音を鳴らし終わってから、退室の処理へ進む ===

            【順番に意味がある】
            ①鳴らす ②鳴り終わるまで待つ ③それから移動する。
            移動してしまうと鳴らす場所そのものが無くなるので、
            **移動より先に鳴らすしかない**（「やらない」と決めたのを、
「退室の処理の前に鳴らせないか」で覆したもの）。

            【退室のビーコンとの順番は変わらない】
            ビーコンが飛ぶのは `pagehide`／`beforeunload`＝**画面が閉じるとき**で、
            それは③の `window.location.href` より後である。
            ここで待つのは①〜③の手前なので、**ビーコンとの前後関係は動かない**
            （そもそも markLeavingRoom を通っているので、この経路ではビーコンは飛ばない）。

            【鳴らないときは、待たない】
            通知音がオフの人・まだ画面を一度も触っていない人は、
            `leaveThenRun` がその場で呼び戻すので、**今までとまったく同じ速さで退室する**
        */
        Sounds.leaveThenRun(leaveRoomAndGoToLounge);

    });

});
