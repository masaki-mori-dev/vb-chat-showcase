Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.AspNet.SignalR

' ============================================================
' ラウンジを SignalR に繋ぐ
' ============================================================
'
' ChatHub の続き（Partial）。部屋の参加者リスト（RoomParticipants）と、
' それを守る鍵（SyncRoot）を、そのまま使うためにここへ置いている。
'
' 【なぜ、別のハブ（LoungeHub）にしなかったのか】
' ラウンジの人数は「部屋に居ないブラウザ」を数える（下の CountLoungeBrowsers）。
' つまり部屋の出入りとラウンジの出入りを、**同じ鍵の中で、同じ通し番号で**並べる必要がある。
' 別のハブにすると、鍵を外へ貸し出すことになる。
' 画面の側も1本の接続（$.connection.chatHub）で済む
'
' 【流れ】
'   1. ラウンジの画面が JoinLounge を呼ぶ → ラウンジのグループへ入り、全部屋の在室者を丸ごと受け取る
'   2. 誰かが部屋に入った・出た → ラウンジのグループへ「その1人ぶん」だけ配る（LoungeUpdate）
'   3. ラウンジの接続が切れた → ラウンジの人数から外す（OnDisconnected）
'   4. ラウンジのページを離れた → ビーコンで人数から外す（LeaveLoungeByConnectionId）。
'      戻るボタンで同じページが戻ってきたら、1 をもう一度呼んで入り直す
'
' 【配る差分を作る場所は3つだけ】
' 部屋の参加者リストを書き換えるのは、次の3か所しか無い。そこへ1行ずつ足してある。
'   ・JoinRoom の新しい1人の追加
'   ・JoinRoom の (a) 2窓目の置き換え（古いほうを外す）
'   ・RemoveParticipantAndNotify（退室ボタン・切断・無言タイムアウト・部屋の移動・ログアウト）
' **参加者リストを書き換える場所を増やしたら、ここへ1行足すこと。**
' 足し忘れると、ラウンジは次に繋ぎ直すまで古い人数のまま（エラーは出ない）
' （3か所とも、部屋の差分のあとに SyncLoungeMembers も呼んでいる。部屋へ入った人は
'   ラウンジの顔から消え、部屋から出た人は、ラウンジを開いていればラウンジの顔に戻るため）
'
' 【ラウンジに居る人の顔】
' 要望：せっかくリアルタイムなのだから、ラウンジに居る人のアイコンなども
' 部屋と同じように出す。部屋に居る人の顔は出ていたのに、
' ラウンジに居る人は人数だけだった。部屋の参加者と同じ形で出す。
'   ・顔（名前・アイコン・トリップ）は、ラウンジに繋いだときに **Cookie から読む**（JoinLounge）。
'     JoinRoom と同じく、クライアントに名乗らせない
'   ・誰が居るかは人数と同じ数え方（CurrentLoungeBrowsers）。数と顔は必ず一致する
'   ・変わったら「その1人ぶん」だけ配る（loungeJoin ／ loungeLeave）。通し番号は部屋の差分と共通
'   ・トリップをラウンジで付け外ししたら、その場で顔にも反映する（loungeChange。UpdateLoungeTrip）
'   ・部屋に居ながら見た目（アイコン・色）を変えたら、その部屋の顔を置き換える（occupantChange。
'     ChatHub.Appearance.vb）。控えの顔（LoungeProfiles）も同じ場で書き換える
'   ・ブラウザの識別子は渡さない。ラウンジの中だけの番号（MemberId）を振る
'
' 【承知のうえで決めたこと：ラウンジでは姿を隠せなくなる】
' それまでは「部屋に入るまで、自分の姿は誰にも見えない」状態だった。開発者と相談して進めた。理由は3つ。
'   ・そもそも匿名で、名前は自分で決めたもの。ログアウトすればいつでも別人になれる
'   ・部屋に入れば1秒後には顔が出るので、ラウンジで隠れられるのは数秒だけ
'   ・誰も居ないように見えるラウンジのほうが損。人数だけだと数字だが、顔が並ぶと
'     「誰か居る」と分かる＝入口として効く
'
' 【無視機能は、ラウンジには効かせない】
' 決定：ラウンジでは会話をしない（できない）ので、無視は効かせなくてよい。
' 無視は「その部屋に居る間だけ」の仕組みで、話す場所でないところに効かせる理由が無い
'
' 【DBは1回も引かない】全部メモリの上の話である
Partial Public Class ChatHub

    ' ラウンジの画面が入る SignalR のグループ名
    ' （部屋のグループは部屋ID＝数字なので、ぶつからない）
    Private Const LoungeGroupName As String = "lounge"

    ' 差分の種類（LoungeUpdate.Kind）。画面（Scripts/lounge-live.js）と同じ綴りにすること
    Private Const LoungeKindJoin As String = "join"
    Private Const LoungeKindLeave As String = "leave"
    Private Const LoungeKindLoungeJoin As String = "loungeJoin"
    Private Const LoungeKindLoungeLeave As String = "loungeLeave"
    Private Const LoungeKindLoungeChange As String = "loungeChange"
    ' 部屋に居る人が、見た目（アイコン・色）を変えた。roomId と occupant（中身を置き換える1人ぶん）
    Private Const LoungeKindOccupantChange As String = "occupantChange"
    ' （12-1〜あった "lounge"＝人数だけが変わった、は 作らなくなった。LoungeSnapshot.vb の LoungeUpdate）

    ' ラウンジを開いている接続（キー：接続ID、値：その接続のブラウザ）
    '
    ' ブラウザが読めなかった接続は Guid.Empty で持つ（人数には数えないが、差分は配る）。
    '
    ' 【数えるのは接続ではなく、ブラウザ】
    ' 同じブラウザでラウンジを2つのタブで開いても、1人と数える
    ' （部屋が「同じブラウザなら1人に絞る」のと揃えてある）。
    ' そのため接続ごとに持ち、数えるときに重複を除く
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared LoungeConnections As New Dictionary(Of String, Guid)

    ' 差分の通し番号（最後に配った差分の番号）。SyncRoot の中でだけ進める
    Private Shared LoungeVersion As Long = 0

    ' ラウンジを開いているブラウザの顔。キー：ブラウザ
    '
    ' LoungeConnections に1本でも接続があるブラウザだけが入っている（最後の1本が外れたら消す。
    ' RemoveLoungeConnectionCore）。部屋に居る間も消さない（部屋から出たとき、ラウンジの顔に戻すため）。
    '
    ' 【同じブラウザの2本目の接続では、書き換えない】
    ' 接続が読める Cookie は「接続を張った時点」のもので、あとから古くなることがある
    ' （ラウンジでトリップを付け外ししても、開いたままの接続の Cookie は変わらない）。
    ' ここにある値は、トリップの付け外し（UpdateLoungeTrip）と、部屋での見た目の変更（UpdateLoungeAppearanceCore。
    ' 15-3a-1）で書き換えてあるので、こちらのほうが新しい。
    ' 名前は、ログアウトしない限り変えられない（ログアウトすればブラウザごと別になる）。
    ' アイコンと色は、部屋に居ながら変えられる。
    '
    ' 【見た目を変えたら、ここも必ず書き換えること】書き換えないと、部屋で色を変えた人が
    ' 部屋を出た瞬間、ラウンジの顔だけ古い色に戻る（この控えは、ラウンジに繋いだ時点の Cookie で作られるため）
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared LoungeProfiles As New Dictionary(Of Guid, LoungeProfile)

    ' 最後に配った（または丸ごとで渡した）「ラウンジに居る人」。キー：ブラウザ、値：ラウンジの中の番号
    '
    ' 人数はこの数（12-3 までの LastLoungeCount の役も、ここが引き継いだ）。
    ' 今の状態と食い違ったぶんだけ差分にする（SyncLoungeMembers）ので、
    ' 同じブラウザの2つ目のタブ・F5 のように、居る人が変わらない出入りでは何も配らない
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared LoungeMembers As New Dictionary(Of Guid, Integer)

    ' ラウンジの中の番号の、次に振る値（部屋の LocalId と同じ欠番方式。使い回さない）
    Private Shared NextLoungeMemberId As Integer = 1


    ' ラウンジに居る人の顔（名前・アイコン・トリップ）。検査を通した値だけを持つ（CreateLoungeProfile）
    Private Class LoungeProfile
        Public Property Nickname As String
        Public Property IconType As String
        Public Property IconColor As String
        Public Property Trip As String

        ' アカウントのユーザー名（アカウントID を表示している人だけ。そのとき Trip は必ず Nothing）
        Public Property AccountName As String
    End Class


    ' ラウンジの画面から、繋いだとき・繋ぎ直したときに呼ばれる
    '
    ' 戻り値 … 全部屋の在室者の丸ごと（LoungeSnapshot）
    '
    ' 【何度呼ばれてもよい】同じ接続から2回呼ばれても、グループへの追加も人数も変わらない。
    ' 画面は、繋ぎ直したとき・番号の飛びに気づいたときにも、これを呼んで丸ごとを取り直す
    '
    ' 【順番に意味がある】
    '   1. 先にグループへ入る
    '   2. そのあとで丸ごとを作る
    ' 逆にすると、丸ごとを作ってからグループに入るまでの間に起きた差分が、
    ' どちらにも入らずに抜け落ちる。この順なら、その間の差分は「丸ごとにも入っていて、
    ' 差分としても届く」ことになり、画面は番号を見て2度目を捨てる
    '
    ' 【ブラウザと顔は Cookie から読む】JoinRoom と同じ（クライアントに名乗らせない）。
    ' ブラウザが読めない・名前が無いときは、人数にも顔にも入れない（差分は届く）
    Public Async Function JoinLounge() As Task(Of LoungeSnapshot)

        Dim browser As Guid = Guid.Empty

        If Not Guid.TryParse(GetBrowserIdFromCookie(), browser) Then
            browser = Guid.Empty
        End If

        ' 顔。検査とトリップの計算は、鍵を取る前に済ませる
        ' （名前の検査は SiteSettings を読む。SyncRoot の中から SiteSettings を読まない決まり）
        Dim profile As LoungeProfile = Nothing

        If browser <> Guid.Empty Then

            ' アカウントID を表示している人か（DB を1行引くことがあるので、これも鍵の外で。JoinRoom と同じ）
            profile = CreateLoungeProfile(
                ReadTokenNickname(),
                GetCookieValue("ChatIconType"),
                GetCookieValue("ChatIconColor"),
                TripCode.FromCookieValue(GetCookieValue(TripCode.CookieName)),
                ResolveAccountLink().UserName)

            If profile Is Nothing Then
                browser = Guid.Empty
            End If

        End If

        Await Groups.Add(Context.ConnectionId, LoungeGroupName)

        Dim updates As List(Of LoungeUpdate)
        Dim snapshot As LoungeSnapshot

        SyncLock SyncRoot

            ' 同じ接続から2度目なら、前の登録をいったん外してから入れ直す（何度呼ばれてもよい形）
            RemoveLoungeConnectionCore(Context.ConnectionId)

            LoungeConnections(Context.ConnectionId) = browser

            If browser <> Guid.Empty AndAlso Not LoungeProfiles.ContainsKey(browser) Then
                LoungeProfiles(browser) = profile
            End If

            ' 自分が来たことでラウンジに居る人が変わったなら、ほかのラウンジの人へ知らせる
            ' （自分にも届くが、丸ごとの番号より古いので画面が捨てる）
            updates = SyncLoungeMembers()

            snapshot = BuildLoungeSnapshotCore(Guid.Empty, Nothing)

            ' この接続の持ち主が入っている部屋。viewer に Guid.Empty を渡しているので、ここで入れる
            snapshot.MyRoomId = RoomIdOfBrowserCore(browser)

            ' この接続の持ち主の顔。控え（LoungeProfiles）は部屋での見た目の変更で書き換えてあるので、
            ' 繋いでいない間に変わっていても、繋ぎ直したときにプロフィールの枠が追いつく。番号は MyRoomSeq と同じ数え方
            Dim ownFace As LoungeProfile = Nothing

            If browser <> Guid.Empty AndAlso LoungeProfiles.TryGetValue(browser, ownFace) Then
                snapshot.MyIconType = ownFace.IconType
                snapshot.MyIconColor = ownFace.IconColor
            End If

        End SyncLock

        PublishLoungeUpdates(updates)

        Return snapshot

    End Function


    ' ページ（ラウンジの Page_Load）が、最初の表示を組み立てるために呼ぶ
    '
    ' viewerBrowserId … これからラウンジに繋ぐ人のブラウザ（Cookie の値）。
    '   まだ繋いでいないが、繋げば数に入る。先に入れておかないと、
    '   開いた直後に人数が1つ増えて見える（画面が「跳ねる」のと同じ話）。
    '   読めなければ入れない
    ' viewerNickname ／ viewerIconType ／ viewerIconColor ／ viewerTrip … その人の顔（Cookie の値。
    '   名前はデコードしたもの）。人数と同じ理由で、顔の一覧にも先に入れておく
    '   （繋いだ瞬間に自分の顔が1つ増えると、一覧の高さが変わって画面が跳ねる）。
    '   検査はここで通すので、Cookie の値をそのまま渡してよい
    '
    ' 【DBを引かない】部屋の在室者はメモリ上にしか無い
    '
    ' viewerAccountName … その人がアカウントID を表示しているなら、そのユーザー名（無ければ Nothing）。
    '   呼ぶ側（RoomList.ascx.vb）が ChatAccountLink.Resolve(Request) で決めた値を渡す
    Public Shared Function GetLoungeSnapshot(viewerBrowserId As String,
                                             viewerNickname As String,
                                             viewerIconType As String,
                                             viewerIconColor As String,
                                             viewerTrip As String,
                                             viewerAccountName As String) As LoungeSnapshot

        Dim viewer As Guid = Guid.Empty

        If Not Guid.TryParse(viewerBrowserId, viewer) Then
            viewer = Guid.Empty
        End If

        ' 鍵を取る前に（SiteSettings を読むため）
        Dim viewerProfile As LoungeProfile = CreateLoungeProfile(
            viewerNickname, viewerIconType, viewerIconColor, viewerTrip, viewerAccountName)

        If viewerProfile Is Nothing Then
            viewer = Guid.Empty
        End If

        SyncLock SyncRoot
            Return BuildLoungeSnapshotCore(viewer, viewerProfile)
        End SyncLock

    End Function


    ' 丸ごとを組み立てる
    '
    ' viewer ／ viewerProfile … まだ繋いでいないが、ラウンジに入れておきたい人（GetLoungeSnapshot）。
    '   無ければ Guid.Empty ／ Nothing。入れるときは番号 0 の顔にする（本物の番号は、繋いだときに振る。
    '   画面は繋いだあとの丸ごとで一覧ごと置き換えるので、仮の番号が残ることはない）。
    '   来た順ではいちばん新しいので、いちばん後ろに置く（繋いだあとの丸ごとでも、いちばん後ろになる）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】参加者リストと通し番号を、同じ瞬間の値で読むため
    Private Shared Function BuildLoungeSnapshotCore(viewer As Guid, viewerProfile As LoungeProfile) As LoungeSnapshot

        Dim rooms As New List(Of LoungeRoomOccupants)

        For Each roomEntry In RoomParticipants

            If roomEntry.Value.Count = 0 Then
                Continue For
            End If

            rooms.Add(New LoungeRoomOccupants With {
                .RoomId = roomEntry.Key,
                .OwnerLocalId = OwnerLocalIdCore(roomEntry.Key),
                .Occupants = roomEntry.Value.OrderBy(Function(p) p.JoinSeq).Select(Function(p) ToLoungeOccupant(p)).ToList()
            })

        Next

        ' ラウンジに居る人（来た順＝番号の順）
        Dim members As New List(Of LoungeMemberInfo)

        For Each entry In LoungeMembers.OrderBy(Function(m) m.Value)

            Dim profile As LoungeProfile = Nothing

            If LoungeProfiles.TryGetValue(entry.Key, profile) Then
                members.Add(ToLoungeMember(entry.Value, profile))
            End If

        Next

        If viewer <> Guid.Empty AndAlso Not LoungeMembers.ContainsKey(viewer) AndAlso
           Not BrowsersInRooms().Contains(viewer) Then
            members.Add(ToLoungeMember(0, viewerProfile))
        End If

        Return New LoungeSnapshot With {
            .Version = LoungeVersion,
            .LoungeCount = members.Count,
            .Rooms = rooms,
            .Members = members,
            .MyRoomId = RoomIdOfBrowserCore(viewer),
            .MyRoomSeq = SelfRoomSeq
        }

    End Function


    ' Cookie の値から、ラウンジの顔を作る。名前が無ければ Nothing（数えない）
    '
    ' 【検査は JoinRoom と同じ】（ChatProfileValidator）。Cookie はいくらでも書き換えられるので、
    ' 検査していない値をラウンジの全員へ配らない。名前が空のときに既定名を付けないのも JoinRoom と同じ
    '
    ' 【SyncRoot の中から呼ばないこと】名前の長さの上限を SiteSettings から読む
    '
    ' accountName … アカウントID を表示している人なら、そのユーザー名（ChatAccountLink が決めた値）。無ければ Nothing。
    ' 【トリップとは、どちらか一方を、ここでも守る】accountName が入っていれば、trip は捨てる。
    ' 呼ぶ側が先に落としていても、ここでもう一度落とす（顔を作る場所はここ1つなので、ここを通れば必ず片方だけになる）
    Private Shared Function CreateLoungeProfile(nickname As String, iconType As String,
                                                iconColor As String, trip As String,
                                                accountName As String) As LoungeProfile

        Dim normalizedNickname As String = ChatProfileValidator.NormalizeNickname(nickname)

        If String.IsNullOrEmpty(normalizedNickname) Then
            Return Nothing
        End If

        Dim hasAccount As Boolean = Not String.IsNullOrEmpty(accountName)

        Return New LoungeProfile With {
            .Nickname = normalizedNickname,
            .IconType = ChatProfileValidator.NormalizeIconType(iconType),
            .IconColor = ChatProfileValidator.NormalizeIconColor(iconColor),
            .Trip = If(hasAccount, Nothing, trip),
            .AccountName = If(hasAccount, accountName, Nothing)
        }

    End Function


    ' この接続の持ち主の名前を、**名前の札から**読む
    '
    ' 【名前の Cookie（ChatNickname）から読んではいけない】
    ' あれは JS から自由に書けるので、書き換えるだけでラウンジの顔とノックの名前が変えられた。
    ' 名前の正は札（ChatNameToken）ただ1つ、というのがサイト全体の決まりである
    ' （「身元の決まり」／Chat/NameToken.vb）。
    '
    ' 【罠】接続が読める Cookie は「接続を張った時点」の値である（GetCookieValue のコメント）。
    ' 名前は在室中に変わらない（変えるにはログアウト＝別人になる）ので、古くて困ることは無い
    Private Function ReadTokenNickname() As String

        Return NameToken.ReadNickname(
            GetCookieValue("ChatBrowserId"),
            GetCookieValue(NameToken.CookieName))

    End Function


    ' ラウンジに居る人1人ぶんを、画面へ渡してよい形にする
    Private Shared Function ToLoungeMember(memberId As Integer, profile As LoungeProfile) As LoungeMemberInfo

        Return New LoungeMemberInfo With {
            .MemberId = memberId,
            .Nickname = profile.Nickname,
            .IconType = profile.IconType,
            .IconColor = profile.IconColor,
            .Trip = profile.Trip,
            .AccountName = profile.AccountName
        }

    End Function


    ' 部屋に居るブラウザ
    '
    ' 【Guid に直してから比べること】参加者が持っているのは Cookie の生の文字列で、
    ' 大文字・小文字や波かっこで表記が揺れうる（TryGetRoomIdByBrowserId と同じ用心）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function BrowsersInRooms() As HashSet(Of Guid)

        Dim inRooms As New HashSet(Of Guid)

        For Each roomEntry In RoomParticipants
            For Each p In roomEntry.Value

                Dim stored As Guid

                If Guid.TryParse(p.BrowserId, stored) Then
                    inRooms.Add(stored)
                End If

            Next
        Next

        Return inRooms

    End Function


    ' 今ラウンジに居るブラウザを数える（CountLoungeBrowsers から名前を変え、集合を返すようにした）
    '
    ' 【数え方】ラウンジを開いているブラウザのうち、**どの部屋にも居ないもの**。
    '
    ' 【なぜ、部屋に居る人を除くのか】
    ' ラウンジから部屋へ入ると、同じタブがラウンジを離れる。ところがラウンジの接続は、
    ' ページを離れてもすぐには切れないことがある。
    '   ・本番の Chrome は、前のページを戻るボタン用に**接続ごと凍らせて保持する**（bfcache）。
    '     本番の実機で、部屋ではないページへ移ったあと2分以上減らなかった
    '   ・SignalR 自身の「切ります」（unload）は、Chrome では許されない（宿題11）
    ' ページを離れるときの合図（LeaveLoungeByConnectionId）を足したが、
    ' 合図は届かないこともある（通信が切れていた、など）。
    ' 部屋に居る人を除いておけば、合図が届いても届かなくても、入室した瞬間にラウンジから減る
    ' （開発機のブラウザは前のページを保持しないので、この遅れは再現しない。
    '   合図が無いときは、タブを閉じてから減るまで約35秒＝SignalR の切断判定だった）
    ' （ラウンジのタブを残したまま別のタブで部屋に入った人も、部屋の側に数える。
    '   合計が、来ている人数と一致する）
    '
    ' 退室の猶予（5秒）の間の人・通信が切れているだけの人も、部屋の人数に入っている間は部屋の側
    '
    ' 【ブラウザが読めなかった接続・名前の無い接続は入らない】（LoungeProfiles に入っていないため）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    ' 部屋の人数 × ラウンジのブラウザ数のメモリ上の走査だけで、DBにも設定にも触れない
    Private Shared Function CurrentLoungeBrowsers() As HashSet(Of Guid)

        Dim inLounge As New HashSet(Of Guid)

        If LoungeProfiles.Count = 0 Then
            Return inLounge
        End If

        Dim inRooms As HashSet(Of Guid) = BrowsersInRooms()

        For Each browser In LoungeProfiles.Keys
            If Not inRooms.Contains(browser) Then
                inLounge.Add(browser)
            End If
        Next

        Return inLounge

    End Function


    ' 「ラウンジに居る人」を今の状態に合わせ、変わったぶんを差分にして返す
    '
    ' 居なくなった人 → loungeLeave、現れた人 → 新しい番号を振って loungeJoin。
    ' 1人ぶんにつき差分1つ（番号も1つ）。人数（LoungeCount）は、その差分を当てた時点の数
    '
    ' 【ラウンジの接続を増やした・減らしたとき、部屋の参加者リストを書き換えたときに、必ず呼ぶこと】
    ' 呼び忘れても次に呼ばれたときに追いつくが、それまでの間は人数も顔も古い
    '
    ' 【ラウンジに誰も居ないときは、状態だけ合わせて差分は作らない】（NextLoungeUpdate が Nothing を返す）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】配るのは抜けてから（PublishLoungeUpdates）
    Private Shared Function SyncLoungeMembers() As List(Of LoungeUpdate)

        Dim updates As New List(Of LoungeUpdate)

        ' 部屋の出入りのたびに呼ばれる。ラウンジに誰も居なければ、部屋を走査しないで帰る
        If LoungeProfiles.Count = 0 AndAlso LoungeMembers.Count = 0 Then
            Return updates
        End If

        Dim current As HashSet(Of Guid) = CurrentLoungeBrowsers()

        ' 居なくなった人
        For Each browser In LoungeMembers.Keys.ToList()

            If current.Contains(browser) Then
                Continue For
            End If

            Dim memberId As Integer = LoungeMembers(browser)

            LoungeMembers.Remove(browser)

            Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindLoungeLeave)

            If update IsNot Nothing Then
                update.MemberId = memberId
                updates.Add(update)
            End If

        Next

        ' 現れた人
        For Each browser In current

            If LoungeMembers.ContainsKey(browser) Then
                Continue For
            End If

            Dim memberId As Integer = NextLoungeMemberId

            NextLoungeMemberId += 1
            LoungeMembers(browser) = memberId

            Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindLoungeJoin)

            If update IsNot Nothing Then
                update.Member = ToLoungeMember(memberId, LoungeProfiles(browser))
                updates.Add(update)
            End If

        Next

        Return updates

    End Function


    ' ラウンジの接続を1本外す。外せたら True
    '
    ' そのブラウザの接続がほかに残っていなければ、顔（LoungeProfiles）も捨てる。
    ' 「ラウンジに居る人」の差分は作らない（呼び出し側が、このあと SyncLoungeMembers を呼ぶ）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function RemoveLoungeConnectionCore(connectionId As String) As Boolean

        Dim browser As Guid

        If connectionId Is Nothing OrElse Not LoungeConnections.TryGetValue(connectionId, browser) Then
            Return False
        End If

        LoungeConnections.Remove(connectionId)

        If browser <> Guid.Empty AndAlso Not LoungeConnections.ContainsValue(browser) Then
            LoungeProfiles.Remove(browser)
        End If

        Return True

    End Function


    ' ラウンジでトリップを付け外ししたとき、その人の顔にも反映する
    '
    ' 呼び出し元は Chat/Lounge.aspx.vb の SetTripKey ／ ClearTripKey（Cookie を書いたあと）。
    ' trip … 新しいトリップ（外したときは Nothing）
    '
    ' 【なぜ要るのか】ラウンジの接続が読める Cookie は「接続を張った時点」のもので、
    ' 付け外ししても変わらない。これが無いと、トリップを付けた本人の顔に印が付かない
    ' （部屋の中は「次に入ったときから」でよいが、ラウンジはトリップの欄と同じ画面にあるので、
    '   押した直後に自分の顔が変わらないのは不自然）
    '
    ' 【本人確認は Cookie】WebMethod が自分の Request から読んだ ChatBrowserId を渡す
    ' （クライアントに名乗らせない）。ラウンジに繋いでいなければ何もしない
    Public Shared Sub UpdateLoungeTrip(browserId As String, trip As String)

        Dim target As Guid

        If Not Guid.TryParse(browserId, target) Then
            Return
        End If

        Dim update As LoungeUpdate = Nothing

        SyncLock SyncRoot

            Dim profile As LoungeProfile = Nothing

            If Not LoungeProfiles.TryGetValue(target, profile) OrElse
               String.Equals(profile.Trip, trip, StringComparison.Ordinal) Then
                Return
            End If

            ' アカウントID を出している顔には、トリップを付けない（どちらか一方）。
            ' 付け外しの口（Lounge.aspx の SetTripKey）が先に断っているので、ふつうはここへ来ない
            If Not String.IsNullOrEmpty(profile.AccountName) Then
                Return
            End If

            profile.Trip = trip

            ' 部屋に居る間は顔が出ていないので、配るものが無い（部屋から出たら、新しいトリップで現れる）
            Dim memberId As Integer

            If LoungeMembers.TryGetValue(target, memberId) Then

                update = NextLoungeUpdate(LoungeKindLoungeChange)

                If update IsNot Nothing Then
                    update.Member = ToLoungeMember(memberId, profile)
                End If

            End If

        End SyncLock

        PublishLoungeUpdate(update)

    End Sub


    ' ラウンジでアカウントID を付け外ししたとき、その人の顔にも反映する
    '
    ' 呼び出し元は Chat/Lounge.aspx.vb の SetAccountIdShown（札を書き直したあと）。
    ' accountName … 付けたならユーザー名、外したなら Nothing
    ' trip        … 外したときに戻るトリップ（合言葉の Cookie から計算したもの。付けたときは Nothing＝どちらか一方）
    '
    ' 形は UpdateLoungeTrip とまったく同じ（顔の印が、四角（ID）と丸（トリップ）で入れ替わる）
    Public Shared Sub UpdateLoungeAccountMark(browserId As String, accountName As String, trip As String)

        Dim target As Guid

        If Not Guid.TryParse(browserId, target) Then
            Return
        End If

        ' どちらか一方（付けたなら、トリップは必ず外す）
        If Not String.IsNullOrEmpty(accountName) Then
            trip = Nothing
        End If

        Dim update As LoungeUpdate = Nothing

        SyncLock SyncRoot

            Dim profile As LoungeProfile = Nothing

            If Not LoungeProfiles.TryGetValue(target, profile) Then
                Return
            End If

            If String.Equals(profile.AccountName, accountName, StringComparison.Ordinal) AndAlso
               String.Equals(profile.Trip, trip, StringComparison.Ordinal) Then
                Return
            End If

            profile.AccountName = accountName
            profile.Trip = trip

            Dim memberId As Integer

            If LoungeMembers.TryGetValue(target, memberId) Then

                update = NextLoungeUpdate(LoungeKindLoungeChange)

                If update IsNot Nothing Then
                    update.Member = ToLoungeMember(memberId, profile)
                End If

            End If

        End SyncLock

        PublishLoungeUpdate(update)

    End Sub


    ' 部屋に居ながら見た目（アイコン・色）を変えた人の、ラウンジの控えの顔を書き換える
    '
    ' 戻り値 … その人がラウンジの顔の一覧に出ていれば loungeChange の差分（ふつうは出ていない＝部屋に居るので Nothing）
    '
    ' 【なぜ要るのか】LoungeProfiles は、ラウンジに繋いだ時点の Cookie で作られる。
    ' 同じブラウザでラウンジのタブを開いたまま部屋で色を変えると、ここが古いまま残り、
    ' **部屋を出た瞬間、ラウンジの顔だけ古い色に戻る**。見本は UpdateLoungeTrip（トリップの付け外し）。
    ' ラウンジに繋いでいなければ何もしない（次に繋いだときは、新しい Cookie から作られる）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function UpdateLoungeAppearanceCore(browserId As String,
                                                       iconType As String,
                                                       iconColor As String) As LoungeUpdate

        Dim target As Guid

        If Not Guid.TryParse(browserId, target) Then
            Return Nothing
        End If

        Dim profile As LoungeProfile = Nothing

        If Not LoungeProfiles.TryGetValue(target, profile) Then
            Return Nothing
        End If

        profile.IconType = iconType
        profile.IconColor = iconColor

        Dim memberId As Integer

        If Not LoungeMembers.TryGetValue(target, memberId) Then
            Return Nothing
        End If

        Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindLoungeChange)

        If update IsNot Nothing Then
            update.Member = ToLoungeMember(memberId, profile)
        End If

        Return update

    End Function


    ' そのブラウザ（Cookie の生の文字列）のラウンジの接続。読めなければ空
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function LoungeConnectionsOfBrowserIdCore(browserId As String) As List(Of String)

        Dim browser As Guid

        If Not Guid.TryParse(browserId, browser) OrElse browser = Guid.Empty Then
            Return New List(Of String)
        End If

        Return LoungeConnectionsOfCore(browser)

    End Function


    ' 部屋に居る人が見た目を変えたことを、ラウンジへ配る差分にする
    '
    ' 戻り値 … 配る差分。ラウンジに誰も居なければ Nothing
    '
    ' 【必ず SyncRoot の中で、参加者の見た目を書き換えた直後に呼ぶこと】（番号を書き換えた順に振るため）
    Private Shared Function NextLoungeOccupantChangeUpdate(roomId As Integer,
                                                           participant As Participant) As LoungeUpdate

        Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindOccupantChange)

        If update IsNot Nothing Then
            update.RoomId = roomId
            update.Occupant = ToLoungeOccupant(participant)
        End If

        Return update

    End Function


    ' 部屋に1人入ったことを、ラウンジへ配る差分にする
    '
    ' quiet … 同じ部屋での2窓目の置き換えなら True（LoungeUpdate.Quiet を参照）
    '
    ' 戻り値 … 配る差分。ラウンジに誰も居なければ Nothing（配る相手が居ないので、作らない）
    '
    ' 【必ず SyncRoot の中で、参加者リストを書き換えた直後に呼ぶこと】
    ' 通し番号を、書き換えた順に振るため。配るのは SyncRoot を抜けてから（PublishLoungeUpdates）
    Private Shared Function NextLoungeJoinUpdate(roomId As Integer,
                                                 participant As Participant,
                                                 quiet As Boolean) As LoungeUpdate

        Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindJoin)

        If update IsNot Nothing Then
            update.RoomId = roomId
            update.Occupant = ToLoungeOccupant(participant)
            update.Quiet = quiet
        End If

        Return update

    End Function


    ' 部屋から1人出たことを、ラウンジへ配る差分にする（呼び方は NextLoungeJoinUpdate と同じ）
    Private Shared Function NextLoungeLeaveUpdate(roomId As Integer,
                                                  localId As Integer,
                                                  quiet As Boolean) As LoungeUpdate

        Dim update As LoungeUpdate = NextLoungeUpdate(LoungeKindLeave)

        If update IsNot Nothing Then
            update.RoomId = roomId
            update.LocalId = localId
            update.Quiet = quiet
        End If

        Return update

    End Function


    ' 番号を1つ進めて、今のラウンジの人数を載せた差分を作る（中身は呼び出し側が入れる）
    '
    ' 人数は LoungeMembers の数。「ラウンジに居る人」を書き換えてから呼ぶこと
    ' （SyncLoungeMembers がそうしている。部屋の差分は、そのあとの SyncLoungeMembers より前に作るので、
    '   部屋へ入った人がまだラウンジに数えられている時点の人数が載る。続く loungeLeave で1つ減る）
    '
    ' 【ラウンジに誰も居ないときは、番号も進めずに Nothing を返す】
    ' 配る相手が居ないので作らない。番号が飛ばないので、あとから来た人は丸ごとを受け取るだけで済む
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function NextLoungeUpdate(kind As String) As LoungeUpdate

        If LoungeConnections.Count = 0 Then
            Return Nothing
        End If

        LoungeVersion += 1

        Return New LoungeUpdate With {
            .Version = LoungeVersion,
            .Kind = kind,
            .LoungeCount = LoungeMembers.Count
        }

    End Function


    ' 在室者1人ぶんを、ラウンジへ渡してよい形にする
    Private Shared Function ToLoungeOccupant(p As Participant) As LoungeOccupantInfo

        Return New LoungeOccupantInfo With {
            .LocalId = p.LocalId,
            .Nickname = p.Nickname,
            .IconType = p.IconType,
            .IconColor = p.IconColor,
            .Trip = p.Trip,
            .AccountName = p.AccountName,
            .JoinSeq = p.JoinSeq
        }

    End Function


    ' ラウンジのページが閉じられる・移られるときに、LeaveBeacon.aspx（lounge=1）から呼ばれる
    ' （部屋の LeaveByConnectionId と同じ形）
    '
    ' 【なぜ、これが要るのか（本番の実機で見つかった不具合）】
    ' ラウンジから部屋ではないページ（説明書など）へ移ると、本番の Chrome では
    ' **2分以上たってもラウンジの人数が減らなかった**（両方向で同じ。タブを閉じたときは約35秒で減った）。
    ' Chrome は、戻るボタンですぐ戻れるように、前のページを**凍らせたまま保持する**
    ' （bfcache。ページを離れる合図＝unload も許されない）。そのとき SignalR の接続も
    ' 生きたまま一緒に保持されるので、サーバーからは「まだラウンジに居る」ようにしか見えない。
    ' 部屋で同じことが起きないのは、部屋はページを離れるときに退室のビーコンを送っているから。
    ' ラウンジにも同じ形の合図を付けた（画面側は Scripts/lounge-live.js の pagehide）
    '
    ' 【本人確認は、接続IDと Cookie の両方】（LeaveByConnectionId と同じ理由）
    ' 接続IDだけだと、他人の接続IDを送りつけて、その人をラウンジの人数から消せてしまう。
    ' Cookie だけだと、同じブラウザの別のタブ（まだ開いているラウンジ）まで消してしまう。
    ' 両方が一致する接続だけを外す
    '
    ' 【人数から外し、ラウンジのグループからも外す】
    ' 「グループにいる接続」と「LoungeConnections にいる接続」を、いつも同じにしておくため。
    ' 差分を作るかどうかは LoungeConnections の数で決めている（NextLoungeUpdate）ので、
    ' グループにだけ残ると、その接続には差分が届いたり届かなかったりする
    ' （開発機で、自分のビーコンで外された画面の数字が古いまま残るのを見た）。
    ' 凍ったページに差分を送り続けても、どのみち使われない。
    ' 戻るボタンで同じページが戻ってきたら、画面が joinLounge を呼び直して、グループにも数にも入り直す
    Public Shared Sub LeaveLoungeByConnectionId(connectionId As String, browserId As String)

        Dim target As Guid

        If String.IsNullOrEmpty(connectionId) OrElse Not Guid.TryParse(browserId, target) Then
            Return
        End If

        Dim updates As List(Of LoungeUpdate)

        SyncLock SyncRoot

            Dim stored As Guid

            If Not LoungeConnections.TryGetValue(connectionId, stored) OrElse stored <> target Then
                ' 見つからない・本人ではない。前者は正常にも起こる（切断の処理が先に済んだ、など）
                Return
            End If

            RemoveLoungeConnectionCore(connectionId)

            updates = SyncLoungeMembers()

        End SyncLock

        ' SignalR の呼び出しは、鍵を抜けてから（戻り値の Task は待たない。RemoveParticipantAndNotify と同じ）
        Try
            Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()
            hubContext.Groups.Remove(connectionId, LoungeGroupName)
        Catch ex As Exception
            ' 外せなくても、人数からは外れている
            ' （ErrorReport へは知らせない。接続が消えかけている相手なので、失敗しうる場面である）
        End Try

        PublishLoungeUpdates(updates)

    End Sub


    ' ラウンジの接続が切れたときに、人数から外す（OnDisconnected から呼ばれる）
    '
    ' ラウンジの接続でなければ何もしない（部屋の接続も、ここを通る）
    Private Shared Sub LeaveLounge(connectionId As String)

        Dim updates As List(Of LoungeUpdate)

        SyncLock SyncRoot

            If Not RemoveLoungeConnectionCore(connectionId) Then
                Return
            End If

            updates = SyncLoungeMembers()

        End SyncLock

        PublishLoungeUpdates(updates)

    End Sub


    ' そのブラウザを、ラウンジの人数から外す（ログアウトのときに LeaveByBrowserId から呼ばれる）
    '
    ' ログアウトしたタブはラウンジを離れるが、その接続がすぐに切れるとは限らない
    ' （CurrentLoungeBrowsers のコメント）。そのあいだ前の身元のまま数えると、
    ' 入り直した新しい身元と2人に見える。部屋の席を外すのと同じ時点で、ここも外す
    ' （ページを離れる合図＝LeaveLoungeByConnectionId も飛ぶが、ログアウトの応答より
    '   後に届くと Cookie がもう新しくなっていて本人確認が通らない。こちらが確実）
    '
    ' 【部屋の席より先に外す】（LeaveByBrowserId の呼ぶ順）
    ' 部屋の席を先に外すと、その瞬間「ラウンジを開いていて、部屋に居ない人」になり、
    ' ほかの人のラウンジに顔が一瞬現れて、すぐ消える
    Private Shared Sub LeaveLoungeByBrowser(target As Guid)

        Dim updates As List(Of LoungeUpdate)

        SyncLock SyncRoot

            Dim keys As List(Of String) = LoungeConnections.Where(
                Function(entry) entry.Value = target).Select(
                Function(entry) entry.Key).ToList()

            If keys.Count = 0 Then
                Return
            End If

            ' 接続そのものはグループに残る（切れるまで差分は届くが、害は無い）。
            ' 人数に数えないよう、ブラウザだけ「読めなかった」扱いにし、顔も捨てる
            For Each key In keys
                LoungeConnections(key) = Guid.Empty
            Next

            LoungeProfiles.Remove(target)

            updates = SyncLoungeMembers()

        End SyncLock

        PublishLoungeUpdates(updates)

    End Sub


    ' 差分を、ラウンジのグループへ配る
    '
    ' 【必ず SyncRoot を抜けてから呼ぶこと】（SignalR の呼び出しを鍵の中へ持ち込まない）。
    ' 鍵の外で配るので、2つの差分が番号と逆の順に届くことがある。
    ' 画面は番号で並べ直すので、それで壊れない
    '
    ' 【配れなくても、呼び出し元（部屋の入退室）を止めない】
    ' ラウンジは、繋ぎ直したときや番号の飛びに気づいたときに、丸ごとを取り直して自分で直る
    Private Shared Sub PublishLoungeUpdates(updates As IEnumerable(Of LoungeUpdate))

        If updates Is Nothing Then
            Return
        End If

        For Each update In updates
            PublishLoungeUpdate(update)
        Next

    End Sub


    Private Shared Sub PublishLoungeUpdate(update As LoungeUpdate)

        If update Is Nothing Then
            Return
        End If

        Try
            Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()
            hubContext.Clients.Group(LoungeGroupName).LoungeUpdate(update)
        Catch ex As Exception
            ' 配れなくても、ラウンジは次に繋ぎ直したときに丸ごとを取り直す。黙らずに知らせる
            ErrorReport.Report(ex, "ChatHub.PublishLoungeUpdate")
        End Try

    End Sub

End Class
