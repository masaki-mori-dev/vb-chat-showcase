Imports System.Web.Services
Imports Newtonsoft.Json

Public Class _Default
    Inherits Page


    ' トップの決める枠の説明の文に添える、使い方・利用規約の文（までは左の「はじめに」の列にあった）
    '
    ' 行き先は記事の「URL の名前」（howto・terms）。**その記事が公開されていなければ、文ごと出さない**
    ' （押しても「見つかりません」になるリンクを置かない。Articles/SiteArticles.vb）。
    ' リンクの文字は SiteArticles.LinkHtml がエスケープする。
    ' 「説明書（guide）」はやめて「使い方（howto）」にまとめた（メニューと同じ）
    Protected ReadOnly Property IntroLinksHtml As String
        Get

            Dim howto As String = SiteArticles.LinkHtml("howto", "使い方")
            Dim terms As String = SiteArticles.LinkHtml("terms", "利用規約")

            Dim html As String = ""

            If howto.Length > 0 Then
                html &= "詳しくは " & howto & " をご覧下さい。"
            End If

            If terms.Length > 0 Then
                html &= If(html.Length > 0, " また、", "") & terms & "もお読みください。"
            End If

            Return html

        End Get
    End Property


    ' 画面（JavaScript）へ渡す設定のJSON
    ' 部屋の画面（ChatRoom.aspx.vb）とまったく同じものを渡している
    Protected ReadOnly Property ClientSettingsJson As String
        Get
            Return SiteSettings.ClientSettingsJson
        End Get
    End Property

    ' ニックネームの文字数の上限
    '
    ' 【なぜ、JSONとは別に用意しているのか】
    ' 入力欄のmaxlength属性とプレースホルダーの文言は、JavaScriptが動く前の
    ' HTMLとして書き出される必要がある（JSが無効でも入力欄は出る）。
    ' JSONはスクリプトの中でしか読めないため、ここだけはサーバー側で埋め込む
    Protected ReadOnly Property NicknameMaxLength As Integer
        Get
            Return ChatProfileValidator.NicknameMaxLength
        End Get
    End Property

    ' 画面のJavaScriptへ「選択済みにするアイコンの種類」を渡すための値
    '
    ' 【なぜ、マークアップに "<%: existingIconType %>" と書かないのか】
    ' <%: %> はHTML用のエスケープである。<script> の中で使うと、たとえば
    ' ダブルクォートが &quot; という文字列のまま変数へ入ってしまう。
    ' 突破できるわけではないが、値が黙って壊れる（宿題7）。
    '
    ' JavaScriptStringEncode の第2引数をTrueにすると、引用符ごと組み立ててくれるので、
    ' マークアップ側は var existingIconType = <%= ExistingIconTypeJs %>; と書ける。
    ' 引用符を自分で書かないため、閉じ忘れや二重引用の事故も起きない
    '
    ' 【なぜプロパティにするのか】
    ' マークアップに HttpUtility. を直接書かないため（「ロジックは必ず別クラスへ出す」）。
    ' Site.Master の Asset() と同じ形
    '
    ' 【Nothing のときも必ず引用符付きで返すこと】
    ' 空を返すと var existingIconType = ; になり、ページ全体が構文エラーで止まる。
    ' PostBack では Page_Load の中身を通らずフィールドが Nothing のままになるため、
    ' ここで String.Empty に寄せてある
    Protected ReadOnly Property ExistingIconTypeJs As String
        Get
            Return HttpUtility.JavaScriptStringEncode(
                If(existingIconType, String.Empty), True)
        End Get
    End Property

    ' アイコン選択の枠を、サーバー側で組み立てる
    '
    ' 【なぜJavaScriptで作らないのか】
    ' 以前は、ページに埋め込んだスプライトから <symbol> を拾って作っていた。
    ' スプライトをファイルとして取りに行く形にしたため、そのままでは
    ' **届くまで選択肢が0個**になり、次の2つが起きる。
    '   1. アイコンの並ぶ場所が高さ0から一気に伸びて、画面が大きく跳ねる（実測で1188px）
    '   2. 取れなかったときは、1つも選べない（名前を決めて先へ進めない）
    '
    ' 名前さえ分かれば枠は先に置ける。中の絵は、スプライトが届いた時点で
    ' <use> が自動的に解決する（まだ無いidを指す<use>は、あとからsymbolが
    ' 現れると描き直される。実測で確認）。
    '
    ' 【出力するのはHTMLなので、必ずエスケープすること】
    ' アイコン名はスプライトのidから取っており、正規表現で
    ' 引用符と山かっこを除いてあるが、**出口でも通す**
    ' （入口の検査だけでは足りない、という既存の決まりと同じ）
    '
    ' 【、<div> から <button role="radio"> へ変えた】
    ' それまでは <div> を押す形で、**キーボードでは1つも選べなかった**（Tab が止まらない）。
    ' 見た目は1pxも変えていない（Content/Site.css の .icon-choice で、ボタンの既定の見た目を打ち消してある）。
    ' 部屋の設定パネルの「見た目」のアイコン（AppearanceIconChoicesHtml）と同じ形にそろえた
    ' ——あちらとの違いは、**こちらは小窓にせず20個を並べたままにしてある**ことだけ
    ' （決定：トップは今のままでよい。ほかに置くものが無いため）。
    ' 名前は「アイコン1」〜（IconMaster に日本語の名前が無いため。付けるなら管理画面＝16番で列を足す）
    Protected ReadOnly Property IconChoicesHtml As String
        Get
            Dim sb As New Text.StringBuilder()
            Dim number As Integer = 0

            For Each iconType As String In IconCatalog.GetOrderedIconTypes()

                number += 1

                Dim safeName As String = HttpUtility.HtmlAttributeEncode(iconType)

                ' 読み上げ用の名前。日本語の名前があればそれ、無ければ今までどおり番号
                ' （IconMaster.Label → スプライトの data-label → ここ）
                Dim readable As String = IconCatalog.GetLabel(iconType)
                If readable = "" Then
                    readable = "アイコン" & number
                End If

                sb.Append("<button type=""button"" class=""icon-choice"" role=""radio"" aria-checked=""false"" tabindex=""-1""")
                sb.Append(" data-icon=""").Append(safeName).Append("""")
                sb.Append(" aria-label=""").Append(HttpUtility.HtmlAttributeEncode(readable)).Append(""">")
                sb.Append("<svg viewBox=""0 0 512 512"" aria-hidden=""true""><use href=""#icon-").Append(safeName).Append("""></use></svg>")
                sb.Append("</button>")

            Next

            Return sb.ToString()
        End Get
    End Property

    ' === ログインしている人のための値（チャットとアカウントの紐づけ） ===
    '
    ' ログインしている人がこのページを開くと、
    '   ・名前の欄に、アカウントの名前（表示名）が最初から入っている（変えてよい）
    '   ・「アカウントID」のスイッチが出て、最初からオン
    ' になる。ログインしていない人には、1文字も出ない（markup の If で囲んである）。
    '
    ' 【ここで読む値は、画面に出すためだけ】本当に結び付けるかは、「決定する」を押したときに
    ' サーバーがログインの札から読み直して決める（Chat/ChatLogin.vb）。ここの値を信じて結び付けることはしない
    Protected isAccountLoggedIn As Boolean

    ' ログインしていない人の1行の「ログイン」の行き先（ログインが済んだら、このトップへ戻す）。
    ' markup から AccountReturnUrl を直接呼ばない理由は Chat/Lounge.aspx.vb の AccountLoginUrl と同じ
    Protected ReadOnly Property AccountLoginUrl As String
        Get
            Return AccountReturnUrl.LoginUrlFor("/")
        End Get
    End Property

    ' 「@ユーザー名」（スイッチの横に出す。どの ID が出るのかを、押す前に見せる）
    Protected accountIdText As String

    Protected existingNickname As String
    Protected existingIconType As String
    Protected existingIconColor As String
    Protected hasExistingProfile As Boolean
    Protected displayIconColor As String

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

        If Not IsPostBack Then

            ' === Cookieが揃っている人は、ラウンジへ送る ===
            '
            ' 【、この転送を戻した】
            ' 「トップページには、誰が来ても弾かない」として
            ' コメントアウトしていたが、よくあるチャットのサイトと同じ方式へ戻すと決まった。
            ' 導線は、この3つで閉じている。
            '
            '   Cookieが無い人が、ラウンジや部屋へ来たら → トップへ
            '   Cookieがある人が、トップページへ来たら   → ラウンジへ（ここ）
            '   Cookieがある人が、部屋へ直接来たら       → ラウンジへ
            '
            ' 【名前を変えたい人の入口が無くなるわけではない】
            ' 弾かないようにしていた理由は「名前・アイコンを変えるためにトップへ
            ' 戻ってくる人がいる」ことだった。その用は、ラウンジの「ログアウト」が
            ' 引き受ける（押すとCookieごと消えるので、戻ってきたトップページは
            ' ここを素通りする）。つまり「見た目だけ変えるためにトップへ来る」経路は、
            ' 意図して無くしてある
            '
            ' 【この Redirect を Try/Catch の中へ移さないこと】
            ' 既定（endResponse = True）は ThreadAbortException を投げるため、
            ' Catch が走って嘘のエラー記録が出る
            If ChatCommon.IsChatProfileComplete() Then
                Response.Redirect(ChatCommon.LoungeUrl)
            End If

            ' 既にCookie（名前・アイコン・色）を持ってる人には、
            ' 画面側で、既存の値を選択済みの状態にしてあげる
            '
            ' 【IsChatProfileComplete ではなく HasProfileCookies を見る】
            ' 上の転送を抜けてここへ来る人のうち、**Cookieは揃っているのに名前の札だけが無い人**がいる
            ' （札の鍵を作り直した回。Chat/NameToken.vb）。あの人たちに既定のアイコンと色を出すと、
            ' 前の見た目が消えたように見える。名前とアイコンを選択済みで出しておけば、
            ' 押すのは「決定する」の1回だけで済む。
            ' 【ここで読む値は、表示のためだけである】本物の名前は札にしか無い（ChatCommon の章）
            hasExistingProfile = ChatCommon.HasProfileCookies()

            If hasExistingProfile Then
                existingNickname = Server.UrlDecode(Request.Cookies("ChatNickname").Value)
                existingIconType = Request.Cookies("ChatIconType").Value
                existingIconColor = Request.Cookies("ChatIconColor").Value
                displayIconColor = existingIconColor
            Else
                ' まだアイコンを選んでいない人に、選択済みで出すアイコン
                '
                ' 【追加（宿題9）】
                ' それまではここが空のままで、画面側のJavaScriptが
                ' document.querySelector('.icon-choice') ＝ スプライトの先頭を選んでいた。
                ' 「アイコン管理」の章が警告しているとおり、先頭を既定にすると、
                ' アイコンを1つ増やしただけで既定が黙って変わり、エラーも出ないため
                ' 誰も気づけない。サーバー側（IconCatalog）は先に直してあったが、
                ' 画面側にだけこの形が残っていた
                '
                ' IconCatalog.DefaultIconType は例外を投げない。設定の名前がスプライトに
                ' 無ければスプライトの先頭へ、それも読めなければ "male" へ落ちる。
                ' つまりこの行が、トップページを500にすることはない
                '
                ' なお画面側の「見つからなければ先頭」というフォールバックは、残してある。
                ' ここが返した名前がスプライトに無い（＝スプライトが読めていない）ときの、
                ' 最後の保険として意味があるため
                existingIconType = IconCatalog.DefaultIconType

                ' まだ色を選んでいない人に出す、アイコンの初期色
                '
                ' 【appSettingsのThemeColorからSiteSettings経由に変えた】
                ' ChatProfileValidator.DefaultIconColorを通しているのは、
                ' そこが "#" を補って形（#＋16進6桁）まで整えてくれるため。
                ' 旧appSettingsは "#" 無しの形（2563EB）だったので、ここだけ
                ' Cookieがある人（#付き）と形が食い違っていた。それも揃う
                displayIconColor = ChatProfileValidator.DefaultIconColor
            End If

            ' === ログインしている人は、名前の欄にアカウントの名前を入れておく ===
            '
            ' 【DB を引くのは、ログインの札を持っている人だけ】持っていない人は、Cookie を見て戻る（AccountSession.Resolve）。
            ' 【前の名前（Cookie）が残っている人は、そちらを優先する】札の鍵を作り直した回の人で、
            ' 「前の名前のまま、決定を1回押すだけ」にしてある流れを崩さないため。
            ' 【長すぎる名前は、上限で切って入れる】アカウントの名前は50文字まで、チャットの名前は NicknameMaxLength まで。
            ' 切った形で欄に出るので、本人が見て直せる（黙って切って送ることはしない＝送るのは欄に見えている文字）
            Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

            If account IsNot Nothing Then

                isAccountLoggedIn = True
                accountIdText = ChatAccountLink.DisplayText(account.UserName)

                If String.IsNullOrEmpty(existingNickname) Then

                    Dim name As String = If(account.DisplayName, "").Trim()

                    If name.Length > ChatProfileValidator.NicknameMaxLength Then
                        name = name.Substring(0, ChatProfileValidator.NicknameMaxLength).Trim()
                    End If

                    existingNickname = name

                End If

                ' 【本人の情報を出す画面は、ブラウザに覚えさせない】（毎回の基本の7）
                ' ログアウトしたあとに戻るボタンで、前の人の名前とアカウントID の入ったトップページが出ないように。
                ' ログインしていない人のトップページには付けない（公開のページは軽いまま）
                PageResponse.SetNoStore(Response)

            End If

        End If

    End Sub


    ' ============================================
    ' 名前を決める
    '
    ' トップページの「決定する」から、$.ajax ではなく fetch で呼ぶ
    ' （このページは jQuery を読み込んでいない。読み込ませるほうが、初回の転送が増えて損）。
    '
    ' 戻り値 … JSON文字列 {"ok":true} ／ {"ok":false,"message":"…"}
    ' （ラウンジの WebMethod と同じく、キー名を小文字で揃えるため自分で JSON にしている）
    '
    ' 【なぜサーバーでやるのか】
    ' それまでは画面のJavaScriptが Cookie を4つ書いていた。名前の札（ChatNameToken）は
    ' サーバーの鍵で署名するものなので、JS からは作れない。
    ' ついでに ChatBrowserId の発行も、JS から取り上げてある（名前を決め直すたびに必ず新しくする、
    ' という決まりを画面の都合で崩さないため）。判断は Chat/ChatLogin.vb が持つ。
    '
    ' 【WebMethod は ChatPageBase の OnInit を素通りする】
    ' ——が、このページはそもそも ChatPageBase ではない（Cookie が無い人が来る画面である）。
    ' ここで IsChatProfileComplete を呼んではいけない：名前がまだ無い人が押すボタンだから。
    '
    ' 【Session は使わない】EnableSession は付けない
    '
    ' 【ここは、誰でも好きな名前で札を1枚もらえる窓口である】
    ' それでよい。札は「この身元の名前はこれ」を証明するだけで、他人になれるわけではない
    ' （もらえる札は必ず**新しい身元**に結び付く）。回数の制限は入れていない——
    ' 叩いても増えるのは Set-Cookie だけで、DBには1行も書かれない
    ' ============================================
    '
    ' 【showAccountId を足した】「アカウントID を表示する」のスイッチ。
    ' WebMethod は引数が足りない呼び出しを 500 で断るので、**発行の瞬間に開いたままだったトップページ**から
    ' 押すと「通信に失敗しました」になる（開き直せば直る。Lounge.CreateRoom に knockEnabled を足したときと同じ）。
    ' 受け取るのは「表示するか」だけで、どのアカウントかは受け取らない（Chat/ChatLogin.vb）
    <WebMethod()>
    Public Shared Function StartChat(nickname As String, iconType As String, iconColor As String,
                                     showAccountId As Boolean) As String
        Return ErrorReport.GuardWebMethod("Default.StartChat",
            Function() StartChatCore(nickname, iconType, iconColor, showAccountId))
    End Function

    ' 【名前（nickname）を、ErrorReport の補足へ渡さないこと】メールに載る
    Private Shared Function StartChatCore(nickname As String, iconType As String, iconColor As String,
                                          showAccountId As Boolean) As String

        Dim result As ChatLogin.SignInResult =
            ChatLogin.SignIn(HttpContext.Current, nickname, iconType, iconColor, showAccountId)

        Dim json As New Dictionary(Of String, Object) From {
            {"ok", result.Ok},
            {"message", result.Message}
        }

        Return JsonConvert.SerializeObject(json)

    End Function


End Class
