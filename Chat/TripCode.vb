Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' トリップ（名前の下に出す、10桁の英数字の印）を計算するクラス
'
' 【トリップとは何か】
' アカウントを作らずに「自分が同じ人物である」ことを示すための印である。
' 利用者が合言葉を入れると、その人だけに決まった16進10桁（例 a1b2c3d4e5）が出る。
' 同じ合言葉を入れれば、名前やアイコンを変えても、別の日でも、同じ印が出る。
'
' 【いちばん大事な性質＝表示するだけで、それ以外は何もしない】
' 仕組みとして身元に効くわけではなく、人が見て「同じ人だ」と分かるだけである。
' 無視・強制退室・入室禁止などとは、一切つながらない。
' **このクラスの値を、判定（誰を通すか・誰を隠すか）に使わないこと**
'
' 【計算】HMAC-SHA256（鍵＝サーバーの秘密のソルト、中身＝利用者が入れた合言葉）の
' 先頭5バイトを、16進の小文字10桁にしたもの。
'
' 【なぜソルトが要るのか】
' 元祖2ちゃんねるのトリップは、材料（計算のしかた）が公開されていたので、
' 手元で総当たりされて破られた。ソルトがサーバーの中にしか無ければ、
' 手元では1回も計算できない。
'
' 【16進にしてある理由】
' 人が目で見て「同じだ」と判断するものなので、0 と O、1 と l と I が混ざる文字集合は使えない。
' 16進（0-9 と a-f）には、紛らわしい組が入っていない。
'
' 【罠】材料に ChatBrowserId を混ぜないこと
' 混ぜると、ログアウトして別人になるたびにトリップも変わる。
' 「名前を変えても同じ自分だと示す」という当の役目が、いちばん要る場面で消え、
' しかもエラーは出ない。材料は「ソルト ＋ 合言葉」の2つだけ。
' （部屋IDも混ぜない。混ぜると部屋ごとに印が変わり、同じく役目が消える。
'   AnonId が roomId を混ぜているのは、あちらが「部屋をまたいで突き合わせられないこと」を
'   守る印だからで、トリップとは目的が正反対である）
Public NotInheritable Class TripCode

    ' 合言葉を入れておく Cookie の名前
    '
    ' 【書くのはサーバーだけ】WriteKeyCookie ／ ClearKeyCookie。
    ' HttpOnly なので、JS からは読むことも書くこともできない（下の WriteKeyCookie を参照）。
    '
    ' 【中身の約束】値は Uri.EscapeDataString でエンコードしたものを入れる。
    ' 読むときは FromCookieValue が戻す。
    ' 合言葉には日本語も入りうるので、生のまま Cookie に入れてはいけない
    Public Const CookieName As String = "ChatTripKey"

    ' トリップの桁数（16進）
    '
    ' ChatMessages.Trip 列の幅（NVARCHAR(10)）と同じ数にしておくこと。
    ' 列より長くすると、ログの INSERT が例外になり、ChatLog.Write の Try/Catch に
    ' 飲まれて**その人のログだけが1行も残らない**（NicknameMaxLength の罠と同じ形）
    Public Const Length As Integer = 10

    ' 合言葉の長さの上限（UTF-16の単位。String.Length と JS の .length と同じ数え方）
    '
    ' 【超えたら、切らずに断る（トリップを出さない）】
    ' 切る形にすると、この上限を変えた日に、上限より長い合言葉を使っていた人の
    ' トリップが**黙って別の値に変わる**（上げても下げても変わる）。
    ' 断る形なら、上限を上げても誰のトリップも変わらない。
    ' 人が「同じ印だ」と信じて見るものなので、黙って変わるのがいちばん困る。
    '
    ' 【SiteSettings（管理画面から変えられる箱）に入れていない理由】
    '  1. 下げると、それより長い合言葉の人のトリップが消える。
    '     管理画面から1つ数字を変えるだけで壊せる箱になり、2段目の丸めでも防げない
    '  2. これは調整したい値ではなく、Cookie の大きさを守る線である。
    '     Cookie はこのサイトへの**すべての要求**（画像・JS も含む）に乗る。
    '     日本語はエンコードすると1文字9バイトになるので、32文字で最大288バイト
    '  3. 変えたくなる場面が思いつかない
    ' 画面（入力欄の maxlength）へ渡すときは、この定数から渡すこと。
    ' 画面に数字を書かない
    Public Const MaxKeyLength As Integer = 32

    ' ソルトの置き場所（App_Data の下のテキストファイル1行）
    '
    ' git にも発行物にも載せない（.gitignore で除外、.vbproj に登録しない）。
    ' 環境ごとに手で置く。開発機と本番で同じ値を使う。
    ' App_Data は ASP.NET が外へ配信しないので、URLを打っても中身は出ない。
    ' **SiteSettings（DB）には絶対に入れないこと**——
    ' 管理画面から見える箱に入れた時点で、手元で計算できるようになり、なりすまし防止が死ぬ。
    '
    ' 【中身の形】1行だけ。前後の空白と改行（BOMも）は無視する。
    ' 行の中に空白や改行が混ざっていたら、止める（下の LoadSalt）。
    ' 説明の行（「#」で始まる行など）も書けない形にしてある——
    ' 書けるようにすると、書き方を1つ間違えただけで**説明の文章がソルトとして読まれ、
    ' 全員のトリップが黙って変わる**。そうなるくらいなら止まるほうがよい。
    ' 説明は、このクラスのコメントに書く。
    '
    ' 【、XML（appSettings file=）をやめてこの形にした】
    ' Web.config の <appSettings file="App_Data\secrets.config"> で読んでいたが、
    ' **そのファイルがXMLとして1文字壊れると、サイト全体が構成エラー（500）で落ちた**
    ' （開発機で実測。トップページも含めて全部）。ASP.NET が設定を読む段階で止まるので、
    ' こちらの Try/Catch も届かない。トリップは飾りなので、壊れても止まるのはトリップだけ、
    ' という形のほうが割に合う。**<appSettings file> に戻さないこと**
    Private Const SaltRelativePath As String = "~/App_Data/trip-salt.txt"

    ' ソルトとして認める最低の長さ
    '
    ' 「test」のような仮の値が入ったまま動くのを防ぐ。
    ' 短いソルトで作ったトリップは、あとで正しいソルトに差し替えた瞬間に全部変わるので、
    ' **最初から動かさない（フェイルクローズ）ほうへ倒す**
    Private Const MinSaltLength As Integer = 32

    ' ソルト（HMACの鍵）。Nothing ならトリップ機能ごと止まっている
    '
    ' 【読むのは、このクラスが最初に使われた1回だけ】
    ' SiteSettings が遅延読み込みにしているのは「DBは起動の瞬間に詰まっていることがあり、
    ' 共用サーバーでは起動の時機を選べない」からだが、こちらはDBではなく
    ' ディスク上の設定ファイルなので、その心配が無い。
    ' それでも Application_Start に書かないのは、AnonId と同じく
    ' Global.asax に初期化処理を溜めないため（効果は同じ）。
    '
    ' 【止まったら、アプリを再起動するまで止まったまま】
    ' 途中で読み直す形にはしていない。途中で動き出すと、止まっていた間の発言だけ
    ' トリップが空になり、「同じ人だった」の証明が途切れる。
    ' ファイルを置いたら、アプリの再起動（発行など）で動き出す
    Private Shared ReadOnly Salt As Byte() = LoadSalt()


    ' トリップ機能が動いているか（ソルトが読めているか）
    '
    ' False のときは、誰のトリップも計算せず、画面へも一切渡さない。
    ' 過去ログに焼き込まれているトリップも出さない（ChatLogReader が見ている）
    Public Shared ReadOnly Property IsEnabled As Boolean
        Get
            Return Salt IsNot Nothing
        End Get
    End Property


    ' 合言葉から、トリップを計算する
    '
    ' 戻り値 … 16進10桁。次のときは Nothing（＝トリップ無し）
    '   ・トリップ機能が止まっている
    '   ・合言葉が空（空白だけを含む）
    '   ・合言葉が MaxKeyLength を超えている
    '
    ' 【合言葉は、前後の空白を落とすだけ。中身は触らない】
    ' 大文字・小文字も、全角・半角も、そのまま区別する（合言葉は本人が決めたものなので、
    ' こちらの都合で「同じ」と見なす範囲を広げない）。
    ' String.Trim は全角の空白（U+3000）も落とす
    Public Shared Function Compute(key As String) As String

        If Salt Is Nothing Then
            Return Nothing
        End If

        If key Is Nothing Then
            Return Nothing
        End If

        key = key.Trim()

        If key.Length = 0 OrElse key.Length > MaxKeyLength Then
            Return Nothing
        End If

        Using hmac As New HMACSHA256(Salt)

            Dim hash As Byte() = hmac.ComputeHash(Encoding.UTF8.GetBytes(key))

            Dim sb As New StringBuilder(Length)
            For i As Integer = 0 To (Length \ 2) - 1
                sb.Append(hash(i).ToString("x2"))
            Next

            Return sb.ToString()

        End Using

    End Function


    ' Cookie に入っていた値（エンコード済み）から、トリップを計算する
    '
    ' 【Uri.UnescapeDataString で戻すこと。HttpUtility.UrlDecode にしないこと】
    ' UrlDecode は「+」を空白に変える（フォームの書き方の約束）。
    ' 書く側の Uri.EscapeDataString（WriteKeyCookie）は「+」を %2B にするので、
    ' 正しく戻すのは、対になる UnescapeDataString のほう。
    ' 取り違えると、「+」を含む合言葉の人だけトリップが別の値になり、エラーは出ない
    Public Shared Function FromCookieValue(rawCookieValue As String) As String

        If Salt Is Nothing OrElse String.IsNullOrEmpty(rawCookieValue) Then
            Return Nothing
        End If

        Dim key As String

        Try
            key = Uri.UnescapeDataString(rawCookieValue)
        Catch ex As Exception
            ' 戻せない値（壊れたエンコード）なら、トリップ無しとして扱う
            Return Nothing
        End Try

        Return Compute(key)

    End Function


    ' ページの要求に付いてきた Cookie から、今のトリップを計算する（無ければ Nothing）
    '
    ' ラウンジが「今このトリップが付いています」を、ページを作るときに埋め込むために使う。
    ' HttpOnly でも、Cookie そのものはサーバーへ届く（JS から読めないだけ）
    Public Shared Function FromRequest(request As HttpRequest) As String

        If request Is Nothing Then
            Return Nothing
        End If

        Dim cookie As HttpCookie = request.Cookies(CookieName)

        If cookie Is Nothing Then
            Return Nothing
        End If

        Return FromCookieValue(cookie.Value)

    End Function


    ' 合言葉が受け付けられないとき、その理由を返す（受け付けられるなら Nothing）
    '
    ' Compute が Nothing を返す理由のうち、利用者が直せるものだけを言葉にする。
    ' トリップ機能が止まっているときは、呼び出し側（ラウンジ）がそもそも欄を出さない
    Public Shared Function ValidateKey(key As String) As String

        Dim trimmed As String = If(key, "").Trim()

        If trimmed.Length = 0 Then
            Return "合言葉を入れてください。"
        End If

        If trimmed.Length > MaxKeyLength Then
            Return "合言葉は" & MaxKeyLength & "文字までにしてください。"
        End If

        Return Nothing

    End Function


    ' 合言葉を Cookie に書く
    '
    ' 【HttpOnly】JS から読めない形にする。合言葉に普段のパスワードを使い回す人は
    ' 必ず出るので、画面に何か紛れ込んでも（XSS）盗まれない形のほうが守りが固い。
    ' そのため、この Cookie は JS ではなくサーバーが書く（設計から変えた）。
    '
    ' 【期限は、名前・アイコンの Cookie と同じ（既定1年）】
    ' 期限を書かない（ブラウザを閉じたら消える）形にしていたが、実機で覆った。
    ' 開発者が実際に閉じて開き直したら、トリップが消えていた。
    ' **トリップは「久しぶりに来ても同じ印が付いている」ことに意味がある**ので、
    ' 閉じるたびに消えるのは本末転倒だった。
    ' 盗まれてできるのは「そのトリップを名乗る」ことだけで、名前・アイコンと扱いも揃う。
    ' 長さは SiteSettings.ProfileCookieDays（他の Cookie の cookieMaxAgeSeconds と同じ元）。
    ' **期限を戻したくなったら、この経緯を先に読むこと**
    '
    ' 【path・Secure・SameSite は、他の Cookie と同じ】
    ' 揃えておかないと、消すときに別の Cookie を1つ作って終わる（ChatLogout と同じ注意）
    Public Shared Sub WriteKeyCookie(response As HttpResponse, key As String)

        Dim cookie As New HttpCookie(CookieName, Uri.EscapeDataString(If(key, "").Trim()))
        ApplyCookieAttributes(cookie)

        ' SiteSettings はキャッシュが空なら DB を読むが、ここは WebMethod の中（SyncLock の外）なので構わない
        cookie.Expires = DateTime.Now.AddDays(SiteSettings.ProfileCookieDays)

        ' Add ではなく Set。Add は同じ名前でも並べて足すので、Set-Cookie が2本出うる
        response.Cookies.Set(cookie)

    End Sub


    ' 合言葉の Cookie を消す（＝トリップを外す）
    '
    ' ログアウトのときは、これを呼ばなくても ChatLogout が届いた Cookie を全部消す
    Public Shared Sub ClearKeyCookie(response As HttpResponse)

        Dim cookie As New HttpCookie(CookieName, String.Empty)
        ApplyCookieAttributes(cookie)

        ' 過去の日付にすると、ブラウザはその場で捨てる
        cookie.Expires = New DateTime(1999, 1, 1)

        response.Cookies.Set(cookie)

    End Sub


    ' 書くときと消すときで、属性を1文字も違えないための共通部分
    Private Shared Sub ApplyCookieAttributes(cookie As HttpCookie)

        cookie.Path = "/"
        cookie.HttpOnly = True

        ' 本番は https 強制、開発機の localhost はブラウザが安全な場所として扱うので、
        ' Secure を付けても届く（ChatLogout・Default.aspx の setCookie と同じ事情）
        cookie.Secure = True
        cookie.SameSite = SameSiteMode.Lax

    End Sub


    ' ソルトを読む（このクラスが最初に使われたときに1回だけ呼ばれる）
    '
    ' 【フェイルクローズ】読めなければ Nothing を返し、トリップ機能ごと止める。
    '
    ' 【ここだけフェイルクローズにする理由】
    ' 空や仮のソルトで計算すると、そのトリップが表示され、
    ' あとから正しいファイルを置いた瞬間に全員のトリップが変わる。置き忘れに誰も気づけない。
    ' 止めておけば「トリップが1つも出ない（ラウンジの欄も出ない）」という、目で見て分かる形になる。
    '
    ' 【壊れていても、サイトは落ちない】
    ' ASP.NET の設定ファイルではなく、ただのテキストを自分で読んでいるので、
    ' 中身がどう壊れていても止まるのはトリップだけである（XML の形との違い）。
    '
    ' 【例外を外へ出さないこと】
    ' ここは Shared フィールドの初期化子から呼ばれる。例外が漏れると
    ' TypeInitializationException になり、**このクラスを使う JoinRoom が毎回落ちる**
    ' （＝誰も入室できなくなる）。トリップが止まるだけで済ませる
    Private Shared Function LoadSalt() As Byte()

        Try

            Dim path As String = HostingEnvironment.MapPath(SaltRelativePath)

            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                ReportDisabled("App_Data\trip-salt.txt がありません")
                Return Nothing
            End If

            ' ReadAllText は BOM を見て文字コードを決め、BOM 自体は結果に入れない
            Dim raw As String = File.ReadAllText(path).Trim()

            ' 行の中に空白・改行・制御文字が混ざっていたら止める（上の「中身の形」）
            For Each ch As Char In raw
                If Char.IsWhiteSpace(ch) OrElse Char.IsControl(ch) Then
                    ReportDisabled("App_Data\trip-salt.txt が1行になっていません（途中に空白か改行があります）")
                    Return Nothing
                End If
            Next

            If raw.Length < MinSaltLength Then
                ReportDisabled("App_Data\trip-salt.txt のソルトが短すぎます（" & raw.Length & "文字。" &
                               MinSaltLength & "文字以上が必要）")
                Return Nothing
            End If

            Return Encoding.UTF8.GetBytes(raw)

        Catch ex As Exception

            ' 読む権限が無い、など
            ReportDisabled("App_Data\trip-salt.txt を読めませんでした： " & ex.Message)
            Return Nothing

        End Try

    End Function


    ' 止まったことを知らせる
    '
    ' エラー報告メールへ繋いだ。それまでは Trace だけで、出力先が無かった。
    ' ここは Shared フィールドの初期化子から呼ばれるが、ErrorReport は例外を投げない
    ' （積むだけ。失敗しても Trace に書いて終わる）ので、TypeInitializationException の心配は無い。
    '
    ' 【理由に、ソルトの中身を入れないこと】メールに載る（今の文言は長さと形だけ）
    Private Shared Sub ReportDisabled(reason As String)

        ErrorReport.ReportProblem("TripCode.LoadSalt",
            "トリップ機能を止めました（誰のトリップも出しません）： " & reason)

    End Sub

End Class
