' ブログのトップ（/articles/{user}）。作り、作り替えた
'
' 【作り替えたこと】
'   ・上に帯（アイコン・ブログ名・説明・投稿数・プロフィールへの入口）
'   ・新しい記事を**タイトル＋出だし**で数本（PerPage 本。本文ごとから変えた）
'   ・固定表示は一覧の先頭に「固定」の印（「おすすめの記事」の枠はやめた）
'   ・ページ送り（?p=2）
'   ・カテゴリの区切りは消した（③で右の枠へ回した。同じものを2か所に出さない）
'
' 【公開の記事だけ】下書き・非公開は、持ち主がログインしていても出さない（一覧はプレビューの入口にしない）。
' 【無い・止めているアカウントは404】記事が1本も無いアカウントは 200 で「まだ公開している記事はありません」。
' 【{user} の大文字小文字違いは、正しい URL へ 301】記事のページと同じ決まり。
' 【AccountPageBase を継承しない】ログインしていない人にも見せるページなので。
'
' 【本文の HTML は、このページには出さない】
' 出だしは ArticleHtml.ExcerptText が文字だけを抜いたもので、画面はそれをエスケープして出す
Public Class ArticleAccountTop
    Inherits System.Web.UI.Page

    ' 1ページに出す本数
    '
    ' 【「タイトル＋出だし」にしたので、本文ごとのときより1本が低い】
    ' 本数は5本のまま（よくあるブログのサービスも5本前後）。**増やすなら、実物を見てから**
    Private Const PerPage As Integer = 5

    ' 出だしの文字数（PC もスマホも同じ。数行ぶん）
    Private Const ExcerptLength As Integer = 100

    ' ページ送りのクエリの名前
    Private Const PageQueryName As String = "p"

    ' ページ番号の上限（これより大きい番号は1ページ目として扱う）
    '
    ' 【要る理由】?p=999999999 のような URL を叩かれると、OFFSET の計算が桁あふれする。
    ' 記事が数十本の世界なので、200ページもあれば足りる
    Private Const MaxPage As Integer = 200


    ' Repeater に渡す1本ぶん（Eval が読むので Public）
    '
    ' 【Html で終わる名前だけが、組み立て済みの HTML】ほかは素の文字で、画面の <%#: %> がエスケープする
    Public Class EntryItem
        Public Property Title As String
        Public Property Url As String
        Public Property DateHtml As String
        Public Property CategoryName As String
        Public Property CategoryUrl As String
        ' 本文から文字だけを抜いた出だし（HTML ではない。画面がエスケープして出す）
        Public Property Excerpt As String
        Public Property IsPinned As Boolean
    End Class


    ' === マークアップから参照する値（Html で終わる名前だけが、組み立て済みの HTML） ===
    Protected Property BlogTitle As String = ""
    Protected Property BlogDescriptionHtml As String = ""
    ' 上の帯のアイコン（Accounts/AccountAvatar.vb が組み立てた HTML。エスケープ済み）
    Protected Property AvatarHtml As String = ""
    Protected Property ArticleCountText As String = "0"
    Protected Property ProfileUrl As String = ""
    Protected Property PageText As String = ""
    Protected Property NewerPageUrl As String = ""
    Protected Property OlderPageUrl As String = ""


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        Dim rawUser As String = TryCast(RouteData.Values("user"), String)

        If String.IsNullOrEmpty(rawUser) OrElse rawUser <> rawUser.Trim() OrElse
           AccountStore.ValidateUserName(rawUser) IsNot Nothing Then

            ShowNotFound()
            Return

        End If

        ' 【Page という名前にしないこと】VB は大文字小文字を区別しないので、
        ' `page` という局所変数は **ページの Page（Page.Title など）を隠す**
        ' （実際に踏んだ。「VB の大文字小文字とフィールドの隠蔽」の落とし穴）
        Dim pageNumber As Integer = ReadPageNumber()

        Dim account As AccountStore.AccountRecord
        Dim profile As AccountProfileStore.ProfileRecord
        Dim body As ArticleStore.TopPage

        Try

            account = AccountStore.FindActiveByUserName(rawUser)

            If account Is Nothing Then
                ShowNotFound()
                Return
            End If

            ' 【大文字小文字違いは、先に転送する】（DB をこれ以上引く前に）
            ' **ページ番号も持っていく**（?p=2 を開いた人が、1ページ目へ戻らないように）
            If Not String.Equals(rawUser, account.UserName, StringComparison.Ordinal) Then
                Response.RedirectPermanent(PageUrlFor(account.UserName, pageNumber))
            End If

            profile = AccountProfileStore.Find(account.AccountId)
            body = ArticleStore.GetPublishedPage(account.AccountId, (pageNumber - 1) * PerPage, PerPage)

        Catch ex As Threading.ThreadAbortException
            ' RedirectPermanent（endResponse = True）が投げる。エラーとして扱わない
            Throw
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleAccountTop.Page_Load")

            PageResponse.MarkTrouble(Response)
            Page.Title = "表示できませんでした"
            TroubleView.Visible = True
            Return

        End Try

        ' === 上の帯 ===
        '
        ' 【ブログ名は、プロフィールで決めていなければ「〇〇の記事」】
        ' 公開のプロフィールページ（②）が、同じ決め方をしている
        BlogTitle = BlogTitleOf(profile, account.DisplayName)
        Page.Title = BlogTitle

        Dim description As String = If(profile Is Nothing, Nothing, profile.BlogDescription)

        If Not String.IsNullOrEmpty(description) Then
            BlogDescriptionHtml = BuildDescriptionHtml(description)
            BandDescription.Visible = True
        End If

        ' 【アイコンは共通の部品】（Accounts/AccountAvatar.vb）
        ' 前は写真の有無で PlaceHolder を2つ出し分けていた（Visible を読んで分けて、丸が2つ出た不具合もあった＝C）。
        ' 部品が1つの HTML を返すので、出し分けそのものが無くなった
        AvatarHtml = AccountAvatar.Html(
            AccountAvatar.Resolve(account.AccountId, account.UserName, account.DisplayName,
                                  If(profile Is Nothing, Nothing, profile.AvatarImageName),
                                  If(profile Is Nothing, Nothing, profile.AvatarLetter),
                                  If(profile Is Nothing, Nothing, profile.AvatarColor)),
            "lg", "blog-band-avatar")

        ArticleCountText = body.Total.ToString(Globalization.CultureInfo.InvariantCulture)
        ProfileUrl = AccountProfileRules.PublicUrl(account.UserName)

        ' === 新しい記事（タイトル＋出だし。固定の記事が先頭） ===
        Dim entries As New List(Of EntryItem)()

        For Each item As ArticleStore.TopItem In body.Items

            entries.Add(New EntryItem With {
                .Title = ArticleRules.DisplayTitle(item.Title),
                .Url = ArticleRules.PageUrl(account.UserName, item.ArticleId, item.Slug),
                .DateHtml = ArticleDisplay.DateHtml(item.FirstPublishedAt),
                .CategoryName = If(item.CategoryName, ""),
                .CategoryUrl = If(item.CategoryId.HasValue,
                                  ArticleRules.CategoryUrl(account.UserName, item.CategoryId.Value), ""),
                .Excerpt = ArticleHtml.ExcerptText(item.ArticleId, item.UpdatedAt, item.BodyHead, ExcerptLength),
                .IsPinned = item.PinOrder.HasValue})

        Next

        ArticleRepeater.DataSource = entries
        ArticleRepeater.DataBind()

        ' === ページ送り ===
        BuildPager(account.UserName, pageNumber, body.Total)

        EmptyView.Visible = (entries.Count = 0)

        ' 【行き過ぎたページ番号は、1ページ目へ返す】（開発機で見つけた）
        ' ?p=2 を記事4本のブログで開くと、**「まだ公開している記事はありません」**と出ていた。
        ' 記事は在るのに「無い」と言うことになるので、嘘である。
        '
        ' 【302（ふつうの転送）にしてある】301（永久）にしないのは、
        ' **あとで記事が増えれば、その番号のページは本当に在る**ようになるため
        ' （301 を返すと、ブラウザがそれを覚えてしまう）。
        ' 【この Redirect を Try/Catch の中へ移さないこと】ThreadAbortException を投げる
        If entries.Count = 0 AndAlso pageNumber > 1 AndAlso body.Total > 0 Then
            Response.Redirect(PageUrlFor(account.UserName, 1))
        End If

        ' 【右の枠を、このブログのものに差し替える】（③）
        ' **プロフィールの行を渡している**＝同じ行を1ページで2回読まないため
        BlogSidebar.Install(Master, account.AccountId, account.UserName, account.DisplayName, profile)

        TopView.Visible = True

    End Sub


    ' ============================================================
    ' 下回り
    ' ============================================================

    ' ブログ名（プロフィールで決めていなければ「〇〇の記事」）
    '
    ' 【公開のプロフィールページ（②）と、同じ決め方にそろえてある】
    ' 片方だけ変えると、プロフィールのリンクの文字とブログのトップの見出しが食い違う
    Private Shared Function BlogTitleOf(profile As AccountProfileStore.ProfileRecord, displayName As String) As String

        ' 【決め方は AccountProfileRules.BlogTitleOf の1か所】（まとめた。ブログの記事一覧の「ブログ：〇〇」も同じ）
        Return AccountProfileRules.BlogTitleOf(If(profile Is Nothing, Nothing, profile.BlogTitle), displayName)

    End Function


    ' ブログの説明（プレーンテキスト）を、改行だけ <br /> にして出す
    '
    ' 【先にエスケープしてから、改行を直す】逆にすると、入れたばかりの <br /> まで文字に化ける
    ' （公開のプロフィールページの自己紹介と、まったく同じ手順）。
    ' 【3つの改行コードを、まず \n へ寄せる】\r\n だけを直すのでは足りない
    Private Shared Function BuildDescriptionHtml(text As String) As String

        Dim flat As String = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)

        Return HttpUtility.HtmlEncode(flat).Replace(vbLf, "<br />")

    End Function


    ' ?p= を読む（1より小さい・数でない・大きすぎる → 1）
    '
    ' 【画面から来た数は、必ず丸める】そのまま OFFSET に渡すと、桁あふれや
    ' 「とんでもない番号のページ」が作れる（記事の状態を丸めるのと、同じ決まり）
    Private Function ReadPageNumber() As Integer

        Dim raw As String = Request.QueryString(PageQueryName)
        Dim value As Integer

        If String.IsNullOrEmpty(raw) OrElse
           Not Integer.TryParse(raw, Globalization.NumberStyles.None,
                                Globalization.CultureInfo.InvariantCulture, value) Then
            Return 1
        End If

        If value < 1 OrElse value > MaxPage Then
            Return 1
        End If

        Return value

    End Function


    ' そのページの URL（1ページ目は ?p= を付けない）
    '
    ' 【1ページ目に ?p=1 を付けない】同じ中身が2つの URL で見えることになる
    ' （検索エンジンには載せない設定だが、URL は1つのほうがよい）
    Private Shared Function PageUrlFor(userName As String, pageNumber As Integer) As String

        Dim url As String = ArticleRules.AccountUrl(userName)

        If pageNumber <= 1 Then
            Return url
        End If

        Return url & "?" & PageQueryName & "=" & pageNumber.ToString(Globalization.CultureInfo.InvariantCulture)

    End Function


    ' ページ送りを組み立てる
    '
    ' 【「前のページ」＝新しいほう／「次のページ」＝古いほう】
    ' トップは新しい順なので、次へ進むと過去へ行く。
    ' **記事のページの「前の記事｜次の記事」とは向きが逆**（あちらは「前＝古い」）。
    ' どちらも日本のブログの決まりどおりなので、そろえないこと
    ' 【引数を page という名前にしないこと】VB は大文字小文字を区別しないので、
    ' ページの Page を隠す（上の Page_Load で実際に踏んだ）
    Private Sub BuildPager(userName As String, pageNumber As Integer, total As Integer)

        Dim lastPage As Integer = Math.Max(1, CInt(Math.Ceiling(total / CDbl(PerPage))))

        ' 【1ページしか無ければ、何も出さない】
        If lastPage <= 1 Then
            Return
        End If

        PageText = pageNumber.ToString(Globalization.CultureInfo.InvariantCulture) & " / " &
                   lastPage.ToString(Globalization.CultureInfo.InvariantCulture) & " ページ"

        If pageNumber > 1 Then
            NewerPageUrl = PageUrlFor(userName, pageNumber - 1)
            PagerNewer.Visible = True
        End If

        If pageNumber < lastPage Then
            OlderPageUrl = PageUrlFor(userName, pageNumber + 1)
            PagerOlder.Visible = True
        End If

        PagerSection.Visible = True

    End Sub


    Private Sub ShowNotFound()

        PageResponse.MarkNotFound(Response)

        Page.Title = "ページが見つかりません"
        NotFoundView.Visible = True

    End Sub

End Class
