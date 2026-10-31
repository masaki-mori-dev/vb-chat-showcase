Imports System.Globalization

' 接続元のポート番号を読む（ChatMessages.RemotePort へ入れる値）
'
' 【何のための値か】（開発者）
' 開示請求に要るかどうかではなく、技術的に個人を特定しやすくするため。
' 携帯の回線などは、1つの IP アドレスを大勢で分け合っている（キャリアの NAT）。
' 回線の会社が「その時刻に、どの契約者だったか」を引くには、IP アドレス＋ポート番号＋時刻の3つが要る。
'
' 【IP アドレスと同じ場所・同じタイミングで読む】IP を読んでいる所（ChatHub.JoinRoom・ノック・ラウンジの鍵・サイトへの出入り）の隣で呼ぶ。
' 部屋の中の行（発言・退室）は、IP と同じく、入室（と入り直し）のときに Participant へ取っておいた値で書かれる。
' SignalR が WebSocket でつながっているあいだは、入室のときの接続のまま発言も届くので、その接続のポート番号である。
'
' 【取れなくても止めない】IP と同じ。読めない・数字でない・範囲の外なら Nothing（ログの列が NULL になるだけ）。
' 読むのは IIS が持っている REMOTE_PORT（ブラウザの言い値ではない）。
'
' 【2つの入口】ページ（HttpRequest）と、SignalR のハブ（GetHttpContext() が返す HttpRequestBase）
Public NotInheritable Class ClientEndpoint

    Private Sub New()
    End Sub


    Public Shared Function ReadPort(request As HttpRequest) As Integer?

        Try
            If request Is Nothing Then Return Nothing
            Return ParsePort(request.ServerVariables("REMOTE_PORT"))
        Catch ex As Exception
            ' 取れなかったときは、ログの RemotePort 列が NULL になるだけ（知らせない。IP と同じ）
            Return Nothing
        End Try

    End Function


    Public Shared Function ReadPort(request As HttpRequestBase) As Integer?

        Try
            If request Is Nothing Then Return Nothing
            Return ParsePort(request.ServerVariables("REMOTE_PORT"))
        Catch ex As Exception
            Return Nothing
        End Try

    End Function


    ' 数字だけの形（符号・空白なし）で、0〜65535 のときだけ
    Private Shared Function ParsePort(raw As String) As Integer?

        Dim port As Integer

        If Integer.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, port) AndAlso
           port >= 0 AndAlso port <= 65535 Then
            Return port
        End If

        Return Nothing

    End Function

End Class
