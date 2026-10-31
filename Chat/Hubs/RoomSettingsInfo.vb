Imports Newtonsoft.Json

' 部屋の5つの設定。部屋の画面とやり取りする形
'
' 部屋名・定員・ノック受付・画像の可否・鍵（パスワード）の有無。
' **パスワードそのもの・ハッシュ・暗号文は、絶対に載せない**（有無だけ）。部屋の全員へ配る形なので。
' 管理人にだけ見せるパスワードは、別の口（ChatHub.GetRoomPassword → RoomPasswordRevealResult）で、押されたときに1回だけ返す
Public Class RoomSettingsInfo

    <JsonProperty("roomName")>
    Public Property RoomName As String

    <JsonProperty("maxUsers")>
    Public Property MaxUsers As Integer

    <JsonProperty("knockEnabled")>
    Public Property KnockEnabled As Boolean

    <JsonProperty("imageEnabled")>
    Public Property ImageEnabled As Boolean

    <JsonProperty("hasPassword")>
    Public Property HasPassword As Boolean

End Class


' GetRoomPassword の戻り値（管理人にだけ、押されたときに返す。配信はしない）
Public Class RoomPasswordRevealResult

    <JsonProperty("ok")>
    Public Property Ok As Boolean

    ' 今のパスワード（ok のときだけ）
    <JsonProperty("password", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Password As String

    ' 見せられない理由（管理人でない・鍵がかかっていない・表示できない）
    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

End Class


' ChangeRoomSettings の戻り値（SendMessageResult・ChangeAppearanceResult と同じ考え方＝呼び出しと1対1で結果が分かる）
Public Class RoomSettingsResult

    ' 受け付けたか（変わったところが無かったときも True。そのときは Changed が False で、誰にも配っていない）
    <JsonProperty("ok")>
    Public Property Ok As Boolean

    ' 何か変わったか（DB を書き換え、記録を残し、部屋とラウンジへ配った）
    <JsonProperty("changed")>
    Public Property Changed As Boolean

    ' 断った理由（管理人でない・部屋名が空・定員が一覧に無い・パスワードが長すぎる、など）。受け付けたら Nothing
    <JsonProperty("message", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Message As String

    ' 続けて保存しすぎたとき、次に保存できるまでのミリ秒（0 なら連打ではない）
    <JsonProperty("retryAfterMs", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property RetryAfterMs As Integer

    ' 受け付けたあとの設定（画面はこれで欄を揃える）。断ったときは Nothing
    <JsonProperty("settings", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Settings As RoomSettingsInfo

End Class
