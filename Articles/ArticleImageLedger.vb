Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.Text

' 記事の画像の台帳
'
' 【何を置くか】表2本（ArticleImageFiles・ArticleImageUses）への出入りだけ。**SQL はここだけ**。
' 画像そのものの決まり（名前の形・置き場所・保存）は Core/SiteImage.vb、
' 本文とのかかわり（src の形・名前の拾い出し・ファイルの後片付け）は Articles/ArticleImages.vb。
'
' 【なぜ台帳にしたか】それまでは、画像について何かを知りたいたびに
' **ディスクの全ファイルと、全記事・全控えの本文（BodyHtml）を丸ごと読んでいた**。
' 記事と画像が増えるほど、毎晩の仕事と、画像を1枚上げるたびの待ち時間が、そのまま増える形だった
' （判断：利用者が増えてからでは大変になるので、VBチャットのうちから入れておく）。
' 今は、読むのは索引だけになっている。
'
' 【書くときの決まり】
'   ・上げるとき … ファイルを保存してから台帳へ1行（書けなければファイルを消して断る＝ArticleImages.Register）
'   ・保存のとき … 本文の保存と**同じトランザクション**で、使っている表を**差分だけ**書き換える
'   ・消すとき   … ファイルを消してから台帳の行を消す
'
' 【名前の一覧の渡し方は XML】本文に画像が何枚あるかは決まっていないので、
' 名前をパラメータに1つずつ並べる形だと、多い本文で上限（2100）に当たりうる。
' **STRING_SPLIT を使わなかったのは、DB の互換性レベルが 130 未満だと使えないため**——
' 本番の互換性レベルはこちらから確かめられないので、SQL Server 2005 から在る XML で組んである
' （ここが動かないと、記事の保存そのものが失敗する場所である）
Public NotInheritable Class ArticleImageLedger

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"

    ' ArticleImageUses.OwnerKind（10刻み。30以降は空けてある）
    Public Const OwnerArticle As Byte = 10
    Public Const OwnerDraft As Byte = 20


    ' ============================================================
    ' 使っている表を、本文の名前に合わせる（差分だけ書く）
    ' ============================================================
    '
    ' 【呼び出し側の SQL に、この断片をそのまま挟む】本文を書く文と同じトランザクションに入れるため、
    ' 別の接続・別のコマンドにしていない。
    '
    ' 挟む前に @SyncArticleId（INT）と @SyncOwnerKind（TINYINT）を DECLARE しておくこと。
    ' パラメータ @ImageNames（XML）は AddNamesParameter が足す。
    '
    ' 【差分だけ書く】消えた名前だけ DELETE、増えた名前だけ INSERT。
    ' **本文の画像が変わっていなければ、どちらも0行**になる（自動保存は3秒ごとに走るので、ここが効く）。
    '
    ' 【台帳に在る名前だけ INSERT する】HTMLモードで存在しない画像の URL を手で書くと、
    ' 形だけ正しい名前が本文に入りうる。そのまま入れると外部キーで落ちて、**記事の保存そのものが失敗する**
    Friend Const SyncUsesSql As String =
        "DECLARE @SyncUse TABLE (ImageName NVARCHAR(64) NOT NULL PRIMARY KEY); " &
        "INSERT INTO @SyncUse (ImageName) " &
        "SELECT DISTINCT LOWER(x.n.value('.', 'NVARCHAR(64)')) " &
        "  FROM @ImageNames.nodes('/n/i') AS x(n); " &
        "DELETE u FROM ArticleImageUses u " &
        " WHERE u.ArticleId = @SyncArticleId AND u.OwnerKind = @SyncOwnerKind " &
        "   AND NOT EXISTS (SELECT 1 FROM @SyncUse s WHERE s.ImageName = u.ImageName); " &
        "INSERT INTO ArticleImageUses (ArticleId, OwnerKind, ImageName) " &
        "SELECT @SyncArticleId, @SyncOwnerKind, s.ImageName " &
        "  FROM @SyncUse s " &
        " WHERE EXISTS (SELECT 1 FROM ArticleImageFiles f WHERE f.ImageName = s.ImageName) " &
        "   AND NOT EXISTS (SELECT 1 FROM ArticleImageUses u " &
        "                    WHERE u.ArticleId = @SyncArticleId AND u.OwnerKind = @SyncOwnerKind " &
        "                      AND u.ImageName = s.ImageName); "


    ' SyncUsesSql が要る @ImageNames を足す
    Friend Shared Sub AddNamesParameter(cmd As SqlCommand, names As HashSet(Of String))

        cmd.Parameters.Add("@ImageNames", SqlDbType.Xml).Value = NamesXml(names)

    End Sub


    ' 名前の一覧を <n><i>2/….png</i>…</n> に組み立てる
    '
    ' 【文字をそのまま入れてよい理由】名前は SiteImage.IsValidName を通った形
    ' （数字・英小文字・/ と . だけ）で、XML で特別な意味を持つ文字は1つも入らない。
    ' それでも念のため、形に当てはまらない名前は捨てる
    Private Shared Function NamesXml(names As HashSet(Of String)) As String

        Dim sb As New StringBuilder("<n>")

        If names IsNot Nothing Then

            For Each name As String In names

                If SiteImage.IsValidName(name, ImagePlace.Article) Then
                    sb.Append("<i>").Append(name.ToLowerInvariant()).Append("</i>")
                End If

            Next

        End If

        sb.Append("</n>")

        Return sb.ToString()

    End Function


    ' ============================================================
    ' 台帳への出入り
    ' ============================================================

    ' 上げた画像を1行書く（既にあれば何もしない）
    Friend Shared Sub Add(imageName As String, accountId As Integer, byteSize As Long,
                          width As Integer, height As Integer)

        Const sql As String =
            "INSERT INTO ArticleImageFiles (ImageName, AccountId, ByteSize, Width, Height) " &
            "SELECT @ImageName, @AccountId, @ByteSize, @Width, @Height " &
            " WHERE NOT EXISTS (SELECT 1 FROM ArticleImageFiles WHERE ImageName = @ImageName);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ImageName", SqlDbType.NVarChar, 64).Value = imageName
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@ByteSize", SqlDbType.BigInt).Value = byteSize
                cmd.Parameters.Add("@Width", SqlDbType.Int).Value = If(width > 0, CObj(width), CObj(DBNull.Value))
                cmd.Parameters.Add("@Height", SqlDbType.Int).Value = If(height > 0, CObj(height), CObj(DBNull.Value))

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    ' 上げた時刻を指定して1行書く（全体の突き合わせが、前から在ったファイルを載せるときだけ使う）
    '
    ' 【時刻はファイルの更新時刻にする】「今」にすると、台帳より前から在った古い画像が
    ' 1日たつまで片付けの対象にならない
    Friend Shared Sub AddWithTime(imageName As String, accountId As Integer, byteSize As Long, uploadedAt As DateTime)

        Const sql As String =
            "INSERT INTO ArticleImageFiles (ImageName, AccountId, ByteSize, UploadedAt) " &
            "SELECT @ImageName, @AccountId, @ByteSize, @UploadedAt " &
            " WHERE NOT EXISTS (SELECT 1 FROM ArticleImageFiles WHERE ImageName = @ImageName);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ImageName", SqlDbType.NVarChar, 64).Value = imageName
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@ByteSize", SqlDbType.BigInt).Value = byteSize
                cmd.Parameters.Add("@UploadedAt", SqlDbType.DateTime2).Value = uploadedAt

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    ' 台帳の行を消す（ファイルを消したあとに呼ぶ）
    '
    ' 【使われている画像は消さない】WHERE に入れてある——1つの文で確かめることで、
    ' 「使われていないことを読む → 消す」の間に本文へ貼り直された画像を消してしまう道を、狭くしている
    Friend Shared Function Remove(imageName As String) As Boolean

        Const sql As String =
            "DELETE FROM ArticleImageFiles " &
            " WHERE ImageName = @ImageName " &
            "   AND NOT EXISTS (SELECT 1 FROM ArticleImageUses u WHERE u.ImageName = @ImageName);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ImageName", SqlDbType.NVarChar, 64).Value = imageName

                conn.Open()

                Return cmd.ExecuteNonQuery() = 1

            End Using
        End Using

    End Function


    ' そのアカウントが置いている合計（バイト）
    '
    ' 【索引 IX_ArticleImageFiles_Account だけで済む】ByteSize を索引に載せてあるため
    Friend Shared Function UsedBytes(accountId As Integer) As Long

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT ISNULL(SUM(ByteSize), 0) FROM ArticleImageFiles WHERE AccountId = @AccountId;", conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                If value Is Nothing OrElse Convert.IsDBNull(value) Then
                    Return 0
                End If

                Return Convert.ToInt64(value, CultureInfo.InvariantCulture)

            End Using
        End Using

    End Function


    ' どこからも使われていない・上げて olderThanDays 日以上たった画像の名前（多くても max 件）
    '
    ' 【1日1回の片付けが、これだけを引く】本文は1行も読まない
    Friend Shared Function FindUnused(olderThanDays As Integer, max As Integer) As List(Of String)

        Const sql As String =
            "SELECT TOP (@Max) f.ImageName " &
            "  FROM ArticleImageFiles f " &
            " WHERE f.UploadedAt < DATEADD(DAY, -@Days, SYSDATETIME()) " &
            "   AND NOT EXISTS (SELECT 1 FROM ArticleImageUses u WHERE u.ImageName = f.ImageName) " &
            " ORDER BY f.UploadedAt;"

        Dim names As New List(Of String)

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Max", SqlDbType.Int).Value = max
                cmd.Parameters.Add("@Days", SqlDbType.Int).Value = olderThanDays

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        names.Add(reader.GetString(0))
                    End While
                End Using

            End Using
        End Using

        Return names

    End Function


    ' 渡した名前のうち、どこからも使われていないもの（記事を削除したあとの後片付けで使う）
    '
    ' 【こちらは日数を見ない】削除した記事に使われていた画像だと分かっているため
    ' （ArticleImages.CleanupAfterDelete の、前からの決まり）
    Friend Shared Function FilterUnused(names As HashSet(Of String)) As List(Of String)

        Dim result As New List(Of String)

        If names Is Nothing OrElse names.Count = 0 Then
            Return result
        End If

        Const sql As String =
            "DECLARE @Ask TABLE (ImageName NVARCHAR(64) NOT NULL PRIMARY KEY); " &
            "INSERT INTO @Ask (ImageName) " &
            "SELECT DISTINCT LOWER(x.n.value('.', 'NVARCHAR(64)')) FROM @ImageNames.nodes('/n/i') AS x(n); " &
            "SELECT f.ImageName FROM ArticleImageFiles f " &
            " WHERE EXISTS (SELECT 1 FROM @Ask a WHERE a.ImageName = f.ImageName) " &
            "   AND NOT EXISTS (SELECT 1 FROM ArticleImageUses u WHERE u.ImageName = f.ImageName);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                AddNamesParameter(cmd, names)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        result.Add(reader.GetString(0))
                    End While
                End Using

            End Using
        End Using

        Return result

    End Function


    ' 台帳に在る名前をすべて。全体の突き合わせ（ArticleImages.Rebuild）で使う
    Friend Shared Function AllNames() As HashSet(Of String)

        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("SELECT ImageName FROM ArticleImageFiles;", conn)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        names.Add(reader.GetString(0))
                    End While
                End Using

            End Using
        End Using

        Return names

    End Function


    ' 使っている表を、本文1つぶんだけ合わせる（この関数だけで完結する形）
    '
    ' 【ふだんは使わない】保存のときは SyncUsesSql を本文の文と同じトランザクションに挟む。
    ' こちらは、全体の突き合わせ（ArticleImages.Rebuild）が本文を1つずつ流し込むためのもの
    Friend Shared Sub SyncUses(articleId As Integer, ownerKind As Byte, names As HashSet(Of String))

        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @SyncArticleId INT = @ArticleId, @SyncOwnerKind TINYINT = @OwnerKind; " &
            SyncUsesSql &
            "COMMIT;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@OwnerKind", SqlDbType.TinyInt).Value = ownerKind
                AddNamesParameter(cmd, names)

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    ' 控え（OwnerKind = 20）の行を消す。控えそのものを消すときに呼ぶ
    Friend Shared Function DeleteDraftUsesSql() As String

        Return "DELETE FROM ArticleImageUses WHERE ArticleId = @ArticleId AND OwnerKind = 20; "

    End Function


    Friend Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
