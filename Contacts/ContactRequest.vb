Imports System.Text

' 問い合わせを受け付ける判断（問い合わせページ）
'
' 【何を、どの順で確かめるか】をここに持つ。
' 画面（Contacts/Contact.aspx）は、ここを呼んで結果を出すだけ
' （Accounts/AccountReset.vb・AccountSettings.vb と同じ立ち位置）。
' C#／ASP.NET Core MVC へ移るときに持っていく。HttpContext.Current を使わず引数で受け取るのも同じ理由。
'
' ============================================================
' 【DB が正、メールは知らせ】
' ============================================================
' 届いた1件は、まず DB（ContactMessages）へ書く。メールは info@ への「届きましたよ」の知らせである。
'   ・メールが送れなかった日でも、1件も失わない
'   ・MVC版の管理画面で「届いた問い合わせの一覧」に使える
'
' そのため、利用者に「送れませんでした」と出すのは **DB にもメールにも残せなかったとき だけ** にしてある。
'
'   DB に書けた           → 「送信しました」。メールは並べるだけ（送れたかは待たない）
'   DB に書けなかった     → その場でメールを送ってみる（最後の手段。5秒だけ待つ）
'       送れた            → 「送信しました」（メールに中身が入ったので、失われていない）
'       送れなかった      → 「送れませんでした」＋ ErrorReport で知らせる
'
' 【なぜ DB のときはメールを待たないのか】
' メールの送信は SMTP の応答を待つ（最大15秒）。ふだんの1件でそれを待たせると、
' 送信ボタンを押した人が十数秒固まる。DB に書けている＝もう失われないので、待つ意味が無い。
'
' ============================================================
' 【送り主の書いたアドレスを、メールの宛先にしない】
' ============================================================
' 宛先は必ず info@（SiteSettings.MailTo）だけ。書かれたアドレスは、本文と Reply-To に入るだけである。
' 宛先にすると、他人のアドレスを書いて何百回も送るだけで、**このサイトが迷惑メールの踏み台になる**。
' 同じ理由で、送った人へ「受け付けました」の控えも返さない。
' 画面に「送信しました」と出すだけにしてある。
'
' 【EnqueueToAccount は使わない】あれはアカウントのお知らせ専用（Accounts/AccountMail.vb だけが呼ぶ）。
' ここは info@ へ送るだけなので SiteMail.Enqueue で足りる
'
' ============================================================
' 【確かめる順番】（3度目の回で組み替えた）
' ============================================================
'   1. ロボット避けの欄　　… 数えない（捨てるだけ）
'   2. 連打・1時間の「もう上限か」… **IsOver（数えない）**。DB を引く手前で止めるため
'   3. 入力の確かめ　　　　… 数えない
'   4. **同じ内容をもう受け付けていないか**… DB を1回引く。**止めたぶんは数えない**
'   5. 連打・1時間を**数える**（TryCount）… ここまで来た＝送ることが決まった
'   6. DB へ書く → メール
'
' **4 を 5 より前に置いてあるのが要**である。逆にすると、誤って二度押した人の枠が減る
Public NotInheritable Class ContactRequest

    Private Sub New()
    End Sub


    ' === どの欄が悪いか（画面の欄の名前ではなく、意味で呼ぶ。画面がどの欄かを決める）===
    Public Const FieldKind As String = "kind"
    Public Const FieldName As String = "name"
    Public Const FieldEmail As String = "email"
    Public Const FieldBody As String = "body"


    ' 結果（画面はこれを見て、文言を出す）
    Public Class ContactResult

        Public Property Ok As Boolean

        ' 断ったとき、どの欄が悪いか（上の Field〇〇。どの欄とも言えなければ Nothing＝歯止め・混み合っている、など）。
        ' 画面がその欄に印を付け、理由がその欄の上に出る
        Public Property Field As String

        ' 画面に出す文言（出口でエスケープする）
        Public Property Message As String

        ' 【入力を残すか、欄ごと消すか】
        ' Ok なら欄ごと消す（もう一度押させない）。Ok でなければ、打った文章を残して直してもらう
        Public Property KeepInput As Boolean

        ' 同じ内容が、少し前にもう受け付けられていた
        '
        ' 【Ok も True である】利用者から見れば「さっき送ったものが届いている」＝送れている。
        ' ここで「送れませんでした」と出すと、「届いていないのか」と思ってもう一度押す。
        ' 画面は、完了の画面へ行ったうえで文言だけを変える（Contacts/Contact.aspx.vb の ShowDone）
        Public Property AlreadyReceived As Boolean

        ' === 確かめたあとの値（Validate が Ok のときだけ入る。確認の画面） ===
        '
        ' 【画面は、この値を確認の画面に出す】打たれたそのままではなく、**正規化を通ったあと**の値である。
        ' 「確認の画面に出したものが、そのまま保存・送信される」ことが、この画面の約束なので、
        ' 画面が自分で正規化し直さないこと（2回別々に整えると、いつか食い違う）
        Public Property Name As String
        Public Property Email As String
        Public Property Body As String
        Public Property Kind As ContactRules.ContactKind

    End Class


    ' === 歯止め（防御なので SiteSettings に入れない。AccountReset と同じ判断） ===

    ' 数える長さ。**件数のほうだけが設定である**（SiteSettings.ContactMaxPerHour）
    Private Shared ReadOnly CountWindow As TimeSpan = TimeSpan.FromHours(1)

    ' 連打（同じIPから続けて押された）を止める間隔
    '
    ' 【1時間の上限とは別の時計】上限が5件でも、3秒より速く2回押されたら2度目は断る。
    ' ボタンの二度押し（通信が遅いときに、待てずにもう一度押す）を、1件の重複にしないためでもある
    Private Shared ReadOnly BurstWindow As TimeSpan = TimeSpan.FromSeconds(3)

    ' 数える道具（Core/RateCounter.vb）。**メモリだけ**＝アプリが再起動すると数え直し
    '
    ' 【1時間の上限のほうは、上限の数が設定なので、あとから作る】
    ' RateCounter は New のときに上限を決める形なので、設定が変わったら作り直す（下の HourLimiter）
    Private Shared ReadOnly BurstLimiter As New RateCounter(BurstWindow, 1)

    Private Shared ReadOnly HourLock As New Object()
    Private Shared _hourLimiter As RateCounter
    Private Shared _hourLimiterMax As Integer

    ' 送信を待つ時間（DB に書けなかったときの、最後の手段のメール）
    Private Const FallbackMailTimeoutMs As Integer = 5000

    ' 断る文言
    Private Const TroubleMessage As String =
        "ただいま送信できませんでした。お手数ですが、時間をおいてもう一度お試しください。"

    Private Const TooManyMessage As String =
        "問い合わせが続いたため、しばらく受け付けません。1時間ほど待ってから、もう一度お試しください。"

    Private Const TooFastMessage As String =
        "送信の間隔が短すぎます。少し待ってから、もう一度押してください。"

    ' 【完了の画面にも出す】（PRG）
    ' 送信のあとは転送して GET で出し直すので、そのときは Submit の戻り値が手元に無い。
    ' 文言はここ1か所に持ち、画面（Contacts/Contact.aspx.vb の ShowDone）がここから取る
    Public Const DoneMessage As String =
        "送信しました。ありがとうございます。メールアドレスを入れていただいた場合は、" &
        "管理人から返事を差し上げることがあります（すべてにお返事できるとは限りません）。"

    ' 同じ内容が、少し前にもう受け付けられていたとき
    '
    ' 【「送れませんでした」と書かないこと】利用者から見れば、さっき送ったものは届いている。
    ' 迷わせないために、**届いていること**と、**もう一度押さなくてよいこと**を、はっきり書く
    Public Const AlreadyReceivedMessage As String =
        "この内容は、すでに受け付けています。先ほどお送りいただいた分が届いていますので、" &
        "もう一度送っていただく必要はありません。別のご用件のときは、内容を書き直してからお送りください。"


    ' ============================================================
    ' 入力を確かめるだけ（確認の画面へ進むとき）
    ' ============================================================
    '
    ' 【1つも数えない・1つも保存しない・メールも出さない】
    ' 歯止め（3秒・1時間に5件）は「実際に送った」ときだけ数える、というのため。
    ' 確認の画面を見るだけで枠が減ると、書き直すたびに送れなくなる。
    '
    ' 【Ok のとき、正規化を通った値を返す】画面はそれを確認の画面に出す。
    ' 「確認の画面に出したものが、そのまま保存・送信される」ことを守るため、
    ' **画面が自分で正規化し直さないこと**
    Public Shared Function Validate(name As String,
                                    email As String,
                                    kindRaw As String,
                                    body As String) As ContactResult

        ' 【body は、もうほどかれている】
        ' 本番の手前の WAF を避けるため、画面の JavaScript（Scripts/site.js の「7」）が
        ' 内容を包んで送るが、**ほどくのは画面側（Contacts/Contact.aspx.vb の DecodeBody）の仕事**である。
        ' ここでもほどくと、**利用者が本当に「vbenc1:」で始まる文を書いたとき**に、その文を壊す
        Dim cleanName As String = ContactRules.NormalizeOneLine(name)
        Dim cleanEmail As String = ContactRules.NormalizeOneLine(email)
        Dim cleanBody As String = ContactRules.Normalize(body)

        ' 【種類は、選ばれていなければ断る】
        ' 「選んでください」のままだと空文字で届く。落とし先（その他）を作らないのは、
        ' 作ると「選ばずに送ったのに、その他として届く」ことになり、必須にした意味が消えるため
        ' 【どの欄で断ったかも覚える】理由を、その欄の上に出すため（Core/FormErrors.vb）
        Dim field As String = FieldKind
        Dim reason As String = ContactRules.ValidateKind(kindRaw)

        If reason Is Nothing Then
            field = FieldName
            reason = ContactRules.ValidateName(cleanName)
        End If

        If reason Is Nothing Then
            field = FieldEmail
            reason = ContactRules.ValidateEmail(cleanEmail)
        End If

        If reason Is Nothing Then
            field = FieldBody
            reason = ContactRules.ValidateBody(cleanBody)
        End If

        If reason IsNot Nothing Then
            Return New ContactResult With {.Ok = False, .Message = reason, .KeepInput = True, .Field = field}
        End If

        Dim kind As ContactRules.ContactKind

        ' ここへ来る時点で ValidateKind を通っているので、必ず読める
        ContactRules.TryGetKind(kindRaw, kind)

        Return New ContactResult With {
            .Ok = True,
            .Name = cleanName,
            .Email = cleanEmail,
            .Body = cleanBody,
            .Kind = kind}

    End Function


    ' 届いた1件を受け付ける
    '
    ' name・email・body・kindRaw … 画面の入力欄の、そのままの値
    ' honeypot … 人には見えない入力欄の値（何か入っていればロボット。下の【ロボット避け】）
    '
    ' 【確認の画面を通らずに呼ばれても大丈夫なように、ここでぜんぶ確かめ直す】
    ' 確認の画面は親切であって、防御ではない
    Public Shared Function Submit(context As HttpContext,
                                  name As String,
                                  email As String,
                                  kindRaw As String,
                                  body As String,
                                  honeypot As String) As ContactResult

        If context Is Nothing Then
            Return Fail(TroubleMessage)
        End If

        ' ============================================================
        ' 【ロボット避け】人には見えない入力欄に、何か入っていたら捨てる
        ' ============================================================
        '
        ' 【reCAPTCHA は使わない】
        '   ・Google へ訪問者の情報が送られる
        '   ・読み上げソフトを使う人・年配の人にとって難しい
        '
        ' 【「送信しました」と出して、黙って捨てる】断る理由を返すと、ロボットの作り手に
        ' 「この欄が罠だ」と教えることになり、次から空で送ってくる。
        '
        ' 【人が入れてしまう危険を、どう小さくしたか】欄は
        '   ・画面の外へ飛ばしてある（display:none ではなく、位置をずらす）
        '   ・tabindex="-1"（Tab で入らない）
        '   ・aria-hidden="true"（読み上げソフトが読まない）
        '   ・autocomplete="off" ＋ ブラウザの自動入力が狙わない名前
        ' にしてある。それでも入ってしまう人が居たら、その1件は黙って失われる——
        ' そこは承知のうえの割り切りである（reCAPTCHA を入れない代わりに払う代償）
        If Not String.IsNullOrWhiteSpace(honeypot) Then
            Return Done()
        End If

        Dim ip As String = ClientIp(context)

        ' ============================================================
        ' 【連打の歯止め（IP で数える）】AccountReset の RateCounter が見本
        ' ============================================================
        '
        ' 【ここでは「もう上限か」を見るだけで、数えない】
        ' 数えるのは、この下の「同じ内容か」を通り抜けたあとである
        ' ——**同じ内容で断ったぶんを、連打の数にも1時間の数にも入れない**ため。
        '
        ' 【それでも、DB を引く前に止まる】重い処理（DB・メール）へ進む手前で断りたい、
        ' という元の狙いは変わっていない。IsOver は数えないだけで、判定は TryCount と同じ
        If BurstLimiter.IsOver(ip) Then
            Return Fail(TooFastMessage)
        End If

        If HourLimiter().IsOver(ip) Then
            Return Fail(TooManyMessage)
        End If

        ' ============================================================
        ' 入力を確かめる（確認の画面へ進むときと、まったく同じ検査）
        ' ============================================================
        Dim checked As ContactResult = Validate(name, email, kindRaw, body)

        If Not checked.Ok Then
            Return checked
        End If

        Dim record As New ContactStore.ContactRecord With {
            .SenderName = checked.Name,
            .ReplyEmail = checked.Email,
            .Kind = checked.Kind,
            .Body = checked.Body,
            .IpAddress = Truncate(ip, 45),
            .UserAgent = Truncate(ContactRules.NormalizeOneLine(UserAgentOf(context)), 256)}

        ' ============================================================
        ' 【同じ内容を、もう受け付けていないか】
        ' ============================================================
        '
        ' 確認の画面まで「戻る」で戻って、もう一度「送信する」を押すと、同じ内容がもう1件入っていた
        ' （本番の実機テストで開発者が踏んだ）。DB を1回引いて止める。
        '
        ' 【止めたぶんは、数に入れない】この下の TryCount より手前でやっているので、
        ' 連打の3秒にも、1時間の5件にも入らない。
        ' 誤って二度押した人の枠を、こちらの都合で減らさないため。
        '
        ' 【DB が読めなかったら、止めずに進む（フェイルオープン）】
        ' 「DB が正」の作りのとおり。ここで止めると、DB が落ちている間、
        ' **メールだけでも届けられるはずの1件まで捨てることになる**
        If IsDuplicate(record) Then
            Return AlreadyReceived()
        End If

        ' ============================================================
        ' ここで初めて数える（送ることが決まったので）
        ' ============================================================
        '
        ' 【上で IsOver を見ているので、ここで断ることはほぼ無い】
        ' ほとんど同時に2つ来たときだけ。そのときは、ふつうに断る
        If Not BurstLimiter.TryCount(ip) Then
            Return Fail(TooFastMessage)
        End If

        If Not HourLimiter().TryCount(ip) Then
            Return Fail(TooManyMessage)
        End If

        ' ============================================================
        ' DB へ書く（正）
        ' ============================================================

        Dim contactId As Integer = 0
        Dim saved As Boolean = False

        Try
            contactId = ContactStore.Add(record)
            saved = True
        Catch ex As Exception
            ' 【本文・お名前・メールアドレスを、知らせの補足に入れない】メールに載る
            ' （「エラー報告メール」の決まり）。番号も無いので、種類だけを書く
            ErrorReport.Report(ex, "ContactRequest.Submit（DBへ保存）",
                              "問い合わせをDBに保存できませんでした。メールだけでも送れるかを試します。")
        End Try

        Dim subject As String = BuildSubject(checked.Kind)
        Dim mailBody As String = BuildBody(record, contactId, saved)

        ' 【Reply-To は、形を確かめたアドレスだけ】空なら付けない（SiteMail 側でも MailAddress が見る＝守りは2枚）
        Dim replyTo As String = If(checked.Email.Length > 0, checked.Email, Nothing)

        If saved Then

            ' 【並べるだけで、送れたかは待たない】もう DB に在るので、失われない
            SiteMail.Enqueue(subject, mailBody, replyTo)
            Return Done()

        End If

        ' ============================================================
        ' DB に書けなかった。メールだけでも届くかを、その場で試す（最後の手段）
        ' ============================================================
        '
        ' 【ここだけ、呼んだスレッドで待つ】SMTP が止まっている日に長く占めないよう、5秒だけ。
        ' Enqueue（並べるだけ）では「送れたか」が返らないので、ここは SendAndWait を使う
        Dim result As MailSendResult = SiteMail.SendAndWait(subject, mailBody, replyTo, FallbackMailTimeoutMs)

        If result = MailSendResult.Sent Then

            ' メールに中身が入った＝失われていない。利用者には、ふつうに受け付けたと出す
            ' （DB に書けなかったことは、上の ErrorReport で開発者に届いている）
            Return Done()

        End If

        ' 【どちらにも残せなかった】ここだけが「送れませんでした」である
        ErrorReport.ReportProblem(
            "ContactRequest.Submit（どちらにも残せなかった）",
            "問い合わせを、DBにもメールにも残せませんでした（メール＝" & result.ToString() & "）。" &
            "利用者には「時間をおいてもう一度」と出しています。中身は、この知らせに入れていません。")

        Return Fail(TroubleMessage)

    End Function


    ' === メールの件名と本文 ===

    ' 【件名は「[vb-chat」で始める】
    ' .NET の件名の組み立ての罠を、SiteMail.EncodeSubject がそれを前提に避けている
    ' （「エラー報告メール」の【罠】。先頭が素の ASCII でないと、文字化けする道が残る）
    Private Shared Function BuildSubject(kind As ContactRules.ContactKind) As String
        Return "[vb-chat 問い合わせ] " & ContactRules.KindLabel(kind)
    End Function


    ' 【本文は、そのまま読んで返事が書ける形にする】
    ' 返信先のアドレスは Reply-To にも入れてあるが、**本文にも文字として必ず入れる**
    ' （Reply-To が付けられなかったときに、手で貼れば返事が書けるように）
    Private Shared Function BuildBody(record As ContactStore.ContactRecord,
                                      contactId As Integer,
                                      saved As Boolean) As String

        Dim sb As New StringBuilder()

        sb.AppendLine("サイトの「お問い合わせ」から、1件届きました。")
        sb.AppendLine()
        sb.AppendLine("種類　　：" & ContactRules.KindLabel(record.Kind))
        sb.AppendLine("お名前　：" & OrNone(record.SenderName))
        sb.AppendLine("返信先　：" & OrNone(record.ReplyEmail))
        sb.AppendLine("IPアドレス：" & OrNone(record.IpAddress))
        sb.AppendLine("ブラウザ：" & OrNone(record.UserAgent))

        If saved Then
            sb.AppendLine("DBの番号：" & contactId & "（ContactMessages.ContactId）")
        Else
            sb.AppendLine("DBの番号：**保存できませんでした**（このメールが、この1件の唯一の控えです）")
        End If

        sb.AppendLine()
        sb.AppendLine("--- 内容 ---")
        sb.AppendLine(record.Body)

        Return sb.ToString()

    End Function


    ' 空なら「（なし）」（SiteMail.FormatTime が「（なし）」と書いているのと、同じ形にそろえてある）
    Private Shared Function OrNone(text As String) As String
        Return If(String.IsNullOrEmpty(text), "（なし）", text)
    End Function


    ' === 道具 ===

    ' 1時間の上限を数える道具。**設定が変わったら作り直す**
    '
    ' 【なぜ作り直すのか】RateCounter は New のときに上限を決める形なので、
    ' SiteSettings.ContactMaxPerHour を変えてアプリを再起動しない場合（管理画面ができたあと）に、
    ' 古い上限のまま数え続けることになる。数えていた分は作り直しで消えるが、
    ' 上限を変えた直後の1回だけの話なので、そこは割り切る
    Private Shared Function HourLimiter() As RateCounter

        Dim max As Integer = SiteSettings.ContactMaxPerHour

        SyncLock HourLock

            If _hourLimiter Is Nothing OrElse _hourLimiterMax <> max Then
                _hourLimiter = New RateCounter(CountWindow, max)
                _hourLimiterMax = max
            End If

            Return _hourLimiter

        End SyncLock

    End Function


    ' 【SiteSettings は SyncLock の外で読む】
    ' キャッシュが空のときDBへ問い合わせるので、鍵を握ったまま呼ぶとそこで詰まる
    ' （「設定値の置き場所」の【罠】と同じ形。上の HourLimiter は、
    '   読んだあとに鍵を取る形にしてある）

    Private Shared Function ClientIp(context As HttpContext) As String

        Try
            Return If(context.Request.UserHostAddress, "")
        Catch ex As Exception
            Return ""
        End Try

    End Function


    Private Shared Function UserAgentOf(context As HttpContext) As String

        Try
            Return If(context.Request.UserAgent, "")
        Catch ex As Exception
            Return ""
        End Try

    End Function


    ' DB の列幅で切る（切っても種類は読み取れる）
    Private Shared Function Truncate(text As String, maxLength As Integer) As String

        If String.IsNullOrEmpty(text) Then
            Return ""
        End If

        If text.Length <= maxLength Then
            Return text
        End If

        Return text.Substring(0, maxLength)

    End Function


    ' 同じ内容が、少し前にもう入っているか
    '
    ' 【読めなかったら False（＝止めずに進む）】フェイルオープン。
    ' 「DB が正」の作りのとおりで、DB が落ちている間も、メールだけは届けられるようにする。
    ' **ここで例外を投げないこと**——投げると、DB が落ちている日に問い合わせが1件も送れなくなる
    Private Shared Function IsDuplicate(record As ContactStore.ContactRecord) As Boolean

        Dim window As TimeSpan = SiteSettings.ContactDuplicateWindow

        ' 0 なら、この確かめをしない（運用で切れる口）
        If window <= TimeSpan.Zero Then
            Return False
        End If

        Try
            Return ContactStore.HasRecentSame(record, DateTime.Now - window)
        Catch ex As Exception
            ' 【本文・お名前・メールアドレスを、知らせの補足に入れない】メールに載る
            ErrorReport.Report(ex, "ContactRequest.Submit（同じ内容かの確かめ）",
                              "同じ内容がもう入っていないかを確かめられませんでした。止めずに進めます。")
            Return False
        End Try

    End Function


    Private Shared Function Done() As ContactResult
        Return New ContactResult With {.Ok = True, .Message = DoneMessage, .KeepInput = False}
    End Function


    ' 同じ内容を、もう受け付けている
    '
    ' 【Ok = True にしてある】利用者から見れば「さっき送ったものが届いている」＝送れている。
    ' 画面は完了の画面へ行き、文言だけが変わる
    Private Shared Function AlreadyReceived() As ContactResult

        Return New ContactResult With {
            .Ok = True,
            .Message = AlreadyReceivedMessage,
            .KeepInput = False,
            .AlreadyReceived = True}

    End Function


    Private Shared Function Fail(message As String) As ContactResult
        Return New ContactResult With {.Ok = False, .Message = message, .KeepInput = True}
    End Function

End Class
