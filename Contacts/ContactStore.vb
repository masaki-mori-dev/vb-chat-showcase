Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' 問い合わせの表（ContactMessages）への出入り（問い合わせページ）
'
' 【SQL をここ1か所に閉じ込めてある】
' Accounts/AccountStore.vb・Articles/ArticleCategoryStore.vb・Chat/ChatLog.vb と同じ考え方。
' C#／ASP.NET Core MVC へ移るとき、画面は書き直しになるが、ここは中身を入れ替えるだけで移せる。
'
' 【DBに書けないときは、例外を投げる（フェイルクローズ）】
' 受け取った Contacts/ContactRequest.vb が握って、メールだけでも届くかを試す。
' **握りつぶさないこと**——書けたのか書けなかったのかが呼ぶ側に伝わらないと、
' 「どちらにも残せなかったとき」を見分けられなくなる。
'
' 【読み出しは、1つだけある】`HasRecentSame`（同じ内容がもう入っていないか）。
' 一覧（管理画面）は MVC版で作る。
' 消す口（古い行の削除）は Core/LogCleanup.vb にある（1日1回の仕事に相乗りしているため、あちらに置いた）
Public NotInheritable Class ContactStore

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' 届いた問い合わせ1件
    '
    ' 【画面から来た文字列は、ここへ来る前に ContactRules を通っていること】
    ' 長さの確かめと正規化は、受け口（ContactRequest）の仕事。ここは入れるだけ
    Public Class ContactRecord

        ' 空なら Nothing でよい（DB には NULL が入る）
        Public Property SenderName As String
        Public Property ReplyEmail As String

        Public Property Kind As ContactRules.ContactKind
        Public Property Body As String

        Public Property IpAddress As String
        Public Property UserAgent As String

    End Class


    ' 1件入れて、付いた番号（ContactId）を返す
    '
    ' 【CreatedAt は入れない】DB の既定値（SYSDATETIME()）に任せる。
    ' 古い行の削除（LogCleanup）と一覧の並び順が、この列で決まるので、
    ' アプリの時計ではなく DB の時計で入れる（ChatMessages.CreatedAt と同じ決まり）
    Public Shared Function Add(record As ContactRecord) As Integer

        Dim sql As String =
            "INSERT INTO ContactMessages (SenderName, ReplyEmail, Kind, Body, IpAddress, UserAgent) " &
            "VALUES (@SenderName, @ReplyEmail, @Kind, @Body, @IpAddress, @UserAgent); " &
            "SELECT CAST(SCOPE_IDENTITY() AS INT);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@SenderName", SqlDbType.NVarChar, ContactRules.NameMaxLength).Value =
                    ToDbValue(record.SenderName)
                cmd.Parameters.Add("@ReplyEmail", SqlDbType.NVarChar, ContactRules.EmailMaxLength).Value =
                    ToDbValue(record.ReplyEmail)
                cmd.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = CInt(record.Kind)
                cmd.Parameters.Add("@Body", SqlDbType.NVarChar, ContactRules.BodyMaxLength).Value =
                    If(record.Body, "")
                cmd.Parameters.Add("@IpAddress", SqlDbType.NVarChar, 45).Value = ToDbValue(record.IpAddress)
                cmd.Parameters.Add("@UserAgent", SqlDbType.NVarChar, 256).Value = ToDbValue(record.UserAgent)

                conn.Open()

                Dim newId As Object = cmd.ExecuteScalar()

                If newId Is Nothing OrElse newId Is DBNull.Value Then
                    ' 1行も入らなかった。呼ぶ側に「書けなかった」と伝わるように、例外にする
                    Throw New InvalidOperationException("問い合わせを保存できませんでした（行が入りませんでした）。")
                End If

                Return CInt(newId)

            End Using
        End Using

    End Function


    ' 同じIPから、同じ内容の問い合わせが、この時刻より後にもう入っているか
    '
    ' ============================================================
    ' 【何のためか】確認の画面から「戻る」で戻って、もう一度「送信する」を押すと、
    ' 同じ内容がもう1件入っていた（本番の実機テストで開発者が踏んだ）。
    ' 送信のときにここを1回引いて、入っていれば2件目を受け付けない。
    '
    ' 【なぜ Session や、1回きりの合言葉ではないのか】（開発者と決めた）
    '   ・共用サーバーは人が居ないと寝る。Session が切れると、書いた文が消える
    '   ・DB はもともと「正」の置き場所で、アプリが寝て起き直しても覚えている
    '   ・**どの画面から戻って送り直しても、ここで止まる**（「どの画面から来たか」に左右されない）
    '   ・表の形を変えなくて済む（本番に流す SQL が要らない）
    '
    ' 【「同じ内容」は、4つすべてが同じこと】種類・お名前・返信先・内容。
    ' 本文だけで比べないのは、**1文字でも直したら送れる**ようにするため
    ' （種類だけを選び直して送り直す人も、ふつうに送れる）。
    '
    ' 【本文は、包みをほどいたあとの値で比べる】
    ' 呼ぶ側（ContactRequest.Submit）が、正規化まで済んだ値を渡す。
    ' 包んだままの値で比べると、同じ文でも包み方が違えば別物になってしまう。
    '
    ' 【索引】IX_ContactMessages_CreatedAt で時刻から絞れる。そのあとに残る行は数件なので、
    ' 本文どうしの比べは、その数件にしか起きない（表の形は変えていない）。
    '
    ' 【大文字小文字・全角半角の扱いは、DB の照合順序に任せる】
    ' 既定では区別しないことがあるので、「A」と「a」が同じと判定されうる。
    ' **それで困らない**——止めたいのは「二度押し」で、窓は10分と短く、
    ' しかも同じIPから、という条件が付いている
    ' ============================================================
    Public Shared Function HasRecentSame(record As ContactRecord, since As DateTime) As Boolean

        Dim sql As String =
            "SELECT TOP (1) 1 FROM ContactMessages " &
            " WHERE CreatedAt >= @Since " &
            "   AND IpAddress = @IpAddress " &
            "   AND Kind = @Kind " &
            "   AND Body = @Body " &
            "   AND ISNULL(SenderName, N'') = @SenderName " &
            "   AND ISNULL(ReplyEmail, N'') = @ReplyEmail;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = since
                cmd.Parameters.Add("@IpAddress", SqlDbType.NVarChar, 45).Value = ToDbValue(record.IpAddress)
                cmd.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = CInt(record.Kind)
                cmd.Parameters.Add("@Body", SqlDbType.NVarChar, ContactRules.BodyMaxLength).Value =
                    If(record.Body, "")
                cmd.Parameters.Add("@SenderName", SqlDbType.NVarChar, ContactRules.NameMaxLength).Value =
                    If(record.SenderName, "")
                cmd.Parameters.Add("@ReplyEmail", SqlDbType.NVarChar, ContactRules.EmailMaxLength).Value =
                    If(record.ReplyEmail, "")

                conn.Open()

                Return cmd.ExecuteScalar() IsNot Nothing

            End Using
        End Using

    End Function


    ' 空文字は NULL にして入れる
    '
    ' 【NULL と空文字を混ぜないため】混ざると、将来の一覧で「名前を入れた人」を数えるときに
    ' 2通りの条件を書くことになる（DB の列のコメントにも同じことを書いてある）
    Private Shared Function ToDbValue(text As String) As Object

        If String.IsNullOrEmpty(text) Then
            Return DBNull.Value
        End If

        Return text

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
