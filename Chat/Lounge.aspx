<%@ Page Language="vb" MasterPageFile="~/Chat/ChatMaster.Master" AutoEventWireup="false" CodeBehind="Lounge.aspx.vb" Inherits="vb_chat.Lounge" %>
<%@ Register Src="~/Chat/UserControls/RoomList.ascx" TagPrefix="uc1" TagName="RoomList" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="ChatBody" runat="server">

    <%-- 退室の理由を知らせる帯。
         部屋の画面から理由を持ち越してきた時だけ、JavaScriptが中身を入れて表示する
         （初期状態では、CSSのdisplay:noneで隠れている） --%>
    <div id="leaveReasonBar" class="leave-reason-bar">
        <span id="leaveReasonText" class="leave-reason-text"></span>
        <button type="button" id="leaveReasonCloseBtn" class="leave-reason-close-btn">閉じる</button>
    </div>

    <%-- 並べ替えのための、外側の箱。
         スマホ幅では、この中の5つ（プロフィール・参加人数・部屋を作るボタン・部屋の一覧・部屋を作るエリア。
         ラウンジにいる人の顔を足して6つになり、顔を部屋の一覧の中へ移して5つに戻った）を
         CSSのorderで並べ替える（PC幅ではグリッドで並べる）。どちらも共通の親が要るため、ここで包んでいる。
         退室理由の帯は並べ替えの対象ではないため、この外に置いたままにする --%>
    <div class="lounge-layout">

    <div class="lounge-header">
        <div class="current-profile">
            <%-- 【、箱を3つ足した】
                 .current-profile-clip … はみ出した区切り線を切り落とす箱（overflow: hidden）
                 .current-profile-row  … 「アイコン・名前・ログアウトの1組」と「トリップの欄」を並べる、折り返す横並び
                 .current-profile-main … アイコン・名前・ログアウトの1組

                 指摘：横幅を狭くすると並び直すのはよいが、線がおかしな位置に残る。
                 消すか、上と下の間に入れる。
                 トリップの欄が横に並んでいるか、下の段へ回ったかは、**名前の長さでも変わる**
                 （画面の幅だけでは決まらない）ので、メディアクエリでは線を切り替えられない。
                 そこで、欄に縦の線と横の線の**両方**を持たせ、要らないほうが枠の外へはみ出して
                 切り落とされる形にした（仕組みは Content/Site.css の .current-profile-clip）。
                 1組を箱にまとめたのは、1組の中のすき間（8px）と、線の左右のすき間を別にするため --%>
            <div class="current-profile-clip">
            <div class="current-profile-row">
            <div class="current-profile-main">
            <div class="icon-frame icon-frame--current">
                <svg viewBox="0 0 512 512" style="color: <%: currentIconColor %>;">
                    <use href="#icon-<%: currentIconType %>"></use>
                </svg>
            </div>
            <%-- 名前と、その下の行のトリップ。
                 トリップの見た目は部屋の中と同じ（等幅・小さい文字・トリップ自身から決まる色）。
                 部屋でどう見えるかの見本も兼ねている。
                 付いていないときは hidden（外すのたびに、下のJSが出し入れする） --%>
            <div class="current-name-block">
                <span class="current-nickname"><%: currentNickname %></span>
                <span id="currentTrip" class="trip-code current-trip" data-trip="<%: currentTrip %>"<% If currentTrip Is Nothing Then %> hidden="hidden"<% End If %>><span class="visually-hidden">トリップ </span><span id="currentTripText"><%: currentTrip %></span></span>
                <%-- アカウントID。アカウントID を表示している人は、トリップの代わりにこちらが出る（どちらか一方）。
                     押すと、自分の公開のプロフィールが新しいタブで開く。部屋でどう見えるかの見本も兼ねている。
                     決めるのはサーバー（Lounge.aspx.vb → Chat/ChatAccountLink.vb） --%>
                <%-- ログインしている人には、表示していなくても置いておく（hidden）。
                     ラウンジの「アカウントID」のスイッチで付けたとき、下の JS がページを読み込み直さずに出す --%>
                <% If isAccountLoggedIn Then %>
                <a id="currentAccount" class="account-id current-account" href="<%: loggedInAccountUrl %>" target="_blank" rel="noopener"<% If Not isAccountShowing Then %> hidden="hidden"<% End If %>><span class="visually-hidden">アカウント </span><%: loggedInAccountText %><span class="visually-hidden">（新しいタブで開きます）</span></a>
                <% ElseIf needsAccountLogin Then %>
                <%-- 名前を決めたときは「アカウントID を表示する」にしたが、今はログインしていない（7日で切れた・ログアウトした）。
                     アカウントID は出さない。ログインし直して、この画面を開き直すと、また出る。
                     ログインが済んだら、このラウンジへ戻す（基本ルールC。戻り先の決まりは Accounts/AccountReturnUrl.vb）。
                     文は「アカウントIDを紐づけられます」（スイッチの名前「アカウントIDを紐づける」とそろえた） --%>
                <a class="current-account-note" href="<%: AccountLoginUrl %>">ログインすると、アカウントIDを紐づけられます</a>
                <% End If %>
            </div>
            <%-- 押すと、このブラウザの身元と設定が全部消えて「別人」になる。
                 名前・アイコンだけを変える道は、意図して用意していない
                 （Cookieユーザーにとって「名前を変える」は、実質
                 「ログアウトして入り直す」ことなので、言葉を実際に合わせた）。

                 【押す前に確認を出す】、実機を見た開発者の判断で
                 そう決まった。中身は Scripts/site.js の vbChatConfirm。
                 見た目でも隣と分けてある（理由は Content/Site.css の .logout-btn）。

                 【、囲んでいた箱（.profile-actions）を外した】
                 あれは「ボタンを、アイコン・名前とは別の行にする」ための箱だった。
