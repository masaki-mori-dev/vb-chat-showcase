' アカウントのアドレスへ送るメール
'
' 【ここだけが、SiteMail.EnqueueToAccount を呼ぶ】
' 組み立てるのは、決まった3通だけ。
'   ・再設定のリンク（SendResetLink）
'   ・パスワードが変わりました（NotifyPasswordChanged）
'   ・メールアドレスが変わりました（NotifyEmailChanged。**前のアドレスへ**）
' 宛先は、必ずアカウントの行のアドレス（か、変える前のアドレス）。**画面から来た文字列を宛先にしない**。
' 何にでも使える口にすると、このサイトがよその人へのメールの踏み台になる（SiteMail の同じ名前の節）。
'
' 【本文に、パスワードを入れない】再設定のリンクの合言葉は、SendResetLink の本文にだけ入る
' （ログにも、エラー報告の知らせにも入れない。送れなかったときの知らせは SiteMail が番号と理由だけで出す）。
'
Public NotInheritable Class AccountMail

    Private Sub New()
    End Sub

    ' 本番のサイトの頭（https://ホスト）
    '
    ' 【リンクのホスト名を、要求の Host ヘッダーから作らない】（パスワード再設定の毒入れ）
    ' Request.Url から作ると、攻撃者が Host を evil.example にして依頼するだけで、
    ' **本物の合言葉が付いた偽のサイトへのリンクが、本人のメールに届く**。
    ' 本番は決まった値。開発機（接続元がこの機械＝Request.IsLocal）だけ、要求のホストを使う
    ' （IsLocal は接続元のアドレスで決まり、Host ヘッダーでは偽れない）
    ' 【Friend にしてある】お知らせの行き先（Notices/SiteNoticeRules.vb）が、
    ' 「このサイトの住所か」を決めるときにも使う。本番の住所は、ここ1か所にしか書かない
    Friend Const ProductionBaseUrl As String = "https://vb-chat.example.com"

    Public Shared Function PublicBaseUrl(request As HttpRequest) As String

        If request IsNot Nothing AndAlso request.IsLocal Then
            Return request.Url.GetLeftPart(UriPartial.Authority)
        End If

        Return ProductionBaseUrl

    End Function


    ' 再設定のリンクを送る
    '
    ' baseUrl … PublicBaseUrl の値（要求の中で決めてから、裏の作業へ渡す）
    ' token   … 合言葉（Base64URL。DB にはハッシュだけを置いてある）
    Public Shared Sub SendResetLink(account As AccountStore.AccountRecord, baseUrl As String,
                                    token As String, lifetime As TimeSpan)

        If account Is Nothing Then
            Return
        End If

        Dim link As String = baseUrl & AccountReset.ResetPath & "?" & AccountReset.TokenQueryName & "=" & token

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）で、パスワードの再設定を受け付けました。" & vbCrLf &
            "次のリンクを開いて、新しいパスワードを決めてください（" & CInt(lifetime.TotalMinutes) & "分以内・1回だけ使えます）。" & vbCrLf & vbCrLf &
            link & vbCrLf & vbCrLf &
            "心当たりが無ければ、このメールは捨ててください。パスワードは変わりません。" & vbCrLf

        SiteMail.EnqueueToAccount(account.Email, "[vb-chat] パスワードの再設定", body,
                                  account.AccountId, "AccountMail.ResetLink")

    End Sub


    ' 「パスワードが変わりました」を、そのアカウントのアドレスへ
    '
    ' how … どこで変わったか（「設定の画面で」「再設定のリンクで」「非常口で」）
    Public Shared Sub NotifyPasswordChanged(account As AccountStore.AccountRecord, how As String)

        If account Is Nothing Then
            Return
        End If

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）のパスワードが変わりました（" & how & "、" & NowText() & "）。" & vbCrLf &
            "ほかの端末のログインは、すべて切れています。" & vbCrLf & vbCrLf &
            "心当たりが無いときは、すぐにログインの画面の「パスワードを忘れたとき」から、パスワードを決め直してください。" & vbCrLf

        SiteMail.EnqueueToAccount(account.Email, "[vb-chat] パスワードが変わりました", body,
                                  account.AccountId, "AccountMail.PasswordChanged")

    End Sub


    ' 「メールアドレスが変わりました」を、**前のアドレスへ**
    '
    ' 【前のアドレスへ送る理由】乗っ取った人が新しいアドレスに変えても、本人の（前の）アドレスに届く
    Public Shared Sub NotifyEmailChanged(account As AccountStore.AccountRecord, oldEmail As String, newEmail As String)

        If account Is Nothing OrElse String.IsNullOrWhiteSpace(oldEmail) Then
            Return
        End If

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）のアカウントのメールアドレスが、次のアドレスに変わりました（" & NowText() & "）。" & vbCrLf &
            "新しいアドレス：" & newEmail & vbCrLf &
            "このアドレスには、これからはお知らせが届きません。" & vbCrLf & vbCrLf &
            "心当たりが無いときは、アカウントを乗っ取られたおそれがあります。サイトの管理人に知らせてください。" & vbCrLf

        SiteMail.EnqueueToAccount(oldEmail, "[vb-chat] メールアドレスが変わりました", body,
                                  account.AccountId, "AccountMail.EmailChanged")

    End Sub


    ' ============================================================
    ' 一般ユーザーの登録
    ' ============================================================
    '
    ' 【ここだけ、宛先が「アカウントの行のアドレス」ではない】
    ' 登録の確認のメールは、**まだアカウントではないアドレス**へ送る。
    ' つまり「このサイトに、好きなアドレスへメールを送らせる」口である——
    ' ダブルオプトインには避けられない口で、塞ぎ方は Accounts/AccountRegistration.vb の
    ' 先頭のコメント（歯止め3つ）にある。
    '
    ' 【踏み台にされないために：本文に、利用者の自由な文字を1文字も入れない】
    ' ここがいちばん大事なところである。
    ' 名前（表示名）を本文に入れると、**50文字の短い文を、好きなアドレスへ送れる機械**になる
    ' （「〇〇さん」のところに、宣伝の文でも脅しの文でも書ける）。
    ' そこで、本文に入れるのは**ユーザー名だけ**にしてある——
    ' あちらは半角の英数字とハイフン・アンダースコアだけ（3〜30文字）なので、
    ' 空白も日本語も入らず、文にならない。
    ' **この決まりを緩めないこと。** 「せっかくだから名前で呼びかけよう」は、口を開くことになる

    ' 確認のリンクを送る（まだアカウントではないアドレスへ）
    '
    ' email    … 打たれたアドレス（形は ValidateEmail を通っている）
    ' userName … 申し込まれたユーザー名（**本文に入れてよい唯一の、利用者が決めた文字**）
    ' baseUrl  … PublicBaseUrl の値（要求の中で決めてから、裏の作業へ渡す）
    ' token    … 合言葉（Base64URL。DB にはハッシュだけを置いてある）
    '
    ' 【「開くと終わる」と書かない】
    ' 開いた先の画面で「登録を終わらせる」を押して、はじめて終わる。ボタンにしてあるのは、
    ' メールを配る途中の機械がリンクを先に開いても、登録が済んでしまわないため（作りは変えない）
    Public Shared Sub SendRegisterLink(email As String, userName As String, baseUrl As String,
                                       token As String, lifetime As TimeSpan)

        If String.IsNullOrWhiteSpace(email) Then
            Return
        End If

        Dim link As String = baseUrl & AccountRegistration.VerifyPath &
                             "?" & AccountRegistration.TokenQueryName & "=" & token

        Dim body As String =
            "VBチャット（仮）で、次のアカウントIDの登録を受け付けました。" & vbCrLf & vbCrLf &
            "  アカウントID：" & userName & vbCrLf & vbCrLf &
            "次のリンクを開いて『登録を終わらせる』を押すと、登録が終わります（" & CInt(lifetime.TotalHours) & "時間以内・1回だけ使えます）。" & vbCrLf & vbCrLf &
            link & vbCrLf & vbCrLf &
            "心当たりが無ければ、このメールは捨ててください（アカウントは作られません）。" & vbCrLf

        ' 【アカウントの番号は、まだ無い】0 を渡す（SiteMail は、送れなかったときの知らせに使うだけ）
        SiteMail.EnqueueToAccount(email, "[vb-chat] 登録の確認", body, 0, "AccountMail.RegisterLink")

    End Sub


    ' 「そのアドレスは、もう登録されています」を、本人のアドレスへ
    '
    ' 【なぜ、このメールが要るのか】
    ' 登録の画面は「登録済みです」と答えない（答えると、そのアドレスの人が会員かどうかを
    ' 外から確かめられる）。**そのかわり、本人だけが読める場所で答える。**
    ' 本人にとっては「登録したつもりなのに、確認のメールが来ない」がいちばん困るので、
    ' 「もう登録されています」と伝わる道は要る。
    '
    ' 【本文に、打たれた文字は1文字も入れない】上の【踏み台にされないために】と同じ。
    ' 入れるのは、こちらが DB から読んだ値（表示名）だけである
    Public Shared Sub SendAlreadyRegistered(account As AccountStore.AccountRecord)

        If account Is Nothing Then
            Return
        End If

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）で、このメールアドレスでの新規登録が試されました（" & NowText() & "）。" & vbCrLf &
            "このアドレスは、すでに登録されています。新しくアカウントは作られていません。" & vbCrLf & vbCrLf &
            "  アカウントID：" & account.UserName & vbCrLf & vbCrLf &
            "ログインは、こちらから。" & vbCrLf &
            ProductionBaseUrl & "/account/login" & vbCrLf & vbCrLf &
            "パスワードを忘れたときは、こちらから決め直せます。" & vbCrLf &
            ProductionBaseUrl & "/account/forgot" & vbCrLf & vbCrLf &
            "心当たりが無ければ、このメールは捨ててください。何も変わっていません。" & vbCrLf

        SiteMail.EnqueueToAccount(account.Email, "[vb-chat] このアドレスは登録済みです", body,
                                  account.AccountId, "AccountMail.AlreadyRegistered")

    End Sub


    ' 「登録が終わりました」を、そのアカウントのアドレスへ
    '
    ' 【なぜ送るのか】**このメールが、本人の手元に残る唯一の「登録した記録」になる**
    ' （ユーザー名・登録した日・ブログのアドレス）。
    ' 確認のリンクを押した画面は、閉じたら何も残らない
    ' baseUrl … PublicBaseUrl の値（本番は決まった値、開発機だけ要求のホスト）
    '
    ' 【要求の Host ヘッダーから作らない】再設定のリンクと同じ理由（PublicBaseUrl のコメント）。
    ' このメールのリンクは「押させる」ものではなく控えなので、毒を入れられても被害は小さいが、
    ' **メールに入れるリンクは、どれも同じ道で組み立てる**（1つだけ例外を作らない）
    Public Shared Sub SendWelcome(account As AccountStore.AccountRecord, baseUrl As String)

        If account Is Nothing Then
            Return
        End If

        Dim site As String = If(String.IsNullOrEmpty(baseUrl), ProductionBaseUrl, baseUrl)

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）への登録が終わりました（" & NowText() & "）。" & vbCrLf & vbCrLf &
            "  アカウントID：" & account.UserName & vbCrLf &
            "  ブログ：" & site & ArticleRules.AccountUrl(account.UserName) & vbCrLf &
            "  プロフィール：" & site & AccountProfileRules.PublicUrl(account.UserName) & vbCrLf & vbCrLf &
            "管理の画面（記事を書く・プロフィールを直す・設定）は、こちらから。" & vbCrLf &
            site & "/account" & vbCrLf & vbCrLf &
            "アカウントIDは、あとから変えられません（ブログとプロフィールのアドレスに使うため）。" & vbCrLf &
            "名前（表示名）・メールアドレス・パスワードは、設定の画面から変えられます。" & vbCrLf

        SiteMail.EnqueueToAccount(account.Email, "[vb-chat] 登録が終わりました", body,
                                  account.AccountId, "AccountMail.Welcome")

    End Sub


    ' 「退会しました」を、そのアカウントのアドレスへ
    '
    ' 【消す前に積むこと】（Accounts/AccountRetire.vb の順番）
    ' SiteMail.EnqueueToAccount は宛先の文字列を控えるので、積んだあとに行が消えても送られる。
    ' 逆にすると、宛先を読む相手が居ない
    Public Shared Sub NotifyRetired(account As AccountStore.AccountRecord)

        If account Is Nothing Then
            Return
        End If

        Dim body As String =
            account.DisplayName & " さん" & vbCrLf & vbCrLf &
            "VBチャット（仮）の退会の手続きが終わりました（" & NowText() & "）。" & vbCrLf & vbCrLf &
            "  アカウントID：" & account.UserName & vbCrLf & vbCrLf &
            "アカウント・プロフィール・記事・画像は、すべて消えました。元に戻すことはできません。" & vbCrLf &
            "このアカウントIDは、あなたも含めて二度と使えません" &
            "（前の持ち主のふりをされないようにするためです）。" & vbCrLf & vbCrLf &
            "またお使いになるときは、同じメールアドレスで、新しいアカウントIDで登録できます。" & vbCrLf &
            ProductionBaseUrl & "/account/register" & vbCrLf & vbCrLf &
            "心当たりが無いときは、すぐにサイトの管理人へお知らせください。" & vbCrLf &
            ProductionBaseUrl & "/contact" & vbCrLf

        SiteMail.EnqueueToAccount(account.Email, "[vb-chat] 退会が終わりました", body,
                                  account.AccountId, "AccountMail.Retired")

    End Sub
    Private Shared Function NowText() As String
        Return DateTime.Now.ToString("yyyy/MM/dd HH:mm", Globalization.CultureInfo.InvariantCulture)
    End Function

End Class
