Imports System.Globalization
Imports System.IO

' ============================================================
' 記事の画像の受け口
' ============================================================
'
' 記事を書く画面（Scripts/article-editor.js の「記事の画像」）から、POST で呼ばれる。
' **形は部屋の画像の受け口（Chat/UploadImage.aspx）とそろえてある**——
'
'     POST /Articles/UploadImage.aspx
'     X-Image-Width   … 画面が数えた幅
'     X-Image-Height  … 画面が数えた高さ
'     （本文）        … multipart/form-data。ファイルの部分の名前は "image"
'
'     {"ok":true,"src":"/Uploads/articles/2/….jpg","width":1280,"height":960}
'     {"ok":false,"reason":"..."}
'
' 【multipart にしてある。生のバイト列で送らないこと】本番の手前の検査（WAF）が、生のバイト列を1割ほど 403 で弾く
' （部屋の画像で踏んだ。理由と実測は Chat/UploadImage.aspx.vb の先頭）。
'
' 【身元はログインの札】（AccountSession.Current）。AccountPageBase は継承しない——あちらはログインの画面へ転送するが、
' ここが返すのは JSON なので、転送の HTML を返されても画面は読めない（部屋の受け口が ChatPageBase を継承しないのと同じ）。
'
' 【よそのサイトから呼ばせない】ログインの Cookie は SameSite=Lax なので、よそのサイトからの POST には付かない
' （＝ログインしていない扱いで断られる）。
'
' 【手順】身元 → 書けるロールか → 中身（先頭バイト）→ 撮影情報を取り除く → 大きさ（1枚・合計）→ 保存 → 返す。
' **安いものから見る**（部屋の受け口と同じ）。画像は記事の行と結びつけず、アカウントの番号のフォルダへ置くだけ
' （本文に貼られて保存されるまで、どの記事の画像かは決まらない。使われないまま残った画像は、記事を削除したときに片付く）
Public Class ArticleUploadImage
    Inherits System.Web.UI.Page

    ' multipart の境界や見出しのぶん（部屋の受け口と同じ）
    Private Const MultipartSlackBytes As Integer = 16 * 1024

    Private Const ReasonNotLoggedIn As String =
        "ログインが切れています。もう一度ログインしてから、貼り直してください。"

    Private Const ReasonCannotWrite As String = "このアカウントでは、記事を書けません。"

    Private Const ReasonBadFormat As String =
        "この形式の画像は貼れません。対応しているのは " & SiteImage.AcceptedFormatsText & " です。"

    Private Const ReasonBrokenImage As String =
        "この画像は読み取れませんでした（ファイルが壊れているかもしれません）。別の画像でお試しください。"

    Private Const ReasonSaveFailed As String =
        "サーバーに保存できませんでした。しばらくしてから、もう一度お試しください。"


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        Response.ContentType = "application/json"
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに書き写さないこと＝毎回の基本の8）
        ' までは、ここだけ2行（期限切れの日付を付ける1行が無い形）だった。窓口にそろえた
        PageResponse.SetNoStore(Response)

        ' 【POST 以外は受け付けない】（GET で叩ける口にすると、よそのページのリンク1つで呼べる）
        If Not String.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) Then
            WriteResult(False, Nothing, 0, 0, Nothing)
            Return
        End If

        Dim reason As String = Nothing
        Dim src As String = Nothing
        Dim width As Integer = 0
        Dim height As Integer = 0

        Dim ok As Boolean

        Try
            ok = TryHandle(src, width, height, reason)
        Catch ex As Exception

            ErrorReport.Report(ex, "ArticleUploadImage.TryHandle")

            ok = False
            reason = ReasonSaveFailed

        End Try

        WriteResult(ok, src, width, height, reason)

    End Sub


    Private Function TryHandle(ByRef src As String,
                               ByRef width As Integer,
                               ByRef height As Integer,
                               ByRef reason As String) As Boolean

        ' === 1. 身元 ===
        Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

        If account Is Nothing Then
            reason = ReasonNotLoggedIn
            Return False
        End If

        ' === 2. 書けるロールか（読めなければ書かせない＝フェイルクローズ。RoleLimits の決まり） ===
        If Not RoleLimits.GetBoolean(account.RoleKey, RoleLimits.KeyCanWriteArticles) Then
            reason = ReasonCannotWrite
            Return False
        End If

        ' === 3. 中身 ===
        Dim maxBytes As Integer = MaxImageBytes(account.RoleKey)

        If maxBytes <= 0 Then
            reason = ReasonCannotWrite
            Return False
        End If

        Dim bytes As Byte() = ReadBody(maxBytes, reason)

        If bytes Is Nothing Then
            Return False
        End If

        ' 拡張子は、中身の先頭バイトだけで決める（名乗られた種類・名前は見ない）
        Dim extension As String = SiteImage.DetectExtension(bytes)

        If extension Is Nothing Then
            reason = ReasonBadFormat
            Return False
        End If

        ' === 4. 撮った場所（GPS）などの情報を取り除く（Core/ImageMetadata.vb） ===
        ' 【区切りの形が読めなければ、受け付けない】途中までで保存しない
        Dim stripped As ImageMetadata.StripResult = ImageMetadata.Strip(bytes, extension)

        If stripped.Bytes Is Nothing Then
            reason = ReasonBrokenImage
            Return False
        End If

        bytes = stripped.Bytes

        ' === 5. アカウント全体の容量（RoleLimits の ArticleStorageMaxBytes。無制限なら -1） ===
        Dim used As Long = ArticleImages.UsedBytes(account.AccountId)

        If Not RoleLimits.IsWithin(account.RoleKey, RoleLimits.KeyArticleStorageMaxBytes, used + bytes.LongLength) Then

            Dim limit As Integer = RoleLimits.GetInt(account.RoleKey, RoleLimits.KeyArticleStorageMaxBytes)

            reason = "画像の置き場所の上限（" & MbText(limit) & "）に達しています。" &
                     "要らない画像を貼った記事を削除してから、もう一度お試しください。"
            Return False

        End If

        ' === 6. 保存 ===
        width = SiteImage.ClampEdge(ReadIntHeader("X-Image-Width"))
        height = SiteImage.ClampEdge(ReadIntHeader("X-Image-Height"))

        Dim imageName As String = SiteImage.NewArticleName(account.AccountId, extension)
        Dim saveError As String = Nothing

        If Not SiteImage.TrySave(bytes, imageName, ImagePlace.Article, saveError) Then
            reason = ReasonSaveFailed
            Return False
        End If

        ' === 7. 台帳へ1行 ===
        '
        ' 【ファイルを保存してから書く】逆にすると、保存に失敗したときに
        ' 「行は在るのにファイルが無い」が残る（合計に数えられ、片付けが消しにいって空振りする）。
        '
        ' 【書けなければ、ファイルを消して断る】台帳に無い画像は、
        '   ・そのアカウントの合計（ArticleStorageMaxBytes）に数えられない
        '   ・1日1回の片付けに拾われない（＝消えずに残り続ける）
        ' ので、**その場でなかったことにする**ほうが安全である
        If Not ArticleImages.Register(imageName, account.AccountId, bytes.LongLength, width, height) Then

            SiteImage.DeleteQuietly(imageName, ImagePlace.Article)

            reason = ReasonSaveFailed
            Return False

        End If

        src = ArticleImages.SrcOf(imageName)
        Return True

    End Function


    ' 1枚の上限（バイト）
    '
    ' 【RoleLimits の ArticleImageMaxBytes と、SiteSettings の天井（3MB）の小さいほう】
    ' 天井は ASP.NET の maxRequestLength（4096KB）から来ている（SiteSettings.ImageMaxBytesCeiling）。
    ' 無制限（-1）でも天井で止める——止めないと、上限より大きいものが ASP.NET に断られ、理由の分からない失敗になる。
    ' 【読めなければ 0（＝貼らせない）】RoleLimits のフェイルクローズ
    Public Shared Function MaxImageBytes(roleKey As String) As Integer

        Dim limit As Integer = RoleLimits.GetInt(roleKey, RoleLimits.KeyArticleImageMaxBytes)

        If limit = RoleLimits.Unlimited Then
            Return SiteSettings.ImageMaxBytesCeiling
        End If

        If limit <= 0 Then
            Return 0
        End If

        Return Math.Min(limit, SiteSettings.ImageMaxBytesCeiling)

    End Function


    ' 送られてきた画像（multipart の "image"）を読む。読みながら大きさを測る（部屋の受け口と同じ形）
    Private Function ReadBody(maxBytes As Integer, ByRef reason As String) As Byte()

        If Request.ContentLength > maxBytes + MultipartSlackBytes Then
            reason = TooLargeText(maxBytes)
            Return Nothing
        End If

        Dim file As HttpPostedFile = Request.Files("image")

        If file Is Nothing OrElse file.ContentLength = 0 Then
            reason = ReasonBadFormat
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

                If buffer.Length + read > maxBytes Then
                    reason = TooLargeText(maxBytes)
                    Return Nothing
                End If

                buffer.Write(chunk, 0, read)
            Loop

            If buffer.Length = 0 Then
                reason = ReasonBadFormat
                Return Nothing
            End If

            Return buffer.ToArray()

        End Using

    End Function


    Private Shared Function TooLargeText(maxBytes As Integer) As String
        Return "画像が大きすぎます（" & MbText(maxBytes) & "まで）。"
    End Function


    ' 【1MB 未満は KB で出す】小さい上限を「0MB」と出さないため
    Private Shared Function MbText(bytes As Integer) As String

        If bytes < 1048576 Then
            Return Math.Max(1, CInt(Math.Round(bytes / 1024.0))).ToString(CultureInfo.InvariantCulture) & "KB"
        End If

        Return (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) & "MB"

    End Function


    Private Function ReadIntHeader(name As String) As Integer

        Dim value As Integer

        If Integer.TryParse(Request.Headers(name), NumberStyles.Integer, CultureInfo.InvariantCulture, value) Then
            Return value
        End If

        Return 0

    End Function


    ' 結果の JSON（文字列は JavaScriptSerializer に任せる。自前で組み立てない）
    Private Sub WriteResult(ok As Boolean, src As String, width As Integer, height As Integer, reason As String)

        Dim payload As New Dictionary(Of String, Object)

        payload("ok") = ok

        If ok Then
            payload("src") = src
            payload("width") = width
            payload("height") = height
        ElseIf reason IsNot Nothing Then
            payload("reason") = reason
        End If

        Response.Write(New Script.Serialization.JavaScriptSerializer().Serialize(payload))

        ' 【Response.End() ではなく CompleteRequest()】（部屋の受け口と同じ）
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
