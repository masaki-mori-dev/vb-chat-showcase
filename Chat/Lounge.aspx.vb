Imports System.Data
Imports System.Data.SqlClient
Imports System.Configuration
Imports System.Web.Services
Imports Newtonsoft.Json

Public Class Lounge
    Inherits ChatPageBase

    ' 画面（JavaScript）へ渡す設定のJSON
    ' 部屋の画面（ChatRoom.aspx.vb）・トップページと、同じものを渡している
    Protected ReadOnly Property ClientSettingsJson As String
        Get
            Return SiteSettings.ClientSettingsJson
        End Get
    End Property

    ' 静的ファイルのURLに ?v= を付ける（common.AssetUrl。Site.Master の Asset と同じ）
    '
    ' 【マークアップから common を直接呼ばない】マークアップのインラインコードは
    ' コードビハインドとは別のコンパイル単位で、名前空間が効かずに実行時だけ500になることがある
    ' 。コードビハインドを経由する
    Protected Function Asset(path As String) As String
        Return common.AssetUrl(path)
    End Function

    ' HTML側（Lounge.aspx）から参照できるように、Protectedで宣言しておく
    Protected currentNickname As String
    Protected currentIconType As String
    Protected currentIconColor As String

    ' サーバー側で断られた理由を、画面のJavaScriptへ渡すためのJSON
    ' 理由が無ければ、JSONの null になる
    '
    ' 【なぜ、これが要るのか】
    ' 部屋の画面のJSが理由を運ぶ経路（sessionStorage）は、サーバーからは書けない。
    ' ChatRoom.aspx.vb の Page_Load が終了した部屋を弾くようになったため、
    ' その理由だけは Session で運んで、ここで受け取る。
    ' 表示そのものは、ラウンジの既存の帯にそのまま合流させてある（見た目は何も変えていない）
    '
    ' 【JSONにして <%= %> で出すこと】
    ' JavaScriptの文字列リテラルの中に <%: %> を書いてはいけない。
    ' あれはHTML用のエスケープなので、<script> の中では &quot; のような文字列が
    ' そのまま変数に入ってしまう（宿題7で直したのと同じ形）
    Protected ReadOnly Property ServerLeaveReasonJson As String
        Get
            Return leaveReasonJson
        End Get
    End Property

    Private leaveReasonJson As String = "null"


    ' === トリップ ===
    '
    ' 【トリップの欄は、ラウンジのプロフィールの枠に置く。トップページには置かない】
    ' トップへ行けるのはログアウトした人だけなので、そこにしか欄が無いと
    ' 「トリップを外すのにログアウトが要る＝別人になる」ことになり、
    ' トリップが名前・アイコンと独立していることと真逆になる。
    ' 要望：トリップはラウンジで作れるので、同じくラウンジで外したり、
    ' 変えたりもできるようにする。付け外ししても別人にはならない
    ' （ChatBrowserId は変わらないので、無視もこっそりの窓も切れない）

    ' トリップ機能が動いているか。False なら、欄を丸ごと出さない（フェイルクローズ）
    Protected ReadOnly Property IsTripEnabled As Boolean
        Get
            Return TripCode.IsEnabled
        End Get
    End Property

    ' 今このブラウザに付いているトリップ（無ければ Nothing）
    '
    ' 合言葉の Cookie は HttpOnly なので JS からは読めないが、サーバーには届く。
    ' ページを作るときに計算して埋め込む
    Protected currentTrip As String

    ' === アカウントID（チャットとアカウントの紐づけ） ===
    '
    ' アカウントID を表示している人は、名前の下に「@ユーザー名」が出て、トリップの欄は出ない（どちらか一方）。
    ' 決めるのは Chat/ChatAccountLink.vb（名前の札とログインの札と DB。画面から来た値は見ない）。

    ' アカウントID を表示しているか
    Protected isAccountShowing As Boolean

    ' 名前を決めたときは「表示する」にしたが、今はログインしていない（ログインし直すと、また出る）
    Protected needsAccountLogin As Boolean

    ' 「ログインすると、アカウントIDを紐づけられます」の行き先（ログインが済んだら、このラウンジへ戻す）。
    ' 【markup から AccountReturnUrl を直接呼ばないこと】.aspx は別のコンパイル単位で、RootNamespace（vb_chat）を
    ' Imports していないので「'AccountReturnUrl' is not declared」で500になる（開発機で踏んだ）。コードビハインドから渡す
    Protected ReadOnly Property AccountLoginUrl As String
        Get
            Return AccountReturnUrl.LoginUrlFor("/chat/lounge")
        End Get
    End Property

    ' 「@ユーザー名」と、押したときの行き先（公開のプロフィール）。表示していないときは Nothing
    Protected currentAccountText As String
    Protected currentAccountUrl As String

    ' アカウントにログインしているか（ラウンジの「アカウントID」のスイッチを出すか）。
    ' ログインしていない人には、スイッチを1文字も出さない（付けられないものを見せない）。
    ' 表示しているかは isAccountShowing。ログインしていても、名乗らずに入っている人は False のまま
    Protected isAccountLoggedIn As Boolean

    ' ログインしているアカウントの「@ユーザー名」と行き先（スイッチを入れたときに名前の下へ出すもの。
    ' 表示していないときもスイッチの横に見せる＝何が出るのかを、押す前に分かるように。トップページと同じ）
    Protected loggedInAccountText As String
    Protected loggedInAccountUrl As String

    ' 合言葉の長さの上限（入力欄の maxlength に使う。画面に数字を書かないため）
    Protected ReadOnly Property TripKeyMaxLength As Integer
        Get
            Return TripCode.MaxKeyLength
        End Get
    End Property


    ' === 部屋を作る ===

    ' 部屋名の文字数の上限（入力欄の maxlength と、注意書きに使う）
    '
    ' 【画面に数字を書かないこと】ここを通せば、決まりは ChatRoomRules の1か所で決まる
    Protected ReadOnly Property RoomNameMaxLength As Integer
        Get
            Return ChatRoomRules.RoomNameMaxLength
        End Get
    End Property

    ' 定員の <option>（選べる一覧は ChatRoomRules が持つ）
    '
    ' 【マークアップに選択肢をベタ書きしないこと】
    ' まで、Lounge.aspx に12個ベタ書きされていた。
    ' サーバー側の検査（ChatRoomRules.IsAllowedMaxUsers）と食い違うと、
    ' 「選べるのに、選ぶと断られる」という状態になる
    ' パスワードの文字数の上限（入力欄の maxlength に使う）
    Protected ReadOnly Property RoomPasswordMaxLength As Integer
        Get
            Return RoomPassword.MaxLength
        End Get
    End Property

    Protected ReadOnly Property MaxUsersOptionsHtml As String
        Get
            Return ChatRoomRules.BuildMaxUsersOptionsHtml(ChatRoomRules.DefaultMaxUsers)
        End Get
    End Property


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        If Not IsPostBack Then

            ' 【名前は札から読む】。名前の Cookie（ChatNickname）は JS から書き換えられる。
            ' ここは ChatPageBase を継承しているので、札が無ければそもそもトップへ返されている
            ' （＝ここへ来た時点で札は必ず有効。Nothing にはならない）
            currentNickname = NameToken.ReadNickname(Request)
            currentIconType = Request.Cookies("ChatIconType").Value
            currentIconColor = Request.Cookies("ChatIconColor").Value

            ' アカウントID を表示している人か（DB を1行引くことがある。引くのは、名前の札にアカウントの番号が入っている人だけ）
            Dim accountLink As ChatAccountLink.LinkResult = ChatAccountLink.Resolve(Context)

            isAccountShowing = accountLink.IsShowing
            needsAccountLogin = (accountLink.State = ChatAccountLink.LinkState.NeedsLogin)

            If isAccountShowing Then
                currentAccountText = ChatAccountLink.DisplayText(accountLink.UserName)
                currentAccountUrl = ChatAccountLink.ProfileUrl(accountLink.UserName)
            End If

            ' ログインしているか（スイッチを出すか）。表示している人はログインしている（ChatAccountLink が DB まで確かめた）。
            ' 表示していない人だけ、ログインの札を確かめる（札が無い人は DB に触らない＝AccountSession.Current の作り）
            Dim loggedInUserName As String = Nothing

            If isAccountShowing Then
                loggedInUserName = accountLink.UserName
            Else
                Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)
                If account IsNot Nothing Then
                    loggedInUserName = account.UserName
                End If
            End If

            isAccountLoggedIn = (loggedInUserName IsNot Nothing)

            If isAccountLoggedIn Then
                loggedInAccountText = ChatAccountLink.DisplayText(loggedInUserName)
                loggedInAccountUrl = ChatAccountLink.ProfileUrl(loggedInUserName)
            End If

            ' トリップ機能が止まっているときは、ここで必ず Nothing になる。
            ' 【アカウントID を出している人には、トリップを出さない】（どちらか一方。部屋の JoinRoom と同じ決め方）
            currentTrip = If(isAccountShowing, Nothing, TripCode.FromRequest(Request))

            ' サイトへの出入りの記録（Chat/SiteVisitLog.vb）
            ' ログインの目印があれば「ログイン（50）」を書いて目印を消し、続けて「ラウンジに来た（52）」を書く。
            ' どちらも1つの身元につき1日1回まで（2度目からは DB に触らない）。例外は投げない
            SiteVisitLog.RecordLoungeArrival(Context)

            ' === サーバー側で断られた理由を受け取る ===
            '
            ' 【読んだら必ず消すこと】
            ' 消さないと、ラウンジを開くたびに毎回同じ帯が出る
            ' （sessionStorage側で removeItem しているのと、同じ理由）
            Dim reason As String = TryCast(Session(ChatCommon.LeaveReasonSessionKey), String)

            If Not String.IsNullOrEmpty(reason) Then

                Session.Remove(ChatCommon.LeaveReasonSessionKey)

                ' ChatLogReader.ToScriptJson と同じ形にしておく。
                ' 不等号の開き文字をエスケープするのは、</script> が値に混ざった場合に
                ' ページが壊れるのを防ぐため（今の文言は定数なので混ざらないが、
                ' 理由が増えたときにここだけ抜けるのを避けたい）
                ' （VBの文字列にはエスケープ記法が無いため、"<" は
                '   「バックスラッシュ・u・0・0・3・c」の6文字として扱われる。これが狙いどおり）
                leaveReasonJson = JsonConvert.SerializeObject(reason).Replace("<", "\u003c")

            End If

        End If

    End Sub


    ' ============================================
    ' 「入室」ボタンが押された時に呼ばれる処理
    '
    ' 戻り値 … JSON文字列（文字列から変えた）
    '   {"ok":true}                                   … 入ってよい（Session に部屋IDを置いた）
    '   {"ok":false,"needPassword":true,"roomName":…} … パスワード付きの部屋。小窓で聞く
    '   {"ok":false,"needPassword":true,"message":…,"retryAfter":秒} … 間違えた／待たせている
    '   {"ok":false,"message":…}                      … それ以外で断った
    '
    ' password … 小窓で打たれたパスワード。小窓を出す前（最初の1回）は null
    '
    ' EnableSession:=True を明示している。
    ' WebMethodは既定ではセッションが有効になっておらず、
    ' Sessionを触るなら、この指定が必要なため
    ' （ChatRoom.aspx.vbのLeaveRoomと揃えてある）
    '
    ' 【ここの照合は、入口の鍵の「本体」ではない】最後に断るのは ChatHub.JoinRoom
    ' （ハブは開発者ツールから直接呼べる）。ここは照合して、通ったら「鍵を通った印」を置く
    ' （ChatHub.GrantRoomPass。ChatHub.RoomKeys.vb）。
    ' **Session に部屋IDを置くのも、照合を通ったあとだけ**——部屋の画面（ChatRoom.aspx.vb）は、
    ' Session の部屋IDを見て過去ログを描くので、先に置くと鍵付きの部屋の過去ログが読めてしまう
    ' ============================================
    '
    ' 【中身は EnterRoomCore。ここは包みだけ】
    ' WebMethod で漏れた例外は Application_Error に来ないので、ErrorReport.GuardWebMethod で
    ' 知らせてから投げ直す（画面から見た振る舞いは変わらない。SetTripKey・ClearTripKey も同じ）
    <WebMethod(EnableSession:=True)>
    Public Shared Function EnterRoom(roomId As Integer, password As String) As String
        Return ErrorReport.GuardWebMethod("Lounge.EnterRoom", Function() EnterRoomCore(roomId, password))
    End Function

    ' 【password を ErrorReport の補足へ渡さないこと】メールに載る
    Private Shared Function EnterRoomCore(roomId As Integer, password As String) As String

        ' WebMethodはShared（static）関数のため、
        ' ChatPageBase.OnInit()のCookieチェックを素通りしてしまう。
        ' そのため、ここで改めて自分でCookieが揃っているか確認する
        If Not ChatCommon.IsChatProfileComplete() Then
            Return EnterResultJson(False, "名前を決めてから入室してください。")
        End If

        Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        ' 部屋が在るか（IsActive = 1）と、パスワードのハッシュを、1回で読む
        ' （15-2a より前は COUNT(*) だった。問い合わせの回数は変わらない）
        Dim found As Boolean = False
        Dim roomName As String = Nothing
        Dim passwordHash As String = Nothing

        ' このブラウザが、この部屋で入室禁止か（同じ1回の SELECT で読む＝問い合わせは増えない）
        Dim isBanned As Boolean = False

        Using conn As New SqlConnection(connStr)

            ' 指定されたRoomIdが、本当にChatRoomsテーブルに存在するか確認する
            ' （不正なRoomIdが送られてくる可能性もゼロではないため、念のためのチェック）
            ' あわせてIsActive = 1もチェックし、終了した部屋（IsActive = 0）へ
            ' 直接URLなどから入室されるのを防いでいる
            Dim sql As String = "SELECT RoomName, PasswordHash, " & ChatHub.IsBannedSqlColumn &
                                " FROM ChatRooms WHERE RoomId = @RoomId AND IsActive = 1"

            Using cmd As New SqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@RoomId", roomId)
                cmd.Parameters.Add("@BanBrowserId", SqlDbType.UniqueIdentifier).Value =
                    ChatHub.BanBrowserParameter(ReadBrowserIdCookie())
                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    If reader.Read() Then
                        found = True
                        roomName = Convert.ToString(reader("RoomName"))
                        If Not IsDBNull(reader("PasswordHash")) Then
                            passwordHash = Convert.ToString(reader("PasswordHash"))
                        End If
                        isBanned = (Convert.ToInt32(reader("IsBanned")) = 1)
                    End If
                End Using

            End Using

        End Using

        If Not found Then
            Return EnterResultJson(False, "その部屋は存在しません。")
        End If

        ' === 入室禁止 ===
        '
        ' 管理人に入室禁止にされた部屋。パスワードの小窓を出す前に断る（知っていても入れないので、聞く意味が無い）。
        ' 【親切であって、防御ではない】最後に断るのは ChatHub.JoinRoom（ハブは直接呼べる）。
        ' Session に部屋IDも置かない（部屋の画面は Session の部屋IDで過去ログを描くため＝鍵付きの部屋と同じ理由）
        If isBanned Then
            Return EnterResultJson(False, ChatHub.BannedJoinMessage)
        End If

        ' === ノックで席を取り置いてもらっている人 ===
        '
        ' 「入室する」を押した＝ここへ来た。取り置きを、部屋の画面が開くまでの間ぶん延ばす（1回だけ）。
        ' パスワード付きの部屋では、このあと小窓で打つ時間がかかるため（ChatHub.ExtendKnockSeat）。
        ' 取り置きが無ければ何も起きない（普通の入室ボタンも、ここを通る）
        Dim knockBrowser As Guid

        If Guid.TryParse(ReadBrowserIdCookie(), knockBrowser) Then
            ChatHub.ExtendKnockSeat(roomId, knockBrowser)
        End If

        ' === もう入っている部屋 ===
        '
        ' ラウンジの「入室中」のボタン（別のタブで入っている部屋）から来た。パスワードは聞き直さない
        ' （部屋の側の JoinRoom も、同じ部屋を開き直す人には聞かない＝(a) 同じ部屋での置き換え）。
        ' 居るかどうかはハブの参加者リストで決める（クライアントに名乗らせない）
        Dim alreadyInRoomId As Integer

        If ChatHub.TryGetRoomIdByBrowserId(ReadBrowserIdCookie(), alreadyInRoomId) AndAlso alreadyInRoomId = roomId Then
            passwordHash = Nothing
        End If

        ' === 入口の鍵 ===
        If Not String.IsNullOrEmpty(passwordHash) Then

            ' 印は「このブラウザ」に置くので、身元が読めなければ入れない
            Dim browserId As Guid

            If Not Guid.TryParse(ReadBrowserIdCookie(), browserId) Then
                Return EnterResultJson(False,
                    "ブラウザの識別情報が読み取れないため、入室できません。お手数ですが、名前とアイコンを決め直してください。")
            End If

            Dim typed As String = RoomPassword.Normalize(password)
            Dim ipAddress As String = HttpContext.Current.Request.UserHostAddress
            Dim browserText As String = browserId.ToString()

            ' 待たせている間は、照合まで行かない（CPUを使う計算をさせない）。記録も書かない。
            ' 小窓を開く前の1回でも見る（開いた時点で、残りの秒数を出せるように）
            Dim waitSeconds As Integer = RoomPassword.GetLockoutSeconds(roomId, browserText, ipAddress)

            If waitSeconds > 0 Then
                Return PasswordResultJson(roomName, LockoutMessage(waitSeconds), waitSeconds)
            End If

            ' 小窓を出す前の1回（まだ何も打っていない＝null）。数えずに、小窓を出してもらう
            If password Is Nothing Then
                Return PasswordResultJson(roomName, Nothing, 0)
            End If

            ' 空のまま「入室する」を押された。数えない（打ち間違いではないため）
            If typed.Length = 0 Then
                Return PasswordResultJson(roomName, "パスワードを入れてください。", 0)
            End If

            ' サイト全体の計算の上限。一杯なら計算しない。数えない・記録もしない（試していないため）
            Dim busyWait As Integer
            Dim verified As Boolean

            Using ticket As IDisposable = RoomPassword.TryBeginWork(SiteSettings.RoomPasswordChecksPerMinute, busyWait)

                If ticket Is Nothing Then
                    Return PasswordResultJson(roomName, RoomPassword.BusyMessage, busyWait)
                End If

                verified = RoomPassword.Verify(typed, passwordHash, roomId)

            End Using

            If Not verified Then

                ' 数える（設定は鍵の外で読む。RoomPassword の中は鍵の中のため）
                Dim lockedFor As Integer = RoomPassword.RecordFailure(
                    roomId, browserText, ipAddress,
                    SiteSettings.RoomPasswordMaxFailures, SiteSettings.RoomPasswordLockout)

                WritePasswordRejectLog(roomId, browserText, ipAddress)

                If lockedFor > 0 Then
                    Return PasswordResultJson(roomName, LockoutMessage(lockedFor), lockedFor)
                End If

                Return PasswordResultJson(roomName, "パスワードが違います。", 0)

            End If

            RoomPassword.RecordSuccess(roomId, browserText)
            ChatHub.GrantRoomPass(roomId, browserId)

        End If

        ' 部屋の存在が確認できたので、Sessionに記録する
        ' （前回いた部屋の情報があっても、確認なしで単純に上書きしてよい方針。
        '   このSession("CurrentRoomId")自体が、そのまま「在室中かどうか」の判定に使われる）
        HttpContext.Current.Session("CurrentRoomId") = roomId

        Return EnterResultJson(True, Nothing)

    End Function


    ' 待たせるときの文言（秒数は画面の側が数え下げる。ここで入れるのは最初の数）
    Private Shared Function LockoutMessage(seconds As Integer) As String
        Return "何度も間違えたため、しばらく受け付けません。" & seconds & "秒たってから、もう一度お試しください。"
    End Function


    ' パスワード違いを記録する（種別41。満員と同じ歯止めを通す）
    '
    ' 記録に残すのは、満員（40）と同じく「その時点の名前・アイコン・トリップ・ブラウザ・IP」。
    ' **打たれたパスワードは残さない**。
    ' 見た目は Cookie、**名前は札（ChatNameToken）から**読み、ハブと同じ検査を通す
    ' （ChatProfileValidator。ログに素の値を入れない。名前の Cookie から札へ移した）
    ' アカウントID を表示している人には、トリップを入れない（どちらか一方。決めるのは ChatAccountLink）
    Private Shared Sub WritePasswordRejectLog(roomId As Integer, browserId As String, ipAddress As String)

        If Not ChatHub.ShouldLogPasswordReject(roomId, browserId) Then
            Return
        End If

        Dim request As HttpRequest = HttpContext.Current.Request

        Dim participant As New Participant With {
            .LocalId = 0,
            .Nickname = ChatProfileValidator.NormalizeNickname(NameToken.ReadNickname(request)),
            .IconType = ChatProfileValidator.NormalizeIconType(CookieValueOf(request, "ChatIconType")),
            .IconColor = ChatProfileValidator.NormalizeIconColor(CookieValueOf(request, "ChatIconColor")),
            .Trip = If(ChatAccountLink.Resolve(HttpContext.Current).IsShowing, Nothing, TripCode.FromRequest(request)),
            .BrowserId = browserId,
            .IpAddress = ipAddress,
            .RemotePort = ClientEndpoint.ReadPort(request)
        }

        ChatLog.Write(roomId, ChatMessageType.JoinRejectedPassword, participant)

    End Sub


    Private Shared Function CookieValueOf(request As HttpRequest, name As String) As String
        Dim cookie As HttpCookie = request.Cookies(name)
        Return If(cookie Is Nothing, Nothing, cookie.Value)
    End Function


    ' EnterRoom の戻り値（パスワード以外）
    Private Shared Function EnterResultJson(ok As Boolean, message As String) As String

        Dim result As New Dictionary(Of String, Object) From {
            {"ok", ok},
            {"message", message}
        }

        Return JsonConvert.SerializeObject(result)

    End Function


    ' EnterRoom の戻り値（パスワードを聞く・聞き直す）
    '
    ' roomName … 小窓の見出しに出す（画面の側は textContent で入れる）
    ' retryAfter … 待たせている秒数（待たせていなければ0）
    Private Shared Function PasswordResultJson(roomName As String, message As String,
                                               retryAfter As Integer) As String

        Dim result As New Dictionary(Of String, Object) From {
            {"ok", False},
            {"needPassword", True},
            {"roomName", roomName},
            {"message", message},
            {"retryAfter", retryAfter}
        }

        Return JsonConvert.SerializeObject(result)

    End Function


    ' ============================================
    ' 部屋を作る
    '
    ' 戻り値 … JSON文字列 {"ok":true,"roomId":12} ／ {"ok":false,"message":"…"}
    '
    ' 【「部屋作成処理の方針」のとおりのハイブリッド方式】
    '   ① ここ（WebMethod）が ChatRooms へ INSERT する。排他制御はDB任せ
    '   ② 成功したら ChatHub.RegisterCreatedRoom へ知らせ、
    '      そこから GetHubContext 経由でラウンジに居る全員へ配る
    '
    ' 【作った本人は、そのまま部屋へ入る】
    ' 決定：部屋を作った本人は、作ると同時にその部屋に入る。なので作れるのは1部屋だけ。
    ' 入室ボタン（EnterRoom）と同じく Session("CurrentRoomId") を置くので、
    ' 画面はこのあと /chat/room へ移るだけでよい（EnableSession:=True が要る）
    '
    ' 【1つのブラウザが同時に持てる部屋は1つ】
    ' 下の INSERT ... WHERE NOT EXISTS が、DBの中でそれを守っている。
    ' **設定にはしていない**——「作れるのは1部屋」は
    ' 「作ると同時に本人がその部屋へ入る」から出てくる決まりで、
    ' 数を増やすなら「入っていない部屋を持てる」という別の機能を考えることになる
    '
    ' 【WebMethod は ChatPageBase の OnInit を素通りする】
    ' そのため中で IsChatProfileComplete を呼ぶ（EnterRoom・SetTripKey と同じ作法）
    ' ============================================
    '
    ' 【password】空なら、パスワードを付けない部屋。
    ' 付けるなら、ハッシュにして PasswordHash へ入れる（RoomPassword.vb）
    '
    ' 【knockEnabled】ノックを受け付けるか（ChatRooms.IsKnockEnabled）。
    ' 満室のとき、ラウンジの入室ボタンが「ノック」になる
    <WebMethod(EnableSession:=True)>
    Public Shared Function CreateRoom(roomName As String, maxUsers As Integer,
                                      imageUploadEnabled As Boolean, knockEnabled As Boolean,
                                      password As String) As String
        Return ErrorReport.GuardWebMethod("Lounge.CreateRoom",
                                          Function() CreateRoomCore(roomName, maxUsers, imageUploadEnabled,
                                                                    knockEnabled, password))
    End Function

    ' 【部屋名・パスワードを ErrorReport の補足へ渡さないこと】メールに載る。
    ' 利用者が打った文字列は、エラーの知らせに入れない決まりである（発言の本文と同じ扱い）
    Private Shared Function CreateRoomCore(roomName As String, maxUsers As Integer,
                                           imageUploadEnabled As Boolean, knockEnabled As Boolean,
                                           password As String) As String

        If Not ChatCommon.IsChatProfileComplete() Then
            Return CreateRoomResultJson(False, 0, "名前を決めてから、部屋を作ってください。")
        End If

        ' === 管理人になるブラウザは、クライアントに名乗らせない ===
        '
        ' Cookie から読む（JoinRoom・LeaveBeacon と同じ）。
        ' ここを引数で受け取ると、**他人を勝手に部屋の管理人に仕立てられる**。
        ' GUID として読めなければ断る（管理人の決まらない部屋を作らないため）
        Dim ownerBrowserId As Guid

        If Not Guid.TryParse(ReadBrowserIdCookie(), ownerBrowserId) Then
            Return CreateRoomResultJson(False, 0,
                "ブラウザの識別情報が読み取れないため、部屋を作れません。お手数ですが、名前とアイコンを決め直してください。")
        End If

        ' === 部屋名 ===
        '
        ' 整えてから検査する（前後の空白・改行を落とし、中の空白の並びを1つにまとめる）。
        ' 20文字を超えたら、切り詰めずに断る（ChatRoomRules のコメント）
        Dim name As String = ChatRoomRules.NormalizeRoomName(roomName)
        Dim nameReason As String = ChatRoomRules.ValidateRoomName(name)

        If nameReason IsNot Nothing Then
            Return CreateRoomResultJson(False, 0, nameReason)
        End If

        ' === 定員 ===
        '
        ' 画面は <select> なので、一覧に無い値が来るのは細工したときだけ。
        ' 既定値へ落とさずに断る（「見た目の値は落とす。身元と部屋は断る」）
        If Not ChatRoomRules.IsAllowedMaxUsers(maxUsers) Then
            Return CreateRoomResultJson(False, 0, "定員が正しくありません。選び直してください。")
        End If

        ' === パスワード ===
        '
        ' 空なら付けない。長すぎたら、切り詰めずに断る（部屋名と同じ考え方）。
        ' ハッシュの計算（約180ms）は、DBの接続を開く前に済ませておく
        Dim typedPassword As String = RoomPassword.Normalize(password)
        Dim passwordReason As String = RoomPassword.Validate(typedPassword)

        If passwordReason IsNot Nothing Then
            Return CreateRoomResultJson(False, 0, passwordReason)
        End If

        Dim hasPassword As Boolean = typedPassword.Length > 0
        Dim passwordHash As String = Nothing
        Dim passwordCipher As String = Nothing

        If hasPassword Then

            ' サイト全体の計算の上限（入室の照合と同じ枠を使う）
            Dim busyWait As Integer

            Using ticket As IDisposable = RoomPassword.TryBeginWork(SiteSettings.RoomPasswordChecksPerMinute, busyWait)

                If ticket Is Nothing Then
                    Return CreateRoomResultJson(False, 0, RoomPassword.BusyMessage)
                End If

                passwordHash = RoomPassword.Hash(typedPassword)

            End Using

            ' 管理人に見せるための暗号文（照合には使わない。鍵が読めなければ Nothing＝「表示できません」になるだけ）
            passwordCipher = RoomPasswordSecret.Protect(typedPassword)

        End If

        Dim newRoomId As Integer = 0

        ' === INSERT ===
        '
        ' 【1つの文で済ませてある】
        ' 「この人はもう部屋を持っていないか」を確かめてから入れる必要があるが、
        ' SELECT と INSERT を分けると、その隙に2回押されて2部屋できる。
        ' INSERT ... SELECT ... WHERE NOT EXISTS なら1つの文なので、DBが直列化してくれる
        ' （UPDLOCK・HOLDLOCK は、同時に来た2本のうち片方を待たせるため。
        '   DailyJobs.TryClaim が「行が無ければ作る」を1文で済ませているのと同じ形）。
        ' **入らなかったときは SCOPE_IDENTITY() が NULL になる**＝もう持っている、と分かる
        '
        ' 【RoomType は 'Temporary' 固定】利用者が作れるのは一時部屋だけである
        ' （'Fixed' を作れてしまうと、既定のルームに混ざる）。
        ' 【OwnerId は NULL】あれはアカウントのユーザー番号を入れる列（Database/CreateTable/ChatRooms.sql）
        ' 【ノックは、画面のスイッチの値を入れる】
        ' 【パスワードは、ハッシュだけを入れる】（付けないなら NULL）
        ' 【見せるための暗号文も入れる】（PasswordCipher。照合は今までどおりハッシュだけ）
        Const sql As String =
            "SET NOCOUNT ON; " &
            "INSERT INTO ChatRooms " &
            "  (RoomName, MaxUsers, IsKnockEnabled, PasswordHash, PasswordCipher, RoomType, OwnerId, " &
            "   OwnerBrowserId, IsImageUploadEnabled, IsAiTopicEnabled) " &
            "SELECT @RoomName, @MaxUsers, @Knock, @PasswordHash, @PasswordCipher, @RoomType, NULL, @Owner, @Image, 0 " &
            " WHERE NOT EXISTS (SELECT 1 FROM ChatRooms WITH (UPDLOCK, HOLDLOCK) " &
            "                    WHERE OwnerBrowserId = @Owner AND IsActive = 1); " &
            "SELECT CAST(SCOPE_IDENTITY() AS INT);"

        Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        Using conn As New SqlConnection(connStr)
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@RoomName", SqlDbType.NVarChar, 100).Value = name
                cmd.Parameters.Add("@MaxUsers", SqlDbType.Int).Value = maxUsers
                cmd.Parameters.Add("@RoomType", SqlDbType.NVarChar, 20).Value = ChatRoomRules.RoomTypeTemporary
                cmd.Parameters.Add("@Owner", SqlDbType.UniqueIdentifier).Value = ownerBrowserId
                cmd.Parameters.Add("@Image", SqlDbType.Bit).Value = imageUploadEnabled
                cmd.Parameters.Add("@Knock", SqlDbType.Bit).Value = knockEnabled
                cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 256).Value =
                    If(passwordHash Is Nothing, CObj(DBNull.Value), passwordHash)
                cmd.Parameters.Add("@PasswordCipher", SqlDbType.NVarChar, 400).Value =
                    If(passwordCipher Is Nothing, CObj(DBNull.Value), passwordCipher)

                conn.Open()

                Dim result As Object = cmd.ExecuteScalar()

                If result Is Nothing OrElse Convert.IsDBNull(result) Then
                    ' 入らなかった＝このブラウザは、まだ有効な部屋を持っている
                    Return CreateRoomResultJson(False, 0,
                        "すでに部屋を1つ作っています。今の部屋から出ると、新しい部屋を作れます。")
                End If

                newRoomId = CInt(result)

            End Using
        End Using

        ' ハブへ知らせる（メモリへの登録と、ラウンジへの配信。ChatHub.Rooms.vb）
        '
        ' 【DBへ入れたあとに呼ぶこと】まだ無い部屋をラウンジへ配ると、
        ' 入室ボタンを押した人が「その部屋は存在しません」で戻される
        '
        ' パスワード付きなら、作った本人にはここで「鍵を通った印」も置かれる（小窓を通らずに入るため）
        ChatHub.RegisterCreatedRoom(newRoomId, name, maxUsers, ownerBrowserId, hasPassword, knockEnabled)

        ' 入室ボタン（EnterRoom）と同じ形で、部屋IDを Session へ置く。
        ' これで画面は /chat/room へ移るだけでよい
        HttpContext.Current.Session("CurrentRoomId") = newRoomId

        Return CreateRoomResultJson(True, newRoomId, Nothing)

    End Function


    ' CreateRoom の戻り値を、決まった形の JSON にする
    Private Shared Function CreateRoomResultJson(ok As Boolean, roomId As Integer,
                                                 message As String) As String

        Dim result As New Dictionary(Of String, Object) From {
            {"ok", ok},
            {"roomId", roomId},
            {"message", message}
        }

        Return JsonConvert.SerializeObject(result)

    End Function


    ' ============================================
    ' トリップを付ける・変える
    '
    ' 合言葉を受け取り、Cookie（HttpOnly）に書いて、できたトリップを返す。
    ' 付けるのも、別の合言葉に変えるのも、この1つ（同じ名前の Cookie を書き直すだけ）。
    '
    ' 戻り値 … JSON文字列 {"ok":true,"trip":"a1b2c3d4e5"} ／ {"ok":false,"message":"…"}
    ' （GetLogEntries と同じく、キー名を小文字で揃えるため自分で JSON にしている）
    '
    ' 【なぜ WebMethod なのか】Cookie が HttpOnly なので、JS では書けない。
    ' ページを読み込み直さずに済む（ポストバックはこのページに無い）。
    '
    ' 【WebMethod は ChatPageBase の OnInit を素通りする】
    ' そのため、中で IsChatProfileComplete を呼ぶ（EnterRoom と同じ作法）。
    '
    ' 【Session は使わない】EnableSession は付けない
    '
    ' 【これは「合言葉を入れるとトリップが出る」窓口である】
    ' 誰でも好きな文字列を試せる。ソルトがあるので手元では計算できないが、
    ' サイトを使って試されるのは止められない。短い合言葉（1234 など）は真似されうるので、
    ' 画面に注意書きを出してある（回数の制限は、今は入れていない）
    ' ============================================
    <WebMethod()>
    Public Shared Function SetTripKey(key As String) As String
        Return ErrorReport.GuardWebMethod("Lounge.SetTripKey", Function() SetTripKeyCore(key))
    End Function

    ' 【合言葉（key）を、ErrorReport の補足へ渡さないこと】メールに載る。合言葉は秘密である
    Private Shared Function SetTripKeyCore(key As String) As String

        If Not ChatCommon.IsChatProfileComplete() Then
            Return TripResultJson(False, Nothing, "名前を決めてから、トリップを付けてください。")
        End If

        ' 止まっているときは、欄そのものを出していない。
        ' ここへ来るのは、止まる前に開いた古いページか、直接呼ばれた場合だけ
        If Not TripCode.IsEnabled Then
            Return TripResultJson(False, Nothing, "今はトリップを使えません。")
        End If

        ' 【アカウントID を表示している間は、トリップを付けさせない】（どちらか一方）
        ' 画面は欄そのものを出していないので、ここへ来るのは直接呼ばれたときだけ。
        ' **決めるのはここ（サーバー）**で、画面が欄を出さないことは守りではない
        If ChatAccountLink.Resolve(HttpContext.Current).IsShowing Then
            Return TripResultJson(False, Nothing, "アカウントIDを紐づけているので、トリップは要りません。")
        End If

        Dim reason As String = TripCode.ValidateKey(key)

        If reason IsNot Nothing Then
            Return TripResultJson(False, Nothing, reason)
        End If

        TripCode.WriteKeyCookie(HttpContext.Current.Response, key)

        ' 返すトリップは、Cookie から読み戻すのと同じ計算（前後の空白を落として計算する）
        Dim trip As String = TripCode.Compute(key)

        ' ラウンジの顔の一覧にも、その場で反映する（ChatHub.UpdateLoungeTrip）
        ChatHub.UpdateLoungeTrip(ReadBrowserIdCookie(), trip)

        Return TripResultJson(True, trip, Nothing)

    End Function


    ' ============================================
    ' トリップを外す
    '
    ' 合言葉の Cookie を消すだけ。確認のダイアログは出さない——
    ' ログアウトと違って取り返しがつく（同じ合言葉を入れ直せば、同じトリップに戻る）
    ' ============================================
    <WebMethod()>
    Public Shared Function ClearTripKey() As String
        Return ErrorReport.GuardWebMethod("Lounge.ClearTripKey", Function() ClearTripKeyCore())
    End Function

    Private Shared Function ClearTripKeyCore() As String

        If Not ChatCommon.IsChatProfileComplete() Then
            Return TripResultJson(False, Nothing, "名前を決めてから、操作してください。")
        End If

        TripCode.ClearKeyCookie(HttpContext.Current.Response)

        ' ラウンジの顔の一覧からも、その場で印を外す
        ChatHub.UpdateLoungeTrip(ReadBrowserIdCookie(), Nothing)

        Return TripResultJson(True, Nothing, Nothing)

    End Function


    ' ============================================
    ' アカウントID を付ける・外す
    '
    ' トリップと同じように、ラウンジの欄でその場で付け外しする。**付け外ししても別人にならない**
    ' （ChatBrowserId は変えない。名前の札だけを書き直す＝Chat/ChatLogin.vb の ChangeAccountIdShown）。
    '
    ' show … True＝付ける／False＝外す。**どのアカウントかは受け取らない**（ログインの札からサーバーが読む）
    '
    ' 戻り値 … JSON文字列
    '   {"ok":true,"showing":true,"account":"@ユーザー名","accountUrl":"/profile/…","trip":null}
    '   {"ok":true,"showing":false,"account":null,"accountUrl":null,"trip":"a1b2c3d4e5"か null}  … 外したら、トリップが戻る
    '   {"ok":false,"message":"…"}
    '
    ' 【開いている部屋には、入り直してから効く】（トリップと同じ。部屋の接続は、繋いだ時点の Cookie で決めている）
    ' 【Session は使わない】EnableSession は付けない（SetTripKey と同じ）
    ' ============================================
    <WebMethod()>
    Public Shared Function SetAccountIdShown(show As Boolean) As String
        Return ErrorReport.GuardWebMethod("Lounge.SetAccountIdShown", Function() SetAccountIdShownCore(show))
    End Function

    Private Shared Function SetAccountIdShownCore(show As Boolean) As String

        If Not ChatCommon.IsChatProfileComplete() Then
            Return AccountResultJson(False, Nothing, Nothing, "名前を決めてから、操作してください。")
        End If

        Dim context As HttpContext = HttpContext.Current

        Dim change As ChatLogin.AccountIdChangeResult = ChatLogin.ChangeAccountIdShown(context, show)

        If Not change.Ok Then
            Return AccountResultJson(False, Nothing, Nothing, change.Message)
        End If

        ' 外したら、トリップの合言葉が残っていれば、そのトリップが戻る（合言葉の Cookie は、付けている間も消していない）
        Dim trip As String = Nothing

        If Not show Then
            trip = TripCode.FromRequest(context.Request)
        End If

        ' ラウンジの顔の一覧にも、その場で反映する（印が四角⇔丸で入れ替わる）
        ChatHub.UpdateLoungeAccountMark(ReadBrowserIdCookie(), change.UserName, trip)

        Return AccountResultJson(True, change.UserName, trip, Nothing)

    End Function


    ' SetAccountIdShown の戻り値を、決まった形の JSON にする
    Private Shared Function AccountResultJson(ok As Boolean, userName As String, trip As String, message As String) As String

        Dim result As New Dictionary(Of String, Object) From {
            {"ok", ok},
            {"showing", ok AndAlso userName IsNot Nothing},
            {"account", ChatAccountLink.DisplayText(userName)},
            {"accountUrl", ChatAccountLink.ProfileUrl(userName)},
            {"trip", trip},
            {"message", message}
        }

        Return JsonConvert.SerializeObject(result)

    End Function


    ' この要求のブラウザ（ChatBrowserId の値。無ければ Nothing）。
    ' ラウンジの顔を「誰のものか」で探すのに使う（クライアントに名乗らせない。JoinRoom と同じ）
    Private Shared Function ReadBrowserIdCookie() As String

        Dim cookie As HttpCookie = HttpContext.Current.Request.Cookies("ChatBrowserId")

        Return If(cookie Is Nothing, Nothing, cookie.Value)

    End Function


    ' SetTripKey / ClearTripKey の戻り値を、決まった形の JSON にする
    Private Shared Function TripResultJson(ok As Boolean, trip As String, message As String) As String

        Dim result As New Dictionary(Of String, Object) From {
            {"ok", ok},
            {"trip", trip},
            {"message", message}
        }

        Return JsonConvert.SerializeObject(result)

    End Function

End Class