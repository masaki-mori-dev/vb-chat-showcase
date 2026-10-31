Imports System.Globalization
Imports System.Text

' ログを調べる道具の、CSV の書き出し（/account/logs/csv?m=中心&from=最初の行&to=最後の行&p=…&pair=…）
'
' 【守り】Admin/LogToolPageBase.vb（サイト管理者だけ・no-store）
'
' 【書き出すのは、再生の画面に出ている範囲】（from〜to。画面の「もっと前／もっと後」で広がる）
' 部屋は中心の行（m）から決める。from・to に、よその部屋の行の番号が来ても、部屋で絞って読むので入らない。
'
' 【見た記録を、返す前に】書けなかったら CSV を返さない（混み合っていますの 503）
'
' 【CSV の形と、CSV インジェクション対策】Admin/LogToolView.vb の Csv（UTF-8・BOM 付き・CRLF・頭が = + - @ などの値に '）
Public Class LogExportPage
    Inherits LogToolPageBase


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        Dim centerId As Long
        Dim fromId As Long
        Dim toId As Long

        If Not Long.TryParse(Request.QueryString("m"), NumberStyles.None, CultureInfo.InvariantCulture, centerId) OrElse
           Not Long.TryParse(Request.QueryString("from"), NumberStyles.None, CultureInfo.InvariantCulture, fromId) OrElse
           Not Long.TryParse(Request.QueryString("to"), NumberStyles.None, CultureInfo.InvariantCulture, toId) OrElse
           fromId > toId Then
            WriteText(400, "書き出す範囲が読めませんでした。再生の画面から、もう一度「この範囲を書き出す」を押してください。")
            Return
        End If

        Dim filter As LogTool.ReplayFilter = LogTool.ReplayFilter.FromQueryString(Request.QueryString)
        Dim center As LogTool.LogRow
        Dim rows As List(Of LogTool.LogRow)
        Dim truncated As Boolean

        Try

            center = LogTool.FindMessage(centerId)

            If center Is Nothing Then
                WriteText(404, "記録が見つかりません（保存期間を過ぎて消えたかもしれません）。")
                Return
            End If

            rows = LogTool.GetRange(center.RoomId, fromId, toId, filter, centerId, truncated)

            ' 【見た記録を、返す前に】
            LogTool.WriteAudit(CurrentAccount, LogTool.AuditExport,
                               "部屋=" & center.RoomId.ToString(CultureInfo.InvariantCulture) &
                               "／中心=#" & centerId.ToString(CultureInfo.InvariantCulture) &
                               "／範囲=#" & fromId.ToString(CultureInfo.InvariantCulture) & "〜#" & toId.ToString(CultureInfo.InvariantCulture) &
                               "／" & filter.Describe() & If(truncated, "／上限で打ち切り", ""),
                               centerId, rows.Count, ClientIp,
                               LogToolView.ReplayPathFor(centerId, filter))

        Catch ex As Exception

            ErrorReport.Report(ex, "LogExportPage.Page_Load")
            WriteText(503, "ただいま混み合っています。少し待ってから、もう一度お試しください。")
            Return

        End Try

        ' 名前＝chatlog_room3_20261011-1647.csv（部屋の番号と、中心の行の時刻）
        Dim fileName As String = "chatlog_room" & center.RoomId.ToString(CultureInfo.InvariantCulture) & "_" &
                                 center.CreatedAt.ToString("yyyyMMdd'-'HHmm", CultureInfo.InvariantCulture) & ".csv"

        ' BOM 付き（Excel が UTF-8 と分かるように）
        Dim body As Byte() = Encoding.UTF8.GetBytes(LogToolView.Csv(rows))
        Dim bom As Byte() = Encoding.UTF8.GetPreamble()

        Response.Clear()
        Response.ContentType = "text/csv"
        Response.Charset = "utf-8"
        Response.AddHeader("Content-Disposition", "attachment; filename=" & fileName)
        Response.BinaryWrite(bom)
        Response.BinaryWrite(body)
        Response.Flush()
        Response.SuppressContent = True
        Context.ApplicationInstance.CompleteRequest()

    End Sub


    ' 書き出せなかったときの短い文（CSV ではなく、文字だけのページ）
    Private Sub WriteText(statusCode As Integer, message As String)

        Response.Clear()
        Response.StatusCode = statusCode
        Response.TrySkipIisCustomErrors = True
        Response.ContentType = "text/plain"
        Response.Charset = "utf-8"
        Response.Write(message)
        Response.Flush()
        Response.SuppressContent = True
        Context.ApplicationInstance.CompleteRequest()

    End Sub

End Class
