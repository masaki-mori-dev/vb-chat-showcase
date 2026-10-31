Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Web.Services

Public Class ChatRoom
    Inherits ChatPageBase

    ' HTML側（ChatRoom.aspx）で<%= %>により参照するため、Protectedで宣言しておく
    Protected RoomName As String = String.Empty
    Protected MaxUsers As Integer = 0

    ' この部屋で、画像を送れるか（ChatRooms.IsImageUploadEnabled）
    '
    ' 【部屋名では決まらない。この列ただ1つで決まる】
    ' 固定部屋のうち「初心者歓迎！」だけを不許可にしてあるが、
    ' コード側で部屋名を見てはいけない（理由：部屋名や設定は今後
    ' 変わるかもしれない）。値の入れ方は Database/InsertData/ChatRooms.sql にある
    '
    ' 【画面に出さないことは、防御ではない】
    ' False のときは「画像」のタブを隠す（hidden）が、それは親切であって守りではない。
    ' 受け口（Chat/UploadImage.aspx）が、同じ列をもう一度自分で確かめている。
    ' 【、部屋に居ながら変わる】管理人が設定で切り替えると、
    ' 画面はその場でタブを出し入れする（そのため、タブと面はいつも出力し、hidden で隠す）
    Protected IsImageUploadEnabled As Boolean = False

    ' この部屋がノックを受け付けるか・鍵（パスワード）がかかっているか。
    ' 管理人の「部屋の設定」の欄の最初の値にだけ使う（ハッシュは読まない＝有無だけ）
    Protected IsKnockEnabled As Boolean = False
    Protected HasPassword As Boolean = False

    ' 「部屋の設定」の定員の選択肢（作るときと同じ ChatRoomRules から。今の定員を選んだ状態）
    Protected ReadOnly Property RoomSettingsMaxUsersOptionsHtml As String
        Get
            Return ChatRoomRules.BuildMaxUsersOptionsHtml(MaxUsers)
        End Get
    End Property

    ' 「画像」のタブの hidden 属性（画像を送れない部屋なら、隠して出す）。
    ' それまでは <% If %> でタブごと出さなかった。部屋に居ながら切り替わるので、いつも出力して隠す
    Protected ReadOnly Property ImageTabHiddenAttribute As String
        Get
            Return If(IsImageUploadEnabled, "", " hidden=""hidden""")
        End Get
    End Property

    ' この画面が開いている部屋のID。Page_Loadが確定させる（TryResolveRoomId）
    '
    ' 【HTML側でSessionを読み直さないこと】
    ' 出どころはSessionだけではなく、Sessionが消えていればハブから引き当てている。
    ' マークアップでSessionを読むと、その場合だけ空になり、
    ' JS側のroomIdがNaNになって入室も発言もできなくなる（しかもエラーは出ない）
    Protected RoomId As Integer = 0

    ' 入室時・F5リロード時に表示する、過去ログのJSON
    ' （HTML側の<script>内へ、<%= %>でそのまま埋め込む）
    '
    ' 会話ログはDBに一本化しており、sessionStorageによる保存・復元は廃止した。
    ' 初期値を"[]"にしてあるのは、取得に失敗した場合でも
    ' JavaScript側が「空の配列」として扱えるようにするため
    Protected InitialLogJson As String = "[]"

    ' こっそりの過去ログ（自分が当事者のものだけ）。
    ' 部屋のログとは別に渡す——出す場所（窓）も、絞り方も違うため
    Protected InitialPrivateLogJson As String = "[]"

    ' 表示行数の選択肢（<select>の中身）と、今選ばれている行数
    ' HTML側で<%= %>により埋め込む
    '
    ' 選択肢をHTMLへべた書きせず、ここで組み立てているのは、
    ' 行数の一覧をChatLogReader.AllowedMessageCountsの1か所だけで管理するため
    ' （HTMLに書くと、選択肢を増やしたときに両方直す必要があり、片方を忘れる）
    Protected LogCountOptionsHtml As String = String.Empty

    ' 画面（JavaScript）へ渡す設定のJSON
    '
    ' 【なぜ、HTML側に数字をべた書きしないのか】
    ' 決めているのはSiteSettings（＝DBのSiteSettingsテーブル）である。
    ' 画面側に同じ数字を書くと、値を変えたときに表示だけが嘘になり、
    ' 「あと10文字と出ているのに送信できない」という形で出る。
    ' 表示行数の選択肢をChatLogReaderから組み立てているのと、同じ考え方
    '
    ' 【値を1つずつ渡す形をやめた】
    ' 以前はMaxMessageLengthだけをプロパティにして`<%= %>`で埋めていたが、
    ' 画面へ渡したい値が増えたため、JSONを1つ渡す形に変えた。
    ' JSONはSiteSettings側でキャッシュされており、ここで組み立て直してはいない
    Protected ReadOnly Property ClientSettingsJson As String
        Get
            Return SiteSettings.ClientSettingsJson
        End Get
    End Property

    ' 設定パネルの「見た目」の、アイコンの選択肢
    '
    ' 【サーバーが枠を出す】トップページの IconChoicesHtml と同じ理由。JSでスプライトの <symbol> を拾って作ると、
    ' スプライトが届くまで選択肢が0個になり、取れなかったときは1つも選べない。
    ' 名前さえ分かれば枠は置ける（中の絵は、スプライトが届いた時点で <use> が自動で描き直される）。
    '
    ' 【押せるもの＝ラジオボタンの形】（role="radio"。トップページの div とは違う。トップは今回は触らない）
    ' 20個から1つを選ぶので、読み上げソフトには「アイコン、ラジオボタン、20個中3個目、選択」のように伝わる。
    ' Tab で止まるのは選ばれている1つだけ（tabindex はJSが付け替える）。矢印キーで隣へ移る（WAI-ARIA のラジオグループの作法）。
    '
    ' 【名前は番号】アイコンの名前（IconMaster.Name）は young_male のような英語の識別子で、読み上げに向かない。
    ' 日本語の名前の列は IconMaster に無い（足すなら、管理画面＝16番でアイコンを登録する回に）。
    ' 番号でも「何個目か」は分かり、絵は目で見る人のためのもの、と割り切った。
    '
    ' 【出口でもエスケープする】名前はスプライトの id から来ているが、入口の検査だけに頼らない（IconChoicesHtml と同じ）
    Protected ReadOnly Property AppearanceIconChoicesHtml As String
        Get
            Dim sb As New Text.StringBuilder()
            Dim number As Integer = 0

            For Each iconType As String In IconCatalog.GetOrderedIconTypes()

                number += 1

                Dim safeName As String = HttpUtility.HtmlAttributeEncode(iconType)

                ' 読み上げ用の名前。日本語の名前があればそれ、無ければ今までどおり番号
                ' （IconMaster.Label → スプライトの data-label → ここ。
                '   トップページの IconChoicesHtml と同じ形にそろえてある）
                Dim readable As String = IconCatalog.GetLabel(iconType)
                If readable = "" Then
                    readable = "アイコン" & number
                End If

                sb.Append("<button type=""button"" class=""appearance-icon-choice"" role=""radio"" aria-checked=""false"" tabindex=""-1""")
                sb.Append(" data-icon=""").Append(safeName).Append("""")
                sb.Append(" aria-label=""").Append(HttpUtility.HtmlAttributeEncode(readable)).Append(""">")
                sb.Append("<svg viewBox=""0 0 512 512"" aria-hidden=""true""><use href=""#icon-").Append(safeName).Append("""></use></svg>")
                sb.Append("</button>")

            Next

            Return sb.ToString()
        End Get
    End Property

    ' 画面（JavaScript）へ渡すスタンプの目次のJSON
    '
    ' 【なぜ vbChatSettings の中に入れず、別の変数にしたのか】
    ' 経路は同じ（ページのインラインの <script> で1回だけ渡す）だが、
    ' ClientSettingsJson は部屋・トップ・ラウンジの3ページが使う。
    ' スタンプを使うのは部屋だけなので、中に入れると使わない2ページにも毎回配ることになる。
    ' 持ち主も別（SiteSettings と StampCatalog）で、捨てるタイミングも違う
    '
    ' JSONは StampCatalog 側でキャッシュされており、ここで組み立て直してはいない
    Protected ReadOnly Property StampCatalogJson As String
        Get
            Return StampCatalog.ClientJson
        End Get
    End Property

    ' ============================================
    ' どの部屋のページを開こうとしているのかを決める
    '
    ' 戻り値 … 決まったかどうか。決まらなければ roomId は 0 のまま
    '
    ' 【順番が大事：Sessionが先、ハブが後】
    '
    '   1. Session("CurrentRoomId") … ラウンジの入室ボタン（EnterRoom）が置いた値
    '   2. ハブの参加者リスト        … 1が無いときだけ聞く
    '
    ' 【なぜ、ハブを先にしてはいけないのか】
    ' ラウンジで入室ボタンを押した直後は、JoinRoomがまだ走っていない
    ' （ページが読み込まれてから走る）ため、ハブはまだこのブラウザを知らない。
    ' しかも直前まで別の部屋に居た場合、ハブは*古いほうの*部屋を答えてしまう。
    ' 「これから入る部屋」を知っているのはSessionだけである
    '
    ' 【なぜ、ハブへの問い合わせが要るのか】
    ' Sessionは20分で消え、アプリが寝ても消える。長時間の凍結から復帰した利用者は、
    ' 在室しているのにF5すると、理由も無くラウンジへ飛ばされていた
    ' （実機の37分放置で発生）。
    ' 在室しているかどうかを本当に知っているのはハブの参加者リストである
    '
    ' 【ハブが答えても、Sessionへ書き戻さないこと】
    ' 書き戻せば次回のF5は速くなるが、Sessionの書き手が増える。
    ' 今は「置くのはEnterRoomだけ、消すのはLeaveRoomだけ」と決まっていて、
    ' だからこそ値の出どころを追える。速さのために、その1点を崩さない
    ' （どちらもメモリ上の処理で、速さの差は測れる大きさではない）
    ' ============================================
    Private Function TryResolveRoomId(ByRef roomId As Integer) As Boolean

        roomId = 0

        ' 1. Session（ラウンジの入室ボタンが置いた値）
        '
        ' 「在室中フラグ方式」：Session("CurrentRoomId")自体を、今在室中かどうかの
        ' 判定にそのまま使う。F5リロードしてもSessionが残っている限り、同じ部屋に居続けられる
        ' （退室ボタン押下時は、LeaveRoom WebMethodでこのSessionを明示的にクリアする）
        Dim sessionValue As Object = Session("CurrentRoomId")

        If sessionValue IsNot Nothing Then
            roomId = CInt(sessionValue)
            Return True
        End If

        ' 2. ハブの参加者リスト（Sessionが消えていても、在室していれば答えが返る）
        Dim browserId As String = Nothing
        Dim browserCookie As HttpCookie = Request.Cookies("ChatBrowserId")

        If browserCookie IsNot Nothing Then
            browserId = browserCookie.Value
        End If

        If Not ChatProfileValidator.IsValidBrowserId(browserId) Then
            Return False
        End If

        Return ChatHub.TryGetRoomIdByBrowserId(browserId, roomId)

    End Function


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' どの部屋のページを開こうとしているのかを決める（上のTryResolveRoomId）
        '
        ' 【罠】ここで Dim roomId As Integer を作らないこと。
        ' VBは大文字・小文字を区別しないため、ローカルの roomId は
        ' フィールドの RoomId をそのまま隠してしまう。
        ' その状態で「RoomId = roomId」と書くと、自分自身への代入になり、
        ' フィールドは0のまま残る。Option Strict Off では警告も出ない。
        ' 表に出るのは、隠しフィールドが value="0" になり、
        ' JS側が「部屋0」でJoinRoomを呼んで「この部屋は終了しました」と
        ' 断られる、という遠い形でだけである（実際に踏んだ）
        If Not TryResolveRoomId(RoomId) Then

            ' Sessionにも無く、ハブも知らない＝本当にどの部屋にも居ない。
            ' 理由を添えてラウンジへ返す
            '
            ' 【無言で返さないこと】
            ' 以前はここで理由を付けずに追い返していた。長時間の凍結でSessionが切れた
            ' あとにF5すると、利用者は何の説明も無くラウンジに立たされることになる
            ' （実機で実際に起きた）
            '
            ' 運び方は、部屋が終了していたときとまったく同じ（Session経由）。
            ' 部屋の画面のJSが使うsessionStorageは、サーバーからは書けないため
            Session(ChatCommon.LeaveReasonSessionKey) = ChatCommon.NotInRoomMessage

            ' 【重要】第2引数（endResponse）は既定のTrueのまま呼ぶこと。
            ' Falseにすると、CompleteRequest()を添えても実行中のページは止まらない
            Response.Redirect(ChatCommon.LoungeUrl)
            Return

        End If

        ' 表示行数（Cookieの値。無ければ既定値）と、その選択肢を先に用意しておく
        Dim logCount As Integer = GetLogCountFromCookie()
        LogCountOptionsHtml = BuildLogCountOptionsHtml(logCount)

        ' 部屋名はページ表示用の情報でしかないため、ページ読み込み時に1回だけ取得する
        ' （IsPostBackやSignalRの通信のたびに、毎回問い合わせる必要はない）
        Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

        Using conn As New SqlConnection(connStr)

            ' 接続は、このUsingの中で1回だけ開く。
            ' 部屋名の取得と、このあとの過去ログの取得で、同じ接続を使い回すため
            conn.Open()

            ' IsActiveは「取得する列」に入れるが、WHEREには入れない。
            '
            ' 【WHEREに入れてはいけない理由（2つある）】
            '  1. 終了した部屋（IsActive = 0）の過去ログを表示する場面で、
            '     RoomIdから部屋名を引けなくなってしまう
            '  2. WHEREで絞ると「行が無い」と「終了している」が区別できなくなる。
            '     ChatHub.TryGetRoomStateが「問い合わせの成否」と「行の有無」を
            '     分けて返しているのと、同じ考え方
            '
            ' なお、終了した部屋への新規入室は、Lounge.aspx.vb側でも止めている
            Dim sql As String =
                "SELECT RoomName, MaxUsers, IsActive, IsImageUploadEnabled, IsKnockEnabled, " &
                "       CASE WHEN PasswordHash IS NULL THEN 0 ELSE 1 END AS HasPassword " &
                "FROM ChatRooms WHERE RoomId = @RoomId"

            ' 部屋が「今も開いているか」。行が見つからなければFalseのまま
            ' （＝存在しない部屋も、終了した部屋も、同じくFalseになる。
            '   ChatHub.TryGetRoomStateのisOpenと、わざと同じ作りにしてある）
            Dim isRoomOpen As Boolean = False

            Using cmd As New SqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@RoomId", roomId)

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    If reader.Read() Then
                        RoomName = reader("RoomName").ToString()
                        MaxUsers = CInt(reader("MaxUsers"))
                        isRoomOpen = CBool(reader("IsActive"))
                        IsImageUploadEnabled = CBool(reader("IsImageUploadEnabled"))
                        IsKnockEnabled = CBool(reader("IsKnockEnabled"))
                        HasPassword = (CInt(reader("HasPassword")) = 1)
                    End If
                End Using
            End Using

            ' === 終了した部屋・存在しない部屋は、ここで止める ===
            '
            ' 【なぜ、この位置なのか】
            ' 以前はこの判定が無く、ページを最後まで組み立てて送っていた。実際に止まるのは
            ' そのあとクライアントがSignalRのJoinRoomを呼んだ時で、それまでの間、
            ' 終了した部屋の画面が見えていた。実測では194KBを送っており、しかも
            ' 下の過去ログのSELECTまで走って、その部屋の会話が丸ごとHTMLに載っていた。
            ' ここで抜ければ、そのSELECTを1回まるごと省ける（＝本体の効果はここで出る）
            '
            ' 【行が無い場合も、同じIfで受ける】
            ' ChatHubは「存在しない部屋」と「終了した部屋」をわざと同じ扱いにしている。
            ' ページ側だけ片方しか見ないと、2つの層で判断が食い違う
            '
            ' 【罠】この判定を、下の過去ログのTry/Catchの中に入れないこと。
            ' Response.Redirectは（endResponse既定のTrueで）ThreadAbortExceptionを投げるため、
            ' Catch ex As Exception が走って「過去ログの取得に失敗しました」という
            ' 嘘の記録が出る
            '
            ' 【罠】この判定そのものをTry/Catchで包んで「エラーならラウンジへ」にしないこと。
            ' DBが一瞬詰まっただけで、在室者が全員部屋から追い出されるフェイルクローズになる。
            ' 例外はそのまま外へ出す（ChatHub.TryGetRoomStateがフェイルオープンなのと、向きを揃える）
            '
            ' 【罠】Session("CurrentRoomId")は消さないこと。
            ' Sessionはタブごとではなくブラウザ単位で共有されているため、
            ' 同じブラウザの別タブが正当に在室している部屋を巻き添えにする
            ' （ChatRoom.aspxのjoinRoomAndRenderに書いてある罠と、まったく同じもの）
            If Not isRoomOpen Then

                ' 断った理由を、ラウンジへ運ぶ。
                ' 部屋の画面のJSが使っているsessionStorageは、サーバーからは書けないため、
                ' ここだけSessionで運び、ラウンジ側で既存の帯に合流させている
                Session(ChatCommon.LeaveReasonSessionKey) = ChatCommon.RoomClosedMessage

                ' 【重要】第2引数（endResponse）は既定のTrueのまま呼ぶこと。
                ' Falseにすると、CompleteRequest()を添えても実行中のページは止まらず、
                ' 302の本文のあとにページ全文が続けて送られる（実測で確認）。
                ' それでは「組み立てて送るのをやめる」という今回の目的が果たせない
                Response.Redirect("/chat/lounge")
                Return

            End If

            ' === 過去ログの読み出し ===
            '
            ' 入室時もF5リロード時も、ここでDBから直近ぶんを読んで画面へ渡す。
            ' クライアントは、SignalRの接続を始める前にこれを描画し、
            ' 以降はリアルタイム受信ぶんだけを追記する
            ' （この境目があるため、二重表示は原理的に起きない）
            Try

                ' 「自分の発言かどうか」の判定に使う。
                ' 【罠】LocalIdでは判定できない（入室のたびに新しい番号が発行されるため）。
                ' ログに焼き込まれたBrowserIdと、このCookieを突き合わせて判定する
                Dim browserId As String = Nothing
                Dim browserCookie As HttpCookie = Request.Cookies("ChatBrowserId")
                If browserCookie IsNot Nothing Then
                    browserId = browserCookie.Value
                End If

                Dim entries = ChatLogReader.GetRecentMessages(conn, roomId, logCount, browserId)
                InitialLogJson = ChatLogReader.ToScriptJson(entries)

                ' こっそりの過去ログ。窓ごとにlogCount行ぶん返ってくる
                ' （絞り込みはサーバー側。当事者でない行は、ここへ1件も来ない）
                Dim privateEntries =
                    ChatLogReader.GetRecentPrivateMessages(conn, roomId, logCount, browserId)
                InitialPrivateLogJson = ChatLogReader.ToScriptJson(privateEntries)

            Catch ex As Exception

                ' 過去ログが読めなくても、部屋には入れるようにする。
                ' （ログの取得に失敗したせいで入室そのものができなくなるほうが、
                '   利用者にとっては深刻なため。ChatLog.Writeと同じ考え方）
                ' この場合、過去ログが空の状態でチャットが始まる
                InitialLogJson = "[]"
                InitialPrivateLogJson = "[]"

                ' 握りつぶしたままだと「過去ログが常に空」という状態に気づけないため、知らせる。
                ' （エラー報告メールへ繋いだ。それまでは Debug.WriteLine で、
                '   本番の Release ビルドでは呼び出しごと消えていた。
                '   同じエラーは1時間に1通にまとめられるので、DB障害中に部屋が開かれ続けても大量には飛ばない）
                ErrorReport.Report(ex, "ChatRoom.Page_Load（過去ログの読み出し）", "部屋 " & roomId)

            End Try

        End Using

    End Sub


    ' 表示行数を、Cookie ChatLogCount から読み取る
    ' Cookieが無い・想定外の値だった場合は、ChatLogReaderの既定値へ丸められる
    Private Function GetLogCountFromCookie() As Integer

        Dim cookie As HttpCookie = Request.Cookies("ChatLogCount")

        If cookie Is Nothing Then
            Return ChatLogReader.DefaultMessageCount
        End If

        Dim count As Integer

        If Not Integer.TryParse(cookie.Value, count) Then
            Return ChatLogReader.DefaultMessageCount
        End If

        ' 白リストによる丸めは、ChatLogReader側でも必ず行われる
        Return ChatLogReader.NormalizeMessageCount(count)

    End Function


    ' 表示行数の<option>タグを組み立てる
    ' selectedCount … 今選ばれている行数（selected属性を付ける対象）
    '
    ' 選択肢はChatLogReader.AllowedMessageCountsから作る。
    ' ここで独自の一覧を持つと、丸め処理（NormalizeMessageCount）と食い違い、
    ' 「選べるのに選ぶと既定値へ戻される」という状態になるため
    Private Shared Function BuildLogCountOptionsHtml(selectedCount As Integer) As String

        Dim sb As New Text.StringBuilder()

        For Each count As Integer In ChatLogReader.AllowedMessageCounts

            sb.Append("<option value=""")
            sb.Append(count)
            sb.Append("""")

            If count = selectedCount Then
                sb.Append(" selected")
            End If

            sb.Append(">")
            sb.Append(count)
            sb.Append("行</option>")

        Next

        Return sb.ToString()

    End Function


    ' ============================================
    ' 過去ログを取り直すためのWebMethod
    ' （表示行数を変えたときと、再接続に成功したときに呼ばれる）
    '
    ' ページを読み込み直すのではなく、ここから取り直して描き直す。
    ' 読み込み直すとSignalRの接続が繋ぎ直しになり、そのぶん会話が途切れるため
    '
    ' 戻り値は、次の3つを見分けられる形のJSONオブジェクトにしてある。
    '
    '   成功           {"entries":[...]}
    '                  「まだ発言が1件も無い部屋」なら、空の配列になる
    '   一時的な失敗   {"entries":null}
    '                  DBが一瞬詰まっただけ。クライアントは今の表示をそのまま残す
    '   恒久的な失敗   {"entries":null,"rejectReason":"...","rejectRedirectUrl":"..."}
    '                  ここに居続けても直らない。理由を添えて移動してもらう
    '
    ' 【なぜ、3つに分ける必要があったのか】
    ' それまでは、どの失敗も一律で "null" を返していた。理由は4つあり
    ' （プロフィール欠け・身元不明・部屋の終了・DBの失敗）、性質がまるで違うのに、
    ' クライアントは全部「今の表示を残す」で扱っていた。
    ' DBの失敗に対しては正しい判断だが、恒久的な失敗に対しては最悪で、
    ' 二度と回復しないのに利用者へ何も知らせないことになる。
    ' 実際、Sessionが切れたときに「再接続もできる・発言もできる・
    ' しかし切断中の発言だけが永久に出ない」という状態が起きていた
    ' （実機の37分放置で発生）
    '
    ' 【重要】失敗を、空の配列("[]")で返してはいけない。
    ' クライアントから見ると「取得に成功して0件だった」と区別が付かず、
    ' DBが一瞬詰まっただけで画面のログを全部消してしまう
    ' （利用者からは「行数を変えた瞬間に、会話が全部消えた」ように見える）
    '
    ' 【EnableSessionを付けていないのは、書き忘れではない】
    ' この関数はSessionを一切見なくなったため、有効にする理由が無い。
    ' そして、無効にしておくこと自体が歯止めになる——
    ' 将来ここでうっかりSessionを読もうとしても、Nothingにしかならず、
    ' 「読めているつもりで古い部屋を掴む」という形には戻れない。
    ' 付いていた頃の副作用（呼ぶたびにSessionの有効期限が延びる）も無くなるが、
    ' それはキープアライブ（4分おき）が本来の担当である
    ' ============================================
    '
    ' 【中身は GetLogEntriesCore。ここは包みだけ】
    ' WebMethod で漏れた例外は Application_Error に来ないので、ErrorReport.GuardWebMethod で
    ' 知らせてから投げ直す（画面から見た振る舞いは変わらない）
    <WebMethod()>
    Public Shared Function GetLogEntries(messageCount As Integer) As String
        Return ErrorReport.GuardWebMethod("ChatRoom.GetLogEntries", Function() GetLogEntriesCore(messageCount))
    End Function

    Private Shared Function GetLogEntriesCore(messageCount As Integer) As String

        ' 【必須】WebMethodはShared（static）関数のため、
        ' ChatPageBase.OnInit()のCookieチェックを素通りしてしまう。
        ' プロフィールが揃っていない相手にログを渡さないよう、ここで改めて確認する
        If Not ChatCommon.IsChatProfileComplete() Then
            Return LogFetchRejected(
                "名前とアイコンが確認できませんでした。お手数ですが、決め直してください。",
                ChatCommon.TopPageUrl)
        End If

        ' 自分の発言かどうかの判定と、どの部屋に居るかの照合に使うBrowserId
        ' （Page_Loadと同じ取り方）
        Dim browserId As String = Nothing
        Dim browserCookie As HttpCookie = HttpContext.Current.Request.Cookies("ChatBrowserId")
        If browserCookie IsNot Nothing Then
            browserId = browserCookie.Value
        End If

        If Not ChatProfileValidator.IsValidBrowserId(browserId) Then
            Return LogFetchRejected(
                "ブラウザの識別情報が読み取れませんでした。お手数ですが、名前とアイコンを決め直してください。",
                ChatCommon.TopPageUrl)
        End If

        ' === どの部屋のログかは、ハブの参加者リストから引く ===
        '
        ' 【Sessionを見ないこと】
        ' 以前は Session("CurrentRoomId") を根拠にしていたが、Sessionは20分で消え、
        ' アプリが寝ても消える。長時間の凍結から復帰した利用者は、再接続にも
        ' 発言にも成功しているのに、ここだけが失敗して、切断中の発言が
        ' 永久に画面へ出ない状態になっていた。
        '
        ' 在室しているかどうかを本当に知っているのはハブの参加者リストで、
        ' Sessionはその弱い写しでしかなかった。
        ' SendMessageがroomIdを受け取るのをやめ、JoinRoomがbrowserIdを
        ' 受け取るのをやめたのと、同じ寄せ方である
        '
        ' 【クライアントに部屋IDを送らせないことは、今までどおり】
        ' 送らせると、入室していない部屋のログまで読めてしまう
        Dim roomId As Integer = 0

        If Not ChatHub.TryGetRoomIdByBrowserId(browserId, roomId) Then

            ' ハブが知らない＝本当にどの部屋にも居ない。
            ' このまま画面に留めても、ログは二度と取り直せない
            ' （文言はPage_Loadと共通。ChatCommonが1つだけ持つ）
            Return LogFetchRejected(ChatCommon.NotInRoomMessage, ChatCommon.LoungeUrl)
        End If

        ' 【必須】その部屋が今も開いているかを、ここでも確かめる。
        '
        ' 【なぜ、Page_Loadで止めているのに、ここにも要るのか】
        ' Page_Loadで塞いだのは「部屋のページを開く」経路だけである。この関数はWebMethodで、
        ' 古いSessionを持ったまま直接叩けばPage_Loadを通らない。また、ページを開いたあとに
        ' 部屋が終了した場合も、この関数はそのまま呼ばれ続ける。
        ' 根はPage_Loadとまったく同じで、Session("CurrentRoomId")を
        ' 「今も生きている部屋に居る証拠」として信用してしまっていた点にある。
        ' 片方だけ塞ぐと「もう大丈夫」と思い込む状態が残るため、同じ回で両方塞いだ
        '
        ' 【フェイルオープン】問い合わせに失敗したときは、通す。
        ' DBが一瞬詰まっただけで会話が読めなくなるほうが深刻なため
        ' （ChatHub.TryGetRoomStateと同じ向き。この関数自身が、失敗時に "null" を返して
        '   今の表示をそのまま残す作りになっているのとも揃う）
        Dim isRoomOpen As Boolean = False

        If TryGetRoomIsOpen(roomId, isRoomOpen) AndAlso Not isRoomOpen Then

            ' 部屋が終了している。これも、ここに居続けても直らない
            Return LogFetchRejected(ChatCommon.RoomClosedMessage, ChatCommon.LoungeUrl)
        End If

        ' 【必須】行数は必ず白リストで丸める。
        ' 画面のセレクトボックス以外の経路から極端な値を送られても困らないようにするため
        ' （GetRecentMessagesの中でも丸めているが、ここでも明示しておく）
        Dim count As Integer = ChatLogReader.NormalizeMessageCount(messageCount)

        Try

            ' 読み出しは、Page_Loadと同じChatLogReaderの関数を使う。
            ' 【重要】ここに独自の読み出し処理を書かないこと。
            ' 「多めに取って、画面に出ない行を落としてからN行に切り詰める」という
            ' 取り方は微妙な条件の積み重ねのため、二重に書くと必ず食い違う。
            ' 将来「もっと過去を読む」ボタンを足すときも、同じ関数を使い回すこと
            Dim entries = ChatLogReader.GetRecentMessages(roomId, count, browserId)

            ' 【こっそりも、同じ1回の呼び出しで返す】
            ' 別のWebMethodに分けると、往復が2回になるうえ、
            ' 片方だけ成功した状態（部屋のログは新しいのに、窓の中は古い）が作れてしまう。
            ' 一緒に返せば、常にどちらも同じ瞬間のものになる
            Dim privateEntries = ChatLogReader.GetRecentPrivateMessages(roomId, count, browserId)

            ' ToScriptJsonは、不等号の開き文字をJSONのユニコードエスケープへ置き換える。
            ' これはJSONとして正しいエスケープのため、クライアントがJSON.parseすれば
            ' 元の文字に戻る。Ajax経由でも問題は無く、むしろ埋め込み用と同じ経路に
            ' 揃えておくほうが安全
            Return LogFetchSucceeded(ChatLogReader.ToScriptJson(entries),
                                     ChatLogReader.ToScriptJson(privateEntries))

        Catch ex As Exception

            ' 取り直しに失敗しても、チャット本体は止めない。
            ' クライアントは今出ているログをそのまま残す
            ' （中途半端に消すより、古いままでも見えているほうがまし）
            '
            ' 【ここだけは「一時的」であること】
            ' DBの詰まりは次に呼べば直る可能性がある。理由を付けてラウンジへ戻すと、
            ' DBが一瞬詰まっただけで在室者が部屋から追い出されることになる
            ' （TryGetRoomIsOpenをフェイルオープンにしてあるのと、同じ向き）
            '
            ' 黙ってはいない（それまでは Debug.WriteLine で、本番では消えていた）
            ErrorReport.Report(ex, "ChatRoom.GetLogEntries（過去ログの取り直し）", "部屋 " & roomId)

            Return LogFetchTemporaryFailure()

        End Try

    End Function


    ' === GetLogEntriesの戻り値を組み立てる（3つの形） ===
    '
    ' 【文字列を直接つないでJSONを作っている】
    ' 今のところ出すのは、固定の文言と行き先だけである。
    ' それでもエスケープを通しているのは、将来ここに動的な文字列
    ' （部屋名など）を混ぜたときに、引用符ひとつでページが壊れるのを防ぐため。
    ' HttpUtility.JavaScriptStringEncode は、第2引数をTrueにすると
    ' 引用符ごと組み立ててくれる（Default.aspx.vb の ExistingIconTypeJs と同じ形）

    Private Shared Function LogFetchSucceeded(entriesJson As String,
                                              privateEntriesJson As String) As String

        Return "{""entries"":" & entriesJson &
               ",""privateEntries"":" & privateEntriesJson & "}"

    End Function

    ' 一時的な失敗。クライアントは今の表示をそのまま残す
    '
    ' 【privateEntries も null にすること】
    ' 空の配列にすると、クライアントは「成功して0件」と読んで、
    ' 開いている窓の中身を全部消してしまう
    Private Shared Function LogFetchTemporaryFailure() As String
        Return "{""entries"":null,""privateEntries"":null}"
    End Function

    ' 恒久的な失敗。理由と行き先を添える。
    ' クライアントは理由をsessionStorageへ預けてから移動し、移動先で帯として出す
    '
    ' 【なぜSession経由で運ばないのか】
    ' 宿題2では、終了した部屋の理由をSessionに入れてラウンジへ運んでいる。
    ' ここでは使えない。Sessionが切れていること自体が、断る理由の1つだからである
    Private Shared Function LogFetchRejected(reason As String, redirectUrl As String) As String

        Return "{""entries"":null,""privateEntries"":null,""rejectReason"":" &
               HttpUtility.JavaScriptStringEncode(reason, True) &
               ",""rejectRedirectUrl"":" &
               HttpUtility.JavaScriptStringEncode(redirectUrl, True) & "}"

    End Function


    ' 部屋が「今も開いているか」を、DBへ1回だけ問い合わせる
    '
    ' 戻り値 … 問い合わせそのものに成功したかどうか
    ' isOpen … 行があり、IsActive = 1 なら True。行が無ければ False のまま
    '           （＝存在しない部屋も、終了した部屋も、同じくFalse）
    '
    ' 【呼び出し側が、戻り値とisOpenを分けて見ること】
    ' 「問い合わせられなかった」と「部屋がもう無い」は別物である。
    ' 前者で断るとDBの一瞬の詰まりで機能が止まる（フェイルクローズ）。
    ' ChatHub.TryGetRoomStateが同じ形をしているのは、同じ理由による
    '
    ' 【既知の重複】同じ問い合わせが、ほかに2か所ある
    ' （ChatHub.TryGetRoomState＝定員も一緒に取る版、
    '   Lounge.EnterRoom＝COUNT(*)でWHEREに IsActive = 1 を入れた版）。
    ' 1か所にまとめるなら、置き場所を決めたうえで3か所同時に寄せること。
    ' 中途半端に2か所だけ寄せると、残った1か所が忘れられる
    Private Shared Function TryGetRoomIsOpen(roomId As Integer, ByRef isOpen As Boolean) As Boolean

        isOpen = False

        Try

            Dim connStr As String =
                ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(
                    "SELECT IsActive FROM ChatRooms WHERE RoomId = @RoomId", conn)

                    cmd.Parameters.AddWithValue("@RoomId", roomId)

                    conn.Open()

                    Dim result As Object = cmd.ExecuteScalar()

                    If result IsNot Nothing AndAlso Not Convert.IsDBNull(result) Then
                        isOpen = CBool(result)
                    End If

                    ' 行が無ければ、isOpenはFalseのまま（＝存在しない部屋）

                End Using
            End Using

            Return True

        Catch ex As Exception

            ' 通す（フェイルオープン）ことは変えないが、黙っては通さない
            ErrorReport.Report(ex, "ChatRoom.TryGetRoomIsOpen", "部屋 " & roomId)

            Return False

        End Try

    End Function


    ' ============================================
    ' 「退室」ボタンが押された時に呼ばれる処理
    ' ChatHub側のLeaveRoom（参加者リストからの削除・グループからの離脱・
    ' 残りの参加者への通知）とは別に、こちらはASP.NETのSessionをクリアするための処理
    ' （SignalRのHubからは、ASP.NETのSessionへ直接アクセスできないため、
    '   WebMethodとして別途用意している）
    '
    ' EnableSession:=True を明示している。
    ' WebMethodは既定ではセッションが有効になっておらず、
    ' Sessionを触るなら、この指定が必要なため
    ' ============================================
    <WebMethod(EnableSession:=True)>
    Public Shared Function LeaveRoom() As String

        ' 漏れた例外は ErrorReport へ知らせてから投げ直す（GetLogEntries と同じ包み）
        Return ErrorReport.GuardWebMethod("ChatRoom.LeaveRoom",
            Function()

                ' WebMethodはShared（static）関数のため、
                ' ChatPageBase.OnInit()のCookieチェックを素通りしてしまうが、
                ' 退室処理はプロフィールの有無に関わらず行ってよいため、ここでのチェックは行わない
                HttpContext.Current.Session.Remove("CurrentRoomId")

                ' 席も外す。退室ボタンは SignalR を待たずにここへ来ることがある
                ' （再接続中のまま止まっていた、など）。ハブの LeaveRoom が先に済んでいれば、何も起きない。
                ' 本人確認は Cookie（ChatHub.LeaveRoomByBrowserId のコメント）
                Dim browserIdCookie = HttpContext.Current.Request.Cookies("ChatBrowserId")
                If browserIdCookie IsNot Nothing AndAlso Not String.IsNullOrEmpty(browserIdCookie.Value) Then
                    ChatHub.LeaveRoomByBrowserId(browserIdCookie.Value)
                End If

                Return "成功"

            End Function)

    End Function

End Class