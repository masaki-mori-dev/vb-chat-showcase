Imports Newtonsoft.Json

' JoinRoomの戻り値。呼び出し元が「自分のLocalId」と「既にその部屋にいた、
' 他の参加者一覧」の両方を、1回のやり取りでまとめて受け取れるようにするためのクラス
' 各プロパティに<JsonProperty>でJS側と同じキャメルケースのキー名を個別指定している
' （SignalR全体に影響するグローバルなシリアライザ設定は、SignalR自身の内部プロトコルの
'   キー名（H/M/A/I/R等）まで巻き込んで壊してしまうため、採用しない方針とした）
Public Class JoinRoomResult

    <JsonProperty("myLocalId")>
    Public Property MyLocalId As Integer

    <JsonProperty("existingParticipants")>
    Public Property ExistingParticipants As List(Of ParticipantInfo)

    ' 自分自身の「入室しました」の文言
    '
    ' 【なぜ、戻り値に載せるのか】
    ' 入室の放送（UserJoined）はClients.OthersInGroupで送っており、自分には届かない。
    ' そのため自分の入室行だけは、クライアントがこの戻り値をもとに自前で描画する。
    ' それでも文言はサーバー側で作る（クライアントで組み立てると、同じ日本語を
    ' 2か所で管理することになるため。詳細はChatEventTextのコメント）
    '
    ' 引き継ぎ(b)（F5で戻ってきた・再接続した）の場合はNothingになる。
    ' 新規の入室ではないため、入室行を出してはいけない
    <JsonProperty("myJoinEventText")>
    Public Property MyJoinEventText As String

    ' 入室を断った理由（例：「この部屋は満員です（20/20）」）
    '
    ' 入室できた場合はNothing。値が入っているときは、他のプロパティは意味を持たない
    ' （参加者リストにも追加されておらず、グループにも入っていない）。
    ' クライアントは、まずこの値を見て、入っていればラウンジへ引き返すこと
    '
    ' 【なぜ、例外ではなく戻り値にするのか】
    ' 例外にすると、クライアント側では「サーバーが落ちている」等の本当の失敗と
    ' 区別が付かず、再接続の処理まで巻き込むことになる。
    ' 満員は異常ではなく、正常に起こりうる結果であるため、戻り値として返す。
    ' 文言をそのまま渡すのは、退室理由（Kicked）の伝え方に揃えるため
    ' （利用者に「気づいたら戻されていた」と思わせないこと）
    <JsonProperty("rejectReason")>
    Public Property RejectReason As String

    ' 入室を断ったときに、利用者を戻す先のURL
    '
    ' 【なぜ、行き先までサーバーが決めるのか】
    ' 断った理由を知っているのはサーバーなので、どこへ戻すべきかもサーバーが知っている。
    '   ・満員、終了した部屋   … ラウンジ（他の部屋を選び直せる）
    '   ・名前が無い、Cookieが壊れている … トップページ（決め直す画面）
    ' クライアントに文言から察させると、断る理由を増やすたびにJS側の分岐が増え、
    ' そのうち必ず食い違う
    <JsonProperty("rejectRedirectUrl")>
    Public Property RejectRedirectUrl As String

    ' 自分自身の匿名ID（AnonIdクラスを参照）
    '
    ' 【何に使うか】自分を無視できないようにするため。
    ' 一覧で自分を選んでも「非表示にする」を押せない形にする
    <JsonProperty("myAnonId")>
    Public Property MyAnonId As String

    ' このブラウザは、直前までこの部屋に居たか
    '
    ' 【何に使うか】無視リスト（sessionStorage）を、維持するか消すかの判定。
    '   True  … F5・再接続・2窓目の置き換え → **維持する**
    '           （理由：F5 で無視が外れたら意味がない）
    '   False … ラウンジからの新規入室・別の部屋からの移動 → **リセットする**
    '
    ' 【なぜ、クライアント側だけでは判定できないのか】
    ' F5も内部的には入室処理を通るため、素直に「入室したらリセット」にすると
    ' F5で無視が消える。サーバーしか区別が付かない
    '
    ' 【中身は Not isNewOccupant】
    ' 「同じBrowserIdの参加者が、既にこの部屋に居た」ということ。
    ' (a)同じ部屋での置き換え と (b)引き継ぎ の両方が、こちら側に入る。
    '
    ' 当初の設計では isTakeOver OrElse isSameRoomReplacement と書いていたが、
    ' **isNewOccupant の否定とまったく同じ値**であり、しかもあちらは
    ' **参加者リストを書き換える前に確定している**ぶん素直なので、そちらを使う
    ' （5つの場合を突き合わせて確認した）。
    '
    ' 【罠】MyJoinEventText Is Nothing と等価になる場面が多いが、それに乗らないこと。
    ' 「入室行を出すか」と「無視リストを消すか」は別の判断であり、
    ' 乗せると片方の都合でもう片方が黙って変わる
    <JsonProperty("isContinuation")>
    Public Property IsContinuation As Boolean

    ' 自分自身のトリップ（無ければ Nothing。そのときはキーごと送らない）
    '
    ' 【なぜ、戻り値に載せるのか】
    ' 参加者一覧の自分の行は、クライアントが Cookie の名前・アイコンから自前で組み立てる
    ' （「自分自身の情報は、放送ではなく戻り値から組み立てる」）。
    ' ところがトリップだけは、ソルトがサーバーの中にしか無いので、クライアントでは計算できない
    <JsonProperty("myTrip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MyTrip As String

    ' 自分自身のアカウントのユーザー名（アカウントID を表示して入った人だけ。無ければキーごと送らない）
    '
    ' 【これも、戻り値でしか渡せない】ログインの札（AccountAuth）も名前の札も HttpOnly で、JS からは読めない。
    ' 「自分の名前の下に @ユーザー名 が出ているか」は、サーバーが決めた結果をここで教わる。
    ' MyTrip とは、どちらか一方（両方入ることは無い）
    <JsonProperty("myAccountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MyAccountName As String

    ' 返事待ちのノック（自分がこの部屋の管理人のときだけ）
    '
    ' 【なぜ、戻り値に載せるのか】ノックの小窓は、届いた瞬間の配信（KnockRequested）で出す。
    ' 管理人が F5 した・通信が切れて繋ぎ直した間に届いたノックは、その配信を受け取れていない。
    ' 入り直したときにここで渡せば、残りの秒数つきで小窓が出し直せる。
    ' 管理人でなければ Nothing（キーごと送らない）。管理人で、返事待ちが無ければ空の一覧
    ' （画面は、空の一覧を受け取ったら、出ている小窓を全部消す）
    <JsonProperty("pendingKnocks", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property PendingKnocks As List(Of KnockRequestInfo)

    ' 自分の、入室した順の番号（Participant.JoinSeq）。
    ' 画面は自分を一覧の末尾ではなく、この番号の位置へ入れる（F5 で自分だけ最後へ動かないように）
    <JsonProperty("myJoinSeq")>
    Public Property MyJoinSeq As Long

    ' 今の管理人の、部屋の中の番号（居ない・固定部屋なら 0）
    '
    ' 画面は、この番号の参加者に「管理人」の印を付ける。以後の付け替えは ownerChanged の配信で届く。
    ' 【ブラウザの識別子は渡さない】LocalId は部屋の中だけの番号（ラウンジの MemberId と同じ考え方）
    <JsonProperty("ownerLocalId")>
    Public Property OwnerLocalId As Integer

End Class
