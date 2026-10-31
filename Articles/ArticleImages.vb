Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.IO
Imports System.Text.RegularExpressions

' 記事の画像の、本文との突き合わせ・容量・後片付け
'
' 【何を置くか】画像そのものの決まり（名前の形・置き場所・保存）は Core/SiteImage.vb（ImagePlace.Article）。
' ここに置くのは「記事の本文とのかかわり」だけ——本文の <img src> の形／そのアカウントが置いている量／
' どこにも使われなくなった画像を消すこと。
'
' 【台帳の形に変えた】それまでは「どの画像がどの記事に使われているかの表は作らず、
' 要るときに本文を読んで数える」形だった（記事の数は多くないので、それで足りる、という判断）。
' **記事と画像が増えるほど、毎晩の仕事と、画像を1枚上げるたびの待ち時間が、そのまま増える**ので、
' 表2本（ArticleImageFiles・ArticleImageUses）に事実を書いておく形に作り替えた。
' 表への出入りは Articles/ArticleImageLedger.vb。ここに残っているのは、
' **本文とのかかわり（src の形・名前の拾い出し）と、ファイルそのものの後片付け**だけ。
'
' 【本文を丸ごと読むのは、全体の突き合わせ（Rebuild）だけになった】ふだんの片付けは台帳の索引だけを見る
Public NotInheritable Class ArticleImages

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' 本文の <img src> に許す形（SiteImage.ArticleUrlPrefix ＋ 記事の画像の名前）
    '
    ' 【ArticleHtml（サニタイザー）と書く画面の JS が、同じ形を使う】JS へは ArticleHtml.AllowedJson の imageSrc で渡す。
    ' **ここを変えたら、SiteImage の ArticleNamePattern も見ること**（名前の形が同じでないと、貼った画像が保存で消える）
    Public Const SrcPatternText As String = "^/Uploads/articles/[1-9][0-9]{0,8}/[0-9a-f]{32}\.(?:jpg|png|gif)$"

    Private Shared ReadOnly SrcPattern As New Regex(SrcPatternText, RegexOptions.Compiled)

    ' 本文の中から、記事の画像の名前を拾う（属性の中の src="…" だけを見る）
    Private Shared ReadOnly SrcInHtml As New Regex(
        "src=""/Uploads/articles/([1-9][0-9]{0,8}/[0-9a-f]{32}\.(?:jpg|png|gif))""",
        RegexOptions.Compiled Or RegexOptions.IgnoreCase)


    ' src が、記事の画像の形か
    Public Shared Function IsArticleImageSrc(src As String) As Boolean
        Return Not String.IsNullOrEmpty(src) AndAlso SrcPattern.IsMatch(src)
    End Function


    ' 画像の名前から、本文に入れる src を作る
    Public Shared Function SrcOf(imageName As String) As String
        Return SiteImage.ArticleUrlPrefix & imageName
    End Function


    ' 本文に使われている記事の画像の名前（重なりなし）
    Public Shared Function NamesIn(html As String) As HashSet(Of String)

        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        If String.IsNullOrEmpty(html) Then
            Return names
        End If

        For Each m As Match In SrcInHtml.Matches(html)
            names.Add(m.Groups(1).Value.ToLowerInvariant())
        Next

        Return names

    End Function


    ' そのアカウントが今置いている画像の合計（バイト）
    '
    ' 【台帳の合計に変えた】それまではフォルダのファイルを1つずつ数えていた。
    ' **画像を1枚上げるたびに、そのアカウントの全ファイルを開いて大きさを足していた**ので、
    ' 置いている枚数が増えるほど、上げるときの待ち時間が伸びる形だった。
    ' 今は索引（IX_ArticleImageFiles_Account）の足し算1回で済む。
    '
    ' 【台帳とディスクがずれたら】1日1回の片付けが「行は在るのにファイルが無い」を消し、
    ' 「ファイルが在るのに行が無い」を知らせる（下の CleanupUnused）。直すのは Rebuild
    Public Shared Function UsedBytes(accountId As Integer) As Long

        Return ArticleImageLedger.UsedBytes(accountId)

    End Function


    ' 上げた画像を台帳へ載せる。載せられなければ False（呼ぶ側がファイルを消す）
    '
    ' 【ファイルを保存してから、台帳へ書く】逆にすると、保存に失敗したときに
    ' 「行は在るのにファイルが無い」が残る（合計に数えられ、片付けが消しにいって空振りする）。
    ' この向きなら、失敗しても残るのは「ファイルだけ」で、呼ぶ側がその場で消せる
    Friend Shared Function Register(imageName As String, accountId As Integer, byteSize As Long,
                                    width As Integer, height As Integer) As Boolean

        Try
            ArticleImageLedger.Add(imageName, accountId, byteSize, width, height)
            Return True
        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleImages.Register", imageName)
            Return False
        End Try

    End Function


    ' ============================================================
    ' 記事を削除したあとの後片付け
    ' ============================================================
    '
    ' **記事の行を消したあとに呼ぶ。** 使っている表（ArticleImageUses）の行は、外部キーの
    ' ON DELETE CASCADE で既に消えている（控えのぶんも一緒に）。
    ' だから、ここでやるのは「その記事に使われていた画像のうち、**もうどこからも使われていないもの**」を
    ' ファイルごと消すことだけである（同じ画像を2つの記事に貼っていたら、残る）。
    '
    ' 【ここだけは1日待たない】消した記事の画像だと分かっているため（前からの決まり）。
    ' 「本文から外しただけ」の画像を1日待つのは、1日1回の片付け（CleanupUnused）の役目
    '
    ' それまでは、そのアカウントの記事と控えの本文をすべて読み直していた。
    ' 今は台帳に聞くだけ（ArticleImageLedger.FilterUnused）
    '
    ' 【失敗しても、記事の削除は取り消さない】消せなかったファイルは、次の1日1回の片付けが拾う
    Public Shared Function CleanupAfterDelete(accountId As Integer, namesOfDeleted As HashSet(Of String)) As Integer

        If namesOfDeleted Is Nothing OrElse namesOfDeleted.Count = 0 Then
            Return 0
        End If

        Dim removed As Integer = 0

        For Each name As String In ArticleImageLedger.FilterUnused(namesOfDeleted)

            Dim fileExisted As Boolean

            If DeleteFileAndRow(name, fileExisted) Then
                removed += 1
            End If

        Next

        Return removed

    End Function


    ' ============================================================
    ' 退会したときに、そのアカウントの画像をフォルダごと消す
    ' ============================================================
    '
    ' 【台帳を1行も読まないのが、ここの肝】
    ' 退会は、その人の記事・控え・台帳の行を、DB の側で全部消してある
    ' （AccountStore.DeleteAccountCompletely）。**呼ばれる時点で、台帳には何も残っていない。**
    ' だから「どの画像が使われているか」を調べる必要が無く、
    ' Uploads/articles/<アカウントの番号>/ を丸ごと消せばよい。
    '
    ' 【1日1回の片付け（CleanupUnused）に任せない理由】
    ' あちらは台帳の行から画像を探すので、**行が消えた画像は、もう二度と見つけられない**
    ' （台帳に無いファイルは「数を知らせるだけで、消さない」形にしてある＝
    '   記事の画像は消えたら戻せないので、機械に任せないという決まり）。
    ' つまり、ここで消さないと**永久に残る。**
    '
    ' 【フォルダごと消してよい理由】このフォルダの下に在るのは、そのアカウントの記事の画像だけである
    ' （名前の形は SiteImage.NewArticleName＝アカウントの番号のフォルダ ＋ 32桁の GUID）。
    ' **プロフィールの画像は、別の根（Uploads/profiles/）に在る**——
    ' 同じフォルダに置いていたら、この1行がプロフィールの画像まで消していた
    ' （根を分けてあることが必須である、という話の3つ目の例）。
    '
    ' 【消せなくても、呼び出し側は止めない】残るだけで、DB からは切り離されている
    Friend Shared Sub DeleteAccountFolder(accountId As Integer)

        If accountId <= 0 Then
            Return
        End If

        Dim folder As String = SiteImage.ArticleFolderOf(accountId)

        If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then
            Return
        End If

        ' 【recursive: True】中のファイルごと消す。
        ' 例外は呼び出し側（Accounts/AccountRetire.vb）が捕まえて、知らせる
        Directory.Delete(folder, True)

    End Sub


    ' ============================================================
    ' 使われていない画像を、1日1回まとめて消す（台帳の形へ）
    ' ============================================================
    '
    ' 【なぜ要るのか】上の後片付けは「記事を削除したとき」にしか走らない。**記事を消さずに本文を貼り替えると、
    ' 本文から外れた画像がフォルダに残り、UsedBytes（＝ロールごとの上限 ArticleStorageMaxBytes）に
    ' 数えられ続ける**（本番の「使い方」を貼り替えて7枚が残ったことで表に出た）。
    '
    ' 【1日1回の仕事に相乗りさせている】（Core/LogCleanup.vb の4つ目）。**新しい仕事にしなかった理由**は、
    ' 問い合わせ（ContactMessages）を同じ仕事に入れたときと同じ——DailyJobs に仕事を足すと
    ' 「どの日付の分まで済んだか」の覚え書きが1つ増える。こちらは日付で区切る話ではない。
    '
    ' 【台帳の形に変えた】それまでは、フォルダの全ファイルと、全記事・全控えの本文を
    ' 丸ごと読んで突き合わせていた。**記事と画像が増えるほど、毎晩の仕事がそのまま増える**ので、
    ' 「どこからも使われていない・上げて1日以上たった画像」を索引で引くだけにした（本文は1行も読まない）。
    '
    ' 【1日待つ】貼ったばかりで、まだ保存（自動保存）されていない画像と、Ctrl+Z で戻した画像を消さないため。
    ' **この条件は外さないこと**
    '
    ' 【却下した形】
    '   ・保存（自動保存も含む）のたびに片付ける … 自動保存は打つのが止まって3秒ごとに走る
    '   ・上限に当たりそうなときだけ片付ける … 今の administrator は -1（無制限）なので**一度も走らない**
    '   ・UsedBytes を「本文に出てくる画像だけ」に変える … 上限の意味が
    '     「ディスクに置いている量」から「記事が参照している量」に変わる。ディスクを守るための上限である
    '   ・保存で本文から外れた瞬間に消す … Quill で画像を切って貼り直す・保存後に Ctrl+Z、で
    '     消したファイルを指す壊れた画像ができる。上の「1日待つ」安全弁をなくすことになる
    '
    ' 【ずれの扱い】ふだんは台帳だけを見るので、台帳とディスクがずれたら気づけない。そこで、ついでに見る。
    '   ・**行は在るのにファイルが無い** … 行を消す（ディスクは減らない＝取り返しのつく操作）。
    '     ただし、そのアカウントのフォルダごと見えないときは何もしない（一時的な障害でごっそり消さないため）
    '   ・**ファイルが在るのに行が無い** … **消さない。数を知らせるだけ**
    '     （記事の画像は消えたら戻せないので、機械に任せない）。直すのは /Admin/RebuildArticleImageLedger.aspx
    Friend Shared Function CleanupUnused() As ArticleImageCleanupResult

        Dim result As New ArticleImageCleanupResult()

        ' --- 1. どこからも使われていない・古い画像を消す（台帳の索引だけを引く） ---
        Dim unused As List(Of String) = ArticleImageLedger.FindUnused(UnusedGraceDays, MaxDeletePerRun)

        result.Scanned = unused.Count

        For Each name As String In unused

            Dim fileExisted As Boolean

            If Not DeleteFileAndRow(name, fileExisted) Then
                result.Failed += 1
            ElseIf fileExisted Then
                result.Files += 1
            Else
                ' ファイルは既に無かった（手で消された・前の回で消えていた）。行だけを片付けたことになる
                result.RowsWithoutFile += 1
            End If

        Next

        ' --- 2. 台帳とディスクのずれを見る ---
        Try
            CheckDrift(result)
        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleImages.CleanupUnused（ずれの確かめ）")
        End Try

        Return result

    End Function


    ' 上げてから、これだけたった画像だけを消す
    Friend Const UnusedGraceDays As Integer = 1

    ' 1回の片付けで消す枚数の上限（多すぎる日は、残りを次の日に回す）
    Private Const MaxDeletePerRun As Integer = 1000


    ' ファイルを消してから、台帳の行を消す
    '
    ' 【ファイルが既に無いときも、行は消す】（手で消された・前の回で消えていた）。
    ' 行だけ残ると、合計に数えられ続けて、毎晩ここへ来ることになる
    Private Shared Function DeleteFileAndRow(imageName As String, ByRef fileExisted As Boolean) As Boolean

        fileExisted = False

        Dim path As String = SiteImage.MapPath(imageName, ImagePlace.Article)

        If path Is Nothing Then
            Return False
        End If

        Try

            If IO.File.Exists(path) Then
                fileExisted = True
                IO.File.Delete(path)
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleImages.DeleteFileAndRow", imageName)
            Return False
        End Try

        Try
            Return ArticleImageLedger.Remove(imageName)
        Catch ex As Exception
            ErrorReport.Report(ex, "ArticleImages.DeleteFileAndRow（台帳）", imageName)
            Return False
        End Try

    End Function


    ' 台帳とディスクのずれを見る（上の【ずれの扱い】）
    Private Shared Sub CheckDrift(result As ArticleImageCleanupResult)

        Dim root As String = SiteImage.ArticleRootPath()

        If root Is Nothing OrElse Not Directory.Exists(root) Then
            Return
        End If

        Dim known As HashSet(Of String) = ArticleImageLedger.AllNames()
        Dim onDisk As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each folder As String In Directory.GetDirectories(root)

            Dim accountId As Integer = FolderAccountId(folder)

            If accountId <= 0 Then
                Continue For
            End If

            Dim prefix As String = accountId.ToString(Globalization.CultureInfo.InvariantCulture) & "/"

            For Each file As String In Directory.GetFiles(folder)

                Dim name As String = prefix & Path.GetFileName(file).ToLowerInvariant()

                ' 【名前の形の外のファイルには触らない】（手で置いたもの・web.config など）
                If SiteImage.IsValidName(name, ImagePlace.Article) Then
                    onDisk.Add(name)
                End If

            Next

        Next

        ' --- 行は在るのに、ファイルが無い（行を消す） ---
        For Each name As String In known

            If onDisk.Contains(name) Then
                Continue For
            End If

            ' 【そのアカウントのフォルダごと見えないときは、何もしない】
            ' 一時的な障害で、台帳をごっそり消さないため
            Dim folder As String = SiteImage.ArticleFolderOf(AccountIdOf(name))

            If folder Is Nothing OrElse Not Directory.Exists(folder) Then
                Continue For
            End If

            Try

                If ArticleImageLedger.Remove(name) Then
                    result.RowsWithoutFile += 1
                End If

            Catch ex As Exception
                ErrorReport.Report(ex, "ArticleImages.CheckDrift（ファイルの無い行）", name)
            End Try

        Next

        ' --- ファイルは在るのに、行が無い（消さずに知らせる） ---
        For Each name As String In onDisk

            If Not known.Contains(name) Then
                result.FilesWithoutRow += 1
            End If

        Next

        If result.FilesWithoutRow > 0 Then

            ErrorReport.ReportProblem("ArticleImages.CleanupUnused",
                "記事の画像で、ファイルは在るのに台帳に行が無いものが " & result.FilesWithoutRow &
                " 件あります。消していません（記事の画像は、消えたら戻せないため）。" &
                "/Admin/RebuildArticleImageLedger.aspx で台帳を作り直すと直ります。")

        End If

    End Sub


    ' ============================================================
    ' 全体の突き合わせ（/Admin/RebuildArticleImageLedger.aspx から）
    ' ============================================================
    '
    ' **ふだんは呼ばない。** 1日1回の片付けは台帳だけを見る（本文は読まない）。
    ' これを呼ぶのは次の2つのときだけ：
    '   ・台帳を作った日の、一度きりの手当て（それまでに在った画像を、台帳に載せる）
    '   ・「ファイルは在るのに台帳に行が無い」と知らされたとき
    '
    ' やること（この順）
    '   1. ディスクに在って台帳に無いファイルを、台帳へ載せる（**ファイルは消さない**）
    '   2. すべての記事と控えの本文から、使っている表を作り直す（本文1つずつ・差分だけ）
    ' **2を先にすると、1で載せたばかりの画像の「使っている」行が作られない**ので、順番を変えないこと
    Friend Shared Function Rebuild() As ArticleImageRebuildResult

        Dim result As New ArticleImageRebuildResult()

        ' --- 1. ディスクのファイルを台帳へ ---
        Dim root As String = SiteImage.ArticleRootPath()

        If root IsNot Nothing AndAlso Directory.Exists(root) Then

            Dim known As HashSet(Of String) = ArticleImageLedger.AllNames()

            For Each folder As String In Directory.GetDirectories(root)

                Dim accountId As Integer = FolderAccountId(folder)

                If accountId <= 0 Then
                    Continue For
                End If

                Dim prefix As String = accountId.ToString(Globalization.CultureInfo.InvariantCulture) & "/"

                For Each file As String In Directory.GetFiles(folder)

                    Dim name As String = prefix & Path.GetFileName(file).ToLowerInvariant()

                    If Not SiteImage.IsValidName(name, ImagePlace.Article) OrElse known.Contains(name) Then
                        Continue For
                    End If

                    Dim info As New FileInfo(file)

                    ' 【上げた時刻は、ファイルの更新時刻にする】台帳より前から在った画像を、
                    ' 「今上げたもの」として載せると、1日たつまで片付けの対象にならない。
                    ' 更新時刻なら、もともと古い画像は最初の片付けで拾われる
                    ArticleImageLedger.AddWithTime(name, accountId, info.Length, info.LastWriteTime)

                    result.Adopted += 1

                Next

            Next

        End If

        ' --- 2. 使っている表を作り直す ---
        For Each owner As ArticleBodyRow In ReadAllBodies()

            ArticleImageLedger.SyncUses(owner.ArticleId, owner.OwnerKind, NamesIn(owner.BodyHtml))
            result.Bodies += 1

        Next

        Return result

    End Function


    ' すべての記事と控えの本文（全体の突き合わせだけが呼ぶ）
    Private Shared Function ReadAllBodies() As List(Of ArticleBodyRow)

        Dim rows As New List(Of ArticleBodyRow)

        Using conn As New SqlConnection(ArticleImageLedger.ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT ArticleId, " & ArticleImageLedger.OwnerArticle & " AS OwnerKind, BodyHtml FROM Articles " &
                "UNION ALL " &
                "SELECT ArticleId, " & ArticleImageLedger.OwnerDraft & " AS OwnerKind, BodyHtml FROM ArticleDrafts;", conn)

                cmd.CommandTimeout = 120

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        rows.Add(New ArticleBodyRow With {
                            .ArticleId = reader.GetInt32(0),
                            .OwnerKind = CByte(reader.GetInt32(1)),
                            .BodyHtml = reader.GetString(2)})
                    End While
                End Using

            End Using
        End Using

        Return rows

    End Function


    ' 画像の名前（"2/0f3c….png"）の頭の、アカウントの番号
    Friend Shared Function AccountIdOf(imageName As String) As Integer

        If String.IsNullOrEmpty(imageName) Then
            Return 0
        End If

        Dim slash As Integer = imageName.IndexOf("/"c)

        If slash <= 0 Then
            Return 0
        End If

        Dim value As Integer

        If Integer.TryParse(imageName.Substring(0, slash), Globalization.NumberStyles.None,
                            Globalization.CultureInfo.InvariantCulture, value) Then
            Return value
        End If

        Return 0

    End Function


    ' フォルダの名前がアカウントの番号なら、その番号（違えば 0）
    '
    ' 【番号に直して書き戻したものと、1文字も違わないこと】まで見る——「007」を 7 と読むと、
    ' 名前は 7/… で組み立てるのにファイルは 007 の中、という食い違いができる
    Private Shared Function FolderAccountId(folder As String) As Integer

        Dim folderName As String = Path.GetFileName(folder)
        Dim accountId As Integer

        If Not Integer.TryParse(folderName, Globalization.NumberStyles.None,
                                Globalization.CultureInfo.InvariantCulture, accountId) OrElse accountId <= 0 Then
            Return 0
        End If

        If Not String.Equals(folderName, accountId.ToString(Globalization.CultureInfo.InvariantCulture),
                             StringComparison.Ordinal) Then
            Return 0
        End If

        Return accountId

    End Function

End Class


' ArticleImages.CleanupUnused の結果
'
' Scanned         … 台帳が「どこからも使われていない・古い」と答えた枚数
' Files           … そのうち、実際にファイルと台帳の行を消せた枚数
' Failed          … 消せなかった枚数（エラー報告メールが出ている）
' RowsWithoutFile … 台帳に行が在るのに、ファイルが無かったので行を消した数
' FilesWithoutRow … ファイルが在るのに、台帳に行が無かった数（**消していない。知らせるだけ**）
Friend NotInheritable Class ArticleImageCleanupResult
    Public Scanned As Integer
    Public Files As Integer
    Public Failed As Integer
    Public RowsWithoutFile As Integer
    Public FilesWithoutRow As Integer
End Class


' ArticleImages.Rebuild の結果
'
' Adopted … ディスクに在って台帳に無かったので、台帳へ載せた枚数
' Bodies  … 読み直した本文の数（記事＋控え）
Friend NotInheritable Class ArticleImageRebuildResult
    Public Adopted As Integer
    Public Bodies As Integer
End Class


' ReadAllBodies が返す1行
Friend NotInheritable Class ArticleBodyRow
    Public ArticleId As Integer
    Public OwnerKind As Byte
    Public BodyHtml As String
End Class
