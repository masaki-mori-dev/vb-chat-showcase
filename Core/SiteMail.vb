Imports System.Collections.Concurrent
Imports System.Configuration
Imports System.Net
Imports System.Net.Configuration
Imports System.Net.Mail
Imports System.Text
Imports System.Threading

' メールを送る、ただ1つの場所（追加）
'
' 【呼ぶ側は「送って」と渡すだけで、すぐ戻る】
' Enqueue は並べるだけで、実際に送るのは裏の作業（SerialRunner）である。
' SMTP の応答は数秒かかることがあり、相手が止まっていれば送信を待つ時間（15秒）まで返ってこない。
' それをチャットの処理（発言・入退室）に待たせないため。
'
' 【使うのは、今のところ3つ】
'   ・ErrorReport（エラーの知らせ）… Enqueue
'   ・Admin/MailTest.aspx（本番から本当に届くかの確認）… Enqueue
'   ・DailyReport（1日1回の集計メール）… SendAndWait
'     （送れたかが分かってから「済んだ日付」を書くため。裏の作業の中から呼ぶ）
'
' 【送る先・送るかどうかは、SiteSettings から読む】
' MailEnabled（メール送信そのもののオン/オフ）と MailTo（届け先）。
' **読むのは裏の作業の中だけ**。Enqueue（＝呼ぶ側のスレッド）では読まない。
' SiteSettings は読み込みに失敗すると ErrorReport へ知らせる。その ErrorReport がここへ来るので、
' 呼ぶ側のスレッドで SiteSettings を読むと、読み込み→失敗→報告→読み込み…の堂々巡りになりうる
'
' 【SMTP の設定（ホスト・ポート・パスワード・差出人）は、mail.config にある】
' Web.config の <smtp configSource="mail.config" /> が参照している、本番にしか無いファイル。
' git にも発行物にも載せない（connections.config と同じ扱い）。
' 開発機の mail.config は SpecifiedPickupDirectory にしてあり、送らずにフォルダへ .eml を書き出す。
' **コードは本番と開発機でまったく同じ**で、違うのはファイルの中身だけ
'
' 【送れなかったことは、ErrorReport へは知らせない】
' 知らせても、その知らせを送るのがここなので、また失敗するだけである。
' 代わりに回数と最後の理由を覚えておき、**次に送れたメールの末尾に書き添える**。
' 送れているかは Admin/MailTest.aspx の status でも見られる
Public NotInheritable Class SiteMail

    Private Sub New()
    End Sub

    ' 送信を待つ時間（ミリ秒）
    '
    ' 【既定の100秒は長すぎる】
    ' 送るのは裏の作業だが、1本で順番に送るので、1通が詰まるとその後ろが全部待つ。
    ' 相手が落ちているなら、15秒待っても100秒待っても結果は同じである
    Private Const SendTimeoutMilliseconds As Integer = 15000

    ' 並べておけるメールの上限
    '
    ' ErrorReport が1時間あたりの上限（既定10通）で絞っているので、ふだんここまで溜まることは無い。
    ' 送れない状態が長く続いたときに、メモリを食い続けないための歯止め
    Private Const MaxQueued As Integer = 100

    ' 件名の長さの上限（文字）。長すぎる件名は、メールソフトで読めなくなるだけ
    Private Const MaxSubjectLength As Integer = 200

    Private Shared ReadOnly Outbox As New ConcurrentQueue(Of OutgoingMail)
    Private Shared ReadOnly Runner As New SerialRunner(AddressOf DrainOutbox, "SiteMail")

    ' === 送った・送れなかったの記録（Admin/MailTest.aspx の status と、書き添えのため） ===
    '
    ' メモリにだけ置く。アプリが再起動すれば消える（ErrorReport と同じ考え方で、DBには書かない）
    Private Shared ReadOnly StatusLock As New Object()
    Private Shared _sentCount As Integer
    Private Shared _lastSentAt As DateTime?
    Private Shared _failedCount As Integer
    Private Shared _failedSinceLastSuccess As Integer
    Private Shared _lastFailureAt As DateTime?
    Private Shared _lastFailureText As String
    Private Shared _droppedWhileDisabled As Integer
    Private Shared _droppedOverflow As Integer

    ' TLS 1.2 を足したか（1回だけでよい）
    Private Shared _tlsPrepared As Integer


    ' メールを送る（並べるだけで、すぐ戻る）
    '
    ' subject … 件名。印（[vb-chat エラー] など）は呼ぶ側が付ける
    ' body    … 本文（プレーンテキスト）
    '
    ' 【例外は投げない】
    ' ここへ来るのは、たいてい「何かが壊れたので知らせたい」ときである。
    ' 知らせる処理が例外を投げて、壊れたところをさらに壊してはいけない
    '
    ' replyTo … 「返信」を押したときの宛先（Reply-To）。省略すれば付けない（問い合わせページ）
    Public Shared Sub Enqueue(subject As String, body As String, Optional replyTo As String = Nothing)

        Try

            If Outbox.Count >= MaxQueued Then
                Interlocked.Increment(_droppedOverflow)
                Return
            End If

            Outbox.Enqueue(New OutgoingMail(subject, body) With {.ReplyTo = replyTo})
            Runner.Kick()

        Catch ex As Exception
            SerialRunner.SafeTrace("メールを並べられませんでした： " & ex.ToString())
        End Try

    End Sub


    ' 並んでいるメールを、1通ずつ送る（裏の作業）
    Private Shared Sub DrainOutbox()

        Dim queued As OutgoingMail = Nothing

        While Outbox.TryDequeue(queued)
            Deliver(queued)
        End While

    End Sub


    Private Shared Sub Deliver(queued As OutgoingMail)

        If queued.ToAddress Is Nothing Then
            DeliverCore(queued.Subject, queued.Body, queued.ReplyTo)
        Else
            DeliverToAccount(queued)
        End If

    End Sub


    ' ============================================================
    ' アカウントのアドレスへ送る
    ' ============================================================
    '
    ' 【何にでも使える口にしない】呼んでよいのは Accounts/AccountMail.vb だけ（Friend にしてある）。
    ' あちらが、決まった3通（再設定のリンク・パスワードが変わりました・メールアドレスが変わりました）だけを組み立て、
    ' 宛先は必ずアカウントの行のアドレス（か、変える前のアドレス）にする。**画面から来た文字列を宛先にしない**。
    ' ここに「好きな宛先へ好きな本文」の口を作ると、このサイトがよその人へのメールの踏み台になる。
    '
    ' 【info@ へのメールと違うところ】
    '   ・本文の後ろに「送信の記録」（送れなかった数・理由）を付けない。利用者に見せるものではない
    '   ・送れなかったら、ErrorReport で info@ へ知らせる（place＝その種類。絞り方は ErrorReport の決まりのまま＝
    '     同じ場所と種類は1時間に1通）。**知らせに、宛先・本文（再設定のリンク）は入れない**。アカウントの番号と理由だけ
    '   ・送り直さない（再設定のリンクなら、本人がもう一度押せばよい）
    '   ・MailEnabled = 0（止める口）は、こちらにも効く
    '
    ' accountId  … 知らせに書く番号
    ' reportPlace … 送れなかったときの ErrorReport の場所（"AccountMail.ResetLink" など、決まった名前）
    Friend Shared Sub EnqueueToAccount(toAddress As String, subject As String, body As String,
                                       accountId As Integer, reportPlace As String)

        Try

            If String.IsNullOrWhiteSpace(toAddress) Then
                Return
            End If

            If Outbox.Count >= MaxQueued Then
                Interlocked.Increment(_droppedOverflow)
                Return
            End If

            Outbox.Enqueue(New OutgoingMail(subject, body, toAddress, accountId, reportPlace))
            Runner.Kick()

        Catch ex As Exception
            SerialRunner.SafeTrace("アカウントのメールを並べられませんでした： " & ex.GetType().Name)
        End Try

    End Sub


    Private Shared Sub DeliverToAccount(queued As OutgoingMail)

        If Not SiteSettings.MailEnabled Then
            SyncLock StatusLock
                _droppedWhileDisabled += 1
            End SyncLock
            Return
        End If

        Try

            SendNow(queued.ToAddress, queued.Subject, queued.Body, SendTimeoutMilliseconds)

            SyncLock StatusLock
                _sentCount += 1
                _lastSentAt = DateTime.Now
                _failedSinceLastSuccess = 0
            End SyncLock

        Catch ex As Exception

            SyncLock StatusLock
                _failedCount += 1
                _failedSinceLastSuccess += 1
                _lastFailureAt = DateTime.Now
                _lastFailureText = ex.GetType().Name & "： " & ex.Message
            End SyncLock

            ' 【件名と宛先はトレースにも書かない】件名は決まった文言だが、念のため種類だけ
            SerialRunner.SafeTrace("アカウントのメールを送れませんでした（" & queued.ReportPlace & "）： " & ex.GetType().Name)

            ' 【ここから ErrorReport を呼んでよい】ErrorReport は並べるだけ（鍵の中で SiteMail を呼び返さない）。
            ' 知らせのメールも同じ SMTP で送るので、SMTP そのものが壊れていれば届かない——
            ' そのときは、次に送れた info@ のメールの「送信の記録」に数が載る（今までの info@ のメールと同じ）
            ErrorReport.ReportProblem(queued.ReportPlace,
                                      "アカウント " & queued.AccountId & " へのメールを送れませんでした（" &
                                      ex.GetType().Name & "： " & ex.Message & "）。" &
                                      "宛先と本文は、この知らせに入れていません。")

        End Try

    End Sub


    ' メールを送り、送れたかどうかを返す（呼んだスレッドで、送り終わるまで待つ）
    '
    ' 【Enqueue との違いは、結果が分かることだけ】
    ' 中身は Enqueue のあとの裏の作業（Deliver）とまったく同じ DeliverCore なので、
    ' MailEnabled の判断・届け先・送信の記録・「前に何があったか」の書き添え・TLS は、どちらでも同じに効く。
    '
    ' 【使うのは、1日1回の集計メール（DailyReport）だけ】
    ' 「どの日の分まで済んだか」を、**送り終わってから**書くために、送れたかを知る必要がある。
    ' Enqueue は並べるだけで、送れたかを返さない。
    '
    ' 【呼んでよいのは、裏の作業の中だけ】SMTP の応答を待つ（最大15秒）。
    ' ページの要求やハブのメソッドから呼ぶと、その間チャットの処理が止まる。そこからは Enqueue を使うこと
    '
    ' 【例外は投げない】送れなかったときは Failed を返す（理由は送信の記録に残る＝MailTest の status）
    '
    ' 【呼ぶ側が待つ時間を決められる】（問い合わせページ）
    ' 0 を渡せば既定（15秒）。問い合わせページは、DB に書けなかったときの最後の手段としてここへ来るので、
    ' **5秒**しか待たない——SMTP が止まっている日に、その時間だけ要求のスレッドを占めるのを短くするため
    ' （並べる Enqueue と違い、こちらは呼んだスレッドで待つ）。
    ' 1日1回の集計メール（DailyReport）は裏の作業なので、今までどおり 0（＝15秒）で呼ぶ
    Friend Shared Function SendAndWait(subject As String, body As String,
                                       Optional replyTo As String = Nothing,
                                       Optional timeoutMilliseconds As Integer = 0) As MailSendResult

        Try
            Return DeliverCore(subject, body, replyTo, timeoutMilliseconds)
        Catch ex As Exception
            SerialRunner.SafeTrace("メールを送れませんでした（件名：" & subject & "）： " & ex.ToString())
            Return MailSendResult.Failed
        End Try

    End Function


    ' 1通送る（Deliver と SendAndWait の中身）
    Private Shared Function DeliverCore(subject As String, body As String,
                                        Optional replyTo As String = Nothing,
                                        Optional timeoutMilliseconds As Integer = 0) As MailSendResult

        ' 設定はここで読む（上の【送る先・送るかどうかは、SiteSettings から読む】）。
        ' SiteSettings は例外を投げない（読めなければ既定値。フェイルオープン）
        If Not SiteSettings.MailEnabled Then

            ' 【止めている間のメールは、あとで送り直さない】
            ' オンに戻した瞬間に、溜まっていた古い知らせがどっと届くのを避ける。
            ' 何通捨てたかだけ覚えておき、次に送るメールに書き添える
            SyncLock StatusLock
                _droppedWhileDisabled += 1
            End SyncLock
            Return MailSendResult.Disabled

        End If

        Dim toAddress As String = SiteSettings.MailTo
        Dim fullBody As String = body & BuildDeliveryNote()

        Try

            SendNow(toAddress, subject, fullBody,
                    If(timeoutMilliseconds > 0, timeoutMilliseconds, SendTimeoutMilliseconds),
                    replyTo)

            SyncLock StatusLock
                _sentCount += 1
                _lastSentAt = DateTime.Now
                _failedSinceLastSuccess = 0
                _droppedWhileDisabled = 0
            End SyncLock

            ' 書き添えた「並べきれずに捨てた数」は、送れた時点で0に戻す
            Interlocked.Exchange(_droppedOverflow, 0)

            Return MailSendResult.Sent

        Catch ex As Exception

            SyncLock StatusLock
                _failedCount += 1
                _failedSinceLastSuccess += 1
                _lastFailureAt = DateTime.Now
                _lastFailureText = ex.GetType().Name & "： " & ex.Message
            End SyncLock

            ' 【ErrorReport へは知らせない】（このファイルの先頭のコメント）
            SerialRunner.SafeTrace("メールを送れませんでした（件名：" & subject & "）： " & ex.ToString())

            Return MailSendResult.Failed

        End Try

    End Function


    ' 最後に送れなかった理由（送信の記録。DailyJobs が「送れなかった：…」と DB の LastResult に残すのに使う）
    Friend Shared ReadOnly Property LastFailureText As String
        Get
            SyncLock StatusLock
                Return _lastFailureText
            End SyncLock
        End Get
    End Property


    ' 次に送れたメールの末尾に書き添える、「前に何があったか」
    Private Shared Function BuildDeliveryNote() As String

        Dim sb As New StringBuilder()

        SyncLock StatusLock

            If _failedSinceLastSuccess > 0 Then
                sb.AppendLine()
                sb.AppendLine("--- 送信の記録 ---")
                sb.AppendLine("このメールの前に、送れなかったメールが " & _failedSinceLastSuccess & " 通あります。")
                sb.AppendLine("最後に失敗した時刻：" & FormatTime(_lastFailureAt))
                sb.AppendLine("最後の理由：" & _lastFailureText)
            End If

            If _droppedWhileDisabled > 0 Then
                sb.AppendLine()
                sb.AppendLine("メール送信を止めていた間（SiteSettings の MailEnabled = 0）に、送らなかったメールが " &
                              _droppedWhileDisabled & " 通あります。")
            End If

        End SyncLock

        Dim overflow As Integer = Interlocked.CompareExchange(_droppedOverflow, 0, 0)

        If overflow > 0 Then
            sb.AppendLine()
            sb.AppendLine("送る順番に並べきれず、捨てたメールが " & overflow & " 通あります（上限 " & MaxQueued & " 通）。")
        End If

        Return sb.ToString()

    End Function


    ' その場で1通送る（呼んだスレッドで送り終わるまで待つ）
    '
    ' 【ふつうは Enqueue を使うこと】
    ' これを直接呼ぶのは、アプリが止まる直前（ErrorReport.ReportFatal）のように、
    ' 並べても裏の作業が走る前にプロセスが消えてしまう場面だけ。
    ' 失敗したら例外を投げる（呼ぶ側が捕まえる）
    '
    ' replyTo … 「返信」を押したときの宛先。省略すれば付けない（問い合わせページ）。
    ' **改行の混ざったアドレスは MailAddress が例外にする**（見出しの差し込み＝ヘッダーインジェクション避け）。
    ' 呼ぶ側でも形を確かめてから渡すこと（守りを2枚にしてある）
    Friend Shared Sub SendNow(toAddress As String, subject As String, body As String,
                              timeoutMilliseconds As Integer,
                              Optional replyTo As String = Nothing)

        PrepareTls()

        ' 差出人は mail.config の from から取る。
        ' ファイルが無い・壊れているときは、ここで ConfigurationErrorsException になる
        ' （開発機で試したとおり、サイトは落ちない。困るのはメールだけ）
        Dim smtp As SmtpSection = ReadSmtpSection()

        If smtp Is Nothing OrElse String.IsNullOrWhiteSpace(smtp.From) Then
            Throw New InvalidOperationException("mail.config に差出人（from）がありません。")
        End If

        If String.IsNullOrWhiteSpace(toAddress) Then
            Throw New InvalidOperationException("届け先（SiteSettings の MailTo）が空です。")
        End If

        ' SmtpClient は、ホスト・ポート・認証・暗号化・送り方を mail.config から読む。
        ' 待つ時間だけは設定ファイルに書く場所が無いので、ここで決める
        Using client As New SmtpClient()

            client.Timeout = timeoutMilliseconds

            Using message As New MailMessage()

                message.From = New MailAddress(smtp.From)
                message.To.Add(New MailAddress(toAddress))

                ' 【返信先（Reply-To）】これが付いていると、開発者は「返信」を1回押すだけで返事が書ける。
                '
                ' 【付けられなかったときは、付けずに送る】送るのをやめてはいけない——
                ' 返信先は「あると便利」なもので、知らせそのものより大事ではない。
                ' 本文にはアドレスが文字として必ず入っているので、手で貼れば返事は書ける
                If Not String.IsNullOrWhiteSpace(replyTo) Then
                    Try
                        message.ReplyToList.Add(New MailAddress(replyTo))
                    Catch ex As Exception
                        SerialRunner.SafeTrace("返信先を付けられませんでした： " & ex.GetType().Name)
                    End Try
                End If

                ' 【件名は、自分で組み立てた見出しとして入れる。Subject プロパティは使わない】
                '
                ' .NET Framework の MailMessage は、日本語の件名を「=?utf-8?B?…?=」の塊（encoded-word）に
                ' 分けるとき、**1文字（UTF-8で3バイト）の途中で切ってしまう**（開発機で
                ' 書き出した .eml の生の見出しで確かめた。「の」＝E3 81 AE が、E3 と 81 AE の2つの塊に割れていた）。
                ' 塊は1つずつ文字に戻すのが規格（RFC 2047）なので、読み手によっては件名が文字化けする
                ' （Gmail のように、隣の塊とつなげてから戻す寛容な読み手なら読める）。
                '
                ' 【直し方】件名を自分で組み立てて、ASCII だけの文字列として Subject に入れる。
                ' .NET は、Subject に入った文字列が **「=?」で始まるときだけ** 塊を文字に戻して組み立て直し、
                ' それ以外の ASCII の文字列は、そのまま見出しに書く（SubjectEncoding も Nothing のまま）。
                ' そこで、先頭の「[vb-chat」だけは素の ASCII で残し、残りを文字の境目で区切った塊にする
                ' （EncodeSubject のコメント）。開発機の .eml で確かめた。
                '
                ' 【試して駄目だった形】Subject を空にして、見出し（Headers）に "Subject" を直接足す
                ' → .NET が送る直前に見出しを組み直すとき、Subject が空なので **件名の行ごと消えた**
                message.Subject = EncodeSubject(ToOneLine(subject, MaxSubjectLength))

                message.Body = If(body, String.Empty)
                message.BodyEncoding = Encoding.UTF8
                message.IsBodyHtml = False

                client.Send(message)

            End Using
        End Using

    End Sub


    ' TLS 1.2 を使えるようにしておく
    '
    ' 【罠】既定の SystemDefault は、OS の設定次第で古いプロトコルを試して失敗しうる
    ' （ホスティング先から外への HTTPS の通り方と同じ用心）。
    ' 「今の値に Tls12 を足す」形にして、既に明示されているものは消さない
    ' （SystemDefault は 0 なので、足すと Tls12 だけになる）。
    ' このアプリで外へ出ていく通信はメールだけなので、他への影響は無い
    Private Shared Sub PrepareTls()

        If Interlocked.Exchange(_tlsPrepared, 1) = 1 Then
            Return
        End If

        Try
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Catch ex As Exception
            SerialRunner.SafeTrace("TLS 1.2 を足せませんでした： " & ex.ToString())
        End Try

    End Sub


    ' mail.config（Web.config の system.net/mailSettings/smtp）を読む
    Private Shared Function ReadSmtpSection() As SmtpSection
        Return TryCast(ConfigurationManager.GetSection("system.net/mailSettings/smtp"), SmtpSection)
    End Function


    ' 件名を1行にそろえ、長すぎれば切る
    Private Shared Function ToOneLine(text As String, maxLength As Integer) As String

        Dim oneLine As String = If(text, String.Empty).Replace(vbCr, " ").Replace(vbLf, " ").Trim()

        If oneLine.Length > maxLength Then
            oneLine = oneLine.Substring(0, maxLength - 1) & "…"
        End If

        Return oneLine

    End Function


    ' 件名を、.NET に組み立て直させない形（ASCII だけ）に組み立てる。**文字の途中では切らない**
    '
    '   "[vb-chat エラー] 続報：…"  →  "[vb-chat =?utf-8?B?…?= =?utf-8?B?…?="
    '
    ' ・ASCII だけの件名は、そのまま返す
    ' ・先頭の、空白までの ASCII の部分（"[vb-chat"）は素のまま残す。これが「=?」で始まらないことが、
    '   .NET に組み立て直させない条件である（Deliver の上のコメント）
    ' ・残り（空白も含めて）を、RFC 2047 の塊（=?utf-8?B?…?=）にする。
    '   塊と塊の間の空白は読み手が読み飛ばす決まりなので、元の空白は塊の中に入れて残す
    ' ・1つの塊は75文字まで、が規格。中身（UTF-8のバイト）を45バイトまでに抑えると、
    '   塊は「=?utf-8?B?」10文字＋Base64 60文字＋「?=」2文字＝72文字に収まる
    ' ・区切るのは「見た目の1文字」（StringInfo の文字要素）の境目。サロゲートペアや結合文字も割らない
    '
    ' 【先頭が ASCII でない件名は、この直しが効かない】そのまま返し、.NET の切り方に任せる
    ' （Gmail などは読める。今の件名は全部 "[vb-chat" で始まるので、ここへは来ない）
    Private Shared Function EncodeSubject(text As String) As String

        Const MaxBytesPerWord As Integer = 45

        If String.IsNullOrEmpty(text) OrElse IsPlainAscii(text) Then
            Return text
        End If

        Dim firstSpace As Integer = text.IndexOf(" "c)

        If firstSpace <= 0 Then
            Return text
        End If

        Dim lead As String = text.Substring(0, firstSpace)

        If Not IsPlainAscii(lead) OrElse lead.StartsWith("=?", StringComparison.Ordinal) Then
            Return text
        End If

        ' 先頭の空白は、塊の中に入れて残す（塊と素の文字の間の空白1つは、読み手が空白として出す）
        Dim rest As String = text.Substring(firstSpace + 1)

        Dim words As New List(Of String)
        Dim current As New StringBuilder()
        Dim currentBytes As Integer = 0

        Dim elements As Globalization.TextElementEnumerator =
            Globalization.StringInfo.GetTextElementEnumerator(rest)

        While elements.MoveNext()

            Dim element As String = elements.GetTextElement()
            Dim size As Integer = Encoding.UTF8.GetByteCount(element)

            If currentBytes > 0 AndAlso currentBytes + size > MaxBytesPerWord Then
                words.Add(ToEncodedWord(current.ToString()))
                current.Clear()
                currentBytes = 0
            End If

            current.Append(element)
            currentBytes += size

        End While

        If current.Length > 0 Then
            words.Add(ToEncodedWord(current.ToString()))
        End If

        Return lead & " " & String.Join(" ", words)

    End Function


    ' 見出しにそのまま書ける ASCII（表示できる文字）だけか
    Private Shared Function IsPlainAscii(text As String) As Boolean

        For Each ch As Char In text
            If AscW(ch) > 126 OrElse AscW(ch) < 32 Then
                Return False
            End If
        Next

        Return True

    End Function


    Private Shared Function ToEncodedWord(part As String) As String
        Return "=?utf-8?B?" & Convert.ToBase64String(Encoding.UTF8.GetBytes(part)) & "?="
    End Function


    Private Shared Function FormatTime(value As DateTime?) As String

        If Not value.HasValue Then
            Return "（なし）"
        End If

        Return value.Value.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture)

    End Function


    ' === 状態の確認（Admin/MailTest.aspx の status から呼ぶ） ===

    ' mail.config が読めているか、何が書いてあるか（パスワードは出さない）
    Friend Shared Function DescribeConfig() As String

        Dim sb As New StringBuilder()

        Try

            Dim smtp As SmtpSection = ReadSmtpSection()

            If smtp Is Nothing Then
                sb.AppendLine("mail.config：読めません（Web.config に smtp の設定がありません）")
                Return sb.ToString()
            End If

            sb.AppendLine("mail.config：読めました")
            sb.AppendLine("  送り方　：" & smtp.DeliveryMethod.ToString())
            sb.AppendLine("  差出人　：" & If(String.IsNullOrEmpty(smtp.From), "（空）", smtp.From))

            If smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory Then
                sb.AppendLine("  書き出し先：" & smtp.SpecifiedPickupDirectory.PickupDirectoryLocation &
                              "（送らずに .eml を書き出す＝開発機の形）")
            Else
                sb.AppendLine("  ホスト　：" & If(String.IsNullOrEmpty(smtp.Network.Host), "（空）", smtp.Network.Host))
                sb.AppendLine("  ポート　：" & smtp.Network.Port)
                sb.AppendLine("  暗号化　：" & If(smtp.Network.EnableSsl, "あり（STARTTLS）", "なし"))
                sb.AppendLine("  ユーザー：" & If(String.IsNullOrEmpty(smtp.Network.UserName), "（空）", smtp.Network.UserName))
                sb.AppendLine("  パスワード：" & If(String.IsNullOrEmpty(smtp.Network.Password), "（空）", "入っています"))
            End If

        Catch ex As Exception
            sb.AppendLine("mail.config：読めません（" & ex.GetType().Name & "： " & ex.Message & "）")
        End Try

        Return sb.ToString()

    End Function


    ' 送った・送れなかったの記録
    Friend Shared Function DescribeStatus() As String

        Dim sb As New StringBuilder()

        SyncLock StatusLock
            sb.AppendLine("送れたメール　：" & _sentCount & " 通（最後 " & FormatTime(_lastSentAt) & "）")
            sb.AppendLine("送れなかったメール：" & _failedCount & " 通（最後 " & FormatTime(_lastFailureAt) & "）")
            If _lastFailureText IsNot Nothing Then
                sb.AppendLine("  最後の理由：" & _lastFailureText)
            End If
            sb.AppendLine("止めていたので送らなかったメール：" & _droppedWhileDisabled & " 通")
        End SyncLock

        sb.AppendLine("まだ送っていない（並んでいる）メール：" & Outbox.Count & " 通")
        sb.AppendLine("（この記録はアプリが再起動すると0に戻ります）")

        Return sb.ToString()

    End Function


    ' 並べておくメール1通
    Private NotInheritable Class OutgoingMail

        Public ReadOnly Subject As String
        Public ReadOnly Body As String

        ' 「返信」を押したときの宛先（問い合わせページ）。付けないときは Nothing。
        ' 【ReadOnly にしていない】New のあとに With { } で入れるため（引数の並びを増やさずに済む）
        Public ReplyTo As String

        ' 宛先（Nothing なら info@＝SiteSettings.MailTo。アカウントのアドレスへ送るときだけ入る）
        Public ReadOnly ToAddress As String
        Public ReadOnly AccountId As Integer
        Public ReadOnly ReportPlace As String

        Public Sub New(subject As String, body As String)
            Me.Subject = subject
            Me.Body = body
        End Sub

        Public Sub New(subject As String, body As String, toAddress As String,
                       accountId As Integer, reportPlace As String)
            Me.Subject = subject
            Me.Body = body
            Me.ToAddress = toAddress
            Me.AccountId = accountId
            Me.ReportPlace = reportPlace
        End Sub

    End Class

End Class


' SiteMail.SendAndWait の結果
Friend Enum MailSendResult

    ' 送れた
    Sent

    ' メール送信を止めている（SiteSettings の MailEnabled = 0）ので、送らなかった。
    ' 止めている間のメールは送り直さない約束なので、呼ぶ側は「済んだ」として扱ってよい
    Disabled

    ' 送ろうとしたが、送れなかった（mail.config が無い・SMTP が応えない、など）。呼ぶ側は、あとでやり直す
    Failed

End Enum
