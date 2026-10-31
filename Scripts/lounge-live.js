/*
 * ラウンジを SignalR に繋いで、部屋の様子（人数）をその場で書き換える
 *、ラウンジに居る人の顔（#loungeMembers）も同じ仕組みで書き換える。
 * 部屋の在室者と同じ部品（renderMembers ／ buildMemberItem）で描く
 *
 * サーバー側は Chat/Hubs/ChatHub.Lounge.vb。
 *
 * 【受け取るものは2つ】
 *   丸ごと … joinLounge の戻り値。全部屋の在室者（繋いだとき・繋ぎ直したとき・食い違いに気づいたとき）
 *   差分   … loungeUpdate の配信。変わった「その1人ぶん」だけ
 *
 * 【どちらにも通し番号（version）が付いている】
 * 丸ごと（戻り値）と差分（配信）は、届く順番が決まっていない。
 * 差分どうしも、サーバーが別々の場所から配るので、番号と逆の順に届くことがある。
 * そこで、この画面は次の決まりで当てる。
 *   ・丸ごとを受け取るまでは、差分は溜めておくだけ
 *   ・丸ごとの番号以下の差分は捨てる（丸ごとに入っている）
 *   ・それより後の差分は、番号の順に1つずつ当てる
 *   ・番号が飛んだまま埋まらなければ、丸ごとを取り直す
 * 差分だけで持ち続けると、1つ取りこぼしただけで、居ない人が居るまま戻らなくなる。
 * 番号を見ていれば、取りこぼしに自分で気づいて直せる
 *
 * 【最初の数字は、サーバーが入れてある】（Chat/UserControls/RoomList.ascx.vb）
 * 繋がるまでの間も、今の数字が出ている。繋がったら丸ごとで置き換える
 *
 * 【、部屋そのものの増減も受け取る】
 * 誰かが部屋を作ったら一覧へ足し（roomCreated）、誰も居なくなって閉じたら外す（roomClosed）。
 *
 * 【割り切り】繋がっていない間の「部屋の増減」は、追いつけない。
 * 丸ごと（joinLounge の戻り値）に入っているのは在室者だけで、部屋の一覧は入っていない
 * （入れるにはラウンジが繋ぐたびにDBを引くことになり、「ラウンジを開いてもDBは2回のまま」を崩す）。
 * そのため、切れている間に作られた部屋は出ず、閉じた部屋は残ったままになる（F5で直る）。
 * 残っていても、その部屋の入室ボタンを押せば「その部屋は存在しません」と断られるので、害は無い
 *
 * 【、ノックも受け持つ】
 * 満室でノックを受け付ける部屋の入室ボタンを「ノック」にし（applyEnterButton）、
 * 押されたらハブの knockRoom を呼び（window.VbChatLoungeLive.knock）、
 * 管理人の返事（knockAnswered）を受け取って、入室の小窓（Scripts/lounge-enter.js）へ渡す。
 * SignalR の受け口を登録するのは、このファイルだけにしてある（start より前に登録する必要があるため）
 *
 * 【読み込む順番】jQuery → jquery.signalR → /signalr/hubs → vbChatSettings のインライン → このファイル
 */
