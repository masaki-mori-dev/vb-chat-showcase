<%@ Page Language="VB" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.vb" Inherits="vb_chat._Default" %>

<%@ Import Namespace="vb_chat" %>


<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- 部屋から戻された理由を知らせる帯。
         ラウンジ（Lounge.aspx）にあるものと、同じ仕組み・同じ見た目。
         部屋の画面から理由を持ち越してきた時だけ、JavaScriptが中身を入れて表示する
         （初期状態では、CSSのdisplay:noneで隠れている）

         ここにも置いてあるのは、戻す先がラウンジとは限らないため。
         名前が決まっていない・ブラウザの識別情報が壊れている場合は、
         決め直せるこの画面へ戻すので、理由もこの画面で伝える必要がある --%>
    <div id="leaveReasonBar" class="leave-reason-bar">
        <span id="leaveReasonText" class="leave-reason-text"></span>
        <button type="button" id="leaveReasonCloseBtn" class="leave-reason-close-btn">閉じる</button>
    </div>

    <div class="intro">
        <h1>VBチャット(仮)へようこそ！</h1>
        <%-- 【サイトの文は、開発者が直した形が正】
             サイトに書く文は開発者が直す。頼まれない限り文面を変えない。

             【検索エンジンに載せない設定（noindex）】
             （Global.asax.vb の Application_BeginRequest） --%>
        <p>VB.NETのWebフォームで作ったリアルタイムチャットです。完全無料で誰でも気軽に参加できます。</p>
    </div>


    <%-- 左の「はじめに」の列をやめて、決める枠 1 つにまとめた（要望：両方を同じ形にする。avatar-chat のトップも同じ形）。
         「はじめに」の文と使い方・利用規約の文は、1 字も変えずに枠の見出しのすぐ下へ移した。見出しは h1 の次なので h2 にした（前は h3）。
         外側の div（intro-and-nickname）は 1 列の入れ物として残した（中は枠 1 つ）。枠の id（nickname-area）は、使い方の写真を撮る道具が使うので残す --%>
    <div class="intro-and-nickname">

        <section class="nickname-area" id="nickname-area">

            <h2>アイコン・色・名前を決めよう！</h2>
            <p class="nickname-area-lead">
                アイコンと色を選び、名前を入れて「決定してラウンジに移動」を押してください。
                <%-- 「使い方」（howto）と「利用規約」（terms）の文は、その名前の記事が公開されているときだけ出る
                     （Default.aspx.vb の IntroLinksHtml。行き先の番号をここに書かない） --%>
                <%= IntroLinksHtml %>
            </p>


            <%-- role="radiogroup"。中の1つ1つは role="radio" のボタンで、
                 Tab で止まるのは選んでいる1つだけ、矢印キー・Home・End で移って選ぶ
                 （部屋の設定パネルの「見た目」のアイコンと同じ形。あちらは小窓の中）。
                 それまでは <div> を押す形で、キーボードでは1つも選べなかった --%>
            <div class="icon-list" role="radiogroup" aria-label="アイコン">
                <%-- 枠はサーバー側が組み立てる（Default.aspx.vb の IconChoicesHtml）。
                     中の絵は、スプライトが届いた時点で <use> が自動的に解決する。

                     【JavaScriptで作らない理由】
                     以前はスプライトの <symbol> を拾って作っていたが、
                     スプライトをファイルとして取りに行く形にしたため、
                     そのままでは届くまで選択肢が0個になり、
                     画面が1188px跳ね、取れなかったときは1つも選べなかった --%>
                <%= IconChoicesHtml %>
            </div>


            <%-- 【label には for を書くこと】（読み上げの点検の回で足した）
                 指す相手の無い label は、読み上げソフトから見るとただの文字で、
                 欄のほうには名前が1つも無い状態だった（「編集 読み取り専用 #2563EB」としか読まれない）。
                 「準備中」を label にしなかったのと同じ話 --%>
            <div class="icon-color-select">
                <label for="iconColorPicker">色を選択</label>
                <input id="iconColorPicker" data-jscolor="{}" value="<%: displayIconColor %>" readonly />
            </div>

            <%-- === 通知音のオン/オフ===

