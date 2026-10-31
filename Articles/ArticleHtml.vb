Imports System.Text.RegularExpressions
Imports Ganss.Xss

' 記事の本文（HTML）を、保存する前に通す関所
'
' ============================================================
' 【このサイトで、ここだけが例外である】
' ============================================================
'
' vb-chat の決まりは「入口の検査だけでは足りない。出口でもエスケープする」である
' （「サーバー側の検証（入室）」の章）。
' **記事の本文だけは、それができない。** 見たまま編集（Quill）の結果が HTML なので、
' 出口でエスケープすると、記事がタグの羅列になって出る。
'
' つまり記事の本文は、出口で生の HTML として出す。
' **唯一の砦が、ここ＝保存するときの HtmlSanitizer である。**
' （記事のページに出す前にもう一度通している＝下の SanitizeForDisplay。
'   あとで決まりを減らした日のための2枚目で、保存のときに通すことの代わりにはならない）
'
'   ・保存を通る道を増やしたら、そこにも必ずここを通すこと。
'     「管理人しか書かないから」を理由に外さないこと——
'     管理人の Cookie が盗まれたら、それはサイト全体の XSS になる
'   ・記事のタイトル・カテゴリ名・アカウント名は**本文ではない**。
'     今までどおり、出口で <%: %> を通してエスケープする
'
' 【ライブラリは HtmlSanitizer（MIT）】
' TinyMCE を却下したのと同じ見方で選んでいる（あちらは v7 から GPLv2+ になった）。
' HtmlSanitizer は MIT なので、GitHub で公開する vb-chat に混ぜても判断が要らない。
' 中身は AngleSharp（本物のHTMLパーサ）で組み立て直す形なので、
' 「正規表現でタグを消す」式の自作とは、破られ方の数が桁で違う。
'
' 【白リスト方式にしてある】
' 既定の許可リストを**いったん空にして**、このサイトで出したいものだけを入れ直している。
' 既定のまま足し引きすると、ライブラリを上げた日に「知らないタグが増えていた」ことが起こりうる。
Public NotInheritable Class ArticleHtml

    Private Sub New()
    End Sub


    ' 許すタグ
    '
    ' ============================================================
    ' 【書く画面（Quill）で作れるもの ＝ ここで許すもの】（そろえ、足した）
    ' ============================================================
    ' 画面で付けられる書式が、保存のときに取り除かれると、利用者には
    ' 「書いたのに消えた」としか見えない。逆に、画面で作れないものをここで許しておくと、
    ' 開発者ツールから送られたものが保存され、次に開いたときに Quill が黙って落とす
    ' （開き直して保存しただけで、記事が変わる）。**そろえてあるのは3か所**——
    '   1. Articles/Edit.aspx のツールバーのボタン
    '   2. Scripts/article-editor.js の FORMATS（Quill が受け付ける書式）
    '   3. ここ（AllowedTags ／ AllowedAttributes ／ AllowedClasses）
    ' **書式を1つ足すときは、3か所とも足すこと。**
    ' （HTMLモードの「使えない書き方」の見分けは、ここの一覧を Edit.aspx.vb が JSON にして渡しているので、4か所目にはならない）
    '
    ' Quill 2.0.3 の getSemanticHTML が出すもの（開発機で、全部のボタンと、よそからの貼り付けで確かめた）：
    '   見出し … h2・h3（h1 と h4〜h6 は、貼り付けた時点で h2・h3 へ寄せている）
    '   太字・斜体・下線・取り消し線 … strong・em・u・s
    '   箇条書き・番号付き … ul・ol・li（入れ子にはならない。Tab を字下げに使っていないため）
    '   引用 … blockquote
    '   コードの囲み … pre（data-language は article-editor.js が外してから送る）
    '   区切り線 … hr
    '   リンク … a（href。**サイトの中（/…）は同じタブ、外は target="_blank"・rel="noopener noreferrer"**。下の NormalizeLink）
    '   文字の色・背景の色 … span style="color: #RRGGBB"／"background-color: #RRGGBB"（下の「色だけは style で許す」）
    '   字寄せ … p・h2・h3・li・blockquote などの class="ql-align-center"／"ql-align-right"（左は class なし）
    '   段落・空の行 … p・br（空の行は article-editor.js が <p><br></p> にしている）
    '
    '   画像 … img（src・alt・width・height。下の「画像は、記事の画像の置き場所のものだけ」）
    '
    ' 【まだ許していないもの】h4〜h6・del・ins・sub・sup・code・div・表。
    ' 表は Quill の表の作りが弱く、スマホで崩れやすいので見送った
    Private Shared ReadOnly AllowedTags As String() = {
        "p", "br", "hr",
        "strong", "em", "u", "s",
        "h2", "h3",
        "blockquote", "pre",
        "ul", "ol", "li",
        "a", "span",
        "img"}


    ' 【h1 を入れていない】
    ' ページの見出し（h1）は記事のタイトルである。本文の中にもう1つ h1 が出ると、
    ' 読み上げソフトの見出しの並びが壊れる。本文の見出しは h2 から始める

    ' 許す属性
    '
    ' 【class は許すが、値は下の AllowedClasses の決まったものだけ】
    ' 字寄せを、Quill に class（ql-align-center・ql-align-right）で出させている。
    '
    ' 【色だけは style で許す。ただし2つの指定・#RRGGBB だけ】
    '、決まった数色のほかに「その他の色…」（カラーピッカー）で自由な色を選べるようにした。
    ' 自由な色は class の名前では表せない（名前を無限に用意できない）ので、style で持つ。
    ' **「何でも通る style」にはしない**——
    '   ・通す指定は color と background-color の2つだけ（AllowedCssProperties）
    '   ・値は「#＋16進6桁」だけ（rgb(…) で届いたものは #RRGGBB に直す。名前の色・var(…)・透明度つき・url(…) は落とす）
    '   ・style を付けてよいのは、ツールバーで作れる「字の飾り」の要素だけ（下の StyleElements。p や h2 などの塊に付いた style は落とす）
    ' 仕組みは下の NormalizeStyle（HtmlSanitizer が通したあとに、もう一度こちらで組み立て直す）
    '
    ' 【画像の4つ（src・alt・width・height）は、img の上でだけ許す】HtmlSanitizer の許す属性は要素を問わないので、
    ' img 以外に付いていたら、あとの NormalizeImage で落とす
    Private Shared ReadOnly AllowedAttributes As String() = {
        "href", "target", "rel",
        "class", "style",
        "src", "alt", "width", "height"}

    ' img の上でだけ許す属性（上の4つ）
    Private Shared ReadOnly ImageAttributes As String() = {"src", "alt", "width", "height"}

    ' 許す style の指定（上の「色だけは style で許す」）
    Private Shared ReadOnly AllowedStyleProperties As String() = {"color", "background-color"}

    ' 色の style を付けてよい要素
    '
    ' 【span だけでは足りなかった】（本番の実機テストで、太字に付けた色が保存で消えた。開発機でも再現して確かめた）
    ' Quill は、太字・斜体・下線・取り消し線・リンクの字に色を付けると、span で包まずに
    ' **その要素そのもの**（<strong style="color: …">・<em>・<u>・<s>・<a>）に色を付ける。
    ' ふつうの字・見出し・引用・箇条書きの中の字には <span style> を使う。コードの囲みには色を付けられない（Quill が断る）。
    ' 「span だけ」にしていたので、太字などに付けた色だけが保存で落ちていた。
    '
    ' 【ツールバーで作れる字の飾りの要素だけ】塊の要素（p・h2・h3・li・blockquote・pre）には、Quill は色を付けない。
    ' 付いていたら（HTMLモード・開発者ツール・よそからの HTML）落とす。**字の飾りを足したら、ここにも足すかを考えること**
    '
    ' 【却下】保存の前に、strong などの色を中の span へ移し替える（「色は span だけ」を保つ）。
    ' Quill が出す形をこちらで組み替えることになり、組み替えの決まりを画面とサーバーの両方に持つことになる。
    ' 許す要素を増やすほうが、保存される HTML が Quill の出す形のままで、そろえる場所が増えない
    Private Shared ReadOnly StyleElements As String() = {"span", "strong", "em", "u", "s", "a"}


    ' 字寄せ（左はクラスなし＝既定なので、中央と右だけ）
    Private Shared ReadOnly AlignNames As String() = {"center", "right"}

    ' 許す class
    '
    ' 【ここを空にしないこと】class を許したうえでここが空だと、HtmlSanitizer は「どの class でも通す」になる。
    ' そうすると、本文からサイトの部品のクラス（.room-item など）を名乗れてしまう
    '
    ' 【色の class（ql-color-〇〇・ql-bg-〇〇）は、外した】
    ' 色は style（#RRGGBB）に1本化した。決まった数色の見本も、押すと theme.css の色の #RRGGBB が入る。
    ' 本番の記事は0件で、class の色で保存された記事は無い
    '
    ' 【画像の大きさと枠】名前の付いた class で持つ。大きさは3つのどれか（小・中・大）、枠は「枠なし」の名前が付いているか。
    ' 付いていない画像（貼ったもの）は「大・枠あり」で出る（Content/Site.css）。
    ' 【この4つは img の上でだけ】ほかの要素に付いていたら NormalizeImage が落とす。
    ' 【書く画面は、この名前をここから受け取る】（AllowedJson の imageSizeClasses・imageNoFrameClass）。JS に書き写さない
    ' 【AllowedClasses より上に書くこと】VB の Shared のフィールドは、書いた順に作られる。下に書くと、AllowedClasses を作る時点で Nothing
    Private Shared ReadOnly ImageSizeClasses As String() = {"article-img-s", "article-img-m", "article-img-l"}
    Private Const ImageNoFrameClass As String = "article-img-noframe"

    ' 【どの画面に出すか】名前の付いた class で持つ。大きさ・枠と、まったく同じ形。
    '
    ' 付いていなければ**両方に出る**（今までの画像は全部これ）。片方だけの名前が付いていたら、もう片方では出ない。
    ' 【`display: none` で消している】（Content/Site.css）読み上げソフトからも消える＝
    ' 「PC用とスマホ用の同じ写真が、2回読まれる」ことが起きない。
    ' 【境目は 700px】サイトのほかの場所（ラウンジの並べ方・部屋のパネル・画像の大きさ）と同じ数字にそろえてある。
    ' 【大きさは画面ごとに別々に選べる】PC用は「大」、スマホ用は「中」のように。どちらか片方しか出ないので、ぶつからない
    Private Shared ReadOnly ImageDeviceClasses As String() = {"article-img-pconly", "article-img-sponly"}

    Private Shared ReadOnly AllowedClasses As String() = BuildAllowedClasses()

    Private Shared Function BuildAllowedClasses() As String()

        Dim list As New List(Of String)()

        For Each name As String In AlignNames
            list.Add("ql-align-" & name)
        Next

        list.AddRange(ImageSizeClasses)
        list.Add(ImageNoFrameClass)
        list.AddRange(ImageDeviceClasses)

        Return list.ToArray()

    End Function



    ' 許す行き先の種類
    '
    ' 【data: を入れていない】
    ' 入れると、画像を data: の長い文字列としてDBへ入れられる（容量の歯止めが効かない）し、
    ' data:image/svg+xml の中にスクリプトを書ける。画像は、ファイルとして置く
    Private Shared ReadOnly AllowedSchemes As String() = {"http", "https", "mailto"}


    ' === 本文を通す ===
    '
    ' 戻り値 … 許した書き方だけが残った HTML
    '
    ' 【毎回インスタンスを作っている】
    ' HtmlSanitizer は、作ってから設定を足す形の部品である。
    ' 1つを共有して使い回すと、どこかで設定を触った瞬間に、
    ' ほかのスレッドが**途中の状態で**通すことになる。
    ' 記事の保存は管理人が押したときにしか起きないので、毎回作る費用は払ってよい
    ' （発言の関所と違い、1秒に何度も通る場所ではない）
    Public Shared Function Sanitize(html As String) As String
        Dim removed As Boolean
        Return Sanitize(html, removed)
    End Function


    ' 同じく通したうえで、何かを取り除いたかを返す
    '
    ' removedSomething … 許していないタグ・属性・class・style などを1つでも取り除いたら True。
    ' 保存のあと「使えない書き方を取り除きました」と知らせるために使う（黙って消さない）。
    ' 書く画面（Quill）から送ったものは、ふつう何も取り除かれない（画面で作れるもの＝ここで許すもの、にしてあるため）。
    ' True になるのは、HTMLモードで範囲の外を書いて JS の見分けをすり抜けたとき・素の入力欄で書いたとき・開発者ツールから送ったとき
    Public Shared Function Sanitize(html As String, ByRef removedSomething As Boolean) As String

        removedSomething = False

        If String.IsNullOrEmpty(html) Then
            Return ""
        End If

        Dim sanitizer As New HtmlSanitizer()

        Apply(sanitizer.AllowedTags, AllowedTags)
        Apply(sanitizer.AllowedAttributes, AllowedAttributes)
        Apply(sanitizer.AllowedClasses, AllowedClasses)
        Apply(sanitizer.AllowedSchemes, AllowedSchemes)

        ' style は色の2つだけ（上の「色だけは style で許す」）。値の形は、あとの NormalizeStyle で #RRGGBB に絞る
        Apply(sanitizer.AllowedCssProperties, AllowedStyleProperties)
        sanitizer.AllowedAtRules.Clear()

        ' data-* の属性も通さない（画面のJavaScriptが読む名前と、ぶつかりうるため）
        sanitizer.AllowDataAttributes = False

        ' 【許していないタグは、中身を残して外す（KeepChildNodes）】（開発機で実測）
        ' 既定（False）は、**タグごと中身も消す**。それだと、よそから貼った
        '   <figure><img src="…"><figcaption>説明</figcaption></figure>
        ' が丸ごと消える——**中の <img> は許しているのに、外側の figure が許されていないせいで**。
        ' 利用者から見れば「書いたのに保存したら消えた」としか分からない。
        ' True にすると、外側だけが外れて「絵と説明」は残る。
        '
        ' 【ただし、これだけだと script の中身が本文に混ざる】
        ' <script>alert(1)</script> の「alert(1)」が、ただの文字として残ってしまう
        ' （出口で文字として書き出されるので**危なくはない**が、記事に混ざるのは困る）。
        ' そこで下の RemovingTag で、外す前に中身を空にしている。
        '
        ' 実測（開発機）：
        '   False … <p>本文</p> だけが残った（見出しも図も消えた）
        '   True  … 大見出し<img …>説明alert(1)p{color:red}<p>本文</p>
        '   True ＋ 下の手当て … 大見出し<img …>説明<p>本文</p>
        '   （この測り方は とき＝img を許していたころのもの。img を外したので、
        '     今は <img> も落ちて「大見出し説明<p>本文</p>」になる。img を足し直す）
        '
        ' 【Quill の画面から保存するときは、ここはほとんど効かない】
        ' Quill は、受け付けない書式を貼り付けの時点で落とすので、許していないタグが届くのは
        ' 開発者ツールから送られたときか、JS が読み込めず素の textarea で書いたときだけである
        sanitizer.KeepChildNodes = True

        AddHandler sanitizer.RemovingTag, AddressOf OnRemovingTag

        ' 【リンクの開き方をそろえる】サイトの中は同じタブ、外は新しいタブ＋rel。
        ' 詳しくは下の NormalizeLink
        '
        ' 【style を #RRGGBB だけに組み立て直す】落としたものがあれば「取り除いた」に数える
        Dim removed As Boolean = False

        AddHandler sanitizer.PostProcessNode,
            Sub(sender, e)
                NormalizeLink(e)
                If NormalizeStyle(e) Then
                    removed = True
                End If
                If NormalizeImage(e) Then
                    removed = True
                End If
            End Sub

        ' 【取り除いたかを数える】どの種類の取り除きでも、印を立てるだけ
        AddHandler sanitizer.RemovingTag, Sub(sender, e) removed = True
        AddHandler sanitizer.RemovingAttribute, Sub(sender, e) removed = True
        AddHandler sanitizer.RemovingCssClass, Sub(sender, e) removed = True
        AddHandler sanitizer.RemovingStyle, Sub(sender, e) removed = True
        AddHandler sanitizer.RemovingAtRule, Sub(sender, e) removed = True
        AddHandler sanitizer.RemovingComment, Sub(sender, e) removed = True

        Dim result As String = sanitizer.Sanitize(html)

        removedSomething = removed

        Return result

    End Function


    ' === 記事のページに出す前に、もう一度通す ===
    '
    ' 【なぜ、表示でも通すのか】保存のときだけだと、**あとで許す書き方を減らした日に、
    ' それより前に保存した記事は古い決まりのまま出続ける**。表示でも通せば、決まりを変えた瞬間から全部の記事が新しい決まりで出る。
    ' **保存のときの Sanitize が砦であることは変わらない**（ここはその後ろの2枚目）。
    '
    ' 【記事の版ごとに1回だけ通して、控えておく】
    ' 実測（開発機）＝約1,600字 1.2ms／約3万字 15ms／約13万字 82ms。本文の上限（20万字）近い記事を
    ' 誰でも叩けるページで毎回通すのは、共用サーバーでは重い。
    ' 鍵は「記事の番号 ＋ 最後に更新した時刻」。保存すると UpdatedAt が変わるので、古い控えは使われなくなる。
    ' **決まりを変える＝発行する＝アプリが再起動する**ので、控えは必ず空になって新しい決まりで通し直される。
    ' 控えは HttpRuntime.Cache（使われなくなって1時間で消える。メモリが足りなければ ASP.NET が先に捨てる）。
    '
    ' 【同じ本文に2回かけても結果は変わらない】（開発機で確かめた）保存のときに通したものを、ここでもう一度通しても形は同じ
    '
    ' 【何かを取り除いたら、知らせる】ふつうは起きない。起きたら「決まりを減らしたあとに、古い記事に残っていた書き方が見つかった」
    ' という意味なので、エラー報告メールで知らせる（控えるので、1つの版につき1回）。本文は補足に入れない
    Private Const DisplayCachePrefix As String = "vbchat.article-display:"

    ' 【tocMode】記事の目次の3択。目次は本文に入っていないので、出すときに組み立てる
    ' （Articles/ArticleToc.vb）。**控えの鍵に混ぜていないが、それでよい**——
    ' 3択を変えるのは保存のときだけで、そのとき UpdatedAt が動くので、鍵はその場で変わる
    Public Shared Function SanitizeForDisplay(articleId As Integer, updatedAt As DateTime, html As String,
                                              tocMode As ArticleRules.ArticleTocMode,
                                              numbering As ArticleRules.HeadingNumberMode) As String
        Return SanitizeForDisplayCore("a", "記事", articleId, updatedAt, html, tocMode, numbering)
    End Function


    ' 同じく、公開中の記事の「編集中の控え」をプレビューするとき
    '
    ' 【控えの鍵は、本体と重ならないようにしてある】（"d" と "a"）。鍵は「記事の番号 ＋ 控えを保存した時刻」で、
    ' 自動保存のたびに時刻が変わるので、古い控えの結果は使われない
    Public Shared Function SanitizeDraftForDisplay(articleId As Integer, savedAt As DateTime, html As String,
                                                   tocMode As ArticleRules.ArticleTocMode,
                                                   numbering As ArticleRules.HeadingNumberMode) As String
        Return SanitizeForDisplayCore("d", "記事の編集中の控え", articleId, savedAt, html, tocMode, numbering)
    End Function


    ' === ブログのトップに出す「出だし」 ===
    '
    ' 【前は本文をそのまま並べていた】（ブログ型の④。SanitizeForPreview）。
    ' 指摘：PC でも各記事の全文を出すのはやりすぎ。PC もスマホも「タイトル＋出だし」にした。
    ' **使われなくなった SanitizeForPreview は消した**（目次の付かない本文を作る口）。
    '
    ' 【サーバーで、文字だけを抜いて短くする】
    ' CSS で本文を数行に切る形（-webkit-line-clamp）は採らなかった——隠した残りの本文も画像も、
    ' ブラウザは全部受け取ってしまう。抜いた文字だけを送れば、本文と画像を丸ごと送らずに済む。
    '
    ' 【抜いた文字は、HTML ではない】呼ぶ側は必ずエスケープして出すこと（<%#: %>）。
    ' そのため、ここは関所（Sanitize）を通していない——タグは全部落とし、文字実体も文字に戻すので、
    ' 返すものに「そのまま出してよい HTML」は1つも無い。
    '
    ' 【本文の頭だけ読めば足りる】（ArticleStore.GetPublishedPage が LEFT(BodyHtml, 4000) で持ってくる）
    ' 途中で切れたタグ（「<img src=…」で終わる）は、最後の OpenTagTail で落とす。
    '
    ' 【控える】鍵は "e" ＋ 記事の番号 ＋ UpdatedAt（記事のページ "a"・編集中の控え "d" と重ならない）
    Private Shared ReadOnly ExcerptBlockEnd As New Regex("</(p|h2|h3|li|blockquote|pre|div|ul|ol)\s*>|<br\s*/?>|<hr\s*/?>",
                                                          RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly ExcerptAnyTag As New Regex("<[^>]*>", RegexOptions.Compiled)
    Private Shared ReadOnly ExcerptOpenTagTail As New Regex("<[^>]*$", RegexOptions.Compiled)

    Public Shared Function ExcerptText(articleId As Integer, updatedAt As DateTime,
                                       htmlHead As String, maxChars As Integer) As String

        Dim key As String = DisplayCachePrefix & "e:" &
            articleId.ToString(Globalization.CultureInfo.InvariantCulture) & ":" &
            updatedAt.Ticks.ToString(Globalization.CultureInfo.InvariantCulture) & ":" &
            maxChars.ToString(Globalization.CultureInfo.InvariantCulture)

        Dim cached As String = TryCast(HttpRuntime.Cache(key), String)

        If cached IsNot Nothing Then
            Return cached
        End If

        Dim text As String = If(htmlHead, "")

        ' 塊の終わりは空白1つに（「見出し本文」のように、別の塊の文字がくっつかないように）
        text = ExcerptBlockEnd.Replace(text, " ")
        text = ExcerptAnyTag.Replace(text, "")
        text = ExcerptOpenTagTail.Replace(text, "")
        text = HttpUtility.HtmlDecode(text)

        ' 空白をまとめて、長ければ切って「…」（プロフィールの自己紹介の出だしと同じ切り方）
        Dim result As String = AccountProfileRules.Excerpt(text, maxChars)

        HttpRuntime.Cache.Insert(key, result, Nothing,
                                 System.Web.Caching.Cache.NoAbsoluteExpiration, TimeSpan.FromHours(1))

        Return result

    End Function


    Private Shared Function SanitizeForDisplayCore(kind As String, what As String, articleId As Integer,
                                                   stamp As DateTime, html As String,
                                                   tocMode As ArticleRules.ArticleTocMode,
                                                   numbering As ArticleRules.HeadingNumberMode) As String

        Dim key As String = DisplayCachePrefix & kind & ":" &
            articleId.ToString(Globalization.CultureInfo.InvariantCulture) & ":" &
            stamp.Ticks.ToString(Globalization.CultureInfo.InvariantCulture)

        Dim cached As String = TryCast(HttpRuntime.Cache(key), String)

        If cached IsNot Nothing Then
            Return cached
        End If

        ' 【順番】関所（Sanitize） → 画像をリンクで包む → 目次を差し込む。
        ' **どちらも関所の外でやる**——<a target="_blank"> も <details> も、関所は通さないため
        ' （通すと、その場で落とされる。ArticleToc.vb の冒頭）
        Dim removed As Boolean
        Dim result As String = ArticleToc.Apply(WrapImagesWithLink(Sanitize(html, removed)), tocMode, numbering)

        If removed Then
            ErrorReport.ReportProblem("ArticleHtml.SanitizeForDisplay",
                what & "（番号 " & articleId.ToString(Globalization.CultureInfo.InvariantCulture) &
                "）の本文に、今の決まりでは許していない書き方が残っていました。表示では取り除いています。" &
                "編集の画面で開いて保存し直すと、DB の本文も今の決まりの形になります。")
        End If

        HttpRuntime.Cache.Insert(key, result, Nothing,
                                 System.Web.Caching.Cache.NoAbsoluteExpiration, TimeSpan.FromHours(1))

        Return result

    End Function


    ' ============================================================
    ' 記事の画像を、押したら新しいタブで開けるようにする
    ' ============================================================
    '
    ' 部屋の画像と同じ見せ方にした（：記事の画像を押しても何も起きなかった）。
    ' 形は `Scripts/chat-room.js` の buildImageHtml と同じ——
    ' <a href="（元の画像）" target="_blank" rel="noopener noreferrer"><img …></a>。
    '
    ' 【DB の本文は書き換えない。出すときに付ける】（開発者に聞かれたので、ここに理由を残す）
    '   ・**本文に <a> を入れる形は採らなかった**。書く画面（Quill）は画像のリンクを作れないので、
    '     開いて保存し直すたびに <a> が落ちる／付け直す、のいたちごっこになる。
    '     既にある記事も全部書き直すことになる
    '   ・出すときに付ければ、**今ある記事も、書き直さずにその日から押せる**。
    '     書く画面は今までどおり（画像を押すと、大きさと説明を直す小窓が開く）
    '   ・JS で付ける形も採らなかった（JS が動かないと押せない／読み上げソフトに伝わるのが遅れる）
    '
    ' 【保存のときの Sanitize の中ではなく、そのあとで包んでいる】
    ' 中で包むと、<a> が NormalizeLink を通って **target="_blank" を外される**
    ' （行き先が「/」で始まる＝サイトの中、という決まりのため）。
    ' ここは「守り」ではなく「見せ方」なので、関所を抜けたあとに、別の1回で付ける。
    '
    ' 【結果は SanitizeForDisplayCore が控える】記事の版ごとに1回しか通らない
    Private Shared Function WrapImagesWithLink(html As String) As String

        If String.IsNullOrEmpty(html) OrElse html.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0 Then
            Return html
        End If

        Try

            Dim parser As New AngleSharp.Html.Parser.HtmlParser()
            Dim doc = parser.ParseDocument("<!DOCTYPE html><html><body>" & html & "</body></html>")

            ' 【先に一覧を控えてから触る】DOM を触りながら生きた一覧を回すと、途中で数が変わる
            Dim images As New List(Of AngleSharp.Dom.IElement)

            For Each node As AngleSharp.Dom.IElement In doc.QuerySelectorAll("img")
                images.Add(node)
            Next

            For Each img As AngleSharp.Dom.IElement In images

                Dim src As String = img.GetAttribute("src")

                ' 記事の画像だけ（よその画像は、そもそもサニタイザーが落としている）
                If Not ArticleImages.IsArticleImageSrc(src) Then
                    Continue For
                End If

                Dim parent = img.ParentElement

                If parent Is Nothing Then
                    Continue For
                End If

                ' 【既にリンクの中なら、何もしない】HTMLモードで自分で <a> を書いた記事のため
                If String.Equals(parent.TagName, "A", StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim link = doc.CreateElement("a")

                link.SetAttribute("href", src)
                link.SetAttribute("target", "_blank")
                link.SetAttribute("rel", "noopener noreferrer")
                link.SetAttribute("class", LinkClassFor(img))
                link.SetAttribute("aria-label", LinkLabelFor(img))

                parent.InsertBefore(link, img)
                link.AppendChild(img)

            Next

            Return doc.Body.InnerHtml

        Catch ex As Exception

            ' 【包めなくても、記事は出す】見せ方の話なので、ここで記事のページを落とさない
            ErrorReport.Report(ex, "ArticleHtml.WrapImagesWithLink")

            Return html

        End Try

    End Function


    ' リンクに付ける class
    '
    ' 【出す画面の class（article-img-pconly／-sponly）を、リンクにも写す】
    ' 写さないと、隠れている画像のぶんのリンクだけが残って、
    ' **中身の無い、押せて焦点も当たるリンク**になる（読み上げソフトにも出てくる）
    Private Shared Function LinkClassFor(img As AngleSharp.Dom.IElement) As String

        Dim names As New List(Of String) From {"article-img-link"}

        For Each name As String In ClassNamesOf(img)

            For Each deviceClass As String In ImageDeviceClasses

                If String.Equals(name, deviceClass, StringComparison.Ordinal) Then
                    names.Add(name)
                End If

            Next

        Next

        Return String.Join(" ", names)

    End Function


    ' リンクの読み上げ用の名前
    '
    ' 【リンクには、必ず名前を付ける】飾りの画像（alt="")を包むと、名前の無いリンクになり、
    ' 読み上げソフトが行き先の URL を読み上げることになる。
    ' 【「（新しいタブで開きます）」を付ける】サイトのほかの新しいタブのリンクと同じ書き方
    Private Shared Function LinkLabelFor(img As AngleSharp.Dom.IElement) As String

        Dim alt As String = If(img.GetAttribute("alt"), String.Empty).Trim()

        If alt.Length = 0 Then
            alt = "画像"
        End If

        Return alt & "（新しいタブで開きます）"

    End Function


    ' 書く画面（HTMLモード）へ渡す「使える書き方」の一覧（JSON）
    '
    ' 【4か所目を作らないため】HTMLモードで書いた本文に「使えない書き方」があるかを、JS が画面で見分けて知らせる。
    ' その一覧を JS に書き写すと、書式を足すたびに直す場所が4か所になる。ここから渡せば、ここを直すだけで済む。
    ' 【これは親切であって、防御ではない】本当に落とすのは、保存のときの Sanitize である
    Public Shared Function AllowedJson() As String

        Dim serializer As New System.Web.Script.Serialization.JavaScriptSerializer()

        Return serializer.Serialize(New With {
            .tags = AllowedTags,
            .attributes = AllowedAttributes,
            .classes = AllowedClasses,
            .styles = AllowedStyleProperties,
            .styleElements = StyleElements,
            .schemes = AllowedSchemes,
            .imageAttributes = ImageAttributes,
            .imageSrc = ArticleImages.SrcPatternText,
            .imageSizeClasses = ImageSizeClasses,
            .imageNoFrameClass = ImageNoFrameClass,
            .imageDeviceClasses = ImageDeviceClasses})

    End Function


    ' 外す前に、中身まで捨てるタグ
    '
    ' 【KeepChildNodes = True とセットで使うこと】
    ' あちらは「許していないタグは、中身を残して外す」なので、
    ' 何もしないと script や style の**中身が、ただの文字として本文に残る**。
    ' 危なくはない（出口で文字として書き出される）が、記事に混ざるのは困る。
    '
    ' 【ここに並べるのは、中身が「文章ではないもの」だけ】
    ' figure・section のような、ただの囲いをここへ足さないこと——
    ' 足した瞬間に、中に書いた文章まで消える
    Private Shared ReadOnly DropWithContent As String() = {
        "SCRIPT", "STYLE", "NOSCRIPT", "TEMPLATE", "IFRAME", "FRAME", "OBJECT", "EMBED", "APPLET"}


    Private Shared Sub OnRemovingTag(sender As Object, e As RemovingTagEventArgs)

        If e.Tag Is Nothing Then
            Return
        End If

        ' AngleSharp の TagName は大文字で返る
        If Array.IndexOf(DropWithContent, e.Tag.TagName) < 0 Then
            Return
        End If

        e.Tag.InnerHtml = ""

    End Sub

    ' === 画像は、記事の画像の置き場所のものだけ===
    '
    ' 戻り値 … 何かを落としたら True
    '
    ' 【src は /Uploads/articles/番号/GUID.拡張子 の形だけ】（ArticleImages.SrcPatternText）
    ' よそのサイトの画像（http://…）・data: の画像・このサイトのほかの場所（部屋の画像・こっそりの画像の取り出し口）は、img ごと落とす。
    '   ・よその画像 … 記事を開いた人の情報（IP など）が、よそのサイトに渡る。よそで絵を差し替えられる
    '   ・data: … 画像を長い文字列として DB へ入れられる（容量の歯止めが効かない。data:image/svg+xml にはスクリプトも書ける）
    '   ・部屋の画像 … 90日で消える（記事から絵が消える）
    ' 【width・height は 1〜9999 の数だけ】（届く前に場所を取るための値。SiteImage.ClampEdge と同じ範囲）
    ' 【alt は文字】中身は HtmlSanitizer が属性として書き出すので、何が書いてあっても HTML にはならない
    Private Shared Function NormalizeImage(e As PostProcessNodeEventArgs) As Boolean

        Dim element = TryCast(e.Node, AngleSharp.Dom.IElement)

        If element Is Nothing Then
            Return False
        End If

        Dim dropped As Boolean = False

        If Not String.Equals(element.TagName, "IMG", StringComparison.OrdinalIgnoreCase) Then

            For Each name As String In ImageAttributes
                If element.HasAttribute(name) Then
                    element.RemoveAttribute(name)
                    dropped = True
                End If
            Next

            ' 【画像の大きさ・枠の class は、img の上でだけ】
            '
            ' 【class を書き直すのは、画像の class を実際に外したときだけ】何も外していないなら触らない
            ' （HtmlSanitizer が決めた形を、要らないところで作り直さない）
            If element.HasAttribute("class") Then

                Dim kept As New List(Of String)()
                Dim removedHere As Boolean = False

                For Each name As String In ClassNamesOf(element)
                    If Array.IndexOf(ImageClassNames(), name) >= 0 Then
                        removedHere = True
                    Else
                        kept.Add(name)
                    End If
                Next

                If removedHere Then

                    dropped = True

                    If kept.Count = 0 Then
                        element.RemoveAttribute("class")
                    Else
                        element.SetAttribute("class", String.Join(" ", kept))
                    End If

                End If

            End If

            Return dropped

        End If

        ' 【img の上では、画像の class だけ】（字寄せなどの class は段落に付くもの）。大きさは1つだけ残す
        If NormalizeImageClasses(element) Then
            dropped = True
        End If

        If Not ArticleImages.IsArticleImageSrc(element.GetAttribute("src")) Then

            ' 【img ごと外す】src だけ外すと、中身の無い img が残る（場所だけ取って何も出ない）
            e.ReplacementNodes.Add(e.Document.CreateTextNode(""))
            Return True

        End If

        For Each name As String In {"width", "height"}

            If Not element.HasAttribute(name) Then
                Continue For
            End If

            Dim value As Integer

            If Not Integer.TryParse(element.GetAttribute(name), Globalization.NumberStyles.None,
                                    Globalization.CultureInfo.InvariantCulture, value) OrElse
               value < 1 OrElse value > 9999 Then

                element.RemoveAttribute(name)
                dropped = True

            End If

        Next

        Return dropped

    End Function


    ' class の文字列を、空白で区切った名前の並びにする
    '
    ' 【ClassList ではなく文字列で扱っている】AngleSharp の ClassList（DOMTokenList）は、PowerShell から DLL を呼んで確かめるときに
    ' 型の解決が絡んで扱いにくかった。文字列なら、何が起きるかが読めば分かる。
    ' （開発機で「StackOverflowException で落ちる」と一度見誤った。落ちていたのは確かめるための PowerShell の道具のほうで、
    '   動いているアプリ（IIS Express）で同じ7通りを通すと、どれも落ちずに正しく出た）
    Private Shared Function ClassNamesOf(element As AngleSharp.Dom.IElement) As String()

        Dim raw As String = element.GetAttribute("class")

        If String.IsNullOrWhiteSpace(raw) Then
            Return New String() {}
        End If

        Return raw.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf, ControlChars.FormFeed},
                         StringSplitOptions.RemoveEmptyEntries)

    End Function


    Private Shared Function ImageClassNames() As String()

        Dim names As New List(Of String)(ImageSizeClasses)
        names.Add(ImageNoFrameClass)
        names.AddRange(ImageDeviceClasses)

        Return names.ToArray()

    End Function


    ' img の class を「大きさ1つ ＋ 枠なし（あれば）＋ どの画面に出すか1つ（あれば）」だけにそろえる。何かを落としたら True
    '
    ' 【どれも「1つだけ」】大きさを2つ、出す画面を2つ書かれたら、先に出てきたほうだけを残す。
    ' PC用とスマホ用の両方を名乗った画像は、どちらの画面でも消えてしまう（`display: none` が2つ効く）ので、
    ' そういう形を本文に残さない
    Private Shared Function NormalizeImageClasses(element As AngleSharp.Dom.IElement) As Boolean

        If Not element.HasAttribute("class") Then
            Return False
        End If

        Dim dropped As Boolean = False
        Dim size As String = Nothing
        Dim device As String = Nothing
        Dim noFrame As Boolean = False

        For Each name As String In ClassNamesOf(element)

            If Array.IndexOf(ImageSizeClasses, name) >= 0 AndAlso size Is Nothing Then
                size = name
            ElseIf Array.IndexOf(ImageDeviceClasses, name) >= 0 AndAlso device Is Nothing Then
                device = name
            ElseIf name = ImageNoFrameClass Then
                noFrame = True
            Else
                dropped = True
            End If

        Next

        ' 【外したものが無ければ、書き直さない】（上の NormalizeImage と同じ）
        If Not dropped Then
            Return False
        End If

        Dim kept As New List(Of String)()

        If size IsNot Nothing Then
            kept.Add(size)
        End If

        If noFrame Then
            kept.Add(ImageNoFrameClass)
        End If

        If device IsNot Nothing Then
            kept.Add(device)
        End If

        If kept.Count = 0 Then
            element.RemoveAttribute("class")
        Else
            element.SetAttribute("class", String.Join(" ", kept))
        End If

        Return dropped

    End Function


    ' === リンクの開き方をそろえる===
    '
    ' | 行き先 | どう開くか |
    ' |---|---|
    ' | **このサイトの中**（`/contact` のような、`/` で始まる行き先） | **同じタブ**（`target` を外す） |
    ' | 外のサイト（`http:` `https:` `mailto:` で始まるもの） | 新しいタブ（`target="_blank"` ＋ `rel="noopener noreferrer"`） |
    '
    ' 【なぜ直したか】記事の中のリンクが全部「新しいタブ」だったので、
    ' サイトの中のページ（お問い合わせなど）へ飛ぶと、**戻るボタンで記事に戻れなかった**。
    '
    ' 【ここ（サニタイザー）でやる理由】保存のときと、記事を出すとき（SanitizeForDisplay）の両方を通るので、
    ' **既に本番に在る記事も、書き直さずに直る**。書く画面（JS）でやると、直すのに全部の記事を開き直すことになる。
    '
    ' 【「サイトの中」の見分け方＝`/` で始まるかどうか、だけ】（`//` は別のサイトなので外す）
    ' このサイトの URL を丸ごと書いた行き先（`https://vb-chat.example.com/…`）は「外」と見なして新しいタブで開く。
    ' ホスト名でも見分けられるが、**サニタイザーは要求の外（1日1回の仕事など）からも呼ばれうる**ので、
    ' 「今のホスト名」を当てにしない形にしてある。サイトの中のリンクは `/` から書くこと
    ' （SiteArticles.LinkHtml が作るリンクも、その形になっている）。
    '
    ' 【「取り除いた」に数えない】ここは落としているのではなく、そろえているだけである。
    ' 数えると、保存のたびに「使えない書き方を取り除きました」と出て、
    ' 記事を出すたびに ErrorReport のメールが飛ぶ（SanitizeForDisplay が、取り除きを知らせる作りのため）
    Private Shared Sub NormalizeLink(e As PostProcessNodeEventArgs)

        Dim element = TryCast(e.Node, AngleSharp.Dom.IElement)

        If element Is Nothing Then
            Return
        End If

        If Not String.Equals(element.TagName, "A", StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        Dim href As String = element.GetAttribute("href")

        If IsSameSiteHref(href) Then

            ' 同じタブで開く。rel も外す（noopener は、別の窓で開くときのためのもの）
            element.RemoveAttribute("target")
            element.RemoveAttribute("rel")
            Return

        End If

        ' 行き先の無いリンク（Quill が about:blank を外したあとの残りなど）は、そのままにしておく
        If String.IsNullOrEmpty(href) Then
            Return
        End If

        ' 【別の窓で開くリンクには、rel を必ず足す】
        ' target="_blank" だけだと、開いた先のページから window.opener 経由で
        ' こちらのページを別のURLへ飛ばせる（古いブラウザで効く）。
        ' 今のブラウザは既定で塞いでいるが、こちらで名乗っておく
        element.SetAttribute("target", "_blank")
        element.SetAttribute("rel", "noopener noreferrer")

    End Sub


    ' 「このサイトの中」の行き先か（上の NormalizeLink）
    Private Shared Function IsSameSiteHref(href As String) As Boolean

        If String.IsNullOrEmpty(href) Then
            Return False
        End If

        ' 「//example.com/…」は、別のサイトである（今と同じ http／https で開くだけ）
        Return href.StartsWith("/", StringComparison.Ordinal) AndAlso
               Not href.StartsWith("//", StringComparison.Ordinal)

    End Function


    ' === style を #RRGGBB だけに組み立て直す===
    '
    ' 戻り値 … 何かを落としたら True（「使えない書き方を取り除きました」と知らせるため）
    '
    ' 【HtmlSanitizer だけでは足りない】AllowedCssProperties は「どの指定を通すか」しか決められず、
    ' 値（var(…)・名前の色・透明度つき、など）は、CSS として正しければ通る。
    ' そこで、HtmlSanitizer が通したあとの style を、こちらで1つずつ読み直し、
    ' 「color／background-color ＝ #RRGGBB」の形でだけ書き直す。読めないものは落とす。
    '
    ' 【値の形】HtmlSanitizer（AngleSharp.Css）は、色を rgba(185, 28, 28, 1) の形に書き直して返すことがある。
    ' 透明度が 1 のものだけを #RRGGBB に直す（半透明は、下の背景しだいで読みやすさが変わるので落とす）
    Private Shared ReadOnly HexColorPattern As New Regex("^#([0-9a-f]{6})$", RegexOptions.IgnoreCase)
    Private Shared ReadOnly ShortHexColorPattern As New Regex("^#([0-9a-f])([0-9a-f])([0-9a-f])$", RegexOptions.IgnoreCase)
    Private Shared ReadOnly RgbColorPattern As New Regex(
        "^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(1|1\.0+)\s*)?\)$",
        RegexOptions.IgnoreCase)

    Private Shared Function NormalizeStyle(e As PostProcessNodeEventArgs) As Boolean

        Dim element = TryCast(e.Node, AngleSharp.Dom.IElement)

        If element Is Nothing OrElse Not element.HasAttribute("style") Then
            Return False
        End If

        Dim raw As String = element.GetAttribute("style")

        ' 【字の飾りの要素のほかは style を持てない】（上の StyleElements）
        If Array.IndexOf(StyleElements, element.TagName.ToLowerInvariant()) < 0 Then
            element.RemoveAttribute("style")
            Return Not String.IsNullOrWhiteSpace(raw)
        End If

        Dim dropped As Boolean = False
        Dim kept As New List(Of String)()

        For Each declaration As String In raw.Split(";"c)

            If String.IsNullOrWhiteSpace(declaration) Then
                Continue For
            End If

            Dim colon As Integer = declaration.IndexOf(":"c)

            If colon <= 0 Then
                dropped = True
                Continue For
            End If

            Dim name As String = declaration.Substring(0, colon).Trim().ToLowerInvariant()
            Dim hex As String = ToHexColor(declaration.Substring(colon + 1).Trim())

            If Array.IndexOf(AllowedStyleProperties, name) < 0 OrElse hex Is Nothing Then
                dropped = True
                Continue For
            End If

            kept.Add(name & ": " & hex)

        Next

        If kept.Count = 0 Then
            element.RemoveAttribute("style")
        Else
            element.SetAttribute("style", String.Join("; ", kept))
        End If

        Return dropped

    End Function

    ' 色の値を #rrggbb（小文字）に直す。直せなければ Nothing
    Public Shared Function ToHexColor(value As String) As String

        If String.IsNullOrEmpty(value) Then
            Return Nothing
        End If

        Dim v As String = value.Trim()

        Dim m As Match = HexColorPattern.Match(v)

        If m.Success Then
            Return "#" & m.Groups(1).Value.ToLowerInvariant()
        End If

        m = ShortHexColorPattern.Match(v)

        If m.Success Then
            Return ("#" & m.Groups(1).Value & m.Groups(1).Value &
                    m.Groups(2).Value & m.Groups(2).Value &
                    m.Groups(3).Value & m.Groups(3).Value).ToLowerInvariant()
        End If

        m = RgbColorPattern.Match(v)

        If m.Success Then

            Dim hex As String = "#"

            For i As Integer = 1 To 3

                Dim n As Integer = Integer.Parse(m.Groups(i).Value, Globalization.CultureInfo.InvariantCulture)

                If n > 255 Then
                    Return Nothing
                End If

                hex &= n.ToString("x2", Globalization.CultureInfo.InvariantCulture)

            Next

            Return hex

        End If

        Return Nothing

    End Function


    ' 許可リストを、こちらの一覧で置き換える（足し引きではなく、入れ替え）
    Private Shared Sub Apply(target As ISet(Of String), values As String())

        target.Clear()

        For Each value As String In values
            target.Add(value)
        Next

    End Sub

End Class
