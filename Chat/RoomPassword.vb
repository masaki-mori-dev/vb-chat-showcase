Imports System.Security.Cryptography
Imports System.Text

' ============================================================
' 部屋のパスワード
' ============================================================
'
' 利用者が作る部屋に付ける「入口の鍵」の、決まり・ハッシュ・間違いの数え方を、ここ1か所に持つ。
'
' 【DBに入れるのはハッシュだけ】
' ChatRooms.PasswordHash（NVARCHAR(256)）に、下の Hash が作る1本の文字列を入れる。
' **管理人でも、あとから見られない。** 忘れたら「新しいものに変える」しかできない。
' 「部屋に居ながらの設定変更」を作るときは、パスワードの欄を**空で出し**、
' 入れ直したときだけ差し替えること（今のパスワードを画面へ出す道は、そもそも無い）。
'
' 【なぜハッシュにするのか】部屋そのものは一時部屋で、守る価値は大きくない。
' 守りたいのは、**利用者が普段のパスワードを使い回して入れてしまったとき**のほうである
' （画面に「他で使っているパスワードは入れないでください」と出してはいるが、入れる人は入れる）。
' DBの中身が漏れたときに、その文字列がそのまま読める形にはしない。
'
' 【トリップのソルト（App_Data\trip-salt.txt）とは別物。混ぜないこと】
' あちらはサイト共通の秘密で、DBに置いてはいけない値。
' こちらは部屋ごとにランダムに作り、ハッシュと一緒にDBへ入れる値（隠す必要が無い）。
'
' 【照合は必ずサーバーで】画面での判定は1つも無い（ハッシュは画面へ出さない）。
' 入室を最後に断るのは ChatHub.JoinRoom で、ここの Verify を呼ぶのは
' ラウンジの WebMethod（Lounge.EnterRoom）である（ChatHub.RoomKeys.vb の「鍵を通った印」）
Public NotInheritable Class RoomPassword

    Private Sub New()
    End Sub


    ' パスワードの文字数の上限
    '
    ' トリップの合言葉（TripCode.MaxKeyLength）と同じ数にしてある。どちらも
    ' 「人に伝えて打ってもらう短い言葉」で、分ける理由が無い。
    ' 数え方は UTF-16（部屋名・発言と同じ）。画面の maxlength もこの数から組み立てる
    Public Const MaxLength As Integer = 32


    ' === ハッシュの形 ===
    '
    '   pbkdf2-sha256$50000$（ソルトの Base64）$（ハッシュの Base64）
    '
    ' 【1本の文字列にまとめてある】列を足さずに済むうえ、**反復回数を文字列の中に持つ**ので、
    ' あとで回数を上げても、前に作った部屋のパスワードはそのまま照合できる（Verify は文字列の回数を使う）。
    ' 長さは約95文字（列は256文字）
    Private Const Scheme As String = "pbkdf2-sha256"

    ' 反復回数
    '
    ' 【5万回にした理由】（開発機で測った。.NET Framework 4.8 の Rfc2898DeriveBytes）
    '   1万回 … 42ms ／ 5万回 … 180ms ／ 10万回 … 365ms ／ 21万回 … 759ms
    ' 部屋を作るとき・パスワードを入れて入室するときに、毎回この時間だけ待たせることになり、
    ' そのあいだ共用サーバーのCPUを1本使う。10万回以上だと、押してから反応するまでが
    ' はっきり遅く感じられる。**守る相手は「漏れたDBを手元で総当たりされる」ことで、
    ' 5万回でも素のSHA-256の5万倍の手間になる**。
    ' .NET Framework の実装は遅い（新しい .NET の数倍）ので、2世代目では回数を上げてよい
    Private Const Iterations As Integer = 50000

    Private Const SaltBytes As Integer = 16
    Private Const HashBytes As Integer = 32


    ' 画面から来た文字列を整える（前後の空白を落とすだけ。中身は触らない）
    '
    ' トリップの合言葉と同じ扱い。スマホの入力で末尾に空白が付くことがあるので、
    ' 作るときも照合するときも、同じくここを通す（片方だけ通すと、同じ言葉が一致しなくなる）。
    ' 大文字・小文字・全角・半角は区別する（本人が決めた言葉なので、こちらで「同じ」の範囲を広げない）。
    ' Nothing は空文字にする
    Public Shared Function Normalize(password As String) As String

        If password Is Nothing Then
            Return String.Empty
        End If

        Return password.Trim()

    End Function


    ' パスワードの検査（整えたあとの文字列を渡す）
    '
    ' 戻り値 … 断る理由（文言）。受け付けてよければ Nothing。
    ' **空は受け付ける**。空が何を意味するかは呼ぶ側が決める
    '   ・部屋を作るとき（Lounge.CreateRoom）… パスワードを付けない部屋
    '   ・部屋の設定を変えるとき（ChatHub.ChangeRoomSettings）… 鍵のスイッチがオンなら「今のまま」
    ' 【ValidateForCreate から改名した】設定の変更でも同じ検査を使うため
    ' （検査と文言を、作るときと変えるときの2か所に分けない）
    Public Shared Function Validate(normalized As String) As String

        If normalized IsNot Nothing AndAlso normalized.Length > MaxLength Then
            Return "パスワードは" & MaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' パスワードからハッシュの文字列を作る（整えたあとの、空でない文字列を渡す）
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
    ' 形が読めないことは、知らせる（ErrorReport。管理人にも本人にも直せない状態のため）。
    ' 部屋番号は知らせてよいが、**打たれたパスワードは補足に入れないこと**（メールに載る）
    Public Shared Function Verify(normalized As String, stored As String, roomId As Integer) As Boolean

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
            ErrorReport.ReportProblem("RoomPassword.Verify",
                                      "部屋 " & roomId & " の PasswordHash が読めない形です。その部屋には誰も入れません。")
            Return False
        End If

        Dim actual As Byte() = Derive(normalized, salt, iterationCount, expected.Length)

        Return FixedTimeEquals(actual, expected)

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
            ' 利用者の入力ではなくDBの値だが、読めないことは呼び出し側がまとめて知らせる
            bytes = Nothing
            Return False
        End Try

    End Function


    ' 2つのバイト列が同じかを、途中で抜けずに比べる
    '
    ' 【= や SequenceEqual で比べないこと】最初に違ったところで抜けるので、
    ' かかった時間から「何バイト目まで合っていたか」を推し量られうる
    ' （.NET Framework 4.8 には CryptographicOperations.FixedTimeEquals が無いので、自前で持つ）
    Friend Shared Function FixedTimeEquals(a As Byte(), b As Byte()) As Boolean

        If a.Length <> b.Length Then
            Return False
        End If

        Dim diff As Integer = 0

        For i As Integer = 0 To a.Length - 1
            diff = diff Or (CInt(a(i)) Xor CInt(b(i)))
        Next

        Return diff = 0

    End Function



    ' ============================================================
    ' 総当たりの歯止め（間違いを数える）
    ' ============================================================
    '
    ' 【決まり】同じ人が同じ部屋で、続けて SiteSettings.RoomPasswordMaxFailures 回
    ' （既定5回）間違えたら、SiteSettings.RoomPasswordLockout（既定60秒）のあいだ受け付けない。
    ' 正しく入れたら、その人の数は0に戻る。
    '
    ' 【「同じ人」を2通りで数える】
    '   ・部屋＋ChatBrowserId … 開発者の決めた単位
    '   ・部屋＋IPアドレス     … こちらで足した（下の理由）
    ' **どちらかが上限に届いたら、受け付けない。**
    ' ChatBrowserId は Cookie なので、利用者が自由に作り直せる。Cookie を1回ごとに捨てられると、
    ' ブラウザの単位だけでは1回も止まらない（満員拒否の記録の歯止めで書いたのと同じ弱さ）。
    ' IPアドレスは作り直すのに手間がかかるので、そちらでも数える。
    ' 【IPの単位の副作用】携帯電話の回線などで、同じIPアドレスを大勢が使っていることがある。
    ' その人たちがまとめて**その部屋だけ**、60秒待たされることがありうる（ほかの部屋には効かない）。
    ' **正しく入れても、IPの数は0に戻さない**——戻すと、同じIPの誰かが1回正しく入るたびに、
    ' 総当たりしている側の数まで消えてしまう。IPの数は、時間で忘れるだけにしてある。
    '
    ' 【判定は、ハッシュの計算より先】受け付けない間は、CPUを使う計算まで行かない
    '
    ' 【覚えておくのはメモリだけ】アプリが再起動すると数え直し（記録の歯止めと同じ割り切り）。
    ' 部屋が閉じても、数は残る（時間で消える。部屋番号は使い回されないので、害は無い）
    '
    ' 【続けて間違えた、とみなす長さ】最後に間違えてから StreakMemory（10分）が過ぎたら、数を忘れる。
    ' 無いと、1時間に1回ずつ間違えた人まで、5度目で待たされる。
    ' **設定にしていない**（運用で変えたくなる場面が見えない。必要になったらそのとき移す）

    Private Shared ReadOnly StreakMemory As TimeSpan = TimeSpan.FromMinutes(10)

    ' 覚えておく相手の数の上限（メモリを食い潰されないため）。
    ' 超えそうになったら、古いもの（忘れてよいもの）を掃除する。
    ' 掃除しても入りきらないときは、新しい相手を数えない（その相手は歯止めの外になるが、
    ' そこまで数を作れる相手は、どちらにしても Cookie と IP を作り直せる相手である）
    Private Const TrackingLimit As Integer = 10000

    Private Class FailureState
        Public Count As Integer
        Public LastFailedAt As DateTime
        Public LockedUntil As DateTime
    End Class

    ' キー：「b|部屋ID|ChatBrowserId」または「i|部屋ID|IPアドレス」
    Private Shared ReadOnly Failures As New Dictionary(Of String, FailureState)(StringComparer.Ordinal)
    Private Shared ReadOnly FailuresLock As New Object()


    ' 今、受け付けない状態か。受け付けないなら、あと何秒か（切り上げ。受け付けるなら0）
    '
    ' browserId・ipAddress … どちらも Nothing でよい（無い単位は数えない）
    Public Shared Function GetLockoutSeconds(roomId As Integer, browserId As String,
                                             ipAddress As String) As Integer

        Dim now As DateTime = DateTime.Now
        Dim remaining As TimeSpan = TimeSpan.Zero

        SyncLock FailuresLock

            For Each key In KeysOf(roomId, browserId, ipAddress)

                Dim state As FailureState = Nothing

                If Failures.TryGetValue(key, state) AndAlso state.LockedUntil > now Then
                    If state.LockedUntil - now > remaining Then
                        remaining = state.LockedUntil - now
                    End If
                End If

            Next

        End SyncLock

        Return CInt(Math.Ceiling(remaining.TotalSeconds))

    End Function


    ' 間違えたことを数える。戻り値は、これで受け付けなくなったなら待つ秒数（まだなら0）
    '
    ' maxFailures・lockout … SiteSettings の値。**呼び出し側が鍵の外で読んで渡す**
    ' （この中は鍵の中なので、ここで SiteSettings を読まない。ChatHub の決まりと同じ）
    Public Shared Function RecordFailure(roomId As Integer, browserId As String, ipAddress As String,
                                         maxFailures As Integer, lockout As TimeSpan) As Integer

        Dim now As DateTime = DateTime.Now
        Dim lockedFor As TimeSpan = TimeSpan.Zero

        SyncLock FailuresLock

            For Each key In KeysOf(roomId, browserId, ipAddress)

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

                If state.Count >= maxFailures Then

                    ' 受け付けない時間を始め、数は0から数え直す
                    ' （待ったあとは、また上限まで試せる。開発者の決めた「5回で60秒」のとおり）
                    state.LockedUntil = now + lockout
                    state.Count = 0

                    If lockout > lockedFor Then
                        lockedFor = lockout
                    End If

                End If

            Next

        End SyncLock

        Return CInt(Math.Ceiling(lockedFor.TotalSeconds))

    End Function


    ' 正しく入れたので、その人（ブラウザの単位）の数を0に戻す。IPの単位は戻さない（上の理由）
    Public Shared Sub RecordSuccess(roomId As Integer, browserId As String)

        If String.IsNullOrEmpty(browserId) Then
            Return
        End If

        SyncLock FailuresLock
            Failures.Remove(BrowserKey(roomId, browserId))
        End SyncLock

    End Sub


    ' 忘れてよいもの（受け付けない時間が終わっていて、最後の間違いから時間が空いたもの）を捨てる
    ' 【必ず FailuresLock の中で呼ぶこと】
    Private Shared Sub PruneCore(now As DateTime)

        Dim expired = Failures.Where(
            Function(entry) entry.Value.LockedUntil <= now AndAlso
                            now - entry.Value.LastFailedAt > StreakMemory).Select(
            Function(entry) entry.Key).ToList()

        For Each key In expired
            Failures.Remove(key)
        Next

    End Sub


    Private Shared Iterator Function KeysOf(roomId As Integer, browserId As String,
                                            ipAddress As String) As IEnumerable(Of String)

        If Not String.IsNullOrEmpty(browserId) Then
            Yield BrowserKey(roomId, browserId)
        End If

        If Not String.IsNullOrEmpty(ipAddress) Then
            Yield "i|" & roomId.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & ipAddress
        End If

    End Function


    ' ============================================================
    ' サイト全体の計算の上限
    ' ============================================================
    '
    ' 【何のためか】上の歯止め（人ごとの回数）は、Cookie と IP の両方を作り直せる相手には効かない。
    ' PBKDF2 の計算は1回で約0.18秒 CPU を使うので、そういう相手に叩き続けられると**サイト全体が重くなる**。
    ' そこで、人を問わずサイト全体で2つを数え、どちらかが一杯なら計算そのものをしない。
    '   ・1分に何回まで（SiteSettings.RoomPasswordChecksPerMinute。既定60回＝CPU1本の約18%で頭打ち）
    '   ・同時にいくつまで（MaxConcurrentWork＝2。一瞬に束で来たときに、CPUを全部取られないため）
    ' 満員拒否の記録の「表が埋まっている間は、新しい相手を記録しない」（RejectLogTrackingLimit）と同じ考え方で、
    ' **相手ごとに数えるのをあきらめ、全体で止める**。
    '
    ' 【数えるのは、照合と、部屋を作るときのハッシュの両方】どちらも同じ計算である。
    ' 【断ったときは、人ごとの間違いの数に入れない・記録（41）も書かない】試していないため。
    '
    ' 【副作用（承知のうえ）】叩かれている間は、正しいパスワードを持っている人も「混み合っています」で待つ
    ' （最大1分）。サイト全体が重くなるより、パスワード付きの部屋にだけ少し待ってもらうほうを選んだ。
    '
    ' 【覚えておくのはメモリだけ】アプリが再起動すると数え直し
    '
    ' 【同時に2つまで、は設定にしていない】CPU の本数で決まる値で、運用で変える場面が見えないため

    Private Const MaxConcurrentWork As Integer = 2

    ' 同時に計算してよい数（取れなければ、待たずに断る。待つとASP.NETのスレッドを握ったまま並ぶため）
    Private Shared ReadOnly WorkSlots As New Threading.SemaphoreSlim(MaxConcurrentWork, MaxConcurrentWork)

    ' 直近1分に計算を始めた時刻（古い順）
    Private Shared ReadOnly WorkStartedAt As New Queue(Of DateTime)
    Private Shared ReadOnly WorkLock As New Object()

    Private Shared ReadOnly WorkWindow As TimeSpan = TimeSpan.FromMinutes(1)


    ' 計算を始めてよいか。よければ「終わったら返す札」を、だめなら Nothing を返す
    '
    ' perMinute         … SiteSettings.RoomPasswordChecksPerMinute。**呼び出し側が読んで渡す**
    ' retryAfterSeconds … だめだったとき、何秒待てばよさそうか（1以上）
    '
    ' 使い方（VB の Using は Nothing を受け付ける。Nothing なら Dispose を呼ばない）
    '   Using ticket = RoomPassword.TryBeginWork(perMinute, retryAfter)
    '       If ticket Is Nothing Then … 「混み合っています」
    '       … Verify / Hash …
    '   End Using
    Public Shared Function TryBeginWork(perMinute As Integer, ByRef retryAfterSeconds As Integer) As IDisposable

        retryAfterSeconds = 0

        Dim now As DateTime = DateTime.Now

        SyncLock WorkLock

            Do While WorkStartedAt.Count > 0 AndAlso now - WorkStartedAt.Peek() >= WorkWindow
                WorkStartedAt.Dequeue()
            Loop

            If WorkStartedAt.Count >= perMinute Then

                ' いちばん古い1回が1分の外へ出るまで
                Dim wait As TimeSpan = WorkStartedAt.Peek() + WorkWindow - now
                retryAfterSeconds = Math.Max(1, CInt(Math.Ceiling(wait.TotalSeconds)))

                Return Nothing

            End If

            ' 同時の数は、1分の数に入れる前に確かめる（取れなかった回を数えないため）
            If Not WorkSlots.Wait(0) Then
                retryAfterSeconds = 2
                Return Nothing
            End If

            WorkStartedAt.Enqueue(now)

        End SyncLock

        Return New WorkTicket()

    End Function


    ' 「計算が終わった」を返す札。Dispose で同時の枠を1つ空ける（2回呼ばれても1回しか空けない）
    Private NotInheritable Class WorkTicket
        Implements IDisposable

        Private _released As Integer = 0

        Public Sub Dispose() Implements IDisposable.Dispose
            If Threading.Interlocked.Exchange(_released, 1) = 0 Then
                WorkSlots.Release()
            End If
        End Sub

    End Class


    ' 混み合っているときの文言（ラウンジの入室と、部屋を作るときで共通）
    Public Const BusyMessage As String = "今、パスワードの確認が混み合っています。少し待ってから、もう一度お試しください。"


    ' ブラウザの単位のキー。**Guid に直してから作る**（Cookie の表記の揺れで、同じ人が別の数にならないため。
    ' AnonId で踏んだのと同じ罠）。GUID として読めなければ、生の文字列のまま使う
    Private Shared Function BrowserKey(roomId As Integer, browserId As String) As String

        Dim parsed As Guid
        Dim normalized As String = If(Guid.TryParse(browserId, parsed), parsed.ToString("N"), browserId)

        Return "b|" & roomId.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & normalized

    End Function

End Class
