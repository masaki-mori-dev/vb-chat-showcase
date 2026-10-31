
' ログインしたあとのページ
'
' 【今は「ログイン中：〇〇」と「準備中です」とログアウトだけ】
' 記事を書く画面は A-2、記事を見せるページは 作る。
' ここを「管理画面」に育てるのか、一般ユーザーも使う「自分のページ」にするのかは、まだ決めていない
' （フォルダを Admin ではなく Account にしてあるのは、後者にもできるようにするため）。
'
' 【AccountPageBase を継承している】
' ログインしていなければ、画面が組み立てられる前にログインの画面へ返される
Public Class AccountHomePage
    Inherits AccountPageBase


    ' 画面に出す表示名（markup が <%: %> でエスケープして出す）
    '
    ' 【、ユーザー名から表示名に替えた】ヘッダーの「表示名（ロール名）」とそろえるため
    Protected ReadOnly Property CurrentDisplayName As String
        Get
            Return CurrentAccount.DisplayName
        End Get
    End Property


    ' 画面に出すロールの名前（日本語）
    '
    ' 【DB の Roles.DisplayName から引く】
    ' コードの中に「administrator なら管理人」という対応表を持つと、
    ' ロールを1つ足すたびに2か所を直すことになる
    ' （SiteSettings の DisplayName を日本語で持っているのと、同じ理由）。
    '
    ' 【引けなければ、ロールの名前（英字）をそのまま出す】
    ' ここで止めると、DB が一瞬詰まっただけでアカウントのページが開かなくなる。
    ' 見せるだけの値なので、フェイルオープンでよい
    ' （ロールごとの上限＝Accounts/RoleLimits.vb は、逆にフェイルクローズである。
    '   あちらは「できること」を決める値なので、読めないときに通してはいけない）
    Protected ReadOnly Property CurrentRoleName As String
        Get
            Return roleDisplayName
        End Get
    End Property

    Private roleDisplayName As String


    ' 管理画面のホームの説明（それまでは1文だけで、中身が何も無いように見えた）
    '
    ' 【管理メニューの項目ごとの箇条書き】並びは管理メニューと同じ（Accounts/AdminMenu.vb の PagesFor をそのまま回す）。
    ' **お知らせは入れない**（開発者しか使わない）。項目の名前はリンクにしてある（ホームから、その画面へ）。
    '
    ' 【記事を書けるかを、ここで読む】（RoleLimits を読む口の1つ）
    ' 書けないアカウントでは、ブログの行の説明の代わりに「このアカウントでは、記事を書けません。」。
    ' 読めなければ「書けない」と出る（フェイルクローズ。上の CurrentRoleName がフェイルオープンなのと、意図して逆）。
    '
    ' 【説明の文はここだけ】項目を足したら、下の DescriptionOf に1行足す（無ければ名前だけが出る）。
    ' 【戻すのは決まった文字列だけ】markup は <%= %> で出す（名前も説明も、このクラスの中の決まった文）
    Protected ReadOnly Property MenuGuideHtml As String
        Get

            Dim canWrite As Boolean = RoleLimits.GetBoolean(CurrentAccount.RoleKey, RoleLimits.KeyCanWriteArticles)
            Dim sb As New Text.StringBuilder()

            sb.Append("<ul class=""admin-home-guide"">")

            For Each entry As AdminMenu.Item In AdminMenu.PagesFor(CurrentAccount)

                If entry.AdministratorOnly Then
                    Continue For
                End If

                sb.Append("<li><a href=""").Append(entry.Url).Append(""">")
                sb.Append(HttpUtility.HtmlEncode(entry.Text)).Append("</a>")

                Dim text As String = DescriptionOf(entry.Url, canWrite)

                If text.Length > 0 Then
                    sb.Append("<span class=""admin-home-guide-text"">").Append(HttpUtility.HtmlEncode(text)).Append("</span>")
                End If

                sb.Append("</li>")

            Next

            sb.Append("</ul>")

            Return sb.ToString()

        End Get
    End Property

    Private Shared Function DescriptionOf(url As String, canWrite As Boolean) As String

        Select Case url
            Case "/account/articles"
                If Not canWrite Then
                    Return "このアカウントでは、記事を書けません。"
                End If
                Return "記事を書く・直す・公開する。書いている途中も自動で下書きに保存されます。カテゴリ分けやブログのタイトルもここで決められます。"
            Case "/account/chatlogs"
                Return "アカウントID を出して入った部屋で、自分が居た間の発言を見返せます。"
            Case "/account/profile"
                Return "アイコン、自己紹介（誕生日・出身地など）、SNS を変えられます。"
            Case "/account/settings"
                Return "名前（表示名）・メールアドレス・パスワードを変えられます。退会もここからできます。"
        End Select

        Return ""

    End Function


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        ' 【、AccountSession.CurrentRoleName へ移した】
        ' ヘッダーの「表示名（ロール）」も同じ値を出すので、引き方を1か所にした（1回の要求で DB は1回）
        roleDisplayName = AccountSession.CurrentRoleName(Context)

    End Sub


End Class
