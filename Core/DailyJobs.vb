Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Threading

' 1日1回の仕事を回す仕組み
'
' 【「仕事」と「きっかけ」を分けてある】
'   ・仕事   … 何をするか。IDailyJob を実装したクラス（集計メールの DailyReport と、古いログの削除の LogCleanup）。
'              「どの日付からどの日付までの分をやってくれ」と渡され、終わったか・やり直すかを返す
'   ・きっかけ … いつやるか。見回りのタイマー（Global.asax、2秒おき）が Tick を呼ぶ。
'              届く時刻はバラバラでよい（開発者の判断）。共用サーバーには定期実行の機能が無く、
'              アプリが寝ている間は何も動かないので、「起きていて、日付が変わったあと最初に見回ったとき」になる
'
' 【どの日付まで済んだかは、DB の表（DailyJobs）が覚える】（Database/CreateTable/DailyJobs.sql）
' 「前回から24時間」にしない。アプリが何日寝ていても、起きたときに抜けた日をまとめて渡せる。
'
' 【同じ仕事を2回走らせないのは、DB の「作業中の印」】（TryClaim）
' IIS はアプリを入れ替えるとき、新旧のアプリを一瞬並べて動かす。メモリのロックはそれぞれの中でしか効かない。
' UPDATE 1本で印（LeaseUntil・LeaseToken）を取れた1本だけが働く。
' 済んだら「済んだ日付」を書いて印を外す。**失敗したら印は外さない**＝期限（10分）が切れたら、どれかのアプリがやり直す。
'
' 【タイマーの中では働かない】
' タイマーは2秒おきで、前の回が終わる前に次の回が来ることもある。Tick は「そろそろ見に行く時刻か」を
' メモリの時刻と比べるだけで、見に行くときは裏の作業（SerialRunner。1本だけ・順番に）に頼んですぐ戻る。
' DB を見に行くのは、アプリが起きて1分後の1回と、そのあとは「DB の時計の次の0時の1分後」だけ
' （うまくいかなかったときは10分おき）。DB を2秒おきに叩くことはない。
'
' 【仕事を足すとき（14番の古いログの削除など）】
' IDailyJob を実装したクラスを作り、下の Jobs に1つ足すだけ。表の行は、最初に見に行ったときにここが作る
' （手で行を入れる形にしないのは、入れ忘れたとき黙って一度も動かないため）。
'
' 【例外は外へ出さない】ここはタイマーから呼ばれる（漏れるとプロセスごと落ちる）。
' 仕事で起きた例外は ErrorReport へ知らせ、作業中の印は外さずに、10分後にやり直す。
' SerialRunner は仕事の例外を Trace にしか書かない（ErrorReport 自身が同じ道具で動いているため）ので、
' 知らせるのはここの役目である
Public NotInheritable Class DailyJobs

    Private Sub New()
    End Sub

    ' 仕事の一覧。**仕事を足すときは、ここに1つ足す**（VB の初期化子の中にコメント行を置かないこと。ビルドが止まる）
    '
    ' 【並びに意味がある：集計メール（DailyReport）が先、古いログの削除（LogCleanup）があと】（14番）
    ' 同じ見回りの中で、この順に1つずつ回る。
    '   ・集計が先なら、アプリが寝ていた日の分をまとめて集計するときも、数える前にログが消えることが無い
    '     （保存期間を短くした日でも、昨日の分は必ず数えてから消す）
    '   ・集計メールに載る「古いログの削除」の結果は、その前の回（＝前の日）のもの。
    '     集計する日と、消した日が一致する（「昨日、古いログを○行消しました」）
    Private Shared ReadOnly Jobs As IDailyJob() = {New DailyReport(), New LogCleanup()}

    ' 作業中の印の期限（秒）。仕事がこれより長くかかると、別のアプリに取られて2回走りうる。
    ' 集計メールは数秒で終わる（DB の問い合わせ数本と、SMTP の15秒の待ち時間が上限）。
    ' 古いログの削除は、自分で3分に区切り、残りは「やり直し」で10分後に続ける（LogCleanup.TimeBudget）。
    ' **仕事ごとに印の長さを変える形にはしていない**（長い印は、途中でアプリが止まったとき、その間だれもやり直せない）
    Private Const LeaseSeconds As Integer = 600

    ' うまくいかなかったとき（印が取れない・DB に繋がらない・送れなかった）に、次に見に行くまでの間
    Private Shared ReadOnly RetryInterval As TimeSpan = TimeSpan.FromSeconds(LeaseSeconds)

    ' アプリが起きてから、最初に見に行くまでの間（起きた直後の要求の邪魔をしない）
    Private Shared ReadOnly FirstCheckDelay As TimeSpan = TimeSpan.FromMinutes(1)

    ' DB の時計の0時を過ぎてから、見に行くまでの間（アプリと DB の時計の小さなずれを吸収する）
    Private Shared ReadOnly AfterMidnightMargin As TimeSpan = TimeSpan.FromMinutes(1)

    ' 次に DB を見に行く時刻（アプリの時計。Ticks を Interlocked で読み書きする）
    '
    ' 【この行は、上の FirstCheckDelay より後ろに置くこと】Shared の初期化は書いた順に走る
    Private Shared _nextCheckTicks As Long = DateTime.Now.Add(FirstCheckDelay).Ticks

    Private Shared ReadOnly Runner As New SerialRunner(AddressOf RunDueJobs, "DailyJobs")

    Private Const ConnectionName As String = "VbChatConnection"

    ' DB の LastResult 列の幅（DailyJobs.sql と同じ数にしておくこと。長いと UPDATE が落ちる）
    Private Const LastResultMaxLength As Integer = 400


    ' 見回りのタイマー（Global.asax の OnTimeoutCheckTick。2秒おき）から呼ぶ
    '
    ' 時刻を比べるだけですぐ戻る。見に行く時刻なら、次の時刻を先に「10分後」へ進めてから裏の作業に頼む
    ' （2秒後のタイマーが、もう一度頼まないように。裏の作業が終わったら、本当の次の時刻に直す）
    Public Shared Sub Tick()

        Dim nowTicks As Long = DateTime.Now.Ticks
        Dim dueTicks As Long = Interlocked.Read(_nextCheckTicks)

        If nowTicks < dueTicks Then
            Return
        End If

        ' 2つのタイマーの回が同時に来たら、1つだけが頼む
        If Interlocked.CompareExchange(_nextCheckTicks, nowTicks + RetryInterval.Ticks, dueTicks) <> dueTicks Then
            Return
        End If

        Runner.Kick()

    End Sub


    ' 仕事を1つずつ見て、やるべき分があればやる（裏の作業）
    Private Shared Sub RunDueJobs()

        Dim nextCheck As DateTime = DateTime.MaxValue

        For Each job As IDailyJob In Jobs

            Dim jobNext As DateTime

            Try
                jobNext = RunOne(job)
            Catch ex As Exception
                ' DB に繋がらない、など（仕事そのものの例外は RunOne の中で知らせてある）
                ErrorReport.Report(ex, "DailyJobs." & job.Name)
                jobNext = DateTime.Now.Add(RetryInterval)
            End Try

            If jobNext < nextCheck Then
                nextCheck = jobNext
            End If

        Next

        If nextCheck = DateTime.MaxValue Then
            nextCheck = DateTime.Now.Add(RetryInterval)
        End If

        ' 【少なくとも1分は空ける】次の時刻が何かの理由で過去になっても（DB とアプリの時計が大きくずれた、など）、
        ' 2秒おきに DB を叩き続けないため
        Dim earliest As DateTime = DateTime.Now.AddMinutes(1)

        If nextCheck < earliest Then
            nextCheck = earliest
        End If

        Interlocked.Exchange(_nextCheckTicks, nextCheck.Ticks)

    End Sub


    ' 仕事を1つ回し、次にこの仕事を見に行く時刻（アプリの時計）を返す
    Private Shared Function RunOne(job As IDailyJob) As DateTime

        Dim token As Guid = Guid.NewGuid()
        Dim claim As ClaimResult = TryClaim(job.Name, token)

        ' DB の時計と、アプリの時計の差。次の0時をアプリの時計に直すのに使う
        Dim clockOffset As TimeSpan = claim.DbNow - claim.LocalAtClaim
        Dim nextDbMidnight As DateTime = claim.DbNow.Date.AddDays(1)
        Dim nextByMidnight As DateTime = nextDbMidnight - clockOffset + AfterMidnightMargin

        If Not claim.Claimed Then

            ' 取れなかった理由は2つ。
            '   ・もう昨日の分まで済んでいる → 次の0時まで見に行かない
            '   ・ほかのアプリが作業中（期限がまだ）→ 10分後にもう一度見る
            If claim.CurrentDone.HasValue AndAlso claim.CurrentDone.Value >= claim.Yesterday Then
                Return nextByMidnight
            End If

            Return DateTime.Now.Add(RetryInterval)

        End If

        ' やる範囲：前に済んだ日の翌日から、昨日まで。まだ一度も済ませていなければ、昨日の分だけ
        ' （表を作った日より前の日を、どっと送らない）
        Dim fromDate As Date = If(claim.PrevDone.HasValue, claim.PrevDone.Value.AddDays(1), claim.Yesterday)
        Dim throughDate As Date = claim.Yesterday

        Dim outcome As DailyJobOutcome

        Try
            outcome = job.Run(fromDate, throughDate)
        Catch ex As Exception
            ErrorReport.Report(ex, "DailyJobs." & job.Name,
                               FormatRange(fromDate, throughDate) & " の分")
            outcome = DailyJobOutcome.Retry("例外で止まった：" & ex.GetType().Name)
        End Try

        If outcome Is Nothing Then
            outcome = DailyJobOutcome.Retry("仕事が結果を返さなかった")
        End If

        If outcome.IsDone Then

            ' 【済んだ日付は、終わってから書く】
            If Not Complete(job.Name, token, throughDate, outcome.Summary) Then

                ' 期限が切れて、ほかのアプリに印を取られていた（仕事が10分より長くかかった）。
                ' 済んだ日付は、そちらが書く。2回走った可能性があるので知らせる
                ErrorReport.ReportProblem("DailyJobs." & job.Name,
                    "作業中の印が、終わる前にほかのアプリに取られていました（" & FormatRange(fromDate, throughDate) &
                    " の分。期限 " & LeaseSeconds & " 秒）。同じ仕事が2回走った可能性があります。")
            End If

            Return nextByMidnight

        End If

        ' 【やり直すときは、作業中の印を外さない】期限が切れた10分後に、どれかのアプリがやり直す
        RecordResult(job.Name, token, outcome.Summary)

        Return DateTime.Now.Add(RetryInterval)

    End Function


    ' 作業中の印を取る（取れたら、前に済んだ日付を返す）
    '
    ' 1本のバッチで次をやる。
    '   1. 仕事の行が無ければ作る（UPDLOCK・HOLDLOCK で、2つのアプリが同時に作ろうとしても1行だけ）
    '   2. 「昨日の分がまだ」かつ「誰も作業していない（期限切れを含む）」なら、印を書く＝取れた
    '   3. DB の今の時刻・昨日・取れたか・前に済んだ日付・今の済んだ日付を返す
    '
    ' 【日付は DB の時計で決める】ChatMessages.CreatedAt が DB の時計で入っているため
    Private Shared Function TryClaim(jobName As String, token As Guid) As ClaimResult

        Const sql As String =
            "SET NOCOUNT ON; " &
            "DECLARE @now DATETIME2(3) = SYSDATETIME(); " &
            "DECLARE @yesterday DATE = DATEADD(DAY, -1, CAST(@now AS DATE)); " &
            "INSERT INTO DailyJobs (JobName) " &
            "SELECT @JobName " &
            "WHERE NOT EXISTS (SELECT 1 FROM DailyJobs WITH (UPDLOCK, HOLDLOCK) WHERE JobName = @JobName); " &
            "DECLARE @claimed TABLE (DoneThrough DATE NULL); " &
            "UPDATE DailyJobs " &
            "   SET LeaseUntil = DATEADD(SECOND, @LeaseSeconds, @now), LeaseToken = @Token " &
            "OUTPUT deleted.DoneThrough INTO @claimed " &
            " WHERE JobName = @JobName " &
            "   AND (DoneThrough IS NULL OR DoneThrough < @yesterday) " &
            "   AND (LeaseUntil IS NULL OR LeaseUntil < @now); " &
            "SELECT @now AS DbNow, @yesterday AS Yesterday, " &
            "       CAST(CASE WHEN EXISTS (SELECT 1 FROM @claimed) THEN 1 ELSE 0 END AS BIT) AS Claimed, " &
            "       (SELECT TOP (1) DoneThrough FROM @claimed) AS PrevDone, " &
            "       (SELECT DoneThrough FROM DailyJobs WHERE JobName = @JobName) AS CurrentDone;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@JobName", SqlDbType.NVarChar, 50).Value = jobName
                cmd.Parameters.Add("@Token", SqlDbType.UniqueIdentifier).Value = token
                cmd.Parameters.Add("@LeaseSeconds", SqlDbType.Int).Value = LeaseSeconds

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    If Not reader.Read() Then
                        Throw New InvalidOperationException("DailyJobs の印を取る問い合わせが、行を返しませんでした。")
                    End If

                    Return New ClaimResult With {
                        .LocalAtClaim = DateTime.Now,
                        .DbNow = reader.GetDateTime(0),
                        .Yesterday = reader.GetDateTime(1).Date,
                        .Claimed = reader.GetBoolean(2),
                        .PrevDone = If(reader.IsDBNull(3), CType(Nothing, Date?), reader.GetDateTime(3).Date),
                        .CurrentDone = If(reader.IsDBNull(4), CType(Nothing, Date?), reader.GetDateTime(4).Date)
                    }

                End Using

            End Using
        End Using

    End Function


    ' 済んだ日付を書いて、作業中の印を外す（印が自分のものであるときだけ）。書けたら True
    Private Shared Function Complete(jobName As String, token As Guid, throughDate As Date, summary As String) As Boolean

        Const sql As String =
            "UPDATE DailyJobs " &
            "   SET DoneThrough = @Through, LeaseUntil = NULL, LeaseToken = NULL, " &
            "       LastRunAt = SYSDATETIME(), LastResult = @Result " &
            " WHERE JobName = @JobName AND LeaseToken = @Token;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@JobName", SqlDbType.NVarChar, 50).Value = jobName
                cmd.Parameters.Add("@Token", SqlDbType.UniqueIdentifier).Value = token
                cmd.Parameters.Add("@Through", SqlDbType.Date).Value = throughDate
                cmd.Parameters.Add("@Result", SqlDbType.NVarChar, LastResultMaxLength).Value = Clip(summary)

                conn.Open()
                Return cmd.ExecuteNonQuery() > 0

            End Using
        End Using

    End Function


    ' やり直すときの記録（済んだ日付も作業中の印も、そのまま）
    Private Shared Sub RecordResult(jobName As String, token As Guid, summary As String)

        Const sql As String =
            "UPDATE DailyJobs SET LastRunAt = SYSDATETIME(), LastResult = @Result " &
            " WHERE JobName = @JobName AND LeaseToken = @Token;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@JobName", SqlDbType.NVarChar, 50).Value = jobName
                cmd.Parameters.Add("@Token", SqlDbType.UniqueIdentifier).Value = token
                cmd.Parameters.Add("@Result", SqlDbType.NVarChar, LastResultMaxLength).Value = Clip(summary)

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function


    Private Shared Function Clip(text As String) As String

        Dim value As String = If(text, String.Empty)

        If value.Length > LastResultMaxLength Then
            value = value.Substring(0, LastResultMaxLength - 1) & "…"
        End If

        Return value

    End Function


    ' 「〜」（記録・知らせ用。画面やメールの本文には DailyReport の書き方を使う）
    Friend Shared Function FormatRange(fromDate As Date, throughDate As Date) As String

        Dim f As String = fromDate.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
        Dim t As String = throughDate.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

        Return If(fromDate = throughDate, f, f & "〜" & t)

    End Function


    ' 印を取る問い合わせの結果
    Private NotInheritable Class ClaimResult
        Public LocalAtClaim As DateTime
        Public DbNow As DateTime
        Public Yesterday As Date
        Public Claimed As Boolean
        Public PrevDone As Date?
        Public CurrentDone As Date?
    End Class

