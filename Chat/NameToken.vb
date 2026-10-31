Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 名前の札
'
' 【何をするものか】
' 「この ChatBrowserId の名前は、これである」ことを、サーバーが署名して Cookie に入れておく。
' 部屋・ラウンジ・ノック・記録で使う名前は、**すべてこの札から読む**。
' クライアントが送ってきた名前（JoinRoom の引数）も、JS から書ける ChatNickname の Cookie も、
' サーバーは名前の出どころとして信じない。
'
' 【なぜ要るのか（塞いだ穴）】
' それまで、部屋へ「新しい1人」として入るときだけは、引数の名前がそのまま通っていた。
' つまり開発者ツールから joinRoom を別の名前で呼ぶ・ChatNickname の Cookie を書き換えて入り直す、で
' **ログアウトせずに名前だけ変えられた**（いったん退室して入り直す・別の部屋へ移る・5秒の猶予を過ぎて F5、の3つ）。
' 「名前を変えたら別人」（「身元の決まり」）が、裏口から破れていた。
' (a)(b) の入り直しは塞いだが、新しく入る経路が残っていた。その最後の1つを塞ぐ。
'
' 【実害は小さい。それでも塞ぐ理由】
' ログアウトすれば正規の手順で別の名前になれるし、無視・入室禁止・こっそりは ChatBrowserId で決まるので、
' この穴で逃げられるわけではない。塞ぐのは、①決まりが裏口から破れること ②サーバーがクライアントの
' 言い値を名前として信じているように見えること（外の人に中を見せるサイトである）の2つのため。
'
' 【形】「v1$（ChatBrowserId の N 形式32桁）$（名前の UTF-8 を Base64）$（HMAC-SHA256 を Base64）」
' 署名の対象は「v1|（32桁）|（名前）」の UTF-8。区切りの前が固定長の16進なので、名前に何が入っても曖昧にならない。
' 先頭の v1 は、あとで形を替えたときに見分けるため（RoomPasswordSecret と同じ作法）。
'
' 【ChatBrowserId に結び付けてある理由】
' 札は誰でも好きな名前で1枚もらえる（トップページで名前を決めるだけ）。結び付けていないと、
' 「別名の札をもらう → 自分の古い ChatBrowserId に戻す」で、身元を保ったまま名前だけ変えられる＝穴が残る。
' 結び付けてあるので、名前を変えるには必ず新しい身元（＝別人）になる。
'
' 【トリップは札に入れていない】
' トリップは合言葉から毎回計算するもので、合言葉を知らなければ他人のトリップは出せない
' （ソルトはサーバーの中にしかない）。名乗らせても他人になりすませないので、札で守る必要が無い。
'
' 【アカウントに結び付けた札（v2）を足した】（チャットとアカウントの紐づけ）
' ログインしている人が、トップページで「アカウントID を表示する」を入れたまま名前を決めると、
' 札に**アカウントの番号**も入る。形は
'   「v2$（32桁）$（名前の Base64）$（アカウントの番号）$（HMAC-SHA256 を Base64）」
' 署名の対象は「v2|（32桁）|（アカウントの番号）|（名前）」。番号は数字だけなので、名前に何が入っても曖昧にならない。
'
'   ・**入れていない人の札は、今までどおり v1**（形を1文字も変えていない）。
'     v1 を読めなくすると、発行した瞬間に全員がトップページへ戻されるため
'   ・札に入れるのは**番号だけ**。ユーザー名は入れない（出すときに毎回 DB から引く＝退会・停止がその場で効く）
'   ・**札だけでは、アカウントID は出ない。** 出すには、同じアカウントのログインの札（AccountAuth）も要る
'     （Chat/ChatAccountLink.vb が両方を確かめる）。ログアウトした・7日で切れた・パスワードを変えた、のあとは出ない
'   ・札に入れる理由＝「表示する／しない」は、名前と同じく**身元を決めたときに決まるもの**にしたいため。
'     あとから書き換えられる Cookie に「表示する」を持たせると、同じ身元のまま出したり消したりできてしまう
'
' 【期限】名前・アイコンの Cookie と同じ（SiteSettings.ProfileCookieDays、既定1年）。
' 札だけ短くすると、名前もアイコンも残っているのに札だけ切れて、トップへ戻されることになる。
Public NotInheritable Class NameToken

    ' 札を入れておく Cookie の名前
    '
    ' 【書くのはサーバーだけ】ChatLogin.SignIn → WriteCookie。HttpOnly なので JS からは読めも書けもしない
    ' （トリップの合言葉 ChatTripKey と同じ扱い）。
    ' JS から読める形にしても札そのものは作れない（鍵が要る）——それでも HttpOnly にするのは、
    ' 画面に何か紛れ込んだとき（XSS）に、札を抜いて別のブラウザで名乗られるのを防ぐため
    Public Const CookieName As String = "ChatNameToken"

    Private Const FormatVersion As String = "v1"

    ' アカウントに結び付けた札の印（上の「v2 を足した」）
    Private Const LinkedFormatVersion As String = "v2"

    ' 鍵の置き場所（App_Data の下。Base64 で32バイトを1行）
    '
    ' 【本番にしか無いファイルの9つ目】git にも発行物にも載せない（.gitignore・.vbproj に登録しない）。
    '
    ' 【部屋のパスワードの鍵（room-password-key.txt）と、わざと別のファイルにしてある】
    ' 壊れたときの直し方が正反対だから。あちらは「在るのに読めないなら、作り直さない」
    '   （作り直すと、それまでの部屋のパスワードが全部「表示できません」になる＝失うものがある）。
    ' こちらは「読めないなら、作り直す」（札は使い捨てで、失うものは「全員が一度トップへ戻る」だけ）。
    ' 1つのファイルを両方で使うと、どちらかが間違った直し方になる
    Private Const KeyRelativePath As String = "~/App_Data/name-token-key.txt"
    Private Const KeyLength As Integer = 32

    Private Shared ReadOnly StateLock As New Object()
    Private Shared _loaded As Boolean
    Private Shared _key As Byte()


    ' 【この仕組みは、絶対に止まらない】
    '
    ' 札が出せない・確かめられない状態になると、**誰もサイトに入れなくなる**
    ' （名前が決まっていない人は、トップへ戻されるため）。トリップのソルトのように
    ' 「読めなければ機能ごと止める」形にはできない。
    '
    ' そこで、鍵は次の順で必ず手に入れる。
    '   1. ファイルが読めて、32バイトなら、それを使う
    '   2. 読めない（無い・壊れている・長さが違う）なら、**新しく作って置く**（知らせのメールを1通）
    '   3. 置くこともできない（権限など）なら、**その場限りの鍵をメモリに持つ**（同じく1通）
    ' 2 と 3 では、それまでに配った札が全部無効になる＝**全員が一度トップへ戻り、名前を決め直す**。
    ' 一時的な身元なので、そこは許容する（開発者の判断）。3 はアプリが再起動するたびに起きるので、
    ' 知らせのメールで気づいて直すこと
    Public Shared ReadOnly Property IsEnabled As Boolean
        Get
            EnsureLoaded()
            Return _key IsNot Nothing
        End Get
    End Property


    ' 札を1枚作る（名前を決めたときだけ呼ぶ）
    '
    ' 戻り値 … 札の文字列。作れなければ Nothing（＝鍵が1つも手に入らなかった。ふつう起きない）
    '
    ' accountId … アカウントID を表示する人なら、そのアカウントの番号（1以上）。表示しない人は 0（＝今までどおりの v1）。
    '             **呼ぶ側（ChatLogin.SignIn）が、ログインの札から確かめた番号だけを渡すこと**（画面から来た数を渡さない）
    Public Shared Function Issue(browserId As Guid, nickname As String, Optional accountId As Integer = 0) As String

        If browserId = Guid.Empty OrElse String.IsNullOrEmpty(nickname) OrElse accountId < 0 Then
            Return Nothing
        End If

        EnsureLoaded()

        If _key Is Nothing Then
            Return Nothing
        End If

        Dim id As String = browserId.ToString("N")
        Dim nameBytes As Byte() = Encoding.UTF8.GetBytes(nickname)

        If accountId > 0 Then

            Dim accountText As String = accountId.ToString(Globalization.CultureInfo.InvariantCulture)

            Return LinkedFormatVersion & "$" & id & "$" & Convert.ToBase64String(nameBytes) & "$" & accountText & "$" &
                   Convert.ToBase64String(ComputeLinkedMac(id, accountText, nickname))

        End If

        Return FormatVersion & "$" & id & "$" & Convert.ToBase64String(nameBytes) & "$" &
               Convert.ToBase64String(ComputeMac(id, nickname))

    End Function


    ' 札から名前を取り出す（その ChatBrowserId のものでなければ Nothing）
    '
    ' browserIdRaw … Cookie に入っている生の値。GUID として読めなければ Nothing を返す
    '                （表記の揺れ＝大文字・小文字・波かっこ、で食い違わないよう、必ず Guid に直してから比べる。
    '                  AnonId・SiteVisitLog と同じ作法）
    Public Shared Function ReadNickname(browserIdRaw As String, tokenRaw As String) As String

        Dim accountId As Integer = 0

        Return ReadCore(browserIdRaw, tokenRaw, accountId)

    End Function


    ' 札に入っているアカウントの番号（結び付けていない札・読めない札なら 0）
    '
    ' 【これだけで「アカウントID を出してよい」と決めないこと】
    ' 札が言っているのは「この身元は、名前を決めたときに、このアカウントとして表示すると決めた」まで。
    ' 今もそのアカウントでログインしているかは、Chat/ChatAccountLink.vb がログインの札と DB で確かめる
    Public Shared Function ReadAccountId(browserIdRaw As String, tokenRaw As String) As Integer

        Dim accountId As Integer = 0

        If ReadCore(browserIdRaw, tokenRaw, accountId) Is Nothing Then
            Return 0
        End If

        Return accountId

    End Function


    ' 札を確かめて、名前を返す（その ChatBrowserId のものでなければ Nothing）。accountId には、札に入っている番号（無ければ 0）
    '
    ' 【名前とアカウントの番号を、同じ1回の確かめで読む】別々の関数で札を割ると、片方だけ直したときに
    ' 「名前は通るのに、番号は署名を見ていない」形ができる
    Private Shared Function ReadCore(browserIdRaw As String, tokenRaw As String, ByRef accountId As Integer) As String

        accountId = 0

        If String.IsNullOrEmpty(browserIdRaw) OrElse String.IsNullOrEmpty(tokenRaw) Then
            Return Nothing
        End If

        Dim browserId As Guid

        If Not Guid.TryParse(browserIdRaw, browserId) OrElse browserId = Guid.Empty Then
            Return Nothing
        End If

        EnsureLoaded()

        If _key Is Nothing Then
            Return Nothing
        End If

        Dim token As String

        Try
            ' 書くときに Uri.EscapeDataString を通してある（TripCode と同じ）。
            ' 【罠】HttpUtility.UrlDecode にしないこと。「+」が空白になる
            token = Uri.UnescapeDataString(tokenRaw)
        Catch ex As Exception
            Return Nothing
        End Try

        Dim parts As String() = token.Split("$"c)

        ' v1（4つ）＝結び付けていない札／v2（5つ）＝アカウントに結び付けた札。それ以外は読まない
        Dim isLinked As Boolean

        If parts.Length = 4 AndAlso parts(0) = FormatVersion Then
            isLinked = False
        ElseIf parts.Length = 5 AndAlso parts(0) = LinkedFormatVersion Then
            isLinked = True
        Else
            Return Nothing
        End If

        ' この札は、この身元のものか
        If Not String.Equals(parts(1), browserId.ToString("N"), StringComparison.Ordinal) Then
            Return Nothing
        End If

        Dim nickname As String
        Dim mac As Byte()

        Try
            nickname = Encoding.UTF8.GetString(Convert.FromBase64String(parts(2)))
            mac = Convert.FromBase64String(parts(If(isLinked, 4, 3)))
        Catch ex As FormatException
            Return Nothing
        End Try

        Dim linkedAccountId As Integer = 0
        Dim expected As Byte()

        If isLinked Then

            ' 【番号は、数字だけの形しか受け付けない】（符号・空白・桁区切りを通さない）
            ' 署名の対象は「書いてあった文字列そのもの」なので、読み替えの余地を残さない
            If Not Integer.TryParse(parts(3), Globalization.NumberStyles.None,
                                    Globalization.CultureInfo.InvariantCulture, linkedAccountId) OrElse
               linkedAccountId <= 0 Then
                Return Nothing
            End If

            expected = ComputeLinkedMac(parts(1), parts(3), nickname)

        Else
            expected = ComputeMac(parts(1), nickname)
        End If

        ' 【時間の差で中身を推測されない比べ方】RoomPassword と同じものを使う
        If Not RoomPassword.FixedTimeEquals(mac, expected) Then
            Return Nothing
        End If

        If String.IsNullOrEmpty(nickname) Then
            Return Nothing
        End If

        accountId = linkedAccountId

        Return nickname

    End Function


    ' ページの要求（HttpRequest）から、今の名前を読む（無ければ Nothing）
    Public Shared Function ReadNickname(request As HttpRequest) As String

        If request Is Nothing Then
            Return Nothing
        End If

        Dim browserCookie As HttpCookie = request.Cookies("ChatBrowserId")
        Dim tokenCookie As HttpCookie = request.Cookies(CookieName)

        If browserCookie Is Nothing OrElse tokenCookie Is Nothing Then
            Return Nothing
        End If

        Return ReadNickname(browserCookie.Value, tokenCookie.Value)

    End Function


    ' 札を Cookie に書く
    '
    ' 【path・Secure・SameSite・期限は、名前・アイコンの Cookie と1文字も違えないこと】
    ' 揃っていないと、ログアウトで消すときに別の Cookie を1つ作って終わる（ChatLogout と同じ注意）
    Public Shared Sub WriteCookie(response As HttpResponse, token As String)

        Dim cookie As New HttpCookie(CookieName, Uri.EscapeDataString(If(token, "")))
        ApplyCookieAttributes(cookie)

        ' SiteSettings はキャッシュが空なら DB を読む。ここは WebMethod の中（SyncLock の外）なので構わない
        cookie.Expires = DateTime.Now.AddDays(SiteSettings.ProfileCookieDays)

        ' Add ではなく Set（Add は同じ名前でも並べて足すので、Set-Cookie が2本出うる）
        response.Cookies.Set(cookie)

    End Sub


    Private Shared Sub ApplyCookieAttributes(cookie As HttpCookie)

        cookie.Path = "/"
        cookie.HttpOnly = True
        cookie.Secure = True
        cookie.SameSite = SameSiteMode.Lax

    End Sub


    Private Shared Function ComputeMac(browserIdN As String, nickname As String) As Byte()

        Using hmac As New HMACSHA256(_key)
            Return hmac.ComputeHash(
                Encoding.UTF8.GetBytes(FormatVersion & "|" & browserIdN & "|" & nickname))
        End Using

    End Function


    ' アカウントに結び付けた札（v2）の署名。【v1 と対象の形が違う】先頭の印が違うので、
    ' v1 の署名を v2 の札に貼り替える（またはその逆）ことはできない
    Private Shared Function ComputeLinkedMac(browserIdN As String, accountText As String, nickname As String) As Byte()

        Using hmac As New HMACSHA256(_key)
            Return hmac.ComputeHash(
                Encoding.UTF8.GetBytes(LinkedFormatVersion & "|" & browserIdN & "|" & accountText & "|" & nickname))
        End Using

    End Function


    Private Shared Sub EnsureLoaded()

        If _loaded Then
            Return
        End If

        SyncLock StateLock

            If _loaded Then
                Return
            End If

            _key = LoadOrCreateKey()
            _loaded = True

        End SyncLock

    End Sub


    ' 鍵を手に入れる（上の IsEnabled のコメントの 1 → 2 → 3）
    Private Shared Function LoadOrCreateKey() As Byte()

        Dim path As String = Nothing

        Try
            path = HostingEnvironment.MapPath(KeyRelativePath)
        Catch ex As Exception
            path = Nothing
        End Try

        If String.IsNullOrEmpty(path) Then

            ' App_Data の場所すら分からない。その場限りの鍵で動かす（3）
            Report("App_Data の場所が分かりませんでした。その場限りの鍵で動かします" &
                   "（アプリが再起動するたびに、全員がトップページへ戻ります）。")

            Return RandomKey()

        End If

        Try

            If File.Exists(path) Then

                Dim raw As String = File.ReadAllText(path).Trim()
                Dim key As Byte() = Nothing

                Try
                    key = Convert.FromBase64String(raw)
                Catch ex As FormatException
                    key = Nothing
                End Try

                If key IsNot Nothing AndAlso key.Length = KeyLength Then
                    Return key
                End If

                ' 在るのに使えない。**作り直す**（RoomPasswordSecret とは逆の判断。上の KeyRelativePath のコメント）
                Report("App_Data\name-token-key.txt が読めなかったので、新しい鍵を作り直します" &
                       "（全員が一度トップページへ戻り、名前を決め直すことになります）。")

                Return CreateKey(path, replaceExisting:=True)

            End If

        Catch ex As Exception

            Report("App_Data\name-token-key.txt を読めませんでした（" & ex.Message & "）。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、全員がトップページへ戻ります）。")

            Return RandomKey()

        End Try

        Return CreateKey(path, replaceExisting:=False)

    End Function


    ' 鍵を作って置く。置けなければ、その場限りの鍵をメモリに持つ
    '
    ' 【FileMode.CreateNew】同時に2つのアプリ（発行の瞬間の新旧など）が作ろうとしても、片方だけが作る。
    ' 負けたほうは、勝ったほうのファイルを読み直す（RoomPasswordSecret と同じ）
    Private Shared Function CreateKey(path As String, replaceExisting As Boolean) As Byte()

        Dim key As Byte() = RandomKey()

        Try

            Dim mode As FileMode = If(replaceExisting, FileMode.Create, FileMode.CreateNew)

            Using fs As New FileStream(path, mode, FileAccess.Write)
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(Convert.ToBase64String(key))
                fs.Write(bytes, 0, bytes.Length)
            End Using

            If Not replaceExisting Then
                Report("App_Data\name-token-key.txt が無かったので、新しい鍵を作りました。" &
                       "それより前に名前を決めていた人は、一度トップページへ戻って名前を決め直すことになります。" &
                       "初めて動かしたときなら、これで正常です。")
            End If

            Return key

        Catch ex As IOException When File.Exists(path)

            ' ほかの誰かが先に作った。そちらを読む
            Try
                Dim other As Byte() = Convert.FromBase64String(File.ReadAllText(path).Trim())
                If other.Length = KeyLength Then
                    Return other
                End If
            Catch
            End Try

            Report("App_Data\name-token-key.txt を作ろうとしたら、読めないファイルが先にありました。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、全員がトップページへ戻ります）。")

            Return key

        Catch ex As Exception

            Report("App_Data\name-token-key.txt を置けませんでした（" & ex.Message & "）。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、全員がトップページへ戻ります）。")

            Return key

        End Try

    End Function


    Private Shared Function RandomKey() As Byte()

        Dim key(KeyLength - 1) As Byte

        Using rng As New RNGCryptoServiceProvider()
            rng.GetBytes(key)
        End Using

        Return key

    End Function


    ' 知らせる（鍵の中身は入れない）
    Private Shared Sub Report(message As String)

        ErrorReport.ReportProblem("NameToken.LoadKey", "名前の札の鍵： " & message)

    End Sub

End Class
