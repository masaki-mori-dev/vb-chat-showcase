Imports System.Data.SqlClient
Imports System.Configuration
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions

' アイコンのスプライト（Content/icon-sprite.svg）を、IconMaster から作り直す実行専用ツール。
'
' 【サイト管理者だけが使える】（塞いだ）
' それまでは**誰でも開けて、誰でも「生成する」を押せた**。
' 中身は DB を読んで Content/icon-sprite.svg を書くだけなので実害は小さかったが、
' 認証の無い「サーバーにファイルを書かせる口」を開けたままにしておく理由が無い。
' アカウントができたので、AccountPageBase に乗せた。
'
' 【ログインの画面から、ここへは戻ってこない】
' AccountReturnUrl.Normalize は「/account の下」しか戻り先に許さない（オープンリダイレクト避け）。
' このページは /Chat/ にあるので、ログインしたあとは /account に着く。
' そのあと、もう一度このURLを開き直すこと（管理人しか使わない道具なので、これでよい）
Public Class GenerateIconSprite
    Inherits AccountPageBase

    ' IconMaster.ViewBox が読めなかった・形がおかしかったときの落とし先
    '
    ' この値は、ViewBox列を足す前に全アイコンへベタ書きしていたものと同じ。
    ' つまり列が空でも、これまでとまったく同じスプライトが出来上がる
    Private Const FallbackViewBox As String = "0 0 512 512"

    ' viewBox として通す形（数値4つを空白で区切ったもの）
    '
    ' 【なぜ検査するのか】
    ' スプライトは1枚の共有ファイルである。1行でも変な値が入ると、
    ' その symbol の属性が壊れてSVG全体が読めなくなり、
    ' **全アイコンが画面から消える**（しかもエラーは出ない）。
    ' 形が合わないものは既定へ落として、残りは今までどおり出す
    Private Shared ReadOnly ViewBoxPattern As New Regex(
        "^-?\d+(\.\d+)?(\s+-?\d+(\.\d+)?){3}$", RegexOptions.Compiled)

    ' ログインの確認は AccountPageBase がやる。ここでは「サイト管理者かどうか」だけを見る
    '
    ' 【ロールの名前を、コードにべた書きしている】
    ' 画面に出す日本語の名前は DB（Roles.DisplayName）から引く決まりだが、
    ' 「誰に許すか」は**動いてよい値ではない**ので、定数（RoleKeys.Administrator）で持つ。
    ' ロールごとの上限（RoleLimits）がフェイルクローズなのと、同じ考え方
    Protected Overrides Sub OnInit(ByVal e As EventArgs)

        MyBase.OnInit(e)

        Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

        If account Is Nothing OrElse
           Not String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal) Then

            ' ログインはしているが、サイト管理者ではない。
            ' この Redirect は既定（endResponse = True）で呼ぶ。Try/Catch の中に置かないこと
            Response.Redirect(AccountPageBase.HomeUrl)

        End If

    End Sub


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

    End Sub

    Protected Sub btnGenerate_Click(ByVal sender As Object, ByVal e As System.EventArgs)

        ' ①IconMasterテーブルから、全アイコンを取得する
        Dim iconTable As New DataTable()

        Using conn As New SqlConnection(ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString)

            ' Label（日本語の名前）は、画面の読み上げ用の名前に使う。
            ' あとから足した列なので、**古い形の DB では先に Label の列を足すこと**
            ' （流していないDBでは、この SELECT が「列名 'Label' が無効です」で止まる）
            Dim sql As String = "SELECT Name, SvgData, ViewBox, Label FROM IconMaster WHERE IsActive = 1 ORDER BY DisplayOrder"

            Using cmd As New SqlCommand(sql, conn)
                Using adapter As New SqlDataAdapter(cmd)
                    adapter.Fill(iconTable)
                End Using
            End Using

        End Using

        ' ②取得したデータをもとに、SVGスプライトのHTML文字列を組み立てる
        Dim sb As New StringBuilder()

        sb.AppendLine("<svg style=""display: none;"">")

        ' viewBox の形がおかしくて既定へ落としたアイコンの名前
        ' （そのまま黙って直すと誰も気づけないので、最後に画面へ出す）
        Dim fallenBack As New List(Of String)

        For Each row As DataRow In iconTable.Rows

            Dim iconName As String = row("Name").ToString()
            Dim svgData As String = row("SvgData").ToString()

            ' このアイコンが、何マスの方眼紙に描かれているか
            '
            ' 【ここをベタ書きに戻さないこと】
            ' viewBox はアイコンの素材ごとに違う（Twemoji=36 / Noto=128 /
            ' Bootstrap Icons=16）。36マスの絵を512マスの紙に貼ると、
            ' 左上に豆粒で表示される。今は自分たちで512に揃えた素材しか無いので
            ' たまたま動くが、別のセットから1つ足した日に黙って壊れる
            '
            ' 【外側の <svg> の viewBox とは、一致していなくてよい】
            ' ChatRoom.aspx / Lounge.aspx / Default.aspx.vb / Scripts/chat-room.js は
            ' <use> を <svg viewBox="0 0 512 512"> で囲んでいるが、そちらは触らない。
            ' <use> は <symbol> 自身の viewBox で中身を伸縮させるため、
            ' 外側は「正方形の座標系」でありさえすればよい
            ' （Chromeで実測。symbol側が36・128・16でも、
            '   80pxの枠いっぱいに描画された）
            Dim viewBox As String = NormalizeViewBox(row("ViewBox").ToString())

            If viewBox <> row("ViewBox").ToString().Trim() Then
                fallenBack.Add(iconName)
            End If

            ' 元のSVGコードから、<svg ...> タグの部分だけを <symbol> に置き換える
            ' （中身のpath等はそのまま流用する）
            '
            ' 【SvgData の中にも viewBox は入っている】
            ' <svg ...> タグごと捨てているので、そちらは使っていない。
            ' 正は IconMaster.ViewBox 列のほうとする
            ' （SVGによっては viewBox を持たず width/height しか無いものがあり、
            '   SvgData から機械的に取り出す形にはできないため）
            Dim innerContent As String = ExtractSvgInnerContent(svgData)

            ' 日本語の名前を、属性として持たせる
            '
            ' 【なぜ <title> ではないのか】
            ' <symbol> の中に <title>キツネ</title> を置くと、<use> で描いたアイコンに
            ' マウスを乗せたときに、ブラウザが名前のポップアップ（ツールチップ）を出す。
            ' 部屋の発言の行・参加者一覧・ラウンジの顔、**全部に出てしまう**。
            ' data-label なら描画に一切影響せず、IconCatalog が正規表現でそのまま読める
            ' （読み上げソフトへは、ボタンの aria-label で渡すので <title> である必要が無い）
            Dim label As String = ""
            If Not IsDBNull(row("Label")) Then
                label = row("Label").ToString().Trim()
            End If

            Dim openTag As String = "  <symbol id=""icon-" & iconName & """ viewBox=""" & viewBox & """"
            If label <> "" Then
                openTag &= " data-label=""" & EscapeAttribute(label) & """"
            End If
            openTag &= ">"

            sb.AppendLine(openTag)
            sb.AppendLine(innerContent)
            sb.AppendLine("  </symbol>")

        Next

        sb.AppendLine("</svg>")

        ' ③Content/icon-sprite.svgとして、ファイルに書き出す
        Dim savePath As String = Server.MapPath("~/Content/icon-sprite.svg")
        File.WriteAllText(savePath, sb.ToString())

        ' 入室時のアイコン種別の検査（ChatHub.JoinRoom）は、このファイルの中身を
        ' 照合元にしてキャッシュしている。作り直したので、そのキャッシュを捨てておく
        ' （最終更新時刻の比較でも自動的に読み直されるが、確実に反映させるため明示的に呼ぶ）
        IconCatalog.Clear()

        ' ④結果を画面に表示する
        lblResult.Text = iconTable.Rows.Count & "件のアイコンを、icon-sprite.svgに書き出しました。"

        ' 既定へ落としたものがあれば、それも知らせる。
        ' 落としたこと自体はフェイルオープン（残りは今までどおり出る）だが、
        ' 黙って直すと、値が間違ったまま気づかれないため
        If fallenBack.Count > 0 Then
            lblResult.Text &= "<br />ただし " & fallenBack.Count &
                "件は、IconMaster.ViewBox の形がおかしかったため " &
                FallbackViewBox & " で書き出しました（" &
                Server.HtmlEncode(String.Join("、", fallenBack)) & "）。"
        End If

    End Sub


    ' IconMaster.ViewBox の値を、そのまま属性に書いてよい形に整える
    '
    ' 数値4つでなければ既定（0 0 512 512）へ落とす。
    ' 「変な値が入っていたら、そのアイコンだけ位置がずれる」で済ませ、
    ' 「スプライト全体が壊れて、全アイコンが消える」にはしないため
    Private Function NormalizeViewBox(value As String) As String

        If String.IsNullOrEmpty(value) Then
            Return FallbackViewBox
        End If

        Dim trimmed As String = value.Trim()

        If ViewBoxPattern.IsMatch(trimmed) Then
            Return trimmed
        End If

        Return FallbackViewBox

    End Function

    ' 属性の値として書いてよい形にする
    '
    ' 【HttpUtility.HtmlEncode を使わない理由】
    ' あちらは .NET 4.0 から「'」も &#39; に変える。
    ' 素材からスプライトを作る別の道具が、同じスプライトを1バイトも違わずに作れるよう、
    ' **こちらで変える文字をはっきり決めておく**（&・<・>・" の4つだけ。& を必ず先に）。
    ' 片方だけ変えないこと
    Private Function EscapeAttribute(value As String) As String

        If String.IsNullOrEmpty(value) Then
            Return ""
        End If

        Return value.Replace("&", "&amp;").
                     Replace("<", "&lt;").
                     Replace(">", "&gt;").
                     Replace("""", "&quot;")

    End Function

    ' SVGコードの中から、<svg ...>タグを取り除いて、中身（<path>など）だけを取り出す
    Private Function ExtractSvgInnerContent(svgData As String) As String

        Dim startIndex As Integer = svgData.IndexOf(">") + 1
        Dim endIndex As Integer = svgData.LastIndexOf("</svg>")

        If startIndex > 0 AndAlso endIndex > startIndex Then
            Return svgData.Substring(startIndex, endIndex - startIndex)
        End If

        Return ""

    End Function

End Class