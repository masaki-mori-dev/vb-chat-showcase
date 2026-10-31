Imports System.Globalization
Imports System.Web.Services

' ログを調べる道具の、再生の画面（/account/logs/view?m=発言の番号）
'
' 【守り】Admin/LogToolPageBase.vb（サイト管理者だけ・no-store）。WebMethod の LoadMore は土台を通らないので、中で確かめる。
'
' 【URL の値】
'   m    … 中心の発言の番号（必須。無い・読めない・見つからない＝「見つかりません」の 404）
'   p    … 1 ならこっそりも混ぜる
'   pair … 「この2人のこっそりだけ」（ブラウザの印を , でつないだ2つ）
' 部屋は、中心の行から決める（URL に部屋の番号を持たない＝食い違いようがない）
Public Class LogReplayPage
    Inherits LogToolPageBase


    Private center As LogTool.LogRow
    Private filter As LogTool.ReplayFilter
    Private rows As List(Of LogTool.LogRow)
    Private presence As List(Of LogTool.LogRow)
    Private hasBeforeValue As Boolean
    Private hasAfterValue As Boolean


    ' ============================================================
    ' markup が読む値
    ' ============================================================

    Protected ReadOnly Property RoomTitle As String
        Get
            Return If(center Is Nothing, "", LogToolView.RoomLabel(center.RoomId, center.RoomName, center.RoomIsActive))
        End Get
    End Property

    ' 「探していた行：2026/10/11 16:47:05.123・ゆずぽん（発言）」
    Protected ReadOnly Property CenterText As String
        Get
            If center Is Nothing Then Return ""
            Return "探していた行：" & LogTool.FormatMillis(center.CreatedAt) & "・" & center.Nickname & "（" & LogTool.KindLabel(center.MessageType) & "）"
        End Get
    End Property

    Protected ReadOnly Property CenterTimeText As String
        Get
            Return If(center Is Nothing, "", LogTool.FormatMinute(center.CreatedAt))
        End Get
    End Property

    Protected ReadOnly Property CenterId As String
        Get
            Return If(center Is Nothing, "0", center.MessageId.ToString(CultureInfo.InvariantCulture))
        End Get
    End Property

    Protected ReadOnly Property FirstId As String
        Get
            Return If(rows Is Nothing OrElse rows.Count = 0, CenterId, rows(0).MessageId.ToString(CultureInfo.InvariantCulture))
        End Get
    End Property

    Protected ReadOnly Property LastId As String
        Get
            Return If(rows Is Nothing OrElse rows.Count = 0, CenterId, rows(rows.Count - 1).MessageId.ToString(CultureInfo.InvariantCulture))
        End Get
    End Property

    Protected ReadOnly Property IncludePrivate As Boolean
        Get
            Return filter IsNot Nothing AndAlso filter.IncludePrivate
        End Get
    End Property

    Protected ReadOnly Property PairValue As String
        Get
            Return If(filter Is Nothing, "", filter.PairText())
        End Get
    End Property

    Protected ReadOnly Property HasBefore As Boolean
        Get
            Return hasBeforeValue
        End Get
    End Property

    Protected ReadOnly Property HasAfter As Boolean
        Get
            Return hasAfterValue
        End Get
    End Property

    Protected ReadOnly Property RowsHtml As String
        Get
            Return If(rows Is Nothing, "", LogToolView.ReplayRowsHtml(rows, center.MessageId, Nothing))
        End Get
    End Property

    Protected ReadOnly Property PresenceHtml As String
        Get
            Return If(presence Is Nothing, "", LogToolView.PresenceHtml(presence))
        End Get
    End Property

    Protected ReadOnly Property PresenceCount As String
        Get
            Return If(presence Is Nothing, "0", presence.Count.ToString(CultureInfo.InvariantCulture))
        End Get
    End Property

    ' 書き出しの住所（今出ている範囲。JS が「もっと前／もっと後」のあとに from・to を書き換える）
    Protected ReadOnly Property ExportUrl As String
        Get
            Return ExportBaseUrl & "&from=" & FirstId & "&to=" & LastId
        End Get
    End Property

    Protected ReadOnly Property ExportBaseUrl As String
        Get
            Dim url As String = LogToolView.ExportUrl & "?m=" & CenterId
            If IncludePrivate Then url &= "&p=1"
            If filter IsNot Nothing AndAlso filter.HasPair Then url &= "&pair=" & HttpUtility.UrlEncode(filter.PairText())
            Return url
        End Get
    End Property

    ' 「この2人のこっそりだけ」の2人の名前（中心の行から。中心がこっそりでなければ「2人」）
    Protected ReadOnly Property PairNames As String
        Get
            If center IsNot Nothing AndAlso center.IsPrivate Then
                Return center.Nickname & " と " & If(center.RecipientName, "（宛先不明）")
            End If
            Return "2人"
        End Get
    End Property

    ' 2人に絞るのをやめる（こっそりを混ぜた形に戻る）
    Protected ReadOnly Property PairClearUrl As String
        Get
            Return LogToolView.ReplayUrl & "?m=" & CenterId & "&p=1"
        End Get
    End Property

    ' 探す画面へ戻る（JS が、来た画面が探す画面なら「戻る」にする＝条件とページを保ったまま）
    Protected ReadOnly Property SearchBackUrl As String
        Get
            Return LogToolView.SearchUrl
        End Get
    End Property


    ' ============================================================
    ' 開いたとき
    ' ============================================================

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        Dim messageId As Long

        If Not Long.TryParse(Request.QueryString("m"), NumberStyles.None, CultureInfo.InvariantCulture, messageId) Then
            ShowNotFound()
            Return
        End If

        filter = LogTool.ReplayFilter.FromQueryString(Request.QueryString)

        Try

            center = LogTool.FindMessage(messageId)

            If center Is Nothing Then
                ShowNotFound()
                Return
            End If

            rows = LogTool.GetAround(center, filter, hasBeforeValue, hasAfterValue)

            ' 在室者（サイトへの出入り＝RoomId 0 には、部屋が無いので出さない）
            If center.RoomId <> 0 Then
                presence = LogTool.GetPresence(center.RoomId, center.MessageId, center.CreatedAt)
            Else
                presence = New List(Of LogTool.LogRow)()
            End If

            ' 【見た記録を、出す前に】
            LogTool.WriteAudit(CurrentAccount, LogTool.AuditView,
                               "部屋=" & center.RoomId.ToString(CultureInfo.InvariantCulture) & "／中心=#" & CenterId &
                               "／範囲=#" & FirstId & "〜#" & LastId & "／" & filter.Describe(),
                               center.MessageId, rows.Count, ClientIp,
                               LogToolView.ReplayPathFor(center.MessageId, filter))

        Catch ex As Exception

            ErrorReport.Report(ex, "LogReplayPage.Page_Load")
            center = Nothing
            rows = Nothing
            TroubleView.Visible = True
            PageResponse.MarkTrouble(Response)
            Return

        End Try

        LogView.Visible = True
        Title = RoomTitle & " のログ"
        PairChip.Visible = filter.HasPair
        PresenceEmpty.Visible = (presence.Count = 0)

    End Sub


    Private Sub ShowNotFound()
        center = Nothing
        NotFoundView.Visible = True
        PageResponse.MarkNotFound(Response)
    End Sub


    ' ============================================================
    ' もっと前／もっと後（WebMethod。Scripts/admin-logs.js が POST で呼ぶ）
    ' ============================================================
    '
    ' 【守り】WebMethod は AccountPageBase（と LogToolPageBase）の OnInit・OnLoad を素通りする。
    ' だから中で LogTool.CurrentAdministrator を呼ぶ（サイト管理者でなければ、何も読まずに断る）。
    ' ASP.NET の WebMethod は Content-Type: application/json の POST しか受けないので、よそのサイトのフォームからは呼べない。
    '
    ' 【部屋は中心の行から決める】画面から部屋の番号を受け取らない。
    '
    ' m        … 中心の行の番号
    ' older    … True＝もっと前（edge より古い行）／False＝もっと後
    ' edge     … 今出ている端の行の番号（その行は含めない）
    ' p        … こっそりも混ぜる
    ' pair     … この2人のこっそりだけ（空なら無し）
    ' lastDate … もっと後のとき、今出ている最後の日付（同じ日付の区切りを2回出さない）
    <WebMethod()>
    Public Shared Function LoadMore(m As Long, older As Boolean, edge As Long, p As Boolean, pair As String,
                                    lastDate As String) As Dictionary(Of String, Object)

        Return ErrorReport.GuardWebMethod("LogReplay.LoadMore", Function() LoadMoreCore(m, older, edge, p, pair, lastDate))

    End Function


    Private Shared Function LoadMoreCore(m As Long, older As Boolean, edge As Long, p As Boolean, pair As String,
                                         lastDate As String) As Dictionary(Of String, Object)

        Dim reply As New Dictionary(Of String, Object)()
        reply("ok") = False

        Dim context As HttpContext = HttpContext.Current
        Dim account As AccountStore.AccountRecord = LogTool.CurrentAdministrator(context)

        If account Is Nothing Then
            reply("message") = "ログインが切れたか、この道具を使えないアカウントです。開き直してください。"
            Return reply
        End If

        Dim qs As New NameValueCollection()
        If p Then qs("p") = "1"
        If Not String.IsNullOrEmpty(pair) Then qs("pair") = pair
        Dim filter As LogTool.ReplayFilter = LogTool.ReplayFilter.FromQueryString(qs)

        Try

            Dim center As LogTool.LogRow = LogTool.FindMessage(m)

            If center Is Nothing Then
                reply("message") = "記録が見つかりません（保存期間を過ぎて消えたかもしれません）。"
                Return reply
            End If

            Dim hasMore As Boolean
            Dim rows As List(Of LogTool.LogRow) = LogTool.GetMore(center.RoomId, edge, older, filter, hasMore)

            Dim ip As String = Nothing
            Try
                ip = context.Request.UserHostAddress
            Catch ex As Exception
                ip = Nothing
            End Try

            ' 【見た記録を、返す前に】
            LogTool.WriteAudit(account, LogTool.AuditMore,
                               "部屋=" & center.RoomId.ToString(CultureInfo.InvariantCulture) & "／中心=#" & m.ToString(CultureInfo.InvariantCulture) &
                               "／" & If(older, "もっと前", "もっと後") & "（#" & edge.ToString(CultureInfo.InvariantCulture) & " から）／" & filter.Describe(),
                               center.MessageId, rows.Count, ip,
                               LogToolView.ReplayPathFor(center.MessageId, filter))

            Dim newEdge As Long = edge
            If rows.Count > 0 Then
                newEdge = If(older, rows(0).MessageId, rows(rows.Count - 1).MessageId)
            End If

            reply("ok") = True
            reply("html") = LogToolView.ReplayRowsHtml(rows, center.MessageId, If(older, Nothing, lastDate))
            reply("edge") = newEdge.ToString(CultureInfo.InvariantCulture)
            reply("hasMore") = hasMore
            reply("count") = rows.Count

        Catch ex As Exception

            ErrorReport.Report(ex, "LogReplay.LoadMore")
            reply("ok") = False
            reply("message") = "ただいま混み合っています。少し待ってから、もう一度お試しください。"

        End Try

        Return reply

    End Function

End Class
