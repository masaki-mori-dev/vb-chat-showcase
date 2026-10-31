Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions

' 管理人が部屋のログを調べる道具の、見せ方の側（データは Admin/LogTool.vb）
'
' 探した結果の行・再生の吹き出し・在室者の一覧の HTML と、CSV を作る。
'
' 【値は、必ずここでエスケープする】名前・本文・部屋の名前・トリップは、どれも利用者が決めた文字。
' 「入口で検査してあるから」を理由に素通しにしないこと（過去のログには、検査を変える前の値も入っている）。
' 属性の中は HtmlAttributeEncode、本文は HtmlEncode。色とアイコンの名前は、決まった形のときだけ style・href に入れる。
'
' 【吹き出しの中身を作るのは、この1か所】最初の表示（LogReplay.aspx）も「もっと前／もっと後」（WebMethod）も、
' ReplayRowsHtml を通す。片方だけ直すと、さかのぼった行だけ見た目が違う、になる。
'
' 【部屋の描画（Scripts/chat-room.js）は使い回していない】あちらは SignalR・入力欄・パネル・音と1つの画面として組んであり、
' 管理の画面に持ってくると、要らないものまで動く。見た目（LINE 風の吹き出し・左にアイコンと名前・右に時刻）だけを
' Content/admin-logs.css で写した。**吹き出しの形を部屋で変えたら、こちらも見直す**（片方だけ直さない）
Public NotInheritable Class LogToolView

    Private Sub New()
    End Sub


    ' 再生の画面の住所（中心の発言の番号。こっそりを混ぜる・この2人だけ）
    Public Const SearchUrl As String = "/account/logs"
    Public Const ReplayUrl As String = "/account/logs/view"
    Public Const ExportUrl As String = "/account/logs/csv"

    Private Shared ReadOnly ColorPattern As New Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled)
    ' アイコンの名前（glasses_male のように _ も入る。ログの IconType 列は NVARCHAR(30)）
    Private Shared ReadOnly IconNamePattern As New Regex("^[A-Za-z0-9_-]{1,30}$", RegexOptions.Compiled)


    Public Const AuditUrl As String = "/account/logs/audit"


    ' 再生の画面の住所（中心の行・こっそりを混ぜるか・この2人だけ）。見た記録の「もう一度開く」にも使う
    Public Shared Function ReplayPathFor(messageId As Long, filter As LogTool.ReplayFilter) As String

        Dim url As String = ReplayUrl & "?m=" & messageId.ToString(CultureInfo.InvariantCulture)

        If filter IsNot Nothing AndAlso filter.IncludePrivate Then url &= "&p=1"
        If filter IsNot Nothing AndAlso filter.HasPair Then url &= "&pair=" & HttpUtility.UrlEncode(filter.PairText())

        Return url

    End Function


    ' 探す画面と見た記録の画面の上の切り替え「探す｜見た記録」。
    ' 見た目はブログの切り替え（Content/Site.css の .blog-tabs）をそのまま使う。here は Page.AppRelativeVirtualPath。
    ' 【戻すのは決まった文字列だけ】呼ぶ側は <%= %> で出す
    Public Shared Function TabsHtml(here As String) As String

        Dim path As String = If(here, "").ToLowerInvariant()
        Dim sb As New StringBuilder()

        sb.Append("<nav class=""blog-tabs"" aria-label=""ログを調べるの切り替え""><ul>")
        AppendTab(sb, "探す", SearchUrl, path = "~/admin/logsearch.aspx")
        AppendTab(sb, "見た記録", AuditUrl, path = "~/admin/logaudit.aspx")
        sb.Append("</ul></nav>")

        Return sb.ToString()

    End Function

    Private Shared Sub AppendTab(sb As StringBuilder, text As String, url As String, current As Boolean)
        sb.Append("<li><a href=""").Append(url).Append("""")
        If current Then sb.Append(" aria-current=""page""")
        sb.Append(">").Append(Enc(text)).Append("</a></li>")
    End Sub


    ' 再生の画面の住所。行がこっそりなら、こっそりを混ぜた形で開く
    Public Shared Function ReplayUrlFor(row As LogTool.LogRow) As String

        Dim url As String = ReplayUrl & "?m=" & row.MessageId.ToString(CultureInfo.InvariantCulture)

        If row.IsPrivate Then
            url &= "&p=1"
        End If

        Return url

    End Function


    ' ============================================================
    ' 部屋の名前・本文
    ' ============================================================

    ' 「雑談（#3）」・閉じた部屋は「雑談（#12・終了）」・RoomId 0 は「サイトへの出入り」
    Public Shared Function RoomLabel(roomId As Integer, roomName As String, isActive As Boolean?) As String

        If roomId = 0 Then
            Return "サイトへの出入り"
        End If

        Dim name As String = If(String.IsNullOrEmpty(roomName), "（名前の無い部屋）", roomName)
        Dim mark As String = "#" & roomId.ToString(CultureInfo.InvariantCulture)

        If isActive.HasValue AndAlso Not isActive.Value Then
            mark &= "・終了"
        End If

        Return name & "（" & mark & "）"

    End Function


    ' 本文を、画面に出す形に（画像・スタンプは印のままでは読めないので言葉に。CSV には生の本文を入れる）
    Public Shared Function DisplayText(row As LogTool.LogRow) As String

        If row.MessageType = 61 Then
            Return If(row.MessageText, "")
        End If

        If Not row.IsTalk Then
            Return ""
        End If

        Dim text As String = If(row.MessageText, "")
        Dim imageName As String = Nothing
        Dim width As Integer
        Dim height As Integer

        If SiteImage.TryParseToken(text, imageName, width, height) Then
            Return If(row.IsPrivate, "（こっそりの画像）", "（画像）")
        End If

        If StampCatalog.ContainsToken(text) Then
            Return "（スタンプ）"
        End If

        Return text

    End Function


    ' 部屋の画像なら、その住所（こっそりの画像は、そのときのブラウザでしか開けない作りなので、出さない）
    Public Shared Function ImageUrl(row As LogTool.LogRow) As String

        If row.MessageType <> 10 Then Return Nothing

        Dim imageName As String = Nothing
        Dim width As Integer
        Dim height As Integer

        If SiteImage.TryParseToken(If(row.MessageText, ""), imageName, width, height) Then
            Return SiteImage.PublicUrlPrefix & imageName
        End If

        Return Nothing

    End Function


    ' お知らせの行の文（「ゆずぽんさん：入室」）。部屋の画面とは違い、退室の理由まで出す（LogTool.KindLabel）
    Public Shared Function NoticeText(row As LogTool.LogRow) As String

        Dim name As String = If(String.IsNullOrEmpty(row.Nickname), "名無し", row.Nickname)
        Dim text As String = name & "さん：" & LogTool.KindLabel(row.MessageType)

        If row.MessageType = 61 AndAlso Not String.IsNullOrEmpty(row.MessageText) Then
            text &= "（" & row.MessageText & "）"
        End If

        Return text

    End Function


    ' 名前の横の印（アカウントID があれば「@名前」。無ければトリップ）。調べる道具なので、トリップ機能が止まっていても出す
    Private Shared Function MarkHtml(row As LogTool.LogRow) As String

        If Not String.IsNullOrEmpty(row.AccountName) Then
            Return "<span class=""account-id logtool-mark""><span class=""visually-hidden"">アカウント </span>@" &
                   Enc(row.AccountName) & "</span>"
        End If

        If Not String.IsNullOrEmpty(row.Trip) Then
            Return "<span class=""trip-code logtool-mark"" data-trip=""" & Attr(row.Trip) & """>" &
                   "<span class=""visually-hidden"">トリップ </span>" & Enc(row.Trip) & "</span>"
        End If

        Return ""

    End Function


    ' ============================================================
    ' 探した結果の一覧
    ' ============================================================

    ' 1件の形（基本ルール A の2＝行数を数で決めない・中身は折り返して字数で切る）。押すと再生の画面へ（その発言を真ん中に）
    '   日付が変わる所に区切りの行（LINE と同じ）。各行は時刻だけ
    '   頭＝時刻・部屋・名前（＋アカウントID かトリップ）。入らなくなったら名前が次の行へ回る（部屋の名前だけは「…」で早めに縮んでよい）
    '   中身＝本文か種類の札。折り返して見せ、ListTextMaxLength（60字）を超えたら「…」（全文は押した先の再生の画面で）
    '   IP:ポートは、中身の最後の行の右に入れば同じ行、入らなければ次の行の右（LINE の時刻と同じ。BodyHtml）
    ' 「この人のほかの記録」は行から外した（再生の画面の小窓の「この人（ブラウザの印）の記録」から）
    Public Shared Function SearchRowsHtml(rows As List(Of LogTool.LogRow)) As String

        Dim sb As New StringBuilder()
        Dim lastDate As String = Nothing

        For Each row As LogTool.LogRow In rows

            Dim dateText As String = ArticleDisplay.DateText(row.CreatedAt)

            If Not String.Equals(dateText, lastDate, StringComparison.Ordinal) Then
                sb.Append("<li class=""logtool-date"">").Append(Enc(dateText)).Append("</li>")
                lastDate = dateText
            End If

            sb.Append("<li class=""logsearch-row")
            If row.IsPrivate Then sb.Append(" logsearch-row--private")
            If Not row.IsTalk Then sb.Append(" logsearch-row--notice")
            sb.Append(""">")

            sb.Append("<a class=""logsearch-hit"" href=""").Append(Attr(ReplayUrlFor(row))).Append(""">")

            ' 1行目
            sb.Append("<span class=""logtool-line"">")
            sb.Append("<time class=""logtool-time"" datetime=""").Append(Attr(row.CreatedAt.ToString("yyyy-MM-dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture)))
            sb.Append(""">").Append(Enc(row.CreatedAt.ToString("HH':'mm", CultureInfo.InvariantCulture))).Append("</time>")
            sb.Append("<span class=""logsearch-room"">").Append(Enc(RoomLabel(row.RoomId, row.RoomName, row.RoomIsActive))).Append("</span>")
            sb.Append("<span class=""logsearch-who""><span class=""logsearch-name"">").Append(Enc(row.Nickname)).Append("</span>")
            sb.Append(MarkHtml(row)).Append("</span>")
            sb.Append("</span>")

            ' 中身
            Dim body As New StringBuilder()

            ' こっそりの札は「→ 宛先」（行の地の紫で、こっそりと分かる。読み上げには「こっそり」を添える）
            If row.IsPrivate Then
                body.Append("<span class=""logtool-private-tag""><span class=""visually-hidden"">こっそり </span>→ ")
                body.Append(Enc(If(row.RecipientName, "（宛先不明）"))).Append("</span>")
            End If

            If row.IsTalk Then
                body.Append(Enc(Clip(OneLine(DisplayText(row)))))
            Else
                body.Append("<span class=""logtool-kind-tag"">").Append(Enc(LogTool.KindLabel(row.MessageType))).Append("</span>")
                If row.MessageType = 61 Then body.Append(Enc(Clip(OneLine(If(row.MessageText, "")))))
            End If

            sb.Append(BodyHtml(body.ToString(), AddressText(row), Nothing))

            sb.Append("</a></li>")

        Next

        Return sb.ToString()

    End Function


    ' 「203.0.113.5:51234」（ポートが無ければ IP だけ。どちらも無ければ「IP なし」）。IPv6 は [ ] で囲む
    Public Shared Function AddressText(row As LogTool.LogRow) As String

        If String.IsNullOrEmpty(row.IpAddress) Then
            Return "IP なし"
        End If

        If Not row.RemotePort.HasValue Then
            Return row.IpAddress
        End If

        Dim port As String = row.RemotePort.Value.ToString(CultureInfo.InvariantCulture)

        If row.IpAddress.Contains(":") Then
            Return "[" & row.IpAddress & "]:" & port
        End If

        Return row.IpAddress & ":" & port

    End Function


    ' 改行や続く空白を1つの空白に（一覧では、改行で縦に伸ばさない）
    Private Shared Function OneLine(text As String) As String
        Return Regex.Replace(If(text, ""), "\s+", " ").Trim()
    End Function


    ' 一覧に出す中身（発言の本文・見た記録の条件）の最大の字数（最大の字数を決めて、そこで切る）。
    ' 幅ではなく字数で切る（幅で切ると、スマホでは早く切れすぎる＝6度目に「期間=2026/10/10 19:27〜」の日付まで切れた）。
    ' 60字＝スマホ（360px）で3行くらい。全文は押した先で読める
    Public Const ListTextMaxLength As Integer = 60

    ' ListTextMaxLength を超えたら切って「…」。絵文字や結合文字の途中では切らない（文字のかたまり＝StringInfo で数える）
    Public Shared Function Clip(text As String) As String

        Dim value As String = If(text, "")
        Dim info As New StringInfo(value)

        If info.LengthInTextElements <= ListTextMaxLength Then
            Return value
        End If

        Return info.SubstringByTextElements(0, ListTextMaxLength) & "…"

    End Function


    ' 一覧の1件の中身の箱（中身は折り返す。右の小さい値は、最後の行の右に入れば同じ行、入らなければ次の行の右）
    '
    ' 【LINE の時刻と同じ作り】中身の最後に、右の値と同じ文字の見えない札（.logtool-side-space）を置き、
    ' 本物（.logtool-side）は箱の右下に重ねる。見えない札が最後の行に入れば、本物はその行の右に乗る。
    ' 入らなければ見えない札だけが次の行へ折り返し、箱が1行伸びて、本物はその行の右に来る（重ならない）。
    ' 見えない札は aria-hidden（読み上げは本物を1回だけ）。
    ' bodyHtml は、呼ぶ側がエスケープ済みの HTML。title は箱に付ける全文（要らなければ Nothing）
    Private Shared Function BodyHtml(bodyHtmlText As String, sideText As String, title As String) As String

        Dim sb As New StringBuilder()

        sb.Append("<span class=""logtool-body""")
        If Not String.IsNullOrEmpty(title) Then sb.Append(" title=""").Append(Attr(title)).Append("""")
        sb.Append(">").Append(bodyHtmlText)
        sb.Append("<span class=""logtool-side-space"" aria-hidden=""true"">").Append(Enc(sideText)).Append("</span>")
        sb.Append("<span class=""logtool-side"">").Append(Enc(sideText)).Append("</span>")
        sb.Append("</span>")

        Return sb.ToString()

    End Function


    ' ============================================================
    ' 再生の吹き出し
    ' ============================================================

    ' 行の HTML（日付が変わる所に区切りの行を挟む）。lastDate は、すでに出ている行の最後の日付（無ければ Nothing）。
    ' centerId の行には印（色付き・真ん中へ）を付ける
    Public Shared Function ReplayRowsHtml(rows As List(Of LogTool.LogRow), centerId As Long, lastDate As String) As String

        Dim sb As New StringBuilder()
        Dim currentDate As String = lastDate

        For Each row As LogTool.LogRow In rows

            Dim dateText As String = ArticleDisplay.DateText(row.CreatedAt)

            If Not String.Equals(dateText, currentDate, StringComparison.Ordinal) Then
                sb.Append("<li class=""logview-date"" data-date=""").Append(Attr(dateText)).Append(""">").Append(Enc(dateText)).Append("</li>")
                currentDate = dateText
            End If

            sb.Append(ReplayRowHtml(row, row.MessageId = centerId))

        Next

        Return sb.ToString()

    End Function


    Private Shared Function ReplayRowHtml(row As LogTool.LogRow, isCenter As Boolean) As String

        Dim sb As New StringBuilder()
        Dim timeText As String = row.CreatedAt.ToString("HH':'mm", CultureInfo.InvariantCulture)

        sb.Append("<li class=""logview-row")
        sb.Append(If(row.IsTalk, " logview-row--talk", " logview-row--notice"))
        If row.IsPrivate Then sb.Append(" logview-row--private")
        If isCenter Then sb.Append(" is-center")
        sb.Append(""" id=""m").Append(row.MessageId.ToString(CultureInfo.InvariantCulture)).Append(""" data-id=""")
        sb.Append(row.MessageId.ToString(CultureInfo.InvariantCulture)).Append(""">")

        ' 押すと小窓（IP・ポート番号・トリップ・アカウントID・ブラウザの印・ミリ秒までの時刻）。中身は data-* から JS が組み立てる
        sb.Append("<button type=""button"" class=""logview-hit"" aria-haspopup=""dialog""")
        sb.Append(DataAttributes(row))
        sb.Append(">")

        If isCenter Then
            sb.Append("<span class=""visually-hidden"">（探していた行）</span>")
        End If

        If row.IsTalk Then

            Dim color As String = If(row.IconColor IsNot Nothing AndAlso ColorPattern.IsMatch(row.IconColor), row.IconColor, "#888888")
            Dim icon As String = If(row.IconType IsNot Nothing AndAlso IconNamePattern.IsMatch(row.IconType), row.IconType, Nothing)

            sb.Append("<span class=""logview-sender"">")
            sb.Append("<span class=""logview-avatar"">")
            If icon IsNot Nothing Then
                sb.Append("<svg viewBox=""0 0 512 512"" aria-hidden=""true"" focusable=""false"" style=""color: ").Append(color).Append(";"">")
                sb.Append("<use href=""#icon-").Append(icon).Append("""></use></svg>")
            End If
            sb.Append("</span>")
            sb.Append("<span class=""logview-name"">").Append(Enc(row.Nickname)).Append("</span>")
            sb.Append(MarkHtml(row))
            sb.Append("</span>")

            sb.Append("<span class=""logview-bubble"" style=""border-color: ").Append(color).Append(";"">")

            If row.IsPrivate Then
                sb.Append("<span class=""logview-private-head"">こっそり：").Append(Enc(row.Nickname)).Append(" → ")
                sb.Append(Enc(If(row.RecipientName, "（宛先不明）"))).Append("</span>")
            End If

            sb.Append("<span class=""logview-text"">").Append(Enc(DisplayText(row))).Append("</span>")
            sb.Append("</span>")

            sb.Append("<time class=""logview-time"">").Append(Enc(timeText)).Append("</time>")

        Else

            sb.Append("<span class=""logview-notice""><time class=""logview-notice-time"">").Append(Enc(timeText)).Append("</time>")
            sb.Append(Enc(NoticeText(row))).Append("</span>")

        End If

        sb.Append("</button></li>")

        Return sb.ToString()

    End Function


    ' 小窓に出す値（JS は textContent で入れる。ここでは属性のエスケープだけ）
    Private Shared Function DataAttributes(row As LogTool.LogRow) As String

        Dim sb As New StringBuilder()

        AddData(sb, "kind", LogTool.KindLabel(row.MessageType))
        AddData(sb, "time", LogTool.FormatMillis(row.CreatedAt))
        AddData(sb, "room", RoomLabel(row.RoomId, row.RoomName, row.RoomIsActive))
        AddData(sb, "name", row.Nickname)
        AddData(sb, "trip", row.Trip)
        AddData(sb, "account", If(row.AccountName Is Nothing, Nothing, "@" & row.AccountName))
        AddData(sb, "account-name", row.AccountName)
        AddData(sb, "ip", row.IpAddress)
        AddData(sb, "port", If(row.RemotePort.HasValue, row.RemotePort.Value.ToString(CultureInfo.InvariantCulture), Nothing))
        AddData(sb, "browser", If(row.SenderBrowserId.HasValue, row.SenderBrowserId.Value.ToString("D"), Nothing))
        AddData(sb, "to-name", If(row.IsPrivate, If(row.RecipientName, "（宛先不明）"), Nothing))
        AddData(sb, "to-browser", If(row.RecipientBrowserId.HasValue, row.RecipientBrowserId.Value.ToString("D"), Nothing))
        AddData(sb, "image", ImageUrl(row))

        If row.IsTalk OrElse row.MessageType = 61 Then
            AddData(sb, "text", DisplayText(row))
        End If

        Return sb.ToString()

    End Function


    Private Shared Sub AddData(sb As StringBuilder, name As String, value As String)
        If Not String.IsNullOrEmpty(value) Then
            sb.Append(" data-").Append(name).Append("=""").Append(Attr(value)).Append("""")
        End If
    End Sub


    ' ============================================================
    ' 在室者
    ' ============================================================

    Public Shared Function PresenceHtml(rows As List(Of LogTool.LogRow)) As String

        Dim sb As New StringBuilder()

        For Each row As LogTool.LogRow In rows

            sb.Append("<li class=""logview-person"">")
            sb.Append("<span class=""logview-person-who""><span class=""logview-person-name"">").Append(Enc(row.Nickname)).Append("</span>")
            sb.Append(MarkHtml(row)).Append("</span>")
            sb.Append("<span class=""logview-person-meta"">入室 ").Append(Enc(LogTool.FormatMinute(row.CreatedAt)))
            sb.Append("・").Append(Enc(AddressText(row))).Append("</span>")

            If row.SenderBrowserId.HasValue Then
                sb.Append("<a class=""logview-person-link"" href=""").Append(Attr(SearchUrl & "?sender=" & row.SenderBrowserId.Value.ToString("D")))
                sb.Append(""">記録を探す<span class=""visually-hidden"">（").Append(Enc(row.Nickname)).Append("）</span></a>")
            End If

            sb.Append("</li>")

        Next

        Return sb.ToString()

    End Function


    ' ============================================================
    ' 見た記録の一覧
    ' ============================================================

    ' 押すと、そのときの探す条件・再生の場所をもう一度開く（7度目に形を作り直した。基本ルール A の2）
    ' （開き直したことは、開いた先の画面が見た記録に残す）。住所の無い行（住所を残す前の行）は、押せない形で出す
    '   日付が変わる所に区切りの行。各行は時刻（秒まで）だけ
    '   頭＝時刻・@アカウントID・［何をした］ …… 右に件数（入らなくなったところで次の行へ）
    '   中身＝条件。折り返して見せ、60字を超えたら「…」（全文は title と、押した先）…… 最後の行の右に小さく管理人の IP
    Public Shared Function AuditRowsHtml(rows As List(Of LogTool.AuditRow)) As String

        Dim sb As New StringBuilder()
        Dim lastDate As String = Nothing

        For Each row As LogTool.AuditRow In rows

            Dim dateText As String = ArticleDisplay.DateText(row.CreatedAt)

            If Not String.Equals(dateText, lastDate, StringComparison.Ordinal) Then
                sb.Append("<li class=""logtool-date"">").Append(Enc(dateText)).Append("</li>")
                lastDate = dateText
            End If

            Dim path As String = SafeOpenPath(row.OpenPath)

            sb.Append("<li class=""logsearch-row logaudit-row"">")

            If path IsNot Nothing Then
                sb.Append("<a class=""logsearch-hit"" href=""").Append(Attr(path)).Append(""">")
            Else
                sb.Append("<div class=""logsearch-hit logaudit-static"">")
            End If

            ' 1行目
            sb.Append("<span class=""logtool-line"">")
            sb.Append("<time class=""logtool-time"" datetime=""").Append(Attr(row.CreatedAt.ToString("yyyy-MM-dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture)))
            sb.Append(""">").Append(Enc(row.CreatedAt.ToString("HH':'mm':'ss", CultureInfo.InvariantCulture))).Append("</time>")
            sb.Append("<span class=""logsearch-who"">@").Append(Enc(row.UserName)).Append("</span>")
            sb.Append("<span class=""logtool-kind-tag"">").Append(Enc(LogTool.AuditActionLabel(row.Action))).Append("</span>")
            sb.Append("<span class=""logtool-count"">")
            If row.ResultCount.HasValue Then
                sb.Append(Enc(row.ResultCount.Value.ToString(CultureInfo.InvariantCulture))).Append(" 件")
            End If
            sb.Append("</span>")
            sb.Append("</span>")

            ' 中身（条件）
            Dim detail As String = If(row.Detail, "")
            Dim clipped As String = Clip(detail)

            sb.Append(BodyHtml(Enc(clipped), "IP " & If(row.IpAddress, "なし"),
                               If(String.Equals(clipped, detail, StringComparison.Ordinal), Nothing, detail)))

            sb.Append(If(path IsNot Nothing, "</a>", "</div>"))
            sb.Append("</li>")

        Next

        Return sb.ToString()

    End Function


    ' 開き直してよい住所か（この道具の画面の住所だけ。javascript: や外のサイトを href にしない）。だめなら Nothing
    Private Shared Function SafeOpenPath(path As String) As String

        If String.IsNullOrEmpty(path) Then Return Nothing

        If path.StartsWith(SearchUrl & "?", StringComparison.Ordinal) OrElse
           String.Equals(path, SearchUrl, StringComparison.Ordinal) OrElse
           path.StartsWith(ReplayUrl & "?", StringComparison.Ordinal) Then
            Return path
        End If

        Return Nothing

    End Function


    ' ============================================================
    ' CSV
    ' ============================================================

    ' CSV（UTF-8・BOM 付き・CRLF・全部の値を "" で囲む）。Excel で開ける形
    '
    ' 【CSV インジェクション対策】値の頭が = + - @ タブ・改行（CR・LF）のときは、頭に ' を付ける（OWASP の勧める形）。
    ' Excel がその値を式として動かさない（=HYPERLINK などで、開いた人の PC から外へ送らせる、を防ぐ）。
    ' 本文は利用者が打ったものなので、どの列も同じに扱う（名前・部屋の名前も利用者が決めた文字）
    Public Shared Function Csv(rows As List(Of LogTool.LogRow)) As String

        Dim sb As New StringBuilder()

        AppendCsvLine(sb, {"番号", "日時", "部屋の番号", "部屋", "種類", "名前", "トリップ", "アカウントID", "本文",
                           "宛先の名前", "宛先のブラウザの印", "IPアドレス", "ポート番号", "ブラウザの印"})

        For Each row As LogTool.LogRow In rows

            AppendCsvLine(sb, {
                row.MessageId.ToString(CultureInfo.InvariantCulture),
                LogTool.FormatMillis(row.CreatedAt),
                row.RoomId.ToString(CultureInfo.InvariantCulture),
                RoomLabel(row.RoomId, row.RoomName, row.RoomIsActive),
                LogTool.KindLabel(row.MessageType),
                row.Nickname,
                row.Trip,
                If(row.AccountName Is Nothing, Nothing, "@" & row.AccountName),
                row.MessageText,
                If(row.IsPrivate, row.RecipientName, Nothing),
                If(row.RecipientBrowserId.HasValue, row.RecipientBrowserId.Value.ToString("D"), Nothing),
                row.IpAddress,
                If(row.RemotePort.HasValue, row.RemotePort.Value.ToString(CultureInfo.InvariantCulture), Nothing),
                If(row.SenderBrowserId.HasValue, row.SenderBrowserId.Value.ToString("D"), Nothing)
            })

        Next

        Return sb.ToString()

    End Function


    Private Shared Sub AppendCsvLine(sb As StringBuilder, values As String())

        For i As Integer = 0 To values.Length - 1
            If i > 0 Then sb.Append(","c)
            sb.Append(CsvCell(values(i)))
        Next

        sb.Append(vbCrLf)

    End Sub


    ' 1つの値（"" で囲む・中の " は "" に・頭が式の文字なら ' を付ける）
    Public Shared Function CsvCell(value As String) As String

        Dim text As String = If(value, "")

        If text.Length > 0 Then
            Select Case text(0)
                Case "="c, "+"c, "-"c, "@"c, ChrW(9), ChrW(13), ChrW(10)
                    text = "'" & text
            End Select
        End If

        Return """" & text.Replace("""", """""") & """"

    End Function


    ' ============================================================
    ' 小さい道具
    ' ============================================================

    Private Shared Function Enc(value As String) As String
        Return HttpUtility.HtmlEncode(If(value, ""))
    End Function

    Private Shared Function Attr(value As String) As String
        Return HttpUtility.HtmlAttributeEncode(If(value, ""))
    End Function

End Class
