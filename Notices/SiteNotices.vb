' 公開側に出す「管理人からのお知らせ」の控えと、HTML の組み立て（ブログ型の⑤）
'
' 【何のためにあるか】お知らせは
'   ・サイト全体のページの右の枠（Site.Master）
'   ・ブログのページの右の枠（③。Articles/BlogSidebar.vb）
' の両方に出る。**どのページを開いても出る**ので、毎回 DB を引かないようメモリに控える。
'
' 【Articles/SiteArticles.vb と、まったく同じ形にしてある】
' 控えの持ち方・捨て方・寿命・フェイルオープン。片方を直したら、もう片方も見ること。
' **別の形にしなかった理由**＝右の枠の中で隣に並ぶ2つ（更新履歴とお知らせ）が、
' 違う仕組みで動いていると、どちらかが古いときに原因の切り分けができない。
'
' 【控えを捨てるとき】①管理の画面で足した・直した・消したとき（Notices/Manage.aspx が Invalidate を呼ぶ）
' ②10分たったとき（SSMS で直接直した分も、10分で出る）③アプリの再起動。
'
' 【DB が読めなかったら、空の控えで画面を止めない（フェイルオープン）】
' お知らせが出ないだけで、サイトの全ページが落ちる筋合いが無い。
' エラー報告メールで知らせ、1分後に取り直す
Public NotInheritable Class SiteNotices

    Private Sub New()
    End Sub

    ' 控えの寿命（SSMS で直接直したときの保険。ふつうは足した・直したときに捨てている）
    Private Shared ReadOnly Lifetime As TimeSpan = TimeSpan.FromMinutes(10)

    ' 読めなかったときの取り直しまでの間
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)


    Private Shared ReadOnly SyncRoot As New Object()
    Private Shared _items As List(Of SiteNoticeStore.NoticeRecord)
    Private Shared _expiresAt As DateTime = DateTime.MinValue

    ' 捨てるたびに1つ進める。読み込みの途中で捨てられたら、その読み込み結果は控えない
    ' （足した直前の古い中身を、足したあとに控えてしまわないように）
    Private Shared _generation As Integer


    ' === 今の控え（無ければ・古ければ、DB から作る） ===
    '
    ' 【DB は鍵の外で引く】引いている間、ほかのページを待たせない
    Public Shared Function Current() As List(Of SiteNoticeStore.NoticeRecord)

        Dim generation As Integer

        SyncLock SyncRoot

            If _items IsNot Nothing AndAlso DateTime.UtcNow < _expiresAt Then
                Return _items
            End If

            generation = _generation

        End SyncLock

        Dim loaded As List(Of SiteNoticeStore.NoticeRecord)
        Dim lifetimeToUse As TimeSpan = Lifetime

        Try
            loaded = SiteNoticeStore.ListVisible(SiteNoticeRules.ShowCount)
        Catch ex As Exception

            ErrorReport.Report(ex, "SiteNotices.Current")

            loaded = New List(Of SiteNoticeStore.NoticeRecord)()
            lifetimeToUse = RetryAfterFailure

        End Try

        SyncLock SyncRoot

            If generation = _generation Then
                _items = loaded
                _expiresAt = DateTime.UtcNow + lifetimeToUse
            End If

        End SyncLock

        Return loaded

    End Function


    ' === 控えを捨てる（管理の画面で足した・直した・消したとき） ===
    Public Shared Sub Invalidate()

        SyncLock SyncRoot
            _items = Nothing
            _expiresAt = DateTime.MinValue
            _generation += 1
        End SyncLock

    End Sub


    ' === 右の枠に出す HTML（見出しの中身だけ。見出しは呼ぶ側が書く） ===
    '
    ' 【1つも無ければ、空文字を返す】呼ぶ側が「見出しごと出さない」を決められるように
    ' （更新履歴は「まだありません」と出すが、お知らせは**無いのがふつう**なので、
    '   わざわざ「お知らせはありません」と場所を取らない）。
    '
    ' 【出口でエスケープする】一言も行き先も、利用者（管理人）が決めた文字である。
    ' 記事の本文だけが「生の HTML を出す」例外で、**ここは例外にしない**。
    '
    ' 【行き先は、こちらが <a> を組み立てる】
    ' DB に入っているのは「/」で始まるアドレスだけ（SiteNoticeRules.ValidateLink）。
    ' 出すときにも、もう一度その形を確かめている——**DB を直に書き換えられた行で、
    ' よそのサイトへ飛ばさないため**（入口の検査だけでは足りない、という決まり）
    Public Shared Function ListHtml() As String
        Return ItemsHtml(Current())
    End Function


    ' 渡された行を、右の枠と同じ形の <ul> にする（ListHtml から分けた）。
    ' お知らせの一覧のページ（Notices/NoticeList.aspx）も、これで並べる＝1件ぶんの形を2か所に書かない
    Public Shared Function ItemsHtml(items As List(Of SiteNoticeStore.NoticeRecord)) As String

        If items Is Nothing OrElse items.Count = 0 Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder("<ul>")

        For Each item As SiteNoticeStore.NoticeRecord In items

            sb.Append("<li>")

            ' 【日付は、記事の更新履歴と同じ形】（ArticleDisplay.TimeHtml）
            sb.Append(ArticleDisplay.TimeHtml(item.NoticeDate, "notice-date"))

            Dim text As String = HttpUtility.HtmlEncode(item.Body)

            Dim kind As SiteNoticeRules.LinkKind = SiteNoticeRules.LinkKindOf(item.LinkUrl)

            If kind = SiteNoticeRules.LinkKind.SiteLocal Then

                sb.Append("<a href=""").Append(HttpUtility.HtmlAttributeEncode(item.LinkUrl)).Append(""">")
                sb.Append(text).Append("</a>")

            ElseIf kind = SiteNoticeRules.LinkKind.External Then

                ' 【よそのサイトは、新しいタブで】記事の中のリンクと同じ扱い（ArticleHtml.NormalizeLink）。
                ' rel="noopener noreferrer" で、開いた先からこのページを触らせない・どこから来たかを渡さない。
                ' 読み上げソフトには、新しいタブで開くことを伝える
                sb.Append("<a href=""").Append(HttpUtility.HtmlAttributeEncode(item.LinkUrl))
                sb.Append(""" target=""_blank"" rel=""noopener noreferrer"">")
                sb.Append(text)
                sb.Append("<span class=""visually-hidden"">（新しいタブで開きます）</span></a>")

            Else

                ' 【リンクにしないときも、同じ <span> で包む】
                ' 包まないと、日付と文の間の余白が、リンクの行とそろわない
                sb.Append("<span class=""notice-text"">").Append(text).Append("</span>")

            End If

            sb.Append("</li>")

        Next

        sb.Append("</ul>")

        Return sb.ToString()

    End Function


    ' 【出すときにも、行き先の形をもう一度確かめている】（SiteNoticeRules.LinkKindOf）
    ' 入口の検査だけでは足りない（「入口の検査だけでは足りない。出口でもエスケープする」）。
    ' 既に DB に入っている行は、入口の検査を通っていないことがありうる（SSMS で手で入れた・検査を足す前に入れた）。
    ' よそのサイトを許したので、前の IsSafeLink（このサイトの中だけ）は、
    ' 入口と同じ決まりを持つ SiteNoticeRules.LinkKindOf へ置き換えた（2つの決まりを別々に持たないため）

End Class
