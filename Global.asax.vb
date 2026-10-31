Imports System.Threading
Imports System.Web.Optimization

Public Class Global_asax
    Inherits HttpApplication

    ' 参加者タイムアウトチェック用のタイマー
    ' ローカル変数にすると、メソッド終了後にGCで回収され止まってしまうため、
    ' Sharedフィールドとしてアプリケーションの生存期間中ずっと保持し続ける
    Private Shared TimeoutCheckTimer As Timer

    Sub Application_Start(sender As Object, e As EventArgs)
        ' アプリケーションの起動時に呼び出されます
        RouteConfig.RegisterRoutes(RouteTable.Routes)
        BundleConfig.RegisterBundles(BundleTable.Bundles)

        ' Validator（asp:RequiredFieldValidator など）が使う jQuery を登録する
        ' （持ち越していた宿題）
        '
        ' 【登録しないと、Validator を置いたページが500で止まる】
        ' ASP.NET 4.5 以降の非侵入型検証は、"jquery" という名前の ScriptResourceMapping を要求する。
        ' それを登録していた AspNet.ScriptManager.jQuery パッケージは、宿題4（jQuery の版上げ）で
        ' 外してあるので、**自分で登録し直す必要がある**
        ' （「管理画面を作る回に、必ず戻ってくる場所」）。
        '
        ' 【黙って検証が効かない、という壊れ方にはならない】
        ' 登録し忘れると例外で止まり、メッセージが直し方を書いている。
        ' 「例外で止まる」ほうを、意図して選んである。
        '
        ' 【パスは Scripts/ の実物に合わせること】
        ' jQuery の版を上げたら、ここも直す。ファイル名に版が入っているので、
        ' common.AssetUrl（?v= を付ける仕組み）は通さなくてよい。
        ' **CdnPath は書いていない**――外から読み込ませない、というサイトの方針どおり
        ScriptManager.ScriptResourceMapping.AddDefinition(
            "jquery",
            New ScriptResourceDefinition With {
                .Path = "~/Scripts/lib/jquery-3.7.1.min.js",
                .DebugPath = "~/Scripts/lib/jquery-3.7.1.js",
                .LoadSuccessExpression = "window.jQuery"})

        ' 退室させるべき参加者がいないかを、定期的に見回るチェックを開始する
        ' 見るのは次の2つ（詳しくはChatHub.CheckTimeoutParticipantsを参照）
        '   ・退室予告（ページを閉じた／F5した）から、猶予を過ぎたもの
        '   ・最後の発言から、無言タイムアウトを過ぎたもの
        '
        ' 【タイマーの間隔は、猶予時間より十分に短くしておくこと】
        ' 以前は5分間隔だったが、それでは5秒の猶予（SiteSettings.LeaveGrace）を
        ' 検出するには粗すぎる。この噛み合わせは、SiteSettings側で
        ' 両方の上限・下限として守ってある（あちらのコメントを参照）
        '
        ' 走査するのはメモリ上の参加者リストだけで、DBへの書き込みは
        ' 実際に退室が発生したときにしか起きないため、2秒間隔でも負荷にはならない
        '
        ' 【ここでSiteSettingsを読むのは、例外的に許している】
        ' 「Application_Startでは設定を読まない（遅延読み込み）」と決めてあるが、
        ' タイマーの間隔だけは、タイマーを作る瞬間にしか決められない。
        ' ここでDBが詰まっていても、SiteSettingsはフェイルオープンで既定値（2秒）を
        ' 返すため、アプリが起動できなくなることはない
        '
        ' 【罠】間隔を変えても、アプリを再起動するまで効かない。
        ' タイマーはここで1回だけ作られるため、Reload()では変わらない。
        ' 管理画面から変えられるようにする際は、そのことを画面に書くこと
        Dim checkInterval As TimeSpan = SiteSettings.TimeoutCheckInterval

        TimeoutCheckTimer = New Timer(
            AddressOf OnTimeoutCheckTick,
            Nothing,
            checkInterval,
            checkInterval)

        ' アプリが止まる直前のエラーを、その場で知らせる
        '
        ' 【これは最後の手段】
        ' ここへ来るのは、どこにも捕まえられずにスレッドの外まで漏れた例外で、
        ' .NET はこのあとプロセスごと止める（在室者は全員切断される）。
        ' 並べて裏で送る余裕が無いので、ErrorReport.ReportFatal がその場で送る。届けば幸運。
        ' 本命は、漏らさないこと（下の OnTimeoutCheckTick が、その1つ）。
        '
        ' Application_Start は1つのアプリにつき1回しか走らないので、二重には登録されない
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
    End Sub


    ' 検索エンジンに載せない
    '
    ' 応答のヘッダー X-Robots-Tag: noindex, nofollow を、すべてのページに付ける。
    '
    ' 【<meta> ではなく、応答のヘッダーにした理由】
    '   ・置き場所が1か所で済む。Site.Master を使わない部屋のページ（Chat/ChatRoom.aspx）も、
    '     記事の公開ページも、これから足すページも、何もしなくても必ず付く。
    '     <meta> にすると <head> のある所すべてに書くことになり、
    '     **足し忘れたページだけが黙って検索に載る**（エラーは出ない）
    '   ・実行時に <meta> を足す形（Page.Header.Controls.Add）は、そもそも使えない。
    '     Site.Master の <head runat="server"> はコードブロック（<%: %>）を含むので、
    '     子を足した瞬間に全ページが500になる。
    '     ビルドの目印 X-VbChat-Build をヘッダーで出しているのと、まったく同じ理由である
    '   ・Web.config 本体に <system.webServer><httpProtocol> を作る形も採らない。
    '     本体に <system.webServer> を作ると、Web.Release.config の InsertIfMissing が
    '     「もう在る」と判断して、本番の https 強制と defaultDocument が黙って消える
    '     （「発行・ファイルまわり」の罠）
    '
    ' 【robots.txt で Disallow はしないこと】
    ' 読むことを禁じると、クローラーはページを取りに来ない＝この noindex 自体が見えなくなり、
    ' 逆にURLだけが検索結果に残る。「読ませたうえで、載せないでくれと伝える」のが正しい形。
    ' robots.txt は置いていない（無ければ「全部読んでよい」と解釈される）。
    '
    ' 【出ているかは発行後の確かめの道具が見る】
    ' ヘッダーは画面のソースに出ないので、目では確かめられない。
    ' 主だったページの Header に X-Robots-Tag を書いてある。
    '
    ' 【承知のうえの穴＝静的ファイルには付かない】（開発機で実測）
    ' .css / .js / .svg / 画像は IIS がそのまま返すので、ここは通らない。
    ' つまり **利用者が上げた画像・記事の画像は、検索避けが付いていない**。
    ' それでも困らないと判断した——どのページも noindex かつ nofollow なので、
    ' クローラーはページから画像へたどらず、外からリンクも張られていないため。
    ' 画像まで塞ぎたくなったら、Web.Release.config の <system.webServer> に
    ' <httpProtocol><customHeaders> を1つ足す（そちらは IIS が全部の応答に付ける）。
    ' **そのときは、このヘッダーが二重に付かないように、ここを消すこと**
    '
    ' 【検索に載せてよくなった日は、X-Robots-Tag の行（Try の1組）だけを消す】
    ' （住所のルールもこの Sub に同居している。Sub ごと消さないこと）
    Sub Application_BeginRequest(sender As Object, e As EventArgs)

        ' 【ここで例外を漏らさないこと】
        ' BeginRequest で漏れた例外は、**すべての要求**で Application_Error へ落ちる。
        ' サイト全体が止まり、そのうえ同じエラーの報告メールが積み上がる。
        ' 検索避けは飾りなので、万一失敗しても黙って付けないだけにしておく
        ' （付いていないことは発行後の確かめの道具が★NGで知らせるので、気づけなくはならない）。
        Try
            Response.AppendHeader("X-Robots-Tag", "noindex, nofollow")
        Catch
        End Try

        ' 住所は1つのページに1つ（住所のルール。中身は Core/CanonicalUrl.vb）。
        ' 実物の住所（/Accounts/Login.aspx など）で開かれたら、正しい住所へ 301（決められないものは 404）。
        ' 【検索避けの行とは別の仕事】検索に載せてよくなって上の行を消す日も、ここは残すこと。
        ' 【例外を漏らさない】上と同じ理由。失敗したら、何もせずにそのまま通す（今までどおり .aspx でも開けるだけ）
        Try
            CanonicalUrl.TryHandle(Context)
        Catch ex As Exception
            ErrorReport.Report(ex, "CanonicalUrl.TryHandle", Nothing)
        End Try

    End Sub


    ' 見回りのタイマー（2秒おき）の中身
    '
    ' 【必ず Try/Catch で包むこと（包んだ）】
    ' タイマー（System.Threading.Timer）の中で例外が1つでも漏れると、
    ' .NET は「スレッドの外まで漏れた例外」として、**プロセスごと落とす**。
    ' 在室者は全員切断され、しかもメールを送る暇も無い。
    ' それまでは包まれておらず、たまたま一度も起きていないだけだった。
    '
    ' 包んだので、漏れた例外は ErrorReport へ知らせて、次の2秒後にまた見回る。
    '
    ' 【1日1回の仕事も、ここに相乗りしている】（DailyJobs.vb）
    ' 共用サーバーには定期実行の機能が無いので、きっかけはこのタイマーの1本だけ。
    ' DailyJobs.Tick は時刻を比べるだけですぐ戻り、DB を見に行くときも裏の作業に頼む（ここでは待たない）。
    '
    ' 【2つは別々の Try で包む】片方が例外を出しても、もう片方は毎回動くようにするため
    ' （1つの Try に並べると、在室の見回りが壊れている間、1日1回の仕事まで一度も動かなくなる）
    Private Shared Sub OnTimeoutCheckTick(state As Object)

        Try
            ChatHub.CheckTimeoutParticipants()
        Catch ex As Exception
            ErrorReport.ReportUnhandled(ex, "見回りのタイマー（2秒おき。Global.asax）", Nothing,
                                        "ChatHub.CheckTimeoutParticipants")
        End Try

        Try
            DailyJobs.Tick()
        Catch ex As Exception
            ErrorReport.ReportUnhandled(ex, "見回りのタイマー（2秒おき。Global.asax）", Nothing,
                                        "DailyJobs.Tick")
        End Try

        ' 誰も居なくなった一時部屋を閉じる（ChatHub.Rooms.vb）
        '
        ' これも時刻を比べるだけですぐ戻り、DBを触るときは裏の作業に頼む（DailyJobs.Tick と同じ）。
        ' 【3つとも、別々の Try で包む】1つにまとめると、
        ' どれか1つが壊れている間、残りが一度も動かなくなる
        Try
            ChatHub.TickRoomCleanup()
        Catch ex As Exception
            ErrorReport.ReportUnhandled(ex, "見回りのタイマー（2秒おき。Global.asax）", Nothing,
                                        "ChatHub.TickRoomCleanup")
        End Try

        ' 管理人が部屋の設定を変えたあと、変更が止まった部屋に1行出す（ChatHub.RoomSettings.vb）。
        ' まとめが無ければ、何もせずすぐ戻る。これも別の Try で包む
        Try
            ChatHub.FlushDueRoomSettingsLogs()
        Catch ex As Exception
            ErrorReport.ReportUnhandled(ex, "見回りのタイマー（2秒おき。Global.asax）", Nothing,
                                        "ChatHub.FlushDueRoomSettingsLogs")
        End Try

        ' DB を直接書き換えたあとの合図を見て、メモリの控えを捨てる（Core/CacheReset.vb）。
        ' 30秒に1回だけ裏の作業に頼む。これも別の Try で包む
        Try
            CacheReset.Tick()
        Catch ex As Exception
            ErrorReport.ReportUnhandled(ex, "見回りのタイマー（2秒おき。Global.asax）", Nothing,
                                        "CacheReset.Tick")
        End Try

    End Sub


    ' どこにも捕まえられなかった例外（上の AddHandler を参照）
    Private Shared Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        ErrorReport.ReportFatal(TryCast(e.ExceptionObject, Exception))
    End Sub


    ' ページ（.aspx）で起きた未処理のエラー（追加）
    '
    ' 【ここに来ないものがある】
    ' ・SignalR のハブのメソッド → Chat/Hubs/ErrorReportHubModule.vb（Startup.vb で登録）
    ' ・WebMethod（$.ajax で呼ぶもの。で9本）→ ASP.NET が自分で捕まえて JSON の500で返すので、
    '   それぞれの WebMethod の中で Catch して知らせている
    ' ・見回りのタイマー → 上の OnTimeoutCheckTick
    '
    ' 【エラー画面の出し方は変えない】
    ' Server.ClearError() は呼ばない。customErrors（RemoteOnly）が、今までどおり画面を出す。
    ' ここは「知らせる」だけ。4xx（存在しないページ・ボット）は ErrorReport が捨てる
    Sub Application_Error(sender As Object, e As EventArgs)

        Dim ex As Exception = Nothing
        Dim path As String = Nothing

        Try
            ex = Server.GetLastError()

            Dim context As HttpContext = HttpContext.Current
            If context IsNot Nothing Then
                ' クエリ文字列は含めない（Path はパスだけ）
                path = context.Request.Path
            End If
        Catch
            ' 要求の情報が取れなくても、例外だけは知らせる
        End Try

        ' 「無い」（404・403）なら、サイトの見た目の「ページが見つかりません」を出して終わる（住所のルール。Core/CanonicalUrl.vb）。
        ' 4xx は ErrorReport も知らせない（IsNoise）ので、知らせる側からは何も失われない
        Try
            If CanonicalUrl.TryShowNotFound(Context, ex) Then
                Return
            End If
        Catch
            ' 出せなかったら、今までどおりの流れ（素のエラー画面）に任せる
        End Try

        ErrorReport.ReportUnhandled(ex, "ページ（Application_Error）", path, If(path, "（ページ）"))

    End Sub


End Class