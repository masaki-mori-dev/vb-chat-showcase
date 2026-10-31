Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 本番から「メールが本当に届くか」を確かめるための受け口
'
' 【ふだんは閉じている（404）】
' 開くのは、App_Data\mail-test.txt が置かれていて、その中身（合言葉）と
' クエリ文字列の key が一致するときだけ。ファイルが無ければ、何を送っても404を返す。
'   ・App_Data は、ASP.NET が外からの直接の要求を拒む場所（ファイルそのものは読めない）
'   ・このファイルは .vbproj に登録しない＝発行されない。確かめるときだけ FTP で置き、
'     終わったら FTP で消す（.gitignore にも入れてある）
'
' 【なぜ、確かめたあと消さずに残すのか】
' 閉じている間は、ほかの存在しないページとまったく同じ404を返すだけで、何もしない。
' SMTP のパスワードを変えた日・サーバーを移した日・2世代目へ移る日に、また同じ確認が要る。
' そのたびに作り直すより、閉じたまま置いておくほうが安い
' （OutboundProbe.aspx は「誰でも叩ける口」だったので、その日に消した。
'   こちらはファイルを置かない限り開かないので、事情が違う）。
' 閉じていることは発行後の確かめの道具が毎回確かめる
'
' 【できること（mode=）】
'   status（既定） … mail.config が読めているか・送った／送れなかった記録・エラーの数え方を、文字で出す
'   mail          … テストのメールを1通、SiteMail の経路で送る（件名 [vb-chat テスト]）
'   throw         … わざと未処理のエラーを起こす。Application_Error → ErrorReport → SiteMail の、
'                   本物のエラーとまったく同じ経路で1通届く（同じエラーは1時間に1通。2度目は続報になる）
'
' 【利用者には関係が無い】
' 誰のCookieも見ず、DBにも何も書かない。throw で出るのは、叩いた本人の画面のエラー画面だけ。
' ChatPageBase は継承しない（名前のCookieが無くても使えるように）
Public Class MailTest
    Inherits System.Web.UI.Page

    ' 合言葉のファイル
    Private Const GateRelativePath As String = "~/App_Data/mail-test.txt"


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' キャッシュさせない（叩くたびに、今の状態を見たい）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに書き写さないこと＝毎回の基本の8）
        ' までは、ここだけ2行（期限切れの日付を付ける1行が無い形）だった。窓口にそろえた
        PageResponse.SetNoStore(Response)

        If Not IsGateOpen(Request.QueryString("key")) Then
            NotFound()
            Return
        End If

        Dim mode As String = If(Request.QueryString("mode"), "status").Trim().ToLowerInvariant()

        Select Case mode

            Case "throw"

                ' 【Try/Catch で包まないこと】包むと Application_Error を通らず、確かめたい経路にならない
                Throw New InvalidOperationException(
                    "メール送信の確認のため、わざと起こしたエラーです（Admin/MailTest.aspx の mode=throw）。")

            Case "mail"

                Dim body As New StringBuilder()
                body.AppendLine("vb-chat から、メール送信の確認のために送ったテストのメールです。")
                body.AppendLine("これが届いていれば、本番の mail.config（ホスト・ポート・認証・暗号化）は正しく働いています。")
                body.AppendLine()
                body.AppendLine("送った時刻　：" & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture))
                body.AppendLine("ビルドの目印：" & common.BuildId())
                body.AppendLine("サーバー　　：" & Environment.MachineName)

                SiteMail.Enqueue("[vb-chat テスト] メール送信の確認", body.ToString())

                WriteText("テストのメールを、送る順番に並べました。" & vbCrLf &
                          "数秒後に mode=status で「送れたメール」「送れなかったメール」を見てください。" & vbCrLf &
                          "（送るのは裏の作業なので、このページが出た時点では、まだ送り終わっていないことがあります）")

            Case Else

                Dim sb As New StringBuilder()
                sb.AppendLine("=== vb-chat のメール送信の状態 ===")
                sb.AppendLine("今の時刻：" & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture))
                sb.AppendLine("ビルドの目印：" & common.BuildId())
                sb.AppendLine()
                sb.AppendLine("--- 設定（SiteSettings） ---")
                sb.AppendLine("メール送信（MailEnabled）：" & If(SiteSettings.MailEnabled, "オン", "オフ（止めています）"))
                sb.AppendLine("届け先（MailTo）：" & SiteSettings.MailTo)
                sb.AppendLine("同じエラーを送らない時間：" & CInt(SiteSettings.ErrorMailQuietPeriod.TotalMinutes) & " 分")
                sb.AppendLine("エラーのメールの上限：1時間に " & SiteSettings.ErrorMailMaxPerHour & " 通")
                sb.AppendLine()
                sb.AppendLine("--- mail.config ---")
                sb.Append(SiteMail.DescribeConfig())
                sb.AppendLine()
                sb.AppendLine("--- 送信の記録 ---")
                sb.Append(SiteMail.DescribeStatus())
                sb.AppendLine()
                sb.AppendLine("--- エラーの数え方 ---")
                sb.Append(ErrorReport.DescribeStatus())

                WriteText(sb.ToString())

        End Select

    End Sub


    ' 合言葉が合っているか（判定は Core/GateFile.vb の1か所。まとめた）
    Private Shared Function IsGateOpen(givenKey As String) As Boolean
        Return GateFile.IsOpen(GateRelativePath, givenKey)
    End Function


    ' 文字を返して、応答を打ち切る
    Private Sub WriteText(text As String)

        Response.Clear()
        Response.ContentType = "text/plain"
        Response.ContentEncoding = Encoding.UTF8
        Response.Write(text)
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub


    ' 閉じているときの404（Chat/PrivateImage.aspx の NotFound と同じ形）
    '
    ' 【本番のIISに、エラー画面へ差し替えさせない】
    ' 差し替えられると、開発機と本番で応答が変わり、発行後の確かめの道具が
    ' 「このページが届いているか」を見分けられなくなる（IIS が 404 の本文を差し替える落とし穴）
    Private Sub NotFound()

        Response.Clear()
        Response.StatusCode = 404
        Response.ContentType = "text/plain"
        Response.TrySkipIisCustomErrors = True
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
