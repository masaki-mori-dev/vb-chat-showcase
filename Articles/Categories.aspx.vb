' カテゴリの画面
'
' 【URLは /account/categories】
'
' 【GridView を、その場で直せる形（インライン編集）で使っている】
' 記事の一覧は Repeater、こちらは GridView。2世代目への移行を予行演習にする、という方針どおり
' （「管理画面とブログCMSでは、Web Forms を『あえて』使い倒す」）。
'
' 【SqlDataSource は使わない】
' SQL をマークアップに書く形で、移行時に対応物がまったく無い。
' RowUpdating のたびに ArticleCategoryStore を呼んで、自分で DataBind している。
'
' 【ロジックはこのページに書かない】
' 名前の検査は Articles/ArticleRules.vb、DBは Articles/ArticleCategoryStore.vb が持つ。
' 画面が全部書き直しになっても、その2つはそのまま移せる
Public Class ArticleCategoriesPage
    Inherits AccountPageBase

    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"


    Private Const SelfUrl As String = "/account/categories"

    ' 【押したあと画面の位置を飛ばさない】（基本ルール U・Scripts/site.js の「17」。お知らせの管理と同じ形）
    ' ［やめる］のときの行の番号。-1 なら印を付けない。保存のあとは ?at=カテゴリの番号 で運ぶ
    Private _returnRowIndex As Integer = -1


    ' ブログの切り替え（記事一覧｜カテゴリ｜ブログの設定）。決まった文字列だけ（Articles/BlogTabs.vb）
    Protected ReadOnly Property BlogTabsHtml As String
        Get
            Return BlogTabs.Html(CurrentAccount.RoleKey, Page.AppRelativeVirtualPath)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        If IsPostBack Then
            ' 【前の回に付けた「理由をどこに出すか」の印を消す】ViewState に残るため（Core/FormErrors.vb）
            ClearErrorMarks()
            Return
        End If

        ' 【書いてよいロールか】
        ' カテゴリは記事のためのものなので、記事を書けないロールには出さない。
        ' 断るのは ArticleCategoryStore ではなく、ここと下のボタンの中である
        If Not CanWrite() Then

            ' 【この Redirect を Try/Catch の中へ移さないこと】ThreadAbortException を投げる
            Response.Redirect("/account/articles")

        End If

        BindSortModes(NewSortModeList, ArticleRules.CategorySortMode.Specified)

        ' 【うまくいったら開き直す（PRG）】（PRG を毎回の基本にする）
        ' 「戻る」や F5 で同じ追加・削除が2回送られないように。文言はこちらが持つ（決まり）
        Select Case Request.QueryString("done")
            Case "added"
                DoneText.Text = HttpUtility.HtmlEncode("カテゴリを追加しました。")
            Case "edited"
                DoneText.Text = HttpUtility.HtmlEncode("カテゴリを編集しました。")
            Case "deleted"
                DoneText.Text = HttpUtility.HtmlEncode("カテゴリを削除しました。中の記事は残っています。")
        End Select

        BindGrid()

    End Sub


    ' === 一覧 ===
    Private Sub BindGrid()

        Dim categories As List(Of ArticleCategoryStore.CategoryRecord)

        Try
            categories = ArticleCategoryStore.GetAll(CurrentAccount.AccountId)
        Catch ex As Exception

            ' 【失敗を、空の一覧で返さないこと】
            ' 「成功して0件」と区別が付かず、DBが一瞬詰まっただけで
            ' 「カテゴリはまだありません」と出てしまう
            ErrorReport.Report(ex, "ArticleCategoriesPage.BindGrid")

            ErrorText.Text = HttpUtility.HtmlEncode(TroubleMessage)
            CategoryGrid.Visible = False
            Return

        End Try

        CategoryGrid.Visible = True
        CategoryGrid.DataSource = categories
        CategoryGrid.DataBind()

        ' 【GridView の見出しの行は、既定では <tbody> の中に出る】（開発機で踏んだ）
        ' UseAccessibleHeader="True" が変えるのは「<td> ではなく <th> にする」ことだけで、
        ' 置き場所は <tbody> のままである。
        ' そのため狭い画面の CSS（.admin-table thead { display: none }）が効かず、
        ' **見出しだけの空っぽな箱が1つ、表の上に出た。**
        ' ここで <thead> へ移している。読み上げソフトにとっても、こちらが正しい
        If CategoryGrid.HeaderRow IsNot Nothing Then
            CategoryGrid.HeaderRow.TableSection = TableRowSection.TableHeader
        End If

    End Sub


    ' === 直す（行の「直す」を押した）===
    Protected Sub CategoryGrid_RowEditing(sender As Object, e As GridViewEditEventArgs) _
        Handles CategoryGrid.RowEditing

        ClearNotices()

        CategoryGrid.EditIndex = e.NewEditIndex
        BindGrid()

    End Sub


    ' === やめる ===
    Protected Sub CategoryGrid_RowCancelingEdit(sender As Object, e As GridViewCancelEditEventArgs) _
        Handles CategoryGrid.RowCancelingEdit

        ClearNotices()

        CategoryGrid.EditIndex = -1
        _returnRowIndex = e.RowIndex
        BindGrid()

    End Sub


    ' === 直した行の、並べ方の選択肢を入れる ===
    '
    ' 【DataBound ではなく RowDataBound で入れている】
    ' EditItemTemplate の中のコントロールは、その行が組み立てられるときにしか掴めない。
    ' 選ばれている値を入れるのも、ここでやる
    Protected Sub CategoryGrid_RowDataBound(sender As Object, e As GridViewRowEventArgs) _
        Handles CategoryGrid.RowDataBound

        If e.Row.RowType <> DataControlRowType.DataRow Then
            Return
        End If

        ' 【狭い画面のための data-label を、ここで入れている】
        ' 700px 以下では見出しの行を隠して、1行を「札のような箱」にする。
        ' そのとき各マスの前に、この文字が項目名として出る（Content/Site.css の ::before）。
        ' 記事の一覧（Articles/Manage.aspx）は Repeater なので markup に直接書いてあるが、
        ' GridView はマスをこちらが組み立てないので、**列の見出しから写している**
        ' ——写すので、見出しを変えたときに直し忘れが起きない
        For i As Integer = 0 To e.Row.Cells.Count - 1

            Dim field As DataControlField = CategoryGrid.Columns(i)

            If Not String.IsNullOrEmpty(field.HeaderText) Then
                e.Row.Cells(i).Attributes("data-label") = field.HeaderText
            End If

        Next

        If (e.Row.RowState And DataControlRowState.Edit) <> DataControlRowState.Edit Then

            ' 【押したあと、この行へ画面を戻す】（基本ルール U）［やめる］のあとの行・保存のあとの行（?at=番号）
            Dim shown As ArticleCategoryStore.CategoryRecord = TryCast(e.Row.DataItem, ArticleCategoryStore.CategoryRecord)
            Dim at As Integer
            If e.Row.RowIndex = _returnRowIndex OrElse
               (shown IsNot Nothing AndAlso Integer.TryParse(Request.QueryString("at"), at) AndAlso at = shown.CategoryId) Then
                e.Row.Attributes("data-return-here") = "1"
                e.Row.CssClass = (e.Row.CssClass & " is-target").Trim()
            End If

            Return
        End If

        ' 【編集を開いたら、その行へ画面を戻し、名前の欄にカーソル】（基本ルール U）
        e.Row.Attributes("data-return-here") = "1"
        Dim nameForFocus As TextBox = TryCast(e.Row.FindControl("NameBox"), TextBox)
        If nameForFocus IsNot Nothing Then
            nameForFocus.Attributes("data-focus-here") = "1"
        End If

        Dim list As DropDownList = TryCast(e.Row.FindControl("SortModeList"), DropDownList)
        Dim record As ArticleCategoryStore.CategoryRecord =
            TryCast(e.Row.DataItem, ArticleCategoryStore.CategoryRecord)

        If list Is Nothing OrElse record Is Nothing Then
            Return
        End If

        BindSortModes(list, record.SortMode)

    End Sub


    ' === 保存 ===
    Protected Sub CategoryGrid_RowUpdating(sender As Object, e As GridViewUpdateEventArgs) _
        Handles CategoryGrid.RowUpdating

        ClearNotices()

        ' 【この行の検査だけを走らせる】
        ' 下の「カテゴリを足す」の空欄に引っかからないよう、ValidationGroup を分けてある
        Page.Validate("categoryEdit")

        If Not Page.IsValid Then
            e.Cancel = True
            Return
        End If

        Dim row As GridViewRow = CategoryGrid.Rows(e.RowIndex)

        Dim nameBox As TextBox = TryCast(row.FindControl("NameBox"), TextBox)
        Dim modeList As DropDownList = TryCast(row.FindControl("SortModeList"), DropDownList)
        Dim orderBox As TextBox = TryCast(row.FindControl("SortOrderBox"), TextBox)

        If nameBox Is Nothing OrElse modeList Is Nothing OrElse orderBox Is Nothing Then
            e.Cancel = True
            Return
        End If

        ' 【番号は DataKeys から取る】
        ' 画面から送られてきた値ではないので、行の取り違えが起きない。
        ' それでも **ArticleCategoryStore が AccountId でも絞っている**（二重の守り）
        Dim categoryId As Integer = CInt(CategoryGrid.DataKeys(e.RowIndex).Value)

        Dim reason As String

        Try

            reason = ArticleCategoryStore.Update(
                CurrentAccount.AccountId,
                categoryId,
                nameBox.Text,
                ArticleRules.NormalizeSortMode(ParseInt(modeList.SelectedValue)),
                OrderValue(orderBox.Text))

        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleCategoriesPage.Update")
            reason = TroubleMessage

        End Try

        e.Cancel = True

        If reason IsNot Nothing Then

            ' 【直しかけの行は、開いたままにする】閉じると打った内容が消える
            ErrorText.Text = HttpUtility.HtmlEncode(reason)

            ' 【理由は、その行のそばに出す】名前の話なら名前の欄の上、そうでなければ行のすぐ上
            If IsAboutName(reason) Then
                FormErrors.MarkField(nameBox)
            Else
                FormErrors.MarkHere(row)
            End If

            Return

        End If

        ' 【開き直す（PRG）】Try/Catch の外。Redirect は ThreadAbortException を投げる。?at=番号＝直した行へ画面を戻す（基本ルール U）
        Response.Redirect(SelfUrl & "?done=edited&at=" & categoryId.ToString(Globalization.CultureInfo.InvariantCulture))

    End Sub


    ' === 消す ===
    '
    ' 【記事は消さない。カテゴリだけが外れる】
    ' その手当ては ArticleCategoryStore.Delete が、1つのトランザクションの中でやっている
    Protected Sub CategoryGrid_RowDeleting(sender As Object, e As GridViewDeleteEventArgs) _
        Handles CategoryGrid.RowDeleting

        ClearNotices()

        Dim categoryId As Integer = CInt(CategoryGrid.DataKeys(e.RowIndex).Value)

        Dim reason As String

        Try
            reason = ArticleCategoryStore.Delete(CurrentAccount.AccountId, categoryId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleCategoriesPage.Delete")
            reason = TroubleMessage

        End Try

        e.Cancel = True

        If reason IsNot Nothing Then
            ErrorText.Text = HttpUtility.HtmlEncode(reason)
            ' 【理由は、消そうとした行のすぐ上に出す】
            FormErrors.MarkHere(CategoryGrid.Rows(e.RowIndex))
            Return
        End If

        Response.Redirect(SelfUrl & "?done=deleted")

    End Sub


    ' === 足す ===
    Protected Sub AddButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles AddButton.Click

        ClearNotices()

        If Not Page.IsValid Then
            Return
        End If

        ' 【画面から消すだけでは守りにならない】
        ' ロールが変わった直後に、開いたままの画面から押されることがある
        If Not CanWrite() Then
            ErrorText.Text = HttpUtility.HtmlEncode("このアカウントでは、記事を書けません。")
            FormErrors.MarkHere(AddButton)
            Return
        End If

        Dim reason As String = Nothing

        ' 【二重送信を防ぐ】（Core/FormOnce.vb）送ったあと、戻るボタンで戻って
        ' もう一度押したときは、ここで止まる。**検査が全部通ったあと・実際に足す直前**に見る
        ' 【カテゴリは、同じ名前を断る決まりもある】（ArticleCategoryStore.Create）。そちらは「同じ名前が2つできない」という
        ' データの決まりで、こちらは「同じ送信を2回受け付けない」。名前の検査より先に見るので、
        ' 戻って押し直した人には「その名前のカテゴリは、すでにあります」ではなく、こちらの理由が出る
        If ArticleRules.ValidateCategoryName(NewNameBox.Text) Is Nothing AndAlso Not FormOnce.TryUse(Request) Then
            reason = FormOnce.AlreadySentMessage
        End If

        If reason Is Nothing Then

            Try

                reason = ArticleCategoryStore.Create(
                    CurrentAccount.AccountId,
                    NewNameBox.Text,
                    ArticleRules.NormalizeSortMode(ParseInt(NewSortModeList.SelectedValue)),
                    OrderValue(NewSortOrderBox.Text))

            Catch ex As Exception

                ErrorReport.Report(ex, "ArticleCategoriesPage.Create")
                reason = TroubleMessage

            End Try

        End If

        If reason IsNot Nothing Then

            ' 【打った名前は残す】打ち直させないため
            ErrorText.Text = HttpUtility.HtmlEncode(reason)

            ' 【理由は、追加の欄のそばに出す】（前は、ページのいちばん上へ飛んでいた）
            ' 名前の話なら名前の欄の上、そうでなければ「追加」のボタンのすぐ上
            If IsAboutName(reason) Then
                FormErrors.MarkField(NewNameBox)
            Else
                FormErrors.MarkHere(AddButton)
            End If

            BindGrid()
            Return

        End If

        ' 【開き直す（PRG）】欄は開き直した画面で空に戻る
        Response.Redirect(SelfUrl & "?done=added")

    End Sub


    ' ============================================================
    ' 中で使うもの
    ' ============================================================

    ' その理由は、名前の欄の話か（名前が空・長すぎる・もう在る）
    '
    ' 【名前の話でないのは、3つだけ】混み合っている（TroubleMessage）・もう無い（NotFoundMessage）・もう送信済み（FormOnce）。
    ' どちらも決まった文なので、文そのもので見分けられる。理由を足したら、ここも見ること
    Private Shared Function IsAboutName(reason As String) As Boolean

        Return Not String.Equals(reason, TroubleMessage, StringComparison.Ordinal) AndAlso
               Not String.Equals(reason, ArticleCategoryStore.NotFoundMessage, StringComparison.Ordinal) AndAlso
               Not String.Equals(reason, FormOnce.AlreadySentMessage, StringComparison.Ordinal)

    End Function


    ' 前の回に付けた印（この欄が悪い・ここに出す）を、全部消す
    Private Sub ClearErrorMarks()

        FormErrors.Clear(NewNameBox, AddButton)

        For Each row As GridViewRow In CategoryGrid.Rows
            FormErrors.Clear(row, TryCast(row.FindControl("NameBox"), WebControl))
        Next

    End Sub


    Private Function CanWrite() As Boolean
        Return RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles)
    End Function


    ' 並べ方の3択を入れる
    '
    ' 【マークアップに <asp:ListItem> をベタ書きしないこと】
    ' 並べ方を1つ足したときに、画面とコードの2か所を直すことになる。
    ' このページには入れる場所が2つある（行を直すとき・足すとき）ので、なおさらである
    Private Shared Sub BindSortModes(list As DropDownList, selected As ArticleRules.CategorySortMode)

        list.Items.Clear()

        For Each mode As ArticleRules.CategorySortMode In ArticleRules.AllSortModes()

            list.Items.Add(New ListItem(
                ArticleRules.SortModeDisplayName(mode),
                CInt(mode).ToString(Globalization.CultureInfo.InvariantCulture)))

        Next

        list.SelectedValue = CInt(selected).ToString(Globalization.CultureInfo.InvariantCulture)

    End Sub


    Private Sub ClearNotices()

        ErrorText.Text = ""
        DoneText.Text = ""

    End Sub


    ' カテゴリの並び順は NULL を許していないので、空欄は 0 にする
    '
    ' 【記事の並び順（NULL＝番号なし）とは、わざと違う】
    ' カテゴリは数が少なく、「番号なし」を作る意味が無い
    Private Shared Function OrderValue(text As String) As Integer

        Dim value As Integer? = ArticleRules.NormalizeOrder(text)

        If value.HasValue Then
            Return value.Value
        End If

        Return 0

    End Function


    Private Shared Function ParseInt(text As String) As Integer

        Dim value As Integer

        If String.IsNullOrEmpty(text) Then
            Return 0
        End If

        If Not Integer.TryParse(text, Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, value) Then
            Return 0
        End If

        Return value

    End Function


    ' マークアップから呼ぶ（削除の確認の窓の文）
    '
    ' 【カテゴリ名を出す】（記事の一覧の削除と同じ形＝Manage.aspx.vb の DeleteConfirmText）
    ' 同じような名前のカテゴリが並んでいると、どれを消そうとしているのか窓の中で分からなかった。
    ' 名前は vbChatConfirm が textContent で入れるので、ここでエスケープしない
    ' （属性の中のエスケープは、LinkButton が描くときにする）
    Protected Function DeleteConfirmText(item As Object) As String

        Const Tail As String = "中の記事は削除されず、カテゴリだけが外れます。よろしいですか？"

        Dim record As ArticleCategoryStore.CategoryRecord =
            TryCast(item, ArticleCategoryStore.CategoryRecord)

        If record Is Nothing OrElse String.IsNullOrEmpty(record.Name) Then
            Return "このカテゴリを削除します。" & Tail
        End If

        Return "「" & record.Name & "」を削除します。" & Tail

    End Function

    ' マークアップから呼ぶ（並べ方の日本語）
    Protected Function SortModeText(item As Object) As String

        Dim record As ArticleCategoryStore.CategoryRecord =
            TryCast(item, ArticleCategoryStore.CategoryRecord)

        If record Is Nothing Then
            Return ""
        End If

        Return ArticleRules.SortModeDisplayName(record.SortMode)

    End Function

    ' 【見出しの行を <thead> へ移すのを、描く直前にもう一度やる】（見つけた）
    ' 上の BindGrid でも移しているが、**保存や追加が断られてポストバックが戻ると、BindGrid は呼ばれず、
    ' 表は ViewState から作り直される**。そのときは見出しの行が <tbody> に戻っていて、
    ' 狭い画面では見出しだけの箱が表の上に出ていた（開発機で、断られた追加のあとに見た）
    Protected Sub Page_PreRender(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.PreRender

        If CategoryGrid.HeaderRow IsNot Nothing Then
            CategoryGrid.HeaderRow.TableSection = TableRowSection.TableHeader
        End If

    End Sub

End Class
