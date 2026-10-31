Imports Newtonsoft.Json

' 管理人の道具（指名してゆずる・退室させる・入室禁止にする・解除する）とやり取りする形
'
' 呼び出しと1対1で結果が分かる形（SendMessageResult・RoomSettingsResult と同じ考え方）。
' 断ったときは ok = False と理由（message）。画面は理由を1行で出す
Public Class OwnerActionResult

    <JsonProperty("ok")>
    Public Property Ok As Boolean

    ' 断った理由（管理人でない・相手がもう居ない・自分を選んだ、など）。受け付けたら Nothing
    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

End Class


' 「入室禁止にした人」の一覧の1人ぶん（部屋の設定の欄に出す）
'
' 【ChatBrowserId そのものは、絶対に載せない】本人証明そのもの（知れば、その人になりすませる）。
' 画面から「この人の禁止を解除して」と指すのは、匿名ID（AnonId。部屋ごとのハッシュ）で行う。
' 参加者一覧・無視・こっそりが使っているのと同じ値で、部屋に居たときのその人と同じになる
Public Class RoomBanInfo

    <JsonProperty("anonId")>
    Public Property AnonId As String

    ' 禁止にした時点の名前・見た目（ChatRoomBans の行に焼き込んだもの）
    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

End Class


' GetRoomBans・LiftRoomBan の戻り値
Public Class RoomBansResult

    <JsonProperty("ok")>
    Public Property Ok As Boolean

    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

    ' 今の一覧（禁止にした順）。断ったときは Nothing
    <JsonProperty("bans", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Bans As List(Of RoomBanInfo)

End Class
