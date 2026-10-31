Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 部屋のパスワードを、管理人に見せるための「暗号文」を作る・戻すクラス
'
' 【なぜ見せるのか】理由：一時部屋のパスワードは管理人自身もすぐ忘れるので、見て分かるようにする。
' アカウントのパスワードは本人しか知らないもの（だからハッシュ）。部屋の合言葉は、管理人が人に教えるためのもの（だから見える）。
' 性質が別物である。「管理人にも見えない」と決めていたが、同じ日の夜に覆った。
'
' 【照合はハッシュのまま】ChatRooms.PasswordHash（PBKDF2。RoomPassword.vb）は今までどおり残し、照合はそちらだけで行う。
' 暗号文（ChatRooms.PasswordCipher）は**見せるためだけ**の写しで、照合には一切使わない。
' だから鍵のファイルが消えても壊れても、止まるのは「見せる」だけ（「今は表示できません」）。鍵付きの部屋には今までどおり入れる。
' （照合まで暗号文に頼ると、鍵が読めなくなった瞬間に鍵付きの部屋へ誰も入れなくなる）
'
' 【方式】AES-256-CBC で暗号化し、HMAC-SHA256 で改ざんを確かめる（暗号化してから MAC＝encrypt-then-MAC）。
' .NET Framework 4.8 には AesGcm が無いので、この組み合わせにしてある。
' 形は1本の文字列：「v1$（IV の Base64）$（暗号文の Base64）$（MAC の Base64）」。先頭の v1 で、あとで方式を替えても見分けられる。
' 暗号の鍵と MAC の鍵は、ファイルの鍵（32バイト）から HMAC で2つに分けて作る（同じ鍵を2つの用途に使わない）。
'
' 【鍵の置き場所】App_Data\room-password-key.txt（Base64 で32バイトを1行）。**DB の外**に置く
' （DB だけが漏れたときに、暗号文を戻せないようにするため。トリップのソルト App_Data\trip-salt.txt と同じ置き方）。
' git にも発行物にも載せない（.gitignore・発行プロファイルで除外。.vbproj に登録しない）。
'
' 【鍵が無ければ、自分で作る】（トリップのソルトとの違い）
' トリップのソルトは、値が変わると全員のトリップが変わるので、手で置いて同じ値を守る。
' こちらは鍵が変わっても、それまでの部屋のパスワードが「表示できません」になるだけ（一時部屋なので害は小さい）。
' だから「ファイルが**無い**」ときだけ、ランダムな鍵を作って置く（エラー報告メールで1通知らせる）。
' **ファイルが在るのに読めない・形が壊れているときは、作り直さない**（上書きすると、今の部屋のパスワードが全部見えなくなる。
' 一時的に読めないだけのこともある）。そのときは「見せる」を止めて知らせる。
Public NotInheritable Class RoomPasswordSecret

    Private Const KeyRelativePath As String = "~/App_Data/room-password-key.txt"
    Private Const KeyLength As Integer = 32
    Private Const FormatVersion As String = "v1"

    ' 読んだ鍵（暗号用・MAC 用）。LoadState で1回だけ決める
    Private Shared ReadOnly StateLock As New Object()
    Private Shared _loaded As Boolean
    Private Shared _encKey As Byte()
    Private Shared _macKey As Byte()


    ' 「見せる」が動いているか（鍵が読めているか）
    Public Shared ReadOnly Property IsEnabled As Boolean
        Get
            EnsureLoaded()
            Return _encKey IsNot Nothing
        End Get
    End Property


    ' パスワードを暗号文にする
    '
    ' 戻り値 … 暗号文。鍵が読めなければ Nothing（DB には NULL が入り、その部屋は「表示できません」になる）
    ' 【例外を投げない】部屋を作る・設定を変えるのを、「見せる」の都合で止めないため
    Public Shared Function Protect(password As String) As String

        If String.IsNullOrEmpty(password) Then
            Return Nothing
        End If

        EnsureLoaded()

        If _encKey Is Nothing Then
            Return Nothing
        End If

        Try

            Dim plain As Byte() = Encoding.UTF8.GetBytes(password)
            Dim iv As Byte()
            Dim cipher As Byte()

            Using aes As Aes = Aes.Create()

                aes.KeySize = 256
                aes.Mode = CipherMode.CBC
                aes.Padding = PaddingMode.PKCS7
                aes.Key = _encKey
                aes.GenerateIV()
                iv = aes.IV

                Using enc As ICryptoTransform = aes.CreateEncryptor()
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length)
                End Using

            End Using

            Dim mac As Byte() = ComputeMac(iv, cipher)

            Return FormatVersion & "$" & Convert.ToBase64String(iv) & "$" &
                   Convert.ToBase64String(cipher) & "$" & Convert.ToBase64String(mac)

        Catch ex As Exception
            ErrorReport.Report(ex, "RoomPasswordSecret.Protect")
            Return Nothing
        End Try

    End Function


    ' 暗号文からパスワードを戻す
    '
    ' 戻り値 … 戻せたか。戻せなければ password は Nothing
    '   （鍵が読めない・形が違う・MAC が合わない＝鍵が作り直された、など）
    ' 【MAC が合わないのは、ふつうに起こりうる】鍵のファイルが消えて作り直されたあとの古い部屋。知らせない（メールが来続ける）
    Public Shared Function TryUnprotect(protectedText As String, ByRef password As String) As Boolean

        password = Nothing

        If String.IsNullOrEmpty(protectedText) Then
            Return False
        End If

        EnsureLoaded()

        If _encKey Is Nothing Then
            Return False
        End If

        Dim parts As String() = protectedText.Split("$"c)

        If parts.Length <> 4 OrElse parts(0) <> FormatVersion Then
            Return False
        End If

        Try

            Dim iv As Byte() = Convert.FromBase64String(parts(1))
            Dim cipher As Byte() = Convert.FromBase64String(parts(2))
            Dim mac As Byte() = Convert.FromBase64String(parts(3))

            If iv.Length <> 16 OrElse Not RoomPassword.FixedTimeEquals(mac, ComputeMac(iv, cipher)) Then
                Return False
            End If

            Using aes As Aes = Aes.Create()

                aes.KeySize = 256
                aes.Mode = CipherMode.CBC
                aes.Padding = PaddingMode.PKCS7
                aes.Key = _encKey
                aes.IV = iv

                Using dec As ICryptoTransform = aes.CreateDecryptor()
                    Dim plain As Byte() = dec.TransformFinalBlock(cipher, 0, cipher.Length)
                    password = Encoding.UTF8.GetString(plain)
                End Using

            End Using

            Return True

        Catch ex As FormatException
            Return False
        Catch ex As CryptographicException
            Return False
        End Try

    End Function


    ' MAC は「形の印・IV・暗号文」をまとめて計算する（どれか1つでも書き換えられたら合わない）
    Private Shared Function ComputeMac(iv As Byte(), cipher As Byte()) As Byte()

        Dim head As Byte() = Encoding.ASCII.GetBytes(FormatVersion)
        Dim all(head.Length + iv.Length + cipher.Length - 1) As Byte

        Buffer.BlockCopy(head, 0, all, 0, head.Length)
        Buffer.BlockCopy(iv, 0, all, head.Length, iv.Length)
        Buffer.BlockCopy(cipher, 0, all, head.Length + iv.Length, cipher.Length)

        Using hmac As New HMACSHA256(_macKey)
            Return hmac.ComputeHash(all)
        End Using

    End Function


    ' 鍵を読む（最初の1回だけ。読めなかったら、アプリが再起動するまで「見せる」は止まったまま）
    '
    ' 【Shared の初期化子にしない】ファイルを作ることがあるので、失敗の扱いを自分で持ちたい
    Private Shared Sub EnsureLoaded()

        If _loaded Then
            Return
        End If

        SyncLock StateLock

            If _loaded Then
                Return
            End If

            Dim master As Byte() = LoadOrCreateKey()

            If master IsNot Nothing Then
                Using hmac As New HMACSHA256(master)
                    _encKey = hmac.ComputeHash(Encoding.ASCII.GetBytes("vb-chat room password: encrypt"))
                    _macKey = hmac.ComputeHash(Encoding.ASCII.GetBytes("vb-chat room password: mac"))
                End Using
            End If

            _loaded = True

        End SyncLock

    End Sub


    Private Shared Function LoadOrCreateKey() As Byte()

        Dim path As String = Nothing

        Try

            path = HostingEnvironment.MapPath(KeyRelativePath)

            If String.IsNullOrEmpty(path) Then
                ReportDisabled("App_Data の場所が分かりません")
                Return Nothing
            End If

            If Not File.Exists(path) Then
                Return CreateKey(path)
            End If

            Dim raw As String = File.ReadAllText(path).Trim()
            Dim key As Byte()

            Try
                key = Convert.FromBase64String(raw)
            Catch ex As FormatException
                ReportDisabled("App_Data\room-password-key.txt が Base64 として読めません（作り直していません）")
                Return Nothing
            End Try

            If key.Length <> KeyLength Then
                ReportDisabled("App_Data\room-password-key.txt の鍵の長さが違います（" & key.Length & "バイト。" &
                               KeyLength & "バイトが必要。作り直していません）")
                Return Nothing
            End If

            Return key

        Catch ex As Exception
            ReportDisabled("App_Data\room-password-key.txt を読めませんでした： " & ex.Message)
            Return Nothing
        End Try

    End Function


    ' 鍵が無いので、作って置く（ランダムな32バイト）
    '
    ' 【FileMode.CreateNew】同時に2つのアプリ（発行の瞬間の新旧など）が作ろうとしても、片方だけが作る。
    ' 負けたほうは、勝ったほうのファイルを読む
    Private Shared Function CreateKey(path As String) As Byte()

        Dim key(KeyLength - 1) As Byte

        Using rng As New RNGCryptoServiceProvider()
            rng.GetBytes(key)
        End Using

        Try

            Using fs As New FileStream(path, FileMode.CreateNew, FileAccess.Write)
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(Convert.ToBase64String(key))
                fs.Write(bytes, 0, bytes.Length)
            End Using

        Catch ex As IOException When File.Exists(path)

            ' ほかの誰かが先に作った。そちらを読む
            Try
                Dim other As Byte() = Convert.FromBase64String(File.ReadAllText(path).Trim())
                If other.Length = KeyLength Then
                    Return other
                End If
            Catch
            End Try

            ReportDisabled("App_Data\room-password-key.txt を作ろうとしたら、読めないファイルが先にありました")
            Return Nothing

        End Try

        ' 作ったことを1通知らせる（鍵が消えていた＝それまでの部屋のパスワードは表示できない、の合図でもある）
        ErrorReport.ReportProblem("RoomPasswordSecret.CreateKey",
            "App_Data\room-password-key.txt が無かったので、新しい鍵を作りました。" &
            "それより前にかけた部屋のパスワードは、管理人の画面に「表示できません」と出ます（入室の照合は今までどおり動きます）。" &
            "初めて動かしたときなら、これで正常です。")

        Return key

    End Function


    ' 「見せる」を止めたことを知らせる（鍵の中身は入れない）
    Private Shared Sub ReportDisabled(reason As String)

        ErrorReport.ReportProblem("RoomPasswordSecret.LoadKey",
            "部屋のパスワードを管理人に見せる機能を止めました（照合は今までどおり動きます）： " & reason)

    End Sub

End Class
