Imports System.Globalization

' 自分が参加したチャット履歴の一覧（/account/chatlogs）
'
' 【AccountPageBase を継承している】ログインしていなければ、画面が組み立てられる前にログインの画面へ返される。
' no-store（ブラウザに覚えさせない）も、そこで付く（「毎回の基本」の7）。
'
' 【ロールでは絞っていない】アカウントの人なら、誰でも自分の記録を読める（サイト管理者も、一般会員も）。
' 「誰の記録か」は、ログインしているアカウントの番号で決まる（画面から受け取らない）。
'
' 【判断は Chat/AccountChatLog.vb が持つ】ここは、呼んで並べるだけ
Public Class MyChatLogsPage
    Inherits AccountPageBase


    ' 一覧のURL・中身のURL（組み立てるのは、この2つだけ）
    Public Const ListUrl As String = "/account/chatlogs"
    Public Const ViewUrl As String = "/account/chatlogs/view"


    Private currentPage As Integer = 1
    Private hasMorePages As Boolean


    ' ログの保存期間（「90日」のような形）。画面に数字を書かないため、設定から出す
    Protected ReadOnly Property RetentionText As String
        Get
            Dim days As Integer = CInt(Math.Floor(SiteSettings.LogRetention.TotalDays))
            Return If(days >= 1, days.ToString(CultureInfo.InvariantCulture) & "日", "保存期間")
        End Get
    End Property


    Protected ReadOnly Property PageText As String
        Get
            Return currentPage.ToString(CultureInfo.InvariantCulture) & " ページ目"
        End Get
    End Property

    Protected ReadOnly Property NewerPageUrl As String
        Get
            Return PageUrlOf(currentPage - 1)
        End Get
    End Property

    Protected ReadOnly Property OlderPageUrl As String
        Get
            Return PageUrlOf(currentPage + 1)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        ' 【画面から来た数は、必ず丸める】そのまま OFFSET に渡さない（記事のページ送りと同じ）
        currentPage = AccountChatLog.NormalizePage(ReadPageNumber(Request.QueryString("p")))

        Dim sessions As List(Of AccountChatLog.Session)

        Try
            sessions = AccountChatLog.GetSessions(CurrentAccount.AccountId, currentPage, hasMorePages)
        Catch ex As Exception

            ' 理由は画面に出さない（エラー報告メールで届く）
            ErrorReport.Report(ex, "MyChatLogsPage.Page_Load")
            ErrorText.Text = HttpUtility.HtmlEncode("ただいま混み合っています。少し待ってから、もう一度お試しください。")
            Return

        End Try

        If sessions.Count = 0 AndAlso currentPage = 1 Then
            EmptyView.Visible = True
            Return
        End If

        SessionList.DataSource = sessions
        SessionList.DataBind()

        ' 1ページに収まるなら、ページ送りは出さない
        If currentPage > 1 OrElse hasMorePages Then
            PagerSection.Visible = True
            PagerNewer.Visible = (currentPage > 1)
            PagerOlder.Visible = hasMorePages
        End If

    End Sub


    ' === 1行ぶんの値（markup の <%#: %> がエスケープして出す） ===

    Protected Function ViewUrlOf(item As Object) As String
        Dim s As AccountChatLog.Session = DirectCast(item, AccountChatLog.Session)
        Return ViewUrl & "?s=" & s.JoinMessageId.ToString(CultureInfo.InvariantCulture)
    End Function

    Protected Function RoomNameOf(item As Object) As String
        Return RoomNameText(DirectCast(item, AccountChatLog.Session))
    End Function

    Protected Function TimeRangeOf(item As Object) As String
        Return TimeRangeText(DirectCast(item, AccountChatLog.Session))
    End Function

    Protected Function IsStillIn(item As Object) As Boolean
        Return DirectCast(item, AccountChatLog.Session).EndKind = AccountChatLog.SessionEndKind.StillIn
    End Function


    ' 部屋の名前（行が引けなかったら、番号で出す。ふつうは起きない＝部屋は論理削除で、行は残る）
    Friend Shared Function RoomNameText(s As AccountChatLog.Session) As String

        If String.IsNullOrEmpty(s.RoomName) Then
            Return "部屋 " & s.RoomId.ToString(CultureInfo.InvariantCulture)
        End If

        Return s.RoomName

    End Function


    ' 「〜 21:40」の形。日をまたいだら、終わりにも日付を付ける
    '
    ' 【年月日は、書式指定を使わない】サーバーの文化の設定に和暦が入っていても、年が化けないように
    ' （部屋の過去ログ・記事の日付と同じ決まり。組み立てているのは ArticleDisplay.DateText の1か所）
    Friend Shared Function TimeRangeText(s As AccountChatLog.Session) As String

        Dim startText As String = DateText(s.JoinedAt) & " " & TimeText(s.JoinedAt)

        Select Case s.EndKind

            Case AccountChatLog.SessionEndKind.StillIn
                Return startText & " 〜"

            Case AccountChatLog.SessionEndKind.Unknown
                ' 退室の行が無い（アプリの再起動など）。見せるのは、自分が最後に書いた行まで
                Return startText & " 〜 " & EndText(s) & " ごろ"

            Case Else
                Return startText & " 〜 " & EndText(s)

        End Select

    End Function

    Private Shared Function EndText(s As AccountChatLog.Session) As String

        If s.EndedAt.Date = s.JoinedAt.Date Then
            Return TimeText(s.EndedAt)
        End If

        Return DateText(s.EndedAt) & " " & TimeText(s.EndedAt)

    End Function

    ' 【日付の形は、サイトで1つ】（Articles/ArticleDisplay.vb の DateText。ここで組み立て直さないこと＝毎回の基本の8）
    Private Shared Function DateText(value As DateTime) As String
        Return ArticleDisplay.DateText(value)
    End Function

    Private Shared Function TimeText(value As DateTime) As String
        Return value.ToString("HH:mm", CultureInfo.InvariantCulture)
    End Function


    ' 1ページ目には ?p= を付けない（同じ中身が2つの URL で見えないように。ブログのトップと同じ）
    Private Shared Function PageUrlOf(pageNumber As Integer) As String

        If pageNumber <= 1 Then
            Return ListUrl
        End If

        Return ListUrl & "?p=" & pageNumber.ToString(CultureInfo.InvariantCulture)

    End Function


    ' 数字だけの形しか読まない（それ以外は 1）
    Friend Shared Function ReadPageNumber(raw As String) As Integer

        Dim value As Integer

        If Integer.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, value) Then
            Return value
        End If

        Return 1

    End Function

End Class
