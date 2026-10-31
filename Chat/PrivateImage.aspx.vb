Imports System.Configuration
Imports System.Data.SqlClient
Imports System.IO

' ============================================================
' こっそりの画像を、当事者にだけ返す
' ============================================================
'
'     GET /chat/private-image?n=2026/09/10/....jpg
'
' E（画像アップロード）の回で作った。
'
' 【なぜ、こっそりの画像だけ .aspx を通すのか】
' 静的ファイルはIISがそのまま返すので、URLをコピーして外の人へ渡せば、
' **部屋に居ない人でも開ける**。技術は要らない——右クリックで画像のアドレスをコピー、
' それだけで成立する。「秘密」と名乗っている以上、そこは守らなければならない
' （判断：秘密のものなので、その程度の確かめのコストは払う）。
'
' 部屋あての画像は、これまでどおり静的ファイルのまま返す。
' **アプリを1行も通らない**ので、いちばん軽い。守る必要が無いものにコストを払わない
'
' 【置き場所を分けていることが、この認可を意味あるものにしている】
' こっそりの画像は App_Data の下にある。ASP.NETがあのフォルダへの直接の要求を拒むため、
' URLを打っても取れない。**確かめる入口の横に、確かめない入口が開いていない**
'
' 【ChatPageBase は継承しない】
' あちらは Cookie が欠けていればトップページへ転送する。
' ここは <img> の src として呼ばれるので、HTMLを返しても意味が無い。
' 見せられないときは 404 を返す（下記）
Public Class PrivateImage
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' === 1. 名前の形を確かめる ===
        '
        ' 【ファイルを触る前に、必ず通すこと】
        ' 名前は利用者の言い値である。SiteImage.IsValidName が、
        ' 「..」も「\」も通さない形になっている。**ここが唯一の壁である**
        Dim imageName As String = Request.QueryString("n")

        If Not SiteImage.IsValidName(imageName) Then
            NotFound()
            Return
        End If

        ' === 2. 自分は誰か ===
        Dim browserId As String = Nothing
        Dim cookie As HttpCookie = Request.Cookies("ChatBrowserId")

        If cookie IsNot Nothing Then
            browserId = cookie.Value
        End If

        Dim me_ As Guid

        If Not Guid.TryParse(browserId, me_) Then
            NotFound()
            Return
        End If

        ' === 3. その画像の発言を引いて、自分が当事者かを見る ===
        If Not IsParticipantOf(imageName, me_) Then
            NotFound()
            Return
        End If

        ' === 4. 中身を流す ===
        Dim fullPath As String = SiteImage.MapPath(imageName, ImagePlace.PrivateTalk)

        If Not File.Exists(fullPath) Then
            NotFound()
            Return
        End If

        Response.ContentType = SiteImage.ContentTypeOf(imageName)

        ' 【このブラウザにだけ、しばらく持たせる】
        ' 窓を開き直すたびに取りに行くと、そのつどDBを1回引くことになる。
        ' private にしてあるので、途中のプロキシや共有のキャッシュには載らない
        '
        ' 【中身は書き換わらない】
        ' 名前がGUIDで、同じ名前が使い回されることは無い。
        ' だから長めに持たせても、古い絵が出続けることが起こらない
        Response.Cache.SetCacheability(HttpCacheability.Private)
        Response.Cache.SetMaxAge(TimeSpan.FromHours(1))

        ' 【インラインで見せる。ダウンロードさせない】
        ' 名前も GUID.jpg のままにしてある——元のファイル名は保存していない
        Response.AddHeader("Content-Disposition", "inline")

        ' 【ブラウザに種類を推測させない】
        ' 中身は先頭バイトで検査済みだが、名乗った種類をそのまま守らせておくほうが堅い
        Response.AddHeader("X-Content-Type-Options", "nosniff")

        Response.TransmitFile(fullPath)

        ' 【Response.End() ではなく CompleteRequest()】
        ' あちらは ThreadAbortException を投げる（KeepAlive.aspx.vb と同じ形）
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub


    ' その画像の発言の、当事者か
    '
    ' 【送り主か、宛先か。そのどちらかであること】
    ' こっそりは1対1なので、見てよいのはこの2人だけである。
    ' 管理画面から見るときは、この経路を通らない（あちらは別機能。16番）
    '
    ' 【索引で一発で引ける】
    ' ChatMessages.ImageName に、フィルター付きの索引を張ってある。
    ' 本文（MessageText）から LIKE で探す形にすると索引が使えず、
    ' 画像を1枚開くたびに、その部屋のログを舐めることになる
    ' （詳しくは Database/CreateTable/ChatMessages.sql の ImageName）
    '
    ' 【MessageType も見ること】
    ' 部屋あての画像（種別10）は、ここを通らずに静的ファイルとして配られる。
    ' 万一その名前がここへ来ても、種別で外れるので App_Data 側は探されない
    Private Shared Function IsParticipantOf(imageName As String, viewer As Guid) As Boolean

        Dim imageGuid As Guid

        If Not SiteImage.TryGetGuid(imageName, imageGuid) Then
            Return False
        End If

        Try
            Dim connStr As String =
                ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            ' 【突き合わせをSQL側でやっていること】
            ' 送り主・宛先のGUIDをアプリまで持ってこない。
            ' 持ってくると、**万一ログに出してしまったときに身元がそのまま漏れる**
            ' （ChatBrowserId は本人証明そのもので、知られればなりすませる）
            Dim sql As String =
                "SELECT TOP 1 1 FROM ChatMessages " &
                "WHERE ImageName = @ImageName " &
                "  AND MessageType = @PrivateType " &
                "  AND (SenderBrowserId = @Viewer OR RecipientBrowserId = @Viewer)"

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.AddWithValue("@ImageName", imageGuid)
                    cmd.Parameters.AddWithValue("@PrivateType",
                                                CByte(ChatMessageType.PrivateMessage))
                    cmd.Parameters.AddWithValue("@Viewer", viewer)

                    conn.Open()

                    Return cmd.ExecuteScalar() IsNot Nothing

                End Using
            End Using

        Catch ex As Exception

            ' 【フェイルクローズ。引けなかったら見せない】
            ' 部屋の存在の確認をフェイルオープンにしてあるのとは、向きが逆である。
            ' あちらは「一瞬の詰まりで誰も入室できなくなるほうが深刻」だが、
            ' こちらは**通してしまうと、当事者でない人にこっそりの画像が見える**。
            ' 見えないだけなら、あとでもう一度開けばよい
            '
            ' 黙ってはいない（それまでは Trace だけ）。
            ' 補足に画像の名前も、見ようとした人の ChatBrowserId も入れない（どちらもメールに載せたくない）
            ErrorReport.Report(ex, "PrivateImage.IsParticipantOf（こっそりの画像の認可）")

            Return False

        End Try

    End Function


    ' 見せられないときは、すべて 404 にする
    '
    ' 【403 と 404 を、わざと区別していない】
    ' 403（見る権利が無い）を返すと、**その画像が存在すること自体**が分かる。
    ' 存在しない部屋と終了した部屋を同じ文言にしてあるのと、まったく同じ考え方である。
    ' 当事者でない人から見れば、その画像は「無い」でよい
    Private Sub NotFound()

        Response.Clear()
        Response.StatusCode = 404
        Response.ContentType = "text/plain"

        ' 【本番のIISに、エラー画面へ差し替えさせない】
        '
        ' 本番のIISは、本文が空の404を**自前のHTMLのエラー画面**へ差し替える
        ' （「ファイルまたはディレクトリが見つかりません」）。開発機の IIS Express は差し替えない。
        ' そのため、同じページが**開発機では空の text/plain、本番ではHTML**を返していた。
        '
        ' 働きには影響しない（<img> から見れば、どちらも「取れなかった」だけ）。
        ' それでも揃えるのは、**開発機と本番で応答が違うと、確かめる道具のほうが狂う**から
        ' である。発行後の確かめの道具が「ページが届いていない」と
        ' 誤って報告した（実際には届いていて、動いていた）。
        '
        ' これを True にすると、IISはこちらが返した応答をそのまま通す
        Response.TrySkipIisCustomErrors = True

        ' 本文は空のまま。理由を書くと、上で消したはずの手がかりが戻る
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
