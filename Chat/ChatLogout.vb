' 「ログアウト」＝ このブラウザの身元と設定を、まとめて捨てる処理
'
' 【何のための機能か】
' Cookieユーザーにとって「名前を変える」は、実質「ログアウトして入り直す」ことである。
' 名前を変えた人は、無視やこっそり話すの相手から見て別人になるため、
' 「見た目だけ変える」という道は用意していない（入口は1本だけ、という）。
'
' 【なぜ .aspx ではなく、クラスに置くのか】
' 近い将来 C#／ASP.NET Core MVC へ移る予定がある。そのとき .aspx は捨てるが、
' 「何を、どの順で捨てるか」という判断は、そのまま持っていきたい。
' 受け口（Chat/Logout.aspx）は、ここを呼んで転送するだけの数行にしてある。
' ChatLog.vb が「書き込みを1か所へ閉じ込めて、呼び出し側を変えずに差し替えられるように」
' しているのと、まったく同じ考え方である
'
' 【HttpContext.Current を使わず、引数で受け取る】
' 隣の ChatCommon は Current を直接読んでいるが、こちらは引数にしてある。
' 移った先（MVCのコントローラー）に HttpContext.Current は無いため、
' 引数で受ける形にしておけば、書き換えるのは中身だけで済む
Module ChatLogout


    ' このブラウザの身元と設定を、まとめて捨てる
    '
    ' | 捨てるもの | なぜ |
    ' | ハブの席 | 部屋の窓を開いたままログアウトすると、古い自分が居残る（下記） |
    ' | Session | Session("CurrentRoomId") が残っていると、ログアウトしたあとでも
    '             部屋のURLを直打ちすれば入れてしまい、導線から外れる |
    ' | 届いたCookie全部 | 身元（ChatBrowserId）と、名前・アイコン・通知音などの設定。
    '                      名前は並べない（下記）。**アカウントのログインの札だけは残す**（下記） |
    '
    ' 【sessionStorage と localStorage は、ここでは捨てられない】
    ' サーバーからは触れないため、Chat/Lounge.aspx のJSが、ここへ来る前に clear() している。
    ' 3つとも全消しにするのは、localStorage の
    ' 「よく使う絵文字」「最後に選んだタブ」も一緒に消える
    Public Sub SignOut(context As HttpContext)

        If context Is Nothing Then
            Return
        End If

        ' === ハブの席を外す（Cookieを消す「前」に） ===
        '
        ' 【順番が決まっている】
        ' 誰の席を外すのかは ChatBrowserId で決まるので、
        ' **その Cookie を消す前に読んでおくこと。**
        ' 下のループは届いた Cookie を全部消すので、あとからでは名乗る値が無くなる。
        '
        ' 【これをやらないと、席が永久に残る】
        ' ログアウトすると ChatBrowserId が新しくなり、
        ' 「同一ブラウザなら1人に絞る」置き換えの仕組みが効かなくなる。
        ' 詳しい経緯は ChatHub.LeaveByBrowserId のコメントにある。
        '
        ' 【失敗しても、ログアウトそのものは続ける】
        ' 席が外せなくても、Cookie と Session は必ず捨てる。
        ' ここで止まると「ログアウトを押したのに、何も消えない」という
        ' いちばん困る形になる（ログの書き込みがチャットを止めないのと同じ考え方）
        Dim browserIdCookie As HttpCookie = context.Request.Cookies("ChatBrowserId")

        If browserIdCookie IsNot Nothing Then
            Try
                ChatHub.LeaveByBrowserId(browserIdCookie.Value)
            Catch ex As Exception
                ' エラー報告メールへ繋いだ。それまでは Trace だけで、出力先が無かった。
                ' 席が外せないと、開いたままの部屋の窓に古い自分が1時間居残る
                ErrorReport.Report(ex, "ChatLogout.SignOut（ハブの席を外す）")
            End Try
        End If

        ' === ログアウトしたことを記録する（種別51） ===
        '
        ' 【これも Cookie を消す「前」に】誰のログアウトかは ChatBrowserId と名前の Cookie から読む。
        ' 席を外したあとに置いてあるのは、時系列で「部屋を出た（37）→ ログアウトした（51）」の順に並べるため。
        ' 1つの身元につき1日1回まで・例外は投げない（Chat/SiteVisitLog.vb）
        SiteVisitLog.RecordLogout(context)

        ' === Session を捨てる ===
        '
        ' Clear で中身を落としてから、Abandon で捨てる。
        '
        ' 【ASP.NET_SessionId が残っても、目的は達している】
        ' あのCookieも下のループで消すが、Sessionを片付ける処理はページのコードより
        ' あとで走るため、ASP.NETが同じIDを配り直すことがありうる。
        ' それでも Clear で中身は空なので、Session("CurrentRoomId") は戻らない。
        ' ここで要るのは「IDが消えること」ではなく「中身が消えること」である
        If context.Session IsNot Nothing Then
            context.Session.Clear()
            context.Session.Abandon()
        End If

        ' === 届いたCookieを、名前を並べずに全部消す ===
        '
        ' 【並べてはいけない】
        ' 今あるのは11個（ChatNickname / ChatIconType / ChatIconColor / ChatBrowserId /
        ' ChatLogCount / ChatSoundEnabled / ChatSoundVolume / ChatPrivateAutoOpen /
        ' ChatTripKey（トリップの合言葉。HttpOnly）/
        ' ChatJustLoggedIn（ログインの目印。寿命60秒。SiteVisitLog）/
        ' ChatNameToken（名前の札。HttpOnly。Chat/NameToken.vb））だが、
        ' ここに書き並べると、次にCookieを1つ足した人が、こちらへの追加を忘れる。
        ' そのとき起きるのは「別人になったつもりで、前の設定だけが残る」で、
        ' しかもエラーは出ない＝誰も気づけない。
        ' 届いたものを全部消す形にしておけば、その穴が最初から無い
        '
        ' 【消し漏れは原理的に起きない】
        ' 消せるのは「そのリクエストで届いたCookie」だけだが、消したいのは
        ' まさに「今このブラウザが送ってきているCookie」である。
        ' 送られてこないCookieは、そもそもこのサイトでは読まれない
        '
        ' 【アカウントのログインの札（AccountAuth）だけは、残す】（チャットとアカウントの紐づけ）
        ' それまでは、これも一緒に消えていた（＝チャットを出ると、アカウントからもログアウトした。承知のうえだった）。
        ' 紐づけができてからは、事情が変わった：
        '   ・ログインしている人は、トップページで「アカウントID を表示する／しない」を選んでチャットに入る。
        '     選び直すには、チャットを出て名前を決め直すしかない（名前を変えるのと同じ入口）。
        '     そのたびにアカウントからもログアウトしていたら、**選び直すたびにログインし直し**になる
        '   ・画面の言葉も、「ログアウト」から「チャットを出る」に変えてある。
        '     「チャットを出る」で、記事を書く画面のログインまで切れるのは、言葉と起きることが合わない
        '
        ' 【「消すものを並べる」形には戻していない】並べているのは**残すもの1つ**である。
        ' 次に Cookie を足した人がここを忘れても、起きるのは「チャットを出たら、それも消える」で、
        ' 今までと同じ安全な側に倒れる（「別人になったつもりで、前の設定が残る」は、今も原理的に起きない）。
        ' **残すものを増やすときは、それが「チャットの身元・設定」ではないことを確かめること**
        '
        ' 【残しても、古い身元のアカウントID は生き返らない】
        ' アカウントID を出すには名前の札（ChatNameToken）も要り、そちらはここで消える（Chat/ChatAccountLink.vb）
        For Each cookieName As String In context.Request.Cookies.AllKeys

            If String.IsNullOrEmpty(cookieName) Then
                Continue For
            End If

            If String.Equals(cookieName, AccountAuth.CookieName, StringComparison.Ordinal) Then
                Continue For
            End If

            Dim expired As New HttpCookie(cookieName, String.Empty)

            ' 【path を揃えないと消えない】
            ' Cookieの身元は「名前・ドメイン・path」の3つで決まる。揃っていないと、
            ' 消したつもりで**別のCookieを1つ作って終わる**（Scripts/chat-room.js が
            ' ChatPrivateSeeThrough を消すときに、同じ注意を書いてある）。
            ' このサイトは書くほうも path=/ で揃えてあるので、ここも / でよい
            expired.Path = "/"

            ' 過去の日付にすると、ブラウザはその場で捨てる
            expired.Expires = New DateTime(1999, 1, 1)

            ' 【Secure と SameSite は、消すための必須条件ではない】
            ' 身元は上の3つで決まるため、これらが違っていても消える。
            ' それでも書いてあるのは、**書いたときと同じ形に揃えておく**ためで、
            ' 片方だけ違う形が混ざると、次に読む人がどちらが正しいのか迷う。
            ' Secure を付けても届く（本番はhttps強制、開発機のlocalhostは
            ' ブラウザが安全な場所として扱う。Default.aspx の setCookie と同じ事情）
            expired.Secure = True
            expired.SameSite = SameSiteMode.Lax

            ' Add ではなく Set。Add は同じ名前でも並べて足すため、
            ' 二重に呼ばれた場合に Set-Cookie が2本出る
            context.Response.Cookies.Set(expired)

        Next

    End Sub


End Module
