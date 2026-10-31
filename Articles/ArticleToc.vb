Imports System.Text.RegularExpressions

' 記事の目次
'
'
'   ・記事を書く画面で、目次を入れるかどうかを選ぶ
'   ・選んだら、見出し（H2・H3）から自動で目次が入る
'   ・H2 と H3 を、見た目でもちゃんと区別する
'   ・目次は開閉できる／既定で開いているか閉じているかも選べる
'
' 【目次は、本文（BodyHtml）に書き込まない】
' 出すたびに、その時点の H2・H3 から組み立てる。理由は3つ。
'   ・**見出しを直すと、目次もついてくる**（書き込む形だと、直すたびに作り直す処理が要る）
'   ・目次の形（見た目・読み上げ用の名前）を変えたい日に、**既にある記事を1本も書き直さなくてよい**
'   ・書く画面（Quill）は <details> を扱えない。本文に入れると、開いて保存し直すたびに壊れる
'     （記事の画像を <a> で包むのを「出すときにやる」と決めたのと、まったく同じ判断）
'
' 【サニタイザーを通したあとに呼ぶこと】
' ここが作る <nav>・<details>・<summary>・<ol>・<li> と、見出しに付ける id は、
' **どれも ArticleHtml の AllowedTags／AllowedAttributes に入っていない**。
' 関所の中で作ると、その場で落とされる。だから ArticleHtml.SanitizeForDisplayCore が、
' 関所を抜けたあと（画像をリンクで包むのと同じ場所）で呼んでいる。
'
' 【見出しの id は、本文には残らない】
' サニタイザーは id を許していないので、**利用者が書いた id は保存の時点で消える**＝
' ここで付ける id と、本文の中の何かがぶつかることは起きない。
' 名前（article-heading-1…）も、ページのほかの id（ASP.NET の MainContent_… など）と重ならない。
'
' 【結果は、呼ぶ側（SanitizeForDisplayCore）が控える】記事の版ごとに1回しか通らない。
' 3択を変えると Articles.UpdatedAt が動くので、控えの鍵（記事の番号＋時刻）もその場で変わる
Public NotInheritable Class ArticleToc

    Private Sub New()
    End Sub


    ' 見出しに付ける id の頭
    '
    ' 【変えるときは、Content/Site.css の scroll-margin-top の説明も見ること】
    Public Const HeadingIdPrefix As String = "article-heading-"

    ' 目次の見出しに出す文字（読み上げ用の名前にも使う）
    Private Const TocLabel As String = "目次"

    ' 中の空白をまとめる（見出しの中で改行していても、目次では1行にする）
    Private Shared ReadOnly SpaceRun As New Regex("\s+", RegexOptions.Compiled)


    ' H2 より前にある H3 に付ける印
    '
    ' 【番号を付けない見出しの目印】見出しの番号は CSS のカウンターで出す（H2＝「1.」、H3＝「1-1」）。
    ' H2 より前にある H3 は、数える相手の H2 が無いので「0-1」になってしまう。
    ' **その行だけ番号を出さない**ために、ここで印を付けておく（CSS 側で content を空にする）
    Public Const OrphanSubClass As String = "article-heading-nonum"

    ' 目次の行に付ける、同じ意味の印（目次の側でも番号を出さない）
    Private Const OrphanTocItemClass As String = "article-toc-item--nonum"


    ' 目次と見出しの番号を入れた HTML を返す（見出しが無いときは、渡されたものをそのまま返す）
    '
    ' 【2つの注文を1回の走査でやっている】目次（`mode`）と見出しの番号（`numbering`）は別の欄だが、
    ' どちらも「本文の H2・H3 を順に見る」ことが要る。2回パースすると、その分だけ遅くなる。
    '
    ' 【どちらも要らない記事にも、見出しの id は付ける】（それまでは付けなかった）
    ' 使い方の記事の「くわしくは〇〇で」を、同じページの中・ほかのページの見出しへ飛べるリンクにするため
    ' （使い方の記事の写真を撮る道具が /articles/…#article-heading-N で指す）。
    ' 付けるのは id だけで、目次も番号も出ない。記事のページは版ごとに控えるので、手間は1版に1回
    ' 【番号の数え方は、目次と同じ】文字の無い見出しは数えない。見出しを足すと、後ろの番号はずれる
    Public Shared Function Apply(html As String,
                                 mode As ArticleRules.ArticleTocMode,
                                 numbering As ArticleRules.HeadingNumberMode) As String

        If String.IsNullOrEmpty(html) Then
            Return html
        End If

        ' 見出しが1つも無い記事では、そもそも何もしない（安い判定で先に抜ける）
        If html.IndexOf("<h2", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
           html.IndexOf("<h3", StringComparison.OrdinalIgnoreCase) < 0 Then

            Return html

        End If

        Try

            Dim parser As New AngleSharp.Html.Parser.HtmlParser()
            Dim doc = parser.ParseDocument("<!DOCTYPE html><html><body>" & html & "</body></html>")

            ' 【先に一覧を控えてから触る】DOM を触りながら生きた一覧を回すと、途中で数が変わる
            ' （画像をリンクで包むところと同じ作法）
            Dim headings As New List(Of AngleSharp.Dom.IElement)

            For Each node As AngleSharp.Dom.IElement In doc.QuerySelectorAll("h2, h3")
                headings.Add(node)
            Next

            ' 目次に載せる見出しだけを選ぶ（文字の無い見出しは載せない）
            Dim entries As New List(Of Entry)
            Dim number As Integer = 0

            ' H2 が1つでも出てきたか（出てくるまでの H3 には、番号を付けない）
            Dim seenTop As Boolean = False

            For Each heading As AngleSharp.Dom.IElement In headings

                Dim isSub As Boolean =
                    String.Equals(heading.TagName, "H3", StringComparison.OrdinalIgnoreCase)

                If Not isSub Then
                    seenTop = True
                End If

                Dim text As String = CleanText(heading.TextContent)

                ' 【中身が文字でない見出しは、目次に出さない】
                ' 画像だけを見出しにした・空の見出しが残っている、など。
                ' 目次に空の項目が並ぶと、押しても何も起きない行になる
                If text.Length = 0 Then
                    Continue For
                End If

                ' 【H2 より前の H3 には、番号を付けない印】（開発者の指定）
                Dim orphan As Boolean = isSub AndAlso Not seenTop

                If orphan Then
                    AddClass(heading, OrphanSubClass)
                End If

                number += 1

                Dim id As String = HeadingIdPrefix &
                    number.ToString(Globalization.CultureInfo.InvariantCulture)

                heading.SetAttribute("id", id)

                entries.Add(New Entry With {
                    .Id = id,
                    .Text = text,
                    .IsSub = isSub,
                    .IsOrphanSub = orphan,
                    .Element = heading})

            Next

            If entries.Count = 0 Then
                Return html
            End If

            ' 【目次を入れない記事でも、ここまでは通る】番号の印と id を付けるため。
            ' 目次そのものは、3択が「入れる」のときだけ差し込む
            If mode = ArticleRules.ArticleTocMode.None Then
                Return doc.Body.InnerHtml
            End If

            Dim nav As AngleSharp.Dom.IElement = BuildNav(doc, entries, mode)

            ' 【置き場所は、最初の見出しのすぐ上】（開発者の指定：最初の H2 のすぐ上、書き出しの文の下）
            '
            ' 【「最初の H2」ではなく「最初の見出し」にしてある】
            ' H2 より前に H3 がある記事・H3 だけの記事だと、「最初の H2」は無いか、ずっと下にある。
            ' そうすると目次だけが記事の途中に現れて、上の見出しは目次から外れて見える。
            ' **どちらの形でも崩れない**ように、種類を問わず最初の見出しの上に置く
            Dim anchor As AngleSharp.Dom.IElement = TopLevelOf(doc, entries(0).Element)

            If anchor Is Nothing OrElse anchor.ParentElement Is Nothing Then
                Return html
            End If

            anchor.ParentElement.InsertBefore(nav, anchor)

            Return doc.Body.InnerHtml

        Catch ex As Exception

            ' 【目次が作れなくても、記事は出す】見せ方の話なので、ここで記事のページを落とさない
            ' （画像をリンクで包むところと同じ判断）
            ErrorReport.Report(ex, "ArticleToc.Apply")

            Return html

        End Try

    End Function


    ' 目次1行ぶん
    Private Class Entry

        Public Property Id As String
        Public Property Text As String

        ' H3 か（H2 なら False）
        Public Property IsSub As Boolean

        ' H2 より前にある H3 か（番号を出さない行）
        Public Property IsOrphanSub As Boolean

        Public Property Element As AngleSharp.Dom.IElement

    End Class


    ' class を1つ足す（もう付いていれば何もしない）
    Private Shared Sub AddClass(element As AngleSharp.Dom.IElement, name As String)

        Dim current As String = If(element.GetAttribute("class"), "").Trim()

        If current.Length = 0 Then
            element.SetAttribute("class", name)
            Return
        End If

        For Each one As String In current.Split(" "c)
            If String.Equals(one, name, StringComparison.Ordinal) Then
                Return
            End If
        Next

        element.SetAttribute("class", current & " " & name)

    End Sub


    ' <nav><details><summary>目次</summary><ol>…</ol></details></nav> を組み立てる
    '
    ' 【文字列で組み立てず、DOM で作っている】
    ' 見出しの文字は、利用者が書いたものである。文字列で連結すると、そこがエスケープ漏れの場所になる。
    ' AngleSharp の TextContent に入れれば、出すときに必ずエスケープされる
    '
    ' 【開閉は <details>／<summary>。JavaScript は使わない】
    '   ・JS が読み込めなくても開閉できる
    '   ・読み上げソフトに「開いている／閉じている」がそのまま伝わる（自分で作ると aria-expanded の付け外しが要る）
    '   ・<nav> で包んで aria-label を付けてあるので、「ナビゲーション、目次」と読まれる
    '
    ' 【H3 を <ol> の入れ子にしていない】
    ' H2 より前に H3 がある記事では、入れ子にする相手の H2 が無い（からの <li> を作ることになる）。
    ' 1段の <ol> にして、H3 の行に class を足して**見た目で1段下げる**（Content/Site.css）。
    ' どんな並びの記事でも崩れない形を採った
    Private Shared Function BuildNav(doc As AngleSharp.Dom.IDocument,
                                     entries As List(Of Entry),
                                     mode As ArticleRules.ArticleTocMode) As AngleSharp.Dom.IElement

        Dim nav = doc.CreateElement("nav")

        nav.SetAttribute("class", "article-toc")
        nav.SetAttribute("aria-label", TocLabel)

        Dim details = doc.CreateElement("details")

        details.SetAttribute("class", "article-toc-details")

        If mode = ArticleRules.ArticleTocMode.Open Then
            details.SetAttribute("open", "open")
        End If

        Dim summary = doc.CreateElement("summary")

        summary.SetAttribute("class", "article-toc-summary")
        summary.TextContent = TocLabel

        details.AppendChild(summary)

        Dim list = doc.CreateElement("ol")

        list.SetAttribute("class", "article-toc-list")

        For Each entry As Entry In entries

            Dim item = doc.CreateElement("li")

            Dim itemClass As String = "article-toc-item"

            If entry.IsSub Then
                itemClass &= " article-toc-item--sub"
            End If

            ' 本文の見出しと同じく、H2 より前の H3 の行には番号を出さない
            If entry.IsOrphanSub Then
                itemClass &= " " & OrphanTocItemClass
            End If

            item.SetAttribute("class", itemClass)

            Dim link = doc.CreateElement("a")

            ' 【class を付けておく】右の枠・スマホの小窓の目次（Scripts/article-toc.js）と同じ名前にしておくと、
            ' 「今読んでいる見出し」の見た目（.is-current）も、この3つで1つの規則で決まる
            link.SetAttribute("class", "article-toc-link")
            link.SetAttribute("href", "#" & entry.Id)
            link.TextContent = entry.Text

            item.AppendChild(link)
            list.AppendChild(item)

        Next

        details.AppendChild(list)
        nav.AppendChild(details)

        Return nav

    End Function


    ' その見出しを含む、body の直接の子までさかのぼる
    '
    ' 【なぜさかのぼるのか】ふつうは見出しが body の直接の子なので、そのまま返る。
    ' ただし HTMLモードで書いた記事だと、見出しが引用（blockquote）などの中に入っていることがある。
    ' そのとき見出しのすぐ上に目次を差し込むと、**引用の中に目次が入って**しまう
    Private Shared Function TopLevelOf(doc As AngleSharp.Dom.IDocument,
                                       element As AngleSharp.Dom.IElement) As AngleSharp.Dom.IElement

        Dim current As AngleSharp.Dom.IElement = element

        While current IsNot Nothing

            Dim parent = current.ParentElement

            If parent Is Nothing Then
                Return Nothing
            End If

            If parent Is doc.Body Then
                Return current
            End If

            current = parent

        End While

        Return Nothing

    End Function


    ' 見出しの文字（前後の空白を落とし、中の空白は1つにまとめる）
    ' 見出しの文字の一覧（上から順。記事のページの id＝article-heading-N の N は、この一覧の何番目か）
    '
    ' 【数え方は Apply と同じ】H2・H3 を上から、文字の無い見出しは数えない。**Apply を変えたら、ここも変えること**
    ' （「?」の「くわしくは」の飛び先＝Core/HelpAnchors.vb が、これで番号を数える）
    Public Shared Function HeadingTexts(html As String) As List(Of String)

        Dim list As New List(Of String)

        If String.IsNullOrEmpty(html) Then
            Return list
        End If

        If html.IndexOf("<h2", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
           html.IndexOf("<h3", StringComparison.OrdinalIgnoreCase) < 0 Then
            Return list
        End If

        Dim parser As New AngleSharp.Html.Parser.HtmlParser()
        Dim doc = parser.ParseDocument("<!DOCTYPE html><html><body>" & html & "</body></html>")

        For Each node As AngleSharp.Dom.IElement In doc.QuerySelectorAll("h2, h3")

            Dim text As String = CleanText(node.TextContent)

            If text.Length > 0 Then
                list.Add(text)
            End If

        Next

        Return list

    End Function


    ' 見出しの文字の比べ方（中の空白をまとめ、前後を落とす）。HeadingTexts の一覧と同じ形にする
    Friend Shared Function NormalizeHeadingText(text As String) As String
        Return CleanText(text)
    End Function


    Private Shared Function CleanText(text As String) As String

        If String.IsNullOrEmpty(text) Then
            Return ""
        End If

        Return SpaceRun.Replace(text, " ").Trim()

    End Function

End Class
