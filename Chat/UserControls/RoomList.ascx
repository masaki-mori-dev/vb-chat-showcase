<%@ Control Language="VB" AutoEventWireup="false" CodeBehind="RoomList.ascx.vb" Inherits="vb_chat.RoomList" %>

<%-- === 部屋の様子（12-1：12-2：同日） ===

     数字・顔・満室の印は、最初はサーバーが入れ（RoomList.ascx.vb）、そのあとラウンジの画面が
     SignalR で受け取った値でその場で書き換える（Scripts/lounge-live.js）。
     書き換える先は id・class・data-* で探しているので、名前を変えるときは lounge-live.js も直すこと。

     【読み上げソフトには、変わるたびに読ませない】（数字に aria-live を付けない）
     人の出入りのたびに「部屋4人」と割り込んで読まれると、ほかの操作の邪魔になる
     （部屋の文字数の表示に aria-live を付けないと決めたのと同じ）。
     数字はその場で書き換えているので、見に行けば今の数字が読める。
     例外は「更新が止まった」ことだけ（#loungeStaleAnnounce。1回だけ読む）

     【置き場所】
       PC   … プロフィールの枠の下（枠の外）。部屋を作るエリアの左の余白に入れる
       スマホ … 「部屋を作る」ボタンの左（1行を2つで使う）
     並べ方は Content/Site.css の「ラウンジの並べ方」。この markup の位置は変えていない
     （このすぐ下にラウンジにいる人の顔の一覧を足し、下の部屋の一覧の中の「ラウンジ」へ移した） --%>
<div class="chat-status">
    <p class="lounge-counts">現在、ラウンジ<span id="loungeCount"><%: loungeTotal %></span>人、部屋<span id="roomsCount"><%: roomsTotal %></span>人</p>

    <%-- 更新が止まったときの印。自動の再接続をあきらめたら、lounge-live.js が出す。
         数字が古いかもしれないことを伝え、自分で繋ぎ直せるようにする --%>
    <p id="loungeStale" class="lounge-stale" hidden="hidden">
        人数の更新が止まっています。
        <button type="button" id="loungeRetryBtn" class="lounge-retry-btn">つなぎ直す</button>
    </p>
    <%-- 読み上げソフト用。止まった瞬間に1回だけ読む（中身が変わったときだけ読まれる）。
         目に見える印とは別にしてある（あちらは hidden で出し入れするので、aria-live が効かない） --%>
    <span id="loungeStaleAnnounce" class="visually-hidden" aria-live="polite"></span>
</div>

