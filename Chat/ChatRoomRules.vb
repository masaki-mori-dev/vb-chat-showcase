Imports System.Linq

' ============================================================
' 利用者が作る部屋の、決まり
' ============================================================
'
' 部屋名の検査と、定員の選べる一覧を、ここ1か所に持つ。
'
' 【なぜ、定員の一覧を SiteSettings（DB）に入れないのか】
' これは値ではなく **白リスト＝防御** である。ChatLogReader.AllowedMessageCounts を
' 設定に入れないと決めたのと、まったく同じ理由。
'   1. 丸めの範囲をDBに置くと、DBが読めないときに丸めが効かない
'   2. 管理画面から一覧を広げられる＝定員（＝入室できる人数の上限）を
'      画面から好きな数にできることになり、防御にならない
'   3. 下の DefaultMaxUsers（SiteSettings が持つ）との噛み合わせを、コード側で守れなくなる
'
' 【画面の <option> も、ここが組み立てる】
' 部屋を作る画面（Chat/Lounge.aspx）と、管理人が部屋の設定を変える画面の
' 2か所で同じ選択肢が要る。一覧を持っているここが出せば、2か所に一覧が生まれない
' （それまでは Lounge.aspx のマークアップに12個ベタ書きされていた）。
'
' 【入室の可否は、ここでは決めない】
' 定員に達しているかの判定は ChatHub.JoinRoom（サーバー側の最後の砦）が行う。
' ここは「部屋を作る・設定を変えるときに、受け付けてよい値か」だけを見る
Public NotInheritable Class ChatRoomRules

    Private Sub New()
    End Sub


    ' 部屋の種別（ChatRooms.RoomType）。利用者が作れるのは一時部屋だけ
    Public Const RoomTypeTemporary As String = "Temporary"
    Public Const RoomTypeFixed As String = "Fixed"


    ' 定員として選べる数
    '
    ' 2人から10人までは1人刻み、そこから先は12・15・20。
    ' 固定部屋（既定のルーム）の20人を上限に合わせてある
    Public Shared ReadOnly AllowedMaxUsers As Integer() =
        {2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 20}

    ' 一覧にも設定にも頼れなくなったときの、最後の受け皿
    ' （AllowedMaxUsers に必ず含まれている値にしておくこと）
    Private Const FallbackMaxUsers As Integer = 10


    ' 部屋名の文字数の上限
    '
    ' 【数え方は UTF-16 の文字数】JavaScript の .length、VB の String.Length、
    ' DB の NVARCHAR がどれも同じ単位で数える（絵文字1つは2文字）。
    ' 発言の文字数（SiteSettings.MaxMessageLength）と同じ考え方で、
    ' 書記素単位にすると3か所で数え方を合わせる必要が出る
    '
    ' 【DBの列幅は NVARCHAR(100)】こちらのほうが狭いので、DBの限界は自動的に満たされる。
    ' 画面の maxlength（Chat/Lounge.aspx）も、この数から組み立てている
    '
    ' 【SiteSettings に入れていない】
    ' 画面の maxlength・JS の検査・サーバーの検査の3か所が噛み合っている値で、
    ' 運用で変えたくなる場面が見えない。必要になったら、そのとき移す
    Public Const RoomNameMaxLength As Integer = 20


    ' 新しい部屋を作るときに、最初に選んでおく定員
    '
    ' 値は SiteSettings.RoomDefaultMaxUsers（既定10）が持つ。
    '
    ' 【罠】既定値が一覧（AllowedMaxUsers）に無いと、白リストが無力になる
    ' NormalizeMaxUsers は「一覧に無ければ既定値を返す」作りなので、
    ' 既定値そのものが一覧に無いと、一覧に無い値を返すことになる。
    ' そこでここで一覧との突き合わせを行い、外れていたら受け皿へ落とす
    ' （ChatLogReader.DefaultMessageCount と、まったく同じ形）
    Public Shared ReadOnly Property DefaultMaxUsers As Integer
        Get
            Dim configured As Integer = SiteSettings.RoomDefaultMaxUsers

            If AllowedMaxUsers.Contains(configured) Then
                Return configured
            End If

            ErrorReport.ReportProblem("SiteSettings.RoomDefaultMaxUsers",
                "SiteSettings の RoomDefaultMaxUsers（" & configured &
                "）が、選べる定員の一覧にありません。" & FallbackMaxUsers & " を使います。")

            Return FallbackMaxUsers

        End Get
    End Property


    ' その定員を受け付けてよいか（一覧にあるか）
    '
    ' 【断る。既定値へ落とさない】
    ' 画面は <select> なので、一覧に無い値が来るのは細工したときだけである。
    ' 名前・アイコン・色のような「見た目の値」は既定へ落として通すが
    ' （Cookieが壊れただけの利用者を締め出さないため）、定員は部屋の性質そのものなので、
    ' 勝手に別の数で作らずに断る（「見た目の値は落とす。身元と部屋は断る」）
    Public Shared Function IsAllowedMaxUsers(maxUsers As Integer) As Boolean
        Return AllowedMaxUsers.Contains(maxUsers)
    End Function


    ' 一覧に無い値を、既定値へ丸める（画面の初期選択を決めるときに使う）
    Public Shared Function NormalizeMaxUsers(maxUsers As Integer) As Integer

        If AllowedMaxUsers.Contains(maxUsers) Then
            Return maxUsers
        End If

        Return DefaultMaxUsers

    End Function


    ' 定員の <option> を組み立てる
    ' selectedMaxUsers … 最初に選んでおく定員（selected を付ける対象）
    '
    ' 【ここで独自の一覧を持たないこと】
    ' 上の AllowedMaxUsers から作る。別に一覧を持つと、丸め（IsAllowedMaxUsers）と食い違い、
    ' 「選べるのに、選ぶと断られる」という状態になる
    ' （ChatRoom.aspx.vb の BuildLogCountOptionsHtml と同じ考え方）
    Public Shared Function BuildMaxUsersOptionsHtml(selectedMaxUsers As Integer) As String

        Dim sb As New Text.StringBuilder()

        For Each value As Integer In AllowedMaxUsers

            sb.Append("<option value=""")
            sb.Append(value)
            sb.Append("""")

            If value = selectedMaxUsers Then
                sb.Append(" selected")
            End If

            sb.Append(">")
            sb.Append(value)
            sb.Append("人</option>")

        Next

        Return sb.ToString()

    End Function


    ' 部屋名を整える（前後の空白・改行を落とし、中の空白の並びを1つにまとめる）
    '
    ' 戻り値 … 整えたあとの名前（空になったら空文字）
    '
    ' 【中の改行・タブも、空白1つにまとめる】
    ' 部屋名は一覧の1行に出す値である。改行が混ざると、そこだけ行が高くなって並びが崩れる。
    ' 「前後の空白と改行は取り除く」を、中身にも同じ考え方で広げてある。
    ' 発言の正規化（ChatHub.NormalizeMessage）が3つの改行コードをすべて見ているのと同じで、
    ' \r だけを並べる細工も、ここで1つの空白になる
    '
    ' 【HTMLエスケープは、ここでは行わない】
    ' 出口で行う（RoomList.ascx の <%#: %>、画面の textContent）。
    ' ここでエスケープすると、DBに &amp; のような形で入ってしまう
    ' （「入口の検査だけでは足りない。出口でもエスケープする」は、
    '   出口でやるという話であって、入口で加工するという話ではない）
    Public Shared Function NormalizeRoomName(roomName As String) As String

        If roomName Is Nothing Then
            Return String.Empty
        End If

        ' 空白・改行・タブの並びを、半角空白1つにまとめる
        Dim collapsed As String =
            Text.RegularExpressions.Regex.Replace(roomName, "\s+", " ")

        Return collapsed.Trim()

    End Function


    ' 部屋名を受け付けてよいか
    '
    ' 戻り値 … 断る理由の文言（受け付けてよければ Nothing）。
    '           TripCode.ValidateKey と同じ形にしてある
    '
    ' 【切り詰めない。断る】
    ' 20文字を超えたら、黙って切るのではなく断る。
    ' 名前欄（12文字で切り詰める）と向きが違うのは、あちらが
    ' 「Cookieが壊れただけの利用者を締め出さない」ための処置で、
    ' こちらは利用者がその場で打ち直せる入力だからである。
    ' 打ったつもりと違う名前の部屋が立つほうが、分かりにくい
    Public Shared Function ValidateRoomName(normalizedName As String) As String

        If String.IsNullOrEmpty(normalizedName) Then
            Return "部屋名を入れてください。"
        End If

        If normalizedName.Length > RoomNameMaxLength Then
            Return "部屋名は" & RoomNameMaxLength & "文字までにしてください。"
        End If

        Return Nothing

    End Function

End Class
