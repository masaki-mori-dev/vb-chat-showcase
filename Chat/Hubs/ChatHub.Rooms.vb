Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Linq
Imports System.Threading

' ============================================================
' 部屋の一生（作る・管理人・空になったら閉じる）
' ============================================================
'
' ChatHub の続き（Partial）。部屋の参加者リスト（RoomParticipants）と、
' それを守る鍵（SyncRoot）をそのまま使うために、ここへ置いている
' （ChatHub.Lounge.vb と同じ理由）。
'
' 【この回でやること・やらないこと】
'   15-1（この回）… 部屋を作る土台／空になったら閉じる／管理人の仕組み（自動の引き継ぎまで）
'   15-2         … 入口の鍵（パスワード・ノック）
'   15-3         … 管理人の道具（指名・強制退室・入室禁止・部屋に居ながらの設定変更・定員の配信）
'
' 【部屋を作る INSERT は、ここには無い】
' あれは WebMethod（Chat/Lounge.aspx.vb の CreateRoom）が行う。
' 「部屋作成処理の方針」で決めてあるハイブリッド方式そのもので、
'   ① WebMethod が ChatRooms へ INSERT（排他制御はDB任せ）
'   ② 成功したら、ここ（RegisterCreatedRoom）へ知らせて、ラウンジへ配る
' という形にしてある。ここに在るのは、そのあとの一生（閉じる・管理人の引き継ぎ）である
'
' 【閉じる仕組みの全体像】
'   ・在室者が0人になった一時部屋を、猶予（SiteSettings.RoomEmptyClose。既定0秒）のあと
'     IsActive = 0 にする（**物理削除はしない**。ChatMessages から部屋名を引けなくなるため）
'   ・きっかけは見回りのタイマー（Global.asax、2秒おき）。TickRoomCleanup は時刻を比べるだけで、
'     DBを触るときは裏の作業（SerialRunner）に頼んですぐ戻る
'     （DailyJobs.Tick と同じ形。DBが詰まっても、在室の見回りを止めない）
'   ・アプリが起き直した直後は、別の猶予（RoomEmptyCloseAfterStartup。既定180秒）を使う。
'     在室者の一覧はメモリにしか無いので、再起動すると全員分が飛ぶ——
'     **繋ぎ直しを待たずに閉じると、人が居る部屋まで消える**
'   ・「作られたばかりで、まだ誰も入っていない部屋」にも、同じ長いほうの猶予を使う
'     （作った本人が部屋の画面を開いて JoinRoom を呼ぶまで、数秒かかる）
Partial Public Class ChatHub

    ' 差分の種類（LoungeUpdate.Kind）。画面（Scripts/lounge-live.js）と同じ綴りにすること
    Private Const LoungeKindRoomCreated As String = "roomCreated"
    Private Const LoungeKindRoomClosed As String = "roomClosed"
    Private Const LoungeKindRoomOwner As String = "roomOwner"   ' 管理人の印の付け替え
    Private Const LoungeKindRoomChanged As String = "roomChanged"   ' 部屋の設定の変更（ChatHub.RoomSettings.vb）


    ' 「一時部屋である」と分かっている部屋
    '
    ' 入るのは、①この回で作られた部屋 ②誰かが入室してDBから RoomType を読めた部屋
    ' ③起き直したあとの見回りでDBから見つけた部屋。
    ' **ここに無い部屋は、空になっても閉じない**（固定部屋がそれ。閉じてはいけない）。
    ' DBへ問い合わせられなかった回は RoomType が分からないので入らない
    ' （その部屋は、次に誰かが入室するまで閉じられない。フェイルクローズ側に倒してある）
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared TemporaryRooms As New HashSet(Of Integer)

    ' 今の管理人（キー：部屋ID、値：その人の ChatBrowserId）
    '
    ' 正はDBの ChatRooms.OwnerBrowserId で、ここはその写しである。
    ' 部屋を作ったとき・DBから読めたとき（入室の1回）・引き継いだときに書く。
    '
    ' 【なぜ写しを持つのか】
    ' 退室のたびに「この人は管理人だったか」を知る必要がある。
    ' そこでDBを引くと、**退室1回につき問い合わせが1回増える**。
    ' 入室のときはどうせ ChatRooms を引いている（TryGetRoomState）ので、そこで一緒に読んでおく
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared RoomOwners As New Dictionary(Of Integer, Guid)

    ' 空になっている一時部屋（キー：部屋ID）
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared EmptyRooms As New Dictionary(Of Integer, EmptyRoomState)

    ' 今まさに閉じている部屋（DBへ UPDATE を投げている間だけ入る）
    '
    ' 【何のために要るのか】
    ' 「空だと決める」のと「DBで閉じる」の間に人が入ると、
    ' **閉じた部屋に人が居る**状態になる（その人はメモリからも消されかねない）。
    ' そこで、閉じると決めた部屋は鍵の中で印を付け、JoinRoom はその部屋への入室を断る。
    ' 印を付けるのも、部屋が空であることを確かめるのも、同じ鍵の中で行うので、
    ' あいだに人が入る隙が無い
    '
    ' 【必ず SyncRoot の中で触ること】
    Private Shared ClosingRooms As New HashSet(Of Integer)

    ' このアプリが起きた時刻（＝在室者の一覧が空になった時刻）
    Private Shared ReadOnly AppStartedAt As DateTime = DateTime.Now

    ' 起き直したあとの「DBから探す」を、もう済ませたか
    ' （DBへ問い合わせられなかったときは False のままにして、次の見回りでやり直す）
    Private Shared StartupSweepDone As Boolean = False

    ' 次に掃除を試みてよい時刻（Ticks。うまくいかなかったときだけ先へ進める）
    '
    ' 【Interlocked で読み書きする】書くのは裏の作業、読むのはタイマーのスレッドである
    Private Shared _sweepNotBeforeTicks As Long = 0

    ' うまくいかなかったとき（DBに繋がらない、など）に、次に試みるまでの間
    Private Shared ReadOnly SweepRetryInterval As TimeSpan = TimeSpan.FromSeconds(60)

    ' 掃除は「1本だけ、順番に」（SiteMail・ErrorReport・DailyJobs と同じ道具）
    '
    ' 【この行は、上の SweepRetryInterval より後ろに置くこと】Shared の初期化は書いた順に走る
    Private Shared ReadOnly RoomSweeper As New SerialRunner(AddressOf SweepRooms, "RoomCleanup")

    Private Const RoomConnectionName As String = "VbChatConnection"


    ' 空になっている部屋の、状態
    Private NotInheritable Class EmptyRoomState

        ' 空になった時刻（アプリの時計）。
        ' まだ誰も入っていない部屋では「作られた（見つけた）時刻」が入る
        Public Since As DateTime

        ' まだ一度も人が入っていないか（True なら、長いほうの猶予を使う）
        Public NeverOccupied As Boolean

    End Class


    ' ============================================================
    '  部屋を作ったことを受け取る（Chat/Lounge.aspx.vb の CreateRoom から）
    ' ============================================================
    '
    ' DBへの INSERT が済んだあとに呼ぶ。ここでやるのは2つ。
    '   ・メモリへの登録（一時部屋である・管理人は誰・まだ誰も入っていない）
    '   ・ラウンジへ「部屋ができた」を配る
    '
    ' 【まだ誰も入っていない部屋にも、猶予を持たせる】
    ' 作った本人は、このあと部屋の画面へ移って JoinRoom を呼ぶ（数秒かかる）。
    ' 空になった部屋の猶予（既定0秒）をここに使うと、**作った瞬間に部屋が消える**。
    ' そこで長いほうの猶予（RoomEmptyCloseAfterStartup。既定180秒）を使う。
    ' 本人の画面が開かなかったとき（タブを閉じた、JSが動かなかった）は、
    ' その猶予のあとに自動で閉じる＝作りかけの部屋が残らない
    '
    ' 【hasPassword】パスワード付きの部屋なら、
    '   ・鍵付きであることを控える（JoinRoom が、DBが読めない回でも鍵付きとして扱えるように）
    '   ・作った本人に「鍵を通った印」を置く（本人は小窓を通らずに、そのまま部屋へ入るため）
    '   ・ラウンジの一覧に鍵の印を出す（LoungeRoomInfo.HasPassword）
    '
    ' 【knockEnabled】ノックを受け付ける部屋なら、ラウンジの一覧が
    ' 満室のときに入室ボタンを「ノック」に差し替える（LoungeRoomInfo.KnockEnabled）。
    ' ノックの受け付けそのものは、ノックのたびにDBの IsKnockEnabled を読んで決める（ChatHub.Knock.vb）
    Public Shared Sub RegisterCreatedRoom(roomId As Integer,
                                          roomName As String,
                                          maxUsers As Integer,
                                          ownerBrowserId As Guid,
                                          hasPassword As Boolean,
                                          knockEnabled As Boolean)

        Dim update As LoungeUpdate = Nothing

        SyncLock SyncRoot

            TemporaryRooms.Add(roomId)
            RoomOwners(roomId) = ownerBrowserId

            EmptyRooms(roomId) = New EmptyRoomState With {
                .Since = DateTime.Now,
                .NeverOccupied = True
            }

            NoteRoomLockCore(roomId, hasPassword)

            If hasPassword Then
                RoomPasses(RoomPassKey(roomId, ownerBrowserId)) = DateTime.Now + RoomPassLifetime
            End If

            update = NextLoungeUpdate(LoungeKindRoomCreated)

            If update IsNot Nothing Then
                update.RoomId = roomId
                update.Room = New LoungeRoomInfo With {
                    .RoomId = roomId,
                    .RoomName = roomName,
                    .MaxUsers = maxUsers,
                    .HasPassword = hasPassword,
                    .KnockEnabled = knockEnabled
                }
            End If

        End SyncLock

        PublishLoungeUpdate(update)

    End Sub


    ' ============================================================
    '  入室・退室のときに呼ぶもの（どれも SyncRoot の中から）
    ' ============================================================

    ' DBから読めた部屋の種別と管理人を、メモリへ控える（JoinRoom から）
    '
    ' roomType       … ChatRooms.RoomType（読めなかったときは Nothing）
    ' ownerBrowserId … ChatRooms.OwnerBrowserId（無い・読めなかったときは Guid.Empty）
    '
    ' 【管理人は、まだ知らない部屋のときだけDBの値を採る】
    ' 引き継ぎ（下の AfterParticipantRemovedCore）はメモリとDBの両方へ書くが、
    ' DBへの書き込みは鍵の外なので、ほんの一瞬だけDBが古い。
    ' その一瞬に入室が重なると、古い管理人で上書きしてしまう。
    ' 「知らない部屋だけ採る」ことにすれば、メモリが正であり続ける
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub NoteRoomStateCore(roomId As Integer,
                                         roomType As String,
                                         ownerBrowserId As Guid)

        If String.Equals(roomType, ChatRoomRules.RoomTypeTemporary, StringComparison.OrdinalIgnoreCase) Then
            TemporaryRooms.Add(roomId)
        End If

        If ownerBrowserId <> Guid.Empty AndAlso Not RoomOwners.ContainsKey(roomId) Then
            RoomOwners(roomId) = ownerBrowserId
        End If

    End Sub


    ' 部屋に人が入った（JoinRoom が参加者リストへ足したあと）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub NoteRoomOccupiedCore(roomId As Integer)
        EmptyRooms.Remove(roomId)
    End Sub


    ' その部屋が今まさに閉じられようとしているか（JoinRoom の入室の可否）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsRoomClosingCore(roomId As Integer) As Boolean
        Return ClosingRooms.Contains(roomId)
    End Function


    ' 参加者を1人、部屋のリストから外したあとの後始末
    '
    ' 戻り値 … DBへ書くべき新しい管理人（引き継ぎが起きなければ Nothing）。
    '           **鍵を抜けてから WriteOwnerHandoff へ渡すこと**
    '
    ' やることは2つ。
    '   ・空になった一時部屋に、印を付ける（あとで見回りが閉じる）
    '   ・出ていったのが管理人なら、残っている人のうち**いちばん古くから居る人**へ引き継ぐ
    '
    ' 【「いちばん古くから居る人」は JoinedAt で決める。LocalId では決めない】
    ' LocalId は入室のたびに振り直されるので、ずっと居た人がF5しただけで新参あつかいになる
    ' （Participant.JoinedAt のコメント）。
    '
    ' 【空になったときは、引き継がない】引き継ぐ相手が居ないうえ、その部屋はこのあと閉じる
    '
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function AfterParticipantRemovedCore(roomId As Integer,
                                                        leaver As Participant) As Guid?

        Dim participants As List(Of Participant) = Nothing

        ' 出ていったのが、部屋の設定を変えていた管理人なら、まとめている途中のログの1行を先に出す。
        ' 「管理人が〇〇さんに替わりました」の行より前に並ぶように、ここ（引き継ぎより前）で積む
        FlushRoomSettingsLogForCore(roomId, leaver)

        If Not RoomParticipants.TryGetValue(roomId, participants) Then
            participants = New List(Of Participant)
        End If

        If participants.Count = 0 Then

            ' 空になった＝管理人も居ない。返事待ちのノックは無かったことにする（取り置いた席は残す。
            ' 部屋が本当に閉じたら、ForgetRoomCore が席も捨てる）
            CancelRoomKnocksCore(roomId, closeSeats:=False)

            ' 空になった。一時部屋なら、猶予のあとに閉じる
            ' （固定部屋は TemporaryRooms に入っていないので、ここを通らない）
            If TemporaryRooms.Contains(roomId) AndAlso Not EmptyRooms.ContainsKey(roomId) Then

                EmptyRooms(roomId) = New EmptyRoomState With {
                    .Since = DateTime.Now,
                    .NeverOccupied = False
                }

            End If

            Return Nothing

        End If

        ' まだ人が居る。出ていったのが管理人だったかを見る
        Dim owner As Guid

        If Not RoomOwners.TryGetValue(roomId, owner) OrElse owner = Guid.Empty Then
            Return Nothing
        End If

        Dim leaverBrowser As Guid

        If Not Guid.TryParse(leaver.BrowserId, leaverBrowser) OrElse leaverBrowser <> owner Then
            Return Nothing
        End If

        ' 管理人が出ていった。その管理人に届いていたノックは、無かったことにする。
        ' **取り置いた席は消さない**（許可はもう済んでいる）
        CancelRoomKnocksCore(roomId, closeSeats:=False)

        ' いちばん古くから居る人へ引き継ぐ（ChatBrowserId が読めない人は飛ばす）
        For Each p In participants.OrderBy(Function(x) x.JoinedAt)

            Dim candidate As Guid

            If Guid.TryParse(p.BrowserId, candidate) AndAlso candidate <> Guid.Empty Then

                RoomOwners(roomId) = candidate

                ' 部屋の全員へ「管理人が〇〇さんに替わりました」。
                ' 鍵の中なので積むだけ。呼び出し元が、退室の配信のあとに配る（FlushRoomNotices）
                QueueOwnerChangedCore(roomId, p, announce:=True)

                Return candidate

            End If

        Next

        Return Nothing

    End Function


    ' 管理人が替わったことを、部屋の全員へ知らせる準備をする
    '
    ' 決定：「管理人が〇〇さんに替わりました」は全員に出す。
    ' 入退室のお知らせ行と同じ見た目で、部屋のログに1行出す（ChatEventText.Build の OwnerChanged）。
    ' **記録もする**（種別60。過去ログにも同じ行が出る＝あとから入った人にも、誰が管理人かが分かる）。
    ' 記録する人は、新しい管理人（その時点の名前を焼き込む）。
    '
    ' 配信には、新しい管理人の LocalId も載せる（画面が管理人の印を付け替えるのに使う。
    ' ブラウザの識別子は渡さない。LocalId は部屋の中だけの番号）
    '
    ' 【必ず SyncRoot の中で呼ぶこと】積むだけ。配るのは FlushRoomNotices
    '
    ' announce … True なら行と記録を出す（本当に交代したとき）。
    '            False なら、管理人の印を付け替えるためだけに番号を配る（行も記録も出さない）。
    '            管理人本人が新しく部屋の番号をもらったとき（部屋を作って入った・同じ部屋を別のタブで開き直した・
    '            アプリが起き直したあとに入ってきた）に使う（⑤。JoinRoom から）
    '
    ' ラウンジにも、管理人の印を付け替える差分（roomOwner）を配る（⑤）
    '
    ' designated … 管理人が参加者一覧から相手を選んでゆずった（指名。ChatHub.OwnerTools.vb）。
    '              True なら記録は 62（OwnerHandedOver）、画面は「ポン」を鳴らす（ownerChanged の3つ目の引数）。
    '              自動の引き継ぎ（False）は今のまま鳴らさない＝直前に退室のドアの音が鳴っているため
    Private Shared Sub QueueOwnerChangedCore(roomId As Integer, newOwner As Participant, announce As Boolean,
                                             Optional designated As Boolean = False)

        Dim notice As New RoomNotice With {
            .OwnerChangedRoomId = roomId,
            .OwnerLocalId = newOwner.LocalId
        }

        If announce Then
            notice.OwnerEventText = ChatEventText.Build(ChatMessageType.OwnerChanged, newOwner.Nickname)
            notice.OwnerDesignated = designated
            notice.LogType = If(designated, ChatMessageType.OwnerHandedOver, ChatMessageType.OwnerChanged)
            notice.LogRoomId = roomId
            notice.LogParticipant = newOwner
        End If

        notice.LoungeUpdate = NextLoungeUpdate(LoungeKindRoomOwner)

        If notice.LoungeUpdate IsNot Nothing Then
            notice.LoungeUpdate.RoomId = roomId
            notice.LoungeUpdate.LocalId = newOwner.LocalId
        End If

        RoomNoticeOutbox.Add(notice)

    End Sub


    ' 引き継いだ管理人を、DBへ書く
    '
    ' 【必ず SyncRoot を抜けてから呼ぶこと】DBを鍵の中に持ち込まない
    '
    ' 【失敗しても、退室は止めない】
    ' メモリ側（RoomOwners）は書き換わっているので、このアプリが動いている間は正しく効く。
    ' 書けないまま再起動すると、DBに残った古い管理人（＝もう部屋に居ない人）が復活するが、
    ' その人が入ってこない限り誰も管理人の道具を使えないだけで、部屋は使える（効いてくる）
    Friend Shared Sub WriteOwnerHandoff(roomId As Integer, newOwner As Guid)

        Try

            Using conn As New SqlConnection(RoomConnectionString())
                Using cmd As New SqlCommand(
                    "UPDATE ChatRooms SET OwnerBrowserId = @Owner " &
                    " WHERE RoomId = @RoomId AND RoomType = @RoomType AND IsActive = 1", conn)

                    cmd.Parameters.Add("@Owner", SqlDbType.UniqueIdentifier).Value = newOwner
                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@RoomType", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary

                    conn.Open()
                    cmd.ExecuteNonQuery()

                End Using
            End Using

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.WriteOwnerHandoff", "部屋 " & roomId)
        End Try

    End Sub


    ' ============================================================
    '  空になった部屋を閉じる
    ' ============================================================

    ' 見回りのタイマー（Global.asax の OnTimeoutCheckTick。2秒おき）から呼ぶ
    '
    ' 時刻を比べるだけで、DBには触らない。閉じるものがあれば、裏の作業に頼んですぐ戻る
    ' （DailyJobs.Tick と同じ形）
    '
    ' 【設定は、鍵を取る前に読む】SiteSettings はキャッシュが空のときDBへ行く
    ' （鍵の中から読むと、その間じゅう全部屋のチャットが止まる）
    Public Shared Sub TickRoomCleanup()

        Dim now As DateTime = DateTime.Now

        If now.Ticks < Interlocked.Read(_sweepNotBeforeTicks) Then
            Return
        End If

        Dim emptyGrace As TimeSpan = SiteSettings.RoomEmptyClose
        Dim startupGrace As TimeSpan = SiteSettings.RoomEmptyCloseAfterStartup

        Dim hasWork As Boolean = False

        SyncLock SyncRoot

            If Not StartupSweepDone AndAlso now - AppStartedAt >= startupGrace Then
                hasWork = True
            End If

            If Not hasWork Then

                For Each entry In EmptyRooms

                    If now >= CloseDueAt(entry.Value, emptyGrace, startupGrace) Then
                        hasWork = True
                        Exit For
                    End If

                Next

            End If

        End SyncLock

        If hasWork Then
            RoomSweeper.Kick()
        End If

    End Sub


    ' その部屋を閉じてよくなる時刻
    Private Shared Function CloseDueAt(state As EmptyRoomState,
                                       emptyGrace As TimeSpan,
                                       startupGrace As TimeSpan) As DateTime

        Return state.Since + If(state.NeverOccupied, startupGrace, emptyGrace)

    End Function


    ' 裏の作業（1本だけ・順番に）
    '
    ' 【例外を外へ出さないこと】SerialRunner は Trace に書くだけなので、
    ' 知らせるのはここの役目である（DailyJobs と同じ）
    Private Shared Sub SweepRooms()

        Try

            Dim startupGrace As TimeSpan = SiteSettings.RoomEmptyCloseAfterStartup
            Dim emptyGrace As TimeSpan = SiteSettings.RoomEmptyClose

            ' ① 起き直したあとの1回だけ、DBから「まだ残っている一時部屋」を探す
            If Not StartupSweepDone AndAlso DateTime.Now - AppStartedAt >= startupGrace Then

                AdoptRoomsFromDatabase()

                SyncLock SyncRoot
                    StartupSweepDone = True
                End SyncLock

            End If

            ' ② 猶予を過ぎた部屋を、1つずつ閉じる
            Do While CloseOneDueRoom(emptyGrace, startupGrace)
            Loop

        Catch ex As Exception

            ' DBに繋がらない、など。少し待ってからやり直す
            ' （2秒おきの見回りに引きずられて、落ちているDBを叩き続けないため）
            Interlocked.Exchange(_sweepNotBeforeTicks, DateTime.Now.Add(SweepRetryInterval).Ticks)

            ErrorReport.Report(ex, "ChatHub.SweepRooms")

        End Try

    End Sub


    ' 猶予を過ぎた部屋を1つ閉じる（閉じるものが無ければ False）
    Private Shared Function CloseOneDueRoom(emptyGrace As TimeSpan,
                                            startupGrace As TimeSpan) As Boolean

        Dim roomId As Integer = -1

        SyncLock SyncRoot

            Dim now As DateTime = DateTime.Now

            For Each entry In EmptyRooms

                If ClosingRooms.Contains(entry.Key) Then
                    Continue For
                End If

                If now < CloseDueAt(entry.Value, emptyGrace, startupGrace) Then
                    Continue For
                End If

                ' 念のため、本当に空かを確かめる（人が増えるのは、この鍵の中だけ）
                Dim participants As List(Of Participant) = Nothing

                If RoomParticipants.TryGetValue(entry.Key, participants) AndAlso
                   participants.Count > 0 Then
                    Continue For
                End If

                roomId = entry.Key

                ' 【印を、この鍵の中で付ける】
                ' これが付いている間、JoinRoom はこの部屋への入室を断る。
                ' 「空だと決める」と「DBで閉じる」の間に人が入る隙を、これで無くしている
                ClosingRooms.Add(roomId)

                Exit For

            Next

        End SyncLock

        If roomId < 0 Then
            Return False
        End If

        Dim closed As Boolean = False
        Dim finished As Boolean = False
        Dim update As LoungeUpdate = Nothing

        Try

            closed = CloseRoomInDatabase(roomId)
            finished = True

        Finally

            SyncLock SyncRoot

                ClosingRooms.Remove(roomId)

                ' 【DBで閉じられなかったとき（0行）も、メモリからは外す】
                ' 0行だったのは「もう閉じている」か「一時部屋ではない」かのどちらかで、
                ' どちらも、この仕組みが面倒を見る部屋ではない。
                ' 外さないと、同じ部屋を毎回の見回りで閉じようとし続ける
                '
                ' 【DBに届かなかった（例外）ときは、外さない】
                ' 本番で「ログイン前のハンドシェイク」のタイムアウト（SqlException -2）が起きたとき、
                ' 作りではここで控えまで消していたので、**その部屋は二度と閉じ直されず、
                ' 誰も居ないのに一覧に残り続けた**（誰かが入って出るか、アプリが再起動するまで）。
                ' 控えを残せば、SweepRooms の「60秒待ってやり直す」のあとに、また閉じにいく。
                ' 閉じかけの印（ClosingRooms）は外すので、そのあいだに人が入ることはできる
                ' （入れば NoteRoomOccupiedCore が控えを消すので、閉じられない）
                If finished Then
                    EmptyRooms.Remove(roomId)
                End If

                If closed Then

                    ForgetRoomCore(roomId)

                    update = NextLoungeUpdate(LoungeKindRoomClosed)

                    If update IsNot Nothing Then
                        update.RoomId = roomId
                    End If

                End If

            End SyncLock

        End Try

        ' 閉じられなかったときは、ラウンジへ何も配らない
        ' （固定部屋が万一ここへ来ても、一覧から消えない向きにしておく）
        PublishLoungeUpdate(update)

        ' 閉じた部屋のノックの「取り消されました」（ForgetRoomCore が積んだもの）
        FlushRoomNotices()

        Return True

    End Function


    ' 閉じた部屋のことを、メモリから忘れる
    '
    ' 【参加者リストと LocalId の発番も捨てる】
    ' 一時部屋は増え続けるので、捨てないとアプリが動いている間じゅう溜まる。
    ' 捨ててよいのは、この部屋がもう使われないから（IsActive = 0 なので入室も断られる）
    '
    ' 【必ず SyncRoot の中で呼ぶこと。空であることを確かめてから呼ぶこと】
    Private Shared Sub ForgetRoomCore(roomId As Integer)

        Dim participants As List(Of Participant) = Nothing

        If RoomParticipants.TryGetValue(roomId, participants) AndAlso participants.Count = 0 Then
            RoomParticipants.Remove(roomId)
            NextLocalId.Remove(roomId)
        End If

        TemporaryRooms.Remove(roomId)
        RoomOwners.Remove(roomId)

        ' 鍵にまつわるもの（鍵付きの控え・鍵を通った印）も忘れる（ChatHub.RoomKeys.vb）
        ForgetRoomPassesCore(roomId)

        ' 入室禁止の控えも忘れる（ChatHub.OwnerTools.vb。DB の行は CloseRoomInDatabase が消す）
        ForgetRoomBansCore(roomId)

        ' ノックと取り置いた席も捨てる（ChatHub.Knock.vb。ノックした人には「取り消されました」）。
        ' 知らせは RoomNoticeOutbox に積まれる。この関数を呼ぶ CloseOneDueRoom が、鍵を抜けてから配る
        CancelRoomKnocksCore(roomId, closeSeats:=True)

        ' 部屋の設定のまとめ（ログの1行）と、部屋全体の上限の数え（ChatHub.RoomSettings.vb）
        ForgetRoomSettingsCore(roomId)

    End Sub


    ' DBで部屋を閉じる（IsActive = 0）。1行でも変えられたら True
    '
    ' 【物理削除はしない】ChatMessages に残った RoomId から部屋名を引けなくなる
    ' （「一時部屋は物理削除ではなく論理削除にする」）
    '
    ' 【RoomType も条件に入れてある】万一この仕組みが固定部屋を閉じようとしても、
    ' DBが受け付けない（最後の砦）
    '
    ' 【入室禁止の行も、同じ1本で消す】（ChatRoomBans）。
    ' 禁止は「部屋が閉じたら消える」。閉じた部屋には誰も入れないので残しても害は無いが、溜まる。
    ' 閉じられた（UPDATE が1行変えた）ときだけ消す。戻り値は UPDATE の行数で決める（DELETE の行数を混ぜない）
    Private Shared Function CloseRoomInDatabase(roomId As Integer) As Boolean

        Using conn As New SqlConnection(RoomConnectionString())
            Using cmd As New SqlCommand(
                "UPDATE ChatRooms SET IsActive = 0 " &
                " WHERE RoomId = @RoomId AND RoomType = @RoomType AND IsActive = 1; " &
                "DECLARE @Closed INT = @@ROWCOUNT; " &
                "IF @Closed > 0 DELETE FROM ChatRoomBans WHERE RoomId = @RoomId; " &
                "SELECT @Closed;", conn)

                cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                cmd.Parameters.Add("@RoomType", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary

                conn.Open()

                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0

            End Using
        End Using

    End Function


    ' 起き直したあと、DBに残っている一時部屋を引き受ける（アプリが起きて1回だけ）
    '
    ' 【なぜ要るのか】
    ' 在室者の一覧はメモリにしか無いので、アプリが再起動すると全員分が飛ぶ。
    ' 部屋の行（IsActive = 1）はDBに残るため、**誰も居ない部屋が一覧に残り続ける**。
    ' そこで起き直しの猶予（既定180秒。繋ぎ直しを待つ時間）のあとに、1回だけ探す
    '
    ' 【人が居る部屋・もう知っている部屋は触らない】
    ' 猶予のあいだに誰かが繋ぎ直した部屋は、参加者が居るので飛ばす。
    ' この回で作られた部屋は、もう EmptyRooms に入っているので飛ばす
    '
    ' 【古さはDBの時計で計る】
    ' ChatRooms.CreatedAt はDBの時計で入っている（アプリの時計とはずれうる）。
    ' そこで「DBの今から見て何秒前に作られたか」を持ってきて、アプリの時計に直す
    ' （DailyJobs が、日付をDBの時計で決めているのと同じ用心）
    Private Shared Sub AdoptRoomsFromDatabase()

        Dim found As New List(Of KeyValuePair(Of Integer, Integer))

        Using conn As New SqlConnection(RoomConnectionString())
            Using cmd As New SqlCommand(
                "SELECT RoomId, DATEDIFF(SECOND, CreatedAt, SYSDATETIME()) AS AgeSeconds " &
                "  FROM ChatRooms WHERE RoomType = @RoomType AND IsActive = 1", conn)

                cmd.Parameters.Add("@RoomType", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        Dim age As Integer = If(reader.IsDBNull(1), 0, reader.GetInt32(1))

                        If age < 0 Then
                            age = 0
                        End If

                        found.Add(New KeyValuePair(Of Integer, Integer)(reader.GetInt32(0), age))

                    End While
                End Using

            End Using
        End Using

        Dim now As DateTime = DateTime.Now

        SyncLock SyncRoot

            For Each room In found

                ' 一時部屋であることは、この問い合わせで分かっている
                TemporaryRooms.Add(room.Key)

                If EmptyRooms.ContainsKey(room.Key) Then
                    Continue For
                End If

                Dim participants As List(Of Participant) = Nothing

                If RoomParticipants.TryGetValue(room.Key, participants) AndAlso
                   participants.Count > 0 Then
                    Continue For
                End If

                ' まだ誰も入っていない（＝起き直しの猶予を使う）扱いにする。
                ' 作られてから起き直しの猶予を過ぎている部屋は、このあとすぐ閉じられる
                EmptyRooms(room.Key) = New EmptyRoomState With {
                    .Since = now.AddSeconds(-room.Value),
                    .NeverOccupied = True
                }

            Next

        End SyncLock

    End Sub


    Private Shared Function RoomConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(RoomConnectionName).ConnectionString
    End Function

End Class
