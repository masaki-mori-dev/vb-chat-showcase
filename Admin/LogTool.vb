Imports System.Collections.Specialized
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.Text

' 管理人が部屋のログを調べる道具の、データの側（画面は Admin/LogSearch.aspx・LogReplay.aspx・LogExport.aspx）
'
' 【何のための道具か】（開発者）
' 通報や警察からの依頼があったとき、該当する記録をすぐ探し、見つけたらその部屋の様子を再生し、
' 問い合わせに答えるのに要る情報（IP アドレス・ポート番号・時刻・トリップ・アカウントID・ブラウザの印）が全部分かりやすく出ること。
'
' 【使えるのはサイト管理者（administrator）だけ】守りは画面の側（RequireAdministrator）と、WebMethod・CSV の口の中（CurrentAdministrator）。
' 外の人に見せる用の見るだけの権限（viewer）には、メニューにも出さず、住所を打っても入れない。
'
' 【見た記録を残す】探した・開いた・さかのぼった・書き出したことを、ChatLogAudit 表に1回ごとに1行（WriteAudit）。
' **記録が書けなかったら、結果を見せない**（見た記録の残らない閲覧を作らない）。どの画面も、結果を出す前に WriteAudit を呼ぶ。
'
' 【C#アバターチャット(仮) へ移しやすく】このファイルは画面を知らない（HTML を作らない・Page を触らない）。
' 条件（SearchQuery・ReplayFilter）→ SQL → 行（LogRow）の形。HTML と CSV は Admin/LogToolView.vb が作る。
' SQL はどれもパラメータで渡す（探す言葉も。LIKE の % と _ と [ は、ただの文字として扱う＝LikeEscape）。
'
' 【ログの表の決まりはそのまま】並び順は MessageId（CreatedAt ではない）。部屋の名前は、閉じた部屋でも引く
Public NotInheritable Class LogTool

    Private Sub New()
    End Sub


    ' 探した結果の1ページの数
    Public Const PageSize As Integer = 50

    ' 何も条件を付けずに開いたときの期間（直近24時間）
    Public Const DefaultPeriodHours As Integer = 24

    ' 再生の画面：最初に出す、押した発言の前後の時間（分）
    Public Const ReplayMinutes As Integer = 30

    ' 再生の画面：最初に出す行の上限（前と後ろ、それぞれ）。にぎわっている部屋で、30分に何千行あっても重くしない
    Public Const ReplayInitialMaxPerSide As Integer = 300

    ' 再生の画面：「もっと前／もっと後」で1回に足す行の数
    Public Const ReplayMoreCount As Integer = 100

    ' CSV の行の上限（それより広い範囲は、出ている範囲を狭めて書き出す）
    Public Const ExportMaxRows As Integer = 20000

    ' 在室者を割り出すときに、さかのぼる日数（入室の行がそれより前の人は、居ないものとみなす）
    Private Const PresenceLookbackDays As Integer = 3

    ' 探す欄の長さの上限（それより長い分は切る）
    Public Const FieldMaxLength As Integer = 100


    ' ============================================================
    ' 種類
    ' ============================================================

    ' 探すときの「種類」（いくつでも選べる＝URL の kinds=1,2）
    '
    ' 【最初は「発言」と「こっそり」だけ】（開発者が本番で見て分かった）新しい順に並べると、
    ' サイトへの出入り（ログイン・ラウンジ）の行で1ページ目が埋まり、通報で探したい発言が出てこなかった。
    ' 入退室・入れなかった記録・サイトへの出入り・部屋の出来事は、印を足したときだけ出る。
    ' 【番号は URL に残るので変えない】All（0）は前の住所の kind=0（すべて）を読むためだけに残してある
    Public Enum KindFilter
        All = 0             ' （前の住所の kind=0 だけ。今は「全部に印」で表す）
        Message = 1         ' 発言（10）
        PrivateMessage = 2  ' こっそり（11）
        InOut = 3           ' 入退室（20・30番台）
        Site = 4            ' サイトへの出入り（ログイン・ラウンジ。RoomId 0・50番台）
        Rejected = 5        ' 入れなかった記録（40番台。満員・パスワード・ノック・入室禁止）
        RoomEvent = 6       ' 部屋の出来事（60番台。管理人が替わった・部屋の設定を変えた など）
    End Enum

    ' 画面に並べる順（チェックボックス）
    Public Shared ReadOnly AllKinds As KindFilter() = {
        KindFilter.Message, KindFilter.PrivateMessage, KindFilter.InOut, KindFilter.Rejected, KindFilter.Site, KindFilter.RoomEvent}

    ' 何も選ばれていないとき・最初に開いたときの印
    Public Shared ReadOnly DefaultKinds As KindFilter() = {KindFilter.Message, KindFilter.PrivateMessage}


    ' 行の種類の、管理人向けの名前（画面の小窓・CSV の「種類」）
    '
    ' 【部屋の画面の文言（Chat/ChatEventText.vb）とは別】あちらは利用者に見せる文で、退室の理由などを伏せている。
    ' 調べる道具では、記録の中身をそのまま出したいので、理由まで書いた名前を持つ。
    ' 番号の意味は Chat/ChatLog.vb の ChatMessageType（種類を足したら、ここにも1行）
    Public Shared Function KindLabel(messageType As Integer) As String

        Select Case messageType
            Case 10 : Return "発言"
            Case 11 : Return "こっそり"
            Case 20 : Return "入室"
            Case 30 : Return "退室（退室ボタン）"
            Case 31 : Return "退室（切断）"
            Case 32 : Return "退室（無言タイムアウト）"
            Case 33 : Return "退室（同じブラウザの別の窓に置き換え）"
            Case 34 : Return "退室（部屋の管理人が退室させた）"
            Case 35 : Return "退室（サイト管理者が退室させた）"
            Case 36 : Return "退室（応答なし）"
            Case 37 : Return "退室（ラウンジでログアウト）"
            Case 38 : Return "退室（部屋の管理人が入室禁止にした）"
            Case 40 : Return "入れなかった（満員）"
            Case 41 : Return "入れなかった（パスワード）"
            Case 42 : Return "ノックした"
            Case 43 : Return "ノックを許可された"
            Case 44 : Return "ノックを断られた"
            Case 45 : Return "ノック（管理人が決めなかった）"
            Case 46 : Return "ノック（許可されたが入らなかった）"
            Case 47 : Return "入れなかった（入室禁止）"
            Case 50 : Return "ログイン"
            Case 51 : Return "ラウンジでログアウト"
            Case 52 : Return "ラウンジに来た"
            Case 60 : Return "管理人が替わった"
            Case 61 : Return "部屋の設定を変えた"
            Case 62 : Return "管理人をゆずった"
            Case 63 : Return "入室禁止を解除された"
        End Select

        Return "種類 " & messageType.ToString(CultureInfo.InvariantCulture)

    End Function


    Public Shared Function KindFilterLabel(kind As KindFilter) As String

        Select Case kind
            Case KindFilter.Message : Return "発言"
            Case KindFilter.PrivateMessage : Return "こっそり"
            Case KindFilter.InOut : Return "入退室"
            Case KindFilter.Site : Return "サイトへの出入り"
            Case KindFilter.Rejected : Return "入れなかった"
            Case KindFilter.RoomEvent : Return "部屋の出来事"
        End Select

        Return "すべて"

    End Function


    ' ============================================================
    ' 探す条件
    ' ============================================================

    ' 探す条件。URL のクエリ文字列と行き来する（探した結果は GET の住所で開ける＝Gmail と同じ。戻る・ブックマークが効く）
    Public NotInheritable Class SearchQuery

        Public Property Text As String          ' q    … 本文と名前から探す言葉（部分一致）
        Public Property FromAt As DateTime?     ' from … いつから（この時刻を含む）
        Public Property ToAt As DateTime?       ' to   … いつまで（この分の終わりまで含む）
        Public Property RoomId As Integer?      ' room … 部屋（Nothing＝すべて。0＝サイトへの出入り）
        Public Property Name As String          ' name … 名前（部分一致）
        Public Property Trip As String          ' trip … トリップ（前方一致）
        Public Property Ip As String            ' ip   … IP アドレス（前方一致。「203.0.113.」のように途中まででもよい）
        Public Property Account As String       ' acct … アカウントID（「@」は無くてもよい。完全一致）
        Public Property Kinds As New List(Of KindFilter)(DefaultKinds)   ' kinds … 種類（いくつでも。並びは AllKinds の順）
        Public Property Sender As Guid?         ' sender … ブラウザの印（SenderBrowserId。「この人のほかの記録」）
        Public Property Page As Integer = 1     ' p

        ' 何も付けずに開いた（＝直近24時間を入れて見せた）
        Public Property IsInitial As Boolean

        ' 日時の欄（datetime-local）と URL の形
        Public Const DateTimeFormat As String = "yyyy-MM-dd'T'HH':'mm"

        ' URL に出てくる名前（これが1つも無ければ「最初に開いた」）
        Private Shared ReadOnly Keys As String() = {"q", "from", "to", "room", "name", "trip", "ip", "acct", "kind", "kinds", "sender", "p", "all"}


        ' URL から読む。読めない値は、無いものとして扱う（断らない。探す画面なので）
        Public Shared Function FromQueryString(qs As NameValueCollection, now As DateTime) As SearchQuery

            Dim q As New SearchQuery()

            Dim anyKey As Boolean = False
            For Each key As String In Keys
                If qs(key) IsNot Nothing Then anyKey = True
            Next

            If Not anyKey Then
                q.IsInitial = True
                q.FromAt = TrimToMinute(now.AddHours(-DefaultPeriodHours))
                Return q
            End If

            q.Text = Clean(qs("q"))
            q.FromAt = ParseDateTime(qs("from"))
            q.ToAt = ParseDateTime(qs("to"))
            q.Name = Clean(qs("name"))
            q.Trip = Clean(qs("trip"))
            q.Ip = Clean(qs("ip"))
            q.Account = Clean(qs("acct"))

            If q.Account IsNot Nothing AndAlso q.Account.StartsWith("@", StringComparison.Ordinal) Then
                q.Account = Clean(q.Account.Substring(1))
            End If

            Dim room As Integer
            If Integer.TryParse(qs("room"), NumberStyles.None, CultureInfo.InvariantCulture, room) Then
                q.RoomId = room
            End If

            q.Kinds = ParseKinds(qs("kinds"), qs("kind"))

            Dim sender As Guid
            If Guid.TryParse(qs("sender"), sender) Then
                q.Sender = sender
            End If

            Dim page As Integer
            If Integer.TryParse(qs("p"), NumberStyles.None, CultureInfo.InvariantCulture, page) AndAlso page >= 1 AndAlso page <= 100000 Then
                q.Page = page
            End If

            Return q

        End Function


        ' URL のクエリ文字列（先頭の ? は付けない）。page が 1 以下なら p を付けない
        Public Function ToQueryString(page As Integer) As String

            Dim parts As New List(Of String)()

            AddPart(parts, "q", Text)
            If FromAt.HasValue Then AddPart(parts, "from", FromAt.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture))
            If ToAt.HasValue Then AddPart(parts, "to", ToAt.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture))
            If RoomId.HasValue Then AddPart(parts, "room", RoomId.Value.ToString(CultureInfo.InvariantCulture))
            AddPart(parts, "name", Name)
            AddPart(parts, "trip", Trip)
            AddPart(parts, "ip", Ip)
            AddPart(parts, "acct", Account)
            ' 数字と「,」だけなので、そのまま（%2c にしない＝住所が読みやすい）
            If Not IsDefaultKinds Then parts.Add("kinds=" & KindsText())
            If Sender.HasValue Then AddPart(parts, "sender", Sender.Value.ToString("D"))
            If page > 1 Then AddPart(parts, "p", page.ToString(CultureInfo.InvariantCulture))

            ' 条件が1つも無い（期間も空）＝「全部の期間・全部」。何も付けないと「最初に開いた」と区別できないので、印を1つ付ける
            If parts.Count = 0 Then parts.Add("all=1")

            Return String.Join("&", parts)

        End Function


        ' 見た記録（ChatLogAudit.Detail）に残す、条件の文
        Public Function Describe() As String

            Dim parts As New List(Of String)()

            If Text IsNot Nothing Then parts.Add("言葉=" & Text)
            parts.Add("期間=" & If(FromAt.HasValue, FormatMinute(FromAt.Value), "") & "〜" & If(ToAt.HasValue, FormatMinute(ToAt.Value), ""))
            If RoomId.HasValue Then parts.Add("部屋=" & RoomId.Value.ToString(CultureInfo.InvariantCulture))
            If Name IsNot Nothing Then parts.Add("名前=" & Name)
            If Trip IsNot Nothing Then parts.Add("トリップ=" & Trip)
            If Ip IsNot Nothing Then parts.Add("IP=" & Ip)
            If Account IsNot Nothing Then parts.Add("アカウントID=" & Account)
            parts.Add("種類=" & String.Join("・", Kinds.Select(Function(k) KindFilterLabel(k))))
            If Sender.HasValue Then parts.Add("ブラウザの印=" & Sender.Value.ToString("D"))
            If Page > 1 Then parts.Add("ページ=" & Page.ToString(CultureInfo.InvariantCulture))
            If IsInitial Then parts.Add("（最初に開いた）")

            Return String.Join("／", parts)

        End Function


        ' 詳しい条件（▼の中）が、1つでも入っているか（入っていれば、開いた形で出す）
        Public ReadOnly Property HasDetail As Boolean
            Get
                Return RoomId.HasValue OrElse Name IsNot Nothing OrElse Trip IsNot Nothing OrElse Ip IsNot Nothing OrElse
                       Account IsNot Nothing OrElse Not IsDefaultKinds OrElse ToAt.HasValue OrElse
                       (Not IsInitial AndAlso FromAt.HasValue)
            End Get
        End Property


        ' 種類が最初の印（発言・こっそり）のままか
        Public ReadOnly Property IsDefaultKinds As Boolean
            Get
                Return Kinds.SequenceEqual(DefaultKinds)
            End Get
        End Property

        ' 全部に印（＝種類で絞らない）か
        Public ReadOnly Property IsAllKinds As Boolean
            Get
                Return Kinds.Count = AllKinds.Length
            End Get
        End Property

        ' 「1,2」（URL の kinds の値）
        Public Function KindsText() As String
            Return String.Join(",", Kinds.Select(Function(k) CInt(k).ToString(CultureInfo.InvariantCulture)))
        End Function

        Private Shared Sub AddPart(parts As List(Of String), key As String, value As String)
            If Not String.IsNullOrEmpty(value) Then
                parts.Add(key & "=" & HttpUtility.UrlEncode(value))
            End If
        End Sub

    End Class


    ' 種類の印を読む。kinds（「1,2,3」）が無ければ、前の住所の kind（1つだけ。0＝すべて）を読む。
    ' 読めるものが1つも無ければ、最初の印（発言・こっそり）。並びは AllKinds の順にそろえる（URL と見た記録の文が、選んだ順で変わらないように）
    Public Shared Function ParseKinds(kindsValue As String, legacyKind As String) As List(Of KindFilter)

        Dim picked As New HashSet(Of KindFilter)()

        If kindsValue IsNot Nothing Then

            For Each part As String In kindsValue.Split(","c)
                Dim n As Integer
                If Integer.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, n) AndAlso
                   n <> 0 AndAlso [Enum].IsDefined(GetType(KindFilter), n) Then
                    picked.Add(CType(n, KindFilter))
                End If
            Next

        ElseIf legacyKind IsNot Nothing Then

            Dim n As Integer
            If Integer.TryParse(legacyKind, NumberStyles.None, CultureInfo.InvariantCulture, n) AndAlso [Enum].IsDefined(GetType(KindFilter), n) Then
                If n = 0 Then
                    picked.UnionWith(AllKinds)
                Else
                    picked.Add(CType(n, KindFilter))
                End If
            End If

        End If

        If picked.Count = 0 Then
            picked.UnionWith(DefaultKinds)
        End If

        Return AllKinds.Where(Function(k) picked.Contains(k)).ToList()

    End Function


    ' 前後の空白を省き、長すぎる分を切る。空なら Nothing
    Public Shared Function Clean(value As String) As String

        If value Is Nothing Then Return Nothing

        Dim trimmed As String = value.Trim()

        If trimmed.Length = 0 Then Return Nothing

        If trimmed.Length > FieldMaxLength Then
            trimmed = trimmed.Substring(0, FieldMaxLength)
        End If

        Return trimmed

    End Function


    ' 「T16:47」（日時の欄＝datetime-local の形）。秒が付いていても読む。読めなければ Nothing
    Public Shared Function ParseDateTime(value As String) As DateTime?

        Dim text As String = Clean(value)
        If text Is Nothing Then Return Nothing

        Dim result As DateTime
        Dim formats As String() = {"yyyy-MM-dd'T'HH':'mm", "yyyy-MM-dd'T'HH':'mm':'ss", "yyyy-MM-dd'T'HH':'mm':'ss'.'fff", "yyyy-MM-dd"}

        If DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, result) Then
            Return TrimToMinute(result)
        End If

        Return Nothing

    End Function


    Public Shared Function TrimToMinute(value As DateTime) As DateTime
        Return New DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0)
    End Function


    ' 「2026/10/11 16:47」（InvariantCulture＝共用サーバーのカルチャで和暦にならない）
    Public Shared Function FormatMinute(value As DateTime) As String
        Return value.ToString("yyyy'/'MM'/'dd HH':'mm", CultureInfo.InvariantCulture)
    End Function


    ' 「2026/10/11 16:47:05.123」（ミリ秒まで。小窓と CSV）
    Public Shared Function FormatMillis(value As DateTime) As String
        Return value.ToString("yyyy'/'MM'/'dd HH':'mm':'ss'.'fff", CultureInfo.InvariantCulture)
    End Function


    ' LIKE の中で、% と _ と [ を「ただの文字」にする（SQL Server の書き方＝[ ] で囲む）
    Public Shared Function LikeEscape(value As String) As String

        Dim sb As New StringBuilder(value.Length + 8)

        For Each c As Char In value
            Select Case c
                Case "["c : sb.Append("[[]")
                Case "%"c : sb.Append("[%]")
                Case "_"c : sb.Append("[_]")
                Case Else : sb.Append(c)
            End Select
        Next

        Return sb.ToString()

    End Function


    ' ============================================================
    ' 行
    ' ============================================================

    ' ChatMessages の1行（と、引いてきた部屋の名前・アカウントID・宛先の名前）
    Public NotInheritable Class LogRow
        Public Property MessageId As Long
        Public Property RoomId As Integer
        Public Property RoomName As String
        Public Property RoomIsActive As Boolean?
        Public Property MessageType As Integer
        Public Property UserId As Integer?
        Public Property AccountName As String       ' UserId のアカウントの、今のアカウントID（退会していれば Nothing）
        Public Property SenderBrowserId As Guid?
        Public Property RecipientBrowserId As Guid?
        Public Property RecipientName As String     ' こっそりの宛先の名前（その部屋で、その行より前に宛先の人が書いた最後の行から）
        Public Property Nickname As String
        Public Property IconType As String
        Public Property IconColor As String
        Public Property Trip As String
        Public Property MessageText As String
        Public Property IpAddress As String
        Public Property RemotePort As Integer?
        Public Property CreatedAt As DateTime

        Public ReadOnly Property IsPrivate As Boolean
            Get
                Return MessageType = 11
            End Get
        End Property

        ' 吹き出しで出す行（発言・こっそり）か。ほかは、お知らせの行
        Public ReadOnly Property IsTalk As Boolean
            Get
                Return MessageType = 10 OrElse MessageType = 11
            End Get
        End Property
    End Class


    ' 行を読むときの列（どの SQL もこれを使う。m＝ChatMessages）
    '
    ' 宛先の名前（rn）＝**同じ部屋で、その行より前に、宛先の人が書いた最後の行の名前**。
    ' 部屋の索引（RoomId, MessageId DESC）を後ろ向きにたどるので、遅くとも宛先が入室した行で止まる。
    ' こっそりの行でだけ引く（ほかの行では APPLY の中の条件が偽になり、何も読まない）
    Private Const RowColumnsSql As String =
        "m.MessageId, m.RoomId, r.RoomName, r.IsActive AS RoomIsActive, m.MessageType, m.UserId, a.UserName AS AccountName, " &
        "m.SenderBrowserId, m.RecipientBrowserId, rn.Nickname AS RecipientName, m.Nickname, m.IconType, m.IconColor, m.Trip, " &
        "m.MessageText, m.IpAddress, m.RemotePort, m.CreatedAt "

    Private Const RowJoinsSql As String =
        "  LEFT JOIN ChatRooms r ON r.RoomId = m.RoomId " &
        "  LEFT JOIN Accounts a ON a.AccountId = m.UserId " &
        " OUTER APPLY (SELECT TOP (1) x.Nickname FROM ChatMessages x " &
        "               WHERE m.RecipientBrowserId IS NOT NULL AND x.RoomId = m.RoomId " &
        "                 AND x.MessageId < m.MessageId AND x.SenderBrowserId = m.RecipientBrowserId " &
        "               ORDER BY x.MessageId DESC) rn "


    Private Shared Function ReadRow(reader As SqlDataReader) As LogRow

        Dim row As New LogRow With {
            .MessageId = Convert.ToInt64(reader("MessageId")),
            .RoomId = Convert.ToInt32(reader("RoomId")),
            .RoomName = NullableString(reader("RoomName")),
            .MessageType = Convert.ToInt32(reader("MessageType")),
            .AccountName = NullableString(reader("AccountName")),
            .RecipientName = NullableString(reader("RecipientName")),
            .Nickname = If(NullableString(reader("Nickname")), ""),
            .IconType = NullableString(reader("IconType")),
            .IconColor = NullableString(reader("IconColor")),
            .Trip = NullableString(reader("Trip")),
            .MessageText = NullableString(reader("MessageText")),
            .IpAddress = NullableString(reader("IpAddress")),
            .CreatedAt = CType(reader("CreatedAt"), DateTime)
        }

        If Not Convert.IsDBNull(reader("RoomIsActive")) Then row.RoomIsActive = CBool(reader("RoomIsActive"))
        If Not Convert.IsDBNull(reader("UserId")) Then row.UserId = Convert.ToInt32(reader("UserId"))
        If Not Convert.IsDBNull(reader("SenderBrowserId")) Then row.SenderBrowserId = CType(reader("SenderBrowserId"), Guid)
        If Not Convert.IsDBNull(reader("RecipientBrowserId")) Then row.RecipientBrowserId = CType(reader("RecipientBrowserId"), Guid)
        If Not Convert.IsDBNull(reader("RemotePort")) Then row.RemotePort = Convert.ToInt32(reader("RemotePort"))

        Return row

    End Function


    Private Shared Function ReadRows(cmd As SqlCommand) As List(Of LogRow)

        Dim rows As New List(Of LogRow)()

        Using reader As SqlDataReader = cmd.ExecuteReader()
            While reader.Read()
                rows.Add(ReadRow(reader))
            End While
        End Using

        Return rows

    End Function


    ' ============================================================
    ' 探す
    ' ============================================================

    ' 条件に合う行を、新しい順に1ページ分。hasMore＝次のページ（もっと古い行）があるか
    '
    ' 【アカウントID】Accounts のアカウントID から番号を引いて、ChatMessages.UserId で絞る。
    ' UserId が入るのは「アカウントID を表示して参加した人の行」だけ（名乗らずに入った行は出ない）
    '
    ' 【索引】期間は IX_ChatMessages_CreatedAt、部屋は IX_ChatMessages_RoomId_MessageId が効く。
    ' 言葉・名前・IP・ブラウザの印は、期間の中を読んで絞る（ログは保存期間で消えていくので、全部読んでも大きくならない）
    Public Shared Function Search(q As SearchQuery, ByRef hasMore As Boolean) As List(Of LogRow)

        hasMore = False

        Dim where As New StringBuilder(" WHERE 1 = 1 ")

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand()

                cmd.Connection = conn

                If q.FromAt.HasValue Then
                    where.Append(" AND m.CreatedAt >= @From ")
                    cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = q.FromAt.Value
                End If

                If q.ToAt.HasValue Then
                    ' 「〜16:47」は 16:47 の1分間も含める（分の終わりまで）
                    where.Append(" AND m.CreatedAt < @To ")
                    cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = q.ToAt.Value.AddMinutes(1)
                End If

                If q.Text IsNot Nothing Then
                    where.Append(" AND (m.MessageText LIKE @Text OR m.Nickname LIKE @Text) ")
                    cmd.Parameters.Add("@Text", SqlDbType.NVarChar, 4000).Value = "%" & LikeEscape(q.Text) & "%"
                End If

                If q.RoomId.HasValue Then
                    where.Append(" AND m.RoomId = @Room ")
                    cmd.Parameters.Add("@Room", SqlDbType.Int).Value = q.RoomId.Value
                End If

                If q.Name IsNot Nothing Then
                    where.Append(" AND m.Nickname LIKE @Name ")
                    cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 4000).Value = "%" & LikeEscape(q.Name) & "%"
                End If

                If q.Trip IsNot Nothing Then
                    where.Append(" AND m.Trip LIKE @Trip ")
                    cmd.Parameters.Add("@Trip", SqlDbType.NVarChar, 4000).Value = LikeEscape(q.Trip) & "%"
                End If

                If q.Ip IsNot Nothing Then
                    where.Append(" AND m.IpAddress LIKE @Ip ")
                    cmd.Parameters.Add("@Ip", SqlDbType.NVarChar, 4000).Value = LikeEscape(q.Ip) & "%"
                End If

                If q.Account IsNot Nothing Then
                    where.Append(" AND m.UserId = (SELECT TOP (1) ac.AccountId FROM Accounts ac WHERE ac.UserName = @Account) ")
                    cmd.Parameters.Add("@Account", SqlDbType.NVarChar, 100).Value = q.Account
                End If

                If q.Sender.HasValue Then
                    where.Append(" AND m.SenderBrowserId = @Sender ")
                    cmd.Parameters.Add("@Sender", SqlDbType.UniqueIdentifier).Value = q.Sender.Value
                End If

                ' 種類（印の付いたものを OR で）。全部に印なら絞らない。
                ' 【部屋に「サイトへの出入り」（RoomId 0）を選んだときは、種類で絞らない】そこには50番台の行しか無いので、
                ' 最初の印（発言・こっそり）のままだと何も出ない
                If Not q.IsAllKinds AndAlso Not (q.RoomId.HasValue AndAlso q.RoomId.Value = 0) Then

                    Dim ors As New List(Of String)()

                    For Each kind As KindFilter In q.Kinds
                        Select Case kind
                            Case KindFilter.Message : ors.Add("(m.RoomId <> 0 AND m.MessageType = 10)")
                            Case KindFilter.PrivateMessage : ors.Add("(m.RoomId <> 0 AND m.MessageType = 11)")
                            Case KindFilter.InOut : ors.Add("(m.RoomId <> 0 AND (m.MessageType = 20 OR (m.MessageType >= 30 AND m.MessageType < 40)))")
                            Case KindFilter.Rejected : ors.Add("(m.RoomId <> 0 AND m.MessageType >= 40 AND m.MessageType < 50)")
                            Case KindFilter.Site : ors.Add("(m.RoomId = 0)")
                            Case KindFilter.RoomEvent : ors.Add("(m.RoomId <> 0 AND m.MessageType >= 60 AND m.MessageType < 70)")
                        End Select
                    Next

                    where.Append(" AND (" & String.Join(" OR ", ors) & ") ")

                End If

                cmd.CommandText =
                    "SELECT " & RowColumnsSql &
                    "  FROM ChatMessages m " & RowJoinsSql &
                    where.ToString() &
                    " ORDER BY m.MessageId DESC " &
                    " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;"

                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = (Math.Max(1, q.Page) - 1) * PageSize
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = PageSize + 1

                conn.Open()

                Dim rows As List(Of LogRow) = ReadRows(cmd)

                If rows.Count > PageSize Then
                    hasMore = True
                    rows.RemoveAt(rows.Count - 1)
                End If

                Return rows

            End Using
        End Using

    End Function


    ' 部屋の一覧（探す条件の「部屋」。閉じた部屋も選べる。開いている部屋が先、その中は新しい順）
    Public NotInheritable Class RoomOption
        Public Property RoomId As Integer
        Public Property RoomName As String
        Public Property IsActive As Boolean
    End Class

    Public Shared Function GetRooms() As List(Of RoomOption)

        Dim list As New List(Of RoomOption)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT RoomId, RoomName, IsActive FROM ChatRooms ORDER BY IsActive DESC, RoomId DESC;", conn)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(New RoomOption With {
                            .RoomId = Convert.ToInt32(reader("RoomId")),
                            .RoomName = If(NullableString(reader("RoomName")), ""),
                            .IsActive = CBool(reader("IsActive"))
                        })
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' ============================================================
    ' 再生
    ' ============================================================

    ' 再生の画面で、どの行を出すか（URL の p・pair と行き来する）
    Public NotInheritable Class ReplayFilter

        ' こっそりも混ぜる（p=1）
        Public Property IncludePrivate As Boolean

        ' 「この2人のこっそりだけ」（pair=印,印）。入っていれば、この2人のあいだのこっそりだけを出す
        Public Property PairA As Guid?
        Public Property PairB As Guid?

        Public ReadOnly Property HasPair As Boolean
            Get
                Return PairA.HasValue AndAlso PairB.HasValue
            End Get
        End Property

        Public Shared Function FromQueryString(qs As NameValueCollection) As ReplayFilter

            Dim f As New ReplayFilter With {
                .IncludePrivate = String.Equals(qs("p"), "1", StringComparison.Ordinal)
            }

            Dim pair As String = qs("pair")

            If pair IsNot Nothing Then

                Dim parts As String() = pair.Split(","c)
                Dim a As Guid
                Dim b As Guid

                If parts.Length = 2 AndAlso Guid.TryParse(parts(0), a) AndAlso Guid.TryParse(parts(1), b) AndAlso a <> b Then
                    f.PairA = a
                    f.PairB = b
                    f.IncludePrivate = True
                End If

            End If

            Return f

        End Function

        Public Function PairText() As String
            If Not HasPair Then Return ""
            Return PairA.Value.ToString("D") & "," & PairB.Value.ToString("D")
        End Function

        ' 見た記録に残す文
        Public Function Describe() As String
            If HasPair Then Return "この2人のこっそりだけ=" & PairText()
            Return If(IncludePrivate, "こっそり=混ぜる", "こっそり=混ぜない")
        End Function

        ' WHERE に足す条件（m＝ChatMessages）。パラメータ @PairA・@PairB は AddParameters が足す
        Friend Function WhereSql() As String
            If HasPair Then
                Return " AND m.MessageType = 11 AND ((m.SenderBrowserId = @PairA AND m.RecipientBrowserId = @PairB) " &
                       "   OR (m.SenderBrowserId = @PairB AND m.RecipientBrowserId = @PairA)) "
            End If
            If Not IncludePrivate Then Return " AND m.MessageType <> 11 "
            Return ""
        End Function

        Friend Sub AddParameters(cmd As SqlCommand)
            If HasPair Then
                cmd.Parameters.Add("@PairA", SqlDbType.UniqueIdentifier).Value = PairA.Value
                cmd.Parameters.Add("@PairB", SqlDbType.UniqueIdentifier).Value = PairB.Value
            End If
        End Sub

    End Class


    ' 1行を番号で引く（再生の中心）。無ければ Nothing
    Public Shared Function FindMessage(messageId As Long) As LogRow

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT " & RowColumnsSql & " FROM ChatMessages m " & RowJoinsSql & " WHERE m.MessageId = @Id;", conn)

                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = messageId

                conn.Open()

                Dim rows As List(Of LogRow) = ReadRows(cmd)
                Return If(rows.Count = 0, Nothing, rows(0))

            End Using
        End Using

    End Function


    ' 中心の行の前後（それぞれ ReplayMinutes 分・ReplayInitialMaxPerSide 行まで）。古い順。
    ' 中心の行は、絞り込み（こっそりを混ぜない・この2人だけ）に合わなくても、必ず入れる（押して来た行なので）
    Public Shared Function GetAround(center As LogRow, filter As ReplayFilter,
                                     ByRef hasBefore As Boolean, ByRef hasAfter As Boolean) As List(Of LogRow)

        Dim result As New List(Of LogRow)()

        Using conn As New SqlConnection(ConnectionString())

            conn.Open()

            Dim before As List(Of LogRow)

            Using cmd As New SqlCommand(
                "SELECT TOP (@Take) " & RowColumnsSql & " FROM ChatMessages m " & RowJoinsSql &
                " WHERE m.RoomId = @Room AND m.MessageId < @Center AND m.CreatedAt >= @From " & filter.WhereSql() &
                " ORDER BY m.MessageId DESC;", conn)

                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = ReplayInitialMaxPerSide
                cmd.Parameters.Add("@Room", SqlDbType.Int).Value = center.RoomId
                cmd.Parameters.Add("@Center", SqlDbType.BigInt).Value = center.MessageId
                cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = center.CreatedAt.AddMinutes(-ReplayMinutes)
                filter.AddParameters(cmd)

                before = ReadRows(cmd)
                before.Reverse()

            End Using

            Dim after As List(Of LogRow)

            Using cmd As New SqlCommand(
                "SELECT TOP (@Take) " & RowColumnsSql & " FROM ChatMessages m " & RowJoinsSql &
                " WHERE m.RoomId = @Room AND m.MessageId > @Center AND m.CreatedAt <= @To " & filter.WhereSql() &
                " ORDER BY m.MessageId;", conn)

                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = ReplayInitialMaxPerSide
                cmd.Parameters.Add("@Room", SqlDbType.Int).Value = center.RoomId
                cmd.Parameters.Add("@Center", SqlDbType.BigInt).Value = center.MessageId
                cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = center.CreatedAt.AddMinutes(ReplayMinutes)
                filter.AddParameters(cmd)

                after = ReadRows(cmd)

            End Using

            result.AddRange(before)
            result.Add(center)
            result.AddRange(after)

            hasBefore = ExistsBeyond(conn, center.RoomId, If(before.Count > 0, before(0).MessageId, center.MessageId), True, filter)
            hasAfter = ExistsBeyond(conn, center.RoomId, If(after.Count > 0, after(after.Count - 1).MessageId, center.MessageId), False, filter)

        End Using

        Return result

    End Function


    ' 「もっと前／もっと後」。edgeId は、今出ている端の行（その行は含めない）。古い順で返す
    Public Shared Function GetMore(roomId As Integer, edgeId As Long, older As Boolean, filter As ReplayFilter,
                                   ByRef hasMore As Boolean) As List(Of LogRow)

        Using conn As New SqlConnection(ConnectionString())

            conn.Open()

            Dim rows As List(Of LogRow)

            Using cmd As New SqlCommand(
                "SELECT TOP (@Take) " & RowColumnsSql & " FROM ChatMessages m " & RowJoinsSql &
                " WHERE m.RoomId = @Room AND " & If(older, "m.MessageId < @Edge", "m.MessageId > @Edge") & filter.WhereSql() &
                " ORDER BY m.MessageId" & If(older, " DESC", "") & ";", conn)

                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = ReplayMoreCount
                cmd.Parameters.Add("@Room", SqlDbType.Int).Value = roomId
                cmd.Parameters.Add("@Edge", SqlDbType.BigInt).Value = edgeId
                filter.AddParameters(cmd)

                rows = ReadRows(cmd)

            End Using

            If older Then rows.Reverse()

            Dim newEdge As Long = edgeId
            If rows.Count > 0 Then
                newEdge = If(older, rows(0).MessageId, rows(rows.Count - 1).MessageId)
            End If

            hasMore = ExistsBeyond(conn, roomId, newEdge, older, filter)

            Return rows

        End Using

    End Function


    ' 書き出す範囲（fromId〜toId。両端を含む）。古い順。ExportMaxRows まで（truncated＝まだ続きがあった）。
    ' 中心の行（centerId）は、絞り込みに合わなくても入れる（画面に出ているものと同じにする）
    Public Shared Function GetRange(roomId As Integer, fromId As Long, toId As Long, filter As ReplayFilter,
                                    centerId As Long, ByRef truncated As Boolean) As List(Of LogRow)

        truncated = False

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (@Take) " & RowColumnsSql & " FROM ChatMessages m " & RowJoinsSql &
                " WHERE m.RoomId = @Room AND m.MessageId >= @FromId AND m.MessageId <= @ToId " &
                "   AND (m.MessageId = @Center OR (1 = 1 " & filter.WhereSql() & ")) " &
                " ORDER BY m.MessageId;", conn)

                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = ExportMaxRows + 1
                cmd.Parameters.Add("@Room", SqlDbType.Int).Value = roomId
                cmd.Parameters.Add("@FromId", SqlDbType.BigInt).Value = fromId
                cmd.Parameters.Add("@ToId", SqlDbType.BigInt).Value = toId
                cmd.Parameters.Add("@Center", SqlDbType.BigInt).Value = centerId
                filter.AddParameters(cmd)

                conn.Open()

                Dim rows As List(Of LogRow) = ReadRows(cmd)

                If rows.Count > ExportMaxRows Then
                    truncated = True
                    rows.RemoveAt(rows.Count - 1)
                End If

                Return rows

            End Using
        End Using

    End Function


    ' edgeId の向こう（older なら古いほう）に、絞り込みに合う行がまだあるか
    Private Shared Function ExistsBeyond(conn As SqlConnection, roomId As Integer, edgeId As Long, older As Boolean,
                                         filter As ReplayFilter) As Boolean

        Using cmd As New SqlCommand(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM ChatMessages m WHERE m.RoomId = @Room AND " &
            If(older, "m.MessageId < @Edge", "m.MessageId > @Edge") & filter.WhereSql() & ") THEN 1 ELSE 0 END;", conn)

            cmd.Parameters.Add("@Room", SqlDbType.Int).Value = roomId
            cmd.Parameters.Add("@Edge", SqlDbType.BigInt).Value = edgeId
            filter.AddParameters(cmd)

            Return Convert.ToInt32(cmd.ExecuteScalar()) = 1

        End Using

    End Function


    ' その時刻に部屋にいた人（入室・退室の記録から割り出す）。入室の行を返す（名前・トリップ・IP はその入室の時点の値）
    '
    ' 1人（ブラウザの印）ごとに、atMessageId までの最後の「入室（20）か退室（30番台）」を見て、入室なら「居た」。
    ' 置き換え（33）のすぐあとの入室（20）も、最後が入室なので「居た」になる。
    ' F5・再接続（引き継ぎ）は行を書かないので、数に影響しない。
    ' 【割り出しなので、外れることがある】アプリが起き直したとき・PresenceLookbackDays より前に入った人など。画面にもそう書く
    Public Shared Function GetPresence(roomId As Integer, atMessageId As Long, at As DateTime) As List(Of LogRow)

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                ";WITH ev AS ( " &
                "  SELECT e.MessageId, e.MessageType, " &
                "         ROW_NUMBER() OVER (PARTITION BY e.SenderBrowserId ORDER BY e.MessageId DESC) AS rn " &
                "    FROM ChatMessages e " &
                "   WHERE e.RoomId = @Room AND e.MessageId <= @At AND e.SenderBrowserId IS NOT NULL " &
                "     AND e.CreatedAt >= @Since " &
                "     AND (e.MessageType = 20 OR (e.MessageType >= 30 AND e.MessageType < 40)) ) " &
                "SELECT " & RowColumnsSql & " FROM ev JOIN ChatMessages m ON m.MessageId = ev.MessageId " & RowJoinsSql &
                " WHERE ev.rn = 1 AND ev.MessageType = 20 " &
                " ORDER BY m.MessageId;", conn)

                cmd.Parameters.Add("@Room", SqlDbType.Int).Value = roomId
                cmd.Parameters.Add("@At", SqlDbType.BigInt).Value = atMessageId
                cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = at.AddDays(-PresenceLookbackDays)

                conn.Open()

                Return ReadRows(cmd)

            End Using
        End Using

    End Function


    ' ============================================================
    ' 見た記録
    ' ============================================================

    Public Const AuditSearch As String = "search"
    Public Const AuditView As String = "view"
    Public Const AuditMore As String = "more"
    Public Const AuditExport As String = "export"

    ' 1行書く。**失敗したら例外のまま上へ**（呼ぶ側は、結果を見せずに「混み合っています」を出す）
    '
    ' openPath … 見た記録の画面から「もう一度開く」ための住所（/account/logs で始まる形だけ）。
    '            探したなら、そのときの条件の住所。開いた・さかのぼった・書き出したなら、再生の画面の住所（書き出しも、CSV ではなく再生の画面を開き直す）
    Public Shared Sub WriteAudit(account As AccountStore.AccountRecord, action As String, detail As String,
                                 targetMessageId As Long?, resultCount As Integer?, ipAddress As String,
                                 openPath As String)

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "INSERT INTO ChatLogAudit (AccountId, UserName, Action, Detail, TargetMessageId, ResultCount, IpAddress, OpenPath) " &
                "VALUES (@AccountId, @UserName, @Action, @Detail, @Target, @Count, @Ip, @OpenPath);", conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = account.AccountId
                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, 50).Value = Truncate(If(account.UserName, ""), 50)
                cmd.Parameters.Add("@Action", SqlDbType.VarChar, 10).Value = action
                cmd.Parameters.Add("@Detail", SqlDbType.NVarChar, 2000).Value = If(detail Is Nothing, CObj(DBNull.Value), Truncate(detail, 2000))
                cmd.Parameters.Add("@Target", SqlDbType.BigInt).Value = If(targetMessageId.HasValue, CObj(targetMessageId.Value), DBNull.Value)
                cmd.Parameters.Add("@Count", SqlDbType.Int).Value = If(resultCount.HasValue, CObj(resultCount.Value), DBNull.Value)
                cmd.Parameters.Add("@Ip", SqlDbType.NVarChar, 45).Value = If(ipAddress Is Nothing, CObj(DBNull.Value), Truncate(ipAddress, 45))

                ' 長すぎる住所は、切ると壊れた住所になるので入れない（開き直せないだけ）
                cmd.Parameters.Add("@OpenPath", SqlDbType.NVarChar, 1000).Value =
                    If(openPath Is Nothing OrElse openPath.Length > 1000, CObj(DBNull.Value), openPath)

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    ' 「何をした」の名前（見た記録の画面）
    Public Shared Function AuditActionLabel(action As String) As String

        Select Case action
            Case AuditSearch : Return "探した"
            Case AuditView : Return "開いた"
            Case AuditMore : Return "もっと前・後"
            Case AuditExport : Return "書き出した"
        End Select

        Return If(action, "")

    End Function

    ' 見た記録の画面の「何をした」の選択肢の順
    Public Shared ReadOnly AuditActions As String() = {AuditSearch, AuditView, AuditMore, AuditExport}


    ' 見た記録の1行
    Public NotInheritable Class AuditRow
        Public Property AuditId As Long
        Public Property UserName As String
        Public Property Action As String
        Public Property Detail As String
        Public Property TargetMessageId As Long?
        Public Property ResultCount As Integer?
        Public Property IpAddress As String
        Public Property OpenPath As String
        Public Property CreatedAt As DateTime
    End Class


    ' 見た記録の画面の条件（URL の from・to・act・p と行き来する）
    Public NotInheritable Class AuditQuery

        Public Property FromAt As DateTime?
        Public Property ToAt As DateTime?
        Public Property Action As String       ' Nothing＝すべて
        Public Property Page As Integer = 1

        Public Shared Function FromQueryString(qs As NameValueCollection) As AuditQuery

            Dim q As New AuditQuery With {
                .FromAt = ParseDateTime(qs("from")),
                .ToAt = ParseDateTime(qs("to"))
            }

            Dim act As String = Clean(qs("act"))
            If act IsNot Nothing AndAlso Array.IndexOf(AuditActions, act) >= 0 Then
                q.Action = act
            End If

            Dim page As Integer
            If Integer.TryParse(qs("p"), NumberStyles.None, CultureInfo.InvariantCulture, page) AndAlso page >= 1 AndAlso page <= 100000 Then
                q.Page = page
            End If

            Return q

        End Function

        ' page が 1 以下なら p を付けない。何も無ければ空文字
        Public Function ToQueryString(page As Integer) As String

            Dim parts As New List(Of String)()

            If FromAt.HasValue Then parts.Add("from=" & HttpUtility.UrlEncode(FromAt.Value.ToString(SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture)))
            If ToAt.HasValue Then parts.Add("to=" & HttpUtility.UrlEncode(ToAt.Value.ToString(SearchQuery.DateTimeFormat, CultureInfo.InvariantCulture)))
            If Action IsNot Nothing Then parts.Add("act=" & HttpUtility.UrlEncode(Action))
            If page > 1 Then parts.Add("p=" & page.ToString(CultureInfo.InvariantCulture))

            Return String.Join("&", parts)

        End Function

    End Class


    ' 見た記録を、新しい順に1ページ分。hasMore＝次のページ（もっと古い行）があるか
    Public Shared Function GetAudit(q As AuditQuery, ByRef hasMore As Boolean) As List(Of AuditRow)

        hasMore = False

        Dim list As New List(Of AuditRow)()
        Dim where As New StringBuilder(" WHERE 1 = 1 ")

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand()

                cmd.Connection = conn

                If q.FromAt.HasValue Then
                    where.Append(" AND CreatedAt >= @From ")
                    cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = q.FromAt.Value
                End If

                If q.ToAt.HasValue Then
                    where.Append(" AND CreatedAt < @To ")
                    cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = q.ToAt.Value.AddMinutes(1)
                End If

                If q.Action IsNot Nothing Then
                    where.Append(" AND Action = @Action ")
                    cmd.Parameters.Add("@Action", SqlDbType.VarChar, 10).Value = q.Action
                End If

                cmd.CommandText =
                    "SELECT AuditId, UserName, Action, Detail, TargetMessageId, ResultCount, IpAddress, OpenPath, CreatedAt " &
                    "  FROM ChatLogAudit " & where.ToString() &
                    " ORDER BY AuditId DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;"

                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = (Math.Max(1, q.Page) - 1) * PageSize
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = PageSize + 1

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        Dim row As New AuditRow With {
                            .AuditId = Convert.ToInt64(reader("AuditId")),
                            .UserName = NullableString(reader("UserName")),
                            .Action = NullableString(reader("Action")),
                            .Detail = NullableString(reader("Detail")),
                            .IpAddress = NullableString(reader("IpAddress")),
                            .OpenPath = NullableString(reader("OpenPath")),
                            .CreatedAt = CType(reader("CreatedAt"), DateTime)
                        }

                        If Not Convert.IsDBNull(reader("TargetMessageId")) Then row.TargetMessageId = Convert.ToInt64(reader("TargetMessageId"))
                        If Not Convert.IsDBNull(reader("ResultCount")) Then row.ResultCount = Convert.ToInt32(reader("ResultCount"))

                        list.Add(row)

                    End While
                End Using

            End Using
        End Using

        If list.Count > PageSize Then
            hasMore = True
            list.RemoveAt(list.Count - 1)
        End If

        Return list

    End Function


    ' ============================================================
    ' 守り
    ' ============================================================

    ' 今ログインしている人が、サイト管理者ならその人。違えば Nothing
    ' （WebMethod と CSV の口から呼ぶ。WebMethod は AccountPageBase の OnInit を素通りするので、中で確かめる）
    Public Shared Function CurrentAdministrator(context As HttpContext) As AccountStore.AccountRecord

        Dim account As AccountStore.AccountRecord = AccountSession.Current(context)

        If account IsNot Nothing AndAlso
           String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal) Then
            Return account
        End If

        Return Nothing

    End Function


    ' ============================================================
    ' 小さい道具
    ' ============================================================

    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString
    End Function

    Private Shared Function NullableString(value As Object) As String
        If value Is Nothing OrElse Convert.IsDBNull(value) Then Return Nothing
        Return value.ToString()
    End Function

    Private Shared Function Truncate(value As String, maxLength As Integer) As String
        If value.Length <= maxLength Then Return value
        Return value.Substring(0, maxLength)
    End Function

End Class
