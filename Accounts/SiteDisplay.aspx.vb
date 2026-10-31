' サイトの表示の設定（/account/display。基本ルール T）
'
' 【サイト管理者だけ】Page_Load のいちばん先で RequireAdministrator（メニューに出さないだけでは守りにならない）。
' 【保存は PRG】うまくいったら /account/display?done=saved へ開き直す。断ったときは開き直さない（入れた値を消さない）。
' 【範囲】1〜20（SiteSettings.ListCountMin・ListCountMax）。数字の欄は送る前に直る（NumberInput）が、ここでもう一度確かめる。
' 【ロジックはこのページに書かない】書くのは Core/SiteSettings.vb の SaveListCounts、控えを捨てるのは Core/CacheReset.vb
Public Class SiteDisplayPage
    Inherits AccountPageBase

    Private Const SelfUrl As String = "/account/display"

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        RequireAdministrator()

        If IsPostBack Then
            FormErrors.Clear(NoticeBox, NewMemberBox, BlogRecentBox, SaveButton)
            Return
        End If

        NoticeBox.Text = SiteSettings.SidebarNoticeCount.ToString(Globalization.CultureInfo.InvariantCulture)
        NewMemberBox.Text = SiteSettings.SidebarNewMemberCount.ToString(Globalization.CultureInfo.InvariantCulture)
        BlogRecentBox.Text = SiteSettings.BlogRecentCount.ToString(Globalization.CultureInfo.InvariantCulture)

        If String.Equals(Request.QueryString("done"), "saved", StringComparison.Ordinal) Then
            DoneText.Text = "表示の設定を保存しました。"
        End If

    End Sub


    Protected Sub SaveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles SaveButton.Click

        Dim values(2) As Integer
        Dim boxes As TextBox() = {NoticeBox, NewMemberBox, BlogRecentBox}

        For i As Integer = 0 To boxes.Length - 1
            Dim v As Integer
            If Not Integer.TryParse(boxes(i).Text.Trim(), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, v) OrElse
               v < SiteSettings.ListCountMin OrElse v > SiteSettings.ListCountMax Then
                ErrorText.Text = HttpUtility.HtmlEncode(String.Format("{0}〜{1} の数字で入れてください。", SiteSettings.ListCountMin, SiteSettings.ListCountMax))
                FormErrors.MarkField(boxes(i))
                Return
            End If
            values(i) = v
        Next

        Dim saved As Boolean

        Try
            saved = SiteSettings.SaveListCounts(values(0), values(1), values(2))
        Catch ex As Exception
            ErrorReport.Report(ex, "SiteDisplayPage.Save")
            ErrorText.Text = "ただいま混み合っています。少し待ってから、もう一度お試しください。"
            FormErrors.MarkHere(SaveButton)
            Return
        End Try

        If Not saved Then
            ' DB に設定の行がまだ無い（Database/InsertData/SiteSettings.sql を流す前）
            ErrorText.Text = "設定の行が DB にまだありません。Database\InsertData\SiteSettings.sql を流してから、もう一度保存してください。"
            FormErrors.MarkHere(SaveButton)
            Return
        End If

        ' 【設定の控えと、右の枠の一覧の控えを、全部捨てる】捨てないと、最大10分古い数のまま
        CacheReset.ResetAll()

        Response.Redirect(SelfUrl & "?done=saved")

    End Sub

End Class
