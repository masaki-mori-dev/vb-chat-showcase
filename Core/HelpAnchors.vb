Imports System.IO
Imports System.Web.Hosting

' 「?」の「くわしくは」の飛び先（使い方の記事の見出し）を数えて、変わったら知らせる
'
' 【飛び先の決め方】Core/HelpTexts.vb の表には「記事の URL の名前＋見出しの文字（＋同じ文字の中で上から何番目か）」が書いてある。
' その記事の見出しの一覧（Articles/SiteArticles.vb の控え）を上から見て、何番目かを数え、#article-heading-N を付ける。
' 数え方は記事のページの id の付け方（Articles/ArticleToc.vb の Apply）と同じ＝H2・H3 を上から、文字の無い見出しは数えない。
' 見出しが見つからなければ、記事の頭へ。記事そのもの（公開・URL の名前付き）が無ければ、リンクを出さない。
'
' 【変わったら、お知らせのメール】（基本ルール Y）
'   ・「?」ごとに前回の番号を App_Data\help-anchors.txt に覚える（DB は増やさない。消えても困らない＝次に数えた番号を覚え直すだけ。
'     そのとき見つからない見出しがあれば、その分だけ知らせる）
'   ・数え直すのは、SiteArticles の控えを作り直したとき（記事を保存・削除したとき・10分おき・再起動）。ページを開くたびではない
'   ・番号が変わった・見つからなくなったら、1通にまとめて info@ へ。**毎日の集計とは別のメール**。中身はそのまま作業の指示として貼れる形
'   ・同じ変化で何通も出さない：送ったら新しい番号を覚える。送ってから1時間は次を送らない（そのあいだは覚え直さず、1時間たってから送る）
'
' 【画面を止めない】数えられない・ファイルが書けない・メールが積めない、どれでも「?」はそのまま出す（リンクの番号は今の数え方で付く）
Public NotInheritable Class HelpAnchors

    Private Sub New()
    End Sub


    ' 飛び先
    Public Class Anchor

        Public Property Url As String

        ' 見出しの番号（見つからなければ 0）
        Public Property Number As Integer

    End Class


    Private Const StoreFile As String = "~/App_Data/help-anchors.txt"

    ' メールとメールのあいだ（同じ日に記事を何度も直しても、1時間に1通まで）
    Private Shared ReadOnly MailInterval As TimeSpan = TimeSpan.FromHours(1)

    Private Shared ReadOnly SyncRoot As New Object()
    Private Shared _lastMailAt As DateTime = DateTime.MinValue


    ' === 「?」1つの飛び先（記事が無ければ Nothing） ===
    Public Shared Function Resolve(e As HelpTexts.HelpEntry) As Anchor

        If e Is Nothing OrElse String.IsNullOrEmpty(e.MoreSlug) Then
            Return Nothing
        End If

        Dim article As SiteArticles.HelpArticleInfo = SiteArticles.HelpArticle(e.MoreSlug)

        If article Is Nothing Then
            Return Nothing
        End If

        Dim n As Integer = FindNumber(article.Headings, e.MoreHeading, e.MoreNth)
        Dim url As String = article.Url

        If n > 0 Then
            url &= "#" & ArticleToc.HeadingIdPrefix & n.ToString(Globalization.CultureInfo.InvariantCulture)
        End If

        Return New Anchor With {.Url = url, .Number = n}

    End Function


    ' 見出しの一覧の中で、その文字の nth 番目が何番か（1から。無ければ 0）
    Friend Shared Function FindNumber(headings As IList(Of String), text As String, nth As Integer) As Integer

        If headings Is Nothing OrElse String.IsNullOrEmpty(text) Then
            Return 0
        End If

        Dim want As String = ArticleToc.NormalizeHeadingText(text)
        Dim seen As Integer = 0

        For i As Integer = 0 To headings.Count - 1
            If String.Equals(headings(i), want, StringComparison.Ordinal) Then
                seen += 1
                If seen = Math.Max(1, nth) Then
                    Return i + 1
                End If
            End If
        Next

        Return 0

    End Function


    ' ============================================================
    ' 見張り（SiteArticles が控えを作り直したときに呼ぶ）
    ' ============================================================

    ' 1つぶんの変化
    Private Class Change
        Public Property Entry As HelpTexts.HelpEntry
        Public Property Url As String
        Public Property Before As String
        Public Property After As Integer   ' -1＝記事が無い、0＝見出しが無い
    End Class


    Friend Shared Sub Check(snap As SiteArticles.Snapshot)

        Try

            SyncLock SyncRoot
                CheckCore(snap)
            End SyncLock

        Catch ex As Exception
            ErrorReport.Report(ex, "HelpAnchors.Check")
        End Try

    End Sub


    Private Shared Sub CheckCore(snap As SiteArticles.Snapshot)

        Dim path As String = HostingEnvironment.MapPath(StoreFile)

        If path Is Nothing Then
            Return
        End If

        Dim stored As Dictionary(Of String, String) = ReadStore(path)
        Dim now As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim changes As New List(Of Change)

        For Each e As HelpTexts.HelpEntry In HelpTexts.All

            If String.IsNullOrEmpty(e.MoreSlug) Then
                Continue For
            End If

            Dim article As SiteArticles.HelpArticleInfo = Nothing

            If snap.HelpArticles IsNot Nothing Then
                snap.HelpArticles.TryGetValue(e.MoreSlug, article)
            End If

            Dim number As Integer = If(article Is Nothing, -1, FindNumber(article.Headings, e.MoreHeading, e.MoreNth))

            ' 覚える形：「記事の名前 / 見出し / 何番目 / 番号」。表の中身（記事・見出し）を直したら、別のものとして覚え直す
            Dim target As String = e.MoreSlug & vbTab & e.MoreHeading & vbTab & e.MoreNth.ToString(Globalization.CultureInfo.InvariantCulture)
            now(e.Key) = target & vbTab & number.ToString(Globalization.CultureInfo.InvariantCulture)

            Dim before As String = Nothing
            Dim beforeNumber As String = Nothing

            If stored.TryGetValue(e.Key, before) AndAlso before.StartsWith(target & vbTab, StringComparison.Ordinal) Then
                beforeNumber = before.Substring(target.Length + 1)
            End If

            Dim nowNumber As String = number.ToString(Globalization.CultureInfo.InvariantCulture)

            If beforeNumber IsNot Nothing Then
                If beforeNumber <> nowNumber Then
                    changes.Add(New Change With {.Entry = e, .Url = If(article Is Nothing, Nothing, article.Url), .Before = beforeNumber, .After = number})
                End If
            ElseIf number <= 0 Then
                ' 前の記録が無い（初めて数えた・表を直した）ときは、見つからないものだけ知らせる
                changes.Add(New Change With {.Entry = e, .Url = If(article Is Nothing, Nothing, article.Url), .Before = Nothing, .After = number})
            End If

        Next

        If changes.Count > 0 Then

            If DateTime.UtcNow - _lastMailAt < MailInterval Then
                ' 送ってから1時間たっていない。覚え直さずに、次に数えたとき（1時間たってから）送る
                Return
            End If

            Dim mail As KeyValuePair(Of String, String) = BuildMail(changes)
            SiteMail.Enqueue(mail.Key, mail.Value)
            _lastMailAt = DateTime.UtcNow

        End If

        If changes.Count > 0 OrElse Not SameMap(stored, now) Then
            WriteStore(path, now)
        End If

    End Sub


    ' 件名と本文（そのまま作業の指示として貼れる形。基本ルール Y）
    Private Shared Function BuildMail(changes As List(Of Change)) As KeyValuePair(Of String, String)

        Dim anyMoved As Boolean = changes.Exists(Function(c) c.After > 0)
        Dim anyMissing As Boolean = changes.Exists(Function(c) c.After <= 0)

        ' 【件名は "[vb-chat" で始める】SiteMail の件名の組み立ての決まり（.NET が日本語の件名を1文字の途中で切る罠を避ける）
        Dim subject As String

        If anyMoved AndAlso anyMissing Then
            subject = "[vb-chat お知らせ] 使い方の見出しの番号が変わった・見つからない「?」があります"
        ElseIf anyMissing Then
            subject = "[vb-chat お知らせ] 使い方の見出しが見つかりません"
        Else
            subject = "[vb-chat お知らせ] 使い方の見出しの番号が変わりました"
        End If

        Dim baseUrl As String = AccountMail.ProductionBaseUrl

        Try
            Dim ctx As HttpContext = HttpContext.Current
            If ctx IsNot Nothing Then
                baseUrl = AccountMail.PublicBaseUrl(ctx.Request)
            End If
        Catch ex As HttpException
            ' 要求の外（裏の作業）なら、本番の住所のまま
        End Try

        Dim sb As New System.Text.StringBuilder()

        sb.AppendLine("【vb-chat】使い方の記事の見出しが変わり、「?」の「さらに詳しく」の飛び先がずれました（" &
                      DateTime.Now.ToString("yyyy-MM-dd HH:mm", Globalization.CultureInfo.InvariantCulture) & "）")
        sb.AppendLine()
        sb.AppendLine("■ 変わったもの")

        For Each c As Change In changes

            Dim e As HelpTexts.HelpEntry = c.Entry
            Dim before As String = If(c.Before Is Nothing, "（前の記録なし）", If(c.Before = "-1", "記事が見つかりません", If(c.Before = "0", "見つかりません", c.Before)))
            Dim after As String = If(c.After = -1, "記事が見つかりません", If(c.After = 0, "見つかりません", c.After.ToString(Globalization.CultureInfo.InvariantCulture)))

            sb.AppendLine("・「?」：" & e.Key & "（" & e.Place & "）")
            sb.AppendLine("  記事：" & If(c.Url Is Nothing, "（公開中の記事に、URL の名前「" & e.MoreSlug & "」が見つかりません）", baseUrl & c.Url))
            sb.AppendLine("  見出し：「" & e.MoreHeading & "」" & If(e.MoreNth > 1, "（同じ文字の中で上から " & e.MoreNth & " 番目）", ""))
            sb.AppendLine("  番号：" & before & " → " & after)

        Next

        sb.AppendLine()
        sb.AppendLine("■ いまの「?」")

        If anyMoved Then
            sb.AppendLine("・番号が変わったものは、新しい番号で自動でつなぎ直してあります（直す必要はありません）。")
        End If

        If anyMissing Then
            sb.AppendLine("・見つからないものは、見出しが見つかるまで記事の頭へ飛びます（記事が見つからないものは、リンクを出していません）。")
            sb.AppendLine("  見出しの文字を変えたのなら、Core/HelpTexts.vb の表の見出しの文字を、新しい文字に直してください。")
        End If

        sb.AppendLine()
        sb.AppendLine("■ お願い")
        sb.AppendLine("使い方の記事どうしのリンク（記事の本文の中の #article-heading-番号 のリンク）も、同じ理由でずれていないか調べて、ずれていれば直し方を開発者に出してください。")

        Return New KeyValuePair(Of String, String)(subject, sb.ToString())

    End Function


    Private Shared Function ReadStore(path As String) As Dictionary(Of String, String)

        Dim map As New Dictionary(Of String, String)(StringComparer.Ordinal)

        Try

            If Not File.Exists(path) Then
                Return map
            End If

            For Each line As String In File.ReadAllLines(path, System.Text.Encoding.UTF8)

                Dim i As Integer = line.IndexOf(vbTab, StringComparison.Ordinal)

                If i > 0 Then
                    map(line.Substring(0, i)) = line.Substring(i + 1)
                End If

            Next

        Catch ex As IOException
            ' 読めなければ、前の記録が無いものとして数え直す（見つからないものだけ知らせる）
        End Try

        Return map

    End Function


    Private Shared Sub WriteStore(path As String, map As Dictionary(Of String, String))

        Dim lines As New List(Of String)

        For Each kv As KeyValuePair(Of String, String) In map
            lines.Add(kv.Key & vbTab & kv.Value)
        Next

        File.WriteAllLines(path, lines, New System.Text.UTF8Encoding(False))

    End Sub


    Private Shared Function SameMap(a As Dictionary(Of String, String), b As Dictionary(Of String, String)) As Boolean

        If a.Count <> b.Count Then
            Return False
        End If

        For Each kv As KeyValuePair(Of String, String) In b
            Dim v As String = Nothing
            If Not a.TryGetValue(kv.Key, v) OrElse v <> kv.Value Then
                Return False
            End If
        Next

        Return True

    End Function

End Class
