Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Microsoft.AspNet.SignalR
Imports Microsoft.AspNet.SignalR.Hubs

Public Class ChatHub
    Inherits Hub

    ' === Shared変数（クラス全体で共有。ChatHubは1リクエストごとに使い捨てインスタンスが
    '     生成されるため、参加者の状態はここに持たせる必要がある） ===

    ' 部屋ごとの参加者リスト（キー：RoomId）
    Private Shared RoomParticipants As New Dictionary(Of Integer, List(Of Participant))

    ' 部屋ごとに、次に発行するLocalId（欠番方式：退室した人の番号は再利用せず、増え続けるだけ）
    Private Shared NextLocalId As New Dictionary(Of Integer, Integer)

    ' ConnectionIdから「どの部屋にいたか」を逆引きするための辞書
    ' （切断時にConnectionIdしか分からないため、部屋を特定する目的で使う）
    Private Shared ConnectionRooms As New Dictionary(Of String, Integer)

    ' 上記のShared変数を複数リクエストから同時に触られないようにするための排他制御用オブジェクト
    Private Shared ReadOnly SyncRoot As New Object()

    ' 次に振る「入室した順の番号」（Participant.JoinSeq）。
    ' サイト全体で1つずつ増やすだけ（部屋ごとに分けなくても、同じ部屋の中の並びはこれで決まる）。
    ' 【SyncRoot の中でだけ進める】
    Private Shared NextJoinSeq As Long = 1



    ' クライアントから、部屋に入室する時に呼ばれる
    ' Groups.Addが非同期処理のため、Async Functionとして実装している
    ' 戻り値として、発行した自分のLocalIdと、今その部屋にいる他の参加者一覧を、
    ' まとめて呼び出し元へ返す（呼び出し元のクライアントが、UserJoinedのブロードキャストを
    ' 見て推測したり、参加者一覧を別途問い合わせたりする必要がないようにするため）
    '
    ' isReconnect … 「これは再接続としての呼び出しである」という文脈。
    '   Trueなら、席が既に無い場合に**新規入室として通さず、断る**（下の isNewOccupant の節）。
    '
    '   【これはクライアントに名乗らせてよい値である】
    '   「身元と部屋IDはクライアントに名乗らせない」という既存の方針とは、向きが違う。
    '   名乗るのは文脈だけで、しかも**「再接続だと名乗ると自分が断られる」方向**なので、
    '   嘘をついても得をせず、他人に害も及ばない
    '     ・Falseと嘘をつく → 新規入室として通る（＝今までどおり。得も損もしない）
    '     ・Trueと嘘をつく → 席が無ければ自分が断られるだけ
    '   browserIdやroomIdを名乗らせてはいけなかったのは、**他人を追い出せるから**だった。
    '   ここにはその性質が無い
    '
    '   【Optionalにしても意味が無い（実測）】
    '   SignalR 2.4.3は、ハブのメソッドを**名前と引数の個数**で解決する。
    '   VBの Optional は無視されるため、`Optional isReconnect As Boolean = False`
    '   にしても、引数4個の呼び出しは
    '   「There was an error invoking Hub method 'chathub.JoinRoom'.」で失敗する
    '   （実際に両方の形をビルドして、ブラウザから4個で呼んで確かめた）。
    '
    '   【だから、必須の引数のままにしてある】
    '   発行の直後、古いまま開かれていたページが古い引数で呼ぶと失敗するが、
    '   その失敗は joinRoomAndRender の .fail が受けて showServerTroubleBar へ合流する。
    '   これは「発行でハブのメソッド署名が変わった直後」として、
    '   もともと設計に織り込んである経路そのものである。
    '   赤い帯で理由が出てラウンジへ戻れば、次にページを開いた時点で新しいものになる。
    '   引数4個の版を残す（オーバーロード）手もあるが、そちらは
    '   **古いページが黙って古い挙動のまま動き続ける**ので採らない
    '
    ' nicknameFromClient … **読まない**。名前は札（ChatNameToken）から読む。
    '   引数を減らすとハブのメソッドの個数が変わり、発行の時点で開いていたページの呼び出しが
    '   全部失敗するので、受け取るだけ受け取って捨てている。
    '   **名前が要るところで、この引数を使わないこと**（下の nickname を使う）
    Public Async Function JoinRoom(roomId As Integer, nicknameFromClient As String, iconType As String,
                                   iconColor As String, isReconnect As Boolean) As Task(Of JoinRoomResult)

        ' === クライアントから渡された値を、まずサーバー側で検査する ===
        '
        ' この3つはCookieの値をクライアントが送ってくるもので、いくらでも書き換えられる。
        ' 開発者ツールからハブを直接呼べば、Cookieすら経由しない。
        ' 検査せずにParticipantへ入れると、そのまま部屋の全員へ配信され、
        ' ChatMessagesにも保存され、過去ログとして何度も再生されることになる。
        '
        ' 【ここで引数そのものを上書きしている理由】
        ' このあと iconType / iconColor は、参加者の生成・入室の配信・
        ' イベント行の文言・ログの書き込みと、いくつもの場所で使われる。
        ' 別の変数に入れる形にすると、直し忘れた1か所だけが素の値を使い続ける。
        ' 入口で引数を差し替えてしまえば、以降どこで使っても検査済みの値になる
        '
        ' 【判定の順番】安いものから先に行う。DBへの問い合わせ（部屋の確認）は最後。
        ' 断ることが先に決まっているのに、DBを引く必要はない
        iconType = ChatProfileValidator.NormalizeIconType(iconType)
        iconColor = ChatProfileValidator.NormalizeIconColor(iconColor)

        ' === ブラウザ識別IDは、クライアントの言い値ではなくCookieから読む ===
        '
        ' 【なぜ引数で受け取らないのか】
        ' この値は「同じブラウザかどうか」の判定に使い、一致した相手は部屋から
        ' 追い出される（同一ブラウザの2窓目による置き換え）。引数で受け取ると、
        ' 他人のBrowserIdを送りつけるだけで、その人を部屋から追い出せてしまう。
        ' 「誰であるか」を名乗らせてはいけない。LeaveBeacon.aspx.vbが本人確認に
        ' Cookieを使っているのと、同じ考え方に揃えてある
        Dim browserId As String = GetBrowserIdFromCookie()

        If Not ChatProfileValidator.IsValidBrowserId(browserId) Then
            ' Cookieが消えている・壊れている。トップページで決め直してもらえば再発行される
            Return Reject(
                "ブラウザの識別情報が読み取れないため、入室できません。お手数ですが、名前とアイコンを決め直してください。",
                TopPageUrl)
        End If

        ' === 名前も、引数ではなく「名前の札」から読む ===
        '
        ' 【引数の nickname は、もう使っていない】
        ' 残してあるのは、ハブのメソッドの引数の**個数**を変えないためだけである
        ' （SignalR 2.4.3 は名前と引数の個数でメソッドを解決するので、減らした瞬間に
        '   発行の時点で開いていたページの JoinRoom が全部失敗する。上の isReconnect の節と同じ話）。
        ' **この引数を読むコードを、二度と足さないこと。**
        '
        ' 【なぜ札にしたのか】
        ' 引数の名前をそのまま使っていたので、開発者ツールから joinRoom を別の名前で呼ぶだけで、
        ' ログアウトせずに名前を変えられた（(a)(b) の入り直しは塞いだが、
        ' 「この部屋にとって新しい1人」として入る経路が残っていた）。
        ' 札は「この ChatBrowserId の名前はこれ」をサーバーが署名したもの（Chat/NameToken.vb）。
        ' 名前の Cookie（ChatNickname）から読む形にしても、あれも本人が書き換えられるので穴は残る。
        '
        ' 【罠】ここで読めるのは「接続を張った時点」の Cookie である（トリップと同じ）。
        ' 名前は在室中に変わらない（変えるにはログアウト＝別人になる）ので、古くて困ることは無い。
        '
        ' 【検査は今までどおり通す】札に入っている名前は、決めた時点で検査済みだが、
        ' そのあと NicknameMaxLength を下げた場合に備えて、ここでも丸める（短くなる方向にしか動かない）
        Dim nickname As String = ChatProfileValidator.NormalizeNickname(
            NameToken.ReadNickname(browserId, GetCookieValue(NameToken.CookieName)))

        ' 名前が無い（札が無い・壊れている・別の身元のもの）場合は、入室を断る
        '
        ' ここで「ゲスト」のような既定名を付けないのは、本人が決めていない名前で
        ' 発言がログに残ってしまうため。名前は本人が決めるものとする。
        ' 戻す先はトップページ（名前とアイコンを決める画面）
        If String.IsNullOrEmpty(nickname) Then
            Return Reject("名前が決まっていないため、入室できません。名前を決めてください。", TopPageUrl)
        End If

        ' === トリップも、引数ではなく Cookie（ChatTripKey）から読む ===
        '
        ' 【ハブのメソッドに引数を足さないため】
        ' SignalR 2.4.3 はメソッドを「名前と引数の個数」で解決するので、
        ' 引数を1つ足した瞬間に、発行の時点で開いていたページの JoinRoom が全部失敗する
        ' （Optional でも吸収できない。実測）。
        ' browserId と同じく Cookie から読めば、引数は1つも増えない。
        '
        ' 【合言葉をクライアントから受け取ってよい理由】
        ' 「browserId や roomId を名乗らせない」方針とはぶつからない。
        ' 嘘の合言葉を送っても変わるのは自分のトリップの表示だけで、他人には何もできない
        ' （他人のトリップを作るにはソルトが要り、それはサーバーの中にしか無い）。
        '
        ' 【罠】ここで読めるのは「接続を張った時点」の Cookie である
        ' （Context.Request.Cookies は、WebSocket のハンドシェイクの値のまま固定される）。
        ' 在室中に合言葉を変えても、繋ぎ直すまでトリップは変わらない。
        '
        ' 【ロックの外で計算しておく】DBは触らないが、ハッシュの計算を
        ' 全部屋共通の鍵の中へ持ち込む理由も無い。
        ' トリップ機能が止まっているときは、ここで必ず Nothing になる（TripCode を参照）
        Dim cookieTrip As String = TripCode.FromCookieValue(GetCookieValue(TripCode.CookieName))

        ' === アカウントID を表示して参加する人か（チャットとアカウントの紐づけ） ===
        '
        ' 【決めるのは Chat/ChatAccountLink.vb の1か所】名前の札に入っているアカウントの番号と、
        ' ログインの札（と DB）が、同じアカウントを指しているときだけ「表示している」になる。
        ' これも引数では受け取らない（Cookie から読む。トリップ・ブラウザ識別IDと同じ）。
        '
        ' 【DB を1行引くことがある。だから鍵（SyncLock）の外で済ませる】
        ' 引くのは、名前の札にアカウントの番号が入っている人だけ（名乗らずに入る人・アカウントの無い人は、DB に触らない）。
        '
        ' 【トリップとは、どちらか一方】アカウントID を出す人には、合言葉の Cookie が在ってもトリップを付けない。
        ' ここ（サーバー）で決める。画面やブラウザから「両方出したい」と言う口は無い
        Dim accountLink As ChatAccountLink.LinkResult = ResolveAccountLink()
        Dim trip As String = If(accountLink.IsShowing, Nothing, cookieTrip)

        ' === 部屋の確認（存在するか・終了していないか・定員は何人か） ===
        '
        ' 【なぜ、ここで存在確認まで行うのか】
        ' roomIdはクライアントの言い値である。ハブを直接呼べば、Lounge.aspx.vbの
        ' 入室チェックも、ChatRoom.aspx.vbのSessionのガードも、まるごと迂回できる。
        ' ChatRoomsには既にPasswordHash・IsKnockEnabledの列があり、将来
        ' パスワード付きの部屋やノックを作れば、その判定もすべてこの経路で
        ' 素通りされることになる。機能を足すほど穴が深くなるため、先に閉じておく
        Dim isRoomOpen As Boolean = False
        Dim maxUsers As Integer = 0

        ' 足した2つ。入室の判定には使わず、メモリへ控えるためだけに読む
        Dim roomType As String = Nothing
        Dim roomOwner As Guid = Guid.Empty

        ' 足した1つ。パスワード付きの部屋か（ハッシュそのものは読まない。照合はラウンジの仕事）
        Dim roomHasPassword As Boolean = False

        ' DBから部屋の状態が読めたか（読めなかった回は、鍵付きかどうかをメモリの控えで決める）
        Dim roomStateKnown As Boolean = False

        ' 足した1つ。このブラウザが、この部屋で入室禁止か（同じ1回の SELECT で読む。ChatHub.OwnerTools.vb）
        Dim roomBannedByDb As Boolean = False

        If TryGetRoomState(roomId, browserId, isRoomOpen, maxUsers, roomType, roomOwner, roomHasPassword, roomBannedByDb) Then

            roomStateKnown = True

            ' 問い合わせに成功した。行が無い、またはIsActive = 0なら断る
            '
            ' 【存在しない部屋と、終了した部屋を、あえて同じ文言にしている】
            ' 普通の利用者はどちらにも到達しないため、区別する価値が無い。
            ' それどころか区別すると「その番号の部屋が存在するかどうか」を
            ' 外から確かめられることになる
            If Not isRoomOpen Then
                ' 文言は ChatCommon が持つ。ChatRoom.aspx.vb の Page_Load も
                ' 同じ場面で同じ文言を出すため（片方だけ直す事故を防ぐ）
                Return Reject(ChatCommon.RoomClosedMessage, LoungeUrl)
            End If

        Else

            ' 【フェイルオープンとフェイルクローズの、向きの使い分け】
            ' DBへ問い合わせられなかった場合は、判定そのものが不能である。
            ' ここで断ると、DBが一瞬詰まっただけで誰も入室できなくなるため通す。
            ' 定員の判定も、maxUsersが0のままなので行われない
            '
            ' 一方、問い合わせに成功したうえで「行が無い・IsActive = 0」だった場合は、
            ' 上のとおり必ず断る。存在チェックは、通してしまうと意味が無くなるため、
            ' ここだけ向きが逆になる
            maxUsers = 0

        End If

        Dim localId As Integer
        Dim existingParticipants As New List(Of ParticipantInfo)

        ' 作成した参加者情報を、SyncLockの外（ログ書き込み）からも参照できるよう、
        ' ここで宣言しておく（実際の生成はSyncLockの内側で行う）
        Dim participant As Participant = Nothing

        ' 同じBrowserId（＝同じブラウザ）の参加者が既にいた場合、扱いは3通りに分かれる。
        ' 分かれ目は「その参加者が、今もSignalRでつながっているかどうか」である
        '   (a) つながっている   … 別のタブ・ウィンドウからの接続なので、古い方を追い出す
        '   (b) 切れていて、同じ部屋へ戻ってきた … 本人が戻ってきただけなので、そのまま引き継ぐ
        '   (c) 切れていて、別の部屋へ入ろうとしている … 部屋を移ったので、元の部屋からは退室
        '
        ' (a)と(c)の後始末はAwaitやDBアクセスを伴うため、SyncLockの中では対象を特定するに留め、
        ' 実際の通知・ログ書き込みはSyncLockを抜けてから行う

        ' (a) 追い出す対象
        Dim kickConnectionId As String = Nothing
        Dim kickLocalId As Integer = 0
        Dim kickRoomId As Integer = 0
        Dim kickParticipant As Participant = Nothing
        Dim hasKickTarget As Boolean = False

        ' (b) 引き継ぐ場合はTrue
        Dim isTakeOver As Boolean = False

        ' (c) 元の部屋から退室させる対象
        Dim leaveRoomId As Integer = 0
        Dim leaveParticipant As Participant = Nothing
        Dim hasLeaveTarget As Boolean = False

        ' 満員で断ったときの理由（Nothingなら、満員ではない）
        ' SyncLockの中で決めて、抜けてから使う
        Dim fullRejectReason As String = Nothing

        ' 満員で断ったことを記録する場合の、記録用の参加者情報（Nothingなら記録しない）
        ' 実際には入室していないため、リストにも入らない「記録のためだけ」のオブジェクト
        Dim rejectLogParticipant As Participant = Nothing

        ' 再接続なのに席が無かったか（SyncLockの中で立て、抜けてから理由を組み立てる）
        Dim seatLostReject As Boolean = False

        ' 今まさに閉じられようとしている部屋だったか
        ' （SyncLockの中で立て、抜けてから断る。seatLostRejectと同じ形）
        Dim closingRoomReject As Boolean = False

        ' DB が読めず、鍵・入室禁止を確かめられない部屋だったか（ChatHub.RoomKeys.vb の IsRoomGuardUnknownCore）
        Dim roomStateUnknownReject As Boolean = False

        ' 鍵付きの部屋なのに、鍵を通った印が無かったか（ChatHub.RoomKeys.vb）
        ' （SyncLockの中で立て、抜けてから断る。seatLostRejectと同じ形）
        Dim passwordReject As Boolean = False

        ' パスワード違いで断ったことを記録する場合の、記録用の参加者情報（Nothingなら記録しない）
        Dim passwordRejectLogParticipant As Participant = Nothing

        ' この部屋で入室禁止になっていたか（ChatHub.OwnerTools.vb）
        ' （SyncLockの中で立て、抜けてから断る。seatLostRejectと同じ形）
        Dim bannedReject As Boolean = False
        Dim bannedRejectLogParticipant As Participant = Nothing

        ' 管理人が出ていったので、DBへ書き直す新しい管理人（(a)の置き換えで元の部屋が空く場合）
        Dim ownerHandoffRoomId As Integer = 0
        Dim ownerHandoff As Guid? = Nothing

        ' この部屋にとって「新しい1人」か（同じBrowserIdの参加者が、この部屋に居なかったか）
        '
        ' 【SyncLockの外で宣言している理由】
        ' 値を決めるのはロックの中だが、**抜けたあとの戻り値（IsContinuation）でも使う**。
        ' ロックの中で Dim すると、そのブロックの外では見えない。
        ' isTakeOver・seatLostReject を、ここでまとめて宣言しているのと同じ事情
        Dim isNewOccupant As Boolean = True

        ' ラウンジへ配る差分（ChatHub.Lounge.vb）
        ' 作るのはロックの中（通し番号を書き換えた順に振るため）、配るのはロックを抜けてから
        Dim loungeUpdates As New List(Of LoungeUpdate)

        ' IPアドレスを取得する（ログ記録用）
        ' 取得できないケースも考えられるため、失敗してもNothingのまま先へ進む
        ' （IPが取れないことで、チャットが止まってはいけない）
        Dim ipAddress As String = Nothing
        Try
            ipAddress = Context.Request.GetHttpContext().Request.UserHostAddress
        Catch ex As Exception
            ' 取得できなかった場合は、ログのIpAddress列がNULLになるだけで、動作に影響はない
        End Try

        ' 接続元のポート番号（IP と同じタイミングで読み、同じ行に入れる。Core/ClientEndpoint.vb）
        Dim remotePort As Integer? = Nothing
        Try
            remotePort = ClientEndpoint.ReadPort(Context.Request.GetHttpContext().Request)
        Catch ex As Exception
            ' 取得できなかった場合は、ログのRemotePort列がNULLになるだけ（IP と同じ）
        End Try

        ' 満員拒否の記録に使う設定を、ロックを取る前に読んでおく
        '
        ' 【罠】ロックの中でSiteSettingsを読んではいけない。
        ' キャッシュが空のときDBへ問い合わせに行くため、その間ロックを握り続け、
        ' 全部屋のチャットが止まる。判定を行うShouldLogJoinRejectは
        ' ロックの中で呼ぶ必要があるので、値のほうを引数で持ち込む
        Dim rejectLogCooldown As TimeSpan = SiteSettings.RejectLogCooldown
        Dim rejectLogTrackingLimit As Integer = SiteSettings.RejectLogTrackingLimit

        ' ノックで入れる臨時の席の上限（同じくロックの手前で読む）
        Dim knockExtraSeats As Integer = SiteSettings.RoomKnockExtraSeats

        ' 入った人が部屋の管理人なら、返事待ちのノック（管理人でなければ Nothing のまま）
        Dim pendingKnocks As List(Of KnockRequestInfo) = Nothing

        ' 今の管理人の、部屋の中の番号（居なければ 0）
        Dim ownerLocalId As Integer = 0

        SyncLock SyncRoot

            ' DBから読めた部屋の種別と管理人を、メモリへ控える（ChatHub.Rooms.vb）
            ' 読めなかった回は、何も控えない（そのときは roomType が Nothing）
            NoteRoomStateCore(roomId, roomType, roomOwner)

            ' 鍵付きかどうかも控える。読めなかった回は控えを触らない＝前に読めた値のまま
            If roomStateKnown Then
                NoteRoomLockCore(roomId, roomHasPassword)
            End If

            ' この部屋の参加者リストが未作成なら、新規作成する
            If Not RoomParticipants.ContainsKey(roomId) Then
                RoomParticipants(roomId) = New List(Of Participant)
            End If

            ' この部屋のLocalId発番カウンターが未作成なら、1から開始する
            If Not NextLocalId.ContainsKey(roomId) Then
                NextLocalId(roomId) = 1
            End If

            ' 全部屋を横断して、同じBrowserId（＝同じブラウザ）からの参加者が、
            ' 既にいないか確認する（別のタブ・ウィンドウから、あるいは切断前に、
            ' どこかの部屋に入っていたケース。今回入ろうとしている部屋と同じ部屋とは
            ' 限らないため、特定の部屋に絞らず全体を見る）
            Dim foundParticipant As Participant = Nothing
            Dim foundRoomId As Integer = 0

            For Each roomEntry In RoomParticipants
                Dim found = roomEntry.Value.FirstOrDefault(
                    Function(p) p.BrowserId = browserId)

                If found IsNot Nothing Then
                    foundParticipant = found
                    foundRoomId = roomEntry.Key
                    Exit For
                End If
            Next

            ' === 定員（MaxUsers）のチェック ===
            '
            ' 【必ず、参加者リストを書き換える前に行うこと】
            ' 満員で引き返すときは、何も起きなかったのと同じ状態でなければならない。
            ' このあとの (a) は、見つかった参加者をここでリストから取り除いてしまうため、
            ' 判定をあとに回すと「別の部屋にいた自分のタブが追い出されたのに、
            ' 新しい部屋へは入れなかった」という、席だけ失う結果になる
            '
            ' 【引き継ぎ・置き換えを、定員で弾いてはいけない】
            ' 同じBrowserIdの参加者が、既にこの部屋（roomId）にいる場合、
            ' その人は新しく席を取るのではなく、自分の席に戻ってくるだけである。
            '   ・再接続、F5、5秒の猶予からの復帰      … (b) 引き継ぎ
            '   ・同じ部屋を別のタブで開き直した       … (a) 同じ部屋での置き換え
            ' ここを取り違えると「満室の部屋でF5したら締め出される」という形で出る。
            ' 別の部屋にいた場合（(c) や、別の部屋からの置き換え）は、この部屋にとっては
            ' 新しい1人なので、通常どおり定員の判定を行う
            isNewOccupant = (foundParticipant Is Nothing OrElse foundRoomId <> roomId)

            ' === 再接続なのに、席が無い ===
            '
            ' 【何が起きていたか】無言タイムアウト（種別32）で退室させられた相手が
            ' 切断中だと、Kickedが届かない（下の RemoveParticipantAndNotify を参照）。
            ' 本人は退室させられたことを知らないまま画面に留まり、復帰したときの
            ' 再接続で JoinRoom を呼ぶ。そこを素通りさせていたため、
            ' **ただの新規入室として通ってしまい、部屋に居座り続けていた**。
            ' そのうえ再接続の直後にログを取り直すため、DBにある種別32（退室）と
            ' 種別20（入室）が過去ログとして並んで表示される
            ' （実機で発見。PCで正常だったのは、タイムアウトの瞬間に
            '   接続が生きていてKickedが届いたからで、コードの違いではない）
            '
            ' 【isNewOccupant がそのまま使える】
            ' あれは (a)同じ部屋での置き換え と (b)引き継ぎ の両方を「新しい1人ではない」側に
            ' まとめており、**参加者リストを書き換える前**に確定している。
            ' つまり「席がまだ在るか」の判定そのものであり、満員判定と同じ場所・同じ形で
            ' 引き返せる
            '
            ' 【拒否のログ（40番台）は残さない】
            ' 満員拒否と違って荒らしの挙動ではなく、普通に起こる正常な結果のため
            '
            ' 【ここでDBを引かないこと】断る理由（何で落とされたか）はログから引けるが、
            ' それはSyncLockを抜けてから行う。ここでは印を立てるだけ
            ' （満員拒否の記録を後回しにしているのと、まったく同じ形）
            If isReconnect AndAlso isNewOccupant Then
                seatLostReject = True
            End If

            ' === 今まさに閉じられようとしている部屋は、断る ===
            '
            ' 空になった一時部屋を閉じる仕組み（ChatHub.Rooms.vb）は、
            ' 「空であることを確かめて、印を付ける」までを この鍵の中で行っている。
            ' ここで断っておけば、**DBで IsActive = 0 にしている数ミリ秒のあいだに入室が通って、
            ' 閉じた部屋に人が残る**ことが無い。
            '
            ' 【文言は「この部屋は終了しました。」】実際そのとおりで、利用者から見れば
            ' 一覧から消えた部屋に入ろうとしたのと同じである。
            ' 拒否のログ（40番台）は残さない（荒らしの挙動ではなく、正常に起こりうる結果）
            If IsRoomClosingCore(roomId) Then
                closingRoomReject = True
            End If

            ' === DB が読めず、守り（鍵・入室禁止）を確かめられないときは、新しく入れない ===
            '
            ' 【定員は通す・守りは通さない】定員は DB が読めなければ通す（上のフェイルオープン）が、鍵と入室禁止は守りの話なので、
            ' 確かめられないなら断る（どの部屋が当たるかは IsRoomGuardUnknownCore）。前は「再起動直後で、まだ誰も入っていない部屋」だけ、
            ' 鍵付きかどうかが分からないまま通していた。拒否のログ（40番台）は残さない（荒らしの挙動ではなく、DB の都合）
            If isNewOccupant AndAlso Not roomStateKnown AndAlso Not seatLostReject AndAlso Not closingRoomReject AndAlso
               IsRoomGuardUnknownCore(roomId) Then
                roomStateUnknownReject = True
            End If

            ' === 入室禁止（ChatHub.OwnerTools.vb） ===
            '
            ' 管理人が「入室禁止にする」を押した人は、この部屋へ新しく入れない（パスワードを知っていても、ノックで席を取り置かれても）。
            ' **ここが最後の砦**で、ラウンジの入室ボタン（EnterRoom）とノック（KnockRoom）の断りは親切である。
            '
            ' 【isNewOccupant でだけ見る】禁止にした時点で、その人は部屋から外れている（同じブラウザの席は1つだけ）。
            ' F5・再接続で戻ってきた人は、上の seatLostReject が先に断る（理由は記録 38 から組み立てる）。
            ' 【鍵・定員より先】入れない人に、パスワードや満員を聞かせる意味が無い。
            ' 【DB が読めなかった回は、メモリの控えで決める】（IsRoomBannedCore。鍵付きかどうかの控えと同じ考え方）
            If isNewOccupant AndAlso Not seatLostReject AndAlso Not closingRoomReject AndAlso Not roomStateUnknownReject AndAlso
               IsRoomBannedCore(roomId, browserId, roomStateKnown, roomBannedByDb) Then

                bannedReject = True

                If ShouldLogJoinReject(ChatMessageType.JoinRejectedBanned, roomId, browserId,
                                       rejectLogCooldown, rejectLogTrackingLimit) Then
                    bannedRejectLogParticipant = New Participant With {
                        .LocalId = 0,
                        .Nickname = nickname,
                        .IconType = iconType,
                        .IconColor = iconColor,
                        .Trip = trip,
                        .BrowserId = browserId,
                        .IpAddress = ipAddress,
                        .RemotePort = remotePort
                    }
                End If

            End If

            ' === 入口の鍵 ===
            '
            ' 鍵付きの部屋へ「新しい1人」として入るには、ラウンジで鍵を通った印が要る
            ' （ChatHub.RoomKeys.vb）。**ここが最後の砦**で、ラウンジの小窓は親切である。
            '
            ' 【isNewOccupant でだけ聞く】F5・再接続（(b) 引き継ぎ）と、同じ部屋を別のタブで開き直した (a) は、
            ' 既に部屋に居る人なので聞き直さない（定員の判定を飛ばしているのと同じ理由）。
            '
            ' 【定員の判定より先】パスワードを知らない人に、満員かどうかを先に教える必要は無い。
            ' 満員の記録（40）も、ここで断った人には書かない（試していないので）。
            '
            ' 【ここでは印を「見る」だけ。使い終わりにするのは、実際に席を取ったとき】
            ' 満員などで断ったのに印を消すと、空くのを待って入り直すときに、また小窓からやり直しになる
            If isNewOccupant AndAlso Not seatLostReject AndAlso Not closingRoomReject AndAlso Not roomStateUnknownReject AndAlso Not bannedReject AndAlso
               IsRoomLockedCore(roomId) AndAlso Not HasRoomPassCore(roomId, browserId) Then

                passwordReject = True

                ' 記録するかどうかも、ここで決める（満員と同じ歯止め。キーは種別ごとに別）
                If ShouldLogJoinReject(ChatMessageType.JoinRejectedPassword, roomId, browserId,
                                       rejectLogCooldown, rejectLogTrackingLimit) Then
                    passwordRejectLogParticipant = New Participant With {
                        .LocalId = 0,
                        .Nickname = nickname,
                        .IconType = iconType,
                        .IconColor = iconColor,
                        .Trip = trip,
                        .BrowserId = browserId,
                        .IpAddress = ipAddress,
                        .RemotePort = remotePort
                    }
                End If

            End If

            If isNewOccupant AndAlso maxUsers > 0 AndAlso Not passwordReject AndAlso Not bannedReject Then

                Dim currentCount As Integer = RoomParticipants(roomId).Count

                ' 【ノックで席を取り置いてもらった人は、臨時の席まで通す】（ChatHub.Knock.vb）
                ' 定員は変えない。定員を超えて入れるのは RoomKnockExtraSeats 人まで（表示は 21/20 の形）。
                ' 普通の席が空いていれば、上の判定に引っかからないので、臨時の席は使わない
                Dim hasKnockSeat As Boolean =
                    currentCount >= maxUsers AndAlso
                    currentCount < maxUsers + knockExtraSeats AndAlso
                    HasKnockSeatCore(roomId, browserId, DateTime.Now)

                If currentCount >= maxUsers AndAlso Not hasKnockSeat Then

                    ' 満員。入室させない
                    '
                    ' 【ここでは判定するだけで、DBには触らない】
                    ' 拒否の記録（ChatLog.Write）は、SyncLockを抜けてから行う。
                    ' 満室の部屋を叩かれている最中は、まさにここが連打される場面であり、
                    ' ロックを握ったままINSERTすると、その間チャット全体が止まる
                    '
                    ' 例外を投げるのではなく戻り値で返すのは、既存の「退室理由の伝達」に
                    ' 合わせるため。クライアントはこの文言をsessionStorageへ預けてから
                    ' ラウンジへ移動し、移動先で帯として表示する
                    '
                    ' 【ここでSessionを消さないこと】
                    ' Sessionはタブごとではなくブラウザ単位で共有されている。
                    ' 同じブラウザの別のタブが、別の部屋に正当に在室している可能性があるため、
                    ' 入れなかったタブがSessionを消すと、そちらを巻き添えにする
                    ' （kickedの処理・LeaveBeaconで踏んだのと、まったく同じ形の罠）
                    fullRejectReason = "この部屋は満員です（" & currentCount & "/" & maxUsers & "）"

                    ' 記録を残すかどうかも、ここで決める
                    ' （判定に使う辞書もShared変数のため、ロックの中で触る必要がある）
                    If ShouldLogJoinReject(ChatMessageType.JoinRejectedFull, roomId, browserId,
                                           rejectLogCooldown, rejectLogTrackingLimit) Then
                        rejectLogParticipant = New Participant With {
                            .LocalId = 0,
                            .Nickname = nickname,
                            .IconType = iconType,
                            .IconColor = iconColor,
                            .Trip = trip,
                            .BrowserId = browserId,
                            .IpAddress = ipAddress,
                            .RemotePort = remotePort
                        }
                    End If

                End If

            End If

            ' 満員で断ったときは、ここから下（参加者リストの書き換え）を一切行わない。
            ' 「何も起きなかったのと同じ状態」で引き返すため
            If fullRejectReason Is Nothing AndAlso Not seatLostReject AndAlso Not closingRoomReject AndAlso
               Not roomStateUnknownReject AndAlso Not passwordReject AndAlso Not bannedReject Then

                If foundParticipant IsNot Nothing Then

                    ' 見つかった参加者が、今まさにJoinRoomを呼んでいるこの接続そのものかどうか
                    '
                    ' 【罠】これを見ないと、新しく開いたページが自分で自分をキックする。
                    ' ページの読み込み中（接続の確立中）にvisibilitychangeが発火すると、
                    ' hub.start()が二重に走り、同じ接続からJoinRoomが2回呼ばれることがある。
                    ' その2度目で、1度目に作られた自分自身が「(a) 別のタブからの接続」と
                    ' 判定され、自分の接続IDに向けてKickedが飛ぶ、という状態になっていた。
                    '
                    ' 同じ接続IDから2回呼ばれたのであれば、それは別のタブ・ウィンドウでは
                    ' あり得ず、同じ接続からの入り直しである。追い出してはいけない
                    '
                    ' クライアント側にも二重start()を防ぐガードを入れてあるが、本命はこちら。
                    ' 「クライアント側のチェックは親切であって防御ではない」という方針どおり、
                    ' JoinRoomが同じ接続から2回呼ばれる経路は、クライアントを直しても
                    ' 塞ぎきれない。ここを塞げば、原因が何であれ自己キックは起きない
                    Dim isSameConnection As Boolean =
                        (foundParticipant.ConnectionId IsNot Nothing AndAlso
                         foundParticipant.ConnectionId = Context.ConnectionId)

                    If foundParticipant.IsConnected AndAlso Not isSameConnection Then

                        ' (a) 今もつながっている＝別のタブ・ウィンドウからの接続。従来通り追い出す
                        kickConnectionId = foundParticipant.ConnectionId
                        kickLocalId = foundParticipant.LocalId
                        kickRoomId = foundRoomId
                        hasKickTarget = True

                        ' 強制退室ログを書くため、オブジェクトそのものも控えておく
                        ' （リストから削除したあとでも、オブジェクト自体は参照できる）
                        kickParticipant = foundParticipant

                        RoomParticipants(foundRoomId).Remove(foundParticipant)
                        ConnectionRooms.Remove(kickConnectionId)

                        ' ラウンジへも「その1人が出た」を。
                        ' 同じ部屋での置き換えなら、ラウンジから見てもその人は居続けているので quiet
                        loungeUpdates.Add(NextLoungeLeaveUpdate(
                            foundRoomId, foundParticipant.LocalId, quiet:=(foundRoomId = roomId)))

                        ' 外したのが別の部屋だった場合、その部屋が空になる・管理人が抜けることがある
                        ' （部屋Aのタブを開いたまま、部屋Bを開いた場合）。
                        '
                        ' 【同じ部屋での置き換えでは、呼ばないこと】
                        '、ここを部屋に関係なく呼んでいた。すると**管理人が同じ部屋を別のタブで開いた・
                        ' F5 の入室が退室ビーコンに競り勝った**だけで、「管理人が出ていった」と数えられ、
                        ' **ほかの人へ管理人が移っていた**（戻ってきた本人は、もう管理人ではない）。
                        ' 同じ部屋なら、このすぐ下で同じブラウザが入り直すので、空にもならず、管理人も替わらない
                        ' （ノックの返事待ちも、取り消してはいけない）
                        If foundRoomId <> roomId Then
                            ownerHandoff = AfterParticipantRemovedCore(foundRoomId, foundParticipant)
                            ownerHandoffRoomId = foundRoomId
                        End If

                    ElseIf foundRoomId = roomId Then

                        ' (b) 本人が、同じ部屋へ入り直した。ここに来るのは次の2通り。
                        '       ・切断中の本人が戻ってきた（通信が復活しただけ）
                        '       ・同じ接続から、JoinRoomが2回呼ばれた（上のisSameConnection）
                        '     どちらも新しい入室ではないため、Participantをそのまま再利用して
                        '     引き継ぐ。新しいLocalIdは発行せず、誰にも配信せず、
                        '     入室ログ（20）も書かない
                        isTakeOver = True
                        participant = foundParticipant

                        ' LocalIdは変更しない。他の参加者から見て、同じ人であり続けるため
                        localId = participant.LocalId

                        ' 新しい接続IDに差し替え、「つながっている」状態に戻す
                        participant.ConnectionId = Context.ConnectionId
                        participant.IsConnected = True

                        ' 「退室予告」が付いていれば、ここで取り消す。
                        ' 本人が猶予のうちに戻ってきたということなので、退室は無かったことになる
                        '
                        ' 【罠】このクリアを忘れると、引き継いだ直後に猶予切れと判定され、
                        ' せっかく戻ってきた本人が退室させられてしまう
                        participant.LeavePendingAt = Nothing

                        ' 【名前は、更新しない】
                        ' その部屋に入ったときの名前を、サーバーが覚えている値のまま使い続ける。
                        ' 名前は身元そのもので、変えるにはログアウトする（＝ChatBrowserId が作り直されて別人になる）しかない
                        ' （「身元の決まり」）。だから「同じブラウザが、同じ部屋へ、別の名前で入り直す」正当な場面は無い。
                        ' ここで引数の名前を採ると、開発者ツールから joinRoom を呼ぶだけで、ログアウトせずに名前を変えられた
                        ' （ほかの人の一覧は変わらないまま、次の発言から別の名前で流れ、ログにも残った。開発機で再現した）。
                        ' 【Cookie から読み直す形も採らない】Cookie も引数と同じく、本人が書き換えられる（信頼の境界の外）。
                        '
                        ' （このコメントは「名前・アイコンは、今回渡された値で更新する（切断している間に
                        '   Cookie が変更されている可能性があるため）」と書かれた。当時は名前を変える手段が無いとは決まっておらず、
                        '   「名前を変えたら別人」が入ってから、名前についてはその理由が成り立たなくなっていた）
                        '
                        ' 【アイコンと色は、今回渡された値で更新する】、部屋に居ながら変えられる
                        ' 見た目になった。画面は受け付けられてから Cookie を書くので、入り直しで送られてくるのは
                        ' サーバーが受け付けた値と同じもの。別のタブで変えたあとの F5 でも、Cookie の値に揃うのが正しい
                        ' （見た目は身元に関わらないので、細工されても本人の見た目が変わるだけ）。
                        '
                        ' トリップは、ここで入れ直す（サーバーが合言葉の Cookie から計算する値。扱いは 「トリップ」の章）
                        '
                        ' 【アカウントID は、更新しない】名前と同じ扱い。この部屋に入ったときに決まった形のまま。
                        ' 途中で変えると、入室の行には番号があるのに退室の行には無い、という記録ができる（Participant.AccountId のコメント）。
                        ' だからトリップも、アカウントID を出して入った人には入れ直さない（どちらか一方、を保つ）
                        participant.IconType = iconType
                        participant.IconColor = iconColor
                        participant.Trip = If(participant.AccountId > 0, Nothing, cookieTrip)
                        participant.IpAddress = ipAddress
                        participant.RemotePort = remotePort

                        ' LastActiveAt（最終発言時刻）は更新しない。
                        ' 戻ってきたことは「発言した」ことにはならず、更新してしまうと
                        ' 無言タイムアウトが働かなくなるため

                        ConnectionRooms(Context.ConnectionId) = roomId

                    Else

                        ' (c) 切断中の本人が、別の部屋へ入ろうとしている。
                        '     部屋を移ったということなので引き継がず、元の部屋からは退室扱いにする
                        '     （リストからの削除は、RemoveParticipantAndNotifyがまとめて行う）
                        leaveRoomId = foundRoomId
                        leaveParticipant = foundParticipant
                        hasLeaveTarget = True

                    End If

                End If

                ' 今入ろうとしている部屋に、既にいる参加者一覧を控えておく
                ' （新規入室の場合、この時点ではまだ自分自身をリストに追加していない。
                '   引き継ぎ(b)の場合は自分が既にリストにいるため、自分自身を除いている。
                '   同じブラウザの古い参加者は、上の(a)の処理で既にこのリストから除かれている）
                '
                ' AnonId は、無視機能でクライアントが「一覧のこの人」と「この行」を
                ' 結び付けるための、使い捨ての印（AnonIdクラスを参照）。
                ' BrowserIdそのものは、絶対にクライアントへ渡さない
                ' 【入室した順に並べる】。リストの並びは、同じ部屋での置き換えで崩れるため
                existingParticipants = RoomParticipants(roomId).Where(
                    Function(p) p IsNot participant).OrderBy(
                    Function(p) p.JoinSeq).Select(
                    Function(p) New ParticipantInfo With {
                        .LocalId = p.LocalId,
                        .Nickname = p.Nickname,
                        .IconType = p.IconType,
                        .IconColor = p.IconColor,
                        .AnonId = AnonId.ComputeFromString(roomId, p.BrowserId),
                        .Trip = p.Trip,
                        .AccountName = p.AccountName,
                        .JoinSeq = p.JoinSeq
                    }).ToList()

                ' 引き継ぎ(b)の場合、新しくLocalIdを発行したり、参加者を追加したりはしない
                If Not isTakeOver Then

                    ' LocalIdを発行し、カウンターを次に進める
                    localId = NextLocalId(roomId)
                    NextLocalId(roomId) += 1

                    ' 【入室した順の番号と、入った時刻】
                    ' 同じ部屋を別のタブで開き直した（(a) 同じ部屋での置き換え）なら、古いほうの値をそのまま移す。
                    ' 移さないと、F5 の入室が退室ビーコンに競り勝っただけで、その人が一覧の最後へ動き、
                    ' 「いちばん古くから居る人」（管理人の引き継ぎ先）からも外れる
                    '
                    ' 【名前も、古いほうから移す】。上の (b) の「名前は、更新しない」と同じ理由。
                    ' 移さないと、名前の Cookie だけ書き換えて同じ部屋を別のタブで開くだけで、ログアウトせずに
                    ' 名前が変わり、ほかの人の一覧もその場で新しい名前に置き換わった（開発機で再現した）。
                    ' 札から読んだ nickname は、この部屋にとって新しい1人のとき（新規入室・別の部屋から移ってきた）だけ使う。
                    ' 【、その nickname も札（ChatNameToken）から読んでいる】
                    ' それまでは引数の言い値だったので、新しく入る経路だけは名前を変えられた（残っていた最後の穴）
                    '
                    ' 【アカウントID も、古いほうから移す】名前と同じ理由。
                    ' 同じ部屋での置き換えは「居続けている人」なので、入ったときに決まった形のまま。
                    ' トリップは、アカウントID を出している人には付けない（どちらか一方）
                    Dim joinSeq As Long
                    Dim joinedAt As DateTime
                    Dim participantNickname As String = nickname
                    Dim participantAccountId As Integer = accountLink.AccountId
                    Dim participantAccountName As String = accountLink.UserName
                    Dim participantTrip As String = trip

                    If hasKickTarget AndAlso kickRoomId = roomId AndAlso kickParticipant IsNot Nothing Then
                        joinSeq = kickParticipant.JoinSeq
                        joinedAt = kickParticipant.JoinedAt
                        participantNickname = kickParticipant.Nickname
                        participantAccountId = kickParticipant.AccountId
                        participantAccountName = kickParticipant.AccountName
                        participantTrip = If(kickParticipant.AccountId > 0, Nothing, cookieTrip)
                    Else
                        joinSeq = NextJoinSeq
                        NextJoinSeq += 1
                        joinedAt = DateTime.Now
                    End If

                    ' 参加者情報を作成し、部屋のリストに追加する
                    participant = New Participant With {
                        .LocalId = localId,
                        .Nickname = participantNickname,
                        .IconType = iconType,
                        .IconColor = iconColor,
                        .Trip = participantTrip,
                        .AccountId = participantAccountId,
                        .AccountName = participantAccountName,
                        .BrowserId = browserId,
                        .ConnectionId = Context.ConnectionId,
                        .IsConnected = True,
                        .LastActiveAt = DateTime.Now,
                        .JoinedAt = joinedAt,
                        .JoinSeq = joinSeq,
                        .IpAddress = ipAddress,
                        .RemotePort = remotePort
                    }
                    RoomParticipants(roomId).Add(participant)

                    ' 鍵を通った印は、これで使い終わり。鍵の無い部屋でも呼んでよい（無ければ何もしない）
                    ConsumeRoomPassCore(roomId, browserId)

                    ' ノックの取り置きも使い終わり。返事待ちのノックがあれば取り下げる
                    ConsumeKnocksOnJoinCore(roomId, browserId)

                    ' この部屋は空ではなくなった（ChatHub.Rooms.vb）。
                    ' 【必ず、参加者リストへ足したあとに呼ぶこと】
                    ' 「空になったら閉じる」の印を外す処理で、足す前に呼ぶと、
                    ' 同じ見回りの回で「まだ空」と判定されうる
                    NoteRoomOccupiedCore(roomId)

                    ' 切断時に部屋を逆引きできるよう、ConnectionIdと部屋の対応を記録する
                    ConnectionRooms(Context.ConnectionId) = roomId

                    ' ラウンジへも「その1人が入った」を（上の (a) と同じ部屋での置き換えなら quiet）
                    loungeUpdates.Add(NextLoungeJoinUpdate(
                        roomId, participant, quiet:=(hasKickTarget AndAlso kickRoomId = roomId)))

                End If

            End If  ' fullRejectReason Is Nothing

            ' ラウンジの顔も合わせる。部屋へ入った人がラウンジを開いたままなら、
            ' ラウンジの顔から消す（ラウンジに居る人は「どの部屋にも居ない人」のため）。
            ' 断った場合は参加者リストを書き換えていないので、何も出てこない
            loungeUpdates.AddRange(SyncLoungeMembers())

            ' 入ったのが管理人なら、返事待ちのノックを渡す（F5・繋ぎ直しの間に届いたぶんを出し直すため）。
            ' 断った場合は participant が Nothing なので、何も渡さない
            pendingKnocks = PendingKnocksForOwnerCore(roomId, participant, DateTime.Now)

            ' === 管理人の印と、ラウンジの「入室中」 ===
            If participant IsNot Nothing Then

                ' 新しく部屋の番号をもらったのが管理人なら、ほかの人の画面の印を付け替える（行は出さない）。
                ' 部屋を作って入った・同じ部屋を別のタブで開き直した・アプリが起き直したあとに入ってきた、のどれでも番号が変わる
                If Not isTakeOver AndAlso IsRoomOwnerCore(roomId, participant.BrowserId) Then
                    QueueOwnerChangedCore(roomId, participant, announce:=False)
                End If

                ' このブラウザがラウンジを開いていれば、「あなたはこの部屋に居る」を知らせる
                QueueSelfRoomCore(browserId)

            End If

            ownerLocalId = OwnerLocalIdCore(roomId)

        End SyncLock

        ' ラウンジへ配る（断った場合は、参加者リストを書き換えていないので空のまま）
        PublishLoungeUpdates(loungeUpdates)

        ' (a) の置き換えで、元の部屋の管理人が抜けていたら、DBへ書き直す
        ' （必ず SyncLock を抜けてから＝DBを鍵の中に持ち込まない）
        If ownerHandoff.HasValue Then
            WriteOwnerHandoff(ownerHandoffRoomId, ownerHandoff.Value)
        End If

        ' 今まさに閉じられようとしている部屋だった。何も起きなかったのと同じ状態で引き返す
        If closingRoomReject Then
            Return Reject(ChatCommon.RoomClosedMessage, LoungeUrl)
        End If

        ' 鍵・入室禁止を確かめられなかった。何も起きなかったのと同じ状態で引き返す
        If roomStateUnknownReject Then
            Return Reject(ChatCommon.RoomStateUnknownMessage, LoungeUrl)
        End If

        ' 満員で断った場合は、記録だけ残して引き返す
        ' （ログの書き込みは、必ずSyncLockを抜けてから。DBをロックの中に持ち込まない）
        ' === 再接続なのに席が無かった ===
        '
        ' 【なぜ、ここでログを引くのか】
        ' 切断中に落とされた相手には Kicked が届かない（接続IDが無いため）。
        ' そのため本人は「なぜ居なくなったか」を知らないままラウンジへ戻ることになり、
        ' 接続が生きていた人（PC）が見る「1時間発言がなかったため、退室しました」と
        ' 食い違って見えていた。
        ' 理由そのものは ChatMessages の退室ログに残っているので、そこから引く。
        ' **サーバー側に覚えておくもの（タイムアウトした人の一覧など）は増やさない**
        '
        ' 【SyncLockを抜けてから引くこと】ここはDBへの問い合わせである
        If seatLostReject Then
            Return Reject(BuildSeatLostReason(roomId, browserId), LoungeUrl)
        End If

        ' 入室禁止だった。記録だけ残して、ラウンジへ戻す
        If bannedReject Then

            If bannedRejectLogParticipant IsNot Nothing Then
                ChatLog.Write(roomId, ChatMessageType.JoinRejectedBanned, bannedRejectLogParticipant)
            End If

            Return Reject(BannedJoinMessage, LoungeUrl)

        End If

        ' 鍵を通っていなかった。記録だけ残して、ラウンジへ戻す。
        ' ふつうの利用者がここへ来るのは、小窓で通ってから部屋が開くまでに60秒以上かかったときか、
        ' 部屋を出たあと（5秒の猶予を過ぎてから）F5 で戻ってきたとき。どちらもラウンジから入り直せばよい
        If passwordReject Then

            If passwordRejectLogParticipant IsNot Nothing Then
                ChatLog.Write(roomId, ChatMessageType.JoinRejectedPassword, passwordRejectLogParticipant)
            End If

            Return Reject(PasswordRequiredMessage, LoungeUrl)

        End If

        If fullRejectReason IsNot Nothing Then

            If rejectLogParticipant IsNot Nothing Then
                ChatLog.Write(roomId, ChatMessageType.JoinRejectedFull, rejectLogParticipant)
            End If

            Return Reject(fullRejectReason, LoungeUrl)

        End If

        ' (a) 古い接続を追い出す場合、"古い接続がいた方の部屋"のグループから外したうえで、
        ' その接続自身にだけ「別の場所で接続されたため切断された」旨を個別に通知し、
        ' 古い接続がいた部屋の残りの参加者へは、通常の退室通知と同じ形で知らせる
        ' （古い接続が今回入ろうとしている部屋と別の部屋にいた場合もあるため、
        '   kickRoomId（roomIdではなく）を使う点に注意）
        If hasKickTarget Then
            Await Groups.Remove(kickConnectionId, kickRoomId.ToString())
            Clients.Client(kickConnectionId).Kicked("他のタブ・ウィンドウで接続されたため、退室しました")

            ' 参加者一覧から外すため、退室の配信そのものは必ず行う。
            ' ただし種別33（置き換え）の文言はNothingが返るため、画面には行が出ない
            ' （記録はする、表示はしない。理由はChatEventTextのコメント）
            Clients.Group(kickRoomId.ToString()).UserLeft(
                kickLocalId,
                ChatEventText.Build(ChatMessageType.Replaced, kickParticipant.Nickname),
                AnonId.ComputeFromString(kickRoomId, kickParticipant.BrowserId))

            ' 強制退室（システムによる置き換え）のログを残す。
            ' ここで何も残さないと「入室ログはあるが退室ログが無い人」がログ上に発生し、
            ' 後から在室期間を復元できなくなるため
            ' （ログの書き込みは、必ずクライアントへの配信のあとに行う。
            '   DBが一時的に詰まっても、チャットの表示速度に影響させないため）
            ChatLog.Write(kickRoomId, ChatMessageType.Replaced, kickParticipant)
        End If

        ' 部屋の知らせ（ノックの小窓を消す・取り消しを伝える、管理人の交代など。鍵の中で積んだもの）。
        ' 【(a) の退室の配信より後に配る】別の部屋のタブを追い出して、その部屋の管理人が替わったとき、
        ' 「管理人が〇〇さんに替わりました」が「退室しました」より先に出ないように。
        ' 断った場合も、ここより上で戻っているが、積んだものは次に配る人が配る
        FlushRoomNotices()

        ' (c) 切断中の本人が別の部屋へ移った場合、元の部屋からは退室として処理する
        '     （通常の退室と同じ扱いのため、種別はLeave）
        If hasLeaveTarget Then
            RemoveParticipantAndNotify(leaveRoomId, leaveParticipant, ChatMessageType.Leave)
        End If

        ' このSignalR接続を、部屋単位のグループに追加する
        ' （以降、このグループ宛のメッセージだけがこの接続に届くようになる）
        Await Groups.Add(Context.ConnectionId, roomId.ToString())

        ' 「同じ部屋を、同じブラウザの別のタブで開き直した」かどうか
        '
        ' このとき起きているのは、種別33（置き換え）で古いタブが抜け、
        ' すぐに新しいタブが入室する、という一連の動きである。
        ' 33の行は表示しない方針のため（ChatEventTextのコメントを参照）、
        ' その直後の入室(20)だけを出すと「入室しました」が唐突に流れることになる。
        ' 同じ部屋にいる他の人から見れば、その人はずっとそこにいるのだから、
        ' 入室の行も出してはいけない。
        '
        ' 【罠】ここで kickRoomId ではなく roomId だけを見て判断してはいけない。
        ' 別の部屋にいた自分のタブが追い出された場合（部屋Aの古いタブ→部屋Bの新しいタブ）、
        ' 部屋Bにとっては本当に新しい入室のため、行を出す必要がある
        Dim isSameRoomReplacement As Boolean = (hasKickTarget AndAlso kickRoomId = roomId)

        ' 入室のお知らせ（イベント行）の文言
        ' 上記の置き換えに当たる場合はNothingにして、行が出ないようにする
        ' （参加者一覧の更新は文言と関係なく行われるため、一覧は正しく保たれる）
        Dim joinEventText As String = Nothing
        '
        ' 【名前は participant.Nickname から】。引数の nickname ではない。
        ' 同じ部屋での置き換えでは、名前を古いほうから移している（上の「名前も、古いほうから移す」）。
        ' 引数を配ると、そこで直した名前がほかの人の画面にだけ漏れる
        If Not isSameRoomReplacement Then
            joinEventText = ChatEventText.Build(ChatMessageType.Join, participant.Nickname)
        End If

        If isTakeOver Then

            ' (b) 引き継ぎの場合、誰にも配信しない。
            '     他の参加者から見ると何も変わっていないため（入室し直したように見えてしまう）。
            '     本人の画面についても、下記のとおり戻り値から自分で一覧へ入れるため、
            '     配信する必要がない
            '
            ' 入室ログ（20）は書かない。新規の入室ではなく、通信が復活しただけのため

        Else

            ' 同じ部屋にいる「自分以外」の全員へ、新しい参加者が入室したことを通知する
            '
            ' 【重要】ここでClients.Group（自分を含む全員）を使ってはいけない。
            ' SignalRでは、Hubメソッドの中で送った放送が、そのHubメソッドの戻り値よりも
            ' 先にクライアントへ届く。そのため自分にも放送してしまうと、クライアントが
            ' 戻り値を受け取って一覧を作り直す際に、先に届いていた自分自身の入室通知を
            ' 消してしまう（実際にこの不具合が起きた）。
            ' 自分自身は、戻り値をもとにクライアント側で一覧へ入れる形にして、
            ' メッセージの到着順に依存しないようにしている
            '
            ' 入室のお知らせ（イベント行）の文言も、あわせて渡す。
            ' クライアントは、受け取った文言をそのまま表示するだけにする
            ' （文言を2か所で管理しないため。詳細はChatEventTextのコメント）
            '
            ' 同じ部屋を別のタブで開き直した場合、joinEventTextはNothingになる。
            ' その場合でも配信そのものは行う。参加者一覧を正しく保つ必要があるため、
            ' 「行を出さない」ことと「配信しない」ことを、混同しないこと
            '
            ' 最後の trip（足した）は、入ってきた人のトリップ（無ければ null）。
            ' 【サーバーからクライアントへの呼び出しは、末尾に足しても壊れない】
            ' ハブのメソッド（クライアント → サーバー）と違い、受け取る側は JS の関数で、
            ' JS は余った引数を黙って捨てる。発行の時点で開いていた古いページも、そのまま動く
            '
            ' 最後の joinSeqは、入室した順の番号。
            ' 同じ部屋での置き換えなら、元の位置の番号が届く（画面は、その位置へ入れ直す）
            '
            ' 最後の accountName（足した）は、入ってきた人のアカウントのユーザー名
            ' （アカウントID を出していない人は null）。trip とは、どちらか一方。
            ' **trip も accountName も、participant に入れた値を配る**（同じ部屋での置き換えでは、古いほうから移した値になる）
            Clients.OthersInGroup(roomId.ToString()).UserJoined(
                localId, participant.Nickname, iconType, iconColor, joinEventText,
                AnonId.ComputeFromString(roomId, browserId), participant.Trip, participant.JoinSeq,
                participant.AccountName)

            ' 入室ログを残す（配信のあとに書き込む。DBの遅延を表示に波及させないため）
            ChatLog.Write(roomId, ChatMessageType.Join, participant)

        End If

        ' 呼び出し元（自分自身）に、発行したLocalIdと、既存の参加者一覧を返す
        '
        ' 自分の入室行の文言も、あわせて返す。UserJoinedは自分には届かないため、
        ' 自分の入室行だけは、クライアントがこの値をもとに自前で描画する。
        '
        ' Nothingを返す（＝入室行を出さない）のは、次の2つの場合。
        '   ・引き継ぎ(b)      … F5で戻ってきただけ・再接続しただけで、入室ではない
        '   ・同じ部屋の置き換え … 他の人へ配信する文言（joinEventText）と揃えるため。
        '                          自分の画面にだけ「入室しました」が出るのはおかしい
        '
        ' MyAnonId は、自分を無視できないようにするために返す（AnonIdクラスを参照）。
        ' IsContinuation は、無視リストを維持するか消すかの判定
        ' （中身は Not isNewOccupant。理由は JoinRoomResult のコメントにある）
        Return New JoinRoomResult With {
            .MyLocalId = localId,
            .ExistingParticipants = existingParticipants,
            .MyJoinEventText = If(isTakeOver, Nothing, joinEventText),
            .MyAnonId = AnonId.ComputeFromString(roomId, browserId),
            .IsContinuation = Not isNewOccupant,
            .MyTrip = If(participant Is Nothing, trip, participant.Trip),
            .MyAccountName = If(participant Is Nothing, Nothing, participant.AccountName),
            .PendingKnocks = pendingKnocks,
            .MyJoinSeq = If(participant Is Nothing, 0L, participant.JoinSeq),
            .OwnerLocalId = ownerLocalId
        }

    End Function



    ' 入室を断ったときに、利用者を戻す先
    ' 文言と一緒に、行き先もサーバー側が決めて返す
    ' （断った理由を知っているのはサーバーなので、どこへ戻すべきかもサーバーが知っている。
    '   クライアントに文言から察させると、理由を増やすたびにJS側の分岐が増える）
    '
    ' 実体はChatCommonが持っている。過去ログの取り直し（GetLogEntries）も
    ' 同じ2つの行き先を使うため、文字列を2か所に書かないようにしてある
    Private Const LoungeUrl As String = ChatCommon.LoungeUrl

    ' 鍵付きの部屋へ、鍵を通らずに入ろうとしたときの文言
    Private Const PasswordRequiredMessage As String =
        "この部屋に入るには、パスワードが要ります。ラウンジの入室ボタンから入り直してください。"
    Private Const TopPageUrl As String = ChatCommon.TopPageUrl


    ' 「再接続したが、席がもう無かった」ときの文言を組み立てる
    '
    ' 直前にその部屋から抜けた記録（ChatMessages の30番台）を1件だけ引き、
    ' 種別に応じた理由を添える。見つからなければ、理由なしの文言に落とす
    ' （アプリが再起動した場合は、そもそも退室ログが書かれていないため必ずこちらになる）
    '
    ' 【PCが見る文言と、わざと少しだけ変えてある】
    ' 接続が生きていた人には Kicked で
    ' 「〇〇発言がなかったため、退室しました」が届く（末尾はここまで）。
    ' こちらは末尾に「お手数ですが、入り直してください。」を足してある。
    ' **不具合が出たときに、画面の文言だけでどちらの経路を通ったか分かるようにするため**
    ' （開発者の判断。実際にこの日、文言だけでは経路を断定できなかった）
    '
    ' 【フェイルオープン】DBが引けなくても、理由なしの文言でラウンジへ返す。
    ' 断ること自体は、理由が分からなくても正しく行われなければならない
    Private Shared Function BuildSeatLostReason(roomId As Integer, browserId As String) As String

        Dim leaveType As Integer = ChatLogReader.GetLastLeaveType(roomId, browserId)

        Select Case leaveType

            Case CInt(ChatMessageType.Timeout)
                Return GetSilentTimeoutText() &
                       "発言がなかったため、退室しました。お手数ですが、入り直してください。"

            Case CInt(ChatMessageType.Replaced)
                Return "他のタブ・ウィンドウで接続されたため、退室しました。" &
                       "お手数ですが、入り直してください。"

            Case CInt(ChatMessageType.Leave), CInt(ChatMessageType.Disconnected)
                Return "部屋から退室した状態になっています。お手数ですが、入り直してください。"

            ' 管理人に退室させられた・入室禁止にされたとき、通信が切れていて Kicked が届かなかった。
            ' 文言は Kicked と少しだけ変えてある（上のコメントの「どちらの経路を通ったか」を画面で見分けるため）
            Case CInt(ChatMessageType.KickedByOwner)
                Return "接続が切れている間に、管理人により退室させられました。"

            Case CInt(ChatMessageType.BannedByOwner)
                Return "接続が切れている間に、この部屋には入れなくなりました（管理人により入室禁止になりました）。"

            Case Else
                ' 記録が見つからない（アプリの再起動など）／DBが引けなかった
                Return ChatCommon.NotInRoomMessage

        End Select

    End Function


    ' 入室を断るときの戻り値を組み立てる
    '
    ' 断られた場合、MyLocalIdやExistingParticipantsは意味を持たない
    ' （参加者リストにも追加されておらず、グループにも入っていない）
    Private Shared Function Reject(reason As String, redirectUrl As String) As JoinRoomResult

        Return New JoinRoomResult With {
            .RejectReason = reason,
            .RejectRedirectUrl = redirectUrl
        }

    End Function


    ' ブラウザ識別ID（ChatBrowserId Cookie）を、SignalRの接続情報から読む
    '
    ' HttpContext.Currentは使わない。WebSocket接続ではNothingになることがあるため、
    ' SignalR自身が持っているCookieのコレクションから取る
    ' （IPアドレスの取得がGetHttpContext()を使っているのは、取れなくても
    '   ログのIpAddress列がNULLになるだけで済むから。こちらは本人の識別に使うため、
    '   同じ作りにしてはいけない）
    Private Function GetBrowserIdFromCookie() As String

        ' 読めなければNothingが返る。呼び出し側が入室を断る
        Return GetCookieValue("ChatBrowserId")

    End Function


    ' この接続の持ち主が、アカウントID を表示して参加する人かを決める
    '
    ' 【判定は Chat/ChatAccountLink.vb の1か所】ここは、接続の Cookie を3つ読んで渡すだけ。
    ' 部屋（JoinRoom）・ラウンジ（JoinLounge）・ノック（KnockRoom）が、みんなこれを呼ぶ。
    '
    ' 【DB を1行引くことがある。SyncLock の中から呼ばないこと】
    ' 【接続を張った時点の Cookie である】ログアウトしても、繋ぎ直すまでは前のまま（トリップ・名前と同じ）
    Private Function ResolveAccountLink() As ChatAccountLink.LinkResult

        Return ChatAccountLink.Resolve(
            GetCookieValue("ChatBrowserId"),
            GetCookieValue(NameToken.CookieName),
            GetCookieValue(AccountAuth.CookieName))

    End Function


    ' SignalRの接続情報から、Cookieの値を1つ読む（無い・読めなければ Nothing）
    '
    ' 【値は加工せずに返す】ChatBrowserId は LeaveBeacon と突き合わせるため素のまま使い、
    ' ChatTripKey は TripCode.FromCookieValue がエンコードを戻す。
    ' どう戻すかは値ごとに違うので、ここでは触らない
    '
    ' 【接続を張った時点の値である】WebSocket では、ハンドシェイクの要求に付いていた
    ' Cookie のまま固定される（在室中に書き換えても、繋ぎ直すまで変わらない）
    Private Function GetCookieValue(name As String) As String

        Try

            Dim cookie As Microsoft.AspNet.SignalR.Cookie = Nothing

            If Context.Request.Cookies.TryGetValue(name, cookie) AndAlso
               cookie IsNot Nothing Then
                Return cookie.Value
            End If

        Catch ex As Exception
            ' 読めなければNothingを返す
        End Try

        Return Nothing

    End Function


    ' 部屋の状態（入室してよい部屋か・定員は何人か）をDBから読む
    '
    ' 戻り値 … Trueなら問い合わせに成功した（isOpen・maxUsersが有効）。
    '           Falseは「DBへ問い合わせられなかった」＝判定不能を表す
    ' isOpen … その部屋が存在し、かつIsActive = 1（終了していない）ならTrue
    '
    ' 【問い合わせ失敗と、行が無いことを、区別して返している理由】
    ' 呼び出し側で、フェイルオープンとフェイルクローズを逆向きに使い分けるため。
    '   ・DBへ問い合わせられなかった → 通す（一瞬の詰まりで全員が入れなくなるのを避ける）
    '   ・行が無い／IsActive = 0     → 断る（ここを通すと、存在チェックの意味が無くなる）
    ' 両方をまとめて「0を返す」形にすると、この2つが区別できなくなる
    '
    ' 【入室のたびにDBを1回引くことについて】
    ' 主キーでの1行取得であり、ChatRoom.aspx.vbもページ表示のたびに同じ表を引いている。
    ' 定員は管理画面から変更できるようにする予定があるため、あえてキャッシュしていない
    ' （古い定員で判定して「入れるはずなのに入れない」となるほうが厄介）
    '
    ' 【roomType・ownerBrowserId も、この1回でまとめて読む】
    ' どちらも入室の判定には使わないが、メモリへ控えるために要る（ChatHub.Rooms.vb）。
    '   roomType       … 一時部屋かどうか（＝空になったら閉じてよい部屋かどうか）
    '   ownerBrowserId … その部屋の管理人（退室のたびにDBを引かずに済ませるため）
    ' **問い合わせの回数は増えていない。** 同じ行から列を2つ増やして読むだけである
    '
    ' 【hasPassword も、同じ1回で読む】
    ' **ハッシュそのものは読まない**（ハブは照合しない。照合はラウンジの EnterRoom の仕事）。
    ' NULL かどうかだけを、DB の中で 0/1 にして持ってくる
    '
    ' 【isBanned も、同じ1回で読む】このブラウザ（browserId）が、この部屋で入室禁止か。
    ' ChatRoomBans を主キーで1行見る EXISTS を足しただけ（問い合わせの回数は増えない）
    Private Shared Function TryGetRoomState(roomId As Integer,
                                            browserId As String,
                                            ByRef isOpen As Boolean,
                                            ByRef maxUsers As Integer,
                                            ByRef roomType As String,
                                            ByRef ownerBrowserId As Guid,
                                            ByRef hasPassword As Boolean,
                                            ByRef isBanned As Boolean) As Boolean

        isOpen = False
        maxUsers = 0
        roomType = Nothing
        ownerBrowserId = Guid.Empty
        hasPassword = False
        isBanned = False

        Try

            Dim connStr As String =
                ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(
                    "SELECT MaxUsers, IsActive, RoomType, OwnerBrowserId, " &
                    "       CASE WHEN PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword, " & IsBannedSqlColumn & " " &
                    "FROM ChatRooms WHERE RoomId = @RoomId", conn)

                    cmd.Parameters.AddWithValue("@RoomId", roomId)
                    cmd.Parameters.Add("@BanBrowserId", SqlDbType.UniqueIdentifier).Value = BanBrowserParameter(browserId)

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()

                        If reader.Read() Then

                            maxUsers = CInt(reader("MaxUsers"))
                            isOpen = CBool(reader("IsActive"))

                            If Not IsDBNull(reader("RoomType")) Then
                                roomType = CStr(reader("RoomType"))
                            End If

                            ' 固定部屋・まだ管理人が決まっていない部屋では NULL
                            If Not IsDBNull(reader("OwnerBrowserId")) Then
                                ownerBrowserId = CType(reader("OwnerBrowserId"), Guid)
                            End If

                            hasPassword = (Convert.ToInt32(reader("HasPassword")) = 1)
                            isBanned = (Convert.ToInt32(reader("IsBanned")) = 1)

                        End If

                        ' 行が無ければ、isOpenはFalseのまま（＝存在しない部屋）

                    End Using

                End Using
            End Using

            Return True

        Catch ex As Exception

            ' 通す（フェイルオープン）ことは変えないが、黙っては通さない。
            ' それまでは Debug.WriteLine で、本番の Release ビルドでは呼び出しごと消えていた
            ErrorReport.Report(ex, "ChatHub.TryGetRoomState", "部屋 " & roomId)

            Return False

        End Try

    End Function


    ' === 満員で断ったことを、どこまで記録するか ===
    '
    ' 記録そのものは、荒らしの挙動の分析と、定員の調整の材料になる。
    ' ただし満室の部屋を叩き続けられると、そのぶん行が増える。
    ' **荒らし対策の記録が、荒らしの燃料になってしまう**ため、歯止めが要る。
    '
    ' 歯止めは2段構え。
    '   1. 同じ相手（部屋＋BrowserId）の拒否は、この時間に1回だけ記録する
    '   2. その「記録済みの相手」を覚えておく表が膨らまないよう、件数に上限を設ける。
    '      上限に達している間は、新しい相手の拒否も記録しない
    '
    ' 2が要るのは、1だけだとBrowserId（Cookie）を毎回変えられた瞬間に無力になるため。
    ' Cookieの値は利用者が自由に決められるので、相手ごとの判定は原理的に破れる。
    ' そこで「表が埋まっている＝既にたくさん断っている」状態では、記録自体を止める。
    ' 記録が止まっても、断ること自体は変わらない
    ' この2つの値はSiteSettingsへ移した
    ' （SiteSettings.RejectLogCooldown … 既定10分、
    '   SiteSettings.RejectLogTrackingLimit … 既定500件）。
    '
    ' 【罠】下の2つの関数は、SyncLockの中から呼ばれる。
    ' そこでSiteSettingsを読むとDBを待つ間ロックを握り続けることになるため、
    ' 値は呼び出し元がロックの手前で読み、引数で渡す形にしてある

    ' キー："種別|部屋ID|BrowserId"、値：記録した時刻
    '
    ' 【種別をキーに入れてある】（パスワード違い＝41を足した）
    ' 入れないと、満員で断った直後のパスワード違い（またはその逆）が、10分のあいだ記録されない。
    ' 表の件数の上限（RejectLogTrackingLimit）は、種別をまたいで1つで数える
    Private Shared RejectLogged As New Dictionary(Of String, DateTime)


    ' 満員の拒否を記録すべきかどうかを判定し、記録する場合は控えを残す
    '
    ' cooldown・trackingLimit … SiteSettingsの値。呼び出し元がロックの手前で読んで渡す
    '
    ' 【必ずSyncLockの中から呼ぶこと】RejectLoggedはShared変数のため
    '
    ' 【設定を、この中で読まないこと】
    ' ロックの中にいるため、ここでSiteSettingsを読むと、キャッシュが空のときに
    ' DBを待つ間ロックを握り続けることになる（＝全部屋のチャットが止まる）。
    ' そのため引数で受け取る形にしてある
    Private Shared Function ShouldLogJoinReject(messageType As ChatMessageType,
                                                roomId As Integer,
                                                browserId As String,
                                                cooldown As TimeSpan,
                                                trackingLimit As Integer) As Boolean

        Dim key As String = CInt(messageType).ToString() & "|" & roomId.ToString() & "|" & browserId
        Dim now As DateTime = DateTime.Now

        Dim lastLoggedAt As DateTime

        If RejectLogged.TryGetValue(key, lastLoggedAt) Then

            If now - lastLoggedAt < cooldown Then
                ' 同じ相手を、つい先ほど記録したばかり
                Return False
            End If

        ElseIf RejectLogged.Count >= trackingLimit Then

            ' 表が埋まっている。新しい相手は記録しない（上の2段目の歯止め）
            Return False

        End If

        RejectLogged(key) = now
        Return True

    End Function


    ' 期限切れの「記録済みの相手」を、表から取り除く
    ' CheckTimeoutParticipants（Global.asaxのタイマー）から呼ばれる
    '
    ' cooldown … SiteSettingsの値。呼び出し元がロックの手前で読んで渡す
    '            （理由はShouldLogJoinRejectのコメントと同じ）
    '
    ' 【必ずSyncLockの中から呼ぶこと】
    Private Shared Sub CleanUpRejectLog(now As DateTime, cooldown As TimeSpan)

        If RejectLogged.Count = 0 Then
            Return
        End If

        Dim expired = RejectLogged.Where(
            Function(entry) now - entry.Value >= cooldown).Select(
            Function(entry) entry.Key).ToList()

        For Each key In expired
            RejectLogged.Remove(key)
        Next

    End Sub



    ' === 発言（SendMessage）の検証に使う、3つの線引き ===
    '
    ' 【値はSiteSettingsが持つ。ここには置かない】
    ' 文字数の上限・連投の間隔・同一発言の幅の3つとも
    ' SiteSettings（＝DBのSiteSettingsテーブル）へ移した。
    ' あわせてChatHub.MaxMessageLengthという中継のプロパティも外している。
    ' 中継を残すと「ChatHubを経由するもの」と「SiteSettingsを直接読むもの」が混ざり、
    ' 次に値を足す人が毎回どちらか迷うことになるため。
    ' 画面（ChatRoom.aspx.vb）も、ここを通さずSiteSettingsを直接読んでいる
    '
    ' 既定値・丸めの範囲・それぞれの噛み合わせは、SiteSettings.vbのコメントにある

    ' 3つ以上続く改行を、2つ（＝空行1つ）にまとめるための正規表現
    ' 毎回組み立てずに済むよう、Sharedで1つだけ持つ（Regexはスレッドセーフ）
    Private Shared ReadOnly ExcessNewLines As New Regex("\n{3,}")


    ' クライアントから、メッセージ送信時に呼ばれる
    '
    ' ニックネーム・LocalId・部屋IDは、いずれもクライアントから送らせない。
    ' サーバー側が管理しているParticipant情報（ConnectionIdから特定）だけを、
    ' 信頼できる情報源として使う
    '
    ' 【なぜ、roomIdを引数から外したのか】
    ' 以前はクライアントから受け取った値を、そのままClients.Groupへ渡していた。
    ' ハブを直接呼べば、入室していない部屋のグループへ発言を投げ込めることになる
    ' （Lounge.aspx.vbの入室チェックも、ChatRoom.aspx.vbのSessionのガードも、
    '   まるごと迂回できる）。
    ' 「受け取ってから突き合わせて弾く」のではなく「そもそも受け取らない」形にする。
    ' BrowserIdを引数から外してCookieから読むようにしたのと、同じ考え方である
    '
    ' 【なぜ、戻り値を返すのか】
    ' 以前はSubで、断る口がまったく無かった。断ったことを伝えられないと、
    ' 利用者から見て「送信ボタンを押しても、何も起きない」状態になる。
    ' 詳細はSendMessageResultのコメント
    Public Function SendMessage(message As String) As SendMessageResult

        ' 宛先を指定しない＝部屋あて。判定も配信もログも、こっそりと同じ道を通る
        Return SendMessageCore(message, Nothing)

    End Function


    ' クライアントから、こっそりの発言を送るときに呼ばれる
    '
    ' recipientAnonId … 宛先の匿名ID（参加者一覧や、届いた行に付いている印）
    '
    ' 【なぜ、SendMessage に引数を足さなかったのか】
    ' SignalR 2.4.3 は、ハブのメソッドを**名前と引数の個数**で解決する。
    ' 引数を1つ増やすと、発行の直後にまだ古いページを開いている人の
    ' 「引数1個の SendMessage」が、その場で失敗するようになる
    ' （VBの Optional でも吸収できないことは、実測済み）。
    ' メソッドを別に立てれば、古いページの発言は今までどおり通り続ける
    '
    ' 【なぜ、宛先はクライアントから受け取ってよいのか】
    ' roomId や browserId を受け取るのをやめたのは、**他人を害せるから**だった
    ' （別の部屋へ投げ込める／他人を追い出せる）。宛先は性質が違う——
    ' 嘘をついても、届く先が変わるだけで、届く相手は必ず**同じ部屋の在室者**である。
    ' そのことを、下の ResolveRecipient がサーバー側で確かめている
    '
    ' 【なぜ、匿名IDで指定するのか】
    ' 生のBrowserIdはクライアントへ渡していない（渡せばなりすませる）。
    ' LocalIdは入室のたびに変わるので、過去ログの行と結び付けられない。
    ' 匿名IDは、参加者一覧にも・リアルタイムの行にも・過去ログの行にも
    ' 同じ値が付いている、ただ1つの共通の鍵である
    Public Function SendPrivateMessage(recipientAnonId As String,
                                       message As String) As SendMessageResult

        Return SendMessageCore(message, If(recipientAnonId, ""))

    End Function


    ' 発言の受け付けを、部屋あてとこっそりで共通に行う
    '
    ' recipientAnonId … Nothing なら部屋あて。文字列なら、こっそりの宛先の匿名ID
    '
    ' 【判定は1か所に寄せ、変えるのは配信先だけ】
    ' 文字数・連投・直前と同じ内容の3つは、どちらの経路でもまったく同じ規則で見る。
    ' 2つに分けて書くと、片方だけ直して食い違う（開発者の指定でもある）
    Private Function SendMessageCore(message As String,
                                     recipientAnonId As String) As SendMessageResult

        ' 誰が送ろうとしているのかを、接続IDから引く
        '
        ' 【ここだけがハブ（接続）に依存する。以降は共通の関所へ渡す】
        ' 画像は別の経路（Chat/UploadImage.aspx）から来るので、接続IDを持たない。
        ' 「誰か」を引く手立てだけが違い、そのあとの判定・配信・記録はまったく同じである
        Dim roomId As Integer = 0
        Dim participant As Participant = FindParticipant(Context.ConnectionId, roomId)

        ' 参加者が見つからない（JoinRoomより前に呼ばれた、既に退室している、
        ' サーバーが再起動した、など）。画面の操作では起こらないため、
        ' 利用者に見せる文言は無い
        If participant Is Nothing Then
            Return NotAccepted(Nothing)
        End If

        Return AcceptAndDeliver(participant, roomId, NormalizeMessage(message),
                                recipientAnonId, Nothing)

    End Function


    ' クライアントから、スタンプを送るときに呼ばれる
    '
    ' recipientAnonId … Nothing（JSの null）なら部屋あて。文字列なら、こっそりの宛先の匿名ID
    ' stampCode       … スタンプの名前（例: frog-01）。**本文ではない**
    '
    ' 【本文（[stamp:frog-01]）は、ここで組み立てる】
    ' 利用者に組み立てさせると、止めたスタンプも、目次に無い名前も送れてしまう。
    ' 受け取るのは名前だけにして、今パネルから送ってよいもの（StampCatalog.IsSendable）
    ' のときだけ、印をこちらで作る。画像の印を受け口が組み立てるのと、同じ形である
    '
    ' 【新しいメソッドを足すのは、破壊的変更ではない】
    ' SignalR は名前と引数の個数で解決するので、困るのは「既にあるメソッドの
    ' 引数を増やす」ときだけ。開いたままの古いページは、そもそもこれを呼ばない
    '
    ' 【宛先の扱いは、SendPrivateMessage とまったく同じ】
    ' 宛先はクライアントに名乗らせてよい値である（届く相手は必ず同じ部屋の在室者。
    ' そのことは AcceptAndDeliver の中の ResolveRecipient が確かめる）
    Public Function SendStamp(recipientAnonId As String,
                              stampCode As String) As SendMessageResult

        ' 【SyncLock の外で呼ぶこと】
        ' 目次のキャッシュが空のときは、DBへ問い合わせに行く。
        ' AcceptAndDeliver の鍵を握る前に済ませておく（SiteSettings を先に読むのと同じ理由）
        If Not StampCatalog.IsSendable(stampCode) Then

            ' 画面の操作で起こるのは、止めたばかりのスタンプを古い目次から押した場合だけ。
            ' 利用者にとっては正常な結果なので、理由を出して伝える
            Return NotAccepted("このスタンプは、今は送れません。")
        End If

        Dim roomId As Integer = 0
        Dim participant As Participant = FindParticipant(Context.ConnectionId, roomId)

        ' SendMessageCore と同じ。画面の操作では起こらないので、理由は返さない
        If participant Is Nothing Then
            Return NotAccepted(Nothing)
        End If

        Return AcceptAndDeliver(participant, roomId, StampCatalog.BuildToken(stampCode),
                                recipientAnonId, Nothing, isStamp:=True)

    End Function


    ' ============================================================
    ' 発言を受け付けて、配信し、記録する（すべての発言が、必ずここを通る）
    ' ============================================================
    '
    ' participant     … 送る人（呼び出し側が既に引き当ててある）
    ' roomId          … その人が今いる部屋
    ' text            … 本文。正規化まで済ませたもの
    ' recipientAnonId … Nothing なら部屋あて。文字列なら、こっそりの宛先の匿名ID
    ' imageName       … 画像の発言なら、その画像の名前。文章なら Nothing
    ' isStamp         … スタンプの発言（本文がサーバーの組み立てた [stamp:…]）なら True
    '
    ' 【入口は4つ、関所は1つ】
    '
    '   SendMessage         … 部屋あての文章（ハブ）
    '   SendPrivateMessage  … こっそりの文章（ハブ）
    '   SendStamp           … スタンプ（ハブ）
    '   SendImage           … 画像（Chat/UploadImage.aspx から。下の Shared メソッド）
    '
    ' 文字数・連投・直前と同じ内容の3つは、どの入口から来てもまったく同じ規則で見る。
    ' 分けて書くと、片方だけ直して食い違う（開発者の指定でもある）。
    ' **例外は1つだけ**——スタンプは「直前と同じ内容」で断らない（下の 6 のコメント）。
    '
    ' 【Shared にしてあること】
    ' 画像の受け口はページであって、ハブの接続を持たない。
    ' そのため配信は Clients ではなく GetHubContext を通す
    ' （RemoveParticipantAndNotify が既にそうしているのと、同じ形）。
    ' Clients.Caller に当たるものは Clients.Client(participant.ConnectionId) で、
    ' ハブから呼ばれたときは**まったく同じ接続**を指す
    ' （participant は、その ConnectionId で引き当てたものだから）
    '
    ' 【参加者を引くのを、ロックの外へ出したこと】
    ' 以前はロックの中で引いていたが、引いたあとに退室された場合の振る舞いは変わらない
    ' （ロックを抜けてから配信するので、どちらにせよ「今しがた出た人の発言が流れる」
    '   ことは起こりうる）。守りたいのは時計と控えの読み書きが割り込まれないことで、
    ' そちらは今までどおり1つのSyncLockの中にある
    Friend Shared Function AcceptAndDeliver(participant As Participant,
                                            roomId As Integer,
                                            text As String,
                                            recipientAnonId As String,
                                            imageName As String,
                                            Optional isStamp As Boolean = False) As SendMessageResult

        ' 部屋あてか、こっそりか
        Dim isPrivate As Boolean = (recipientAnonId IsNot Nothing)

        ' 画像の発言か（＝本文が、サーバーの組み立てた印そのものか）
        Dim isImage As Boolean = (imageName IsNot Nothing)

        ' 本文がサーバーの組み立てた印（画像かスタンプ）か。
        ' 印を手で打ち込ませない判定（下の 3）は、これが False のときだけ掛ける
        Dim isBuiltToken As Boolean = isImage OrElse isStamp

        Dim now As DateTime = DateTime.Now

        ' 3つの線引きは、いずれもロックを取る前に読んでおく
        '
        ' 【罠】ここをSyncLockの中で読んではいけない。
        ' SiteSettingsは、キャッシュが空のときDBへ問い合わせに行く。
        ' SyncRootを握ったままDBを待つと、その間
        ' 全部屋のチャットが止まる（満員拒否の記録をロックの外へ出したのと同じ理由）
        Dim maxLength As Integer = SiteSettings.MaxMessageLength
        Dim messageInterval As TimeSpan = SiteSettings.MessageInterval
        Dim duplicateWindow As TimeSpan = SiteSettings.DuplicateWindow

        Dim rejectReason As String = Nothing

        ' こっそりの宛先（部屋あてのときは、最後までNothingのまま）
        '
        ' 【SyncLockの外で宣言しておくこと】
        ' 値を決めるのはロックの中だが、配信とログを組み立てるのは
        ' ロックを抜けたあとである（isNewOccupant を外へ出したのと同じ事情）
        Dim recipient As Participant = Nothing

        ' === 判定と、状態の更新を、ひとつのSyncLockの中でまとめて行う ===
        '
        ' 【なぜ、分けてはいけないのか】
        ' SignalRは、同じ接続から呼ばれたハブのメソッドを直列化しない。
        ' つまり同じ人の発言が、別々のスレッドで同時に走ることがある。
        ' 「読む → 判定する → 書く」を分けると、ほぼ同時に来た2発言が
        ' どちらも「まだ前の発言は無い」と読んでしまい、両方とも通り抜ける
        ' （＝レート制限も、連続同一発言の禁止も、素通りする）
        SyncLock SyncRoot

            ' === 1. こっそりの宛先を、サーバー側で確かめる ===
            '
            ' 【必ず、同じ部屋の在室者に限ること】
            ' ここを緩めると、ハブを直接叩いて別の部屋の人へ送れるようになる
            ' （roomId を引数から外したのと、まったく同じ話である）。
            ' 探す範囲を「この人が今いる部屋の参加者」に限っているので、
            ' 宛先の言い値がどうであれ、部屋の外へは出ない
            '
            ' 【自分自身へは送れない】
            ' 送れても意味が無いうえ、窓が自分あてに1つ増えて混乱するだけである
            If isPrivate Then

                recipient = ResolveRecipient(roomId, recipientAnonId)

                If recipient Is Nothing Then

                    ' 相手が退室した直後などに起こりうる。
                    ' 利用者にとっては正常な結果なので、理由を出して伝える
                    Return NotAccepted("相手が見つかりません。退室したかもしれません。")
                End If

                If recipient.LocalId = participant.LocalId Then

                    ' 画面の操作では起こらない（自分は選べても押せない形にしてある）。
                    ' ここへ来るのはハブを直接叩いた場合だけなので、黙って捨てる
                    Return NotAccepted(Nothing)
                End If

            End If

            ' === 2. 空の発言 ===
            '
            ' 正規化した結果、本文が空になった（空文字・空白・改行だけだった）。
            ' 画面側も空では送らないため、ここへ来るのはハブを直接叩いた場合だけ。
            ' 断る理由を見せる相手がいないので、黙って捨てる
            If text.Length = 0 Then
                Return NotAccepted(Nothing)
            End If

            ' この発言の「宛先」を表す鍵。直前と同じ内容かの判定に使う
            ' （部屋あてとこっそり、そしてこっそりどうしも、別々に見る）
            Dim destinationKey As String = BuildDestinationKey(recipient)

            ' === 3. 画像の印を、手で打ち込ませない ===
            '
            ' 画像の発言は、**利用者に組み立てさせない。サーバーが作る。**
            ' 保存に成功した受け口だけが、その場で印を組み立てて SendImage を呼ぶ。
            '
            ' 【これが無いと、どうなるか】
            ' 誰でも他人の画像の名前を打てば、それを自分の発言として貼れる。
            ' こっそりの画像は認可で守られるが、**部屋の画像は静的ファイルなので
            ' そのまま出てしまう**。
            '
            ' 【文言を出して断ること】
            ' 「[img:」で始まる文章を普通に打つ人は、まず居ない。
            ' それでも黙って捨てると、打った本人には何が起きたのか分からない
            If Not isBuiltToken AndAlso SiteImage.ContainsToken(text) Then

                rejectReason = "画像は、画像のボタンから送ってください。"

            ElseIf Not isBuiltToken AndAlso StampCatalog.ContainsToken(text) Then

                ' スタンプの印も、画像とまったく同じ理由で手打ちを断る
                ' （パネルを通らずに送れると、止めたスタンプも送れてしまう）
                rejectReason = "スタンプは、スタンプの一覧から送ってください。"

            ElseIf text.Length > maxLength Then

                ' === 4. 文字数の上限 ===
                '
                ' 判定は正規化後の長さで行う。正規化は文字を減らす方向にしか働かないため、
                ' 画面側が数えている生の長さより、必ず短いか同じになる。
                ' つまり「画面では入力できたのに、サーバーに弾かれる」は起こらない。
                '
                ' 【画像の印は、この上限にぶつからない】
                ' 印はいちばん長くても50文字ほどで、上限は最低でも1文字より大きい……
                ' ではなく、MaxMessageLength の下限は1である。
                ' 万一そこまで絞られていても、画像だけが通ってしまうのは筋が通らないので、
                ' 画像も同じ上限で見ている
                rejectReason = maxLength.ToString() & "文字を超えています。短くしてください。"

            ElseIf participant.LastMessageAt.HasValue AndAlso
                   now - participant.LastMessageAt.Value < messageInterval Then

                ' === 5. 連投（前の発言からの間隔が短すぎる） ===
                '
                ' 【画像も、同じ時計で見る】（開発者の指定）
                ' 画像だけ別の入口を通るので、何もしないとこの判定を素通りする。
                ' 画像は文章よりずっと重いので、むしろ効かせたい側である
                rejectReason = "送信の間隔が短すぎます。少し間を空けてください。"

            ElseIf Not isStamp AndAlso
                   IsSameAsLastText(participant, destinationKey, text, now, duplicateWindow) Then

                ' === 6. 直前と、まったく同じ内容 ===
                '
                ' 【この判定だけは、宛先ごとに持つ】
                ' 1つにすると、Aさんに「こんにちは」と送ったあと、
                ' Bさんに同じ挨拶が送れない。平行して複数の人と話すのは、
                ' こっそりの使い方そのものである。
                ' 連投の間隔（上の5）は、1本の時計のままでよい——
                ' あちらは「打つ速さ」の話で、宛先が変わっても速さは変わらない
                '
                ' 【画像がここで断られることは、まず無い】
                ' 名前がGUIDなので、同じ画像を2回送っても本文は別物になる。
                ' それでよい——同じ写真をもう一度送るのは、正当な操作である
                '
                ' 【スタンプは、この判定の対象外】（開発者の判断）
                ' この判定は、コピペの誤爆や荒らしの貼り付けを止めるためのものである。
                ' スタンプは同じものを続けて押すのが普通の使い方（笑い・相づち）で、
                ' 同じ理屈を当てると使いにくくなるだけ。
                ' **連投の間隔（上の 5）は、スタンプにもそのまま効く**——押しっぱなしの
                ' 連打を止めるのはあちらの役目で、画面側もその間はボタンを休ませている。
                ' 受け付けたスタンプも、下で本文の控えは更新する（「直前」の意味を保つため。
                ' 文章 → スタンプ → 同じ文章、は「直前と同じ」ではない）
                rejectReason = "直前と同じ内容です。少し時間を空けてから送信してください。"

            End If

            If rejectReason IsNot Nothing Then

                ' === 断った発言では、時計を一切進めない ===
                '
                ' 【罠】ここでLastMessageAtを更新してはいけない。
                ' 更新すると、断られるたびに3秒の待ち時間が引き延ばされ、
                ' 送信を連打している人ほど、いつまでも発言できなくなる
                ' （待ち時間の起点は、あくまで「最後に部屋へ流れた発言」である）。
                '
                ' LastActiveAt（無言タイムアウトの時計）も更新しない。
                ' 連投で断られ続けている間も、無言タイムアウトは進み続ける
                Return NotAccepted(rejectReason)

            End If

            ' 受け付けた。ここではじめて、時計と控えを進める
            '
            ' LastMessageAt（連投の時計）は宛先を問わず1本、
            ' 本文の控えは宛先ごと、という使い分けをそのまま書いてある
            participant.LastActiveAt = now
            participant.LastMessageAt = now

            participant.LastTextByDestination(destinationKey) =
                New LastAcceptedText With {.Text = text, .At = now}

        End SyncLock

        ' === 配信とDBへの書き込みは、必ずSyncLockを抜けてから ===
        ' （ロックの中にDBを持ち込まない。その間、部屋全体が止まってしまうため）

        ' 【Shared なので、Clients ではなく GetHubContext を通す】
        ' ハブから来ても、画像の受け口から来ても、同じ形で配信できる
        Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()

        Dim senderAnonId As String = AnonId.ComputeFromString(roomId, participant.BrowserId)

        If Not isPrivate Then

            ' === 部屋あて ===
            '
            ' 同じ部屋のグループにだけ配信する（Clients.Allは使わない）。
            ' 部屋IDは、クライアントの言い値ではなくConnectionRoomsから引いたものを使う
            '
            ' 最後の引数は送り主のトリップ（足した。無ければ null）。
            ' 末尾に足しても、古いページは余った引数を捨てるだけで壊れない
            ' （JoinRoom の UserJoined のコメントを参照）
            '
            ' そのあとの引数は送り主のアカウントのユーザー名（足した。
            ' アカウントID を出していない人は null。trip とは、どちらか一方）
            hubContext.Clients.Group(roomId.ToString()).ReceiveMessage(
                participant.LocalId, participant.Nickname, text,
                participant.IconType, participant.IconColor, senderAnonId,
                participant.Trip, participant.AccountName)

            ' 発言ログを残す（配信のあとに書き込む。DBの遅延を表示に波及させないため）
            ChatLog.Write(roomId, ChatMessageType.Message, participant, text,
                          imageName:=imageName)

            Return New SendMessageResult With {.Accepted = True}

        End If

        ' === こっそり ===
        '
        ' 【配信先は、相手と自分の2人だけ】
        ' グループへは絶対に流さないこと。1文字でもここを間違えると、
        ' 部屋の全員にこっそりが配られる
        Dim recipientAnonIdValue As String =
            AnonId.ComputeFromString(roomId, recipient.BrowserId)

        ' 相手へ。
        '
        ' 【接続が切れている相手には送らない】
        ' 在室していても IsConnected が False（スマホの画面消灯など）のことがあり、
        ' そのとき ConnectionId は Nothing である。空の接続IDを Clients.Client へ渡すと
        ' 例外になり、**そのあとのログ書き込みまで巻き添えで止まる**
        ' （無言タイムアウトで Kicked を送らないのと、まったく同じ用心）。
        ' 送れなくても、相手が戻ってきたときに過去ログとして必ず出る
        '
        ' 最後の引数は送り主のトリップ（部屋あての ReceiveMessage と同じく末尾に足した）
        If Not String.IsNullOrEmpty(recipient.ConnectionId) Then

            hubContext.Clients.Client(recipient.ConnectionId).ReceivePrivateMessage(
                participant.LocalId, participant.Nickname, text,
                participant.IconType, participant.IconColor,
                senderAnonId, recipientAnonIdValue, recipient.Nickname,
                participant.Trip, participant.AccountName)

        End If

        ' 自分へ。
        '
        ' 【自分にも配信すること】
        ' 部屋あての発言は Clients.Group に自分も含まれるので、送った本人の画面にも
        ' 同じ経路で返ってくる。こっそりも同じ形に揃えておくと、
        ' 「画面に出す」処理がクライアント側で1つで済む
        ' （送信時にその場で描く形にすると、時刻の作り方が2通りになる）
        '
        ' 【自分の接続も、切れていることがある】
        ' 画像の受け口から来た場合、送った本人がちょうど切断中ということが
        ' 原理的には起こりうる（アップロードの往復のあいだに凍結した、など）。
        ' 相手側と同じ用心をしておけば、そこで例外になって記録まで失うことがない
        If Not String.IsNullOrEmpty(participant.ConnectionId) Then

            hubContext.Clients.Client(participant.ConnectionId).ReceivePrivateMessage(
                participant.LocalId, participant.Nickname, text,
                participant.IconType, participant.IconColor,
                senderAnonId, recipientAnonIdValue, recipient.Nickname,
                participant.Trip, participant.AccountName)

        End If

        ' ログを残す。種別11と、宛先の印を添える
        ChatLog.Write(roomId, ChatMessageType.PrivateMessage, participant, text,
                      recipient.BrowserId, imageName)

        Return New SendMessageResult With {.Accepted = True}

    End Function


    ' ============================================================
    ' 画像の発言を、受け付けて配信する（Chat/UploadImage.aspx から呼ばれる）
    ' ============================================================
    '
    ' browserId       … 送る人の ChatBrowserId Cookie の値
    ' imageName       … 保存済みの画像の名前（SiteImage.IsValidName を通したもの）
    ' width / height  … 画面が数えた寸法（場所を確保するためだけに使う）
    ' recipientAnonId … Nothing なら部屋あて。文字列なら、こっそりの宛先
    '
    ' 【なぜ、ページからハブの Shared メソッドを呼ぶのか】
    ' 既にある形である。LeaveBeacon.aspx.vb が LeaveByConnectionId を、
    ' ChatLogout.vb が LeaveByBrowserId を、まったく同じ形で呼んでいる
    '
    ' 【なぜ、ハブのメソッドを1つ足す形にしなかったのか】
    ' 画像の中身はHTTPで送るしかない（SignalRで数MBのバイト列は流さない）。
    ' 受け口が既にHTTPなのだから、そこから続けて配信まで済ませるほうが、
    ' 往復が1回で済む。ハブのメソッドを足すと**破壊的変更**にもなる
    ' （SignalRは名前と引数の個数で解決するため）
    Public Shared Function SendImage(browserId As String,
                                     imageName As String,
                                     width As Integer,
                                     height As Integer,
                                     recipientAnonId As String) As SendMessageResult

        ' 【名前は、必ずここでも確かめること】
        ' 呼び出し側でも確かめているが、この関数は Public である。
        ' 印を組み立てる手前に壁を置いておけば、将来べつの呼び出し側が
        ' 増えたときも、形の壊れた名前が本文へ入らない
        If Not SiteImage.IsValidName(imageName) Then
            Return NotAccepted(Nothing)
        End If

        Dim roomId As Integer = 0
        Dim participant As Participant = FindParticipantByBrowserId(browserId, roomId)

        If participant Is Nothing Then

            ' 部屋に居ない（退室した・タイムアウトした・サーバーが再起動した）。
            ' アップロードの往復のあいだに起こりうるので、理由を出して伝える
            Return NotAccepted("部屋に入っていない状態になりました。お手数ですが、入り直してください。")
        End If

        Dim text As String = SiteImage.BuildToken(imageName,
                                                  SiteImage.ClampEdge(width),
                                                  SiteImage.ClampEdge(height))

        Return AcceptAndDeliver(participant, roomId, text, recipientAnonId, imageName)

    End Function


    ' ブラウザの印から、その人と、今いる部屋を引く
    '
    ' 【FindParticipant（接続IDから引く）と、対になっている】
    ' あちらはハブの中から、こちらはページから使う。
    ' 画像の受け口はハブの接続を持たないので、Cookie の値で引くしかない
    '
    ' 【必ず Guid に直してから比べること】
    ' 参加者が持っているのはCookieの生の文字列（JoinRoomが加工せずに保存している）で、
    ' 大文字・小文字や波かっこで表記が揺れうる。文字列のまま比べると、
    ' **同じブラウザなのに一致せず、しかもエラーは出ない**
    ' （TryGetRoomIdByBrowserId が、まったく同じ用心をしている）
    Private Shared Function FindParticipantByBrowserId(browserId As String,
                                                       ByRef roomId As Integer) As Participant

        roomId = 0

        Dim target As Guid

        If Not Guid.TryParse(browserId, target) Then
            Return Nothing
        End If

        SyncLock SyncRoot

            For Each roomEntry In RoomParticipants

                For Each p In roomEntry.Value

                    Dim stored As Guid

                    If Guid.TryParse(p.BrowserId, stored) AndAlso stored = target Then
                        roomId = roomEntry.Key
                        Return p
                    End If

                Next

            Next

        End SyncLock

        Return Nothing

    End Function


    ' こっそりの宛先を、匿名IDから引き当てる
    '
    ' 【探す範囲を、その部屋の在室者に限っていることが要点である】
    ' 全部屋から探す形にすると、匿名IDを1つ手に入れるだけで
    ' 別の部屋の人へ送れるようになる。匿名IDには roomId が混ぜてあるので
    ' 他室の印は当たらないはずだが、**「当たらないはず」に頼らない**
    '
    ' 【呼び出し側が SyncLock を握っていること】
    ' 参加者リストを走査するため。VBのSyncLockは同じスレッドからの入れ子を許すので、
    ' ロックの中から呼んで問題ない
    Private Shared Function ResolveRecipient(roomId As Integer,
                                             recipientAnonId As String) As Participant

        If String.IsNullOrEmpty(recipientAnonId) Then
            Return Nothing
        End If

        Dim list As List(Of Participant) = Nothing

        If Not RoomParticipants.TryGetValue(roomId, list) Then
            Return Nothing
        End If

        ' 【印は覚えず、その場で計算し直す】AnonIdクラスの方針そのままである。
        ' 部屋の人数ぶんのハッシュ計算で、定員20人なら一瞬で終わる
        For Each p As Participant In list

            If String.Equals(AnonId.ComputeFromString(roomId, p.BrowserId),
                             recipientAnonId, StringComparison.Ordinal) Then
                Return p
            End If

        Next

        Return Nothing

    End Function


    ' 「直前と同じ内容か」の判定に使う、宛先ごとの鍵を作る
    '
    ' 部屋あて（recipient が Nothing）は空文字。
    ' こっそりは、宛先のBrowserIdを決まった書式へ直したもの
    '
    ' 【必ず書式を揃えること】
    ' 参加者が持っているのはCookieの生の文字列で、"{A1B2...}" と "a1b2..." のように
    ' 表記が揺れうる。生のまま鍵にすると、同じ相手に別々の控えができ、
    ' 「同じ内容を2回送れてしまう」形で静かに緩む（AnonIdが ToString("N") で
    ' 揃えているのと、まったく同じ理由）
    '
    ' 【名前が Build で始まっているのは、書き間違いではない】
    ' VBは大文字・小文字を区別しないため、DestinationKey という名前にすると
    ' 呼び出し側の Dim destinationKey がこの関数を隠してしまう
    ' （Page_Load の roomId が RoomId を隠した件と、まったく同じ罠。
    '   Option Strict Off なので、運が悪ければ警告も出ずに通る）
    Private Shared Function BuildDestinationKey(recipient As Participant) As String

        If recipient Is Nothing Then
            Return ""
        End If

        Dim parsed As Guid

        If Not String.IsNullOrEmpty(recipient.BrowserId) AndAlso
           Guid.TryParse(recipient.BrowserId, parsed) Then
            Return parsed.ToString("N")
        End If

        ' Guidとして読めない相手（通常は起こらない。入室時に検査しているため）。
        ' 鍵を作れないので、LocalIdで代用する
        Return "local:" & recipient.LocalId.ToString(
            Globalization.CultureInfo.InvariantCulture)

    End Function


    ' その宛先へ、直近 duplicateWindow のあいだに、まったく同じ本文を送っていたか
    '
    ' 【時刻も宛先ごとに見ること】
    ' 共通の LastMessageAt で見ると、別の宛先へ送った発言が窓を延長して、
    ' 関係のない宛先で「直前と同じ内容です」と断られる
    Private Shared Function IsSameAsLastText(participant As Participant,
                                             destinationKey As String,
                                             text As String,
                                             now As DateTime,
                                             duplicateWindow As TimeSpan) As Boolean

        Dim last As LastAcceptedText = Nothing

        If Not participant.LastTextByDestination.TryGetValue(destinationKey, last) Then
            Return False
        End If

        If now - last.At >= duplicateWindow Then
            Return False
        End If

        ' 大文字・小文字を区別する比較（Ordinal）にしている。
        ' 「まったく同じ文言か」を見たいのであって、似ているかを見たいのではない
        Return String.Equals(text, last.Text, StringComparison.Ordinal)

    End Function


    ' 発言を受け付けなかったときの戻り値を組み立てる
    ' reasonがNothingなら、利用者に見せる文言が無い（黙って捨てた）ことを表す
    Private Shared Function NotAccepted(reason As String) As SendMessageResult

        Return New SendMessageResult With {
            .Accepted = False,
            .RejectReason = reason
        }

    End Function


    ' 発言の本文を、保存・配信してよい形に整える
    '
    ' 【なぜ、サーバー側にも置くのか】
    ' 同じ処理はChatRoom.aspxのJavaScript側にもあるが、あちらは残り文字数の表示と
    ' 数え方を揃えるための「親切」であって、防御ではない。
    ' ハブを直接呼べば、JavaScriptは1行も動かない。信用の境界はサーバー側にしかない
    '
    ' 処理の順番には意味がある。
    '   1. 改行コードを \n に統一する
    '   2. 3つ以上続く改行を、2つ（＝空行1つ）にまとめる
    '   3. 前後の空白・改行を落とす
    ' 2を先に、3を後に行うのは、まとめた結果として末尾に残る改行まで落とすため
    '
    ' 【罠】\r\n だけを \n に直すのでは足りない。
    ' JavaScript側は \r\n しか見ていないが、ハブを直接呼べば \r だけを並べて送れる。
    ' \r 単体もブラウザでは改行として表示されるため（.message-bubble は
    ' white-space: pre-wrap）、「\r を1000個」で改行をまとめる処理を丸ごと回避できてしまう。
    ' 3つの改行コード（\r\n・\r・\n）を、すべて \n に寄せてから数えること
    Private Shared Function NormalizeMessage(message As String) As String

        If String.IsNullOrEmpty(message) Then
            Return String.Empty
        End If

        Dim text As String = message.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)

        ' 改行だけを大量に送られると、吹き出しが画面を埋め尽くしてしまう。
        ' 段落を分けたい人の意図（空行1つ）は残しつつ、嫌がらせだけを防ぐ線引き
        text = ExcessNewLines.Replace(text, vbLf & vbLf)

        Return text.Trim()

    End Function



    ' クライアントから、退室ボタンが押された時に呼ばれる
    ' OnDisconnected（接続自体が切れた時）と違い、こちらはSignalR接続を維持したまま
    ' 部屋だけを離れる操作のため、Groups.Removeを明示的に呼ぶ必要がある
    Public Async Function LeaveRoom(roomId As Integer) As Task

        ' このSignalR接続を、部屋単位のグループから明示的に外す
        Await Groups.Remove(Context.ConnectionId, roomId.ToString())

        ' 参加者リストからの削除、および残りの参加者への退室通知は、
        ' OnDisconnectedと共通の後片付け処理（RemoveParticipantAndNotify）を流用する
        ' 退室の理由はここでしか分からないため、種別を引数で渡す
        ' （RemoveParticipantAndNotifyは部屋と参加者を直接受け取るため、先に特定しておく）
        Dim foundRoomId As Integer = 0
        Dim participant = FindParticipant(Context.ConnectionId, foundRoomId)

        If participant IsNot Nothing Then
            RemoveParticipantAndNotify(foundRoomId, participant, ChatMessageType.Leave)
        End If

    End Function



    ' SignalRの接続が切断された時に、自動的に呼ばれる
    '
    ' 【重要】ここでは「接続」と「在室」を切り離して扱う。
    ' 引数のstopCalledは、クライアントが意図的に接続を切ったかどうかを表す。
    ' これを見ずに一律で退室扱いにしていると、スマホの画面消灯や電波の途切れといった
    ' 「通信が切れただけ」のケースまで退室になってしまい、利用者はスマホを
    ' ポケットに出し入れするたびに、他の参加者から見て入退室を繰り返すことになる。
    ' 通信の話（接続）と、その部屋にいるかどうかの話（在室）は、別物として扱う
    Public Overrides Function OnDisconnected(stopCalled As Boolean) As Task

        ' ラウンジの接続なら、ラウンジの人数から外す（ChatHub.Lounge.vb）。
        ' ラウンジには「在室」の考え方が無いので、stopCalled に関係なくその場で外す
        ' （ここへ来るのは、SignalR が「もう戻ってこない」と判断したあと）。
        ' 部屋の接続なら何も起きない
        LeaveLounge(Context.ConnectionId)

        If stopCalled Then

            ' タブ・ブラウザを閉じた、ページを離れた＝本人の意思で離脱した。
            '
            ' ただし、ここでも即座には退室させず「退室予告」に留める。
            ' F5でリロードした場合も、ここへ来るためである
            ' （退室ビーコンと同じ猶予に必ず揃えること。片方だけを猶予にしても、
            '   もう片方が即座に退室させてしまい、意味が無くなる）。
            ' 猶予を過ぎたぶんは、CheckTimeoutParticipantsが種別31で退室させる
            Dim roomId As Integer = 0
            Dim participant = FindParticipant(Context.ConnectionId, roomId)

            If participant IsNot Nothing Then
                MarkDisconnectedCore(roomId, participant, True)
            End If

        Else

            ' 通信が切れただけ。退室させず、「今はつながっていない」印を付けるに留める。
            ' 戻ってきたら、JoinRoomがBrowserIdで本人を特定して引き継ぐ
            MarkDisconnected(Context.ConnectionId)

        End If

        Return MyBase.OnDisconnected(stopCalled)

    End Function



    ' 通信が切れただけの接続に、「今はつながっていない」という印を付ける
    '
    ' 参加者リストからは削除しない（＝退室させない）。
    ' 他の参加者への通知もせず、ログも書かない。他の人から見れば、その人はまだ部屋にいる
    Private Shared Sub MarkDisconnected(connectionId As String)

        Dim roomId As Integer = 0
        Dim participant = FindParticipant(connectionId, roomId)

        If participant Is Nothing Then
            ' JoinRoom前の切断など、参加者が特定できない場合は何もしない
            ' （逆引きの登録だけが残っている可能性もあるため、念のため消しておく）
            SyncLock SyncRoot
                ConnectionRooms.Remove(connectionId)
            End SyncLock
            Return
        End If

        ' 通信が切れただけなので、退室予告は付けない
        ' （在室のまま、最終発言から1時間の無言タイムアウトを待つ）
        MarkDisconnectedCore(roomId, participant, False)

    End Sub



    ' 「今はつながっていない」印を付ける、共通の処理
    '
    ' isLeavePending  … Trueなら、あわせて「退室予告」の印（LeavePendingAt）を付ける。
    '                    退室ビーコンが届いた場合や、タブを閉じた場合に使う
    '                    Falseなら、通信が切れただけの扱いにする
    '
    ' どちらの場合も、参加者リストからは削除しない。
    ' 他の参加者への通知もせず、ログも書かない
    ' （退室が確定した時点で、RemoveParticipantAndNotifyがまとめて行う）
    Private Shared Sub MarkDisconnectedCore(roomId As Integer, participant As Participant, isLeavePending As Boolean)

        ' SignalRのグループから外す作業は、ConnectionIdがまだ残っているうちに
        ' 控えておく必要がある（このあとNothingにしてしまうため）
        Dim connectionId As String = participant.ConnectionId

        SyncLock SyncRoot

            participant.IsConnected = False

            ' この接続IDはもう使えないため、参加者から切り離しておく
            ' （戻ってきたときは、BrowserIdで本人を特定する）
            participant.ConnectionId = Nothing

            If isLeavePending Then
                participant.LeavePendingAt = DateTime.Now
            End If

            ' 逆引き用の対応も削除する（この接続IDは、もうどの部屋にも紐づかない）
            If connectionId IsNot Nothing Then
                ConnectionRooms.Remove(connectionId)
            End If

        End SyncLock

        ' 退室予告の場合は、SignalRのグループからも外しておく。
        ' 猶予の間、参加者リストには残り続けるため、古い接続に部屋への放送が
        ' 届き続けないようにするため
        '
        ' SignalRの呼び出しはSyncLockの外で行う（ロックの中に持ち込まない）。
        ' Sharedのため戻り値のTaskは待てないが、待つ必要もない
        If isLeavePending AndAlso connectionId IsNot Nothing Then
            Try
                Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()
                hubContext.Groups.Remove(connectionId, roomId.ToString())
            Catch ex As Exception
                ' 外せなくても、退室予告そのものは成立している
                ' （ErrorReport へは知らせない。接続が消えかけている相手なので、失敗しうる場面である。
                '   RemoveParticipantAndNotify の同じ Catch を参照）
            End Try
        End If

    End Sub



    ' ページが閉じられた際に、LeaveBeacon.aspxから呼ばれる
    '
    ' 引数の意味と、それぞれの役割
    '   connectionId … 「どの接続の話か」の特定に使う（ビーコンを送ったページ自身の接続ID）
    '   browserId    … 「本人かどうか」の確認に使う（ChatBrowserId Cookieの値）
    ' 参加者は、この2つを両方満たすものだけを対象にする
    '
    ' 【罠】BrowserIdだけで参加者を探してはいけない
    '
    ' 以前はBrowserIdだけで探していたが、F5の際に「新しいページのJoinRoom」が
    ' 「古いページのビーコン」を追い越すと、次の順序で別人を掴んでしまう。
    '
    '   1. 新ページのJoinRoomが先に届く。古い参加者はまだIsConnected = Trueのため
    '      (a)と判定され、追い出されて種別33が記録される。続けて新しい参加者が登録される
    '   2. 遅れてビーコンが届く。BrowserIdだけで探すと、もう新しい参加者しか
    '      残っていないため、それを掴んで退室予告を立て、
    '      生きている接続をSignalRのグループから外してしまう
    '   3. 猶予が切れると、種別31で退室が確定し、他の参加者の一覧から消える
    '
    ' このとき、本人の画面には何の変化も起きない。種別31はKickedを送らないため
    ' 通知は出ず、SignalRの接続自体は生きているので接続状態バーも出ない。
    ' 利用者から見ると「画面は正常なのに、自分の発言が誰にも届かず、
    ' 他人の発言も届かない」という状態になる（グループから外されているため）。
    ' 本人にもこちらにも気づく手段が無い、いちばん質の悪い壊れ方であるため、
    ' 「Cookieだけで十分では」と単純化しないこと。
    '
    ' 【なぜBrowserIdの照合も残すのか】
    ' 接続IDだけを鍵にすると、他人の接続IDを送りつけるだけで、その人を部屋から
    ' 追い出せてしまうため。接続IDは「どの接続か」の特定に、Cookieは「本人かどうか」の
    ' 確認に使う。本人確認は、必ずサーバー側で行うこと
    '
    ' 【ここで退室させず、「退室予告」に留める理由】
    ' このビーコンは、タブを閉じたときだけでなく、F5でリロードしたときにも飛んでくる。
    ' サーバー側からは、その2つを区別できない。
    ' その場で退室させると、F5のたびに入室(20)と切断(31)のログが対で書き込まれ、
    ' 他の参加者の画面にも「退室しました／入室しました」が流れてしまう
    ' （本番のログで、発言106件に対してイベント行が402件という状態が実際に起きていた）。
    '
    ' そこで、ここでは印を付けるだけにして、LeaveGraceの猶予のうちに同じブラウザが
    ' 同じ部屋へ戻ってきたら、退室そのものを無かったことにする。
    ' 戻ってきたときの処理は、既に実装済みのJoinRoomの(b)引き継ぎがそのまま使える
    ' （猶予中はIsConnected = Falseのため、JoinRoomは自然に(b)へ落ちる）。
    ' 猶予を過ぎたぶんは、CheckTimeoutParticipantsが種別31で退室させる
    '
    ' なお、OnDisconnected（stopCalled = True）も同じく退室予告に留めている。
    ' F5ではビーコンとSignalRのstopの両方が走るため、片方だけを猶予にしても、
    ' もう片方が即座に退室させてしまい意味が無い
    Public Shared Sub LeaveByConnectionId(roomId As Integer, connectionId As String, browserId As String)

        ' どちらか一方でも欠けていると、誰の話か特定できないか、本人確認ができない。
        ' 中途半端な状態で処理を進めると、上に書いた「別人を掴む」事故につながるため、
        ' ここで打ち切る
        If String.IsNullOrEmpty(connectionId) OrElse String.IsNullOrEmpty(browserId) Then
            Return
        End If

        Dim participant As Participant = Nothing

        SyncLock SyncRoot
            If RoomParticipants.ContainsKey(roomId) Then
                ' 接続IDで「どの接続か」を特定し、あわせてBrowserIdで本人かどうかを確認する。
                ' 両方を満たす参加者だけが対象
                participant = RoomParticipants(roomId).FirstOrDefault(
                    Function(p) p.ConnectionId = connectionId AndAlso p.BrowserId = browserId)
            End If
        End SyncLock

        ' 見つからない場合は何もしない
        '
        ' これは異常ではなく、次のような正常な状態でも起こる。
        '   ・既に新しいページに入れ替わっている（このビーコンは、もう居ない接続の話）
        '   ・OnDisconnectedが先に処理を済ませ、ConnectionIdがNothingになっている
        '   ・既に退室済み、あるいはサーバーが再起動したあと
        If participant Is Nothing Then
            Return
        End If

        MarkDisconnectedCore(roomId, participant, True)

    End Sub



    ' そのブラウザの席を、全部屋から外す（ログアウトのときに呼ばれる）
    '
    ' 【なぜ、これが要るのか（本番で見つかった不具合）】
    ' ログアウトすると ChatBrowserId が新しくなる。
    ' ところが「同一ブラウザなら1人に絞る」置き換えの仕組み（JoinRoomの(a)）は、
    ' **その値が一致することを頼りに動いている。**
    ' つまりログアウトした瞬間、古い自分は誰にも追い出されなくなる。
    '
    ' 本番では、ログアウトとログインを繰り返した結果、参加者一覧に自分が5人並び、
    ' (5/20) と表示された。**繰り返すと定員が埋まって、誰も入れなくなる。**
    '
    ' 席が残るのは、部屋の窓を開いたままログアウトしたときである
    ' （その窓の接続は生きているので、無言タイムアウトの1時間まで消えない）。
    '
    ' 【却下した案：storage イベントで、他のタブに自分から出て行かせる】
    ' サーバーに何も足さずに済むが、**問題の本体はサーバー側の参加者リストに
    ' 残ることである。** あちらは画面の後始末しかできず、
    ' タブが凍結していれば動かない（Chromeは裏のタブを凍結する）。
    ' 同じことを2経路でやることにもなる
    '
    ' 【ページから呼ばれる Shared メソッドである】
    ' LeaveBeacon.aspx.vb が LeaveByConnectionId を呼んでいるのと、まったく同じ形。
    ' 呼び出し元は Chat/ChatLogout.vb
    Public Shared Sub LeaveByBrowserId(browserId As String)

        Dim target As Guid

        ' 【Guid に直してから比べること】
        ' 参加者リストが持っているのは Cookie の生の文字列（JoinRoom が加工せずに
        ' 保存している）で、大文字・小文字や波かっこで表記が揺れうる。
        ' 文字列のまま比べると、同じブラウザなのに一致せず、
        ' **しかもエラーは出ず「席が残り続ける」という形でしか表に出ない**
        ' （TryGetRoomIdByBrowserId と同じ理由・同じ書き方）
        If Not Guid.TryParse(browserId, target) Then
            Return
        End If

        ' 【SyncLock の中では、席を見つけるところまで】
        ' RemoveParticipantAndNotify は、配信とDBへの書き込みを行う。
        ' ロックを握ったまま呼ぶと、DBが詰まっている間じゅう全部屋のチャットが止まる
        ' （既存の「記録の書き込みは SyncLock を抜けてから」と、まったく同じ話）。
        ' あわせて、列挙している最中にリストを書き換えることも避けられる
        Dim found As New List(Of KeyValuePair(Of Integer, Participant))()

        SyncLock SyncRoot

            For Each roomEntry In RoomParticipants

                For Each p In roomEntry.Value

                    Dim stored As Guid

                    If Guid.TryParse(p.BrowserId, stored) AndAlso stored = target Then
                        found.Add(New KeyValuePair(Of Integer, Participant)(roomEntry.Key, p))
                    End If

                Next

            Next

            ' 前の身元のノック・取り置きも捨てる（ChatHub.Knock.vb の ForgetKnocksOfBrowserCore）
            ForgetKnocksOfBrowserCore(target)

        End SyncLock

        FlushRoomNotices()

        ' ラウンジの人数からも外す（理由は LeaveLoungeByBrowser のコメント）
        '
        ' 【部屋の席より先に外す】（順番を入れ替えた）
        ' 席を先に外すと、ラウンジを開いたままの人はその瞬間ラウンジの顔に戻り、
        ' ほかの人のラウンジに一瞬現れてすぐ消える
        LeaveLoungeByBrowser(target)

        ' 【1つとは限らない、という形で書いてある】
        ' 実際には、同じ BrowserId が2つ以上残ることはない（JoinRoom が必ず1つに絞る）。
        ' それでも全部を回すのは、**取りこぼすと席が永久に残る**側の失敗だからである。
        ' 見つからないことも正常（部屋に居ないままログアウトした場合）
        For Each entry In found
            RemoveParticipantAndNotify(entry.Key, entry.Value, ChatMessageType.Logout)
        Next

    End Sub



    ' 退室ボタンが押されたが、SignalR がつながっていないときに、HTTP で席を外す
    '
    ' 【なぜ、これが要るのか】
    ' スマホを放っておいたあとに戻ると、SignalR が「再接続中」のまま止まっていることがある。
    ' 以前の退室ボタンはハブの LeaveRoom の返事を待ってから移動していたので、
    ' その間は押しても何も起きず（返事が来ない・例外で止まる）、ラウンジへ移れなかった。
    ' 画面は SignalR を待たずに移動し、席はこの口（ChatRoom.aspx の WebMethod LeaveRoom から）で外す
    '
    ' 【本人確認は Cookie の ChatBrowserId】ログアウトの LeaveByBrowserId と同じ考え方。
    ' 外すのは「このブラウザの席」だけなので、他人を追い出す口にはならない。
    ' 退室ビーコン（LeaveByConnectionId）と違って接続IDを見ないのは、退室ボタンは
    ' 本人が「出る」と決めた操作で、F5 の追い越し（新しいページの席を掴む）を心配する場面ではないため
    '
    ' 【種別は 30（退室ボタン）】ビーコンの「退室予告→5秒後に31」ではなく、その場で外す。
    ' ハブの LeaveRoom が先に済んでいれば、ここでは見つからず何もしない（二重に書かれない）
    Public Shared Sub LeaveRoomByBrowserId(browserId As String)

        Dim target As Guid

        ' Guid に直してから比べる（LeaveByBrowserId と同じ理由）
        If Not Guid.TryParse(browserId, target) Then
            Return
        End If

        ' SyncLock の中では見つけるところまで（配信と DB はロックの外）
        Dim found As New List(Of KeyValuePair(Of Integer, Participant))()

        SyncLock SyncRoot
            For Each roomEntry In RoomParticipants
                For Each p In roomEntry.Value
                    Dim stored As Guid
                    If Guid.TryParse(p.BrowserId, stored) AndAlso stored = target Then
                        found.Add(New KeyValuePair(Of Integer, Participant)(roomEntry.Key, p))
                    End If
                Next
            Next
        End SyncLock

        ' 同じ BrowserId の席は1つにしかならない（JoinRoom が絞る）が、取りこぼすと席が残るので全部回す
        For Each entry In found
            RemoveParticipantAndNotify(entry.Key, entry.Value, ChatMessageType.Leave)
        Next

    End Sub



    ' 接続IDから、その接続がいる部屋と、参加者を特定する
    ' 見つからなければNothingを返す（その場合、roomIdの内容は不定）
    '
    ' RemoveParticipantAndNotifyが部屋と参加者を直接受け取る形になっているため、
    ' 接続IDしか手元にない呼び出し元（LeaveRoom・OnDisconnected）が、事前に使う
    Private Shared Function FindParticipant(connectionId As String, ByRef roomId As Integer) As Participant

        SyncLock SyncRoot

            If Not ConnectionRooms.TryGetValue(connectionId, roomId) Then
                Return Nothing
            End If

            If Not RoomParticipants.ContainsKey(roomId) Then
                Return Nothing
            End If

            Return RoomParticipants(roomId).FirstOrDefault(
                Function(p) p.ConnectionId = connectionId)

        End SyncLock

    End Function



    ' ============================================
    ' そのブラウザが今どの部屋に居るかを、参加者リストから引く
    '
    ' 戻り値 … 見つかったかどうか。見つからなければ roomId は 0 のまま
    '
    ' 【なぜ、これが要るのか】
    ' 過去ログの取り直し（ChatRoom.aspx.vb の GetLogEntries）は WebMethod のため、
    ' SignalRの接続IDを持っていない。それまでは Session("CurrentRoomId") を
    ' 「どの部屋に居るか」の根拠にしていたが、Sessionは20分で消えるうえ、
    ' アプリが寝ても消える。長時間の凍結から復帰すると、
    ' 「再接続にも成功していて、発言もできるのに、切断中の発言だけが
    '   永久に画面へ出ない」という状態になっていた（実機で発生）。
    '
    ' 在室しているかどうかを本当に知っているのは、このリストである。
    ' Sessionはその弱い写しでしかなかった
    '   ・Sessionはブラウザ全体で共有される（タブごとではない）
    '   ・Sessionは古くなりうる（だからGetLogEntries側にIsActiveの再確認が要った）
    '   ・Sessionは時間で消えるが、こちらは在室している限り消えない
    '
    ' 【SendMessage・JoinRoomと、同じ形である】
    ' 「クライアントやSessionに名乗らせるのをやめ、サーバーが既に知っている
    '   ほうから引く」という、この2つでやったことと同じ寄せ方をしている
    '
    ' 【SyncLockの中で、重いことをしないこと】
    ' ここでやるのはメモリ上の走査だけで、DBにもSiteSettingsにも触れていない。
    ' 呼び出し側（GetLogEntries）は、この関数から戻ったあとにDBを引く。
    ' ロックを握ったままDBを引くと、詰まっている間、全部屋のチャットが止まる
    '
    ' 【走査の量】部屋数 × 在室者数。呼ばれるのは再接続の直後と、
    ' 表示行数を変えたときだけで、発言のたびではない
    '
    ' 【罠】BrowserIdは、Guidに直してから比べること。
    ' 参加者リストが持っているのはCookieの生の文字列（JoinRoomが加工せずに
    ' 保存している）で、大文字・小文字や波かっこの有無で表記が揺れうる。
    ' 文字列のまま比べると、同じブラウザなのに一致しないことがあり、
    ' しかもエラーは出ず「ログだけ取り直せない」という形でしか表に出ない
    ' ============================================
    Public Shared Function TryGetRoomIdByBrowserId(browserId As String,
                                                   ByRef roomId As Integer) As Boolean

        roomId = 0

        Dim target As Guid

        If Not Guid.TryParse(browserId, target) Then
            Return False
        End If

        SyncLock SyncRoot

            For Each roomEntry In RoomParticipants

                For Each p In roomEntry.Value

                    Dim stored As Guid

                    If Guid.TryParse(p.BrowserId, stored) AndAlso stored = target Then
                        roomId = roomEntry.Key
                        Return True
                    End If

                Next

            Next

        End SyncLock

        Return False

    End Function



    ' === 無言タイムアウトと退室猶予は、SiteSettingsが持つ ===
    '
    ' SiteSettings.SilentTimeout（既定1時間）と
    ' SiteSettings.LeaveGrace（既定5秒）へ移した。
    '
    ' 退室猶予について。ページが閉じられた通知（退室ビーコン）が届いても、
    ' それが「タブを閉じた」のか「F5した」のかは区別できない。
    ' 猶予のうちに同じブラウザが同じ部屋へ戻ってくれば、
    ' 退室そのものが無かったことになる（JoinRoomの(b)引き継ぎ）。
    '
    ' 既定を5秒にしている根拠：本番のログを見ると、更新ボタンを押し続けたときの
    ' 1周が0.2〜0.3秒だった。通常のF5でも1〜2秒のため、5秒あれば十分に足りる。
    ' 見回りタイマー（SiteSettings.TimeoutCheckInterval）との噛み合わせは、
    ' SiteSettings.vbのコメントを参照

    ' 無言タイムアウトの長さを、「1時間」「30分」のような読める文言に組み立てる
    '
    ' 【なぜ、べた書きにしないのか】
    ' 本人への通知（Kicked）の文言に「1時間」とべた書きしてしまうと、
    ' SilentTimeoutを変更したときに文言だけが嘘になってしまうため。
    ' 必ず設定から組み立てること
    '
    ' Friendにしているのは、ChatEventText（イベント行の文言を組み立てるクラス）からも
    ' 呼ぶため。同じアセンブリの中だけで使えれば足りるので、Publicにはしない
    Friend Shared Function GetSilentTimeoutText() As String

        ' 【必ずSyncLockの外から呼ぶこと】
        ' SiteSettingsは、キャッシュが空のときDBへ問い合わせに行く。
        ' 今の呼び出し元は RemoveParticipantAndNotify だけで、
        ' そこへ来る4つの経路はいずれもロックを抜けたあとである（確認済み）
        Dim silentTimeout As TimeSpan = SiteSettings.SilentTimeout

        ' 1時間未満なら、分だけで表す（例：30分）
        ' （SiteSettings側で下限を60秒にしてあるため、ここが「0分」になることはない）
        If silentTimeout.TotalHours < 1 Then
            Return CInt(silentTimeout.TotalMinutes).ToString() & "分"
        End If

        Dim hours As Integer = CInt(Math.Floor(silentTimeout.TotalHours))
        Dim minutes As Integer = silentTimeout.Minutes

        ' ちょうど何時間なら、時間だけで表す（例：1時間）
        If minutes = 0 Then
            Return hours.ToString() & "時間"
        End If

        ' 端数がある場合は、時間と分を並べる（例：1時間30分）
        Return hours.ToString() & "時間" & minutes.ToString() & "分"

    End Function



    ' タイムアウトチェック用：全部屋を見回り、退室させるべき参加者を強制的に削除する
    ' Global.asaxのTimerから、定期的に呼び出される
    '
    ' 判定は2つある。どちらも「接続が生きているかどうか」は見ない
    ' （見てしまうと、スマホの画面消灯で退室扱いになってしまうため）。
    '
    '   1. 退室予告の猶予切れ（種別31）
    '        ページが閉じられた／F5された通知が届いてから、LeaveGraceを過ぎたもの。
    '        猶予のうちに戻ってこなかったので、ここで退室を確定させる
    '
    '   2. 無言タイムアウト（種別32）
    '        最後の発言からSilentTimeoutを過ぎたもの。
    '        「タブを閉じた」という通知はブラウザから必ず届くとは限らないため、
    '        届かなかった場合の最後の受け皿としても働く
    '
    ' 同じ参加者が両方に該当した場合は、1（猶予切れ）を優先する。
    ' 実際に起きたのは「ページを閉じた」ことであり、そちらのほうが理由として正確なため
    '
    ' 切断中（IsConnected = False）の参加者も、当然この判定の対象になる
    Public Shared Sub CheckTimeoutParticipants()

        Dim now As DateTime = DateTime.Now

        ' 設定は、SyncLockを取る前に読む（この形は移行前からそうなっていた）。
        ' 満員拒否の記録の設定も、ここでまとめて読んでおく
        ' （ShouldLogJoinReject・CleanUpRejectLogはロックの中で呼ばれるため、
        '   あちらへは引数で渡す。詳しくはそれぞれのコメント）
        Dim silentBorder As DateTime = now - SiteSettings.SilentTimeout
        Dim leaveBorder As DateTime = now - SiteSettings.LeaveGrace
        Dim rejectLogCooldown As TimeSpan = SiteSettings.RejectLogCooldown

        ' 退室させる相手を、どの部屋の誰かというセットで控えておく
        ' （切断中の参加者はConnectionIdがNothingのため、接続IDでは特定できない）
        '
        ' 理由ごとに別のリストへ分けているのは、退室ログの種別が違うため。
        ' SyncLockの中では対象を集めるだけにして、実際の通知・削除・DBアクセスは
        ' ロックを抜けてから行う（ロックの中にDBを持ち込まないこと）
        Dim leaveTargets As New List(Of KeyValuePair(Of Integer, Participant))
        Dim silentTargets As New List(Of KeyValuePair(Of Integer, Participant))

        SyncLock SyncRoot
            For Each roomEntry In RoomParticipants
                For Each participant In roomEntry.Value

                    If participant.LeavePendingAt.HasValue AndAlso
                       participant.LeavePendingAt.Value < leaveBorder Then

                        ' 1. 退室予告の猶予切れ（無言タイムアウトより優先する）
                        leaveTargets.Add(New KeyValuePair(Of Integer, Participant)(
                            roomEntry.Key, participant))

                    ElseIf participant.LastActiveAt < silentBorder Then

                        ' 2. 無言タイムアウト
                        silentTargets.Add(New KeyValuePair(Of Integer, Participant)(
                            roomEntry.Key, participant))

                    End If

                Next
            Next

            ' ついでに、満員で断った記録の「記録済みの相手」の表も掃除しておく
            ' （この表はJoinRoomが積むだけで、放っておくと期限切れの分が残り続ける。
            '   2秒おきに回っているこのタイマーへ相乗りさせるのが、いちばん確実）
            CleanUpRejectLog(now, rejectLogCooldown)

            ' 期限の切れた「鍵を通った印」も、同じく掃除する（ChatHub.RoomKeys.vb）
            CleanUpRoomPassesCore(now)

            ' 時間切れのノック・取り置きも片付ける（ChatHub.Knock.vb。知らせは下で配る）
            CollectExpiredKnocksCore(now)

        End SyncLock

        ' ノックの「返事がありませんでした」「取り置きが外れました」を配る。
        ' 【別の Try で包む】ここで失敗しても、下の退室の見回りは止めない
        Try
            FlushRoomNotices()
        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.CheckTimeoutParticipants（ノックの時間切れ）")
        End Try

        ' 対象の参加者を、通常の退室時と同じ処理で退室させる
        '
        ' 【1人ずつ Try/Catch で包む】
        ' ここはタイマー（Global.asax）から呼ばれている。1人の退室処理で例外が出ると、
        ' 残りの人がこの回の見回りで退室させてもらえなくなる（外側でも包んであるので、
        ' プロセスは落ちない。次の2秒後の見回りでまた拾われる）。
        ' 1人ずつ包めば、失敗した1人だけを ErrorReport へ知らせて、残りはそのまま続けられる
        For Each target In leaveTargets
            Try
                RemoveParticipantAndNotify(target.Key, target.Value, ChatMessageType.Disconnected)
            Catch ex As Exception
                ErrorReport.Report(ex, "ChatHub.CheckTimeoutParticipants（猶予切れの退室）", "部屋 " & target.Key)
            End Try
        Next

        For Each target In silentTargets
            Try
                RemoveParticipantAndNotify(target.Key, target.Value, ChatMessageType.Timeout)
            Catch ex As Exception
                ErrorReport.Report(ex, "ChatHub.CheckTimeoutParticipants（無言タイムアウト）", "部屋 " & target.Key)
            End Try
        Next

    End Sub



    ' 指定された参加者を、所属する部屋のリストから削除し、
    ' 同じ部屋の残りの参加者へ退室を通知する
    ' 退室ボタン・切断・タイムアウト・部屋の移動、すべての経路から呼ばれる共通処理
    '
    ' 【接続IDではなく、部屋と参加者を直接受け取る】
    ' 切断中の参加者はConnectionIdがNothingになっているため、接続IDからは探せない。
    ' 接続IDしか手元にない呼び出し元（LeaveRoom・OnDisconnected）は、
    ' FindParticipantで先に特定してから、ここへ渡すこと
    '
    ' 退室ログも、この1か所にまとめて書き込む（全経路がここへ合流するため、
    ' ここに集約しておけば、ログが重複したり抜けたりしない）。
    ' ただし退室の理由は呼び出し元によって異なるため、種別は引数で受け取る
    '
    ' なお、退室ボタンを押した場合は、LeaveRoomのあとページ遷移によってOnDisconnectedも
    ' 発火するが、その時点では既に参加者リストから削除済みでisRemovedがFalseとなるため、
    ' ログが二重に書かれることはない
    Private Shared Sub RemoveParticipantAndNotify(roomId As Integer, participant As Participant, messageType As ChatMessageType)

        Dim isRemoved As Boolean = False

        ' 退室ログにはニックネーム・アイコン・IPも必要だが、リストから削除したあとでも
        ' オブジェクト自体は参照できるため、そのまま使い続けてよい
        Dim removedLocalId As Integer = participant.LocalId

        ' 切断中の参加者はNothingになっている
        Dim connectionId As String = participant.ConnectionId

        ' ラウンジへ配る差分（ChatHub.Lounge.vb）
        Dim loungeUpdates As New List(Of LoungeUpdate)

        ' 管理人が出ていったときの、新しい管理人（ChatHub.Rooms.vb）
        Dim ownerHandoff As Guid? = Nothing

        SyncLock SyncRoot

            If RoomParticipants.ContainsKey(roomId) Then
                isRemoved = RoomParticipants(roomId).Remove(participant)
            End If

            ' 空になった一時部屋に印を付け、管理人が抜けていたら引き継ぐ
            ' （実際に外せたときだけ）
            If isRemoved Then
                ownerHandoff = AfterParticipantRemovedCore(roomId, participant)
            End If

            ' 実際に外せたときだけ、ラウンジへ「その1人が出た」を作る
            ' （作るのはロックの中＝通し番号を書き換えた順に振るため。配るのは抜けてから）。
            ' 続けてラウンジの顔も合わせる。部屋を出た人がラウンジを開いたままなら、
            ' ラウンジの顔に戻る（別のタブでラウンジを開いたまま、部屋を出た場合など）
            If isRemoved Then
                loungeUpdates.Add(NextLoungeLeaveUpdate(roomId, removedLocalId, quiet:=False))
                loungeUpdates.AddRange(SyncLoungeMembers())

                ' その人のラウンジの「入室中」を外す
                QueueSelfRoomCore(participant.BrowserId)
            End If

            ' 逆引き用の対応も、あわせて削除する
            ' （切断中はConnectionIdがNothingで、Dictionary.Removeが例外を投げるため、
            '   ここだけは判定が必要。なおその場合、逆引きの登録はMarkDisconnectedで
            '   既に消えている）
            If connectionId IsNot Nothing Then
                ConnectionRooms.Remove(connectionId)
            End If

        End SyncLock

        ' 実際に参加者を削除できた場合のみ、部屋の残りの参加者へ通知する
        ' （このメソッドはHubのインスタンスメソッドではない場所からも呼ばれるため、
        '   Clientsプロパティの代わりにGlobalHost経由でHubContextを取得している）
        If isRemoved Then
            Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()

            ' SignalRのグループからも必ず外す。
            ' 参加者リストから消すだけだと、退室扱いになった本人にも、その部屋への放送が
            ' 届き続けてしまう（自分のUserLeftを自分で受け取って、自分を参加者一覧から
            ' 消す、という状態が実際に起きていた）
            '
            ' このメソッドはSharedのためAwaitできない。戻り値のTaskは待たずに進める。
            ' グループから外れるのが数ミリ秒遅れても実害が無いのに対し、ここで待つと
            ' 呼び出し元（OnDisconnectedやタイマー）を不必要に止めてしまうため
            Try
                hubContext.Groups.Remove(connectionId, roomId.ToString())
            Catch ex As Exception
                ' 外せなくても、退室処理そのものは続行する
                '
                ' 【ErrorReport へは知らせない（決めた）】
                ' 切断中の参加者は connectionId が Nothing なので、ここは**失敗して当たり前**の場面がある
                ' （猶予切れ・無言タイムアウトで外すとき）。知らせると、正常な退室のたびにメールが来る
            End Try

            ' 退室のお知らせ（イベント行）の文言は、サーバー側で組み立てて渡す。
            ' 退室の理由は種別によって変わるため、messageTypeをそのまま文言に反映させる
            ' （クライアントは、受け取った文言をそのまま表示するだけ）
            hubContext.Clients.Group(roomId.ToString()).UserLeft(
                removedLocalId,
                ChatEventText.Build(messageType, participant.Nickname),
                AnonId.ComputeFromString(roomId, participant.BrowserId))

            ' ラウンジへも配る（DBへの書き込みより前に。DBの遅れをラウンジに波及させない）
            PublishLoungeUpdates(loungeUpdates)

            ' 本人にも、退室した理由を知らせる
            ' （利用者から見て「気づいたら退室扱いになっていた」状態を避けるため）
            '   Leave        … 本人が退室ボタンを押したので、知らせる必要がない
            '   Disconnected … 接続が既に無いので、送っても届かない
            Dim kickedMessage As String = Nothing

            If messageType = ChatMessageType.Timeout Then
                kickedMessage = GetSilentTimeoutText() & "発言がなかったため、退室しました"

            ElseIf messageType = ChatMessageType.KickedByOwner Then

                ' 管理人が「退室させる」を押した。ラウンジの帯に出す（開発者の決めた文言）
                kickedMessage = KickedByOwnerMessage

            ElseIf messageType = ChatMessageType.BannedByOwner Then

                ' 管理人が「入室禁止にする」を押した
                kickedMessage = BannedByOwnerMessage

            ElseIf messageType = ChatMessageType.Logout Then

                ' ログアウトは、別の窓（ラウンジ）で押されている。
                ' この Kicked が届く先は、**開いたままになっている部屋の窓**である。
                ' 何も知らせないと、その窓だけが「まだ居るつもり」で残るため、必ず送る
                kickedMessage = ChatCommon.LoggedOutMessage
            End If

            ' 切断中（ConnectionIdがNothing）の相手には送らない。
            ' 届かないからではなく、Clients.Clientが空の接続IDに対して例外を投げ、
            ' このあとのログ書き込みまで巻き添えで止まってしまうため
            If kickedMessage IsNot Nothing AndAlso connectionId IsNot Nothing Then
                hubContext.Clients.Client(connectionId).Kicked(kickedMessage)
            End If

            ' 退室ログを残す（配信のあとに書き込む。DBの遅延を表示に波及させないため）
            ChatLog.Write(roomId, messageType, participant)

            ' 部屋の知らせ（管理人の交代＝15-2c、返事待ちのノックの取り消し＝15-2b。鍵の中で積んだもの）。
            ' 【退室の配信（UserLeft）のあとに配る】「管理人が〇〇さんに替わりました」が「退室しました」より先に出ないように
            FlushRoomNotices()

            ' 管理人が出ていったので、新しい管理人をDBへ書く
            '
            ' 【必ず SyncLock を抜けてから】DBを鍵の中に持ち込まない。
            ' 【配信とログのあとに置いてある】
            ' メモリ側（RoomOwners）は鍵の中で書き換わっているので、
            ' この書き込みが遅れても、部屋の中の動きには影響しない
            If ownerHandoff.HasValue Then
                WriteOwnerHandoff(roomId, ownerHandoff.Value)
            End If
        End If

    End Sub

End Class
