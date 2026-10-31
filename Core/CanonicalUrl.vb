' 住所は1つのページに1つ（開発者が決めた「住所のルール」）
'
' 【何をするか】ルーティングで決まった住所があるページを、実物の住所（/Accounts/Login.aspx など）で
' 開かれたときに、正しい住所（/account/login）へ 301 で送る。正しい住所が決められないもの
' （/Articles/ArticleDetail.aspx のように、ルートの値＝誰の・どの記事か、が無いと開けないページ）は 404 にする。
' 呼ぶのは Global.asax の Application_BeginRequest の1か所だけ（画面ごとに書き写さない＝基本ルールの8）。
'
' 【触らないもの】
'   ・POST（ポストバック・WebMethod・画像の受け口）……止めない・飛ばさない。GET と HEAD だけを見る
'   ・WebMethod の住所（/Chat/Lounge.aspx/EnterRoom）…….aspx のあとに続きがあるので、下の表に当たらない
'   ・表に無い .aspx（KeepAlive・LeaveBeacon・画像の受け口・Admin\ の道具）……直接開くのが正しい形。何もしない
'   ・ルートの住所そのもの（/account/login）……BeginRequest の時点ではまだ .aspx に結び付いていないので当たらない
'
' 【行き先はサーバーが知っている値だけで組む】行き先のパスは下の表に書いた決まった文字列で、
' 要求から取るのは「表に書いたクエリ文字列の名前の値」だけ（UrlEncode して付け直す）。
' クエリ文字列をそのまま行き先にしない＝よそのサイトへ飛ばせる口（オープンリダイレクト）にならない。
' 表に無い名前（?done= など）は落とす。
'
' 【RawUrl で見る理由】トップページ（/）は、IIS が既定のドキュメントとして Default.aspx を中で開く。
' そのとき Request.Path が /Default.aspx に書き換わる場合があっても、RawUrl は利用者が打った「/」のままなので、
' 「/ を / へ 301」の無限ループにならない
'
' 【404 の画面】ここで 404 にするものは、中で NotFound.aspx に差し替える（住所は変わらない）。
' ほかの「無い住所」は、ASP.NET の 404 なら Global.asax の Application_Error が、静的ファイル・フォルダの住所なら
' 本番の IIS の httpErrors（Web.Release.config）が、同じ NotFound.aspx を出す
Public NotInheritable Class CanonicalUrl

    Private Sub New()
    End Sub

    ' 1ページぶんの決まり。Path が Nothing なら 404
    Private NotInheritable Class Rule
        Public ReadOnly Path As String
        Public ReadOnly Keys As String()
        Public Sub New(path As String, ParamArray keys As String())
            Me.Path = path
            Me.Keys = keys
        End Sub
    End Class

    ' 実物の住所（アプリから見た相対パス・小文字）→ 正しい住所
    '
    ' 【ページやルートを足したら、ここにも1行足すこと】足し忘れても壊れはしない（.aspx でも開けるまま、になるだけ）。
    ' 足し忘れは発行後の確かめの道具の【住所のルール】で見る
    Private Shared ReadOnly Rules As New Dictionary(Of String, Rule)(StringComparer.OrdinalIgnoreCase) From {
        {"~/default.aspx", New Rule("/")},
        {"~/contacts/contact.aspx", New Rule("/contact")},
        {"~/chat/lounge.aspx", New Rule("/chat/lounge")},
        {"~/chat/chatroom.aspx", New Rule("/chat/room")},
        {"~/chat/logout.aspx", New Rule("/chat/logout")},
        {"~/chat/privateimage.aspx", New Rule("/chat/private-image", "n")},
        {"~/chat/mychatlogs.aspx", New Rule("/account/chatlogs", "p")},
        {"~/chat/mychatlogview.aspx", New Rule("/account/chatlogs/view", "s", "p")},
        {"~/accounts/home.aspx", New Rule("/account")},
        {"~/accounts/login.aspx", New Rule("/account/login", AccountReturnUrl.QueryName)},
        {"~/accounts/logout.aspx", New Rule("/account/logout")},
        {"~/accounts/settings.aspx", New Rule("/account/settings")},
        {"~/accounts/profile.aspx", New Rule("/account/profile")},
        {"~/accounts/register.aspx", New Rule("/account/register")},
        {"~/accounts/verify.aspx", New Rule("/account/verify", AccountRegistration.TokenQueryName)},
        {"~/accounts/forgot.aspx", New Rule("/account/forgot")},
        {"~/accounts/reset.aspx", New Rule("/account/reset", AccountReset.TokenQueryName)},
        {"~/accounts/recover.aspx", New Rule("/account/recover", "key")},
        {"~/accounts/setup.aspx", New Rule("/account/setup", "key")},
        {"~/articles/manage.aspx", New Rule("/account/articles", "view", "category")},
        {"~/articles/edit.aspx", New Rule("/account/articles/edit", "id")},
        {"~/articles/categories.aspx", New Rule("/account/categories")},
        {"~/articles/blogsettings.aspx", New Rule("/account/blog/settings")},
        {"~/notices/manage.aspx", New Rule("/account/notices")},
        {"~/notices/noticelist.aspx", New Rule("/news", "p")},
        {"~/accounts/sitedisplay.aspx", New Rule("/account/display")},
        {"~/admin/logsearch.aspx", New Rule("/account/logs", "q", "from", "to", "room", "name", "trip", "ip", "acct", "kind", "sender", "p", "all")},
        {"~/admin/logreplay.aspx", New Rule("/account/logs/view", "m", "p", "pair")},
        {"~/admin/logexport.aspx", New Rule("/account/logs/csv", "m", "from", "to", "p", "pair")},
        {"~/admin/logaudit.aspx", New Rule("/account/logs/audit", "from", "to", "act", "p")},
        {"~/articles/articledetail.aspx", New Rule(Nothing)},
        {"~/articles/accounttop.aspx", New Rule(Nothing)},
        {"~/articles/categorylist.aspx", New Rule(Nothing)},
        {"~/accounts/publicprofile.aspx", New Rule(Nothing)}}


    ' 当たったら応答を決めて True（呼び元は何もしなくてよい。要求はここで終わる）
    Public Shared Function TryHandle(context As HttpContext) As Boolean

        Dim request As HttpRequest = context.Request

        If Not (String.Equals(request.HttpMethod, "GET", StringComparison.Ordinal) OrElse
                String.Equals(request.HttpMethod, "HEAD", StringComparison.Ordinal)) Then
            Return False
        End If

        ' 利用者が打った住所のパスの部分（クエリ文字列の手前まで）
        Dim raw As String = If(request.RawUrl, "")
        Dim q As Integer = raw.IndexOf("?"c)
        Dim rawPath As String = If(q >= 0, raw.Substring(0, q), raw)

        If Not rawPath.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If

        ' %41 のような書き方でも同じページに着くので、ほどいてから表を引く
        Dim decoded As String
        Try
            decoded = HttpUtility.UrlDecode(rawPath)
        Catch
            Return False
        End Try

        Dim appRelative As String = VirtualPathUtility.ToAppRelative(decoded)
        Dim rule As Rule = Nothing

        If Not Rules.TryGetValue(appRelative, rule) Then
            Return False
        End If

        If rule.Path Is Nothing Then
            ' 中で NotFound.aspx に差し替える（住所はそのまま。状態の番号は NotFound.aspx.vb が 404 にする）。
            ' 【空の 404 を返して IIS の httpErrors に任せる形は、採らなかった】IIS Express で試したら差し替えられず、
            ' 本文の無い 404 がそのまま返った。自分で出せば、開発機でも本番でも同じになる
            context.RewritePath("~/NotFound.aspx", False)
            Return True
        End If

        context.Response.RedirectPermanent(BuildTarget(rule, request), False)
        context.ApplicationInstance.CompleteRequest()
        Return True

    End Function


    ' 行き先を組む（表のパス ＋ 表に書いた名前の値だけ）
    Private Shared Function BuildTarget(rule As Rule, request As HttpRequest) As String

        Dim sb As New System.Text.StringBuilder(rule.Path)
        Dim first As Boolean = True

        For Each key As String In rule.Keys
            Dim value As String = request.QueryString(key)
            If String.IsNullOrEmpty(value) Then
                Continue For
            End If
            sb.Append(If(first, "?", "&"))
            sb.Append(key).Append("=").Append(HttpUtility.UrlEncode(value))
            first = False
        Next

        Return sb.ToString()

    End Function

    ' ASP.NET が「無い」と判断した要求（無い .aspx・ルートに当たらない住所・.Master のような配らない種類）を、
    ' 素のエラー画面ではなく NotFound.aspx で返す。呼ぶのは Global.asax の Application_Error の1か所。
    '
    ' 【403 も 404 にそろえる】ASP.NET が 403 を返すのは、.Master・.vb のような「配らない種類のファイル」だけで、
    ' 利用者にとっては「無い」と同じ。合図は 404 にそろえる（開発者の決まり）
    ' 【GET で開き直す】ポストバックの本文（__VIEWSTATE）を NotFound.aspx に渡すと、よそのページの ViewState として
    ' 読もうとして 500 になるので、送られた本文は捨てて GET で開く
    Public Shared Function TryShowNotFound(context As HttpContext, ex As Exception) As Boolean

        If context Is Nothing OrElse ex Is Nothing Then
            Return False
        End If

        Dim http As HttpException = TryCast(ex, HttpException)
        If http Is Nothing Then
            http = TryCast(ex.GetBaseException(), HttpException)
        End If
        If http Is Nothing Then
            Return False
        End If

        Dim code As Integer = http.GetHttpCode()
        If code <> 404 AndAlso code <> 403 Then
            Return False
        End If

        ' NotFound.aspx そのものが「無い」と言われたら（発行漏れ）、ぐるぐる回さずに素の画面のままにする
        If String.Equals(VirtualPathUtility.ToAppRelative(context.Request.Path), "~/NotFound.aspx",
                         StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If

        context.Server.ClearError()
        context.Response.Clear()
        context.Server.TransferRequest("~/NotFound.aspx", False, "GET", Nothing)
        Return True

    End Function

End Class
