' 「名前を決める」＝ このブラウザに新しい身元を配る処理
'
' 【何のための機能か】
' トップページで名前とアイコンを決めて「決定する」を押したときの、サーバー側の入口。
' それまでは、名前・アイコン・色・ChatBrowserId の4つを **画面のJavaScriptが自分で Cookie に書いていた**。
' サーバーは、その Cookie（と JoinRoom の引数）を名前の出どころとして信じるしかなかった＝
' 「名前を変えたら別人」という決まりが、Cookie を1つ書き換えるだけで裏口から破れていた
' （「身元の決まり」）。
' そこで「名前を決める」ことだけをサーバーの1か所に集め、名前には署名した札を添えるようにした（Chat/NameToken.vb）。
'
' 【なぜ .aspx ではなく、クラスに置くのか】
' ChatLogout とまったく同じ理由。近い将来 C#／ASP.NET Core MVC へ移るときに .aspx は捨てるが、
' 「何を、どの順で配るか」という判断はそのまま持っていきたい。
' 受け口（Default.aspx.vb の WebMethod StartChat）は、ここを呼んで結果を返すだけにしてある。
' HttpContext.Current を使わず引数で受け取るのも、同じ理由（移った先に Current は無い）。
'
' 【ログアウトと対になっている】
' ChatLogout.SignOut … 身元と設定を、まとめて捨てる
' ChatLogin.SignIn   … 新しい身元を、まとめて配る
' 片方を直したら、もう片方も見ること（とくに Cookie の属性）。
Module ChatLogin


    ' 「決定する」の結果
    Public Class SignInResult

        ' 受け付けたか
        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す
        Public Property Message As String

    End Class


    ' === このブラウザに、新しい身元を配る ===
    '
    ' 配るもの（すべてサーバーが書く。JS からは書かない）
    '   ChatBrowserId … **毎回、必ず新しい GUID**（下の「必ず振り直す」）
    '   ChatNameToken … 名前の札（HttpOnly。Chat/NameToken.vb）
    '   ChatNickname  … 画面が読む名前（encodeURIComponent 相当。**表示のためだけ**）
    '   ChatIconType / ChatIconColor … 見た目
    '   ChatJustLoggedIn … ログインの目印（寿命60秒。ラウンジが読んで種別50を書き、その場で消す）
    '
    ' 【必ず振り直す】
    ' それまでは「ChatBrowserId が読めたら、その値のまま書き直す」形だった。
    ' 名前を変えたら別人、と決めている以上、名前を決め直す＝身元も作り直すのが筋である。
    ' 使い回していたままだと、**名前の Cookie だけを手で消してトップへ行けば、
    ' 身元（＝無視されている・入室禁止になっている、といった状態）を引き継いだまま名前を変えられた**。
    ' ふつうの利用者は、ログアウトで全部消えてからここへ来るので、振り直しで失うものは無い。
    ' 使い回す形は、まだ「名前を変えても同じ人」だった頃の名残である。
    '
    ' 【古い身元の席は、先に外す】
    ' 身元が変わると「同じブラウザなら1人に絞る」置き換え（JoinRoom の (a)）が効かなくなり、
    ' 部屋の窓を開いたままだと古い自分が最大1時間居残る。ログアウトで踏んだのと同じ不具合なので、
    ' 同じ手当て（ChatHub.LeaveByBrowserId）を、Cookie を書き換える前に通す。
    ' ふつうはここを通らない（ログアウト済みか、初めての人なので古い身元が無い）。
    ' 通るのは、札の鍵が変わって全員がトップへ戻された回などである。
    '
    ' 【トリップの合言葉（ChatTripKey）は消さない】
    ' トリップは「名前を変えても同じ自分だと示す印」で、身元を作り直しても変わらないのが正しい。
    ' ログアウトのときは全部消える（あちらは身元ごと捨てる操作）。
    '
    ' 【showAccountId … アカウントID を表示して参加するか】（チャットとアカウントの紐づけ）
    ' 画面から受け取るのは、この「表示する／しない」の1つだけ。**どのアカウントかは、画面から受け取らない**
    ' ——ログインの札（AccountAuth）から、サーバーが読む（AccountSession.Current）。
    '   ・表示する ＋ ログインしている → 名前の札に、そのアカウントの番号を入れる（NameToken の v2）
    '   ・表示する ＋ ログインしていない → **断る**（黙って「表示しない」で入れると、出ているつもりで話すことになる。
    '     ログアウトしたあとに、戻るボタンで出した古いトップページから押した、など）
    '   ・表示しない → 今までどおりの札（v1）。ログインしていても、アカウントとは結び付けない
    '     （：オフなら今までどおりの参加者として入れる＝**名乗らずに入る自由**）
    ' 嘘をついても（ログインしていないのに True）、断られるだけで他人には何もできない
    Public Function SignIn(context As HttpContext,
                           nickname As String,
                           iconType As String,
                           iconColor As String,
                           showAccountId As Boolean) As SignInResult

        If context Is Nothing Then
            Return Rejected("画面を読み込み直してから、もう一度お試しください。")
        End If

        ' === 名前 ===
        '
        ' 【名前だけは、落とさずに断る】
        ' 見た目（アイコン・色）は、壊れていても既定値へ落として通す。名前は身元そのものなので、
        ' 決まっていなければ断る（「見た目の値は落とす。身元と部屋は断る」＝ChatHub.JoinRoom と同じ線）。
        '
        ' 【長すぎるときも、切らずに断る】
        ' NormalizeNickname は上限で切るが、ここで黙って切ると、本人が決めていない名前が
        ' 札に焼き込まれて以後ずっと使われる。画面（maxlength とJSの検査）でも止めてあるので、
        ' ここへ来るのは直接叩かれたときだけである
        Dim trimmed As String = If(nickname, "").Trim()

        If trimmed.Length = 0 Then
            Return Rejected("名前を入力してください。")
        End If

        Dim maxLength As Integer = ChatProfileValidator.NicknameMaxLength

        If trimmed.Length > maxLength Then
            Return Rejected("名前は" & maxLength & "文字以内で入力してください。")
        End If

        ' 検査は、ハブ・ラウンジの顔・記録と同じものを通す（ここだけ違う形にしない）
        Dim finalNickname As String = ChatProfileValidator.NormalizeNickname(trimmed)

        If String.IsNullOrEmpty(finalNickname) Then
            Return Rejected("名前を入力してください。")
        End If

        Dim finalIconType As String = ChatProfileValidator.NormalizeIconType(iconType)
        Dim finalIconColor As String = ChatProfileValidator.NormalizeIconColor(iconColor)

        ' === アカウントID を表示するなら、誰のものかをログインの札から読む ===
        '
        ' 【DB を1行引く】AccountSession.Current が、札の署名・期限と、DB の3つ（在る・止めていない・世代）を確かめる。
        ' 表示しない人・ログインしていない人は、ここを通らない（DB に触らない）
        Dim linkedAccountId As Integer = 0

        If showAccountId Then

            Dim account As AccountStore.AccountRecord = AccountSession.Current(context)

            If account Is Nothing Then
                Return Rejected("アカウントのログインが切れています。ログインし直すか、「アカウントID」をオフにしてください。")
            End If

            linkedAccountId = account.AccountId

        End If

        ' === 新しい身元 ===
        Dim browserId As Guid = Guid.NewGuid()
        Dim token As String = NameToken.Issue(browserId, finalNickname, linkedAccountId)

        If String.IsNullOrEmpty(token) Then

            ' 札の鍵がどうしても手に入らなかった（ふつう起きない。NameToken が知らせのメールを出している）。
            ' ここで通してしまうと、名前の無い人としてトップへ戻され続ける＝何が起きたか分からないので、断る
            Return Rejected("ただいま混み合っています。少し待ってから、もう一度お試しください。")

        End If

        ' === 古い身元の席を外す（Cookie を書き換える前に） ===
        Dim oldBrowserId As HttpCookie = context.Request.Cookies("ChatBrowserId")

        If oldBrowserId IsNot Nothing AndAlso Not String.IsNullOrEmpty(oldBrowserId.Value) Then
            Try
                ChatHub.LeaveByBrowserId(oldBrowserId.Value)
            Catch ex As Exception
                ' 外せなくても、名前を決めること自体は続ける（ChatLogout.SignOut と同じ判断）
                ErrorReport.Report(ex, "ChatLogin.SignIn（古い身元の席を外す）")
            End Try
        End If

        ' === Cookie を配る ===
        '
        ' 【名前だけ URL エンコードする】日本語が入るため。
        ' アイコンの種類と色は素のまま（"#2563EB" のまま読む場所があるので、まとめて掛けない。
        ' Default.aspx の setCookie に同じ注意が書いてある）
        Dim response As HttpResponse = context.Response

        WriteProfileCookie(response, "ChatBrowserId", browserId.ToString())
        WriteProfileCookie(response, "ChatNickname", Uri.EscapeDataString(finalNickname))
        WriteProfileCookie(response, "ChatIconType", finalIconType)
        WriteProfileCookie(response, "ChatIconColor", finalIconColor)

        ' 札は HttpOnly なので、書き方が違う（NameToken が持つ）
        NameToken.WriteCookie(response, token)

        ' ログインの目印。ラウンジの Page_Load が読んで種別50を書き、その場で消す。
        ' 【寿命60秒】ラウンジへ移るまでの間だけあればよい。届かなかったとき、あとで開いたラウンジを
        ' ログインと取り違えないため、長くしない。
        ' 【名前は SiteVisitLog.JustLoggedInCookieName と同じにしておくこと】片方だけ変えると、
        ' ログインが1件も記録されなくなる（エラーは出ない）
        Dim marker As New HttpCookie(SiteVisitLog.JustLoggedInCookieName, "1")
        ApplyCookieAttributes(marker)
        marker.Expires = DateTime.Now.AddSeconds(60)
        response.Cookies.Set(marker)

        Return New SignInResult With {.Ok = True}

    End Function


    ' 「アカウントID の付け外し」の結果
    Public Class AccountIdChangeResult

        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す
        Public Property Message As String

        ' 付けたあとのアカウント（外したとき・断ったときは 0 と Nothing）
        Public Property AccountId As Integer
        Public Property UserName As String

    End Class


    ' === アカウントID を、付ける・外す（ラウンジの欄から） ===
    '
    '＝トリップと同じように、ラウンジでその場で付け外しできる。**付け外ししても別人にならない。**
    '
    ' 【やること】名前の札（ChatNameToken）だけを書き直す。中身は「同じ身元・同じ名前」のまま、
    ' アカウントの番号だけを入れる（付ける）／抜く（外す）。
    '   ・**ChatBrowserId は変えない**（SignIn と違うところ。変えると別人になり、無視・こっそりが切れる）
    '   ・名前は、今の札から読んだもの（画面から受け取らない）
    '   ・どのアカウントかは、ログインの札から読む（SignIn と同じ。画面から受け取るのは「付けるか」だけ）
    '   ・ほかの Cookie（名前・アイコン・色・ログインの目印）には触らない
    '
    ' 【札に入れる形のままで足りた】札は「身元＋名前＋番号」に署名したもので、身元はそのまま書き直せる
    ' （NameToken.Issue に同じ ChatBrowserId を渡すだけ）。番号を札の外（別の Cookie や表）に持つ形は、
    ' 札と食い違う状態が作れてしまうので採らない。
    '
    ' 【開いている部屋には、入り直してから効く】部屋の接続は、繋いだ時点の Cookie で決めている（トリップと同じ）
    Public Function ChangeAccountIdShown(context As HttpContext, show As Boolean) As AccountIdChangeResult

        If context Is Nothing Then
            Return New AccountIdChangeResult With {.Message = "画面を読み込み直してから、もう一度お試しください。"}
        End If

        Dim request As HttpRequest = context.Request

        Dim browserCookie As HttpCookie = request.Cookies("ChatBrowserId")
        Dim tokenCookie As HttpCookie = request.Cookies(NameToken.CookieName)

        Dim browserIdRaw As String = If(browserCookie Is Nothing, Nothing, browserCookie.Value)
        Dim tokenRaw As String = If(tokenCookie Is Nothing, Nothing, tokenCookie.Value)

        ' 今の名前（札が無い・壊れている・別の身元のものなら Nothing）
        Dim nickname As String = NameToken.ReadNickname(browserIdRaw, tokenRaw)

        Dim browserId As Guid

        If String.IsNullOrEmpty(nickname) OrElse Not Guid.TryParse(browserIdRaw, browserId) Then
            Return New AccountIdChangeResult With {.Message = "名前を決めてから、操作してください。"}
        End If

        Dim accountId As Integer = 0
        Dim userName As String = Nothing

        If show Then

            Dim account As AccountStore.AccountRecord = AccountSession.Current(context)

            If account Is Nothing Then
                Return New AccountIdChangeResult With {
                    .Message = "アカウントのログインが切れています。ログインし直すと、アカウントIDを紐づけられます。"
                }
            End If

            accountId = account.AccountId
            userName = account.UserName

        End If

        Dim token As String = NameToken.Issue(browserId, nickname, accountId)

        If String.IsNullOrEmpty(token) Then
            Return New AccountIdChangeResult With {.Message = "ただいま混み合っています。少し待ってから、もう一度お試しください。"}
        End If

        NameToken.WriteCookie(context.Response, token)

        Return New AccountIdChangeResult With {.Ok = True, .AccountId = accountId, .UserName = userName}

    End Function


    ' 名前・アイコン・身元の Cookie を1つ書く
    '
    ' 【属性は、ほかの Cookie と1文字も違えないこと】
    ' 揃っていないと、ログアウトで消すときに別の Cookie を1つ作って終わる。
    ' HttpOnly は付けない——部屋の画面のJavaScript（Scripts/chat-room.js）が、
    ' 名前・アイコン・色を自分で読んで画面に出しているため（「検討したが、この作りでは無理」）。
    ' 札（ChatNameToken）だけは JS が読む必要が無いので、あちらは HttpOnly にしてある
    Private Sub WriteProfileCookie(response As HttpResponse, name As String, value As String)

        Dim cookie As New HttpCookie(name, value)
        ApplyCookieAttributes(cookie)

        ' 有効期間はサーバーが決める（DB の ProfileCookieDays。既定365日）。
        ' 【札と同じ期限にしておくこと】札だけ先に切れると、名前もアイコンも残っているのに
        ' トップへ戻される（NameToken.WriteCookie も同じ値を使っている）
        cookie.Expires = DateTime.Now.AddDays(SiteSettings.ProfileCookieDays)

        ' Add ではなく Set（Add は同じ名前でも並べて足す）
        response.Cookies.Set(cookie)

    End Sub


    Private Sub ApplyCookieAttributes(cookie As HttpCookie)

        cookie.Path = "/"

        ' 【SameSite=Lax】他のサイトに埋め込まれた形での送信を防ぐ。
        ' Strict にすると、外部リンクから来た1度目で Cookie が送られず、
        ' 名前を決めてあるのにトップページへ戻される
        cookie.SameSite = SameSiteMode.Lax

        ' 【Secure】https でしか送らせない。本番は https 強制なので費用ゼロ。
        ' 開発機の IIS Express も、localhost はブラウザが安全な場所として扱うので http のまま届く
        cookie.Secure = True

    End Sub


    Private Function Rejected(message As String) As SignInResult
        Return New SignInResult With {.Ok = False, .Message = message}
    End Function


End Module
