' ============================================================
' 入口の鍵（ChatHub の Partial）
' ============================================================
'
' パスワード付きの部屋へ入るときの、「鍵を通った印」をここで持つ。
'
' 【流れ】
'   1. ラウンジの入室ボタン → 小窓でパスワードを入れる → WebMethod（Lounge.EnterRoom）が照合する
'   2. 合っていたら GrantRoomPass で、この部屋とこのブラウザの組に**期限付きの印**を置く
'      （部屋を作った本人にも、RegisterCreatedRoom が同じ印を置く）
'   3. 部屋の画面が開いて JoinRoom が呼ばれたら、鍵付きの部屋では印を確かめる。無ければ断る
'   4. 入れたら、印は使い終わり（消す）
'
' 【最後の砦は JoinRoom】ラウンジの小窓・照合は親切であって、防御ではない。
' ハブは開発者ツールから直接呼べるので、ラウンジを通らずに JoinRoom を呼ばれても、
' 印が無ければここで断られる。
'
' 【なぜ JoinRoom の引数にパスワードを足さなかったのか】（選んだ。
'   この回は「ハブの署名を変えてよい」回だったが、それでもこちらを採った）
'   ・部屋の画面はパスワードを知らない。引数で渡すには、ラウンジで打った言葉を
'     sessionStorage などに**平文で置いて**ページをまたいで運ぶことになる
'   ・F5・自動再接続のたびに JoinRoom が呼ばれる。そのたびにパスワードを持ち回る
'     （か、聞き直す）ことになる
'   ・照合（PBKDF2。約180ms）が、全部屋共通の鍵（SyncRoot）の手前とはいえ、ハブの呼び出しの中に入る
'   印の形なら、パスワードはラウンジの1回の POST にしか乗らず、ハブの署名も変わらない
'
' 【F5・再接続（JoinRoom の (b) 引き継ぎ）では、鍵を聞き直さない】
' 既に部屋に居る人なので、定員の判定を飛ばしているのと同じ扱い（isNewOccupant で判定）。
' 同じ部屋を別のタブで開き直した (a) も、同じ理由で聞き直さない
'
' 【割り切り】5秒の猶予（退室予告）を過ぎてから戻ってきた人は、もう部屋に居ないので、
' 入り直しにはパスワードが要る（ラウンジへ戻され、「パスワードが必要です」と出る）。
' スマホで F5 に5秒以上かかった場合がこれに当たる。印を長く残せば避けられるが、
' そのぶん「一度入れた人が、あとから何度でも入れる」時間が延びるので、採らなかった
'
' 【15-3（設定変更）でパスワードを付け外し・変更するときに、ここへ足すこと】
'   ・その部屋の印を全部捨てる（ForgetRoomPassesCore）。変える前に通った人が、あとから入れないように
'   ・LockedRooms を書き換える
'   ・部屋の画面（ChatRoom.aspx.vb）は、Session の部屋IDだけで過去ログを描いている。
'     パスワードを後から付けた部屋では、前に入ったことのある人が、Session が残っている間は
'     過去ログだけ読める（入室そのものは JoinRoom が断る）。後から付けられるようになったら、ここを塞ぐ
Partial Public Class ChatHub

    ' 印の寿命
    '
    ' 【60秒】ラウンジで照合してから、部屋の画面が開いて JoinRoom が呼ばれるまでの間だけ持てばよい
    ' （ふつうは数秒）。遅いスマホでも足りる長さにしてある。
    ' **設定にしていない**（開発者との打ち合わせで「60秒程度」。運用で変える値ではない）
    Private Shared ReadOnly RoomPassLifetime As TimeSpan = TimeSpan.FromSeconds(60)

    ' 鍵を通った印（キー：「部屋ID|ChatBrowserId（Guid の N 形式）」、値：期限）
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared RoomPasses As New Dictionary(Of String, DateTime)(StringComparer.Ordinal)

    ' パスワード付きだと分かっている部屋（DB の ChatRooms.PasswordHash が NULL でない部屋）
    '
    ' 入るのは、①この回で作られた部屋（RegisterCreatedRoom）②誰かが入室して、DBから読めた部屋。
    '
    ' 【なぜメモリにも持つのか】JoinRoom が DB へ問い合わせられなかった回は、「鍵付きかどうか」が DB からは分からない。
    ' ここに控えてあれば、DBが詰まっている間も鍵付きの部屋は鍵付きのまま扱える（**鍵はフェイルクローズ側に倒す**）。
    ' 【塞いだ】前は「アプリが再起動した直後で、まだ誰もその部屋に入っておらず、しかも DB が読めない」ときだけ、
    ' 鍵付きかどうかが分からないまま通していた。今は、この起動のあいだに一度も DB から読めていない部屋（KnownRoomStates に無い部屋）へは、
    ' DB が読めない回は新しく入れない（JoinRoom の roomStateUnknownReject）：文を消すのではなく塞ぐ
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared LockedRooms As New HashSet(Of Integer)

    ' この起動のあいだに、鍵付きかどうかを DB から知った部屋。
    ' 入るのは NoteRoomLockCore を通った部屋＝①誰かが入室して DB から読めた部屋 ②この起動で作られた部屋。
    ' ここに在る部屋なら、LockedRooms に無い＝鍵は無い、と言える（無い部屋は「分からない」）
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared KnownRoomStates As New HashSet(Of Integer)


    ' 鍵を通った印を置く（Lounge.EnterRoom が照合に成功したとき）
    ' 部屋を作った本人の印は、RegisterCreatedRoom が同じ鍵の中で置く
    '
    ' 【browserId は Guid で受け取る】呼び出し側が Cookie を Guid.TryParse で読めたときだけ呼べる。
    ' 表記の揺れ（大文字・小文字・波かっこ）で印が見つからない、を防ぐため（AnonId で踏んだ罠）
    Public Shared Sub GrantRoomPass(roomId As Integer, browserId As Guid)

        Dim expires As DateTime = DateTime.Now + RoomPassLifetime

        SyncLock SyncRoot
            RoomPasses(RoomPassKey(roomId, browserId)) = expires
        End SyncLock

    End Sub


    ' 作られた部屋がパスワード付きなら、控える（RegisterCreatedRoom から）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub NoteRoomLockCore(roomId As Integer, hasPassword As Boolean)

        KnownRoomStates.Add(roomId)

        If hasPassword Then
            LockedRooms.Add(roomId)
        Else
            LockedRooms.Remove(roomId)
        End If

    End Sub


    ' 鍵付きの部屋か（JoinRoom から。NoteRoomLockCore で控えたあとに呼ぶ）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsRoomLockedCore(roomId As Integer) As Boolean
        Return LockedRooms.Contains(roomId)
    End Function


    ' DB が読めなかった回に、守り（鍵・入室禁止）を確かめられない部屋か（JoinRoom から）
    '
    ' 【定員と向きが逆】定員は「DB に問い合わせられなかったら通す」（DB が一瞬詰まっただけで誰も入れなくならないように）。
    ' 鍵と入室禁止は守りの話なので、確かめられないなら**通さない**側に倒す。
    '   ・この起動のあいだに一度も読めていない部屋 … 鍵付きかどうかが分からない
    '   ・一時部屋 … 入室禁止は「その部屋 × そのブラウザ」ごとに DB にあり、メモリの控え（KnownBans）は、
    '     この起動で見かけた分しか知らない（再起動の前に禁止された人は分からない）。だから一時部屋は、読めない回は通さない
    '   ・固定部屋（一度読めた部屋で、一時部屋ではない）… 管理人が居ないので入室禁止は無い。鍵は控えで分かる。今までどおり通す
    ' 断るのは新しく入る人だけ（F5・再接続で席を引き継ぐ人は、もう部屋に居るので断らない）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsRoomGuardUnknownCore(roomId As Integer) As Boolean
        Return Not KnownRoomStates.Contains(roomId) OrElse TemporaryRooms.Contains(roomId)
    End Function


    ' このブラウザが、この部屋の鍵を通った印を持っているか（期限切れは持っていない扱い）
    '
    ' browserId … Cookie の生の値（JoinRoom が読んだもの）。Guid に直してから探す
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function HasRoomPassCore(roomId As Integer, browserId As String) As Boolean

        Dim parsed As Guid

        If Not Guid.TryParse(browserId, parsed) Then
            Return False
        End If

        Dim expires As DateTime

        Return RoomPasses.TryGetValue(RoomPassKey(roomId, parsed), expires) AndAlso
               expires > DateTime.Now

    End Function


    ' 印を使い終わった（入室できた）ので捨てる
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub ConsumeRoomPassCore(roomId As Integer, browserId As String)

        Dim parsed As Guid

        If Guid.TryParse(browserId, parsed) Then
            RoomPasses.Remove(RoomPassKey(roomId, parsed))
        End If

    End Sub


    ' 期限の切れた印を捨てる（見回りのタイマー＝CheckTimeoutParticipants から）
    '
    ' 印は「入れなかった人」の分が残り続ける（照合したが部屋を開かなかった、など）ので、掃除が要る
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub CleanUpRoomPassesCore(now As DateTime)

        If RoomPasses.Count = 0 Then
            Return
        End If

        Dim expired = RoomPasses.Where(
            Function(entry) entry.Value <= now).Select(
            Function(entry) entry.Key).ToList()

        For Each key In expired
            RoomPasses.Remove(key)
        Next

    End Sub


    ' 閉じた部屋の、鍵にまつわるものを忘れる（ForgetRoomCore から）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub ForgetRoomPassesCore(roomId As Integer)

        LockedRooms.Remove(roomId)

        If RoomPasses.Count = 0 Then
            Return
        End If

        Dim prefix As String = roomId.ToString(Globalization.CultureInfo.InvariantCulture) & "|"

        Dim keys = RoomPasses.Keys.Where(
            Function(k) k.StartsWith(prefix, StringComparison.Ordinal)).ToList()

        For Each key In keys
            RoomPasses.Remove(key)
        Next

    End Sub


    ' パスワード違いの拒否（種別41）を、記録すべきか（ラウンジの EnterRoom から）
    '
    ' 満員（40）と同じ歯止めを通す（同じ相手は RejectLogCooldown に1回・覚える相手は RejectLogTrackingLimit まで）。
    ' ラウンジの WebMethod はハブの外にいるので、鍵を取るところまでここで引き受ける
    Public Shared Function ShouldLogPasswordReject(roomId As Integer, browserId As String) As Boolean

        ' 【鍵を取る前に読む】（SiteSettings を SyncLock の中で読まない決まり）
        Dim cooldown As TimeSpan = SiteSettings.RejectLogCooldown
        Dim trackingLimit As Integer = SiteSettings.RejectLogTrackingLimit

        SyncLock SyncRoot
            Return ShouldLogJoinReject(ChatMessageType.JoinRejectedPassword, roomId, browserId,
                                       cooldown, trackingLimit)
        End SyncLock

    End Function


    Private Shared Function RoomPassKey(roomId As Integer, browserId As Guid) As String
        Return roomId.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & browserId.ToString("N")
    End Function

End Class