(function ($) {

    'use strict';

    if (!$) {
        return;
    }

    // === 参加者を押したら、名前の全体とトリップを出す（トリップ、名前の全体）===
    //
    // 一覧の名前は、入る幅まで出して「…」で切っている。
    // 押すと、その下に名前の全体と、付けていればトリップ（10桁）が出る。もう一度押すと隠れる。
    // トリップを付けている人は、アイコンの隅の小さい印で分かる（目立たない印でよい、という判断）。
    //
    // 【全員が押せる】（12-2 はトリップを付けている人だけだった）
    // 名前が切れた人を読む場所が要るので、誰を押しても名前の全体が出る＝ボタンにしてよい。
    //
    // 【SignalR に繋ぐ処理より前に置く】繋げなかったとき（下で帰る）でも、
    // サーバーが最初に出した顔は押せるようにしておく
    //
    // 開いている人を覚えておく（「一覧:その一覧の中の番号」）。顔の一覧を描き直しても閉じないように。
    // 一覧は、部屋なら部屋ID、ラウンジにいる人なら 'lounge'（LOUNGE_SCOPE）
    var expandedMembers = {};

    var LOUNGE_SCOPE = 'lounge';

    // その顔が、どの一覧のものか（部屋ID ／ 'lounge' ／ 分からなければ null）
    function memberScopeOf(element) {

        var room = $(element).closest('.room-item')[0];

        if (room) {
            return room.getAttribute('data-room-id');
        }

        if ($(element).closest('#loungeMembers').length > 0) {
            return LOUNGE_SCOPE;
        }

        return null;
    }

    $(document).on('click', '.room-member-btn', function () {

        var isOpen = this.getAttribute('aria-expanded') !== 'true';
        var detail = this.querySelector('.room-member-detail');
        var item = $(this).closest('.room-member')[0];
        var scope = memberScopeOf(this);

        this.setAttribute('aria-expanded', isOpen ? 'true' : 'false');

        if (detail) {
            detail.hidden = !isOpen;
        }

        // アカウントID を出している人は、ボタンのすぐ後ろに「プロフィール」のリンクがある。
        // 開いている間だけ出す（1人ぶんがボタンなので、リンクはボタンの外に置いてある）
        var profile = item ? item.querySelector('.room-member-profile') : null;

        if (profile) {
            profile.hidden = !isOpen;
        }

        if (item && scope !== null) {

            var key = scope + ':' + item.getAttribute('data-local-id');

            if (isOpen) {
                expandedMembers[key] = true;
            } else {
                delete expandedMembers[key];
            }
        }
    });

    // SignalR が読み込めていない、または古いハブ（joinLounge が無い）なら、ここから先は何もしない。
    // 数字はサーバーが入れた値のまま動かないだけで、ラウンジのほかの操作は使える
    if (!$.connection || !$.connection.chatHub ||
        typeof $.connection.chatHub.server.joinLounge !== 'function') {
        return;
    }

    var hub = $.connection.chatHub;
    var connection = $.connection.hub;

    var CONNECTED = $.signalR.connectionState.connected;
    var DISCONNECTED = $.signalR.connectionState.disconnected;

    // 自動再接続の待ち時間（部屋と同じ設定。SiteSettings の ReconnectDelaysSeconds）
    var settings = window.vbChatSettings || {};
    var RECONNECT_DELAYS_MS = (Array.isArray(settings.reconnectDelaysMs) && settings.reconnectDelaysMs.length > 0)
        ? settings.reconnectDelaysMs
        : [3000, 10000, 30000];

    // 番号が飛んだまま、この時間たっても埋まらなければ、丸ごとを取り直す。
    // 順番が入れ替わっただけなら、ふつうは一瞬で埋まる
    var GAP_RESYNC_MS = 5000;

    // 差分の種類（ChatHub.Lounge.vb の LoungeKind* と同じ綴り）
    var KIND_JOIN = 'join';
    var KIND_LEAVE = 'leave';
    var KIND_LOUNGE_JOIN = 'loungeJoin';       // ラウンジに1人現れた
    var KIND_LOUNGE_LEAVE = 'loungeLeave';     // ラウンジから1人居なくなった
    var KIND_LOUNGE_CHANGE = 'loungeChange';   // ラウンジに居る人がトリップを付け外しした
    var KIND_ROOM_CREATED = 'roomCreated';     // 利用者が部屋を作った
    var KIND_ROOM_CLOSED = 'roomClosed';       // 部屋が閉じた（誰も居なくなった一時部屋）
    var KIND_ROOM_OWNER = 'roomOwner';         // 部屋の管理人が替わった（顔の「管理人」の札）
    var KIND_OCCUPANT_CHANGE = 'occupantChange'; // 部屋に居る人が見た目（アイコン・色）を変えた
    var KIND_ROOM_CHANGED = 'roomChanged';     // 管理人が部屋の設定を変えた（部屋名・定員・鍵・ノック）

    // ノックの状態（ChatHub.Knock.vb の KnockStatus* と同じ綴り）
    var KNOCK_WAITING = 'waiting';
    var KNOCK_APPROVED = 'approved';
    var KNOCK_NOT_FULL = 'notFull';
    var KNOCK_DENIED = 'denied';
    var KNOCK_EXPIRED = 'expired';
    var KNOCK_CANCELLED = 'cancelled';
    var KNOCK_SEAT_EXPIRED = 'seatExpired';

    // 返事待ちの部屋（部屋ID → 待ち終わる時刻。Date.now() の値）。
    // この間は、その部屋の入室ボタンを「返事待ち」にする（サーバーは知らない、画面だけの状態）
    var knockWaiting = {};
    var knockWaitingTimers = {};

    // ノックの問い合わせを投げている部屋（二重に投げない）
    var knockBusy = {};

    // === この画面が持っている「部屋の様子」 ===
    var state = {
        // 最後に当てた差分の番号。丸ごとを受け取るまでは null（差分は溜めるだけ）
        version: null,
        // 部屋ID → 在室者の配列（入った順）。居ない部屋は入っていない
        rooms: {},
        // ラウンジに居る人の配列（来た順）。
        // 丸ごとに入っていなかった（古いサーバー）ときは null のまま＝サーバーが最初に出した顔を触らない
        members: null,
        // ラウンジに居る人数（サーバーが数える。この画面では数えない）
        loungeCount: null,
        // 部屋ID → 管理人の部屋の中の番号（居ない部屋は 0 か、入っていない）
        owners: {},
        // この画面の持ち主（ブラウザ）が今入っている部屋（0 なら居ない）と、その知らせの通し番号。
        // 最初の値はサーバーが書いた入室ボタン（data-mode="mine"）から読む（下の readInitialMyRoom）
        myRoomId: 0,
        myRoomSeq: -1,
        myFaceSeq: -1,   // 自分の顔（プロフィールの枠）の通し番号（loungeSelfFace と丸ごとの myRoomSeq）
        // まだ当てていない差分（番号 → 差分）
        pending: {}
    };

    // 顔の一覧を描き直す部屋（部屋ID → true）。差分で変わった部屋だけ描き直す
    var dirtyRooms = {};

    // 一覧に足す部屋・一覧から外す部屋。DOMを触るのは render の中だけにするため、ここへ溜める
    var pendingRoomAdds = [];
    var pendingRoomRemovals = [];
    var pendingRoomChanges = [];   // 部屋の設定の変更。足す・外すのあとで当てる

    // 丸ごとを受け取ったあとは、全部屋を描き直す
    var allRoomsDirty = false;

    // ラウンジに居る人の顔を描き直すか
    var membersDirty = false;

    var gapTimer = null;


    // === 丸ごとを当てる ===
    function applySnapshot(snapshot) {

        if (!snapshot || typeof snapshot.version !== 'number') {
            return;
        }

        var rooms = {};
        var owners = {};

        $.each(snapshot.rooms || [], function (index, room) {
            rooms[room.roomId] = (room.occupants || []).slice();
            owners[room.roomId] = room.ownerLocalId || 0;
        });

        state.owners = owners;

        // 自分が入っている部屋。loungeSelfRoom の配信と、番号の大きいほうを信じる
        if (typeof snapshot.myRoomSeq === 'number') {
            applySelfRoom(snapshot.myRoomId || 0, snapshot.myRoomSeq, true);
        }

        // 自分の顔。控えがあるときだけ入っている（無ければ、サーバーが描いた枠のまま）
        if (snapshot.myIconType && typeof snapshot.myRoomSeq === 'number') {
            applySelfFace(snapshot.myIconType, snapshot.myIconColor, snapshot.myRoomSeq, true);
        }

        // 番号も含めて丸ごと置き換える。
        // （アプリが再起動すると、サーバーの番号は 0 からやり直しになる。
        //   前より小さい番号でも、丸ごとなら信じてよい）
        state.rooms = rooms;
        state.version = snapshot.version;
        state.loungeCount = snapshot.loungeCount;

        // ラウンジに居る人の顔。サーバーが最初に出した顔（本人は仮の番号 0）も、ここで一覧ごと置き換わる
        if (Array.isArray(snapshot.members)) {
            state.members = snapshot.members.slice();
            membersDirty = true;
        }

        // 顔の一覧も、全部屋を描き直す
        allRoomsDirty = true;

        // 受け取れた＝更新は止まっていない
        setStale(false);

        drainPending();
        render();
    }


    // === 差分を受け取る（サーバーの loungeUpdate） ===
    function receiveUpdate(update) {

        if (!update || typeof update.version !== 'number') {
            return;
        }

        // 丸ごとに入っているか、もう当てたもの
        if (state.version !== null && update.version <= state.version) {
            return;
        }

        state.pending[update.version] = update;

        // 丸ごとがまだなら、溜めておくだけ
        if (state.version === null) {
            return;
        }

        drainPending();
        render();
    }


    // 溜めてある差分のうち、続き番号になっているものを順に当てる
    function drainPending() {

        Object.keys(state.pending).forEach(function (key) {
            if (Number(key) <= state.version) {
                delete state.pending[key];
            }
        });

        while (state.pending[state.version + 1]) {

            var next = state.pending[state.version + 1];

            delete state.pending[state.version + 1];

            applyUpdate(next);
            state.version = next.version;
        }

        // まだ残っている＝その手前の番号が届いていない
        if (Object.keys(state.pending).length > 0) {
            startGapTimer();
        } else {
            stopGapTimer();
        }
    }


    // === 自分の顔＝上のプロフィールの枠===
    //
    // 開発者の実機で、ラウンジのタブを開いたまま別のタブの部屋で色とアイコンを変えて退室すると、
    // 「ラウンジのアイコンは新しいのになってるけど、プロフィールは古いまま」だった
    // （一覧の自分の顔はサーバーの控えから描くので新しく、上の枠はページを開いたときの Cookie でサーバーが描いたまま）。
    // 同じ画面に自分の顔が2つ出て食い違うので、枠も合わせる。
    //
    // 知らせは2つの道で来る（丸ごとの myIconType と、loungeSelfFace の配信）。届く順番は決まっていないので、
    // 「自分が入っている部屋」と同じく、通し番号の大きいほうだけを信じる。
    // 描くのはアイコンの絵と色だけ（名前は変わらない。トリップの付け外しは、この画面の欄が自分で描き直している）。
    // 値はサーバーが ChatProfileValidator を通したもの。それでも style と href にしか入れない
    function applySelfFace(iconType, iconColor, seq, fromSnapshot) {

        if (typeof seq !== 'number' || !iconType) {
            return;
        }

        if (fromSnapshot ? seq < state.myFaceSeq : seq <= state.myFaceSeq) {
            return;
        }

        state.myFaceSeq = seq;

        var svg = document.querySelector('.current-profile .icon-frame--current svg');

        if (!svg) {
            return;
        }

        svg.style.color = iconColor || '';

        var use = svg.querySelector('use');

        if (use) {
            use.setAttribute('href', '#icon-' + iconType);
        }
    }


    // === 自分が入っている部屋===
    //
    // 開発者の実機で、管理人が別のタブでラウンジを開くと、自分の部屋に「ノック」が出ていた
    // （押すと「もうこの部屋に入っています」と断られる）。その部屋のボタンを「入室中」にする。
    //
    // 知らせは2つの道で来る（丸ごとの myRoomId と、loungeSelfRoom の配信）。届く順番は決まっていないので、
    // **通し番号の大きいほうだけを信じる**（丸ごとは、作った時点の番号＝同じ番号なら丸ごとを信じる）
    function applySelfRoom(roomId, seq, fromSnapshot) {

        if (typeof seq !== 'number') {
            return;
        }

        if (fromSnapshot ? seq < state.myRoomSeq : seq <= state.myRoomSeq) {
            return;
        }

        var before = state.myRoomId;

        state.myRoomSeq = seq;
        state.myRoomId = roomId || 0;

        if (before !== state.myRoomId) {
            if (before) {
                refreshEnterButton(before);
            }
            if (state.myRoomId) {
                refreshEnterButton(state.myRoomId);
            }
        }
    }

    // サーバーが最初に書いたボタンから、自分の部屋を読む（丸ごとが来るまでの間も「入室中」を保つため）
    function readInitialMyRoom() {

        var mine = document.querySelector('.btn-enter[data-mode="mine"]');

        if (mine) {
            state.myRoomId = parseInt(mine.getAttribute('data-room-id'), 10) || 0;
        }
    }

    readInitialMyRoom();


    // 在室者を、入室した順の位置へ入れる
    //
    // 部屋の参加者一覧と同じ並びにする（joinSeq＝サーバーの Participant.JoinSeq）。
    // 同じ部屋を別のタブで開き直した人は、元の番号のまま届くので、元の位置に戻る。
    // 番号が無い（古いサーバー）ときは末尾へ
    function insertByJoinSeq(list, occupant) {

        var seq = (typeof occupant.joinSeq === 'number') ? occupant.joinSeq : null;
        var at = list.length;

        if (seq !== null) {
            for (var i = 0; i < list.length; i++) {
                if (typeof list[i].joinSeq === 'number' && list[i].joinSeq > seq) {
                    at = i;
                    break;
                }
            }
        }

        list.splice(at, 0, occupant);
    }


    // 差分1つを、部屋の様子に当てる
    //
    // 【同じ差分を2度当てても壊れない形にしてある】
    // 入った人が既に居れば足さない、出た人が居なければ何もしない
    function applyUpdate(update) {

        var list;

        if (update.kind === KIND_JOIN) {

            if (update.occupant) {

                list = state.rooms[update.roomId];

                if (!list) {
                    list = [];
                    state.rooms[update.roomId] = list;
                }

                var exists = list.some(function (o) {
                    return o.localId === update.occupant.localId;
                });

                if (!exists) {
                    insertByJoinSeq(list, update.occupant);
                }

                dirtyRooms[update.roomId] = true;
            }

        } else if (update.kind === KIND_LEAVE) {

            list = state.rooms[update.roomId];

            dirtyRooms[update.roomId] = true;

            if (list) {

                list = list.filter(function (o) {
                    return o.localId !== update.localId;
                });

                if (list.length > 0) {
                    state.rooms[update.roomId] = list;
                } else {
                    delete state.rooms[update.roomId];
                }
            }

        } else if (state.members !== null &&
                   (update.kind === KIND_LOUNGE_JOIN || update.kind === KIND_LOUNGE_CHANGE)) {

            // ラウンジに1人現れた／中身が変わった。同じ番号が居れば置き換え、居なければ後ろに足す
            // （現れたのに既に居る＝2度目、変わったのに居ない＝取り違え。どちらでも壊れない形）
            if (update.member) {

                var replaced = false;

                state.members = state.members.map(function (m) {
                    if (m.memberId === update.member.memberId) {
                        replaced = true;
                        return update.member;
                    }
                    return m;
                });

                if (!replaced) {
                    state.members.push(update.member);
                }

                membersDirty = true;
            }

        } else if (state.members !== null && update.kind === KIND_LOUNGE_LEAVE) {

            state.members = state.members.filter(function (m) {
                return m.memberId !== update.memberId;
            });

            membersDirty = true;

        } else if (update.kind === KIND_OCCUPANT_CHANGE) {

            // 部屋に居る人が見た目（アイコン・色）を変えた。同じ番号の人を置き換え、その部屋の顔だけ描き直す。
            // 並び（joinSeq）はそのまま。居なければ何もしない（出ていった直後に届いた、など）
            list = state.rooms[update.roomId];

            if (list && update.occupant) {

                state.rooms[update.roomId] = list.map(function (o) {
                    return (o.localId === update.occupant.localId) ? update.occupant : o;
                });

                dirtyRooms[update.roomId] = true;
            }

        } else if (update.kind === KIND_ROOM_CHANGED) {

            // 管理人が部屋の設定を変えた。描くときに、その部屋の行をその場で書き換える
            // （作り直さない＝顔の一覧・開いている顔・焦点はそのまま）
            if (update.room) {
                pendingRoomChanges.push(update.room);
            }

        } else if (update.kind === KIND_ROOM_OWNER) {

            // 管理人の札を付け替える。その部屋の顔だけ描き直す
            state.owners[update.roomId] = update.localId || 0;
            dirtyRooms[update.roomId] = true;

        } else if (update.kind === KIND_ROOM_CREATED) {

            // 誰かが部屋を作った。一覧の最後（「みんなが作ったルーム」の末尾）へ足す。
            // まだ誰も入っていないので、在室者は持たない（state.rooms には入れない）
            if (update.room) {
                pendingRoomAdds.push(update.room);
            }

        } else if (update.kind === KIND_ROOM_CLOSED) {

            // 部屋が閉じた。誰も居なくなった一時部屋なので、在室者も残っていないはず
            delete state.rooms[update.roomId];
            delete dirtyRooms[update.roomId];

            pendingRoomRemovals.push(update.roomId);
        }

        // "lounge"（12-3 までの「ラウンジの人数だけが変わった」）と、この画面がまだ知らない種類は、
        // 中身には何もせず、人数だけ受け取る。
        // 知らない種類でも番号は進める（止めると、そこから先の差分が1つも当たらなくなる）
        if (typeof update.loungeCount === 'number') {
            state.loungeCount = update.loungeCount;
        }
    }


    // === 画面へ書く ===
    //
    // 【変わったところだけ書く】同じ数字を書き直さない（読み上げソフトが読んでいる最中の行を、
    // 意味もなく触らないため。この画面は aria-live を付けていないので、書いても読まれはしない）
    function render() {

        // 先に、部屋の増減を一覧へ当てる。
        // 【人数を書くより前に行うこと】足した部屋にも、この回の人数（0人）が入るようにするため
        applyRoomChanges();

        var total = 0;

        // 一覧に出ていない部屋の人も、部屋の合計には入れる（サーバーの最初の表示と同じ数え方）
        $.each(state.rooms, function (roomId, list) {
            total += list.length;
        });

        setText(document.getElementById('roomsCount'), total);

        if (typeof state.loungeCount === 'number') {
            setText(document.getElementById('loungeCount'), state.loungeCount);
        }

        $('.room-item[data-room-id]').each(function () {

            var roomId = this.getAttribute('data-room-id');
            var list = state.rooms[roomId] || [];

            setText($(this).find('.room-count-current')[0], list.length);

            renderFull(this, list.length);

            // 顔の一覧は、変わった部屋だけ描き直す（開いているトリップ・焦点を崩す回数を減らす）
            if (allRoomsDirty || dirtyRooms[roomId]) {

                var ul = this.querySelector('.room-members');

                if (ul) {
                    renderMembers(ul, roomId, list, 'localId', state.owners[roomId] || 0);
                    ul.hidden = (list.length === 0);
                }
            }
        });

        // ラウンジに居る人の顔。誰も居なければ一覧だけ隠す
        // （見出し「ラウンジ」は残す。部屋の一覧の中の1セクションにした。部屋の在室者の一覧と同じ扱い）
        if (membersDirty && state.members !== null) {

            var loungeList = document.getElementById('loungeMembers');

            if (loungeList) {
                renderMembers(loungeList, LOUNGE_SCOPE, state.members, 'memberId');
                loungeList.hidden = (state.members.length === 0);
            }
        }

        allRoomsDirty = false;
        dirtyRooms = {};
        membersDirty = false;
    }


    // === 満室の印===
    //
    // 届いた人数が定員に届いたら、入室ボタンを灰色に（aria-disabled）。減ったら戻す。
    // 【これは親切であって、防御ではない】断るのはハブの JoinRoom の定員の判定。
    // 押されたときの扱いは Chat/Lounge.aspx の入室ボタンの処理
    function renderFull(roomItem, count) {

        var max = parseInt(roomItem.getAttribute('data-max-users'), 10);
        var isFull = (max > 0 && count >= max);

        roomItem.classList.toggle('is-full', isFull);

        applyEnterButton(roomItem);
    }


    // === 入室ボタンの形（ノックを足した）===
    //
    //   空いている                                     … 「入室」
    //   満室・ノックを受け付けない                     … 「入室」＋ aria-disabled（灰色）
    //   満室・ノックを受け付ける（data-knock-enabled） … 「ノック」＋ data-mode="knock"
    //   満室・返事待ち（この画面でノックした）         … 「返事待ち」＋ data-mode="waiting" ＋ aria-disabled
    //   自分が（別のタブで）入っている部屋             … 「入室中」＋ data-mode="mine"（いちばん先に見る）
    //
    // 満室かどうかは .room-item の is-full で見る（renderFull が付け外しする。
    // サーバーの最初の表示でも付いている＝丸ごとを受け取る前でも正しく決まる）。
    //
    // 【同じ形を、サーバーでも作っている】RoomList.ascx.vb の EnterButtonHtml（返事待ち以外の3つ）。
    // **片方の形を変えたら、もう片方も直すこと**。
    //
    // 【空いたら、返事待ちでも「入室」に戻す】待っている間に普通に席が空いたら、そのまま入ればよい
    // （入れば、サーバーが返事待ちのノックを取り下げる）
    function applyEnterButton(roomItem) {

        var button = roomItem.querySelector('.btn-enter');

        if (!button) {
            return;
        }

        var roomId = roomItem.getAttribute('data-room-id');
        var isFull = roomItem.classList.contains('is-full');
        var knockable = roomItem.getAttribute('data-knock-enabled') === '1';
        var mode;

        if (state.myRoomId && String(state.myRoomId) === String(roomId)) {
            // 自分が（別のタブで）入っている部屋。満室でも押せる（このタブで開き直す）
            mode = 'mine';
        } else if (!isFull) {
            mode = 'enter';
        } else if (isKnockWaiting(roomId)) {
            mode = 'waiting';
        } else if (knockable) {
            mode = 'knock';
        } else {
            mode = 'full';
        }

        setText(button, mode === 'knock' ? 'ノック' :
                        mode === 'waiting' ? '返事待ち' :
                        mode === 'mine' ? '入室中' : '入室');

        if (mode === 'knock' || mode === 'waiting' || mode === 'mine') {
            button.setAttribute('data-mode', mode);
        } else {
            button.removeAttribute('data-mode');
        }

        if (mode === 'full' || mode === 'waiting') {
            button.setAttribute('aria-disabled', 'true');
        } else {
            button.removeAttribute('aria-disabled');
        }
    }


    function refreshEnterButton(roomId) {

        var item = document.querySelector('.room-item[data-room-id="' + roomId + '"]');

        if (item) {
            applyEnterButton(item);
        }
    }


    // === 部屋が増えた・減った===
    //
    // 誰かが部屋を作ったら一覧の最後へ足し、部屋が閉じたら外す。
    //
    // 【足す場所は「みんなが作ったルーム」の末尾＝部屋の一覧の末尾】
    // 3つのセクション（ラウンジ・既定のルーム・みんなが作ったルーム）は、どれも
    // section.room-list の直接の子として並んでいて、利用者が作った部屋はいちばん後ろにある
    // （サーバー側は RoomId の順に出す＝新しい部屋がいちばん後ろ。同じ並びになる）。
    // 見出しの id（userRoomsHeading）を目印にして、**古い RoomList.ascx が届いているときは
    // 何もしない**（見出しが無ければ、足す場所が分からない）
    function applyRoomChanges() {

        if (pendingRoomRemovals.length > 0) {

            pendingRoomRemovals.forEach(function (roomId) {

                // 返事待ちも終わり（部屋が無くなった。サーバーからも「取り消されました」が届く）
                clearKnockWaiting(roomId);

                var item = document.querySelector('.room-item[data-room-id="' + roomId + '"]');

                if (item && item.parentNode) {
                    item.parentNode.removeChild(item);
                }

                // 開いていた顔の「開いている」印も捨てる（その部屋はもう無い）
                var prefix = roomId + ':';

                Object.keys(expandedMembers).forEach(function (key) {
                    if (key.indexOf(prefix) === 0) {
                        delete expandedMembers[key];
                    }
                });
            });

            pendingRoomRemovals = [];
        }

        if (pendingRoomAdds.length === 0) {
            applyRoomSettingChanges();
            return;
        }

        var heading = document.getElementById('userRoomsHeading');
        var list = heading ? heading.parentNode : null;

        pendingRoomAdds.forEach(function (room) {

            if (!list) {
                return;
            }

            // 同じ部屋が既に在れば足さない（丸ごとの取り直しと差分が重なったとき）
            if (document.querySelector('.room-item[data-room-id="' + room.roomId + '"]')) {
                return;
            }

            list.appendChild(buildRoomItem(room));
        });

        pendingRoomAdds = [];

        // 作られたばかりの部屋の設定が、同じ回に変わっていることもあるので、足したあとで当てる
        applyRoomSettingChanges();
    }


    // === 部屋の設定の変更===
    //
    // 管理人が変えた部屋名・定員・鍵の有無・ノック受付を、一覧のその部屋の行へ、その場で当てる。
    // 行の形は RoomList.ascx と buildRoomItem の2か所で作っているが、中の並びは同じなので、どちらにも同じ書き換えが効く：
    //   ・部屋名   … .room-name-text（textContent）と、顔の一覧の aria-label
    //   ・鍵の印   … .room-name の中の .room-name-text 以外（buildLockMark の2つ）を外し、鍵付きなら付け直す
    //   ・定員     … .room-item の data-max-users と、.room-count の中の数字（いちばん外の文字の並び）
    //   ・ノック   … .room-item の data-knock-enabled
    // 最後に満室の印と入室ボタンの形を決め直す（renderFull。定員を今居る人数より下げると 5/3 で満室）
    //
    // 【名前を applyRoomChanges にしないこと】
    //、この関数を上の「部屋の増減」と同じ名前で書いていた。JavaScript では同じ名前の関数宣言は
    // **あとのものが黙って勝つ**（エラーも警告も出ない）ので、部屋の増減（roomCreated・roomClosed）が
    // 一度も一覧へ当たらなくなった（本番で「部屋を作っても、他の端末のラウンジに F5 するまで出ない」）
    function applyRoomSettingChanges() {

        if (pendingRoomChanges.length === 0) {
            return;
        }

        pendingRoomChanges.forEach(function (room) {

            var item = document.querySelector('.room-item[data-room-id="' + room.roomId + '"]');

            if (!item) {
                return;
            }

            var name = item.querySelector('.room-name');
            var nameText = item.querySelector('.room-name-text');

            if (name && nameText) {

                nameText.textContent = room.roomName || '';

                Array.prototype.slice.call(name.childNodes).forEach(function (node) {
                    if (node !== nameText) {
                        name.removeChild(node);
                    }
                });

                if (room.hasPassword) {
                    name.insertBefore(buildLockMark(), nameText);
                }
            }

            var members = item.querySelector('.room-members');

            if (members) {
                members.setAttribute('aria-label', (room.roomName || '') + 'にいる人');
            }

            item.setAttribute('data-max-users', String(room.maxUsers));

            var count = item.querySelector('.room-count');

            if (count) {
                Array.prototype.slice.call(count.childNodes).forEach(function (node) {
                    if (node.nodeType === 3 && node.nodeValue.trim() !== '') {
                        node.nodeValue = String(room.maxUsers);
                    }
                });
            }

            if (room.knockEnabled) {
                item.setAttribute('data-knock-enabled', '1');
            } else {
                item.removeAttribute('data-knock-enabled');
            }

            var current = item.querySelector('.room-count-current');
            renderFull(item, current ? (parseInt(current.textContent, 10) || 0) : 0);
        });

        pendingRoomChanges = [];
    }


    // 1部屋ぶん（.room-item）。形は RoomList.ascx の2つの Repeater と同じ
    //
    //   <div class="room-item-head">
    //     <span class="room-name">[鍵の印]<span class="room-name-text">部屋名</span></span>   ← 鍵の印は 15-2a
    //     <ul class="room-members">…</ul>          ←、ここへ移した（部屋名の横）
    //     <span class="room-item-actions">人数/定員 入室</span>
    //   </div>
    //
    // **片方の形を変えたら、もう片方も直すこと**（顔1人ぶんが2か所にあるのと同じ事情）。
    //
    // 【文字は textContent で入れる】部屋名を HTML として解釈させない
    // （サーバー側は <%#: %> でエスケープしている。「入口の検査だけでは足りない。出口でもエスケープする」）
    //
    // 【入室ボタンは、必ず付ける】
    // サーバー側は「名前のCookieがある人」にだけ出しているが、
    // ラウンジは名前が無ければ開けない（ChatPageBase がトップへ返す）ので、
    // このJSが動いている画面では必ず在る
    function buildRoomItem(room) {

        var item = document.createElement('div');
        item.className = 'room-item';
        item.setAttribute('data-room-id', String(room.roomId));
        item.setAttribute('data-max-users', String(room.maxUsers));

        // ノックを受け付ける部屋。形は RoomList.ascx.vb の KnockAttributeHtml と同じ
        if (room.knockEnabled) {
            item.setAttribute('data-knock-enabled', '1');
        }

        var head = document.createElement('div');
        head.className = 'room-item-head';

        // 部屋名。パスワード付きなら、名前の前に鍵の印。
        // 形は RoomList.ascx.vb の LockMarkHtml と同じ（**片方だけ変えないこと**）
        var name = document.createElement('span');
        name.className = 'room-name';

        if (room.hasPassword) {
            name.appendChild(buildLockMark());
        }

        var nameText = document.createElement('span');
        nameText.className = 'room-name-text';
        nameText.textContent = room.roomName || '';
        name.appendChild(nameText);

        head.appendChild(name);

        // 顔の一覧（作られたばかりなので、まだ誰も居ない＝hidden）。
        // **部屋名と、人数・入室の箱の「あいだ」に入れること**（列の並びがそのまま見た目になる）
        var members = document.createElement('ul');
        members.className = 'room-members';
        members.setAttribute('aria-label', (room.roomName || '') + 'にいる人');
        members.hidden = true;
        head.appendChild(members);

        // 人数と入室は1つの箱にまとめる（折り返したときに、2つが離れないため）
        var actions = document.createElement('span');
        actions.className = 'room-item-actions';

        // 「3/20」。目で見ると「3/20」、読み上げソフトには「3人、定員20人」
        var count = document.createElement('span');
        count.className = 'room-count';
        count.setAttribute('data-room-id', String(room.roomId));

        var current = document.createElement('span');
        current.className = 'room-count-current';
        current.textContent = '0';
        count.appendChild(current);

        var slash = document.createElement('span');
        slash.setAttribute('aria-hidden', 'true');
        slash.textContent = '/';
        count.appendChild(slash);

        var unit = document.createElement('span');
        unit.className = 'visually-hidden';
        unit.textContent = '人、定員';
        count.appendChild(unit);

        count.appendChild(document.createTextNode(String(room.maxUsers)));

        var people = document.createElement('span');
        people.className = 'visually-hidden';
        people.textContent = '人';
        count.appendChild(people);

        actions.appendChild(count);

        var enter = document.createElement('a');
        enter.href = '#';
        enter.className = 'btn-enter';
        enter.setAttribute('data-room-id', String(room.roomId));
        enter.textContent = '入室';
        actions.appendChild(enter);

        head.appendChild(actions);

        item.appendChild(head);

        return item;
    }


    // 鍵の印。サーバーの LockMarkHtml と同じ形の、SVG ＋ 読み上げ用の文字
    //
    // 【innerHTML にしていない】中身は決まった文字列なので害は無いが、
    // このファイルでは部屋名・名前を必ず textContent で入れる決まりにしてあり、例外を作らない
    // （SVG の名前空間は、下の buildMemberItem の中にも同じ値がある。あちらは関数の中の変数）
    var LOCK_SVG_NS = 'http://www.w3.org/2000/svg';

    function buildLockMark() {

        var fragment = document.createDocumentFragment();

        var svg = document.createElementNS(LOCK_SVG_NS, 'svg');
        svg.setAttribute('class', 'room-lock-icon');
        svg.setAttribute('viewBox', '0 0 16 16');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');

        var path = document.createElementNS(LOCK_SVG_NS, 'path');
        path.setAttribute('fill-rule', 'evenodd');
        path.setAttribute('d', 'M5 7V5a3 3 0 0 1 6 0v2h.5A1.5 1.5 0 0 1 13 8.5v5A1.5 1.5 0 0 1 11.5 15h-7A1.5 1.5 0 0 1 3 13.5v-5A1.5 1.5 0 0 1 4.5 7H5zm1.5 0h3V5a1.5 1.5 0 0 0-3 0v2z');
        svg.appendChild(path);
        fragment.appendChild(svg);

        var hidden = document.createElement('span');
        hidden.className = 'visually-hidden';
        hidden.textContent = 'パスワード付き：';
        fragment.appendChild(hidden);

        return fragment;
    }


    // === 顔の一覧===
    //
    // 部屋の全員を、アイコンと名前で並べる（要望：部屋の全員を出す。
    // 名前もいつも全部出す）。
    //
    // 【同じ形を、サーバーでも作っている】Chat/UserControls/RoomList.ascx.vb の MembersHtml。
    // 最初の表示はあちら、そのあとの変化はこちら。**片方の形を変えたら、もう片方も直すこと**
    //
    // 【文字は textContent で入れる】名前を HTML として解釈させない
    // （「入口の検査だけでは足りない。出口でもエスケープする」）
    //
    // 【ラウンジに居る人も、これで描く】部屋の在室者とまったく同じ形。
    //   ul     … 描く先の <ul class="room-members">
    //   scope  … どの一覧か（部屋ID ／ LOUNGE_SCOPE）。「開いている」印の鍵に使う
    //   list   … 並べる人（入った順・来た順）
    //   idKey  … 1人ぶんの番号が入っているキー（部屋＝'localId'、ラウンジ＝'memberId'）。
    //            どちらも <li> の data-local-id に入る（「その一覧の中の番号」の意味で共用している）
    // 一覧の出し入れ（hidden）は呼び出し側が決める（ラウンジは見出しごと隠すため）
    //   ownerId … その一覧の管理人の番号（部屋だけ。ラウンジと、居ないときは 0）
    function renderMembers(ul, scope, list, idKey, ownerId) {

        // 一覧の中の誰かに焦点があれば、描き直したあとで同じ人へ戻す
        // （描き直しで要素が入れ替わると、キーボードや読み上げソフトの居場所が消えるため）
        var focusedLocalId = null;
        var active = document.activeElement;

        if (active && ul.contains(active)) {
            var activeItem = $(active).closest('.room-member')[0];
            focusedLocalId = activeItem ? activeItem.getAttribute('data-local-id') : null;
        }

        // 居なくなった人の「開いている」印は捨てる
        var present = {};

        list.forEach(function (o) {
            present[scope + ':' + o[idKey]] = true;
        });

        Object.keys(expandedMembers).forEach(function (key) {
            if (key.indexOf(scope + ':') === 0 && !present[key]) {
                delete expandedMembers[key];
            }
        });

        ul.textContent = '';

        orderOwnerFirst(list, idKey, ownerId).forEach(function (o) {
            ul.appendChild(buildMemberItem(scope, o[idKey], o, !!ownerId && o[idKey] === ownerId));
        });

        if (focusedLocalId !== null) {

            var target = ul.querySelector('.room-member[data-local-id="' + focusedLocalId + '"] .room-member-btn');

            if (target) {
                target.focus();
            }
        }
    }


    // 管理人を先頭に置いた並びを返す。ほかの人は入った順のまま。
    //
    // 決定：管理人を先頭に置き、さらに名前より管理人の印を前に置く。
    // 【並べ替えるのは描くときだけ】state の配列（入った順）は触らない。管理人が替わったとき（roomOwner）・
    // 誰かが入ってきたとき（join）は、どちらもこの関数を通って描き直されるので、先頭が保たれる。
    // 【サーバーの最初の表示も同じ並び】RoomList.ascx.vb の MembersHtml（片方だけ変えないこと）
    // ownerId が 0（管理人が居ない・ラウンジ）なら、そのままの並び
    function orderOwnerFirst(list, idKey, ownerId) {

        if (!ownerId) {
            return list;
        }

        var owner = [];
        var others = [];

        list.forEach(function (o) {
            (o[idKey] === ownerId ? owner : others).push(o);
        });

        return owner.concat(others);
    }


    // 1人ぶん（<li>）。形は RoomList.ascx.vb の MemberItemHtml と同じ（作り直した）
    //
    //   <button class="room-member-btn" aria-expanded>
    //     <span class="room-member-main">[管理人の札] [アイコン（＋トリップの印）] [名前（…で切れる）]</span>
    //     （トリップあり）                    ← 読み上げ用。付けている人だけ
    //     <span class="room-member-detail" hidden>名前の全体（aria-hidden） ／ トリップ</span>
    //   </button>
    //
    // scope ／ id … どの一覧の、何番の人か（renderMembers の説明）
    // isOwner    … その部屋の管理人なら true。「管理人」の札（アイコンの前＝いちばん左）
    function buildMemberItem(scope, id, o, isOwner) {

        var SVG_NS = 'http://www.w3.org/2000/svg';

        // アカウントID を表示している人か。トリップとは、どちらか一方（決めるのはサーバー）。
        // 【形を、ここでも確かめる】半角の英数字とハイフン・アンダースコアの3〜30文字（サーバーのユーザー名の決まりと同じ）。
        // 行き先（/profile/…）を組み立てるので、形の合わない値は無いものとして扱う
        var accountName = (typeof o.accountName === 'string' && /^[A-Za-z0-9_-]{3,30}$/.test(o.accountName))
            ? o.accountName : null;
        var hasAccount = !!accountName;
        var hasTrip = !hasAccount && (typeof o.trip === 'string' && o.trip.length > 0);
        var isOpen = !!expandedMembers[scope + ':' + id];

        var li = document.createElement('li');
        li.className = isOwner ? 'room-member room-member--owner' : 'room-member';
        li.setAttribute('data-local-id', String(id));

        var body = document.createElement('button');
        body.type = 'button';
        body.className = 'room-member-btn';
        body.setAttribute('aria-expanded', isOpen ? 'true' : 'false');

        var main = document.createElement('span');
        main.className = 'room-member-main';

        // 管理人の札。、名前の右ではなくアイコンの前（いちばん左）。
        // 決定：名前より管理人の印を前に置く
        if (isOwner) {
            var badge = document.createElement('span');
            badge.className = 'owner-badge room-member-owner';
            badge.setAttribute('aria-hidden', 'true');
            badge.textContent = '管理人';
            main.appendChild(badge);
        }

        var iconFrame = document.createElement('span');
        iconFrame.className = 'room-member-icon';

        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('viewBox', '0 0 512 512');
        svg.setAttribute('aria-hidden', 'true');
        svg.style.color = o.iconColor || '';

        var use = document.createElementNS(SVG_NS, 'use');
        use.setAttribute('href', '#icon-' + (o.iconType || ''));

        svg.appendChild(use);
        iconFrame.appendChild(svg);

        if (hasTrip) {
            var mark = document.createElement('span');
            mark.className = 'room-member-trip-mark';
            mark.setAttribute('aria-hidden', 'true');
            iconFrame.appendChild(mark);
        } else if (hasAccount) {
            var accountMark = document.createElement('span');
            accountMark.className = 'room-member-trip-mark room-member-trip-mark--account';
            accountMark.setAttribute('aria-hidden', 'true');
            iconFrame.appendChild(accountMark);
        }

        main.appendChild(iconFrame);

        var name = document.createElement('span');
        name.className = 'room-member-name';
        name.textContent = o.nickname || '';
        main.appendChild(name);

        // 読み上げは名前のあとに「（管理人）」（札の文字は読ませない＝二重に読ませない）。
        // 札は見た目で前へ移したが、読む順は「名前（管理人）」のまま（部屋の参加者一覧と同じ読ませ方）
        if (isOwner) {
            var badgeText = document.createElement('span');
            badgeText.className = 'visually-hidden';
            badgeText.textContent = '（管理人）';
            main.appendChild(badgeText);
        }

        body.appendChild(main);

        if (hasTrip) {
            var hint = document.createElement('span');
            hint.className = 'visually-hidden';
            hint.textContent = '（トリップあり）';
            body.appendChild(hint);
        } else if (hasAccount) {
            var accountHint = document.createElement('span');
            accountHint.className = 'visually-hidden';
            accountHint.textContent = '（アカウントあり）';
            body.appendChild(accountHint);
        }

        // 押すと出る部分。名前の全体は読み上げさせない（上の名前が、切れずに読まれているため）
        var detail = document.createElement('span');
        detail.className = 'room-member-detail';
        detail.hidden = !isOpen;

        var fullName = document.createElement('span');
        fullName.className = 'room-member-fullname';
        fullName.setAttribute('aria-hidden', 'true');
        fullName.textContent = o.nickname || '';
        detail.appendChild(fullName);

        if (hasTrip) {
            var trip = document.createElement('span');
            trip.className = 'trip-code room-member-trip';
            trip.setAttribute('data-trip', o.trip);
            trip.textContent = o.trip;
            detail.appendChild(trip);
        } else if (hasAccount) {
            // 「@ユーザー名」。ここは文字だけ（1人ぶんがボタンなので、中にリンクは置けない）
            var account = document.createElement('span');
            account.className = 'account-id room-member-account';

            var accountLabel = document.createElement('span');
            accountLabel.className = 'visually-hidden';
            accountLabel.textContent = 'アカウント ';
            account.appendChild(accountLabel);
            account.appendChild(document.createTextNode('@' + accountName));

            detail.appendChild(account);
        }

        body.appendChild(detail);

        li.appendChild(body);

        if (hasAccount) {
            // 公開のプロフィールへ（新しいタブ）。ボタンのすぐ後ろ。開いている間だけ出す
            // 【行き先の形は、サーバーの AccountProfileRules.PublicUrl の写し】（RoomList.ascx.vb の MemberItemHtml と同じ）
            var profile = document.createElement('a');
            profile.className = 'room-member-profile';
            profile.target = '_blank';
            profile.rel = 'noopener';
            profile.hidden = !isOpen;
            profile.href = '/profile/' + encodeURIComponent(accountName);
            profile.appendChild(document.createTextNode('プロフィール'));

            var profileHint = document.createElement('span');
            profileHint.className = 'visually-hidden';
            profileHint.textContent = '（新しいタブで開きます）';
            profile.appendChild(profileHint);

            li.appendChild(profile);
        }

        return li;
    }


    // === 更新が止まったときの印===
    //
    // 自動の再接続をあきらめたら出す。数字を少し薄くし、「人数の更新が止まっています。」と
    // 「つなぎ直す」を出す。読み上げソフトには、止まった瞬間に1回だけ伝える
    // （#loungeStaleAnnounce は目に見えない。中身が変わったときだけ読まれる。
    //   消すときは空へ戻す——戻さないと、次に止まったときに読まれない）
    function setStale(isStale) {

        var box = document.getElementById('loungeStale');
        var status = $('.chat-status')[0];
        var announce = document.getElementById('loungeStaleAnnounce');

        if (box) {
            box.hidden = !isStale;
        }

        if (status) {
            status.classList.toggle('is-stale', isStale);
        }

        if (announce) {
            setText(announce, isStale ? '人数の更新が止まっています。' : '');
        }
    }


    function setText(element, value) {

        if (!element) {
            return;
        }

        var text = String(value);

        if (element.textContent !== text) {
            element.textContent = text;
        }
    }


    // === 番号の飛び ===
    function startGapTimer() {

        if (gapTimer !== null) {
            return;
        }

        gapTimer = setTimeout(function () {

            gapTimer = null;
            console.log('[ラウンジ] 差分の番号が飛んだまま埋まらないため、丸ごとを取り直します');

            // 溜めてある差分は、ここで捨ててよい。
            // どれも「今から頼む丸ごと」より前にサーバーで作られたもので、丸ごとに入っている。
            // 残しておくと、丸ごとの番号より大きい（あり得ない）番号が1つ混ざっただけで、
            // 取り直しが止まらなくなる。頼んだあとに届く差分は、今までどおり溜める
            state.pending = {};

            joinLounge();

        }, GAP_RESYNC_MS);
    }


    function stopGapTimer() {

        if (gapTimer !== null) {
            clearTimeout(gapTimer);
            gapTimer = null;
        }
    }


    // === 丸ごとを受け取る（何度呼んでもよい。サーバー側も同じ接続からの2度目を受け付ける） ===
    function joinLounge() {

        if (connection.state !== CONNECTED) {
            return;
        }

        hub.server.joinLounge()
            .done(applySnapshot)
            .fail(function (error) {

                console.log('[ラウンジ] 丸ごとの受け取りに失敗しました', error);

                // 繋がっているのに失敗した（サーバーが一瞬詰まった、など）。少し待ってから取り直す。
                // 切れて失敗したのなら、繋ぎ直したときに start が取り直すので、ここでは何もしない
                if (connection.state === CONNECTED) {
                    startGapTimer();
                }
            });
    }


    // ============================================================
    // 接続と、繋ぎ直し
    // ============================================================
    //
    // 部屋（chat-room.js）より簡単にしてある。ラウンジには「在室」の考え方が無く、
    // 繋がっていない間は数字が止まるだけで、入室などの操作は使えるため
    var isLeavingPage = false;
    var isStarting = false;
    var retryIndex = 0;
    var retryTimer = null;


    function start() {

        if (isLeavingPage || isStarting || connection.state !== DISCONNECTED) {
            return;
        }

        clearRetryTimer();
        isStarting = true;

        // 新しい接続では、前の番号の続きになるとは限らない（アプリが再起動すると 0 から）。
        // 丸ごとを受け取るまで、差分は溜めるだけにする
        state.version = null;
        state.pending = {};
        stopGapTimer();

        connection.start()
            .done(function () {
                retryIndex = 0;
                joinLounge();
            })
            .fail(function (error) {
                // 【ここで再試行を予約しないこと】start が失敗すると、SignalR 自身が
                // disconnected も出す。両方で予約すると、1回の失敗で予約が2つ進む（部屋で踏んだ罠）
                console.log('[ラウンジ] 接続に失敗しました', error);
            })
            .always(function () {
                isStarting = false;
            });
    }


    // 自動再接続（予約はここだけ）
    function scheduleRetry() {

        if (retryTimer !== null) {
            return;
        }

        // 決まった回数だけ試したら、あとは画面が表に戻る・回線が戻るのを待つ
        // （サーバーが本当に落ちているときに、叩き続けないため）
        if (retryIndex >= RECONNECT_DELAYS_MS.length) {
            console.log('[ラウンジ] 自動再接続をあきらめました（画面を表に戻すと、また試します）');

            // 数字がここから古くなる。止まったことを見せる
            setStale(true);
            return;
        }

        var delay = RECONNECT_DELAYS_MS[retryIndex];

        retryIndex += 1;
        console.log('[ラウンジ] 自動再接続を予約（' + retryIndex + '度目・' + delay + 'ms後）');

        retryTimer = setTimeout(function () {
            retryTimer = null;
            start();
        }, delay);
    }


    function clearRetryTimer() {

        if (retryTimer !== null) {
            clearTimeout(retryTimer);
            retryTimer = null;
        }
    }


    // 画面が表に戻った・回線が戻った。切れていれば、回数を数え直してすぐ繋ぐ
    function resume() {

        if (isLeavingPage || connection.state !== DISCONNECTED) {
            return;
        }

        retryIndex = 0;
        clearRetryTimer();
        start();
    }


    // 「つなぎ直す」（更新が止まったときの印の中）。
    // 印はいったん引っ込め、自動の再接続を最初の回数から試し直す。
    // また3回ともだめなら、上の scheduleRetry が印を出し直す
    $('#loungeRetryBtn').on('click', function () {

        setStale(false);

        if (connection.state === DISCONNECTED) {
            resume();
        } else {
            joinLounge();
        }
    });

    // 【必ず start より前に登録すること】
    // SignalR は、start の時点でクライアントの関数が1つでもあるハブだけを購読する。
    // 後から登録しても、配信は届かない
    hub.client.loungeUpdate = receiveUpdate;

    // ノックの行く末。**これも start より前に登録すること**
    hub.client.knockAnswered = receiveKnockAnswer;

    // 自分が入っている部屋が変わった。**これも start より前に**
    hub.client.loungeSelfRoom = function (roomId, seq) {
        applySelfRoom(roomId, seq, false);
    };

    // 自分の顔が変わった（別のタブの部屋で見た目を変えた）。これも start より前に
    hub.client.loungeSelfFace = function (iconType, iconColor, seq) {
        applySelfFace(iconType, iconColor, seq, false);
    };

    // SignalR 自身が繋ぎ直せたとき（同じ接続のまま）。
    // 取りこぼしは SignalR が送り直してくれるはずだが、それに頼らず丸ごとを取り直す
    connection.reconnected(function () {
        console.log('[ラウンジ] 再接続しました');
        joinLounge();
    });

    connection.disconnected(function () {

        // ページを離れる途中の切断は、繋ぎ直さない
        if (isLeavingPage) {
            return;
        }

        console.log('[ラウンジ] 切断されました');
        scheduleRetry();
    });

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            resume();
        }
    });

    window.addEventListener('online', resume);

    // ============================================================
    // ページを離れるときの合図
    // ============================================================
    //
    // 【何が起きていたか（本番の実機で見つかった）】
    // ラウンジから部屋ではないページ（説明書など）へ移ると、2分以上たっても人数が減らなかった。
    // Chrome は、戻るボタンですぐ戻れるように前のページを**凍らせたまま保持する**（bfcache）。
    // そのとき SignalR の接続も生きたまま保持されるので、サーバーからは「まだ居る」ように見える。
    // SignalR 自身の「切ります」は unload で送る作りだが、Chrome は unload を許さない。
    // 部屋で同じことが起きないのは、部屋はページを離れるときに退室のビーコンを送っているから。
    //
    // 【2つのことをする】
    //   1. ビーコンで「この接続はラウンジを離れます」を送る（すぐに人数が減る）
    //   2. 自分から接続を閉じる（凍ったページが、接続を握ったまま残らないように）
    // ビーコンだけでも人数は正しくなるが、接続が残るとサーバーの資源を握り続ける。
    // 閉じるだけだと、サーバーが気づくのは切断判定（約35秒）のあとになる
    //
    // 【persisted を問わず送る】（部屋とは違う）
    // 部屋は「在室」を守るために持ち越しの形を作っているが、ラウンジには在室が無い。
    // 保持されたページでも、戻ってこなければ居ないのと同じなので、いったん外してよい。
    // 戻ってきたら、下の pageshow で数に入り直す
    var leaveBeaconSent = false;

    function sendLeaveBeacon() {

        if (leaveBeaconSent) {
            return;
        }

        var connectionId = connection.id;

        // まだ繋がっていない（数に入っていない）なら、送るものが無い。
        // sendBeacon が無い（かなり古いブラウザ）ときは、切断判定に任せる
        if (!connectionId || connection.state !== CONNECTED ||
            !navigator.sendBeacon) {
            return;
        }

        leaveBeaconSent = true;

        // 本人確認は、接続IDとCookie（sendBeacon は Cookie を付けて送る）の両方（サーバー側）
        navigator.sendBeacon('/Chat/LeaveBeacon.aspx?lounge=1&connectionId=' +
                             encodeURIComponent(connectionId));
    }

    window.addEventListener('pagehide', function () {

        // 先に立てること。このあと接続を閉じると disconnected が出るが、
        // それを「予期しない切断」と取り違えて繋ぎ直しに行かないため
        isLeavingPage = true;

        sendLeaveBeacon();

        // 自分から閉じる。サーバーへの「切ります」は送らない（ビーコンで済んでいる。
        // ページを離れる最中の通信は、どのみち届くとは限らない）
        try {
            connection.stop(true, false);
        } catch (stopError) {
        }

        stopGapTimer();
        clearRetryTimer();
    });

    // 戻るボタンで、保持されていたページがそのまま戻ってきた（bfcache）。
    // 上で閉じた接続を繋ぎ直し、数に入り直して、丸ごとを受け取り直す
    // （保持されていた間の変化は、画面にはまだ当たっていない）
    window.addEventListener('pageshow', function (e) {

        if (!e.persisted) {
            return;
        }

        isLeavingPage = false;
        leaveBeaconSent = false;

        if (connection.state === DISCONNECTED) {
            resume();
        } else {
            // 閉じきれずに生きていた。数から外されているので、入り直す
            joinLounge();
        }
    });

    // ============================================================
    // ノック
    // ============================================================
    //
    // 流れはサーバーの ChatHub.Knock.vb の冒頭。ここは画面の側の受け持ち。
    //   ・「ノック」を押す → knockRoom → 返事待ちの印（ボタンが「返事待ち」）
    //   ・管理人の返事（knockAnswered）→ 許可なら入室の小窓（lounge-enter.js の openKnockApproved）
    //
    // 【結果は警告（site.js の showWarning）に出す】、警告そのものが読み上げソフトにも伝わる
    // （それまでは読み上げ用の1行 #knockAnnounce にも同じ文言を入れていた。二重に読まれないよう外した）

    function isKnockWaiting(roomId) {
        var until = knockWaiting[roomId];
        return typeof until === 'number' && until > Date.now();
    }

    function setKnockWaiting(roomId, seconds) {

        var ms = Math.max(1, seconds) * 1000;

        clearKnockWaiting(roomId);

        knockWaiting[roomId] = Date.now() + ms;

        // 時間が過ぎたら、画面の印だけは自分で外す（サーバーからも「返事がありませんでした」が届く）
        knockWaitingTimers[roomId] = setTimeout(function () {
            delete knockWaitingTimers[roomId];
            delete knockWaiting[roomId];
            refreshEnterButton(roomId);
        }, ms + 1000);

        refreshEnterButton(roomId);
    }

    function clearKnockWaiting(roomId) {

        if (knockWaitingTimers[roomId]) {
            clearTimeout(knockWaitingTimers[roomId]);
            delete knockWaitingTimers[roomId];
        }

        if (knockWaiting[roomId]) {
            delete knockWaiting[roomId];
            refreshEnterButton(roomId);
        }
    }

    // ノックの結果を知らせる（目で見える警告。読み上げも showWarning の箱が受け持つ）
    function tellKnock(text) {

        if (typeof window.showWarning === 'function') {
            window.showWarning(text);
        }
    }

    function roomLabel(info) {
        return '「' + ((info && info.roomName) || 'その部屋') + '」';
    }

    function enterApi() {
        return window.VbChatLoungeEnter || null;
    }

    // 「ノック」を押された（lounge-enter.js から呼ばれる）
    function knock(roomId, opener) {

        if (connection.state !== CONNECTED) {
            tellKnock('接続が切れているため、今はノックできません。少し待ってから、もう一度お試しください。');
            return;
        }

        if (knockBusy[roomId]) {
            return;
        }

        knockBusy[roomId] = true;

        hub.server.knockRoom(roomId)
            .done(function (result) {

                if (!result) {
                    tellKnock('ノックできませんでした。');
                    return;
                }

                if (result.status === KNOCK_WAITING) {
                    setKnockWaiting(roomId, result.seconds || 0);
                    tellKnock(result.message ||
                              ('ノックしました。' + roomLabel(result) + 'の管理人の返事を待っています（' +
                               (result.seconds || 0) + '秒）。'));
                    return;
                }

                if (result.status === KNOCK_APPROVED) {
                    // もう許可されている（前に出た小窓を閉じてしまった、など）。小窓を出し直す
                    if (enterApi()) {
                        enterApi().openKnockApproved(result, opener);
                    }
                    return;
                }

                if (result.status === KNOCK_NOT_FULL) {
                    // 空いていた（一覧の人数が古かった）。ノックせずに、そのまま入る
                    if (enterApi()) {
                        enterApi().startEnter(roomId, opener);
                    }
                    return;
                }

                tellKnock(result.message || 'ノックできませんでした。');
            })
            .fail(function (error) {
                console.log('[ラウンジ] ノックに失敗しました', error);
                tellKnock('通信エラーが発生しました。もう一度お試しください。');
            })
            .always(function () {
                delete knockBusy[roomId];
            });
    }

    // 許可の小窓で「やめる」を押した（lounge-enter.js から呼ばれる）。
    // 取り置いてもらった席を手放す。繋がっていなければ何もしない（取り置きは時間で外れる）
    function releaseKnockSeat(roomId) {

        if (connection.state !== CONNECTED) {
            return;
        }

        hub.server.releaseKnockSeat(roomId).fail(function (error) {
            console.log('[ラウンジ] 取り置きを手放せませんでした（時間で外れます）', error);
        });
    }

    // 管理人の返事が届いた
    function receiveKnockAnswer(answer) {

        if (!answer) {
            return;
        }

        clearKnockWaiting(answer.roomId);

        var label = roomLabel(answer);

        switch (answer.status) {

            case KNOCK_APPROVED:
                if (enterApi()) {
                    enterApi().openKnockApproved(answer, null);
                } else {
                    tellKnock(label + 'への入室が許可されました。入室ボタンから入ってください。');
                }
                break;

            case KNOCK_DENIED:
                tellKnock(label + 'の管理人から、今は入れないと返事がありました。');
                break;

            case KNOCK_EXPIRED:
                tellKnock(label + 'の管理人から、返事がありませんでした。');
                break;

            case KNOCK_CANCELLED:
                if (enterApi()) {
                    enterApi().closeKnockApproved(answer.roomId);
                }
                tellKnock(label + 'へのノックは取り消されました（管理人が替わったか、部屋が閉じました）。');
                break;

            case KNOCK_SEAT_EXPIRED:
                if (enterApi()) {
                    enterApi().closeKnockApproved(answer.roomId);
                }
                tellKnock('時間内に入らなかったため、' + label + 'の席の取り置きが外れました。');
                break;

            default:
                // 知らない種類（新しいサーバー・古い画面）。何もしない
                break;
        }
    }

    window.VbChatLoungeLive = {
        knock: knock,
        releaseKnockSeat: releaseKnockSeat
    };

    $(start);

})(window.jQuery);
