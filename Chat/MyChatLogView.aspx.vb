Imports System.Globalization
Imports System.Text

' 自分が参加したチャット履歴の、1回ぶんの中身（/account/chatlogs/view?s=…）
'
' 【AccountPageBase を継承している】ログインの確かめと no-store は、土台が受け持つ。
'
' 【?s= の番号を、信じない】
' 読む前に AccountChatLog.FindSession を通し、「ログインしている人の入室の行か」を DB で確かめる。
' 合わなければ 404（在るか無いかを言わない。記事の下書きを 404 にするのと同じ考え方）。
' そのあとに読む範囲は、確かめた行（Session）から取る。URL から来た数を、SQL の範囲に使わない
Public Class MyChatLogViewPage
    Inherits AccountPageBase


    ' 【名前を session にしないこと】VB は大文字小文字を区別しないので、ページの Session を隠す
    Private logSession As AccountChatLog.Session
    Private currentPage As Integer = 1
    Private rowsHtmlValue As String = ""


    ' 見出し（部屋の名前）。markup が <%: %> でエスケープして出す
    Protected ReadOnly Property RoomNameText As String
        Get
            Return If(logSession Is Nothing, "", MyChatLogsPage.RoomNameText(logSession))
        End Get
    End Property

    ' 「〜 21:40」（一覧と同じ形）
    Protected ReadOnly Property TimeRangeText As String
        Get
            Return If(logSession Is Nothing, "", MyChatLogsPage.TimeRangeText(logSession))
        End Get
    End Property

    ' 行の HTML（値は、組み立てるときに1つずつエスケープしてある。markup は <%= %> で出す）
    Protected ReadOnly Property RowsHtml As String
        Get
            Return rowsHtmlValue
        End Get
    End Property

    Protected ReadOnly Property PageText As String
        Get
            Return currentPage.ToString(CultureInfo.InvariantCulture) & " ページ目"
        End Get
    End Property

    Protected ReadOnly Property PrevPageUrl As String
        Get
            Return PageUrlOf(currentPage - 1)
        End Get
    End Property

    Protected ReadOnly Property NextPageUrl As String
        Get
            Return PageUrlOf(currentPage + 1)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        ' 【番号は、数字だけの形しか読まない】符号・空白・小数は、無いものとして扱う
        Dim joinMessageId As Long

        If Not Long.TryParse(Request.QueryString("s"), NumberStyles.None,
                             CultureInfo.InvariantCulture, joinMessageId) Then
            ShowNotFound()
            Return
        End If

        currentPage = AccountChatLog.NormalizePage(MyChatLogsPage.ReadPageNumber(Request.QueryString("p")))

        Dim rows As List(Of AccountChatLog.Row)
        Dim hasMore As Boolean

        Try

            ' ここが関所（自分の入室の行でなければ Nothing）
            logSession = AccountChatLog.FindSession(CurrentAccount.AccountId, joinMessageId)

            If logSession Is Nothing Then
                ShowNotFound()
                Return
            End If

            rows = AccountChatLog.GetRows(logSession, currentPage, hasMore)

        Catch ex As Exception

            ErrorReport.Report(ex, "MyChatLogViewPage.Page_Load")
            logSession = Nothing
            TroubleView.Visible = True
            PageResponse.MarkTrouble(Response)
            Return

        End Try

        LogView.Visible = True
        Title = MyChatLogsPage.RoomNameText(logSession) & " のチャット履歴"

        If rows.Count = 0 Then
            EmptyView.Visible = True
        Else
            rowsHtmlValue = BuildRowsHtml(rows)
        End If

        If currentPage > 1 OrElse hasMore Then
            PagerSection.Visible = True
            PagerPrev.Visible = (currentPage > 1)
            PagerNext.Visible = hasMore
        End If

    End Sub


    Private Sub ShowNotFound()

        logSession = Nothing
        NotFoundView.Visible = True

        ' 404 にする（本番の IIS が本文を差し替えないように、の手当ても PageResponse が持つ）
        PageResponse.MarkNotFound(Response)

    End Sub


    ' 行を HTML にする
    '
    ' 【値は、必ずここでエスケープする】名前・本文・お知らせの文言（部屋の名前が入る）は、どれも利用者が決めた文字。
    ' 「入口で検査してあるから」を理由に素通しにしないこと（過去のログには、検査を変える前の値も入っている）。
    '
    ' 【日付が変わった所に、区切りの行を挟む】部屋の画面と同じ（時刻だけでは、日をまたいだ記録で迷う）。
    '
    ' 【読み上げ】自分の行には、見えない「（自分）」を添える（色だけで伝えない）。こっそりは「こっそり」と文字で出す
    Private Function BuildRowsHtml(rows As List(Of AccountChatLog.Row)) As String

        Dim sb As New StringBuilder()
        Dim lastDate As String = Nothing

        For Each row As AccountChatLog.Row In rows

            If Not String.Equals(row.DateText, lastDate, StringComparison.Ordinal) Then
                sb.Append("<li class=""chatlog-date"">").Append(Enc(row.DateText)).Append("</li>")
                lastDate = row.DateText
            End If

            If row.Kind = AccountChatLog.RowKind.Notice Then

                sb.Append("<li class=""chatlog-notice""><span class=""chatlog-time"">").Append(Enc(row.TimeText))
                sb.Append("</span><span class=""chatlog-text"">").Append(Enc(row.Text)).Append("</span></li>")
                Continue For

            End If

            Dim isPrivate As Boolean = (row.Kind = AccountChatLog.RowKind.PrivateMessage)

            sb.Append("<li class=""chatlog-row")
            If row.IsMine Then sb.Append(" chatlog-row--mine")
            If isPrivate Then sb.Append(" chatlog-row--private")
            sb.Append(""">")

            sb.Append("<span class=""chatlog-time"">").Append(Enc(row.TimeText)).Append("</span>")

            sb.Append("<span class=""chatlog-who"">")

            If isPrivate Then
                sb.Append("<span class=""chatlog-private-mark"">こっそり</span>")
            End If

            sb.Append("<span class=""chatlog-name"">").Append(Enc(row.Nickname)).Append("</span>")

            If row.IsMine Then
                sb.Append("<span class=""visually-hidden"">（自分）</span>")
            End If

            If Not String.IsNullOrEmpty(row.MarkText) Then

                If Not String.IsNullOrEmpty(row.MarkUrl) Then

                    ' アカウントID（「@ユーザー名」）。押すと公開のプロフィール（新しいタブ）
                    sb.Append("<a class=""account-id chatlog-mark"" target=""_blank"" rel=""noopener"" href=")
                    sb.Append(""""c).Append(HttpUtility.HtmlAttributeEncode(row.MarkUrl)).Append(""""c)
                    sb.Append("><span class=""visually-hidden"">アカウント </span>").Append(Enc(row.MarkText))
                    sb.Append("<span class=""visually-hidden"">（新しいタブで開きます）</span></a>")

                Else

                    ' トリップ（色は theme.css の対応表が、1桁目で決める）
                    sb.Append("<span class=""trip-code chatlog-mark"" data-trip=")
                    sb.Append(""""c).Append(HttpUtility.HtmlAttributeEncode(row.MarkText)).Append(""""c)
                    sb.Append("><span class=""visually-hidden"">トリップ </span>").Append(Enc(row.MarkText)).Append("</span>")

                End If

            End If

            sb.Append("</span>")

            sb.Append("<span class=""chatlog-text"">")

            If Not String.IsNullOrEmpty(row.ImageUrl) Then

                ' 部屋の画像（新しいタブ）。名前の形は SiteImage.TryParseToken が確かめたもの
                sb.Append("<a target=""_blank"" rel=""noopener"" href=")
                sb.Append(""""c).Append(HttpUtility.HtmlAttributeEncode(row.ImageUrl)).Append(""""c)
                sb.Append(">").Append(Enc(row.Text)).Append("<span class=""visually-hidden"">（新しいタブで開きます）</span></a>")

            Else
                sb.Append(Enc(row.Text))
            End If

            sb.Append("</span></li>")

        Next

        Return sb.ToString()

    End Function


    Private Shared Function Enc(value As String) As String
        Return HttpUtility.HtmlEncode(If(value, ""))
    End Function


    ' 1ページ目には &p= を付けない
    Private Function PageUrlOf(pageNumber As Integer) As String

        Dim url As String = MyChatLogsPage.ViewUrl & "?s=" &
                            If(logSession Is Nothing, "0", logSession.JoinMessageId.ToString(CultureInfo.InvariantCulture))

        If pageNumber > 1 Then
            url &= "&p=" & pageNumber.ToString(CultureInfo.InvariantCulture)
        End If

        Return url

    End Function

End Class
