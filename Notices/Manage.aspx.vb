' 管理人からのお知らせの画面（ブログ型の⑤）
'
' 【URLは /account/notices】
'
' 【サイト管理者だけ】ログインの確かめ（AccountPageBase）のうえに、
' Page_Load のいちばん先で RequireAdministrator を通してある。
' **管理メニューに出さないだけでは守りにならない**（URL を直に打てる）。
'
' 【カテゴリの画面（Articles/Categories.aspx.vb）と同じ形にそろえてある】
' GridView のインライン編集＋下に「追加」。片方を直したら、もう片方も見ること。
'
' 【ロジックはこのページに書かない】
' 検査は Notices/SiteNoticeRules.vb、DB は Notices/SiteNoticeStore.vb が持つ。
' 画面が全部書き直しになっても、その2つはそのまま移せる
Public Class SiteNoticeManagePage
    Inherits AccountPageBase

    Private Const SelfUrl As String = "/account/notices"

    ' 【押したあと画面の位置を飛ばさない】（基本ルール U・Scripts/site.js の「17」）
    ' 画面が戻る先の行（［やめる］のときの行の番号）。-1 なら印を付けない。保存のあとは ?at=お知らせの番号 で運ぶ
    Private _returnRowIndex As Integer = -1

    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        ' 【いちばん先に、ロールを見る】（IsPostBack の外）
        ' ポストバックでも通すこと——開いたままの画面から押されることがある。
        ' 【この Redirect を Try/Catch の中へ移さないこと】ThreadAbortException を投げる
        RequireAdministrator()

        ' 【包んで送る欄をつなぐ】
        WireEncode(NewBodyBox, NewBodyEncoded)
        WireEncode(NewLinkBox, NewLinkEncoded)

        If IsPostBack Then

            ' 【前の回に付けた「理由をどこに出すか」の印を消す】ViewState に残るので、消さないと直したあとも赤いまま
            ClearErrorMarks()

            ' 【包んで届いた値を、ここで欄へ戻す】（Page_Load＝検査より前）
            ' 見えている欄は送る瞬間に止めてあって POST に乗らないので、戻す前に検査（RequiredFieldValidator）が走ると
            ' 「お知らせの文を入力してください」と的外れに断る（開発機で踏んだ）。
            ' 編集している行の欄は、ViewState から作り直されたあと（Load の時点）に在る
            DecodeInto(NewBodyBox, NewBodyEncoded, SiteNoticeRules.BodyMaxLength, "お知らせの文")
            DecodeInto(NewLinkBox, NewLinkEncoded, SiteNoticeRules.LinkMaxLength, "行き先")

            If NoticeGrid.EditIndex >= 0 AndAlso NoticeGrid.EditIndex < NoticeGrid.Rows.Count Then
                Dim row As GridViewRow = NoticeGrid.Rows(NoticeGrid.EditIndex)
                DecodeInto(TryCast(row.FindControl("BodyBox"), TextBox), TryCast(row.FindControl("BodyEncoded"), HiddenField),
                           SiteNoticeRules.BodyMaxLength, "お知らせの文")
                DecodeInto(TryCast(row.FindControl("LinkBox"), TextBox), TryCast(row.FindControl("LinkEncoded"), HiddenField),
                           SiteNoticeRules.LinkMaxLength, "行き先")
            End If

            Return

        End If

        ' 【うまくいったら開き直す（PRG）】（PRG を毎回の基本にする）
        ' 前は、追加・編集・削除のあとも同じポストバックの結果を描いていたので、
        ' 「戻る」や F5 で同じお知らせが2件入った。今は ?done=〇〇 で開き直し、文言はこちらが持つ
        ' （クエリ文字列で文言そのものを運ばない＝決まり）
        Select Case Request.QueryString("done")
            Case "added"
                DoneText.Text = HttpUtility.HtmlEncode("お知らせを追加しました。")
            Case "edited"
                DoneText.Text = HttpUtility.HtmlEncode("お知らせを編集しました。")
            Case "deleted"
                DoneText.Text = HttpUtility.HtmlEncode("お知らせを削除しました。")
        End Select


        ' 【新しく足すときの日付は、今日を入れておく】いちばん多い使い方なので
        NewDateBox.Text = DateTime.Now.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

        BindGrid()

    End Sub


    ' === 一覧 ===
    Private Sub BindGrid()

        Dim notices As List(Of SiteNoticeStore.NoticeRecord)

        Try
            notices = SiteNoticeStore.ListAll()
        Catch ex As Exception

            ' 【失敗を、空の一覧で返さないこと】
            ' 「成功して0件」と区別が付かず、DB が一瞬詰まっただけで
            ' 「お知らせは、まだありません」と出てしまう
            ErrorReport.Report(ex, "SiteNoticeManagePage.BindGrid")

            ErrorText.Text = HttpUtility.HtmlEncode(TroubleMessage)
            NoticeGrid.Visible = False
            Return

        End Try

        NoticeGrid.Visible = True
        NoticeGrid.DataSource = notices
        NoticeGrid.DataBind()

        ' 【GridView の見出しの行は、既定では <tbody> の中に出る】（カテゴリの画面と同じ手当て）
        ' UseAccessibleHeader="True" が変えるのは「<td> ではなく <th> にする」ことだけで、
        ' 置き場所は <tbody> のままである。そのため狭い画面の CSS
        ' （.admin-table thead { display: none }）が効かず、見出しだけの空っぽな箱が表の上に出る
        If NoticeGrid.HeaderRow IsNot Nothing Then
            NoticeGrid.HeaderRow.TableSection = TableRowSection.TableHeader
        End If

    End Sub


    ' ============================================================
    ' マークアップ（<%#: %>）から呼ぶ、出すための文字
    ' ============================================================

    ' 一覧に出す日付
    Protected Function DateText(item As Object) As String

        Dim record As SiteNoticeStore.NoticeRecord = TryCast(item, SiteNoticeStore.NoticeRecord)

        If record Is Nothing Then
            Return ""
        End If

        Return SiteNoticeRules.DateText(record.NoticeDate)

    End Function


    ' 直す行の <input type="date"> に入れる値
    '
    ' 【yyyy-MM-dd に決まっている】<input type="date"> が受け取る形はこれだけである。
    ' **文化を渡して組み立てること**（渡さないと、共用サーバーのカルチャ次第で和暦になる）
    Protected Function DateValue(item As Object) As String

        Dim record As SiteNoticeStore.NoticeRecord = TryCast(item, SiteNoticeStore.NoticeRecord)

        If record Is Nothing Then
            Return ""
        End If

        Return record.NoticeDate.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

    End Function


    ' ============================================================
    ' 直す
    ' ============================================================

    Protected Sub NoticeGrid_RowEditing(sender As Object, e As GridViewEditEventArgs) _
        Handles NoticeGrid.RowEditing

        ClearNotices()

        NoticeGrid.EditIndex = e.NewEditIndex
        BindGrid()

    End Sub


    Protected Sub NoticeGrid_RowCancelingEdit(sender As Object, e As GridViewCancelEditEventArgs) _
        Handles NoticeGrid.RowCancelingEdit

        ClearNotices()

        NoticeGrid.EditIndex = -1
        _returnRowIndex = e.RowIndex
        BindGrid()

    End Sub


    ' === 狭い画面のための data-label を入れる（カテゴリの画面と同じ） ===
    '
    ' 700px 以下では見出しの行を隠して、1行を「札のような箱」にする。
    ' そのとき各マスの前に、この文字が項目名として出る（Content/Site.css の ::before）。
    ' **列の見出しから写している**ので、見出しを変えたときに直し忘れが起きない
    Protected Sub NoticeGrid_RowDataBound(sender As Object, e As GridViewRowEventArgs) _
        Handles NoticeGrid.RowDataBound

        If e.Row.RowType <> DataControlRowType.DataRow Then
            Return
        End If

        For i As Integer = 0 To e.Row.Cells.Count - 1

            Dim field As DataControlField = NoticeGrid.Columns(i)

            If Not String.IsNullOrEmpty(field.HeaderText) Then
                e.Row.Cells(i).Attributes("data-label") = field.HeaderText
            End If

        Next

        ' 【押したあと、この行へ画面を戻す】（基本ルール U）［やめる］のあとの行・保存のあとの行（?at=番号）
        If (e.Row.RowState And DataControlRowState.Edit) <> DataControlRowState.Edit Then
            Dim id As Integer = CInt(DataBinder.Eval(e.Row.DataItem, "NoticeId"))
            Dim at As Integer
            If e.Row.RowIndex = _returnRowIndex OrElse
               (Integer.TryParse(Request.QueryString("at"), at) AndAlso at = id) Then
                e.Row.Attributes("data-return-here") = "1"
                e.Row.CssClass = (e.Row.CssClass & " is-target").Trim()
            End If
        End If

        ' 【編集している行】包んで送る欄をつなぎ、この行の欄だけを確かめる区切りにする（site.js の「7」「8」）
        If (e.Row.RowState And DataControlRowState.Edit) = DataControlRowState.Edit Then

            WireEncode(TryCast(e.Row.FindControl("BodyBox"), TextBox), TryCast(e.Row.FindControl("BodyEncoded"), HiddenField))
            WireEncode(TryCast(e.Row.FindControl("LinkBox"), TextBox), TryCast(e.Row.FindControl("LinkEncoded"), HiddenField))
            e.Row.Attributes("data-validate-scope") = "1"

            ' 【編集の欄は、表の幅いっぱいの1つのマス】（基本ルール Q）
            ' 欄は全部、最初の列（日付）の EditItemTemplate に入れてある。そのマスを全部の列にまたがらせ、ほかのマスは出さない。
            ' 狭い画面の「札」の項目名（data-label）も、このマスには付けない（中に項目名が並んでいるため）
            Dim cell As TableCell = e.Row.Cells(0)
            cell.ColumnSpan = e.Row.Cells.Count
            cell.Attributes.Remove("data-label")
            cell.CssClass = "notice-edit-cell"

            ' 【編集を開いたら、その欄へ画面を戻し、文の欄にカーソル】（基本ルール U。Scripts/site.js の「17」）
            cell.Attributes("data-return-here") = "1"
            Dim bodyForFocus As TextBox = TryCast(e.Row.FindControl("BodyBox"), TextBox)
            If bodyForFocus IsNot Nothing Then
                bodyForFocus.Attributes("data-focus-here") = "1"
            End If

            For i As Integer = 1 To e.Row.Cells.Count - 1
                e.Row.Cells(i).Visible = False
            Next

        End If

    End Sub


    ' === 保存 ===
    Protected Sub NoticeGrid_RowUpdating(sender As Object, e As GridViewUpdateEventArgs) _
        Handles NoticeGrid.RowUpdating

        ClearNotices()

        ' 【この行の検査だけを走らせる】下の「追加」の空欄に引っかからないよう、
        ' ValidationGroup を分けてある
        Page.Validate("noticeEdit")

        If Not Page.IsValid Then
            e.Cancel = True
            Return
        End If

        Dim row As GridViewRow = NoticeGrid.Rows(e.RowIndex)

        Dim dateBox As TextBox = TryCast(row.FindControl("DateBox"), TextBox)
        Dim bodyBox As TextBox = TryCast(row.FindControl("BodyBox"), TextBox)
        Dim linkBox As TextBox = TryCast(row.FindControl("LinkBox"), TextBox)
        Dim visibleBox As CheckBox = TryCast(row.FindControl("VisibleBox"), CheckBox)

        If dateBox Is Nothing OrElse bodyBox Is Nothing OrElse
           linkBox Is Nothing OrElse visibleBox Is Nothing Then
            e.Cancel = True
            Return
        End If

        ' 【番号は DataKeys から取る】画面から送られてきた値ではないので、行の取り違えが起きない
        Dim noticeId As Integer = CInt(NoticeGrid.DataKeys(e.RowIndex).Value)

        Dim record As SiteNoticeStore.NoticeRecord = Nothing
        Dim badField As String = Nothing

        ' 【包んで届いた値は、Page_Load で欄へ戻してある】戻せなかったときの理由は _decodeReason
        Dim reason As String = _decodeReason

        If reason Is Nothing Then
            reason = BuildRecord(dateBox.Text, bodyBox.Text, linkBox.Text, visibleBox.Checked, record, badField)
        End If

        e.Cancel = True

        If reason Is Nothing Then

            record.NoticeId = noticeId

            Try

                If Not SiteNoticeStore.Update(record) Then
                    reason = "そのお知らせは、もうありません。"
                End If

            Catch ex As Exception
                ErrorReport.Report(ex, "SiteNoticeManagePage.Update")
                reason = TroubleMessage
            End Try

        End If

        If reason IsNot Nothing Then

            ' 【直しかけの行は、開いたままにする】閉じると打った内容が消える（断ったときは開き直さない）
            ErrorText.Text = HttpUtility.HtmlEncode(reason)

            ' 【理由は、その行のそばに出す】悪い欄が分かればその欄の上、分からなければ行のすぐ上
            If Not MarkInvalid(badField, dateBox, bodyBox, linkBox) Then
                FormErrors.MarkHere(row)
            End If

            Return

        End If

        ' 【公開側の控えを捨てる】捨てないと、右の枠が最大10分古いまま
        SiteNotices.Invalidate()

        ' 【開き直す（PRG）】Try/Catch の外。Redirect は ThreadAbortException を投げる
        ' 【?at=番号】開き直したら、直した行へ画面を戻す（基本ルール U）
        Response.Redirect(SelfUrl & "?done=edited&at=" & noticeId.ToString(Globalization.CultureInfo.InvariantCulture))

    End Sub


    ' === 消す ===
    Protected Sub NoticeGrid_RowDeleting(sender As Object, e As GridViewDeleteEventArgs) _
        Handles NoticeGrid.RowDeleting

        ClearNotices()

        Dim noticeId As Integer = CInt(NoticeGrid.DataKeys(e.RowIndex).Value)

        Dim reason As String = Nothing

        Try

            If Not SiteNoticeStore.Delete(noticeId) Then
                reason = "そのお知らせは、もうありません。"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "SiteNoticeManagePage.Delete")
            reason = TroubleMessage
        End Try

        e.Cancel = True

        If reason IsNot Nothing Then
            ErrorText.Text = HttpUtility.HtmlEncode(reason)
            ' 【理由は、消そうとした行のすぐ上に出す】
            FormErrors.MarkHere(NoticeGrid.Rows(e.RowIndex))
            Return
        End If

        SiteNotices.Invalidate()

        Response.Redirect(SelfUrl & "?done=deleted")

    End Sub


    ' === 追加 ===
    Protected Sub AddButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles AddButton.Click

        ClearNotices()

        If Not Page.IsValid Then
            Return
        End If

        Dim record As SiteNoticeStore.NoticeRecord = Nothing
        Dim badField As String = Nothing

        ' 【包んで届いた値は、Page_Load で欄へ戻してある】戻せなかったときの理由は _decodeReason
        Dim reason As String = _decodeReason

        If reason Is Nothing Then
            reason = BuildRecord(NewDateBox.Text, NewBodyBox.Text, NewLinkBox.Text, NewVisibleBox.Checked, record, badField)
        End If

        ' 【二重送信を防ぐ】（Core/FormOnce.vb）送ったあと、戻るボタンで戻って
        ' もう一度押したときは、ここで止まる。**検査が全部通ったあと・実際に足す直前**に見る
        If reason Is Nothing AndAlso Not FormOnce.TryUse(Request) Then
            reason = FormOnce.AlreadySentMessage
        End If

        If reason Is Nothing Then

            Try
                SiteNoticeStore.Insert(record)
            Catch ex As Exception
                ErrorReport.Report(ex, "SiteNoticeManagePage.Insert")
                reason = TroubleMessage
            End Try

        End If

        If reason IsNot Nothing Then
            ' 【断ったときは開き直さない】打った内容を消さないため
            ErrorText.Text = HttpUtility.HtmlEncode(reason)

            ' 【理由は、追加の欄のそばに出す】悪い欄が分かればその欄の上、分からなければ「追加」のボタンのすぐ上
            ' （「もう送信済みです」も、どの欄とも言えない理由なので、ボタンのすぐ上）
            If Not MarkInvalid(badField, NewDateBox, NewBodyBox, NewLinkBox) Then
                FormErrors.MarkHere(AddButton)
            End If

            Return
        End If

        SiteNotices.Invalidate()

        ' 【開き直す（PRG）】欄は開き直した画面で空になり、日付は今日に戻る（Page_Load）
        Response.Redirect(SelfUrl & "?done=added")

    End Sub


    ' ============================================================
    ' 下回り
    ' ============================================================

    ' 打たれた値を確かめて、1行ぶんにまとめる
    '
    ' 戻り値 … 断る理由（受け付けてよければ Nothing。そのとき record に入る）
    '
    ' 【検査は SiteNoticeRules が持つ】画面側の Validator は親切であって、防御ではない
    ' （開発者ツールから外せる）。**ここを通らない道を作らないこと**
    ' badField … 断ったとき、どの欄が悪いか（"date"／"body"／"link"）。画面が aria-invalid を付ける
    Private Function BuildRecord(dateText As String, body As String, link As String,
                                 isVisible As Boolean,
                                 ByRef record As SiteNoticeStore.NoticeRecord,
                                 ByRef badField As String) As String

        record = Nothing
        badField = Nothing

        Dim noticeDate As Date
        Dim reason As String = SiteNoticeRules.ValidateDate(dateText, noticeDate)

        If reason IsNot Nothing Then
            badField = "date"
            Return reason
        End If

        reason = SiteNoticeRules.ValidateBody(body)

        If reason IsNot Nothing Then
            badField = "body"
            Return reason
        End If

        Dim cleanLink As String = Nothing
        reason = SiteNoticeRules.ValidateLink(link, cleanLink)

        If reason IsNot Nothing Then
            badField = "link"
            Return reason
        End If

        record = New SiteNoticeStore.NoticeRecord With {
            .NoticeDate = noticeDate,
            .Body = SiteNoticeRules.NormalizeBody(body),
            .LinkUrl = cleanLink,
            .IsVisible = isVisible}

        Return Nothing

    End Function


    ' 悪い欄に「この欄が悪い」の印を付ける（理由をその欄の上へ移すのは Scripts/site.js の「9」）
    '
    ' 戻り値 … 印を付けたか（どの欄とも言えない理由なら False。呼ぶ側が、行やボタンに印を付ける）
    Private Shared Function MarkInvalid(badField As String, dateBox As TextBox, bodyBox As TextBox, linkBox As TextBox) As Boolean

        Dim box As TextBox = Nothing

        Select Case badField
            Case "date" : box = dateBox
            Case "body" : box = bodyBox
            Case "link" : box = linkBox
        End Select

        If box Is Nothing Then
            Return False
        End If

        FormErrors.MarkField(box)

        Return True

    End Function


    ' 前の回に付けた印（この欄が悪い・ここに出す）を、全部消す
    Private Sub ClearErrorMarks()

        FormErrors.Clear(NewDateBox, NewBodyBox, NewLinkBox, AddButton)

        For Each row As GridViewRow In NoticeGrid.Rows
            FormErrors.Clear(row,
                             TryCast(row.FindControl("DateBox"), WebControl),
                             TryCast(row.FindControl("BodyBox"), WebControl),
                             TryCast(row.FindControl("LinkBox"), WebControl))
        Next

    End Sub


    ' 「送る直前に包む」欄をつなぐ（Scripts/site.js の「7」・Core/BodyTransport.vb）
    '
    ' 【なぜ包むのか】行き先に「javascript:」、文に「<script>」を書くと、
    ' 本番の WAF がアプリに届く前に 403 の画面を返していた（理由が何も出ない）。
    ' 包めば WAF を通り、**サーバーの SiteNoticeRules が理由を出して断る**（断るのはサーバーが本体）
    Private Shared Sub WireEncode(box As TextBox, hidden As HiddenField)

        If box Is Nothing OrElse hidden Is Nothing Then
            Return
        End If

        box.Attributes("data-encode-on-submit") = hidden.ClientID
        box.Attributes("data-encoded-prefix") = BodyTransport.EncodedPrefix

    End Sub


    ' 包んで届いた値を、見えている欄へ戻す（届いていなければ何もしない＝JS が動かなかったとき）
    '
    ' 【戻した値を欄へ入れ直す】見えている欄は送る瞬間だけ止めてあって POST に乗らないので、
    ' 入れ直さないと、検査が空だと断り、断って画面を返したときには打った文が消える（問い合わせの画面と同じ手当て）。
    ' 【戻せなかったとき】理由を _decodeReason に入れる（押されたボタンの処理が、その理由で断る）
    Private Sub DecodeInto(box As TextBox, hidden As HiddenField, maxLength As Integer, itemName As String)

        If box Is Nothing OrElse hidden Is Nothing OrElse String.IsNullOrEmpty(hidden.Value) Then
            Return
        End If

        Dim decoded As BodyTransport.DecodeResult =
            BodyTransport.Decode(hidden.Value, maxLength, itemName, "保存していません。")

        hidden.Value = ""

        If Not decoded.Ok Then
            If _decodeReason Is Nothing Then
                _decodeReason = decoded.Message
            End If
            Return
        End If

        box.Text = decoded.Html

    End Sub

    ' 包んで届いた値を戻せなかった理由（戻せたら Nothing）
    Private _decodeReason As String


    ' 一覧の「表示中／非表示」の印（押せないチェックをやめた）
    '
    ' 【戻すのは決まった文字列だけ】<%# %>（エスケープしない形）で出すので、外から来た値を混ぜないこと
    Protected Function VisibleMarkHtml(item As Object) As String

        Dim record As SiteNoticeStore.NoticeRecord = TryCast(item, SiteNoticeStore.NoticeRecord)

        If record IsNot Nothing AndAlso record.IsVisible Then
            Return "<span class=""status-mark status-mark--on"">表示中</span>"
        End If

        Return "<span class=""status-mark status-mark--off"">非表示</span>"

    End Function


    Private Sub ClearNotices()

        ErrorText.Text = ""
        DoneText.Text = ""

    End Sub

    ' 【見出しの行を <thead> へ移すのを、描く直前にもう一度やる】（見つけた）
    ' 上の BindGrid でも移しているが、**保存や追加が断られてポストバックが戻ると、BindGrid は呼ばれず、
    ' 表は ViewState から作り直される**。そのときは見出しの行が <tbody> に戻っていて、
    ' 狭い画面では見出しだけの箱が表の上に出ていた（開発機で、断られた追加のあとに見た）
    Protected Sub Page_PreRender(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.PreRender

        If NoticeGrid.HeaderRow IsNot Nothing Then
            NoticeGrid.HeaderRow.TableSection = TableRowSection.TableHeader
        End If

    End Sub

End Class