**狭い画面でも横1行に並べる**ことになったので、
                 別の行にする理由そのものが無くなった。
                 箱を外すと、この <a> がアイコン・名前と同じ行に並ぶ（CSS側は1行も足していない）。
                 （並ぶ箱は .current-profile-main。それまでは .current-profile の直接の子だった）

                 【ボタン要素ではなく、リンクにしてある】
                 下のJSは、押されたら確認を出して、押し進められたときだけ
                 /chat/logout へ移動する（つまり preventDefault する）。
                 それでもリンクのままにしてあるのは、**JSが動かなかった日でも
                 チャットを出られる**ようにするため——そのときは確認が出ずに
                 そのまま消えるが、JSが死んでいる画面では入室ボタンも効かないので、
                 「何もできない」より「確認なしでも出られる」ほうを採った。

                 【文言は「チャットを出る」】
                 理由：アカウントのログイン・ログアウト・新規登録を本格的に作るので、
                 チャットのほうを「ログアウト」と呼ぶのをやめる。
                 **これから一般ユーザーの登録が入り、アカウントの「ログアウト」ができる**ので、
                 同じ言葉だと区別がつかなくなる。「チャットを出る」にすると、
                 部屋の「退室」（名前はそのまま部屋を出る）とも、アカウントの「ログアウト」とも言葉が分かれる。
                 **やることは今までどおり**（Cookie・sessionStorage・localStorage を全部消して、別人になる）。
                 【URL は /chat/logout のまま】外から見える言葉ではないため（理由は ChatLogout.vb の頭）。
                 URLは RouteConfig.vb の "chat/logout" --%>
            <a href="/chat/logout" id="logoutBtn" class="logout-btn">チャットを出る</a>
            <%-- 「?」（文は Core/HelpTexts.vb の "lounge.logout"） --%>
            <%= HelpButton("lounge.logout") %>
            </div><%-- /.current-profile-main --%>

            <%-- === トリップの欄 ===

                 この枠だけで「付ける」「別の合言葉に変える」「外す」の3つが全部できる。
                 名前を変える（＝ログアウトして別人になる）のとは違い、付け外ししても別人にはならない
                 （無視もこっそりの窓も切れない）。要望：トリップはラウンジで作れるので、
                 同じくラウンジで外したり変えたりもできるようにする。

                 【トップページには置かない】トップへ行けるのはログアウトした人だけなので、
                 そこにしか欄が無いと「外すのにログアウトが要る」ことになる。

                 【入力欄は、いつも空】合言葉の Cookie は HttpOnly なので、今の合言葉は読めない。
                 今付いているトリップは、名前の下と、この欄の1行目で分かる。

                 【狭い画面】この欄は枠の幅いっぱいの行を取る（CSS）。アイコン・名前・ログアウトの
                 横1行は崩さない。

                 【ソルトが読めないときは、丸ごと出さない】（フェイルクローズ）

                 【付けている間は、付けるための部品を出さない】
                   付けていない … 「付いていません」＋入力欄＋決定＋注意書き2本（#tripEntry）
                   付けている   … 「付いています　[外す]」だけ
                 【欄に10桁は出さない】10桁は名前の下に出ている。欄の1行目にも出していて、
                 同じ10桁が枠の中に2つ並んでいた（が重複していた）
                 合言葉を変えるときは「外す」→ 入力欄が出る → 入れ直す、の2手になる
                 （開発者の判断：そのほうが分かりやすい）。注意書きも消すのは、
                 トリップを付けた人は、もう読んでいるため（開発者の判断）。
                 出し入れは hidden で行う（下のJSの renderTrip）。サーバーは最初の形だけを決める --%>
            <%-- === アカウントID のスイッチ ===

                 トリップと同じように、ここでその場で付け外しできる。**付け外ししても別人にならない**
                 （名前の札だけを書き直す。Chat/ChatLogin.vb の ChangeAccountIdShown）。
                 【ログインしている人にだけ出す】していない人には、1文字も出ない（付けられないものを見せない）。
                 【オンの間は、トリップの欄を隠す】（どちらか一方。の決まり）。
                 オフにすると、トリップの欄が戻る（合言葉が残っていれば、前のトリップもそのまま戻る）。
                 **隠すことは守りではない**——トリップを付ける口（SetTripKey）が、サーバーで断る。
                 【スイッチの横に「@ユーザー名」を出さない】（指摘：それがあるとスイッチで外しても出たままなので、
                 外れたのかが分からない）。付いているかは、名前の下の @ID が出る・消えるで分かる。
                 （トップページのスイッチの横には、今も出している＝押す前に何が出るのかを見せる場所なので）
                 【開いたままの部屋には、入り直してから効く】（トリップと同じ） --%>
            <% If isAccountLoggedIn Then %>
            <div class="trip-form trip-form--account" id="accountIdBox">
                <div class="trip-form-head">
                    <label for="accountIdToggle" class="trip-form-label">アカウントIDを紐づける</label>
                    <%= HelpButton("lounge.accountId") %>
                    <label class="toggle-switch">
                        <input type="checkbox" id="accountIdToggle"<% If isAccountShowing Then %> checked="checked"<% End If %> />
                        <span class="toggle-slider"></span>
                    </label>
                </div>
                <%-- 断られた理由（ログインが切れていた、など）。欄の上に出す決まり（毎回の基本の5）だが、
                     この箱には入力欄が無いので、スイッチの下に置いてある --%>
                <p id="accountIdStatus" class="trip-status trip-status--error" aria-live="polite"></p>
                <%-- 【説明は置かない】前は「オンの間は、トリップを使えません。」を添えていたが、
                     ID で本人と分かるのでトリップは要らないだけ（損のように書かない＝基本ルールC）。
                     オンの間は、トリップの欄ごと出さない（下の #tripForm の hidden） --%>
            </div>
            <% End If %>
            <% If IsTripEnabled Then %>
            <div class="trip-form" id="tripForm"<% If isAccountShowing Then %> hidden="hidden"<% End If %>>
                <div class="trip-form-head">
                    <label for="tripKeyInput" class="trip-form-label">トリップ</label>
                    <%-- === 「?」の説明の小窓（このサイトの1個目） ===

                         使い方・増やし方・「もっと詳しく」のリンクを足す場所は、
                         Scripts/site.js の「4. 共通の、『?』の説明の小窓」に1か所だけ書いてある。
                         **2個目からは、この4行を置くだけでよい**（JSもCSSも触らない）。
                         `data-help-target` と `id` と `aria-controls` を、同じ名前にすること。

                         【2026年10月6日から、文は Core/HelpTexts.vb の表（"lounge.trip"）】ここに書いてある説明は、そのころの記録。
                         【文は開発者が決めたもの】勝手に言い換えないこと。
                         欄の下の注意書き2本（#tripNotes）を消し、「大事なパスワードは使わないでください。」の
                         1行をこの中へ入れた。「もっと詳しく」のリンクも消した（＝説明は足りている、という判断） --%>
                    <%= HelpButton("lounge.trip") %>
                    <%-- 付いている／いないが変わったら、読み上げソフトにも伝える
                         （「付けました」のお知らせを消したので、ここが知らせの役を引き継ぐ） --%>
                    <span id="tripState" class="trip-state" aria-live="polite"><% If currentTrip Is Nothing Then %>付いていません<% Else %>付いています<% End If %></span>
                    <button type="button" id="tripClearBtn" class="trip-btn trip-btn--clear"<% If currentTrip Is Nothing Then %> hidden="hidden"<% End If %>>外す</button>
                </div>
                <%-- 付けるための部品（付けていない間だけ出す） --%>
                <div id="tripEntry" class="trip-entry"<% If currentTrip IsNot Nothing Then %> hidden="hidden"<% End If %>>
                    <%-- 断られた理由（空・長すぎるなど）だけを出す。付けた・外したのお知らせは出さない。

                         【入力欄の「上」に置いてある】（下から移した）
                         スマホでは、断られたあとカーソルが合言葉の欄へ移ってキーボードが出る。
                         下に置くと、**その理由がキーボードに隠れて読めない**
                         （部屋の発言の理由＝.chat-input-info も、こっそりの窓の理由も、
                         同じ理由で入力欄の上に置いてある。キーボードは必ず画面の下から出る） --%>
                    <p id="tripStatus" class="trip-status trip-status--error" aria-live="polite"></p>
                    <div class="trip-form-row">
                        <%-- autocomplete="off" … ブラウザに合言葉の入力履歴を覚えさせない。
                             type="password" にしないのは、ブラウザが「パスワードを保存しますか」と
                             聞いてくるため（普段のパスワードと同じ扱いをさせたくない） --%>
                        <input type="text" id="tripKeyInput" class="trip-key-input"
                               maxlength="<%= TripKeyMaxLength %>" placeholder="好きな合言葉"
                               autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false" />
                        <button type="button" id="tripSetBtn" class="trip-btn trip-btn--set">決定</button>
                    </div>
                </div>
            </div>
            <% End If %>
            </div><%-- /.current-profile-row --%>
            </div><%-- /.current-profile-clip --%>
        </div>
        <%-- 部屋を作るエリアへ飛ぶボタン。
             スマホ幅では、部屋を作るエリアが画面のずっと下（部屋の様子の下）へ移るため、
             その入り口としてここに置く。PC幅ではCSSで隠している（すぐ右隣にあるため）。

             【、プロフィールの枠の外へ出した】
             指摘：前の形では、間違って違う方を押すおそれがある。PC は別の枠なので問題ない。
             ログアウトの文言が短くなった副産物で、狭い画面でボタン2つが横に並ぶようになり、
             **その横並びが、ログアウトの押し間違いを招く形**になっていた。
             CSSだけでは箱の外へ出せない（親が変わらない）ので、markupごと移してある --%>
        <a href="#createRoomArea" class="goto-create-room-btn">部屋を作る</a>
    </div>

    <uc1:RoomList runat="server" ID="RoomList" />

    <%-- 【部屋を作るエリアは、部屋の一覧より「あと」に置く】（読み上げの点検の回）

         見た目の並びは CSS の order が決めており（Content/Site.css。PC もスマホも 5＝いちばん下）、
         その狙いは「縦に並んだとき、部屋を作るエリアが大きすぎて『部屋を作るしかない』ように読める」のを
         避けることだった（スマホ幅、PC へも広げた）。
         ところが markup では .lounge-header の中（プロフィールのすぐあと）に置いたままで、
         **読み上げソフトとキーボードは markup の順に読む**ので、
         その人たちだけが「部屋の一覧より先に、作る欄が丸ごと来る」ままだった
         （Tab も、部屋名・定員・画像・ノック・パスワード・作成ボタンを全部通ってから一覧へ着く）。
         order と markup の並びを合わせて、見た目と読み上げを同じ順にしてある。

         【order の指定は変えていない】見た目は1pxも動かない（.lounge-header は
         PC でもスマホでも display: contents なので、この箱から出しても並びに関係が無い）。
         残る食い違いは「部屋を作るボタン（order 3）」と「人数の行（order 2）」の1組だけで、
         あちらは .lounge-header と RoomList にまたがっていて markup では入れ替えられない
         （短いリンク1本と1行の前後なので、そのままにした） --%>
    <%-- === 部屋を作るエリア（中身が動くようになった） ===

         それまでは、押しても何も起きないボタンが置いてあった
         （「押しても何も無い場所を、先に作ってよい」＝まだ一般公開していないため）。

         【作れたのは、部屋名・定員・画像の可否の3つ】
         パスワードを、15-2b（同じ日）でノックを足した。
         それまでは、中身の無いものは入力欄を置かずに「準備中」と出していた
         （中身が無いのに入力できると、付けたつもりの鍵やノックが効かないことになる）。

         【定員の選択肢は、サーバーが組み立てる】
         一覧を持っているのは ChatRoomRules（＝サーバー側の検査と同じ一覧）。
         ここへ <option> をベタ書きすると、検査と食い違って
         「選べるのに、選ぶと断られる」ことになる。
         部屋名の maxlength も同じ理由で、数字を書かずにサーバーから受け取る --%>
    <div class="create-room-area" id="createRoomArea">
        <h3>部屋を作る<%= HelpButton("lounge.createRoom") %></h3>
        <%-- 断られた理由（部屋名が空・長すぎる、すでに1部屋持っている、など）。

             【入力欄より「上」に置いてある】（下から移した）
             スマホでは、断られたあとカーソルが部屋名の欄へ移ってキーボードが出る。
             ボタンのそば（下）に置くと、**赤字がキーボードに隠れて読めない**
             （開発者が実機で見つけた。キーボードは必ず画面の下から出るので、上なら隠れない）。
             **カーソルを欄へ移すのは、そのまま残す**（どこを直せばよいか分かるのと、読み上げソフトのため）。

             出るまでは高さを持たない（:empty）。aria-live で、読み上げソフトにも変わった瞬間に伝える --%>
        <p id="createRoomError" class="create-room-error" aria-live="polite"></p>
        <%-- === 2列に並べる ===

             PC幅では 左＝部屋名・定員／右＝画像・ノック・パスワード の2列にして、エリアの高さを半分にする
             （15-1 までは右が空いていた。開発者と決めた）。
             狭い画面（700px以下）では、今までどおり縦1列（CSS の grid は PC幅の中にだけ書いてある）。
             **並び順は、読み上げの順番でもある**（左の列 → 右の列）。見た目のために入れ替えないこと --%>
        <div class="create-room-columns">
        <div class="create-room-column">
        <div class="form-row">
            <label for="roomNameInput">部屋名</label>
            <input type="text" id="roomNameInput" class="room-name-input"
                   placeholder="部屋名（<%: RoomNameMaxLength %>文字以内）"
                   maxlength="<%= RoomNameMaxLength %>" />
        </div>
        <div class="form-row">
            <label for="maxUsersSelect">定員</label>
            <select id="maxUsersSelect" class="max-users-select"><%= MaxUsersOptionsHtml %></select>
        </div>
        </div><%-- /.create-room-column（左） --%>
        <div class="create-room-column">
        <%-- 画像の可否（ChatRooms.IsImageUploadEnabled）。既定はオン。
             列そのものの既定値は0のままにしてある——画面から作る部屋にはアプリが必ず値を入れるので、
             列の既定は「指定されなかったときの受け皿」である --%>
        <%-- 説明は、ノック（下）とまったく同じ形にしてある。
             スイッチだけでは、何が起きるのか分からないため。
             aria-describedby で、読み上げソフトにもスイッチの説明として紐づける --%>
        <div class="form-row">
            <label for="imageUploadToggle">画像</label>
            <label class="toggle-switch">
                <input type="checkbox" id="imageUploadToggle" checked="checked"
                       aria-describedby="imageUploadNote" />
                <span class="toggle-slider"></span>
            </label>
            <span id="imageUploadNote" class="form-row-note">部屋の中で、画像を送れるようにします</span>
        </div>
        <%-- === ノック ===

             オンにすると、満室のときにラウンジの入室ボタンが「ノック」になり、
             あなた（部屋の管理人）が入れるかどうかを決められる（ChatRooms.IsKnockEnabled）。
             既定はオフ。
             説明は aria-describedby で読ませる（スイッチだけでは、何が起きるか分からないため）。
             見た目は、画像のスイッチと同じトグルスイッチ --%>
        <div class="form-row">
            <label for="knockToggle">ノック</label>
            <label class="toggle-switch">
                <input type="checkbox" id="knockToggle" aria-describedby="knockNote" />
                <span class="toggle-slider"></span>
            </label>
            <span id="knockNote" class="form-row-note">満員のときに、入れてほしい人がノックできます</span>
        </div>
        <%-- === パスワード ===

             空欄なら、誰でも入れる部屋。入れたら、サーバーがハッシュにして保存する（Chat/RoomPassword.vb）。
             **あとから誰にも見られない**（作った本人にも）ので、忘れたら変えるしかない。

             【type="text" にしてある】トリップの合言葉と同じ理由——type="password" にすると、
             ブラウザが「パスワードを保存しますか」と聞いてきて、普段のパスワードと同じ扱いになる。
             この言葉は人に伝えて使うものなので、打った文字が見えるほうが間違えにくい。
             autocomplete="off" … ブラウザに入力の履歴を覚えさせない --%>
        <div class="form-row">
            <label for="roomPasswordInput">パスワード</label>
            <input type="text" id="roomPasswordInput" class="room-password-input"
                   placeholder="空欄なら誰でも可"
                   maxlength="<%= RoomPasswordMaxLength %>"
                   autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false" />
            <%-- 【説明は「?」の中】（ラウンジは何度も使う画面なので低くする＝基本ルールB）
                 前は欄の下（作成ボタンの左）に3行あった。部品はトリップの「?」と同じ（Scripts/site.js の「4」） --%>
            <%= HelpButton("lounge.roomPassword") %>
        </div>
        </div><%-- /.create-room-column（右） --%>
        </div><%-- /.create-room-columns --%>
        <%-- 作成ボタンの行（右寄せ）。パスワードの注意書き（.create-room-note）は、パスワードの欄の「?」へ移した --%>
        <div class="create-room-footer">
            <%-- 【「?」は見出し「部屋を作る」の右へ移した】（部屋の一覧の「?」も見出しの横なのでそろう） --%>
            <button type="button" id="createRoomBtn" class="create-room-btn">部屋を作成する</button>
        </div>
    </div>

    </div><%-- .lounge-layout --%>

