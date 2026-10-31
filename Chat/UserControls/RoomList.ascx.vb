Imports System.Data
Imports System.Data.SqlClient
Imports System.Configuration

Public Class RoomList
    Inherits System.Web.UI.UserControl

    ' 「?」のボタンと小窓（文は Core/HelpTexts.vb の表の1か所）。markup からは <%= HelpButton("鍵") %> の1行だけ。
    ' 【markup から HelpTexts を直接呼ばない】別のコンパイル単位で、実行したときだけ500になりうる（ここを経由する）
    Protected Shared Function HelpButton(key As String) As String
        Return HelpTexts.ButtonHtml(key)
    End Function

    ' HTML側（RoomList.ascx）のRepeaterから参照できるように、Protectedで宣言
    Protected fixedRooms As DataTable
    Protected userRooms As DataTable

    Protected hasNickname As Boolean

    ' === 部屋の様子 ===
    '
    ' 最初の表示は、ここ（サーバー）で作る。ラウンジの画面は、そのあと SignalR に繋いで
    ' 丸ごとを受け取り直し、以後は差分でその場で書き換える（Scripts/lounge-live.js）。
    '
    ' 【繋がるまで数字が出ない形にしない】
    ' 画面で作る形にすると、繋がるまでの間「?」や空欄になり、繋がった瞬間に書き換わる
    ' （アイコンの選択肢が0個から一気に並んで、画面が1188px跳ねたのと同じ形）。
    '
    ' 【DBへの問い合わせは増えない】在室者はハブのメモリ上にしか無い（ChatHub.GetLoungeSnapshot）

    ' 部屋ID → 今の在室者（入った順。居ない部屋は入っていない）
    Private occupantsByRoom As New Dictionary(Of Integer, List(Of LoungeOccupantInfo))

    ' 部屋ID → 管理人の部屋の中の番号（居ない部屋は入っていない）
    Private ownerByRoom As New Dictionary(Of Integer, Integer)

    ' この画面を開いた人が、今入っている部屋（居なければ 0）。その部屋のボタンは「入室中」
    Private myRoomId As Integer

    ' 部屋に居る人の合計（一覧に出ていない部屋の人も含む）
    Protected roomsTotal As Integer

    ' ラウンジに居る人数（この画面を開いた本人を含む。数え方は ChatHub.Lounge.vb の CurrentLoungeBrowsers）
    Protected loungeTotal As Integer

    ' ラウンジに居る人の顔（来た順。この画面を開いた本人を含む）
    Private loungeMembers As List(Of LoungeMemberInfo)

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        ' 名前が決まっているか（Cookieが揃っていて、名前の札も有効か）をチェック
        '
        ' 【、札も見るようにした】判定はラウンジ・部屋と同じ1か所へ寄せる
        ' （ChatCommon.IsChatProfileComplete）。このコントロールはラウンジにしか置いていないので、
        ' ここが False になることは今は無い（ラウンジは ChatPageBase が先に弾く）が、
        ' 判定の形が2つあると、どちらかだけを直す事故が起きる
        hasNickname = ChatCommon.IsChatProfileComplete()

        ' ChatRoomsテーブルから、部屋の一覧を読み込む
        LoadRooms()

        ' 在室の様子を、ハブのメモリから読む（DBは引かない）
        LoadOccupancy()

        ' 取得したデータを、HTML側のRepeaterに反映させる
        ' （これが無いと、fixedRooms・userRoomsにデータは入っても、画面には何も表示されない）
        FixedRoomsRepeater.DataBind()
        UserRoomsRepeater.DataBind()



    End Sub

    ' ChatRoomsテーブルを読み込んで、固定部屋・ユーザー作成部屋に分ける処理
    Private Sub LoadRooms()

        ' 接続文字列はWeb.configのconnectionStringsセクションから取得
        ' 接続文字列名は、実際のWeb.configの名前に合わせて調整してください
        Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        Using conn As New SqlConnection(connStr)

            ' RoomTypeが'Fixed'（固定部屋）か、それ以外（ユーザー作成部屋）かで、
            ' 2回に分けてSELECTする（見出しを分けて表示したいため）
            ' IsActive = 1 で絞り込み、終了した部屋は一覧に表示しない
            ' （終了した部屋も、ログ表示のために行だけはテーブルに残っている）
            Dim sql As String = "SELECT RoomId, RoomName, MaxUsers, IsKnockEnabled, " &
                                 "CASE WHEN PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword " &
                                 "FROM ChatRooms WHERE RoomType = @RoomType AND IsActive = 1 ORDER BY RoomId"

            Using cmd As New SqlCommand(sql, conn)

                conn.Open()

                ' ①固定部屋を取得
                cmd.Parameters.AddWithValue("@RoomType", "Fixed")
                Using adapter As New SqlDataAdapter(cmd)
                    fixedRooms = New DataTable()
                    adapter.Fill(fixedRooms)
                End Using

                ' ②ユーザー作成部屋を取得（一時部屋・永続部屋、両方まとめて）
                cmd.Parameters.Clear()
                cmd.Parameters.AddWithValue("@RoomType", "Temporary")
                Using adapter As New SqlDataAdapter(cmd)
                    userRooms = New DataTable()
                    adapter.Fill(userRooms)
                End Using

            End Using

        End Using

    End Sub

    ' 在室の様子（部屋ごとの人数・部屋の合計・ラウンジの人数）を読む
    '
    ' 【この画面を開いた本人を、ラウンジの人数に入れておく】
    ' 本人はこのあと SignalR に繋ぎ、繋いだ時点で数に入る。
    ' 先に入れておかないと、開いた直後に人数が1つ増えて見える。
    ' このコントロールはラウンジにしか置いていない。ラウンジ以外（繋がない画面）に置くときは、
    ' 本人を入れないよう引数を Nothing にすること
    Private Sub LoadOccupancy()

        ' 本人の顔も、同じ理由で先に入れておく。
        ' 【名前は札（ChatNameToken）から読む】。ラウンジのプロフィールの枠
        ' （Lounge.aspx.vb）と同じ出どころにして、枠と顔の一覧で名前が食い違わないようにする。
        ' 名前の Cookie（ChatNickname）は JS から書き換えられるので、名前の出どころにしない。
        ' 検査（名前の長さ・アイコンの種類・色）はハブの側で通す
        '
        ' 【アカウントID を表示している人は、トリップの代わりにユーザー名】
        ' 決めるのは Chat/ChatAccountLink.vb（ラウンジのプロフィールの枠と同じ判定。1回の要求で DB は1回）。
        ' どちらか一方にするのは、ハブの側（CreateLoungeProfile）がもう一度やる
        Dim accountLink As ChatAccountLink.LinkResult = ChatAccountLink.Resolve(Context)

        Dim snapshot As LoungeSnapshot = ChatHub.GetLoungeSnapshot(
            CookieValue("ChatBrowserId"),
            NameToken.ReadNickname(Request),
            CookieValue("ChatIconType"),
            CookieValue("ChatIconColor"),
            If(accountLink.IsShowing, Nothing, TripCode.FromRequest(Request)),
            accountLink.UserName)

        roomsTotal = 0

        For Each room In snapshot.Rooms
            occupantsByRoom(room.RoomId) = room.Occupants
            ownerByRoom(room.RoomId) = room.OwnerLocalId
            roomsTotal += room.Occupants.Count
        Next

        myRoomId = snapshot.MyRoomId

        loungeTotal = snapshot.LoungeCount
        loungeMembers = snapshot.Members

    End Sub

    Private Function CookieValue(name As String) As String
        Dim cookie As HttpCookie = Request.Cookies(name)
        Return If(cookie Is Nothing, Nothing, cookie.Value)
    End Function

    ' その部屋の今の在室者（居なければ空）
    Private Function OccupantsOf(roomId As Object) As List(Of LoungeOccupantInfo)

        Dim list As List(Of LoungeOccupantInfo) = Nothing

        If roomId IsNot Nothing AndAlso Not IsDBNull(roomId) AndAlso
           occupantsByRoom.TryGetValue(CInt(roomId), list) Then
            Return list
        End If

        Return New List(Of LoungeOccupantInfo)

    End Function

    ' その部屋の今の在室人数（Repeater の行から呼ぶ）
    Protected Function OccupantCount(roomId As Object) As Integer
        Return OccupantsOf(roomId).Count
    End Function

    ' 満室か（Repeater の行から呼ぶ）
    '
    ' 【これは親切であって、防御ではない】断るのはハブの JoinRoom の定員の判定である。
    ' ここは入室ボタンを灰色にするだけ（そのあとの変化は Scripts/lounge-live.js が付け外しする）
    Protected Function IsFull(roomId As Object, maxUsers As Object) As Boolean

        If maxUsers Is Nothing OrElse IsDBNull(maxUsers) Then
            Return False
        End If

        Dim max As Integer = CInt(maxUsers)

        Return max > 0 AndAlso OccupantCount(roomId) >= max

    End Function

    ' その部屋の在室者の一覧（<ul> ごと）を組み立てる（Repeater の行から呼ぶ）
    '
    ' 【同じ形を、画面の側でも作っている】Scripts/lounge-live.js の buildMemberItem。
    ' 最初の表示はここ（繋がるまで顔が出ない形にしない）、そのあとの変化は画面の側。
    ' **片方の形を変えたら、もう片方も直すこと**（class 名・属性・並び）
    '
    ' 【エスケープ】名前はそのまま HTML に入れず、必ず HtmlEncode を通す。
    ' アイコンの種類と色は入室の時点で検査してある（ChatProfileValidator）が、
    ' 「入口の検査だけでは足りない。出口でもエスケープする」の決まりどおり、属性にも通す
    Protected Function MembersHtml(roomId As Object, roomName As Object) As String

        Dim list As List(Of LoungeOccupantInfo) = OccupantsOf(roomId)
        Dim sb As New System.Text.StringBuilder()

        ' 読み上げソフトには「〇〇にいる人」という一覧として伝える。
        ' 居ない部屋は hidden（空の一覧を読ませない）。画面の側が付け外しする
        sb.Append("<ul class=""room-members"" aria-label=""")
        sb.Append(HttpUtility.HtmlAttributeEncode(Convert.ToString(roomName) & "にいる人"))
        sb.Append("""")

        If list.Count = 0 Then
            sb.Append(" hidden=""hidden""")
        End If

        sb.Append(">")

        Dim ownerLocalId As Integer = 0

        If roomId IsNot Nothing AndAlso Not IsDBNull(roomId) Then
            ownerByRoom.TryGetValue(CInt(roomId), ownerLocalId)
        End If

        ' 管理人を先頭に。ほかの人は入った順のまま。
        ' lounge-live.js の orderOwnerFirst と同じ並び（片方だけ変えないこと。繋いだあとの描き直しで並びが跳ねる）
        Dim ordered As New List(Of LoungeOccupantInfo)(list.Count)

        For Each o In list
            If ownerLocalId > 0 AndAlso o.LocalId = ownerLocalId Then
                ordered.Insert(0, o)
            Else
                ordered.Add(o)
            End If
        Next

        For Each o In ordered
            sb.Append(MemberItemHtml(o.LocalId, o.Nickname, o.IconType, o.IconColor, o.Trip, o.AccountName,
                                     isOwner:=(ownerLocalId > 0 AndAlso o.LocalId = ownerLocalId)))
        Next

        sb.Append("</ul>")

        Return sb.ToString()

    End Function

    ' ラウンジに居る人の一覧（<ul> ごと）を組み立てる（RoomList.ascx から呼ぶ）
    '
    ' 1人ぶんの形は、部屋の在室者とまったく同じ（MemberItemHtml を共用）。
    ' data-local-id には、ラウンジの中の番号（MemberId）が入る。
    ' 本人の顔は、まだ番号が無いので 0（繋いだあとの丸ごとで、画面が一覧ごと置き換える）。
    ' 見出し（「ラウンジ」。見える h3 にした）は RoomList.ascx の側にあり、一覧はそれを aria-labelledby で指す。
    ' 誰も居なければ一覧だけ hidden（見出しは残す。部屋の在室者の一覧と同じ。画面の側も付け外しする）
    Protected Function LoungeMembersHtml() As String

        Dim sb As New System.Text.StringBuilder()

        sb.Append("<ul class=""room-members lounge-members"" id=""loungeMembers"" aria-labelledby=""loungeMembersHeading""")

        If IsLoungeEmpty Then
            sb.Append(" hidden=""hidden""")
        End If

        sb.Append(">")

        If loungeMembers IsNot Nothing Then
            For Each m In loungeMembers
                sb.Append(MemberItemHtml(m.MemberId, m.Nickname, m.IconType, m.IconColor, m.Trip, m.AccountName, isOwner:=False))
            Next
        End If

        sb.Append("</ul>")

        Return sb.ToString()

    End Function

    ' ラウンジに誰も居ないか（一覧を隠すため）。
    ' 画面を開いた本人は入っているので、ふつうは居る。空になるのは、本人が別のタブで部屋に居るときなど
    Protected ReadOnly Property IsLoungeEmpty As Boolean
        Get
            Return loungeMembers Is Nothing OrElse loungeMembers.Count = 0
        End Get
    End Property

    ' パスワード付きの部屋なら、部屋名の前に置く鍵の印（Repeater の行から呼ぶ）
    '
    ' hasPassword … SELECT の HasPassword（0 か 1）
    '
    ' 【同じ形を、画面の側でも作っている】Scripts/lounge-live.js の buildRoomItem（部屋ができた差分）。
    ' **片方の形を変えたら、もう片方も直すこと**。
    '
    ' 【絵は、その場の SVG】利用者のアイコンのスプライト（Content/icon-sprite.svg）には混ぜない
    ' （あれは利用者が選ぶアイコンの一覧で、スプライトから選択肢を作っている。部屋の歯車と同じ扱い）。
    ' 読み上げソフトには、絵ではなく「パスワード付き：」という文字で伝える
    Protected Shared Function LockMarkHtml(hasPassword As Object) As String

        If hasPassword Is Nothing OrElse IsDBNull(hasPassword) OrElse Convert.ToInt32(hasPassword) = 0 Then
            Return String.Empty
        End If

        Return LockMarkMarkup

    End Function

    ' ノックを受け付ける部屋なら、.room-item に付ける属性（Repeater の行から呼ぶ）
    '
    ' 画面（Scripts/lounge-live.js）は、この印を見て、満室のときの入室ボタンを「ノック」にする。
    ' 部屋ができた差分（roomCreated）で足す部屋には、lounge-live.js の buildRoomItem が同じ属性を付ける
    Protected Shared Function KnockAttributeHtml(knockEnabled As Object) As String

        If Not IsTrue(knockEnabled) Then
            Return String.Empty
        End If

        Return " data-knock-enabled=""1"""

    End Function


    ' 入室ボタン（Repeater の行から呼ぶ。マークアップから移した）
    '
    ' 3つの形がある（「入室中」を足して4つ）。
    '   自分が入っている部屋           … 「入室中」＋ data-mode="mine"（押すと、このタブで開き直す）
    '   空いている                     … 「入室」
    '   満室・ノックを受け付けない部屋 … 「入室」＋ aria-disabled（灰色。押すと「満員です」）
    '   満室・ノックを受け付ける部屋   … 「ノック」＋ data-mode="knock"（灰色にしない。押すとノック）
    ' 4つ目の「返事待ち」は、画面の側だけが持つ（サーバーは知らない。lounge-live.js の applyEnterButton）。
    '
    ' 【同じ形を、画面の側でも作っている】Scripts/lounge-live.js の applyEnterButton（人数が変わるたびに付け替える）。
    ' **片方の形を変えたら、もう片方も直すこと**。
    '
    ' 【これは親切であって、防御ではない】満室で断るのも、ノックを受け付けるかを決めるのも、ハブ
    ' （ChatHub.JoinRoom ／ ChatHub.KnockRoom）である
    Protected Function EnterButtonHtml(roomId As Object, maxUsers As Object, knockEnabled As Object) As String

        Dim id As String = HttpUtility.HtmlAttributeEncode(
            Convert.ToString(roomId, Globalization.CultureInfo.InvariantCulture))
        Dim full As Boolean = IsFull(roomId, maxUsers)
        Dim knock As Boolean = full AndAlso IsTrue(knockEnabled)

        ' 自分が（別のタブで）入っている部屋は「入室中」。押すと、そのままこのタブで開き直す
        Dim mine As Boolean = myRoomId > 0 AndAlso roomId IsNot Nothing AndAlso Not IsDBNull(roomId) AndAlso
                              CInt(roomId) = myRoomId

        Dim sb As New System.Text.StringBuilder()

        sb.Append("<a href=""#"" class=""btn-enter"" data-room-id=""")
        sb.Append(id)
        sb.Append("""")

        If mine Then
            sb.Append(" data-mode=""mine"">入室中</a>")
        ElseIf knock Then
            sb.Append(" data-mode=""knock"">ノック</a>")
        ElseIf full Then
            sb.Append(" aria-disabled=""true"">入室</a>")
        Else
            sb.Append(">入室</a>")
        End If

        Return sb.ToString()

    End Function


    ' DB の BIT（Boolean）・0/1 を、真偽として読む（読めなければ False）
    Private Shared Function IsTrue(value As Object) As Boolean

        If value Is Nothing OrElse IsDBNull(value) Then
            Return False
        End If

        Try
            Return Convert.ToBoolean(value)
        Catch ex As FormatException
            ' 画面の印なので、読めなければ「受け付けない」側に倒す（知らせない。DB の BIT なので、ふつうは来ない）
            Return False
        End Try

    End Function


    ' 鍵の絵（16×16の方眼。体と、つるの形。つるの内側は evenodd で抜く）
    ' 【Friend】「?」の小窓の絵（Core/HelpTexts.vb）も、この同じ絵を使う
    Friend Const LockMarkMarkup As String =
        "<svg class=""room-lock-icon"" viewBox=""0 0 16 16"" aria-hidden=""true"" focusable=""false"">" &
        "<path fill-rule=""evenodd"" d=""M5 7V5a3 3 0 0 1 6 0v2h.5A1.5 1.5 0 0 1 13 8.5v5A1.5 1.5 0 0 1 11.5 15h-7A1.5 1.5 0 0 1 3 13.5v-5A1.5 1.5 0 0 1 4.5 7H5zm1.5 0h3V5a1.5 1.5 0 0 0-3 0v2z""></path>" &
        "</svg><span class=""visually-hidden"">パスワード付き：</span>"


    ' 1人ぶん（<li>）。作り直した
    '
    ' 部屋の在室者（MembersHtml）とラウンジに居る人（LoungeMembersHtml）の両方で使う。
    ' id … その一覧の中の番号（部屋＝LocalId、ラウンジ＝MemberId）。data-local-id に入る
    '
    '   [アイコン 名前…]            ← 横並びの1組。幅は全員同じ（CSS）。あふれた名前は「…」
    '   （押すと、下に）名前の全体
    '                   トリップ    ← 付けている人だけ
    '
    ' 【全員を押せるボタンにした】（12-2 はトリップを付けている人だけだった）
    ' 名前を「…」で切るようにしたので、切れた人の名前を読む場所が要る。
    ' 押すと名前の全体（とトリップ）が出るので、誰を押しても何かが起きる＝ボタンにしてよい。
    ' トリップを付けている人は、今までどおりアイコンの隅の印で分かる
    '
    ' 【読み上げ】名前は見た目だけ「…」で切っていて、文字は最後まで入っている。
    ' 読み上げソフトは切られていない名前を読むので、押したあとに出る「名前の全体」は aria-hidden にしてある（2度読ませない）
    '
    ' isOwner … その部屋の管理人なら True。「管理人」の札（アイコンの前。読み上げは名前のあとに「（管理人）」）。
    '           ラウンジに居る人（部屋の外）は、いつも False
    '
    ' accountName … アカウントID を表示している人なら、そのユーザー名（無ければ Nothing）。
    '   トリップとは、どちらか一方。形は次のとおり（lounge-live.js の buildMemberItem と同じ形にしてあること）
    '     ・アイコンの隅の印 … トリップと同じ場所に、形の違う印（四角）。読み上げは「（アカウントあり）」
    '     ・押すと出る部分 … 名前の全体の下に「@ユーザー名」（ここは文字だけ。1人ぶんがボタンなので、中にリンクは置けない）
    '     ・ボタンのすぐ後ろ … 公開のプロフィールへのリンク（開いている間だけ出る。新しいタブ）
    Private Shared Function MemberItemHtml(id As Integer, nickname As String, iconType As String,
                                           iconColor As String, trip As String, accountName As String,
                                           isOwner As Boolean) As String

        Dim hasAccount As Boolean = Not String.IsNullOrEmpty(accountName)
        Dim hasTrip As Boolean = Not hasAccount AndAlso Not String.IsNullOrEmpty(trip)
        Dim sb As New System.Text.StringBuilder()
        Dim name As String = HttpUtility.HtmlEncode(nickname)

        sb.Append(If(isOwner, "<li class=""room-member room-member--owner"" data-local-id=""", "<li class=""room-member"" data-local-id="""))
        sb.Append(id.ToString(Globalization.CultureInfo.InvariantCulture))
        sb.Append(""">")

        sb.Append("<button type=""button"" class=""room-member-btn"" aria-expanded=""false"">")

        sb.Append("<span class=""room-member-main"">")

        If isOwner Then
            ' 札はアイコンの前（開発者の指定：名前よりも管理人の印を前に置く）。
            ' 文字は読ませず、名前のあとの「（管理人）」を読ませる（lounge-live.js の buildMemberItem と同じ形）
            sb.Append("<span class=""owner-badge room-member-owner"" aria-hidden=""true"">管理人</span>")
        End If

        sb.Append("<span class=""room-member-icon""><svg viewBox=""0 0 512 512"" aria-hidden=""true"" style=""color: ")
        sb.Append(HttpUtility.HtmlAttributeEncode(iconColor))
        sb.Append(";""><use href=""#icon-")
        sb.Append(HttpUtility.HtmlAttributeEncode(iconType))
        sb.Append("""></use></svg>")

        If hasTrip Then
            sb.Append("<span class=""room-member-trip-mark"" aria-hidden=""true""></span>")
        ElseIf hasAccount Then
            sb.Append("<span class=""room-member-trip-mark room-member-trip-mark--account"" aria-hidden=""true""></span>")
        End If

        sb.Append("</span>")
        sb.Append("<span class=""room-member-name"">")
        sb.Append(name)
        sb.Append("</span>")

        If isOwner Then
            ' 読み上げは「名前（管理人）」の順のまま（札は見た目だけ前へ移した。部屋の参加者一覧と同じ読ませ方）
            sb.Append("<span class=""visually-hidden"">（管理人）</span>")
        End If

        sb.Append("</span>")

        If hasTrip Then
            sb.Append("<span class=""visually-hidden"">（トリップあり）</span>")
        ElseIf hasAccount Then
            sb.Append("<span class=""visually-hidden"">（アカウントあり）</span>")
        End If

        sb.Append("<span class=""room-member-detail"" hidden=""hidden"">")
        sb.Append("<span class=""room-member-fullname"" aria-hidden=""true"">")
        sb.Append(name)
        sb.Append("</span>")

        If hasTrip Then
            sb.Append("<span class=""trip-code room-member-trip"" data-trip=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(trip))
            sb.Append(""">")
            sb.Append(HttpUtility.HtmlEncode(trip))
            sb.Append("</span>")
        ElseIf hasAccount Then
            sb.Append("<span class=""account-id room-member-account""><span class=""visually-hidden"">アカウント </span>")
            sb.Append(HttpUtility.HtmlEncode(ChatAccountLink.DisplayText(accountName)))
            sb.Append("</span>")
        End If

        sb.Append("</span>")
        sb.Append("</button>")

        If hasAccount Then
            ' 公開のプロフィールへ（新しいタブ）。開いていない間は hidden（lounge-live.js が、ボタンと一緒に出し入れする）
            sb.Append("<a class=""room-member-profile"" target=""_blank"" rel=""noopener"" hidden=""hidden"" href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(ChatAccountLink.ProfileUrl(accountName)))
            sb.Append(""">プロフィール<span class=""visually-hidden"">（新しいタブで開きます）</span></a>")
        End If

        sb.Append("</li>")

        Return sb.ToString()

    End Function

End Class
