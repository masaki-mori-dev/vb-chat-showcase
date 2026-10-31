Imports System.Text

' 長い本文を「包んで」送る・受け取る（Core へ出した）
'
' ============================================================
' 【なぜ包むのか】
' ============================================================
' 本番サーバーの手前には、ホスティング会社の WAF（全員向けの粗い網）が立っている。
' テストで、記事の本文に**説明の文として**
'   「<script> タグは、ページに JavaScript を書くときに使います。」
' と書いただけで、保存が 403 で止まった。Quill はその文字を &lt;script&gt; にして送っていたが、
' WAF は戻してから見ている。**正しい記事が書けない（誤検知）**。
'
' そこで、その欄だけ、送る前にブラウザが Base64 に包み（暗号ではない。元に戻せる変換）、
' WAF から中身が見えない形で送る。サーバーはここで元に戻し、**そのあと必ず、その欄の本当の守りを通す**。
'
' 【守りの本体は、包むこととは別にある】
' WAF は、どのサイトにも同じ網をかける粗い守りで、このサイトの決まりを何ひとつ知らない。
' 包んで失うのは、その欄に対する WAF の粗い目だけで、こちらの守りは1つも外れない。
'   ・記事の本文　… 戻したあと HtmlSanitizer（Articles/ArticleHtml.vb）を通してから保存する
'   ・問い合わせの内容 … 戻したあと ContactRules で長さを見て、画面へ出すときは必ずエスケープする
'
' 【包むのは、その欄だけ】サイトのほかの場所（チャットの発言・部屋名・ログイン・タイトル・お名前・メールアドレス）は、
' 今までどおり WAF に守らせる。
'
' 【却下した案】（理由は 「記事の追加・編集機能」の章の WAF の節）
'   B 全角で書く（＜script＞）… 書き手が毎回気にする必要があり、読む人がコピーしても動かない
'   C ホスティング会社に、このURLだけ WAF を外してもらう … 共用サーバーでは個別の設定ができない見込み。
'     外したURLは、本文だけでなくタイトルも含めて丸ごと WAF の外に出る
'
' 【形】 "vbenc1:" ＋ Base64（UTF-8 のバイト列）
'   先頭の印に版の番号を入れてある（あとで包み方を替えたときに見分けられる）。
'   印が無いものは、包まれていない（JavaScript が動かなかったときの素の入力欄）として、そのまま受け取る。
'   そのときは WAF に止められうる——止められたら、JavaScript が動く環境で書き直してもらうしかない
'
' ============================================================
' 【、Articles\ArticleBodyTransport.vb から Core へ移した】
' ============================================================
' 問い合わせページ（Contacts/）でも同じ形が要るようになったため。
' **2つ以上の機能から使うものは Core/**（「フォルダ構成」の「何をどこに置くか」）。
'
' 移すときに変えたのは、次の2つだけである。
'   ・長さの上限を、引数（maxPlainLength）で受け取るようにした
'     （前は ArticleRules.BodyMaxLength を直に見ていた＝記事しか使えない形だった）
'   ・断る理由の文言の、呼ぶ人ごとに違う2語（「本文」か「内容」か／「保存していません」か「送っていません」か）を
'     引数で受け取るようにした。**組み立て方そのものは1か所のまま**
'
' 【画面側（JavaScript）は、2か所にある】
'   Scripts/article-editor.js … 記事の本文（包む・戻すの両方。書く欄への読み込みにも使う）
'   Scripts/site.js の「7」　 … data-encode-on-submit を書いた欄を、送る直前に包む（問い合わせページ）
' **どちらも先頭の印は EncodedPrefix と同じ文字列にすること。** 片方だけ変えないこと
Public NotInheritable Class BodyTransport

    Private Sub New()
    End Sub

    ' 包んだものの先頭の印。
    ' **Scripts/article-editor.js の ENCODED_PREFIX と、Scripts/site.js の「7」と、同じ文字列にすること**
    Public Const EncodedPrefix As String = "vbenc1:"

    ' 【UTF-8 として正しくないバイト列は、例外にして断る】
    ' 既定の Encoding.UTF8 は、壊れたバイトを黙って「?」の字（U+FFFD）に置き換える。
    ' それだと、包み方が壊れていても、化けた本文がそのまま保存される
    Private Shared ReadOnly StrictUtf8 As New UTF8Encoding(False, True)


    ' 戻した結果
    Public Class DecodeResult

        ' 戻せたか（包まれていなかったものも、そのまま受け取れるので True）
        Public Property Ok As Boolean

        ' 戻した本文（Ok のときだけ）
        Public Property Html As String

        ' 断った理由（Ok でないときだけ）。画面にそのまま出す
        Public Property Message As String

        ' 包まれていたか（JavaScript が動いていたか）
        Public Property WasEncoded As Boolean

    End Class


    ' 包んだものの長さの上限（印を除いた Base64 の文字数）
    '
    ' 中身の上限ぶんの文字が全部3バイトの字でも入る長さ＝上限×3バイト → Base64 で ×4/3。
    ' これを超えるものは、戻しても必ず上限を超えるので、**戻す前に断る**（大きな値を無駄に戻さない）。
    ' 上限ぎりぎりのものは戻したあと呼ぶ側が文字数で断るので、ここは余裕を持たせてある
    Public Shared Function MaxEncodedLengthFor(maxPlainLength As Integer) As Integer
        Return maxPlainLength * 4 + 16
    End Function


    ' 送られてきた欄の値を、元の文字列に戻す
    '
    ' maxPlainLength … 戻したあとの中身の、文字数の上限（記事なら ArticleRules.BodyMaxLength）
    ' itemName　　　 … 断る理由に出す、この欄の呼び名（「本文」「内容」）
    ' notDoneText　　… 断る理由に出す、やらなかったこと（「保存していません。」「送っていません。」）
    '
    ' 【戻せないときは、空で保存しない】
    ' 空として受け取ると、「入力してください」という的外れな理由で断るか、最悪は空のまま保存される。
    ' ここでは理由を返し、呼ぶ側が保存（送信）をやめる
    Public Shared Function Decode(raw As String,
                                  maxPlainLength As Integer,
                                  itemName As String,
                                  notDoneText As String) As DecodeResult

        If raw Is Nothing Then
            Return New DecodeResult With {.Ok = True, .Html = "", .WasEncoded = False}
        End If

        If Not raw.StartsWith(EncodedPrefix, StringComparison.Ordinal) Then
            Return New DecodeResult With {.Ok = True, .Html = raw, .WasEncoded = False}
        End If

        Dim payload As String = raw.Substring(EncodedPrefix.Length)

        If payload.Length > MaxEncodedLengthFor(maxPlainLength) Then
            Return Failed(itemName & "が長すぎます（" & maxPlainLength.ToString("#,0") &
                          "文字まで）。" & notDoneText)
        End If

        Try

            Dim bytes As Byte() = Convert.FromBase64String(payload)

            Return New DecodeResult With {
                .Ok = True,
                .Html = StrictUtf8.GetString(bytes),
                .WasEncoded = True}

        Catch ex As FormatException

            ' 【ErrorReport へは送らない】画面から送れば必ず戻せる形なので、ここへ来るのは
            ' 開発者ツールで書き換えたときか、通信の途中で壊れたときだけ。
            ' 送られてきた値そのものは、メールに載せない（中身が入っているため）
            Return Failed(itemName & "を読み取れませんでした（送る途中で壊れた可能性があります）。" &
                          notDoneText & "ページを開き直してから、もう一度書いてください。")

        Catch ex As ArgumentException

            ' 壊れた UTF-8（DecoderFallbackException は ArgumentException の仲間）
            Return Failed(itemName & "を読み取れませんでした（文字が壊れています）。" & notDoneText)

        End Try

    End Function


    ' 文字列を包む（画面へ渡すとき用）
    '
    ' 【なぜ画面へ渡すときにも包むのか】記事の編集の画面は、保存されている本文を textarea に
    ' 生の HTML として出している。ページの応答の途中で、その一部（style など）が書き換えられると、
    ' 書く欄に読み込む本文まで変わってしまう。同じ本文を、書く欄の箱の data-initial-body に
    ' 「包んだ形」でも出し、JS はそちらから読む（Scripts/article-editor.js の loadInitial）。
    ' 送るとき（JS → サーバー）に包むのと、同じ考え方を受け取る側にも当てた。
    '
    ' 【もう包まれているものは、そのまま返す】保存が断られて戻ってきたとき、欄の値が包んだ形のままのことがある
    Public Shared Function Encode(html As String) As String

        If String.IsNullOrEmpty(html) Then
            Return ""
        End If

        If html.StartsWith(EncodedPrefix, StringComparison.Ordinal) Then
            Return html
        End If

        Return EncodedPrefix & Convert.ToBase64String(New UTF8Encoding(False).GetBytes(html))

    End Function


    Private Shared Function Failed(message As String) As DecodeResult
        Return New DecodeResult With {.Ok = False, .Html = Nothing, .Message = message}
    End Function

End Class
