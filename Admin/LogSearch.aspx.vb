Imports System.Globalization

' ログを調べる道具の、探す画面（/account/logs）
'
' 【守り】Admin/LogToolPageBase.vb（サイト管理者だけ・no-store）
'
' 【流れ】
'   GET  … URL の条件（LogTool.SearchQuery）で探し、見た記録を書いてから、結果を出す。
'          何も付けずに開いたときは、期間に直近24時間を入れて探す（Gmail の受信トレイを開いたのと同じ＝最初から何か出ている）
'   POST … ［探す］。欄の値から条件を作り、URL に入れて開き直す（PRG。結果の住所は GET の形だけ）
Public Class LogSearchPage
    Inherits LogToolPageBase


    Private query As LogTool.SearchQuery
    Private rowsHtmlValue As String = ""
    Private summaryValue As String = ""
    Private hasMoreValue As Boolean


    Protected ReadOnly Property RowsHtml As String
        Get
            Return rowsHtmlValue
        End Get
    End Property

    Protected ReadOnly Property SummaryText As String
        Get
            Return summaryValue
        End Get
    End Property

    ' 詳しい条件（▼）を開いた形で出すか
    Protected ReadOnly Property DetailOpen As Boolean
        Get
            Return query IsNot Nothing AndAlso (query.HasDetail OrElse query.Sender.HasValue)
        End Get
    End Property

    Protected ReadOnly Property SenderText As String
        Get
            Return If(query IsNot Nothing AndAlso query.Sender.HasValue, query.Sender.Value.ToString("D"), "")
        End Get
    End Property

    ' 種類のチェックボックス（決まった文字だけで組み立てる。markup は <%= %> で出す）
    Protected ReadOnly Property KindChecksHtml As String
        Get
            Dim sb As New Text.StringBuilder()
            For Each kind As LogTool.KindFilter In LogTool.AllKinds
                Dim value As String = CInt(kind).ToString(CultureInfo.InvariantCulture)
                sb.Append("<label class=""logsearch-kind""><input type=""checkbox"" name=""kinds"" value=""").Append(value).Append("""")
                If query IsNot Nothing AndAlso query.Kinds.Contains(kind) Then sb.Append(" checked=""checked""")
                sb.Append(" />").Append(HttpUtility.HtmlEncode(LogTool.KindFilterLabel(kind))).Append("</label>")
            Next
            Return sb.ToString()
        End Get
    End Property

    ' 「条件を消す」＝何も付けずに開き直す（直近24時間に戻る）
    Protected ReadOnly Property ResetUrl As String
        Get
            Return LogToolView.SearchUrl
        End Get
    End Property

    Protected ReadOnly Property PageText As String
        Get
            Return If(query Is Nothing, "", query.Page.ToString(CultureInfo.InvariantCulture) & " ページ目")
        End Get
    End Property

    Protected ReadOnly Property NewerPageUrl As String
        Get
            Return LogToolView.SearchUrl & "?" & query.ToQueryString(query.Page - 1)
        End Get
    End Property

    Protected ReadOnly Property OlderPageUrl As String
        Get
            Return LogToolView.SearchUrl & "?" & query.ToQueryString(query.Page + 1)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        query = LogTool.SearchQuery.FromQueryString(Request.QueryString, DateTime.Now)

        If IsPostBack Then
            ' 欄はブラウザから来た値のまま（作り直さない）。押されたボタンの処理で、URL を作って開き直す
            Return
        End If

        Dim rooms As List(Of LogTool.RoomOption)

        Try
            rooms = LogTool.GetRooms()
        Catch ex As Exception
            ErrorReport.Report(ex, "LogSearchPage.GetRooms")
            ShowTrouble()
            Return
        End Try

        FillFields(rooms)

        Dim rows As List(Of LogTool.LogRow)

        Try

            rows = LogTool.Search(query, hasMoreValue)

            ' 【見た記録を、結果を出す前に】書けなければ、下の Catch で結果を出さない
            ' 開き直す住所＝この条件・このページ（最初に開いたときは、そのときの直近24時間が from に入る）
            LogTool.WriteAudit(CurrentAccount, LogTool.AuditSearch, query.Describe(), Nothing, rows.Count, ClientIp,
                               LogToolView.SearchUrl & "?" & query.ToQueryString(query.Page))

        Catch ex As Exception

            ErrorReport.Report(ex, "LogSearchPage.Search")
            ShowTrouble()
            Return

        End Try

        ResultView.Visible = True
        rowsHtmlValue = LogToolView.SearchRowsHtml(rows)
        summaryValue = BuildSummary(rows.Count)

        If query.Page > 1 OrElse hasMoreValue Then
            PagerSection.Visible = True
            PagerNewer.Visible = (query.Page > 1)
            PagerOlder.Visible = hasMoreValue
        End If

    End Sub


    ' 欄に今の条件を入れる（部屋と種類の選択肢も、ここで作る）
    Private Sub FillFields(rooms As List(Of LogTool.RoomOption))

        QueryBox.Text = If(query.Text, "")
        FromBox.Text = If(query.FromAt.HasValue, query.FromAt.Value.ToString(LogTool.SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture), "")
        ToBox.Text = If(query.ToAt.HasValue, query.ToAt.Value.ToString(LogTool.SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture), "")
        NameBox.Text = If(query.Name, "")
        TripBox.Text = If(query.Trip, "")
        IpBox.Text = If(query.Ip, "")
        AccountBox.Text = If(query.Account, "")

        ' 部屋：すべて／サイトへの出入り（RoomId 0）／部屋（開いている部屋が先・閉じた部屋は「終了」）
        RoomList.Items.Clear()
        RoomList.Items.Add(New ListItem("すべての部屋", ""))
        RoomList.Items.Add(New ListItem(LogToolView.RoomLabel(0, Nothing, Nothing), "0"))

        For Each room As LogTool.RoomOption In rooms
            RoomList.Items.Add(New ListItem(LogToolView.RoomLabel(room.RoomId, room.RoomName, room.IsActive),
                                            room.RoomId.ToString(CultureInfo.InvariantCulture)))
        Next

        If query.RoomId.HasValue Then
            Dim item As ListItem = RoomList.Items.FindByValue(query.RoomId.Value.ToString(CultureInfo.InvariantCulture))
            If item IsNot Nothing Then
                RoomList.ClearSelection()
                item.Selected = True
            End If
        End If

        ' ブラウザの印は、欄ではなく札で出す（打つ値ではないので）。値は ViewState に持たせて、［探す］でも引き継ぐ
        SenderRow.Visible = query.Sender.HasValue
        ViewState("sender") = If(query.Sender.HasValue, query.Sender.Value.ToString("D"), Nothing)

    End Sub


    ' 「1〜50件目（新しい順）・期間 2026/10/10 18:00〜」
    Private Function BuildSummary(count As Integer) As String

        Dim period As String = "期間 " & If(query.FromAt.HasValue, LogTool.FormatMinute(query.FromAt.Value), "はじめ") & " 〜 " &
                               If(query.ToAt.HasValue, LogTool.FormatMinute(query.ToAt.Value), "今")

        If query.IsInitial Then
            period &= "（直近" & LogTool.DefaultPeriodHours.ToString(CultureInfo.InvariantCulture) & "時間）"
        End If

        If count = 0 Then
            Return "見つかりませんでした。" & period
        End If

        Dim first As Integer = (query.Page - 1) * LogTool.PageSize + 1
        Dim last As Integer = first + count - 1

        Return first.ToString(CultureInfo.InvariantCulture) & "〜" & last.ToString(CultureInfo.InvariantCulture) &
               "件目（新しい順）・" & period

    End Function


    Private Sub ShowTrouble()
        ResultView.Visible = False
        TroubleView.Visible = True
        PageResponse.MarkTrouble(Response)
    End Sub


    ' ［探す］＝欄の値から条件を作り、URL に入れて開き直す
    Protected Sub SearchButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles SearchButton.Click
        Response.Redirect(LogToolView.SearchUrl & "?" & BuildQueryFromFields(True).ToQueryString(1))
    End Sub


    ' ブラウザの印の札の「×」＝その条件だけ外して、ほかの欄のまま探し直す
    Protected Sub ClearSenderButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ClearSenderButton.Click
        Response.Redirect(LogToolView.SearchUrl & "?" & BuildQueryFromFields(False).ToQueryString(1))
    End Sub


    Private Function BuildQueryFromFields(keepSender As Boolean) As LogTool.SearchQuery

        Dim q As New LogTool.SearchQuery With {
            .Text = LogTool.Clean(QueryBox.Text),
            .FromAt = LogTool.ParseDateTime(FromBox.Text),
            .ToAt = LogTool.ParseDateTime(ToBox.Text),
            .Name = LogTool.Clean(NameBox.Text),
            .Trip = LogTool.Clean(TripBox.Text),
            .Ip = LogTool.Clean(IpBox.Text),
            .Account = LogTool.Clean(AccountBox.Text)
        }

        If q.Account IsNot Nothing AndAlso q.Account.StartsWith("@", StringComparison.Ordinal) Then
            q.Account = LogTool.Clean(q.Account.Substring(1))
        End If

        Dim room As Integer
        If Integer.TryParse(Request.Form(RoomList.UniqueID), NumberStyles.None, CultureInfo.InvariantCulture, room) Then
            q.RoomId = room
        End If

        ' 種類（印の付いたチェックボックスの値。1つも無ければ、最初の印＝発言・こっそり）
        Dim kinds As String() = Request.Form.GetValues("kinds")
        q.Kinds = LogTool.ParseKinds(If(kinds Is Nothing, Nothing, String.Join(",", kinds)), Nothing)

        Dim senderGuid As Guid
        If keepSender AndAlso Guid.TryParse(TryCast(ViewState("sender"), String), senderGuid) Then
            q.Sender = senderGuid
        End If

        Return q

    End Function

End Class
