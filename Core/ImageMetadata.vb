Imports System.IO
Imports System.Text

' ============================================================
' 画像から、撮った場所（GPS）などの情報を取り除く
' ============================================================
'
' 【なぜ要るのか】スマホで撮った写真には、撮った場所（GPS）・機種・日時が入っていることがある。
' 記事は誰でも見られて、消されずに残るので、そのまま置くと**書いた人の家の場所を配ることになる**。
'
' 【使うのは2か所】記事の画像の受け口（Articles/UploadImage.aspx.vb）と、部屋の画像の受け口（Chat/UploadImage.aspx.vb。
' 部屋あて・こっそりの両方）。**部屋と記事の両方が使うので、Chat/ に置いてある**（Articles/ にあった）
'
' 【ふつうの経路では、もう消えている】書く画面（Scripts/article-editor.js）も部屋の画面（Scripts/chat-room.js）も、GIF 以外を canvas で描き直してから送る。
' canvas から書き出した JPEG・PNG には、元の EXIF は入らない（開発機の Chrome で確かめた）。
' **それでもサーバーで消すのは、受け口は開発者ツールから直接叩けるから**（描き直しは画面の親切であって、守りではない）。
'
' 【描き直さない（再エンコードしない）】
' サーバーで絵を復号しない、という Core/SiteImage.vb の DetectExtension の決まりを守る
' （壊れた画像を解析する処理をサーバーに増やさない）。やるのは、**入れ物の区切りを数えて、決まった区切りを抜くことだけ**。
' 区切りの形が読めなければ Nothing を返す（＝受け付けない。途中までで保存しない）。
'
' 【向き（EXIF の Orientation）も消える】ふつうの経路（canvas）では、ブラウザが向きを直してから描くので困らない。
' 元の写真を直接送った場合だけ、横倒しに見えうる（割り切り）
Public NotInheritable Class ImageMetadata

    Private Sub New()
    End Sub


    ' 取り除いた結果（Bytes が Nothing なら、形が読めなかった）
    Public Class StripResult
        Public Property Bytes As Byte()
        ' 何かを取り除いたか（確かめるときのため）
        Public Property Removed As Boolean
    End Class


    ' 拡張子（SiteImage.ExtensionJpeg など。DetectExtension が先頭バイトで決めたもの）に合わせて取り除く
    Public Shared Function Strip(bytes As Byte(), extension As String) As StripResult

        Try

            Select Case extension
                Case SiteImage.ExtensionJpeg
                    Return StripJpeg(bytes)
                Case SiteImage.ExtensionPng
                    Return StripPng(bytes)
                Case SiteImage.ExtensionGif
                    Return StripGif(bytes)
                Case Else
                    Return New StripResult()
            End Select

        Catch ex As IndexOutOfRangeException
            ' 区切りの長さが中身より長い（途中で切れた・細工された）。読めないものとして断る
            Return New StripResult()
        End Try

    End Function


    ' ============================================================
    ' JPEG … FF D8 のあと、「FF xx ＋ 長さ（2バイト、自分を含む）＋ 中身」の区切りが並ぶ。
    ' FF DA（画像の本体の始まり）から先は、区切りを数えずにそのまま写す
    ' ============================================================
    '
    ' 消す：APP1（EXIF・XMP＝GPS はここ）／APP3〜APP13／APP15（IPTC・写真ソフトの情報など）／COM（注釈）
    ' 残す：APP0（JFIF）／APP2 のうち ICC_PROFILE（色の決まり）／APP14（Adobe。CMYK の色の変換に要る）／それ以外の区切り（表・フレームなど、絵を描くのに要るもの）
    Private Shared Function StripJpeg(bytes As Byte()) As StripResult

        Dim result As New StripResult()

        If bytes Is Nothing OrElse bytes.Length < 4 OrElse bytes(0) <> &HFF OrElse bytes(1) <> &HD8 Then
            Return result
        End If

        Using output As New MemoryStream(bytes.Length)

            output.WriteByte(&HFF)
            output.WriteByte(&HD8)

            Dim i As Integer = 2

            Do

                If i + 1 >= bytes.Length Then
                    Return result          ' FF DA に届く前に終わった＝読めない
                End If

                If bytes(i) <> &HFF Then
                    Return result
                End If

                Dim marker As Byte = bytes(i + 1)

                ' 詰め物の FF（FF が続く）は読み飛ばす
                If marker = &HFF Then
                    i += 1
                    Continue Do
                End If

                ' 長さを持たない区切り（TEM・RSTn）
                If marker = &H1 OrElse (marker >= &HD0 AndAlso marker <= &HD7) Then
                    output.WriteByte(&HFF)
                    output.WriteByte(marker)
                    i += 2
                    Continue Do
                End If

                ' 画像の終わり（本体が無いまま終わった）
                If marker = &HD9 Then
                    Return result
                End If

                Dim length As Integer = (CInt(bytes(i + 2)) << 8) Or bytes(i + 3)

                If length < 2 OrElse i + 2 + length > bytes.Length Then
                    Return result
                End If

                ' 画像の本体の始まり。ここから先は丸ごと写す
                If marker = &HDA Then
                    output.Write(bytes, i, bytes.Length - i)
                    Exit Do
                End If

                If ShouldDropJpegSegment(marker, bytes, i + 4, length - 2) Then
                    result.Removed = True
                Else
                    output.Write(bytes, i, 2 + length)
                End If

                i += 2 + length

            Loop

            result.Bytes = output.ToArray()

        End Using

        Return result

    End Function


    Private Shared Function ShouldDropJpegSegment(marker As Byte, bytes As Byte(), start As Integer, count As Integer) As Boolean

        ' COM（注釈）
        If marker = &HFE Then
            Return True
        End If

        ' APP0〜APP15 は E0〜EF
        If marker < &HE0 OrElse marker > &HEF Then
            Return False
        End If

        Select Case marker
            Case &HE0
                Return False                                        ' APP0（JFIF）
            Case &HE2
                Return Not StartsWithAscii(bytes, start, count, "ICC_PROFILE" & ChrW(0))   ' APP2 は色の決まりだけ残す
            Case &HEE
                Return Not StartsWithAscii(bytes, start, count, "Adobe")                    ' APP14 は Adobe だけ残す
            Case Else
                Return True                                         ' APP1（EXIF・XMP）ほか
        End Select

    End Function


    ' ============================================================
    ' PNG … 8バイトの印のあと、「長さ（4）＋種類（4）＋中身＋CRC（4）」のかたまりが並ぶ。IEND で終わる
    ' ============================================================
    '
    ' 消す：tEXt・zTXt・iTXt（文字の注釈・XMP）／eXIf（EXIF）／tIME（最後に変えた日時）
    Private Shared ReadOnly PngDropChunks As String() = {"tEXt", "zTXt", "iTXt", "eXIf", "tIME"}

    Private Shared Function StripPng(bytes As Byte()) As StripResult

        Dim result As New StripResult()

        If bytes Is Nothing OrElse bytes.Length < 8 Then
            Return result
        End If

        Using output As New MemoryStream(bytes.Length)

            output.Write(bytes, 0, 8)

            Dim i As Integer = 8
            Dim sawEnd As Boolean = False

            Do While i + 12 <= bytes.Length

                Dim length As Long =
                    (CLng(bytes(i)) << 24) Or (CLng(bytes(i + 1)) << 16) Or (CLng(bytes(i + 2)) << 8) Or bytes(i + 3)

                If length < 0 OrElse i + 12 + length > bytes.Length Then
                    Return result
                End If

                Dim type As String = Encoding.ASCII.GetString(bytes, i + 4, 4)
                Dim total As Integer = CInt(12 + length)

                If Array.IndexOf(PngDropChunks, type) >= 0 Then
                    result.Removed = True
                Else
                    output.Write(bytes, i, total)
                End If

                i += total

                If type = "IEND" Then
                    sawEnd = True
                    Exit Do
                End If

            Loop

            If Not sawEnd Then
                Return result
            End If

            result.Bytes = output.ToArray()

        End Using

        Return result

    End Function


    ' ============================================================
    ' GIF … 見出し（6）＋画面の説明（7）＋色の表、のあと、「拡張（21 xx …）」「画像（2C …）」が並び、3B で終わる。
    ' 拡張と画像の中身は「長さ（1）＋中身」の小分けが並び、長さ 0 で終わる
    ' ============================================================
    '
    ' 消す：注釈の拡張（21 FE）／アプリの拡張（21 FF）のうち NETSCAPE2.0・ANIMEXTS1.0 以外（XMP など）
    ' 残す：絵を描く拡張（21 F9＝アニメの間隔・透明）・文字の拡張（21 01）・画像そのもの・繰り返しの指定（NETSCAPE2.0）
    Private Shared Function StripGif(bytes As Byte()) As StripResult

        Dim result As New StripResult()

        If bytes Is Nothing OrElse bytes.Length < 13 Then
            Return result
        End If

        Using output As New MemoryStream(bytes.Length)

            ' 見出し＋画面の説明
            Dim i As Integer = 13
            Dim packed As Byte = bytes(10)

            If (packed And &H80) <> 0 Then
                i += 3 * (1 << ((packed And &H7) + 1))       ' 全体の色の表
            End If

            If i > bytes.Length Then
                Return result
            End If

            output.Write(bytes, 0, i)

            Do

                If i >= bytes.Length Then
                    Return result                              ' 3B に届く前に終わった
                End If

                Dim kind As Byte = bytes(i)

                If kind = &H3B Then
                    output.WriteByte(&H3B)
                    Exit Do
                End If

                If kind = &H21 Then

                    Dim label As Byte = bytes(i + 1)
                    Dim blockEnd As Integer = SkipSubBlocks(bytes, i + 2)

                    If blockEnd < 0 Then
                        Return result
                    End If

                    Dim drop As Boolean = False

                    If label = &HFE Then
                        drop = True
                    ElseIf label = &HFF Then
                        ' アプリの拡張の最初の小分けは、名前（8）＋認証（3）の 11 バイト
                        drop = Not (bytes(i + 2) = 11 AndAlso
                                    (StartsWithAscii(bytes, i + 3, 11, "NETSCAPE2.0") OrElse
                                     StartsWithAscii(bytes, i + 3, 11, "ANIMEXTS1.0")))
                    End If

                    If drop Then
                        result.Removed = True
                    Else
                        output.Write(bytes, i, blockEnd - i)
                    End If

                    i = blockEnd
                    Continue Do

                End If

                If kind = &H2C Then

                    ' 画像の説明（2C を含めて 10 バイト）＋色の表＋LZW の最小の長さ（1）＋小分け
                    If i + 10 > bytes.Length Then
                        Return result
                    End If

                    Dim localPacked As Byte = bytes(i + 9)
                    Dim j As Integer = i + 10

                    If (localPacked And &H80) <> 0 Then
                        j += 3 * (1 << ((localPacked And &H7) + 1))
                    End If

                    j += 1                                     ' LZW の最小の長さ

                    Dim imageEnd As Integer = SkipSubBlocks(bytes, j)

                    If imageEnd < 0 Then
                        Return result
                    End If

                    output.Write(bytes, i, imageEnd - i)
                    i = imageEnd
                    Continue Do

                End If

                Return result                                  ' 知らない区切り

            Loop

            result.Bytes = output.ToArray()

        End Using

        Return result

    End Function


    ' 小分け（長さ1バイト＋中身）を、長さ 0 のところまで読み飛ばす。次の位置を返す（読めなければ -1）
    Private Shared Function SkipSubBlocks(bytes As Byte(), start As Integer) As Integer

        Dim i As Integer = start

        Do
            If i >= bytes.Length Then
                Return -1
            End If

            Dim size As Integer = bytes(i)
            i += 1

            If size = 0 Then
                Return i
            End If

            i += size
        Loop

    End Function


    Private Shared Function StartsWithAscii(bytes As Byte(), start As Integer, count As Integer, text As String) As Boolean

        If count < text.Length OrElse start + text.Length > bytes.Length Then
            Return False
        End If

        For k As Integer = 0 To text.Length - 1
            If bytes(start + k) <> AscW(text(k)) Then
                Return False
            End If
        Next

        Return True

    End Function

End Class