</asp:Content>





<asp:Content ID="ScriptsForThisPage" ContentPlaceHolderID="ChatScripts" runat="server">


    <%-- jQuery：これが無いと $ が使えない --%>
    <script src="/Scripts/lib/jquery-3.7.1.min.js"></script>

    <%-- SignalR。部屋の様子をその場で書き換えるため、ラウンジも繋ぐ。
         順番は部屋（ChatRoom.aspx）と同じ：jQuery → jquery.signalR → /signalr/hubs。
         /signalr/hubs は $.connection.chatHub をここで生やす。
         動かすのは、いちばん下の lounge-live.js --%>
    <script src="/Scripts/lib/jquery.signalR-2.4.3.min.js"></script>
    <script src="/signalr/hubs"></script>

    <%-- === ログアウトの音===

         ログアウトを押して確認で「ログアウトする」を選んだら、鳴らしてから移動する
         （部屋の退室ボタンと同じ形。中身は Scripts/sounds.js）。

         【ファイルは lounge-leave.mp3】部屋のドアの音とは別の名前にしてある。
         開発者が素材を用意したら、**このファイルを FTP で差し替えるだけ**で変わる。
         置くまでは部屋のドアの音（door-close.mp3）の写しを置いておく。置き忘れても、合成音で鳴る。

         URL は隠しフィールドで渡す（<script> の中に <%: %> を書かない。宿題7） --%>
    <input type="hidden" id="loungeLeaveSoundUrl" value="<%: Asset("~/Content/sounds/lounge-leave.mp3") %>" />
    <script src="<%: Asset("~/Scripts/sounds.js") %>"></script>


    <script>
        <%--
            サーバーから渡された設定（SiteSettings.ClientSettingsJson）

            部屋の画面（ChatRoom.aspx）・トップページ（Default.aspx）と、同じ中身が入る。
            このページ自身は今のところ使っていないが、共通の site.js が
            警告メッセージの表示時間に使うため、3ページとも口を開けてある
        --%>
        var vbChatSettings = <%= ClientSettingsJson %>;

        <%-- サーバー側で断られた理由（無ければ null）。

             部屋の画面を経由せずにサーバーが弾いた場合は、部屋のJSが sessionStorage へ
             預ける経路が使えないため、Session で運ばれてここへ出てくる。

             【JSONで受け取る】文字列リテラルの中に "<%: 値 %>" を書いてはいけない。
             あれはHTML用のエスケープなので、<script> の中では &quot; のような文字列が
             そのまま変数に入る（宿題7で直したのと同じ形）。

             この説明をJavaScriptの // コメントに書かないこと。
             「//」はサーバー側のコードブロックを守らないため、コメントのつもりで書いた
             ものまで解釈されてページが止まる（Default.aspx で実際に踏んだ） --%>
        var serverLeaveReason = <%= ServerLeaveReasonJson %>;

        $(function () {

            <%--
                === 退室の理由の表示 ===

                部屋の画面（ChatRoom.aspx）から持ち越された退室の理由があれば、帯として表示する

                【なぜ、部屋の画面ではなくここで表示するのか】
                Chromeは、背面のタブから出されるダイアログ（alert・confirm）を抑制する。
                2窓の置き換えでは、新しいタブを開いた時点で古いタブが背面に回っているため、
                部屋の画面でalertを出しても表示されなかった。無言タイムアウトでも同じで、
                むしろ放置している人ほどタブは背面にあり、一番理由を知りたい人に一番届かない。
                帯であれば、あとでタブを開いた時に読むことができる

                alertは使わない（今回の問題の原因そのもののため）
            --%>
            var LEAVE_REASON_STORAGE_KEY = 'vbchat_leave_reason';

            var leaveReason = null;

            try {
                leaveReason = sessionStorage.getItem(LEAVE_REASON_STORAGE_KEY);

                if (leaveReason) {
                    <%--
                        表示したら必ず削除する。
                        消さないと、ラウンジを開くたびに毎回出てしまうため
                    --%>
                    sessionStorage.removeItem(LEAVE_REASON_STORAGE_KEY);
                }
            } catch (e) {
                <%--
                    sessionStorageが使えない場合は、理由が表示されないだけで、他に影響はない
                    （下のサーバー側の理由は、この失敗とは無関係に使える）
                --%>
            }

            <%--
                === 運び方が2つある ===

                ・sessionStorage … 部屋の画面のJSが預けたもの（ハブに断られた・kickedされた）
                ・serverLeaveReason … サーバーが Session で運んできたもの
                  （ChatRoom.aspx.vb の Page_Load が、終了した部屋を組み立てる前に弾いた）

                サーバー側は、部屋の画面のJSが動く前に弾いているため、2つが同時に入ることは
                基本的に無い。それでも順番を決めておくのは、
                「同じタブで起きたこと」（sessionStorage）のほうが、
                ブラウザ単位で共有される Session より確かなため
            --%>
            if (!leaveReason) {
                leaveReason = serverLeaveReason;
            }

            if (leaveReason) {
                <%-- textを使う（htmlではない）。サーバーからの文言をそのまま流し込むため --%>
                $('#leaveReasonText').text(leaveReason);
                $('#leaveReasonBar').addClass('is-visible');
            }

            <%--
                帯は自動では消さない。背面のタブで表示された場合、
                利用者が画面を見た時には消えてしまっているため
            --%>
            $('#leaveReasonCloseBtn').on('click', function () {
                $('#leaveReasonBar').removeClass('is-visible');
            });


            <%--
                === ログアウト ===

                サーバー（Chat/Logout.aspx）が ハブの席・Cookie・Session を捨てるが、
                sessionStorage と localStorage には**サーバーからは触れない**。
                そのぶんだけ、ここで消す。

                消えるもの … 無視リスト・こっそりの窓・退室理由（sessionStorage）／
                              よく使う絵文字・最後に選んだタブ（localStorage）。
                3つとも全消しにするのはある

                【、確認を出すようにした】
                実機を見た開発者の判断で覆した（それまでは「確認は出さない」だった）。
                確認を出す以上、リンクの既定の移動は止めないといけない。
                止めたことで失うものは、markup側のコメントに書いてある

                【2つの clear を、別々に囲むこと】
                ブラウザの設定や容量超過で、storageへの操作は例外を投げる。
                まとめて囲むと、sessionStorage が投げただけで
                localStorage が手つかずで残る

                【文言は「何が消えるか」だけを言う】
                「ログアウトすると、別人になります。」の
                1文を削った。**取り返しがつかないのは、消えることのほうである。**
                「別人になる」は仕組みの説明であって、押す前に要る情報ではない

                【2文を、別々の段落にしてある】
                lines は1つにつき1つの <p> になる（Scripts/site.js の vbChatConfirm）。
                **1つにまとめて長い1文にしないこと。**
                開発者の判断：ひと息入るほうが読みやすい。
                素人・年配の人が来る前提のサイトなので、そちらを採っている
            --%>
            <%-- ログアウトの音。通知音のオン/オフと音量は、部屋の設定と同じ Cookie から読む
                 （ラウンジには設定の画面が無い。部屋で決めたものがそのまま効く）。
                 sounds.js が読めなかったときは null で、音が鳴らないだけ --%>
            var loungeSounds = null;

            if (window.VbChatSounds) {
                loungeSounds = VbChatSounds.create({
                    leaveUrl: $('#loungeLeaveSoundUrl').val()
                });
                loungeSounds.setEnabled(VbChatSounds.readSavedEnabled(vbChatSettings.notificationSoundDefault));
                loungeSounds.setVolumeLevel(VbChatSounds.readSavedVolumeLevel(loungeSounds, vbChatSettings.notificationSoundVolumeDefault));
                VbChatSounds.unlockOnFirstInput(loungeSounds);
            }

            $('#logoutBtn').on('click', function (e) {

                e.preventDefault();

                vbChatConfirm({
                    title: 'チャットを出ますか？',
                    lines: [
                        '名前とアイコン、通知音などの設定は、すべて消えます。',
                        'こっそり話した内容も、見られなくなります。'
                    ],
                    okText: '出る',
                    cancelText: 'やめる',
                    onOk: function () {

                        var goLogout = function () {

                            try {
                                sessionStorage.clear();
                            } catch (storageError) {
                            }

                            try {
                                localStorage.clear();
                            } catch (storageError) {
                            }

                            window.location.href = '/chat/logout';
                        };

                        <%-- 鳴らしてから移動する（鳴らないときは、すぐ移動する。待つのは最大0.7秒）。
                             「ログアウトする」を押したこと自体が操作なので、ここなら音を出せる --%>
                        if (loungeSounds) {
                            loungeSounds.leaveThenRun(goLogout);
                        } else {
                            goLogout();
                        }
                    }
                });

            });


            <%--
                === トリップ ===

                付ける・変える … SetTripKey（合言葉をサーバーへ送り、Cookie を書いてもらう）
                外す           … ClearTripKey（Cookie を消してもらう）

                【JS では Cookie を触らない】合言葉の Cookie は HttpOnly なので、読むことも書くことも
                できない。だからサーバーの WebMethod を呼ぶ。

                【確認のダイアログは出さない】ログアウトと違って取り返しがつく
                （同じ合言葉を入れ直せば、同じトリップに戻る）。

                【部屋の中の表示は、次に部屋へ入ったときから変わる】
                部屋の接続は、繋いだ時点の Cookie でトリップを決めている。
                別のタブで部屋を開いたままなら、そちらは入り直すまで前のまま。
                （これを「付けました」「外しました」の1行で伝えていたが、
                  お知らせごと消した＝付けている間は付けるための部品を出さない）

                【付けている／いないで、出す部品を丸ごと入れ替える】
                  付けていない … 入力欄＋決定＋注意書き（#tripEntry）
                  付けている   … 「付いています　[外す]」だけ（10桁は名前の下）
                入れ替えは renderTrip の1か所だけで行う

                【トリップの欄が無い（機能が止まっている）ときは、何もしない】
            --%>
            var $tripForm = $('#tripForm');

            <%-- トリップの見た目を書き直す関数（下の if の中で入る）。アカウントID を外したときにも呼ぶ --%>
            var loungeRenderTrip = null;

            if ($tripForm.length) {

                var tripBusy = false;

                <%-- 今のトリップを、名前の下・欄の1行目・部品の出し入れに反映する（trip が null なら「無し」） --%>
                var renderTrip = function (trip) {

                    var hasTrip = !!trip;
                    var currentTripEl = document.getElementById('currentTrip');

                    <%-- 10桁を出すのは名前の下だけ（欄の1行目の10桁を消した） --%>
                    currentTripEl.setAttribute('data-trip', trip || '');
                    document.getElementById('currentTripText').textContent = trip || '';
                    currentTripEl.hidden = !hasTrip;

                    $('#tripState').text(hasTrip ? '付いています' : '付いていません');
                    document.getElementById('tripClearBtn').hidden = !hasTrip;
                    document.getElementById('tripEntry').hidden = hasTrip;

                    <%-- 前に断られた理由が残っていたら消す（付けたあと・外したあとには用が無い） --%>
                    $('#tripStatus').text('');
                };

                loungeRenderTrip = renderTrip;

                <%--
                    断られた理由を出す。
                    入力欄が出ている（付けていない）ときは、欄の下の1行へ。
                    付けているとき（＝「外す」が断られた）は、その1行が隠れているので、
                    ページの上に出る警告（site.js の showWarning）へ
                --%>
                var showTripError = function (text) {
                    if (document.getElementById('tripEntry').hidden) {
                        showWarning(text);
                    } else {
                        $('#tripStatus').text(text);
                    }
                };

                <%--
                    WebMethod を呼ぶ共通の形。
                    戻り値は JSON文字列 {"ok":…,"trip":…,"message":…}（response.d に入る）
                --%>
                var callTrip = function (method, data, onOk) {

                    if (tripBusy) {
                        return;
                    }

                    tripBusy = true;
                    $('#tripSetBtn, #tripClearBtn').prop('disabled', true);

                    <%--
                        【ボタンを戻すのは、結果を処理する「前」】
                        .always は .done より後に走るので、そこで戻すと、
                        onOk の中で「決定」へ焦点を移そうとしたときにボタンがまだ押せない状態で、
                        焦点が移らない（開発機で実際にそうなった）
                    --%>
                    var finish = function () {
                        tripBusy = false;
                        $('#tripSetBtn, #tripClearBtn').prop('disabled', false);
                    };

                    $.ajax({
                        type: 'POST',
                        url: '/Chat/Lounge.aspx/' + method,
                        data: JSON.stringify(data),
                        contentType: 'application/json; charset=utf-8',
                        dataType: 'json'
                    }).done(function (response) {

                        finish();

                        var result = null;

                        try {
                            result = JSON.parse(response.d);
                        } catch (parseError) {
                        }

                        if (result && result.ok) {
                            onOk(result);
                        } else {
                            showTripError((result && result.message) || '受け付けられませんでした。');
                        }

                    }).fail(function () {
                        finish();
                        showTripError('通信エラーが発生しました。もう一度お試しください。');
                    });
                };

                var submitTripKey = function () {

                    var key = $('#tripKeyInput').val();

                    <%-- 空のときは、サーバーへ行かずにここで知らせる（サーバーも同じ判定をする） --%>
                    if (!key || !key.trim()) {
                        showTripError('合言葉を入れてください。');
                        $('#tripKeyInput').trigger('focus');
                        return;
                    }

                    callTrip('SetTripKey', { key: key }, function (result) {

                        <%-- 入力欄は空に戻す（合言葉を画面に残さない。このあと欄ごと隠れる） --%>
                        $('#tripKeyInput').val('');

                        renderTrip(result.trip);

                        <%-- 押した「決定」が消えるので、焦点を「外す」へ移す（居場所を見失わせない） --%>
                        $('#tripClearBtn').trigger('focus');
                    });
                };

                $('#tripSetBtn').on('click', submitTripKey);

                <%--
                    Enter でも決定できる。
                    【日本語入力の変換を確定する Enter は除く】（落とし穴）
                --%>
                $('#tripKeyInput').on('keydown', function (e) {

                    if (e.originalEvent && e.originalEvent.isComposing) {
                        return;
                    }
                    if (e.which === 229) {
                        return;
                    }

                    if (e.key === 'Enter') {
                        e.preventDefault();
                        submitTripKey();
                    }
                });

                $('#tripClearBtn').on('click', function () {

                    callTrip('ClearTripKey', {}, function () {

                        renderTrip(null);

                        <%-- 押したボタンが消えるので、焦点を「決定」へ移す（居場所を見失わせない）。
                             入力欄へ移さないのは、スマホでソフトキーボードが勝手に開くため --%>
                        $('#tripSetBtn').trigger('focus');
                    });
                });
            }


            <%--
                === アカウントID の付け外し ===

                スイッチを押すと、SetAccountIdShown を呼ぶ（名前の札をサーバーが書き直す。JS は Cookie を触らない）。
                  付けた … 名前の下に「@ユーザー名」を出し、トリップの欄と名前の下のトリップを隠す（どちらか一方）
                  外した … 「@ユーザー名」を隠し、トリップの欄を戻す（合言葉が残っていれば、そのトリップも戻る）
                【確認のダイアログは出さない】取り返しがつく（もう一度押せば戻る）。トリップの「外す」と同じ。
                【断られたら、スイッチを元に戻す】見た目と実際が食い違ったままにしない。
                【開いている部屋には、入り直してから効く】（トリップと同じ）
            --%>
            var accountToggle = document.getElementById('accountIdToggle');

            if (accountToggle) {

                var accountBusy = false;

                var renderAccount = function (result) {

                    var currentAccountEl = document.getElementById('currentAccount');
                    var tripFormEl = document.getElementById('tripForm');

                    if (currentAccountEl) {
                        currentAccountEl.hidden = !result.showing;
                    }

                    if (tripFormEl) {
                        tripFormEl.hidden = !!result.showing;
                    }

                    if (result.showing) {
                        <%-- 名前の下のトリップも隠す（欄が無いときにも、名前の下には出ていることがある） --%>
                        var currentTripEl = document.getElementById('currentTrip');
                        if (currentTripEl) {
                            currentTripEl.hidden = true;
                        }
                    } else if (loungeRenderTrip) {
                        loungeRenderTrip(result.trip || null);
                    }

                    accountToggle.checked = !!result.showing;
                    $('#accountIdStatus').text('');
                };

                $(accountToggle).on('change', function () {

                    if (accountBusy) {
                        return;
                    }

                    var wanted = accountToggle.checked;

                    accountBusy = true;
                    accountToggle.disabled = true;

                    var finish = function () {
                        accountBusy = false;
                        accountToggle.disabled = false;
                    };

                    var fail = function (text) {
                        finish();
                        accountToggle.checked = !wanted;
                        $('#accountIdStatus').text(text);
                        accountToggle.focus();
                    };

                    $.ajax({
                        type: 'POST',
                        url: '/Chat/Lounge.aspx/SetAccountIdShown',
                        data: JSON.stringify({ show: wanted }),
                        contentType: 'application/json; charset=utf-8',
                        dataType: 'json'
                    }).done(function (response) {

                        var result = null;

                        try {
                            result = JSON.parse(response.d);
                        } catch (parseError) {
                        }

                        if (result && result.ok) {
                            finish();
                            renderAccount(result);
                            accountToggle.focus();
                        } else {
                            fail((result && result.message) || '受け付けられませんでした。');
                        }

                    }).fail(function () {
                        fail('通信エラーが発生しました。もう一度お試しください。');
                    });
                });
            }


            <%--
                === 部屋を作る ===

                サーバーの WebMethod（Lounge.aspx.vb の CreateRoom）が、
                DBへ入れて、ラウンジに居る全員へ「部屋ができた」を配る。
                作った本人は、そのまま部屋へ移る（サーバーが Session に部屋IDを置いている）。

                【押せなくするのは、返事が来るまで】
                二重に押されると、DB側で断られる（1つのブラウザが持てる部屋は1つ）ので
                2部屋できることはないが、断りの文言が出て利用者が驚く。

                【成功したら、ボタンは戻さない】このあと画面ごと移るため。
                戻すと、移動までの一瞬に押せてしまう

                【ここでの検査は親切であって、防御ではない】
                空かどうかは打った時点で分かるので、その場で知らせる。
                本当の検査はサーバー側（ChatRoomRules）で、
                WebMethod は開発者ツールから直接叩ける
            --%>
            var $createRoomBtn = $('#createRoomBtn');
            var createRoomBusy = false;

            var showCreateRoomError = function (text) {
                $('#createRoomError').text(text);
            };

            var submitCreateRoom = function () {

                if (createRoomBusy) {
                    return;
                }

                var roomName = $('#roomNameInput').val() || '';

                if (!roomName.trim()) {
                    showCreateRoomError('部屋名を入れてください。');
                    $('#roomNameInput').trigger('focus');
                    return;
                }

                createRoomBusy = true;
                $createRoomBtn.prop('disabled', true);
                showCreateRoomError('');

                $.ajax({
                    type: 'POST',
                    url: '/Chat/Lounge.aspx/CreateRoom',
                    data: JSON.stringify({
                        roomName: roomName,
                        maxUsers: parseInt($('#maxUsersSelect').val(), 10),
                        imageUploadEnabled: $('#imageUploadToggle').is(':checked'),
                        knockEnabled: $('#knockToggle').is(':checked'),
                        password: $('#roomPasswordInput').val() || ''
                    }),
                    contentType: 'application/json; charset=utf-8',
                    dataType: 'json'
                }).done(function (response) {

                    var result = null;

                    try {
                        result = JSON.parse(response.d);
                    } catch (parseError) {
                    }

                    if (result && result.ok) {
                        <%-- 作った本人は、そのまま自分の部屋へ入る --%>
                        window.location.href = '/chat/room';
                        return;
                    }

                    createRoomBusy = false;
                    $createRoomBtn.prop('disabled', false);
                    showCreateRoomError((result && result.message) || '部屋を作れませんでした。');

                }).fail(function () {
                    createRoomBusy = false;
                    $createRoomBtn.prop('disabled', false);
                    showCreateRoomError('通信エラーが発生しました。もう一度お試しください。');
                });
            };

            $createRoomBtn.on('click', submitCreateRoom);

            <%-- 部屋名・パスワードの欄で Enter を押しても作れる。
                 【日本語入力の変換を確定する Enter は除く】（落とし穴） --%>
            $('#roomNameInput, #roomPasswordInput').on('keydown', function (e) {

                if (e.originalEvent && e.originalEvent.isComposing) {
                    return;
                }
                if (e.which === 229) {
                    return;
                }

                if (e.key === 'Enter') {
                    e.preventDefault();
                    submitCreateRoom();
                }
            });


            <%-- 「入室」ボタンが押された時の処理は、Scripts/lounge-enter.js へ移した（
                 パスワードの小窓と一緒に置くため。このページに書くと、開くたびに毎回配信される） --%>

        });
    </script>

    <%-- 部屋の様子（人数）を、SignalR で受け取ってその場で書き換える。

         【上の <script> より後に置くこと】vbChatSettings（自動再接続の待ち時間）を読む。

         【必ず Asset（common.AssetUrl）を通すこと】?v= が無いと、直しても30日間ブラウザに届かない。

         【読み込めなかったときは、黙って止まる】
         部屋の chat-room.js と違って、帯は出さない。数字がサーバーの入れた値のまま動かなくなるだけで、
         入室・ログアウト・トリップ（上のインラインのJS）はそのまま使えるため --%>
    <script src="<%: Asset("~/Scripts/lounge-live.js") %>"></script>

    <%-- 入室ボタンと、パスワードの小窓。

         【上のインラインの <script> より後に置くこと】showWarning（site.js）と、
         パスワードの文字数の上限（下の隠しフィールド）を使う。
         lounge-live.js との順番は問わない（入室ボタンは、document に委ねて拾っている。
         ノックの返事は lounge-live.js が受け取り、ここの window.VbChatLoungeEnter を呼ぶ）。

         【必ず Asset（common.AssetUrl）を通すこと】

         【読み込めなかったときは、入室ボタンが効かなくなる】
         15-2a まではこの処理がページの中にあった。外へ出したので、このファイルが届かないと
         「入室を押しても何も起きない」になる。**発行のあとは発行後の確かめの道具で確かめる** --%>
    <input type="hidden" id="roomPasswordMaxLength" value="<%= RoomPasswordMaxLength %>" />
    <script src="<%: Asset("~/Scripts/lounge-enter.js") %>"></script>

</asp:Content>