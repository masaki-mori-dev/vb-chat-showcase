' 「同じ相手が、この時間の中に何回まで」を数える道具
'
' 【、Accounts/AccountReset.vb の中から Core へ出した】
' 問い合わせページ（Contacts/ContactRequest.vb）でも同じものが要るようになったため。
' **2つ以上の機能から使うものは Core/**（「フォルダ構成」の「何をどこに置くか」）。
' 中身は1行も変えていない。VB はフォルダが名前空間にならないので、呼び方（RateCounter）も変わらない。
'
' 【覚えておくのはメモリだけ。DBの表を作らない】
' 外から叩くだけで行を増やせる口になるため（AccountPassword の歯止め・記録の歯止めと同じ判断）。
' アプリが再起動すると数え直しになるが、それは承知のうえの割り切りである
' ——共用サーバーは人が居ないと寝るので、そこを厳密にしても得るものが少ない。
'
' 【使い分け】
'   TryCount … 1回数えて、上限に達していれば数えずに False（＝これから1回やる、という場面）
'   IsOver　 … 数えずに「もう上限か」だけを見る（＝画面を出すかどうかを決める場面）
Friend NotInheritable Class RateCounter

    Private ReadOnly window As TimeSpan
    Private ReadOnly max As Integer
    Private ReadOnly hits As New Dictionary(Of String, Queue(Of DateTime))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly gate As New Object()

    ' 覚えておく相手の数の上限（メモリを食い潰されないため）
    Private Const TrackingLimit As Integer = 10000

    Public Sub New(window As TimeSpan, max As Integer)
        Me.window = window
        Me.max = max
    End Sub

    ' 1回数える。上限に達していれば数えずに False
    Public Function TryCount(key As String) As Boolean

        Dim k As String = If(key, "")
        Dim now As DateTime = DateTime.UtcNow

        SyncLock gate

            Dim q As Queue(Of DateTime) = Nothing

            If Not hits.TryGetValue(k, q) Then

                If hits.Count >= TrackingLimit Then
                    Prune(now)
                End If

                q = New Queue(Of DateTime)()
                hits(k) = q

            End If

            Trim(q, now)

            If q.Count >= max Then
                Return False
            End If

            q.Enqueue(now)
            Return True

        End SyncLock

    End Function

    ' もう上限か（数えない）
    Public Function IsOver(key As String) As Boolean

        Dim now As DateTime = DateTime.UtcNow

        SyncLock gate
            Dim q As Queue(Of DateTime) = Nothing
            If Not hits.TryGetValue(If(key, ""), q) Then
                Return False
            End If
            Trim(q, now)
            Return q.Count >= max
        End SyncLock

    End Function

    Private Sub Trim(q As Queue(Of DateTime), now As DateTime)
        While q.Count > 0 AndAlso now - q.Peek() > window
            q.Dequeue()
        End While
    End Sub

    Private Sub Prune(now As DateTime)
        Dim empty As New List(Of String)
        For Each entry In hits
            Trim(entry.Value, now)
            If entry.Value.Count = 0 Then
                empty.Add(entry.Key)
            End If
        Next
        For Each key In empty
            hits.Remove(key)
        Next
    End Sub

End Class
