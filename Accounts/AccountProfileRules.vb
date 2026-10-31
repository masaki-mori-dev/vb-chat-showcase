Imports System.Globalization
Imports System.Text.RegularExpressions

' プロフィールの決まり（長さ・形の検査・整え方と、画面に出す文字の組み立て）
' （ブログ型の①）
'
' 【ここは純粋な関数だけ】DB にも HttpContext にも触らない。
' C#／ASP.NET Core MVC へ移るときに .aspx は捨てるが、「何を、どう確かめるか」はそのまま持っていく
' （Articles/ArticleRules.vb・Contacts/ContactRules.vb と同じ立ち位置）。
'
' 【表示のときにどう使われるかを、先に見越してある】（②③の回で使う）
'   ・自己紹介は**プレーンテキスト**で持つ … ③の右の枠で「出だしだけ」を切っても壊れない（Excerpt）
'   ・SNS は**利用者名だけ**を持ち、URL はこちらが組み立てる … javascript: が入る隙が無い
'   ・見せるかは列で持つ … 公開ページは、1回の読み出しで「出してよいか」まで分かる
Public NotInheritable Class AccountProfileRules

    Private Sub New()
    End Sub


    ' ============================================================
    ' 長さ（DB の列の幅と、必ずそろえること）
    ' ============================================================
    '
    ' 【SiteSettings に入れない】管理画面から変えたい値ではなく、
    ' 「列の幅を超えて保存が失敗する」のを防ぐための線である
    ' （ArticleRules.BodyMaxLength・AccountPassword.MinLength と同じ判断）。
    ' **DB の列を広げるときは、ここも一緒に直すこと**

    Public Const IntroductionMaxLength As Integer = 1000
    Public Const OccupationTextMaxLength As Integer = 50
    Public Const BlogTitleMaxLength As Integer = 60
    Public Const BlogDescriptionMaxLength As Integer = 200

    ' SNS の利用者名の長さ（それぞれのサービスの上限に合わせてある）
    Public Const XNameMaxLength As Integer = 15
    Public Const FacebookNameMaxLength As Integer = 50
    Public Const InstagramNameMaxLength As Integer = 30


    ' ============================================================
    ' 断ったとき、どの欄が悪いのか
    ' ============================================================
    '
    ' 【理由の文だけでは足りない】知らせは画面の下の帯に出るので、
    ' 長い画面では「どの欄のことか」が分からない。画面（Profile.aspx.vb）は、この名前から欄を引いて
    ' 赤い枠を付け、そこへカーソルを移す。
    '
    ' 【画面に見せる文字ではない】ここは、コードどうしの符丁である。
    ' **欄を1つ足したら、ここと画面の ControlOf の両方に1つ足すこと**

    Public Const FieldDisplayName As String = "displayname"
    Public Const FieldIntroduction As String = "introduction"
    Public Const FieldBirthDate As String = "birthdate"
    Public Const FieldOccupationText As String = "occupationtext"
    Public Const FieldBlogTitle As String = "blogtitle"
    Public Const FieldBlogDescription As String = "blogdescription"
    Public Const FieldX As String = "x"
    Public Const FieldFacebook As String = "facebook"
    Public Const FieldInstagram As String = "instagram"
    Public Const FieldAvatarLetter As String = "avatarletter"
    Public Const FieldAvatarColor As String = "avatarcolor"


    ' ============================================================
    ' SNS の利用者名
    ' ============================================================
    '
    ' 【URL ではなく利用者名を持つ】（理由は AccountProfiles.sql）
    ' 要望：そのサービスのアドレスしか受け付けない（javascript: などを仕込まれないように）。
    ' URL のまま持つと、**出すたびに「本当にそのサービスか」を確かめ続ける**ことになる。
    ' 利用者名なら、入れるときに文字の形だけを見ればよく、URL はこちらが組み立てるので、
    ' 「javascript:」や「//evil.example」が入る隙が、そもそも無い。
    '
    ' 【欄には URL を貼ってもよい】人は自分のページを開いて、アドレスをコピーして貼る。
    ' そこから利用者名を取り出すのは、こちらの仕事である

    Private Shared ReadOnly XNamePattern As New Regex("^[A-Za-z0-9_]{1,15}$", RegexOptions.Compiled)
    Private Shared ReadOnly FacebookNamePattern As New Regex("^[A-Za-z0-9.]{5,50}$", RegexOptions.Compiled)
    Private Shared ReadOnly InstagramNamePattern As New Regex("^[A-Za-z0-9._]{1,30}$", RegexOptions.Compiled)

    ' 受け付けるホスト名（www. は落としてから見る）
    Private Shared ReadOnly XHosts As String() = {"x.com", "twitter.com", "mobile.twitter.com"}
    Private Shared ReadOnly FacebookHosts As String() = {"facebook.com", "m.facebook.com", "fb.com"}
    Private Shared ReadOnly InstagramHosts As String() = {"instagram.com"}


    ' 画面に打たれた文字列から、X（旧 Twitter）の利用者名を取り出す
    '
    ' 戻り値 … 利用者名（空欄なら Nothing）。受け付けられないときは reason に理由が入る
    Public Shared Function NormalizeXName(input As String, ByRef reason As String) As String
        Return NormalizeSnsName(input, XHosts, XNamePattern,
                                "X（旧 Twitter）", "半角の英数字とアンダースコア（_）で15文字まで", reason)
    End Function

    Public Shared Function NormalizeFacebookName(input As String, ByRef reason As String) As String
        Return NormalizeSnsName(input, FacebookHosts, FacebookNamePattern,
                                "Facebook", "半角の英数字とピリオド（.）で5〜50文字", reason)
    End Function

    Public Shared Function NormalizeInstagramName(input As String, ByRef reason As String) As String
        Return NormalizeSnsName(input, InstagramHosts, InstagramNamePattern,
                                "Instagram", "半角の英数字とピリオド（.）・アンダースコア（_）で30文字まで", reason)
    End Function


    ' SNS の利用者名の取り出し（3つで共通）
    '
    ' 受け付ける形は3つ … 「name」「@name」「https://（そのサービス）/name」
    '
    ' 【ほかのサービスの URL は、はっきり断る】黙って利用者名として受け取ると、
    ' たとえば「https://evil.example/abc」の「abc」だけが残り、
    ' **本人が貼ったつもりのアドレスと違うところへ飛ぶリンク**ができる
    Private Shared Function NormalizeSnsName(input As String,
                                             hosts As String(),
                                             pattern As Regex,
                                             serviceName As String,
                                             formText As String,
                                             ByRef reason As String) As String

        reason = Nothing

        Dim text As String = If(input, "").Trim()

        If text.Length = 0 Then
            Return Nothing
        End If

        ' 「@name」の形
        If text.StartsWith("@", StringComparison.Ordinal) Then
            text = text.Substring(1).Trim()
        End If

        If LooksLikeUrl(text, hosts) Then

            Dim fromUrl As String = NameFromUrl(text, hosts)

            If fromUrl Is Nothing Then
                reason = serviceName & "のアドレスか、利用者名を入れてください。"
                Return Nothing
            End If

            text = fromUrl

        End If

        If Not pattern.IsMatch(text) Then
            reason = serviceName & "の利用者名の形が違います（" & formText & "）。"
            Return Nothing
        End If

        Return text

    End Function


    ' URL らしいか（スキームが付いているか、受け付けるホスト名で始まっているか）
    '
    ' 【「.」が入っているかでは決めない】Facebook の利用者名は「john.smith」のように
    ' ピリオドを含みうるので、それを URL と見なすと、ふつうの利用者名が断られる
    Private Shared Function LooksLikeUrl(text As String, hosts As String()) As Boolean

        Dim lower As String = text.ToLowerInvariant()

        If lower.StartsWith("http://", StringComparison.Ordinal) OrElse
           lower.StartsWith("https://", StringComparison.Ordinal) OrElse
           lower.StartsWith("//", StringComparison.Ordinal) Then
            Return True
        End If

        For Each host As String In hosts
            If lower.StartsWith(host & "/", StringComparison.Ordinal) OrElse
               lower.StartsWith("www." & host & "/", StringComparison.Ordinal) Then
                Return True
            End If
        Next

        Return False

    End Function


    ' URL から利用者名を取り出す（受け付けるホストでなければ Nothing）
    Private Shared Function NameFromUrl(text As String, hosts As String()) As String

        Dim candidate As String = text

        If candidate.StartsWith("//", StringComparison.Ordinal) Then
            candidate = "https:" & candidate
        ElseIf Not candidate.ToLowerInvariant().StartsWith("http", StringComparison.Ordinal) Then
            candidate = "https://" & candidate
        End If

        Dim uri As Uri = Nothing

        If Not Uri.TryCreate(candidate, UriKind.Absolute, uri) Then
            Return Nothing
        End If

        ' 【スキームを確かめる】ここを飛ばすと「javascript:x.com/abc」のようなものが通りうる
        If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then
            Return Nothing
        End If

        Dim host As String = uri.Host.ToLowerInvariant()

        If host.StartsWith("www.", StringComparison.Ordinal) Then
            host = host.Substring(4)
        End If

        Dim known As Boolean = False

        For Each allowed As String In hosts
            If host = allowed Then
                known = True
                Exit For
            End If
        Next

        If Not known Then
            Return Nothing
        End If

        ' 最初の区切りだけを見る（/name?lang=ja や /name/ も同じ名前になる）
        Dim path As String = uri.AbsolutePath.Trim("/"c)

        If path.Length = 0 Then
            Return Nothing
        End If

        Dim slash As Integer = path.IndexOf("/"c)

        If slash >= 0 Then
            path = path.Substring(0, slash)
        End If

        Return Uri.UnescapeDataString(path)

    End Function


    ' ============================================================
    ' 出すときの URL（組み立てるのは、この3つだけ）
    ' ============================================================
    '
    ' 【名前は、必ず保存のときに形を確かめてある】それでも出口で組み立てを1か所にしてあるのは、
    ' 「どこで URL が作られているか」を探さずに済むようにするため

    Public Shared Function XUrl(userName As String) As String
        Return If(String.IsNullOrEmpty(userName), Nothing, "https://x.com/" & userName)
    End Function

    Public Shared Function FacebookUrl(userName As String) As String
        Return If(String.IsNullOrEmpty(userName), Nothing, "https://www.facebook.com/" & userName)
    End Function

    Public Shared Function InstagramUrl(userName As String) As String
        Return If(String.IsNullOrEmpty(userName), Nothing, "https://www.instagram.com/" & userName & "/")
    End Function


    ' ============================================================
    ' 文字列の整え
    ' ============================================================

    ' 1行の欄（前後の空白を落とし、空なら Nothing）
    '
    ' 【空文字ではなく Nothing にする】DB には「入れていない＝NULL」で持つ。
    ' 空文字を入れると、「空欄」と「空文字を入れた」の2つの状態ができる
    Public Shared Function NormalizeLine(value As String) As String

        Dim text As String = If(value, "").Trim()

        Return If(text.Length = 0, Nothing, text)

    End Function


    ' 自己紹介（改行を残したまま整える）
    '
    ' 【3つの改行コードを \n へ寄せてから、3つ以上続く改行を2つにまとめる】
    ' チャットの発言の正規化（ChatHub）と、まったく同じ形。
    ' \r だけを1000個送られても、ブラウザでは改行として出るので、そこも寄せておく
    Public Shared Function NormalizeMultiLine(value As String) As String

        Dim text As String = If(value, "")

        text = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        text = Regex.Replace(text, "\n{3,}", vbLf & vbLf)
        text = text.Trim()

        Return If(text.Length = 0, Nothing, text)

    End Function


    ' ============================================================
    ' 表示（②③の回で使う）
    ' ============================================================

    ' 自己紹介の「出だし」を切る（③の右の枠の、プロフィールの簡易版）
    '
    ' 【自己紹介をプレーンテキストで持っているから、これができる】
    ' HTML だったら、途中で切った瞬間にタグが壊れる（記事の本文の抜粋を作っていないのは、そのため）。
    '
    ' 【改行は、1つの空白にまとめる】狭い枠に数行だけ出すので、元の改行をそのまま入れると
    ' 「1行だけ出して終わり」になりうる
    Public Shared Function Excerpt(text As String, maxChars As Integer) As String

        If String.IsNullOrEmpty(text) OrElse maxChars <= 0 Then
            Return ""
        End If

        Dim flat As String = Regex.Replace(text, "\s+", " ").Trim()

        If flat.Length <= maxChars Then
            Return flat
        End If

        Dim cut As Integer = maxChars

        ' 【サロゲートペアの途中で切らない】絵文字（2文字ぶんで1字）を半分にすると、文字化けする
        If Char.IsHighSurrogate(flat(cut - 1)) Then
            cut -= 1
        End If

        Return flat.Substring(0, cut) & "…"

    End Function


    ' 生年月日を、出しかたに合わせた文字にする（出さないなら空文字）
    '
    ' 【書式指定を使わず、自前で連結している】共用サーバーのカルチャ次第で和暦になるため
    ' （Articles/ArticleDisplay.vb の日付と、まったく同じ理由）
    Public Shared Function BirthDateText(birthDate As Date?, mode As Integer) As String

        If Not birthDate.HasValue Then
            Return ""
        End If

        Dim d As Date = birthDate.Value

        Select Case mode

            Case ProfileChoices.BirthMonthDay
                Return d.Month.ToString(CultureInfo.InvariantCulture) & "月" &
                       d.Day.ToString(CultureInfo.InvariantCulture) & "日"

            Case ProfileChoices.BirthFull
                Return d.Year.ToString(CultureInfo.InvariantCulture) & "年" &
                       d.Month.ToString(CultureInfo.InvariantCulture) & "月" &
                       d.Day.ToString(CultureInfo.InvariantCulture) & "日"

            Case Else
                Return ""

        End Select

    End Function


    ' 職業（選んだもの ＋ 自由記入）を、1つの文字にする
    '
    ' 【選ばずに自由記入だけ、も許す】当てはまるものが無い人を止めないため
    Public Shared Function OccupationText(occupation As Integer?, freeText As String) As String

        Dim chosen As String = ProfileChoices.OccupationName(occupation)
        Dim extra As String = If(freeText, "").Trim()

        If chosen.Length = 0 Then
            Return extra
        End If

        If extra.Length = 0 Then
            Return chosen
        End If

        Return chosen & "（" & extra & "）"

    End Function


    ' ブログのタイトルと説明を整えて確かめる（ブログの設定の画面＝Articles/BlogSettings.aspx から呼ぶ）
    '
    ' 【戻り値】断る理由（通れば Nothing）。title・description は整えた形に書き換える（空なら Nothing）。
    ' fieldKey には、どの欄の話か（FieldBlogTitle・FieldBlogDescription）が入る
    Public Shared Function PrepareBlog(ByRef title As String, ByRef description As String,
                                       ByRef fieldKey As String) As String

        fieldKey = Nothing

        title = NormalizeLine(title)

        If title IsNot Nothing AndAlso title.Length > BlogTitleMaxLength Then
            fieldKey = FieldBlogTitle
            Return "ブログのタイトルは" & BlogTitleMaxLength & "文字以内にしてください。"
        End If

        description = NormalizeMultiLine(description)

        If description IsNot Nothing AndAlso description.Length > BlogDescriptionMaxLength Then
            fieldKey = FieldBlogDescription
            Return "ブログの説明は" & BlogDescriptionMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' ブログのタイトル（決めていなければ「〇〇の記事」）。1か所へまとめた
    '
    ' 使うのは、ブログのトップの見出し（Articles/AccountTop.aspx.vb）・公開のプロフィールのリンクの文字
    ' （Accounts/PublicProfile.aspx.vb）・ブログの記事一覧の「ブログ：〇〇」（Articles/Manage.aspx.vb）・ブログの設定の画面の案内。
    ' **片方だけ変えると、同じブログの名前が画面ごとに食い違う**ので、決め方はここだけにある
    Public Shared Function BlogTitleOf(blogTitle As String, displayName As String) As String

        If Not String.IsNullOrWhiteSpace(blogTitle) Then
            Return blogTitle
        End If

        Return displayName & "の記事"

    End Function


    ' ============================================================
    ' 検査（画面から集めた値を整えて、断る理由を返す）
    ' ============================================================
    '
    ' 【record の中身を、その場で書き換える】（整えた値を入れ直す）
    ' 呼ぶ側は、戻り値が Nothing なら、そのまま保存してよい。
    '
    ' 【長さの検査は、整えたあとの長さで見る】前後の空白を落とす前の長さで断ると、
    ' 「空白を消せば入るのに断られる」ことになる
    '
    ' 【fieldKey に、悪かった欄の名前を入れて返す】
    ' 知らせが画面の下の帯に出るようになったので、理由の文だけでは「どの欄か」が分からない。
    ' 画面はこの名前から欄を引いて、赤い枠を付けてカーソルを移す
    Public Shared Function Prepare(record As AccountProfileStore.ProfileRecord,
                                   ByRef fieldKey As String) As String

        fieldKey = Nothing

        If record Is Nothing Then
            Return "プロフィールを読み取れませんでした。ページを開き直してから、もう一度お試しください。"
        End If

        ' --- 自己紹介 ---
        record.Introduction = NormalizeMultiLine(record.Introduction)

        If record.Introduction IsNot Nothing AndAlso record.Introduction.Length > IntroductionMaxLength Then
            fieldKey = FieldIntroduction
            Return "自己紹介は" & IntroductionMaxLength.ToString("#,0") & "文字以内にしてください。"
        End If

        ' --- 選ぶ欄（一覧に無い値は「選んでいない」へ落とす） ---
        record.Gender = ProfileChoices.NormalizeOrNothing(ProfileChoices.Genders(), record.Gender)
        record.BloodType = ProfileChoices.NormalizeOrNothing(ProfileChoices.BloodTypes(), record.BloodType)
        record.RelationshipStatus =
            ProfileChoices.NormalizeOrNothing(ProfileChoices.Relationships(), record.RelationshipStatus)
        record.Occupation = ProfileChoices.NormalizeOrNothing(ProfileChoices.Occupations(), record.Occupation)
        record.Hometown = ProfileChoices.NormalizeOrNothing(ProfileChoices.Prefectures(), record.Hometown)
        record.Residence = ProfileChoices.NormalizeOrNothing(ProfileChoices.Prefectures(), record.Residence)

        ' --- 生年月日の出しかた（2択）と、公開・非公開 ---
        ' 【決めた値の外なら、年月日へ】2択のどちらでもない値は、画面を書き換えて送られたときだけ来る
        If Not ProfileChoices.IsBirthDateMode(record.BirthDateMode) Then
            record.BirthDateMode = ProfileChoices.BirthFull
        End If

        record.BirthDateVisibility = ProfileChoices.NormalizeVisibility(record.BirthDateVisibility)

        ' 【ありえない日付は断る】未来の日付・150年より前
        If record.BirthDate.HasValue Then

            Dim d As Date = record.BirthDate.Value.Date

            ' 理由を分けた（3つの欄にしたので、どこを直せばよいかを言える）
            If d > Date.Today Then
                fieldKey = FieldBirthDate
                Return "生年月日に、これから先の日は入れられません。"
            End If

            If d < Date.Today.AddYears(-150) Then
                fieldKey = FieldBirthDate
                Return "生年月日の年が古すぎます。西暦（例：1971）で入れてください。"
            End If

        End If

        ' --- 職業の自由記入 ---
        record.OccupationText = NormalizeLine(record.OccupationText)

        If record.OccupationText IsNot Nothing AndAlso
           record.OccupationText.Length > OccupationTextMaxLength Then
            fieldKey = FieldOccupationText
            Return "職業の説明は" & OccupationTextMaxLength & "文字以内にしてください。"
        End If

        ' --- ブログ（タイトルと説明）は、ここでは見ない ---
        ' 【、プロフィールの画面からブログの設定（Articles/BlogSettings.aspx）へ移した】
        ' 確かめと整え方は上の PrepareBlog。保存も AccountProfileStore.SaveBlog の別の道（プロフィールの保存はこの2列に触らない）

        ' --- SNS ---
        Dim reason As String = Nothing

        record.XUserName = NormalizeXName(record.XUserName, reason)

        If reason IsNot Nothing Then
            fieldKey = FieldX
            Return reason
        End If

        record.FacebookUserName = NormalizeFacebookName(record.FacebookUserName, reason)

        If reason IsNot Nothing Then
            fieldKey = FieldFacebook
            Return reason
        End If

        record.InstagramUserName = NormalizeInstagramName(record.InstagramUserName, reason)

        If reason IsNot Nothing Then
            fieldKey = FieldInstagram
            Return reason
        End If

        ' --- 文字のアイコン（決まりは Accounts/AccountAvatar.vb） ---
        '
        ' 【空なら「最初の文字」に戻す】Nothing を入れる＝DB の NULL
        record.AvatarLetter = AccountAvatar.NormalizeLetter(record.AvatarLetter, reason)

        If reason IsNot Nothing Then
            fieldKey = FieldAvatarLetter
            Return reason
        End If

        ' 【最初の色と同じなら、入れない（NULL）】色の一覧を入れ替えた日に、変えていない人が付いていけるように
        If String.IsNullOrWhiteSpace(record.AvatarColor) Then
            record.AvatarColor = Nothing
        Else

            Dim color As String = AccountAvatar.NormalizeColor(record.AvatarColor)

            If color Is Nothing Then
                fieldKey = FieldAvatarColor
                Return "アイコンの色が正しくありません。色のボタンから選び直してください。"
            End If

            record.AvatarColor = If(color = AccountAvatar.DefaultColor(record.AccountId), Nothing, color)

        End If

        ' --- 見せるか（画面のチェックボックスから来た値を、決めた2つへ落とす） ---
        record.IntroductionVisibility = ProfileChoices.NormalizeVisibility(record.IntroductionVisibility)
        record.GenderVisibility = ProfileChoices.NormalizeVisibility(record.GenderVisibility)
        record.BloodTypeVisibility = ProfileChoices.NormalizeVisibility(record.BloodTypeVisibility)
        record.HometownVisibility = ProfileChoices.NormalizeVisibility(record.HometownVisibility)
        record.ResidenceVisibility = ProfileChoices.NormalizeVisibility(record.ResidenceVisibility)
        record.RelationshipStatusVisibility =
            ProfileChoices.NormalizeVisibility(record.RelationshipStatusVisibility)
        record.OccupationVisibility = ProfileChoices.NormalizeVisibility(record.OccupationVisibility)
        record.XVisibility = ProfileChoices.NormalizeVisibility(record.XVisibility)
        record.FacebookVisibility = ProfileChoices.NormalizeVisibility(record.FacebookVisibility)
        record.InstagramVisibility = ProfileChoices.NormalizeVisibility(record.InstagramVisibility)

        Return Nothing

    End Function


    ' 公開のプロフィールページの URL（/profile/{ユーザー名}）。ブログ型の②
    '
    ' 【文字列で組み立てないこと】ArticleRules.AccountUrl・PageUrl と同じ決まりで、
    ' 組み立てるのはここ1か所だけ。URL の形を変える日が来ても、直すのはここになる
    Public Shared Function PublicUrl(userName As String) As String
        Return "/profile/" & HttpUtility.UrlPathEncode(If(userName, ""))
    End Function

End Class