、却下した案を覆した。
                 当時の理由は ①トップは簡単・気楽な入口にしておきたい ②設定は部屋の設定パネル1か所に集める。
                 ②はバーへスピーカーを足した時点で「状態は1か所（Cookie）、入口は2つ」に変わっていた。
                 残る①を守るため、**置くのはオン/オフだけ。音量（5段階）は置かない**
                 （トップが設定画面のようになると、①が本当に崩れる）。

                 【トップに来る人は、毎回「既定」から始まる】ログアウトすると通知音の Cookie も消えるため。
                 ここは、その人に最初のオン/オフを与える場所になる。

                 【保存先は、部屋・ラウンジと同じ Cookie（ChatSoundEnabled）】押した値がそのまま部屋・ラウンジへ持っていける。
                 見た目はラウンジの「ノック」と同じトグルスイッチ（Content/Site.css）。動きは下の <script> の topSoundToggle --%>
            <div class="top-sound-row">
                <label for="topSoundToggle">通知音</label>
                <label class="toggle-switch">
                    <input type="checkbox" id="topSoundToggle" />
                    <span class="toggle-slider"></span>
                </label>
            </div>

            <%-- === アカウントID（チャットとアカウントの紐づけ）===

                 【ログインしている人にだけ出る】していない人には、1文字も出ない。
                 最初からオン。オンのまま入ると、名前のそばに「@ユーザー名」が出て、押すと公開のプロフィールが開く。
                 オフにすれば、今までどおりの参加者として入れる（名乗らずに入る自由。トリップも使える）。

                 【見た目は、すぐ上の「通知音」と同じ行・同じスイッチ】（毎回の基本の1＝見た目をそろえる）。
                 スイッチの横に、実際に出る「@ユーザー名」を見せてある（何が出るのかを、押す前に分かるように）。

                 【説明は置かない】前は「オンの間は、トリップを使えません」と添えていたが、
                 ID を出していれば本人だと分かるので、トリップは要らないだけ（損のように書かない＝基本ルールC）。
                 【名前は「アカウントIDを紐づける」】ラウンジのスイッチと同じ言葉（基本ルールA）

                 【ここのチェックを、サーバーは鵜呑みにしない】受け取るのは「表示するか」だけ。
                 どのアカウントかは、ログインの札からサーバーが読む（Chat/ChatLogin.vb） --%>
            <% If isAccountLoggedIn Then %>
            <div class="top-sound-row top-account-row">
                <label for="topAccountToggle">アカウントIDを紐づける</label>
                <label class="toggle-switch">
                    <input type="checkbox" id="topAccountToggle" checked="checked" />
                    <span class="toggle-slider"></span>
                </label>
                <span class="top-account-name"><%: accountIdText %></span>
            </div>
            <% Else %>
            <%-- 【ログインしていない人には、スイッチの代わりに1行だけ】（＝紐づけられることを知らせる）
                 名前を決めて気軽に話したいだけの人のじゃまにならないよう、小さい字の1行（360 幅でも1行に入る長さにしてある）。
                 「ログイン」と「新規登録」の両方を置く（アカウントを持っているのにログインが切れた人もいる）。
                 言葉は右上の角（「ログイン」「新規登録」）とそろえた（基本ルールA＝同じものに名前を2つ付けない）。
                 【ログインが済んだら、このトップへ戻す】（基本ルールC。戻り先の決まりは Accounts/AccountReturnUrl.vb）。
                 戻ったトップでは、スイッチがオンの形で出る --%>
            <p class="top-account-note">アカウントIDを紐づけるなら
                <a href="<%: AccountLoginUrl %>">ログイン</a><span class="top-account-note-sep" aria-hidden="true">／</span><a href="/account/register">新規登録</a></p>
            <% End If %>

            <div class="nickname-input-row">
                <%-- maxlengthは、入力の時点で上限を超えさせないための手当て。
                     下のJavaScript側のチェックも残してある（貼り付けや、開発者ツールで
                     属性を外された場合に効かせるため）。サーバー側でも
                     ChatProfileValidatorが同じ長さで切り詰めており、こちらが本命。

                     文字数はSiteSettings.NicknameMaxLengthが決める。
                     ここ（maxlength）と、すぐ隣のプレースホルダーの文言と、
                     下のJavaScriptの判定と警告文の4か所すべてが同じ値を使うこと。
                     1か所でも取りこぼすとエラーは出ず、
                     「入力できたのに弾かれる」という形でだけ表に出る --%>
                <%-- 【aria-label を足した】（読み上げの点検の回）
                     それまで欄の名前は placeholder 頼みだった。placeholder は名前の「最後の手段」で、
                     打ち始めると消えるうえ、Cookie を持っている人は最初から値が入っていて一度も出ない。
                     見える文字は増やさず（見出し「アイコン・色・名前を決めよう！」がその役をしている）、
                     読み上げソフトにだけ名前を渡す --%>
                <input type="text" id="nicknameInput" class="nickname-input" aria-label="名前" placeholder="名前を入れてね（<%: NicknameMaxLength %>文字以内）" maxlength="<%: NicknameMaxLength %>" value="<%: existingNickname %>" />
                <button type="button" id="startChatBtn">決定してラウンジに移動</button>
            </div>


        </section>

        <%-- スプライトはここに埋め込まない（変更）。
             Site.Master が読み込む Scripts/icon-sprite.js が、
             ファイルとして1回だけ取ってきて差し込む --%>
    </div>




