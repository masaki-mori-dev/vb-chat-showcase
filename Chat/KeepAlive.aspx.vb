' アプリケーションプールが「アイドル状態」と判断されて停止するのを防ぐための、
' キープアライブ専用ページ
'
' チャットルームのJavaScriptから、一定間隔で呼び出される。
' 表示する内容は無く、"ok"とだけ返す
'
' ChatPageBaseは継承していない。名前・アイコンのCookieチェックは不要なため
' （このページは画面として使うものではなく、リクエストを発生させることだけが目的）
'
' 【重要】あえて通常の.aspxにしている。
' ASP.NETのセッション状態が自動的に有効になるため、このリクエストが飛ぶたびに、
' アプリケーションプールのアイドルタイマーだけでなく、
' Sessionのタイムアウト（既定20分・スライド式）も同時に延長される。
' 静的ファイルや、セッションを使わないハンドラ（.ashx）にすると、
' Sessionの延長という効果が得られなくなる
Public Class KeepAlive
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' ブラウザやプロキシにキャッシュさせない。
        ' キャッシュされると、2度目以降のリクエストが実際にはサーバーへ届かず、
        ' キープアライブとしての意味が無くなってしまうため
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
