' 断った理由を「どこに出すか」の印を付ける
'
' ============================================================
' 【決まり】理由は、入力欄の上に、操作したところのそばに出す
' ============================================================
' 決定：これを基本ルールにする（「毎回の基本」の5）。
'   ・スマホで、カーソルは欄に合ったのに、キーボードが開いて欄の下の理由が隠れた
'   ・カテゴリの画面は、理由の場所がページの上に1か所しかなく、ずっと下の「追加」で断られると上へ飛んでいた
'
' 【このクラスがやるのは、印を付けることだけ】
' 理由の文そのものは、今までどおり各画面の理由の要素（data-error-jump を付けた <p>）に書く。
' ここで付けた印を見て、画面の JavaScript（Scripts/site.js の「9」）が、理由をその場所へ移す。
'
'   MarkField(欄)   … aria-invalid="true"。理由が、その欄の**上**へ移る。カーソルもそこへ行く
'   MarkHere(もの)  … data-error-here="1"。欄ではないもの（表の行・ボタン）のすぐ上へ移る
'                      （「そのお知らせは、もうありません」のように、どの欄とも言えないが、場所は分かるとき）
'   どちらも付けない … 理由は、画面が置いた場所のまま（各画面は、理由の要素を区切りの欄より上に置いておくこと）
'
' 【JavaScript が動かなくても、理由は出る】印は「どこへ移すか」の合図でしかなく、
' 理由の文は最初から画面に書いてある。移らないだけである。
'
' 【aria-invalid は、読み上げソフトへの「この欄が悪い」の印でもある】見た目（赤い枠）も、これで付く
' （Content/Site.css）。だから、印の名前を別のもの（data-〇〇）に替えないこと。
'
' 【印は ViewState に残る】WebControl.Attributes は ViewState に入るので、
' 次のポストバックでも付いたまま返ってくる。**ポストバックのはじめに Clear を呼ぶこと**
' （呼ばないと、直したあとも赤いまま・別の区切りで断られたのに前の欄へ理由が行く）。
'
' 【Core に置いた理由】アカウント・記事・お知らせ・問い合わせの画面が、どれも使う
' （「フォルダ構成」の「2つ以上の機能から使うものは Core/」）
Public NotInheritable Class FormErrors

    Private Sub New()
    End Sub

    ' 画面の JavaScript と、同じ名前にすること（Scripts/site.js の「9」）
    Private Const InvalidAttribute As String = "aria-invalid"
    Private Const HereAttribute As String = "data-error-here"


    ' 「この欄が悪い」の印を付ける（Nothing なら何もしない）
    '
    ' 【チェックボックスは InputAttributes へ付ける】asp:CheckBox の Attributes は、
    ' 外側の <span> に出る（<input> には出ない）。span に付けても、読み上げソフトにも JavaScript にも伝わらない
    Public Shared Sub MarkField(control As WebControl)

        If control Is Nothing Then
            Return
        End If

        Dim check As CheckBox = TryCast(control, CheckBox)

        If check IsNot Nothing Then
            check.InputAttributes(InvalidAttribute) = "true"
            Return
        End If

        control.Attributes(InvalidAttribute) = "true"

    End Sub


    ' 「理由は、このすぐ上に」の印を付ける（表の行・ボタンなど、欄ではないもの）
    Public Shared Sub MarkHere(control As WebControl)

        If control Is Nothing Then
            Return
        End If

        control.Attributes(HereAttribute) = "1"

    End Sub


    ' 前の回に付けた印を消す（ポストバックのはじめに呼ぶ）
    Public Shared Sub Clear(ParamArray controls As WebControl())

        If controls Is Nothing Then
            Return
        End If

        For Each control As WebControl In controls

            If control Is Nothing Then
                Continue For
            End If

            control.Attributes.Remove(InvalidAttribute)
            control.Attributes.Remove(HereAttribute)

            Dim check As CheckBox = TryCast(control, CheckBox)

            If check IsNot Nothing Then
                check.InputAttributes.Remove(InvalidAttribute)
            End If

        Next

    End Sub

End Class
