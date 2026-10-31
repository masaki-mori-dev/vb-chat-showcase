' ログアウトの受け口
'
' ラウンジの「ログアウト」ボタン（Chat/Lounge.aspx）から、/chat/logout として呼ばれる。
' 画面としては何も出さない。捨てて、トップページへ返すだけである
'
' 【ChatPageBase は継承しない】
' あちらは OnInit で「名前・アイコンのCookieが揃っていなければトップへ転送」する。
' 継承すると、Cookieが欠けている人はここへ来た瞬間に転送され、
' **残りのCookie（表示行数・通知音など）が消えないまま終わる**。
' KeepAlive.aspx・LeaveBeacon.aspx と同じ扱いにしてある
'
' 【あえて通常の .aspx にしている】
' .ashx（ハンドラ）では、ASP.NETのセッション状態が既定で有効にならない。
' このページは Session を捨てるのが仕事なので、それでは用が足りない
' （KeepAlive.aspx が .aspx である理由と、まったく同じ）
'
' 【捨てる処理そのものは ChatLogout が持つ】
' 近い将来 C#／MVC へ移るときに、この受け口は捨ててコントローラーのアクションへ
' 置き換わる。判断をこちらに書かないこと
Public Class Logout
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' ブラウザやプロキシにキャッシュさせない。
        ' キャッシュされると、2度目のログアウトが**サーバーへ届かない**
        ' （押しても何も起きないように見える）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        ChatLogout.SignOut(Context)

        ' 【この Redirect は、既定（endResponse = True）で呼ぶこと】
        ' False にすると出力を止められず、302の本文のあとにページ全文が続く。
        ' そして endResponse = True は ThreadAbortException を投げるため、
        ' **この行を Try/Catch の中に置いてはいけない**（嘘のエラー記録が出る）
        '
        ' 【Cookieを消す指示は、この転送に巻き込まれない】
        ' Redirect が捨てるのは出力の中身で、Response.Cookies は別に持たれている。
        ' そのため Set-Cookie は302と一緒に送られ、ブラウザは
        ' 「Cookieを捨ててから、トップページを取りに行く」順で動く
        ' （開発機の実機で確認済み）
        Response.Redirect(ChatCommon.TopPageUrl)

    End Sub

End Class
