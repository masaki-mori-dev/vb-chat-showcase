<%@ Page Language="vb" AutoEventWireup="false" CodeBehind="ChatRoom.aspx.vb" Inherits="vb_chat.ChatRoom" %>

<%@ Import Namespace="vb_chat" %>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <%-- 【interactive-widget=resizes-content は、この画面の要である】

         この指定が無いと、Android Chrome は既定（resizes-visual）で動く。
         それは「ソフトキーボードはページを縮めず、下に覆いかぶさる」という意味で、
         .chat-room は 100dvh のまま＝入力エリアがキーボードの下に潜ったままになる。

         ブラウザはキャレットが見える位置までスクロールしてくれるが、
         キャレットは入力欄の一番上の行にある。入力欄が3行ぶんの高さを持っていると、
         その下端（＝送信ボタンの高さ）はキーボードの下に残り、押せない。
         これが「スマホで送信ボタンが押せない」の正体だった（実測）。

         resizes-content にすると、キーボードが出たときにページそのものが縮む。
         100dvh がキーボードの上の高さになるため、チャットログだけが縮んで、
         入力エリアは必ず画面の中に残る。

         【iOS Safari は、この指定を解釈しない】
         そちらは入力行そのものを低くすること（1行から始める）で対処している。
         詳しい経緯は 「A-1」の節にある --%>
    <meta name="viewport" content="width=device-width, initial-scale=1.0, interactive-widget=resizes-content" />
    <title>チャットルーム</title>
    <%-- 配色の定義元。ChatRoom.cssより先に読み込むこと（var()の参照先が先に必要なため）。
         このページはSite.Masterを使わない独立ページのため、Site.Master側の<link>は効かない。
         そのため、ここに自前で書く必要がある（消すと色がまとめて効かなくなる）。

         href は common.AssetUrl を通す。ファイルを直すと URL の ?v= が自動で変わり、
         本番の30日キャッシュを確実に飛び越えられる。
         （このページは <%@ Import Namespace="vb_chat" %> があるので、
           Site.Master のように受け渡し役のプロパティを挟まなくても common を直接呼べる。
           下の iconSpriteUrl の value と同じ形）
         【ルール】ここに <link> や <script> を足すときは、必ず AssetUrl を通すこと。
         通し忘れたファイルは、直しても30日間、利用者に届かない --%>
    <link href="<%: common.AssetUrl("~/Content/theme.css") %>" rel="stylesheet" type="text/css" />
    <link href="<%: common.AssetUrl("~/Content/ChatRoom.css") %>" rel="stylesheet" type="text/css" />
    <%-- 「?」の説明の小窓（Site.Master と同じファイル。写しを作らない） --%>
    <link href="<%: common.AssetUrl("~/Content/help.css") %>" rel="stylesheet" type="text/css" />
