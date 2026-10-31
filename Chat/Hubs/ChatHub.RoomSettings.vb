Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Linq
Imports Microsoft.AspNet.SignalR

' ============================================================
' 管理人だけが、部屋の5つの設定を変える（同じ日の夜に 作り替えた）
' ============================================================
'
' ChatHub の続き（Partial）。部屋の参加者リスト・管理人の控え（RoomOwners）と、鍵（SyncRoot）をそのまま使う。
'
' 【開発者が決めたこと】
'   ・変えられるのは、部屋を作るときに決める5つ＝部屋名・定員・ノック受付・パスワード・画像の可否
'   ・定員は、今居る人数より少なくできる（表示は 5/3）。今居る人は追い出さず、新しく入る人だけ断る
'     （決定：表示は 5/3 のままでよい。
'       定員を下げるのは、混みすぎた部屋を落ち着かせる道具でもある）
'   ・5つとも、部屋のログに1行出す。記録も残す（種別61）
'   ・画像をオフにしても、それまでに貼られた画像は見えたまま（「個人的に見えなくできるしね」＝無視）
'
' 【変えたこと（同じ日の夜。開発者が本番の画面を見て）】
'   ①「保存」ボタンをやめて、その場で反映する形にした。
'     指摘：ほかの場所はトグルを動かしただけで変更が反映されるのに、
'     管理人の設定だけは保存ボタンを押さないと反映されず、そろっていない。
'     画面は項目ごとに呼ぶ（部屋名とパスワードの文字はボタン・Enter で、定員・ノック・画像・鍵のスイッチは動かしたとき）。
'     **このメソッドの形（5つまとめて受け取る）は変えていない**——画面は今の値に1つだけ差し替えて送る。
'     サーバーは今の値と比べて、変わったところだけを扱う（発行の瞬間に開いていた古い画面の「保存」も、そのまま動く）
'   ② 歯止めは「同じ項目を続けて変える」ときだけ（RoomSettingsChangeInterval。既定3秒）。
'     保存ボタンが無くなると、5つを続けて触るのは普通の操作になるので、項目をまたいでは止めない。
'     そのかわり、部屋全体に緩い上限を1本だけ残す（1分に RoomSettingsMaxChangesPerMinute 項目まで。5つを延々と往復されたときの保険）
'   ③ ログの1行（61）は、変更が止まってから数秒後に、まとめて1行（RoomSettingsLogQuiet）。
'     開発者と「3つ変えて3行並べない」と決めた形を、保存ボタンが無くなっても守るため。
'     **画面の反映（部屋の全員・ラウンジ）は今までどおり即時**で、ログの行と記録だけが少し遅れて出る。
'     行には「まとめた間の、最初と最後の差」を出す（ノックをオン→オフと往復しただけなら、行は出ない）
'   ④ パスワードは、管理人自身には見えるようにした（GetRoomPassword。下の節）
'
' 【流れ】
'   1. 部屋の画面（設定パネルの「部屋の設定」。管理人にだけ出る）が ChangeRoomSettings を呼ぶ
'   2. 検査（部屋を作るときと同じ関数）→ 管理人か（鍵の中で、サーバーの控え RoomOwners で）
'   3. DB の今の値を読み、変わった項目を数える（無ければ何もしない）→ 項目ごとの歯止め・部屋全体の上限（鍵の中）
'   4. DB を書き換え、鍵の中で控え（鍵付きか・鍵を通った印・ノック）を合わせ、知らせを積む。鍵を抜けて配る
'      （部屋の全員へ roomSettingsChanged、ラウンジへ kind roomChanged）。ログの1行と記録 61 は、まとめ役（RoomSettingsLogBatch）へ
Partial Public Class ChatHub

    ' パスワードをどうするか（DB を書き換えるときの形）
    Private Enum PasswordChange
        Keep = 0     ' 今のまま
        SetNew = 1   ' かけた・かけ直した（新しいハッシュ）
        Clear = 2    ' 外した
    End Enum

    ' 項目の名前（歯止めを項目ごとに数えるときのキー。画面へは出さない）
    Private Const SettingItemName As String = "name"
    Private Const SettingItemMaxUsers As String = "maxUsers"
    Private Const SettingItemKnock As String = "knock"
    Private Const SettingItemPassword As String = "password"
    Private Const SettingItemImage As String = "image"

    ' 部屋全体の緩い上限：1分に何項目まで変えられるか
    '
    ' 【SiteSettings に入れていない】ふつうの操作では届かない「保険」の線で、調整したくなる場面が思いつかない。
    ' 5つを全部変えても5項目。20は、それを4回続けてやっと届く数（同じ項目は3秒に1回なので、1分にせいぜい20回）
    Private Const RoomSettingsMaxChangesPerMinute As Integer = 20

    ' ログの1行をまとめて出すまでの静かな時間（最後の変更から）と、まとめる長さの上限（最初の変更から）
    '
    ' 静かな時間は、見回りのタイマー（2秒おき）で見るので、実際には5〜7秒後に出る。
    ' 上限は、4秒おきに変え続けられて、いつまでも行が出ない（記録も残らない）のを防ぐため
    Private Shared ReadOnly RoomSettingsLogQuiet As TimeSpan = TimeSpan.FromSeconds(5)
    Private Shared ReadOnly RoomSettingsLogMaxHold As TimeSpan = TimeSpan.FromSeconds(60)

    ' まとめている途中の、ログの1行（部屋ごとに1つ）
    Private NotInheritable Class RoomSettingsLogBatch
        ' まとめ始める前の設定（行には、これと Latest の差を出す）
        Public Baseline As RoomSettingsInfo
        ' 最後に受け付けた設定
        Public Latest As RoomSettingsInfo
        ' まとめている間に、新しいパスワードをかけたか（「かけ直した」を出すため。有無だけでは分からない）
        Public PasswordSet As Boolean
        ' 変えた管理人（記録の名前・アイコン。最後に変えたときのもの）
        Public Participant As Participant
        Public FirstAt As DateTime
        Public LastAt As DateTime
    End Class

    ' 【どれも SyncRoot の中でしか触らないこと】
    Private Shared RoomSettingsLogBatches As New Dictionary(Of Integer, RoomSettingsLogBatch)
    ' 部屋ごとの、直近1分に変えた項目の時刻（部屋全体の上限のため。1項目につき1つ）
    Private Shared RoomSettingsRecentChanges As New Dictionary(Of Integer, List(Of DateTime))


    ' 部屋の画面（管理人）から、部屋の設定を変えるときに呼ばれる
    '
    ' roomName     … 部屋名（作るときと同じ ChatRoomRules で整えて検査する）
    ' maxUsers     … 定員（白リスト ChatRoomRules.AllowedMaxUsers の中だけ。**今居る人数より少なくてよい**）
    ' knockEnabled … ノックを受け付けるか
    ' imageEnabled … 画像を送れるか
    ' lockEnabled  … 鍵をかけるか（スイッチ）
    ' password     … 入れ直したパスワード。**空なら「今のまま」**（鍵がかかっている部屋なら今のパスワードのまま。
    '                かかっていない部屋で鍵をオンにするなら、空では断る）
    '
    ' 【画面は、今の値に1つだけ差し替えて送る】。サーバーは今の値と比べて、変わった項目だけを扱う。
    ' 【roomId は引数で受け取らない】（SendMessage・ChangeAppearance と同じ論法）。呼んだ接続から部屋と人を引く。
    ' 【管理人かどうかは、必ずここで決める】画面に欄を出す／出さないは親切で、防御ではない（ハブは開発者ツールから呼べる）。
    Public Function ChangeRoomSettings(roomName As String, maxUsers As Integer, knockEnabled As Boolean,
                                       imageEnabled As Boolean, lockEnabled As Boolean,
                                       password As String) As RoomSettingsResult

        ' === 検査（部屋を作るときと同じ関数。文言も同じ） ===
        ' 【鍵の外で】SiteSettings を読むもの・計算の重いものは、鍵の中へ持ち込まない
        Dim name As String = ChatRoomRules.NormalizeRoomName(roomName)
        Dim nameReason As String = ChatRoomRules.ValidateRoomName(name)

        If nameReason IsNot Nothing Then
            Return RoomSettingsRejected(nameReason)
        End If

        ' 定員は白リストの中だけ（**今居る人数より少なくてよい**。下げるのは荒らし対策の道具でもある）
        If Not ChatRoomRules.IsAllowedMaxUsers(maxUsers) Then
            Return RoomSettingsRejected("定員が正しくありません。選び直してください。")
        End If

        Dim typedPassword As String = RoomPassword.Normalize(password)
        Dim passwordReason As String = RoomPassword.Validate(typedPassword)

        If passwordReason IsNot Nothing Then
            Return RoomSettingsRejected(passwordReason)
        End If

        Dim interval As TimeSpan = SiteSettings.RoomSettingsChangeInterval
        Dim roomId As Integer = 0
        Dim owner As Participant = Nothing

        ' === 管理人か（鍵の中で、サーバーの控えを見る） ===
        SyncLock SyncRoot

            owner = FindOwnerByConnectionCore(roomId)

            If owner Is Nothing Then
                ' 部屋に居ない（画面の操作では起こらない）
                Return RoomSettingsRejected("部屋に入っていないため、設定を変えられません。")
            End If

            If Not IsRoomOwnerCore(roomId, owner.BrowserId) Then
                Return RoomSettingsRejected("部屋の管理人ではないため、設定を変えられません。")
            End If

        End SyncLock

        ' === 今の値を読み、変わったところだけを数える（DB。鍵の外で） ===
        Dim current As RoomSettingsInfo = Nothing

        If Not TryReadRoomSettings(roomId, current) Then
            Return RoomSettingsRejected("今は設定を変えられません。少し待ってから、もう一度お試しください。")
        End If

        If current Is Nothing Then
            ' 閉じた・一時部屋ではない（管理人が居るのは一時部屋だけなので、ふつうは起こらない）
            Return RoomSettingsRejected(ChatCommon.RoomClosedMessage)
        End If

        ' パスワードの扱い：鍵のスイッチ × 欄の中身 × 今かかっているか
        Dim pwChange As PasswordChange = PasswordChange.Keep

        If lockEnabled Then

            If typedPassword.Length > 0 Then
                pwChange = PasswordChange.SetNew
            ElseIf Not current.HasPassword Then
                Return RoomSettingsRejected("鍵をかけるときは、パスワードを入れてください。")
            End If

        ElseIf current.HasPassword Then
            pwChange = PasswordChange.Clear
        End If

        ' 変わった項目（歯止めを項目ごとに数えるため）
        Dim items As New List(Of String)

        If Not String.Equals(current.RoomName, name, StringComparison.Ordinal) Then items.Add(SettingItemName)
        If current.MaxUsers <> maxUsers Then items.Add(SettingItemMaxUsers)
        If current.KnockEnabled <> knockEnabled Then items.Add(SettingItemKnock)
        If pwChange <> PasswordChange.Keep Then items.Add(SettingItemPassword)
        If current.ImageEnabled <> imageEnabled Then items.Add(SettingItemImage)

        ' 変わったところが無ければ、何もしない（DB も記録も知らせも無し。時計も進めない）
        If items.Count = 0 Then
            Return New RoomSettingsResult With {.Ok = True, .Changed = False, .Settings = current}
        End If

        ' === 歯止め（鍵の中）。通ったら、先に時計を進めて席を取る（同時に来た2本が両方通らないように）。
        '     このあと失敗したら、元に戻す（受け付けたときだけ進める、の決まり） ===
        Dim now As DateTime = DateTime.Now
        Dim savedTimes As New Dictionary(Of String, Date?)

        SyncLock SyncRoot

            Dim waitMs As Integer = 0

            ' 同じ項目を続けて変えるときだけ止める
            For Each item In items

                Dim last As Date

                If owner.RoomSettingChangedAt.TryGetValue(item, last) Then

                    Dim elapsed As TimeSpan = now - last

                    If elapsed < interval Then
                        waitMs = Math.Max(waitMs, CInt(Math.Ceiling((interval - elapsed).TotalMilliseconds)))
                    End If

                End If

            Next

            If waitMs > 0 Then
                Return New RoomSettingsResult With {
                    .RetryAfterMs = waitMs,
                    .Message = "同じ項目を続けては変えられません。少し待ってから、もう一度お試しください。"
                }
            End If

            ' 部屋全体の緩い上限（直近1分の項目数）
            Dim recent As List(Of DateTime) = Nothing

            If Not RoomSettingsRecentChanges.TryGetValue(roomId, recent) Then
                recent = New List(Of DateTime)
                RoomSettingsRecentChanges(roomId) = recent
            End If

            recent.RemoveAll(Function(t) now - t >= TimeSpan.FromMinutes(1))

            If recent.Count + items.Count > RoomSettingsMaxChangesPerMinute Then

                Dim oldest As DateTime = recent.Min()

                Return New RoomSettingsResult With {
                    .RetryAfterMs = Math.Max(1000, CInt(Math.Ceiling((oldest.AddMinutes(1) - now).TotalMilliseconds))),
                    .Message = "続けて変えすぎています。少し待ってから、もう一度お試しください。"
                }

            End If

            For Each item In items

                Dim last As Date

                savedTimes(item) = If(owner.RoomSettingChangedAt.TryGetValue(item, last), CType(last, Date?), Nothing)
                owner.RoomSettingChangedAt(item) = now
                recent.Add(now)

            Next

        End SyncLock

        Dim accepted As Boolean = False

        Try

            ' 新しいパスワードのハッシュ（約180ms。部屋を作るときと同じく、サイト全体の計算の上限を通す）と、見せるための暗号文
            Dim passwordHash As String = Nothing
            Dim passwordCipher As String = Nothing

            If pwChange = PasswordChange.SetNew Then

                Dim busyWait As Integer

                Using ticket As IDisposable = RoomPassword.TryBeginWork(SiteSettings.RoomPasswordChecksPerMinute, busyWait)

                    If ticket Is Nothing Then
                        Return RoomSettingsRejected(RoomPassword.BusyMessage)
                    End If

                    passwordHash = RoomPassword.Hash(typedPassword)

                End Using

                ' 鍵が読めなければ Nothing（その部屋は「表示できません」になるだけ。照合はハッシュで動く）
                passwordCipher = RoomPasswordSecret.Protect(typedPassword)

            End If

            Dim updated As New RoomSettingsInfo With {
                .RoomName = name,
                .MaxUsers = maxUsers,
                .KnockEnabled = knockEnabled,
                .ImageEnabled = imageEnabled,
                .HasPassword = (pwChange = PasswordChange.SetNew) OrElse (pwChange = PasswordChange.Keep AndAlso current.HasPassword)
            }

            If Not TryWriteRoomSettings(roomId, updated, pwChange, passwordHash, passwordCipher) Then
                Return RoomSettingsRejected("今は設定を変えられません。少し待ってから、もう一度お試しください。")
            End If

            accepted = True

            ' === 控えを合わせ、知らせを積む（鍵の中） ===
            SyncLock SyncRoot

                ' 鍵付きかどうかの控え（JoinRoom が DB を読めなかったときに使う）
                NoteRoomLockCore(roomId, updated.HasPassword)

                ' パスワードをかけ直した・外したら、それまでの「鍵を通った印」（60秒）は捨てる
                ' （古いパスワードで通った人が、そのあと入れてしまわないため）
                If pwChange <> PasswordChange.Keep Then
                    ForgetRoomPassesCore(roomId)
                End If

                ' ノックを受け付けなくしたら、返事待ちのノックは取り消す（ノックした人には「取り消されました」）。
                ' 許可して取り置いた席は残す（管理人が替わったときと同じの形に揃えた）
                If current.KnockEnabled AndAlso Not knockEnabled Then
                    CancelRoomKnocksCore(roomId, closeSeats:=False)
                End If

                ' ログの1行は、まとめ役へ（変更が止まってから出す）
                NoteRoomSettingsLogCore(roomId, current, updated, pwChange = PasswordChange.SetNew, owner, DateTime.Now)

                ' 画面の反映は即時（行は付けない＝eventText は Nothing）
                Dim notice As New RoomNotice With {
                    .RoomSettingsRoomId = roomId,
                    .RoomSettings = updated,
                    .RoomSettingsEventText = Nothing
                }

                ' ラウンジの一覧の、その部屋（部屋名・定員・鍵の印・ノックの形）
                notice.LoungeUpdate = NextLoungeUpdate(LoungeKindRoomChanged)

                If notice.LoungeUpdate IsNot Nothing Then
                    notice.LoungeUpdate.RoomId = roomId
                    notice.LoungeUpdate.Room = New LoungeRoomInfo With {
                        .RoomId = roomId,
                        .RoomName = updated.RoomName,
                        .MaxUsers = updated.MaxUsers,
                        .HasPassword = updated.HasPassword,
                        .KnockEnabled = updated.KnockEnabled
                    }
                End If

                RoomNoticeOutbox.Add(notice)

            End SyncLock

            FlushRoomNotices()

            Return New RoomSettingsResult With {.Ok = True, .Changed = True, .Settings = updated}

        Finally

            ' 受け付けなかった（計算が混み合っていた・DB に書けなかった）ら、進めた時計を戻す
            If Not accepted Then

                SyncLock SyncRoot

                    For Each pair In savedTimes

                        If pair.Value.HasValue Then
                            owner.RoomSettingChangedAt(pair.Key) = pair.Value.Value
                        Else
                            owner.RoomSettingChangedAt.Remove(pair.Key)
                        End If

                    Next

                    Dim recent As List(Of DateTime) = Nothing

                    If RoomSettingsRecentChanges.TryGetValue(roomId, recent) Then
                        For i As Integer = 1 To savedTimes.Count
                            recent.Remove(now)
                        Next
                    End If

                End SyncLock

            End If

        End Try

    End Function


    ' ============================================================
    '  今のパスワードを、管理人に見せる
    ' ============================================================
    '
    ' 決定：パスワードは、管理人自身には見えるようにする
    ' 「一時部屋のパスワードなんか　管理人自身もすぐ忘れると思うねん。なので分かるようにしたい」。
    '
    ' 【管理人にだけ渡す。管理人かどうかは、必ずここで決める】画面の「見る」ボタンを出さないことは防御ではない。
    ' 【配信しない。押されたときに1回だけ、その接続へ返す】部屋の全員に配ると、管理人でない人の画面にも届く。
    ' 【画面は最初は伏せておき、押したら見せる】（スマホで肩越しに見られるため）。ここは押されたときにだけ呼ばれる。
    '
    ' 戻り値 … ok なら password に今のパスワード。見せられないときは ok = False と理由（message）
    '   ・鍵がかかっていない
    '   ・暗号文が無い（見せる仕組みより前にかけた・鍵が読めなかったときにかけた）→ かけ直せば見えるようになる
    '   ・鍵が読めない（App_Data\room-password-key.txt が壊れている）→ 照合は動いている
    Public Function GetRoomPassword() As RoomPasswordRevealResult

        Dim roomId As Integer = 0

        SyncLock SyncRoot

            Dim owner As Participant = FindOwnerByConnectionCore(roomId)

            If owner Is Nothing OrElse Not IsRoomOwnerCore(roomId, owner.BrowserId) Then
                Return New RoomPasswordRevealResult With {
                    .Ok = False, .Message = "部屋の管理人だけが見られます。"
                }
            End If

        End SyncLock

        Dim hasPassword As Boolean
        Dim cipher As String = Nothing

        If Not TryReadRoomPasswordCipher(roomId, hasPassword, cipher) Then
            Return New RoomPasswordRevealResult With {
                .Ok = False, .Message = "今は表示できません。少し待ってから、もう一度お試しください。"
            }
        End If

        If Not hasPassword Then
            Return New RoomPasswordRevealResult With {
                .Ok = False, .Message = "この部屋には、パスワードがかかっていません。"
            }
        End If

        If Not RoomPasswordSecret.IsEnabled Then
            Return New RoomPasswordRevealResult With {
                .Ok = False, .Message = "今は表示できません（入室のときの確かめは、今までどおり動いています）。"
            }
        End If

        Dim plain As String = Nothing

        If cipher Is Nothing OrElse Not RoomPasswordSecret.TryUnprotect(cipher, plain) Then
            Return New RoomPasswordRevealResult With {
                .Ok = False,
                .Message = "このパスワードは表示できません（表示できるようになる前にかけたため）。かけ直すと、表示できるようになります。"
            }
        End If

        Return New RoomPasswordRevealResult With {.Ok = True, .Password = plain}

    End Function


    ' ============================================================
    '  ログの1行をまとめる
    ' ============================================================

    ' 受け付けた変更を、その部屋のまとめ役に足す
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub NoteRoomSettingsLogCore(roomId As Integer, before As RoomSettingsInfo, after As RoomSettingsInfo,
                                               passwordSet As Boolean, owner As Participant, now As DateTime)

        Dim batch As RoomSettingsLogBatch = Nothing

        ' 別の人（管理人が替わった）のまとめが残っていたら、先にそれを出す（1行に2人分を混ぜない）
        If RoomSettingsLogBatches.TryGetValue(roomId, batch) AndAlso
           Not SameBrowserText(batch.Participant.BrowserId, owner.BrowserId) Then
            QueueRoomSettingsLogCore(roomId)
            batch = Nothing
        End If

        If batch Is Nothing Then
            batch = New RoomSettingsLogBatch With {.Baseline = before, .FirstAt = now}
            RoomSettingsLogBatches(roomId) = batch
        End If

        batch.Latest = after
        batch.PasswordSet = batch.PasswordSet OrElse passwordSet
        batch.Participant = owner
        batch.LastAt = now

    End Sub


    ' まとめた1行を、知らせの箱に積む（まとめ役は消す）。差が無ければ（往復しただけなら）何も積まない
    ' 【必ず SyncRoot の中で呼ぶこと】配るのは呼び出し元が鍵を抜けてから（FlushRoomNotices）
    Private Shared Sub QueueRoomSettingsLogCore(roomId As Integer)

        Dim batch As RoomSettingsLogBatch = Nothing

        If Not RoomSettingsLogBatches.TryGetValue(roomId, batch) Then
            Return
        End If

        RoomSettingsLogBatches.Remove(roomId)

        Dim detail As String = DescribeRoomSettingsChange(batch.Baseline, batch.Latest, batch.PasswordSet)

        If String.IsNullOrEmpty(detail) Then
            Return
        End If

        ' 部屋の全員へ1行（設定は付けない＝ Nothing。画面の反映は、変えたその場で済んでいる）と、記録 61
        RoomNoticeOutbox.Add(New RoomNotice With {
            .RoomSettingsRoomId = roomId,
            .RoomSettings = Nothing,
            .RoomSettingsEventText = ChatEventText.BuildRoomSettingsChanged(detail),
            .LogType = ChatMessageType.RoomSettingsChanged,
            .LogRoomId = roomId,
            .LogParticipant = batch.Participant,
            .LogText = detail
        })

    End Sub


    ' 見回りのタイマー（2秒おき。Global.asax）から呼ばれる。静かになった部屋のまとめを、1行にして配る
    '
    ' 時刻を比べるだけですぐ戻る（まとめが無ければ、何もしない）
    Public Shared Sub FlushDueRoomSettingsLogs()

        Dim queued As Boolean = False
        Dim now As DateTime = DateTime.Now

        SyncLock SyncRoot

            If RoomSettingsLogBatches.Count = 0 Then
                Return
            End If

            For Each roomId In RoomSettingsLogBatches.Keys.ToList()

                Dim batch As RoomSettingsLogBatch = RoomSettingsLogBatches(roomId)

                If now - batch.LastAt >= RoomSettingsLogQuiet OrElse now - batch.FirstAt >= RoomSettingsLogMaxHold Then
                    QueueRoomSettingsLogCore(roomId)
                    queued = True
                End If

            Next

        End SyncLock

        If queued Then
            FlushRoomNotices()
        End If

    End Sub


    ' 管理人が部屋から外れた・部屋が閉じたときに、まとめを待たずに出す
    ' （AfterParticipantRemovedCore・ForgetRoomCore から。「管理人が替わりました」の行より前に並ぶように）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub FlushRoomSettingsLogForCore(roomId As Integer, leaver As Participant)

        Dim batch As RoomSettingsLogBatch = Nothing

        If Not RoomSettingsLogBatches.TryGetValue(roomId, batch) Then
            Return
        End If

        If leaver Is Nothing OrElse SameBrowserText(batch.Participant.BrowserId, leaver.BrowserId) Then
            QueueRoomSettingsLogCore(roomId)
        End If

    End Sub


    ' 部屋を忘れるときの後片付け（ForgetRoomCore から）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub ForgetRoomSettingsCore(roomId As Integer)
        FlushRoomSettingsLogForCore(roomId, Nothing)
        RoomSettingsRecentChanges.Remove(roomId)
    End Sub


    ' 最初と最後の差を、行の文言にする（「部屋名「…」・定員 3人・ノックを受け付ける」）。差が無ければ Nothing
    '
    ' 【パスワード】有無だけでは「かけ直した」が分からないので、まとめている間に新しくかけたか（passwordSet）も見る
    '   ・最後にかかっていない … 最初にかかっていれば「外した」、かかっていなければ何も出さない
    '   ・最後にかかっている   … 最初にかかっていなければ「かけた」、かかっていて新しくかけていれば「かけ直した」
    ' 【パスワードそのものは入れない】（記録の本文にも入る）
    Private Shared Function DescribeRoomSettingsChange(before As RoomSettingsInfo, after As RoomSettingsInfo,
                                                       passwordSet As Boolean) As String

        Dim parts As New List(Of String)

        If Not String.Equals(before.RoomName, after.RoomName, StringComparison.Ordinal) Then
            parts.Add("部屋名「" & after.RoomName & "」")
        End If

        If before.MaxUsers <> after.MaxUsers Then
            parts.Add("定員 " & after.MaxUsers & "人")
        End If

        If before.KnockEnabled <> after.KnockEnabled Then
            parts.Add(If(after.KnockEnabled, "ノックを受け付ける", "ノックを受け付けない"))
        End If

        If Not after.HasPassword Then
            If before.HasPassword Then parts.Add("パスワードを外した")
        ElseIf Not before.HasPassword Then
            parts.Add("パスワードをかけた")
        ElseIf passwordSet Then
            parts.Add("パスワードをかけ直した")
        End If

        If before.ImageEnabled <> after.ImageEnabled Then
            parts.Add(If(after.ImageEnabled, "画像を送れる", "画像を送れない"))
        End If

        If parts.Count = 0 Then
            Return Nothing
        End If

        Return String.Join("・", parts)

    End Function


    ' ============================================================
    '  小さな道具
    ' ============================================================

    ' 呼んだ接続の参加者と、その部屋（居なければ Nothing）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Function FindOwnerByConnectionCore(ByRef roomId As Integer) As Participant

        roomId = 0

        If ConnectionRooms.TryGetValue(Context.ConnectionId, roomId) AndAlso
           RoomParticipants.ContainsKey(roomId) Then

            Return RoomParticipants(roomId).FirstOrDefault(
                Function(p) p.ConnectionId = Context.ConnectionId)

        End If

        Return Nothing

    End Function


    ' ブラウザの識別子どうしが同じか（Cookie の生の文字列なので、Guid に直して比べる）
    Private Shared Function SameBrowserText(a As String, b As String) As Boolean

        Dim ga As Guid
        Dim gb As Guid

        If Guid.TryParse(a, ga) AndAlso Guid.TryParse(b, gb) Then
            Return ga = gb
        End If

        Return String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

    End Function


    Private Shared Function RoomSettingsRejected(message As String) As RoomSettingsResult
        Return New RoomSettingsResult With {.Ok = False, .Message = message}
    End Function


    ' 部屋の今の設定を読む（一時部屋で、開いているものだけ）
    '
    ' 戻り値 … 問い合わせられたか。行が無ければ settings は Nothing
    ' 【ハッシュも暗号文も読まない】有無だけ（TryGetRoomState と同じ）
    Private Shared Function TryReadRoomSettings(roomId As Integer, ByRef settings As RoomSettingsInfo) As Boolean

        settings = Nothing

        Const sql As String =
            "SELECT RoomName, MaxUsers, IsKnockEnabled, IsImageUploadEnabled, " &
            "       CASE WHEN PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword " &
            "  FROM ChatRooms " &
            " WHERE RoomId = @RoomId AND IsActive = 1 AND RoomType = @Temp"

        Try

            Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@Temp", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()

                        If reader.Read() Then
                            settings = New RoomSettingsInfo With {
                                .RoomName = reader("RoomName").ToString(),
                                .MaxUsers = CInt(reader("MaxUsers")),
                                .KnockEnabled = CBool(reader("IsKnockEnabled")),
                                .ImageEnabled = CBool(reader("IsImageUploadEnabled")),
                                .HasPassword = (CInt(reader("HasPassword")) = 1)
                            }
                        End If

                    End Using

                End Using
            End Using

            Return True

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryReadRoomSettings", "RoomId=" & roomId)
            Return False
        End Try

    End Function


    ' 見せるための暗号文を読む（GetRoomPassword 用。ハッシュは有無だけ）
    '
    ' 戻り値 … 問い合わせられたか。部屋が無い・閉じていれば hasPassword = False
    Private Shared Function TryReadRoomPasswordCipher(roomId As Integer, ByRef hasPassword As Boolean,
                                                      ByRef cipher As String) As Boolean

        hasPassword = False
        cipher = Nothing

        Const sql As String =
            "SELECT CASE WHEN PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword, PasswordCipher " &
            "  FROM ChatRooms " &
            " WHERE RoomId = @RoomId AND IsActive = 1 AND RoomType = @Temp"

        Try

            Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@Temp", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()

                        If reader.Read() Then
                            hasPassword = (CInt(reader("HasPassword")) = 1)
                            If Not IsDBNull(reader("PasswordCipher")) Then
                                cipher = reader("PasswordCipher").ToString()
                            End If
                        End If

                    End Using

                End Using
            End Using

            Return True

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryReadRoomPasswordCipher", "RoomId=" & roomId)
            Return False
        End Try

    End Function


    ' 部屋の設定を書き換える（1つの文で。一時部屋で、開いているものだけ）
    '
    ' 戻り値 … 1行書き換えられたか
    ' 【RoomType も条件に入れる】固定部屋は書き換えさせない（最後の砦）
    ' 【暗号文はハッシュと一緒に動かす】かけた＝両方入れる／外した＝両方 NULL／今のまま＝両方そのまま
    Private Shared Function TryWriteRoomSettings(roomId As Integer, settings As RoomSettingsInfo,
                                                 pwChange As PasswordChange, passwordHash As String,
                                                 passwordCipher As String) As Boolean

        Const sql As String =
            "UPDATE ChatRooms " &
            "   SET RoomName = @RoomName, MaxUsers = @MaxUsers, " &
            "       IsKnockEnabled = @Knock, IsImageUploadEnabled = @Image, " &
            "       PasswordHash = CASE @PwMode WHEN 1 THEN @PasswordHash WHEN 2 THEN NULL ELSE PasswordHash END, " &
            "       PasswordCipher = CASE @PwMode WHEN 1 THEN @PasswordCipher WHEN 2 THEN NULL ELSE PasswordCipher END " &
            " WHERE RoomId = @RoomId AND IsActive = 1 AND RoomType = @Temp"

        Try

            Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@Temp", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary
                    cmd.Parameters.Add("@RoomName", SqlDbType.NVarChar, 100).Value = settings.RoomName
                    cmd.Parameters.Add("@MaxUsers", SqlDbType.Int).Value = settings.MaxUsers
                    cmd.Parameters.Add("@Knock", SqlDbType.Bit).Value = settings.KnockEnabled
                    cmd.Parameters.Add("@Image", SqlDbType.Bit).Value = settings.ImageEnabled
                    cmd.Parameters.Add("@PwMode", SqlDbType.Int).Value = CInt(pwChange)
                    cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 256).Value =
                        If(passwordHash Is Nothing, CObj(DBNull.Value), passwordHash)
                    cmd.Parameters.Add("@PasswordCipher", SqlDbType.NVarChar, 400).Value =
                        If(passwordCipher Is Nothing, CObj(DBNull.Value), passwordCipher)

                    conn.Open()

                    Return cmd.ExecuteNonQuery() = 1

                End Using
            End Using

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryWriteRoomSettings", "RoomId=" & roomId)
            Return False
        End Try

    End Function

End Class
