Imports System.IO

Module ChatCommon


    ' 部屋へ入れない・部屋がもう無いときに、利用者へ見せる文言
    '
    ' 【なぜ、共通の定数にしているのか】
    ' 同じ場面を2か所で断っている。ChatHub.JoinRoom（ハブを直接叩かれた場合の防御）と、
    ' ChatRoom.aspx.vb の Page_Load（画面の導線）である。
    ' 文言をそれぞれに書くと、片方だけ直したときに、同じ理由なのに違う説明が出る。
    ' 「防御と導線の両方が要る」という関係は下の IsChatProfileComplete と同じなので、
    ' 置き場所も揃えてある
    '
    ' 【存在しない部屋と、終了した部屋を、あえて同じ文言にしている】
    ' 普通の利用者はどちらにも到達しないため、区別する価値が無い。
    ' それどころか区別すると「その番号の部屋が存在するかどうか」を外から
    ' 確かめられることになる（詳しい理由は ChatHub.JoinRoom のコメント）
    Public Const RoomClosedMessage As String = "この部屋は終了しました。"

    ' 部屋の情報（鍵・入室禁止）を確かめられなかったので、入室を断ったとき（ChatHub.JoinRoom）
    ' DB が一時的に読めないときに出る。待てば入れるようになるので、そう伝える
    Public Const RoomStateUnknownMessage As String = "いま部屋の情報を読めません。少し待ってから入り直してください。"


    ' 部屋に居ることを確かめられなかったときに、利用者へ見せる文言
    '
    ' 断る場所は2か所ある。ChatRoom.aspx.vb の Page_Load（部屋のページを開いたとき）と、
    ' GetLogEntries（過去ログの取り直し）である。RoomClosedMessage と同じ理由で、
    ' 文言を2か所に書かず、ここから参照する
    '
    ' 【どういうときに出るか】
    ' 退室したあとにURLを直接叩いた／長時間の凍結でSessionが切れ、そのうえで
    ' ハブの参加者リストからも既に外れていた（無言タイムアウト・アプリの再起動）、など。
    ' いずれも「今このブラウザは、どの部屋にも居ない」が共通している
    Public Const NotInRoomMessage As String =
        "部屋に入っていない状態になりました。お手数ですが、入り直してください。"


    ' ログアウトで席を外されたときに、開いたままの部屋の窓へ見せる文言
    '
    ' 【誰が見るのか】
    ' ログアウトを押した窓ではない。**別の窓で部屋を開いたままにしていた自分**である。
    ' 押した窓はそのままトップページへ行くので、この文言を見ることはない。
    '
    ' 【なぜ、これだけ「ログアウト」と理由を書くのか】
    ' 部屋のお知らせ行（ChatEventText）では、理由を添えないと決めている——
    ' 周りの人にとっては単なる退室だからである。
    ' こちらは**本人にだけ届く**ので、逆に理由が要る。
    ' 「触っていない窓が、急に部屋から出された」ように見えるのを防ぐ
    ' （無言タイムアウトの Kicked に、時間を含んだ文言を残してあるのと同じ考え方）
    ' 【文言は「チャットを出る」でそろえてある】
    ' アカウントの「ログアウト」と区別がつかなくなるため、チャット側では「ログアウト」と言わない
    Public Const LoggedOutMessage As String = "チャットを出たため、退室しました。"


    ' 退室・入室拒否の理由を、次の画面（ラウンジ）へ運ぶための Session のキー名
    '
    ' 書くのは ChatRoom.aspx.vb の Page_Load、読むのは Lounge.aspx.vb。
    ' キー名を打ち間違えても例外は出ず、ただ「理由が出ない」だけになって気づけないため、
    ' 文字列を2か所に書かず、ここから参照する
    '
    ' 【なぜ Session で運ぶのか（sessionStorage ではなく）】
    ' 部屋の画面のJSが理由を運ぶ経路（sessionStorage）は、サーバーからは書けない。
    ' サーバー側で断る今回のような場面では、別の運び方が要る。
    ' 表示はラウンジの既存の帯にそのまま合流させてあるので、増えたのは運び方だけである
    Public Const LeaveReasonSessionKey As String = "LeaveReason"


    ' 断ったときに、利用者を戻す先
    '
    ' 【なぜ、共通の定数にしているのか】
    ' 断る場所が増えるたびに、戻す先の文字列も増える。
    ' ChatHub.JoinRoom（ハブを直接叩かれた場合）と、
    ' ChatRoom.aspx.vb の GetLogEntries（過去ログの取り直し）が、
    ' それぞれ同じ2つの行き先を使う。
    ' RoomClosedMessage を1か所にしたのと、まったく同じ理由である
    '
    ' 【末尾にスラッシュを付けないこと】
    ' WebMethodの呼び出し（物理パスを直接叩く形）が、
    ' 末尾スラッシュ付きのURLから呼ぶと404になる
    Public Const LoungeUrl As String = "/chat/lounge"
    Public Const TopPageUrl As String = "/"



    ' 名前・アイコンのCookieが揃ってるか確認し、
    ' 揃ってなければトップページへ強制的に転送する
    ' チャット関連ページ（ChatPageBase継承ページ）から自動的に呼ばれる
    Public Sub RequireChatProfile()

        If Not IsChatProfileComplete() Then
            HttpContext.Current.Response.Redirect("~/")
        End If

    End Sub



    ' 名前・アイコンのCookieが揃っていて、かつ**名前の札が有効か**を判定するだけの関数
    ' RequireNickname()と違って、こっちはリダイレクトはせず、
    ' TrueかFalseの結果を返すだけ（呼び出し側で、その後の処理を自由に決められる）
    '
    ' 【Cookieが「有る」だけでは足りない】
    ' 以前はCookieの存在しか見ていなかったため、ChatNicknameが空のCookieを持っていると、
    ' 名前が無いままラウンジにも部屋にも入れてしまった。
    ' 名前は本人が決めるものとし、こちらで「ゲスト」のような既定名は付けない方針のため、
    ' 名前が空の人は、ここで止めてトップページ（名前を決める画面）へ戻す
    ' （ChatHub.JoinRoomでも同じ判定をしている。あちらはハブを直接叩かれた場合の防御で、
    '   こちらは画面の導線。両方要る）
    '
    ' 【、名前の札（ChatNameToken）も見るようにした】
    ' 上の3つのCookieは、どれもJSから自由に書ける。それだけを条件にしていると、
    ' 名前のCookieを書き換えるだけで「名前を変えたら別人」の決まりが裏口から破れる。
    ' 札は「この ChatBrowserId の名前はこれ」をサーバーが署名したもので、
    ' **名前の出どころはこれ1つにしてある**（Chat/NameToken.vb）。
    '
    ' 【3つのCookieのチェックも、そのまま残してある】
    ' 名前の正は札だが、画面のJavaScript（部屋のプロフィールバーなど）は今までどおり
    ' Cookieを読む。ここで札だけを条件にすると、ChatNicknameを手で消した人が
    ' 部屋まで入れてしまい、**名前が null のまま描かれる**。
    ' 「札が有る」ことと「画面が描ける」ことは別の条件なので、両方見る
    '
    ' 【札が無い人は、トップページで名前を決め直すことになる】
    ' 札の鍵を作り直した回（NameToken を参照）は、全員が一度ここで False になる。
    ' そのときトップページは、名前とアイコンをCookieから読んで**選択済みで出す**ので、
    ' 押すのは「決定する」の1回だけで済む（Default.aspx.vb の HasProfileCookies）
    Public Function IsChatProfileComplete() As Boolean

        If Not HasProfileCookies() Then
            Return False
        End If

        Return Not String.IsNullOrEmpty(NameToken.ReadNickname(HttpContext.Current.Request))

    End Function


    ' 名前・アイコン・色のCookieが揃っているか（札は見ない）
    '
    ' 【何に使うのか】トップページが、入力欄とアイコンを「前回の値」で出すため。
    ' 札だけが無い人（鍵を作り直した回など）にも、前の名前とアイコンを出したい。
    ' **入れてよいかどうかの判定には使わないこと**——それは上の IsChatProfileComplete である
    Public Function HasProfileCookies() As Boolean

        Dim nicknameCookie As HttpCookie = HttpContext.Current.Request.Cookies("ChatNickname")

        If nicknameCookie Is Nothing Then
            Return False
        End If

        ' Cookieの値はencodeURIComponentで書かれているため、比べる前に元へ戻す
        ' （%20だけ、のような「復号すると空白だけ」の値を見逃さないため）
        Dim nickname As String = HttpUtility.UrlDecode(nicknameCookie.Value)

        If String.IsNullOrEmpty(nickname) OrElse nickname.Trim().Length = 0 Then
            Return False
        End If

        Return (HttpContext.Current.Request.Cookies("ChatIconType") IsNot Nothing) _
        AndAlso (HttpContext.Current.Request.Cookies("ChatIconColor") IsNot Nothing)

    End Function






End Module
