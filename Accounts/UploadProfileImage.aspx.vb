Imports System.Globalization
Imports System.IO

' ============================================================
' プロフィールの画像（アイコン・カバー）の受け口（ブログ型の①）
' ============================================================
'
' プロフィールの編集画面（Scripts/profile-editor.js）から、POST で呼ばれる。
' **形は記事の画像の受け口（Articles/UploadImage.aspx）とそろえてある**——
'
'     POST /Accounts/UploadProfileImage.aspx
'     X-Image-Kind    … "avatar" か "cover"
'     X-Image-Width   … 画面が数えた幅
'     X-Image-Height  … 画面が数えた高さ
'     （本文）        … multipart/form-data。ファイルの部分の名前は "image"
'
'     {"ok":true,"src":"/Uploads/profiles/2/….jpg"}
'     {"ok":false,"reason":"..."}
'
' 【multipart にしてある。生のバイト列で送らないこと】本番の手前の検査（WAF）が、生のバイト列を1割ほど 403 で弾く
' （部屋の画像で踏んだ。理由と実測は Chat/UploadImage.aspx.vb の先頭）。
'
' 【身元はログインの札】（AccountSession.Current）。AccountPageBase は継承しない——あちらはログインの画面へ転送するが、
' ここが返すのは JSON なので、転送の HTML を返されても画面は読めない。
'
' 【よそのサイトから呼ばせない】ログインの Cookie は SameSite=Lax なので、よそのサイトからの POST には付かない。
'
' 【記事の画像と違って、保存と同時に DB の列も書き換える】
' 記事の画像は「本文に貼られて、記事が保存されるまで、どの記事のものか決まらない」ので、
' 受け口は保存するだけだった（そのぶん、使われないまま残る画像を片付ける仕事が要った）。
' プロフィールの画像は**入れ先が最初から決まっている**ので、その場で差し替えてしまえば、
' 宙に浮いた画像が1枚も生まれない。あわせて、古い画像もその場で消える（AccountProfileImages.Replace）
Public Class UploadProfileImage
    Inherits System.Web.UI.Page

    ' multipart の境界や見出しのぶん（記事の受け口と同じ）
    Private Const MultipartSlackBytes As Integer = 16 * 1024

    Private Const ReasonNotLoggedIn As String =
        "ログインが切れています。もう一度ログインしてから、選び直してください。"

    Private Const ReasonBadKind As String = "画像の種類が分かりませんでした。ページを開き直してください。"

    Private Const ReasonBadFormat As String =
        "この形式の画像は使えません。対応しているのは " & SiteImage.AcceptedFormatsText & " です。"

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
            WriteResult(False, Nothing, Nothing)
            Return
        End If

        Dim reason As String = Nothing
        Dim src As String = Nothing
        Dim ok As Boolean

        Try
            ok = TryHandle(src, reason)
        Catch ex As Exception

            ErrorReport.Report(ex, "UploadProfileImage.TryHandle")

            ok = False
            reason = ReasonSaveFailed

        End Try

        WriteResult(ok, src, reason)

    End Sub


    Private Function TryHandle(ByRef src As String, ByRef reason As String) As Boolean

        ' === 1. 身元 ===
        Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

        If account Is Nothing Then
            reason = ReasonNotLoggedIn
            Return False
        End If

        ' === 2. どちらの画像か ===
        Dim kind As ProfileImageKind

        If Not AccountProfileImages.TryParseKind(Request.Headers("X-Image-Kind"), kind) Then
            reason = ReasonBadKind
            Return False
        End If

        ' === 3. 中身 ===
        Dim maxBytes As Integer = AccountProfileImages.MaxBytes

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

        ' === 5. 保存 ===
        Dim imageName As String = SiteImage.NewProfileName(account.AccountId, extension)
        Dim saveError As String = Nothing

        If Not SiteImage.TrySave(bytes, imageName, ImagePlace.Profile, saveError) Then
            reason = ReasonSaveFailed
            Return False
        End If

        ' === 6. DB の列を差し替える（済んでから、古いファイルが消える） ===
        '
        ' 【ここで失敗したら、いま保存したファイルを消す】
        ' 残すと、どこからも指されていないファイルが1枚増える（片付ける仕事を作っていないので、消えない）
        Try
            AccountProfileImages.Replace(account.AccountId, kind, imageName)
        Catch ex As Exception

            SiteImage.DeleteQuietly(imageName, ImagePlace.Profile)

            ErrorReport.Report(ex, "UploadProfileImage.Replace")

            reason = ReasonSaveFailed
            Return False

        End Try

        src = AccountProfileImages.SrcOf(imageName)
        Return True

    End Function


    ' 送られてきた画像（multipart の "image"）を読む。読みながら大きさを測る（記事の受け口と同じ形）
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


    ' 【1MB 未満は KB で出す】小さい上限を「0MB」と出さないため（記事の受け口と同じ）
    Private Shared Function MbText(bytes As Integer) As String

        If bytes < 1048576 Then
            Return Math.Max(1, CInt(Math.Round(bytes / 1024.0))).ToString(CultureInfo.InvariantCulture) & "KB"
        End If

        Return (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) & "MB"

    End Function


    ' 結果の JSON（文字列は JavaScriptSerializer に任せる。自前で組み立てない）
    Private Sub WriteResult(ok As Boolean, src As String, reason As String)

        Dim payload As New Dictionary(Of String, Object)

        payload("ok") = ok

        If ok Then
            payload("src") = src
        ElseIf reason IsNot Nothing Then
            payload("reason") = reason
        End If

        Response.Write(New Script.Serialization.JavaScriptSerializer().Serialize(payload))

        ' 【Response.End() ではなく CompleteRequest()】（記事の受け口と同じ）
        Response.Flush()
        HttpContext.Current.ApplicationInstance.CompleteRequest()

    End Sub

End Class
