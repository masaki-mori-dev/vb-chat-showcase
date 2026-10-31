Imports System.Data
Imports System.Data.SqlClient
Imports System.Linq
Imports Microsoft.AspNet.SignalR

' ============================================================
' 管理人の道具（ChatHub の Partial）
' ============================================================
'
' 開発者と決めた3つ。入口はどれも、部屋の参加者一覧で相手を選ぶ操作
' （無視の「非表示にする」のボタンの並び。画面は Scripts/chat-room.js の OwnerTools）。
'   ① 指名して管理人をゆずる（HandOverOwner）
'   ② 退室させる（KickParticipant）…… その時限り。すぐ入り直せる。記録は 34
'   ③ 入室禁止にする（BanParticipant）…… 退室させたうえで、この部屋にはもう入れない。記録は 38
'      解除は「部屋の設定」の「入室禁止にした人」の一覧から（GetRoomBans・LiftRoomBan。記録は 63）
'
' 【画面に出さないことは防御ではない】ボタンは管理人の画面にだけ出すが、ここで必ず確かめる：
'   ・呼んだ人が、その部屋の今の管理人か（RoomOwners。部屋も呼んだ接続から引く＝クライアントに名乗らせない）
'   ・相手が同じ部屋に居るか（匿名IDは、その部屋の在室者の中からだけ探す＝ResolveRecipient）
'   ・相手が自分ではないか
'
' 【相手は匿名ID（AnonId）で指す】参加者一覧・無視・こっそりと同じ値。ChatBrowserId は画面へ渡さない。
'
' 【入室禁止の正は DB（ChatRoomBans）】一時部屋はアプリの再起動をまたいで残るので、メモリだけだと禁止が黙って消える。
' 入室のたびの確かめは、JoinRoom がもともと引いている部屋の状態の SELECT に EXISTS を1つ足しただけ（問い合わせは増えない）。
' メモリの控え（KnownBans）は、この起動で見かけた禁止を覚えておくもの。
' 【DB が読めなかった回は、一時部屋へは新しく入れない】（塞いだ）控えは、再起動の前に禁止された人を知らないため。
' 入室禁止は守りの話なので、確かめられないなら通さない（ChatHub.RoomKeys.vb の IsRoomGuardUnknownCore）。定員は今までどおり通す
'
' 【目印は ChatBrowserId】名前を変えただけでは、同じ人のまま。
Partial Public Class ChatHub

    ' 入室禁止の部屋へ入ろうとした人への文言（JoinRoom・ラウンジの入室ボタン・ノックで同じ）
    Public Const BannedJoinMessage As String = "この部屋には入れません（管理人により入室禁止になっています）。"

    ' 退室させられた本人へ（Kicked → ラウンジの帯）。開発者の決めた文言
    Private Const KickedByOwnerMessage As String = "管理人により退室させられました"
    Private Const BannedByOwnerMessage As String = "この部屋には入れなくなりました"

    ' 1部屋で入室禁止にしておける人数の上限
    '
    ' 【なぜ要るのか】Cookie を作り直せる相手は何度でも来られるので、管理人はそのたびに禁止にすることになる。
    ' 上限が無いと、表と「部屋の設定」の一覧が際限なく伸びる。設定にしていない（ふつうの操作では届かない保険の線。
    ' 部屋の設定の「1分に20項目」と同じ扱い）。届いたら、古い禁止を解除すれば足せる
    Private Const MaxBansPerRoom As Integer = 200

    ' 入室禁止の控え（部屋ID → 禁止になっているブラウザ）。DB が正で、ここは DB が読めなかった回の受け皿。
    ' 禁止にした・DB で禁止と分かったときに足し、解除したときに消す。部屋を忘れるときに捨てる（ForgetRoomBansCore）。
    ' 【SyncRoot の中でしか触らないこと】
    Private Shared KnownBans As New Dictionary(Of Integer, HashSet(Of Guid))


    ' ============================================================
    '  ① 指名して管理人をゆずる
    ' ============================================================
    '
    ' 【ゆずったら、自分では取り戻せない】だから画面は押す前に確認を1回出す（取り戻すには、新しい管理人にゆずってもらう）。
    ' 【今つながっている人にだけゆずれる】通信が切れている人（スマホの画面を消している、など）にゆずると、
    ' その人が戻るまで誰も管理人の道具を使えない（ノックの返事もできない）。
    ' 【部屋の全員に「管理人が〇〇さんに替わりました」】自動の引き継ぎ（60）と同じ行。記録は 62（指名でゆずった）。
    ' 【指名のときだけ「ポン」を鳴らす】決定：指名のときは音を出す。自動の引き継ぎは今のまま鳴らさない
    ' （直前に退室のドアの音が鳴るので、音が重なる）。鳴らすかは画面が ownerChanged の3つ目の引数で決める。
    ' 【返事待ちのノックは、新しい管理人へ移す】自動の引き継ぎ（前の管理人が居なくなった）と違い、ノックした人は
    ' 待たせたままでよい（許可できる人が居るため）。新しい管理人の画面に、残りの秒数つきで小窓が出る。
    ' 前の管理人の画面の小窓は、画面が「自分はもう管理人ではない」と分かった時点で消す
    Public Function HandOverOwner(targetAnonId As String) As OwnerActionResult

        Dim roomId As Integer = 0
        Dim newOwner As Guid = Guid.Empty
        Dim newOwnerConnectionId As String = Nothing
        Dim movedKnocks As New List(Of KnockRequestInfo)

        SyncLock SyncRoot

            Dim owner As Participant = FindOwnerByConnectionCore(roomId)
            Dim rejected As String = CheckOwnerCore(roomId, owner)

            If rejected IsNot Nothing Then
                Return OwnerActionRejected(rejected)
            End If

            Dim target As Participant = ResolveRecipient(roomId, targetAnonId)

            If target Is Nothing Then
                Return OwnerActionRejected("その人は、もう部屋に居ません。")
            End If

            If SameBrowserText(target.BrowserId, owner.BrowserId) Then
                Return OwnerActionRejected("自分にはゆずれません。")
            End If

            If Not target.IsConnected Then
                Return OwnerActionRejected("その人は今、接続が切れています。戻ってきてから、もう一度お試しください。")
            End If

            If Not Guid.TryParse(target.BrowserId, newOwner) OrElse newOwner = Guid.Empty Then
                Return OwnerActionRejected("その人には、ゆずれません。")
            End If

            ' まとめている途中の「部屋の設定を変えました」の1行を、先に出す（行が「管理人が替わりました」より前に並ぶように）
            FlushRoomSettingsLogForCore(roomId, owner)

            RoomOwners(roomId) = newOwner

            QueueOwnerChangedCore(roomId, target, announce:=True, designated:=True)

            ' 返事待ちのノックを、新しい管理人へ移す（配るのは鍵の外）
            Dim now As DateTime = DateTime.Now

            For Each request In KnockRequests.Values.Where(Function(r) r.RoomId = roomId AndAlso r.ExpiresAt > now)
                movedKnocks.Add(ToKnockRequestInfo(request, now))
            Next

            newOwnerConnectionId = target.ConnectionId

        End SyncLock

        ' 「管理人が〇〇さんに替わりました」（部屋の全員）・ラウンジの札の付け替え・記録 62
        FlushRoomNotices()

        If newOwnerConnectionId IsNot Nothing AndAlso movedKnocks.Count > 0 Then
            Try
                For Each info In movedKnocks
                    Clients.Client(newOwnerConnectionId).KnockRequested(info)
                Next
            Catch ex As Exception
                ' 届かなくても、ゆずったことは成立している（新しい管理人が F5 すれば JoinRoomResult.PendingKnocks で出直す）
                ErrorReport.Report(ex, "ChatHub.HandOverOwner（ノックを移す）", "部屋 " & roomId)
            End Try
        End If

        ' DB へ（鍵の外で。失敗しても、メモリ側は書き換わっているので部屋の中の動きには影響しない）
        WriteOwnerHandoff(roomId, newOwner)

        Return New OwnerActionResult With {.Ok = True}

    End Function


    ' ============================================================
    '  ② 退室させる ／ ③ 入室禁止にする
    ' ============================================================

    ' 退室させる（その時限り。すぐ入り直せる）
    Public Function KickParticipant(targetAnonId As String) As OwnerActionResult
        Return RemoveByOwner(targetAnonId, ban:=False)
    End Function

    ' 入室禁止にする（退室させたうえで、この部屋にはもう入れない）
    Public Function BanParticipant(targetAnonId As String) As OwnerActionResult
        Return RemoveByOwner(targetAnonId, ban:=True)
    End Function


    ' 退室させる・入室禁止にするの本体
    '
    ' 【退室そのものは、ふつうの退室と同じ出口（RemoveParticipantAndNotify）を通す】
    ' 参加者リストから外す・SignalR のグループから外す・部屋の全員へ「〇〇さんが管理人により…」の行とドアの音・
    ' ラウンジへの差分・空になった印と管理人の引き継ぎ（AfterParticipantRemovedCore）・部屋の知らせ（FlushRoomNotices）・記録（34／38）。
    ' 足し忘れると静かに壊れる場所が、全部そこに揃っている（「参加者を部屋のリストから外す処理を足したら」）。
    '
    ' 【本人には Kicked で理由を届ける】接続が生きていれば、その場でラウンジへ移り、帯が出る。
    ' **通信が切れている（凍結している）相手には Kicked を送れない**（Clients.Client が空の接続IDで例外を投げる）。
    ' その人が戻ってきたときは、再接続の JoinRoom が「席が無い」と断り、記録（34／38）から理由を組み立てて帯に出す
    ' （BuildSeatLostReason。無言タイムアウトで落とされたときと同じ仕組み）。席はここで確実に外れている。
    '
    ' 【入室禁止は、DB に書けてから退室させる】書けなかったのに追い出すと、禁止のつもりが「退室させた」だけになり、
    ' 管理人は気づけない。書けなければ、何もせずに理由を返す
    Private Function RemoveByOwner(targetAnonId As String, ban As Boolean) As OwnerActionResult

        Dim roomId As Integer = 0
        Dim target As Participant = Nothing
        Dim ownerBrowser As Guid = Guid.Empty
        Dim targetBrowser As Guid = Guid.Empty

        SyncLock SyncRoot

            Dim owner As Participant = FindOwnerByConnectionCore(roomId)
            Dim rejected As String = CheckOwnerCore(roomId, owner)

            If rejected IsNot Nothing Then
                Return OwnerActionRejected(rejected)
            End If

            target = ResolveRecipient(roomId, targetAnonId)

            If target Is Nothing Then
                Return OwnerActionRejected("その人は、もう部屋に居ません。")
            End If

            If SameBrowserText(target.BrowserId, owner.BrowserId) Then
                Return OwnerActionRejected(If(ban, "自分は入室禁止にできません。", "自分は退室させられません。"))
            End If

            Guid.TryParse(owner.BrowserId, ownerBrowser)

            If ban AndAlso (Not Guid.TryParse(target.BrowserId, targetBrowser) OrElse targetBrowser = Guid.Empty) Then
                Return OwnerActionRejected("その人は、入室禁止にできません。")
            End If

        End SyncLock

        If ban Then

            Select Case TryInsertRoomBan(roomId, targetBrowser, target, ownerBrowser)

                Case RoomBanInsertResult.Full
                    Return OwnerActionRejected(
                        "入室禁止にできるのは、1つの部屋で " & MaxBansPerRoom & " 人までです。" &
                        "「部屋の設定」の一覧から、古い禁止を解除してください。")

                Case RoomBanInsertResult.Failed
                    Return OwnerActionRejected("今は入室禁止にできませんでした。少し待ってから、もう一度お試しください。")

            End Select

            ' 控えにも足す。DB へ書いている間に、その人が退室して入り直していることがある（別の参加者の形になる）ので、
            ' 外す相手はブラウザで探し直す（入り直していたら、そちらを外す。居なければ誰も外さない＝禁止だけが効く）
            SyncLock SyncRoot

                NoteRoomBanCore(roomId, targetBrowser)

                Dim list As List(Of Participant) = Nothing

                If RoomParticipants.TryGetValue(roomId, list) Then
                    target = list.FirstOrDefault(Function(p) SameBrowser(p.BrowserId, targetBrowser))
                Else
                    target = Nothing
                End If

            End SyncLock

            If target Is Nothing Then
                Return New OwnerActionResult With {.Ok = True}
            End If

        End If

        ' 相手が直前に自分で退室していたら、何も起きない（RemoveParticipantAndNotify は外せたときだけ配る）
        RemoveParticipantAndNotify(roomId, target, If(ban, ChatMessageType.BannedByOwner, ChatMessageType.KickedByOwner))

        Return New OwnerActionResult With {.Ok = True}

    End Function


    ' ============================================================
    '  入室禁止にした人の一覧・解除（部屋の設定の欄から）
    ' ============================================================
    '
    ' 【一覧は、押されたとき（設定パネルを開いたとき・解除したとき）にだけ返す】配信しない。
    ' 管理人しか変えられないので、管理人の画面が開くたびに取り直せば足りる（部屋の全員へ配る必要も無い）
    Public Function GetRoomBans() As RoomBansResult

        Dim roomId As Integer = 0

        SyncLock SyncRoot

            Dim owner As Participant = FindOwnerByConnectionCore(roomId)
            Dim rejected As String = CheckOwnerCore(roomId, owner)

            If rejected IsNot Nothing Then
                Return RoomBansRejected(rejected)
            End If

        End SyncLock

        Dim rows As List(Of RoomBanRow) = Nothing

        If Not TryReadRoomBans(roomId, rows) Then
            Return RoomBansRejected("今は一覧を読めません。少し待ってから、もう一度お試しください。")
        End If

        Return New RoomBansResult With {.Ok = True, .Bans = ToRoomBanInfos(roomId, rows)}

    End Function


    ' 入室禁止を解除する（間違えて押したときに戻せるように。開発者が一任）
    '
    ' 解除しても、部屋のログには何も出さない（記録 63 だけ。理由は ChatMessageType.RoomBanLifted のコメント）
    Public Function LiftRoomBan(targetAnonId As String) As RoomBansResult

        Dim roomId As Integer = 0

        SyncLock SyncRoot

            Dim owner As Participant = FindOwnerByConnectionCore(roomId)
            Dim rejected As String = CheckOwnerCore(roomId, owner)

            If rejected IsNot Nothing Then
                Return RoomBansRejected(rejected)
            End If

        End SyncLock

        Dim rows As List(Of RoomBanRow) = Nothing

        If Not TryReadRoomBans(roomId, rows) Then
            Return RoomBansRejected("今は解除できませんでした。少し待ってから、もう一度お試しください。")
        End If

        ' 匿名IDから、どの行かを探す（ブラウザの識別子は画面に渡していないので、その場で計算して突き合わせる）
        Dim row As RoomBanRow = rows.FirstOrDefault(
            Function(r) String.Equals(AnonId.Compute(roomId, r.BrowserId), targetAnonId, StringComparison.Ordinal))

        If row Is Nothing Then
            ' もう解除されている（別の画面で解除した、など）。今の一覧を返す
            Return New RoomBansResult With {.Ok = True, .Bans = ToRoomBanInfos(roomId, rows)}
        End If

        If Not TryDeleteRoomBan(roomId, row.BrowserId) Then
            Return RoomBansRejected("今は解除できませんでした。少し待ってから、もう一度お試しください。")
        End If

        SyncLock SyncRoot

            Dim known As HashSet(Of Guid) = Nothing

            If KnownBans.TryGetValue(roomId, known) Then
                known.Remove(row.BrowserId)
            End If

        End SyncLock

        rows.Remove(row)

        ' 記録（63）。記録する人は解除された人（禁止にした時点の見た目）
        ChatLog.Write(roomId, ChatMessageType.RoomBanLifted, New Participant With {
            .LocalId = 0,
            .Nickname = row.Nickname,
            .IconType = row.IconType,
            .IconColor = row.IconColor,
            .Trip = row.Trip,
            .BrowserId = row.BrowserId.ToString()
        })

        Return New RoomBansResult With {.Ok = True, .Bans = ToRoomBanInfos(roomId, rows)}

    End Function


    ' ============================================================
    '  入室の確かめ（JoinRoom から）
    ' ============================================================

    ' そのブラウザが、その部屋で入室禁止か
    '
    ' dbKnown … JoinRoom が部屋の状態を DB から読めたか。読めたなら dbBanned（DB の答え）を採る。
    '           読めなかった回は、控え（KnownBans）で決める。ただし一時部屋は、その前に JoinRoom が断っている（IsRoomGuardUnknownCore）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function IsRoomBannedCore(roomId As Integer, browserId As String,
                                             dbKnown As Boolean, dbBanned As Boolean) As Boolean

        Dim browser As Guid

        If Not Guid.TryParse(browserId, browser) Then
            Return False
        End If

        If dbKnown Then

            If dbBanned Then
                NoteRoomBanCore(roomId, browser)
            End If

            Return dbBanned

        End If

        Dim known As HashSet(Of Guid) = Nothing

        Return KnownBans.TryGetValue(roomId, known) AndAlso known.Contains(browser)

    End Function


    ' 控えに足す
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub NoteRoomBanCore(roomId As Integer, browser As Guid)

        Dim known As HashSet(Of Guid) = Nothing

        If Not KnownBans.TryGetValue(roomId, known) Then
            known = New HashSet(Of Guid)
            KnownBans(roomId) = known
        End If

        known.Add(browser)

    End Sub


    ' 部屋を忘れるときに、控えも捨てる（ForgetRoomCore から。DB の行は CloseRoomInDatabase が消す）
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Sub ForgetRoomBansCore(roomId As Integer)
        KnownBans.Remove(roomId)
    End Sub


    ' 部屋の状態を読む SELECT に足す「このブラウザは入室禁止か」（JoinRoom・ラウンジの入室ボタン・ノックで同じ形）。
    ' 呼ぶ側は @RoomId と @BanBrowserId（UNIQUEIDENTIFIER。読めなければ DBNull＝禁止ではない扱い）を渡す。
    ' 主キー（RoomId, BrowserId）で1行を見るだけ＝問い合わせの回数は増えない
    Public Const IsBannedSqlColumn As String =
        "CASE WHEN EXISTS (SELECT 1 FROM ChatRoomBans b WHERE b.RoomId = @RoomId AND b.BrowserId = @BanBrowserId) " &
        "THEN 1 ELSE 0 END AS IsBanned"

    ' @BanBrowserId に入れる値（Cookie の生の値。読めなければ DBNull）
    Public Shared Function BanBrowserParameter(browserId As String) As Object

        Dim browser As Guid

        If Guid.TryParse(browserId, browser) Then
            Return browser
        End If

        Return DBNull.Value

    End Function


    ' ============================================================
    '  小さな道具
    ' ============================================================

    ' 呼んだ人が、その部屋の管理人の道具を使えるか。使えなければ理由、使えれば Nothing
    ' 【必ず SyncRoot の中で呼ぶこと】
    Private Shared Function CheckOwnerCore(roomId As Integer, owner As Participant) As String

        If owner Is Nothing Then
            Return "部屋に入っていないため、使えません。"
        End If

        If Not TemporaryRooms.Contains(roomId) OrElse Not IsRoomOwnerCore(roomId, owner.BrowserId) Then
            Return "部屋の管理人だけが使えます。"
        End If

        Return Nothing

    End Function


    Private Shared Function OwnerActionRejected(message As String) As OwnerActionResult
        Return New OwnerActionResult With {.Ok = False, .Message = message}
    End Function


    Private Shared Function RoomBansRejected(message As String) As RoomBansResult
        Return New RoomBansResult With {.Ok = False, .Message = message}
    End Function


    Private Shared Function ToRoomBanInfos(roomId As Integer, rows As List(Of RoomBanRow)) As List(Of RoomBanInfo)

        Return rows.Select(Function(r) New RoomBanInfo With {
            .AnonId = AnonId.Compute(roomId, r.BrowserId),
            .Nickname = r.Nickname,
            .IconType = r.IconType,
            .IconColor = r.IconColor,
            .Trip = r.Trip
        }).ToList()

    End Function


    ' ============================================================
    '  DB（ChatRoomBans）
    ' ============================================================

    ' 表の1行（ChatBrowserId を持つので、画面へはそのまま渡さない）
    Private NotInheritable Class RoomBanRow
        Public BrowserId As Guid
        Public Nickname As String
        Public IconType As String
        Public IconColor As String
        Public Trip As String
    End Class

    Private Enum RoomBanInsertResult
        Inserted
        Full
        Failed
    End Enum


    ' 入室禁止を1行足す。もう禁止になっていれば、何もせずに Inserted（管理人から見れば同じ結果）
    '
    ' 【「もう禁止か」「上限か」「足す」を1本で】分けると、2回押されたときに主キーの重複で例外になる
    ' 【鍵の外で呼ぶこと】DB を鍵の中に持ち込まない
    Private Shared Function TryInsertRoomBan(roomId As Integer, browser As Guid, target As Participant,
                                             bannedBy As Guid) As RoomBanInsertResult

        Const sql As String =
            "IF EXISTS (SELECT 1 FROM ChatRoomBans WHERE RoomId = @RoomId AND BrowserId = @BrowserId) " &
            "    SELECT 0; " &
            "ELSE IF (SELECT COUNT(*) FROM ChatRoomBans WHERE RoomId = @RoomId) >= @MaxBans " &
            "    SELECT 1; " &
            "ELSE " &
            "BEGIN " &
            "    INSERT INTO ChatRoomBans (RoomId, BrowserId, Nickname, IconType, IconColor, Trip, BannedByBrowserId) " &
            "    VALUES (@RoomId, @BrowserId, @Nickname, @IconType, @IconColor, @Trip, @BannedBy); " &
            "    SELECT 0; " &
            "END"

        Try

            Using conn As New SqlConnection(RoomConnectionString())
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@BrowserId", SqlDbType.UniqueIdentifier).Value = browser
                    cmd.Parameters.Add("@MaxBans", SqlDbType.Int).Value = MaxBansPerRoom
                    cmd.Parameters.Add("@Nickname", SqlDbType.NVarChar, 50).Value = If(target.Nickname, "")
                    cmd.Parameters.Add("@IconType", SqlDbType.NVarChar, 30).Value = DbText(target.IconType)
                    cmd.Parameters.Add("@IconColor", SqlDbType.NVarChar, 10).Value = DbText(target.IconColor)
                    cmd.Parameters.Add("@Trip", SqlDbType.NVarChar, 10).Value = DbText(target.Trip)
                    cmd.Parameters.Add("@BannedBy", SqlDbType.UniqueIdentifier).Value =
                        If(bannedBy = Guid.Empty, CObj(DBNull.Value), CObj(bannedBy))

                    conn.Open()

                    Return If(Convert.ToInt32(cmd.ExecuteScalar()) = 1, RoomBanInsertResult.Full, RoomBanInsertResult.Inserted)

                End Using
            End Using

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryInsertRoomBan", "部屋 " & roomId)
            Return RoomBanInsertResult.Failed
        End Try

    End Function


    ' 入室禁止を1行消す（もう無くても成功）
    Private Shared Function TryDeleteRoomBan(roomId As Integer, browser As Guid) As Boolean

        Try

            Using conn As New SqlConnection(RoomConnectionString())
                Using cmd As New SqlCommand(
                    "DELETE FROM ChatRoomBans WHERE RoomId = @RoomId AND BrowserId = @BrowserId", conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId
                    cmd.Parameters.Add("@BrowserId", SqlDbType.UniqueIdentifier).Value = browser

                    conn.Open()
                    cmd.ExecuteNonQuery()

                End Using
            End Using

            Return True

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryDeleteRoomBan", "部屋 " & roomId)
            Return False
        End Try

    End Function


    ' その部屋の入室禁止の一覧（禁止にした順）
    Private Shared Function TryReadRoomBans(roomId As Integer, ByRef rows As List(Of RoomBanRow)) As Boolean

        rows = New List(Of RoomBanRow)

        Try

            Using conn As New SqlConnection(RoomConnectionString())
                Using cmd As New SqlCommand(
                    "SELECT BrowserId, Nickname, IconType, IconColor, Trip FROM ChatRoomBans " &
                    " WHERE RoomId = @RoomId ORDER BY BannedAt, BrowserId", conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()

                        While reader.Read()
                            rows.Add(New RoomBanRow With {
                                .BrowserId = CType(reader("BrowserId"), Guid),
                                .Nickname = Convert.ToString(reader("Nickname")),
                                .IconType = If(IsDBNull(reader("IconType")), Nothing, Convert.ToString(reader("IconType"))),
                                .IconColor = If(IsDBNull(reader("IconColor")), Nothing, Convert.ToString(reader("IconColor"))),
                                .Trip = If(IsDBNull(reader("Trip")), Nothing, Convert.ToString(reader("Trip")))
                            })
                        End While

                    End Using

                End Using
            End Using

            Return True

        Catch ex As Exception
            ErrorReport.Report(ex, "ChatHub.TryReadRoomBans", "部屋 " & roomId)
            Return False
        End Try

    End Function


    Private Shared Function DbText(value As String) As Object
        Return If(String.IsNullOrEmpty(value), CObj(DBNull.Value), CObj(value))
    End Function

End Class
