' 管理人からのお知らせの「決まり」（ブログ型の⑤）
'
' 【何を置く場所か】文字数の上限・行き先の形の検査・画面に出す日付の形を、ここ1か所に集めてある。
' Articles/ArticleRules.vb・Contacts/ContactRules.vb と同じ立ち位置である。
'
' 【SiteSettings には入れない】
' ここにある値は「管理画面から変えたい設定」ではなく、画面・DB・SQL の3つが噛み合っている
' 「決まり」である（ArticleRules の先頭のコメントと同じ判断）。
'
' 【なぜ Notices\ という新しいフォルダを作ったのか】
' お知らせは記事ではない（本文も・カテゴリも・目次も・URL の名前も持たない）。
' Articles\ に混ぜると、**記事の一覧・ブログのトップ・更新履歴・カテゴリの一覧の全部に
' 「お知らせは除く」という条件が要る**（1か所忘れると、お知らせが記事として出る）。
' 問い合わせページのために Contacts\ を作ったのと、まったく同じ判断である。
'
' 【URL は /account/notices（2区切り）なので、403.14 の罠には当たらない】
' 罠は「URL の1区切り目と同じ名前のフォルダ」で起きる（「URL構成」）。
' **notices だけの1区切りのルートを足すなら、そのときは罠に当たる**
Public NotInheritable Class SiteNoticeRules

    Private Sub New()
    End Sub


    ' === 長さ ===
    '
    ' 【DB の列幅と同じ数にしてある】（Database/CreateTable/SiteNotices.sql）
    ' ここを広くすると、画面が通した長さで SQL 例外になり、その1件が丸ごと失われる
    ' （NicknameMaxLength と ChatMessages.Nickname で踏んだ罠と、同じ形）

    ' 一言（必須）
    Public Const BodyMaxLength As Integer = 200

    ' 行き先（任意）
    Public Const LinkMaxLength As Integer = 400


    ' 右の枠に出す本数
    '
    ' 【、管理画面で変えられる】（SiteSettings.SidebarNoticeCount。既定5＝更新履歴と同じ。基本ルール T）
    Public Shared ReadOnly Property ShowCount As Integer
        Get
            Return SiteSettings.SidebarNoticeCount
        End Get
    End Property


    ' ============================================================
    ' 検査（戻り値は断る理由。受け付けてよければ Nothing）
    ' ============================================================

    ' 日付
    '
    ' 【未来の日付を許す】「こうなります」を、先に書いておけるようにするため
    ' （出すか出さないかは IsVisible で決める）。
    ' 【範囲だけ見る】DATE 列に入らない値（1753年より前など）を止める
    Public Shared Function ValidateDate(text As String, ByRef result As Date) As String

        result = Date.MinValue

        Dim trimmed As String = If(text, "").Trim()

        If trimmed.Length = 0 Then
            Return "日付を入力してください。"
        End If

        Dim parsed As Date

        ' 【文化を明示して読む】共用サーバーのカルチャ次第で、年が和暦として読まれうる
        ' （日付を出すときに文化を渡すのと、同じ理由）。
        ' 画面の欄は <input type="date"> なので、来るのは必ず yyyy-MM-dd である
        If Not Date.TryParse(trimmed, Globalization.CultureInfo.InvariantCulture,
                             Globalization.DateTimeStyles.None, parsed) Then
            Return "日付の形が正しくありません。"
        End If

        If parsed < New Date(1900, 1, 1) OrElse parsed > New Date(2999, 12, 31) Then
            Return "日付が正しくありません。"
        End If

        result = parsed.Date

        Return Nothing

    End Function


    ' 一言
    Public Shared Function ValidateBody(body As String) As String

        Dim trimmed As String = NormalizeBody(body)

        If trimmed.Length = 0 Then
            Return "お知らせの文を入力してください。"
        End If

        If trimmed.Length > BodyMaxLength Then
            Return "お知らせの文は" & BodyMaxLength & "文字以内にしてください。"
        End If

        Return Nothing

    End Function


    ' 一言を整える（前後の空白を落とし、改行を1つの空白へ寄せる）
    '
    ' 【1行のものだから、改行を残さない】右の枠の狭い場所に出すので、
    ' 改行を残すと、出すときに <br> にするか捨てるかを決めることになる。
    ' **入口で1行にしてしまえば、出口で悩まない**（問い合わせの NormalizeOneLine と同じ考え方）
    Public Shared Function NormalizeBody(body As String) As String

        Dim text As String = If(body, "")

        ' 【3つの改行コードを、まず寄せる】\r\n だけを直すのでは足りない
        ' （発言の正規化と同じ罠。「サーバー側の検証（発言）」）
        text = text.Replace(vbCrLf, " ").Replace(vbCr, " ").Replace(vbLf, " ")

        Return text.Trim()

    End Function


    ' 行き先（空でよい）
    '
    ' 【広げた】（要望：外部の URL を案内したいことがある
    '   ／内部のページでも http から始まる形で書けるようにする）
    ' 受け付けるのは3つの形だけ：
    '   ① このサイトの中（「/」で始まる形）              … そのまま
    '   ② このサイトの住所を丸ごと書いた形（https://vb-chat.example.com/…）
    '                                                      … **「/…」の形に直して保存する**（同じタブで開く）
    '   ③ よそのサイト（http:// か https:// だけ）      … 正規化した形で保存する（新しいタブで開く）
    '
    ' 【前は「このサイトの中だけ」だった】管理画面を乗っ取られたときに、サイトの全ページの右の枠から
    ' 好きな所へ人を飛ばせないようにするための守りだった。広げたので、**入口（ここ）と出口
    ' （SiteNotices.LinkKindOf）の2か所の確かめは、そのまま2枚で残す**。
    ' javascript: や data: のような、http と https 以外の形は、今までどおり受け付けない。
    '
    ' 【「このサイトか」は、要求のホスト名ではなく、決まった本番の住所で決める】
    ' 再設定のメールのリンクと同じ考え方（AccountMail.PublicBaseUrl）。
    ' 要求の Host ヘッダーは、送る側が好きに書ける。
    '
    ' 【「//」で始まるものを断る】「//evil.example/」は、ブラウザには
    ' 「今と同じスキームで、evil.example へ」と読まれる（プロトコル相対URL）。
    ' ②で直したあとにも、もう一度この確かめを通す——
    ' 「https://vb-chat.example.com//evil.example」を直すと「//evil.example」になるため
    Public Shared Function ValidateLink(link As String, ByRef result As String) As String

        result = Nothing

        Dim trimmed As String = NormalizeBody(link)

        If trimmed.Length = 0 Then
            Return Nothing
        End If

        If trimmed.Length > LinkMaxLength Then
            Return "行き先は" & LinkMaxLength & "文字以内にしてください。"
        End If

        Dim external As Uri = Nothing

        If LooksAbsolute(trimmed) Then

            If Not TryParseWebUrl(trimmed, external) Then
                Return "その行き先は使えません（http:// か https:// で始まるアドレスか、「/」で始まるこのサイトのアドレスにしてください）。"
            End If

            If IsThisSite(external) Then
                ' ② このサイトの住所 → 「/…」の形に直してから、①と同じ確かめへ
                trimmed = external.PathAndQuery & external.Fragment
            Else
                ' ③ よそのサイト
                Dim normalized As String = external.AbsoluteUri

                If normalized.Length > LinkMaxLength Then
                    Return "行き先は" & LinkMaxLength & "文字以内にしてください。"
                End If

                result = normalized
                Return Nothing
            End If

        End If

        ' ① このサイトの中
        If Not IsSiteLocalPath(trimmed) Then
            Return "その行き先は使えません（http:// か https:// で始まるアドレスか、「/」で始まるこのサイトのアドレスにしてください）。"
        End If

        result = trimmed

        Return Nothing

    End Function


    ' 行き先の種類（出口の確かめ。SiteNotices が使う）
    Public Enum LinkKind
        None = 0
        SiteLocal = 1
        External = 2
    End Enum


    ' 保存されている行き先が、どの種類か（どれにも当たらなければ None＝リンクにしない）
    '
    ' 【入口と同じ決まりで、もう一度見る】DB を SSMS から直に書き換えられても、
    ' ここを通らない形はリンクにならない
    Public Shared Function LinkKindOf(link As String) As LinkKind

        If String.IsNullOrEmpty(link) Then
            Return LinkKind.None
        End If

        If IsSiteLocalPath(link) Then
            Return LinkKind.SiteLocal
        End If

        Dim u As Uri = Nothing

        If LooksAbsolute(link) AndAlso TryParseWebUrl(link, u) AndAlso Not IsThisSite(u) Then
            Return LinkKind.External
        End If

        Return LinkKind.None

    End Function


    ' 「/」で始まり、「//」で始まらず、「\」も制御文字も含まない
    '
    ' 【「\」を断る】ブラウザによっては「/\evil.example」を「//evil.example」と同じに読む
    Private Shared Function IsSiteLocalPath(value As String) As Boolean

        If Not value.StartsWith("/", StringComparison.Ordinal) OrElse
           value.StartsWith("//", StringComparison.Ordinal) OrElse
           value.IndexOf("\"c) >= 0 Then
            Return False
        End If

        For Each c As Char In value
            If Char.IsControl(c) OrElse Char.IsWhiteSpace(c) Then
                Return False
            End If
        Next

        Return True

    End Function


    ' 「英字＋コロン」で始まるか（http: https: javascript: などの形）
    Private Shared Function LooksAbsolute(value As String) As Boolean
        Return Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9+.\-]*:")
    End Function


    ' http か https の、ちゃんとしたアドレスとして読めるか
    Private Shared Function TryParseWebUrl(value As String, ByRef result As Uri) As Boolean

        result = Nothing

        Dim u As Uri = Nothing

        If Not Uri.TryCreate(value, UriKind.Absolute, u) Then
            Return False
        End If

        If u.Scheme <> Uri.UriSchemeHttp AndAlso u.Scheme <> Uri.UriSchemeHttps Then
            Return False
        End If

        If String.IsNullOrEmpty(u.Host) Then
            Return False
        End If

        result = u
        Return True

    End Function


    ' このサイト（決まった本番の住所）か
    '
    ' 【ユーザー情報の付いた形は、このサイトと見なさない】
    ' 「https://vb-chat.example.com@evil.example/」のホストは evil.example なので、
    ' Uri が読んだホスト名で比べれば、見た目にだまされない
    Private Shared Function IsThisSite(u As Uri) As Boolean

        Dim site As New Uri(AccountMail.ProductionBaseUrl)

        Return String.Equals(u.Host, site.Host, StringComparison.OrdinalIgnoreCase) AndAlso
               String.IsNullOrEmpty(u.UserInfo)

    End Function


    ' ============================================================
    ' 画面に出す形
    ' ============================================================

    ' 日付の文字
    '
    ' 【記事の日付と同じ形にそろえる】（Articles/ArticleDisplay.vb の DateText）
    ' 右の枠では、お知らせと更新履歴が縦に並ぶので、日付の形が違うと目につく
    Public Shared Function DateText(value As Date) As String
        Return ArticleDisplay.DateText(value)
    End Function

End Class
