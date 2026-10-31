Imports Newtonsoft.Json

' ============================================================
' ラウンジへ渡す「部屋の様子」
' ============================================================
'
' ラウンジは SignalR に繋がり、次の2つを受け取る（仕組みは ChatHub.Lounge.vb）。
'
'   LoungeSnapshot … 全部屋の在室者を丸ごと（繋いだとき・繋ぎ直したとき・食い違いに気づいたとき）
'   LoungeUpdate   … 変わった「その1人ぶん」だけ（誰かが部屋に入った・出た、ラウンジの人数が変わった）
'
' 【どちらにも version（通し番号）が付いている】
' 丸ごとの戻り値と、差分の配信は、**届く順番が決まっていない**
' （放送が戻り値より先に届く。「自分自身の情報は、
'   放送ではなく戻り値から組み立てる」の落とし穴と同じ事情）。
' 番号があれば、ラウンジの画面は「丸ごとの時点より後の差分だけ」を、番号の順に当てられる。
' 番号が飛んだら（＝どれかを取りこぼした）、自分から丸ごとを取り直す。
'
' 各プロパティに <JsonProperty> でキャメルケースのキー名を個別指定している
' （理由は ParticipantInfo.vb の冒頭と同じ。SignalR 全体の設定は変えない）
Public Class LoungeSnapshot

    ' この丸ごとが、どの差分まで含んでいるか（LoungeUpdate.Version と同じ数え方）
    <JsonProperty("version")>
    Public Property Version As Long

    ' ラウンジに居る人数（数え方は ChatHub.Lounge.vb の CurrentLoungeBrowsers。Members の数と同じ）
    <JsonProperty("loungeCount")>
    Public Property LoungeCount As Integer

    ' 在室者が1人以上いる部屋だけを並べる（居ない部屋は 0 人とみなす）
    <JsonProperty("rooms")>
    Public Property Rooms As List(Of LoungeRoomOccupants)

    ' ラウンジに居る人の顔（来た順）。
    ' 人数（LoungeCount）と同じ数え方の人たちで、数は LoungeCount と必ず一致する
    <JsonProperty("members")>
    Public Property Members As List(Of LoungeMemberInfo)

    ' この画面の持ち主（ブラウザ）が今入っている部屋（居なければ 0）。
    ' 画面は、その部屋の入室ボタンを「入室中」にする。以後の変化は loungeSelfRoom の配信で届く
    <JsonProperty("myRoomId")>
    Public Property MyRoomId As Integer

    ' 上の値の通し番号（loungeSelfRoom と同じ数え方。画面は大きいほうだけを信じる）
    <JsonProperty("myRoomSeq")>
    Public Property MyRoomSeq As Long

    ' この接続の持ち主の顔。ラウンジの上のプロフィールの枠を合わせるのに使う。
    ' 控え（LoungeProfiles）が無ければ送らない（画面はサーバーが描いた枠のまま）。番号は myRoomSeq（loungeSelfFace と同じ数え方）
    <JsonProperty("myIconType", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MyIconType As String

    <JsonProperty("myIconColor", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MyIconColor As String

End Class


' 1つの部屋の在室者
Public Class LoungeRoomOccupants

    <JsonProperty("roomId")>
    Public Property RoomId As Integer

    ' 入った順（部屋の参加者一覧と同じ並び）
    <JsonProperty("occupants")>
    Public Property Occupants As List(Of LoungeOccupantInfo)

    ' 管理人の、部屋の中の番号（居ない・固定部屋なら 0）。顔に「管理人」の印を付ける
    <JsonProperty("ownerLocalId")>
    Public Property OwnerLocalId As Integer

End Class


' ラウンジへ渡してよい、在室者1人ぶんの情報
'
' 【ParticipantInfo を使い回さない理由】
' あちらには匿名ID（AnonId）が入っている。無視機能のための印で、ラウンジには要らない。
' 要らないものは渡さない（部屋の外の人に、部屋の中の印を配らない）。
'
' 使うのは人数だけだが、名前・アイコン・トリップも今から載せてある。
' 顔を出すときに、ハブを触らずに済むようにするため
' （サーバーからクライアントへの配信は、あとから中身を足しても壊れないが、
'   同じ回に2度ハブを触る理由も無い）
Public Class LoungeOccupantInfo

    ' 部屋の中の番号。差分（出た）で「どの人か」を指すのに使う
    <JsonProperty("localId")>
    Public Property LocalId As Integer

    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    ' トリップ（無ければキーごと送らない。ParticipantInfo と同じ）
    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

    ' アカウントのユーザー名（アカウントID を表示して参加している人だけ。無ければキーごと送らない）
    '
    ' 画面は「@ユーザー名」と出し、押すと公開のプロフィールが開く。**トリップとは、どちらか一方**
    ' （アカウントID を出している人の Trip は、必ず Nothing。決めるのは Chat/ChatAccountLink.vb）。
    ' 渡すのはユーザー名だけ——アカウントの番号・メールアドレスは渡さない（ユーザー名は、もともと公開の情報）
    <JsonProperty("accountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property AccountName As String

    ' 入室した順の番号。ラウンジの顔も、部屋の一覧と同じ順に並べる
    <JsonProperty("joinSeq")>
    Public Property JoinSeq As Long

End Class


' ラウンジに居る人1人ぶん
'
' 部屋の在室者（LoungeOccupantInfo）と同じ中身で、番号だけが違う。
'   memberId … ラウンジの中の番号。ラウンジに現れたときに振り、居なくなったら捨てる
'             （部屋の LocalId と同じ欠番方式。部屋へ行って戻ってきたら、新しい番号になる）
'
' 【ブラウザの識別子（ChatBrowserId）は渡さない】あれは本人証明そのもの
' （知れば他人を部屋から追い出せる）。ラウンジの中だけで通じる番号を別に振っている
Public Class LoungeMemberInfo

    <JsonProperty("memberId")>
    Public Property MemberId As Integer

    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    ' トリップ（無ければキーごと送らない。LoungeOccupantInfo と同じ）
    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

    ' アカウントのユーザー名（アカウントID を表示して参加している人だけ。無ければキーごと送らない）
    '
    ' 画面は「@ユーザー名」と出し、押すと公開のプロフィールが開く。**トリップとは、どちらか一方**
    ' （アカウントID を出している人の Trip は、必ず Nothing。決めるのは Chat/ChatAccountLink.vb）。
    ' 渡すのはユーザー名だけ——アカウントの番号・メールアドレスは渡さない（ユーザー名は、もともと公開の情報）
    <JsonProperty("accountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property AccountName As String

End Class


' ラウンジに出す、部屋1つぶんの情報
'
' 「部屋ができた」（roomCreated）の差分に載せる。ラウンジの画面は、これで部屋の行を1つ作る。
'
' 【載せるのは、一覧に出すものだけ】
' パスワードのハッシュ・管理人のブラウザ（OwnerBrowserId）は絶対に載せない。
' 前者は秘密、後者は本人証明そのものである（ラウンジの顔に MemberId を振っているのと同じ考え方）。
' ノックの可否は、足した（満室のときに入室ボタンを「ノック」にするため）
Public Class LoungeRoomInfo

    <JsonProperty("roomId")>
    Public Property RoomId As Integer

    <JsonProperty("roomName")>
    Public Property RoomName As String

    <JsonProperty("maxUsers")>
    Public Property MaxUsers As Integer

    ' パスワード付きの部屋か（一覧で部屋名の前に鍵の印を出す）
    '
    ' 【載せるのは「付いているか」だけ】パスワードのハッシュは、絶対にここへ載せない
    <JsonProperty("hasPassword")>
    Public Property HasPassword As Boolean

    ' ノックを受け付ける部屋か。
    ' 満室のとき、ラウンジの入室ボタンを「ノック」に差し替える（Scripts/lounge-live.js の renderFull）。
    ' 固定部屋は、DBの値にかかわらず受け付けない（管理人が居ないため。ChatHub.Knock.vb）
    <JsonProperty("knockEnabled")>
    Public Property KnockEnabled As Boolean

End Class


' ラウンジへ配る差分1つ
'
' kind の種類（ChatHub.Lounge.vb の LoungeKind* 定数）
'   "join"         … roomId の部屋に occupant が入った
'   "leave"        … roomId の部屋から localId の人が出た
'   "loungeJoin"   … ラウンジに member が現れた
'   "loungeLeave"  … ラウンジから memberId の人が居なくなった
'   "loungeChange" … ラウンジに居る member の中身が変わった（トリップを付け外しした）
'   "roomCreated"  … 利用者が部屋を作った（room に部屋1つぶんが入る）
'   "roomClosed"   … 部屋が閉じた（roomId。誰も居なくなった一時部屋）
'   "roomOwner"    … roomId の部屋の管理人が、localId の人になった（顔の「管理人」の印を付け替える）
'   "occupantChange" … roomId の部屋に居る occupant が、見た目（アイコン・色）を変えた（同じ localId の人を置き換える）
'   "roomChanged"  … roomId の部屋の設定を管理人が変えた（room に部屋名・定員・鍵の有無・ノック受付が入る）
'
' 【kind ではない、ラウンジへの配信】（loungeUpdate を通らない。**その画面の持ち主だけ**に届く＝ほかの人には配らない）
'   loungeSelfRoom(roomId, seq)             … この画面の持ち主が今入っている部屋（その部屋のボタンを「入室中」に）
'   loungeSelfFace(iconType, iconColor, seq) … この画面の持ち主の顔（上のプロフィールの枠を合わせる）
'   knockAnswered(answer)                    … この画面の持ち主のノックの行く末
'   どれも丸ごと（LoungeSnapshot の myRoomId・myRoomSeq・myIconType・myIconColor）と順番が入れ替わりうるので、
'   画面は番号（seq）の大きいほうだけを信じる（ノックの行く末は番号を持たない）。
'   **ほかの人にも配ってよいものは kind、持ち主だけのものは別の口**、と分けてある
'
' 【12-1〜は "lounge"（ラウンジの人数だけが変わった）があった】
' 顔を出すようにしてからは、人数が変わる＝誰かが現れた・居なくなったなので、
' "loungeJoin" と "loungeLeave" がその役を引き継いだ（もう作らない）。
' 古い画面は、新しい3つを「知らない kind」として人数だけ受け取るので、壊れない
'
' 【種類を増やすときは、kind を1つ足すだけ】（部屋作成＝15番の「部屋ができた」「部屋が閉じた」など）
' 配信の口（クライアントの loungeUpdate）は1つのまま増やさない。
' 知らない kind を受け取った古い画面は、番号だけ進めて中身を捨てる（Scripts/lounge-live.js）
Public Class LoungeUpdate

    ' 通し番号。1ずつ増える（アプリが再起動すると 0 からやり直す。画面は丸ごとで番号ごと置き換える）
    <JsonProperty("version")>
    Public Property Version As Long

    <JsonProperty("kind")>
    Public Property Kind As String

    <JsonProperty("roomId", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property RoomId As Integer?

    ' "join" のときだけ
    <JsonProperty("occupant", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Occupant As LoungeOccupantInfo

    ' "leave"・"roomOwner" のときだけ
    <JsonProperty("localId", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property LocalId As Integer?

    ' "loungeJoin"・"loungeChange" のときだけ
    <JsonProperty("member", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Member As LoungeMemberInfo

    ' "loungeLeave" のときだけ
    <JsonProperty("memberId", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MemberId As Integer?

    ' "roomCreated" のときだけ。"roomClosed" は roomId だけで足りる
    <JsonProperty("room", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Room As LoungeRoomInfo

    ' 同じブラウザの2窓目による、同じ部屋での置き換え（部屋の種別33とその直後の入室）なら True。
    '
    ' 部屋の中ではこの2つの行を出さない（外から見れば、その人はずっとそこにいる）。
    ' ラウンジも同じ扱いにできるよう、印だけ付けておく
    ' （入退室の音を鳴らすとき、これが True の差分では鳴らさない）。
    ' False のときはキーごと送らない
    <JsonProperty("quiet", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Quiet As Boolean

    ' この差分を当てた時点の、ラウンジの人数（どの種類の差分にも必ず載せる）
    '
    ' 【人数は、部屋の出入りでも変わる】
    ' ラウンジの人数は「部屋に居ないブラウザ」だけを数えるため、
    ' ラウンジを開いたままの人が部屋へ入ると、それだけで1人減る
    <JsonProperty("loungeCount")>
    Public Property LoungeCount As Integer

End Class
