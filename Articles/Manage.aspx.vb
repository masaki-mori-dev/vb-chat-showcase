' 記事の一覧
'
' 【URLは /account/articles】置き場所のフォルダ（Articles\）とは一致していない。
' ページの置き場所は「機能ごと」（Feature Folders）、URLは「ログインの向こう側」で決めている。
' Accounts\ フォルダのURLが /account（複数形と単数形）なのと、同じ考え方である
' （あちらは403の罠を避けるためだったが、こちらは機能で分けた結果）。
'
' 【AccountPageBase を継承している】
' ログインしていなければ、画面が組み立てられる前にログインの画面へ返される。
' **「記事を書けるロールか」は、それとは別に確かめている**（下の Page_Load）
Public Class ArticleManagePage
    Inherits AccountPageBase

    ' 絞り込みの「すべて」を表す値（DropDownList の value）
    Private Const AllCategories As String = "0"


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        If Not IsPostBack Then

            ' 削除のあと、編集の画面から渡されたお知らせ
            ' （**保存の知らせは書く画面のほうに出る**。下の ShowNotice）
            '
            ' 【クエリ文字列で文言そのものを運ばないこと】
            ' 「?msg=〇〇」にすると、**誰でもその文字列を作れる**——
            ' リンクを送りつけて、他人の画面に好きな文を出せる。
            ' そこで「どの知らせか」だけを合図で受け取り、文言はこちら側が持つ
            ' （退室の理由を URL で運ばないと決めたのと、まったく同じ判断）
            ShowNotice(Request.QueryString("done"))

            ' 【ゴミ箱の画面】?view=trash。一覧とは別の画面にしてある（カテゴリのドロップダウンに混ぜない）
            If IsTrashView Then
                ShowTrashView()
                Return
            End If

            BindCategoryFilter()

            ' 【削除のあとに開き直したとき、絞り込みを戻す】（?category=。無い番号なら何もしない＝「すべて」）
            Dim wanted As String = Request.QueryString("category")

            If Not String.IsNullOrEmpty(wanted) AndAlso CategoryFilter.Items.FindByValue(wanted) IsNot Nothing Then
                CategoryFilter.SelectedValue = wanted
            End If

            BindArticles()
            BindTrashLink()

        End If

        ' 【ポストバックでは、ここで一覧を組み直さない】（変えた）
        ' 一覧の各行に削除のボタンを置いたので、Load で組み直すと、
        ' 押されたボタンが入っている行を作り直してから押された出来事を探すことになる。
        ' 一覧は ViewState から元どおりに戻るので、組み直すのは
        ' 「絞り込みが変わったとき」と「削除したあと」の2か所だけにしてある

    End Sub


    ' ブログの切り替え（記事一覧｜カテゴリ｜ブログの設定）。決まった文字列だけ（Articles/BlogTabs.vb）
    Protected ReadOnly Property BlogTabsHtml As String
        Get
            Return BlogTabs.Html(CurrentAccount.RoleKey, Page.AppRelativeVirtualPath)
        End Get
    End Property


    ' === 絞り込みを変えた ===
    Protected Sub CategoryFilter_SelectedIndexChanged(ByVal sender As Object, ByVal e As EventArgs) _
        Handles CategoryFilter.SelectedIndexChanged

        DoneText.Text = ""
        BindArticles()

    End Sub


    ' === 一覧の行の「削除」 ===
    '
    ' 【削除は POST で届く】理由は Manage.aspx の削除ボタンのコメント。
    ' 【番号は CommandArgument から読むが、それを信じて消すのではない】
    ' ArticleStore.Delete が ArticleId と AccountId の両方で絞るので、
    ' 番号を書き換えられても、他人の記事は消えない（見つからないとだけ返る）
    Protected Sub ArticleList_ItemCommand(ByVal source As Object, ByVal e As RepeaterCommandEventArgs) _
        Handles ArticleList.ItemCommand

        If e.CommandName <> "DeleteArticle" Then
            Return
        End If

        ErrorText.Text = ""
        DoneText.Text = ""

        Dim articleId As Integer

        If Not Integer.TryParse(Convert.ToString(e.CommandArgument), Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, articleId) OrElse articleId <= 0 Then
            Return
        End If

        Dim reason As String

        Try
            ' 【ゴミ箱へ移す】すぐには消さない。完全に消すのはゴミ箱から（か、30日たって 1日1回の片付けが）
            reason = ArticleStore.MoveToTrash(CurrentAccount.AccountId, articleId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleManagePage.MoveToTrash")
            reason = "ただいま混み合っています。少し待ってから、もう一度お試しください。"

        End Try

        If reason IsNot Nothing Then
            ' 【断ったときは開き直さない】その場で理由を出す
            ErrorText.Text = HttpUtility.HtmlEncode(reason)

            ' 【理由は、消そうとした行のすぐ上に出す】（決まり＝理由は操作したところのそばに）
            ' 一覧を描き直すときに、その行に印を付ける（下の ErrorHereAttribute）。
            ' 行がもう無ければ（記事が見つからなかった）、理由は一覧の上の、いまの場所に出る
            _errorArticleId = articleId

            BindArticles()
            Return
        End If

        ' 【開き直す（PRG）】「戻る」や F5 で削除が2回送られないように。
        ' 【絞り込みを持っていく】開き直しても、同じカテゴリの一覧のまま（?category=）
        Dim url As String = "/account/articles?done=trashed"
        Dim category As Integer? = SelectedCategoryId()

        If category.HasValue Then
            url &= "&category=" & category.Value.ToString(Globalization.CultureInfo.InvariantCulture)
        End If

        ' Try/Catch の外。Redirect は ThreadAbortException を投げる
        Response.Redirect(url)

    End Sub


    ' 削除を断った記事の番号（その行のすぐ上に、理由を出す。0 なら無い）
    Private _errorArticleId As Integer

    ' 一覧の行の <tr> に付ける印（Scripts/site.js の「9」が、理由をこの行のすぐ上へ移す）
    '
    ' 【戻すのは決まった文字列だけ】<%# %>（エスケープしない形）で出すので、外から来た値を混ぜないこと
    Protected Function ErrorHereAttribute(item As Object) As String

        Dim record As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If record IsNot Nothing AndAlso _errorArticleId > 0 AndAlso record.ArticleId = _errorArticleId Then
            Return " data-error-here=""1"""
        End If

        Return ""

    End Function


    ' === カテゴリの絞り込み ===
    Private Sub BindCategoryFilter()

        CategoryFilter.Items.Clear()
        CategoryFilter.Items.Add(New ListItem("すべて", AllCategories))

        Dim categories As List(Of ArticleCategoryStore.CategoryRecord)

        Try
            categories = ArticleCategoryStore.GetAll(CurrentAccount.AccountId)
        Catch ex As Exception

            ' 【一覧そのものは出す】絞り込みが使えないだけで、記事が見えなくなるほどではない
            ErrorReport.Report(ex, "ArticleManagePage.BindCategoryFilter")
            Return

        End Try

        For Each c As ArticleCategoryStore.CategoryRecord In categories

            ' 【カテゴリ名だけ】（前は「（指定した順）」などの並べ方を添えていた）
            CategoryFilter.Items.Add(New ListItem(
                c.Name,
                c.CategoryId.ToString(Globalization.CultureInfo.InvariantCulture)))

        Next

    End Sub


    ' === 記事の一覧 ===
    Private Sub BindArticles()

        ' 【書けるロールかを、画面にも出す】
        ' 書けないロールでも一覧は見えてよい（自分の記事だから）。
        ' 書く口を出さないだけで、**断るのは保存のとき**である（ArticleStore.Save）
        Dim canWrite As Boolean =
            RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles)

        NewArticleLink.Visible = canWrite

        Dim articles As List(Of ArticleStore.ArticleRecord)

        Try
            articles = ArticleStore.GetList(CurrentAccount.AccountId, SelectedCategoryId())
        Catch ex As Exception

            ' 【失敗を、空の一覧で返さないこと】
            ' 「成功して0件」と区別が付かず、DBが一瞬詰まっただけで
            ' 「記事が1つもありません」と出てしまう（過去ログで踏んだのと同じ罠）
            ErrorReport.Report(ex, "ArticleManagePage.BindArticles")

            ErrorText.Text = HttpUtility.HtmlEncode(
                "ただいま混み合っています。少し待ってから、もう一度お試しください。")

            ArticleList.Visible = False
            EmptyText.Text = ""
            Return

        End Try

        ArticleList.Visible = (articles.Count > 0)
        ArticleList.DataSource = articles
        ArticleList.DataBind()

        If articles.Count = 0 Then

            EmptyText.Text = HttpUtility.HtmlEncode(
                If(SelectedCategoryId().HasValue,
                   "このカテゴリの記事は、まだありません。",
                   "記事は、まだありません。"))

        Else
            EmptyText.Text = ""
        End If

        LimitText.Text = HttpUtility.HtmlEncode(BuildLimitText(canWrite))

    End Sub


    ' 件数と画像の量の行（「いま」を消し、画像の量を同じ行に足した）
    ' 例「13 件 / 上限 200 件　画像 12/50MB」「13 件（上限なし）　画像 3MB（上限なし）」。組み立ては Articles/ArticleUsage.vb
    '
    ' 【上限が読めないときも、そう書く】
    ' RoleLimits はフェイルクローズなので、読めないと書けないロールに見える。
    ' 黙って0と出すと「上限を1件に設定した覚えは無いのに」と探すことになるので、
    ' 書けない理由のほうを出す（読めなかったこと自体は、ErrorReport で開発者に届いている）
    Private Function BuildLimitText(canWrite As Boolean) As String

        If Not canWrite Then
            Return "このアカウントでは、記事を書けません。"
        End If

        Return ArticleUsage.LimitLine(CurrentAccount)

    End Function


    ' いま選ばれているカテゴリ（「すべて」なら Nothing）
    Private Function SelectedCategoryId() As Integer?

        Dim raw As String = CategoryFilter.SelectedValue
        Dim value As Integer

        If String.IsNullOrEmpty(raw) OrElse raw = AllCategories Then
            Return Nothing
        End If

        If Not Integer.TryParse(raw, Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, value) Then
            Return Nothing
        End If

        Return value

    End Function


    ' 削除のあとのお知らせ（合図 → 文言）
    '
    ' 【"saved" と "saved-cleaned" を、ここから外した】
    ' 保存しても一覧へ戻らなくなり、保存の知らせは書く画面の保存の帯に出るようになった
    ' （Articles/Edit.aspx.vb の ShowDoneNotice）。「使えない書き方を取り除いた」も、
    ' その場で本文を確かめられる書く画面のほうへ移した。
    ' **来なくなった合図をここに残さない**——次に読む人が「まだ一覧にも出る」と取り違えるため
    Private Sub ShowNotice(signal As String)

        Select Case signal

            Case "deleted"
                DoneText.Text = HttpUtility.HtmlEncode("記事を削除しました。")

            Case "trashed"
                DoneText.Text = HttpUtility.HtmlEncode("記事をゴミ箱へ移しました。" & ArticleStore.TrashKeepDays & " 日のうちなら、ゴミ箱から元に戻せます。")

            Case "restored"
                DoneText.Text = HttpUtility.HtmlEncode("記事を元に戻しました。")

            Case "restored-noslug"
                DoneText.Text = HttpUtility.HtmlEncode("記事を元に戻しました。記事のアドレスは、ほかの記事で使われていたので空にしてあります（記事を書く画面で付け直せます）。")

            Case "purged"
                DoneText.Text = HttpUtility.HtmlEncode("記事を完全に削除しました。")

            Case Else
                DoneText.Text = ""

        End Select

    End Sub


    ' ============================================================
    ' マークアップから呼ぶもの
    ' ============================================================
    '
    ' 【ロジックをマークアップに書かない】という決まりに沿って、
    ' 見せ方の組み立ては全部こちらに置いてある

    ' タイトル（空なら「（タイトル未入力）」）。エスケープは出すところ（<%#: %>）がする
    Protected Function TitleText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return ""
        End If

        Return ArticleRules.DisplayTitle(article.Title)

    End Function


    ' タイトルの見た目のクラス（空のタイトルは薄く出す）。決まった2つの文字列しか返らない
    Protected Function TitleCssClass(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article IsNot Nothing AndAlso String.IsNullOrWhiteSpace(article.Title) Then
            Return "admin-title-text admin-title-text--untitled"
        End If

        Return "admin-title-text"

    End Function


    ' 「未反映の編集あり」の印。**利用者の入力は混ざらない**（決まった文字列だけ）
    Protected Function DraftMarkHtml(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing OrElse Not article.HasDraft Then
            Return ""
        End If

        Return "<span class=""admin-draft-mark"">未反映の変更あり</span>"

    End Function


    ' 公開中で、編集中の控えがある記事か（「編集中の内容をプレビュー」を出すか）
    Protected Function ShowDraftPreview(item As Object) As Boolean

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        Return article IsNot Nothing AndAlso article.HasDraft AndAlso
               article.Status = ArticleRules.ArticleStatus.Published

    End Function


    Protected Function DraftPreviewUrl(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return "#"
        End If

        Return ArticleRules.DraftPreviewUrl(CurrentAccount.UserName, article.ArticleId, article.Slug)

    End Function


    ' 狭い画面の2行目（状態・カテゴリ・最初に公開した日・番号があるときだけ並び順と固定）。A-2b-4
    '
    ' 【カテゴリ名は利用者の入力】ここでエスケープする（ほかは決まった文字列と数だけ）
    Protected Function MetaLineHtml(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder()

        ' 【目に見えない項目名】この2行目は見出しの行が無いので、読み上げソフトでは
        ' 「公開 作成日記 」と値だけが並び、どれがカテゴリでどれが日付かが分からなかった。
        ' 目では札の形と並び順で分かるので、項目名は読み上げ用（visually-hidden）にだけ付ける。並び順・固定の札は、もともと文字で名乗っている
        sb.Append("<span class=""visually-hidden"">状態：</span>")
        sb.Append("<span class=""").Append(StatusCssClass(item)).Append(""">")
        sb.Append(HttpUtility.HtmlEncode(StatusText(item))).Append("</span>")

        sb.Append("<span class=""admin-meta-item admin-meta-category""><span class=""visually-hidden"">カテゴリ：</span>").Append(HttpUtility.HtmlEncode(CategoryText(item))).Append("</span>")

        If article.FirstPublishedAt.HasValue Then
            sb.Append("<span class=""admin-meta-item""><span class=""visually-hidden"">最初に公開した日：</span>").Append(HttpUtility.HtmlEncode(DateText(article.FirstPublishedAt.Value))).Append("</span>")
        End If

        If article.SortOrder.HasValue Then
            sb.Append("<span class=""admin-order-tag"" title=""カテゴリの中の順番"">順番 ").Append(article.SortOrder.Value.ToString(Globalization.CultureInfo.InvariantCulture)).Append("</span>")
        End If

        If article.PinOrder.HasValue Then
            sb.Append("<span class=""admin-order-tag"">トップに固定 ").Append(article.PinOrder.Value.ToString(Globalization.CultureInfo.InvariantCulture)).Append("</span>")
        End If

        Return sb.ToString()

    End Function


    Protected Function StatusText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return ""
        End If

        Return ArticleRules.StatusDisplayName(article.Status)

    End Function


    ' 状態ごとの見た目のクラス
    '
    ' 【クラス名だけを返す。文言は返さない】
    ' 属性値に入るので、ここが利用者の入力を混ぜる形になると穴になる。
    ' 決まった3つの文字列しか返らないようにしてある
    Protected Function StatusCssClass(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return "admin-status"
        End If

        Select Case article.Status
            Case ArticleRules.ArticleStatus.Published
                Return "admin-status admin-status--published"
            Case ArticleRules.ArticleStatus.Hidden
                Return "admin-status admin-status--hidden"
            Case Else
                Return "admin-status admin-status--draft"
        End Select

    End Function


    Protected Function CategoryText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing OrElse String.IsNullOrEmpty(article.CategoryName) Then
            Return "（なし）"
        End If

        Return article.CategoryName

    End Function


    ' 並び順・固定の番号（空欄なら「―」）
    Protected Function OrderText(value As Object) As String

        If value Is Nothing OrElse Convert.IsDBNull(value) Then
            Return "―"
        End If

        Return Convert.ToInt32(value).ToString(Globalization.CultureInfo.InvariantCulture)

    End Function


    ' 日付
    '
    ' 【形は ArticleDisplay.DateText に任せる】（日付の書き方を1つにそろえる）
    ' 前はここだけ 2026/09/22 で、記事のページと右の枠は だった。
    ' **時刻の付かない日付は、サイト全体でこの1つの形にそろえてある**
    ' （時刻まで出すもの＝自動保存の「2026/09/27 11:50」は別。あちらは Edit.aspx.vb の Stamp）
    Protected Function DateText(value As Object) As String

        If value Is Nothing OrElse Convert.IsDBNull(value) Then
            Return "―"
        End If

        Return ArticleDisplay.DateText(Convert.ToDateTime(value))

    End Function


    ' 行の操作の読み上げ用の名前（「〇〇を編集」「〇〇を削除」）
    '
    ' 【タイトルは利用者が書いた文字列】ここではエスケープしない。
    ' 出すところ（<%#: %> と、asp:Button の属性の書き出し）がエスケープする
    Protected Function ActionLabel(item As Object, suffix As String) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return suffix.TrimStart("を"c)
        End If

        Return "「" & ArticleRules.DisplayTitle(article.Title) & "」" & suffix

    End Function


    ' 削除の確認の窓の文言
    '
    ' 【タイトルを入れて、どれを消すのかを見せる】一覧から押すので、押し間違いがいちばん起きやすい。
    ' 確認の窓（vbChatConfirm）は文言を textContent で入れるので、タイトルに何が書いてあっても HTML にはならない
    Protected Function DeleteConfirmText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return "この記事をゴミ箱へ移します。よろしいですか？"
        End If

        ' 【ゴミ箱へ移す】すぐには消えないことを、窓の文でも伝える
        Return "「" & ArticleRules.DisplayTitle(article.Title) & "」をゴミ箱へ移します。" &
               ArticleStore.TrashKeepDays & " 日のうちなら元に戻せます。"

    End Function


    ' 編集の画面へのURL
    '
    ' 【番号は組み立てるだけで、外から来た文字列をそのまま繋がない】
    ' Integer に直してから文字列にしているので、URLに余計なものは混ざらない
    Protected Function EditUrl(articleId As Object) As String

        Dim id As Integer = Convert.ToInt32(articleId)

        Return "/account/articles/edit?id=" & id.ToString(Globalization.CultureInfo.InvariantCulture)

    End Function


    ' === 記事のページへのリンク ===
    '
    ' 公開中＝「公開ページを見る」、下書き・非公開＝「プレビュー」（持ち主だけが見られる）。
    ' URL は ArticleRules.PageUrl の1か所で組み立てる（ユーザー名はログインしている本人のもの）
    Protected Function ViewUrl(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article Is Nothing Then
            Return "#"
        End If

        Return ArticleRules.PageUrl(CurrentAccount.UserName, article.ArticleId, article.Slug)

    End Function


    Protected Function ViewText(item As Object) As String
        Return ArticleViewText(item)
    End Function


    ' 狭い画面の短い形。公開中＝「見る」、下書き・非公開＝「プレビュー」
    Protected Function ViewShortText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article IsNot Nothing AndAlso article.Status = ArticleRules.ArticleStatus.Published Then
            Return "見る"
        End If

        Return "プレビュー"

    End Function


    ' 読み上げ用の名前（「「〇〇」の公開ページを見る（新しいタブで開きます）」）
    Protected Function ViewLabel(item As Object) As String
        Return ActionLabel(item, "の" & ArticleViewText(item) & "（新しいタブで開きます）")
    End Function


    Private Shared Function ArticleViewText(item As Object) As String

        Dim article As ArticleStore.ArticleRecord = TryCast(item, ArticleStore.ArticleRecord)

        If article IsNot Nothing AndAlso article.Status = ArticleRules.ArticleStatus.Published Then
            Return "公開ページを見る"
        End If

        Return "プレビュー"

    End Function


    ' ============================================================
    ' ゴミ箱
    ' ============================================================

    Private ReadOnly Property IsTrashView As Boolean
        Get
            Return String.Equals(Request.QueryString("view"), "trash", StringComparison.Ordinal)
        End Get
    End Property

    Protected ReadOnly Property TrashKeepDays As Integer
        Get
            Return ArticleStore.TrashKeepDays
        End Get
    End Property


    ' 「ゴミ箱 (n)」。0件でも出す（どこにゴミ箱があるかを、いつも同じ場所で見せる）
    Private Sub BindTrashLink()

        Dim count As Integer = 0

        Try
            count = ArticleStore.CountTrash(CurrentAccount.AccountId)
        Catch ex As Exception
            ' 数えられなくても入口は出す（数だけ出さない）
            ErrorReport.Report(ex, "ArticleManagePage.BindTrashLink")
            TrashLink.Text = "ゴミ箱"
            Return
        End Try

        TrashLink.Text = "ゴミ箱 (" & count.ToString(Globalization.CultureInfo.InvariantCulture) & ")"
        TrashLink.Attributes("aria-label") = "ゴミ箱（" & count & " 件）"

    End Sub


    Private Sub ShowTrashView()

        ListView.Visible = False
        TrashView.Visible = True
        NewArticleLink.Visible = False
        BindTrash()

    End Sub


    Private Sub BindTrash()

        Dim items As List(Of ArticleStore.TrashItem)

        Try
            items = ArticleStore.GetTrash(CurrentAccount.AccountId)
        Catch ex As Exception

            ' 【失敗を、空のゴミ箱で返さない】「成功して0件」と区別が付かないため
            ErrorReport.Report(ex, "ArticleManagePage.BindTrash")
            ErrorText.Text = HttpUtility.HtmlEncode("ただいま混み合っています。少し待ってから、もう一度お試しください。")
            TrashList.Visible = False
            TrashEmptyText.Text = ""
            Return

        End Try

        TrashList.Visible = (items.Count > 0)
        TrashList.DataSource = items
        TrashList.DataBind()

        TrashEmptyText.Text = If(items.Count = 0, HttpUtility.HtmlEncode("ゴミ箱は空です。"), "")

    End Sub


    ' 断った記事の番号（その行のすぐ上に理由を出す）
    Private _trashErrorArticleId As Integer

    Protected Function TrashErrorHereAttribute(item As Object) As String

        Dim record As ArticleStore.TrashItem = TryCast(item, ArticleStore.TrashItem)

        If record IsNot Nothing AndAlso _trashErrorArticleId > 0 AndAlso record.ArticleId = _trashErrorArticleId Then
            Return " data-error-here=""1"""
        End If

        Return ""

    End Function


    ' === ゴミ箱の「元に戻す」「今すぐ完全に削除」 ===
    ' 【番号は CommandArgument から読むが、それを信じて動かすのではない】ArticleStore が AccountId でも絞る
    Protected Sub TrashList_ItemCommand(ByVal source As Object, ByVal e As RepeaterCommandEventArgs) _
        Handles TrashList.ItemCommand

        If e.CommandName <> "RestoreArticle" AndAlso e.CommandName <> "PurgeArticle" Then
            Return
        End If

        ErrorText.Text = ""
        DoneText.Text = ""

        Dim articleId As Integer

        If Not Integer.TryParse(Convert.ToString(e.CommandArgument), Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, articleId) OrElse articleId <= 0 Then
            Return
        End If

        Dim reason As String = Nothing
        Dim done As String

        Try

            If e.CommandName = "RestoreArticle" Then

                Dim result As ArticleStore.RestoreResult =
                    ArticleStore.Restore(CurrentAccount.AccountId, CurrentAccount.RoleKey, articleId)

                reason = result.Reason
                done = If(result.SlugCleared, "restored-noslug", "restored")

            Else

                reason = ArticleStore.PurgeFromTrash(CurrentAccount.AccountId, articleId)
                done = "purged"

            End If

        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleManagePage.Trash." & e.CommandName)
            reason = "ただいま混み合っています。少し待ってから、もう一度お試しください。"
            done = ""

        End Try

        If reason IsNot Nothing Then
            ' 【断ったときは開き直さない】その場で、その行のすぐ上に理由を出す
            ErrorText.Text = HttpUtility.HtmlEncode(reason)
            _trashErrorArticleId = articleId
            BindTrash()
            Return
        End If

        ' 【開き直す（PRG）】戻したら一覧へ（戻した記事が見える）。完全に消したらゴミ箱のまま
        Dim url As String = If(done = "purged",
                               "/account/articles?view=trash&done=purged",
                               "/account/articles?done=" & done)

        ' Try/Catch の外。Redirect は ThreadAbortException を投げる
        Response.Redirect(url)

    End Sub


    Protected Function TrashTitle(item As Object) As String
        Dim record As ArticleStore.TrashItem = TryCast(item, ArticleStore.TrashItem)
        Return If(record Is Nothing, "", ArticleRules.DisplayTitle(record.Title))
    End Function

    Protected Function DaysLeftText(item As Object) As String

        Dim record As ArticleStore.TrashItem = TryCast(item, ArticleStore.TrashItem)

        If record Is Nothing Then
            Return ""
        End If

        If record.DaysLeft <= 0 Then
            Return "次の片付けで消えます"
        End If

        Return "あと " & record.DaysLeft.ToString(Globalization.CultureInfo.InvariantCulture) & " 日で消えます"

    End Function

    Protected Function TrashActionLabel(item As Object, suffix As String) As String
        Return "「" & TrashTitle(item) & "」" & suffix
    End Function

    ' 名前は vbChatConfirm が textContent で入れるので、ここでエスケープしない（属性の中のエスケープは Button が描くときにする）
    Protected Function PurgeConfirmText(item As Object) As String
        Return "「" & TrashTitle(item) & "」を完全に削除します。元に戻せません。よろしいですか？"
    End Function

End Class