</head>
<body>
    <%-- アイコンスプライトの置き場所。下の icon-sprite.js が、これを読んで取りに行く。

         【、埋め込みからファイルへ変えた】
         以前はここで <%= common.GetIconSpriteHtml() %> と書いて86KBを貼り付けていた。
         .aspx はキャッシュされないため、部屋を開くたびに送り直しており、
         実測で **gzip後の約7割** がこれだった。

         【必ず common.AssetUrl を通すこと】付く ?v= が無いと、
         アイコンを足しても30日間ブラウザに届かない --%>
    <input type="hidden" id="iconSpriteUrl" value="<%: common.AssetUrl("~/Content/icon-sprite.svg") %>" />
    <%-- サーバー側（Page_Load）が確定させた部屋IDを、JSへ受け渡すための隠しフィールド

         【Sessionを直接読まないこと】
         部屋IDの出どころはSessionだけではない。Sessionが消えていても在室していれば、
         Page_LoadがハブのRoomParticipantsから引き当てている（TryResolveRoomId）。
         ここでSessionを読み直すと、その場合だけ空になり、JS側のroomIdがNaNになって
         入室も発言もできなくなる（しかもエラーは出ない） --%>
    <input type="hidden" id="roomId" value="<%= RoomId %>" />
    <%-- 定員（人数表示の分母）。現在人数はリアルタイムに変わるためJS側で数える --%>
    <input type="hidden" id="maxUsers" value="<%= MaxUsers %>" />
    <%-- 入室・退室で鳴らす効果音（mp3）の場所。

         【なぜ隠しフィールド経由なのか】
         URLを <script> の中へ直接書くと、`<%: %>`（HTML用のエスケープ）を
         JavaScriptの文字列の中で使うことになり、宿題7で踏んだ罠の再来になる。
         属性値なら `<%: %>` が正しいエスケープであり、roomId・maxUsers と同じ形で済む。

         【AssetUrl を通してある】ファイルを差し替えると ?v= が変わるので、
         本番の30日キャッシュを確実に飛び越えられる。

         【ファイルはリポジトリにも発行物にも無い】効果音ラボの素材で再配布が
         禁止されているため、FTPで手置きする（「効果音（mp3）の置き場所」）。
         置き忘れても読み込みに失敗するだけで、合成音に落ちて動き続ける --%>
    <%-- この部屋で、画像を送れるか（ChatRooms.IsImageUploadEnabled）。

         【なぜ、隠しフィールドで渡すのか】
         こっそりの窓の入力行にも画像のボタンを出すが、あの窓はJSが組み立てるので、
         markup の <% If %> では出し分けられない。

         【出し分けの元は、1つだけであること】
         この値も、上の「画像」タブの <% If IsImageUploadEnabled Then %> も、
         同じ ChatRoom.aspx.vb のプロパティから来ている。
         2か所で別々に判断すると、いつか食い違う --%>
    <input type="hidden" id="imageUploadEnabled" value="<%: If(IsImageUploadEnabled, "1", "0") %>" />
    <input type="hidden" id="soundEnterUrl" value="<%: common.AssetUrl("~/Content/sounds/door-open.mp3") %>" />
    <input type="hidden" id="soundLeaveUrl" value="<%: common.AssetUrl("~/Content/sounds/door-close.mp3") %>" />
    <%-- ノックの音。ドアの音と同じく FTP で手置き（git にも発行物にも入らない）。
         置き忘れても、合成音の「コン、コン」が鳴る（Scripts/sounds.js） --%>
    <input type="hidden" id="soundKnockUrl" value="<%: common.AssetUrl("~/Content/sounds/knock.mp3") %>" />
    <%-- 絵文字ピッカーの、本体と日本語データの場所。

         【ページを開いた時点では、どちらも読まない】
         😀ボタンを初めて押したときに、はじめて取りに行く（LINEと同じ挙動）。
         合わせて 670KB あるので、絵文字を使わない人に配ってはいけない。
         2度目からはブラウザのキャッシュから出る。

         上の効果音とまったく同じ理由で、隠しフィールド経由にしてある
         （<script> の中に `<%: %>` を書かないため）。

         【AssetUrl を通してある・必須】
         本番の <clientCache> は .json にも掛かる（30日）。?v= が付かないと、
         データを作り直しても30日間ブラウザに届かない --%>
    <input type="hidden" id="emojiLibUrl" value="<%: common.AssetUrl("~/Scripts/lib/emoji/emoji-mart.js") %>" />
    <%-- 【重要】emoji-ja.js は、名前は .js だが中身は JSON である。
         「拡張子が間違っている」と判断して .json へ直さないこと。

         IIS は既定で application/json を圧縮しない。.json のままだと
         673KB が無圧縮のまま流れる（本番で実測）。.js にすると
         Content-Type が application/javascript になり、圧縮対象に入る。

         実測（まったく同じ中身で比較）
           .json → 圧縮なし  673,372 B
           .js   → gzip      139,686 B

         読み方は今までどおり fetch ＋ Response.json()。
         あれは Content-Type を見ないので、名前が .js でも JSON として読める。
         判断の経緯と、却下した2案は にある --%>
    <input type="hidden" id="emojiDataUrl" value="<%: common.AssetUrl("~/Scripts/lib/emoji/emoji-ja.js") %>" />
    <%-- 色を選ぶ道具（jscolor。62KB）の場所。

         【ページを開いた時点では読まない】設定パネルの「見た目」で色のボタンを押したときに、はじめて取りに行く
         （絵文字ピッカーと同じ考え方。色を変えない人に、毎回62KBを配らない）。
         ファイル名にバージョンが入っていないので、AssetUrl を必ず通す（トップページの <script> と同じ理由） --%>
    <input type="hidden" id="jscolorUrl" value="<%: common.AssetUrl("~/Scripts/lib/jscolor.min.js") %>" />
    <div class="chat-room">
        <%-- 自分のプロフィール表示エリア（アイコン・名前は、JS側でCookieの値から差し替える） --%>
        <div class="my-profile-bar">
            <%-- 部屋名と、クリックで参加者一覧パネルを開閉する人数表示（現在人数/定員）を、ひとまとめにする。

                 【人数▼と歯車を .room-actions で包んだ】
                 横幅が足りなくなったとき、**部屋名を上の段・ボタン2つを下の段**へ
                 折り返させるため（こうすると部屋名が省略されなくなる）。
                 包まないと、.room-info が折り返したときに
                 **歯車だけが下の段へ落ちる**（部屋名と人数▼が上、歯車が下）。
                 理由の全体は Content/ChatRoom.css の .room-info のコメントにある --%>
            <div class="room-info">
                <span class="room-name" title="<%= Server.HtmlEncode(RoomName) %>"><%= Server.HtmlEncode(RoomName) %></span>
                <div class="room-actions">
                    <%-- 人数（参加者一覧を開くボタン）。

                         【読み上げソフトには、別の言い方で渡す】（読み上げの点検の回）
                         それまでは中の文字がそのまま名前になっていて、「かっこ 3 スラッシュ 20 かっこ 黒い下向き三角形」
                         のようにしか読まれなかった——**何のボタンなのかも、押すと何が起きるのかも伝わらない**。
                         目に見える「(3/20)」と「▼」は aria-hidden にして、名前は aria-label で
                         「参加者 3人、定員20人」と渡す（ラウンジの .room-count が「3人、定員20人」と
                          読ませているのと同じ言い方に揃えた）。数字は JS の updateParticipantCount が書き換える。

                         【aria-expanded / aria-controls】押すと参加者一覧が開くことを伝える。
                         値の付け替えは setOpenPanel が一手に引き受けているが、**初期値はここに書く**
                         ——一度もパネルを開いていない人には、JS が一度も触っていないため --%>
                    <button type="button" id="participantCountBtn" class="participant-count-btn"
                            aria-expanded="false" aria-controls="participantPanel"
                            aria-label="参加者 0人、定員<%= MaxUsers %>人">
                        <span id="participantCountText" aria-hidden="true">(0/<%= MaxUsers %>)</span>
                        <span id="participantToggleIcon" class="participant-toggle-icon" aria-hidden="true">▼</span>
                    </button>

                    <%-- 設定パネルを開閉する歯車ボタン。
                         アイコンはインラインのSVGで描いている。絵文字（⚙）は環境によって
                         見た目が大きく変わるため使わない。
                         Content/icon-sprite.svg は利用者のアイコン用のため、そこへは混ぜない --%>
                    <%-- 通知音のオン/オフ。

                         要望：通知音のオン・オフは、歯車の中だけでなく、
                         表でもすぐに切り替えられるようにする。
                         音を止めたい場面は急に来る（人が来た・電車に乗った）ので、
                         歯車を開いて項目を探すのでは遅い。

                         【表に出すのは「鳴らす／鳴らさない」だけ】音量の5段階は歯車の中のまま。
                         【状態は1か所】設定パネルのスイッチと、JS側の setSoundEnabled で揃える。
                         【形で見せる】オンは音波、オフは ✕。どちらを出すかは aria-pressed が決める
                         （初期値は JS が入れる。ここは既定の「オフ」の形で出しておく） --%>
                    <button type="button" id="soundBtn" class="sound-btn"
                            aria-pressed="false" aria-label="通知音" title="通知音：オフ（押すとオン）">
                        <svg class="sound-icon" viewBox="0 0 24 24" aria-hidden="true"
                             fill="none" stroke="currentColor" stroke-width="2"
                             stroke-linecap="round" stroke-linejoin="round">
                            <%-- スピーカーの本体（オン・オフ共通） --%>
                            <path d="M4 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4 Z" fill="currentColor" />
                            <%-- オン：音波が2本 --%>
                            <g class="sound-icon-on">
                                <path d="M15.2 9.2 Q17 12 15.2 14.8" />
                                <path d="M17.8 6.8 Q21.4 12 17.8 17.2" />
                            </g>
                            <%-- オフ：✕ --%>
                            <g class="sound-icon-off">
                                <line x1="15.5" y1="9.5" x2="20.5" y2="14.5" />
                                <line x1="20.5" y1="9.5" x2="15.5" y2="14.5" />
                            </g>
                        </svg>
                    </button>

                    <%-- 【aria-label と aria-expanded を書くこと】（読み上げの点検の回）
                         中の絵は aria-hidden なので、名前は title だけが支えていた
                         （title は「ほかに何も無いときの最後の手段」で、当てにしない）。
                         開閉は setOpenPanel が付け替えるが、初期値はここ（人数ボタンと同じ理由） --%>
                    <button type="button" id="settingsBtn" class="settings-btn" title="設定"
                            aria-label="設定" aria-expanded="false" aria-controls="settingsPanel">
                        <svg class="settings-icon" viewBox="0 0 24 24" aria-hidden="true">
                            <%-- 歯車の歯。中心から放射状に、短い線を8本並べている --%>
                            <g stroke="currentColor" stroke-width="2.4" stroke-linecap="round">
                                <line x1="12" y1="2.6" x2="12" y2="5.6" />
                                <line x1="12" y1="18.4" x2="12" y2="21.4" />
                                <line x1="2.6" y1="12" x2="5.6" y2="12" />
                                <line x1="18.4" y1="12" x2="21.4" y2="12" />
                                <line x1="5.35" y1="5.35" x2="7.47" y2="7.47" />
                                <line x1="16.53" y1="16.53" x2="18.65" y2="18.65" />
                                <line x1="5.35" y1="18.65" x2="7.47" y2="16.53" />
                                <line x1="16.53" y1="7.47" x2="18.65" y2="5.35" />
                            </g>
                            <%-- 歯車の輪と、中心の穴 --%>
                            <circle cx="12" cy="12" r="5.4" fill="none" stroke="currentColor" stroke-width="2" />
                            <circle cx="12" cy="12" r="1.9" fill="currentColor" />
                        </svg>
                    </button>

                    <%-- こっそりの未読の知らせ。

                         【なぜ、参加者一覧の中だけでは足りないのか】
                         あちらはパネルを開かないと見えない。「自動で開く」をオフに
                         していると窓も開かないので、**パネルを開くまで、届いたこと自体に
                         気づけない**（指摘：作った本人でも何の音か分からず、
                         不具合かと思った）。音が鳴るのと同じタイミングで、ここにも出す。

                         【参加者一覧の中の印は、そのまま残す】
                         **誰から来たのかは、そちらでしか分からない**ため。
                         ここに出すのは「来ている」という事実だけ。

                         【この箱の中に置いてあるが、並んではいない】
                         CSS で absolute にして、人数ボタンのすぐ下へぶら下げている。
                         **バーの中に並べると、狭い画面で部屋名が押し出されて
                         バーが2行になる**（375pxで 53px → 76px。CSS側に実測を書いた）。

                         既定では非表示。JS（PrivateTalk）が hidden を付け外しする。

                         【押すと、最後に来た相手の窓が開く】（span から button にした）
                         それまでは押しても何も起きず、人数 → 一覧で相手を選ぶ、の2段だった。
                         押せる範囲は印の大きさぴったり（CSS 側に理由）--%>
                    <button type="button" id="privateUnreadMark" class="private-unread-mark" hidden="hidden"
                            title="押すと、こっそりの窓が開きます">こっそり未読</button>
                </div>
            </div>

            <%-- アイコン・名前・退室ボタンを、右側の1グループとしてまとめる --%>
            <div class="my-profile-group">
                <div class="my-avatar" id="myAvatar">
                    <svg viewBox="0 0 512 512">
                        <use href="#icon-male"></use>
                    </svg>
                </div>
                <%-- 名前と、その下の行のトリップ。
                     トリップは JoinRoom の戻り値（myTrip）から JS が入れる。
                     合言葉の Cookie は HttpOnly なので、JS では計算も読み取りもできない。
                     付けていない人・トリップ機能が止まっているときは hidden のまま --%>
                <div class="my-name-block">
                    <%-- 名前と、管理人の札。札は自分が管理人のときだけ JS が出す --%>
                    <span class="my-name-row">
                        <span class="my-nickname" id="myNickname"></span>
                        <span class="owner-badge my-owner-badge" id="myOwnerBadge" hidden="hidden"><span aria-hidden="true">管理人</span><span class="visually-hidden">（管理人）</span></span>
                    </span>
                    <span class="trip-code my-trip" id="myTrip" hidden="hidden"><span class="visually-hidden">トリップ </span><span id="myTripText"></span></span>
                    <%-- アカウントID。アカウントID を表示して入った人は、トリップの代わりにこちらが出る（どちらか一方）。
                         値は JoinRoom の戻り値（myAccountName）から JS が入れる（Scripts/chat-room.js の setMyTrip）。
                         押すと、自分の公開のプロフィールが新しいタブで開く --%>
                    <a class="account-id my-account" id="myAccount" hidden="hidden" target="_blank" rel="noopener"><span class="visually-hidden">アカウント </span><span id="myAccountText"></span><span class="visually-hidden">（新しいタブで開きます）</span></a>
                </div>
                <button type="button" id="leaveRoomBtn" class="leave-btn">退室</button>
            </div>
        </div>

        <%-- 接続が切れた時に表示する帯。既定は非表示で、JS側が状態に応じて出し分ける

             aria-live は、帯全体（#connectionBar）ではなく文字の<span>に付ける。
             帯に付けると、再接続ボタンの出し入れまで読み上げの対象になってしまう。

             polite にしてあるのは、#sendRejectText・#counterAnnounce と揃えるため。
             この帯は「今すぐ操作が要る」場面と「再接続中です」の両方を出す1本の窓口で、
             assertive にすると再接続の試行のたびに読み上げへ割り込み、かえって聞き取れなくなる --%>
        <div id="connectionBar" class="connection-bar">
            <span id="connectionBarText" aria-live="polite"></span>
            <button type="button" id="reconnectBtn" class="connection-bar-btn">再接続</button>
            <button type="button" id="backToLoungeBtn" class="connection-bar-btn">ラウンジに戻る</button>
        </div>

        <%-- チャットログと、その上に覆いかぶさる各パネル（参加者一覧・設定）をまとめるラッパー --%>
        <div class="chat-log-wrapper">
            <%-- ダミー発言は削除。発言はすべてJSがReceiveMessageを受けて動的に追加する

                 tabindex="-1" は、「新着あり」を押したあとの、フォーカスの行き先にするため。
                 -1 なのでTabキーの順番には入らない（＝目で見て使う人の操作は何も変わらない）。
                 これが無いと、ボタンを押した瞬間にボタン自身が消えてフォーカスが迷子になり、
                 キーボードだけで使っている人が、読んでいた位置を見失う。

                 【aria-live や role="log" は付けない】
                 付けると、届いた発言をすべて読み上げ続けることになり、会話にならない。
                 新着を知らせるのは、下の #newMessageAnnounce が「出たときに1回だけ」行う --%>
            <div id="chatLog" class="chat-log" tabindex="-1"></div>

            <%-- 参加者一覧パネル（既定では非表示。人数表示のクリックで開閉）

                 【上部のボタンは1組だけ】
                 参加者ごとにボタンを並べる形にはしない。定員20人なら40個になってしまう。
                 「一覧で相手を選ぶ → このボタンを押す」という2段の形にしてある。

                 【ボタンは1つのまま、文言が変わる】
                 既に非表示の人を選ぶと「非表示をやめる」になる。
                 これが無いと、間違えて押した人は部屋を出るまで戻せない。

                 【「こっそり話す」も、同じ行に並べてある】
                 選び方（一覧で相手を選ぶ）も、押せない条件（自分は選べても押せない）も
                 まったく同じなので、2段の形をそのまま共有している --%>
            <div id="participantPanel" class="participant-panel">
                <%-- 閉じるボタン。3つのパネル（参加者一覧・設定・絵文字）すべてに、
                     同じ見た目・同じ位置（右上）で置いてある。

                     【なぜ3つとも付けるのか】
                     「外を触ると閉じる」は、慣れている人には自明だが、
                     そうでない人には見つけられない（開発者の判断：年配の利用者にはそのほうが分かりやすい）。
                     **閉じ方が3つとも同じなら、1回覚えれば全部に効く。**

                     【なぜ、パネルの中身に重ねず、独立した行にしているのか】
                     右上へ絶対配置すると、参加者一覧では「非表示にする」ボタンと重なる。
                     3つで中の構造が違うので、重ねずに1行を確保するほうが、
                     どのパネルでも同じ位置になる --%>
                <div class="panel-close-row">
                    <button type="button" class="panel-close-btn" data-close-panel="#participantPanel"
                            aria-label="閉じる" title="閉じる">×</button>
                </div>
                <div class="participant-panel-actions">
                    <button type="button" id="ignoreToggleBtn" class="ignore-toggle-btn" disabled="disabled">非表示にする</button>
                    <%-- 名前は「こっそり話す」。「秘密トーク」「内緒モード」のような言い方を
                         やめたのは、素人・年配の方が来る前提のサイトだからである --%>
                    <button type="button" id="privateTalkBtn" class="ignore-toggle-btn" disabled="disabled">こっそり話す</button>
                    <%-- 「プロフィール」。選んだ相手がアカウントID を出しているときだけ出る（hidden を JS が外す）。
                         押すと公開のプロフィールが新しいタブで開く。行き先と読み上げ用の名前は JS が入れる（updateIgnoreButton） --%>
                    <a id="participantProfileLink" class="ignore-toggle-btn participant-profile-link" hidden="hidden"
                       target="_blank" rel="noopener">プロフィール</a>
                    <%= HelpButton("room.participants") %>
                </div>
                <%-- 管理人の道具（Scripts/chat-room.js の OwnerTools）。
                     **管理人の画面にだけ出す**（hidden を JS が外す）。出さないことは防御ではない（ハブが必ず確かめる）。

                     【無視・こっそりと同じ「選んでから押す」】相手ごとにボタンを並べない（定員20人なら60個になる）。
                     押せる条件も同じ（誰かを選んでいて、それが自分ではない）。押すと確認を1回出す。

                     【別の行にしてある】無視・こっそりは「自分の画面の話」、こちらは「部屋の話」で、押したときの重さが違う。
                     同じ行に並べると、「非表示にする」のつもりで「入室禁止にする」を押す、が起きやすい。
                     地の色は「部屋の設定」と同じ「管理人のもの」の色（theme.css の --color-owner-bg・--color-owner-line）

                     【理由の1行（#ownerToolsStatus）は aria-live】断られた理由（その人はもう部屋に居ない、など）

                     【見出しは出さない】（決定：「管理人の道具」という見出しは要らない。歯車の方と同じく背景の色が違うだけでよい）。
                     管理人の場所だと分かるのは、地の色だけ（「部屋の設定」と同じ）。
                     **読み上げソフトには名前を残す**＝role="group" と aria-label。焦点がこの中のボタンに入った瞬間に
                     「管理人だけが使えるボタン、グループ」のように1回読まれる（見出しの代わり。目に見える文字は増えない）。
                     .visually-hidden の文字にしなかったのは、Tab で進む人には読まれないため（文字は焦点が止まらない） --%>
                <div id="ownerTools" class="owner-tools" role="group" aria-label="管理人だけが使えるボタン" hidden="hidden">
                    <div class="owner-tools-buttons">
                        <button type="button" id="ownerHandOverBtn" class="ignore-toggle-btn owner-tool-btn" disabled="disabled">管理人をゆずる</button>
                        <button type="button" id="ownerKickBtn" class="ignore-toggle-btn owner-tool-btn" disabled="disabled">退室させる</button>
                        <button type="button" id="ownerBanBtn" class="ignore-toggle-btn owner-tool-btn owner-tool-btn--strong" disabled="disabled">入室禁止にする</button>
                        <%= HelpButton("room.ownerTools") %>
                    </div>
                    <span id="ownerToolsStatus" class="owner-tools-status" aria-live="polite"></span>
                </div>
                <ul id="participantList" class="participant-list"></ul>
            </div>

            <%-- 設定パネル（既定では非表示。settingsBtnクリックで開閉）

                 【設計の意図】今後、通知音のオン/オフをはじめ、利用者が変えられる設定は
                 増えていく。それらをすべてプロフィールバーに並べると、すぐに破綻する。
                 そのため設定は最初からこの1か所にまとめ、ここへ項目を足していく。
                 今は項目が1つ（ログ表示数）だけだが、増える前提の作りにしてある。

                 項目を増やすときは、.settings-item の行をそのまま1つ足すだけでよい --%>
            <div id="settingsPanel" class="settings-panel">
                <%-- 閉じるボタン（3つのパネル共通。理由は参加者一覧パネルのコメント） --%>
                <div class="panel-close-row">
                    <button type="button" class="panel-close-btn" data-close-panel="#settingsPanel"
                            aria-label="閉じる" title="閉じる">×</button>
                </div>
                <div class="settings-list">

                    <%-- 項目：見た目（部屋に居ながら、自分のアイコンと色を変える）

                         【名前は変えられない】（名前だけは変えさせない）。
                         名前は身元そのもの。色とアイコンは見た目だけで、身元に関わらない。
                         変えても部屋のログにお知らせ行は出さない。過去ログの色とアイコンも変わらない。

                         【形】ふつうの .settings-item の1行に、項目名「見た目」と、アイコンのボタン・色のボタンを並べ、
                         状態の1行だけを次の行へ落とす（flex-basis: 100%）。
                         「こっそり話す」の説明文を次の行へ回したのと同じ形で、揃えるための指定は足していない。
                         **15-3a-2（管理人だけが部屋の5つを変える）も、この形に行を並べる**。

                         【、アイコンの20個を小窓へ移した】
                         要望：アイコンはこれからも増えるので、色と同じく、押したら小窓が開く形にする。
                         それまでは20個を行の下に並べていたので、増やすほど設定パネルが縦に伸びた。
                         今は「今のアイコンが入ったボタン」を1つ置き、押すと小窓（#appearanceIconPanel）が開く。
                         **増えても縦に伸びるのは小窓の中だけ**（小窓の中がスクロールする。設定パネルの大きさは変わらない）。

                         【4つ目のパネルにしなかった】パネルは「開くのは1つだけ」なので、設定パネルから開くと
                         設定パネルが閉じる。閉じたあと焦点をどこへ戻すかを新しく作り込むことになる。

                         【色を選ぶ道具（jscolor 62KB）は、色のボタンを押したときに初めて取りに行く】
                         場所は下の隠しフィールド #jscolorUrl（AssetUrl を通す）。取りに行っている間はボタンに
                         「読み込み中」と出し、取れなかったら理由を出して、もう一度押せるようにする（部屋は止まらない）。
                         アイコンの並びは、jscolor が取れなくても使える。

                         【項目名は label ではなく span】色のボタンの名前は「アイコンの色」で、項目名とは別にしてある
                         （label for にすると、項目名を押しただけで色の道具が開く）。 --%>
                    <div class="settings-item">
                        <span class="settings-item-label">見た目</span>
                        <%-- アイコンを選ぶボタン（中の絵は、今のアイコン。JS が描き替える）。
                             押すと下の小窓が開く。**中の svg は押されない**（押したのはボタン、にする。
                             色のボタンの中の span と同じ理由＝下の .appearance-icon-btn > svg） --%>
                        <button type="button" id="appearanceIconBtn" class="appearance-icon-btn"
                                aria-label="アイコンを選ぶ" aria-expanded="false"
                                aria-controls="appearanceIconPanel" aria-describedby="appearanceStatus">
                            <svg viewBox="0 0 512 512" aria-hidden="true"><use id="appearanceIconBtnUse" href=""></use></svg>
                        </button>
                        <button type="button" id="appearanceColorBtn" class="appearance-color-btn"
                                aria-label="アイコンの色を選ぶ" aria-describedby="appearanceStatus">
                            <span class="appearance-color-swatch" id="appearanceColorSwatch" aria-hidden="true"></span>
                            <span class="appearance-color-text" id="appearanceColorText" aria-hidden="true">色</span>
                        </button>
                        <%-- アイコンを選ぶ小窓。

                             【position: fixed で、body ではなくここに置いてある】
                             設定パネルは中がスクロールするので、absolute だと切り落とされる。
                             fixed なら画面が基準になるので切られない（置き場所は JS が入れる）。
                             DOM ではパネルの中の子なので、「外を押したら閉じる」の判定でも
                             わざわざ例外を足さずに済む……わけではない（fixed でも closest は DOM をたどるので中とみなされる）。

                             【小窓の中は、今までと同じラジオボタンの形】ただし**矢印で移っただけでは決まらない**。
                             移って、Enter か Space（かクリック）で決める＝決めた瞬間に小窓が閉じるため。
                             理由は Scripts/chat-room.js の Appearance の「小窓の中の選び方」 --%>
                        <div id="appearanceIconPanel" class="appearance-icon-panel" hidden="hidden">
                            <div id="appearanceIcons" class="appearance-icons" role="radiogroup" aria-label="アイコン">
                                <%= AppearanceIconChoicesHtml %>
                            </div>
                        </div>
                        <%-- 状態の1行（読み込み中・取れなかった・変えられなかった）。
                             aria-live は、隠れている間の変更を拾わないので、この要素は隠さない（空のときは高さ0） --%>
                        <span id="appearanceStatus" class="appearance-status" aria-live="polite"></span>
                    </div>

                    <%-- 項目：ログ表示数
                         選択肢はChatLogReader.AllowedMessageCountsから組み立てている
                         （発言も、入退室のお知らせ行も、まとめてこの数に入る） --%>
                    <div class="settings-item">
                        <label class="settings-item-label" for="logCountSelect">ログ表示数</label>
                        <select id="logCountSelect" class="log-count-select" title="過去ログを何行表示するか">
                            <%= LogCountOptionsHtml %>
                        </select>
                    </div>

                    <%-- 項目：通知音（入室・退室・発言で、それぞれ違う音を鳴らす）

                         【既定はオフ】初期状態はサーバーが決める（SiteSettingsの
                         NotificationSoundDefault）。一度でも切り替えると、以後は
                         Cookie（ChatSoundEnabled）が優先される。

                         【組み立て方は、ラウンジの「ノック」と同じ形にしてある】
                         項目名の<label for>で操作するものと結びつけ、その隣に
                         見た目だけスイッチにしたチェックボックスを置く。
                         こうしておくと、読み上げソフトでも「通知音 チェックボックス」と
                         読まれ、項目名をタップしても切り替わる（当たりが広くなる）--%>
                    <div class="settings-item">
                        <label class="settings-item-label" for="soundToggle">通知音</label>
                        <label class="toggle-switch">
                            <input type="checkbox" id="soundToggle" />
                            <span class="toggle-slider"></span>
                        </label>
                    </div>

                    <%-- 項目：こっそり話す（送られてきたら自動で開く）

                         【既定はオン】（開発者の指定）

                         【見出しが、そのまま説明になる形にしてある】
                         ボタンの名前だけで説明しようとすると意味が伝わらない
                         （指摘：「チェンジ」という名前では何が起きるか分からない）。
                         そのため、この行だけ項目名を2行にして、
                         下の行に「何が起きるか」を書いている。

                         【ふつうの .settings-item のままにしてあること】
                         最初は「項目名を2行にする専用の行」として作ったが、
                         項目名に flex: 1 を持たせたせいで**トグルが右端まで飛び、
                         他の項目とスイッチの左端が揃わなくなった**
                         （本番の実機で発覚）。

                         今の形は、他の項目とまったく同じ
                         「項目名（6em）＋ 操作するもの」の1行に、
                         説明だけを次の行へ回り込ませたものである
                         （.settings-item は flex-wrap: wrap なので、
                           flex-basis: 100% を持つ子は自然に次の行へ落ちる）。

                         「こっそり話す」はちょうど6文字なので、
                         項目名の min-width: 6em にそのまま収まる --%>
                    <div class="settings-item">
                        <label class="settings-item-label" for="privateAutoOpenToggle">こっそり話す</label>
                        <label class="toggle-switch">
                            <input type="checkbox" id="privateAutoOpenToggle" />
                            <span class="toggle-slider"></span>
                        </label>
                        <span class="settings-item-note">送られてきたら自動で開く</span>
                    </div>

                    <%-- 項目：通知音の大きさ（小・中・大）

                         【なぜ、サイト側にこの設定が要るのか】
                         端末のスピーカーによって、聞こえ方がまるで違う
                         （実測で、ChromeOS機はWindows機の約2倍でちょうど良かった）。
                         サイト側で1つの値に決められないため、利用者に選ばせている。

                         【スライダーではなくセレクトボックスにしてある】
                         スマホで指で摘まみにくいのと、通知音にそこまでの細かさが要らないため。
                         表示行数と同じ形にしておけば、作りも1つで済む。

                         【項目名は6文字まで】
                         .settings-item-label の min-width が 6em のため、
                         これより長い項目名を足すときはCSSのほうも広げること
                         （広げないと、この行だけ操作するものの左端がずれる）--%>
                    <div class="settings-item">
                        <label class="settings-item-label" for="soundVolumeSelect">通知音の音量</label>
                        <select id="soundVolumeSelect" class="log-count-select"
                                title="このサイトの通知音の大きさ">
                            <%-- 順番は「小さいほうから大きいほうへ」。
                                 value（段階の名前）と倍率の対応は、JS側の VOLUME_SCALES が持つ。
                                 【罠】ここに倍率の数字を書かないこと。書くと2か所管理になる --%>
                            <option value="very-quiet">いちばん小さい</option>
                            <option value="quiet">小さめ</option>
                            <option value="normal">ふつう</option>
                            <option value="loud">大きめ</option>
                            <option value="very-loud">いちばん大きい</option>
                        </select>
                    </div>

                    <%-- === 部屋の設定（管理人だけ）===

                         変えられるのは、部屋を作るときに決める5つ。
                         **管理人にだけ出す**（JS の RoomSettings が、管理人の番号と自分の番号を見て hidden を外す。
                         管理人が替わったら、その場で出し入れする）。
                         **出さないことは防御ではない**——管理人かどうかは、ハブ（ChangeRoomSettings）が必ず確かめる。

                         【形】「見た目」と同じ .settings-item の行を並べる（項目名6文字まで・左端が揃う）。
                         説明とパスワードの欄は flex-basis: 100% で次の行へ落とす（揃えるための指定は足していない）。

                         【その場で反映する】（「保存」ボタンをやめた）
                         指摘：ほかの場所はトグルを動かしただけで変更が反映されるのに、
                         管理人の設定だけ保存ボタンを押さないと反映されないのは、そろっていない。
                         定員＝選んだとき／ノック・画像＝スイッチを動かしたとき／鍵＝外すのはスイッチを切ったとき、
                         かけるのは文字を入れて「かける」を押したとき（Enter でも）／部屋名＝「変える」を押したとき（Enter でも）。
                         部屋のログの1行は、変更が止まってから数秒後にまとめて1行（サーバーがまとめる）。

                         【今のパスワードは、管理人には見える】（「管理人にも見えない」を覆した）
                         要望：一時部屋のパスワードは管理人自身もすぐ忘れるので、確かめられるようにする。
                         最初は伏せておき、「見る」を押したときだけハブ（GetRoomPassword）から1回もらう（スマホで肩越しに見られるため）。
                         パネルを閉じたら、また伏せる。**ページには埋め込まない**（管理人でない人の画面にも届くため）。
                         新しいパスワードの欄は空で出す。type="text"・autocomplete="off" はラウンジの「部屋を作る」と同じ理由
                         （人に伝えて使う言葉。ブラウザにパスワードとして保存させない）。

                         最初の値は Page_Load が DB から読んだもの（data-* に入れる）。そのあとは配信（roomSettingsChanged）で揃う --%>
                    <div id="roomSettings" class="room-settings" hidden="hidden"
                         data-room-name="<%: RoomName %>" data-max-users="<%= MaxUsers %>"
                         data-knock="<%= If(IsKnockEnabled, "1", "0") %>"
                         data-image="<%= If(IsImageUploadEnabled, "1", "0") %>"
                         data-has-password="<%= If(HasPassword, "1", "0") %>">

                        <h3 class="room-settings-title">部屋の設定<span class="room-settings-title-note">（管理人だけ）</span></h3>

                        <div class="settings-item">
                            <label class="settings-item-label" for="roomSettingName">部屋名</label>
                            <input type="text" id="roomSettingName" class="room-setting-input"
                                   maxlength="<%= ChatRoomRules.RoomNameMaxLength %>" autocomplete="off"
                                   enterkeyhint="done" value="<%: RoomName %>" />
                            <button type="button" id="roomSettingNameApply" class="room-setting-btn">変える</button>
                        </div>

                        <div class="settings-item">
                            <label class="settings-item-label" for="roomSettingMaxUsers">定員</label>
                            <select id="roomSettingMaxUsers" class="log-count-select" aria-describedby="roomSettingMaxUsersNote">
                                <%= RoomSettingsMaxUsersOptionsHtml %>
                            </select>
                            <%-- 【説明は aria-describedby で項目と結ぶこと】（読み上げの点検の回）
                                 ラウンジの「部屋を作る」は最初からそうなっていたのに、ここだけパスワード以外が
                                 結ばれておらず、読み上げでは「定員 コンボボックス」で終わっていた --%>
                            <span class="settings-item-note" id="roomSettingMaxUsersNote">今居る人数より少なくしても、居る人は出されません（新しく入る人だけ断ります）</span>
                        </div>

                        <div class="settings-item">
                            <label class="settings-item-label" for="roomSettingKnock">ノック</label>
                            <label class="toggle-switch">
                                <input type="checkbox" id="roomSettingKnock" aria-describedby="roomSettingKnockNote" />
                                <span class="toggle-slider"></span>
                            </label>
                            <span class="settings-item-note" id="roomSettingKnockNote">満員のときに、入れてほしい人がノックできます</span>
                        </div>

                        <div class="settings-item">
                            <label class="settings-item-label" for="roomSettingImage">画像</label>
                            <label class="toggle-switch">
                                <input type="checkbox" id="roomSettingImage" aria-describedby="roomSettingImageNote" />
                                <span class="toggle-slider"></span>
                            </label>
                            <span class="settings-item-note" id="roomSettingImageNote">画像を送れるようにします（オフにしても、これまでの画像は消えません）</span>
                        </div>

                        <div class="settings-item">
                            <label class="settings-item-label" for="roomSettingLock">パスワード</label>
                            <label class="toggle-switch">
                                <input type="checkbox" id="roomSettingLock" aria-describedby="roomSettingLockNote" />
                                <span class="toggle-slider"></span>
                            </label>
                            <span class="settings-item-note" id="roomSettingLockNote">鍵をかける（入る人にパスワードを聞きます）</span>
                            <div class="room-setting-current" id="roomSettingPasswordCurrent" hidden="hidden">
                                <span class="room-setting-current-label">今のパスワード：</span>
                                <%-- 【aria-live を付けてある】（読み上げの点検の回）
                                     「見る」を押しても焦点はボタンのままなので、付けていないと
                                     **パスワードそのものが一言も読まれない**（押したのに何も起きないように聞こえる）。
                                     伏せ字（●）に戻したときは、●を6つ読ませても意味が無いので、
                                     JS の hidePassword が「伏せています」という読み上げ用の文字に差し替える --%>
                                <span class="room-setting-current-value" id="roomSettingPasswordValue" aria-live="polite">●●●●●●</span>
                                <button type="button" id="roomSettingPasswordReveal" class="room-setting-btn room-setting-btn--quiet"
                                        aria-pressed="false" aria-controls="roomSettingPasswordValue">見る</button>
                            </div>
                            <div class="room-setting-password-row">
                                <input type="text" id="roomSettingPassword" class="room-setting-input room-setting-password"
                                       maxlength="<%= RoomPassword.MaxLength %>" autocomplete="off" enterkeyhint="done"
                                       aria-label="新しいパスワード" />
                                <button type="button" id="roomSettingPasswordApply" class="room-setting-btn">かける</button>
                            </div>
                        </div>

                        <%-- 変えた・断られた理由（aria-live）。保存ボタンは やめた --%>
                        <span id="roomSettingsStatus" class="room-settings-status" aria-live="polite"></span>

                        <%-- 入室禁止にした人（Scripts/chat-room.js の RoomSettings の「入室禁止にした人」）。
                             1人ずつ［アイコン 名前（トリップ）］［解除］。中身は設定パネルを開いたときにハブ（getRoomBans）から取る。
                             **ChatBrowserId は画面に無い**（1人ぶんは匿名IDで指す）。項目名は6文字まで（.settings-item-label の決まり） --%>
                        <div class="settings-item room-bans">
                            <span class="settings-item-label" id="roomBansLabel">入室禁止</span>
                            <span class="settings-item-note">入室禁止にした人です。「解除」を押すと、また入れるようになります</span>
                            <ul id="roomBanList" class="room-ban-list" aria-labelledby="roomBansLabel" hidden="hidden"></ul>
                            <span id="roomBanEmpty" class="room-ban-empty" tabindex="-1">いません</span>
                            <span id="roomBansStatus" class="room-settings-status" aria-live="polite"></span>
                        </div>

                    </div>

                </div>
            </div>

            <%-- こっそりの窓を置く層。窓はJS（PrivateTalk）が動的に足す。

                 【パネルより後ろ、「新着あり」より前に置いてある】
                 重なり順はDOMの順で決めている（z-indexではない）。
                 窓は参加者一覧・設定パネルより上に出したいが、
                 「新着あり」は窓の上に残しておきたい。

                 【この回では、位置の作り込みをしない】
                 ドラッグ移動・カスケード・スマホでのタブ切り替えは次の回。
                 今は「往復が成立すること」だけを見る --%>
            <div id="privateTalkLayer" class="private-talk-layer">
                <%-- 狭い画面（スマホ）で、相手の名前を並べて窓を切り替えるタブ。
                     覆いかぶさる形なので、一度に見えるのは1つだけになる。
                     中身はJS（PrivateTalk.renderTabs）が作る。
                     広い画面ではCSSで消している（窓を並べて置けるので、要らない） --%>
                <div id="privateTalkTabs" class="private-talk-tabs"
                     role="tablist" aria-label="こっそり話している相手" hidden="hidden"></div>
            </div>

            <%-- 「新着あり」。過去ログを遡って読んでいる最中に新しい行が届いたときだけ出る。
                 既定は非表示で、JS側（showNewMessageBadge）が is-visible を付け外しする。

                 【パネルより後ろに置いてある】
                 参加者一覧・設定パネルと同じ絶対配置のため、DOMで後に置いたほうが上に来る。
                 パネルを開いている最中に新着が来ても、押せる位置に残しておきたいので後ろに置く。

                 【<button>であること自体が、読み上げソフトへの配慮になっている】
                 divにクリックを付けると、キーボードでも読み上げソフトでも到達できない。
                 出たことを知らせるのは、下の #newMessageAnnounce の役目 --%>
            <button type="button" id="newMessageBtn" class="new-message-btn">
                <span id="newMessageBtnText"></span>
                <span class="new-message-arrow" aria-hidden="true">▼</span>
            </button>

            <%-- 【読み上げ専用】新着が届いたことの知らせ。

                 「新着あり」ボタンは、目で見れば出たことが分かる。音声で使っている人には
                 その変化が届かないため、ここへ1回だけ文言を入れて知らせる。
                 消えるときに空へ戻すので、次に出たときも必ず読み上げられる
                 （文言が変わらないと読み上げが起きないため、空に戻すことが必要）。

                 【罠】この要素を display: none にしてはいけない。
                 aria-live は、隠れている要素の中では一言も読まれない（宿題1で踏んだ罠）。
                 .visually-hidden は1pxに切り抜くだけで、読み上げ対象には残る --%>
            <span id="newMessageAnnounce" class="visually-hidden" aria-live="polite"></span>

            <%-- === ノックの返事待ち（部屋の管理人の画面にだけ中身が入る）===

                 満室の部屋に誰かがノックすると、ここに「〇〇さんがノックしています [断る] [入れる]」が並ぶ
                 （Scripts/chat-room.js の KnockRequests。60秒のカウントダウン付き）。
                 ログの上端に浮かせる。重なり順は 25（パネルの手前・新着ありの奥。ChatRoom.css の「重なり順の決め方」）。

                 【role="region" ＋ 名前】読み上げソフトの「ランドマーク」で、ここへ飛べるようにする。
                 中身が無い間は hidden（空の領域を読ませない）。
                 【焦点は奪わない】管理人は話している最中かもしれないので、届いたことは下の1行で知らせるだけ --%>
            <div id="knockRequests" class="knock-requests" role="region" aria-label="ノックの返事" hidden="hidden"></div>

            <%-- 【読み上げ専用】ノックが届いたことの知らせ（KnockRequests の tell）。
                 【display: none にしないこと】aria-live は隠れている要素の中では読まれない --%>
            <span id="knockAnnounce" class="visually-hidden" aria-live="polite"></span>

            <%-- 【読み上げ専用】ログの中のスタンプ・画像に焦点が乗ったときに添える説明。

                 ログの中のスタンプ・画像は、Tab で止まるのが1つだけで、中は ↑↓ で移る
                 （Scripts/chat-room.js の LogKeyNav）。その動かし方を、各スタンプ・画像の
                 aria-describedby からここを指して伝える。部屋のログも、こっそりの窓のログも、ここ1つを使う。
                 aria-live ではない（焦点が乗ったときに、名前に続けて読まれるだけ）。
                 【display: none にしないこと】aria-describedby の先が隠れていると読まれない --%>
            <span id="logKeyHint" class="visually-hidden">上下の矢印キーで、ほかのスタンプや画像へ移れます。</span>

            <%-- 【読み上げ専用】こっそりの未読が届いたことの知らせ。

                 上の #newMessageAnnounce と、まったく同じ考え方で作ってある。
                 目で見える人には上の「こっそり未読」の文字で伝わるが、
                 音声で使っている人にはその変化が届かないため、ここへ文言を入れる。

                 【罠】既に出ているときは、何もせずに抜けること。
                 抜けないと、届くたびに同じ文言が入り直して読み上げ続ける。

                 【罠】消えるときは、必ず空へ戻すこと。
                 aria-live は「中身が変わったとき」に読むので、
                 残したままだと次に出ても一言も読まれない。

                 【罠】display: none にしてはいけない。
                 aria-live は隠れている要素の中では一言も読まれない --%>
            <span id="privateUnreadAnnounce" class="visually-hidden" aria-live="polite"></span>
        </div>
        <%-- 入力欄の上の1行。左に「送信できなかった理由」、右に文字数（現在 / 上限）を出す。

             【接続状態バー（connection-bar）とは、別の入れ物にしてある】
             あちらは「サーバーとつながっているか」専用で、出ている間は入力欄ごと
             使えなくなる。こちらは「つながってはいるが、この発言は受け付けられなかった」
             という別の話で、入力欄は使えたままにする必要がある。
             1つにまとめると、状態の組み合わせが増えて必ず取り違える。

             【行そのものは、常に置いておく】
             理由が出たときだけ行を作ると、そのぶん入力欄が下から突き上げられて、
             打っている最中に画面が跳ねる。文字数を常に出しておけば高さが動かない --%>
        <div class="chat-input-info">
            <%-- 改行のしかたの案内。文言はJS側（applyInputHint）が、端末に応じて入れる
                 （PCは Shift+Enter、タッチ端末は Enter。キー操作の仕様そのものが違うため）。

                 【なぜ要るのか】
                 入力欄を1行から始める形にしたため、「複数行も打てる」ことが
                 見た目から伝わらなくなった。その埋め合わせをここで行う。

                 【なぜ、ここに置くのか】
                 この行は「跳ねないように」常に高さを確保してある場所で、
                 左半分は理由が出るときまで空いている。新しい行を作らずに済む。
                 理由が出たら、そちらへ場所を譲る（showSendRejectが入れ替える）。

                 textareaのaria-describedbyからも参照しているので、
                 入力欄にカーソルを入れた時点で、音声でも改行のしかたが分かる --%>
            <span id="inputHint" class="input-hint"></span>

            <%-- 送信できなかった理由。aria-liveを付けているのは、音声だけで使っている人に
                 「送信ボタンを押したのに、何も起きなかった」と思わせないため。
                 値が変わったときにだけ読まれるので、普段は黙っている --%>
            <span id="sendRejectText" class="send-reject-text" aria-live="polite"></span>

            <%-- 文字数（現在 / 上限）。textareaのaria-describedbyから参照しており、
                 入力欄にカーソルを入れた時点で、上限が音声でも分かるようにしてある --%>
            <span id="messageCounter" class="message-counter"></span>

            <%-- 【読み上げ専用】目には見えない、上限が近いことの知らせ。

                 数字そのもの（#messageCounter）をaria-liveにしてはいけない。
                 1文字打つたびに数字を読み上げられ、会話にならなくなる。
                 ここは「段階が変わった瞬間」だけ文言が入るようにしてあり、
                 普段は空＝何も読まれない --%>
            <span id="counterAnnounce" class="visually-hidden" aria-live="polite"></span>
        </div>
        <div class="chat-input-row">
            <%-- 【maxlengthは、あえて付けていない】
                 名前欄と違って、ここは長文を貼る場所である。maxlengthを付けると、
                 上限を超えた分がブラウザによって黙って捨てられ、利用者には
                 「なぜか途中までしか入らない」としか見えない。
                 上限は、文字数の表示と送信ボタンの無効化で知らせ、
                 最終的な判定はサーバー（ChatHub.SendMessage）で行う

                 aria-describedbyで文字数の表示と結びつけている。
                 目で見える人が「0 / 500 文字」を最初から見られるのと同じように、
                 音声で使っている人も、カーソルを入れた時点で上限を知れるようにするため。
                 #inputHint も一緒に読ませて、改行のしかたも同時に伝える
                 （並べた順に読まれるので、改行の案内 → 文字数、の順にしてある）

                 【rows="3" にしてある。自動伸縮（LINE式）は入れて、戻した】
                 2026年9月4日に rows="1" から中身に応じて伸ばす形を一度入れたが、
                 見た目が変わるだけの価値が無いと判断して戻した
                 （伸縮は iOS Safari のための備えだったが、手元にiOS端末が無く、
                   効いているかを確認できないため
                   の入力エリアの項）。

                 キーボードに隠れる問題のほうは、viewportの
                 interactive-widget=resizes-content が受け持っている --%>
            <textarea id="messageInput" class="message-input" placeholder="メッセージを入力" rows="3"
                      aria-describedby="inputHint messageCounter"></textarea>

            <%-- 送信ボタンと絵文字ボタンを、縦に積む箱。

                 【なぜ箱で包むのか】
                 .chat-input-row は align-items: flex-end なので、この箱ごと
                 下端に貼り付く。中で縦に積めば、送信ボタンは今までと同じ位置のまま、
                 その上に丸いボタンが乗る。入力欄の幅も、送信ボタンの大きさも変わらない
                 （揃えを stretch から center へ変えても送信は 60px のまま。実測で確認済み）。

                 【空きがあることは実測してある】
                 入力欄の高さ 93.5px に対して、44 ＋ すき間 8 ＋ 送信 33 ＝ 85px。
                 8.5px 余る（実際の画面で計測）。 --%>
            <div class="chat-input-actions">
                <%-- 発言に入れるもの（絵文字・スタンプ）を選ぶパネルを開くボタン。

                     【、😀 の文字から丸いボタンへ作り替えた】
                     それまでは絵文字の 😀 をそのまま中身にしていた。
                     絵文字は環境ごとにフォントが違い、字面の余白も見た目の大きさも変わるため、
                     「人の目から見て、まんまるが大きく見えること」を満たせない。
                     歯車ボタンが同じ理由でインラインの SVG になっているので、判断も揃う。
                     **「絵文字だけは 😀 を使ってよい例外」は、この作り替えで役目を終えた。**

                     中の絵は、外側の円のふちに沿う大きさで描いてある。
                     色は指定しない（currentColor で、ボタン側の color を拾う）。 --%>
                <button type="button" id="insertBtn" class="insert-btn"
                        title="絵文字・スタンプを入れる" aria-label="絵文字・スタンプを入れる"
                        aria-expanded="false" aria-controls="insertPanel">
                    <svg class="insert-btn-icon" viewBox="0 0 24 24" aria-hidden="true"
                         fill="none" stroke="currentColor" stroke-width="2">
                        <%-- 顔の輪郭。線の太さ2なので、外へ1はみ出して 11.6 まで。
                             viewBox の端（12）まで 0.4 残る --%>
                        <circle cx="12" cy="12" r="10.6" />
                        <%-- 目。塗りつぶしなので、線の指定を打ち消しておく --%>
                        <circle cx="8.4" cy="9.6" r="1.5" fill="currentColor" stroke="none" />
                        <circle cx="15.6" cy="9.6" r="1.5" fill="currentColor" stroke="none" />
                        <%-- 口。両端から下へふくらむ曲線 --%>
                        <path d="M7.3 14.3 Q12 18.6 16.7 14.3" stroke-linecap="round" />
                    </svg>
                </button>
                <button type="button" id="sendMessageBtn" class="send-btn">送信</button>
            </div>

            <%-- 発言に入れるものを選ぶパネル。入力欄の上へ浮かせる
                 （.chat-input-row が position: relative）。

                 【emojiPanel から改名した】
                 中身が「絵文字」と「スタンプ」の2つになり、いずれ画像アップロード（Eの回）の
                 入口もここに並びうるため、絵文字に寄せた名前をやめた。

                 【入口のボタンは1つのまま。中でタブを切り替える（LINE と同じ）】
                 スタンプ用のボタンを縦にもう1つ積むと、狭い画面で入力欄が潰れる。

                 【中身は、押されるまで空のまま】
                 ピッカー本体もデータも、絵文字のタブが最初に表に出たときに取りに行く。
                 初回だけ待つので、待っていることが分かる表示を出す（#emojiTabStatus）。
                 **スタンプのタブを覚えたまま開いたときは、絵文字のデータを取りに行かない。**

                 【内蔵の検索窓は使わない】
                 Emoji Mart には検索語の前処理フックが無いため、内蔵の窓では
                 利用者が打った語に正規化（カタカナ→ひらがな等）を掛けられない。
                 searchPosition: 'none' で消して、下の自前の窓から
                 SearchIndex.search() を呼ぶ。これは公開APIで、内部に手を入れる形ではない。 --%>
            <div id="insertPanel" class="insert-panel" role="dialog" aria-label="絵文字・スタンプを選ぶ">
                <%-- タブと閉じるボタンの行。

                     【閉じるボタンは、3つのパネルとも右端。動かしていない】
                     .panel-close-row は justify-content: flex-end なので、
                     タブ側の margin-right: auto だけで、タブが左・閉じるが右端になる。

                     【このパネルだけ、閉じたときの後片付けがある】
                     参加者一覧と設定は panels 配列と setOpenPanel() が面倒を見ているが、
                     こちらはそこに乗ったうえで、検索窓と結果を片付ける必要がある。
                     JS側の InsertPanel／EmojiPicker がまとめて扱っている --%>
                <div class="panel-close-row">
                    <div class="insert-tabs" role="tablist" aria-label="入れるものを選ぶ">
                        <button type="button" class="insert-tab" id="insertTabEmoji"
                                role="tab" aria-selected="true" aria-controls="insertPaneEmoji"
                                data-insert-tab="emoji">絵文字</button>
                        <button type="button" class="insert-tab" id="insertTabStamp"
                                role="tab" aria-selected="false" aria-controls="insertPaneStamp"
                                data-insert-tab="stamp">スタンプ</button>