<section class="room-list">
    <%-- 【「部屋の様子」の見出し（h2）は、外した】
         スマホだけ見た目から消して、読み上げには残していた。
         PC でも消すことになり、あわせて「ラウンジ」が「既定のルーム」
         「みんなが作ったルーム」と同じ段の見出しになった。上に「部屋の様子」を残すと、
         ラウンジが「部屋の様子」の一部として読まれ、中身と合わない。
         このページのほかの見出し（部屋を作る・更新履歴）も同じ h3 なので、5つが横に並ぶ形に揃えた --%>

    <%-- === ラウンジ（顔を足し、1セクションにした）===

         ［── ラウンジ ──────］ ← 見出しは部屋の一覧と同じ .room-list h3 をそのまま使う（新しく書かない）
         ［顔 顔 顔 顔 …        ］ ← 部屋の在室者と同じ形（1人ぶんは RoomList.ascx.vb の MemberItemHtml を共用）

         順番は ラウンジ → 既定のルーム → みんなが作ったルーム。
         自分自身も並ぶ（開発者の判断：並んでよい）。
         【最初はサーバーが入れ、そのあとは lounge-live.js が書き換える】（繋がるまで顔が出ない形にしない）。
         誰も居なければ一覧だけ hidden（見出しは残す。「みんなが作ったルーム」が空のときと同じ）。

         【見出しと一覧は、ほかの h3 と同じく .room-list の直接の子に置く】（箱で包まない）。
         スマホの「最初の見出しだけ上の余白を 0 に」（.room-list h3:first-of-type）は、親ごとに数えるので、
         箱で包むと「既定のルーム」まで最初の見出し扱いになって詰まる --%>
    <h3 id="loungeMembersHeading">ラウンジ</h3>
    <%= LoungeMembersHtml() %>

    <%-- 1部屋の形（下の2つの Repeater で共通。片方だけ変えないこと）

         [部屋名] [顔 顔 顔 顔 …] [人数/定員 入室]   ← 1行。顔は入るだけ並んで折り返す
                  [顔 顔 顔 …   ]                    ← 折り返しても、部屋名の下には入らない（列は分かれている）

         【、顔を部屋名の「横」へ移した】
         要望：参加者を部屋名の横に並べる（前は部屋名の下に来ていた）
         「固定部屋も今は下になってるんや。どっちも横にして」。
         **顔の <ul> を .room-item-head の中へ入れただけ**で、列の分かれ方は CSS が決める。

         【人数と入室は、1つの箱（.room-item-actions）で包む】
         包まないと、狭い画面で折り返したときに**入室ボタンだけが下の段へ落ちる**
         （部屋の中のバーで、歯車だけが落ちたのと同じ形。「入りきらなくなったら」）。

         部屋名：パスワード付きの部屋は、名前の前に鍵の印（RoomList.ascx.vb の LockMarkHtml）。
               名前そのものは .room-name-text に入れる（入室の小窓が、ここから部屋名を読む）。
         人数：目で見ると「3/20」。読み上げソフトには「3人、定員20人」と読ませる。
         満室：.room-item に is-full、入室ボタンに aria-disabled="true"（灰色。押しても入らない）。
               断るのはサーバー（ChatHub.JoinRoom の定員の判定）が本丸で、これは親切である。
         ノック：ノックを受け付ける部屋（みんなが作ったルームだけ）は、.room-item に
               data-knock-enabled="1"。満室のときは、入室ボタンが灰色ではなく「ノック」になる（data-mode="knock"）。
               ボタンの形は RoomList.ascx.vb の EnterButtonHtml（lounge-live.js の applyEnterButton と同じ形）。
               固定部屋は管理人が居ないので、ノックを受け付けない（ここでは必ず False を渡す） --%>
    <h3>既定のルーム<%= HelpButton("lounge.roomsFixed") %></h3>
    <asp:Repeater ID="FixedRoomsRepeater" runat="server" DataSource='<%# fixedRooms %>'>
        <ItemTemplate>
            <div class="room-item<%# If(IsFull(Eval("RoomId"), Eval("MaxUsers")), " is-full", "") %>" data-room-id='<%#: Eval("RoomId") %>' data-max-users='<%#: Eval("MaxUsers") %>'>
                <div class="room-item-head">
                    <span class="room-name"><%# LockMarkHtml(Eval("HasPassword")) %><span class="room-name-text"><%#: Eval("RoomName") %></span></span>
                    <%# MembersHtml(Eval("RoomId"), Eval("RoomName")) %>
                    <span class="room-item-actions">
                        <span class="room-count" data-room-id='<%#: Eval("RoomId") %>'><span class="room-count-current"><%#: OccupantCount(Eval("RoomId")) %></span><span aria-hidden="true">/</span><span class="visually-hidden">人、定員</span><%#: Eval("MaxUsers") %><span class="visually-hidden">人</span></span>
                        <% If hasNickname Then %>
                            <%# EnterButtonHtml(Eval("RoomId"), Eval("MaxUsers"), False) %>
                        <% End If %>
                    </span>
                </div>
            </div>
        </ItemTemplate>
    </asp:Repeater>

    <%-- 【id は、画面の側が「部屋を足す場所」の目印に使う】
         誰かが部屋を作ったら、Scripts/lounge-live.js がこの見出しの親（section.room-list）の
         末尾へ1部屋ぶんを足す。利用者が作った部屋は、この一覧のいちばん後ろに並ぶ
         （サーバー側は RoomId の順に出すので、新しい部屋が後ろ＝同じ並びになる）。
         **この id を消さないこと**。無いと、作った部屋がその場で出なくなる（F5すれば出る） --%>
    <h3 id="userRoomsHeading">みんなが作ったルーム<%= HelpButton("lounge.roomsUser") %></h3>
    <asp:Repeater ID="UserRoomsRepeater" runat="server" DataSource='<%# userRooms %>'>
        <ItemTemplate>
            <div class="room-item<%# If(IsFull(Eval("RoomId"), Eval("MaxUsers")), " is-full", "") %>" data-room-id='<%#: Eval("RoomId") %>' data-max-users='<%#: Eval("MaxUsers") %>'<%# KnockAttributeHtml(Eval("IsKnockEnabled")) %>>
                <div class="room-item-head">
                    <span class="room-name"><%# LockMarkHtml(Eval("HasPassword")) %><span class="room-name-text"><%#: Eval("RoomName") %></span></span>
                    <%# MembersHtml(Eval("RoomId"), Eval("RoomName")) %>
                    <span class="room-item-actions">
                        <span class="room-count" data-room-id='<%#: Eval("RoomId") %>'><span class="room-count-current"><%#: OccupantCount(Eval("RoomId")) %></span><span aria-hidden="true">/</span><span class="visually-hidden">人、定員</span><%#: Eval("MaxUsers") %><span class="visually-hidden">人</span></span>
                        <% If hasNickname Then %>
                            <%# EnterButtonHtml(Eval("RoomId"), Eval("MaxUsers"), Eval("IsKnockEnabled")) %>
                        <% End If %>
                    </span>
                </div>
            </div>
        </ItemTemplate>
    </asp:Repeater>

</section>