</asp:Content>



<asp:Content ID="ScriptsForThisPage" ContentPlaceHolderID="ScriptsContent" runat="server">


    <%-- 色を選ぶ部品（アイコンの色）。ファイル名にバージョンが入っていないため、
         必ず common.AssetUrl を通すこと。通さないと、本番の30日キャッシュに
         引っかかって、直しても利用者に届かない
         （jquery-3.7.1.min.js のようにファイル名へバージョンが入っているものは、
           名前そのものが印になっているので通す必要がない） --%>
    <script src="<%: common.AssetUrl("~/Scripts/lib/jscolor.min.js") %>"></script>
    <script>
        jscolor.presets.default = {
            palette: [
                '#FF0000', '#FF6B6B', '#FF9900', '#FFE66D',
                '#22B14B', '#4ECDC4', '#00A1E7', '#3F47CC',
                '#A349A4', '#95E1D3', '#7d7d7d', '#000000'
            ],
            paletteCols: 6,
            width: 181,
            height: 101,
            closeButton: true,
            closeText: '閉じる'
        };
    </script>


    <%-- === ラウンジへ入る音===

         名前を決めて「ラウンジに移動」を押したら、鳴らしてから移動する（部屋の退室ボタンと同じ形）。

         【ファイルは lounge-enter.mp3】部屋のドアの音とは別の名前にしてある。
         開発者が素材を用意したら、**このファイルを FTP で差し替えるだけ**で変わる
         （AssetUrl がファイルの更新時刻で ?v= を付けるので、発行し直さなくてもブラウザに届く）。
         置くまでは部屋のドアの音（door-open.mp3）の写しを置いておく。置き忘れても、合成音で鳴る。

         【鳴るのは、通知音がオンのときだけ】このページに来るのは Cookie が無い人
         （初めての人・ログアウトした人）なので、最初はサイトの既定（SiteSettings の NotificationSoundDefault）に従う。
