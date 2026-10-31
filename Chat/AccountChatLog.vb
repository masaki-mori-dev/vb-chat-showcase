Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Globalization

' 自分が参加したチャット履歴（チャットとアカウントの紐づけ）
'
' 【何のためのクラスか】
' アカウントの人が、管理画面（/account/chatlogs）で、自分が参加したチャットのログを読むための読み出し。
' SQL はここだけに書く（画面＝Chat/MyChatLogs.aspx・MyChatLogView.aspx は、呼んで並べるだけ）。
'
' 【見せてよい範囲】（開発者が決めたこと）
'   ① **アカウントID を表示して入った部屋だけ**。名乗らずに入ったときのぶんは、結び付けない（名乗らずに入る自由）
'   ② **その部屋に居た間**（入室〜退室。何回も出入りしたら、それぞれの間）の発言だけ
'   ③ こっそりは、**自分が送ったもの・自分宛てのもの**だけ
'   ④ 期間は、ログの保存期間と同じ（古いログが消えれば、ここからも消える。ここは ChatMessages を読むだけなので、何もしなくてもそうなる）
'
' 【表を1つも足していない】
' 「居た間」は、ChatMessages に最初から書かれている**入室の行（20）と退室の行（30番台）**から組み立てる。
' その行が誰のものかは UserId（アカウントの番号）で分かる——ChatLog.Write が、アカウントID を表示して
' 参加している人の行にだけ入れている。「参加の記録」の表を別に作ると、同じ事実（いつ入って、いつ出たか）を
' 2か所に書くことになり、片方だけ書けなかったときに食い違う。
'
' 【「1回の参加」の決め方】
'   始まり … UserId が自分で、種別が入室（20）の行
'   終わり … そのあとの、**同じ部屋・同じブラウザ**の退室の行（30番台のどれでも）
'   退室の行が無いとき（アプリが再起動した＝参加者がメモリごと消えて、退室の行が書かれない）は、
'     ・今もその部屋に居る（ハブの参加者リストに居る）→ 今まで
'     ・居ない → **その参加の中で、自分が最後に書いた行まで**
'   最後の形は、わざと狭い側に倒してある。再起動のあと、その人が画面を見ていたかどうかは分からないので、
'   「確かに居た」と言える所（自分の最後の発言）までしか見せない。
'
' 【置き換え（33）と、その直後の入室（20）は「続き」】（本番の実機テストで見つかった）
' 部屋で F5 を押すと、新しいページの入室が古いページの「閉じた」合図より先に着くことがある。
' そのときハブは「同じブラウザの2窓目」として古い席を置き換え（33）、新しい席の入室（20）を書く（ChatHub.JoinRoom の (a)）。
' 部屋の画面ではこの2行を出さない（外から見れば、その人は居続けている）が、ここが「退室の行＝30番台のどれでも」と
' 数えていたので、1回の参加が F5 のたびに分かれていた（本番で、1回入っただけなのに4行に分かれた）。
' そこで、次の2つを「続き」として扱う。
'   ・続きの入室 … 自分（同じブラウザ）の1つ前の行が、同じ部屋の置き換え（33）である入室
'                  → 1回の参加の始まりにしない（一覧に出さない・?s= に指定しても見つからない）
'   ・続きの置き換え … 自分（同じブラウザ）の1つ後の行が、同じ部屋の入室（20）である置き換え
'                  → 1回の参加の終わりにしない
' 「自分の1つ前／1つ後の行」は、どの部屋の行でも数える（別の部屋のタブに置き換えられた＝本当に出た、を取り違えないため）。
' 中身の画面では、続きの入室の行は今までどおり出さない（ほかの人の行が間に挟まっても、同じブラウザの直前で判定する）。
'
' 【ほかの人の記録を、番号を変えて読めない】
' 1回の参加は「入室の行の番号」で指す（URL の ?s=）。読む前に必ず FindSession を通し、
' **その番号の行が、自分の入室の行である**ことを DB で確かめる。合わなければ、無いものとして扱う。
' そのあとの読み出しは、そこで確かめた部屋・範囲・ブラウザだけを使う（URL から来た値を SQL の範囲に使わない）。
'
' 【重くならないように】
'   ・自分の行は、索引 IX_ChatMessages_UserId（UserId, MessageId ＋ 種別・部屋・ブラウザ・時刻）だけで引ける
'     （Database\CreateTable\ChatMessages.sql の索引。索引が無くても動く——遅いだけで、結果は同じ）
'   ・部屋の行は、今までの索引 IX_ChatMessages_RoomId_MessageId の範囲で引く（部屋の過去ログと同じ引き方）
'   ・どちらもページに分ける（1回の参加の一覧は SessionsPerPage、中身は RowsPerPage）
'
' 【例外は投げる】DB が読めなかったときの扱い（混み合っています、と出す）は、画面が決める
Public NotInheritable Class AccountChatLog

    Private Sub New()
    End Sub


    ' 1ページに出す「1回の参加」の数
    Public Const SessionsPerPage As Integer = 20

    ' 1ページに出す行の数（発言もお知らせの行も、この数に入る）
    Public Const RowsPerPage As Integer = 100

    ' ページ番号の上限（画面から来た数を、そのまま OFFSET に渡さないための線。記事のページ送りと同じ考え方）
    Public Const MaxPageNumber As Integer = 500


    ' 終わりが、何で決まったか
    Public Enum SessionEndKind

        ' 退室の行があった
        Left = 1

        ' 今もその部屋に居る
        StillIn = 2

        ' 退室の行が無い（アプリの再起動など）。自分が最後に書いた行までを見せる
        Unknown = 3

    End Enum


    ' 1回の参加
    Public Class Session

        ' 入室の行の番号（URL の ?s= に使う。これだけでは読めない＝FindSession が持ち主を確かめる）
        Public Property JoinMessageId As Long

        Public Property RoomId As Integer

        ' 部屋の名前（閉じた部屋でも引ける＝ChatRooms は論理削除。行が無ければ Nothing）
        Public Property RoomName As String

        ' そのとき使っていたブラウザ（ChatBrowserId）。こっそりの「自分宛て」を見分けるのに使う。
        ' 【画面へ出さないこと】本人確認の値である
        Public Property BrowserId As Guid

        Public Property JoinedAt As DateTime

        ' 見せてよい最後の行の番号と、その時刻
        Public Property EndMessageId As Long
        Public Property EndedAt As DateTime

        Public Property EndKind As SessionEndKind

    End Class


    ' 行の種類（画面が、見た目を分けるために使う）
    Public Enum RowKind

        ' 部屋の発言
        Message = 1

        ' こっそり（自分が送った／自分宛て）
        PrivateMessage = 2

        ' 入退室などのお知らせ
        Notice = 3

    End Enum


    ' 画面に出す1行
    Public Class Row

        Public Property Kind As RowKind

        ' 「」と「21:05」（作り方は部屋の過去ログと同じ。文化の設定に左右されない）
        Public Property DateText As String
        Public Property TimeText As String

        ' 発言した人の名前（その時点の名前）。お知らせの行では Nothing
        Public Property Nickname As String

        ' 名前の横に添える印（トリップの10桁か、「@ユーザー名」。無ければ Nothing）
        Public Property MarkText As String

        ' 印がアカウントID のときの行き先（公開のプロフィール）。トリップ・印なしのときは Nothing
        Public Property MarkUrl As String

        ' 自分の行か
        Public Property IsMine As Boolean

        ' 本文（発言）か、文言（お知らせ）。**HTML ではない**（画面がエスケープして出す）
        Public Property Text As String

        ' 本文が部屋の画像なら、その URL（こっそりの画像・スタンプ・文章では Nothing）
        Public Property ImageUrl As String

    End Class


    ' ============================================================
    ' 1回の参加の一覧（新しい順）
    ' ============================================================
    '
    ' pageNumber … 1 から。hasMore … 次のページがあるか（1件多く取って確かめる。COUNT は引かない）
    Public Shared Function GetSessions(accountId As Integer, pageNumber As Integer,
                                       ByRef hasMore As Boolean) As List(Of Session)

        hasMore = False

        Dim list As New List(Of Session)()

        If accountId <= 0 Then
            Return list
        End If

        pageNumber = NormalizePage(pageNumber)

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                SessionSelectSql &
                " WHERE j.UserId = @AccountId AND j.MessageType = @Join " &
                NotContinuationJoinSql &
                " ORDER BY j.MessageId DESC " &
                " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;", conn)

                AddSessionParameters(cmd, accountId)
                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = (pageNumber - 1) * SessionsPerPage
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = SessionsPerPage + 1

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(ReadSession(reader))
                    End While
                End Using

            End Using
        End Using

        If list.Count > SessionsPerPage Then
            hasMore = True
            list.RemoveAt(list.Count - 1)
        End If

        FinishSessions(accountId, list)

        Return list

    End Function


    ' 1回の参加を、入室の行の番号から引く（**自分のものでなければ Nothing**）
    '
    ' 【ここが、ほかの人の記録を読ませないための関所】
    ' WHERE に UserId = 自分 と 種別 = 入室 の両方が入っている。番号だけを変えても、他人の行・入室でない行には当たらない
    Public Shared Function FindSession(accountId As Integer, joinMessageId As Long) As Session

        If accountId <= 0 OrElse joinMessageId <= 0 Then
            Return Nothing
        End If

        Dim list As New List(Of Session)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                SessionSelectSql &
                " WHERE j.MessageId = @JoinId AND j.UserId = @AccountId AND j.MessageType = @Join " &
                NotContinuationJoinSql & ";", conn)

                AddSessionParameters(cmd, accountId)
                cmd.Parameters.Add("@JoinId", SqlDbType.BigInt).Value = joinMessageId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    If reader.Read() Then
                        list.Add(ReadSession(reader))
                    End If
                End Using

            End Using
        End Using

        If list.Count = 0 Then
            Return Nothing
        End If

        FinishSessions(accountId, list)

        Return list(0)

    End Function


    ' ============================================================
    ' 1回の参加の中身（古い順）
    ' ============================================================
    '
    ' session … 必ず FindSession が返したものを渡すこと（部屋・範囲・ブラウザを、ここから読む）
    '
    ' 【読む行】その部屋の、入室の行から終わりの行までのうち
    '   ・こっそり以外 … 部屋の過去ログと同じ行（40番台＝入れなかった記録・ノックは読まない）
    '   ・こっそり（種別11）… **送り主か宛先が、そのときの自分のブラウザである行だけ**
    ' 部屋の過去ログ（ChatLogReader）が「こっそりは絶対に読まない」としているのと、向きは同じである。
    ' こちらは当事者にだけ見せるので、当事者かどうかを SQL の中で絞る（読んでから画面で隠す形にしない）
    Public Shared Function GetRows(session As Session, pageNumber As Integer,
                                   ByRef hasMore As Boolean) As List(Of Row)

        hasMore = False

        Dim rows As New List(Of Row)()

        If session Is Nothing Then
            Return rows
        End If

        pageNumber = NormalizePage(pageNumber)

        ' トリップを画面へ渡してよいか（止まっているときは、焼き込まれていても出さない。部屋の過去ログと同じ判断）
        Dim isTripEnabled As Boolean = TripCode.IsEnabled

        ' 置き換え（33）の直後の、同じ人の入室（20）は出さない（部屋の過去ログと同じ。外から見れば、その人は居続けている）。
        ' ページの頭でだけ、前のページの最後の行が見えないので、そこは出ることがある（害は無い）
        '
        ' 「直前の行」は、同じブラウザの直前の行で見る（ほかの人の発言が間に挟まっても、続きの入室を隠せるように）
        Dim lastTypeByBrowser As New Dictionary(Of Guid, Integer)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT m.MessageId, m.MessageType, m.SenderBrowserId, m.Nickname, m.Trip, m.MessageText, m.CreatedAt, " &
                "       a.UserName AS AccountName " &
                "  FROM ChatMessages m " &
                "  LEFT JOIN Accounts a ON a.AccountId = m.UserId AND a.IsActive = 1 " &
                " WHERE m.RoomId = @RoomId " &
                "   AND m.MessageId >= @FromId AND m.MessageId <= @ToId " &
                "   AND ( (m.MessageType <> @Private AND (m.MessageType < 40 OR m.MessageType >= 50)) " &
                "      OR (m.MessageType = @Private AND (m.SenderBrowserId = @Me OR m.RecipientBrowserId = @Me)) ) " &
                " ORDER BY m.MessageId " &
                " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;", conn)

                cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = session.RoomId
                cmd.Parameters.Add("@FromId", SqlDbType.BigInt).Value = session.JoinMessageId
                cmd.Parameters.Add("@ToId", SqlDbType.BigInt).Value = session.EndMessageId
                cmd.Parameters.Add("@Private", SqlDbType.TinyInt).Value = CByte(ChatMessageType.PrivateMessage)
                cmd.Parameters.Add("@Me", SqlDbType.UniqueIdentifier).Value = session.BrowserId
                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = (pageNumber - 1) * RowsPerPage
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = RowsPerPage + 1

                conn.Open()

                Dim taken As Integer = 0

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    While reader.Read()

                        taken += 1

                        If taken > RowsPerPage Then
                            hasMore = True
                            Exit While
                        End If

                        Dim messageType As Integer = Convert.ToInt32(reader("MessageType"))
                        Dim createdAt As DateTime = CType(reader("CreatedAt"), DateTime)

                        Dim browser As Guid? = Nothing
                        If Not Convert.IsDBNull(reader("SenderBrowserId")) Then
                            browser = CType(reader("SenderBrowserId"), Guid)
                        End If

                        Dim nickname As String = reader("Nickname").ToString()
                        Dim text As String = NullableString(reader("MessageText"))

                        Dim row As New Row With {
                            .DateText = ArticleDisplay.DateText(createdAt),
                            .TimeText = createdAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                            .IsMine = (browser.HasValue AndAlso browser.Value = session.BrowserId)
                        }

                        Dim previousTypeOfBrowser As Integer = 0

                        If browser.HasValue Then
                            lastTypeByBrowser.TryGetValue(browser.Value, previousTypeOfBrowser)
                            lastTypeByBrowser(browser.Value) = messageType
                        End If

                        Dim isHiddenRejoin As Boolean =
                            (messageType = CInt(ChatMessageType.Join) AndAlso
                             previousTypeOfBrowser = CInt(ChatMessageType.Replaced))

                        If messageType = CInt(ChatMessageType.Message) OrElse
                           messageType = CInt(ChatMessageType.PrivateMessage) Then

                            row.Kind = If(messageType = CInt(ChatMessageType.Message),
                                          RowKind.Message, RowKind.PrivateMessage)
                            row.Nickname = nickname

                            ' 名前の横の印（アカウントID があれば、そちら。トリップとは、どちらか一方）
                            Dim accountName As String = NullableString(reader("AccountName"))

                            If accountName IsNot Nothing Then
                                row.MarkText = ChatAccountLink.DisplayText(accountName)
                                row.MarkUrl = ChatAccountLink.ProfileUrl(accountName)
                            ElseIf isTripEnabled Then
                                row.MarkText = NullableString(reader("Trip"))
                            End If

                            FillBody(row, text)

                        Else

                            If isHiddenRejoin Then
                                Continue While
                            End If

                            row.Kind = RowKind.Notice

                            If messageType = CInt(ChatMessageType.RoomSettingsChanged) Then
                                row.Text = ChatEventText.BuildRoomSettingsChanged(text)
                            Else
                                row.Text = ChatEventText.Build(CType(messageType, ChatMessageType), nickname)
                            End If

                            ' 文言の無い種別（置き換えの 33 など）は、部屋の画面と同じく出さない
                            If String.IsNullOrEmpty(row.Text) Then
                                Continue While
                            End If

                        End If

                        rows.Add(row)

                    End While

                End Using

            End Using
        End Using

        Return rows

    End Function


    ' 本文を、画面に出す形に直す
    '
    ' 画像・スタンプの発言は、本文が「印」だけになっている（[img:…]／[stamp:…]。サーバーが組み立てたもの）。
    ' 印の文字をそのまま見せても意味が分からないので、言葉に置き換える。
    '   ・部屋の画像 … 「（画像）」＋その画像への行き先（画像も、ログと同じ保存期間で消える）
    '   ・こっそりの画像 … 「（画像）」だけ。行き先は付けない——こっそりの画像の出口（Chat/PrivateImage.aspx）は、
    '     **そのときのブラウザ（ChatBrowserId）**で本人を確かめる作りで、あとからこの画面を開いたブラウザは別の身元でありうる。
    '     確かめ方を緩めて通す口を足すより、ここでは出さないほうを選んだ
    '   ・スタンプ … 「（スタンプ）」
    Private Shared Sub FillBody(row As Row, text As String)

        Dim imageName As String = Nothing
        Dim width As Integer
        Dim height As Integer

        If SiteImage.TryParseToken(text, imageName, width, height) Then

            row.Text = "（画像）"

            If row.Kind = RowKind.Message Then
                row.ImageUrl = SiteImage.PublicUrlPrefix & imageName
            End If

            Return

        End If

        If StampCatalog.ContainsToken(text) Then
            row.Text = "（スタンプ）"
            Return
        End If

        row.Text = If(text, "")

    End Sub


    ' ============================================================
    ' SQL（1回の参加を1行で引く）
    ' ============================================================
    '
    ' nx … 同じ部屋・同じブラウザの、次の入室の行（この参加の範囲を、そこで打ち切るため）。**続きの入室は数えない**
    ' lv … この参加の退室の行（次の入室より前にある、30番台の最初の1行）。**続きの置き換えは数えない**
    ' la … この参加の中で、自分が最後に書いた行（退室の行が無いときの終わり）
    '
    ' 【どれも、自分の行だけを見ている】（x.UserId = @AccountId）ので、索引 IX_ChatMessages_UserId の中だけで済む。
    ' 部屋の名前は、閉じた部屋でも引く（IsActive で絞らない＝過去ログの部屋名と同じ扱い）
    Private Const SessionSelectSql As String =
        "SELECT j.MessageId, j.RoomId, j.SenderBrowserId, j.CreatedAt, r.RoomName, " &
        "       lv.MessageId AS LeaveId, lv.CreatedAt AS LeftAt, " &
        "       la.MessageId AS LastOwnId, la.CreatedAt AS LastOwnAt, " &
        "       nx.MessageId AS NextJoinId " &
        "  FROM ChatMessages j " &
        "  LEFT JOIN ChatRooms r ON r.RoomId = j.RoomId " &
        " OUTER APPLY (SELECT TOP (1) p.MessageType, p.RoomId FROM ChatMessages p " &
        "               WHERE p.UserId = @AccountId AND p.SenderBrowserId = j.SenderBrowserId " &
        "                 AND p.MessageId < j.MessageId " &
        "               ORDER BY p.MessageId DESC) pj " &
        " OUTER APPLY (SELECT TOP (1) x.MessageId FROM ChatMessages x " &
        "               OUTER APPLY (SELECT TOP (1) p.MessageType, p.RoomId FROM ChatMessages p " &
        "                             WHERE p.UserId = @AccountId AND p.SenderBrowserId = x.SenderBrowserId " &
        "                               AND p.MessageId < x.MessageId " &
        "                             ORDER BY p.MessageId DESC) px " &
        "               WHERE x.UserId = @AccountId AND x.MessageId > j.MessageId " &
        "                 AND x.RoomId = j.RoomId AND x.SenderBrowserId = j.SenderBrowserId " &
        "                 AND x.MessageType = @Join " &
        "                 AND NOT (ISNULL(px.MessageType, 0) = @Replaced AND px.RoomId = x.RoomId) " &
        "               ORDER BY x.MessageId) nx " &
        " OUTER APPLY (SELECT TOP (1) x.MessageId, x.CreatedAt FROM ChatMessages x " &
        "               OUTER APPLY (SELECT TOP (1) q.MessageType, q.RoomId FROM ChatMessages q " &
        "                             WHERE q.UserId = @AccountId AND q.SenderBrowserId = x.SenderBrowserId " &
        "                               AND q.MessageId > x.MessageId " &
        "                             ORDER BY q.MessageId) qx " &
        "               WHERE x.UserId = @AccountId AND x.MessageId > j.MessageId " &
        "                 AND (nx.MessageId IS NULL OR x.MessageId < nx.MessageId) " &
        "                 AND x.RoomId = j.RoomId AND x.SenderBrowserId = j.SenderBrowserId " &
        "                 AND x.MessageType >= @LeaveFrom AND x.MessageType < @LeaveTo " &
        "                 AND NOT (x.MessageType = @Replaced AND ISNULL(qx.MessageType, 0) = @Join AND qx.RoomId = x.RoomId) " &
        "               ORDER BY x.MessageId) lv " &
        " OUTER APPLY (SELECT TOP (1) x.MessageId, x.CreatedAt FROM ChatMessages x " &
        "               WHERE x.UserId = @AccountId AND x.MessageId >= j.MessageId " &
        "                 AND (nx.MessageId IS NULL OR x.MessageId < nx.MessageId) " &
        "                 AND x.RoomId = j.RoomId AND x.SenderBrowserId = j.SenderBrowserId " &
        "               ORDER BY x.MessageId DESC) la "


    ' 1回の参加の始まりにしてよい入室か（続きの入室を外す）。SessionSelectSql の pj を使う。WHERE の後ろへ足す
    Private Const NotContinuationJoinSql As String =
        " AND NOT (ISNULL(pj.MessageType, 0) = @Replaced AND pj.RoomId = j.RoomId) "


    Private Shared Sub AddSessionParameters(cmd As SqlCommand, accountId As Integer)

        cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
        cmd.Parameters.Add("@Join", SqlDbType.TinyInt).Value = CByte(ChatMessageType.Join)
        cmd.Parameters.Add("@Replaced", SqlDbType.TinyInt).Value = CByte(ChatMessageType.Replaced)

        ' 退室は30番台のどれでも（範囲で見る。退室の理由が増えても、ここは直さなくてよい）
        cmd.Parameters.Add("@LeaveFrom", SqlDbType.TinyInt).Value = CByte(30)
        cmd.Parameters.Add("@LeaveTo", SqlDbType.TinyInt).Value = CByte(40)

    End Sub


    ' 1行を読む（終わりは、ここでは「退室の行があったか」までしか決めない。残りは FinishSessions）
    Private Shared Function ReadSession(reader As SqlDataReader) As Session

        Dim session As New Session With {
            .JoinMessageId = Convert.ToInt64(reader("MessageId")),
            .RoomId = Convert.ToInt32(reader("RoomId")),
            .RoomName = NullableString(reader("RoomName")),
            .JoinedAt = CType(reader("CreatedAt"), DateTime)
        }

        If Not Convert.IsDBNull(reader("SenderBrowserId")) Then
            session.BrowserId = CType(reader("SenderBrowserId"), Guid)
        End If

        If Not Convert.IsDBNull(reader("LeaveId")) Then

            session.EndKind = SessionEndKind.Left
            session.EndMessageId = Convert.ToInt64(reader("LeaveId"))
            session.EndedAt = CType(reader("LeftAt"), DateTime)

        Else

            ' 退室の行が無い。ひとまず「自分が最後に書いた行まで」（入室の行そのものが最低1行ある）
            session.EndKind = SessionEndKind.Unknown
            session.EndMessageId = Convert.ToInt64(reader("LastOwnId"))
            session.EndedAt = CType(reader("LastOwnAt"), DateTime)

            ' 次の入室が無い（＝いちばん新しい参加）ときだけ、「今も居るか」をこのあと確かめる。印として 0 を入れておく
            If Convert.IsDBNull(reader("NextJoinId")) Then
                session.EndKind = SessionEndKind.StillIn
            End If

        End If

        Return session

    End Function


    ' 「今も居るか」を、ハブの参加者リストで確かめて仕上げる
    '
    ' ReadSession が StillIn の候補にしたもの（退室の行も、次の入室も無い）だけを見る。
    '   居る   … 終わり＝今（その部屋の、いちばん新しい行まで見せる）
    '   居ない … 終わり＝自分が最後に書いた行（Unknown）
    '
    ' 【ハブの鍵の中で DB を引かない】先にハブへ聞き（メモリだけ）、そのあとで DB を引く
    Private Shared Sub FinishSessions(accountId As Integer, sessions As List(Of Session))

        For Each session As Session In sessions

            If session.EndKind <> SessionEndKind.StillIn Then
                Continue For
            End If

            If Not ChatHub.IsAccountInRoom(session.RoomId, session.BrowserId, accountId) Then
                session.EndKind = SessionEndKind.Unknown
                Continue For
            End If

            ' 今も居る。その部屋の、いちばん新しい行まで
            Using conn As New SqlConnection(ConnectionString())
                Using cmd As New SqlCommand(
                    "SELECT TOP (1) MessageId, CreatedAt FROM ChatMessages " &
                    " WHERE RoomId = @RoomId ORDER BY MessageId DESC;", conn)

                    cmd.Parameters.Add("@RoomId", SqlDbType.Int).Value = session.RoomId

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)
                        If reader.Read() Then

                            Dim lastId As Long = Convert.ToInt64(reader("MessageId"))

                            If lastId > session.EndMessageId Then
                                session.EndMessageId = lastId
                            End If

                        End If
                    End Using

                End Using
            End Using

            session.EndedAt = DateTime.Now

        Next

    End Sub


    ' 画面から来たページ番号を丸める（1 より小さい・大きすぎる → 範囲の中へ）
    Public Shared Function NormalizePage(pageNumber As Integer) As Integer

        If pageNumber < 1 Then
            Return 1
        End If

        If pageNumber > MaxPageNumber Then
            Return MaxPageNumber
        End If

        Return pageNumber

    End Function


    Private Shared Function NullableString(value As Object) As String

        If value Is Nothing OrElse Convert.IsDBNull(value) Then
            Return Nothing
        End If

        Return value.ToString()

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString
    End Function

End Class
