' 記事とカテゴリの「決まり」
'
' 【何を置く場所か】
' 状態の3択・カテゴリの並べ方の3択・タイトルや名前の長さ・並べ替えの規則を、ここ1か所に集めてある。
' Chat/ChatRoomRules.vb（部屋名の検査・定員の白リスト）と同じ立ち位置である。
'
' 【SiteSettings には入れない】
' ここにある値は、管理画面から変えられるようにしたい「設定」ではなく、
' 画面・DB・SQL の3つが噛み合っている「決まり」である。
' 状態の3択を DB の値で増やせるようにしても、その状態を画面もSQLも知らない。
' （部屋の定員の白リスト AllowedMessageCounts を SiteSettings に入れなかったのと、同じ理由）
Public NotInheritable Class ArticleRules

    Private Sub New()
    End Sub


    ' ============================================================
    ' 記事の状態（Articles.Status）
    ' ============================================================
    '
    ' 【真偽値2つの組み合わせにしない】
    ' 「書きかけ」と「引っ込めた」は別物なので、3つの状態として1つの列で持つ。
    ' 真偽値2つにすると、両方立った行・両方とも寝た行という、意味の無い組み合わせが作れてしまう。
    '
    ' 【10刻み】あとから間に差し込めるように（ChatMessageType と同じ決まり）
    Public Enum ArticleStatus

        ' 下書き（自分だけ見られる）
        Draft = 10

        ' 公開（誰でも見られる）
        Published = 20

        ' 非公開（公開したものを、消さずに引っ込める）
        Hidden = 30

    End Enum


    ' ============================================================
    ' カテゴリの並べ方（ArticleCategories.SortMode）
    ' ============================================================
    Public Enum CategorySortMode

        ' 指定した順（Articles.SortOrder。番号の無い記事は、番号付きのあとに日付順）
        Specified = 10

        ' 新しい順（最初に公開した日の降順）
        Newest = 20

        ' 古い順（同じく昇順）
        Oldest = 30

    End Enum


    ' ============================================================
    ' 記事の目次（Articles.TocMode ／ ArticleDrafts.TocMode）
    ' ============================================================
    '
    ' 本文の見出し（H2・H3）から、目次を自動で作って本文の頭に出す。
    '
    ' 【真偽値2つの組み合わせにしない】（状態の3択と、まったく同じ理由）
    ' 「入れるか」と「開いた状態か」を2つ持つと、**「入れない＋開く」という意味の無い組み合わせ**が作れてしまう。
    ' 1つの列の3択なら、その食い違いが構造として起きない。
    '
    ' 【既定は「入れない」】既にある記事も、新しい記事も。
    ' 記事を書いた人が「目次が欲しい」と思ったときに選ぶもので、黙って付くものではない
    '
    ' 【10刻み】あとから間に差し込めるように（ArticleStatus と同じ決まり）
    Public Enum ArticleTocMode

        ' 入れない（既定）
        None = 10

        ' 入れる（開いた状態で）
        Open = 20

        ' 入れる（閉じた状態で）
        Closed = 30

    End Enum


    ' ============================================================
    ' 見出しの番号（Articles.HeadingNumberMode ／ ArticleDrafts.HeadingNumberMode）
    ' ============================================================
    '
    ' 本文の見出しと目次の両方に、同じ通し番号を出す。H2＝「1.」、H3＝「1-1」。
    '
    ' 【目次の3択（ArticleTocMode）と、別の列にしてある】（開発者と決めた）
    ' **目次を入れずに、番号だけ振る記事もありうる**ため。1つの欄に2つの意味を持たせない。
    '
    ' 【片方だけには、ならない】「付ける」にしたら、本文の見出しにも目次にも出る。
    ' 番号は**本文には書き込まず**、表示のときに CSS のカウンターで出す（`Content/Site.css`）ので、
    ' あとから「付けない」に戻しても本文はそのままである
    Public Enum HeadingNumberMode

        ' 付けない（既定）
        None = 10

        ' 付ける
        Numbered = 20

    End Enum


    ' ============================================================
    ' 長さの決まり
    ' ============================================================

    ' タイトル（Articles.Title の列幅と同じ数にしてある）
    '
    ' 【列幅より長い上限を書かないこと】
    ' 列より長いと、その記事だけが保存のときに例外で落ちる。
    ' SiteSettings の NicknameMaxLength と ChatMessages.Nickname(50) で踏んだのと、同じ噛み合わせである
    Public Const TitleMaxLength As Integer = 200

    ' 本文（Articles.BodyHtml は NVARCHAR(MAX) なので、列のほうには上限が無い）
    '
    ' 【なぜ上限を設けるのか】
    ' 上限が無いと、貼り付け1回でDBに何MBでも入る。DBの容量は10GBで、チャットのログと分け合っている。
    '
    ' 【なぜ SiteSettings に入れないのか】
    ' 管理画面から変えたい値ではなく、「壊れ方を防ぐための線」だからである
    ' （AccountPassword.MinLength をコードの定数にしてあるのと同じ判断）。
    ' 【ロールごとに変えたくなったら、RoleLimits へ足すこと】SiteSettings ではない
    '
    ' 【数えるのは、Sanitizer を通したあとの HTML の文字数】
    ' タグも数に入る。人が読む文字数とは違うので、**画面にこの数を「本文の文字数」として出さないこと**
    Public Const BodyMaxLength As Integer = 200000

    ' カテゴリの名前（ArticleCategories.Name の列幅と同じ）
    Public Const CategoryNameMaxLength As Integer = 50

    ' 並び順・固定表示の順に入れてよい数
    '
    ' 【0 から始める】「番号なし」は NULL で表すので、0 を特別扱いしない
    Public Const SortOrderMin As Integer = 0
    Public Const SortOrderMax As Integer = 9999


    ' ============================================================
    ' 画面に出す名前
    ' ============================================================
    '
    ' 【日本語を画面に直接書かないこと】
    ' 一覧にも編集画面にも同じ言葉が出るので、書き写すと片方だけ古くなる

    Public Shared Function StatusDisplayName(status As ArticleStatus) As String

        Select Case status
            Case ArticleStatus.Draft
                Return "下書き"
            Case ArticleStatus.Published
                ' 【「公開中」】（「公開」から変えた）保存の帯の印と、記事一覧の状態の字をそろえる（同じものに名前を2つ付けない）
                Return "公開中"
            Case ArticleStatus.Hidden
                Return "非公開"
        End Select

        ' 【Case Else に任せないこと】
        ' 状態を1つ足したのにここを直し忘れたとき、**黙って「下書き」と出る**のがいちばん困る。
        ' 番号をそのまま出して、直し忘れに気づけるようにしてある
        Return "不明(" & CInt(status) & ")"

    End Function


    ' ============================================================
    ' 記事を書く画面の、保存の帯（選ぶ欄からボタン2つへ作り直した）
    ' ============================================================
    '
    ' 【きっかけ】同じ日の午後に「状態」を選ぶ欄（ドロップダウン）を帯に入れたが、
    ' 指摘：ドロップダウンリストが余計だった。選んだ時点で反映されたのかどうかが、直感で分からない。
    ' 選ぶ欄は「選んだだけでは何も起きず、隣のボタンを押して初めて効く」形で、そこが見ただけでは分からなかった。
    '
    ' 【今の形】帯にあるのは、今の状態の印と、ボタン2つ。**押したことが、そのまま起きること**である。
    '
    '   保存してある状態   印         そのまま保存      状態を変える
    '   下書き（新しい記事も） 下書き     下書き保存        公開する
    '   公開中             公開中     更新する          非公開にする
    '   非公開             非公開     保存              もう一度公開する
    '
    ' 【色の付いたボタンは、いつも「そのまま保存」のほう】状態を変えるほうは、枠だけの控えめな形にして、
    ' 押す前に確かめの窓を出す（並んでいるので、押し間違えても公開されないように）。
    '
    ' 【言葉は、ここにしか無い】前は同じ決まりが Scripts/article-editor.js にもあった（選び直したその場で
    ' ボタンの言葉を変えるため）。選ぶ欄が無くなったので、画面の側の写しは消した

    ' 保存の帯の、今の状態の印（新しい記事は Nothing＝まだ保存していないので、下書き）
    Public Shared Function StatusMarkText(saved As ArticleStatus?) As String

        If Not saved.HasValue Then
            Return StatusDisplayName(ArticleStatus.Draft)
        End If

        If saved.Value = ArticleStatus.Published Then
            Return "公開中"
        End If

        Return StatusDisplayName(saved.Value)

    End Function


    ' 「そのまま保存」のボタンを押したときの状態（＝保存してある状態。新しい記事は下書き）
    Public Shared Function KeepStatus(saved As ArticleStatus?) As ArticleStatus
        Return If(saved.HasValue, saved.Value, ArticleStatus.Draft)
    End Function

    ' 「そのまま保存」のボタンの言葉
    Public Shared Function SaveButtonText(saved As ArticleStatus?) As String

        Select Case KeepStatus(saved)
            Case ArticleStatus.Published
                Return "更新する"
            Case ArticleStatus.Hidden
                Return "保存"
        End Select

        Return "下書き保存"

    End Function


    ' 「状態を変える」ボタンを押したときの状態（公開中なら非公開へ、それ以外は公開へ）
    Public Shared Function SwitchStatus(saved As ArticleStatus?) As ArticleStatus

        If KeepStatus(saved) = ArticleStatus.Published Then
            Return ArticleStatus.Hidden
        End If

        Return ArticleStatus.Published

    End Function

    ' 「状態を変える」ボタンの言葉
    Public Shared Function SwitchButtonText(saved As ArticleStatus?) As String

        Select Case KeepStatus(saved)
            Case ArticleStatus.Published
                Return "非公開にする"
            Case ArticleStatus.Hidden
                Return "もう一度公開する"
        End Select

        Return "公開する"

    End Function

    ' 「状態を変える」ボタンを押したときの、確かめの窓の文（何が起きるかを、ここで1行で言う）
    Public Shared Function SwitchConfirmText(saved As ArticleStatus?) As String

        Select Case KeepStatus(saved)
            Case ArticleStatus.Published
                Return "公開をやめて、自分だけが見られるようにします。よろしいですか？"
            Case ArticleStatus.Hidden
                Return "この記事を、もう一度みんなに公開します。よろしいですか？"
        End Select

        Return "この記事を、みんなに公開します。よろしいですか？"

    End Function

    ' 確かめの窓の「実行する側」のボタンの言葉
    Public Shared Function SwitchConfirmOkText(saved As ArticleStatus?) As String

        If KeepStatus(saved) = ArticleStatus.Published Then
            Return "非公開にする"
        End If

        Return "公開する"

    End Function


    Public Shared Function SortModeDisplayName(mode As CategorySortMode) As String

        Select Case mode
            Case CategorySortMode.Specified
                Return "指定した順"
            Case CategorySortMode.Newest
                Return "新しい順"
            Case CategorySortMode.Oldest
                Return "古い順"
        End Select

        Return "不明(" & CInt(mode) & ")"

    End Function


    ' 画面の選択肢（<asp:DropDownList> に流し込む）
    '
    ' 【マークアップに <asp:ListItem> をベタ書きしないこと】
    ' 選択肢を1つ足したときに、画面とコードの2か所を直すことになる
    ' （部屋の定員の <option> を VB 側で組み立てているのと、同じ決まり）
    Public Shared Function AllStatuses() As ArticleStatus()

        Return New ArticleStatus() {
            ArticleStatus.Draft,
            ArticleStatus.Published,
            ArticleStatus.Hidden}

    End Function


    Public Shared Function AllSortModes() As CategorySortMode()

        Return New CategorySortMode() {
            CategorySortMode.Specified,
            CategorySortMode.Newest,
            CategorySortMode.Oldest}

    End Function


    ' 目次の3択
    '
    ' 【名前で選ばせる】「入れる」と「開く」の2つのスイッチにしないと決めたので、
    ' 画面にもそのまま3つの名前で出す。何が起きるかが、名前だけで分かる
    Public Shared Function TocModeDisplayName(mode As ArticleTocMode) As String

        Select Case mode
            Case ArticleTocMode.None
                Return "入れない"
            Case ArticleTocMode.Open
                Return "入れる（開いた状態で）"
            Case ArticleTocMode.Closed
                Return "入れる（閉じた状態で）"
        End Select

        Return "不明(" & CInt(mode) & ")"

    End Function


    Public Shared Function AllTocModes() As ArticleTocMode()

        Return New ArticleTocMode() {
            ArticleTocMode.None,
            ArticleTocMode.Open,
            ArticleTocMode.Closed}

    End Function


    ' 見出しの番号の2択
    Public Shared Function HeadingNumberDisplayName(mode As HeadingNumberMode) As String

        Select Case mode
            Case HeadingNumberMode.None
                Return "付けない"
            Case HeadingNumberMode.Numbered
                Return "付ける"
        End Select

        Return "不明(" & CInt(mode) & ")"

    End Function


    Public Shared Function AllHeadingNumberModes() As HeadingNumberMode()

        Return New HeadingNumberMode() {
            HeadingNumberMode.None,
            HeadingNumberMode.Numbered}

    End Function


    ' ============================================================
    ' 丸め（画面から来た値を、決まりの中へ落とす）
    ' ============================================================
    '
    ' 【画面の選択肢は、防御ではない】
    ' <select> の中身は開発者ツールから書き換えられる。**受け取った側で必ず丸める**

    Public Shared Function NormalizeStatus(value As Integer) As ArticleStatus

        For Each s As ArticleStatus In AllStatuses()
            If CInt(s) = value Then
                Return s
            End If
        Next

        ' 知らない値は、いちばん安全な側（誰にも見えない）へ落とす
        Return ArticleStatus.Draft

    End Function


    Public Shared Function NormalizeSortMode(value As Integer) As CategorySortMode

        For Each m As CategorySortMode In AllSortModes()
            If CInt(m) = value Then
                Return m
            End If
        Next

        Return CategorySortMode.Specified

    End Function


    ' 【知らない値は「入れない」へ落とす】いちばん何も起きない側
    Public Shared Function NormalizeTocMode(value As Integer) As ArticleTocMode

        For Each m As ArticleTocMode In AllTocModes()
            If CInt(m) = value Then
                Return m
            End If
        Next

        Return ArticleTocMode.None

    End Function


    ' 【知らない値は「付けない」へ落とす】同じく、いちばん何も起きない側
    Public Shared Function NormalizeHeadingNumberMode(value As Integer) As HeadingNumberMode

        For Each m As HeadingNumberMode In AllHeadingNumberModes()
            If CInt(m) = value Then
                Return m
            End If
        Next

        Return HeadingNumberMode.None

    End Function


    ' 並び順・固定表示の順。空欄なら Nothing（＝番号なし）
    '
    ' 【Integer? で返している】
    ' 「番号なし」を 0 や -1 で表すと、本当にその数を指定したい人と区別が付かない。
    ' DB 側も NULL で持っている（Articles.SortOrder / PinOrder）
    Public Shared Function NormalizeOrder(text As String) As Integer?

        ' 【全角・前後の空白も受け取る】（Core/NumberInput.vb＝数字の欄の決まりの1か所）
        ' 数字と認識できなければ Nothing＝この欄の既定（番号なし）
        Dim value As Integer

        If Not NumberInput.TryParseInt(text, value) Then
            Return Nothing
        End If

        If value < SortOrderMin Then
            Return SortOrderMin
        End If

        If value > SortOrderMax Then
            Return SortOrderMax
        End If

        Return value

    End Function


    ' ============================================================
    ' 検査（戻り値は断る理由。受け付けてよければ Nothing）
    ' ============================================================

    Public Shared Function ValidateTitle(title As String) As String

        Dim trimmed As String = If(title, "").Trim()

        If trimmed.Length = 0 Then
            Return "タイトルを入力してください。"
        End If

        If trimmed.Length > TitleMaxLength Then
            Return "タイトルは" & TitleMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' 長さだけを見る（下書き・非公開の保存。空でよい）
    Public Shared Function ValidateTitleLength(title As String) As String

        If If(title, "").Trim().Length > TitleMaxLength Then
            Return "タイトルは" & TitleMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    Public Shared Function ValidateBodyLength(sanitizedHtml As String) As String

        If If(sanitizedHtml, "").Length > BodyMaxLength Then
            Return "本文が長すぎます。短くしてください。"
        End If

        Return Nothing

    End Function


    ' 本文の検査
    '
    ' 【Sanitizer を通したあとの文字列を渡すこと】
    ' 通す前に数えると、取り除かれるはずの長いタグまで数に入って、
    ' 「画面では入らないのに、保存したら入った」という向きの食い違いが出る
    Public Shared Function ValidateBody(sanitizedHtml As String) As String

        Dim body As String = If(sanitizedHtml, "")

        If body.Trim().Length = 0 Then
            Return "本文を入力してください。"
        End If

        If body.Length > BodyMaxLength Then
            Return "本文が長すぎます。短くしてください。"
        End If

        Return Nothing

    End Function


    Public Shared Function ValidateCategoryName(name As String) As String

        Dim trimmed As String = If(name, "").Trim()

        If trimmed.Length = 0 Then
            Return "カテゴリの名前を入力してください。"
        End If

        If trimmed.Length > CategoryNameMaxLength Then
            Return "カテゴリの名前は" & CategoryNameMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' ============================================================
    ' 並べ替えの規則（SQL の ORDER BY）
    ' ============================================================
    '
    ' 【ここ1か所だけが、3つの並べ方を知っている】
    ' 管理画面の一覧（Articles/Manage.aspx）でカテゴリを選んだときと、
    ' 作るカテゴリのページの両方が、これを使う。
    ' 2か所に書くと、片方だけ直したときに「管理画面で見た順番と、公開の順番が違う」ことになる。
    '
    ' 【文字列を組み立てているが、利用者の入力は1文字も混ざらない】
    ' 戻すのは、この関数の中に書いてある決まった文字列のどれかだけである
    ' （並べ方は上の NormalizeSortMode を通った3択で、それ以外は来ない）
    Public Shared Function OrderByClause(mode As CategorySortMode) As String

        Select Case mode

            Case CategorySortMode.Newest

                ' 最初に公開した日の降順。まだ公開していない記事（NULL）は最後
                Return " ORDER BY CASE WHEN a.FirstPublishedAt IS NULL THEN 1 ELSE 0 END, " &
                       "          a.FirstPublishedAt DESC, a.ArticleId DESC"

            Case CategorySortMode.Oldest

                Return " ORDER BY CASE WHEN a.FirstPublishedAt IS NULL THEN 1 ELSE 0 END, " &
                       "          a.FirstPublishedAt ASC, a.ArticleId ASC"

            Case Else

                ' 指定した順。
                ' 【番号の無い記事は、番号付きのあとに日付順】
                ' 日付順の向きは「新しい順」にそろえてある
                Return " ORDER BY CASE WHEN a.SortOrder IS NULL THEN 1 ELSE 0 END, " &
                       "          a.SortOrder ASC, " &
                       "          CASE WHEN a.FirstPublishedAt IS NULL THEN 1 ELSE 0 END, " &
                       "          a.FirstPublishedAt DESC, a.ArticleId DESC"

        End Select

    End Function


    ' 記事のページの「前の記事／次の記事」を決める並び（ArticleStore.FindNeighbors）
    '   ・指定した順 … カテゴリの一覧と同じ（OrderByClause）。前＝ひとつ上
    '   ・新しい順／古い順 … どちらも古い順（前＝ひとつ古い／次＝ひとつ新しい。日本のブログの決まり）
    ' 【決まった文字列だけを返す】SQL にそのまま入る
    Public Shared Function ReadingOrderClause(mode As CategorySortMode) As String

        If mode = CategorySortMode.Specified Then
            Return OrderByClause(CategorySortMode.Specified)
        End If

        Return OrderByClause(CategorySortMode.Oldest)

    End Function


    ' 記事のページの URL（/articles/{user}/{id} か /articles/{user}/{名前}）。名前は A-3b
    '
    ' 【ここ1か所で組み立てる】記事のページ（正しい URL への転送）・記事の一覧・編集画面・トップ・カテゴリの一覧・
    ' 「サイトの記事」の控え（SiteArticles）が使う。
    ' 【名前（Slug）が付いていれば、名前の URL が正しい URL】番号で来たら、記事のページが名前の URL へ転送する。
    ' 【ユーザー名はそのまま繋いでよい】半角の英数字とハイフン・アンダースコアだけ（AccountStore.ValidateUserName）で、
    ' URL で意味を持つ文字は入らない。それでも念のため UrlPathEncode を通す（決まりが緩んだ日に、URL が壊れないように）
    Public Shared Function PageUrl(userName As String, articleId As Integer, slug As String) As String

        Dim key As String

        If String.IsNullOrEmpty(slug) Then
            key = articleId.ToString(Globalization.CultureInfo.InvariantCulture)
        Else
            key = HttpUtility.UrlPathEncode(slug)
        End If

        Return AccountUrl(userName) & "/" & key

    End Function


    ' アカウントのトップページの URL（/articles/{user}）。A-3b
    Public Shared Function AccountUrl(userName As String) As String
        Return "/articles/" & HttpUtility.UrlPathEncode(If(userName, ""))
    End Function


    ' カテゴリの一覧の URL（/articles/{user}/category/{番号}）。A-3b
    '
    ' 【「category」は記事の名前に使えない】（ReservedSlugs）ので、記事の URL とは重ならない
    ' （記事は2区切り目で終わる。こちらは3区切り目に番号がある＝ルートも別）
    Public Shared Function CategoryUrl(userName As String, categoryId As Integer) As String
        Return AccountUrl(userName) & "/category/" & categoryId.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function


    ' 公開中の記事の「編集中の控え」のプレビュー
    '
    ' 記事のページの URL に ?draft=1 を付けたもの。**控えを見られるのは持ち主だけ**で、
    ' ほかの人には、この合図は無いものとして公開中の記事がそのまま出る（Articles/ArticleDetail.aspx.vb）
    Public Const DraftPreviewQuery As String = "draft=1"

    Public Shared Function DraftPreviewUrl(userName As String, articleId As Integer, slug As String) As String
        Return PageUrl(userName, articleId, slug) & "?" & DraftPreviewQuery
    End Function


    ' 画面に出すタイトル（空なら「（タイトル未入力）」）。A-2b-4
    '
    ' 【自動保存では、タイトルが空の下書きができる】管理画面の一覧・プレビューで、空の行や空の見出しにしないため。
    ' 公開の記事は、手で押す保存でタイトルが必須なので、ふつうは空にならない。
    ' 【エスケープはしない】出すところ（<%: %> など）がする
    Public Const UntitledText As String = "（タイトル未入力）"

    Public Shared Function DisplayTitle(title As String) As String

        If String.IsNullOrWhiteSpace(title) Then
            Return UntitledText
        End If

        Return title

    End Function


    ' ============================================================
    ' 記事の「URL の名前」（Articles.Slug）。A-3b
    ' ============================================================
    '
    ' /articles/kanrinin/guide のように、番号ではなく英字の名前で記事を引くためのもの。
    ' メニューの「使い方」などの行き先は、コードにこの名前を書いておき、記事の側に名前を付ければつながる
    ' （番号は記事を書くまで決まらないので、番号をコードに書くと、書き直すたびに発行が要る）。
    '
    ' 【英小文字で始める決まりで、番号と見分けている】記事のページは、URL の最後の区切りが
    ' 数字だけなら番号、英小文字で始まれば名前として引く（Articles/ArticleDetail.aspx.vb）。
    ' 名前が数字で始められれば「123」が番号なのか名前なのか分からなくなる。
    '
    ' 【小文字だけ】URL の大文字小文字違いで別の記事にならないように。大文字で来た URL は、記事のページが小文字へ転送する

    Public Const SlugMaxLength As Integer = 40

    Private Shared ReadOnly SlugPattern As New Text.RegularExpressions.Regex(
        "^[a-z][a-z0-9-]*$", Text.RegularExpressions.RegexOptions.Compiled)

    ' 使えない名前。この先 URL の区切りに使うかもしれない言葉（いま使っているのは category だけ）
    Private Shared ReadOnly ReservedSlugs As New HashSet(Of String)(
        {"category", "categories", "page", "feed", "tag"}, StringComparer.Ordinal)


    ' 画面から来た名前をそろえる（前後の空白を落とし、小文字にする）。空なら Nothing（＝名前を付けない）
    Public Shared Function NormalizeSlug(text As String) As String

        Dim trimmed As String = If(text, "").Trim().ToLowerInvariant()

        If trimmed.Length = 0 Then
            Return Nothing
        End If

        Return trimmed

    End Function


    ' 名前の検査（NormalizeSlug のあとの値で）。戻り値は断る理由（よければ Nothing。Nothing＝名前なしも、よい）
    Public Shared Function ValidateSlug(slug As String) As String

        If slug Is Nothing Then
            Return Nothing
        End If

        If slug.Length > SlugMaxLength Then
            Return "記事のアドレスは" & SlugMaxLength & "文字以内にしてください。"
        End If

        If Not SlugPattern.IsMatch(slug) Then
            Return "記事のアドレスは、英小文字で始めて、英小文字・数字・ハイフン（-）だけで入力してください（例 guide）。"
        End If

        If ReservedSlugs.Contains(slug) Then
            Return "「" & slug & "」は記事のアドレスに使えません。別の名前にしてください。"
        End If

        Return Nothing

    End Function


    ' URL の最後の区切りが、記事の名前の形か（大文字でもよい＝小文字へ転送するため）
    Public Shared Function LooksLikeSlug(text As String) As Boolean

        If String.IsNullOrEmpty(text) OrElse text.Length > SlugMaxLength Then
            Return False
        End If

        Return SlugPattern.IsMatch(text.ToLowerInvariant())

    End Function

End Class