End Class


' 1日1回の仕事（DailyJobs.Jobs に並べる）
Friend Interface IDailyJob

    ' DailyJobs 表の JobName（50文字まで。**あとから変えないこと**＝変えると別の仕事として、昨日の分からやり直す）
    ReadOnly Property Name As String

    ' fromDate から throughDate まで（両端を含む。DB の時計の日付）の分をやる。
    ' 済んだら DailyJobOutcome.Done、あとでやり直すなら DailyJobOutcome.Retry を返す。
    ' 例外を投げてもよい（DailyJobs が ErrorReport へ知らせて、やり直しにする）
    Function Run(fromDate As Date, throughDate As Date) As DailyJobOutcome

End Interface


' 仕事の結果
Friend NotInheritable Class DailyJobOutcome

    ' True＝済んだ（済んだ日付を進める）／False＝あとでやり直す
    Public ReadOnly IsDone As Boolean

    ' DailyJobs 表の LastResult に残す一言（例：「送った」）
    Public ReadOnly Summary As String

    Private Sub New(isDone As Boolean, summary As String)
        Me.IsDone = isDone
        Me.Summary = summary
    End Sub

    Public Shared Function Done(summary As String) As DailyJobOutcome
        Return New DailyJobOutcome(True, summary)
    End Function

    Public Shared Function Retry(summary As String) As DailyJobOutcome
        Return New DailyJobOutcome(False, summary)
    End Function

End Class
