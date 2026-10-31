' 数字の入力欄の、サーバー側の整え方（指摘：数字を全角で打つと通らなかった。決定：すべての数字の入力欄で受ける）
'
' 【決まり】数字の入力欄は全部
'   ①前後の空白（全角の空白も）を省く ②全角の数字・マイナスを半角にする（Unicode の NFKC 正規化）
'   ③数字と認識できなければ、その欄の既定の数字にする（決めるのは呼ぶ側）
'
' 【画面の側にも同じ整え方がある】Scripts/site.js の「16」（data-number-field の欄。欄から離れたときと、送る直前）。
' こちらは最後の守り（JS が動かない・開発者ツールから送られた値も、同じ形で受け取る）。**片方だけ直さないこと**
'
' 【置き場所が Core なのは】サイト全体の数字の欄の決まりだから（今の呼び元は記事とカテゴリだけ。数字の欄を足したら、ここを通す）
Public NotInheritable Class NumberInput

    Private Sub New()
    End Sub


    ' 整えた文字列（NFKC ＋ 前後の空白を省く）。Nothing は空文字
    Public Shared Function Normalize(text As String) As String

        If String.IsNullOrEmpty(text) Then
            Return ""
        End If

        ' NFKC＝「１２」→「12」、全角のマイナス（－）→「-」、全角の空白→半角の空白
        Dim s As String = text.Normalize(System.Text.NormalizationForm.FormKC)

        ' 数字の前後のマイナスの仲間（−・‐ など）も、ハイフンマイナスにそろえる
        s = s.Replace(ChrW(&H2212), "-").Replace(ChrW(&H2010), "-").Replace(ChrW(&H2011), "-")

        Return s.Trim()

    End Function


    ' 整えてから整数として読む。読めなければ False（呼ぶ側が既定の数字を使う）
    Public Shared Function TryParseInt(text As String, ByRef value As Integer) As Boolean

        Dim s As String = Normalize(text)

        If s.Length = 0 Then
            Return False
        End If

        Return Integer.TryParse(s, Globalization.NumberStyles.AllowLeadingSign,
                                Globalization.CultureInfo.InvariantCulture, value)

    End Function


    ' 画面の中の数字の欄（data-number-field を付けた asp:TextBox）を全部整える。
    ' 決まりは画面の側（Scripts/site.js の「16」）と同じ：整える → 数字でなければ data-number-default → 範囲（data-number-min／max）の端へ寄せる。
    ' 呼ぶのは Accounts/AccountPageBase.vb の OnLoad（ポストバックのとき・検査より先）
    Public Shared Sub FixFields(root As Web.UI.Control)

        If root Is Nothing Then
            Return
        End If

        Dim box As Web.UI.WebControls.TextBox = TryCast(root, Web.UI.WebControls.TextBox)

        If box IsNot Nothing AndAlso Not String.IsNullOrEmpty(box.Attributes("data-number-field")) Then
            box.Text = FixValue(box.Text, box.Attributes("data-number-default"),
                                box.Attributes("data-number-min"), box.Attributes("data-number-max"))
        End If

        If root.HasControls() Then
            For Each child As Web.UI.Control In root.Controls
                FixFields(child)
            Next
        End If

    End Sub


    Private Shared Function FixValue(raw As String, def As String, minText As String, maxText As String) As String

        Dim s As String = Normalize(raw)

        If s.Length = 0 Then
            Return ""
        End If

        ' 【Long で読む】桁の多すぎる数（12345678901 など）も「範囲の外」として端へ寄せるため（Integer だと読めずに既定へ落ちる）
        Dim value As Long

        If Not System.Text.RegularExpressions.Regex.IsMatch(s, "^-?[0-9]{1,18}$") OrElse
           Not Long.TryParse(s, Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture, value) Then
            Return If(def, "")
        End If

        Dim bound As Long

        If Long.TryParse(minText, bound) AndAlso value < bound Then
            value = bound
        End If

        If Long.TryParse(maxText, bound) AndAlso value > bound Then
            value = bound
        End If

        Return value.ToString(Globalization.CultureInfo.InvariantCulture)

    End Function

End Class
