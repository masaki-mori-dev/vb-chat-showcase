Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 一般ユーザーの登録
'
' 【何を、どの順で確かめるか】をここに持つ（AccountSession・AccountSettings・AccountReset と同じ立ち位置）。
' 画面（Accounts/Register.aspx・Verify.aspx）は、ここを呼んで結果を出すだけ。
' C#／ASP.NET Core MVC へ移るときに持っていく。HttpContext.Current を使わず引数で受け取るのも同じ理由。
'
' 【ダブルオプトイン（確認のメール）にしてある】
' 「登録」を押した時点では仮登録（AccountRegistrations の1行）で、
' 確認のメールのリンクを押してはじめて Accounts へ入る。
' **仕組みは A-2e（パスワードの再設定）をそのまま使い回してある**
'   ・合言葉は 32バイトの乱数（Base64URL で43文字）。DB には SHA-256 だけ
'   ・リンクを開いたら、合言葉を HttpOnly の Cookie へ移して、同じ URL へ 302 で移り直す
'     （アドレスバー・閲覧の履歴・Referer に合言葉を残さない）
'   ・使うのは1回だけ（取り出して消すのを、1つの文の中でやる）
' **片方を直したら、もう片方（AccountReset）も見ること。**
'
' 【A-2e と1つだけ違うところ：メールの宛先】
' 再設定のメールは「アカウントの行のアドレス」へ送るが、
' **確認のメールは、まだアカウントではないアドレスへ送る。**
' つまり「このサイトに、好きなアドレスへメールを送らせる」口である。塞ぎ方は3つ。
'   ① **本文に、利用者の自由な文字を1文字も入れない**（下の【踏み台にされないために】）
'   ② 同じアドレスへは MinResendInterval に1通まで（DB の RequestedAt で決める）
'   ③ IP ごと・サイト全体で、1時間あたりの回数を決める（下の歯止め）
Module AccountRegistration


    ' 結果
    Public Class RegisterResult

        Public Property Ok As Boolean

        ' 画面に出す文言（出口でエスケープする）
        Public Property Message As String

        ' リンクがもう使えない（確認の画面は、ボタンを隠して「もう一度登録する」へ案内する）
        Public Property LinkDead As Boolean

        ' 断ったとき、どの欄が悪いか（下の Field〇〇。どの欄とも言えなければ Nothing）。
        ' 画面がその欄に印を付け、理由がその欄の上に出る（Core/FormErrors.vb）
        Public Property Field As String

    End Class


    ' === どの欄が悪いか（画面の欄の名前ではなく、意味で呼ぶ。画面がどの欄かを決める）===
    Public Const FieldUserName As String = "username"
    Public Const FieldDisplayName As String = "displayname"
    Public Const FieldEmail As String = "email"
    Public Const FieldPassword As String = "password"
    Public Const FieldConfirm As String = "confirm"
    Public Const FieldAgree As String = "agree"

    ' 同意のチェックが無いときの理由（画面の側の確かめ＝Register.aspx.vb の data-error-required にも同じ文を渡す）
    Public Const AgreeRequiredMessage As String = "利用規約に同意してください。"


    ' === 決まり（防御なので SiteSettings に入れない。AccountPassword.MinLength と同じ判断）===

    ' 確認のリンクの期限
    '
    ' 【再設定の30分より、ずっと長くしてある】
    ' パスワードの再設定は「今すぐ困っている人」が押すが、登録の確認は
    ' 「あとでメールを見た人」が押す（夜に登録して、朝に見る）。
    ' 長くしても危なくないのは、合言葉を知っている人にできるのが
    ' 「自分で申し込んだアカウントを作る」ことだけだから
    Public ReadOnly TokenLifetime As TimeSpan = TimeSpan.FromHours(24)

    ' 同じアドレスへ送る間隔（これより短い間に2回申し込まれても、2通目は送らない。答えは同じ）
    Private ReadOnly MinResendInterval As TimeSpan = TimeSpan.FromMinutes(5)

    ' 同じ IP からの申し込み（1時間に5回まで）
    '
    ' 【再設定（15分に5回）より長い窓にしてある】
    ' 登録は、ふつう一生に1回しかしない操作である。5回も要らない
    Private ReadOnly RequestLimiter As New RateCounter(TimeSpan.FromHours(1), 5)

    ' サイト全体の申し込み（1時間に30回まで）
    '
    ' 【IP ごとの歯止めだけでは足りない】IP は簡単に変えられるので、
    ' **「このサイトが1時間に送るメールの本数」そのものに上限を置く**。
    ' 越えたら断る＝まっとうな登録も断られるが、**知らないアドレスへ何百通も送るより、ずっとよい**
    ' （エラー報告メールの「全体で1時間に何通まで」と、同じ考え方）
    Private ReadOnly GlobalLimiter As New RateCounter(TimeSpan.FromHours(1), 30)

    ' 確認の画面で、合言葉が合わなかった回数（同じ IP。10分に10回まで）
    Private ReadOnly TokenMissLimiter As New RateCounter(TimeSpan.FromMinutes(10), 10)

    ' 合言葉の長さ（32バイトの乱数 → Base64URL で43文字）
    Private Const TokenBytes As Integer = 32
    Private Const TokenLength As Integer = 43

    ' リンクの場所と、クエリの名前（メールの本文と画面が使う）
    Public Const VerifyPath As String = "/account/verify"
    Public Const TokenQueryName As String = "t"

    ' 合言葉を一時的に預ける Cookie（アドレスバー・履歴・Referer に合言葉を残さないため）
    Public Const TokenCookieName As String = "AccountVerifyToken"

    ' 登録のあとに連れて行く先（プロフィールの編集の画面）
    '
    ' 【なぜホーム（/account）ではないのか】
    ' 登録した直後の人は、名前しか決めていない。
    ' ブログを持つ人にとって、いちばん先に埋めたいのはプロフィールである
    Public Const AfterVerifyUrl As String = "/account/profile"


    ' どのアドレスか分からない答え（在っても無くても、これだけを返す）
    '
    ' 【「もう登録されています」と絶対に言わない】
    ' 言うと、**このサイトが「そのアドレスの人が会員かどうか」を答える機械になる**。
    ' 登録済みのアドレスには、かわりに
    ' 「このアドレスは登録済みです。パスワードを忘れたときはこちら」のメールを送る
    ' （本人だけが読める場所で答える＝AccountMail.SendAlreadyRegistered）
    ' 【に直した】
    ' 「メールのリンクを開くと、登録が終わります」は、実際はボタンを押して終わるので外した。
    ' 時間はコードの TokenLifetime から出す（画面に数字を書くと、決まりを変えた日にここだけ古く残る）
    Public ReadOnly Property SentMessage As String
        Get
            Return "確認のメールを送りました（リンクは" & CInt(TokenLifetime.TotalHours) & "時間有効）。" &
                   "届かないときは、迷惑メールのフォルダも見てください。"
        End Get
    End Property

    ' 【理由は「期限切れか、使用済み」の2つだけ書く】
    ' 「送り直したため」も実際には起きるが、送り直した人は新しいほうのメールを見るので、書かなくても迷わない。
    ' 「もう一度登録する」のリンクは、Verify.aspx の RetryArea が出す
    Private Const InvalidLinkMessage As String =
        "このリンクは使えません（期限切れか、使用済みです）。"

    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"

    Private Const TooManyMessage As String =
        "登録の申し込みが続いたため、しばらく受け付けません。" &
        "しばらく待ってから、もう一度お試しください。"


    ' ============================================================
    ' ① 申し込みを受け付ける（/account/register）
    ' ============================================================
    '
    ' honeypot … 人には見えない入力欄の値（何か入っていればロボット）
    '
    ' 【確かめる順番】
    '   0. ロボット避け（人には見えない欄に、何か入っていたら黙って捨てる）
    '   1. 歯止め（IP・サイト全体）… DB へ行く前
    '   2. 入力の形（ユーザー名・名前・メールアドレス・パスワード・規約の同意）
    '   3. ユーザー名が空いているか（**ここだけ DB を引いて、答えを返す**）
    '   4. ここから先は、裏の作業でやる（メールアドレスが登録済みかを、時間でも見分けられないように）
    Public Function Begin(context As HttpContext,
                          userName As String, displayName As String, email As String,
                          password As String, confirmPassword As String,
                          agreedToTerms As Boolean, honeypot As String) As RegisterResult

        If context Is Nothing Then
            Return Fail(TroubleMessage)
        End If

        ' === 0. ロボット避け ===
        '
        ' 【問い合わせページ（Contacts/ContactRequest.vb）と、まったく同じ形】
        ' 「送りました」と出して黙って捨てる（断る理由を返すと、ロボットの作り手に
        ' 「この欄が罠だ」と教えることになり、次から空で送ってくる）。
        ' **reCAPTCHA は使わない**（Google へ訪問者の情報が送られるうえ、
        ' 読み上げソフトを使う人・年配の人にとって難しい）
        If Not String.IsNullOrWhiteSpace(honeypot) Then
            Return New RegisterResult With {.Ok = True, .Message = SentMessage}
        End If

        Dim ip As String = ClientIp(context)

        ' === 1. 歯止め（DB を引く前・パスワードを計算する前） ===
        '
        ' 【ここでは「もう上限か」を見るだけで、数えない】（問い合わせページと同じ）
        ' 数えるのは、入力の検査を通り抜けたあとである
        ' ——**打ち間違いで断ったぶんを、回数に入れない**
        If RequestLimiter.IsOver(ip) OrElse GlobalLimiter.IsOver("") Then
            Return Fail(TooManyMessage)
        End If

        ' === 2. 入力の形 ===
        Dim name As String = If(userName, "").Trim()
        Dim shown As String = If(displayName, "").Trim()
        Dim mail As String = If(email, "").Trim()
        Dim secret As String = AccountPassword.Normalize(password)

        ' 【どの欄で断ったかも覚える】理由を、その欄の上に出すため
        Dim field As String = FieldUserName
        Dim reason As String = AccountStore.ValidateUserName(name)

        ' 【予約語は、DB を引く前に落とす】管理人のふりに使える名前・サイトのアドレスとぶつかる名前
        If reason Is Nothing AndAlso AccountUserNames.IsReserved(name) Then
            reason = AccountUserNames.ReservedMessage
        End If

        ' 【名前（表示名）の検査は、AccountSettings に合わせる】
        ' 2つの画面（設定・プロフィール）から変えられる値なので、入口の検査を1か所にそろえてある
        If reason Is Nothing Then
            field = FieldDisplayName
            reason = ValidateDisplayName(shown)
        End If

        If reason Is Nothing Then
            field = FieldEmail
            reason = AccountStore.ValidateEmail(mail)
        End If

        If reason Is Nothing Then
            field = FieldPassword
            reason = AccountPassword.Validate(secret)
        End If

        If reason Is Nothing AndAlso
           Not String.Equals(secret, AccountPassword.Normalize(confirmPassword), StringComparison.Ordinal) Then
            field = FieldConfirm
            reason = "パスワードと、確認のパスワードが違います。"
        End If

        ' 【規約の同意は、いちばん最後に見る】
        ' ほかの欄の打ち間違いで断るときも、チェックは画面に残る（画面側がそうしてある）。
        ' 順番を先にすると「チェックを入れてください」だけが出て、
        ' ほかの欄の間違いに気づくのが1往復あとになる
        If reason Is Nothing AndAlso Not agreedToTerms Then
            field = FieldAgree
            reason = AgreeRequiredMessage
        End If

        If reason IsNot Nothing Then
            Return Fail(reason, field)
        End If

        ' === 3. ユーザー名が空いているか ===
        '
        ' 【ここは答えてよい】ユーザー名はブログのアドレスに出るので、もともと公開の情報である
        ' （メールアドレスの有無とは、扱いがまったく逆）
        Try

            Select Case AccountStore.CheckUserName(name)

                Case AccountStore.UserNameAvailability.Taken
                    Return Fail("そのアカウントIDは、もう使われています。別のIDにしてください。", FieldUserName)

                Case AccountStore.UserNameAvailability.Retired
                    ' 【退会した人の名前】理由は言わない（誰が退会したかを教えることになる）
                    Return Fail(AccountUserNames.ReservedMessage, FieldUserName)

            End Select

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRegistration.Begin（ユーザー名を確かめる）")
            Return Fail(TroubleMessage)
        End Try

        ' ここまで来たら、1回数える（メールを送る側へ進むので）
        RequestLimiter.TryCount(ip)
        GlobalLimiter.TryCount("")

        ' === 4. ここから先は、裏の作業 ===
        '
        ' 【なぜ裏でやるのか】ここでやるのは
        '   ・そのアドレスのアカウントが在るかを引く
        '   ・パスワードをハッシュにする（PBKDF2 10万回＝約0.4秒）
        '   ・メールを1通組み立てる
        ' の3つで、**「アドレスが登録済みか」で中身が変わる。**
        ' 画面がその結果を待つと、**返事の速さの違いで、登録済みかどうかが分かってしまう**
        ' （/account/forgot と、まったく同じ理由）。
        ' 画面は、引く前に答えを返す。
        '
        ' 【リンクの頭は、要求の中で決める】裏の作業には要求が無い（AccountMail.PublicBaseUrl のホスト名の話）
        Dim baseUrl As String = AccountMail.PublicBaseUrl(context.Request)

        Try
            HostingEnvironment.QueueBackgroundWorkItem(
                Sub(ct) SendInBackground(name, shown, mail, secret, ip, baseUrl))
        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRegistration.Begin（裏の作業を並べる）")
            Return Fail(TroubleMessage)
        End Try

        Return New RegisterResult With {.Ok = True, .Message = SentMessage}

    End Function


    ' 名前（表示名）の検査
    '
    ' 【AccountSettings.ChangeDisplayName と同じ決まりにそろえてある】
    ' 「今の名前と同じです」だけは、ここには無い（新しく作るので、比べる相手が居ない）
    Private Function ValidateDisplayName(displayName As String) As String

        If displayName.Length = 0 Then
            Return "名前（表示名）を入力してください。"
        End If

        If displayName.Length > AccountStore.DisplayNameMaxLength Then
            Return "名前（表示名）は" & AccountStore.DisplayNameMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' 裏の作業：アカウントの有無を見て、確認のメールか「もう登録済みです」のメールを送る
    '
    ' 【ここで起きたことは、画面には何も返らない】うまくいかなかったら ErrorReport で知らせる。
    ' **打たれたメールアドレス・パスワードは、知らせに入れない**
    Private Sub SendInBackground(userName As String, displayName As String, email As String,
                                 password As String, ip As String, baseUrl As String)

        Try

            ' 【もう登録済みのアドレスなら、仮登録は作らない】
            ' 作ると、本登録のときに EmailTaken で断ることになる（作る意味が無い）。
            ' かわりに本人のアドレスへ「登録済みです」を1通だけ送る
            Dim existing As AccountStore.AccountRecord = AccountStore.FindForLogin(email)

            If existing IsNot Nothing Then

                ' 【止めているアカウント（IsActive = 0）にも送る】
                ' 本人には「登録済み」であることに変わりが無いし、
                ' 送らないと「止められている」ことが返事の有無から分かってしまう
                AccountMail.SendAlreadyRegistered(existing)
                Return

            End If

            Dim token As String = NewToken()

            ' 【パスワードのハッシュは、ここで作る】0.4秒かかるので、画面を待たせない
            Dim hash As String = AccountPassword.Hash(password)

            ' 【同じアドレスへは5分に1通まで】確かめと書き込みは1つの文（AccountStore.SaveRegistration）
            If Not AccountStore.SaveRegistration(userName, displayName, email, hash, HashToken(token),
                                                 DateTime.Now, ip, TokenLifetime, MinResendInterval) Then
                Return
            End If

            AccountMail.SendRegisterLink(email, userName, baseUrl, token, TokenLifetime)

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRegistration.SendInBackground")
        End Try

    End Sub


    ' ============================================================
    ' ② 確認のリンクを開いたとき（/account/verify）
    ' ============================================================

    ' 合言葉の形か（長さと文字だけを見る。DB は引かない）
    '
    ' 【AccountReset.LooksLikeToken と同じ中身】写しになっているのは、
    ' 長さ（TokenLength）を別々に決められるようにしてあるため。今はどちらも43文字
    Public Function LooksLikeToken(token As String) As Boolean

        If token Is Nothing OrElse token.Length <> TokenLength Then
            Return False
        End If

        For Each ch As Char In token
            If Not (Char.IsLetterOrDigit(ch) AndAlso AscW(ch) < 128) AndAlso ch <> "-"c AndAlso ch <> "_"c Then
                Return False
            End If
        Next

        Return True

    End Function


    ' 合言葉が今も使えるか（画面の出し分け。**消さない**）
    '
    ' 戻り値 … 使えるなら仮登録の1行、使えないなら Nothing（reason に画面へ出す文言が入る）
    '
    ' 【なぜ、リンクを開いただけでは本登録しないのか】（/account/reset と同じ形にした）
    '   ① メールを配る途中の機械（会社の検査・迷惑メールの判定）が、
    '      **本人より先にリンクを開くことがある**。開いただけで本登録が済む形だと、
    '      その機械がアカウントを作り、本人は「使えないリンク」を見ることになる
    '   ② 「押したら何が起きるか」を1枚見せてから確かめるほうが、利用者にとって親切
    ' そこで、開いた時点では**確かめるだけ**にして、ボタン（POST）で本登録する
    Public Function CheckToken(context As HttpContext, token As String, ByRef reason As String) As AccountStore.RegistrationRecord

        reason = Nothing

        Dim ip As String = ClientIp(context)

        If TokenMissLimiter.IsOver(ip) Then
            reason = "リンクの確かめが続いたため、しばらく受け付けません。10分ほど待ってから、もう一度お試しください。"
            Return Nothing
        End If

        If Not LooksLikeToken(token) Then
            TokenMissLimiter.TryCount(ip)
            reason = InvalidLinkMessage
            Return Nothing
        End If

        Try

            Dim found As AccountStore.RegistrationRecord = AccountStore.FindRegistration(HashToken(token))

            If found Is Nothing Then
                TokenMissLimiter.TryCount(ip)
                reason = InvalidLinkMessage
                Return Nothing
            End If

            Return found

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRegistration.CheckToken")
            reason = TroubleMessage
            Return Nothing
        End Try

    End Function


    ' 本登録する（ボタンを押されたとき）
    '
    ' うまくいったら、**その場でログインさせる**（AccountSession.SignInNewAccount）。
    ' 画面は、そのあと AfterVerifyUrl（プロフィールの編集）へ移す。
    '
    ' 【パスワードをもう一度打たせない】仮登録の行にハッシュが入っているので、照合の必要が無い。
    ' 「リンクを開けた＝そのアドレスの持ち主」まで確かめが済んでいる
    Public Function Complete(context As HttpContext, token As String) As RegisterResult

        Dim ip As String = ClientIp(context)

        If TokenMissLimiter.IsOver(ip) Then
            Return Fail("リンクの確かめが続いたため、しばらく受け付けません。10分ほど待ってから、もう一度お試しください。")
        End If

        If Not LooksLikeToken(token) Then
            TokenMissLimiter.TryCount(ip)
            Return Dead(InvalidLinkMessage)
        End If

        Dim account As AccountStore.AccountRecord

        Try

            Dim done As AccountStore.RegistrationResult = AccountStore.CompleteRegistration(HashToken(token))

            Select Case done.Completion

                Case AccountStore.RegistrationCompletion.LinkDead
                    TokenMissLimiter.TryCount(ip)
                    Return Dead(InvalidLinkMessage)

                Case AccountStore.RegistrationCompletion.UserNameTaken
                    Return Dead("待っているあいだに、そのアカウントIDがほかの人のものになりました。" &
                                "お手数ですが、別の名前でもう一度登録してください。")

                Case AccountStore.RegistrationCompletion.EmailTaken
                    Return Dead("そのメールアドレスのアカウントは、もう作られています。" &
                                "ログインの画面から入ってください（パスワードが分からないときは「パスワードを忘れたとき」へ）。")

            End Select

            account = AccountStore.FindById(done.AccountId)

            If account Is Nothing Then
                ' 入れた直後なので、ふつう起きない
                Return Fail(TroubleMessage)
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRegistration.Complete")
            Return Fail(TroubleMessage)
        End Try

        ' 【右上のメニューと更新履歴の控えを捨てる】（Articles/SiteArticles.vb）
        ' あの控えは「有効な administrator のうち、いちばん番号の小さい1人」を覚えている。
        ' 一般会員が増えても中身は変わらないが、**アカウントの表が変わったら捨てる**という形に
        ' そろえておく（将来、控えの引き方を変えた日に、ここを足し忘れないため）
        SiteArticles.Invalidate()

        ' 【「新しいメンバー」の枠に、すぐ出す】
        NewMembers.Invalidate()

        AccountSession.SignInNewAccount(context, account)

        AccountMail.SendWelcome(account, AccountMail.PublicBaseUrl(context.Request))

        Return New RegisterResult With {.Ok = True}

    End Function


    ' ============================================================
    ' 合言葉の Cookie（リンクを開いたら、合言葉を URL から Cookie へ移す）
    ' ============================================================
    '
    ' 【AccountReset の同じ名前の3つと、同じ中身】
    ' 別の Cookie（名前も path も別）にしてあるのは、**再設定の途中に登録の確認を開いても、
    ' 互いの合言葉を上書きしないため**

    Public Sub WriteTokenCookie(response As HttpResponse, token As String)

        Dim cookie As New HttpCookie(TokenCookieName, token) With {
            .HttpOnly = True,
            .Secure = True,
            .Path = VerifyPath,
            .Expires = DateTime.Now.Add(TokenLifetime)
        }
        cookie.SameSite = SameSiteMode.Lax

        response.Cookies.Add(cookie)

    End Sub

    Public Function ReadTokenCookie(request As HttpRequest) As String

        Dim cookie As HttpCookie = request.Cookies(TokenCookieName)
        Return If(cookie Is Nothing, Nothing, cookie.Value)

    End Function

    ' 【path をそろえて消す】そろえないと、消したつもりで別の Cookie を1つ作って終わる
    Public Sub ClearTokenCookie(response As HttpResponse)

        Dim cookie As New HttpCookie(TokenCookieName, "") With {
            .HttpOnly = True,
            .Secure = True,
            .Path = VerifyPath,
            .Expires = DateTime.Now.AddDays(-1)
        }
        cookie.SameSite = SameSiteMode.Lax

        response.Cookies.Add(cookie)

    End Sub


    ' ============================================================
    ' 下回り（AccountReset と同じ中身。あちらを直したら、こちらも見ること）
    ' ============================================================

    ' 32バイトの乱数を Base64URL（=・+・/ を使わない形）にする
    Private Function NewToken() As String

        Dim bytes(TokenBytes - 1) As Byte

        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using

        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)

    End Function

    ' DB に置くのは SHA-256 だけ
    Private Function HashToken(token As String) As Byte()

        Using sha As SHA256 = SHA256.Create()
            Return sha.ComputeHash(Encoding.UTF8.GetBytes(token))
        End Using

    End Function

    Private Function ClientIp(context As HttpContext) As String

        Try
            Return If(context.Request.UserHostAddress, "")
        Catch ex As Exception
            Return ""
        End Try

    End Function

    Private Function Fail(message As String, Optional field As String = Nothing) As RegisterResult
        Return New RegisterResult With {.Ok = False, .Message = message, .Field = field}
    End Function

    Private Function Dead(message As String) As RegisterResult
        Return New RegisterResult With {.Ok = False, .Message = message, .LinkDead = True}
    End Function

End Module
