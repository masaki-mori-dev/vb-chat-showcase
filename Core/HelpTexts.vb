' 「?」の説明の小窓の文の表
'
' 【何のためにあるか】
' 「?」の文を、この1か所にまとめる。文を直すときは、この表だけを直せばよい（画面の markup には文を書かない）。
' markup には呼ぶ1行だけを書く：<%= HelpButton("lounge.trip") %>
'   （HelpButton は、ページの土台＝Chat/ChatPageBase.vb・Accounts/AccountPageBase.vb と、Chat/UserControls/RoomList.ascx.vb にある。
'     .aspx の markup から Core のクラスを直接呼ばないこと＝別のコンパイル単位で、ビルドは通るのに実行で500になりうる。基本ルール C の罠）
' 小窓の開け閉め・置き場所は Scripts/help.js、見た目は Content/help.css。
'
' 【部屋（Chat/ChatRoom.aspx）も同じ表】部屋の「?」は、どれもページを描くときに在る場所（参加者の一覧・管理人のボタン・スタンプの面）に置いたので、
' ほかのページと同じく、サーバーが組み立てた HTML をそのまま出している（JSON にしてページへ渡す形は、要らなかった）。
'
' 【「くわしくは」のリンク＝使い方の記事の見出しの文字で指す】（指摘：記事の頭に飛ぶのでは役に立たない）
' 表には「記事の URL の名前＋見出しの文字（＋同じ文字の中で上から何番目か）」を書く。
' 番号（#article-heading-N）は、ページを出すたびに Articles/SiteArticles.vb の控え（記事の見出しの一覧）から数える（DB は増えない）。
' 見出しが見つからないときは、記事の頭へ（Core/HelpAnchors.vb が、お知らせのメールを出す）。記事そのものが無ければ、リンクを出さない。
'
' 【id は、一度決めたら変えない】入力欄の aria-describedby が指しているものがある（helpNoticeLink）
Public NotInheritable Class HelpTexts

    Private Sub New()
    End Sub


    ' 小窓の中の1行
    Public Class HelpLine

        ' 文（素の文字。ここでエスケープする）
        Public Property Text As String

        ' 絵（本物の部品の HTML。無ければ Nothing）。読み上げでは飛ばし（aria-hidden）、意味は Text で伝える
        Public Property FigureHtml As String

    End Class


    ' 「?」1つぶん
    Public Class HelpEntry

        Public Property Key As String

        ' 小窓の id（data-help-target・aria-controls と同じ）
        Public Property Id As String

        ' ボタンの読み上げ用の名前（「〇〇の説明」）
        Public Property Label As String

        ' 小窓の見出し（無ければ Nothing）
        Public Property Title As String

        Public Property Lines As List(Of HelpLine)

        ' 「くわしくは」の飛び先（無ければ Nothing）
        Public Property MoreSlug As String
        Public Property MoreHeading As String

        ' 同じ文字の見出しが2つ以上あるとき、上から何番目か（1から）
        Public Property MoreNth As Integer = 1

        ' お知らせのメールに書く、置き場所の説明
        Public Property Place As String

    End Class


    ' === 表 ===
    Private Shared ReadOnly Entries As Dictionary(Of String, HelpEntry) = BuildEntries()

    ' 表の中身（並びは置き場所ごと）
    Public Shared ReadOnly Property All As IEnumerable(Of HelpEntry)
        Get
            Return Entries.Values
        End Get
    End Property

    ' 「くわしくは」で指している記事の URL の名前（Articles/SiteArticles.vb が、この記事の見出しだけを控える）
    Public Shared Function ReferencedSlugs() As List(Of String)

        Dim slugs As New List(Of String)

        For Each e As HelpEntry In Entries.Values
            If Not String.IsNullOrEmpty(e.MoreSlug) AndAlso Not slugs.Contains(e.MoreSlug) Then
                slugs.Add(e.MoreSlug)
            End If
        Next

        Return slugs

    End Function


    ' === 「?」のボタンと小窓の HTML ===
    '
    ' 【鍵が表に無ければ、何も出さない】（画面を落とさない。書き間違いは、見れば「?」が無いので分かる）
    ' extraClass … 外側の .help に足す class（並べ方の調整。無ければ空）
    Public Shared Function ButtonHtml(key As String, Optional extraClass As String = Nothing) As String

        Dim e As HelpEntry = Nothing

        If String.IsNullOrEmpty(key) OrElse Not Entries.TryGetValue(key, e) Then
            Return ""
        End If

        Dim id As String = HttpUtility.HtmlAttributeEncode(e.Id)
        Dim sb As New System.Text.StringBuilder()

        sb.Append("<span class=""help")
        If Not String.IsNullOrEmpty(extraClass) Then
            sb.Append(" ").Append(HttpUtility.HtmlAttributeEncode(extraClass))
        End If
        sb.Append(""">")

        sb.Append("<button type=""button"" class=""help-btn"" data-help-target=""").Append(id)
        sb.Append(""" aria-label=""").Append(HttpUtility.HtmlAttributeEncode(e.Label))
        sb.Append(""" aria-expanded=""false"" aria-controls=""").Append(id).Append(""">?</button>")

        sb.Append("<span class=""help-popover"" id=""").Append(id).Append(""" hidden=""hidden"">")

        ' 【中は span ではなく、段落ごとの span（display: block）】小窓は見出し（h3）や <p> の中にも置くので、
        ' <div>・<p> を入れると HTML として正しくない（ブラウザが勝手に閉じて、形が崩れる）。見た目は help.css が <p> と同じにする
        If Not String.IsNullOrEmpty(e.Title) Then
            sb.Append("<span class=""help-popover-title"">").Append(HttpUtility.HtmlEncode(e.Title)).Append("</span>")
        End If

        For Each line As HelpLine In e.Lines

            If String.IsNullOrEmpty(line.FigureHtml) Then
                sb.Append("<span class=""help-popover-p"">").Append(HttpUtility.HtmlEncode(line.Text)).Append("</span>")
            Else
                sb.Append("<span class=""help-popover-p help-popover-fig"">")
                sb.Append("<span class=""help-fig"" aria-hidden=""true"">").Append(line.FigureHtml).Append("</span>")
                sb.Append("<span>").Append(HttpUtility.HtmlEncode(line.Text)).Append("</span>")
                sb.Append("</span>")
            End If

        Next

        Dim more As HelpAnchors.Anchor = HelpAnchors.Resolve(e)

        If more IsNot Nothing Then
            ' 【見える文字は、どの「?」も「さらに詳しく」】（すべて「さらに詳しく」にそろえる）
            ' 前は「くわしくは「見出し」で」。読み上げ用の名前には、どの見出しへ飛ぶかを残す
            Dim text As String = "さらに詳しく"
            sb.Append("<span class=""help-popover-p help-popover-more""><a href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(more.Url))
            sb.Append(""" target=""_blank"" rel=""noopener"" aria-label=""")
            sb.Append(HttpUtility.HtmlAttributeEncode("さらに詳しく：使い方の「" & e.MoreHeading & "」（新しいタブで開きます）"))
            sb.Append(""">").Append(HttpUtility.HtmlEncode(text)).Append("</a></span>")
        End If

        sb.Append("</span></span>")

        Return sb.ToString()

    End Function


    ' ============================================================
    ' 表の中身
    ' ============================================================
    '
    ' 【決めた文】勝手に言い換えないこと（直すときは、決めた文を入れる）。
    ' 「／」で区切られていた文は、1つずつの行にしてある。
    Private Shared Function BuildEntries() As Dictionary(Of String, HelpEntry)

        Dim d As New Dictionary(Of String, HelpEntry)(StringComparer.Ordinal)

        ' --- 前からある8つ（markup から移した。文は1文字も変えていない） ---

        Add(d, "lounge.trip", "helpTrip", "トリップの説明", "ラウンジ／トリップの欄",
            title:="トリップとは",
            lines:={"あなたが本人だと示すための印です。",
                    "合言葉を入れると、名前の下に10桁の英数字が付きます。",
                    "同じ合言葉なら、名前やアイコンを変えても、別の日でも、同じ10桁になります。",
                    "合言葉を知らない人には、同じ印は作れません。",
                    "付けなくても、そのまま遊べます。",
                    "大事なパスワードは使わないでください。"},
            moreSlug:="howto-trip", moreHeading:="トリップを付ける")

        Add(d, "lounge.roomPassword", "helpRoomPassword", "パスワードの説明", "ラウンジ／部屋を作る／パスワード",
            lines:={"あとから「部屋の設定」で見られます。",
                    "大事なパスワードは使わないでください。"},
            moreSlug:="howto-create", moreHeading:="部屋を作る")

        Add(d, "article.html", "helpHtml", "HTMLで直すときの説明", "記事を書く画面／HTML で直すとき",
            lines:={"使える書き方：見出し（h2・h3）、太字・斜体・下線・取り消し線、文字の色・背景の色（#から始まる6けたの色）、字寄せ、箇条書き、引用、コードの囲み、区切り線、リンク、画像のボタンで貼った画像。",
                    "画像の説明は、img の alt=""…"" を直せば変わります。",
                    "それ以外（iframe・style のほかの指定など）は、書く画面に戻したときと保存するときに取り除かれます。"},
            moreSlug:="howto-blog", moreHeading:="記事を書く")

        ' 【キーボード操作の説明は書かない】（「画面の言葉」）前は「パソコンでは、Tab キーで本文の欄から次の項目へ移ります。」があった
        Add(d, "article.body", "helpBody", "本文の説明", "記事を書く画面／本文",
            lines:={"使える書式：見出し・文字の飾り・色・字寄せ・箇条書き・引用・コードの囲み・区切り線・リンク・画像。",
                    "取り除かれるもの：表・ほかのサイトの画像など。",
                    "画像は、画像のボタンで選ぶか、写真をそのまま貼り付け・落とし込み。貼った画像を押すと、説明・大きさ・枠を直せます。"},
            moreSlug:="howto-blog", moreHeading:="記事を書く")

        Add(d, "categories.sortMode", "helpCategorySort", "並べ方の説明", "カテゴリ／追加の「並べ方」",
            lines:={"「並べ方」で、そのカテゴリの記事を並べる順番を決めます。" &
                    "「指定した順」は、記事を書く画面の「カテゴリの中の順番」が小さいものから並びます。番号の無い記事は、そのあとに新しい順で並びます。"},
            moreSlug:="howto-blog", moreHeading:="置き場所と本文の見た目")

        Add(d, "notices.link", "helpNoticeLink", "行き先の説明", "お知らせの管理／行き先",
            lines:={"空なら、リンクを付けません。",
                    "ページのアドレス（https://…）を、そのまま貼り付けてください。外部サイトは新しいタブで開きます。",
                    "このサイトのページなら、/articles/… のように「/」からでも大丈夫です。"})

        Add(d, "notices.visible", "helpNoticeVisible", "表示の説明", "お知らせの管理／表示",
            lines:={"「表示」を外すと、データを消さずに画面から隠せます。"})

        Add(d, "display.blogRecent", "helpBlogRecent", "ブログの最新の記事の説明", "管理画面「表示」／ブログの最新の記事",
            lines:={"みんなのブログのページの、右の枠に出す数です。1〜20件。"})

        ' --- 足した12か所（決めた文） ---

        ' ラウンジ
        Add(d, "lounge.logout", "helpLogout", "チャットを出るの説明", "ラウンジ／［チャットを出る］の横",
            lines:={"トップに戻って、名前を決め直します。",
                    "名前・アイコン・色、通知音などの設定、トリップはすべて消えます。",
                    "アカウントのログインはそのまま残ります。"},
            moreSlug:="howto-start", moreHeading:="ラウンジの自分の枠")

        Add(d, "lounge.accountId", "helpAccountId", "アカウントIDを紐づけるの説明", "ラウンジ／「アカウントIDを紐づける」の横",
            lines:={"オンにすると、名前の下に @アカウントID が出て、あなただと分かります。"},
            moreSlug:="howto-account", moreHeading:="ラウンジで付け外しする")

        Dim fixedRooms As HelpEntry = Add(d, "lounge.roomsFixed", "helpRoomsFixed", "既定のルームの説明", "ラウンジ／「既定のルーム」の見出しの横",
            lines:={"いつでもある部屋です。はじめての人は「初心者歓迎！」へどうぞ。"},
            moreSlug:="howto-start", moreHeading:="ラウンジで部屋を選ぶ")
        fixedRooms.Lines.Add(Fig(CountFigure, "人数の表示（今の人数／定員）。"))
        fixedRooms.Lines.Add(Fig(FullButtonFigure, "満員で入れません。"))

        Dim userRooms As HelpEntry = Add(d, "lounge.roomsUser", "helpRoomsUser", "みんなが作ったルームの説明", "ラウンジ／「みんなが作ったルーム」の見出しの横",
            lines:={"利用者が作った部屋です。誰もいなくなると、自動で閉じて一覧から消えます。"},
            moreSlug:="howto-start", moreHeading:="ラウンジで部屋を選ぶ")
        userRooms.Lines.Add(Fig(LockFigure, "パスワード付きの部屋。「入室」を押すとパスワードを聞かれます。"))
        userRooms.Lines.Add(Fig(OwnerBadgeFigure, "その部屋の管理人。"))
        userRooms.Lines.Add(Fig(KnockButtonFigure, "満員でもノックを受け付ける部屋。押すと管理人に知らせ、許可されると定員を超えて入れます。パスワード付きの部屋は、許可されたあとパスワードを聞かれます。"))
        userRooms.Lines.Add(Fig(FullButtonFigure, "満員で、ノックを受け付けない部屋。"))

        Add(d, "lounge.createRoom", "helpCreateRoom", "部屋を作るの説明", "ラウンジ／見出し「部屋を作る」の右",
            lines:={"作った人が、その部屋の管理人になります。",
                    "1人1部屋までです。",
                    "誰もいなくなると、自動で閉じます。"},
            moreSlug:="howto-create", moreHeading:="部屋を作る")

        ' 部屋の中
        ' 【人数▼の横の「?」（room.count）は、外した】開発者の判断：人数の「?」は要らない
        ' （▼で開くのは見て分かる。PC で「(1/20)▲」の▲に重なっていた）

        Add(d, "room.participants", "helpParticipants", "参加者の一覧のボタンの説明", "部屋の中／参加者の一覧の中",
            lines:={"非表示にする：その人の発言が、あなたの画面にだけ出なくなります。相手には伝わりません。",
                    "こっそり話す：相手を選んでから押すと、2人だけの窓が開きます。"},
            moreSlug:="howto-private", moreHeading:="参加者の一覧")

        Add(d, "room.ownerTools", "helpOwnerTools", "管理人のボタンの説明", "部屋の中／管理人の3つのボタンの横",
            lines:={"管理人をゆずる：選んだ人が管理人になります。自分では取り戻せません。",
                    "退室させる：ラウンジへ戻します。すぐ入り直せます。",
                    "入室禁止にする：ラウンジへ戻し、この部屋に入れなくします。「部屋の設定」の入室禁止の一覧から解除できます。"},
            moreSlug:="howto-create", moreHeading:="部屋の管理人だけのボタン")

        Add(d, "room.stamp", "helpStamp", "スタンプの説明", "部屋の中／スタンプの並びの横",
            lines:={"スタンプを押すと、送る前に確かめの帯が出ます。［送る］で送られます。"},
            moreSlug:="howto-room", moreHeading:="絵文字・スタンプ・画像を送る")

        ' 記事を書く・記事一覧・カテゴリ
        Add(d, "article.status", "helpArticleStatus", "記事の状態の説明", "記事を書く画面／保存の帯の状態の印の横",
            lines:={"下書き：まだ誰にも見えません。",
                    "公開中：みんなが見られます。",
                    "非公開：一度公開した記事を、自分だけが見られる状態に戻したものです。",
                    "ボタンは、今の状態に合わせて入れ替わります。"},
            moreSlug:="howto-blog", moreHeading:="記事を書く")

        Add(d, "articles.filter", "helpArticleFilter", "カテゴリの絞り込みの説明", "記事一覧／カテゴリの絞り込みの横",
            lines:={"選ぶと、そのカテゴリの記事だけを、カテゴリで決めた並べ方で出します。"},
            moreSlug:="howto-blog", moreHeading:="ブログの一覧")

        Add(d, "articles.columns", "helpArticleColumns", "記事一覧の項目の説明", "記事一覧／表の上",
            lines:={"状態：下書き・公開中・非公開。",
                    "順番：カテゴリの中の順番。",
                    "トップに固定：ブログのトップに固定する順。",
                    "「未反映の変更あり」：公開中の記事に、まだ公開ページに出していない変更があります。"},
            moreSlug:="howto-blog", moreHeading:="ブログの一覧")

        Add(d, "categories.order", "helpCategoryOrder", "カテゴリの順番の説明", "カテゴリ／追加の「カテゴリの順番」の横",
            lines:={"カテゴリそのものを並べる順です。小さい数字が先に出ます。記事の並びは「並べ方」で決めます。"})

        Return d

    End Function


    Private Shared Function Add(d As Dictionary(Of String, HelpEntry), key As String, id As String, label As String, place As String,
                                Optional title As String = Nothing,
                                Optional lines As String() = Nothing,
                                Optional moreSlug As String = Nothing,
                                Optional moreHeading As String = Nothing) As HelpEntry

        Dim e As New HelpEntry With {
            .Key = key, .Id = id, .Label = label, .Title = title, .Place = place,
            .MoreSlug = moreSlug, .MoreHeading = moreHeading,
            .Lines = New List(Of HelpLine)}

        If lines IsNot Nothing Then
            For Each t As String In lines
                e.Lines.Add(New HelpLine With {.Text = t})
            Next
        End If

        d.Add(key, e)

        Return e

    End Function


    Private Shared Function Fig(figureHtml As String, text As String) As HelpLine
        Return New HelpLine With {.Text = text, .FigureHtml = figureHtml}
    End Function


    ' === 絵（本物の部品を、同じ class で小さく出す） ===
    '
    ' 【同じ class を使う＝見た目が画面とずれない】ラウンジの部品の CSS は Content/Site.css にある（ラウンジは Site.Master のページ）。
    ' 押しても何も起きない（Scripts/help.js が、絵の中の押下を外へ流さない）。
    ' 鍵の印は、ラウンジの一覧と同じ絵（Chat/UserControls/RoomList.ascx.vb の LockMarkMarkup）

    ' 人数の表示（3/20）
    Private Const CountFigure As String = "<span class=""room-count"">3/20</span>"

    ' 満室で入れないボタン（灰色）
    Private Const FullButtonFigure As String = "<span class=""btn-enter"" aria-disabled=""true"">入室</span>"

    ' 満室でノックを受け付ける部屋のボタン
    Private Const KnockButtonFigure As String = "<span class=""btn-enter"" data-mode=""knock"">ノック</span>"

    ' 「管理人」の札
    Private Const OwnerBadgeFigure As String = "<span class=""owner-badge"">管理人</span>"

    Private Shared ReadOnly Property LockFigure As String
        Get
            Return RoomList.LockMarkMarkup
        End Get
    End Property

End Class
