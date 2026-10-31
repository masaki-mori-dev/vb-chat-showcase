Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' プロフィールの表（AccountProfiles）への出入り（ブログ型の①）
'
' 【SQL をここ1か所に閉じ込めてある】AccountStore・ArticleStore と同じ形。
' C#／ASP.NET Core MVC へ移るとき、画面は全部書き直しになるが、ここは中身を入れ替えるだけで移せる。
'
' 【DB が読めないときは、例外を投げる（フェイルクローズ）】
' 受け取った側（画面）が握って、利用者には「混み合っています」とだけ出し、ErrorReport で知らせる。
' AccountStore と同じ向きにそろえてある。
'
' 【画像の2列は、Save では触らない】
' 画像は選んだ時点で差し替わる（UpdateImage）ので、フォームの「保存する」が
' 古い名前を書き戻さないよう、Save の UPDATE 文から外してある。
' **画像の列を Save に足さないこと**——足すと、画像を差し替えたあとに保存を押しただけで、
' 画面が開いたときの（古い）名前に戻る
Public NotInheritable Class AccountProfileStore

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' ============================================================
    ' 1人ぶんのプロフィール
    ' ============================================================
    '
    ' 【名前（表示名）は、ここに無い】Accounts.DisplayName が正である（理由は AccountProfiles.sql）。
    ' 公開ページへ渡すときだけ、下の PublicProfile が表示名も一緒に持つ
    Public Class ProfileRecord

        Public Property AccountId As Integer

        Public Property AvatarImageName As String
        Public Property CoverImageName As String

        ' 文字のアイコン（写真が無いときの「色の付いた丸に1文字」）
        ' **自分で変えたときだけ入る。Nothing が「最初のまま」**（最初の文字と色は Accounts/AccountAvatar.vb が決める）
        Public Property AvatarLetter As String
        Public Property AvatarColor As String

        Public Property Introduction As String
        Public Property IntroductionVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property Gender As Integer?
        Public Property GenderVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property BirthDate As Date?
        Public Property BirthDateMode As Integer = ProfileChoices.BirthFull
        ' 生年月日の公開・非公開（ほかの〇〇Visibility と同じ値）。BirthDateMode とは別に持つ
        Public Property BirthDateVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property BloodType As Integer?
        Public Property BloodTypeVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property Hometown As Integer?
        Public Property HometownVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property Residence As Integer?
        Public Property ResidenceVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property RelationshipStatus As Integer?
        Public Property RelationshipStatusVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property Occupation As Integer?
        Public Property OccupationText As String
        Public Property OccupationVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property XUserName As String
        Public Property XVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property FacebookUserName As String
        Public Property FacebookVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property InstagramUserName As String
        Public Property InstagramVisibility As Integer = ProfileChoices.VisibilityPublic

        Public Property BlogTitle As String
        Public Property BlogDescription As String

    End Class


    ' 公開ページ（②③④）へ渡す1人ぶん
    '
    ' 【守る列を、1つも持っていない】パスワードのハッシュ・鍵の世代・メールアドレスは読まない。
    ' **これが、Accounts と別の表にした理由そのものである**（AccountProfiles.sql の先頭）。
    ' 公開ページに要る「ユーザー名（URL に出る）」と「表示名」だけを、Accounts から取っている
    Public Class PublicProfile
        Public Property AccountId As Integer
        Public Property UserName As String
        Public Property DisplayName As String
        Public Property Profile As ProfileRecord
    End Class


    ' ============================================================
    ' 読む
    ' ============================================================

    ' そのアカウントのプロフィール
    '
    ' 【行が無ければ、空のプロフィールを返す】（Nothing を返さない）
    ' 行は「はじめて保存したとき」に作られる。呼ぶ側に「行が無い場合」の分岐を作らせないため、
    ' 既定値の入った器を返す
    Public Shared Function Find(accountId As Integer) As ProfileRecord

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(SelectColumns & " FROM AccountProfiles WHERE AccountId = @Id;", conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return New ProfileRecord With {.AccountId = accountId}
                    End If

                    Return ReadRecord(reader)

                End Using

            End Using
        End Using

    End Function


    ' 公開ページのための読み出し（ユーザー名で引く。無い・止めているアカウントなら Nothing）
    '
    ' 【1回の問い合わせで、Accounts とプロフィールをまとめて取る】
    ' 【読む列を選んである】Accounts からは AccountId・UserName・DisplayName・IsActive だけ。
    ' **PasswordHash・TokenGeneration・Email を、ここへ足さないこと**
    Public Shared Function FindPublicByUserName(userName As String) As PublicProfile

        If AccountStore.ValidateUserName(userName) IsNot Nothing Then
            Return Nothing
        End If

        Dim sql As String =
            "SELECT TOP (1) a.AccountId, a.UserName, a.DisplayName, " & ProfileColumns("p") &
            "  FROM Accounts AS a " &
            "  LEFT JOIN AccountProfiles AS p ON p.AccountId = a.AccountId " &
            " WHERE a.UserName = @Name AND a.IsActive = 1;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, AccountStore.UserNameMaxLength).Value = userName

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Dim accountId As Integer = reader.GetInt32(0)

                    ' 【プロフィールの行がまだ無いこともある】LEFT JOIN なので、そのときは全部 NULL。
                    ' 空の器を返して、呼ぶ側に分岐を作らせない
                    Dim profile As ProfileRecord

                    If reader.IsDBNull(3) Then
                        profile = New ProfileRecord With {.AccountId = accountId}
                    Else
                        profile = ReadRecord(reader, 3)
                    End If

                    Return New PublicProfile With {
                        .AccountId = accountId,
                        .UserName = reader.GetString(1),
                        .DisplayName = reader.GetString(2),
                        .Profile = profile}

                End Using

            End Using
        End Using

    End Function


    ' ============================================================
    ' 書く
    ' ============================================================

    ' プロフィールを保存する（行が無ければ作る）
    '
    ' 【画像の2列は触らない】（このクラスの先頭のコメント）
    ' 【ブログのタイトルと説明の2列も触らない】（ブログの設定の画面へ移した＝下の SaveBlog）。
    ' 触ると、プロフィールを保存するたびに、ブログの設定で決めたタイトルと説明が空で上書きされる
    '
    ' 【UPDATE してから、0行なら INSERT】MERGE は使っていない。
    ' 1つのトランザクションの中で、UPDLOCK, HOLDLOCK を掛けてから見るので、
    ' ほぼ同時に2回押されても、行が2つできることはない（主キーでも止まる）
    Public Shared Sub Save(record As ProfileRecord)

        Dim sql As String =
            "UPDATE AccountProfiles WITH (UPDLOCK, HOLDLOCK) SET " &
            "       Introduction = @Introduction, IntroductionVisibility = @IntroductionVis, " &
            "       Gender = @Gender, GenderVisibility = @GenderVis, " &
            "       BirthDate = @BirthDate, BirthDateMode = @BirthDateMode, BirthDateVisibility = @BirthDateVis, " &
            "       BloodType = @BloodType, BloodTypeVisibility = @BloodTypeVis, " &
            "       Hometown = @Hometown, HometownVisibility = @HometownVis, " &
            "       Residence = @Residence, ResidenceVisibility = @ResidenceVis, " &
            "       RelationshipStatus = @Relationship, RelationshipStatusVisibility = @RelationshipVis, " &
            "       Occupation = @Occupation, OccupationText = @OccupationText, " &
            "       OccupationVisibility = @OccupationVis, " &
            "       XUserName = @XName, XVisibility = @XVis, " &
            "       FacebookUserName = @FbName, FacebookVisibility = @FbVis, " &
            "       InstagramUserName = @IgName, InstagramVisibility = @IgVis, " &
            "       AvatarLetter = @AvatarLetter, AvatarColor = @AvatarColor, " &
            "       UpdatedAt = SYSDATETIME() " &
            " WHERE AccountId = @Id; " &
            "IF @@ROWCOUNT = 0 " &
            "INSERT INTO AccountProfiles (AccountId, Introduction, IntroductionVisibility, " &
            "       Gender, GenderVisibility, BirthDate, BirthDateMode, BirthDateVisibility, BloodType, BloodTypeVisibility, " &
            "       Hometown, HometownVisibility, Residence, ResidenceVisibility, " &
            "       RelationshipStatus, RelationshipStatusVisibility, " &
            "       Occupation, OccupationText, OccupationVisibility, " &
            "       XUserName, XVisibility, FacebookUserName, FacebookVisibility, " &
            "       InstagramUserName, InstagramVisibility, " &
            "       AvatarLetter, AvatarColor) " &
            "VALUES (@Id, @Introduction, @IntroductionVis, " &
            "       @Gender, @GenderVis, @BirthDate, @BirthDateMode, @BirthDateVis, @BloodType, @BloodTypeVis, " &
            "       @Hometown, @HometownVis, @Residence, @ResidenceVis, " &
            "       @Relationship, @RelationshipVis, " &
            "       @Occupation, @OccupationText, @OccupationVis, " &
            "       @XName, @XVis, @FbName, @FbVis, @IgName, @IgVis, " &
            "       @AvatarLetter, @AvatarColor);"

        Using conn As New SqlConnection(ConnectionString())
            conn.Open()

            Using tx As SqlTransaction = conn.BeginTransaction()
                Using cmd As New SqlCommand(sql, conn, tx)

                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = record.AccountId

                    AddText(cmd, "@Introduction", record.Introduction, AccountProfileRules.IntroductionMaxLength)
                    cmd.Parameters.Add("@IntroductionVis", SqlDbType.TinyInt).Value = record.IntroductionVisibility

                    AddNumber(cmd, "@Gender", record.Gender)
                    cmd.Parameters.Add("@GenderVis", SqlDbType.TinyInt).Value = record.GenderVisibility

                    AddDate(cmd, "@BirthDate", record.BirthDate)
                    cmd.Parameters.Add("@BirthDateMode", SqlDbType.TinyInt).Value = record.BirthDateMode
                    cmd.Parameters.Add("@BirthDateVis", SqlDbType.TinyInt).Value = record.BirthDateVisibility

                    AddNumber(cmd, "@BloodType", record.BloodType)
                    cmd.Parameters.Add("@BloodTypeVis", SqlDbType.TinyInt).Value = record.BloodTypeVisibility

                    AddNumber(cmd, "@Hometown", record.Hometown)
                    cmd.Parameters.Add("@HometownVis", SqlDbType.TinyInt).Value = record.HometownVisibility

                    AddNumber(cmd, "@Residence", record.Residence)
                    cmd.Parameters.Add("@ResidenceVis", SqlDbType.TinyInt).Value = record.ResidenceVisibility

                    AddNumber(cmd, "@Relationship", record.RelationshipStatus)
                    cmd.Parameters.Add("@RelationshipVis", SqlDbType.TinyInt).Value =
                        record.RelationshipStatusVisibility

                    AddNumber(cmd, "@Occupation", record.Occupation)
                    AddText(cmd, "@OccupationText", record.OccupationText,
                            AccountProfileRules.OccupationTextMaxLength)
                    cmd.Parameters.Add("@OccupationVis", SqlDbType.TinyInt).Value = record.OccupationVisibility

                    AddText(cmd, "@XName", record.XUserName, AccountProfileRules.XNameMaxLength)
                    cmd.Parameters.Add("@XVis", SqlDbType.TinyInt).Value = record.XVisibility

                    AddText(cmd, "@FbName", record.FacebookUserName, AccountProfileRules.FacebookNameMaxLength)
                    cmd.Parameters.Add("@FbVis", SqlDbType.TinyInt).Value = record.FacebookVisibility

                    AddText(cmd, "@IgName", record.InstagramUserName, AccountProfileRules.InstagramNameMaxLength)
                    cmd.Parameters.Add("@IgVis", SqlDbType.TinyInt).Value = record.InstagramVisibility

                    ' 【空（Nothing）なら NULL＝最初の文字・色のまま】AddText が DBNull にする
                    AddText(cmd, "@AvatarLetter", record.AvatarLetter, AccountAvatar.LetterMaxChars)

                    Dim colorParam As SqlParameter = cmd.Parameters.Add("@AvatarColor", SqlDbType.Char, 7)
                    colorParam.Value = If(String.IsNullOrEmpty(record.AvatarColor), CObj(DBNull.Value), CObj(record.AvatarColor))

                    cmd.ExecuteNonQuery()

                End Using

                tx.Commit()

            End Using
        End Using

        ' 【右上の角と「新しいメンバー」の枠の控えを捨てる】文字と色が、ここで変わりうる（Accounts/AccountAvatar.vb）
        AccountAvatar.Invalidate(record.AccountId)
        NewMembers.Invalidate()

    End Sub


    ' ブログのタイトルと説明だけを保存する（行が無ければ作る。ブログの設定の画面）
    '
    ' 【プロフィールのほかの列には触らない】Save と逆（あちらはこの2列に触らない）。
    ' 2つの画面が同じ行の別々の列を持つので、どちらで保存しても、もう片方で決めたものは消えない。
    ' 【確かめは呼ぶ側】AccountProfileRules.PrepareBlog を通してから渡すこと。
    ' 【UPDATE してから、0行なら INSERT】Save と同じ形（UPDLOCK, HOLDLOCK で、ほぼ同時の2回でも行が2つにならない）
    Public Shared Sub SaveBlog(accountId As Integer, blogTitle As String, blogDescription As String)

        Dim sql As String =
            "UPDATE AccountProfiles WITH (UPDLOCK, HOLDLOCK) SET " &
            "       BlogTitle = @BlogTitle, BlogDescription = @BlogDescription, UpdatedAt = SYSDATETIME() " &
            " WHERE AccountId = @Id; " &
            "IF @@ROWCOUNT = 0 " &
            "INSERT INTO AccountProfiles (AccountId, BlogTitle, BlogDescription) " &
            "VALUES (@Id, @BlogTitle, @BlogDescription);"

        Using conn As New SqlConnection(ConnectionString())
            conn.Open()

            Using tx As SqlTransaction = conn.BeginTransaction()
                Using cmd As New SqlCommand(sql, conn, tx)

                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                    AddText(cmd, "@BlogTitle", blogTitle, AccountProfileRules.BlogTitleMaxLength)
                    AddText(cmd, "@BlogDescription", blogDescription, AccountProfileRules.BlogDescriptionMaxLength)

                    cmd.ExecuteNonQuery()

                End Using

                tx.Commit()

            End Using
        End Using

    End Sub


    ' 画像の名前を書き換える。**戻り値は、それまで入っていた名前**（無ければ Nothing）
    '
    ' 【古い名前を返す理由】呼ぶ側（AccountProfileImages）が、書き換えが済んでから
    ' 古いファイルを消せるようにするため。**先にファイルを消してはいけない**
    ' ——書き換えに失敗したら、DB が指しているファイルだけが消えて、絵の出ない画像になる
    '
    ' 【列の名前は、決まった2つからしか選べない】（imageKind）
    ' 画面から来た文字列を、そのまま SQL に入れる形にはしていない
    Public Shared Function UpdateImage(accountId As Integer,
                                       kind As ProfileImageKind,
                                       imageName As String) As String

        Dim column As String

        Select Case kind
            Case ProfileImageKind.Avatar
                column = "AvatarImageName"
            Case ProfileImageKind.Cover
                column = "CoverImageName"
            Case Else
                Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select

        Dim sql As String =
            "UPDATE AccountProfiles WITH (UPDLOCK, HOLDLOCK) " &
            "   SET " & column & " = @Name, UpdatedAt = SYSDATETIME() " &
            "OUTPUT deleted." & column & " " &
            " WHERE AccountId = @Id; " &
            "IF @@ROWCOUNT = 0 " &
            "INSERT INTO AccountProfiles (AccountId, " & column & ") VALUES (@Id, @Name);"

        Using conn As New SqlConnection(ConnectionString())
            conn.Open()

            Using tx As SqlTransaction = conn.BeginTransaction()
                Using cmd As New SqlCommand(sql, conn, tx)

                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                    AddText(cmd, "@Name", imageName, 60)

                    Dim old As Object = cmd.ExecuteScalar()

                    tx.Commit()

                    ' 【右上の角の控えを捨てる】写真が変わった（カバーでも捨てるが、害は無い）
                    AccountAvatar.Invalidate(accountId)

                    If old Is Nothing OrElse Convert.IsDBNull(old) Then
                        Return Nothing
                    End If

                    Return CStr(old)

                End Using
            End Using
        End Using

    End Function


    ' ============================================================
    ' 中身の読み書き（共通）
    ' ============================================================

    ' 【読む列と、その並びは、この2つだけで決める】
    ' ReadRecord の番号と必ずそろえること（1か所で持つので、ずれようが無い）
    Private Shared ReadOnly Property SelectColumns As String
        Get
            Return "SELECT " & ProfileColumns("")
        End Get
    End Property

    Private Shared Function ProfileColumns(prefix As String) As String

        Dim p As String = If(String.IsNullOrEmpty(prefix), "", prefix & ".")

        Return p & "AccountId, " & p & "AvatarImageName, " & p & "CoverImageName, " &
               p & "Introduction, " & p & "IntroductionVisibility, " &
               p & "Gender, " & p & "GenderVisibility, " &
               p & "BirthDate, " & p & "BirthDateMode, " &
               p & "BloodType, " & p & "BloodTypeVisibility, " &
               p & "Hometown, " & p & "HometownVisibility, " &
               p & "Residence, " & p & "ResidenceVisibility, " &
               p & "RelationshipStatus, " & p & "RelationshipStatusVisibility, " &
               p & "Occupation, " & p & "OccupationText, " & p & "OccupationVisibility, " &
               p & "XUserName, " & p & "XVisibility, " &
               p & "FacebookUserName, " & p & "FacebookVisibility, " &
               p & "InstagramUserName, " & p & "InstagramVisibility, " &
               p & "BlogTitle, " & p & "BlogDescription, " &
               p & "AvatarLetter, " & p & "AvatarColor, " &
               p & "BirthDateVisibility"

    End Function


    ' offset … 先頭の列が、読み出しの何番目から始まるか（FindPublicByUserName は3列ぶん前に置いている）
    Private Shared Function ReadRecord(reader As SqlDataReader, Optional offset As Integer = 0) As ProfileRecord

        Dim record As New ProfileRecord With {
            .AccountId = reader.GetInt32(offset),
            .AvatarImageName = TextOf(reader, offset + 1),
            .CoverImageName = TextOf(reader, offset + 2),
            .Introduction = TextOf(reader, offset + 3),
            .IntroductionVisibility = ByteOf(reader, offset + 4),
            .Gender = NumberOf(reader, offset + 5),
            .GenderVisibility = ByteOf(reader, offset + 6),
            .BirthDate = DateOf(reader, offset + 7),
            .BirthDateMode = ByteOf(reader, offset + 8),
            .BloodType = NumberOf(reader, offset + 9),
            .BloodTypeVisibility = ByteOf(reader, offset + 10),
            .Hometown = NumberOf(reader, offset + 11),
            .HometownVisibility = ByteOf(reader, offset + 12),
            .Residence = NumberOf(reader, offset + 13),
            .ResidenceVisibility = ByteOf(reader, offset + 14),
            .RelationshipStatus = NumberOf(reader, offset + 15),
            .RelationshipStatusVisibility = ByteOf(reader, offset + 16),
            .Occupation = NumberOf(reader, offset + 17),
            .OccupationText = TextOf(reader, offset + 18),
            .OccupationVisibility = ByteOf(reader, offset + 19),
            .XUserName = TextOf(reader, offset + 20),
            .XVisibility = ByteOf(reader, offset + 21),
            .FacebookUserName = TextOf(reader, offset + 22),
            .FacebookVisibility = ByteOf(reader, offset + 23),
            .InstagramUserName = TextOf(reader, offset + 24),
            .InstagramVisibility = ByteOf(reader, offset + 25),
            .BlogTitle = TextOf(reader, offset + 26),
            .BlogDescription = TextOf(reader, offset + 27),
            .AvatarLetter = TextOf(reader, offset + 28),
            .AvatarColor = TextOf(reader, offset + 29),
            .BirthDateVisibility = ByteOf(reader, offset + 30)}
        ' 【BirthDateVisibility は列の最後に足した】途中に挟むと、上の番号が全部ずれるため

        ' 【古い「出さない」（BirthDateMode = 10）は、「非公開＋年月日」として読む】
        ' DB の移し替え（古い値 10 を新しい形へ）は流してあるが、
        ' SQL を流してから新しいコードが届くまでの間に、古いコードが 10 を書いた行がありうるため
        If record.BirthDateMode = ProfileChoices.BirthHidden Then
            record.BirthDateMode = ProfileChoices.BirthFull
            record.BirthDateVisibility = ProfileChoices.VisibilityPrivate
        End If

        Return record

    End Function


    Private Shared Function TextOf(reader As SqlDataReader, index As Integer) As String
        Return If(reader.IsDBNull(index), Nothing, reader.GetString(index))
    End Function

    Private Shared Function ByteOf(reader As SqlDataReader, index As Integer) As Integer
        Return If(reader.IsDBNull(index), ProfileChoices.VisibilityPublic, CInt(reader.GetByte(index)))
    End Function

    Private Shared Function NumberOf(reader As SqlDataReader, index As Integer) As Integer?
        Return If(reader.IsDBNull(index), CType(Nothing, Integer?), CType(CInt(reader.GetByte(index)), Integer?))
    End Function

    Private Shared Function DateOf(reader As SqlDataReader, index As Integer) As Date?
        Return If(reader.IsDBNull(index), CType(Nothing, Date?), CType(reader.GetDateTime(index), Date?))
    End Function


    ' 【Nothing は DBNull で入れる】空文字を入れると、「入れていない」と「空を入れた」の2つの状態ができる
    Private Shared Sub AddText(cmd As SqlCommand, name As String, value As String, size As Integer)

        Dim p As SqlParameter = cmd.Parameters.Add(name, SqlDbType.NVarChar, size)

        p.Value = If(String.IsNullOrEmpty(value), CObj(DBNull.Value), CObj(value))

    End Sub

    Private Shared Sub AddNumber(cmd As SqlCommand, name As String, value As Integer?)

        Dim p As SqlParameter = cmd.Parameters.Add(name, SqlDbType.TinyInt)

        p.Value = If(value.HasValue, CObj(CByte(value.Value)), CObj(DBNull.Value))

    End Sub

    Private Shared Sub AddDate(cmd As SqlCommand, name As String, value As Date?)

        Dim p As SqlParameter = cmd.Parameters.Add(name, SqlDbType.Date)

        p.Value = If(value.HasValue, CObj(value.Value.Date), CObj(DBNull.Value))

    End Sub


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class


' プロフィールの画像は2枚だけ（ブログ型の①）
'
' 【文字列ではなく列挙型にしてある】受け口（Accounts/UploadProfileImage.aspx）は
' 画面から「どちらの画像か」を受け取る。文字列のまま持ち回ると、
' その文字列が SQL の列名や、ファイルの置き場所の組み立てに紛れ込みうる
Public Enum ProfileImageKind
    Avatar
    Cover
End Enum
