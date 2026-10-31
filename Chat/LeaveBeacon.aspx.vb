' ページが閉じられる瞬間に、退室を受け付けるための専用ページ
'
' チャットルームのJavaScriptから、pagehideイベント＋navigator.sendBeaconで呼ばれる。
' は、ラウンジの画面も lounge=1 を付けて同じ形で呼ぶ。
' 表示する内容は無く、"ok"とだけ返す
'
' 【なぜ、このページが要るのか】
' SignalRは「今から閉じます」の通知にunloadイベントを使っているが、
' Chromeがunloadを廃止に向けて制限しており（コンソールに
' 「unload is not allowed in this document」の警告が出る）、
' ウィンドウごと閉じた場合には通知が届かないことが本番で確認できた。
' その結果、OnDisconnectedがstopCalled=Falseで呼ばれ、
' 「閉じた」のに「通信が切れただけ」と判定されてしまう。
' そこでunloadに頼らず、閉じられる瞬間の通知用に作られたpagehideとsendBeaconで、
' サーバーへ直接「退室します」と伝える
'
' ChatPageBaseは継承していない。名前・アイコンのCookieチェックは不要なため
' （このページは画面として使うものではなく、退室を受け付けることだけが目的）
Public Class LeaveBeacon
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' このリクエストが飛んでくる時、ブラウザ側のページは既に閉じられている。
        ' 例外が起きても、利用者に伝える手段も、やり直す手段も無いため、
        ' 全体をTry/Catchで囲み、失敗しても静かに終わるようにしている
        Try

            ' どの接続が閉じられたか（クエリ文字列で受け取る）
            '
            ' 【なぜ、これが要るのか】
            ' 以前はBrowserIdだけをサーバーへ伝えていた。しかしF5の際、
            ' 新しいページのJoinRoomが、このビーコンを追い越して先に届くことがある。
            ' そうなると、BrowserIdで探した先に居るのは「入れ替わったあとの新しい参加者」で、
            ' 生きている接続を掴んで退室させてしまう。
            ' しかも本人の画面には何の変化も出ないため、誰も気づけない。
            ' 詳しい順序は ChatHub.LeaveByConnectionId のコメントに書いてある。
            '
            ' そのため「このブラウザが抜けます」ではなく
            ' 「この接続が抜けます」と伝えるようにしている
            Dim connectionId As String = Request.QueryString("connectionId")
            If String.IsNullOrEmpty(connectionId) Then
                ' どの接続の話か分からないため、何もしない
                Exit Try
            End If

            ' 誰が退室するか（Cookieで受け取る）
            ' sendBeaconはCookieを付けて送るため、こちらで受け取ることができる。
            '
            ' 接続IDだけを鍵にすると、他人の接続IDを送りつけるだけで、その人を部屋から
            ' 追い出せてしまう。接続IDは「どの接続か」の特定に使い、
            ' このCookieは「本人かどうか」の確認に使う（照合はChatHub側で行う）
            Dim browserIdCookie = Request.Cookies("ChatBrowserId")
            If browserIdCookie Is Nothing OrElse String.IsNullOrEmpty(browserIdCookie.Value) Then
                Exit Try
            End If

            ' === ラウンジを離れる合図===
            '
            ' ラウンジの画面（Scripts/lounge-live.js）が、ページを離れるときに lounge=1 で送ってくる。
            ' 部屋ではないので roomId は無い。本人確認は部屋と同じ（接続ID ＋ Cookie）。
            ' なぜ要るのかは ChatHub.LeaveLoungeByConnectionId のコメントにある
            '
            ' 【別のページにしなかった理由】「ページを離れる瞬間の合図を受ける」という役目が同じで、
            ' 受け取るもの（接続ID・Cookie）と、キャッシュさせない・例外を外へ出さない作りも同じ。
            ' ページを分けると、その作りを2か所で持つことになる
            If Request.QueryString("lounge") = "1" Then
                ChatHub.LeaveLoungeByConnectionId(connectionId, browserIdCookie.Value)
                Exit Try
            End If

            ' どの部屋から退室するか（クエリ文字列で受け取る）
            Dim roomId As Integer
            If Not Integer.TryParse(Request.QueryString("roomId"), roomId) Then
                ' 数値として取れない場合は、誰を退室させるべきか判断できないため何もしない
                Exit Try
            End If

            ' 参加者リストから外し、同じ部屋の残りの参加者へ退室を通知する
            ChatHub.LeaveByConnectionId(roomId, connectionId, browserIdCookie.Value)

            ' 【罠】ここで Session("CurrentRoomId") = Nothing としてはいけない。
            '
            ' F5でリロードした場合も pagehide は発火し、このビーコンが飛んでくる。
            ' サーバー側からは「タブを閉じた」のか「F5した」のかを区別できないため、
            ' ここでSessionを消すと、F5のたびに毎回Sessionが消えてしまう。
            '
            ' しかも症状が「1回ずれて」出るため、原因が分かりにくい。
            '   1. F5 → pagehide → ビーコン送信（非同期）
            '   2. ブラウザが新しいページを要求 → Page_LoadのSessionチェックを通過
            '      （この時点では、まだSessionが残っている）
            '   3. そのあとビーコンが処理され、Sessionが消える
            '   4. 2度目のF5で、Page_Loadのチェックに引っかかりラウンジへ飛ばされる
            '
            ' 消さないことで失うのは「タブを閉じた人が、ラウンジを経由せず
            ' URL直打ちで部屋へ戻れてしまう」ことだけ。
            ' そもそもSessionを消すことは、定員の防御にはならない。定員は
            ' ChatHub.JoinRoomがサーバー側で判定しており、部屋のページを開けたかどうかとは
            ' 関係なく、満員なら入室そのものが断られる。
            ' 在室の後始末の最終防衛線は、最終発言から1時間の無言タイムアウトが引き続き担う

        Catch ex As Exception
            ' 退室処理に失敗しても、ここでできることは無いため握りつぶす。
            ' 万一取りこぼしても、最終発言から1時間の無言タイムアウトが保険として働く。
            '
            ' 【ただし、黙っては握りつぶさない】
            ' ここが壊れると、閉じた人が一覧に1時間居残る。利用者の画面には何も出ないので、
            ' 知らせが無いと誰も気づけない
            ErrorReport.Report(ex, "LeaveBeacon.Page_Load")
        End Try

        ' ブラウザやプロキシにキャッシュさせない。
        ' キャッシュされると、実際のリクエストがサーバーへ届かず、意味が無くなるため
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        Response.ContentType = "text/plain"
        Response.Write("ok")

        ' ここで応答を打ち切る。Response.End()は使わない。
        ' Response.End()は、内部でThreadAbortExceptionを発生させて処理を中断する仕組みで、
        ' ASP.NETが受け止めるため動作はするものの、呼ばれるたびに例外が発生してしまう。
        ' CompleteRequest()なら、例外を使わずに以降のパイプライン処理を飛ばせる
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
