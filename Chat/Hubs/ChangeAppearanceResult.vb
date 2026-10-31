Imports Newtonsoft.Json

' ChangeAppearance（部屋に居ながら、自分のアイコンと色を変える）の戻り値
'
' 【なぜ、戻り値で返すのか】SendMessageResult と同じ理由。
' 画面は「受け付けられてから Cookie を書く」ので、呼び出しと1対1で結果が分からなければならない。
' 変えた本人の画面は、配信（appearanceChanged）ではなく、この戻り値で自分のバーと Cookie を書き換える
' （放送は戻り値より先に届くことがある。自分自身の情報は戻り値から組み立てる、の決まり）
Public Class ChangeAppearanceResult

    ' 受け付けたか（今と同じ値だったときも True。そのときは誰にも配っていない）
    <JsonProperty("accepted")>
    Public Property Accepted As Boolean

    ' 受け付けた見た目（検査を通したあとの値）。画面はこちらを Cookie に書く
    ' （送った値が一覧に無いアイコン・形の違う色なら、既定の値に落ちている）
    <JsonProperty("iconType", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property IconType As String

    <JsonProperty("iconColor", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property IconColor As String

    ' 連打で断ったとき、次に変えられるまでのミリ秒（0 なら連打ではない）。
    ' 画面はこれだけ待って、そのとき選んでいる見た目を送り直す
    <JsonProperty("retryAfterMs", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property RetryAfterMs As Integer

End Class
