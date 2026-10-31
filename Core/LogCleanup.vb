Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Threading

' 古いログの自動削除（実装の優先順位の14番。DailyJobs の仕事の1つ）
'
' 保存期間（SiteSettings.LogRetention。既定90日）を過ぎたログ（ChatMessages の行）と、
' 同じ期間を過ぎた画像（部屋の Uploads/images/<日付>/ と、こっそりの App_Data/private-images/<日付>/）を消す。
'
'
'   ・既定の保存期間は3か月（それまでの設計は1年）。Cookie ユーザーのサイトで3か月前の過去ログを読み返しに来る人はいない
'   ・画像もログと同じ期間で一緒に消す。消さないと、誰も辿り着けない画像だけが溜まり続ける（表示する過去ログが無い）
'   ・消したことを毎日の集計メールに載せる（黙って消えていくのがいちばん怖い。DailyReport が DailyJobs の LastResult を読む）
'
' 【消す順番：ログが先、画像があと】
' 画像を先に消すと、まだ残っている過去ログに穴が空く（絵が出ない）。ログを先に消せば、
' 失敗しても「誰にも見えない画像が1日多く残る」だけで済む。
'
' 【どの日の画像を消してよいか】（DeleteImagesKeepFrom）
' 「まだ残っているいちばん古いログの日付」と「保存期間の切れ目」の、早いほうの**前の日**より前だけを消す。
'   ・ログが消しきれていない日（下の時間の区切り）は、残っているログの日付が古いので、その日の画像は残る
'   ・前の日まで1日余分に残すのは、画像のフォルダの日付がアプリの時計、ログの日付が DB の時計で決まるから。
'     0時をまたいで送られた画像は、フォルダが前の日・ログが次の日になりうる
'
' 【1回で消しきれないとき：3分で区切り、残りは10分後に続ける】
' DailyJobs の作業中の印は10分。印を長くする形・次の日に回す形は、どちらも採らなかった。
'   ・印を長くする … 途中でアプリが止まると、その長さのあいだ誰もやり直せない
'   ・次の日に回す … 保存期間を短くした日など、溜まった量が多いと片付くまで何日もかかる
' 3分ぶん消して残りがあれば「やり直し」を返す（DailyJobs は印を外さず、期限が切れた10分後にまた呼ぶ）。
' 済んだ日付は、消しきった日にだけ進む。
'
' 【1回に1000行ずつ】（「ログ（ChatMessages）機能」の章で決めてあった数）
' SQL Server は1つの文で約5000の行ロックを取るとテーブルごとのロックに切り替える。1000行なら切り替わらず、
' チャットの書き込み（INSERT）を止めない。1回ごとに少し間を空けて、書き込みを先に通す。
'
' 【RoomId = 0 の行（種別50〜52。サイトへの出入り）も、同じ期間で消す】部屋のログではないが、同じ表の行で、
' 集計メールが読むのも前の日の分だけなので、残す理由が無い
'
' 【日付は DB の時計で決める】ChatMessages.CreatedAt が DB の時計で入っているため
'
' ============================================================
' 【問い合わせ（ContactMessages）も、ここで消す】
' ============================================================
' 保存期間は別（SiteSettings.ContactRetention。既定365日）で、ログより長い——
' 問い合わせは「対応の記録」として残す値打ちがあるため。それでも無期限にしないのは、
' **お名前・メールアドレス・IPアドレスという個人情報が入る表**だから。
'
' 【1日1回の仕事を、2つに分けなかった理由】
' DailyJobs に仕事を足すと「どの日付の分まで済んだか」の覚え書きが1つ増える。
' 問い合わせは1日に数件しかない表なので、同じ仕事の中で必ず消しきれる（区切る必要が無い）。
' そのため、ログのあと・画像のあとに、最後に1回消すだけにしてある。
'
' 【消した件数は、集計メールに載る】（BuildSummary）
' 「黙って消えていくのがいちばん怖い」はログと同じ
'
' ============================================================
' 【使われていない記事の画像も、ここで消す】
' ============================================================
' 記事の画像（Uploads/articles/<アカウントの番号>/）は、保存期間では消さない（記事は残るもの）。
' 消すのは「**どこからも使われていなくて、上げて1日以上たったファイル**」だけ。
'
' 【なぜ要るのか】それまで後片付けは「記事を削除したとき」にしか走らず、
' **記事を消さずに本文を貼り替えると、外れた画像がロールごとの上限（ArticleStorageMaxBytes）に
' 数えられ続けた**。中身・却下した形は Articles/ArticleImages.vb の CleanupUnused。
'
' 【台帳の形に変えた】それまでは、フォルダの全ファイルと全記事・全控えの本文を
' 丸ごと読んでいた（記事と画像が増えるほど、毎晩の仕事がそのまま増える形）。
' 今は表2本を索引で引くだけで、本文は読まない。
'
' 【1日1回の仕事を、増やさなかった理由】問い合わせと同じ——DailyJobs に仕事を足すと
' 「どの日付の分まで済んだか」の覚え書きが1つ増える。日付で区切る話ではないので、要らない。
'
' 【いちばん最後に置く】ログ → 日付の画像 → 問い合わせ → 記事の画像 → 期限が切れた仮登録。
' 画像を消す前にログを消しておくのと同じ考え方で、失敗しても前の結果を取り消さない
Friend NotInheritable Class LogCleanup
    Implements IDailyJob

    ' DailyJobs 表の JobName（あとから変えないこと。DailyReport がこの名前で結果を読む）
    Friend Const JobName As String = "LogCleanup"

    ' 1回の DELETE で消す行数（上の【1回に1000行ずつ】）
    Private Const BatchSize As Integer = 1000

    ' 1回の仕事で消し続ける時間の上限。**DailyJobs の作業中の印（10分）より十分短くしておくこと**
    ' （越えると、ほかのアプリに印を取られて2回走る）
    Private Shared ReadOnly TimeBudget As TimeSpan = TimeSpan.FromMinutes(3)

    ' DELETE と DELETE のあいだの間（ミリ秒）。チャットの書き込みを先に通すため
    Private Const PauseBetweenBatchesMs As Integer = 100

    ' 1回の DELETE を待つ時間（秒）
    Private Const CommandTimeoutSeconds As Integer = 60

    Private Const ConnectionName As String = "VbChatConnection"


    Public ReadOnly Property Name As String Implements IDailyJob.Name
        Get
            Return JobName
        End Get
    End Property


    ' fromDate・throughDate は使わない（消す範囲は、保存期間と今の時刻で決まる）
    Public Function Run(fromDate As Date, throughDate As Date) As DailyJobOutcome Implements IDailyJob.Run

        Dim retentionDays As Integer = RetentionDaysOf(SiteSettings.LogRetention)
        Dim retentionSeconds As Integer = CInt(Math.Min(Integer.MaxValue, Math.Max(86400.0, SiteSettings.LogRetention.TotalSeconds)))

        ' 切れ目：今（DB の時計）から保存期間を引いた日の0時。これより前の行を消す
        Dim cutoff As Date = ReadCutoffDate(retentionSeconds)

        ' --- ログ ---
        Dim started As DateTime = DateTime.Now
        Dim deletedRows As Integer = 0
        Dim finished As Boolean = False

        Do
            Dim n As Integer = DeleteBatch(cutoff)
            deletedRows += n

            If n < BatchSize Then
                finished = True
                Exit Do
            End If

            If DateTime.Now - started >= TimeBudget Then
                Exit Do
            End If

            Thread.Sleep(PauseBetweenBatchesMs)
        Loop

        ' --- 画像（ログのあと） ---
        Dim oldest As Date? = ReadOldestRemainingDate()
        Dim keepFrom As Date = DeleteImagesKeepFrom(cutoff, oldest)

        Dim roomImages As FolderCleanupResult = SiteImage.DeleteDayFoldersBefore(keepFrom, ImagePlace.Room)
        Dim privateImages As FolderCleanupResult = SiteImage.DeleteDayFoldersBefore(keepFrom, ImagePlace.PrivateTalk)

        ' --- 問い合わせ（ログ・画像のあと） ---
        '
        ' 【ログの区切り（3分）を使い切っていても、ここは必ず1回やる】
        ' 数件しかない表なので、1回の DELETE で済む。やらずに次の回へ回すと、
        ' ログが溜まっている間ずっと問い合わせが消えなくなる。
        '
        ' 【消せなくても、仕事は続ける】ここで例外を投げると、ログを消した結果まで
        ' 「やり直し」として扱われ、同じログを2回数えることになる。
        ' 知らせて（ErrorReport）、件数のかわりに理由を残す
        Dim contactRetentionDays As Integer = RetentionDaysOf(SiteSettings.ContactRetention)
        Dim contactPart As String

        Try

            Dim contactSeconds As Integer =
                CInt(Math.Min(Integer.MaxValue, Math.Max(86400.0, SiteSettings.ContactRetention.TotalSeconds)))

            Dim contactCutoff As Date = ReadCutoffDate(contactSeconds)
            Dim contactRows As Integer = DeleteContacts(contactCutoff)

            If contactRows > 0 Then
                contactPart = "問い合わせ " & contactRows & " 件を消した（" &
                              DailyReport.FormatDay(contactCutoff) & "より前。保存期間 " & contactRetentionDays & " 日）"
            Else
                contactPart = "消す問い合わせは無かった（" &
                              DailyReport.FormatDay(contactCutoff) & "より前。保存期間 " & contactRetentionDays & " 日）"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "LogCleanup.Run（問い合わせの削除）")
            contactPart = "問い合わせを消せなかった（エラーのメールを見ること）"
        End Try

        ' --- 使われていない記事の画像（いちばん最後） ---
        '
        ' 【区切った回でも、必ず1回やる】問い合わせと同じ理由。飛ばす形にすると、
        ' 古いログが溜まっている間ずっと画像が片付かない。
        ' 【、台帳（ArticleImageFiles・ArticleImageUses）を引くだけ】
        ' 本文（BodyHtml）は1行も読まない。ついでに台帳とディスクのずれも見る。
        ' 中身と、却下した形は Articles/ArticleImages.vb の CleanupUnused
        '
        ' 【消せなくても、仕事は続ける】ログを消した結果まで「やり直し」にしないため（問い合わせと同じ）
        Dim articlePart As String

        Try

            Dim unused As ArticleImageCleanupResult = ArticleImages.CleanupUnused()

            If unused.Files > 0 Then
                articlePart = "使われていない記事の画像 " & unused.Files & " 枚を消した"
            Else
                articlePart = "消す記事の画像は無かった"
            End If

            If unused.Failed > 0 Then
                articlePart &= "。" & unused.Failed & " 枚を消せなかった（エラーのメールを見ること）"
            End If

            ' 台帳とディスクのずれ。ふだんは両方とも0
            If unused.RowsWithoutFile > 0 Then
                articlePart &= "。台帳にだけ在った " & unused.RowsWithoutFile & " 件の行を消した"
            End If

            If unused.FilesWithoutRow > 0 Then
                articlePart &= "。台帳に無いファイルが " & unused.FilesWithoutRow &
                               " 枚ある（消していない。/Admin/RebuildArticleImageLedger.aspx で直す）"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "LogCleanup.Run（使われていない記事の画像の削除）")
            articlePart = "使われていない記事の画像を消せなかった（エラーのメールを見ること）"
        End Try

        ' --- 期限が切れた仮登録（一般ユーザーの登録） ---
        '
        ' 【区切った回でも、必ず1回やる】問い合わせ・記事の画像と同じ理由。
        ' 【なぜ、ここで消すのか】仮登録の行は「確認のメールのリンクを押さなかった申し込み」で、
        ' 期限（24時間）を過ぎたら、もう1バイトも使い道が無い。
        ' 残しておくと、**そのメールアドレスでの再登録が、5分の歯止め越しにしか通らない**
        ' （AccountStore.SaveRegistration が、同じアドレスの行を上書きする形なので）。
        ' 【消す量はごく少ない】1日に数件の表なので、区切らずに1回の DELETE で済ませる
        Dim registrationPart As String

        Try

            Dim expired As Integer = AccountStore.DeleteExpiredRegistrations()

            If expired > 0 Then
                registrationPart = "期限が切れた仮登録 " & expired & " 件を消した"
            Else
                registrationPart = "消す仮登録は無かった"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "LogCleanup.Run（期限が切れた仮登録の削除）")
            registrationPart = "期限が切れた仮登録を消せなかった（エラーのメールを見ること）"
        End Try

        ' --- 使った「使い捨ての目印」（Core/FormOnce.vb） ---
        '
        ' 【区切った回でも、必ず1回やる】問い合わせ・記事の画像・仮登録と同じ理由。
        ' 【別の Try で包む】1つにまとめると、どれか1つが壊れている間、残りが一度も動かない。
        ' 【集計メールには、消したときだけ載せる】毎日「消す目印は無かった」と出しても、読むところが増えるだけ
        ' （要約は400文字までで、もう余裕が少ない）
        Try

            Dim tokens As Integer = FormOnce.DeleteOld()

            If tokens > 0 Then
                registrationPart &= "／使い終わった送信の目印 " & tokens & " 件を消した"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "LogCleanup.Run（使った送信の目印の削除）")
            registrationPart &= "／送信の目印を消せなかった（エラーのメールを見ること）"
        End Try

        ' --- ゴミ箱の記事（移してから ArticleStore.TrashKeepDays 日たったもの） ---
        '
        ' 【1本ずつ ArticleStore.Delete を通す】画像の後片付け（ほかの記事が使っている画像は残す）も、ふだんの削除と同じ道。
        ' 【1回に 200 本まで】残りは次の日（ふつうはそんなに溜まらない）。
        ' 【別の Try で包む】【消したときだけ載せる】上と同じ理由
        Try

            Dim purged As Integer = ArticleStore.PurgeExpired(200)

            If purged > 0 Then
                articlePart &= "／ゴミ箱の記事 " & purged & " 本を完全に消した"
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "LogCleanup.Run（ゴミ箱の記事の削除）")
            articlePart &= "／ゴミ箱の記事を消せなかった（エラーのメールを見ること）"
        End Try

        Dim summary As String = BuildSummary(deletedRows, cutoff, retentionDays, keepFrom, roomImages, privateImages,
                                             contactPart, articlePart, registrationPart, finished)

        If finished Then
            Return DailyJobOutcome.Done(summary)
        End If

        ' 残りがある。印は外さずに、10分後に続きをやる（DailyJobs）
        Return DailyJobOutcome.Retry(summary)

    End Function


    ' どの日付より前の画像のフォルダを消してよいか（上の【どの日の画像を消してよいか】）
    '
    '   保存期間の切れ目と、まだ残っているいちばん古いログの日付の、早いほう … その日のログは残っている
    '   その前の日 … 0時をまたいだ画像のフォルダが、ここに入りうる
    '   それより前 … 消してよい
    Friend Shared Function DeleteImagesKeepFrom(cutoff As Date, oldestRemaining As Date?) As Date

        Dim keepBase As Date = cutoff

        If oldestRemaining.HasValue AndAlso oldestRemaining.Value < keepBase Then
            keepBase = oldestRemaining.Value
        End If

        Return keepBase.AddDays(-1)

    End Function


    ' 保存期間を日数で（メールと記録に書くため）
    Friend Shared Function RetentionDaysOf(retention As TimeSpan) As Integer
        Return CInt(Math.Floor(retention.TotalDays))
    End Function


    Private Shared Function ReadCutoffDate(retentionSeconds As Integer) As Date

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("SELECT CAST(DATEADD(SECOND, -@RetentionSeconds, SYSDATETIME()) AS DATE);", conn)

                cmd.Parameters.Add("@RetentionSeconds", SqlDbType.Int).Value = retentionSeconds
                conn.Open()

                Return CType(cmd.ExecuteScalar(), DateTime).Date

            End Using
        End Using

    End Function


    ' 古い行を最大 BatchSize 行消し、消した行数を返す
    '
    ' 【CreatedAt で絞る】索引 IX_ChatMessages_CreatedAt で引ける。古い行は主キーの先頭にもまとまっているので、
    ' どちらの読み方になっても、1000行を見つけるのに表全体を舐めない
    Private Shared Function DeleteBatch(cutoff As Date) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("DELETE TOP (@Batch) FROM ChatMessages WHERE CreatedAt < @Cutoff;", conn)

                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.Parameters.Add("@Batch", SqlDbType.Int).Value = BatchSize
                cmd.Parameters.Add("@Cutoff", SqlDbType.DateTime2).Value = cutoff

                conn.Open()

                ' SET NOCOUNT を付けていないので、消した行数が返る
                Return cmd.ExecuteNonQuery()

            End Using
        End Using

    End Function


    ' 古い問い合わせを消す
    '
    ' 【区切らずに1文で消す】1日に数件しかない表なので、ログのように TOP (1000) で刻む必要が無い
    ' （刻むと、この表のためだけに「残りがある」の受け渡しが増える）。
    ' 万一大量に溜まっていても、索引 IX_ContactMessages_CreatedAt で引けるので、表全体は舐めない。
    '
    ' 【フィルター付きの索引を張っていない】ので、SET QUOTED_IDENTIFIER は要らない
    ' （ChatMessages.ImageName で踏んだ罠を、この表では最初から避けてある）
    Private Shared Function DeleteContacts(cutoff As Date) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("DELETE FROM ContactMessages WHERE CreatedAt < @Cutoff;", conn)

                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.Parameters.Add("@Cutoff", SqlDbType.DateTime2).Value = cutoff

                conn.Open()

                Return cmd.ExecuteNonQuery()

            End Using
        End Using

    End Function


    ' まだ残っているいちばん古いログの日付（1行も無ければ Nothing）
    Private Shared Function ReadOldestRemainingDate() As Date?

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("SELECT CAST(MIN(CreatedAt) AS DATE) FROM ChatMessages;", conn)

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                If value Is Nothing OrElse value Is DBNull.Value Then
                    Return Nothing
                End If

                Return CType(value, DateTime).Date

            End Using
        End Using

    End Function


    ' DailyJobs の LastResult に残す一言（集計メールにそのまま載る。400文字まで）
    Private Shared Function BuildSummary(deletedRows As Integer, cutoff As Date, retentionDays As Integer, keepFrom As Date,
                                         roomImages As FolderCleanupResult, privateImages As FolderCleanupResult,
                                         contactPart As String, articlePart As String,
                                         registrationPart As String,
                                         finished As Boolean) As String

        Dim logPart As String

        If deletedRows > 0 Then
            logPart = "ログ " & deletedRows & " 行を消した（" & DailyReport.FormatDay(cutoff) & "より前。保存期間 " & retentionDays & " 日）"
        Else
            logPart = "消すログは無かった（" & DailyReport.FormatDay(cutoff) & "より前。保存期間 " & retentionDays & " 日）"
        End If

        Dim folders As Integer = roomImages.Folders + privateImages.Folders
        Dim imagePart As String

        If folders > 0 Then
            imagePart = "画像のフォルダ " & folders & " 日分・" & (roomImages.Files + privateImages.Files) & " 枚を消した（部屋 " &
                        roomImages.Folders & " 日分・こっそり " & privateImages.Folders & " 日分。" &
                        DailyReport.FormatDay(keepFrom) & "より前）"
        Else
            imagePart = "消す画像のフォルダは無かった（" & DailyReport.FormatDay(keepFrom) & "より前）"
        End If

        Dim failed As Integer = roomImages.Failed + privateImages.Failed

        If failed > 0 Then
            imagePart &= "。画像のフォルダ " & failed & " つを消せなかった（エラーのメールを見ること）"
        End If

        Dim summary As String = logPart & "／" & imagePart & "／" & contactPart & "／" & articlePart & "／" & registrationPart

        If Not finished Then
            summary &= "／まだ古いログが残っている（" & CInt(TimeBudget.TotalMinutes) & " 分で区切った。10分後に続きを消す）"
        End If

        Return summary

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
