Imports System.Globalization

' アカウントのアイコン（開発者の案）
'
' 【何をするか】アカウントのアイコンを、**サイトのどこに出すときも、ここ1か所で組み立てる**（基本ルールの12）。
'   ・写真（AccountProfiles.AvatarImageName）があれば、写真
'   ・無ければ「色の付いた丸に1文字」
' 出している場所＝右上の角（Site.Master の AccountCornerHtml）・右の枠の「新しいメンバー」（NewMembers）・
' ブログの右の枠（BlogSidebar）・ブログのトップの帯（AccountTop）・公開のプロフィール（PublicProfile）・
' プロフィールの編集画面の見本（Profile）。**アイコンを出す場所を足したら、ここの Html を呼ぶこと**（写して書かない）。
'
' 【最初の文字と色は、こちらが決める】要望：どちらも最初からこちらで決めておき、そのままでよければ利用者は何もしなくてよい
'   ・文字＝名前（表示名）の1文字目。記号や絵文字で始まるなら、ユーザー名の頭文字（大文字）
'   ・色＝アカウントの番号から（同じ人は、どの画面でもいつも同じ色）
' **自分で変えたときだけ、DB（AccountProfiles.AvatarLetter・AvatarColor）に入る。NULL が「最初のまま」**
' ＝名前を変えれば、最初の文字もついて変わる。色の一覧を入れ替えた日も、変えていない人は付いていく。
'
' 【文字の色（白か黒）は、地の明るさで自動】理由：アイコンなので、文字が読める必要はない。デザインとして扱う。
' それでも、地に対して見えやすいほう（コントラスト比の大きいほう）を選ぶ。画面の JS（Scripts/site.js の「12」）にも同じ計算がある。
' **片方だけ直さないこと**（見本で見た色と、保存したあとの色が食い違う）
'
' 【1文字は「見た目の1文字」で数える】絵文字は、中では2文字以上のことがある（👨‍👩‍👧 は11、🇯🇵 は4）。
' .NET Framework の StringInfo は、絵文字のつなぎ（ZWJ）や旗を1文字に数えないので、ここで数えている（CountGraphemes）
Public NotInheritable Class AccountAvatar

    Private Sub New()
    End Sub


    ' ============================================================
    ' 決まり
    ' ============================================================

    ' DB の列の大きさ（AvatarLetter NVARCHAR(16)）。見た目の1文字を入れる器。
    ' 中の文字数（UTF-16）がこれを超える1文字は、断る（絵文字でも、ふつうは11まで）
    Public Const LetterMaxChars As Integer = 16

    ' 最初の色の一覧（どれも白か黒の文字が乗る、はっきりした色）。
    '
    ' 【並びを入れ替えてもよい】DB に入っているのは「自分で変えた色」だけなので、
    ' 入れ替えると、変えていない人の色がまとめて変わるだけで、壊れはしない
    Private Shared ReadOnly DefaultPalette As String() = {
        "#E53935", "#D81B60", "#8E24AA", "#5E35B1", "#3949AB", "#1E88E5", "#039BE5",
        "#00897B", "#43A047", "#7CB342", "#F4511E", "#FB8C00", "#6D4C41", "#546E7A"}

    ' 地の上に乗せる文字の色（明るい地には黒っぽい色、暗い地には白）
    Public Const LightText As String = "#FFFFFF"
    Public Const DarkText As String = "#1A1A1A"

    ' 白い文字にしてよい、いちばん低いコントラスト比（WCAG の大きい文字の基準）
    Private Const LargeTextContrast As Double = 3.0


    ' 1人ぶんの見た目
    Public Class Look
        ' 写真の URL（無ければ Nothing）
        Public Property ImageSrc As String
        Public Property Letter As String
        Public Property Color As String
        Public Property TextColor As String
    End Class


    ' ============================================================
    ' 組み立てる
    ' ============================================================

    ' 見た目を決める（写真 → 自分で決めた文字と色 → 最初の文字と色）
    '
    ' 【DB の値も、もう一度確かめてから使う】出口でも疑う（入口の検査だけでは足りない、のサイトの決まり）。
    ' 形の外の値なら、最初の文字・色へ落とす
    Public Shared Function Resolve(accountId As Integer, userName As String, displayName As String,
                                   avatarImageName As String, avatarLetter As String,
                                   avatarColor As String) As Look

        Dim letter As String = Nothing

        If Not String.IsNullOrEmpty(avatarLetter) AndAlso IsOneLetter(avatarLetter) Then
            letter = avatarLetter
        End If

        If letter Is Nothing Then
            letter = DefaultLetter(displayName, userName)
        End If

        Dim color As String = NormalizeColor(avatarColor)

        If color Is Nothing Then
            color = DefaultColor(accountId)
        End If

        Return New Look With {
            .ImageSrc = AccountProfileImages.SrcOf(avatarImageName),
            .Letter = letter,
            .Color = color,
            .TextColor = TextColorFor(color)}

    End Function


    ' HTML（写真なら <img>、無ければ <span> の丸）
    '
    ' sizeClass … 大きさの名前（"xs"＝24px／"sm"＝32px／"md"＝40px／"lg"＝64px／"xl"＝96px）。Content/Site.css の .account-avatar--〇〇
    ' extraClass … 置き場所ごとの class（余白など）。空でよい
    '
    ' 【alt は空・丸は aria-hidden】どこに出すときも、すぐ隣に名前が文字で出ている（読み上げソフトに2回言わせない）。
    ' 名前の無い場所に出すときは、まわりのボタン・リンクの名前（aria-label）で伝えること
    '
    ' 【色は style に入れる】値はこちらが確かめた #RRGGBB だけ（NormalizeColor）。文字は HtmlEncode
    Public Shared Function Html(look As Look, sizeClass As String, Optional extraClass As String = "") As String

        Dim cls As String = "account-avatar account-avatar--" & sizeClass &
                            If(String.IsNullOrEmpty(extraClass), "", " " & extraClass)

        If look Is Nothing Then
            Return "<span class=""" & cls & " account-avatar--letter"" aria-hidden=""true""></span>"
        End If

        If Not String.IsNullOrEmpty(look.ImageSrc) Then
            Return "<img class=""" & cls & """ src=""" & HttpUtility.HtmlAttributeEncode(look.ImageSrc) & """ alt="""" />"
        End If

        Return "<span class=""" & cls & " account-avatar--letter"" aria-hidden=""true"" style=""background-color:" &
               look.Color & ";color:" & look.TextColor & """>" & HttpUtility.HtmlEncode(look.Letter) & "</span>"

    End Function


    ' ============================================================
    ' 最初の文字・最初の色
    ' ============================================================

    ' 名前（表示名）の1文字目。文字か数字でなければ（記号・絵文字・空白）、ユーザー名の頭文字（大文字）
    '
    ' 【英字は大文字にする】「taro」なら「T」（Google のアイコンと同じ見せ方）。かな・漢字はそのまま
    Public Shared Function DefaultLetter(displayName As String, userName As String) As String

        Dim name As String = If(displayName, "").Trim()

        If name.Length > 0 AndAlso Char.IsLetterOrDigit(name, 0) Then

            ' 【サロゲートペア（𠮷 など）は2つで1文字】
            Dim length As Integer = If(Char.IsSurrogatePair(name, 0), 2, 1)

            Return name.Substring(0, length).ToUpperInvariant()

        End If

        ' ユーザー名は英数字とハイフン・アンダースコアだけ（AccountStore の決まり）。最初の英数字を使う
        For Each c As Char In If(userName, "")
            If Char.IsLetterOrDigit(c) Then
                Return Char.ToUpperInvariant(c).ToString()
            End If
        Next

        Return "?"

    End Function


    ' アカウントの番号から、色の一覧の1つを選ぶ
    '
    ' 【番号に5を掛けてから割った余り】そのまま割ると、続けて登録した人どうしが隣の色（赤とピンク）になる。
    ' 5 と一覧の数（14）は割り切れ合わないので、どの色も同じだけ使われる
    Public Shared Function DefaultColor(accountId As Integer) As String

        Dim n As Integer = DefaultPalette.Length
        Dim index As Integer = CInt((CLng(Math.Abs(accountId)) * 5L) Mod n)

        Return DefaultPalette(index)

    End Function


    ' 地の色に乗せる文字の色（WCAG の相対輝度で計算）
    '
    ' 【白で 3:1 あれば白、なければ黒っぽい色】丸の中の1文字は「大きい太字」なので、WCAG の大きい文字の基準（3:1）で足りる。
    ' 「比の大きいほう」で選ぶと、オレンジ・水色のような中くらいの明るさの色で黒になり、
    ' Gmail などの見慣れた「色の丸に白い文字」から外れて見えた（開発機で見て決めた）
    Public Shared Function TextColorFor(color As String) As String

        Dim normalized As String = NormalizeColor(color)

        If normalized Is Nothing Then
            Return LightText
        End If

        Dim r As Double = Channel(normalized.Substring(1, 2))
        Dim g As Double = Channel(normalized.Substring(3, 2))
        Dim b As Double = Channel(normalized.Substring(5, 2))

        Dim luminance As Double = 0.2126 * r + 0.7152 * g + 0.0722 * b

        ' 白（相対輝度 1）との比
        Dim withWhite As Double = 1.05 / (luminance + 0.05)

        Return If(withWhite >= LargeTextContrast, LightText, DarkText)

    End Function

    Private Shared Function Channel(hex As String) As Double

        Dim v As Double = Integer.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0

        Return If(v <= 0.03928, v / 12.92, Math.Pow((v + 0.055) / 1.055, 2.4))

    End Function


    ' ============================================================
    ' 画面から来た値を確かめる（プロフィールの編集画面。AccountProfileRules.Prepare から呼ぶ）
    ' ============================================================

    ' 文字。空なら Nothing（＝最初の文字に戻す）。1文字でなければ reason に理由を入れて Nothing
    Public Shared Function NormalizeLetter(raw As String, ByRef reason As String) As String

        reason = Nothing

        Dim text As String = If(raw, "").Trim()

        If text.Length = 0 Then
            Return Nothing
        End If

        If Not IsOneLetter(text) Then
            reason = "アイコンの文字は、1文字にしてください（絵文字なら1つ）。"
            Return Nothing
        End If

        Return text

    End Function


    ' 見た目の1文字か（空白・制御文字だけのものは数えない）
    Public Shared Function IsOneLetter(text As String) As Boolean

        If String.IsNullOrEmpty(text) OrElse text.Length > LetterMaxChars Then
            Return False
        End If

        If Char.IsWhiteSpace(text, 0) OrElse Char.IsControl(text, 0) Then
            Return False
        End If

        Return CountGraphemes(text) = 1

    End Function


    ' 色。#RRGGBB の形なら大文字にそろえて返す。形の外なら Nothing
    Public Shared Function NormalizeColor(raw As String) As String

        Dim text As String = If(raw, "").Trim()

        If text.Length = 6 Then
            text = "#" & text
        End If

        If text.Length <> 7 OrElse text(0) <> "#"c Then
            Return Nothing
        End If

        For i As Integer = 1 To 6
            If Not Uri.IsHexDigit(text(i)) Then
                Return Nothing
            End If
        Next

        Return text.ToUpperInvariant()

    End Function


    ' 見た目の文字数（書記素の数）。絵文字のつなぎ・旗・肌の色・異体字セレクタ・結合文字を、前の文字にまとめて数える
    '
    ' 【全部の決まり（UAX #29）ではない】このサイトで1文字を確かめるのに要る分だけ。
    ' 画面の側は Intl.Segmenter（本物）で数えるので、ふつうに打った1文字はどちらでも1になる
    Public Shared Function CountGraphemes(text As String) As Integer

        If String.IsNullOrEmpty(text) Then
            Return 0
        End If

        Dim count As Integer = 0
        Dim i As Integer = 0
        Dim joinNext As Boolean = False
        Dim regionalOpen As Boolean = False

        While i < text.Length

            Dim cp As Integer = Char.ConvertToUtf32(text, i)
            Dim width As Integer = If(Char.IsSurrogatePair(text, i), 2, 1)

            Dim isRegional As Boolean = (cp >= &H1F1E6 AndAlso cp <= &H1F1FF)

            If count = 0 Then
                count = 1
                regionalOpen = isRegional
            ElseIf joinNext Then
                ' ZWJ の次の文字は、前の文字につながる
                joinNext = False
            ElseIf IsExtender(cp) Then
                ' 前の文字の一部
            ElseIf isRegional AndAlso regionalOpen Then
                ' 旗（地域を表す文字の2つ目）
                regionalOpen = False
            Else
                count += 1
                regionalOpen = isRegional
            End If

            If cp = &H200D Then
                joinNext = True
            End If

            i += width

        End While

        Return count

    End Function

    Private Shared Function IsExtender(cp As Integer) As Boolean

        ' ZWJ・異体字セレクタ・肌の色・囲み（キーキャップ）・タグ（地域の旗）
        If cp = &H200D OrElse (cp >= &HFE00 AndAlso cp <= &HFE0F) OrElse
           (cp >= &H1F3FB AndAlso cp <= &H1F3FF) OrElse cp = &H20E3 OrElse
           (cp >= &HE0020 AndAlso cp <= &HE007F) OrElse (cp >= &HE0100 AndAlso cp <= &HE01EF) Then
            Return True
        End If

        ' 結合文字（濁点の結合形・アクセントなど）
        Dim category As UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(Char.ConvertFromUtf32(cp), 0)

        Return category = UnicodeCategory.NonSpacingMark OrElse
               category = UnicodeCategory.SpacingCombiningMark OrElse
               category = UnicodeCategory.EnclosingMark

    End Function


    ' ============================================================
    ' ログインしている人の見た目（右上の角）。メモリに控える
    ' ============================================================
    '
    ' 【なぜ控えるのか】右上の角は、ログインしている人が開く**すべてのページ**に出る。
    ' 名前（表示名）とユーザー名は、ログインの確かめ（AccountSession.Current）がもう読んでいるので、
    ' 足りないのはプロフィールの表の3列（写真・文字・色）だけ。それを毎回引くと、1ページにつき DB が1回増える。
    ' 3列は「自分で変えたとき」にしか変わらないので、人ごとに10分控え、変えたときに捨てる（Invalidate）。
    '
    ' 【控えを捨てる口】AccountProfileStore.Save（文字と色）・UpdateImage（写真）・退会（AccountRetire）。
    ' **プロフィールの表の3列を書き換える道を足したら、Invalidate も呼ぶこと**（呼ばないと、最大10分古いまま）
    '
    ' 【読めなかったら、最初の文字と色で出す】（フェイルオープン）見せるだけの値なので、ページは止めない

    Private Class Entry
        Public ImageName As String
        Public Letter As String
        Public Color As String
        Public ExpiresAt As DateTime
    End Class

    Private Shared ReadOnly Lifetime As TimeSpan = TimeSpan.FromMinutes(10)
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)

    ' 【上限】ログインしている人の数だけ増える。これを超えたら、いったん全部捨てる（伸び続けない）
    Private Const MaxEntries As Integer = 2000

    Private Shared ReadOnly SyncRoot As New Object()
    Private Shared ReadOnly Cache As New Dictionary(Of Integer, Entry)()


    ' ログインしている人の見た目（ログインしていなければ Nothing）
    Public Shared Function ForAccount(account As AccountStore.AccountRecord) As Look

        If account Is Nothing Then
            Return Nothing
        End If

        Dim entry As Entry = Nothing

        SyncLock SyncRoot
            If Cache.TryGetValue(account.AccountId, entry) AndAlso DateTime.UtcNow >= entry.ExpiresAt Then
                entry = Nothing
            End If
        End SyncLock

        If entry Is Nothing Then

            ' 【DB は鍵の外で読む】鍵を持ったまま読むと、DB が詰まっている間、ほかのページが全部待つ
            entry = New Entry()
            Dim lifetimeToUse As TimeSpan = Lifetime

            Try

                Dim p As AccountProfileStore.ProfileRecord = AccountProfileStore.Find(account.AccountId)

                entry.ImageName = p.AvatarImageName
                entry.Letter = p.AvatarLetter
                entry.Color = p.AvatarColor

            Catch ex As Exception

                ErrorReport.Report(ex, "AccountAvatar.ForAccount")
                lifetimeToUse = RetryAfterFailure

            End Try

            entry.ExpiresAt = DateTime.UtcNow + lifetimeToUse

            SyncLock SyncRoot

                If Cache.Count >= MaxEntries Then
                    Cache.Clear()
                End If

                Cache(account.AccountId) = entry

            End SyncLock

        End If

        Return Resolve(account.AccountId, account.UserName, account.DisplayName,
                       entry.ImageName, entry.Letter, entry.Color)

    End Function


    ' 控えを捨てる（その人のプロフィールの写真・文字・色が変わった）
    Public Shared Sub Invalidate(accountId As Integer)

        SyncLock SyncRoot
            Cache.Remove(accountId)
        End SyncLock

    End Sub


    ' 全員ぶんの控えを捨てる（DB を直接書き換えたあと。Core/CacheReset.vb）
    Public Shared Sub InvalidateAll()

        SyncLock SyncRoot
            Cache.Clear()
        End SyncLock

    End Sub

End Class
