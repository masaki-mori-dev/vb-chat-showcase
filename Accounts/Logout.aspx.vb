' アカウントのログアウトの受け口
'
' /account/logout として呼ばれる。画面としては何も出さず、札を消してログインの画面へ返すだけ。
' Chat/Logout.aspx と同じ形である。
'
' 【AccountPageBase は継承しない】
' あちらは「ログインしていなければログインの画面へ」転送する。
' 継承すると、札が壊れている人がここへ来た瞬間に転送され、**壊れた Cookie が消えないまま終わる**
' （Chat/Logout.aspx が ChatPageBase を継承していないのと、まったく同じ理由）。
'
' 【消すのは、ログインの札1つだけ】
' チャットの Cookie（名前・アイコン・通知音など）には触らない。
' 逆向き（チャットのログアウト）は、届いた Cookie を全部消すので、こちらの札も消える——
' 承知のうえでそうしている（Accounts/AccountSession.vb の先頭のコメント）
Public Class AccountLogoutPage
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【キャッシュさせない】
        ' キャッシュされると、2度目のログアウトがサーバーへ届かない
        ' （押しても何も起きないように見える。Chat/Logout.aspx と同じ）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        AccountSession.SignOut(Context)

        ' 【この Redirect は、既定（endResponse = True）で呼ぶこと】
        ' False にすると出力を止められず、302の本文のあとにページ全文が続く。
        ' そして endResponse = True は ThreadAbortException を投げるため、
        ' **この行を Try/Catch の中に置いてはいけない**（嘘のエラー記録が出る）
        '
        ' 【Cookieを消す指示は、この転送に巻き込まれない】
        ' Redirect が捨てるのは出力の中身で、Response.Cookies は別に持たれている
        ' （チャットのログアウトで実測済み）
        Response.Redirect(AccountPageBase.LoginUrl)

    End Sub

End Class
