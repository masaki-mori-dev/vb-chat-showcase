' 記事の公開ページで使い回す、見せ方の部品
'
' 記事のページ（ArticleDetail）・アカウントのトップページ（AccountTop）・カテゴリの一覧（CategoryList）の3つが使う。
' 3か所に同じ組み立てを書くと、片方だけ直したときに見た目が食い違うので、ここに1つだけ置く。
'
' 【ここが返す HTML には、利用者の文字を必ずエスケープしてから入れる】（タイトルは本文ではない）
Public NotInheritable Class ArticleDisplay

    Private Sub New()
    End Sub


    ' 日付1つを、人が読む形にする
    '
    ' 【サイトの日付の書き方は、この1つに揃えてある】（決定：日付の書き方を1つにそろえる）
    ' 記事のページの「公開した日」「更新した日」も、アカウントのトップページ・カテゴリの一覧も、
    ' （右の枠の更新履歴も通っていたが、更新履歴をやめた）
    ' **日付を出す場所を足すときも、必ずここを通すこと**（2か所で組み立てると、片方だけ形が古くなる）。
    '
    ' 【年月日は自分で繋ぐ】書式の指定（"D" など）は、共用サーバーのカルチャ次第で和暦になりうる
    ' （部屋の日付の区切り行で決めたのと同じ）
    Public Shared Function DateText(d As DateTime) As String

        Dim inv = Globalization.CultureInfo.InvariantCulture

        Return d.Year.ToString(inv) & "年" & d.Month.ToString(inv) & "月" & d.Day.ToString(inv) & "日"

    End Function


    ' 日付1つを <time> にする（class は呼ぶ側が決める。空なら付けない）
    '
    ' 【datetime 属性は、機械の読める形】読み上げソフト・検索エンジンのため。こちらは InvariantCulture で決まった形
    Public Shared Function TimeHtml(d As DateTime, cssClass As String) As String

        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim sb As New Text.StringBuilder("<time")

        If Not String.IsNullOrEmpty(cssClass) Then
            sb.Append(" class=""").Append(HttpUtility.HtmlAttributeEncode(cssClass)).Append("""")
        End If

        sb.Append(" datetime=""").Append(d.ToString("yyyy-MM-dd", inv)).Append(""">")
        sb.Append(HttpUtility.HtmlEncode(DateText(d))).Append("</time>")

        Return sb.ToString()

    End Function


    ' 公開した日を <time> にする（まだ公開していない記事では、その文言）
    Public Shared Function DateHtml(value As DateTime?) As String

        If Not value.HasValue Then
            Return HttpUtility.HtmlEncode("まだ公開していません")
        End If

        Return TimeHtml(value.Value, "")

    End Function


    ' 記事の一覧（タイトルのリンクと、公開した日）。空なら空文字
    '
    ' 【本文は出さない】抜粋を作るなら、ArticleHtml.SanitizeForDisplay を通すこと（記事の章の罠）
    Public Shared Function ListHtml(articles As IEnumerable(Of ArticleStore.ArticleRecord), userName As String) As String

        If articles Is Nothing Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder()

        For Each a As ArticleStore.ArticleRecord In articles

            sb.Append("<li class=""article-list-item""><a href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(ArticleRules.PageUrl(userName, a.ArticleId, a.Slug)))
            sb.Append(""">")
            sb.Append(HttpUtility.HtmlEncode(a.Title))
            sb.Append("</a> <span class=""article-list-date"">")
            ' 【読み上げソフトに、どの日付かを伝える】目では「一覧に並ぶ日付」で分かるが、
            ' 耳で聞くと日付だけが読まれて、公開した日なのか更新した日なのか分からない
            ' （記事のページには両方が出るようになったので、なおさら）。
            ' 出しているのは公開した日＝カテゴリの「新しい順／古い順」の並びに使っている値と同じ
            sb.Append("<span class=""visually-hidden"">公開した日：</span>")
            sb.Append(DateHtml(a.FirstPublishedAt))
            sb.Append("</span></li>")

        Next

        If sb.Length = 0 Then
            Return ""
        End If

        Return "<ul class=""article-list"">" & sb.ToString() & "</ul>"

    End Function


    ' 【404・503・控えさせない の3つは、Core/PageResponse.vb へ移した】
    ' 公開のプロフィールページからも呼ぶようになり、呼び元が2つのフォルダにまたがったため。
    ' 呼び方は PageResponse.MarkNotFound(Response) など

End Class
