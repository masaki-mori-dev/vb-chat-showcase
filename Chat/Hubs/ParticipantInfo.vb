Imports Newtonsoft.Json

' 参加者一覧をクライアントへ返す際に使う、公開してよい情報だけを持つクラス
' Participantクラスとは違い、ConnectionId・BrowserId・LastActiveAtなど、
' サーバー内部でしか使わない情報は含めない
' 各プロパティに<JsonProperty>でJS側と同じキャメルケースのキー名を個別指定している
' （SignalR全体に影響するグローバルなシリアライザ設定は、SignalR自身の内部プロトコルの
'   キー名（H/M/A/I/R等）まで巻き込んで壊してしまうため、採用しない方針とした）
Public Class ParticipantInfo

    <JsonProperty("localId")>
    Public Property LocalId As Integer

    <JsonProperty("nickname")>
    Public Property Nickname As String

    <JsonProperty("iconType")>
    Public Property IconType As String

    <JsonProperty("iconColor")>
    Public Property IconColor As String

    ' 「同じ人かどうか」だけが分かる、使い捨ての印（AnonIdクラスを参照）
    '
    ' 無視機能で、クライアントが一覧の相手と発言の行を結び付けるために使う。
    ' 生のBrowserIdは絶対に渡さない（なりすましに使えるため）
    <JsonProperty("anonId")>
    Public Property AnonId As String

    ' トリップ（16進10桁。無ければ Nothing）
    '
    ' 【無いときは、キーごと送らない】（NullValueHandling.Ignore）
    ' トリップ機能が止まっているときは「一切渡さない」と決めてあり、
    ' 付けていない人のぶんも "trip":null を並べる必要が無い
    <JsonProperty("trip", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Trip As String

    ' アカウントのユーザー名（アカウントID を表示して参加している人だけ。無ければキーごと送らない）
    '
    ' 画面は「@ユーザー名」と出し、押すと公開のプロフィールが開く。**トリップとは、どちらか一方**
    ' （アカウントID を出している人の Trip は、必ず Nothing。決めるのは Chat/ChatAccountLink.vb）。
    ' 渡すのはユーザー名だけ——アカウントの番号・メールアドレスは渡さない（ユーザー名は、もともと公開の情報）
    <JsonProperty("accountName", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property AccountName As String

    ' 入室した順の番号（Participant.JoinSeq）。画面はこれで一覧を並べる
    <JsonProperty("joinSeq")>
    Public Property JoinSeq As Long

End Class
