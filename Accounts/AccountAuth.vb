Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' ログインの札
'
' 【何をするものか】
' 「このブラウザは、このアカウントとしてログイン中である」ことを、サーバーが署名して Cookie に入れる。
' 名前の札（Chat/NameToken.vb）と、まったく同じ考え方である。
'
' 【なぜ Session ではないのか】
' 共用サーバーはアクセスが無いとアプリを寝かせるので、Session はいつ消えるか分からない
' （寝た瞬間に、書きかけの記事の途中でログアウトさせられることになる）。
' 署名した Cookie なら、サーバーが覚えるのは鍵1本だけで済む。
' 「クライアントに名乗らせない」の2つのやり方のうち、②署名したものを持たせる
' 。
'
' 【形】「v1$（アカウントID）$（鍵の世代）$（期限のUNIX秒）$（HMAC-SHA256 を Base64）」
' 署名の対象は、その前半を「|」で区切り直した文字列の UTF-8。
' 区切りの間は数字しか入らないので、名前の札のように曖昧になることがない。
'
' 【期限を、署名の中にも入れてある】
' Cookie の Expires はブラウザ側の話で、利用者が自由に伸ばせる（開発者ツールで書き換えられる）。
' 署名の中に入れておけば、伸ばした瞬間に署名が合わなくなる。
' **延長はしない**（使っている間に伸びる形にしない。SiteSettings.AccountLoginCookieDays ＝ 既定7日で、
' 必ず入り直す。開発者と）
'
' 【鍵の世代（Accounts.TokenGeneration）を入れてある理由】
' 署名 Cookie だけだと、盗まれても無効にできない（サーバー側に状態が無いため）。
' 世代を Cookie にも入れて毎回突き合わせ、**パスワードを変えたら1つ進める**＝
' それまでに配った Cookie が全部その場で無効になる。
'
' 【名前の札の鍵とは、別のファイルにしてある】
' どちらも「壊れたら作り直してよい鍵」なので共用は成り立つが、用途の違うものを1本に乗せると、
' 片方の寿命をもう片方に押し付けることになる
' （名前の札の鍵を作り直した日に、管理人まで巻き込んでログアウトさせる理由が無い）。
' 壊れたときの直し方は、名前の札と同じ「作り直す」である
' （失うのは「管理人がもう一度ログインする」ことだけ。部屋のパスワードの鍵＝
'  Chat/RoomPasswordSecret.vb とは逆で、あちらは作り直すと既存の部屋のパスワードが全部見えなくなる）。
Public NotInheritable Class AccountAuth

    Private Sub New()
    End Sub


    ' 札を入れておく Cookie の名前
    '
    ' 【HttpOnly】画面の JavaScript は、この値を1度も読まない。
    ' 名前・アイコンの Cookie（部屋の画面が読む）とは事情が違うので、隠せる
    Public Const CookieName As String = "AccountAuth"

    Private Const FormatVersion As String = "v1"

    ' 鍵の置き場所（App_Data の下。Base64 で32バイトを1行）
    '
    ' 【本番にしか無いファイルの10個目】git にも発行物にも載せない
    ' （.gitignore に入れ、.vbproj には登録しない）
    Private Const KeyRelativePath As String = "~/App_Data/account-auth-key.txt"
    Private Const KeyLength As Integer = 32

    Private Shared ReadOnly StateLock As New Object()
    Private Shared _loaded As Boolean
    Private Shared _key As Byte()


    ' 札から読み取った中身
    Public Class Ticket
        Public Property AccountId As Integer
        Public Property Generation As Integer
    End Class


    ' 札を1枚作る（ログインに成功したときだけ呼ぶ）
    '
    ' lifetime … この長さで切れる。**あとから延ばさない**
    ' 戻り値 … 札の文字列。鍵が手に入らなければ Nothing
    Public Shared Function Issue(accountId As Integer, generation As Integer, lifetime As TimeSpan) As String

        If accountId <= 0 Then
            Return Nothing
        End If

        EnsureLoaded()

        If _key Is Nothing Then
            Return Nothing
        End If

        Dim expires As Long = ToUnixSeconds(DateTime.UtcNow + lifetime)
        Dim payload As String = BuildPayload(accountId, generation, expires)

        Return payload & "$" & Convert.ToBase64String(ComputeMac(payload))

    End Function


    ' 札を確かめる（合っていなければ Nothing）
    '
    ' 【ここで見るのは、札そのものの正しさだけである】
    ' 「そのアカウントが今も有効か」「世代が合っているか」は DB を見ないと分からないので、
    ' Accounts/AccountSession.vb の Current が受け持つ。**片方だけで通さないこと**
    Public Shared Function Read(tokenRaw As String) As Ticket

        If String.IsNullOrEmpty(tokenRaw) Then
            Return Nothing
        End If

        EnsureLoaded()

        If _key Is Nothing Then
            Return Nothing
        End If

        Dim token As String

        Try
            ' 書くときに Uri.EscapeDataString を通してある（NameToken・TripCode と同じ）。
            ' 【罠】HttpUtility.UrlDecode にしないこと。「+」が空白になる
            token = Uri.UnescapeDataString(tokenRaw)
        Catch ex As Exception
            Return Nothing
        End Try

        Dim parts As String() = token.Split("$"c)

        If parts.Length <> 5 OrElse parts(0) <> FormatVersion Then
            Return Nothing
        End If

        Dim accountId As Integer
        Dim generation As Integer
        Dim expires As Long
        Dim culture = Globalization.CultureInfo.InvariantCulture

        Dim readable As Boolean =
            Integer.TryParse(parts(1), Globalization.NumberStyles.None, culture, accountId) AndAlso
            Integer.TryParse(parts(2), Globalization.NumberStyles.None, culture, generation) AndAlso
            Long.TryParse(parts(3), Globalization.NumberStyles.None, culture, expires)

        If Not readable OrElse accountId <= 0 Then
            Return Nothing
        End If

        Dim mac As Byte()

        Try
            mac = Convert.FromBase64String(parts(4))
        Catch ex As FormatException
            Return Nothing
        End Try

        ' 【時間の差で中身を推測されない比べ方】部屋のパスワードと同じものを使う
        If Not RoomPassword.FixedTimeEquals(mac, ComputeMac(BuildPayload(accountId, generation, expires))) Then
            Return Nothing
        End If

        ' 期限は、署名を確かめたあとで見る
        ' （先に見ると、期限だけ書き換えた札と本物の期限切れの札を、かかった時間で見分けられうる）
        If expires <= ToUnixSeconds(DateTime.UtcNow) Then
            Return Nothing
        End If

        Return New Ticket With {.AccountId = accountId, .Generation = generation}

    End Function


    ' 札を Cookie に書く
    '
    ' 【属性は、ほかの Cookie とそろえてある】path=/・Secure・SameSite=Lax。
    ' HttpOnly だけは、名前の札（ChatNameToken）と同じく付ける（JS が読む必要が無いため）
    Public Shared Sub WriteCookie(response As HttpResponse, token As String, lifetime As TimeSpan)

        Dim cookie As New HttpCookie(CookieName, Uri.EscapeDataString(If(token, "")))
        ApplyCookieAttributes(cookie)

        ' 【Cookie の期限と、札の中の期限を同じにしておくこと】
        ' 揃っていないと、ブラウザには残っているのにサーバーが断る（または逆）状態ができ、
        ' 「ログインしているのに、押すと必ずログイン画面へ戻される」という分かりにくい形になる
        cookie.Expires = DateTime.Now + lifetime

        ' Add ではなく Set（Add は同じ名前でも並べて足すので、Set-Cookie が2本出うる）
        response.Cookies.Set(cookie)

    End Sub


    ' 札を消す（ログアウト）
    '
    ' 【path・Secure・SameSite を、書いたときと揃えること】
    ' Cookie の身元は「名前・ドメイン・path」で決まる。揃っていないと、
    ' 消したつもりで別の Cookie を1つ作って終わる（ChatLogout に同じ注意がある）
    Public Shared Sub ClearCookie(response As HttpResponse)

        Dim cookie As New HttpCookie(CookieName, String.Empty)
        ApplyCookieAttributes(cookie)
        cookie.Expires = New DateTime(1999, 1, 1)

        response.Cookies.Set(cookie)

    End Sub


    Private Shared Sub ApplyCookieAttributes(cookie As HttpCookie)

        cookie.Path = "/"
        cookie.HttpOnly = True
        cookie.Secure = True
        cookie.SameSite = SameSiteMode.Lax

    End Sub


    Private Shared Function BuildPayload(accountId As Integer, generation As Integer, expires As Long) As String

        Dim culture = Globalization.CultureInfo.InvariantCulture

        Return FormatVersion & "$" &
               accountId.ToString(culture) & "$" &
               generation.ToString(culture) & "$" &
               expires.ToString(culture)

    End Function


    Private Shared Function ComputeMac(payload As String) As Byte()

        ' 署名の対象は「$」ではなく「|」で区切る（札の文字列そのものと取り違えないため）
        Using hmac As New HMACSHA256(_key)
            Return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.Replace("$"c, "|"c)))
        End Using

    End Function


    Private Shared Function ToUnixSeconds(utc As DateTime) As Long
        Return CLng((utc - New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds)
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


    ' 鍵を手に入れる（名前の札＝Chat/NameToken.vb とまったく同じ順）
    '   1. ファイルが読めて、32バイトなら、それを使う
    '   2. 読めない（無い・壊れている・長さが違う）なら、新しく作って置く（知らせのメールを1通）
    '   3. 置くこともできないなら、その場限りの鍵をメモリに持つ（同じく1通）
    '
    ' 2 と 3 で失うのは「ログイン中の人が、もう一度ログインする」ことだけである
    Private Shared Function LoadOrCreateKey() As Byte()

        Dim path As String = Nothing

        Try
            path = HostingEnvironment.MapPath(KeyRelativePath)
        Catch ex As Exception
            path = Nothing
        End Try

        If String.IsNullOrEmpty(path) Then

            Report("App_Data の場所が分かりませんでした。その場限りの鍵で動かします" &
                   "（アプリが再起動するたびに、ログインし直しになります）。")

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

                Report("App_Data\account-auth-key.txt が読めなかったので、新しい鍵を作り直します" &
                       "（ログイン中の人は、もう一度ログインすることになります）。")

                Return CreateKey(path, replaceExisting:=True)

            End If

        Catch ex As Exception

            Report("App_Data\account-auth-key.txt を読めませんでした（" & ex.Message & "）。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、ログインし直しになります）。")

            Return RandomKey()

        End Try

        Return CreateKey(path, replaceExisting:=False)

    End Function


    ' 鍵を作って置く。置けなければ、その場限りの鍵をメモリに持つ
    '
    ' 【FileMode.CreateNew】同時に2つのアプリ（発行の瞬間の新旧など）が作ろうとしても、片方だけが作る。
    ' 負けたほうは、勝ったほうのファイルを読み直す（NameToken・RoomPasswordSecret と同じ）
    Private Shared Function CreateKey(path As String, replaceExisting As Boolean) As Byte()

        Dim key As Byte() = RandomKey()

        Try

            Dim mode As FileMode = If(replaceExisting, FileMode.Create, FileMode.CreateNew)

            Using fs As New FileStream(path, mode, FileAccess.Write)
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(Convert.ToBase64String(key))
                fs.Write(bytes, 0, bytes.Length)
            End Using

            If Not replaceExisting Then
                Report("App_Data\account-auth-key.txt が無かったので、新しい鍵を作りました。" &
                       "それより前にログインしていた人は、もう一度ログインすることになります。" &
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

            Report("App_Data\account-auth-key.txt を作ろうとしたら、読めないファイルが先にありました。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、ログインし直しになります）。")

            Return key

        Catch ex As Exception

            Report("App_Data\account-auth-key.txt を置けませんでした（" & ex.Message & "）。" &
                   "その場限りの鍵で動かします（アプリが再起動するたびに、ログインし直しになります）。")

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

        ErrorReport.ReportProblem("AccountAuth.LoadKey", "ログインの札の鍵： " & message)

    End Sub

End Class