<%-- 【この部屋で画像が許されていなければ、タブを隠す】

     判定は ChatRooms.IsImageUploadEnabled ただ1つで、部屋名では決まらない
     （開発者の判断：今後、部屋名や設定は変えるかもしれない）。

     【出さないことは、防御ではない】
     受け口（Chat/UploadImage.aspx）が、同じ列をもう一度自分で確かめている。
     ここでやっているのは「押せない入口を見せない」という親切のほうである。

     【、いつも出力して hidden で隠す】
     それまでは <% If %> でタブごと出さなかった。管理人が部屋に居ながら画像の可否を切り替えられるようになり、
     その場で出し入れする（Scripts/chat-room.js の InsertPanel.setImageEnabled）。出力しない形のままだと、
     オンにしても F5 するまでタブを出せない。
     【JS側の白リストは、隠れているタブを外す】InsertPanel は、隠れていないタブの名前を
     DOMから拾って白リストにする。隠れていれば、覚えていた「image」も既定へ落ちる --%>
                        <button type="button" class="insert-tab" id="insertTabImage"
                                role="tab" aria-selected="false" aria-controls="insertPaneImage"
                                data-insert-tab="image"<%= ImageTabHiddenAttribute %>>画像</button>
                    </div>
                    <button type="button" class="panel-close-btn" data-close-panel="#insertPanel"
                            aria-label="閉じる" title="閉じる">×</button>
                </div>

                <%-- 打ちかけの文の見本の帯。

                     こっそりの窓のボタンから開いたとき、**狭い画面でだけ**出す
                     （本体あてのときは hidden のまま。広い画面では CSS が出さない）。
                     狭い画面では、パネルが窓の入力欄を覆うので、入れた絵文字が見えない。
                     そこで窓の入力欄の末尾をここへ写す。

                     【宛先の名前は出さない】窓のボタンから開いている以上、宛先は自明
                     （理由：その人とのこっそりの窓から開いた絵文字などのパネルなので）。
                     中身は Scripts/chat-room.js の InsertPanel.refreshPreview が入れる --%>
                <div id="insertTargetRow" class="insert-target-row" hidden>
                    <span id="insertTargetPreview" class="insert-target-preview"></span>
                </div>

                <%-- 絵文字の面 --%>
                <div id="insertPaneEmoji" class="insert-pane" role="tabpanel"
                     aria-labelledby="insertTabEmoji">
                    <div class="emoji-tab-head">
                        <%-- type="search" にすると、ブラウザが「×」を出してくれる。
                             name を付けていないのは、この画面にフォームが無いため --%>
                        <input type="search" id="emojiSearchInput" class="emoji-search-input"
                               placeholder="絵文字を探す（例：わらい）" autocomplete="off"
                               aria-label="絵文字を探す" aria-describedby="emojiSearchCount" />
                    </div>

                    <%-- 検索結果は、こちらで並べる。1件が <button>😀</button> で済むので
                         （絵でもSVGでもなく、ただの文字なので）これで足りる --%>
                    <div id="emojiSearchResults" class="emoji-search-results" hidden></div>

                    <%-- 件数の読み上げ。目には見えない。
                         打つたびに数字を読ませないよう、件数が変わったときだけ入れる --%>
                    <span id="emojiSearchCount" class="visually-hidden" aria-live="polite"></span>

                    <%-- Emoji Mart のピッカー（カテゴリを見るためのもの）が、ここへ入る --%>
                    <div id="emojiPickerHost" class="emoji-picker-host"></div>

                    <%-- 読み込み中・読み込めなかったときの表示 --%>
                    <p id="emojiTabStatus" class="emoji-tab-status" aria-live="polite"></p>
                </div>

                <%-- スタンプの面。

                     【中身は、ここには書かない】
                     セットのタブ（カエル／ヒヨコ）もグリッドも、JS（StampPicker）が
                     目次（vbChatStamps）から作る。**グリッドは、そのセットのタブが
                     初めて表に出たときに作る**——作った時点で <img> が絵を取りに行くので、
                     見てもいないセットの絵まで取らないため（絵文字のタブと同じ考え方）。

                     【押すと「選ばれた」状態になり、下の確認の帯が出る】
                     「送る」か、選んだスタンプをもう一度押すと送れる。「やめる」で取り消せる。
                     それまでは「押したら即送信」だったが、開発者が実機を見て覆した
                     （要件の本体は「送る前に中止できること」）。入力欄には入れない。
                     宛先は、パネルを開いたボタンで決まる（InsertPanel.target）。

                     【送れたあとも、パネルは開いたまま】（開発者の判断）
                     続けて送れるのが自然なため。閉じるのは ×・外を押す・Esc のとき --%>
                <div id="insertPaneStamp" class="insert-pane insert-pane--stamp" role="tabpanel"
                     aria-labelledby="insertTabStamp" hidden>
                    <%-- 【「?」はセットの選び（tablist）の外、同じ行の右端】tablist の中にボタンを置くと、読み上げで「タブ」の数がずれる --%>
                    <div class="stamp-set-row">
                        <div id="stampSetTabs" class="stamp-set-tabs" role="tablist"
                             aria-label="スタンプのセット"></div>
                        <%= HelpButton("room.stamp") %>
                    </div>
                    <div id="stampGridHost" class="stamp-grid-host"></div>

                    <%-- 確認の帯。スタンプが選ばれている間だけ出る（StampPicker.select）。
                         読み上げソフトには、下の #stampConfirmAnnounce で「選んだ」ことを伝える --%>
                    <div id="stampConfirm" class="insert-confirm stamp-confirm" hidden>
                        <img id="stampConfirmImg" class="stamp-confirm-img" alt="" />
                        <div class="insert-confirm-side">
                            <span id="stampConfirmLabel" class="insert-confirm-label"></span>
                            <div class="insert-confirm-actions">
                                <button type="button" id="stampConfirmSend"
                                        class="insert-confirm-btn insert-confirm-btn--send">送る</button>
                                <button type="button" id="stampConfirmCancel"
                                        class="insert-confirm-btn">やめる</button>
                            </div>
                        </div>
                    </div>
                    <span id="stampConfirmAnnounce" class="visually-hidden" aria-live="polite"></span>

                    <%-- 断られた理由・読めなかったときの表示。

                         【入力欄の上の1行（#sendRejectText）には出さない】
                         パネルは入力行の上に覆いかぶさるので、あの行はパネルの下に隠れる。
                         スタンプを送ったあともパネルは開いたままなので、
                         **利用者が見ているこの面の中に出す** --%>
                    <p id="stampStatus" class="stamp-status" aria-live="polite"></p>
                </div>

                <%-- 画像の面。中身は「大きなボタン1つ＋注意書き1行」と、選んだあとの確認
                     （足した。確認が出ている間は、ボタンと注意書きは隠れる）。

                     【なぜ、ここに入口を置いたのか】
                     入力行の右は、丸ボタン44 ＋ すき間8 ＋ 送信33 ＝ 85px で既に埋まっていて、
                     3つ目を積むと狭い画面で入力欄が潰れる。左に置けば入力欄の幅を食う。
                     **パネルの中なら、入力行の寸法にいっさい触らずに済む**。

                     【押しても何も無い場所を、先に作ってよい……ではない】
                     スタンプの面と違い、こちらは押せば実際に動く。
                     この部屋で許されていなければ、タブが隠れていて、この面には来られない
                     （面もいつも出力する。タブの出し入れだけで済ませるため） --%>
                <div id="insertPaneImage" class="insert-pane insert-pane--image" role="tabpanel"
                     aria-labelledby="insertTabImage" hidden>

                    <%-- 【絵は、ここには書かない】
                         同じ絵を、こっそりの窓の小さなボタンでも使う。
                         2か所に書くと、片方だけ直して食い違う。
                         Scripts/chat-room.js の imageIconSvg() が、読み込み時にここへ入れる
                         （JSが読み込めていない画面では、そもそも押しても動かない。
                           そのときは赤い帯が出る） --%>
                    <button type="button" id="imagePickBtn" class="image-pick-btn">
                        <span class="image-pick-label">画像を選ぶ</span>
                    </button>

                    <%-- 【上限の数字を、ここに書かないこと】
                         決めているのは SiteSettings（＝DBの ImageMaxBytes）である。
                         画面に書くと、値を変えたときに案内だけが嘘になる。
                         JS側が、渡された設定から組み立てて入れる --%>
                    <p class="insert-pane-notice" id="imagePickNotice"></p>

                    <%-- 選んだ画像の確認。

                         選んだあと、**縮めたあとの送る実物**を面いっぱいに大きく出してから送る
                         （選ぶ画面のサムネイルでは、間違った写真かどうか分からないため。開発者の判断）。
                         「選び直す」はファイルを選ぶ画面をすぐ開く（やめてから選び直す手間を省く）。
                         出ている間は、上の「画像を選ぶ」と案内の1行を隠す（ImageUpload.showConfirm） --%>
                    <div id="imageConfirm" class="insert-confirm image-confirm" hidden>
                        <img id="imageConfirmImg" class="image-confirm-img" alt="送る画像" />
                        <div class="insert-confirm-actions">
                            <button type="button" id="imageConfirmSend"
                                    class="insert-confirm-btn insert-confirm-btn--send">送る</button>
                            <button type="button" id="imageConfirmRepick"
                                    class="insert-confirm-btn">選び直す</button>
                            <button type="button" id="imageConfirmCancel"
                                    class="insert-confirm-btn">やめる</button>
                        </div>
                    </div>

                    <%-- 用意している・送っている・断られた理由（パネルが開いている間はここに出す） --%>
                    <p id="imageStatus" class="stamp-status" aria-live="polite"></p>
                    <span id="imageConfirmAnnounce" class="visually-hidden" aria-live="polite"></span>

                </div>
            </div>
        </div>
    </div>
    <script src="/Scripts/lib/jquery-3.7.1.min.js"></script>
    <script src="/Scripts/lib/jquery.signalR-2.4.3.min.js"></script>

    <%-- アイコンを1回だけ取ってきて、ページへ差し込む。
         この画面は待たない——まだ存在しないidを指す <use> は、
         symbol があとから現れた時点で自動的に描き直されるため（実測で確認）。
         あとから増える行（発言・入退室）も、同じ理由でそのまま出る --%>
    <script src="<%: common.AssetUrl("~/Scripts/icon-sprite.js") %>"></script>
    <script src="/signalr/hubs"></script>

    <%-- 通知音（Sounds）の中身。ラウンジ・トップページと共通（chat-room.js から外へ出した）。
         【必ず chat-room.js より前に置くこと】chat-room.js が読み込まれた時点で使う。
         読めなかったときは、chat-room.js が「鳴らない Sounds」に落とすので、部屋は止まらない --%>
    <script src="<%: common.AssetUrl("~/Scripts/sounds.js") %>"></script>

    <%-- 「?」の説明の小窓（Site.Master と同じファイル。chat-room.js とは関係しない＝どこに置いてもよい） --%>
    <script src="<%: common.AssetUrl("~/Scripts/help.js") %>"></script>

    <%-- サーバー側（Page_Load）が、ChatMessagesテーブルから読み出した過去ログ。
         入室時もF5リロード時も、この値をもとに画面へ描画する。

         【罠】ここは<%= %>でJSONを生のまま出力している。
         発言本文に "</script>" と書かれるとページが壊れるため、
         ChatLogReader.ToScriptJson()が "<" を "\u003c" へ置き換えてから渡している --%>

    <%-- サーバーから渡された設定（SiteSettings.ClientSettingsJson）

         【ここに数字をべた書きしないこと】
         決めているのはサーバー（DBのSiteSettingsテーブル）である。
         同じ数字を画面側へ書くと、サーバー側を変えたときに画面だけが嘘になり、
         「あと10文字と出ているのに、送信できない」という形で出る

         【中身は選別されている。全部の設定が入っているわけではない】
         画面が実際に使う値だけが入る。連投の間隔や無言タイムアウトのような
         サーバー内部の都合は、そもそも渡ってこない

         単位はミリ秒。DB側は秒で持っており、変換はサーバー側の1か所で済んでいる

         【なぜ、この2つだけがページに残るのか】
         下の chat-room.js は静的ファイルで、ASP.NETが中身を見ない（<% %>が効かない）。
         サーバーが決めた値を渡せるのは、この場所だけである。
         逆に言えば、**サーバーの値が要るものだけをここに置き、残りは全部外へ出す**。
         値を1つ増やしたくなったら、この <script> に1行足すこと --%>
    <%-- スタンプの目次（StampCatalog.ClientJson）

         sets   … パネルに並べるもの（IsActive が 1 のものだけ。配列の順が並び順）
         stamps … 絵を引く表（IsActive が 0 のものも入る。過去ログの絵を出すため）

         絵のURLは ?v= 付きでサーバーが組み立てて渡す。画面で組み立てないこと。
         読むのは chat-room.js（パネルの StampPicker と、発言の絵を出す buildStampHtml）。
         "</script>" で閉じられないよう、StampCatalog 側で "<" を逃がしてある --%>
    <script>
        var vbChatInitialLog = <%= InitialLogJson %>;
        var vbChatPrivateLog = <%= InitialPrivateLogJson %>;
        var vbChatSettings = <%= ClientSettingsJson %>;
        var vbChatStamps = <%= StampCatalogJson %>;
    </script>

    <%-- 部屋の画面を動かすJavaScript本体。

         【必ず、上の <script> より後に置くこと】
         このファイルは、上の <script> の4つの変数を読む。
         jQuery・SignalR・/signalr/hubs より後であることも必要
         （読み込む順番の理由は、chat-room.js の先頭のコメントにまとめてある）。

         【必ず AssetUrl を通すこと】
         本番は静的ファイルを30日キャッシュさせる設定のため、
         ?v= が付いていないと、直しても30日間ブラウザに届かない --%>
    <%-- 【読み込めなかったときに、黙って死なせないための帯】

         このファイルが取れないと、部屋は「見た目はあるが、何をしても無反応」になる。
         発言もできず、入退室も出ず、エラーも出ない——いちばん気づきにくい壊れ方である。

         インラインで書いていた頃は、この壊れ方が存在しなかった
         （HTMLが届いた時点でJSも届いていたため）。外部ファイルにした代償なので、
         代わりの見張りをここに置く。

         【CSSに頼らないこと】
         見た目はすべてこの場で指定してある。ChatRoom.css も一緒に届いていない
         可能性があるため、外部ファイルを1つも当てにしない --%>
    <div id="scriptLoadError"
         style="display: none; position: fixed; top: 0; left: 0; right: 0; z-index: 9999;
                padding: 12px 16px; background: #B91C1C; color: #FFFFFF;
                font-size: 15px; line-height: 1.5; text-align: center;">
        画面を動かすファイルを読み込めませんでした。ページを読み込み直してください。
    </div>
    <script src="<%: common.AssetUrl("~/Scripts/chat-room.js") %>"
            onerror="var e = document.getElementById(&#39;scriptLoadError&#39;); if (e) { e.style.display = &#39;block&#39;; }"></script>
</body>
</html>
