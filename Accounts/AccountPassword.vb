Imports System.Security.Cryptography
Imports System.Text

' ============================================================
' アカウントのパスワード
' ============================================================
'
' ログインのパスワードの、決まり・ハッシュ・間違いの数え方を、ここ1か所に持つ。
' **部屋のパスワード（Chat/RoomPassword.vb）と同じ作りを、意図してなぞってある。**
' 片方を直したら、もう片方も見ること。
'
' 【部屋のパスワードと、どこが違うか】
'
' | | 部屋のパスワード | アカウントのパスワード |
' |---|---|---|
' | 守るもの | 一時部屋の入口 | **サイトそのもの**（記事を書ける） |
' | 長さ | 32文字まで（下限なし） | **12文字以上・128文字まで** |
' | 見せるか | 管理人には見せる（暗号文を別に持つ） | **絶対に見せない**（ハッシュだけ） |
' | 間違えたとき | 5回で60秒、部屋ごとに待たせる | **IPは待たせる／アカウントは遅らせるだけ**（下記） |
'
' 【下限と上限を、SiteSettings に入れていない】
' 「12文字以上」は防御であって設定ではない。DB に置くと管理画面から弱められる
' ——設定にすると、管理画面から守りを外せる「壊せる箱」ができてしまう。
' 上限（128文字）も同じで、**PBKDF2 に長すぎる入力を渡させないための歯止め**だから、
' 運用で緩める箱を作らない（部屋のパスワードの MaxLength を定数にしてあるのと同じ）。
Public NotInheritable Class AccountPassword

    Private Sub New()
    End Sub


    ' パスワードの長さ
    '
    ' 【下限12文字】
    ' 管理人のアカウントは、乗っ取られるとサイトの記事を好きに書き換えられる。
    ' 部屋のパスワード（下限なし）とは守るものの重さが違う。
    '
    ' 【上限128文字】PBKDF2 は入力の長さぶん計算が伸びる。
    ' 上限が無いと、長い文字列を投げるだけでサーバーのCPUを食わせられる
    ' （下の「サイト全体の計算の上限」でも止めているが、入口でも絞っておく）
    Public Const MinLength As Integer = 12
    Public Const MaxLength As Integer = 128


    ' === ハッシュの形 ===
    '
    '   pbkdf2-sha256$100000$（ソルトの Base64）$（ハッシュの Base64）
    '
    ' 部屋のパスワードと同じ形にしてある（1本の文字列にまとめ、反復回数を中に持つ）。
    ' **回数を上げても、前に作ったパスワードはそのまま照合できる**（Verify は文字列の回数を使う）
    Private Const Scheme As String = "pbkdf2-sha256"

    ' 反復回数
    '
    ' 【部屋の5万回より多くしてある】（開発機での実測＝10万回で約365ms）
    ' 部屋のパスワードは「入室のたびに」計算するので待たせたくなかったが、
    ' ログインは1日に何度もするものではない。0.4秒は待ってもらえる。
    ' 守る相手（漏れたDBを手元で総当たりされる）の重さも、こちらのほうが上である。
    ' .NET Framework の実装は遅い（新しい .NET の数倍）ので、2世代目では回数を上げてよい
    Private Const Iterations As Integer = 100000

    Private Const SaltBytes As Integer = 16
    Private Const HashBytes As Integer = 32


    ' 画面から来たパスワードを整える
    '
    ' 【前後の空白を落とさない】（部屋のパスワードとは、ここだけ逆にしてある）
    ' 部屋のパスワードは「人に伝えて打ってもらう短い言葉」なので Trim していたが、
    ' ログインのパスワードはパスワード管理ソフトから貼られることが多く、
    ' 空白を含むパスフレーズもありうる。**こちらで中身を変えない**。
    ' Nothing は空文字にするだけ
    Public Shared Function Normalize(password As String) As String
        Return If(password, String.Empty)
    End Function


    ' パスワードの検査（整えたあとの文字列を渡す）
    '
    ' 戻り値 … 断る理由（文言）。受け付けてよければ Nothing
    Public Shared Function Validate(normalized As String) As String

        If String.IsNullOrEmpty(normalized) Then
            Return "パスワードを入力してください。"
        End If

        If normalized.Length < MinLength Then
            Return "パスワードは" & MinLength & "文字以上にしてください。"
        End If

        If normalized.Length > MaxLength Then
            Return "パスワードは" & MaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' パスワードからハッシュの文字列を作る
    Public Shared Function Hash(normalized As String) As String

        If String.IsNullOrEmpty(normalized) Then
            Throw New ArgumentException("パスワードが空です。", NameOf(normalized))
        End If

        Dim salt(SaltBytes - 1) As Byte

        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(salt)
        End Using

        Dim derived As Byte() = Derive(normalized, salt, Iterations, HashBytes)

        Return Scheme & "$" &
               Iterations.ToString(Globalization.CultureInfo.InvariantCulture) & "$" &
               Convert.ToBase64String(salt) & "$" &
               Convert.ToBase64String(derived)

    End Function


    ' 打たれたパスワードが、ハッシュの文字列と合っているか
    '
    ' 【ハッシュの形が読めなければ False】（フェイルクローズ）
    ' 誰でも入れる形に倒すと、DBの値が1文字壊れただけで鍵が外れる。
    ' 形が読めないことは知らせる（本人には直せない状態のため）。
    ' **打たれたパスワードも、ユーザー名も、補足に入れないこと**（メールに載る）
    Public Shared Function Verify(normalized As String, stored As String, accountId As Integer) As Boolean

        If String.IsNullOrEmpty(normalized) OrElse String.IsNullOrEmpty(stored) Then
            Return False
        End If

        Dim parts As String() = stored.Split("$"c)
        Dim iterationCount As Integer
        Dim salt As Byte() = Nothing
        Dim expected As Byte() = Nothing

        Dim readable As Boolean =
            parts.Length = 4 AndAlso
            parts(0) = Scheme AndAlso
            Integer.TryParse(parts(1), Globalization.NumberStyles.None,
                             Globalization.CultureInfo.InvariantCulture, iterationCount) AndAlso
            iterationCount >= 1000 AndAlso iterationCount <= 10000000 AndAlso
            TryFromBase64(parts(2), salt) AndAlso
            TryFromBase64(parts(3), expected) AndAlso
            salt.Length > 0 AndAlso expected.Length > 0

        If Not readable Then
            ErrorReport.ReportProblem("AccountPassword.Verify",
                                      "アカウント " & accountId & " の PasswordHash が読めない形です。" &
                                      "そのアカウントでは、誰もログインできません。")
            Return False
        End If

        Dim actual As Byte() = Derive(normalized, salt, iterationCount, expected.Length)

        ' 【時間の差で中身を推測されない比べ方】部屋のパスワードのものを使い回す
        Return RoomPassword.FixedTimeEquals(actual, expected)

    End Function


    Private Shared Function Derive(password As String, salt As Byte(), iterationCount As Integer,
                                   length As Integer) As Byte()

        Using kdf As New Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterationCount,
                                            HashAlgorithmName.SHA256)
            Return kdf.GetBytes(length)
        End Using

    End Function


    Private Shared Function TryFromBase64(text As String, ByRef bytes As Byte()) As Boolean

        Try
            bytes = Convert.FromBase64String(text)
            Return True
        Catch ex As FormatException
            bytes = Nothing
            Return False
        End Try

    End Function



    ' ============================================================
    ' 総当たりの歯止め
    ' ============================================================
    '
    ' 【IPとアカウントの両方で数える。ただし、やることが違う】
    '
    ' | 単位 | どうするか | なぜ |
    ' |---|---|---|
    ' | IPアドレス | **受け付けない**（既定5回で60秒） | 手を動かしている側を止める |
    ' | アカウント | **遅らせるだけ**（返事を待たせる） | **ロックにすると、管理人を締め出せてしまう** |
    '
    ' アカウント側をロックにすると、管理人のユーザー名（記事のURLに出るので公開されている）を
    ' わざと5回間違えるだけで、本人がログインできなくなる。
    ' **正しいパスワードを持っている人を、他人が締め出せる形にしてはいけない。**
    ' 遅らせるだけなら、総当たりの速さは落とせて、本人は（少し待つだけで）必ず入れる。
    '
    ' 【アカウントの遅らせ方は、コードの定数】1回間違えるごとに 0.4秒、上限2秒。
    ' 運用で変える場面が見えないので、設定にしていない
    ' （部屋のパスワードの StreakMemory・TrackingLimit と同じ扱い）。
    ' **待つのはサーバーのスレッド1本だけ**なので、上限は短くしてある
    ' （長くすると、叩かれている間にスレッドが埋まる）
    '
    ' 【覚えておくのはメモリだけ】アプリが再起動すると数え直し（部屋のパスワードと同じ割り切り）。
    ' **DBの表を作らなかった理由**＝外から叩くだけで行を増やせる口になる（記録の歯止めと同じ話）。
    ' ログインを試せるのは管理人1人ぶんなので、メモリで足りる

    ' 1回の間違いにつき、どれだけ待たせるか（アカウントの単位）
    Private Shared ReadOnly DelayPerFailure As TimeSpan = TimeSpan.FromMilliseconds(400)
    Private Shared ReadOnly DelayMax As TimeSpan = TimeSpan.FromSeconds(2)

    ' 続けて間違えた、とみなす長さ（これを過ぎたら数を忘れる）
    Private Shared ReadOnly StreakMemory As TimeSpan = TimeSpan.FromMinutes(10)

    ' 覚えておく相手の数の上限（メモリを食い潰されないため）
    Private Const TrackingLimit As Integer = 10000

    Private Class FailureState
        Public Count As Integer
        Public LastFailedAt As DateTime
        Public LockedUntil As DateTime
    End Class

    ' キー：「a|ログインに使われた文字列」または「i|IPアドレス」
    Private Shared ReadOnly Failures As New Dictionary(Of String, FailureState)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly FailuresLock As New Object()


    ' 今、このIPからは受け付けない状態か。受け付けないなら、あと何秒か（受け付けるなら0）
    Public Shared Function GetLockoutSeconds(ipAddress As String) As Integer

        If String.IsNullOrEmpty(ipAddress) Then
            Return 0
        End If

        Dim now As DateTime = DateTime.Now

        SyncLock FailuresLock

            Dim state As FailureState = Nothing

            If Failures.TryGetValue(IpKey(ipAddress), state) AndAlso state.LockedUntil > now Then
                Return CInt(Math.Ceiling((state.LockedUntil - now).TotalSeconds))
            End If

        End SyncLock

        Return 0

    End Function


    ' このログインIDについて、返事を遅らせる長さ（間違えた回数から決まる）
    '
    ' 【呼ぶのは、パスワードを確かめる前】
    ' 確かめたあとだと、成功した人まで待たせることになる……ではなく、逆である。
    ' **確かめる前に待たせておくと、「そのアカウントが在るかどうか」で待ち時間が変わらない**
    ' （在る・無いを、かかった時間で見分けられないようにする）。
    ' 0 なら待たなくてよい
    Public Shared Function GetDelay(loginId As String) As TimeSpan

        If String.IsNullOrEmpty(loginId) Then
            Return TimeSpan.Zero
        End If

        Dim now As DateTime = DateTime.Now
        Dim count As Integer = 0

        SyncLock FailuresLock

            Dim state As FailureState = Nothing

            If Failures.TryGetValue(AccountKey(loginId), state) AndAlso
               now - state.LastFailedAt <= StreakMemory Then
                count = state.Count
            End If

        End SyncLock

        If count <= 0 Then
            Return TimeSpan.Zero
        End If

        Dim delay As TimeSpan = TimeSpan.FromMilliseconds(DelayPerFailure.TotalMilliseconds * count)

        Return If(delay > DelayMax, DelayMax, delay)

    End Function


    ' 間違えたことを数える
    '
    ' maxFailures・lockout … SiteSettings の値。**呼び出し側が鍵の外で読んで渡す**
    ' （この中は鍵の中なので、ここで SiteSettings を読まない。ChatHub・RoomPassword と同じ決まり）
    '
    ' 【IPの単位だけが、受け付けない状態になる】アカウントの単位は数えるだけ（上の GetDelay が使う）
    Public Shared Sub RecordFailure(loginId As String, ipAddress As String,
                                    maxFailures As Integer, lockout As TimeSpan)

        Dim now As DateTime = DateTime.Now

        SyncLock FailuresLock

            For Each item In KeysOf(loginId, ipAddress)

                Dim key As String = item.Key
                Dim isIp As Boolean = item.Value
                Dim state As FailureState = Nothing

                If Not Failures.TryGetValue(key, state) Then

                    If Failures.Count >= TrackingLimit Then
                        PruneCore(now)
                    End If

                    If Failures.Count >= TrackingLimit Then
                        Continue For
                    End If

                    state = New FailureState()
                    Failures(key) = state

                ElseIf now - state.LastFailedAt > StreakMemory Then

                    ' 前の間違いから時間が空いた。続けて間違えたことにはしない
                    state.Count = 0

                End If

                state.Count += 1
                state.LastFailedAt = now

                ' 【受け付けない状態にするのは、IPの単位だけ】上の表の理由
                If isIp AndAlso state.Count >= maxFailures Then
                    state.LockedUntil = now + lockout
                    state.Count = 0
                End If

            Next

        End SyncLock

    End Sub


    ' 正しく入れたので、そのログインIDの数を0に戻す
    '
    ' 【IPの数は戻さない】戻すと、同じIPの誰かが1回正しく入るたびに、
    ' 総当たりしている側の数まで消えてしまう（部屋のパスワードと同じ判断）。
    ' IPの数は、時間で忘れるだけにしてある
    Public Shared Sub RecordSuccess(loginId As String)

        If String.IsNullOrEmpty(loginId) Then
            Return
        End If

        SyncLock FailuresLock
            Failures.Remove(AccountKey(loginId))
        End SyncLock

    End Sub


    ' 忘れてよいものを捨てる。【必ず FailuresLock の中で呼ぶこと】
    Private Shared Sub PruneCore(now As DateTime)

        Dim expired = Failures.Where(
            Function(entry) entry.Value.LockedUntil <= now AndAlso
                            now - entry.Value.LastFailedAt > StreakMemory).Select(
            Function(entry) entry.Key).ToList()

        For Each key In expired
            Failures.Remove(key)
        Next

    End Sub


    ' 数える相手のキーと、「それはIPの単位か」の組
    Private Shared Iterator Function KeysOf(loginId As String,
                                            ipAddress As String) As IEnumerable(Of KeyValuePair(Of String, Boolean))

        If Not String.IsNullOrEmpty(loginId) Then
            Yield New KeyValuePair(Of String, Boolean)(AccountKey(loginId), False)
        End If

        If Not String.IsNullOrEmpty(ipAddress) Then
            Yield New KeyValuePair(Of String, Boolean)(IpKey(ipAddress), True)
        End If

    End Function


    ' 【打たれた文字列をそのままキーにしている】
    ' ユーザー名でもメールアドレスでも、打たれたとおりに数える。
    ' 「在るアカウントか」を見てからキーを決める形にすると、在る・無いで振る舞いが変わってしまう。
    ' 大文字・小文字は区別しない（Failures の比較子が OrdinalIgnoreCase）
    Private Shared Function AccountKey(loginId As String) As String
        Return "a|" & loginId
    End Function


    Private Shared Function IpKey(ipAddress As String) As String
        Return "i|" & ipAddress
    End Function

End Class
