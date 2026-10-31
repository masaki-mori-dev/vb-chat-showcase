' 公開のプロフィールページ（/profile/{ユーザー名}）。ブログ型の②
'
' 【ログインは要らない】誰でも見られるページなので、AccountPageBase を継承しない
' （記事の公開ページ＝Articles/AccountTop.aspx.vb と同じ）。
'
' 【読み出しは AccountProfileStore.FindPublicByUserName だけ】
' あれは Accounts から AccountId・UserName・DisplayName しか読まない＝
' **PasswordHash・TokenGeneration・Email を1文字も読まない**。
' 「ログインの情報と、人に見せる情報を分ける」というは、
' ここでそれを使い続けることで守られる。**ほかの読み出しを足さないこと。**
'
' 【非公開の項目と、空の項目は、HTML に1文字も出さない】
' CSS で隠す形にすると、F12 でページのソースを開けば読めてしまい、「非公開」の意味が無くなる。
' このファイルは「出すと決めたものだけ」を組み立てる。
'
' 【無いユーザー名・止めているアカウントは 404、大文字小文字違いは 301】
' 記事のページとまったく同じ決まりにそろえてある。
Public Class PublicProfilePage
    Inherits System.Web.UI.Page


    ' === マークアップから参照する値 ===
    '
    ' 【名前が「〜Html」のものだけが、組み立て済みの HTML】
    ' それ以外はマークアップ側で <%: %> を通す（エスケープされる）
    Protected Property DisplayName As String = ""
    Protected Property BlogTitle As String = ""
    Protected Property BlogUrl As String = ""
    Protected Property CoverSrc As String = ""
    ' アイコン（Accounts/AccountAvatar.vb が組み立てた HTML。エスケープ済み）
    Protected Property AvatarHtml As String = ""
    Protected Property IntroductionHtml As String = ""
    Protected Property FactsHtml As String = ""
    Protected Property SnsHtml As String = ""


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        Dim rawUser As String = TryCast(RouteData.Values("user"), String)

        ' 【形の検査を、DB を引く前に】ユーザー名として在りえない文字列で、毎回DBを叩かせない
        If String.IsNullOrEmpty(rawUser) OrElse rawUser <> rawUser.Trim() OrElse
           AccountStore.ValidateUserName(rawUser) IsNot Nothing Then

            ShowNotFound()
            Return

        End If

        Try

            Dim found As AccountProfileStore.PublicProfile =
                AccountProfileStore.FindPublicByUserName(rawUser)

            ' 無いユーザー名も、止めているアカウントも、同じ404
            If found Is Nothing Then
                ShowNotFound()
                Return
            End If

            ' 【大文字小文字違いは、正しい URL へ 301】記事のページと同じ決まり。
            ' **ここで転送すると ThreadAbortException が飛ぶ**ので、下の Catch で素通しする
            If Not String.Equals(rawUser, found.UserName, StringComparison.Ordinal) Then
                Response.RedirectPermanent(AccountProfileRules.PublicUrl(found.UserName))
            End If

            ShowProfile(found)

        Catch ex As Threading.ThreadAbortException

            ' 【RedirectPermanent が投げる】ふつうの例外として扱わない（記事のページと同じ形）
            Throw

        Catch ex As Exception

            ' DB が読めなかった。理由は利用者に見せず、メールで開発者に届ける
            ErrorReport.Report(ex, "PublicProfile.Page_Load")
            ShowTrouble()

        End Try

    End Sub


    ' 画面に出すものを、ここで全部決める
    ' 【Render という名前にしないこと】Page が持っている Render を隠してしまい、
    ' ビルドで警告が出る（BC40005）。ページのコントロール名とクラス名をぶつけない、と
    ' 同じ形の罠である
    Private Sub ShowProfile(found As AccountProfileStore.PublicProfile)

        Dim profile As AccountProfileStore.ProfileRecord = found.Profile

        DisplayName = found.DisplayName

        ' 【ブログ名が空なら「〇〇の記事」】ブログのトップの見出しと同じ文言にそろえてある
        ' （プロフィールの編集の画面の説明にも、そう書いてある）
        BlogTitle = AccountProfileRules.BlogTitleOf(profile.BlogTitle, found.DisplayName)

        BlogUrl = ArticleRules.AccountUrl(found.UserName)

        ' --- 画像（URL を組み立てるのは SrcOf の1か所。無ければ Nothing が返る） ---
        CoverSrc = If(AccountProfileImages.SrcOf(profile.CoverImageName), "")
        CoverBlock.Visible = (CoverSrc.Length > 0)

        ' 【アイコンは共通の部品】写真が無ければ「色の付いた丸に1文字」。
        ' 前は写真の有無で PlaceHolder を2つ出し分けていて、Visible を読んで分けたせいで丸が2つ出たことがある
        ' （「Visible の読み取りは、親まで見る」の落とし穴）。部品が1つの HTML を返すので、出し分けごと無くなった
        AvatarHtml = AccountAvatar.Html(
            AccountAvatar.Resolve(found.AccountId, found.UserName, found.DisplayName,
                                  profile.AvatarImageName, profile.AvatarLetter, profile.AvatarColor),
            "xl", "profile-head-avatar")

        ' --- 自己紹介 ---
        If Not ProfileChoices.IsPrivate(profile.IntroductionVisibility) AndAlso
           Not String.IsNullOrEmpty(profile.Introduction) Then

            IntroductionHtml = BuildIntroductionHtml(profile.Introduction)
            IntroSection.Visible = True

        End If

        ' --- 基本情報 ---
        FactsHtml = BuildFactsHtml(profile)
        FactsSection.Visible = (FactsHtml.Length > 0)

        ' --- SNS ---
        SnsHtml = BuildSnsHtml(profile)
        SnsSection.Visible = (SnsHtml.Length > 0)

        Page.Title = found.DisplayName & "のプロフィール"
        ProfileView.Visible = True

    End Sub


    ' 自己紹介（プレーンテキスト）を、画面に出す HTML にする
    '
    ' 【順番を入れ替えないこと】**先にエスケープしてから、改行を br に直す**。
    ' 逆にすると、入れたばかりの <br /> まで文字に化ける。
    ' （記事の本文と違い、ここは利用者がタグを書けない欄である＝サニタイザーは通さない）
    Private Shared Function BuildIntroductionHtml(text As String) As String

        Dim escaped As String = HttpUtility.HtmlEncode(text)

        ' 【3つの改行コードを、まず1つに寄せる】発言の正規化（ChatHub）と同じ考え方。
        ' \r だけの改行を見落とすと、その行だけ改行が消える
        escaped = escaped.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)

        Return escaped.Replace(vbLf, "<br />")

    End Function


    ' 基本情報の「項目名：値」の組を組み立てる
    '
    ' 【非公開・空の項目は、1文字も出さない】ここで足さない＝ページのソースにも出ない。
    ' 【生年月日も、ほかの項目と同じ「非公開」のチェック】（それまでは3択に混ざっていた）
    ' 出しかた（月日だけ／年月日）は BirthDateText、日付が無いときは空文字
    Private Shared Function BuildFactsHtml(profile As AccountProfileStore.ProfileRecord) As String

        Dim sb As New Text.StringBuilder()

        AddFact(sb, "性別", profile.GenderVisibility, ProfileChoices.GenderName(profile.Gender))

        AddFact(sb, "生年月日", profile.BirthDateVisibility,
                AccountProfileRules.BirthDateText(profile.BirthDate, profile.BirthDateMode))

        AddFact(sb, "血液型", profile.BloodTypeVisibility, ProfileChoices.BloodTypeName(profile.BloodType))
        AddFact(sb, "出身地", profile.HometownVisibility, ProfileChoices.PrefectureName(profile.Hometown))
        AddFact(sb, "居住地", profile.ResidenceVisibility, ProfileChoices.PrefectureName(profile.Residence))
        AddFact(sb, "ステータス", profile.RelationshipStatusVisibility,
                ProfileChoices.RelationshipName(profile.RelationshipStatus))

        ' 職業は「選んだもの（自由記入）」の形にまとめてから出す
        AddFact(sb, "職業", profile.OccupationVisibility,
                AccountProfileRules.OccupationText(profile.Occupation, profile.OccupationText))

        Return sb.ToString()

    End Function


    ' 1組ぶん。**非公開か、値が空なら、何も足さない**
    Private Shared Sub AddFact(sb As Text.StringBuilder, label As String,
                               visibility As Integer, value As String)

        If ProfileChoices.IsPrivate(visibility) OrElse String.IsNullOrEmpty(value) Then
            Return
        End If

        sb.Append("<div class=""profile-fact"">")
        sb.Append("<dt class=""profile-fact-label"">").Append(HttpUtility.HtmlEncode(label)).Append("</dt>")
        sb.Append("<dd class=""profile-fact-value"">").Append(HttpUtility.HtmlEncode(value)).Append("</dd>")
        sb.Append("</div>")

    End Sub


    ' SNS のリンクを組み立てる
    '
    ' 【URL を、ここで文字列から作らないこと】XUrl・FacebookUrl・InstagramUrl が作る。
    ' 利用者が入れられるのは「利用者名」だけなので、javascript: のようなものが入る隙が無い
    ' （①でそう決めた）
    Private Shared Function BuildSnsHtml(profile As AccountProfileStore.ProfileRecord) As String

        Dim sb As New Text.StringBuilder()

        AddSns(sb, "X（旧 Twitter）", profile.XVisibility,
               profile.XUserName, AccountProfileRules.XUrl(profile.XUserName))

        AddSns(sb, "Facebook", profile.FacebookVisibility,
               profile.FacebookUserName, AccountProfileRules.FacebookUrl(profile.FacebookUserName))

        AddSns(sb, "Instagram", profile.InstagramVisibility,
               profile.InstagramUserName, AccountProfileRules.InstagramUrl(profile.InstagramUserName))

        Return sb.ToString()

    End Function


    ' 1つぶん。**非公開か、URL が作れなければ、何も足さない**
    '
    ' 【よそのサイトなので、新しいタブで開く】rel="noopener noreferrer" を必ず付ける
    ' （記事の中のリンクと同じ決まり。ArticleHtml.NormalizeLink）
    Private Shared Sub AddSns(sb As Text.StringBuilder, service As String,
                              visibility As Integer, userName As String, url As String)

        If ProfileChoices.IsPrivate(visibility) OrElse String.IsNullOrEmpty(url) Then
            Return
        End If

        sb.Append("<li class=""profile-sns-item"">")
        sb.Append("<span class=""profile-sns-service"">").Append(HttpUtility.HtmlEncode(service)).Append("</span>")
        sb.Append("<a class=""profile-sns-link"" href=""").Append(HttpUtility.HtmlAttributeEncode(url))
        sb.Append(""" target=""_blank"" rel=""noopener noreferrer"">")
        sb.Append(HttpUtility.HtmlEncode(userName)).Append("</a>")
        sb.Append("</li>")

    End Sub


    Private Sub ShowNotFound()

        PageResponse.MarkNotFound(Response)
        Page.Title = "ページが見つかりません"
        NotFoundView.Visible = True

    End Sub


    Private Sub ShowTrouble()

        PageResponse.MarkTrouble(Response)
        Page.Title = "表示できませんでした"
        TroubleView.Visible = True

    End Sub


End Class
