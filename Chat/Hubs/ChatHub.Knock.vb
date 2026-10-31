Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Linq
Imports Microsoft.AspNet.SignalR

' ============================================================
' ノック（ChatHub の Partial）
' ============================================================
'
' 満室の部屋へ「入れてほしい」と伝え、管理人が許可したら、その人のために席を1つ取り置く。
'
' 【流れ】
'   1. ラウンジで満室の部屋の「ノック」を押す → KnockRoom（ラウンジの接続から）
'   2. 部屋の中へ「〇〇さんがノックしました」＋音（KnockReceived）、
'      管理人へ許可を聞く小窓（KnockRequested。60秒のカウントダウン）
'   3. 管理人が「入れる」→ AnswerKnock → 席を取り置く（30秒）→ ノックした人のラウンジへ KnockAnswered
'   4. ノックした人が「入室する」→ いつもの入室（Lounge.EnterRoom → 部屋の画面 → JoinRoom）。
'      JoinRoom は、取り置きがあれば定員を超えて通す（臨時の席。1部屋あたり RoomKnockExtraSeats まで）
'
' 【取り置き＝「臨時の席」の予約である】定員（ChatRooms.MaxUsers）は変えない。
' 表示は (21/20) の形になる。待っている間に普通に席が空いたら、普通の席に座る（臨時の席は使わない）。
' 臨時の席の数は数えて持たず、「今の人数 − 定員」で決まる（入った人が出れば、自然に空く）。
'
' 【パスワードとの関係】パスワード付きの部屋では、許可されたあとの「入室する」でも小窓を通る
' （管理人が許すのは「席を空ける」ことで、「鍵を開ける」ことではない）。
' JoinRoom は「鍵を通った印」（ChatHub.RoomKeys.vb）と「取り置き」を別々に見る。
'
' 【固定部屋はノックを受け付けない】管理人（OwnerBrowserId）が居ないため。DBの IsKnockEnabled が
' 1 でも、RoomType が Temporary でなければ断る（将来は管理画面＝16番の仕事）。
'
' 【配信は、どれも鍵（SyncRoot）を抜けてから】記録（ChatLog.Write）も同じ。
' 鍵の中で起きた「知らせるべきこと」は RoomNoticeOutbox に積み、抜けてから FlushRoomNotices で配る
' （退室の後始末・見回りのタイマーのように、鍵の中で決まることがあるため）
Partial Public Class ChatHub

    ' ノックした人へ返す状態（KnockResult.Status ／ KnockAnswer.Status）。
    ' 画面（Scripts/lounge-live.js）と同じ綴りにすること
    Private Const KnockStatusWaiting As String = "waiting"
    Private Const KnockStatusApproved As String = "approved"
    Private Const KnockStatusNotFull As String = "notFull"
    Private Const KnockStatusRejected As String = "rejected"
    Private Const KnockStatusDenied As String = "denied"
    Private Const KnockStatusExpired As String = "expired"
    Private Const KnockStatusCancelled As String = "cancelled"
    Private Const KnockStatusSeatExpired As String = "seatExpired"

    ' 1部屋に、同時に返事待ちにしておけるノックの数
    '
    ' 【なぜ要るのか】Cookie と IP を作り直せる相手は、連打の間隔（RoomKnockInterval）をすり抜けられる。
    ' 上限が無いと、管理人の画面が小窓で埋まる。設定にしていない（小窓を並べて出せる数の話で、運用で変える値ではない）
    Private Const MaxPendingKnocksPerRoom As Integer = 10

    ' 連打の間隔を覚えておく長さ（掃除の目安）。RoomKnockInterval の上限（60秒）より長ければよい
    Private Shared ReadOnly KnockIntervalMemory As TimeSpan = TimeSpan.FromMinutes(2)


    ' 返事待ちのノック1つ
    Private NotInheritable Class PendingKnock
        Public KnockId As Integer
        Public RoomId As Integer
        Public RoomName As String
        ' ノックした時点の定員（許可するときに、臨時の席がまだあるかを数えるのに使う）
        Public MaxUsers As Integer
        Public Browser As Guid
        ' 記録（ChatLog.Write）と小窓に使う、ノックした人の見た目（LocalId は 0＝番号なし）
        Public Knocker As Participant
        Public ExpiresAt As DateTime
        ' このノックを記録するか（B）。**ノックを受け付けたときに1回だけ決め、行く末まで持ち回す**
        ' （下の「ノックの記録は、1回のノックにつき1回だけ決める」）
        Public ShouldLog As Boolean
    End Class

    ' 取り置いた席1つ（許可されたノック）
    Private NotInheritable Class KnockSeat
        Public RoomId As Integer
        Public RoomName As String
        Public Browser As Guid
        Public Knocker As Participant
        Public ExpiresAt As DateTime
        ' 「入室する」を押したときの延長を、もう使ったか（1回だけ。ExtendKnockSeat）
        Public Extended As Boolean
        ' 元のノックを記録したか（PendingKnock.ShouldLog の写し。取り置きの行く末＝46 もこれに従う）
        Public ShouldLog As Boolean
        ' もとのノックの番号（取り置きが外れたとき、管理人の画面の知らせに使う）
        Public KnockId As Integer
    End Class

    ' 鍵の中で決まった「知らせること・記録すること」1つ（FlushRoomNotices が配る）
    '
    ' 【、ノック以外の部屋の知らせも積むようになった】（管理人の交代）。
    ' それに合わせて名前を KnockNotice → RoomNotice、KnockOutbox → RoomNoticeOutbox、
    ' FlushKnockOutbox → FlushRoomNotices に変えた
    Private NotInheritable Class RoomNotice
        ' ノックした人のラウンジの接続（Nothing なら知らせない）
        Public AnswerTo As List(Of String)
        Public Answer As KnockAnswer
        ' 管理人の画面から小窓を消す（Nothing なら消さない）
        Public WithdrawTo As String
        Public WithdrawKnockId As Integer
        ' 消える理由（Nothing なら黙って消す）。画面は小窓をこの文言に差し替え、少し置いてから消す
        Public WithdrawText As String
        ' ノックした人の匿名ID（管理人が無視している人なら、知らせも出さない）
        Public WithdrawAnonId As String
        ' 小窓がもう無くても、知らせだけ出すか（取り置きが外れたとき＝小窓は許可した時点で消えている）
        Public WithdrawStandalone As Boolean
        ' 記録する種別（Nothing なら記録しない）。**ここに積んだものは、歯止めを通さずに書く**。
        '   ・ノックの行く末（43〜46）… 記録するかは、元のノックを受け付けたときに1回だけ決めてある
        '     （PendingKnock.ShouldLog。記録しないノックなら、ここに種別を入れない＝KnockLogTypeIf）
        '   ・管理人の交代（60）… 外から叩いて増やせる記録ではない（在室者の出入りで起きる）ので、毎回残す
        ' （まで、ノックの行く末は種別ごとに別々の10分の歯止めを通していた。
        '   それで「ノック 8 回・行く末の合計 11」のような、数の噛み合わない集計になっていた）
        Public LogType As ChatMessageType?
        Public LogRoomId As Integer
        Public LogParticipant As Participant
        ' 管理人が替わった。部屋の全員へ ownerChanged を配る（0 なら配らない）
        Public OwnerChangedRoomId As Integer
        Public OwnerLocalId As Integer
        Public OwnerEventText As String
        ' 指名でゆずった。画面は「ポン」を鳴らす（自動の引き継ぎは鳴らさない）
        Public OwnerDesignated As Boolean
        ' ラウンジへ配る差分（管理人の印の付け替え）
        Public LoungeUpdate As LoungeUpdate
        ' ラウンジの「自分が入っている部屋」。そのブラウザのラウンジの接続へだけ配る
        Public SelfRoomTo As List(Of String)
        Public SelfRoomId As Integer
        Public SelfRoomSeq As Long
        ' 部屋に居ながら見た目（アイコン・色）を変えた。部屋の全員へ appearanceChanged を配る（0 なら配らない）
        Public AppearanceRoomId As Integer
        Public AppearanceLocalId As Integer
        Public AppearanceIconType As String
        Public AppearanceIconColor As String
        ' その人のラウンジの「自分の顔」（プロフィールの枠）を書き換える。そのブラウザのラウンジの接続へだけ配る
        Public SelfFaceTo As List(Of String)
        Public SelfFaceIconType As String
        Public SelfFaceIconColor As String
        Public SelfFaceSeq As Long
        ' 管理人が部屋の設定を変えた。部屋の全員へ roomSettingsChanged を配る（0 なら配らない）
        Public RoomSettingsRoomId As Integer
        Public RoomSettings As RoomSettingsInfo
        Public RoomSettingsEventText As String
        ' 記録の本文（部屋の設定の変更＝61 だけが使う。何をどう変えたか。パスワードそのものは入れない）
        Public LogText As String
    End Class

    ' 【どれも SyncRoot の中でしか触らないこと】
    Private Shared KnockRequests As New Dictionary(Of Integer, PendingKnock)
    Private Shared NextKnockId As Integer = 1

    ' キー：「部屋ID|ChatBrowserId（N 形式）」（RoomKeys.vb の RoomPassKey と同じ形）
    Private Shared KnockSeats As New Dictionary(Of String, KnockSeat)(StringComparer.Ordinal)

    ' 最後にノックを受け付けた時刻（キー：「b|ブラウザ」「i|IPアドレス」）。
    ' **受け付けたときだけ進める**（断ったときに進めると、押し続けている人がいつまでもノックできない。
    ' 発言の連投の時計と同じ決まり）
    Private Shared LastKnockAt As New Dictionary(Of String, DateTime)(StringComparer.Ordinal)

    Private Shared RoomNoticeOutbox As New List(Of RoomNotice)


    ' ============================================================
    '  ノックする（ラウンジの画面から）
    ' ============================================================
    '
    ' 【ラウンジの接続から呼ぶ】許可の返事を、その場で届けるため（KnockAnswered）。
    ' 身元は Cookie、**名前は名前の札（ChatNameToken）から読む**（JoinRoom・JoinLounge と同じ。
    ' クライアントに名乗らせない。、名前の Cookie から札へ移した）
    '
    ' 【DBへの問い合わせは1回】部屋の状態（開いているか・一時部屋か・ノックを受け付けるか・定員・部屋名）。
    ' 連打は RoomKnockInterval で止まるので、回数は抑えられている。
    ' DBが読めないときは**断る**（フェイルクローズ。ノックは入口を開ける頼みごとなので、分からないときは通さない）
    Public Function KnockRoom(roomId As Integer) As KnockResult

        Dim browser As Guid

        If Not Guid.TryParse(GetBrowserIdFromCookie(), browser) Then
            Return KnockRejected(roomId,
                "ブラウザの識別情報が読み取れないため、ノックできません。お手数ですが、名前とアイコンを決め直してください。")
        End If

        ' 見た目（名前の検査は SiteSettings を読むので、鍵の外で）
        ' アカウントID を表示している人なら、トリップの代わりにユーザー名が入る（JoinLounge と同じ）
        Dim profile As LoungeProfile = CreateLoungeProfile(
            ReadTokenNickname(),
            GetCookieValue("ChatIconType"),
            GetCookieValue("ChatIconColor"),
            TripCode.FromCookieValue(GetCookieValue(TripCode.CookieName)),
            ResolveAccountLink().UserName)

        If profile Is Nothing Then
            Return KnockRejected(roomId, "名前を決めてから、ノックしてください。")
        End If

        Dim ipAddress As String = Nothing

        Try
            ipAddress = Context.Request.GetHttpContext().Request.UserHostAddress
        Catch ex As Exception
            ' 取れなければ、ブラウザの単位だけで間隔を見る
        End Try

        ' 接続元のポート番号（ノックの記録に、IP と一緒に入れる。Core/ClientEndpoint.vb）
        Dim remotePort As Integer? = Nothing

        Try
            remotePort = ClientEndpoint.ReadPort(Context.Request.GetHttpContext().Request)
        Catch ex As Exception
            ' 取れなければ NULL のまま記録する
        End Try

        Dim decision As TimeSpan = SiteSettings.RoomKnockDecision
        Dim interval As TimeSpan = SiteSettings.RoomKnockInterval

        ' 記録の歯止め（満員・パスワードと同じ）。このノックを記録するかは、下の鍵の中で1回だけ決める
        Dim rejectLogCooldown As TimeSpan = SiteSettings.RejectLogCooldown
        Dim rejectLogTrackingLimit As Integer = SiteSettings.RejectLogTrackingLimit

        Dim room As KnockRoomState = Nothing

        If Not TryGetKnockRoomState(roomId, browser, room) Then
            Return KnockRejected(roomId, "今はノックできません。少し待ってから、もう一度お試しください。")
        End If

        If room Is Nothing OrElse Not room.IsOpen Then
            Return KnockRejected(roomId, ChatCommon.RoomClosedMessage)
        End If

        If Not room.IsTemporary OrElse Not room.IsKnockEnabled Then
            Return KnockRejected(roomId, "この部屋は、ノックを受け付けていません。")
        End If

        ' 入室禁止になっている人は、ノックもできない（入れないので、管理人に聞く意味が無い）。
        ' 部屋の中に「ノックしました」の行も音も出さない＝禁止にした人から頼まれ続けることが無い
        If room.IsBanned Then
            Return KnockRejected(roomId, BannedJoinMessage)
        End If

        Dim now As DateTime = DateTime.Now
        Dim request As PendingKnock = Nothing
        Dim ownerConnectionId As String = Nothing

        Dim browserKey As String = "b|" & browser.ToString("N")
        Dim ipKey As String = If(String.IsNullOrEmpty(ipAddress), Nothing, "i|" & ipAddress)

        SyncLock SyncRoot

            ' DBから読めた種別と管理人を控える（JoinRoom と同じ。知らない部屋のときだけ管理人を採る）
            NoteRoomStateCore(roomId, If(room.IsTemporary, ChatRoomRules.RoomTypeTemporary, Nothing), room.OwnerBrowserId)

            ' 見た目は、ラウンジの控えの顔があればそちらを使う。
            ' 上で読んだのは「ラウンジの接続を張った時点」の Cookie で、そのあと別のタブの部屋で色を変えていると古い。
            ' 控えは部屋での見た目の変更（UpdateLoungeAppearanceCore）とトリップの付け外しで書き換えてあるので、こちらが新しい。
            ' 名前は控えと Cookie で変わらない（名前を変える＝ログアウト＝別のブラウザ）
            Dim knownFace As LoungeProfile = Nothing

            If LoungeProfiles.TryGetValue(browser, knownFace) Then
                profile.IconType = knownFace.IconType
                profile.IconColor = knownFace.IconColor
                profile.Trip = knownFace.Trip
                profile.AccountName = knownFace.AccountName
            End If

            ' 連打（同じブラウザか、同じIP）
            If IsKnockTooSoonCore(browserKey, now, interval) OrElse
               (ipKey IsNot Nothing AndAlso IsKnockTooSoonCore(ipKey, now, interval)) Then
                Return KnockRejected(roomId, "続けてノックはできません。少し待ってから、もう一度お試しください。")
            End If

            Dim participants As List(Of Participant) = Nothing

            If Not RoomParticipants.TryGetValue(roomId, participants) Then
                participants = New List(Of Participant)
            End If

            If participants.Any(Function(p) SameBrowser(p.BrowserId, browser)) Then
                Return KnockRejected(roomId, "もうこの部屋に入っています。")
            End If

            ' 満室でなければ、ノックは要らない（画面は、そのまま入室の流れへ進む）。
            ' 【返事待ち・取り置きの確認より先に見る】待っている間に席が空いたら、返事を待たずに入ってよい
            ' （入れば、JoinRoom が返事待ちのノックを取り下げ、取り置きも使い終わりにする）
            If participants.Count < room.MaxUsers Then
                Return New KnockResult With {
                    .Status = KnockStatusNotFull,
                    .RoomId = roomId,
                    .RoomName = room.RoomName
                }
            End If

            ' もう許可されている（小窓を閉じてしまった、など）。入室の小窓を出し直してもらう
            Dim seat As KnockSeat = Nothing

            If KnockSeats.TryGetValue(RoomPassKey(roomId, browser), seat) AndAlso seat.ExpiresAt > now Then
                Return New KnockResult With {
                    .Status = KnockStatusApproved,
                    .RoomId = roomId,
                    .RoomName = room.RoomName,
                    .Seconds = SecondsUntil(seat.ExpiresAt, now)
                }
            End If

            ' もうノックしてある。返事を待ってもらう（2度目は受け付けない＝部屋の中に行も音も出さない）
            Dim waiting As PendingKnock = KnockRequests.Values.FirstOrDefault(
                Function(r) r.RoomId = roomId AndAlso r.Browser = browser AndAlso r.ExpiresAt > now)

            If waiting IsNot Nothing Then
                Return New KnockResult With {
                    .Status = KnockStatusWaiting,
                    .RoomId = roomId,
                    .RoomName = room.RoomName,
                    .Seconds = SecondsUntil(waiting.ExpiresAt, now),
                    .Message = "ノックしてあります。管理人の返事を待ってください。"
                }
            End If

            ' 管理人が部屋に居なければ、許可できる人が居ない
            Dim ownerParticipant As Participant = FindOwnerParticipantCore(roomId, participants)

            If ownerParticipant Is Nothing Then
                Return KnockRejected(roomId, "この部屋の管理人が見つからないため、今はノックできません。")
            End If

            If KnockRequests.Values.Where(Function(r) r.RoomId = roomId).Count() >= MaxPendingKnocksPerRoom Then
                Return KnockRejected(roomId, "今、この部屋にはノックが集まっています。少し待ってから、もう一度お試しください。")
            End If

            ' 受け付ける
            request = New PendingKnock With {
                .KnockId = NextKnockId,
                .RoomId = roomId,
                .RoomName = room.RoomName,
                .MaxUsers = room.MaxUsers,
                .Browser = browser,
                .Knocker = New Participant With {
                    .LocalId = 0,
                    .Nickname = profile.Nickname,
                    .IconType = profile.IconType,
                    .IconColor = profile.IconColor,
                    .Trip = profile.Trip,
                    .AccountName = profile.AccountName,
                    .BrowserId = browser.ToString(),
                    .IpAddress = ipAddress,
                    .RemotePort = remotePort
                },
                .ExpiresAt = now + decision
            }

            NextKnockId += 1
            KnockRequests(request.KnockId) = request

            ' このノックを記録するか（42）を、ここで1回だけ決める。行く末（43〜46）も、この判断に従う
            request.ShouldLog = ShouldLogJoinReject(ChatMessageType.KnockRequested, roomId, request.Knocker.BrowserId,
                                                    rejectLogCooldown, rejectLogTrackingLimit)

            LastKnockAt(browserKey) = now

            If ipKey IsNot Nothing Then
                LastKnockAt(ipKey) = now
            End If

            ' 管理人が今繋がっていれば、その接続へ小窓を送る。
            ' 切れていても受け付ける（繋ぎ直したら JoinRoomResult.PendingKnocks で受け取れる）
            If ownerParticipant.IsConnected Then
                ownerConnectionId = ownerParticipant.ConnectionId
            End If

        End SyncLock

        ' === ここから下は、鍵の外 ===

        ' 部屋の中へ「〇〇さんがノックしました」と音。
        ' 匿名IDを添えるのは、その人を無視している画面が行も音も出さないため（無視機能の決まり）
        Dim knockerAnonId As String = AnonId.Compute(roomId, browser)

        Clients.Group(roomId.ToString()).KnockReceived(ChatEventText.BuildKnock(profile.Nickname), knockerAnonId)

        If ownerConnectionId IsNot Nothing Then
            Clients.Client(ownerConnectionId).KnockRequested(ToKnockRequestInfo(request, now))
        End If

        If request.ShouldLog Then
            ChatLog.Write(roomId, ChatMessageType.KnockRequested, request.Knocker)
        End If

        Return New KnockResult With {
            .Status = KnockStatusWaiting,
            .RoomId = roomId,
            .RoomName = room.RoomName,
            .Seconds = SecondsUntil(request.ExpiresAt, now)
        }

    End Function


    ' ============================================================
    '  許可する・断る（部屋の管理人の画面から）
    ' ============================================================
    '
    ' 【管理人かどうかは、サーバーが決める】呼んだ接続から部屋の参加者を探し、
    ' その人のブラウザが今の管理人（RoomOwners）と一致するときだけ受け付ける。
    ' ノックの番号（knockId）はクライアントの言い値だが、**その部屋のノックでなければ受け付けない**
    Public Function AnswerKnock(knockId As Integer, approve As Boolean) As AnswerKnockResult

        Dim roomId As Integer = 0
        Dim answerer As Participant = FindParticipant(Context.ConnectionId, roomId)

        If answerer Is Nothing Then
            Return AnswerRejected(False, "部屋に入っていないため、返事ができません。")
        End If

        ' 鍵を取る前に読む
        Dim seatHold As TimeSpan = SiteSettings.RoomKnockSeatHold
        Dim extraSeats As Integer = SiteSettings.RoomKnockExtraSeats

        Dim now As DateTime = DateTime.Now
        Dim notice As RoomNotice = Nothing

        SyncLock SyncRoot

            Dim request As PendingKnock = Nothing

            If Not KnockRequests.TryGetValue(knockId, request) OrElse request.RoomId <> roomId OrElse
               request.ExpiresAt <= now Then
                Return AnswerRejected(False, "このノックには、もう返事ができません（時間切れか、取り消されました）。")
            End If

            If Not IsRoomOwnerCore(roomId, answerer.BrowserId) Then
                Return AnswerRejected(False, "部屋の管理人ではないため、返事ができません。")
            End If

            Dim participants As List(Of Participant) = Nothing

            If Not RoomParticipants.TryGetValue(roomId, participants) Then
                participants = New List(Of Participant)
            End If

            If Not approve Then

                KnockRequests.Remove(knockId)

                notice = New RoomNotice With {
                    .AnswerTo = LoungeConnectionsOfCore(request.Browser),
                    .Answer = New KnockAnswer With {
                        .Status = KnockStatusDenied, .RoomId = roomId, .RoomName = request.RoomName},
                    .LogType = KnockLogTypeIf(request.ShouldLog, ChatMessageType.KnockDenied),
                    .LogRoomId = roomId,
                    .LogParticipant = request.Knocker
                }

            Else

                ' 返事を待つ間に、ふつうに入ってきていた（席が空いた）。取り置きは要らない
                If participants.Any(Function(p) SameBrowser(p.BrowserId, request.Browser)) Then
                    KnockRequests.Remove(knockId)
                    Return New AnswerKnockResult With {.Ok = True}
                End If

                ' 臨時の席がまだあるか（今の人数 ＋ ほかの人の取り置き）
                Dim otherSeats As Integer = KnockSeats.Values.Where(
                    Function(s) s.RoomId = roomId AndAlso s.ExpiresAt > now AndAlso s.Browser <> request.Browser).Count()

                If participants.Count + otherSeats >= request.MaxUsers + extraSeats Then
                    ' 小窓は残す（あとで断れるように）
                    Return AnswerRejected(True,
                        "これ以上は入れられません（定員を超えて入れるのは" & extraSeats & "人までです）。")
                End If

                KnockRequests.Remove(knockId)

                KnockSeats(RoomPassKey(roomId, request.Browser)) = New KnockSeat With {
                    .RoomId = roomId,
                    .RoomName = request.RoomName,
                    .Browser = request.Browser,
                    .Knocker = request.Knocker,
                    .ExpiresAt = now + seatHold,
                    .KnockId = request.KnockId,
                    .ShouldLog = request.ShouldLog
                }

                notice = New RoomNotice With {
                    .AnswerTo = LoungeConnectionsOfCore(request.Browser),
                    .Answer = New KnockAnswer With {
                        .Status = KnockStatusApproved, .RoomId = roomId, .RoomName = request.RoomName,
                        .Seconds = CInt(Math.Floor(seatHold.TotalSeconds))},
                    .LogType = KnockLogTypeIf(request.ShouldLog, ChatMessageType.KnockApproved),
                    .LogRoomId = roomId,
                    .LogParticipant = request.Knocker
                }

            End If

        End SyncLock

        SendRoomNotice(notice)

        Return New AnswerKnockResult With {.Ok = True}

    End Function


    ' ============================================================
    '  取り置きを自分から手放す（許可の小窓で「やめる」を押した。ラウンジの画面から）
    ' ============================================================
    Public Sub ReleaseKnockSeat(roomId As Integer)

        Dim browser As Guid

        If Not Guid.TryParse(GetBrowserIdFromCookie(), browser) Then
            Return
        End If

        Dim notice As RoomNotice = Nothing

        SyncLock SyncRoot

            Dim key As String = RoomPassKey(roomId, browser)
            Dim seat As KnockSeat = Nothing

            If KnockSeats.TryGetValue(key, seat) Then

                KnockSeats.Remove(key)

                ' ノックした本人へは配らない（本人の操作）。記録を残し（入らなかった）、
                ' 管理人の画面には「入室をやめました」を出す
                notice = New RoomNotice With {
                    .LogType = KnockLogTypeIf(seat.ShouldLog, ChatMessageType.KnockSeatExpired),
                    .LogRoomId = roomId,
                    .LogParticipant = seat.Knocker
                }

                AddSeatWithdrawCore(notice, seat, KnockWithdrawReason.SeatReleased)

            End If

        End SyncLock

        SendRoomNotice(notice)

    End Sub


    ' ============================================================
    '  ラウンジの入室（Lounge.EnterRoom）から
    ' ============================================================

    ' 取り置きを、部屋の画面が開くまでの間ぶん延ばす（1回だけ）
    '
    ' 【なぜ要るのか】取り置きは「入室する」を押すまでの時間（既定30秒）。
    ' パスワード付きの部屋では、押したあとに小窓で打つ時間がかかり、そのあいだに切れてしまう。
    ' 押した（＝EnterRoom が呼ばれた）ときに、「鍵を通った印」と同じ長さ（RoomPassLifetime）まで延ばす。
    ' **1回だけ**にしてあるのは、何度も呼んで席を握り続けられないため
    Public Shared Sub ExtendKnockSeat(roomId As Integer, browser As Guid)

        Dim now As DateTime = DateTime.Now

        SyncLock SyncRoot

            Dim seat As KnockSeat = Nothing

            If Not KnockSeats.TryGetValue(RoomPassKey(roomId, browser), seat) OrElse
               seat.Extended OrElse seat.ExpiresAt <= now Then
                Return
            End If

            seat.Extended = True

            If seat.ExpiresAt < now + RoomPassLifetime Then
                seat.ExpiresAt = now + RoomPassLifetime
            End If

        End SyncLock

    End Sub


    ' ============================================================
    '  JoinRoom から（どれも SyncRoot の中で呼ぶ）
    ' ============================================================

    ' このブラウザが、この部屋に席を取り置いてもらっているか（期限切れは持っていない扱い）
    Private Shared Function HasKnockSeatCore(roomId As Integer, browserId As String, now As DateTime) As Boolean

        Dim parsed As Guid

        If Not Guid.TryParse(browserId, parsed) Then
            Return False
        End If

        Dim seat As KnockSeat = Nothing

        Return KnockSeats.TryGetValue(RoomPassKey(roomId, parsed), seat) AndAlso seat.ExpiresAt > now

    End Function


    ' 部屋に入れた。取り置きを使い終わりにし、返事待ちのノックがあれば取り下げる
    ' （待っている間に普通に席が空いて入った場合。管理人の小窓も消す）
    Private Shared Sub ConsumeKnocksOnJoinCore(roomId As Integer, browserId As String)

        Dim parsed As Guid

        If Not Guid.TryParse(browserId, parsed) Then
            Return
        End If

        KnockSeats.Remove(RoomPassKey(roomId, parsed))

        Dim mine = KnockRequests.Values.Where(
            Function(r) r.RoomId = roomId AndAlso r.Browser = parsed).ToList()

        If mine.Count = 0 Then
            Return
        End If

        Dim ownerConnectionId As String = OwnerConnectionIdCore(roomId)

        For Each request In mine

            KnockRequests.Remove(request.KnockId)

            If ownerConnectionId IsNot Nothing Then
                RoomNoticeOutbox.Add(New RoomNotice With {
                    .WithdrawTo = ownerConnectionId,
                    .WithdrawKnockId = request.KnockId
                })
            End If

        Next

    End Sub


    ' 管理人に渡す「返事待ちのノック」（入った人が管理人でなければ Nothing）
    Private Shared Function PendingKnocksForOwnerCore(roomId As Integer,
                                                      participant As Participant,
                                                      now As DateTime) As List(Of KnockRequestInfo)

        If participant Is Nothing OrElse Not IsRoomOwnerCore(roomId, participant.BrowserId) Then
            Return Nothing
        End If

        Return KnockRequests.Values.Where(
            Function(r) r.RoomId = roomId AndAlso r.ExpiresAt > now).OrderBy(
            Function(r) r.KnockId).Select(
            Function(r) ToKnockRequestInfo(r, now)).ToList()

    End Function


    ' ============================================================
    '  部屋の一生から（どれも SyncRoot の中で呼ぶ）
    ' ============================================================

    ' その部屋の返事待ちのノックを、無かったことにする（管理人が居なくなった・部屋が閉じた）
    '
    ' ノックした人には「取り消されました」を知らせる（黙って消すと、60秒待たされたあと「返事が無かった」になる）。
    ' 記録は残さない（：無かったことにする）。
    ' 管理人の画面の小窓は消しに行かない（その管理人は、もう部屋を出ている）
    '
    ' closeSeats … True なら、取り置いた席も捨てる（部屋が閉じたとき）。
    '              管理人が替わっただけなら、**取り置いた席は消さない**
    Private Shared Sub CancelRoomKnocksCore(roomId As Integer, closeSeats As Boolean)

        For Each request In KnockRequests.Values.Where(Function(r) r.RoomId = roomId).ToList()

            KnockRequests.Remove(request.KnockId)

            RoomNoticeOutbox.Add(New RoomNotice With {
                .AnswerTo = LoungeConnectionsOfCore(request.Browser),
                .Answer = New KnockAnswer With {
                    .Status = KnockStatusCancelled, .RoomId = roomId, .RoomName = request.RoomName}
            })

        Next

        If Not closeSeats Then
            Return
        End If

        For Each entry In KnockSeats.Where(Function(e) e.Value.RoomId = roomId).ToList()

            KnockSeats.Remove(entry.Key)

            RoomNoticeOutbox.Add(New RoomNotice With {
                .AnswerTo = LoungeConnectionsOfCore(entry.Value.Browser),
                .Answer = New KnockAnswer With {
                    .Status = KnockStatusCancelled, .RoomId = roomId, .RoomName = entry.Value.RoomName}
            })

        Next

    End Sub


    ' そのブラウザのノックと取り置きを、全部捨てる（ログアウトしたとき）
    '
    ' ログアウトすると ChatBrowserId が作り直されるので、前の身元のノックは誰にも使えない。
    ' 残しておくと、管理人が「居ない人」を許可して、席が30秒ふさがる。
    ' 記録は残さない（本人が居なくなっただけで、ノックの行く末ではない）
    Private Shared Sub ForgetKnocksOfBrowserCore(browser As Guid)

        For Each request In KnockRequests.Values.Where(Function(r) r.Browser = browser).ToList()

            KnockRequests.Remove(request.KnockId)

            Dim ownerConnectionId As String = OwnerConnectionIdCore(request.RoomId)

            If ownerConnectionId IsNot Nothing Then
                RoomNoticeOutbox.Add(New RoomNotice With {
                    .WithdrawTo = ownerConnectionId,
                    .WithdrawKnockId = request.KnockId,
                    .WithdrawText = ChatEventText.BuildKnockWithdrawn(KnockWithdrawReason.LoggedOut, request.Knocker.Nickname),
                    .WithdrawAnonId = AnonId.Compute(request.RoomId, request.Browser)
                })
            End If

        Next

        For Each entry In KnockSeats.Where(Function(e) e.Value.Browser = browser).ToList()

            KnockSeats.Remove(entry.Key)

            ' 許可したのに来ない理由を、管理人にも
            Dim seatNotice As New RoomNotice()
            AddSeatWithdrawCore(seatNotice, entry.Value, KnockWithdrawReason.SeatLoggedOut)

            If seatNotice.WithdrawTo IsNot Nothing Then
                RoomNoticeOutbox.Add(seatNotice)
            End If

        Next

    End Sub


    ' ============================================================
    '  見回り（CheckTimeoutParticipants の鍵の中から）
    ' ============================================================

    ' 時間切れのノックと取り置きを片付ける（知らせることは RoomNoticeOutbox へ）
    Private Shared Sub CollectExpiredKnocksCore(now As DateTime)

        If KnockRequests.Count > 0 Then

            For Each request In KnockRequests.Values.Where(Function(r) r.ExpiresAt <= now).ToList()

                KnockRequests.Remove(request.KnockId)

                RoomNoticeOutbox.Add(New RoomNotice With {
                    .AnswerTo = LoungeConnectionsOfCore(request.Browser),
                    .Answer = New KnockAnswer With {
                        .Status = KnockStatusExpired, .RoomId = request.RoomId, .RoomName = request.RoomName},
                    .WithdrawTo = OwnerConnectionIdCore(request.RoomId),
                    .WithdrawKnockId = request.KnockId,
                    .WithdrawText = ChatEventText.BuildKnockWithdrawn(KnockWithdrawReason.Expired, request.Knocker.Nickname),
                    .WithdrawAnonId = AnonId.Compute(request.RoomId, request.Browser),
                    .LogType = KnockLogTypeIf(request.ShouldLog, ChatMessageType.KnockExpired),
                    .LogRoomId = request.RoomId,
                    .LogParticipant = request.Knocker
                })

            Next

        End If

        If KnockSeats.Count > 0 Then

            For Each entry In KnockSeats.Where(Function(e) e.Value.ExpiresAt <= now).ToList()

                KnockSeats.Remove(entry.Key)

                Dim seatNotice As New RoomNotice With {
                    .AnswerTo = LoungeConnectionsOfCore(entry.Value.Browser),
                    .Answer = New KnockAnswer With {
                        .Status = KnockStatusSeatExpired, .RoomId = entry.Value.RoomId,
                        .RoomName = entry.Value.RoomName},
                    .LogType = KnockLogTypeIf(entry.Value.ShouldLog, ChatMessageType.KnockSeatExpired),
                    .LogRoomId = entry.Value.RoomId,
                    .LogParticipant = entry.Value.Knocker
                }

                ' 管理人の画面にも「入室しませんでした」
                AddSeatWithdrawCore(seatNotice, entry.Value, KnockWithdrawReason.SeatExpired)

                RoomNoticeOutbox.Add(seatNotice)

            Next

        End If

        If LastKnockAt.Count > 0 Then

            For Each key In LastKnockAt.Where(
                Function(e) now - e.Value >= KnockIntervalMemory).Select(Function(e) e.Key).ToList()
                LastKnockAt.Remove(key)
            Next

        End If

    End Sub


    ' 鍵の中で積んだ知らせを、配る・記録する
    '
    ' 【必ず SyncRoot を抜けてから呼ぶこと】入退室の後始末（RemoveParticipantAndNotify・JoinRoom）、
    ' 見回り（CheckTimeoutParticipants）、ログアウト（LeaveByBrowserId）が、鍵を抜けたあとに呼ぶ。
    ' 誰が積んだものでも、最初に呼んだ人がまとめて配る（順番が前後しても、どれも独立した知らせである）
    Private Shared Sub FlushRoomNotices()

        Dim notices As List(Of RoomNotice)

        SyncLock SyncRoot

            If RoomNoticeOutbox.Count = 0 Then
                Return
            End If

            notices = RoomNoticeOutbox
            RoomNoticeOutbox = New List(Of RoomNotice)

        End SyncLock

        For Each notice In notices
            SendRoomNotice(notice)
        Next

    End Sub


    ' 知らせ1つを配り、記録する（鍵の外で）
    '
    ' 【配れなくても、止めない】相手の接続が消えかけていることは普通にある。知らせるだけ知らせる
    Private Shared Sub SendRoomNotice(notice As RoomNotice)

        If notice Is Nothing Then
            Return
        End If

        Try

            Dim hubContext = GlobalHost.ConnectionManager.GetHubContext(Of ChatHub)()

            If notice.Answer IsNot Nothing AndAlso notice.AnswerTo IsNot Nothing AndAlso notice.AnswerTo.Count > 0 Then
                hubContext.Clients.Clients(notice.AnswerTo).KnockAnswered(notice.Answer)
            End If

            ' 小窓を消す（理由の文言・匿名ID・「小窓が無くても出すか」も添える。
            ' サーバーからクライアントへの呼び出しなので、末尾に足しても古い画面は壊れない）
            If notice.WithdrawTo IsNot Nothing Then
                hubContext.Clients.Client(notice.WithdrawTo).KnockWithdrawn(
                    notice.WithdrawKnockId, notice.WithdrawText, notice.WithdrawAnonId, notice.WithdrawStandalone)
            End If

            ' ラウンジの「自分が入っている部屋」
            If notice.SelfRoomTo IsNot Nothing AndAlso notice.SelfRoomTo.Count > 0 Then
                hubContext.Clients.Clients(notice.SelfRoomTo).LoungeSelfRoom(notice.SelfRoomId, notice.SelfRoomSeq)
            End If

            ' 管理人の交代。部屋の全員へ（新しい管理人本人にも）。
            ' 3つ目の designated は 足した（指名でゆずったら画面が音を鳴らす。古い画面は余った引数を捨てるので壊れない）
            If notice.OwnerChangedRoomId > 0 Then
                hubContext.Clients.Group(notice.OwnerChangedRoomId.ToString()).OwnerChanged(
                    notice.OwnerLocalId, notice.OwnerEventText, notice.OwnerDesignated)
            End If

            ' ラウンジの「自分の顔」。そのブラウザのラウンジの接続へだけ（ブラウザの識別子は載せない）
            If notice.SelfFaceTo IsNot Nothing AndAlso notice.SelfFaceTo.Count > 0 Then
                hubContext.Clients.Clients(notice.SelfFaceTo).LoungeSelfFace(
                    notice.SelfFaceIconType, notice.SelfFaceIconColor, notice.SelfFaceSeq)
            End If

            ' 部屋の設定を変えた。部屋の全員へ（変えた管理人にも。設定は「置き換える」、1行は画面が出す）
            If notice.RoomSettingsRoomId > 0 Then
                hubContext.Clients.Group(notice.RoomSettingsRoomId.ToString()).RoomSettingsChanged(
                    notice.RoomSettings, notice.RoomSettingsEventText)
            End If

            ' 見た目を変えた。部屋の全員へ（変えた本人にも。どれも「置き換える」だけなので、2度届いても壊れない）
            If notice.AppearanceRoomId > 0 Then
                hubContext.Clients.Group(notice.AppearanceRoomId.ToString()).AppearanceChanged(
                    notice.AppearanceLocalId, notice.AppearanceIconType, notice.AppearanceIconColor)
            End If

            ' ラウンジの管理人の印・見た目を変えた人の顔
            If notice.LoungeUpdate IsNot Nothing Then
                PublishLoungeUpdate(notice.LoungeUpdate)
            End If

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.SendRoomNotice")
        End Try

        If notice.LogType.HasValue AndAlso notice.LogParticipant IsNot Nothing Then
            ChatLog.Write(notice.LogRoomId, notice.LogType.Value, notice.LogParticipant, messageText:=notice.LogText)
        End If

    End Sub


    ' ノックの行く末（43〜46）を記録するときの種別。元のノックを記録しなかったなら Nothing（記録しない）
    '
    ' 【ノックの記録は、1回のノックにつき1回だけ決める】（B。理由：数が合わない表は読まれない）
    ' 決めるのはノックを受け付けたとき（KnockRoom。満員・パスワードと同じ歯止め＝ShouldLogJoinReject を、種別 42 で1回だけ通す）。
    ' 行く末は、その判断をそのまま持ち回す（PendingKnock.ShouldLog → KnockSeat.ShouldLog）。
    ' 42 を書かなかったノックは、43〜46 も書かない。これで集計の数が、作りとして噛み合う：
    '   ・43（許可）＋44（断った）＋45（返事が無かった）≦ 42（ノックした）
    '     1回のノックの行く末は、この3つのどれか1つか、記録しないもの（取り消し・普通に入ってきた・ログアウト）
    '   ・46（許可されたが入らなかった）≦ 43（許可）…… 46 は許可されたノックのその先なので、43 の内訳である
    '     （1回のノックで 43 と 46 の両方が書かれることがある。だから 42 ≧ 43+44+45+46 ではない）
    ' 45 は60秒・46 は30秒（延長しても60秒）で決まるので、どちらも10分の歯止めの窓の中に収まる。
    ' 【日をまたいだノックだけは例外】ノックが 23:59、行く末が翌日なら、集計は別々の日に入る（ChatMessages.CreatedAt の日で数えるため）
    Private Shared Function KnockLogTypeIf(shouldLog As Boolean, messageType As ChatMessageType) As ChatMessageType?

        If shouldLog Then
            Return messageType
        End If

        Return Nothing

    End Function


    ' ============================================================
    '  小さな道具
    ' ============================================================

    ' 取り置きが外れたことを、管理人の画面へ知らせる準備
    '
    ' 小窓は許可した時点で消えているので、「小窓が無くても出す」（WithdrawStandalone）で送る。
    ' 管理人が部屋に居ない・繋がっていなければ、何も足さない
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub AddSeatWithdrawCore(notice As RoomNotice, seat As KnockSeat, reason As KnockWithdrawReason)

        Dim ownerConnectionId As String = OwnerConnectionIdCore(seat.RoomId)

        If ownerConnectionId Is Nothing Then
            Return
        End If

        notice.WithdrawTo = ownerConnectionId
        notice.WithdrawKnockId = seat.KnockId
        notice.WithdrawText = ChatEventText.BuildKnockWithdrawn(reason, seat.Knocker.Nickname)
        notice.WithdrawAnonId = AnonId.Compute(seat.RoomId, seat.Browser)
        notice.WithdrawStandalone = True

    End Sub


    ' そのブラウザが今入っている部屋（居なければ 0）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function RoomIdOfBrowserCore(browser As Guid) As Integer

        If browser = Guid.Empty Then
            Return 0
        End If

        For Each roomEntry In RoomParticipants
            If roomEntry.Value.Any(Function(p) SameBrowser(p.BrowserId, browser)) Then
                Return roomEntry.Key
            End If
        Next

        Return 0

    End Function


    ' そのブラウザのラウンジへ「あなたが入っている部屋」を知らせる準備
    '
    ' 開発者の実機で、管理人が別のタブでラウンジを開くと、自分の部屋に「ノック」が出ていた
    ' （押すと「もうこの部屋に入っています」と断られる）。ラウンジの画面は、その部屋のボタンを「入室中」にする。
    '
    ' 【ほかの人には配らない】誰がどの部屋に居るかは、ラウンジの顔で見えるが、
    ' 「この画面の持ち主はここ」は本人だけの話（ブラウザの識別子は、どこにも出さない）。
    '
    ' 【通し番号を付ける】入った・出たの知らせが、別々の場所から配られて入れ替わって届くことがある。
    ' 画面は番号の大きいほうだけを信じる（丸ごとにも同じ番号が入る）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】参加者リストを書き換えたあとに呼ぶ
    Private Shared Sub QueueSelfRoomCore(browserId As String)

        Dim browser As Guid

        If Not Guid.TryParse(browserId, browser) Then
            Return
        End If

        Dim connections As List(Of String) = LoungeConnectionsOfCore(browser)

        If connections.Count = 0 Then
            Return
        End If

        SelfRoomSeq += 1

        RoomNoticeOutbox.Add(New RoomNotice With {
            .SelfRoomTo = connections,
            .SelfRoomId = RoomIdOfBrowserCore(browser),
            .SelfRoomSeq = SelfRoomSeq
        })

    End Sub

    ' 「自分が入っている部屋」の知らせの通し番号（SyncRoot の中でだけ進める）。
    '、ラウンジの「自分の顔」（loungeSelfFace）もこの番号で数える
    ' （どちらも「この画面の持ち主」だけに配る知らせで、丸ごとと配信の順番が入れ替わりうる。画面は種類ごとに、大きいほうだけを信じる）
    Private Shared SelfRoomSeq As Long = 0


    ' その部屋の管理人の、部屋の中の番号（居なければ 0）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function OwnerLocalIdCore(roomId As Integer) As Integer

        Dim participants As List(Of Participant) = Nothing

        If Not RoomParticipants.TryGetValue(roomId, participants) Then
            Return 0
        End If

        Dim owner As Participant = FindOwnerParticipantCore(roomId, participants)

        Return If(owner Is Nothing, 0, owner.LocalId)

    End Function

    ' 部屋の状態（ノックの判断に使うぶん）
    Private NotInheritable Class KnockRoomState
        Public RoomName As String
        Public MaxUsers As Integer
        Public IsOpen As Boolean
        Public IsTemporary As Boolean
        Public IsKnockEnabled As Boolean
        Public OwnerBrowserId As Guid
        ' ノックした人が、この部屋で入室禁止か
        Public IsBanned As Boolean
    End Class


    ' 部屋の状態をDBから読む。戻り値は「問い合わせに成功したか」。行が無ければ room は Nothing
    ' browser … ノックした人（入室禁止かを同じ1回で見る）
    Private Shared Function TryGetKnockRoomState(roomId As Integer, browser As Guid, ByRef room As KnockRoomState) As Boolean

        room = Nothing

        Try

            Using conn As New SqlConnection(ConfigurationManager.ConnectionStrings(RoomConnectionName).ConnectionString)
                Using cmd As New SqlCommand(
                    "SELECT RoomName, MaxUsers, IsActive, RoomType, IsKnockEnabled, OwnerBrowserId, " & IsBannedSqlColumn &
                    "  FROM ChatRooms WHERE RoomId = @RoomId", conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@BanBrowserId", SqlDbType.UniqueIdentifier).Value = browser

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()

                        If reader.Read() Then

                            room = New KnockRoomState With {
                                .RoomName = Convert.ToString(reader("RoomName")),
                                .MaxUsers = CInt(reader("MaxUsers")),
                                .IsOpen = CBool(reader("IsActive")),
                                .IsTemporary = String.Equals(Convert.ToString(reader("RoomType")),
                                                             ChatRoomRules.RoomTypeTemporary,
                                                             StringComparison.OrdinalIgnoreCase),
                                .IsKnockEnabled = CBool(reader("IsKnockEnabled")),
                                .OwnerBrowserId = If(IsDBNull(reader("OwnerBrowserId")), Guid.Empty,
                                                     CType(reader("OwnerBrowserId"), Guid)),
                                .IsBanned = (Convert.ToInt32(reader("IsBanned")) = 1)
                            }

                        End If

                    End Using

                End Using
            End Using

            Return True

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryGetKnockRoomState", "部屋 " & roomId)
            Return False
        End Try

    End Function


    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsKnockTooSoonCore(key As String, now As DateTime, interval As TimeSpan) As Boolean

        Dim last As DateTime

        Return LastKnockAt.TryGetValue(key, last) AndAlso now - last < interval

    End Function


    ' その人のブラウザが、今の管理人か
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsRoomOwnerCore(roomId As Integer, browserId As String) As Boolean

        Dim owner As Guid

        If Not RoomOwners.TryGetValue(roomId, owner) OrElse owner = Guid.Empty Then
            Return False
        End If

        Return SameBrowser(browserId, owner)

    End Function


    ' 部屋に居る管理人（居なければ Nothing）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function FindOwnerParticipantCore(roomId As Integer,
                                                     participants As List(Of Participant)) As Participant

        Dim owner As Guid

        If Not RoomOwners.TryGetValue(roomId, owner) OrElse owner = Guid.Empty Then
            Return Nothing
        End If

        Return participants.FirstOrDefault(Function(p) SameBrowser(p.BrowserId, owner))

    End Function


    ' 管理人の今の接続（居ない・切れていれば Nothing）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function OwnerConnectionIdCore(roomId As Integer) As String

        Dim participants As List(Of Participant) = Nothing

        If Not RoomParticipants.TryGetValue(roomId, participants) Then
            Return Nothing
        End If

        Dim owner As Participant = FindOwnerParticipantCore(roomId, participants)

        If owner Is Nothing OrElse Not owner.IsConnected Then
            Return Nothing
        End If

        Return owner.ConnectionId

    End Function


    ' そのブラウザが開いているラウンジの接続（全部。タブが2つなら2つとも）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function LoungeConnectionsOfCore(browser As Guid) As List(Of String)

        Return LoungeConnections.Where(
            Function(entry) entry.Value = browser).Select(
            Function(entry) entry.Key).ToList()

    End Function


    ' Cookie の生の値（参加者が持っている形）と Guid を比べる（表記の揺れを吸収する）
    Private Shared Function SameBrowser(browserId As String, browser As Guid) As Boolean

        Dim parsed As Guid

        Return Guid.TryParse(browserId, parsed) AndAlso parsed = browser

    End Function


    Private Shared Function SecondsUntil(expiresAt As DateTime, now As DateTime) As Integer
        Return Math.Max(0, CInt(Math.Ceiling((expiresAt - now).TotalSeconds)))
    End Function


    Private Shared Function ToKnockRequestInfo(request As PendingKnock, now As DateTime) As KnockRequestInfo

        Return New KnockRequestInfo With {
            .KnockId = request.KnockId,
            .Nickname = request.Knocker.Nickname,
            .IconType = request.Knocker.IconType,
            .IconColor = request.Knocker.IconColor,
            .Trip = request.Knocker.Trip,
            .AccountName = request.Knocker.AccountName,
            .AnonId = AnonId.Compute(request.RoomId, request.Browser),
            .Seconds = SecondsUntil(request.ExpiresAt, now)
        }

    End Function


    Private Shared Function KnockRejected(roomId As Integer, message As String) As KnockResult
        Return New KnockResult With {.Status = KnockStatusRejected, .RoomId = roomId, .Message = message}
    End Function


    Private Shared Function AnswerRejected(keep As Boolean, message As String) As AnswerKnockResult
        Return New AnswerKnockResult With {.Ok = False, .Keep = keep, .Message = message}
    End Function

End Class
