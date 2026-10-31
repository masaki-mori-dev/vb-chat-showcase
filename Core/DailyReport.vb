Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.Text

' 1日1回の集計メール（DailyJobs の仕事の1つ）
'
' 前の日（アプリが寝ていた日があれば、その日も）の、ログイン・ラウンジ・入退室・発言などの数を、
' 1通のメールで info@（SiteSettings の MailTo）へ送る。件名は [vb-chat 集計] で始まる（エラーの [vb-chat エラー] と分ける）。
'
' 【材料は ChatMessages だけ】新しく記録を増やしていない。足した 50〜52（サイトへの出入り）と、
' 前からある入退室・発言の行を、日付で切って数える。
'
' 【日付は DB の時計で切る】ChatMessages.CreatedAt が DB の時計で入っているため（DailyJobs と同じ）。
'
' 【読み方：その日の行を、主キーの範囲で読む】（回に200万行で実測）
' CreatedAt の範囲でそのまま集計すると、索引 IX_ChatMessages_CreatedAt で行を見つけたあと、
' 1行ずつ主キーを引き直す（1日5,480行で論理読み取り 16,811）。
' その日の最初の MessageId と、次の日の最初の MessageId を、同じ索引で1つずつ引き、
' 主キーの範囲で読むと 152（約100分の1）。
' MessageId と CreatedAt は、どちらも INSERT のときに決まるので同じ順に並ぶ。ただし同時の INSERT で、
' 日付の境目の前後が数ミリ秒入れ替わりうるので、**範囲は前後1分ずつ広めに取り、CreatedAt の条件も残す**
'
' 【「人」とは書かない。「ブラウザ」と書く】（決めた方針）
' 身元は ChatBrowserId（ブラウザの Cookie）で、同じ人でも PC とスマホで2つ、ログアウトするたびに別になる。
' 出せない数字（同時に居た人数の最大・滞在時間／人の正確な数／初めて来た人か）は、
' 出せるかのように書かず、メールの末尾に理由と一緒に「出していないもの」として書く
'
' 【メールに入れないもの】ChatBrowserId（本人証明そのもの）・IP・発言の本文（こっそりは宛先の1人にしか見せない）。
' 名前とトリップは、部屋の中で誰にでも見えているものなので載せる
Friend NotInheritable Class DailyReport
    Implements IDailyJob

    ' 件名の先頭の印（メールソフトで振り分けるため。[vb-chat で始めること＝SiteMail.EncodeSubject の条件）
    Friend Const SubjectPrefix As String = "[vb-chat 集計] "

    ' 1通にまとめる日数の上限。アプリが長く寝ていた・表の日付がおかしい、などで範囲が長すぎるとき、
    ' 新しいほうからこの日数だけ出し、それより前は省略したと書く
    Private Const MaxDaysInOneMail As Integer = 31

    ' 名前の一覧に載せる数の上限（1日あたり）
    Private Const MaxPeopleListed As Integer = 30

    ' 「発言の多かった時間帯」に載せる数
    Private Const MaxHoursListed As Integer = 5

    ' 集計の問い合わせを待つ時間（秒）。1日1回・裏の作業なので、長めでよい
    Private Const QueryTimeoutSeconds As Integer = 120

    ' サイトへの出入り（ログイン・ラウンジ・ログアウト＝種別50〜52）を記録し始めた日（13-2a を本番へ出した日）。
    ' これより前の日は、その3つが0なのではなく「記録が無い」。0と書くと、誰も来なかったように読めてしまう
    Private Shared ReadOnly VisitLogStartDate As New Date(2026, 9, 15)

    Private Const ConnectionName As String = "VbChatConnection"


    ' DailyJobs 表の JobName（あとから変えないこと。変えると別の仕事として、昨日の分からやり直す）
    Public ReadOnly Property Name As String Implements IDailyJob.Name
        Get
            Return "DailyReport"
        End Get
    End Property


    Public Function Run(fromDate As Date, throughDate As Date) As DailyJobOutcome Implements IDailyJob.Run

        Dim rangeText As String = DailyJobs.FormatRange(fromDate, throughDate)

        ' 【送らない設定のときは、数えもしない】それでも「済んだ」を返す＝済んだ日付は進む
        ' （進めないと、1に戻した日に止めていた間の分がまとめて届く）
        If Not SiteSettings.DailyReportMailEnabled Then
            Return DailyJobOutcome.Done("集計メールを送らない設定（DailyReportMailEnabled = 0）なので、数えずに済ませた（" & rangeText & "）")
        End If

        ' 長すぎる範囲は、新しいほうから MaxDaysInOneMail 日だけにする
        Dim skippedFrom As Date? = Nothing
        Dim skippedThrough As Date? = Nothing

        If (throughDate - fromDate).Days + 1 > MaxDaysInOneMail Then
            skippedFrom = fromDate
            fromDate = throughDate.AddDays(-(MaxDaysInOneMail - 1))
            skippedThrough = fromDate.AddDays(-1)
        End If

        Dim visitLogLimit As Integer = SiteSettings.VisitLogDailyLimit

        Dim days As New List(Of DayStats)
        Dim day As Date = fromDate

        While day <= throughDate
            days.Add(LoadDay(day))
            day = day.AddDays(1)
        End While

        Dim subject As String = BuildSubject(days)
        Dim body As String = BuildBody(days, visitLogLimit, skippedFrom, skippedThrough)

        ' 【送れたかが分かってから、済んだことにする】SendAndWait は送り終わるまで待つ（ここは裏の作業の中）
        Select Case SiteMail.SendAndWait(subject, body)

            Case MailSendResult.Sent
                Return DailyJobOutcome.Done("送った（" & rangeText & "）")

            Case MailSendResult.Disabled
                ' メール全体を止めている。止めている間のメールは送り直さない約束（SiteMail）なので、済んだことにする
                Return DailyJobOutcome.Done("メール送信を止めている（MailEnabled = 0）ので、送らなかった（" & rangeText & "）")

            Case Else
                Return DailyJobOutcome.Retry("送れなかった（" & rangeText & "）：" & If(SiteMail.LastFailureText, "理由不明"))

        End Select

    End Function


    ' ============================================================
    '  1日ぶんを数える
    ' ============================================================

    ' 1本のバッチで、その日の行を一時表（#day）へ写し、そこから6つの結果を返す
    ' （ChatMessages を読むのは1回だけ。あとは一時表を数えるだけ）
    Private Shared ReadOnly DaySql As String =
        "SET NOCOUNT ON; " &
        "DECLARE @From DATETIME2(3) = CAST(@Day AS DATETIME2(3)); " &
        "DECLARE @To DATETIME2(3) = DATEADD(DAY, 1, @From); " &
        "DECLARE @FirstId BIGINT = (SELECT TOP (1) MessageId FROM ChatMessages " &
        "    WHERE CreatedAt >= DATEADD(MINUTE, -1, @From) ORDER BY CreatedAt, MessageId); " &
        "DECLARE @EndId BIGINT = (SELECT TOP (1) MessageId FROM ChatMessages " &
        "    WHERE CreatedAt >= DATEADD(MINUTE, 1, @To) ORDER BY CreatedAt, MessageId); " &
        "IF @EndId IS NULL SELECT @EndId = ISNULL(MAX(MessageId), 0) + 1 FROM ChatMessages; " &
        "IF @FirstId IS NULL SET @FirstId = @EndId; " &
        "SELECT MessageId, RoomId, MessageType, SenderBrowserId, Nickname, Trip, CreatedAt, " &
        "       CASE WHEN ImageName IS NOT NULL THEN 1 ELSE 0 END AS IsImage, " &
        "       CASE WHEN MessageText LIKE N'[[]stamp:%' THEN 1 ELSE 0 END AS IsStamp " &
        "INTO #day FROM ChatMessages " &
        "WHERE MessageId >= @FirstId AND MessageId < @EndId " &
        "  AND CreatedAt >= @From AND CreatedAt < @To; " &
        "SELECT COUNT(*), COUNT(DISTINCT SenderBrowserId) FROM #day; " &
        "SELECT MessageType, COUNT(*), COUNT(DISTINCT SenderBrowserId), SUM(IsImage), SUM(IsStamp) " &
        "  FROM #day GROUP BY MessageType; " &
        "SELECT " &
        "  (SELECT COUNT(DISTINCT v.SenderBrowserId) FROM #day v WHERE v.MessageType = 52 " &
        "     AND EXISTS (SELECT 1 FROM #day l WHERE l.MessageType = 50 AND l.SenderBrowserId = v.SenderBrowserId)), " &
        "  (SELECT COUNT(DISTINCT v.SenderBrowserId) FROM #day v WHERE v.MessageType = 52 " &
        "     AND NOT EXISTS (SELECT 1 FROM #day e WHERE e.MessageType = 20 AND e.SenderBrowserId = v.SenderBrowserId)); " &
        "SELECT d.RoomId, r.RoomName, " &
        "       SUM(CASE WHEN d.MessageType = 20 THEN 1 ELSE 0 END), " &
        "       COUNT(DISTINCT CASE WHEN d.MessageType = 20 THEN d.SenderBrowserId END), " &
        "       SUM(CASE WHEN d.MessageType = 10 THEN 1 ELSE 0 END), " &
        "       SUM(CASE WHEN d.MessageType = 11 THEN 1 ELSE 0 END), " &
        "       SUM(CASE WHEN d.MessageType = 40 THEN 1 ELSE 0 END) " &
        "  FROM #day d LEFT JOIN ChatRooms r ON r.RoomId = d.RoomId " &
        " WHERE d.RoomId <> 0 GROUP BY d.RoomId, r.RoomName ORDER BY d.RoomId; " &
        "SELECT TOP (@MaxHours) DATEPART(HOUR, CreatedAt) AS H, COUNT(*) AS C " &
        "  FROM #day WHERE MessageType IN (10, 11) " &
        " GROUP BY DATEPART(HOUR, CreatedAt) ORDER BY C DESC, H; " &
        "WITH b AS ( " &
        "  SELECT SenderBrowserId, MIN(CreatedAt) AS FirstAt, " &
        "         SUM(CASE WHEN MessageType IN (10, 11) THEN 1 ELSE 0 END) AS Posts, " &
        "         COUNT(DISTINCT CASE WHEN MessageType = 20 THEN RoomId END) AS Rooms, " &
        "         MAX(CASE WHEN MessageType = 50 THEN 1 ELSE 0 END) AS ViaLogin, " &
        "         MAX(MessageId) AS LastId " &
        "    FROM #day WHERE SenderBrowserId IS NOT NULL AND (MessageType < 40 OR MessageType >= 50) AND MessageType <> 63 " &
        "   GROUP BY SenderBrowserId) " &
        "SELECT TOP (@MaxPeople) d.Nickname, d.Trip, b.Posts, b.Rooms, b.ViaLogin, COUNT(*) OVER () " &
        "  FROM b INNER JOIN #day d ON d.MessageId = b.LastId " &
        " ORDER BY b.Posts DESC, b.FirstAt, b.LastId; " &
        "DROP TABLE #day;"


    Private Shared Function LoadDay(day As Date) As DayStats

        Dim stats As New DayStats With {.Day = day}

        Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString

        Using conn As New SqlConnection(connStr)
            Using cmd As New SqlCommand(DaySql, conn)

                cmd.CommandTimeout = QueryTimeoutSeconds
                cmd.Parameters.Add("@Day", SqlDbType.Date).Value = day
                cmd.Parameters.Add("@MaxHours", SqlDbType.Int).Value = MaxHoursListed
                cmd.Parameters.Add("@MaxPeople", SqlDbType.Int).Value = MaxPeopleListed

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    ' 1. その日の行の数・ブラウザの数
                    If reader.Read() Then
                        stats.AllRows = reader.GetInt32(0)
                        stats.AllBrowsers = reader.GetInt32(1)
                    End If

                    ' 2. 種別ごと
                    reader.NextResult()
                    While reader.Read()
                        stats.ByType(CInt(reader.GetByte(0))) = New TypeCount With {
                            .Rows = reader.GetInt32(1),
                            .Browsers = reader.GetInt32(2),
                            .Images = reader.GetInt32(3),
                            .Stamps = reader.GetInt32(4)
                        }
                    End While

                    ' 3. ラウンジに来たブラウザの内訳
                    reader.NextResult()
                    If reader.Read() Then
                        stats.LoungeViaLogin = reader.GetInt32(0)
                        stats.LoungeOnly = reader.GetInt32(1)
                    End If

                    ' 4. 部屋ごと
                    reader.NextResult()
                    While reader.Read()
                        stats.Rooms.Add(New RoomLine With {
                            .RoomId = reader.GetInt32(0),
                            .RoomName = If(reader.IsDBNull(1), Nothing, reader.GetString(1)),
                            .Joins = reader.GetInt32(2),
                            .JoinBrowsers = reader.GetInt32(3),
                            .Messages = reader.GetInt32(4),
                            .Privates = reader.GetInt32(5),
                            .FullRejects = reader.GetInt32(6)
                        })
                    End While

                    ' 5. 発言の多かった時間帯
                    reader.NextResult()
                    While reader.Read()
                        stats.Hours.Add(New KeyValuePair(Of Integer, Integer)(reader.GetInt32(0), reader.GetInt32(1)))
                    End While

                    ' 6. 来たブラウザの名前
                    reader.NextResult()
                    While reader.Read()
                        stats.People.Add(New PersonLine With {
                            .Nickname = reader.GetString(0),
                            .Trip = If(reader.IsDBNull(1), Nothing, reader.GetString(1)),
                            .Posts = reader.GetInt32(2),
                            .Rooms = reader.GetInt32(3),
                            .ViaLogin = reader.GetInt32(4) <> 0
                        })
                        stats.PeopleTotal = reader.GetInt32(5)
                    End While

                End Using

            End Using
        End Using

        Return stats

    End Function


    ' ============================================================
    '  メールの文面
    ' ============================================================

    Private Shared Function BuildSubject(days As List(Of DayStats)) As String

        Dim browsers As Integer = days.Sum(Function(s) s.AllBrowsers)
        Dim posts As Integer = days.Sum(Function(s) s.RowsOf(10) + s.RowsOf(11))

        Dim first As Date = days.First().Day
        Dim last As Date = days.Last().Day

        If days.Count = 1 Then
            Return SubjectPrefix & FormatDay(first) & " 来たブラウザ " & N(browsers) & "・発言 " & N(posts)
        End If

        Return SubjectPrefix & FormatSpan(first, last) & "（" & N(days.Count) & "日分） " &
               "来たブラウザ 延べ " & N(browsers) & "・発言 " & N(posts)

    End Function


    Private Shared Function BuildBody(days As List(Of DayStats), visitLogLimit As Integer,
                                      skippedFrom As Date?, skippedThrough As Date?) As String

        Dim sb As New StringBuilder()

        If days.Count = 1 Then
            sb.AppendLine("vb-chat の " & FormatDay(days(0).Day) & " の集計です。")
        Else
            sb.AppendLine("vb-chat の " & FormatSpan(days.First().Day, days.Last().Day) & " の集計です。")
            sb.AppendLine("アプリが寝ていた日の分を、まとめて1通にしています（日付ごとに分けてあります）。")
        End If

        If skippedFrom.HasValue Then
            sb.AppendLine("※ " & FormatSpan(skippedFrom.Value, skippedThrough.Value) & " の分は、長すぎるので省略しました（1通に " &
                          N(MaxDaysInOneMail) & " 日分まで）。")
        End If

        sb.AppendLine("数えているのは「人」ではなく「ブラウザ」です（末尾の「読み方」）。")

        For Each stats As DayStats In days
            sb.AppendLine()
            AppendDay(sb, stats, visitLogLimit)
        Next

        sb.AppendLine()
        AppendCleanupStatus(sb)

        AppendFooter(sb)

        Return sb.ToString()

    End Function


    ' 【古いログの自動削除】の欄（14番。理由：黙って消えていくのがいちばん怖い）
    '
    ' 消す仕事（LogCleanup）と、この集計は別の仕事なので、**DB の DailyJobs 表で受け渡す**
    ' （LogCleanup が LastResult に書いた一言を、ここで読んで載せる）。メモリで受け渡さないのは、
    ' 2つが別々のアプリ（入れ替わりで並んだ新旧）で動くことがあり、アプリが起き直すとメモリが消えるため。
    '
    ' 【載るのは「前の回」の結果】DailyJobs.Jobs の並びで、集計が先・削除があとに走る。
    ' そのため、ここに載るのは前の日（＝集計している日）に消した結果になる。
    '
    ' 【止まっていたら、そう書く】最後に動いてから2日を超えていたら、目立つ一言を添える。
    ' 消す仕事が例外で止まり続けていても、集計メールは毎日来るので、ここで気づける
    Private Shared Sub AppendCleanupStatus(sb As StringBuilder)

        Dim retentionDays As Integer = LogCleanup.RetentionDaysOf(SiteSettings.LogRetention)

        sb.AppendLine("【古いログの自動削除】（保存期間 " & N(retentionDays) & " 日。SiteSettings の LogRetentionSeconds）")

        Try

            Dim status As JobStatus = ReadJobStatus(LogCleanup.JobName)

            If Not status.LastRunAt.HasValue Then
                sb.AppendLine("　まだ一度も動いていません（この仕事は、集計メールのあとに動きます。結果は次の集計メールに載ります）")
                Return
            End If

            sb.AppendLine("　前回：" & status.LastRunAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) & "（DB の時計）")
            sb.AppendLine("　結果：" & If(status.LastResult, "（記録なし）"))

            Dim sinceDays As Double = (status.DbNow - status.LastRunAt.Value).TotalDays

            If sinceDays > 2 Then
                sb.AppendLine("　※ 最後に動いてから " & N(CInt(Math.Floor(sinceDays))) & " 日たっています。止まっていないか、SSMS の SELECT * FROM DailyJobs と、" &
                              "エラーのメールを見てください（アプリが何日も寝ていたときも、こう出ます）")
            End If

        Catch ex As Exception
            ' 読めなくても、集計メールそのものは送る
            ErrorReport.Report(ex, "DailyReport.AppendCleanupStatus")
            sb.AppendLine("　（古いログの削除の様子を読めませんでした。エラーのメールを見てください）")
        End Try

    End Sub


    ' DailyJobs の1行（その仕事が最後に動いた時刻と結果）と、DB の今の時刻
    Private Shared Function ReadJobStatus(jobName As String) As JobStatus

        ' 行が無くても1行返す形（まだ一度も動いていない仕事は、行そのものが無い）
        Const sql As String =
            "SELECT j.LastRunAt, j.LastResult, SYSDATETIME() " &
            "  FROM (SELECT 1 AS x) AS one LEFT JOIN DailyJobs AS j ON j.JobName = @JobName;"

        Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString

        Using conn As New SqlConnection(connStr)
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@JobName", SqlDbType.NVarChar, 50).Value = jobName
                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    reader.Read()

                    Return New JobStatus With {
                        .LastRunAt = If(reader.IsDBNull(0), CType(Nothing, DateTime?), reader.GetDateTime(0)),
                        .LastResult = If(reader.IsDBNull(1), Nothing, reader.GetString(1)),
                        .DbNow = reader.GetDateTime(2)
                    }

                End Using

            End Using
        End Using

    End Function


    Private NotInheritable Class JobStatus
        Public LastRunAt As DateTime?
        Public LastResult As String
        Public DbNow As DateTime
    End Class


    Private Shared Sub AppendDay(sb As StringBuilder, s As DayStats, visitLogLimit As Integer)

        sb.AppendLine("■ " & FormatDay(s.Day))

        If s.AllRows = 0 Then
            sb.AppendLine("　記録なし（ラウンジにも部屋にも、誰も来ていません）")
            Return
        End If

        ' --- サイトに来た ---
        sb.AppendLine()
        sb.AppendLine("【サイトに来た】")
        sb.AppendLine("　この日に何かの記録があったブラウザ：" & N(s.AllBrowsers))

        If s.Day < VisitLogStartDate Then

            sb.AppendLine("　（ログイン・ラウンジ・ログアウトの記録は " & FormatDay(VisitLogStartDate) &
                          " から始めたので、この日の分はありません）")

        Else

            If s.Day = VisitLogStartDate Then
                sb.AppendLine("　（ログイン・ラウンジ・ログアウトの記録は、この日の途中から始めています）")
            End If

            Dim lounge As Integer = s.BrowsersOf(52)

            sb.AppendLine("　ラウンジに来たブラウザ：" & N(lounge))
            sb.AppendLine("　　・この日にトップで名前を決めてから来た：" & N(s.LoungeViaLogin))
            sb.AppendLine("　　・トップを通らずに来た（前から名前を持っていた）：" & N(lounge - s.LoungeViaLogin))
            sb.AppendLine("　　　※「新しく来た／また来た」に近い数ですが、言葉どおりには読めません（末尾の「読み方」）")
            sb.AppendLine("　　・ラウンジに来て、部屋には入らなかった：" & N(s.LoungeOnly))
            sb.AppendLine("　ログインしたブラウザ（トップで名前を決めた）：" & N(s.BrowsersOf(50)))

            ' 【「そのうち部屋を開いたまま（37）」を、ここに並べない】
            ' 37 は 51 より前から書かれていて、上限に達した日も 37 だけは書かれるので、
            ' 「ログアウト 0（そのうち 2）」のような食い違いが起こりうる（開発機で実際に出た）。
            ' 37 は下の【部屋】の退室の内訳に出す
            sb.AppendLine("　ログアウトしたブラウザ（ラウンジのボタン）：" & N(s.BrowsersOf(51)))

            ' 1日の記録の上限（SiteSettings.VisitLogDailyLimit）に並んでいたら、それ以上は数えていない
            Dim capped As New List(Of String)
            If s.BrowsersOf(50) >= visitLogLimit Then capped.Add("ログイン")
            If s.BrowsersOf(51) >= visitLogLimit Then capped.Add("ログアウト")
            If lounge >= visitLogLimit Then capped.Add("ラウンジに来た")

            If capped.Count > 0 Then
                sb.AppendLine("　※ " & String.Join("・", capped) & " の記録が、1日の上限（" & N(visitLogLimit) &
                              " ブラウザ。SiteSettings の VisitLogDailyLimit）に達しています。それ以上は数えていません")
            End If

        End If

        ' --- 部屋 ---
        sb.AppendLine()
        sb.AppendLine("【部屋】")

        Dim joinRows As Integer = s.RowsOf(20)
        Dim replaced As Integer = s.RowsOf(33)

        sb.AppendLine("　部屋に入ったブラウザ：" & N(s.BrowsersOf(20)) &
                      "（入室 " & N(joinRows) & " 回" &
                      If(replaced > 0, "。うち " & N(replaced) & " 回は、同じブラウザの別の窓が前の窓と入れ替わったもの", "") & "）")
        sb.AppendLine("　満員で断った：" & N(s.RowsOf(40)) & " 回")
        sb.AppendLine("　パスワードで断った：" & N(s.RowsOf(41)) & " 回（同じ人は10分に1回だけ数えます）")
        ' ノック。記録は満員・パスワードと同じ歯止めを通すので、行の数は回数そのものではない。
        '
        ' 【本体と行く末を揃えて書く】（B。理由：数が合わない表は読まれない）
        ' ぶんは「ノック 8 回／許可 4／断った 3／返事が無かった 4／入らなかった 1」で、内訳の合計が本体より多かった
        ' （行く末が、種別ごとに別々の10分の歯止めで書かれていたため）。今は、記録するかをノック1回につき1回だけ決め、
        ' 行く末もそれに従う（ChatHub.Knock.vb の KnockLogTypeIf）。だから：
        '   ・許可＋断った＋返事が無かった ≦ ノック。残りは「そのほか」（取り消し・空いた席から先に入った・ログアウト）
        '   ・入らなかった（46）は、許可（43）の内訳（許可されたノックのその先）。1回のノックで両方書かれるので、横に並べて足さない
        ' 【例外】日をまたいだノック（23:59 にノックして、行く末が翌日）は、行く末が次の日に数えられる。
        ' そのときだけ「そのほか」が負になりうるので、負なら数を出さずに一言添える。
        ' 以前の行（歯止めが種別ごとだった頃）も、同じ理由で噛み合わないことがある
        Dim knocks As Integer = s.RowsOf(42)
        Dim knockApproved As Integer = s.RowsOf(43)
        Dim knockDenied As Integer = s.RowsOf(44)
        Dim knockExpired As Integer = s.RowsOf(45)
        Dim knockNotEntered As Integer = s.RowsOf(46)

        If knocks + knockApproved + knockDenied + knockExpired + knockNotEntered > 0 Then

            Dim knockOther As Integer = knocks - knockApproved - knockDenied - knockExpired

            sb.AppendLine("　ノック：" & N(knocks) & " 回（ノックしたブラウザ " & N(s.BrowsersOf(42)) &
                          "。同じ人は10分に1回だけ数え、そのノックの行く末も一緒に数えます）")
            sb.AppendLine("　　その行く末：許可 " & N(knockApproved) & "（うち、入らなかった " & N(knockNotEntered) & "）" &
                          "／断った " & N(knockDenied) & "／返事が無かった " & N(knockExpired) &
                          If(knockOther >= 0,
                             "／そのほか " & N(knockOther) & "（取り消された・空いた席から先に入った・ログアウトした など）",
                             ""))

            If knockOther < 0 Then
                sb.AppendLine("　　※ 前の日の終わりにノックした分の行く末が、この日に入っています（行く末は、決まった日に数えます）")
            End If

        End If
        sb.AppendLine("　退室：退室ボタン・部屋の移動 " & N(s.RowsOf(30)) &
                      "／閉じた・切れた " & N(s.RowsOf(31)) &
                      "／無言で退室させた " & N(s.RowsOf(32)) &
                      "／別の窓と入れ替わり " & N(replaced) &
                      "／部屋を開いたままログアウト " & N(s.RowsOf(37)))

        ' 管理人の道具。1つも無い日は出さない（ノックの行と同じ）。
        ' 34・38・62 は管理人が押した回数そのもの（歯止めを通さない）。47 は満員と同じ歯止め（同じ人は10分に1回）。
        ' 63（解除）は部屋のログに出さないが、ここには出す（禁止のしすぎ・間違いに気づけるように）。
        ' 63 の記録する人は「解除された人」で、その日に来ていないことがあるので、人の一覧（DaySql の最後）からは外してある
        Dim ownerKicks As Integer = s.RowsOf(34)
        Dim ownerBans As Integer = s.RowsOf(38)
        Dim bannedRejects As Integer = s.RowsOf(47)
        Dim handOvers As Integer = s.RowsOf(62)
        Dim banLifts As Integer = s.RowsOf(63)

        If ownerKicks + ownerBans + bannedRejects + handOvers + banLifts > 0 Then
            sb.AppendLine("　管理人の道具：退室させた " & N(ownerKicks) &
                          "／入室禁止にした " & N(ownerBans) &
                          "（解除 " & N(banLifts) & "）" &
                          "／入室禁止で断った " & N(bannedRejects) & "（同じ人は10分に1回だけ数えます）" &
                          "／管理人をゆずった " & N(handOvers))
        End If

        Dim messages As TypeCount = s.CountOf(10)
        Dim privates As TypeCount = s.CountOf(11)

        sb.AppendLine("　発言：" & N(messages.Rows) & " 件（うち画像 " & N(messages.Images) & "・スタンプ " & N(messages.Stamps) &
                      "）／発言したブラウザ " & N(messages.Browsers))
        sb.AppendLine("　こっそり：" & N(privates.Rows) & " 件（うち画像 " & N(privates.Images) & "・スタンプ " & N(privates.Stamps) &
                      "）／送ったブラウザ " & N(privates.Browsers))

        If s.Rooms.Count > 0 Then
            sb.AppendLine("　部屋ごと：")
            For Each room As RoomLine In s.Rooms
                sb.AppendLine("　　" & If(room.RoomName, "部屋 " & N(room.RoomId)) &
                              "　入室 " & N(room.Joins) & " 回（" & N(room.JoinBrowsers) & " ブラウザ）" &
                              "・発言 " & N(room.Messages) &
                              "・こっそり " & N(room.Privates) &
                              If(room.FullRejects > 0, "・満員で断った " & N(room.FullRejects), ""))
            Next
        End If

        If s.Hours.Count > 0 Then
            sb.AppendLine("　発言の多かった時間帯（こっそりを含む）：" &
                          String.Join("／", s.Hours.Select(Function(h) N(h.Key) & "時台 " & N(h.Value))))
        End If

        ' --- 名前 ---
        If s.People.Count > 0 Then

            sb.AppendLine()
            sb.AppendLine("【来たブラウザの名前】（発言の多い順。" &
                          If(s.PeopleTotal > s.People.Count,
                             "全部で " & N(s.PeopleTotal) & " のうち " & N(s.People.Count) & " まで",
                             "全部で " & N(s.PeopleTotal)) & "）")

            For Each person As PersonLine In s.People

                Dim notes As New List(Of String)
                notes.Add("発言 " & N(person.Posts))

                ' 「入った部屋」は、その日の入室の行（20）から数える。
                ' 日付をまたいで部屋に居た人は、入室の行が無いまま発言している（開発機で実際に出た）。
                ' そこを「部屋には入らず」と書くと嘘になるので、発言があれば分けて書く
                If person.Rooms > 0 Then
                    notes.Add("入った部屋 " & N(person.Rooms))
                ElseIf person.Posts > 0 Then
                    notes.Add("この日の入室なし（前の日から部屋に居た）")
                Else
                    notes.Add("部屋には入らず")
                End If

                If person.ViaLogin Then notes.Add("この日に名前を決めた")

                sb.AppendLine("　" & person.Nickname &
                              If(String.IsNullOrEmpty(person.Trip), "", "［" & person.Trip & "］") &
                              "　" & String.Join("・", notes))
            Next

        End If

    End Sub


    Private Shared Sub AppendFooter(sb As StringBuilder)

        sb.AppendLine()
        sb.AppendLine("――――――――――――――――")
        sb.AppendLine("■ 読み方")
        sb.AppendLine("・「ブラウザ」で数えています。身元はブラウザの Cookie（ChatBrowserId）なので、同じ人でも PC とスマホなら2つ、")
        sb.AppendLine("　ログアウトするたびに別のブラウザとして数えます。")
        sb.AppendLine("・「この日にトップで名前を決めてから来た／トップを通らずに来た」は、アクセス解析の「新しく来た／また来た」に")
        sb.AppendLine("　近い数ですが、次の3つの点で言葉どおりではありません。")
        sb.AppendLine("　　① 数えているのはブラウザ（同じ人の PC とスマホは、別々に数える）")
        sb.AppendLine("　　② Cookie を消した人は、前から来ている人でも「名前を決めてから来た」側に入る")
        sb.AppendLine("　　③ ログアウトした人（名前を変えるときもログアウトする）も、「名前を決めてから来た」側に入る")
        sb.AppendLine("　　　（ログアウトすると、同じ名前でも別のブラウザとして数え直すため）")
        sb.AppendLine("　②と③は、また来た人を「名前を決めてから来た」側へ寄せます。「トップを通らずに来た」は、")
        sb.AppendLine("　「また来てくれた人」の目安としては少なめに出る数、と読むのが安全です。")
        sb.AppendLine("・ログイン・ラウンジに来た・ログアウトは、1つのブラウザにつき1日1回だけ記録しています。")
        sb.AppendLine("・「無言で退室させた」は、最後の発言から決まった時間（SiteSettings の SilentTimeoutSeconds）が過ぎて退室させたもの。")
        sb.AppendLine()
        sb.AppendLine("■ このメールで出していないもの")
        sb.AppendLine("・同時に居た人数の最大と、滞在時間")
        sb.AppendLine("　… アプリが再起動すると、そのとき部屋に居た人の退室の行が書かれません。入室と退室の行から組み立てると、")
        sb.AppendLine("　　居ないはずの人が居続けることになり、間違った数になります。")
        sb.AppendLine("・「人」の正確な数")
        sb.AppendLine("　… 身元はブラウザ単位で、アカウントがまだ無いため、同じ人かどうかを確かめる手段がありません。")
        sb.AppendLine("・初めて来た人かどうか")
        sb.AppendLine("　… 過去のログを全部（最大1年分）突き合わせる必要があり、毎日の集計には重すぎます。")
        sb.AppendLine("　　代わりに上の「名前を決めてから来た／トップを通らずに来た」を出しています。")
        sb.AppendLine()
        sb.AppendLine("集計した時刻（サーバーの時計）：" &
                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        sb.AppendLine("ビルドの目印：" & SafeBuildId())
        sb.AppendLine("このメールを止めるには：SiteSettings の DailyReportMailEnabled を 0 にして、アプリを再起動させる")

    End Sub


    ' ============================================================
    '  書き方の小道具
    ' ============================================================

    ' 「」
    '
    ' 【ToString("yyyy年…") を使わない】共用サーバーのカルチャに和暦が入っていると、年が和暦になる
    ' （ChatLogReader が日付を自前で連結しているのと同じ理由）
    Friend Shared Function FormatDay(day As Date) As String
        Return day.Year.ToString(CultureInfo.InvariantCulture) & "年" &
               day.Month.ToString(CultureInfo.InvariantCulture) & "月" &
               day.Day.ToString(CultureInfo.InvariantCulture) & "日（" &
               "日月火水木金土".Substring(CInt(day.DayOfWeek), 1) & "）"
    End Function


    ' 「」「」「〜」
    Private Shared Function FormatSpan(first As Date, last As Date) As String

        Dim head As String = first.Year & "年" & first.Month & "月" & first.Day & "日"

        If first.Year <> last.Year Then
            Return head & "〜" & last.Year & "年" & last.Month & "月" & last.Day & "日"
        End If

        If first.Month <> last.Month Then
            Return head & "〜" & last.Month & "月" & last.Day & "日"
        End If

        Return head & "〜" & last.Day & "日"

    End Function


    ' 数を書く（カルチャに頼らない）
    Private Shared Function N(value As Integer) As String
        Return value.ToString(CultureInfo.InvariantCulture)
    End Function


    Private Shared Function SafeBuildId() As String
        Try
            Return common.BuildId()
        Catch
            ' 確認用の値なので、取れなくても集計は送る（ErrorReport と同じ扱い）
            Return "（取れませんでした）"
        End Try
    End Function


    ' ============================================================
    '  1日ぶんの数
    ' ============================================================

    Private NotInheritable Class DayStats

        Public Day As Date
        Public AllRows As Integer
        Public AllBrowsers As Integer
        Public ReadOnly ByType As New Dictionary(Of Integer, TypeCount)
        Public LoungeViaLogin As Integer
        Public LoungeOnly As Integer
        Public ReadOnly Rooms As New List(Of RoomLine)
        Public ReadOnly Hours As New List(Of KeyValuePair(Of Integer, Integer))
        Public ReadOnly People As New List(Of PersonLine)
        Public PeopleTotal As Integer

        ' その種別の数（無ければ全部0）
        Public Function CountOf(messageType As Integer) As TypeCount
            Dim found As TypeCount = Nothing
            If ByType.TryGetValue(messageType, found) Then
                Return found
            End If
            Return New TypeCount()
        End Function

        Public Function RowsOf(messageType As Integer) As Integer
            Return CountOf(messageType).Rows
        End Function

        ' その種別の行があったブラウザの数（50〜52 は、アプリが起き直すと同じブラウザが2行になりうるので、こちらで数える）
        Public Function BrowsersOf(messageType As Integer) As Integer
            Return CountOf(messageType).Browsers
        End Function

    End Class

    Private NotInheritable Class TypeCount
        Public Rows As Integer
        Public Browsers As Integer
        Public Images As Integer
        Public Stamps As Integer
    End Class

    Private NotInheritable Class RoomLine
        Public RoomId As Integer
        Public RoomName As String
        Public Joins As Integer
        Public JoinBrowsers As Integer
        Public Messages As Integer
        Public Privates As Integer
        Public FullRejects As Integer
    End Class

    Private NotInheritable Class PersonLine
        Public Nickname As String
        Public Trip As String
        Public Posts As Integer
        Public Rooms As Integer
        Public ViaLogin As Boolean
    End Class

End Class
