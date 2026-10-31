Imports System.Globalization

' ログを調べる道具の「見た記録」（/account/logs/audit）
'
' 【守り】Admin/LogToolPageBase.vb（サイト管理者だけ・no-store）
' 【この画面を見たことは、見た記録に書かない】（開発者の答え。中身は管理人の操作の記録）。
' ただし、探した条件（言葉・IP・ブラウザの印など）は条件の文に入っているので、利用者の情報がまったく無いわけではない（報告に書いた）
Public Class LogAuditPage
    Inherits LogToolPageBase


    Private query As LogTool.AuditQuery
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

    Protected ReadOnly Property ResetUrl As String
        Get
            Return LogToolView.AuditUrl
        End Get
    End Property

    Protected ReadOnly Property PageText As String
        Get
            Return If(query Is Nothing, "", query.Page.ToString(CultureInfo.InvariantCulture) & " ページ目")
        End Get
    End Property

    Protected ReadOnly Property NewerPageUrl As String
        Get
            Return UrlFor(query, query.Page - 1)
        End Get
    End Property

    Protected ReadOnly Property OlderPageUrl As String
        Get
            Return UrlFor(query, query.Page + 1)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        query = LogTool.AuditQuery.FromQueryString(Request.QueryString)

        If IsPostBack Then
            Return
        End If

        FillFields()

        Dim rows As List(Of LogTool.AuditRow)

        Try
            rows = LogTool.GetAudit(query, hasMoreValue)
        Catch ex As Exception
            ErrorReport.Report(ex, "LogAuditPage.Page_Load")
            TroubleView.Visible = True
            PageResponse.MarkTrouble(Response)
            Return
        End Try

        ResultView.Visible = True
        rowsHtmlValue = LogToolView.AuditRowsHtml(rows)

        If rows.Count = 0 Then
            summaryValue = "見た記録はありません。"
        Else
            Dim first As Integer = (query.Page - 1) * LogTool.PageSize + 1
            summaryValue = first.ToString(CultureInfo.InvariantCulture) & "〜" &
                           (first + rows.Count - 1).ToString(CultureInfo.InvariantCulture) & "件目（新しい順）"
        End If

        If query.Page > 1 OrElse hasMoreValue Then
            PagerSection.Visible = True
            PagerNewer.Visible = (query.Page > 1)
            PagerOlder.Visible = hasMoreValue
        End If

    End Sub


    Private Sub FillFields()

        FromBox.Text = If(query.FromAt.HasValue, query.FromAt.Value.ToString(LogTool.SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture), "")
        ToBox.Text = If(query.ToAt.HasValue, query.ToAt.Value.ToString(LogTool.SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture), "")

        ActionList.Items.Clear()
        ActionList.Items.Add(New ListItem("すべて", ""))

        For Each action As String In LogTool.AuditActions
            Dim item As New ListItem(LogTool.AuditActionLabel(action), action)
            item.Selected = String.Equals(action, query.Action, StringComparison.Ordinal)
            ActionList.Items.Add(item)
        Next

    End Sub


    ' ［絞る］＝欄の値から条件を作り、URL に入れて開き直す（PRG）
    Protected Sub FilterButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles FilterButton.Click

        Dim q As New LogTool.AuditQuery With {
            .FromAt = LogTool.ParseDateTime(FromBox.Text),
            .ToAt = LogTool.ParseDateTime(ToBox.Text)
        }

        Dim act As String = LogTool.Clean(Request.Form(ActionList.UniqueID))
        If act IsNot Nothing AndAlso Array.IndexOf(LogTool.AuditActions, act) >= 0 Then
            q.Action = act
        End If

        Response.Redirect(UrlFor(q, 1))

    End Sub


    Private Shared Function UrlFor(q As LogTool.AuditQuery, page As Integer) As String
        Dim qs As String = q.ToQueryString(page)
        Return LogToolView.AuditUrl & If(qs.Length = 0, "", "?" & qs)
    End Function

End Class
