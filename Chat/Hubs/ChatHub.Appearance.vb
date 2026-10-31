Imports System.Linq
Imports Microsoft.AspNet.SignalR

' ============================================================
' 部屋に居ながら、自分の見た目（アイコン・色）を変える
' ============================================================
'
' ChatHub の続き（Partial）。部屋の参加者リスト（RoomParticipants）と鍵（SyncRoot）を、そのまま使う。
'
' 【開発者が決めたこと】
'   ・全員が、部屋に居ながら自分の色とアイコンを変えられる
'   ・**名前だけは変えられないまま**。
'     名前は身元そのもの（変えたら別人になり、無視もこっそりも切れる）。色とアイコンは見た目だけで、身元に関わらない
'   ・「〇〇さんが色を変えました」のお知らせ行は出さない（会話の邪魔になるうえ、別人になるわけではない）
'   ・過去ログの色とアイコンは変わらない（発言した時点の値が焼き込まれている＝名前と同じ扱い）
'   ・参加者一覧・自分のバー・ラウンジの顔は、その場で変わる（他の人の画面でも）
'
' 【流れ】
'   1. 部屋の画面（設定パネルの「見た目」）が ChangeAppearance(iconType, iconColor) を呼ぶ
'   2. 検査（JoinRoom と同じ2本）→ 連打の歯止め（別の時計）→ 参加者の見た目を書き換える
'   3. 知らせを RoomNoticeOutbox に積み、鍵を抜けてから FlushRoomNotices で配る
'      （部屋の全員へ appearanceChanged ／ ラウンジへ occupantChange。ラウンジの控えの顔も書き換える）
'   4. 画面は、受け付けられてから Cookie を書く（サーバーと食い違わせないため）
'
' 【DBは1回も引かない】見た目の変更は記録しない（お知らせ行を出さないのと同じ判断。
' 過去ログは発言の行に、その時点の見た目が焼き込まれている）
Partial Public Class ChatHub

    ' 部屋の画面から、自分の見た目（アイコン・色）を変えるときに呼ばれる
    '
    ' 【roomId は引数で受け取らない】（SendMessage から roomId を外したのと同じ論法）。
    ' 誰が・どの部屋で、は接続IDから引く（FindParticipant と同じ引き方）。
    ' 受け取ると、ハブを直接呼んで、入っていない部屋のグループへ知らせを投げ込めることになる
    '
    ' 【身元（ChatBrowserId）も名前も受け取らない】変えられるのは、呼んだ接続の本人の見た目だけ
    '
    ' 【新しいメソッドを足すのは、破壊的変更ではない】SignalR は名前と引数の個数で解決するので、
    ' 困るのは既にあるメソッドの引数を増やすときだけ。開いたままの古いページは、そもそもこれを呼ばない
    Public Function ChangeAppearance(iconType As String, iconColor As String) As ChangeAppearanceResult

        ' === 検査は JoinRoom と同じ2本（見た目の値は、断らずに落とす） ===
        '
        ' 【鍵の外で】NormalizeIconType は IconCatalog を、NormalizeIconColor は SiteSettings を読む
        ' （どちらもキャッシュが空なら DB へ行く）。鍵の中で読むと、その間チャット全体が止まる。
        ' 間隔の設定も同じ理由で、ここで読んでおく
        Dim normalizedType As String = ChatProfileValidator.NormalizeIconType(iconType)
        Dim normalizedColor As String = ChatProfileValidator.NormalizeIconColor(iconColor)
        Dim interval As TimeSpan = SiteSettings.AppearanceChangeInterval

        Dim result As New ChangeAppearanceResult()

        SyncLock SyncRoot

            ' 呼んだ接続が、どの部屋の誰か（FindParticipant と同じ引き方。あちらは自分で鍵を取るので、中身を写してある）
            Dim roomId As Integer
            Dim participant As Participant = Nothing

            If ConnectionRooms.TryGetValue(Context.ConnectionId, roomId) AndAlso
               RoomParticipants.ContainsKey(roomId) Then

                participant = RoomParticipants(roomId).FirstOrDefault(
                    Function(p) p.ConnectionId = Context.ConnectionId)

            End If

            ' 部屋に居ない（JoinRoom より前・もう退室した・アプリが起き直した、など）。
            ' 画面の操作では起こらないので、利用者に見せる文言は無い（SendMessage と同じ）
            If participant Is Nothing Then
                Return result
            End If

            ' 今と同じなら、何もしない（時計も進めない・誰にも配らない）。
            ' 画面が同じ値を2度送ってきた（送り直し・連打の取りこぼし）ときの受け皿
            If String.Equals(participant.IconType, normalizedType, StringComparison.Ordinal) AndAlso
               String.Equals(participant.IconColor, normalizedColor, StringComparison.OrdinalIgnoreCase) Then

                result.Accepted = True
                result.IconType = participant.IconType
                result.IconColor = participant.IconColor
                Return result

            End If

            ' === 連打の歯止め（別の時計：Participant.LastAppearanceAt） ===
            '
            ' 変えるたびに部屋の全員とラウンジへ知らせが飛ぶので、それを止める。
            ' 断ったときは時計を進めない（発言の連投と同じ決まり）。残りの時間を返し、画面はそれだけ待って送り直す
            Dim now As DateTime = DateTime.Now

            If participant.LastAppearanceAt.HasValue Then

                Dim elapsed As TimeSpan = now - participant.LastAppearanceAt.Value

                If elapsed < interval Then
                    result.RetryAfterMs = CInt(Math.Ceiling((interval - elapsed).TotalMilliseconds))
                    Return result
                End If

            End If

            participant.IconType = normalizedType
            participant.IconColor = normalizedColor
            participant.LastAppearanceAt = now

            ' 部屋の全員へ（参加者一覧の顔と、変えた本人のバー）
            Dim notice As New RoomNotice With {
                .AppearanceRoomId = roomId,
                .AppearanceLocalId = participant.LocalId,
                .AppearanceIconType = normalizedType,
                .AppearanceIconColor = normalizedColor
            }

            ' ラウンジの、その部屋の顔（ChatHub.Lounge.vb）
            notice.LoungeUpdate = NextLoungeOccupantChangeUpdate(roomId, participant)

            RoomNoticeOutbox.Add(notice)

            ' ラウンジの控えの顔も書き換える（【必ず塞ぐ】部屋を出た瞬間、ラウンジの顔だけ古い色に戻るのを防ぐ）。
            ' その人がラウンジの顔の一覧に出ていれば（ふつうは部屋に居るので出ていない）、その差分も配る
            Dim loungeChange As LoungeUpdate = UpdateLoungeAppearanceCore(participant.BrowserId, normalizedType, normalizedColor)

            If loungeChange IsNot Nothing Then
                RoomNoticeOutbox.Add(New RoomNotice With {.LoungeUpdate = loungeChange})
            End If

            ' 同じブラウザのラウンジの画面の、上のプロフィールの枠（「自分の顔」）も書き換える。
            ' あの枠はラウンジのページを開いたときの Cookie でサーバーが描いたもので、開いたままのタブでは古いまま残る。
            ' 一覧の自分の顔（上の控え）は新しくなるので、同じ画面に自分の顔が2つ出て食い違っていた（本番の実機で開発者が見つけた）。
            ' 配るのは、そのブラウザのラウンジの接続だけ（ほかの人には配らない。ブラウザの識別子も載せない。loungeSelfRoom と同じ形）
            Dim ownLounge As List(Of String) = LoungeConnectionsOfBrowserIdCore(participant.BrowserId)

            If ownLounge.Count > 0 Then
                SelfRoomSeq += 1
                RoomNoticeOutbox.Add(New RoomNotice With {
                    .SelfFaceTo = ownLounge,
                    .SelfFaceIconType = normalizedType,
                    .SelfFaceIconColor = normalizedColor,
                    .SelfFaceSeq = SelfRoomSeq
                })
            End If

            result.Accepted = True
            result.IconType = normalizedType
            result.IconColor = normalizedColor

        End SyncLock

        ' 配るのは鍵を抜けてから（誰が積んだものでも、最初に呼んだ人がまとめて配る）
        FlushRoomNotices()

        Return result

    End Function

End Class
