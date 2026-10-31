Imports System.Text.RegularExpressions

' 記事のページ（/articles/{user}/{id}）。A-3a
'
' 【誰に見せるか】
'   ・公開 … 誰でも
'   ・下書き・非公開 … **その記事の持ち主がログインしているときだけ**「プレビュー」として（上に帯）。
'                       ほかの人（ログインしていない人を含む）には404
'
' 【404 は「無い」と「在るけど見せない」を区別させない】
' 同じ画面・同じステータスで返す。分けると「その番号の記事が在るか」を外から確かめられる
' （部屋の番号・こっそりの画像と同じ考え方）。
'
' 【{user} の大文字小文字違い・番号の書き方違い（01）は、正しい URL へ転送する】
' ユーザー名は大文字も使えるが、DB の照合順序が大文字小文字を区別しないので、違っていても指す人は1人に決まる。
' 転送先を1つにしておけば、検索エンジンに同じ記事が2つの URL で載らない。
' ただし**見せない記事は、転送せずに404**（転送すると、その記事が在ることが分かってしまう）。
' 決めた理由は 「記事の追加・編集機能」の章の「設計」。
'
' 【このページは AccountPageBase を継承しない】ログインしていない人にも見せるページなので。
' ログインしているかは AccountSession.Current で見るだけ（ログインの画面へは飛ばさない）
Public Class ArticleDetail
    Inherits System.Web.UI.Page

    ' 番号として受け付ける形（1〜9桁の数字だけ）。これ以外（1a・-1・空）は404
    Private Shared ReadOnly IdPattern As New Regex("^[0-9]{1,9}$", RegexOptions.Compiled)


    ' === マークアップ（ArticleDetail.aspx）から参照する値 ===
    '
    ' 【タイトル・表示名・カテゴリ名は、マークアップ側の <%: %> がエスケープする】
    ' ここでは素のまま持つ。**エスケープ済みの値を返すのは PublishedDateHtml と BodyHtml だけ**（名前に Html を付けてある）

    Protected Property ArticleTitle As String = ""
    Protected Property AuthorName As String = ""
    Protected Property CategoryName As String = ""
    Protected Property PreviewText As String = ""
    Protected Property AuthorUrl As String = ""

    ' 前後の記事と「一覧」（ブログ型の④）
    '
    ' 【前＝ひとつ古い／次＝ひとつ新しい】日本のブログの決まりどおり。
    ' **ブログのトップのページ送りとは向きが逆**（あちらは「次のページ」で過去へ進む）
    Protected Property PrevUrl As String = ""
    Protected Property PrevTitle As String = ""
    Protected Property NextUrl As String = ""
    Protected Property NextTitle As String = ""

    ' 「一覧」の行き先（そのブログのトップ）
    '
    ' 【「書いた人」（AuthorUrl）とは別】あちらは②で公開のプロフィールへ変えたので、
    ' ブログのトップへ戻る道が記事のページから無くなっていた。この回で戻した
    Protected Property BlogUrl As String = ""
    Protected Property CategoryUrl As String = ""
    Protected Property EditUrl As String = ""

    ' 公開した日・更新した日（<time> 要素まで組み立てた HTML。中に利用者の入力は混ざらない）
    Protected Property PublishedDateHtml As String = ""
    Protected Property UpdatedDateHtml As String = ""

    ' 本文（HtmlSanitizer を通したもの）。**ここ以外に、通していない値を入れないこと**
    Protected Property BodyHtml As String = ""

    ' 本文の箱に付ける class
    '
    ' 【見出しの番号を出す記事だけ、印が1つ増える】番号は CSS のカウンターで出すので、
    ' 「この記事は番号を出す」という印が CSS に要る。**中身は決まった2つの文字列だけ**（利用者の入力は混ざらない）
    Protected ReadOnly Property BodyCssClass As String
        Get
            If _numbering = ArticleRules.HeadingNumberMode.Numbered Then
                Return "article-body article-numbered"
            End If

            Return "article-body"
        End Get
    End Property

    Private _numbering As ArticleRules.HeadingNumberMode = ArticleRules.HeadingNumberMode.None


    ' 静的ファイルの URL（?v= 付き）。マークアップから <%: Asset(...) %> で呼ぶ
    '
    ' 【マークアップから common.AssetUrl を直接呼ばないこと】
    ' マークアップのインラインコードは、コードビハインドとは別のコンパイル単位で、
    ' ビルドは通るのに実行すると500になることがある。
    ' Site.Master.vb の Asset と同じ形
    Protected Function Asset(ByVal virtualPath As String) As String
        Return common.AssetUrl(virtualPath)
    End Function


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        Dim rawUser As String = TryCast(RouteData.Values("user"), String)
        Dim rawId As String = TryCast(RouteData.Values("id"), String)

        ' === 形の検査（DB を引く前に落とせるものは落とす） ===
        '
        ' 【最後の区切りは、番号か「URL の名前」】数字だけなら番号、英小文字（大文字でもよい）で始まれば名前。
        ' 名前は数字で始められない決まり（ArticleRules.ValidateSlug）なので、取り違えは起きない
        If String.IsNullOrEmpty(rawUser) OrElse AccountStore.ValidateUserName(rawUser) IsNot Nothing OrElse
           rawUser <> rawUser.Trim() OrElse String.IsNullOrEmpty(rawId) Then

            ShowNotFound()
            Return

        End If

        Dim byId As Boolean = IdPattern.IsMatch(rawId)
        Dim articleId As Integer = 0

        If byId Then

            articleId = Integer.Parse(rawId, Globalization.CultureInfo.InvariantCulture)

            If articleId <= 0 Then
                ShowNotFound()
                Return
            End If

        ElseIf Not ArticleRules.LooksLikeSlug(rawId) Then

            ShowNotFound()
            Return

        End If

        ' === 引く ===
        Dim article As ArticleStore.ArticlePageRecord

        Try
            If byId Then
                article = ArticleStore.FindForPage(rawUser, articleId)
            Else
                article = ArticleStore.FindForPageBySlug(rawUser, rawId.ToLowerInvariant())
            End If
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleDetail.Page_Load")

            ShowTrouble()
            Return

        End Try

        If article Is Nothing Then
            ShowNotFound()
            Return
        End If

        ' === 見せてよいか ===
        Dim isPublished As Boolean = (article.Status = ArticleRules.ArticleStatus.Published)

        Dim viewer As AccountStore.AccountRecord = AccountSession.Current(Context)
        Dim isOwner As Boolean = (viewer IsNot Nothing AndAlso viewer.AccountId = article.AccountId)

        If Not isPublished AndAlso Not isOwner Then

            ' 【転送の判定より先に置くこと】見せない記事を正しい URL へ転送すると、在ることが分かってしまう
            ShowNotFound()
            Return

        End If

        ' 【編集中の控えのプレビュー（?draft=1）を見られるのは、持ち主だけ】
        ' ほかの人には、この合図は無いものとして扱う（公開中の記事がそのまま出る。控えが在るかどうかは分からない）
        Dim wantsDraft As Boolean =
            isOwner AndAlso String.Equals(Request.QueryString("draft"), "1", StringComparison.Ordinal)

        ' === 正しい URL でなければ、転送する ===
        '
        ' 【この Redirect を Try/Catch の中へ移さないこと】
        ' 既定（endResponse = True）は ThreadAbortException を投げるため、Catch が走って嘘のエラー記録が出る
        '
        ' 【名前の付いた記事は、名前の URL が正しい URL】番号で来たら名前の URL へ、大文字の名前は小文字へ
        Dim canonical As String = ArticleRules.PageUrl(article.UserName, article.ArticleId, article.Slug)

        If wantsDraft Then
            ' 控えのプレビューは、転送しても控えのプレビューのまま
            canonical = ArticleRules.DraftPreviewUrl(article.UserName, article.ArticleId, article.Slug)
        End If

        Dim canonicalKey As String =
            If(String.IsNullOrEmpty(article.Slug),
               article.ArticleId.ToString(Globalization.CultureInfo.InvariantCulture),
               article.Slug)

        If Not String.Equals(rawUser, article.UserName, StringComparison.Ordinal) OrElse
           Not String.Equals(rawId, canonicalKey, StringComparison.Ordinal) Then

            If isPublished AndAlso Not wantsDraft Then
                ' 公開の記事は 301（恒久的）。検索エンジンにも「こちらが正しい URL」と伝わる
                Response.RedirectPermanent(canonical)
            Else
                ' プレビューは 302（ブラウザに覚えさせない）
                SetNoStore()
                Response.Redirect(canonical)
            End If

        End If

        ' === 編集中の控えを引く（?draft=1 のときだけ） ===
        Dim draft As ArticleStore.DraftRecord = Nothing

        If wantsDraft Then

            Dim draftCategoryName As String = Nothing

            Try

                draft = ArticleStore.FindDraft(article.AccountId, article.ArticleId)

                If draft IsNot Nothing AndAlso draft.CategoryId.HasValue Then

                    Dim category As ArticleCategoryStore.CategoryRecord =
                        ArticleCategoryStore.Find(article.AccountId, draft.CategoryId.Value)

                    If category IsNot Nothing Then
                        draftCategoryName = category.Name
                    End If

                End If

            Catch ex As Exception

                ErrorReport.Report(ex, "ArticleDetail.Page_Load（編集中の控え）")

                ShowTrouble()
                Return

            End Try

            If draft Is Nothing Then

                ' 【控えが無い（反映した・捨てた）なら、ふつうのページへ】302（覚えさせない）。
                ' 古いタブのリンクから来たときに、404 や空のページにしない
                SetNoStore()
                Response.Redirect(ArticleRules.PageUrl(article.UserName, article.ArticleId, article.Slug))

            End If

            ' 【見せるのは控えの中身。書いた人・最初に公開した日は本体のまま】
            article.Title = draft.Title
            article.BodyHtml = draft.BodyHtml

            ' 【目次の3択も控えの側で】打っている途中で選び直したものが、
            ' 「編集中の内容をプレビュー」でそのまま見られるように
            article.TocMode = draft.TocMode
            article.HeadingNumberMode = draft.HeadingNumberMode
            article.CategoryId = If(draftCategoryName Is Nothing, CType(Nothing, Integer?), draft.CategoryId)
            article.CategoryName = draftCategoryName

        End If

        ' === 見せる ===
        If wantsDraft Then

            SetNoStore()
            Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")

            PreviewBanner.Visible = True
            PreviewText = "未反映の変更です。まだ公開ページには反映していません（" &
                          DraftStamp(draft.SavedAt) & " に自動保存した内容）。"
            EditUrl = "/account/articles/edit?id=" & article.ArticleId.ToString(Globalization.CultureInfo.InvariantCulture)

        ElseIf Not isPublished Then

            ' 【プレビュー】検索エンジンに載せない・どこにも控えさせない。
            ' 持ち主以外は404になるので、検索エンジン（ログインしていない）がここへ来ることはないが、念のため2重に。
            ' <meta name="robots"> は <head> に書く必要があり、Site.Master の <head> にはコードブロックがあるので
            ' 実行時に足せない（【発行後チェック】の「<meta> を Page.Header.Controls.Add で足す形は、使えない」）。
            ' 同じ働きをする応答のヘッダー（X-Robots-Tag）で付けている
            SetNoStore()
            Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")

            PreviewBanner.Visible = True
            ' 【下書きと非公開で言い方を分ける】（テストで出た直し）
            ' 非公開は一度公開した記事なので、「まだ」が合わない
            If article.Status = ArticleRules.ArticleStatus.Hidden Then
                PreviewText = "この記事は公開されていません（非公開にしています）。表示は、保存した内容です。"
            Else
                PreviewText = "この記事はまだ公開されていません（下書きです）。表示は、保存した内容です。"
            End If
            EditUrl = "/account/articles/edit?id=" & article.ArticleId.ToString(Globalization.CultureInfo.InvariantCulture)

        End If

        ' 【右の枠を、このブログのものに差し替える】（ブログ型の③）
        ' プロフィールの簡易版 → その人の最新の記事 → カテゴリ → お知らせ。
        ' **失敗したら差し替えない**（サイト全体の更新履歴がそのまま出る＝フェイルオープン）。
        ' 【記事の行が持っている3つを渡す】DB をもう1回引かないため（BlogSidebar.Install のコメント）
        BlogSidebar.Install(Master, article.AccountId, article.UserName, article.DisplayName)

        ' 【前後の記事】（ブログ型の④）
        '
        ' 【並び】カテゴリに入っている記事はそのカテゴリの並び順、入っていない記事は公開した順（ArticleStore.FindNeighbors）
        ' 【一覧への道は、いつも出す】前も次も無い記事（1本しか無いブログ）でも要る。
        ' 【下書き・非公開のプレビューでも「一覧」は出す】前後は公開記事の中からしか引かないので、
        ' プレビュー中の記事は「どこにも挟まっていない」状態になる（それで正しい）
        BlogUrl = ArticleRules.AccountUrl(article.UserName)

        Dim older As ArticleStore.Neighbor = Nothing
        Dim newer As ArticleStore.Neighbor = Nothing

        Try
            ArticleStore.FindNeighbors(article.AccountId, article.ArticleId, article.CategoryId, older, newer)
        Catch ex As Exception

            ' 【止めない】前後のリンクが出ないだけで、記事は読める（フェイルオープン）
            ErrorReport.Report(ex, "ArticleDetail.FindNeighbors", "記事 " & article.ArticleId)

        End Try

        If older IsNot Nothing Then
            PrevUrl = ArticleRules.PageUrl(article.UserName, older.ArticleId, older.Slug)
            PrevTitle = ArticleRules.DisplayTitle(older.Title)
            PrevLink.Visible = True
        End If

        If newer IsNot Nothing Then
            NextUrl = ArticleRules.PageUrl(article.UserName, newer.ArticleId, newer.Slug)
            NextTitle = ArticleRules.DisplayTitle(newer.Title)
            NextLink.Visible = True
        End If

        ' 【タイトルが空の下書き】自動保存で作られた下書きは、タイトルが空のことがある（持ち主のプレビューでだけ見える）
        Page.Title = ArticleRules.DisplayTitle(article.Title)

        ArticleTitle = ArticleRules.DisplayTitle(article.Title)
        AuthorName = article.DisplayName
        CategoryName = If(article.CategoryName, "")
        CategoryMeta.Visible = Not String.IsNullOrEmpty(CategoryName)
        ' 【ブログ型の②】「書いた人」の行き先を、
        ' アカウントのトップページ（/articles/{user}）から**公開のプロフィール**へ変えた。
        ' ブログへは、プロフィールのページのブログ名から入れる（よくあるブログのサービスと同じ形）
        AuthorUrl = AccountProfileRules.PublicUrl(article.UserName)

        If article.CategoryId.HasValue Then
            CategoryUrl = ArticleRules.CategoryUrl(article.UserName, article.CategoryId.Value)
        End If
        PublishedDateHtml = ArticleDisplay.DateHtml(article.FirstPublishedAt)

        ' 【更新した日】（決定：最初に公開した日と最後に更新した日の両方を出す）
        ' 右の枠の更新履歴と同じ値（Articles.UpdatedAt）を出す。
        '
        ' 【同じ日なら出さない】公開したその日にしか直していない記事では、同じ日付が2つ並ぶだけになる。
        ' 出さないこと自体が「公開してから直していない」という意味になるので、情報は失われない。
        ' 日だけで見比べている（時刻まで見ると、公開の直後に1文字直しただけで2つ並ぶ）
        UpdatedDateHtml = ArticleDisplay.TimeHtml(article.UpdatedAt, "")
        UpdatedMeta.Visible = Not (article.FirstPublishedAt.HasValue AndAlso
                                   article.FirstPublishedAt.Value.Date = article.UpdatedAt.Date)

        ' 【見出しの番号を出すかは、本文の箱の class で伝える】番号は CSS のカウンターで出す
        _numbering = article.HeadingNumberMode

        ' 【目次を入れる記事のときだけ、目次の道具（JS）を読み込む】
        ' 入れない記事では、右の枠の目次も、スマホの浮かぶボタンも出さない
        TocScript.Visible = (article.TocMode <> ArticleRules.ArticleTocMode.None)

        ' 【表示でも、もう一度サニタイザーを通す】記事の版ごとに1回だけ（Articles/ArticleHtml.vb の SanitizeForDisplay）
        ' 【目次も、ここで入る】目次は本文（BodyHtml）に入っていないので、
        ' 出すときに、その時点の H2・H3 から組み立てる（Articles/ArticleToc.vb）
        If draft IsNot Nothing Then
            BodyHtml = ArticleHtml.SanitizeDraftForDisplay(article.ArticleId, draft.SavedAt,
                                                           article.BodyHtml, article.TocMode, article.HeadingNumberMode)
        Else
            BodyHtml = ArticleHtml.SanitizeForDisplay(article.ArticleId, article.UpdatedAt,
                                                      article.BodyHtml, article.TocMode, article.HeadingNumberMode)
        End If

        ArticleView.Visible = True

    End Sub


    ' === 見つからない（無い・見せない、を同じ形で） ===
    '
    ' 404・TrySkipIisCustomErrors・控えさせない、は Articles/ArticleDisplay.vb の MarkNotFound（3つのページで同じ）
    Private Sub ShowNotFound()

        PageResponse.MarkNotFound(Response)

        Page.Title = "記事が見つかりません"
        NotFoundView.Visible = True

    End Sub


    Private Sub ShowTrouble()

        PageResponse.MarkTrouble(Response)

        Page.Title = "記事を表示できませんでした"
        TroubleView.Visible = True

    End Sub


    Private Sub SetNoStore()
        PageResponse.SetNoStore(Response)
    End Sub


    ' 「9/22 21:45」の形（書式指定を使わず自前で。共用サーバーのカルチャ次第で和暦になるため）
    Private Shared Function DraftStamp(value As DateTime) As String
        Return value.Month & "/" & value.Day & " " & value.Hour.ToString("00") & ":" & value.Minute.ToString("00")
    End Function

End Class
