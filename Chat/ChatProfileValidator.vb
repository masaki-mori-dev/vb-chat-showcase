Imports System.Text.RegularExpressions

' 入室時にクライアントから送られてくるプロフィール（ニックネーム・アイコン種別・アイコン色）を、
' サーバー側で検査して安全な値に整えるためのクラス
'
' 【なぜサーバー側で検査するのか】
' この3つはCookieの値をクライアントが送ってくるもので、いくらでも書き換えられる。
' 開発者ツールからハブを直接呼べば、Cookieすら経由しない。
' そのままParticipantへ格納すると、部屋にいる全員へ配信され、ChatMessagesにも保存され、
' 過去ログとして何度も再生される。つまり「信用の境界」はサーバー側にしか置けない
'
' 【方針：入室そのものは通し、値だけ既定値へ落とす】
' 不正な値を理由に入室を断ると、Cookieが壊れただけの利用者を締め出すことになる。
' 攻撃に対しては、値を無害化した時点で目的を達している
'
' 【出口側の処理と、役割が別であること】
' ChatRoom.aspx のJavaScript側でも、アイコン色・アイコン種別を escapeAttr に通している。
' 入口（ここ）の検査は、これから入ってくる新しい値を止める。
' 出口の処理は、既にDBへ入っている値を無害化する。どちらか一方では足りない
Public Class ChatProfileValidator

    ' ニックネームの上限（文字数）
    '
    ' 【SiteSettingsへ移した】
    ' 値はSiteSettings.NicknameMaxLength（既定12文字）が持つ。
    ' ここをプロパティとして残しているのは、呼び出し側（NormalizeNickname）と
    ' 画面側が、これまでどおり「プロフィールの決まりごと」としてこのクラスを
    ' 見に来られるようにするため
    '
    ' .NETのString.Lengthは、JavaScriptのlengthと同じくUTF-16の単位で数えるため、
    ' 絵文字（サロゲートペア）の扱いも画面側と一致する
    '
    ' 【DBの列幅とは、あえて一致させていない】
    ' ChatMessages.Nickname は NVARCHAR(50) で、こちらのほうが広い。
    ' 狭いほう（画面の仕様）に合わせておけば、広いほう（DBの限界）は自動的に満たされる。
    ' SiteSettings側で上限を50に丸めてあるのは、そこを超えさせないため
    Public Shared ReadOnly Property NicknameMaxLength As Integer
        Get
            Return SiteSettings.NicknameMaxLength
        End Get
    End Property

    ' アイコン色として認める形（#＋16進6桁）
    Private Shared ReadOnly IconColorPattern As New Regex(
        "^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled)

    ' 設定のアイコン色が読めなかった・壊れていたときの、最後の受け皿
    '
    ' 【SiteSettingsの既定値と同じ値だが、二重管理ではない】
    ' あちらは「運用者が変えられる値」、こちらは「その値が色として読めなかったときに、
    ' それでも画面へ出せる何か」である。IconCatalogのFallbackIconTypeと同じ位置づけ
    Private Const FallbackIconColor As String = "#2563EB"


    ' ニックネームを、前後の空白を取り除いたうえで、上限の長さに収める
    '
    ' 長すぎるぶんは切り詰める（入室そのものは通す）。
    ' ただし**空になった場合は、空文字のまま返す**。呼び出し側で入室を断ること。
    ' ここで「ゲスト」のような既定名を付けないのは、本人が決めていない名前で
    ' 発言がログに残ってしまうため。名前は本人が決めるものとする
    '
    ' 前後の空白を落とすのは、トップページ（`value.trim()`）と揃えるため
    Public Shared Function NormalizeNickname(nickname As String) As String

        If nickname Is Nothing Then
            Return String.Empty
        End If

        nickname = nickname.Trim()

        If nickname.Length > NicknameMaxLength Then
            Return nickname.Substring(0, SafeCutLength(nickname, NicknameMaxLength))
        End If

        Return nickname

    End Function


    ' 切る位置が、絵文字の真ん中でないことを確かめる
    '
    ' 【何が起きるのか】
    ' 絵文字（😀 など）は、UTF-16では2つの単位（サロゲートペア）で1文字を表す。
    ' 上限が12で、12番目がそのペアの前半だった場合、そのまま Substring(0, 12) で
    ' 切ると、**片割れだけが残る**。片割れは文字として成立しないため、
    ' 画面には「�」が出て、DBにも壊れた値が入る。
    '
    ' 【文字数の数え方は、変えていない】
    ' 上限は今までどおりUTF-16の単位で12のままである（つまり絵文字1つで2つ減る）。
    ' JavaScriptの .length、VBの String.Length、DBの NVARCHAR は、いずれも
    ' UTF-16の単位で数える。この3か所を揃えてあることが、
    ' 「画面では入力できたのに、サーバーに弾かれる」を起こさない土台になっている
    ' （「サーバー側の検証（発言）」の章、決定）。
    '
    ' **書記素単位に変えてはいけない。** 数え方を合わせる場所が増えるだけで、
    ' 同じ罠を自分で作ることになる。ここで直すのは「割らない」ことだけである。
    '
    ' 【普通の利用では、ここまで来ない】
    ' トップページの maxlength は、割れるくらいならその文字を入れない
    ' （Chrome 148で実測。残り1単位のところへ絵文字を入れても、1文字も入らない）。
    ' しかし Cookie を直接書き換えたり、ハブを直接呼んだりすれば、
    ' 上限を超えた文字列がそのままここへ届く。だからサーバー側で受け止める
    Private Shared Function SafeCutLength(value As String, maxLength As Integer) As Integer

        ' 切ろうとしている位置の直前が「ペアの前半」なら、1つ手前で切る
        ' （そのぶん1単位短くなるが、壊れた文字を作るよりよい）
        If maxLength > 0 AndAlso maxLength < value.Length AndAlso
           Char.IsHighSurrogate(value(maxLength - 1)) Then

            Return maxLength - 1
        End If

        Return maxLength

    End Function


    ' ブラウザ識別ID（ChatBrowserId Cookieの値）が、GUIDとして読める形かどうか
    '
    ' 【なぜ形まで見るのか】
    ' この値は「同じブラウザかどうか」の判定に使われ、一致した相手は部屋から
    ' 追い出される（同一ブラウザの2窓目による置き換え）。つまり、そのまま
    ' 「誰として扱われるか」を決める値である。空文字や短い固定文字列を許すと、
    ' 同じ値を送り合った人どうしが同一人物と見なされ、互いを追い出し合う。
    ' また ChatMessages.SenderBrowserId は UNIQUEIDENTIFIER 列のため、
    ' GUIDでない値はログに残らない（Guid.TryParseで弾かれてNULLになる）
    '
    ' 値そのものは加工せずに使う（大文字・小文字の正規化もしない）。
    ' LeaveBeacon.aspx.vb が同じCookieを素のまま読んで突き合わせているため、
    ' 片方だけ加工すると、退室ビーコンが本人を見つけられなくなる
    Public Shared Function IsValidBrowserId(browserId As String) As Boolean

        If String.IsNullOrEmpty(browserId) Then
            Return False
        End If

        Dim parsed As Guid
        Return Guid.TryParse(browserId, parsed)

    End Function


    ' アイコン種別を、実在するアイコン名に収める
    ' スプライトに無い名前だった場合は、既定のアイコンへ落とす
    Public Shared Function NormalizeIconType(iconType As String) As String

        If IconCatalog.IsValid(iconType) Then
            Return iconType
        End If

        Return IconCatalog.DefaultIconType

    End Function


    ' アイコン色を「#＋16進6桁」に収める
    ' 形が違う場合は、既定の色へ落とす
    Public Shared Function NormalizeIconColor(iconColor As String) As String

        If iconColor IsNot Nothing AndAlso IconColorPattern.IsMatch(iconColor) Then
            Return iconColor
        End If

        Return DefaultIconColor

    End Function


    ' 既定のアイコン色
    '
    ' 【appSettingsのThemeColorからSiteSettingsへ移した】
    ' 設定の名前もDefaultIconColorへ改めている。旧名のThemeColorは、用途が
    ' 「アイコンの初期色」であって画面全体の配色（Content/theme.css）とは別物なのに、
    ' 名前がそう読めなかったため
    '
    ' この値は、トップページ（Default.aspx.vb）がCookieを持たない新規の利用者へ
    ' 初期色として出しているものと同じで、そこに揃えている
    '
    ' 【自前のキャッシュは持たない】
    ' 以前はここで組み立てた結果をSyncLockつきで抱えていたが、
    ' SiteSettings自身がキャッシュするため、二重に持つ意味が無くなった。
    ' 二重に持つと、SiteSettings.Reload()を呼んでもこちらが古いままになり、
    ' 「設定を変えたのに、アイコンの色だけ変わらない」という形で表に出る
    Public Shared ReadOnly Property DefaultIconColor As String
        Get
            Return NormalizeConfiguredColor(SiteSettings.DefaultIconColor)
        End Get
    End Property


    ' 設定に書かれたアイコン色を、"#＋16進6桁" の形に整える
    Private Shared Function NormalizeConfiguredColor(value As String) As String

        If String.IsNullOrEmpty(value) Then
            Return FallbackIconColor
        End If

        value = value.Trim()

        ' "#" 付きで書かれていても、付いていなくても、どちらでも受け取れるようにしておく
        ' （旧appSettingsのThemeColorは "#" 無しの形（2563EB）で書かれていた。
        '   移行後もどちらでも通るようにして、書き方で事故が起きないようにする）
        If Not value.StartsWith("#") Then
            value = "#" & value
        End If

        ' 設定側が壊れていた場合に、壊れた色をそのまま配ってしまわないよう、
        ' 既定値そのものも、利用者から来た値と同じ検査に通す
        If Not IconColorPattern.IsMatch(value) Then
            Return FallbackIconColor
        End If

        Return value

    End Function

End Class
