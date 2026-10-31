Imports System.Globalization

' プロフィールの編集の画面（ブログ型の①）
'
' 【判断は Accounts/AccountProfileRules.vb、SQL は Accounts/AccountProfileStore.vb】
' ここは、画面の値を集めて渡し、結果を出すだけ（Accounts/Settings.aspx.vb と同じ立ち位置）。
'
' 【AccountPageBase を継承している】ログインしていなければ、ログインの画面へ返される（戻り先つき）。
'
' 【本人のプロフィールしか触れない】
' 画面のどこにも「誰のプロフィールか」を渡していない。**必ず CurrentAccount.AccountId を使う**。
' ほかの人のプロフィールを編集する口を足すなら、そのときにロールの確かめから作ること
'
' 【うまくいったら、同じ画面へ移り直す】（?done=saved）
' F5 で同じ変更が2回送られないように。文言はこちらが持つ（クエリ文字列で文言そのものを運ばない＝決まり）
Public Class AccountProfilePage
    Inherits AccountPageBase

    Private Const SelfUrl As String = "/account/profile"

    ' 画像がまだ無いときの src（1×1 の透明な GIF）
    '
    ' 【空文字にしないこと】src="" は、ブラウザによっては**今のページをもう一度取りに行く**。
    ' 画像そのものは data-empty="1" の側で隠している
    Private Const BlankImageSrc As String =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"

    ' 「非公開」のチェックの読み上げ用の前置き（見える文字は「非公開」だけ）
    '
    ' 【1か所で組み立てる】markup に entity で書くと、読みにくいうえ、
    ' 属性の中の「&quot;」が本当にデコードされるかを、画面ごとに確かめることになる
    Private Shared Function PrivateLabel(itemName As String) As String
        Return "<span class=""visually-hidden"">" & HttpUtility.HtmlEncode(itemName) & "を</span>非公開"
    End Function


    ' 今のプロフィール（毎回 DB から1行引く。画像の src を描くのに、ポストバックでも要る）
    Private _profile As AccountProfileStore.ProfileRecord

    ' 包まれて届いた欄を、ほどいた結果（ポストバックのときだけ入る）
    Private _introDecode As BodyTransport.DecodeResult


    ' ============================================================
    ' マークアップから読む値
    ' ============================================================

    ' 静的ファイルの URL（?v= 付き）。マークアップから <%: Asset(...) %> で呼ぶ
    '
    ' 【マークアップから common.AssetUrl を直接呼ばないこと】
    ' マークアップのインラインコードは、コードビハインドとは別のコンパイル単位で、
    ' ビルドは通るのに実行すると500になることがある
    Protected Function Asset(ByVal virtualPath As String) As String
        Return common.AssetUrl(virtualPath)
    End Function

    Protected ReadOnly Property AvatarSrc As String
        Get
            Return If(AccountProfileImages.SrcOf(CurrentProfile().AvatarImageName), BlankImageSrc)
        End Get
    End Property

    Protected ReadOnly Property CoverSrc As String
        Get
            Return If(AccountProfileImages.SrcOf(CurrentProfile().CoverImageName), BlankImageSrc)
        End Get
    End Property

    Protected ReadOnly Property AvatarEmptyFlag As String
        Get
            Return If(AccountProfileImages.SrcOf(CurrentProfile().AvatarImageName) Is Nothing, "1", "0")
        End Get
    End Property

    Protected ReadOnly Property CoverEmptyFlag As String
        Get
            Return If(AccountProfileImages.SrcOf(CurrentProfile().CoverImageName) Is Nothing, "1", "0")
        End Get
    End Property

    ' === 文字のアイコン（決まりは Accounts/AccountAvatar.vb） ===

    ' 最初の文字（名前の1文字目。記号・絵文字で始まるならユーザー名の頭文字）。欄の placeholder と、見本の data-default-letter
    Protected ReadOnly Property DefaultLetter As String
        Get
            Return AccountAvatar.DefaultLetter(CurrentAccount.DisplayName, CurrentAccount.UserName)
        End Get
    End Property

    ' 最初の色（アカウントの番号から）
    Protected ReadOnly Property DefaultColor As String
        Get
            Return AccountAvatar.DefaultColor(CurrentAccount.AccountId)
        End Get
    End Property

    ' 見本が、名前を打ち変えたときに最初の文字を決め直すのに使う（記号で始まる名前なら、ユーザー名の頭文字）
    Protected ReadOnly Property CurrentUserName As String
        Get
            Return CurrentAccount.UserName
        End Get
    End Property

    ' 文字のアイコンの丸（いつも「文字」のほう）。size＝"xl"（アイコンの枠の中。写真が無いときに見える）／"xs"（写真があるときの小さな見本）
    ' 【ポストバックで戻ってきたときは、欄に打った値で描く】断られて画面が戻っても、見本と欄が食い違わないように
    Protected Function LetterPreviewHtml(size As String) As String

        Dim letter As String
        Dim color As String

        If IsPostBack Then
            letter = AvatarLetterBox.Text
            color = AvatarColorBox.Text
        Else
            letter = CurrentProfile().AvatarLetter
            color = CurrentProfile().AvatarColor
        End If

        Dim look As AccountAvatar.Look = AccountAvatar.Resolve(
            CurrentAccount.AccountId, CurrentAccount.UserName, CurrentAccount.DisplayName,
            Nothing, If(letter, "").Trim(), color)

        Return AccountAvatar.Html(look, size)

    End Function

    Protected ReadOnly Property ImageFormatsText As String
        Get
            Return SiteImage.AcceptedFormatsText
        End Get
    End Property

    Protected ReadOnly Property UploadUrl As String
        Get
            Return ResolveUrl("~/Accounts/UploadProfileImage.aspx")
        End Get
    End Property

    Protected ReadOnly Property MaxImageBytes As String
        Get
            Return AccountProfileImages.MaxBytes.ToString(CultureInfo.InvariantCulture)
        End Get
    End Property

    Protected ReadOnly Property AvatarMaxEdge As String
        Get
            Return AccountProfileImages.AvatarMaxEdge.ToString(CultureInfo.InvariantCulture)
        End Get
    End Property

    Protected ReadOnly Property CoverMaxEdge As String
        Get
            Return AccountProfileImages.CoverMaxEdge.ToString(CultureInfo.InvariantCulture)
        End Get
    End Property


    ' ============================================================
    ' 画面を組み立てる
    ' ============================================================

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【ブラウザに覚えさせない（no-store）のは、AccountPageBase.OnInit が付けている】
        ' （各画面の Page_Load から1か所へまとめた。ここに同じ3行を書き戻さないこと）

        DoneText.Text = ""
        ErrorText.Text = ""

        ' 【包む仕掛けを、画面へつなぐ】（Scripts/site.js の「7」）
        '
        ' 【マークアップの属性に「<%」で始まる式を書かないこと】
        ' runat="server" を付けた要素の属性にコードブロックを書くと、
        ' 「コードブロックはこのコンテキストではサポートされていません」でページが止まる
        ' 。コードビハインドから Attributes に入れる
        IntroductionBox.Attributes("data-encode-on-submit") = IntroductionEncoded.ClientID
        IntroductionBox.Attributes("data-encoded-prefix") = BodyTransport.EncodedPrefix


        ' 「非公開」のチェックの文言（読み上げ用の前置きつき）
        IntroductionPrivate.Text = PrivateLabel("自己紹介")
        GenderPrivate.Text = PrivateLabel("性別")
        BirthDatePrivate.Text = PrivateLabel("生年月日")
        BloodTypePrivate.Text = PrivateLabel("血液型")
        HometownPrivate.Text = PrivateLabel("出身地")
        ResidencePrivate.Text = PrivateLabel("居住地")
        RelationshipPrivate.Text = PrivateLabel("ステータス")
        OccupationPrivate.Text = PrivateLabel("職業")
        XPrivate.Text = PrivateLabel("X（旧 Twitter）")
        FacebookPrivate.Text = PrivateLabel("Facebook")
        InstagramPrivate.Text = PrivateLabel("Instagram")

        ' 【項目に部品が2つ並ぶ行は、2つ目にも名前を付ける】
        ' asp:Label の AssociatedControlID は1つ目にしか付かないので、
        ' 2つ目（生年月日の出しかた・職業の自由記入）は読み上げソフトから「編集」としか分からない
        BirthDateModeList.Attributes("aria-label") = "生年月日の出しかた"
        OccupationTextBox.Attributes("aria-label") = "職業（自由記入）"

        ' 【欄の中のグレーの字】
        ' 「右の欄は自由記入です」という説明の行をやめて、欄そのものに書いた
        ' （方針：説明されなくても直感で分かる見た目と作りにする）。
        ' 【placeholder は打つと消える】だから、上の aria-label が要る
        ' ——placeholder を名前の代わりにしてはいけない
        ' 【短くした】「自由記入（例：Web エンジニア）」は 267px あり、
        ' スマホの幅（360px）では**欄に入りきらずに切れていた**（開発者の報告）。
        ' 今の字は開発機の実測で 230px で、320px の画面でも切れない
        OccupationTextBox.Attributes("placeholder") = "自由記入（例：エンジニア）"

        ' 【文字のアイコンの欄のグレーの字＝最初の文字】空にすると、これに戻る（説明を書かずに、見て分かるように）
        AvatarLetterBox.Attributes("placeholder") = DefaultLetter

        ' 【色の欄は、打てない欄】押すと色を選ぶ道具が開く。Attributes で付ける（ReadOnly プロパティにすると、送られた値を読まなくなる）
        AvatarColorBox.Attributes("readonly") = "readonly"

        ' 【「ブログの右の枠」と書かない】
        ' 見た目の位置で指すと、幅によって変わる（「常設の観点」）
        IntroductionHint.Text = HttpUtility.HtmlEncode(
            "プロフィールのページに出ます（" &
            AccountProfileRules.IntroductionMaxLength.ToString("#,0") &
            "文字まで）。はじめのほうは、ブログのページにも出ます。")

        If IsPostBack Then

            ' 【前の回に付いた赤い枠を、先に消す】
            ' WebControl.Attributes は ViewState に残るので、消さないと
            ' 一度エラーになった欄が、そのあとずっと赤いままになる
            ClearInvalidMarks()

            ' 【包んで送られてきた欄を、ここで元に戻す】
            ' Page_Load は、送られてきた値が入ったあと・押されたボタンの処理の**前**に走る。
            ' ここで戻しておけば、そのあとの検査も保存も「ふつうの文字列」として扱える
            ' （Contacts/Contact.aspx.vb の DecodeBody と、まったく同じ形）
            DecodeWrapped()

            Return

        End If

        BindChoices()
        BindProfile()

        If String.Equals(Request.QueryString("done"), "saved", StringComparison.Ordinal) Then
            DoneText.Text = HttpUtility.HtmlEncode("プロフィールを保存しました。")
        ElseIf String.Equals(Request.QueryString("done"), "image", StringComparison.Ordinal) Then
            DoneText.Text = HttpUtility.HtmlEncode("画像を外しました。")
        End If

    End Sub


    ' 選択肢を入れる
    '
    ' 【マークアップに <asp:ListItem> をベタ書きしないこと】
    ' 選択肢は Accounts/ProfileChoices.vb が1か所で持つ（記事の状態の3択・部屋の定員と同じ決まり）
    Private Sub BindChoices()

        FillList(GenderList, ProfileChoices.Genders(), "選んでいません")
        FillList(BloodTypeList, ProfileChoices.BloodTypes(), "選んでいません")
        FillList(RelationshipList, ProfileChoices.Relationships(), "選んでいません")
        FillList(OccupationList, ProfileChoices.Occupations(), "選んでいません")
        FillList(HometownList, ProfileChoices.Prefectures(), "選んでいません")
        FillList(ResidenceList, ProfileChoices.Prefectures(), "選んでいません")

        ' 生年月日の出しかた（年月日／月日だけ）は、必ずどちらかが選ばれている（空の行を作らない）
        FillList(BirthDateModeList, ProfileChoices.BirthDateModes(), Nothing)

    End Sub


    Private Shared Sub FillList(list As DropDownList, choices As ProfileChoice(), emptyText As String)

        list.Items.Clear()

        If emptyText IsNot Nothing Then
            list.Items.Add(New ListItem(emptyText, ""))
        End If

        For Each choice As ProfileChoice In choices
            list.Items.Add(New ListItem(choice.Text, choice.Value.ToString(CultureInfo.InvariantCulture)))
        Next

    End Sub


    ' 保存されている中身を、欄へ入れる
    Private Sub BindProfile()

        Dim p As AccountProfileStore.ProfileRecord = CurrentProfile()

        ' 【公開のプロフィールへのリンク】
        ' 記事を書く画面の ViewLink と同じ形。**URL を組み立てるのは PublicUrl の1か所**。
        ' 読み上げソフトには「新しいタブで開きます」まで伝える（記事の画面と同じ）
        ViewLink.NavigateUrl = AccountProfileRules.PublicUrl(CurrentAccount.UserName)
        ViewLink.Attributes("aria-label") = "公開ページを見る（新しいタブで開きます）"

        DisplayNameBox.Text = CurrentAccount.DisplayName

        IntroductionBox.Text = If(p.Introduction, "")
        IntroductionPrivate.Checked = ProfileChoices.IsPrivate(p.IntroductionVisibility)

        SelectValue(GenderList, p.Gender)
        GenderPrivate.Checked = ProfileChoices.IsPrivate(p.GenderVisibility)

        ' 【年・月・日の3つの欄】（お試しのあと同じ日に採用）数字は InvariantCulture で書く
        ' （書式にカルチャを渡さないと、共用サーバーの設定次第で和暦になる＝Articles/ArticleDisplay.vb の日付と同じ罠）。
        ' 月・日は頭に0を付けない（打つときの形と同じ）
        If p.BirthDate.HasValue Then
            BirthYearBox.Text = p.BirthDate.Value.Year.ToString(CultureInfo.InvariantCulture)
            BirthMonthBox.Text = p.BirthDate.Value.Month.ToString(CultureInfo.InvariantCulture)
            BirthDayBox.Text = p.BirthDate.Value.Day.ToString(CultureInfo.InvariantCulture)
        Else
            BirthYearBox.Text = ""
            BirthMonthBox.Text = ""
            BirthDayBox.Text = ""
        End If
        SelectValue(BirthDateModeList, p.BirthDateMode)
        BirthDatePrivate.Checked = ProfileChoices.IsPrivate(p.BirthDateVisibility)

        SelectValue(BloodTypeList, p.BloodType)
        BloodTypePrivate.Checked = ProfileChoices.IsPrivate(p.BloodTypeVisibility)

        SelectValue(HometownList, p.Hometown)
        HometownPrivate.Checked = ProfileChoices.IsPrivate(p.HometownVisibility)

        SelectValue(ResidenceList, p.Residence)
        ResidencePrivate.Checked = ProfileChoices.IsPrivate(p.ResidenceVisibility)

        SelectValue(RelationshipList, p.RelationshipStatus)
        RelationshipPrivate.Checked = ProfileChoices.IsPrivate(p.RelationshipStatusVisibility)

        SelectValue(OccupationList, p.Occupation)
        OccupationTextBox.Text = If(p.OccupationText, "")
        OccupationPrivate.Checked = ProfileChoices.IsPrivate(p.OccupationVisibility)

        XBox.Text = If(p.XUserName, "")
        XPrivate.Checked = ProfileChoices.IsPrivate(p.XVisibility)

        FacebookBox.Text = If(p.FacebookUserName, "")
        FacebookPrivate.Checked = ProfileChoices.IsPrivate(p.FacebookVisibility)

        InstagramBox.Text = If(p.InstagramUserName, "")
        InstagramPrivate.Checked = ProfileChoices.IsPrivate(p.InstagramVisibility)

        ' 【ブログのタイトルと説明は、ブログの設定（Articles/BlogSettings.aspx）へ移した】

        ' 文字のアイコン。【自分で変えていなければ、文字の欄は空】欄の中のグレーの字が最初の文字を見せる。
        ' 色の欄は、いつも今の色（自分で変えていなければ最初の色）を入れておく（色を選ぶ道具が、その色から開く）
        AvatarLetterBox.Text = If(p.AvatarLetter, "")
        AvatarColorBox.Text = If(AccountAvatar.NormalizeColor(p.AvatarColor), AccountAvatar.DefaultColor(CurrentAccount.AccountId))

    End Sub


    Private Shared Sub SelectValue(list As DropDownList, value As Integer?)

        Dim text As String = If(value.HasValue, value.Value.ToString(CultureInfo.InvariantCulture), "")
        Dim item As ListItem = list.Items.FindByValue(text)

        list.ClearSelection()

        If item IsNot Nothing Then
            item.Selected = True
        ElseIf list.Items.Count > 0 Then
            list.Items(0).Selected = True
        End If

    End Sub


    ' 今のプロフィール（1回の要求の中では、1回しか引かない）
    '
    ' 【DB が読めなければ、空の器で描く】画面は出て、保存しようとしたときに理由が出る。
    ' 知らせは ErrorReport で届く（読めないことを黙らせない）
    Private Function CurrentProfile() As AccountProfileStore.ProfileRecord

        If _profile IsNot Nothing Then
            Return _profile
        End If

        Try
            _profile = AccountProfileStore.Find(CurrentAccount.AccountId)
        Catch ex As Exception

            ErrorReport.Report(ex, "AccountProfilePage.Find")

            _profile = New AccountProfileStore.ProfileRecord With {.AccountId = CurrentAccount.AccountId}

        End Try

        Return _profile

    End Function


    ' ============================================================
    ' 包まれて届いた欄を、ほどく
    ' ============================================================
    '
    ' 【ほどくのは、ここ1か所だけ】2か所でほどくと、
    ' **利用者が本当に「vbenc1:」で始まる文を書いたとき**に、2度目のほどきがその文を壊す
    '
    ' 【JavaScript が動かなかったときは、見えている欄の生の文をそのまま使う】
    ' （そのときは WAF に止められうるが、受け取らないと JavaScript の無い環境で何も保存できなくなる）
    Private Sub DecodeWrapped()

        _introDecode = Unwrap(IntroductionEncoded, IntroductionBox,
                              AccountProfileRules.IntroductionMaxLength, "自己紹介")

    End Sub


    Private Shared Function Unwrap(hidden As HiddenField, box As TextBox,
                                   maxLength As Integer, itemName As String) As BodyTransport.DecodeResult

        If String.IsNullOrEmpty(hidden.Value) Then
            Return Nothing
        End If

        Dim result As BodyTransport.DecodeResult =
            BodyTransport.Decode(hidden.Value, maxLength, itemName, "保存していません。")

        ' 【戻せなかったときは、欄を空にしない】
        ' 空にすると「入力してください」という的外れな理由になる。包んだままの値を残し、
        ' 保存の処理が本当の理由を出して断る
        box.Text = If(result.Ok, result.Html, hidden.Value)

        Return result

    End Function


    ' ほどけなかったか（ほどけなかったときは、理由を画面に出してある）
    Private Function DecodeFailed() As Boolean

        For Each result As BodyTransport.DecodeResult In New BodyTransport.DecodeResult() {_introDecode}

            If result IsNot Nothing AndAlso Not result.Ok Then

                ShowError(result.Message)

                IntroductionEncoded.Value = ""

                Return True

            End If

        Next

        Return False

    End Function


    ' ============================================================
    ' 保存する
    ' ============================================================

    Protected Sub SaveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles SaveButton.Click

        If DecodeFailed() Then
            Return
        End If

        ' --- プロフィールの各項目を、先に整える ---
        '
        ' 【名前より先に見る】断る理由が見つかったら、**DB を1つも触らずに止まれる**。
        ' 名前を先に書いてしまうと、そのあと SNS の形で断ったときに「名前だけ変わった」状態が残る
        Dim record As AccountProfileStore.ProfileRecord = CollectFromForm()

        ' 【生年月日の3つの欄】（お試しのあと同じ日に採用）どれかだけ・ない日付は、ここで断る（悪い欄に印を付ける）。
        ' これから先の日・古すぎる年は、今までどおり AccountProfileRules.Prepare が断る
        Dim birthDate As Date? = Nothing
        Dim birthField As WebControl = Nothing
        Dim birthReason As String = ReadBirthDate(birthDate, birthField)

        If birthReason IsNot Nothing Then
            ShowErrorOn(birthReason, birthField)
            Return
        End If

        record.BirthDate = birthDate

        Dim fieldKey As String = Nothing
        Dim reason As String = AccountProfileRules.Prepare(record, fieldKey)

        If reason IsNot Nothing Then
            ShowError(reason, fieldKey)
            Return
        End If

        ' --- 名前（表示名）---
        '
        ' 【書き換える道は AccountSettings.ChangeDisplayName の1本】
        ' 設定の画面とこの画面の両方から変えられるので、**入力の検査が食い違わないように1本へ寄せてある**。
        ' **AccountStore.UpdateDisplayName を、ここから直接呼ばないこと。**
        ' 今と同じ名前のときは、あちらが DB を触らずに成功で返す（この画面は、名前を触らずに保存することが多い）
        Dim nameResult As AccountSettings.ChangeResult =
            AccountSettings.ChangeDisplayName(CurrentAccount, DisplayNameBox.Text)

        If Not nameResult.Ok Then
            ShowError(nameResult.Message, AccountProfileRules.FieldDisplayName)
            Return
        End If

        Try

            ' 【名前は、もう書き終わっている】
            ' 2つの表なので、1つのトランザクションにはしていない。
            ' **どちらも「もう一度押せば直る」書き換え**なので、途中で失敗しても、
            ' 直し方は「理由を見て、もう一度押す」で足りる。
            ' 取り返しのつかない操作（お金・削除）なら、この割り切りはできない
            AccountProfileStore.Save(record)

        Catch ex As Exception

            ErrorReport.Report(ex, "AccountProfilePage.Save")

            ShowError("ただいま混み合っています。少し待ってから、もう一度お試しください。")

            Return

        End Try

        ' 【Try/Catch の中に置かない】Redirect は ThreadAbortException を投げる
        Response.Redirect(SelfUrl & "?done=saved")

    End Sub


    ' 画面の値を、1つの器へ集める（整えるのは AccountProfileRules.Prepare）
    Private Function CollectFromForm() As AccountProfileStore.ProfileRecord

        Dim record As New AccountProfileStore.ProfileRecord With {.AccountId = CurrentAccount.AccountId}

        record.Introduction = IntroductionBox.Text
        record.IntroductionVisibility = ProfileChoices.VisibilityOf(IntroductionPrivate.Checked)

        record.Gender = SelectedNumber(GenderList)
        record.GenderVisibility = ProfileChoices.VisibilityOf(GenderPrivate.Checked)

        ' 生年月日（record.BirthDate）は、3つの欄を確かめてから SaveButton_Click が入れる（ReadBirthDate）
        record.BirthDateMode = If(SelectedNumber(BirthDateModeList), ProfileChoices.BirthFull)
        record.BirthDateVisibility = ProfileChoices.VisibilityOf(BirthDatePrivate.Checked)

        record.BloodType = SelectedNumber(BloodTypeList)
        record.BloodTypeVisibility = ProfileChoices.VisibilityOf(BloodTypePrivate.Checked)

        record.Hometown = SelectedNumber(HometownList)
        record.HometownVisibility = ProfileChoices.VisibilityOf(HometownPrivate.Checked)

        record.Residence = SelectedNumber(ResidenceList)
        record.ResidenceVisibility = ProfileChoices.VisibilityOf(ResidencePrivate.Checked)

        record.RelationshipStatus = SelectedNumber(RelationshipList)
        record.RelationshipStatusVisibility = ProfileChoices.VisibilityOf(RelationshipPrivate.Checked)

        record.Occupation = SelectedNumber(OccupationList)
        record.OccupationText = OccupationTextBox.Text
        record.OccupationVisibility = ProfileChoices.VisibilityOf(OccupationPrivate.Checked)

        record.XUserName = XBox.Text
        record.XVisibility = ProfileChoices.VisibilityOf(XPrivate.Checked)

        record.FacebookUserName = FacebookBox.Text
        record.FacebookVisibility = ProfileChoices.VisibilityOf(FacebookPrivate.Checked)

        record.InstagramUserName = InstagramBox.Text
        record.InstagramVisibility = ProfileChoices.VisibilityOf(InstagramPrivate.Checked)

        ' 文字のアイコン（確かめと整え方は AccountProfileRules.Prepare → AccountAvatar）
        record.AvatarLetter = AvatarLetterBox.Text
        record.AvatarColor = AvatarColorBox.Text

        Return record

    End Function


    Private Shared Function SelectedNumber(list As DropDownList) As Integer?

        Dim value As Integer

        If Integer.TryParse(list.SelectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, value) Then
            Return value
        End If

        Return Nothing

    End Function


    ' 生年月日の3つの欄（年・月・日）を読む（採用。前は type="date" の1つの欄）
    '
    ' 戻り値＝断る理由（読めたら Nothing）。result に日付（3つとも空なら Nothing＝未設定）、
    ' 断るときは field に印を付ける欄。
    ' 【欄の値は、もう整っている】AccountPageBase.OnLoad が NumberInput.FixFields を通している
    ' （前後の空白を省く・全角を半角に・数字でなければ空）。ここでは数字として読めるかと、日付として在るかだけを見る。
    ' 【これから先の日・古すぎる年は、ここで見ない】AccountProfileRules.Prepare の1か所（画面が変わっても決まりは同じ）
    Private Function ReadBirthDate(ByRef result As Date?, ByRef field As WebControl) As String

        result = Nothing
        field = Nothing

        Dim boxes As TextBox() = {BirthYearBox, BirthMonthBox, BirthDayBox}
        Dim filled As Integer = 0

        For Each box As TextBox In boxes
            If Not String.IsNullOrWhiteSpace(box.Text) Then
                filled += 1
            End If
        Next

        If filled = 0 Then
            Return Nothing
        End If

        If filled < boxes.Length Then

            For Each box As TextBox In boxes
                If String.IsNullOrWhiteSpace(box.Text) Then
                    field = box
                    Exit For
                End If
            Next

            Return "生年月日は、年・月・日の3つとも入れてください（出さないなら3つとも空に）。"

        End If

        Dim y, m, d As Integer

        If Not NumberInput.TryParseInt(BirthYearBox.Text, y) OrElse y < 1 OrElse y > 9999 Then
            field = BirthYearBox
            Return "生年月日の年が正しくありません。西暦（例：1971）で入れてください。"
        End If

        If Not NumberInput.TryParseInt(BirthMonthBox.Text, m) OrElse m < 1 OrElse m > 12 Then
            field = BirthMonthBox
            Return "生年月日の月は、1〜12で入れてください。"
        End If

        If Not NumberInput.TryParseInt(BirthDayBox.Text, d) OrElse d < 1 OrElse d > Date.DaysInMonth(y, m) Then
            field = BirthDayBox
            Return "生年月日の日が正しくありません（" & m & "月は" & Date.DaysInMonth(y, m) & "日まで）。"
        End If

        result = New Date(y, m, d)

        Return Nothing

    End Function


    ' ============================================================
    ' 断ったことを知らせる
    ' ============================================================
    '
    ' 【知らせは、画面の下の保存の帯に出す】（指摘：出る場所が2つあって、ちぐはぐだった）
    ' 保存した・画像を差し替えた・画像を外した・断った理由の4つとも、帯の中に出す。
    '
    ' 【断ったときは、悪い欄も知らせる】帯は画面の下にあるので、文だけでは「どの欄か」が分からない。
    ' 赤い枠（aria-invalid）を付けて、そこへカーソルを移す
    ' （`Page.SetFocus` は、ブラウザにその欄までスクロールさせる）
    Private Sub ShowError(message As String, Optional fieldKey As String = Nothing)
        ShowErrorOn(message, ControlOf(fieldKey))
    End Sub


    ' 欄を名前ではなく、その欄そのもので受け取る形（生年月日の3つの欄のように、名前が1つで欄が3つあるもの）
    Private Sub ShowErrorOn(message As String, target As WebControl)

        ErrorText.Text = HttpUtility.HtmlEncode(message)

        ' うまくいった知らせが残っていたら消す（2つ並ぶと、どちらが今の話か分からない）
        DoneText.Text = ""

        ' 【印を付けるだけ】理由をその欄の上へ移すのと、カーソルを置くのは、Scripts/site.js の「9」がやる
        ' （前は Page.SetFocus で欄へ飛ばしていたが、理由は下の帯に出たままで、
        '   スマホではキーボードに隠れた。サイト全体の決まり＝理由は入力欄の上、にそろえた）
        FormErrors.MarkField(target)

    End Sub


    ' 断る理由に添えられた名前から、その欄を引く（無ければ Nothing）
    '
    ' 【名前は Accounts/AccountProfileRules.vb が持っている】
    ' **欄を1つ足したら、あちらの定数と、ここと、下の一覧の3つに足すこと**
    Private Function ControlOf(fieldKey As String) As WebControl

        Select Case fieldKey

            Case AccountProfileRules.FieldDisplayName
                Return DisplayNameBox

            Case AccountProfileRules.FieldIntroduction
                Return IntroductionBox

            Case AccountProfileRules.FieldBirthDate
                ' 【3つの欄の代表は「年」】Prepare が断るのは、これから先の日・古すぎる年（どちらも年を直すことが多い）
                Return BirthYearBox

            Case AccountProfileRules.FieldOccupationText
                Return OccupationTextBox

            Case AccountProfileRules.FieldX
                Return XBox

            Case AccountProfileRules.FieldFacebook
                Return FacebookBox

            Case AccountProfileRules.FieldInstagram
                Return InstagramBox

            Case AccountProfileRules.FieldAvatarLetter
                Return AvatarLetterBox

            Case AccountProfileRules.FieldAvatarColor
                Return AvatarColorBox

            Case Else
                Return Nothing

        End Select

    End Function


    ' 前の回に付いた赤い枠を消す
    '
    ' 【要る理由】`WebControl.Attributes` は ViewState に残るので、
    ' 消さないと、一度エラーになった欄がそのあとずっと赤いままになる
    Private Sub ClearInvalidMarks()

        Dim keys As String() = {
            AccountProfileRules.FieldDisplayName,
            AccountProfileRules.FieldIntroduction,
            AccountProfileRules.FieldBirthDate,
            AccountProfileRules.FieldOccupationText,
            AccountProfileRules.FieldX,
            AccountProfileRules.FieldFacebook,
            AccountProfileRules.FieldInstagram,
            AccountProfileRules.FieldAvatarLetter,
            AccountProfileRules.FieldAvatarColor}

        For Each key As String In keys

            Dim target As WebControl = ControlOf(key)

            If target IsNot Nothing Then
                target.Attributes.Remove("aria-invalid")
            End If

        Next

        ' 生年月日の月・日（代表の「年」は上の一覧で消える。名前を持たない2つの欄）
        BirthMonthBox.Attributes.Remove("aria-invalid")
        BirthDayBox.Attributes.Remove("aria-invalid")

    End Sub


    ' ============================================================
    ' 画像を外す
    ' ============================================================
    '
    ' 【GET のリンクにしないこと】ログインの Cookie は SameSite=Lax で、
    ' **Lax はよそのサイトからの GET のページ移動には Cookie を付けて送る**。
    ' GET で消せる形だと、よそのページに置いたリンク1つで画像を消させられる（決まり）

    Protected Sub AvatarRemoveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles AvatarRemoveButton.Click

        RemoveImage(ProfileImageKind.Avatar)

    End Sub

    Protected Sub CoverRemoveButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles CoverRemoveButton.Click

        RemoveImage(ProfileImageKind.Cover)

    End Sub

    Private Sub RemoveImage(kind As ProfileImageKind)

        Try
            AccountProfileImages.Replace(CurrentAccount.AccountId, kind, Nothing)
        Catch ex As Exception

            ErrorReport.Report(ex, "AccountProfilePage.RemoveImage")

            ShowError("画像を外せませんでした。少し待ってから、もう一度お試しください。")

            Return

        End Try

        Response.Redirect(SelfUrl & "?done=image")

    End Sub

End Class
