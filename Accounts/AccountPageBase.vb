' アカウントのページ専用の、共通の土台となるクラス
'
' このクラスを継承したページは、画面が組み立てられる前に
' 「ログインしているか」が確かめられ、していなければログインの画面へ返される。
' Chat/ChatPageBase.vb とまったく同じ形である。
'
' 【WebMethod は、この OnInit を素通りする】
' ChatPageBase と同じ落とし穴なので、アカウントのページに WebMethod を足すときは、
' その中で改めて AccountSession.Current を呼ぶこと。
' （WebMethod を1つも作っていなかった。今は記事の編集画面に2本＝自動保存・控えを捨てる。どちらも中で確かめている）
Public Class AccountPageBase
    Inherits System.Web.UI.Page

    ' 「?」のボタンと小窓（文は Core/HelpTexts.vb の表の1か所）。markup からは <%= HelpButton("鍵") %> の1行だけ。
    ' 【markup から HelpTexts を直接呼ばない】別のコンパイル単位で、実行したときだけ500になりうる（ここを経由する）
    Protected Shared Function HelpButton(key As String) As String
        Return HelpTexts.ButtonHtml(key)
    End Function

    ' ログインの画面のURL
    '
    ' 【末尾にスラッシュを付けないこと】
    ' chat/lounge・chat/room と同じ決まりにそろえてある（「URL構成」）
    Public Const LoginUrl As String = "/account/login"

    ' ログインしたあとに行く先
    Public Const HomeUrl As String = "/account"


    ' 今ログインしている人（ページのコードビハインドから読む）
    '
    ' OnInit を通っている以上、ここが Nothing になることはない
    Protected ReadOnly Property CurrentAccount As AccountStore.AccountRecord
        Get
            Return AccountSession.Current(Context)
        End Get
    End Property


    ' 今ログインしているのは、サイト管理者か（一般ユーザーの登録）
    '
    ' 【メニューの出し分けにも使う】（Accounts/AdminMaster.Master.vb）
    ' 出さないだけでは守りにならない（URL を直に打てる）ので、
    ' **サイト管理者だけの画面は、必ず下の RequireAdministrator を通すこと**
    Protected ReadOnly Property IsAdministrator As Boolean
        Get
            Dim account As AccountStore.AccountRecord = CurrentAccount
            Return account IsNot Nothing AndAlso
                   String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal)
        End Get
    End Property


    ' サイト管理者でなければ、ホーム（/account）へ返す
    '
    ' 【Page_Load のいちばん先で呼ぶこと】（お知らせの画面が使っている）
    ' OnInit でやっていない理由＝**ロールを見るには DB を1行引く**ので、
    ' 「ログインしているか」の検査（OnInit）とは分けてある。
    ' ログインしていない人は、OnInit の時点でログインの画面へ行くので、ここまで来ない。
    '
    ' 【403 ではなく、ホームへ返す】ログインしている本人なので、
    ' 「その画面が在ること」を隠す意味が無い（記事の下書きを404にするのとは、場面が違う）。
    ' ホームには管理メニューが在るので、迷子にならない。
    '
    ' 【この Redirect は、既定（endResponse = True）で呼ぶ】
    ' False にすると出力を止められず、302の本文のあとにページ全文が続く。
    ' そして endResponse = True は ThreadAbortException を投げるので、
    ' **Try/Catch の中に置いてはいけない**（嘘のエラー記録が出る）
    Protected Sub RequireAdministrator()

        If Not IsAdministrator Then
            Response.Redirect(HomeUrl)
        End If

    End Sub


    Protected Overrides Sub OnInit(ByVal e As EventArgs)

        ' ============================================================
        ' 【ログインが要る画面は、ブラウザに覚えさせない】（「毎回の基本」の7）
        ' ============================================================
        ' 応答に Cache-Control: no-store を付ける。**共用のパソコンでログアウトしたあと、
        ' 戻るボタンで前の人の設定や記事の下書きが見えないように**するため（OWASP が勧めている形）。
        ' no-store の画面は、戻ったときにブラウザがサーバーへ取り直しに来る。そのときはもうログインしていないので、
        ' すぐ下の検査が、ログインの画面へ返す（＝戻るボタンを押すと、ログインの画面が出る）。
        '
        ' 【ここ1か所で付ける】前は、ログインが要る画面のそれぞれが Page_Load に同じ3行を書いていた
        ' （書き忘れた画面だけが、黙ってブラウザに残る形だった。実際に Admin/GenerateIconSprite.aspx が抜けていた）。
        ' **このクラスを継承すれば、付く。** ログインが要る画面を足すときは、必ず継承すること。
        '
        ' 【ログインの検査より先に付ける】ログインの画面へ返す応答（302）にも付けたい。
        '
        ' 【公開のページには付けない】トップ・記事・公開のプロフィールは、今までどおりブラウザに覚えさせる（軽さのため）。
        ' このクラスを継承しないので、ここは通らない。
        '
        ' 【このクラスを継承しない「本人の情報を出す口」は、それぞれが PageResponse.SetNoStore を呼んでいる】
        ' ログイン・新規登録・パスワードの再設定などの画面（ログインしていない人が開く）と、
        ' 画像を受け取る口（UploadImage・UploadProfileImage）、編集中の内容のプレビュー（ArticleDetail の ?draft=1）、
        ' ログイン中のトップページ（Default.aspx。表示名とアカウントID が出る）。
        ' **、本当にそうした**——それまでは17か所が同じ3行（5か所は2行）を自分で書いていた。
        ' 今は、Response.Cache.SetCacheability(NoCache) と書いてあるのは Core/PageResponse.vb の中だけである
        ' （grep で1件。2件になったら、誰かが書き写している）
        PageResponse.SetNoStore(Response)

        If AccountSession.Current(Context) Is Nothing Then

            ' 【戻り先を持たせて飛ばす】
            ' /account/articles を開いた人は、ログインしたあと /account/articles へ戻る。
            ' **戻り先の判定は Accounts/AccountReturnUrl.vb の1か所だけ**
            ' （オープンリダイレクトにしないため。理由はあちらの先頭のコメント）。
            ' RawUrl は「パス＋クエリ文字列」で、スキームもホストも含まない
            '
            ' 【この Redirect は、既定（endResponse = True）で呼ぶ】
            ' False にすると出力を止められず、302の本文のあとにページ全文が続く。
            ' そして endResponse = True は ThreadAbortException を投げるので、
            ' **Try/Catch の中に置いてはいけない**（嘘のエラー記録が出る）
            Response.Redirect(AccountReturnUrl.LoginUrlFor(Request.RawUrl))

        End If

        MyBase.OnInit(e)

    End Sub


    ' 【数字の入力欄を、検査より先に整える】（Core/NumberInput.vb の FixFields）
    ' 送られてきた値（全角・前後の空白）を、Validator が見る前に半角の数字にする。
    ' 画面の側（Scripts/site.js の「16」）でも整えるが、JS を通らない送信もここで同じ形になる。
    ' Load の前（Page_Load より先）に呼ぶ＝検査（Page.Validate）はこのあと
    Protected Overrides Sub OnLoad(ByVal e As EventArgs)

        If IsPostBack Then
            NumberInput.FixFields(Me)
        End If

        MyBase.OnLoad(e)

    End Sub

End Class
