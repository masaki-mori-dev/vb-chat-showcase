Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' 「サイトの記事」の控え
'
' 【何のためにあるか】
' 一般向けのページ（トップ・ラウンジ・記事のページ…）は、開くたびに次の2つが要る。
'   ・右上のメニューの「使い方」「利用規約」の行き先
'   ・トップの「使い方」「利用規約」、ラウンジの「?」の「もっと詳しく」の行き先
' 【、右の枠の「更新履歴」をやめた】（お知らせと役目がかぶる・管理人の記事は右上のメニューから行ける・
'   ブログのページの右の枠の「最新の記事」で新しい順に見られる。使い方の8ページを作り直したら、更新履歴が使い方だけで埋まった）。
'   それまでここが持っていた「最後に更新した順の公開記事」（Recent）も消した
' 毎回 DB を引かないように、1回の問い合わせでまとめて作ってメモリに控える。
'
' 【リンクの行き先は、記事の「URL の名前」（Articles.Slug）で決まる】
' コードには howto・terms などの名前だけを書いておき、開発者が記事にその名前を付けて公開すれば、つながる。
' **行き先の記事が無い（まだ書いていない・下書き・非公開）なら、リンクは出さない**（文字ごと出さない）。
'
' 【「サイトの記事」を書くアカウント】有効な administrator のうち、いちばん番号の小さい1人。
' ユーザー名をコードに書かない（本番と開発機でユーザー名が違うため）。
'
' 【控えを捨てるとき】①記事を保存・削除したとき（ArticleStore が Invalidate を呼ぶ）②10分たったとき
' （SSMS で直接直した分も、10分で出る）③アプリの再起動。
'
' 【DB が読めなかったら、空の控えで画面を止めない（フェイルオープン）】見せるだけの値なので。
' リンクは出ない。エラー報告メールで知らせ、1分後に取り直す
Public NotInheritable Class SiteArticles

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"

    ' 控えの寿命（SSMS で直接直したときの保険。ふつうは保存・削除のときに捨てている）
    Private Shared ReadOnly Lifetime As TimeSpan = TimeSpan.FromMinutes(10)

    ' 読めなかったときの取り直しまでの間
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)


    ' 控えの中身（作ったあとは書き換えない＝鍵なしで何人が読んでもよい）
    Public Class Snapshot

        ' サイトの記事を書くアカウントのユーザー名（居なければ Nothing）
        Public Property UserName As String

        ' 名前の付いた公開記事（名前 → URL）
        Public Property LinksBySlug As Dictionary(Of String, String)

        ' 「?」の「くわしくは」で指している使い方の記事（名前 → URL と見出しの一覧）。（Core/HelpAnchors.vb）
        Public Property HelpArticles As Dictionary(Of String, HelpArticleInfo)

    End Class


    ' 使い方の記事1本ぶん（作ったあとは書き換えない）
    Public Class HelpArticleInfo

        Public Property Url As String

        ' 見出しの文字（上から順。N 番目が #article-heading-N。数え方は ArticleToc.HeadingTexts）
        Public Property Headings As List(Of String)

    End Class


    Private Shared ReadOnly SyncRoot As New Object()
    Private Shared _snapshot As Snapshot
    Private Shared _expiresAt As DateTime = DateTime.MinValue

    ' 捨てるたびに1つ進める。読み込みの途中で捨てられたら、その読み込み結果は控えない
    ' （保存の直前の古い中身を、保存のあとに控えてしまわないように）
    Private Shared _generation As Integer


    ' === 今の控え（無ければ・古ければ、DB から作る） ===
    '
    ' 【DB は鍵の外で引く】引いている間、ほかのページを待たせない（SiteSettings を SyncLock の中から読まない、と同じ考え方）。
    ' 同時に2人が作ることはありうるが、中身は同じなので害は無い
    Public Shared Function Current() As Snapshot

        Dim generation As Integer

        SyncLock SyncRoot

            If _snapshot IsNot Nothing AndAlso DateTime.UtcNow < _expiresAt Then
                Return _snapshot
            End If

            generation = _generation

        End SyncLock

        Dim loaded As Snapshot
        Dim lifetimeToUse As TimeSpan = Lifetime

        Try
            loaded = Load()
        Catch ex As Exception

            ErrorReport.Report(ex, "SiteArticles.Current")

            loaded = Empty()
            lifetimeToUse = RetryAfterFailure

        End Try

        SyncLock SyncRoot

            If generation = _generation Then
                _snapshot = loaded
                _expiresAt = DateTime.UtcNow + lifetimeToUse
            End If

        End SyncLock

        ' 【「?」の飛び先の番号が変わっていないかを、作り直したときにだけ見る】
        ' ページを開くたびではない（控えを作り直すのは、記事の保存・削除・10分おき・再起動のときだけ）。
        ' 読めなかったとき（空の控え）は見ない＝「記事が見つかりません」と誤って知らせないため。鍵の外で（中で DB もファイルも触らない、が決まり）
        If lifetimeToUse = Lifetime Then
            HelpAnchors.Check(loaded)
        End If

        Return loaded

    End Function


    ' === 控えを捨てる（記事を保存・削除したとき） ===
    Public Shared Sub Invalidate()

        SyncLock SyncRoot
            _snapshot = Nothing
            _expiresAt = DateTime.MinValue
            _generation += 1
        End SyncLock

    End Sub


    ' === 名前で、公開記事の URL を引く（無ければ Nothing） ===
    Public Shared Function UrlFor(slug As String) As String

        Dim snap As Snapshot = Current()
        Dim url As String = Nothing

        If snap.LinksBySlug IsNot Nothing AndAlso Not String.IsNullOrEmpty(slug) Then
            snap.LinksBySlug.TryGetValue(slug, url)
        End If

        Return url

    End Function


    ' === <a> を組み立てる（行き先の記事が無ければ空文字＝何も出さない） ===
    '
    ' text … 画面に出す文字。**ここでエスケープする**（呼ぶ側は素の文字を渡す）
    Public Shared Function LinkHtml(slug As String, text As String) As String

        Dim url As String = UrlFor(slug)

        If url Is Nothing Then
            Return ""
        End If

        Return "<a href=""" & HttpUtility.HtmlAttributeEncode(url) & """>" & HttpUtility.HtmlEncode(text) & "</a>"

    End Function


    ' 【「もっと詳しく」の HelpMoreHtml は、消した】どこからも呼ばれていなかった。
    ' 「?」の「くわしくは」のリンクは、見出しの文字で指す形に作り直した（Core/HelpTexts.vb と Core/HelpAnchors.vb）


    ' === 「?」の飛び先の記事（無ければ Nothing） ===
    Public Shared Function HelpArticle(slug As String) As HelpArticleInfo

        Dim snap As Snapshot = Current()
        Dim info As HelpArticleInfo = Nothing

        If snap.HelpArticles IsNot Nothing AndAlso Not String.IsNullOrEmpty(slug) Then
            snap.HelpArticles.TryGetValue(slug, info)
        End If

        Return info

    End Function


    Private Shared Function Empty() As Snapshot

        Return New Snapshot With {
            .UserName = Nothing,
            .LinksBySlug = New Dictionary(Of String, String)(StringComparer.Ordinal),
            .HelpArticles = New Dictionary(Of String, HelpArticleInfo)(StringComparer.Ordinal)}

    End Function


    ' === DB から作る（1回の問い合わせで、結果を3つ受け取る） ===
    '
    ' 【3つ目＝「?」の飛び先の使い方の記事の本文】見出しを数えるため。指している記事（7本ほど）だけを読む。
    ' 書いたアカウントは「有効なサイト管理者のうち、番号の小さい順で最初に見つかったもの」
    ' （本番はサイト管理者が1人。開発機は2人いて、使い方の記事は2人目が持っているため。メニューの行き先＝1つ目の結果は、今までどおり1人目だけ）
    Private Shared Function Load() As Snapshot

        Dim helpSlugs As List(Of String) = HelpTexts.ReferencedSlugs()
        Dim helpIn As New System.Text.StringBuilder()

        For i As Integer = 0 To helpSlugs.Count - 1
            If i > 0 Then helpIn.Append(", ")
            helpIn.Append("@h").Append(i.ToString(Globalization.CultureInfo.InvariantCulture))
        Next

        Dim sql As String =
            "DECLARE @a INT = (SELECT TOP (1) AccountId FROM Accounts " &
            "                   WHERE RoleKey = @Role AND IsActive = 1 ORDER BY AccountId); " &
            "SELECT UserName FROM Accounts WHERE AccountId = @a; " &
            "SELECT ArticleId, Slug FROM Articles " &
            " WHERE AccountId = @a AND Status = 20 AND Slug IS NOT NULL;"

        If helpSlugs.Count > 0 Then
            sql &= " SELECT ac.UserName, a.ArticleId, a.Slug, a.BodyHtml FROM Articles a " &
                   " JOIN Accounts ac ON ac.AccountId = a.AccountId " &
                   " WHERE ac.RoleKey = @Role AND ac.IsActive = 1 AND a.Status = 20 AND a.Slug IN (" & helpIn.ToString() & ") " &
                   " ORDER BY ac.AccountId, a.ArticleId;"
        End If

        Dim snap As Snapshot = Empty()

        Using conn As New SqlConnection(ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString)
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = RoleKeys.Administrator

                For i As Integer = 0 To helpSlugs.Count - 1
                    cmd.Parameters.Add("@h" & i.ToString(Globalization.CultureInfo.InvariantCulture), SqlDbType.NVarChar, 100).Value = helpSlugs(i)
                Next

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    ' 1つ目：ユーザー名（サイトのアカウントが居なければ行が無い）
                    If reader.Read() Then
                        snap.UserName = reader.GetString(0)
                    End If

                    If snap.UserName Is Nothing Then
                        Return snap
                    End If

                    ' 2つ目：名前の付いた公開記事
                    reader.NextResult()

                    While reader.Read()

                        Dim slug As String = reader.GetString(1)

                        snap.LinksBySlug(slug) = ArticleRules.PageUrl(snap.UserName, reader.GetInt32(0), slug)

                    End While

                    ' 3つ目：「?」の飛び先の使い方の記事（同じ名前が2本あれば、先に来たほう）
                    If helpSlugs.Count > 0 AndAlso reader.NextResult() Then

                        While reader.Read()

                            Dim slug As String = reader.GetString(2)

                            If snap.HelpArticles.ContainsKey(slug) Then
                                Continue While
                            End If

                            Dim body As String = If(reader.IsDBNull(3), "", reader.GetString(3))

                            snap.HelpArticles(slug) = New HelpArticleInfo With {
                                .Url = ArticleRules.PageUrl(reader.GetString(0), reader.GetInt32(1), slug),
                                .Headings = ArticleToc.HeadingTexts(body)}

                        End While

                    End If

                End Using

            End Using
        End Using

        Return snap

    End Function

End Class