、上の「通知音」のスイッチでその場でオンにできる。オンにした人にだけ鳴る。

         URL は隠しフィールドで渡す（<script> の中に <%: %> を書かない。宿題7） --%>
    <input type="hidden" id="loungeEnterSoundUrl" value="<%: common.AssetUrl("~/Content/sounds/lounge-enter.mp3") %>" />
    <script src="<%: common.AssetUrl("~/Scripts/sounds.js") %>"></script>


    <script>
        <%-- ============================================
           このページ（Default.aspx）専用のスクリプト
           アイコン選択・色選択・決定するボタンの処理
           ============================================ --%>

        <%--
            サーバーから渡された設定（SiteSettings.ClientSettingsJson）

            部屋の画面（ChatRoom.aspx）とまったく同じ中身が入る。
            変数名をvbChatSettingsで揃えているのは、ページをまたいでも
            同じ名前で同じものを指すようにするため
            （vbChatInitialLog・vbchat_leave_reason と同じ流儀）
        --%>
        var vbChatSettings = <%= ClientSettingsJson %>;

        <%-- ラウンジへ入る音（上の sounds.js と、隠しフィールドの説明を参照）。
             sounds.js が読めなかったときは null で、音が鳴らないだけで先へ進む --%>
        var topSounds = null;

        if (window.VbChatSounds) {
            topSounds = VbChatSounds.create({
                enterUrl: document.getElementById('loungeEnterSoundUrl').value
            });
            topSounds.setEnabled(VbChatSounds.readSavedEnabled(vbChatSettings.notificationSoundDefault));
            topSounds.setVolumeLevel(VbChatSounds.readSavedVolumeLevel(topSounds, vbChatSettings.notificationSoundVolumeDefault));
            VbChatSounds.unlockOnFirstInput(topSounds);
        }

        <%-- === 通知音のスイッチ（markup 側のコメントを参照）===

             最初の状態は、部屋・ラウンジと同じ規則（Cookie が '1' か '0'。無ければサイトの既定）。

             【オンにした瞬間に、発言の音（ポン）を1回鳴らす】部屋のバーのスピーカーと同じ形。
             ・鳴ったことで、オンになったと分かる
             ・押したこと自体が「利用者の操作」なので、その中で鳴らせばブラウザの自動再生の制限も同時に解ける
               （このあと「ラウンジに移動」で鳴らす音も、そのまま鳴る）
             オフにしたときは鳴らさない（止めたい人の前で音を出さない）。

             【Cookie はその場で書く】下の setCookie（名前・アイコンと同じ属性・同じ期限）。
             「ラウンジに移動」を押さずに離れても、残るのは通知音の1本だけで害は無い
             （名前が決まっていないので、次に来てもこのページが出る）。

             sounds.js が読めなかったときも、Cookie は書く（部屋・ラウンジでは効く）。音で確かめられないだけ --%>
        var topSoundToggle = document.getElementById('topSoundToggle');

        if (topSoundToggle) {

            topSoundToggle.checked = window.VbChatSounds
                ? VbChatSounds.readSavedEnabled(vbChatSettings.notificationSoundDefault)
                : false;

            topSoundToggle.addEventListener('change', function () {

                var isOn = this.checked;

                setCookie('ChatSoundEnabled', isOn ? '1' : '0');

                if (topSounds) {
                    topSounds.setEnabled(isOn);

                    if (isOn) {
                        topSounds.message();
                    }
                }
            });
        }

        <%--
            === 部屋から戻された理由の表示 ===

            部屋の画面（ChatRoom.aspx）が、sessionStorageへ預けてきた理由があれば帯で出す。
            仕組み・キー名は、ラウンジ（Lounge.aspx）とまったく同じ。
            alertを使わないのは、背面のタブではダイアログが抑制されてしまうため
        --%>
        var LEAVE_REASON_STORAGE_KEY = 'vbchat_leave_reason';

        try {
            var leaveReason = sessionStorage.getItem(LEAVE_REASON_STORAGE_KEY);

            if (leaveReason) {
                <%-- 表示したら必ず削除する。消さないと、開くたびに毎回出てしまうため --%>
                sessionStorage.removeItem(LEAVE_REASON_STORAGE_KEY);

                <%-- textContentを使う（innerHTMLではない） --%>
                document.getElementById('leaveReasonText').textContent = leaveReason;
                document.getElementById('leaveReasonBar').classList.add('is-visible');
            }
        } catch (e) {
            <%-- sessionStorageが使えない場合は、理由が表示されないだけで、他に影響はない --%>
        }

        <%--
            帯は自動では消さない。背面のタブで表示された場合、
            利用者が画面を見た時には消えてしまっているため
        --%>
        document.getElementById('leaveReasonCloseBtn').addEventListener('click', function () {
            document.getElementById('leaveReasonBar').classList.remove('is-visible');
        });

        <%-- VB側で計算した「選択済みにするアイコンの種類」を、JavaScript側の変数として受け取る。

             Cookieを持っている人はその値、初めての人は設定の既定アイコン
             （IconCatalog.DefaultIconType）が入る。

             【引用符を書いていないのは、書き忘れではない】
             ExistingIconTypeJs（Default.aspx.vb）が
             HttpUtility.JavaScriptStringEncode で引用符ごと組み立てて返す。
             ここに "<%: existingIconType %>" と書いてはいけない。
             あれはHTML用のエスケープなので、<script> の中では &quot; のような
             文字列がそのまま変数に入り、値が黙って壊れる（宿題7）。

             【この説明が、JavaScriptの // コメントではなくサーバー側コメントにある理由】
             JavaScriptの // は、サーバー側のコードブロックを守ってくれない。
             「//」の後ろに書いた <%: %> も、ASP.NETは本物のコードブロックとして解釈する
             （実際に「式を空の文字列にすることはできません」でページが止まった）。
             サーバー側コメントなら中身ごと読み飛ばされるうえ、ブラウザへも送られない --%>
        var existingIconType = <%= ExistingIconTypeJs %>;

        <%--
            【この画面は、アイコンが届くのを待たない】
            選択肢の枠はサーバーが出しており（IconChoicesHtml）、
            中の絵はスプライトが届いた時点で <use> が自動的に解決するため。

            以前はここで <symbol> を拾って選択肢を組み立てていたので、
            届くまで選択肢が0個になり、画面が1188px跳ねていた
        --%>
        document.addEventListener("DOMContentLoaded", function () {
            <%--
                アイコン一覧を作り終わったら、
                既存のアイコン（Cookieに保存されてたもの）があれば、それを選択済みにする
                無ければ、今まで通り、一番最初のアイコンを選ぶ
            --%>
            var iconToSelect = null;

            if (existingIconType !== "") {
                iconToSelect = document.querySelector('.icon-choice[data-icon="' + existingIconType + '"]');
            }

            if (!iconToSelect) {
                iconToSelect = document.querySelector('.icon-choice');
            }

            <%--
                アイコンを1つ選ぶ（クリック・矢印キー・Enter／Space から呼ばれる）。

                【、キーボードで選べるようにした】
                それまでは <div> のクリックだけで、Tab も止まらなかった。
                今は role="radiogroup" ＋ role="radio" のボタンで、**Tab で止まるのは選んでいる1つだけ**
                （roving tabindex）、矢印キー・Home・End で移って選ぶ。

                【部屋の小窓と違って、移った時点で選んでよい】
                部屋の「見た目」の小窓は、選んだ瞬間に小窓が閉じるので「移る」と「決める」を分けてある。
                こちらは閉じるものが無いので、ふつうのラジオボタンどおり**選択は焦点についてくる**形でよい。

                【見た目は今までと同じ】色は jscolor で選んでいる色を、その場で反映する
            --%>
            function selectIcon(icon, moveFocus) {

                var selectedColor = document.getElementById('iconColorPicker').jscolor
                    ? document.getElementById('iconColorPicker').jscolor.toHEXString()
                    : document.getElementById('iconColorPicker').value;

                document.querySelectorAll('.icon-choice').forEach(function (i) {
                    var isChosen = (i === icon);
                    i.classList.toggle('selected', isChosen);
                    i.setAttribute('aria-checked', isChosen ? 'true' : 'false');
                    i.tabIndex = isChosen ? 0 : -1;
                    i.style.color = isChosen ? selectedColor : '';
                    i.style.borderColor = isChosen ? selectedColor : '';
                });

                if (moveFocus) {
                    icon.focus();
                }
            }

            if (iconToSelect) {
                selectIcon(iconToSelect, false);
            }

            document.querySelectorAll('.icon-choice').forEach(function (icon) {
                icon.addEventListener('click', function () {
                    selectIcon(this, false);
                });
            });

            <%-- 矢印キー・Home・End で隣のアイコンへ移って選ぶ（Tab は並びの外へ出る） --%>
            var iconList = document.querySelector('.icon-list');

            if (iconList) {
                iconList.addEventListener('keydown', function (e) {

                    var list = Array.prototype.slice.call(document.querySelectorAll('.icon-choice'));
                    var index = list.indexOf(document.activeElement);

                    if (index < 0) {
                        return;
                    }

                    var next = null;

                    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
                        next = list[(index + 1) % list.length];
                    } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
                        next = list[(index - 1 + list.length) % list.length];
                    } else if (e.key === 'Home') {
                        next = list[0];
                    } else if (e.key === 'End') {
                        next = list[list.length - 1];
                    } else if (e.key === ' ' || e.key === 'Enter') {
                        next = list[index];
                    }

                    if (next) {
                        e.preventDefault();
                        selectIcon(next, true);
                    }
                });
            }

        });

        <%--
            jscolor で色を選んでいる間も、
            今選ばれているアイコンの色をリアルタイムに更新する
        --%>
        document.getElementById('iconColorPicker').addEventListener('input', function () {
            var selectedColor = this.jscolor.toHEXString();
            var selectedIcon = document.querySelector('.icon-choice.selected');

            if (selectedIcon) {
                selectedIcon.style.color = selectedColor;
                selectedIcon.style.borderColor = selectedColor;
            }
        });

        <%--
            Cookieを書き込む

            【、この関数で書くのは通知音のオン/オフだけになった】
            名前・アイコン・色・ChatBrowserId は、**サーバーが書く**
            （下の「決定する」→ Default.aspx.vb の StartChat → Chat/ChatLogin.vb）。
            名前には、サーバーの鍵で署名した札（ChatNameToken）が添えられる。
            JSから名前のCookieを書ける形のままだと、書き換えるだけで
            「名前を変えたら別人」の決まりが裏口から破れるため（「身元の決まり」）。

            有効期間はサーバーが決める（DB側は ProfileCookieDays。既定365日）。
            Chat/ChatRoom.aspx の setCookie（表示行数の ChatLogCount）と、
            同じ値・同じ属性を使っている。

            【SameSite=Lax】他のサイトに埋め込まれた形での送信を防ぐ。
            Laxにしてあるのは、Strictだと外部リンクから来た1度目のアクセスで
            Cookieが送られず、名前を決めてあるのにトップページへ戻されるため

            【Secure】httpsでしか送らせない。本番はhttps強制なので費用ゼロ。
            開発機のIIS Expressも、localhost はブラウザが安全な場所として扱うため
            http:// のまま受け付ける（実測で確認済み）

            【罠】この関数で encodeURIComponent を掛けてはいけない。値のエンコードは
            呼び出し側の責任にしてある（今の呼び出し先は '1' か '0' なので、通す必要が無い）

            【maxAgeSeconds は省略できる】省略すればサーバーが決めた期間（上）
        --%>
        function setCookie(name, value, maxAgeSeconds) {
            document.cookie = name + "=" + value +
                              "; max-age=" + (maxAgeSeconds || vbChatSettings.cookieMaxAgeSeconds) +
                              "; path=/; SameSite=Lax; Secure";
        }

        <%-- 「決定する」ボタンを押したら、入力チェック → サーバーへ（Cookieはサーバーが書く） --%>
        document.getElementById('startChatBtn').addEventListener('click', function () {
            var nickname = document.getElementById('nicknameInput').value.trim();

            if (nickname === "") {
                showWarning("名前を入力してください");
                return;
            }

            <%-- 上限はサーバーが決める。上のmaxlength・プレースホルダーと同じ値を使うこと --%>
            if (nickname.length > vbChatSettings.nicknameMaxLength) {
                showWarning("名前は" + vbChatSettings.nicknameMaxLength + "文字以内で入力してください");
                return;
            }

            <%--
                【保険】アイコンが1つも出ていなくても、先へ進めるようにしておく。

                アイコンはファイルとして取りに行くようになったため、
                取れないことがありうる（電波・サーバーの一時的な不調）。
                以前のように querySelector の結果をそのまま使うと、
                選択肢が0個のときに null になって、
                **決定ボタンを押しても何も起きない**状態になる。

                落とし先は existingIconType——Cookieを持っている人はその値、
                初めての人はサーバーが入れた既定のアイコン（IconCatalog.DefaultIconType）。
                アイコンは見えないが、名前は決められるし、部屋にも入れる
            --%>
            var selectedIcon = document.querySelector('.icon-choice.selected');
            var iconType = selectedIcon ? selectedIcon.getAttribute('data-icon') : existingIconType;

            if (!iconType) {
                showWarning("アイコンを読み込めませんでした。画面を読み込み直してください");
                return;
            }
            var iconColor = document.getElementById('iconColorPicker').jscolor.toHEXString();

            <%-- 「アカウントID」のスイッチ（ログインしている人にだけ在る。無ければ false）。
                 送るのは「表示するか」だけ。どのアカウントかは、サーバーがログインの札から読む --%>
            var accountToggle = document.getElementById('topAccountToggle');
            var showAccountId = !!(accountToggle && accountToggle.checked);

            <%--
                === ここから先は、サーバーが決める ===

                名前・アイコン・色・ChatBrowserId・名前の札（ChatNameToken）・
                ログインの目印（ChatJustLoggedIn）を、まとめてサーバーが Cookie に書く。
                判断は Chat/ChatLogin.vb、受け口は Default.aspx.vb の WebMethod StartChat。

                【jQuery ではなく fetch で呼ぶ】
                このページは jQuery を読み込んでいない（ラウンジと部屋だけが読む）。
                Cookie を1つ書くために30KBを初回の転送へ足すのは、割に合わない。
                fetch は Scripts/icon-sprite.js でも使っていて、対象の端末では動く。

                【罠】fetch は404でも500でも reject しない。res.ok を自分で見ること
                （icon-sprite.js で踏んだのと同じ罠）。

                【WebMethod の戻り値は {"d":"…"} の形】
                中身は文字列で、その文字列がこちらの JSON である（二重になっている）。

                【押せなくするのは、通信を始める前】待っている間に2度押されないため。
                断られたとき・通信に失敗したときは、必ず押せる状態へ戻す
                （戻さないと「押しても何も起きない」になる）
            --%>
            var startBtn = this;
            startBtn.disabled = true;

            fetch("/Default.aspx/StartChat", {
                method: "POST",
                headers: { "Content-Type": "application/json; charset=utf-8" },
                <%-- Cookie を書かせるので、同じサイトへの資格情報は必ず付ける --%>
                credentials: "same-origin",
                body: JSON.stringify({
                    nickname: nickname,
                    iconType: iconType,
                    iconColor: iconColor,
                    showAccountId: showAccountId
                })
            }).then(function (res) {
                if (!res.ok) {
                    throw new Error("HTTP " + res.status);
                }
                return res.json();
            }).then(function (envelope) {

                var result = JSON.parse(envelope.d);

                if (!result.ok) {
                    startBtn.disabled = false;
                    showWarning(result.message || "名前を決められませんでした。もう一度お試しください。");
                    return;
                }

                <%-- ラウンジへ入る音を鳴らしてから移動する（鳴らないときは、すぐ移動する） --%>
                if (topSounds) {
                    topSounds.enterThenRun(function () {
                        window.location.href = "/chat/lounge";
                    });
                } else {
                    window.location.href = "/chat/lounge";
                }

            }).catch(function (err) {
                startBtn.disabled = false;
                showWarning("通信に失敗しました。もう一度お試しください。");
                console.log('[vb-chat] StartChat に失敗： ' + err);
            });

        });

        <%-- 戻るボタンで、このページが保持されたまま戻ってきたとき（bfcache）、押せないままにしない --%>
        window.addEventListener('pageshow', function (e) {
            if (e.persisted) {
                document.getElementById('startChatBtn').disabled = false;
            }
        });


    </script>


</asp:Content>