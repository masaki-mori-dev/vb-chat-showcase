' チャット関連ページ専用の、共通の土台となるクラス
' このクラスを継承したページは、ページが表示される前に
' 自動的に「名前決めてるかチェック」が実行される
Public Class ChatPageBase
    Inherits System.Web.UI.Page

    ' 「?」のボタンと小窓（文は Core/HelpTexts.vb の表の1か所）。markup からは <%= HelpButton("鍵") %> の1行だけ。
    ' 【markup から HelpTexts を直接呼ばない】別のコンパイル単位で、実行したときだけ500になりうる（ここを経由する）
    Protected Shared Function HelpButton(key As String) As String
        Return HelpTexts.ButtonHtml(key)
    End Function

    Protected Overrides Sub OnInit(ByVal e As EventArgs)

        ' ページ自体の初期化処理より先に、名前チェックを実行する
        ChatCommon.RequireChatProfile()

        ' 親クラス（System.Web.UI.Page）本来の初期化処理も、忘れずに呼ぶ
        MyBase.OnInit(e)

    End Sub

End Class
