' 管理人からのお知らせを、全部並べるページ（/news。基本ルール T）
'
' 【1ページに20件】右の枠の数（SiteSettings.SidebarNoticeCount）とは別。こちらは「全部を見る」ページなので、ページ送りで全部たどれればよい。
' 【DB は1回】数と1ページぶんを、1つの問い合わせで引く（SiteNoticeStore.ListVisiblePage）。控えは持たない（開く人が少ないページ）
' 【読めなかったら】「読み込めませんでした」と出して、知らせる（ページは止めない）
Public Class SiteNoticeListPage
    Inherits System.Web.UI.Page

    Private Const PageSize As Integer = 20

    Protected ListHtml As String = ""
    Protected HasPager As Boolean
    Protected PageText As String = ""
    Protected NewerPageUrl As String
    Protected OlderPageUrl As String

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【右の枠のお知らせは出さない】本文がお知らせの一覧そのものなので（ほかの欄はそのまま）
        Dim master As SiteMaster = TryCast(Me.Master, SiteMaster)
        If master IsNot Nothing Then
            master.HideSidebarNotices = True
        End If

        ' ?p= は数字だけ。読めない・1より小さいなら1ページ目
        Dim pageNumber As Integer = 1
        Dim raw As String = Request.QueryString("p")
        If Not String.IsNullOrEmpty(raw) Then
            Integer.TryParse(raw, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, pageNumber)
        End If
        If pageNumber < 1 Then
            pageNumber = 1
        End If

        Dim total As Integer = 0
        Dim items As List(Of SiteNoticeStore.NoticeRecord)

        Try
            items = SiteNoticeStore.ListVisiblePage((pageNumber - 1) * PageSize, PageSize, total)
        Catch ex As Exception
            ErrorReport.Report(ex, "SiteNoticeListPage.Load")
            ListHtml = "<p class=""history-empty"">お知らせを読み込めませんでした。少し待ってから、もう一度開いてください。</p>"
            Return
        End Try

        Dim pageCount As Integer = Math.Max(1, CInt(Math.Ceiling(total / CDbl(PageSize))))

        ' 【無いページ】最後のページより先を開かれたら、404（住所のルール＝無い住所にページを出さない）
        If pageNumber > pageCount Then
            Response.StatusCode = 404
            Response.TrySkipIisCustomErrors = True
            ListHtml = "<p class=""history-empty"">このページはありません。<a href=""/news"">お知らせの1ページ目へ</a></p>"
            Return
        End If

        If items.Count = 0 Then
            ListHtml = "<p class=""history-empty"">お知らせは、まだありません。</p>"
        Else
            ListHtml = "<div class=""notice-area notice-area--page"">" & SiteNotices.ItemsHtml(items) & "</div>"
        End If

        If pageCount > 1 Then
            HasPager = True
            PageText = pageNumber.ToString(Globalization.CultureInfo.InvariantCulture) & " / " & pageCount.ToString(Globalization.CultureInfo.InvariantCulture)
            If pageNumber > 1 Then
                NewerPageUrl = If(pageNumber = 2, "/news", "/news?p=" & (pageNumber - 1).ToString(Globalization.CultureInfo.InvariantCulture))
            End If
            If pageNumber < pageCount Then
                OlderPageUrl = "/news?p=" & (pageNumber + 1).ToString(Globalization.CultureInfo.InvariantCulture)
            End If
        End If

    End Sub

End Class
