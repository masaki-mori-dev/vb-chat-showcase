' 記事を書く・編集する画面
'
' 【URLは /account/articles/edit】新規は番号なし、編集は ?id=12
'
' 【本文の入力は、この回では素の textarea である】
' 見たまま編集（Quill）は 差し替える。
' 差し替えても、**保存のときに HtmlSanitizer を通すことは変わらない**——
' 唯一の砦は Articles/ArticleHtml.vb であって、画面ではない。
'
' 【ポストバックで動かしている】
' 管理画面まわりは Web Forms をあえて使い倒す、という方針どおり
' （「管理画面とブログCMSでは、Web Forms を『あえて』使い倒す」）。
' そのかわり**判断はこのページに書かない**——検査は Articles/ArticleRules.vb、
' 保存は Articles/ArticleStore.vb が持っていて、ここは呼んで画面を組み立てるだけである
Imports System.Web.Services

Public Class ArticleEditPage
    Inherits AccountPageBase

    ' 一覧の URL（削除のあとに戻る先。**保存のあとは、下の書く画面へ移り直す**）
    Private Const ListUrl As String = "/account/articles"

    ' 書く画面の URL
    ' 保存したあと、一覧ではなく「同じ記事の書く画面」へ移り直すために足した（EditUrlFor）
    Private Const EditUrl As String = "/account/articles/edit"

    ' カテゴリを選んでいないときの値
    Private Const NoCategory As String = "0"

    ' DBが読めなかったときの文言（理由は利用者に見せない。中身は ErrorReport で届く）
    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"


    ' 静的ファイルの URL（?v= 付き）。マークアップから <%: Asset(...) %> で呼ぶ
    '
    ' 【マークアップから common.AssetUrl を直接呼ばないこと】
    ' マークアップのインラインコードは、コードビハインドとは別のコンパイル単位で、
    ' ビルドは通るのに実行すると500になることがある。
    ' Site.Master.vb の Asset と同じ形
    Protected Function Asset(ByVal virtualPath As String) As String
        Return common.AssetUrl(virtualPath)
    End Function


    ' HTMLモードで「使える書き方」を見分けるための一覧（JSON）。markup の data-allowed に入る
    '
    ' 【一覧は ArticleHtml が持つ】JS に書き写すと、書式を足すたびに直す場所が増える。
    ' 【<%: %> で属性に入れる】JSON の " は &quot; になり、ブラウザが属性を読むときに元へ戻す
    Protected ReadOnly Property EditorAllowedJson As String
        Get
            Return ArticleHtml.AllowedJson()
        End Get
    End Property

    ' 保存されている本文を包んだもの。markup の data-initial-body に入る
    '
    ' JS はこちらから書く欄へ読み込む（textarea の生の HTML は、JS が動かないときの素の入力欄のため）。
    ' 理由は Core/BodyTransport.vb の Encode
    Protected ReadOnly Property InitialBodyEncoded As String
        Get
            Return BodyTransport.Encode(BodyBox.Text)
        End Get
    End Property

    ' 今ひらいている記事の番号（新しい記事なら 0）。markup の data-article-id に入る（自動保存が使う）
    Protected ReadOnly Property ArticleIdText As String
        Get
            Return EditingArticleId.ToString(Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property

    ' 自動保存の最初の1回で新しい記事を作ったとき、JS がその番号を入れて送ってくる欄の name
    '
    ' 【ViewState の番号（EditingArticleId）は、JS からは書き換えられない】ページの中で閉じているため。
    ' そこで、新しい記事のときだけ、この欄の番号を「編集していた記事」として読む（SaveButton_Click）。
    ' 書き換えられても、保存の口が「自分の記事か」を DB で確かめるので、他人の記事は触れない
    Protected Const AutoSavedIdField As String = "articleAutoSavedId"

    ' 自動保存を止めて描くか（二重送信で断った画面だけ True。Edit.aspx の data-autosave-url を空にする）
    Protected Property AutoSaveStopped As Boolean

    ' 自動保存の口の URL（止めているときは空＝Scripts/article-editor.js は、空なら自動保存をしない）
    Protected ReadOnly Property AutoSaveUrl As String
        Get
            Return If(AutoSaveStopped, "", "/Articles/Edit.aspx/AutoSave")
        End Get
    End Property

    ' 記事の画像の、1枚の上限（バイト）・縮めるときの長辺・画質（0〜1）。markup の data-image-* に入る
    '
    ' 【画面の上限は親切。守りは受け口】（Articles/UploadImage.aspx.vb の MaxImageBytes が、同じ値でもう一度止める）
    Protected ReadOnly Property ImageMaxBytesText As String
        Get
            Return ArticleUploadImage.MaxImageBytes(CurrentAccount.RoleKey).ToString(Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property

    ' 1枚の上限を「3MB」のような形で（画像を貼る小窓の一言。K）
    Protected ReadOnly Property ImageMaxMbText As String
        Get
            Dim bytes As Integer = ArticleUploadImage.MaxImageBytes(CurrentAccount.RoleKey)
            Return (bytes / 1048576.0).ToString("0.#", Globalization.CultureInfo.InvariantCulture) & "MB"
        End Get
    End Property

    Protected ReadOnly Property ImageMaxEdgeText As String
        Get
            Return SiteSettings.ImageMaxEdge.ToString(Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property

    Protected ReadOnly Property ImageQualityText As String
        Get
            Return (SiteSettings.ImageJpegQualityPercent / 100.0).ToString("0.##", Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property

    ' 記事のアドレスの説明の例。「/articles/kanrinin/guide」のように、**その人のユーザー名で**見せる
    ' （開発者の決めた文は「/articles/あなたのID/guide」。自分の ID がそのまま出るほうが、見ただけで分かるため）。
    ' 組み立ては ArticleRules.PageUrl の1か所（記事の公開ページの URL を作るのと同じ道）。markup の <%: %> がエスケープする
    Protected ReadOnly Property SlugExampleUrl As String
        Get
            Return ArticleRules.PageUrl(CurrentAccount.UserName, 0, "guide")
        End Get
    End Property

    ' 包んだ本文の先頭の印。markup の data-encoded-prefix に入る（JS と印を1か所で持つため）
    Protected ReadOnly Property BodyEncodedPrefix As String
        Get
            Return BodyTransport.EncodedPrefix
        End Get
    End Property


    ' 今ひらいている記事の番号（0 なら新規）
    '
    ' 【ViewState に持たせている】
    ' クエリ文字列から毎回読む形にすると、ポストバックのときに ?id= が消えていた場合に
    ' 「編集していたつもりが、新しい記事として増える」ことになる。
    ' ViewState はページの中で閉じていて、書き換えられても
    ' **保存のときに「自分の記事か」をDBで確かめている**ので、番号を打ち替えても他人の記事は触れない
    ' 保存してある状態（新しい記事は -1）。
    ' 保存の帯の「今の状態の印」と、ボタンの言葉（「公開する」か「更新する」か）を決めるのに使う。
    ' 【ViewState に持つ】断られて画面を返したときも、保存してある状態は変わっていないため
    Private Property SavedStatusValue As Integer
        Get
            Dim value As Object = ViewState("SavedStatusValue")
            Return If(value Is Nothing, -1, CInt(value))
        End Get
        Set(value As Integer)
            ViewState("SavedStatusValue") = value
        End Set
    End Property

    Private ReadOnly Property SavedStatus As ArticleRules.ArticleStatus?
        Get
            If SavedStatusValue < 0 Then
                Return Nothing
            End If
            Return CType(SavedStatusValue, ArticleRules.ArticleStatus)
        End Get
    End Property

    ' 保存の帯の「今の状態の印」（markup の <%: %> がエスケープする）。言葉は ArticleRules が持つ
    Protected ReadOnly Property StatusMarkText As String
        Get
            Return ArticleRules.StatusMarkText(SavedStatus)
        End Get
    End Property

    ' 【保存してある状態を、JS へ渡す】自動保存の1行を「下書きを…」「非公開のまま…」と言い分けるため
    ' （Scripts/article-editor.js の autosaveSavedText）。draft／hidden／published のどれか。新しい記事は draft
    Protected ReadOnly Property StatusKey As String
        Get
            If Not SavedStatus.HasValue Then
                Return "draft"
            End If
            Select Case SavedStatus.Value
                Case ArticleRules.ArticleStatus.Published
                    Return "published"
                Case ArticleRules.ArticleStatus.Hidden
                    Return "hidden"
                Case Else
                    Return "draft"
            End Select
        End Get
    End Property

    Protected ReadOnly Property StatusMarkClass As String
        Get
            If SavedStatus.HasValue AndAlso SavedStatus.Value = ArticleRules.ArticleStatus.Published Then
                Return "status-mark--on"
            End If
            Return "status-mark--off"
        End Get
    End Property


    Private Property EditingArticleId As Integer
        Get
            Dim value As Object = ViewState("EditingArticleId")
            Return If(value Is Nothing, 0, CInt(value))
        End Get
        Set(value As Integer)
            ViewState("EditingArticleId") = value
        End Set
    End Property


    ' 開いた記事（新しい記事のときは Nothing のまま）
    '
    ' 【何に使うか】保存できたことの知らせ（SavedMessage）＝保存した時刻と、
    ' 「（公開ページに反映しました）」を添えるかどうかの判断。
    ' 【画面の欄を見ないこと】画面は、まだ保存していない中身も持ちうる。
    ' ここに入れるのは、LoadArticle が DB から読んだ行だけである
    Private _loadedArticle As ArticleStore.ArticleRecord = Nothing


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        ' 【サイト管理者にだけ出していた「?」の1行（howto・terms・trip）は、消した】
        ' 決まりは説明に書かず、外れたら断る理由で伝える

        If IsPostBack Then

            ' 【前の回に付けた「この欄が悪い」の印を消す】（Core/FormErrors.vb。印は ViewState に残る）
            FormErrors.Clear(TitleBox, BodyBox, SlugBox, CategoryList)

            ' 【包んで送られてきた本文を、ここで元に戻す】
            ' Page_Load は、送られてきた値が入ったあと・検査（Validator）と押されたボタンの処理の前に走る。
            ' ここで戻しておけば、サーバー側の検査も保存も、今までどおり「本文＝HTML」として扱える。
            ' 仕組みと理由は Core/BodyTransport.vb
            DecodeBody()

            ' 【この画面の中で作ったカテゴリを、選択肢に入れ直す】
            ' 保存が断られて画面が返ってきたとき、選択肢は ViewState の古い並びのままなので、
            ' 作ったばかりのカテゴリが消えて「（なし）」に戻って見える
            EnsurePostedCategory()

            Return

        End If

        ' === 書いてよいロールか ===
        '
        ' 【画面を出さないだけでは守りにならないが、出さないこともやる】
        ' 断るのは保存のとき（ArticleStore.Save）で、そちらが本命。
        ' ここは「押しても断られるボタン」を見せないための親切である
        If Not RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles) Then

            ' 【この Redirect を Try/Catch の中へ移さないこと】
            ' 既定（endResponse = True）は ThreadAbortException を投げるため、
            ' Catch が走って嘘のエラー記録が出る
            Response.Redirect(ListUrl)

        End If

        BindChoices()

        LoadArticle()

        ' 【保存したあと・編集中の控えを捨てたあとのお知らせ】文言ではなく合図を受け取る（Manage.aspx.vb と同じ決まり）
        '
        ' 【LoadArticle より後で呼ぶこと】保存した時刻と「（公開ページに反映しました）」の判断に、
        ' DB から読んだ行（_loadedArticle）を使うため
        ShowDoneNotice(Request.QueryString("done"))

    End Sub


    ' === 包んで送られてきた本文を戻す ===
    '
    ' 【戻せなかったときは、欄の値を包んだまま残す】
    ' 空にすると、サーバー側の検査が「本文を入力してください」という的外れな理由で断る。
    ' 包んだまま残せば、検査は通り、保存のボタンの処理（SaveButton_Click）が本当の理由を出して断る。
    ' 画面へ戻ったとき、JS（article-editor.js）は包まれた値を見分けて、戻せれば書く欄へ入れ直す
    Private Sub DecodeBody()

        _bodyDecode = BodyTransport.Decode(BodyBox.Text, ArticleRules.BodyMaxLength,
                                          "本文", "保存していません。")

        If _bodyDecode.Ok Then
            BodyBox.Text = _bodyDecode.Html
        End If

    End Sub

    ' 戻した結果（ポストバックのときだけ入る）
    Private _bodyDecode As BodyTransport.DecodeResult


    ' 【断った理由を、2か所に出す】
    ' 画面のいちばん上（ErrorText）と、保存のボタンのそば（SaveStatusText）。
    ' 上だけだと、長い記事を書いて下のボタンを押した人には見えない。
    ' どちらも role="alert" ではなく、ボタンのそばのほうだけを読み上げの知らせ（aria-live）にしてある
    ' （同じ文を2回読ませないため。上の1行は目で見る人のため）
    Private Sub ShowError(message As String, Optional field As String = Nothing)

        Dim encoded As String = HttpUtility.HtmlEncode(message)

        ErrorText.Text = encoded
        SaveStatusText.Text = encoded

        ' 【どの欄の話か分かるときは、その欄の上へ】（基本ルールの⑧⑨）
        ' 印を付けるだけで、理由を移してカーソルを置くのは Scripts/site.js の「9」（ErrorText に data-error-jump）。
        ' 本文の textarea は隠れているので、data-error-anchor で Quill の欄の前を指す（article-editor.js も同じ値を入れる）
        Dim target As WebControl = FieldControl(field)

        If target IsNot Nothing Then
            FormErrors.MarkField(target)
        End If

    End Sub

    ' ArticleStore の「どの欄の話か」（SaveResult.Field）→ 画面の欄
    Private Function FieldControl(field As String) As WebControl

        Select Case field
            Case ArticleStore.FieldTitle
                Return TitleBox
            Case ArticleStore.FieldBody
                Return BodyBox
            Case ArticleStore.FieldSlug
                Return SlugBox
            Case ArticleStore.FieldCategory
                Return CategoryList
        End Select

        Return Nothing

    End Function


    ' === 保存したあと・控えを捨てたあとのお知らせ（合図 → 文言）===
    '
    ' 【保存したあと一覧へ戻さなくなった】
    ' 指摘：管理画面で記事の保存ボタンを押すと、一覧に戻ってしまうのが不便。
    ' 自動保存は本番に反映しないので、反映を確定させたいときに意識してボタンを押すが、
    ' そうすると、まだ書いている途中なのに必ず一覧に戻ってしまう。
    ' そこで、保存したら同じ記事の書く画面へ移り直し、結果は**保存の帯**に出す。
    '
    ' 【出し先を2つに分けている】
    '   ・保存できた        … 保存の帯（SavedStatusText）。押したボタンのすぐそば
    '   ・控えを捨てた      … 画面のいちばん上（DoneText）。画面の中身が入れ替わったことの知らせなので
    Private Sub ShowDoneNotice(signal As String)

        Select Case signal

            Case "saved"
                ShowSaved(SavedMessage())

            ' 【状態を変えるボタンで保存した】何が起きたかを、そのまま言う。
            ' どちらになったかは、DB から読んだ状態で決める（合図には入れていない）
            Case "saved-switched"
                ShowSaved(SwitchedMessage())

            ' 【保存はしたが、使えない書き方を取り除いた】（黙って消さない、という約束事）
            ' 【一覧ではなくこの画面に出すようにした】
            ' その場で本文を確かめられるので、「開いて、内容を確かめてください」と頼む必要が無くなった
            Case "saved-cleaned"
                ShowSaved(SavedMessage() &
                          " 使えない書き方が入っていたので、取り除いてから保存しました。内容を確かめてください。")

            Case "discarded"
                DoneText.Text = HttpUtility.HtmlEncode("未反映の変更を破棄しました。公開中の内容で開いています。")

        End Select

    End Sub


    ' 保存できたことの知らせを、保存の帯に出す
    '
    ' 【帯の要素が無いときは、画面の上（DoneText）へ逃がす】
    ' 発行は、変更したファイルを落とすことがある（【発行後チェック】）。
    ' DLL だけ届いて Edit.aspx が届かなかったとき、SavedStatusText は Nothing になる。
    ' そのまま代入すると、**保存はできているのに、保存のあとの画面が 500 になる**——
    ' いちばん原因の分かりにくい壊れ方である。
    ' 逃がしておけば、知らせが出る場所が変わるだけで済む
    Private Sub ShowSaved(message As String)

        Dim encoded As String = HttpUtility.HtmlEncode(message)

        If SavedStatusText IsNot Nothing Then
            SavedStatusText.Text = encoded
        Else
            DoneText.Text = encoded
        End If

    End Sub


    ' 「保存しました 16:02」（公開中の記事なら「（公開ページに反映しました）」も添える）
    '
    ' 【時刻は、DB の「最後の保存」（UpdatedAt）を出す。画面を描いたときの時計にしない】
    ' ?done=saved はアドレスに残るので、描くたびに「今の時刻」を出すと、
    ' 保存したあとに F5 を押しただけで「たったいま保存した」ように見える時刻が出てしまう。
    ' UpdatedAt なら、何度描き直しても本当に保存した時刻のままで、
    ' 画面のいちばん下の「最後の保存」（BuildMetaText）とも必ず一致する。
    ' 【書式指定を使わない】共用サーバーのカルチャ次第で和暦になるため（下の Stamp と同じ決まり）。
    ' 【_loadedArticle が Nothing のときは、時刻を出さない】?id= の無い ?done=saved を
    ' 誰かが作って開いたときに、落とさないため（ありえない組み合わせだが、断れるようにしておく）
    ' 【何を保存したかまで書く】（基本ルール X）
    '   下書き＝「下書きを保存しました 16:02」／非公開＝「非公開のまま保存しました 16:02」／
    '   公開中＝「公開中の記事を更新しました 16:02（公開ページに反映しました）」（押したボタンが［更新する］なので、言葉をそろえる）
    Private Function SavedMessage() As String

        If _loadedArticle Is Nothing Then
            Return "記事を保存しました"
        End If

        Dim stamp As String = " " & _loadedArticle.UpdatedAt.Hour.ToString("00") &
                              ":" & _loadedArticle.UpdatedAt.Minute.ToString("00")

        ' 【公開中かは、DB から読んだ状態で見る】
        Select Case _loadedArticle.Status
            Case ArticleRules.ArticleStatus.Published
                Return "公開中の記事を更新しました" & stamp & "（公開ページに反映しました）"
            Case ArticleRules.ArticleStatus.Hidden
                Return "非公開のまま保存しました" & stamp
            Case Else
                Return "下書きを保存しました" & stamp
        End Select

    End Function


    ' 「公開しました 16:02」「非公開にしました 16:02」
    '
    ' 【DB から読んだ状態で言う】合図（saved-switched）はアドレスに残るので、あとで状態が変わってから
    ' F5 を押されても、嘘を言わないように
    Private Function SwitchedMessage() As String

        If _loadedArticle Is Nothing Then
            Return "記事を保存しました"
        End If

        Dim stamp As String = " " & _loadedArticle.UpdatedAt.Hour.ToString("00") &
                              ":" & _loadedArticle.UpdatedAt.Minute.ToString("00")

        ' 何をしたかまで書く（基本ルール X）＝「記事を」を足した
        If _loadedArticle.Status = ArticleRules.ArticleStatus.Published Then
            Return "記事を公開しました" & stamp
        End If

        Return "記事を非公開にしました" & stamp & "（もう公開ページには出ていません）"

    End Function


    ' 書く画面の URL（?id= と ?done= を付ける）
    '
    ' 【done に入るのは、このクラスの中で書いた合図だけ】利用者の入力は通らないので、組み立てで包む必要が無い
    Private Shared Function EditUrlFor(articleId As Integer, done As String) As String

        Return EditUrl &
               "?id=" & articleId.ToString(Globalization.CultureInfo.InvariantCulture) &
               "&done=" & done

    End Function


    ' === 保存に失敗して画面が戻ってきたら、「保存していない」の印を付ける ===
    '
    ' 【保存に成功したら、同じ画面へ「移り直す」（302）】ので、ポストバックのあとに
    ' この画面がそのまま描かれるのは、保存か削除が断られた（あるいは DB が読めなかった）ときだけである。
    ' 【移り直す先を一覧から書く画面に変えたが、この見分け方は変わらない】
    ' 成功＝302 を返す・失敗＝この画面を描く、という形のままである。
    ' 画面には打ったとおりに出ているが、DB には入っていない。
    ' Scripts/article-editor.js がこの印を見て、ページを離れる前に確かめる
    Protected Overrides Sub OnPreRender(e As EventArgs)

        MyBase.OnPreRender(e)

        If IsPostBack Then
            BodyBox.Attributes("data-unsaved") = "1"
        End If

        ' 【保存の帯の2つのボタンの言葉を、保存してある状態に合わせる】
        ' 描く直前に決める（断られて画面を返したときも正しくなる）。言葉と決まりは ArticleRules（保存の帯の節）。
        ' 【確かめの窓の文も、ここで入れる】状態を変えるほうのボタンだけ（Scripts/site.js の「5」）
        SaveButton.Text = ArticleRules.SaveButtonText(SavedStatus)

        SwitchButton.Text = ArticleRules.SwitchButtonText(SavedStatus)
        SwitchButton.Attributes("data-confirm") = ArticleRules.SwitchConfirmText(SavedStatus)
        SwitchButton.Attributes("data-confirm-ok") = ArticleRules.SwitchConfirmOkText(SavedStatus)

        ' 【どのボタンが、どの検査の組を走らせるか】
        ' タイトルと本文が要るのは「公開になる保存」だけ（ArticleStore.Save と同じ決まり）。
        '   公開中　　　　… そのまま保存（更新する）＝ articlePublish ／ 状態を変える（非公開にする）＝ article
        '   下書き・非公開 … そのまま保存 ＝ article ／ 状態を変える（公開する）＝ articlePublish
        ' 【ここで決めると、ポストバックのときも同じ組で検査される】ValidationGroup は ViewState に残るため
        ' 【確かめの窓の前の検査（data-confirm-group）も、同じ組にそろえる】Scripts/site.js の「5」
        Dim published As Boolean = (SavedStatus.HasValue AndAlso SavedStatus.Value = ArticleRules.ArticleStatus.Published)

        SaveButton.ValidationGroup = If(published, PublishGroup, DraftGroup)
        SwitchButton.ValidationGroup = If(published, DraftGroup, PublishGroup)
        SwitchButton.Attributes("data-confirm-group") = SwitchButton.ValidationGroup

    End Sub

    Private Const DraftGroup As String = "article"
    Private Const PublishGroup As String = "articlePublish"


    ' 見出しの横の「使っている量」。中身は ArticleUsage.HeadingHtml（決まった文字と数だけ）
    Protected ReadOnly Property HeadingUsageHtml As String
        Get
            Return ArticleUsage.HeadingHtml(CurrentAccount)
        End Get
    End Property


    ' === 選択肢（カテゴリ・状態）===
    Private Sub BindChoices()

        ' 【状態を選ぶ欄は無い】（保存の帯のボタン2つへ作り直した。ArticleRules の保存の帯の節）

        ' 目次の3択。状態と同じく、名前と値はコード側が持つ
        TocList.Items.Clear()

        For Each m As ArticleRules.ArticleTocMode In ArticleRules.AllTocModes()

            TocList.Items.Add(New ListItem(
                ArticleRules.TocModeDisplayName(m),
                CInt(m).ToString(Globalization.CultureInfo.InvariantCulture)))

        Next

        ' 見出しの番号の2択。目次と同じ流儀で、名前も値もコード側が持つ
        HeadingNumberList.Items.Clear()

        For Each m As ArticleRules.HeadingNumberMode In ArticleRules.AllHeadingNumberModes()

            HeadingNumberList.Items.Add(New ListItem(
                ArticleRules.HeadingNumberDisplayName(m),
                CInt(m).ToString(Globalization.CultureInfo.InvariantCulture)))

        Next

        ' カテゴリ
        CategoryList.Items.Clear()
        CategoryList.Items.Add(New ListItem("（なし）", NoCategory))

        Try

            For Each c As ArticleCategoryStore.CategoryRecord In
                ArticleCategoryStore.GetAll(CurrentAccount.AccountId)

                CategoryList.Items.Add(New ListItem(
                    c.Name,
                    c.CategoryId.ToString(Globalization.CultureInfo.InvariantCulture)))

            Next

        Catch ex As Exception

            ' 【カテゴリが引けなくても、記事は書けるようにしておく】
            ' カテゴリは NULL を許しているので、「（なし）」のままなら保存まで通る
            ErrorReport.Report(ex, "ArticleEditPage.BindChoices")

        End Try

    End Sub


    ' === 開いた記事を、画面へ流し込む ===
    Private Sub LoadArticle()

        Dim id As Integer = ParseId(Request.QueryString("id"))

        If id = 0 Then

            ' 新規
            EditingArticleId = 0
            HeadingText.Text = HttpUtility.HtmlEncode("新しい記事")
            DeleteButton.Visible = False
            MetaText.Text = ""

            SavedStatusValue = -1

            SelectTocMode(ArticleRules.ArticleTocMode.None)
            SelectHeadingNumberMode(ArticleRules.HeadingNumberMode.None)

            ' 【［プレビュー］を、隠したまま描いておく】
            ' 自動保存が下書きを作った時点で、Scripts/article-editor.js が行き先（頭＋番号）を入れて出す。
            ' ［下書き保存］を押したときは、移り直した画面で LoadArticle の下のほうが同じリンクを出す（どちらも同じ見た目・同じ行き先）
            ViewLinkRow.Visible = True
            ViewLink.Text = HttpUtility.HtmlEncode("プレビュー")
            ViewLink.Attributes("aria-label") = "プレビュー（新しいタブで開きます）"
            ViewLink.Attributes("hidden") = "hidden"
            ViewLink.Attributes("data-view-link") = "1"
            ViewLink.Attributes("data-preview-base") = ArticleRules.AccountUrl(CurrentAccount.UserName) & "/"

            Return

        End If

        Dim article As ArticleStore.ArticleRecord

        Try
            article = ArticleStore.Find(CurrentAccount.AccountId, id)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEditPage.LoadArticle")

            ShowError(TroubleMessage)
            HideForm()
            Return

        End Try

        If article Is Nothing Then

            ' 【他人の記事も、存在しない記事も、同じ扱いにする】
            ' 分けると「その番号の記事が在るか」を外から確かめられる
            ' （こっそりの画像を、403 ではなく404にしてあるのと同じ判断）。
            ' ここは画面なので、一覧へ戻す
            Response.Redirect(ListUrl)

        End If

        EditingArticleId = article.ArticleId

        ' 保存できたことの知らせに使う（SavedMessage）。**DB から読んだ行**であって、画面の欄ではない
        _loadedArticle = article

        HeadingText.Text = HttpUtility.HtmlEncode("記事を編集")
        DeleteButton.Visible = True

        ' === 編集中の控え ===
        '
        ' 【控えがあれば、控えの中身で開く】公開中の記事を書きかけて閉じた・戻るを押した、のあとに開き直したとき。
        ' 上に「まだ公開ページに反映していない編集があります」と出して、「反映する（保存）」「この編集を捨てる」を選ばせる。
        ' 【状態の欄だけは本体のまま】控えは状態を持たない（状態の切り替えは「保存する」のときだけ）
        Dim draft As ArticleStore.DraftRecord = Nothing

        Try
            draft = ArticleStore.FindDraft(CurrentAccount.AccountId, article.ArticleId)
        Catch ex As Exception

            ' 【控えが読めなくても、本体は開ける】ただし黙って本体で開くと、控えの中身を上書きしてしまいうるので、そう書く
            ErrorReport.Report(ex, "ArticleEditPage.LoadArticle（編集中の控え）")
            ShowError("編集中の控えを読めませんでした。公開中の内容で開いています。少し待ってから開き直してください。")

        End Try

        If draft IsNot Nothing Then

            TitleBox.Text = draft.Title
            SlugBox.Text = If(draft.Slug, "")
            BodyBox.Text = draft.BodyHtml
            SelectCategory(draft.CategoryId)
            SortOrderBox.Text = OrderToText(draft.SortOrder)
            PinOrderBox.Text = OrderToText(draft.PinOrder)

            ' 【目次・見出しの番号は控えの値で開く】状態（Status）と違って、控えにも持たせてある
            SelectTocMode(draft.TocMode)
            SelectHeadingNumberMode(draft.HeadingNumberMode)

            DraftNotice.Visible = True
            DraftNoticeText.Text = HttpUtility.HtmlEncode(
                "未反映の変更があります（" & Stamp(draft.SavedAt) & " に自動保存）。" &
                "画面には、その変更の内容を出しています。")

        Else

            TitleBox.Text = article.Title
            SlugBox.Text = If(article.Slug, "")
            BodyBox.Text = article.BodyHtml
            SelectCategory(article.CategoryId)
            SortOrderBox.Text = OrderToText(article.SortOrder)
            PinOrderBox.Text = OrderToText(article.PinOrder)

            SelectTocMode(article.TocMode)
            SelectHeadingNumberMode(article.HeadingNumberMode)

        End If

        SavedStatusValue = CInt(article.Status)

        MetaText.Text = HttpUtility.HtmlEncode(BuildMetaText(article))

        ' 【記事のページへのリンク】保存してある状態で文言を決める。
        ' 読み上げ用の名前に「新しいタブで開きます」を入れる（見える文字は短いまま）
        Dim viewText As String =
            If(article.Status = ArticleRules.ArticleStatus.Published, "公開ページを見る", "プレビュー")

        ViewLinkRow.Visible = True
        ViewLink.NavigateUrl = ArticleRules.PageUrl(CurrentAccount.UserName, article.ArticleId, article.Slug)
        ViewLink.Text = HttpUtility.HtmlEncode(viewText)
        ViewLink.Attributes("aria-label") = viewText & "（新しいタブで開きます）"

        ' 【公開中の記事だけ、「編集中の内容をプレビュー」も置く】（持ち越した分）
        ' 控えが無い間は隠しておき、自動保存で控えができたら article-editor.js が出す（控えが消えたら、また隠す）
        If article.Status = ArticleRules.ArticleStatus.Published Then

            DraftPreviewLink.Visible = True
            DraftPreviewLink.NavigateUrl =
                ArticleRules.DraftPreviewUrl(CurrentAccount.UserName, article.ArticleId, article.Slug)

            ' 【保存の帯の［破棄する］も、黄色いボタンと同じ出し方】
            DiscardDraftBarButton.Visible = True

            If draft Is Nothing Then
                DraftPreviewLink.Attributes("hidden") = "hidden"
                DiscardDraftBarButton.Attributes("hidden") = "hidden"
            End If

        End If

    End Sub


    ' === 保存 ===
    ' 【「反映する（保存）」（ApplyDraftButton）も、この同じ保存である】
    ' 画面は控えの中身で開いているので、ふつうに保存すれば本体へ反映され、控えは Save の中で消える
    '
    ' 【状態は、押したボタンで決まる】
    '   SaveButton・ApplyDraftButton … 保存してある状態のまま
    '   SwitchButton　　　　　　　　 … 状態を変える（公開中なら非公開へ、それ以外は公開へ）
    ' **画面から「どの状態にするか」の値は受け取らない**（選ぶ欄が無い）。決めるのは ArticleRules
    Protected Sub SaveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles SaveButton.Click, ApplyDraftButton.Click, SwitchButton.Click

        ' 【Validator の結果を、サーバー側でも見ること】
        ' 画面の検査は開発者ツールから外せるので、これだけでは守りにならないが、
        ' 外していない普通の利用では、ここで止まって文言が出る
        If Not Page.IsValid Then
            Return
        End If

        ' 【包んだ本文を戻せなかったら、保存しない】（黙って空の本文で保存しない）
        If _bodyDecode IsNot Nothing AndAlso Not _bodyDecode.Ok Then
            ShowError(_bodyDecode.Message)
            Return
        End If

        ' 【新しい記事を、自動保存が先に作っていたら、その記事を保存する】
        ' そうしないと、自動保存で作った下書きとは別に、もう1つ記事が増える
        If EditingArticleId = 0 Then
            ' 【Me.Request と書くこと】VB は大文字小文字を区別しないので、下のローカル変数 request と同じ名前になる
            EditingArticleId = ParseId(Me.Request.Form(AutoSavedIdField))
        End If

        Dim request As New ArticleStore.SaveRequest With {
            .ArticleId = EditingArticleId,
            .AccountId = CurrentAccount.AccountId,
            .CategoryId = SelectedCategoryId(),
            .Title = TitleBox.Text,
            .Slug = SlugBox.Text,
            .BodyHtml = BodyBox.Text,
            .Status = If(sender Is SwitchButton,
                         ArticleRules.SwitchStatus(SavedStatus),
                         ArticleRules.KeepStatus(SavedStatus)),
            .SortOrder = ArticleRules.NormalizeOrder(SortOrderBox.Text),
            .PinOrder = ArticleRules.NormalizeOrder(PinOrderBox.Text),
            .TocMode = ArticleRules.NormalizeTocMode(ParseId(TocList.SelectedValue)),
            .HeadingNumberMode = ArticleRules.NormalizeHeadingNumberMode(ParseId(HeadingNumberList.SelectedValue)),
            .OnceToken = FormOnce.TokenOf(Me.Request)}

        ' 【.OnceToken＝二重送信を防ぐ目印】（毎回の基本の6。足した＝点検で見つかった抜け）
        ' 見るのは ArticleStore.Insert（新しい記事を足すときだけ。足す文と同じトランザクションの中）。
        ' 番号のある記事の保存は「直す」なので、2回やっても結果が同じ＝見ない

        Dim result As ArticleStore.SaveResult

        Try
            result = ArticleStore.Save(request, CurrentAccount.RoleKey)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEditPage.Save")

            ShowError(TroubleMessage)
            Return

        End Try

        If Not result.Ok Then

            ' 【二重送信だった】（戻るボタンで戻って、もう一度保存した）
            ' この画面の自動保存を止める。描き直した画面には新しい目印が入るので、止めないと、
            ' 次に1文字打っただけで、自動保存が「別の記事」を黙って作ってしまう。
            ' 手で「もう一度押す」ことはできる（断る文に書いてある。FormOnce のいつもの決まり）
            If result.AlreadySaved Then
                AutoSaveStopped = True
            End If

            ' 【打った本文は、画面に残す】打ち直させないため
            ShowError(result.Message, result.Field)
            Return

        End If

        ' 【保存しても、この画面に留まる】（それまでは一覧へ戻していた）
        ' 指摘：反映を確定させたいときに意識してボタンを押すが、そうすると必ず一覧に戻ってしまう。
        ' まだ途中なのに。一覧へ戻していたのは、そう決めたからではなく、最初の作りのままだった。
        '
        ' 【そのまま描き直さず、同じ画面へ「移り直す」（302）】理由は3つある。
        '   ①F5 で、もう一度保存が送られない（描き直すと、そのページは POST の結果になる）。
        '     Accounts/Settings.aspx が ?done=name|email|password でやっているのと同じ形である
        '   ②「保存していない変更」の印（OnPreRender の data-unsaved）が付かない状態で開き直すので、
        '     保存したあとに「一覧へ戻る」を押しても、離れる確かめの窓（beforeunload）が出ない
        '   ③控え（ArticleDrafts）は Save の中で消えているので、読み直せば知らせの帯も自然に消える
        ' 【引き換えに、画面はいちばん上から始まる】そこは Scripts/article-editor.js が、
        ' 押す前に見ていた場所へ戻している（「保存したあと、押す前に見ていた場所へ戻す」の囲み）。
        '
        ' 【新しい記事は、採番された番号の画面へ】（result.ArticleId）。以後の F5 も、その記事を開く。
        '
        ' 【文言ではなく「合図」を渡す】
        ' 「?msg=保存しました」にすると、誰でもその文字列を作れて、
        ' リンクを送りつけるだけで他人の画面に好きな文を出せる（Manage.aspx.vb のコメント）。
        ' 【使えない書き方を取り除いて保存したときは、そう知らせる】（黙って消さない）
        If result.RemovedUnsafe Then
            Response.Redirect(EditUrlFor(result.ArticleId, "saved-cleaned"))
        End If

        ' 【状態を変えたときは、そう知らせる】「公開しました」「非公開にしました」（ShowDoneNotice）。
        ' 【合図は saved で始める】Scripts/article-editor.js が「?done=saved…」のときだけ、押す前の場所へ戻すため
        If sender Is SwitchButton Then
            Response.Redirect(EditUrlFor(result.ArticleId, "saved-switched"))
        End If

        Response.Redirect(EditUrlFor(result.ArticleId, "saved"))

    End Sub


    ' === 消す ===
    Protected Sub DeleteButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles DeleteButton.Click

        If EditingArticleId = 0 Then
            Return
        End If

        Dim reason As String

        Try
            ' 【ゴミ箱へ移す】一覧の「削除」と同じ。完全に消すのはゴミ箱から
            reason = ArticleStore.MoveToTrash(CurrentAccount.AccountId, EditingArticleId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEditPage.MoveToTrash")

            ShowError(TroubleMessage)
            Return

        End Try

        If reason IsNot Nothing Then
            ShowError(reason)
            Return
        End If

        Response.Redirect(ListUrl & "?done=trashed")

    End Sub


    ' === 編集中の控えを捨てる ===
    '
    ' 【POST（ボタンのポストバック）】削除と同じ理由（GET だと、よそのページのリンク1つで捨てさせられる）。
    ' 捨てたら、公開中の中身で開き直す
    ' 【2つのボタンで共有】画面の上の［未反映の変更を破棄する］と、保存の帯の［破棄する］
    Protected Sub DiscardDraftButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles DiscardDraftButton.Click, DiscardDraftBarButton.Click

        If EditingArticleId = 0 Then
            Return
        End If

        Try
            ArticleStore.DeleteDraft(CurrentAccount.AccountId, EditingArticleId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEditPage.DiscardDraft")

            ShowError(TroubleMessage)
            Return

        End Try

        Response.Redirect(EditUrlFor(EditingArticleId, "discarded"))

    End Sub


    ' ============================================================
    ' 自動保存の口（WebMethod）
    ' ============================================================
    '
    ' Scripts/article-editor.js が、打つのが止まって数秒たったら呼ぶ（物理パス /Articles/Edit.aspx/AutoSave を直接）。
    '
    ' 【WebMethod は AccountPageBase を素通りする】（Shared なので、ページの OnInit が走らない）。
    ' ログインしているかは、ここで AccountSession.Current を見て確かめる（ほかの画面と同じ仕組み）。
    ' 書いてよいロールか・自分の記事か・自分のカテゴリかは、ArticleStore.AutoSave が確かめる。
    '
    ' 【よそのサイトから呼ばせない】ASP.NET の WebMethod は Content-Type: application/json の POST しか受け付けない。
    ' よそのページのフォームからは、その形では送れない（送るには CORS の事前確認が要り、このサイトは許していない）。
    ' そのうえ、ログインの Cookie は SameSite=Lax なので、よそのサイトからの POST には付かない。
    '
    ' 【本文は包んで届く】手で押す保存と同じ（BodyTransport.Decode → ArticleStore.AutoSave の中で Sanitize）。
    ' 包まないと、本番の WAF が「<script> タグは…」のような説明の文だけで 403 にする。
    '
    ' 【戻り値は、画面に出してよいものだけ】保存した時刻・どこへ書いたか・断った理由
    '
    ' 【tocMode を足した。引数が増えると、発行の瞬間だけ自動保存が失敗する】
    ' WebMethod は、引数が足りない呼び出しを 500 で断る（Lounge.CreateRoom に knockEnabled を足したときと同じ）。
    ' 発行の瞬間に**書く画面を開いたままの人**がいると、その画面の JS は古いままなので tocMode を送らず、
    ' 次の自動保存が「自動保存できませんでした」になる。**打った内容は画面に残っている**ので、
    ' 「保存する」を押す（ページごと送る）か、開き直せば直る。承知のうえで足した
    <WebMethod()>
    Public Shared Function AutoSave(articleId As Integer, title As String, slug As String, categoryId As Integer,
                                    sortOrder As String, pinOrder As String, tocMode As Integer,
                                    headingNumberMode As Integer,
                                    body As String) As Dictionary(Of String, Object)

        Return ErrorReport.GuardWebMethod(
            "ArticleEdit.AutoSave",
            Function() AutoSaveCore(articleId, title, slug, categoryId, sortOrder, pinOrder,
                                    tocMode, headingNumberMode, body))

    End Function

    ' 【本文・タイトルを ErrorReport の補足へ渡さないこと】メールに載る
    Private Shared Function AutoSaveCore(articleId As Integer, title As String, slug As String, categoryId As Integer,
                                         sortOrder As String, pinOrder As String, tocMode As Integer,
                                         headingNumberMode As Integer,
                                         body As String) As Dictionary(Of String, Object)

        Dim account As AccountStore.AccountRecord = AccountSession.Current(HttpContext.Current)

        If account Is Nothing Then
            Return AutoSaveReply(False, "ログインが切れています。もう一度ログインしてから、「保存する」を押してください。",
                                 articleId, "none", False, Nothing, True)
        End If

        Dim decoded As BodyTransport.DecodeResult =
            BodyTransport.Decode(body, ArticleRules.BodyMaxLength, "本文", "保存していません。")

        If Not decoded.Ok Then
            Return AutoSaveReply(False, decoded.Message, articleId, "none", False, Nothing, False)
        End If

        Dim request As New ArticleStore.SaveRequest With {
            .ArticleId = Math.Max(0, articleId),
            .AccountId = account.AccountId,
            .CategoryId = If(categoryId > 0, categoryId, CType(Nothing, Integer?)),
            .Title = If(title, ""),
            .Slug = slug,
            .BodyHtml = decoded.Html,
            .Status = ArticleRules.ArticleStatus.Draft,
            .SortOrder = ArticleRules.NormalizeOrder(sortOrder),
            .PinOrder = ArticleRules.NormalizeOrder(pinOrder),
            .TocMode = ArticleRules.NormalizeTocMode(tocMode),
            .HeadingNumberMode = ArticleRules.NormalizeHeadingNumberMode(headingNumberMode),
            .OnceToken = FormOnce.TokenOf(HttpContext.Current.Request)}

        ' 【.OnceToken】自動保存は、目印をヘッダー（FormOnce.HeaderName）で運んでくる。
        ' **引数にしなかった**＝WebMethod は引数が足りないと500で断るので、発行の瞬間に開いていた画面の自動保存が失敗する。
        ' ヘッダーなら、古い画面は送ってこないだけ（目印なし＝確かめない）で、今までどおり動く

        Dim result As ArticleStore.AutoSaveResult

        Try
            result = ArticleStore.AutoSave(request, account.RoleKey)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEdit.AutoSave")

            Return AutoSaveReply(False, TroubleMessage, articleId, "none", False, Nothing, False)

        End Try

        If Not result.Ok Then

            Dim rejected As Dictionary(Of String, Object) =
                AutoSaveReply(False, result.Message, articleId, "none", False, Nothing, False)

            ' 二重送信だった。画面は、これを見て自動保存を止める（同じ理由を、打つたびに出し続けない）
            rejected("alreadySaved") = result.AlreadySaved

            Return rejected

        End If

        Dim reply As Dictionary(Of String, Object) =
            AutoSaveReply(True, Nothing, result.ArticleId, result.Target, result.HasDraft, result.SavedAt, False)

        reply("removedUnsafe") = result.RemovedUnsafe

        Return reply

    End Function

    ' ============================================================
    ' 編集中の控えを捨てる口（WebMethod）
    ' ============================================================
    '
    ' 【いつ呼ばれるか】公開中の記事を書きかけて控えができたあと、**同じ画面の中で、開いたときの中身へ戻したとき**
    ' （Scripts/article-editor.js の publishedSnapshot）。控えを残すと、何も変わっていないのに
    ' 一覧に「未反映の編集あり」が出続ける。
    '
    ' 【なぜ自動保存の口（AutoSave）に任せなかったのか】あちらも「公開中の中身と同じなら控えを消す」と比べているが、
    ' 本文を1文字ずつ比べるので、書く欄（Quill）を通った本文が保存してある本文と少しでも違う形だと、消えない。
    ' 画面は「開いたときの中身と同じ」を、同じ欄から作った文字列どうしで確かめられるので、そちらで決めて、ここは消すだけ。
    '
    ' 【AutoSave に引数を足さなかった】引数を足すと、発行の瞬間に開いていた画面の自動保存が失敗する（上のコメント）。
    ' 口を1つ足すぶんには、古い画面は何も変わらない。
    '
    ' 【守りは AutoSave と同じ】ログインは中で見る（WebMethod は AccountPageBase を素通りする）。
    ' 消すのは「その人の・その記事の」控えだけ（ArticleStore.DeleteDraft が AccountId でも絞る）。
    ' よそのサイトからは呼べない（application/json の POST だけ・Cookie は SameSite=Lax）。
    ' 画面の「未反映の変更を破棄する」（DiscardDraftButton）と同じことを、確かめの窓なしでやる口だが、
    ' **消えるのは控えだけ**で、公開中の記事には触らない
    <WebMethod()>
    Public Shared Function DiscardDraft(articleId As Integer) As Dictionary(Of String, Object)

        Return ErrorReport.GuardWebMethod(
            "ArticleEdit.DiscardDraft",
            Function() DiscardDraftCore(articleId))

    End Function

    Private Shared Function DiscardDraftCore(articleId As Integer) As Dictionary(Of String, Object)

        Dim account As AccountStore.AccountRecord = AccountSession.Current(HttpContext.Current)

        If account Is Nothing Then
            Return AutoSaveReply(False, "ログインが切れています。もう一度ログインしてから、「保存する」を押してください。",
                                 articleId, "none", False, Nothing, True)
        End If

        If articleId <= 0 Then
            Return AutoSaveReply(False, "その記事は見つかりませんでした。", articleId, "none", False, Nothing, False)
        End If

        Try
            ArticleStore.DeleteDraft(account.AccountId, articleId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEdit.DiscardDraft")

            Return AutoSaveReply(False, TroubleMessage, articleId, "none", False, Nothing, False)

        End Try

        ' 【返す形は、自動保存の「控えへ書いたが、同じだったので消した」と同じ】画面は同じ処理で受け取る
        Return AutoSaveReply(True, Nothing, articleId, "draft", False, DateTime.Now, False)

    End Function


    ' ============================================================
    ' この画面から、その場でカテゴリを作る口（WebMethod。開発者が採用）
    ' ============================================================
    '
    ' 【ページを読み込み直さない】書きかけを失う不安を作らないため（ポストバックにすると、画面が描き直される）。
    ' 【決まりはカテゴリの画面と同じ1本の道】ArticleCategoryStore.Create（名前の検査・同じ名前を断る・一意索引）。
    ' 並べ方は「新しい順」、カテゴリの並び順は 0（どちらもカテゴリの画面で変えられる）。
    ' 【守りは AutoSave と同じ】ログインは中で見る（WebMethod は AccountPageBase を素通りする）／書けるロールか／
    ' よそのサイトからは呼べない（application/json の POST だけ・Cookie は SameSite=Lax）。
    ' 【連打】画面はボタンを押せなくしてから送る。2回届いても、同じ名前は ArticleCategoryStore が断る（2つできない）。
    ' 【使い捨ての目印（FormOnce）は使っていない】同じ名前を2回足すことはデータの決まりで起きないため
    ' （カテゴリの画面で目印を使っているのは「戻るボタンで戻って押し直した」ときの理由の文を分けるため）
    <WebMethod()>
    Public Shared Function AddCategory(name As String) As Dictionary(Of String, Object)

        Return ErrorReport.GuardWebMethod(
            "ArticleEdit.AddCategory",
            Function() AddCategoryCore(name))

    End Function

    Private Shared Function AddCategoryCore(name As String) As Dictionary(Of String, Object)

        Dim reply As New Dictionary(Of String, Object)()
        reply("ok") = False
        reply("message") = ""
        reply("categoryId") = 0
        reply("name") = ""

        Dim account As AccountStore.AccountRecord = AccountSession.Current(HttpContext.Current)

        If account Is Nothing Then
            reply("message") = "ログインが切れています。もう一度ログインしてください。"
            Return reply
        End If

        If Not RoleLimits.GetBoolean(account.RoleKey, RoleLimits.KeyCanWriteArticles) Then
            reply("message") = "このアカウントでは、記事を書けません。"
            Return reply
        End If

        Dim newId As Integer
        Dim reason As String

        Try
            reason = ArticleCategoryStore.Create(account.AccountId, If(name, ""),
                                                 ArticleRules.CategorySortMode.Newest, 0, newId)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleEdit.AddCategory")
            reply("message") = TroubleMessage
            Return reply

        End Try

        If reason IsNot Nothing Then
            reply("message") = reason
            Return reply
        End If

        ' 【名前は、保存したとおり（前後の空白を落とした形）で返す】画面の選択肢に入れる文字。画面は textContent で入れる
        reply("ok") = True
        reply("categoryId") = newId
        reply("name") = name.Trim()

        Return reply

    End Function


    ' 【時刻は「21:45」の形の文字列で返す】JSON の日付の形（/Date(…)/）を、画面で読み直さずに済むように
    Private Shared Function AutoSaveReply(ok As Boolean, message As String, articleId As Integer, target As String,
                                          hasDraft As Boolean, savedAt As DateTime?,
                                          loggedOut As Boolean) As Dictionary(Of String, Object)

        Dim stamp As String = ""

        If savedAt.HasValue Then
            stamp = savedAt.Value.Hour.ToString("00") & ":" & savedAt.Value.Minute.ToString("00")
        End If

        Dim reply As New Dictionary(Of String, Object)()

        reply("ok") = ok
        reply("message") = If(message, "")
        reply("articleId") = articleId
        reply("target") = target
        reply("hasDraft") = hasDraft
        reply("savedAt") = stamp
        reply("loggedOut") = loggedOut
        reply("removedUnsafe") = False

        Return reply

    End Function


    ' ============================================================
    ' 中で使うもの
    ' ============================================================

    ' 画面の入力を全部隠す（DBが読めなかったとき）
    '
    ' 【理由の1行だけを残す】
    ' 入力欄を出したまま「混み合っています」と書くと、打ってから保存して、もう一度断られる
    Private Sub HideForm()

        TitleBox.Visible = False
        CategoryList.Visible = False
        SwitchButton.Visible = False
        SortOrderBox.Visible = False
        PinOrderBox.Visible = False
        BodyBox.Visible = False
        SaveButton.Visible = False
        DeleteButton.Visible = False
        DraftNotice.Visible = False

    End Sub


    Private Sub SelectCategory(categoryId As Integer?)

        Dim value As String = NoCategory

        If categoryId.HasValue Then
            value = categoryId.Value.ToString(Globalization.CultureInfo.InvariantCulture)
        End If

        Dim item As ListItem = CategoryList.Items.FindByValue(value)

        ' 【見つからなければ「（なし）」のまま】
        ' カテゴリが引けなかったときや、開いている間に消されたときに、ここで落とさない
        If item IsNot Nothing Then
            CategoryList.SelectedValue = value
        End If

    End Sub


    Private Sub EnsurePostedCategory()

        Dim id As Integer? = SelectedCategoryId()

        If Not id.HasValue Then
            Return
        End If

        Dim value As String = id.Value.ToString(Globalization.CultureInfo.InvariantCulture)

        If CategoryList.Items.FindByValue(value) IsNot Nothing Then
            CategoryList.SelectedValue = value
            Return
        End If

        Try

            Dim c As ArticleCategoryStore.CategoryRecord = ArticleCategoryStore.Find(CurrentAccount.AccountId, id.Value)

            ' 自分のカテゴリでなければ何もしない（保存のほうで断る）
            If c IsNot Nothing Then
                CategoryList.Items.Add(New ListItem(c.Name, value))
                CategoryList.SelectedValue = value
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleEditPage.EnsurePostedCategory")
        End Try

    End Sub


    ' 目次の3択を、画面の欄に選ばせる
    '
    ' 【見つからなければ、そのまま】3択の外の値が DB に入っていても、ここで落とさない
    ' （読んだ時点で NormalizeTocMode を通しているので、ふつうは起きない）
    Private Sub SelectTocMode(mode As ArticleRules.ArticleTocMode)

        Dim value As String = CInt(mode).ToString(Globalization.CultureInfo.InvariantCulture)

        If TocList.Items.FindByValue(value) IsNot Nothing Then
            TocList.SelectedValue = value
        End If

    End Sub


    ' 見出しの番号の2択を、画面の欄に選ばせる（目次と同じ形）
    Private Sub SelectHeadingNumberMode(mode As ArticleRules.HeadingNumberMode)

        Dim value As String = CInt(mode).ToString(Globalization.CultureInfo.InvariantCulture)

        If HeadingNumberList.Items.FindByValue(value) IsNot Nothing Then
            HeadingNumberList.SelectedValue = value
        End If

    End Sub


    ' 【送られてきた値を、そのまま読む】
    ' この画面の中で作ったカテゴリ（AddCategory）は、画面が描いたときの選択肢に無い。
    ' DropDownList.SelectedValue は「描いたときの選択肢に無い値」を捨てて「（なし）」のままにするので、
    ' そのカテゴリで保存すると、カテゴリが黙って外れる。
    ' 【信じてよい理由】自分のカテゴリかは ArticleStore.Save が確かめる（他人の番号は断る）
    Private Function SelectedCategoryId() As Integer?

        Dim posted As String = If(IsPostBack, Me.Request.Form(CategoryList.UniqueID), Nothing)
        Dim value As Integer = ParseId(If(posted, CategoryList.SelectedValue))

        If value = 0 Then
            Return Nothing
        End If

        Return value

    End Function


    ' 数として読めない文字列は 0 にする
    '
    ' 【例外にしないこと】?id=abc のような URL は、誰でも作れる。
    ' 落とすと、リンクを送りつけるだけでエラー報告メールを出させられる
    Private Shared Function ParseId(text As String) As Integer

        Dim value As Integer

        If String.IsNullOrEmpty(text) Then
            Return 0
        End If

        If Not Integer.TryParse(text, Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, value) Then
            Return 0
        End If

        If value < 0 Then
            Return 0
        End If

        Return value

    End Function


    Private Shared Function OrderToText(value As Integer?) As String

        If Not value.HasValue Then
            Return ""
        End If

        Return value.Value.ToString(Globalization.CultureInfo.InvariantCulture)

    End Function


    ' 画面の下に出す、記事のいまの状態
    '
    ' 【日付は書式指定を使わず、自前で組み立てている】
    ' 共用サーバーのカルチャ次第で和暦になるため（部屋のログの日付の区切り行と同じ決まり）
    Private Shared Function BuildMetaText(article As ArticleStore.ArticleRecord) As String

        Dim text As String = "作成 " & Stamp(article.CreatedAt) &
                             "／最後の保存 " & Stamp(article.UpdatedAt)

        If article.FirstPublishedAt.HasValue Then
            text &= "／最初に公開した日 " & Stamp(article.FirstPublishedAt.Value)
        Else
            text &= "／まだ一度も公開していません"
        End If

        Return text

    End Function


    Private Shared Function Stamp(value As DateTime) As String

        Return value.Year & "/" & value.Month.ToString("00") & "/" & value.Day.ToString("00") &
               " " & value.Hour.ToString("00") & ":" & value.Minute.ToString("00")

    End Function

End Class
