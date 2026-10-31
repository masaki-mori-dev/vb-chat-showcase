Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' お知らせの表（SiteNotices）への出入り（ブログ型の⑤）
'
' 【SQL をここ1か所に閉じ込めてある】AccountStore・ArticleStore と同じ考え方。
' C#／ASP.NET Core MVC へ移るとき、画面は書き直しになるが、ここは中身を入れ替えるだけで移せる。
'
' 【DB が読めないときは、例外を投げる（フェイルクローズ）】
' 受け取った側（管理の画面）が握って「混み合っています」と出す。
' **公開側（Notices/SiteNotices.vb）だけは逆にフェイルオープン**——
' お知らせが読めないだけで、サイトの全ページを落とす筋合いが無い
Public NotInheritable Class SiteNoticeStore

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' 1行ぶんのお知らせ
    Public Class NoticeRecord
        Public Property NoticeId As Integer
        Public Property NoticeDate As Date
        Public Property Body As String
        ' 行き先（無ければ Nothing）
        Public Property LinkUrl As String
        Public Property IsVisible As Boolean
    End Class


    ' === 管理の画面の一覧（出していないものも含めて、全部） ===
    '
    ' 【全部読む】数十行の表なので、ページ送りは作らない
    ' （足したくなったら、そのときに TOP と OFFSET を足す）
    Public Shared Function ListAll() As List(Of NoticeRecord)

        Dim list As New List(Of NoticeRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT NoticeId, NoticeDate, Body, LinkUrl, IsVisible FROM SiteNotices " &
                " ORDER BY NoticeDate DESC, NoticeId DESC;", conn)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(ReadRow(reader))
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' === 公開側に出すぶん（出すと決めたもの・新しい順に何本か） ===
    '
    ' 【未来の日付のものも出す】「こうなります」を先に出せるようにするため
    ' （出すかどうかは IsVisible が決める、という形にそろえてある。
    '   日付でも隠すと、「出したのに出ない」の原因が2つになる）
    Public Shared Function ListVisible(count As Integer) As List(Of NoticeRecord)

        Dim list As New List(Of NoticeRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (@Top) NoticeId, NoticeDate, Body, LinkUrl, IsVisible FROM SiteNotices " &
                " WHERE IsVisible = 1 " &
                " ORDER BY NoticeDate DESC, NoticeId DESC;", conn)

                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = count

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(ReadRow(reader))
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' === 公開側の一覧のページ（/news）の1ページぶん ===
    ' 並びは右の枠と同じ（新しい順）。skip 件飛ばして take 件。total に、出すと決めたものの全部の数を入れる
    Public Shared Function ListVisiblePage(skip As Integer, take As Integer, ByRef total As Integer) As List(Of NoticeRecord)

        Dim list As New List(Of NoticeRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT COUNT(*) FROM SiteNotices WHERE IsVisible = 1; " &
                "SELECT NoticeId, NoticeDate, Body, LinkUrl, IsVisible FROM SiteNotices " &
                " WHERE IsVisible = 1 " &
                " ORDER BY NoticeDate DESC, NoticeId DESC " &
                " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;", conn)

                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = skip
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    If reader.Read() Then
                        total = reader.GetInt32(0)
                    End If
                    reader.NextResult()
                    While reader.Read()
                        list.Add(ReadRow(reader))
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' === 1行引く（無ければ Nothing） ===
    Public Shared Function Find(noticeId As Integer) As NoticeRecord

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (1) NoticeId, NoticeDate, Body, LinkUrl, IsVisible FROM SiteNotices " &
                " WHERE NoticeId = @Id;", conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = noticeId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Return ReadRow(reader)

                End Using

            End Using
        End Using

    End Function


    ' === 足す（新しい番号を返す） ===
    Public Shared Function Insert(record As NoticeRecord) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "INSERT INTO SiteNotices (NoticeDate, Body, LinkUrl, IsVisible) " &
                "OUTPUT INSERTED.NoticeId " &
                "VALUES (@Date, @Body, @Link, @Visible);", conn)

                AddParameters(cmd, record)

                conn.Open()

                Return CInt(cmd.ExecuteScalar())

            End Using
        End Using

    End Function


    ' === 直す（書き換えたら True。行が無ければ False） ===
    Public Shared Function Update(record As NoticeRecord) As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "UPDATE SiteNotices " &
                "   SET NoticeDate = @Date, Body = @Body, LinkUrl = @Link, IsVisible = @Visible, " &
                "       UpdatedAt = SYSDATETIME() " &
                " WHERE NoticeId = @Id;", conn)

                AddParameters(cmd, record)
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = record.NoticeId

                conn.Open()

                Return cmd.ExecuteNonQuery() = 1

            End Using
        End Using

    End Function


    ' === 消す（消したら True） ===
    '
    ' 【お知らせは、行ごと消してよい】記事と違って、どこからも番号で参照されていない
    ' （記事は「非公開」で引っ込める形にしてあるが、あちらは URL がすでに人に知られているため）。
    ' 出さずに残しておきたいだけなら、IsVisible を寝かせればよい
    Public Shared Function Delete(noticeId As Integer) As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("DELETE FROM SiteNotices WHERE NoticeId = @Id;", conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = noticeId

                conn.Open()

                Return cmd.ExecuteNonQuery() = 1

            End Using
        End Using

    End Function


    Private Shared Sub AddParameters(cmd As SqlCommand, record As NoticeRecord)

        cmd.Parameters.Add("@Date", SqlDbType.Date).Value = record.NoticeDate
        cmd.Parameters.Add("@Body", SqlDbType.NVarChar, SiteNoticeRules.BodyMaxLength).Value = record.Body
        cmd.Parameters.Add("@Link", SqlDbType.NVarChar, SiteNoticeRules.LinkMaxLength).Value =
            If(String.IsNullOrEmpty(record.LinkUrl), CObj(DBNull.Value), CObj(record.LinkUrl))
        cmd.Parameters.Add("@Visible", SqlDbType.Bit).Value = record.IsVisible

    End Sub


    Private Shared Function ReadRow(reader As SqlDataReader) As NoticeRecord

        Return New NoticeRecord With {
            .NoticeId = reader.GetInt32(0),
            .NoticeDate = reader.GetDateTime(1).Date,
            .Body = reader.GetString(2),
            .LinkUrl = If(reader.IsDBNull(3), Nothing, reader.GetString(3)),
            .IsVisible = reader.GetBoolean(4)}

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
