Imports Newtonsoft.Json

' ============================================================
' ノックで画面とやり取りする形
' ============================================================
'
' 仕組みは Chat/Hubs/ChatHub.Knock.vb。ここは「画面へ渡してよい形」だけを持つ。
'
' 【ブラウザの識別子（ChatBrowserId）は、どこにも載せない】あれは本人証明そのもの。
' ノックは、サーバーの中だけで通じる番号（KnockId）で指す（ラウンジの顔の MemberId と同じ考え方）。
'
' 各プロパティに <JsonProperty> でキャメルケースのキー名を個別指定している
' （理由は ParticipantInfo.vb の冒頭と同じ。SignalR 全体の設定は変えない）


' 管理人の画面へ出す、ノック1つぶん（KnockRequested の配信と、JoinRoomResult.PendingKnocks）
Public Class KnockRequestInfo

    ' このノックの番号（許可・断るときに、これで指す）
    <JsonProperty("knockId")>
    Public Property KnockId As Integer

    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    ' トリップ（無ければキーごと送らない）
    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

    ' アカウントのユーザー名（アカウントID を表示して参加している人だけ。無ければキーごと送らない）
    '
    ' 画面は「@ユーザー名」と出し、押すと公開のプロフィールが開く。**トリップとは、どちらか一方**
    ' （アカウントID を出している人の Trip は、必ず Nothing。決めるのは Chat/ChatAccountLink.vb）。
    ' 渡すのはユーザー名だけ——アカウントの番号・メールアドレスは渡さない（ユーザー名は、もともと公開の情報）
    <JsonProperty("accountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property AccountName As String

    ' ノックした人の、この部屋での匿名ID（AnonId）。
    ' 管理人がその人を無視していたら、画面は小窓を出さない（無視機能の決まりに乗せる）。
    ' 部屋に居ない人でも、同じブラウザなら部屋に居たときと同じ値になる（AnonId は部屋とブラウザから作る）
    <JsonProperty("anonId")>
    Public Property AnonId As String

    ' 決めるまでの残りの秒数（画面が数え下げる）
    <JsonProperty("seconds")>
    Public Property Seconds As Integer

End Class


' ノックした人へ返すもの（ChatHub.KnockRoom の戻り値）
'
' status の種類（KnockStatus* 定数。画面＝Scripts/lounge-live.js と同じ綴り）
'   "waiting"  … ノックを受け付けた。管理人の返事を待つ（seconds＝残りの秒数）
'   "approved" … もう許可されている（席を取り置いてある。seconds＝取り置きの残りの秒数）
'   "notFull"  … 満室ではない。ノックせずに、そのまま入ればよい
'   "rejected" … ノックできなかった（message に理由）
Public Class KnockResult

    <JsonProperty("status")>
    Public Property Status As String

    <JsonProperty("roomId")>
    Public Property RoomId As Integer

    ' 部屋名（許可されたときの小窓に出す。画面は textContent で入れる）
    <JsonProperty("roomName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property RoomName As String

    <JsonProperty("seconds")>
    Public Property Seconds As Integer

    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

End Class


' ノックの行く末を、ノックした人のラウンジへ知らせるもの（KnockAnswered の配信）
'
' status の種類
'   "approved"    … 許可された（seconds＝取り置きの残りの秒数）
'   "denied"      … 断られた
'   "expired"     … 管理人が時間内に決めなかった
'   "cancelled"   … 管理人が替わった・部屋が閉じたので、取り消された
'   "seatExpired" … 許可されたが、取り置きの時間内に入らなかった
Public Class KnockAnswer

    <JsonProperty("status")>
    Public Property Status As String

    <JsonProperty("roomId")>
    Public Property RoomId As Integer

    <JsonProperty("roomName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property RoomName As String

    <JsonProperty("seconds")>
    Public Property Seconds As Integer

End Class


' 管理人が許可・断ったときの戻り値（ChatHub.AnswerKnock）
'
'   ok = True  … 受け付けた。画面は小窓を消す
'   ok = False … 受け付けなかった。
'                  keep = True なら小窓を残して message を出す（臨時の席がいっぱい、など。あとで断れる）
'                  keep = False なら小窓を消す（もう無効なノック、管理人ではない、など）
Public Class AnswerKnockResult

    <JsonProperty("ok")>
    Public Property Ok As Boolean

    <JsonProperty("keep")>
    Public Property Keep As Boolean

    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

End Class
