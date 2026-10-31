' 記事の数と画像の量（使っている量と上限。AdminMaster.Master.vb から移した）
'
' 【出す場所は2つだけ】（ブログはこのサイトの主な機能ではないため）
'   ・記事一覧の上の件数の行（LimitLine）＝「13 件 / 上限 200 件　画像 12/50MB」
'   ・記事を書く画面の見出しの横（HeadingHtml）＝「記事 3/200　画像 0/50MB」（上限が無い項目は出さない）
' 「管理メニューの行の右端に、どの管理画面でも」は取り消した。
'
' 【DB は2回だけ、どちらも索引1本で済む】記事の数＝IX_Articles_Account_Status、
' 画像の合計＝台帳の IX_ArticleImageFiles_Account（INCLUDE (ByteSize)）。フォルダは舐めない。
'
' 【戻すのは、決まった文字と数だけ】<%= %>（エスケープしない形）で出してよい。外から来た値を混ぜないこと
Public NotInheritable Class ArticleUsage

    Private Sub New()
    End Sub


    ' 記事一覧の上の1行（文字だけ。画面が HtmlEncode して出す）。
    ' 【上限なし】件数は「13 件（上限なし）」、画像も「画像 12MB（上限なし）」（同じ行の中で、書き方をそろえる）。
    ' 【上限が読めない（RoleLimits のフェイルクローズで 0）】画像は出さない（「12/0MB」のような嘘の表示をしない）。
    ' 数えられなかったら空文字（見せるだけの値なので、ここで一覧を止めない。知らせだけ送る）
    Public Shared Function LimitLine(account As AccountStore.AccountRecord) As String

        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim countLimit As Integer = RoleLimits.GetInt(account.RoleKey, RoleLimits.KeyArticleMaxCount)
        Dim storageLimit As Integer = RoleLimits.GetInt(account.RoleKey, RoleLimits.KeyArticleStorageMaxBytes)

        Dim count As Integer
        Dim used As Long

        Try
            count = ArticleStore.CountByAccount(account.AccountId)
            used = ArticleImages.UsedBytes(account.AccountId)
        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleUsage.LimitLine")
            Return ""
        End Try

        Dim countText As String =
            If(countLimit = RoleLimits.Unlimited,
               count.ToString(inv) & " 件（上限なし）",
               count.ToString(inv) & " 件 / 上限 " & countLimit.ToString(inv) & " 件")

        Dim imageText As String = ""

        If storageLimit = RoleLimits.Unlimited Then
            imageText = "画像 " & MbNumber(used) & "MB（上限なし）"
        ElseIf storageLimit > 0 Then
            imageText = "画像 " & MbNumber(used) & "/" & MbNumber(storageLimit) & "MB"
        End If

        Return If(imageText.Length = 0, countText, countText & "　" & imageText)

    End Function


    ' 記事を書く画面の見出しの横。**上限が無い（無制限の）項目は出さない**＝サイト管理者には何も出ない
    Public Shared Function HeadingHtml(account As AccountStore.AccountRecord) As String

        If account Is Nothing Then
            Return ""
        End If

        Dim countLimit As Integer = RoleLimits.GetInt(account.RoleKey, RoleLimits.KeyArticleMaxCount)
        Dim storageLimit As Integer = RoleLimits.GetInt(account.RoleKey, RoleLimits.KeyArticleStorageMaxBytes)

        Dim showCount As Boolean = countLimit > 0
        Dim showStorage As Boolean = storageLimit > 0

        If Not showCount AndAlso Not showStorage Then
            Return ""
        End If

        Dim parts As New List(Of String)()
        Dim inv = Globalization.CultureInfo.InvariantCulture

        Try

            If showCount Then
                Dim count As Integer = ArticleStore.CountByAccount(account.AccountId)
                parts.Add("記事 " & count.ToString(inv) & "/" & countLimit.ToString(inv))
            End If

            If showStorage Then
                Dim used As Long = ArticleImages.UsedBytes(account.AccountId)
                parts.Add("画像 " & MbNumber(used) & "/" & MbNumber(storageLimit) & "MB")
            End If

        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleUsage.HeadingHtml")
            Return ""

        End Try

        ' 【読み上げ用に、何の数かを言葉で補う】「3/200」だけでは、耳では何の数か分からない
        Return "<p class=""admin-usage""><span class=""visually-hidden"">使っている量と上限：</span>" &
               String.Join("<span class=""admin-usage-sep"" aria-hidden=""true"">　</span>", parts) & "</p>"

    End Function


    ' バイトを MB の数に（小数1けたまで。0.05MB 未満は 0）
    Private Shared Function MbNumber(bytes As Long) As String
        Return (bytes / 1048576.0).ToString("0.#", Globalization.CultureInfo.InvariantCulture)
    End Function

End Class
