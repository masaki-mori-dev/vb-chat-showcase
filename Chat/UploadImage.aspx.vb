Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.IO

' ============================================================
' 画像アップロードの受け口
' ============================================================
'
' 部屋の画面（Scripts/chat-room.js の ImageUpload）から、POSTで呼ばれる。
' 画像は multipart/form-data の "image" という名前のファイルとして送り、
' 寸法と宛先はヘッダーで渡す。
'
'     POST /Chat/UploadImage.aspx
'     X-Image-Width      … 画面が数えた幅
'     X-Image-Height     … 画面が数えた高さ
'     X-Image-Recipient  … こっそりの宛先の匿名ID（部屋あてなら空）
'     （本文）           … multipart/form-data。ファイルの部分の名前は "image"
'
' 返すのはJSON。
'
'     {"ok":true,"name":"2026/09/10/....jpg","width":1280,"height":960}
'     {"ok":false,"reason":"..."}
'
' 【必読】なぜ multipart なのか（生のバイト列で送ってはいけない）
'
' 最初は画像のバイト列を、本文へそのまま入れて送っていた（初版）。
' 「1回に1枚しか送らないのだから、境界の組み立てと解析が両側に増えるだけ」
' という判断だった。**本番で、それが通らなかった。**
'
' **本番のサーバーの手前に、POSTの本文を調べて弾く仕組みがいる。**
' 生のバイト列は、そこで**文字として採点され、たまたま点が高い画像が403で弾かれる**。
' 受け口のコードまで届かないので、利用者には「通信の状態をお確かめください」としか
' 出ていなかった（実機テストで、連投を断られたと思われていたのは、実はこれだった）。
'
' 実測（本番。部屋に入っていないダミーで送り、何も保存させずに測った）
'
'     送り方               40枚のうち弾かれた枚数
'     生のバイト列         4枚（10%）
'     multipart            0枚
'     base64               0枚
'
' 同じ画像は、間を空けても、Content-Type を変えても、毎回同じように弾かれた
' （回数ではなく、中身で決まっている）。開発機（IIS Express）では1枚も弾かれない。
' **本番でしか起きないので、開発機の動作確認では見つけられない。**
'
' multipart のファイルの部分は、採点の対象から外れるらしい。
' base64 も通ったが、大きさが3割増えるので採らなかった。
'
' 【ここを生のバイト列に戻さないこと】
' 開発機では何も困らないので、「multipart は大げさ」と見えて戻したくなる。
' 戻すと、本番で1割の画像が理由も分からないまま送れなくなる
'
' 【ChatPageBase は継承しない】
' あちらは Cookie が欠けていればトップページへ転送する。
' ここが返すのはJSONなので、HTMLの転送を返されても画面側は読めない。
' Cookie の検査は、下で自分で行う（KeepAlive.aspx・LeaveBeacon.aspx と同じ扱い）
'
' 【なぜ .ashx ではないのか】
' このプロジェクトの受け口は、すべて .aspx で揃えてある
' （KeepAlive・LeaveBeacon・Logout）。Session は使わないので .ashx でも足りるが、
' 形が1つ増えるほうの損が大きい。判断そのものは Core/SiteImage.vb と ChatHub が持つので、
' C#／MVC へ移るときに、このファイルは捨てられる
Public Class UploadImage
    Inherits System.Web.UI.Page

    ' 画面へ返す文言
    '
    ' 【1か所にまとめてあること】
    ' 断る理由が増えるたびに文字列が散らばると、言い回しが揃わなくなる。
    ' 画面側はこの文言をそのまま「断られた理由」の行に出すので、
    ' **利用者がそのまま読める日本語**でなければならない
    Private Const ReasonNotInRoom As String =
        "部屋に入っていない状態になりました。お手数ですが、入り直してください。"

    Private Const ReasonNotAllowed As String = "この部屋では、画像を送れません。"

    Private Const ReasonBadFormat As String =
        "この形式の画像は送れません。対応しているのは " & SiteImage.AcceptedFormatsText & " です。"

    Private Const ReasonSaveFailed As String =
        "サーバーに保存できませんでした。しばらくしてから、もう一度お試しください。"


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 返すのはJSONで、キャッシュされては困る
        Response.ContentType = "application/json"
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに書き写さないこと＝毎回の基本の8）
        ' までは、ここだけ2行（期限切れの日付を付ける1行が無い形）だった。窓口にそろえた
        PageResponse.SetNoStore(Response)

        ' 【POST以外は受け付けない】
        ' GETで叩ける口にしておくと、他所のページからリンク1つで呼べてしまう
        If Not String.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) Then
            WriteResult(False, Nothing, 0, 0, Nothing)
            Return
        End If

        Dim reason As String = Nothing
        Dim savedName As String = Nothing
        Dim width As Integer = 0
        Dim height As Integer = 0

        Dim ok As Boolean = TryHandle(savedName, width, height, reason)

        WriteResult(ok, savedName, width, height, reason)

    End Sub


    ' 受け付けから配信までを、順に行う
    '
    ' 【順番に意味がある】
    '   1. 身元（Cookie）→ 2. 中身の検査 → 3. 部屋の可否 → 4. 保存 → 5. 発言として流す
    '
    ' **安いものから見る**（入室の判定で、DBを引く部屋の確認を最後にしてあるのと同じ）。
    ' 5の中で連投などに断られたら、4で置いたファイルを消す
    Private Function TryHandle(ByRef savedName As String,
                               ByRef width As Integer,
                               ByRef height As Integer,
                               ByRef reason As String) As Boolean

        ' === 1. 身元 ===
        '
        ' 【引数では受け取らない。Cookieから自分で読む】
        ' この値は「誰の発言として流すか」を決める。引数で受け取ると、
        ' 他人になりすまして発言できることになる
        ' （ChatHub.JoinRoom が browserId を引数から外したのと、まったく同じ理由）
        Dim browserId As String = Nothing
        Dim cookie As HttpCookie = Request.Cookies("ChatBrowserId")

        If cookie IsNot Nothing Then
            browserId = cookie.Value
        End If

        If Not ChatProfileValidator.IsValidBrowserId(browserId) Then
            reason = ReasonNotInRoom
            Return False
        End If

        ' 今どの部屋に居るか。居なければ、ここで終わり
        '
        ' 【Session ではなく、ハブに聞くこと】
        ' Sessionは20分で消え、アプリが寝ても消える。在室しているかどうかを
        ' 本当に知っているのはハブの参加者リストである
        ' （GetLogEntries が Session をやめたのと、まったく同じ話）
        Dim roomId As Integer = 0

        If Not ChatHub.TryGetRoomIdByBrowserId(browserId, roomId) Then
            reason = ReasonNotInRoom
            Return False
        End If

        ' === 2. 中身 ===
        Dim bytes As Byte() = ReadBody(reason)

        If bytes Is Nothing Then
            Return False
        End If

        ' 拡張子は、中身の先頭バイトだけで決める（名乗られた種類は見ない）
        Dim extension As String = SiteImage.DetectExtension(bytes)

        If extension Is Nothing Then
            reason = ReasonBadFormat
            Return False
        End If

        ' === 撮った場所（GPS）などの情報を取り除く（Core/ImageMetadata.vb） ===
        '
        ' 【部屋あても、こっそりも】画面は canvas で描き直してから送るので、ふつうはもう入っていない。
        ' それでも受け口は開発者ツールから直接叩けるので、元の写真がそのまま届きうる（記事の画像の受け口と同じ守り）。
        ' 【区切りの形が読めなければ、受け付けない】途中までで保存しない
        Dim stripped As ImageMetadata.StripResult = ImageMetadata.Strip(bytes, extension)

        If stripped.Bytes Is Nothing Then
            reason = ReasonBadFormat
            Return False
        End If

        bytes = stripped.Bytes

        ' === 3. この部屋で、画像を送ってよいか ===
        '
        ' 【画面がタブを出さないことは、防御ではない】
        ' この受け口は開発者ツールから直接叩ける。
        ' 「クライアント側の検証は防御ではなく親切」という、この画面の決まりどおり
        '
        ' 【引けなかったときは、断る（フェイルクローズ）】
        ' 部屋の存在の確認をフェイルオープンにしてあるのとは、向きが逆である。
        ' あちらは「一瞬の詰まりで誰も入室できなくなるほうが深刻」だが、
        ' こちらは**通してしまうと、禁止した部屋へ画像が流れる**。
        ' 失敗しても、利用者は少しあとでもう一度押せばよい
        If Not IsImageAllowed(roomId) Then
            reason = ReasonNotAllowed
            Return False
        End If

        ' === 4. 保存 ===
        Dim recipientAnonId As String = ReadRecipient()
        Dim isPrivate As Boolean = (recipientAnonId IsNot Nothing)

        width = SiteImage.ClampEdge(ReadIntHeader("X-Image-Width"))
        height = SiteImage.ClampEdge(ReadIntHeader("X-Image-Height"))

        Dim imageName As String = SiteImage.NewName(extension)
        Dim saveError As String = Nothing

        If Not SiteImage.TrySave(bytes, imageName, PlaceOf(isPrivate), saveError) Then
            reason = ReasonSaveFailed
            Return False
        End If

        ' === 5. 発言として流す ===
        '
        ' 判定（連投・直前と同じ・文字数）も、配信も、記録も、
        ' 文章の発言とまったく同じ関所（ChatHub.AcceptAndDeliver）が行う
        Dim result As SendMessageResult =
            ChatHub.SendImage(browserId, imageName, width, height, recipientAnonId)

        If result Is Nothing OrElse Not result.Accepted Then

            ' === 断られたら、置いたファイルを消す ===
            '
            ' 【なぜ「置いてから断られる」形にしてあるのか】
            ' 逆にすると、判定と保存のあいだに割り込まれる隙ができる。
            ' 先に置いておけば、配信に至った時点でファイルは必ず在る。
            '
            ' 断られるのは連投のときだけなので、この掃除はめったに走らない。
            ' 消し損ねても実害は無い（保存期間が過ぎれば、日付のフォルダごと消える）
            SiteImage.DeleteQuietly(imageName, PlaceOf(isPrivate))

            reason = If(result Is Nothing, Nothing, result.RejectReason)
            Return False

        End If

        savedName = imageName
        Return True

    End Function


    ' こっそりか・部屋か、から置き場所へ（SiteImage は、置き場所を列挙型で受け取る）
    Private Shared Function PlaceOf(isPrivate As Boolean) As ImagePlace
        Return If(isPrivate, ImagePlace.PrivateTalk, ImagePlace.Room)
    End Function


    ' multipart の境界や見出しのぶん、本文全体はファイルより少し大きくなる。
    ' 門前払いの線を、そのぶんだけ緩めておく（ファイルそのものは、下で上限どおりに測る）
    Private Const MultipartSlackBytes As Integer = 16 * 1024


    ' 送られてきた画像（multipart の "image"）を読む
    '
    ' 読めなければ Nothing を返し、理由を入れる
    Private Function ReadBody(ByRef reason As String) As Byte()

        Dim maxBytes As Integer = SiteSettings.ImageMaxBytes

        ' 【まず、名乗られた長さで門前払いする】
        ' Request.Files に触ると、本文がまるごと解析される。
        ' 大きすぎるものは、その手前で安く断る。
        ' Content-Length は言い値だが、**嘘をついても下の読み取りで止まる**ので、
        ' ここは「素直な相手を安く断る」ためのものである
        If Request.ContentLength > maxBytes + MultipartSlackBytes Then
            reason = TooLargeText(maxBytes)
            Return Nothing
        End If

        Try
            Dim file As HttpPostedFile = Request.Files("image")

            If file Is Nothing OrElse file.ContentLength = 0 Then
                reason = Nothing
                Return Nothing
            End If

            If file.ContentLength > maxBytes Then
                reason = TooLargeText(maxBytes)
                Return Nothing
            End If

            Using buffer As New MemoryStream()

                Dim chunk(8191) As Byte
                Dim read As Integer

                Do
                    read = file.InputStream.Read(chunk, 0, chunk.Length)

                    If read <= 0 Then
                        Exit Do
                    End If

                    ' 【読みながら測ること】
                    ' Content-Length を信じて読み切ってしまうと、
                    ' 「短いと名乗って長く送る」形で上限を超えられる
                    If buffer.Length + read > maxBytes Then
                        reason = TooLargeText(maxBytes)
                        Return Nothing
                    End If

                    buffer.Write(chunk, 0, read)
                Loop

                If buffer.Length = 0 Then
                    reason = Nothing
                    Return Nothing
                End If

                Return buffer.ToArray()

            End Using

        Catch ex As Exception

            ' 黙ってはいない（それまでは Trace だけ）。
            ' 送っている途中で相手が接続を切っただけのもの（HttpException の決まった番号）は、
            ' ErrorReport が自分で捨てる
            ErrorReport.Report(ex, "UploadImage.ReadBody（画像の受け取り）")

            reason = ReasonSaveFailed
            Return Nothing

        End Try

    End Function


    ' 大きすぎるときの文言
    '
    ' 【上限の数字を、べた書きしないこと】
    ' 決めているのは SiteSettings（＝DBの ImageMaxBytes）である。
    ' 画面側にも同じ文言があるが、あちらも設定から組み立てている
    Private Shared Function TooLargeText(maxBytes As Integer) As String

        Dim mb As Double = maxBytes / 1048576.0

        Return "画像が大きすぎます（" & mb.ToString("0.#", CultureInfo.InvariantCulture) &
               "MBまで）。"

    End Function


    ' こっそりの宛先。ヘッダーが空なら Nothing（＝部屋あて）
    '
    ' 【Nothing と 空文字を、はっきり分けること】
    ' ChatHub 側は「Nothing なら部屋あて」で判断している。
    ' 空文字を渡すと**こっそり扱いになって、宛先が見つからず断られる**
    Private Function ReadRecipient() As String

        Dim value As String = Request.Headers("X-Image-Recipient")

        If String.IsNullOrEmpty(value) Then
            Return Nothing
        End If

        Return value

    End Function


    ' ヘッダーの数値を読む（読めなければ0）
    Private Function ReadIntHeader(name As String) As Integer

        Dim value As Integer

        If Integer.TryParse(Request.Headers(name), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, value) Then
            Return value
        End If

        Return 0

    End Function


    ' この部屋で、画像を送ってよいか（ChatRooms.IsImageUploadEnabled）
    '
    ' 【問い合わせに失敗したら False（＝断る）】
    ' 上の「フェイルクローズ」の理由を参照
    Private Shared Function IsImageAllowed(roomId As Integer) As Boolean

        Try
            Dim connStr As String =
                ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            ' IsActive も一緒に見る。終了した部屋へ画像だけ流れ込むことがないように
            Dim sql As String =
                "SELECT IsImageUploadEnabled FROM ChatRooms " &
                "WHERE RoomId = @RoomId AND IsActive = 1"

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.AddWithValue("@RoomId", roomId)

                    conn.Open()

                    Dim value As Object = cmd.ExecuteScalar()

                    If value Is Nothing OrElse Convert.IsDBNull(value) Then
                        Return False
                    End If

                    Return CBool(value)

                End Using
            End Using

        Catch ex As Exception

            ' 断る（フェイルクローズ）ことは変えないが、黙っては断らない
            ErrorReport.Report(ex, "UploadImage.IsImageAllowed", "部屋 " & roomId)

            Return False

        End Try

    End Function


    ' 結果のJSONを書く
    '
    ' 【文字列は必ずエスケープすること】
    ' 理由の文言は今のところ定数だけだが、SendMessageResult.RejectReason は
    ' ChatHub が組み立てた文字列である。自前で組み立てず、
    ' JavaScriptSerializer に任せる
    ' （SiteSettings.BuildClientJson が数値だけを扱っているのと、事情が違う）
    Private Sub WriteResult(ok As Boolean,
                            imageName As String,
                            width As Integer,
                            height As Integer,
                            reason As String)

        Dim payload As New Dictionary(Of String, Object)

        payload("ok") = ok

        If ok Then
            payload("name") = imageName
            payload("width") = width
            payload("height") = height
        ElseIf reason IsNot Nothing Then
            payload("reason") = reason
        End If

        Dim serializer As New Script.Serialization.JavaScriptSerializer()

        Response.Write(serializer.Serialize(payload))

        ' 【Response.End() ではなく CompleteRequest()】
        ' あちらは ThreadAbortException を投げる。ここは応答を出しきってから
        ' 打ち切りたいだけなので、例外を使う必要が無い
        ' （KeepAlive.aspx.vb と、まったく同じ形）
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
