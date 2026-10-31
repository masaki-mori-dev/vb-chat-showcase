Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Text.RegularExpressions

' アカウントの表（Accounts）への出入りと、ユーザー名・メールアドレスの決まり
'
' 【SQL をここ1か所に閉じ込めてある】
' Chat/ChatLog.vb が「ログの書き込みを1か所へ閉じ込めて、呼び出し側を変えずに差し替えられるように」
' しているのと同じ考え方。近い将来 C#／ASP.NET Core MVC へ移るとき、
' 画面は全部書き直しになるが、ここは中身を入れ替えるだけで移せる。
'
' 【DBが読めないときは、例外を投げる（フェイルクローズ）】
' SiteSettings はフェイルオープン（既定値で通す）だが、こちらは逆である。
' 「DBが読めないので、とりあえず通す」は、そのままログインを素通りさせることになる。
' 受け取った側（Accounts/AccountSession.vb）が握って、利用者には
' 「混み合っています」とだけ出し、ErrorReport で知らせる。
Public NotInheritable Class AccountStore

    Private Sub New()
    End Sub

    ' 接続文字列の名前（ChatLog.vb など、ほかの箇所と揃えている）
    Private Const ConnectionName As String = "VbChatConnection"


    ' 1行ぶんのアカウント
    '
    ' 【PasswordHash も持っている】照合に要るため。
    ' **画面へ渡すときは、このまま渡さないこと**（Accounts/Home.aspx は UserName と DisplayName だけを読む）
    Public Class AccountRecord
        Public Property AccountId As Integer
        Public Property UserName As String
        Public Property DisplayName As String
        Public Property Email As String
        Public Property PasswordHash As String
        Public Property RoleKey As String
        Public Property TokenGeneration As Integer
        Public Property IsActive As Boolean
    End Class


    ' ============================================================
    ' ユーザー名・メールアドレスの決まり
    ' ============================================================
    '
    ' 【なぜ、この4つなのか】（「記事の追加・編集機能」の章）
    '   ① ユーザー名に「@」を使わせない … 欄が1つなので、「@ が在ればメール」で振り分けられる
    '   ② メールもユーザー名も、重複させない … どちらで引いても1人に決まるようにするため
    '   ③ 失敗したときは「どちらが違うか」を言わない … アカウントの有無を外から探られないため
    '   ④ メールアドレスは必須 … パスワードの再設定に要る

    Public Const UserNameMinLength As Integer = 3
    Public Const UserNameMaxLength As Integer = 30
    Public Const DisplayNameMaxLength As Integer = 50
    Public Const EmailMaxLength As Integer = 256


    ' ユーザー名に使える文字
    '
    ' 【英数字とハイフン・アンダースコアだけ】
    ' この値は**記事のURL（/articles/{user}/{id}）にそのまま出る**ので、
    ' URLで意味を持つ文字（/ ? # % &）も、エンコードが要る日本語も入れない。
    ' 「@」が入らないのは、この一覧に無いことの結果でもある（①は二重に守られている）
    Private Shared ReadOnly UserNamePattern As New Regex("^[A-Za-z0-9_-]+$", RegexOptions.Compiled)

    ' メールアドレスの形（ざっくり。厳密な検査はしない）
    '
    ' 【厳密にしない理由】RFC どおりに検査しても「そのアドレスが届くか」は分からない。
    ' 本当に確かめたいなら確認のメールを送るしかない（それは将来やる）。
    ' ここで見るのは「@ が1つあって、前後に何か在って、空白が無い」だけ
    Private Shared ReadOnly EmailPattern As New Regex("^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled)


    ' ユーザー名の検査。戻り値は断る理由（受け付けてよければ Nothing）
    Public Shared Function ValidateUserName(userName As String) As String

        Dim trimmed As String = If(userName, "").Trim()

        If trimmed.Length = 0 Then
            Return "アカウントIDを入力してください。"
        End If

        If trimmed.Length < UserNameMinLength OrElse trimmed.Length > UserNameMaxLength Then
            Return "アカウントIDは" & UserNameMinLength & "〜" & UserNameMaxLength & "文字にしてください。"
        End If

        If Not UserNamePattern.IsMatch(trimmed) Then
            Return "アカウントIDは、半角の英数字とハイフン・アンダースコアだけで入力してください（「@」は使えません）。"
        End If

        Return Nothing

    End Function


    ' メールアドレスの検査。戻り値は断る理由（受け付けてよければ Nothing）
    Public Shared Function ValidateEmail(email As String) As String

        Dim trimmed As String = If(email, "").Trim()

        If trimmed.Length = 0 Then
            Return "メールアドレスを入力してください。"
        End If

        If trimmed.Length > EmailMaxLength Then
            Return "メールアドレスは" & EmailMaxLength & "文字以内にしてください。"
        End If

        If Not EmailPattern.IsMatch(trimmed) Then
            Return "メールアドレスの形が正しくありません。"
        End If

        Return Nothing

    End Function


    ' ログインの欄に打たれた文字列を整える（前後の空白を落とすだけ）
    Public Shared Function NormalizeLoginId(loginId As String) As String
        Return If(loginId, "").Trim()
    End Function


    ' 打たれた文字列は、メールアドレスのつもりか
    '
    ' 【「@ が在ればメール」】ユーザー名に「@」を入れさせないので、これで足りる（上の①）
    Public Shared Function LooksLikeEmail(loginId As String) As Boolean
        Return Not String.IsNullOrEmpty(loginId) AndAlso loginId.IndexOf("@"c) >= 0
    End Function


    ' ============================================================
    ' DB
    ' ============================================================

    ' ユーザー名またはメールアドレスで、1行引く（無ければ Nothing）
    '
    ' 【止めているアカウント（IsActive = 0）も返す】
    ' ここで落とすと「止めているアカウントだから入れない」のか
    ' 「そんなアカウントは無い」のかを、呼び出し側が区別できてしまう。
    ' **どちらも同じ文言で断る**のは Accounts/AccountSession.vb の仕事なので、行はそのまま渡す
    Public Shared Function FindForLogin(loginId As String) As AccountRecord

        Dim byEmail As Boolean = LooksLikeEmail(loginId)

        Dim sql As String =
            "SELECT TOP (1) AccountId, UserName, DisplayName, Email, PasswordHash, RoleKey, " &
            "       TokenGeneration, IsActive " &
            "  FROM Accounts " &
            " WHERE " & If(byEmail, "Email = @Id", "UserName = @Id") & ";"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Id", SqlDbType.NVarChar,
                                   If(byEmail, EmailMaxLength, UserNameMaxLength)).Value = loginId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)
                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Return ReadRecord(reader)
                End Using

            End Using
        End Using

    End Function


    ' 記事の公開ページ（/articles/{user}）のために、ユーザー名で有効なアカウントを引く（無い・止めている → Nothing）
    '
    '
    ' 【中身は FindForLogin】ユーザー名の決まりに「@」が無いので、必ずユーザー名で引かれる。
    ' 止めているアカウントは、ここで落とす（公開ページには出さない）。
    ' 照合順序が大文字小文字を区別しないので、正しい綴りは戻り値の UserName を見ること
    Public Shared Function FindActiveByUserName(userName As String) As AccountRecord

        If ValidateUserName(userName) IsNot Nothing Then
            Return Nothing
        End If

        Dim account As AccountRecord = FindForLogin(userName)

        If account Is Nothing OrElse Not account.IsActive Then
            Return Nothing
        End If

        Return account

    End Function


    ' アカウントIDで、1行引く（無ければ Nothing）
    '
    ' 【ログインの札を確かめるたびに呼ばれる】
    ' 主キーで1行だけなので軽いが、**アカウントのページを開くたびにDBを1回引く**ことになる。
    ' キャッシュしていないのは、止めた（IsActive = 0）・世代を進めたアカウントを
    ' **その場で締め出せるようにしておきたい**から。
    ' 記事を書く画面は管理人しか開かないので、この1回は払ってよい
    Public Shared Function FindById(accountId As Integer) As AccountRecord

        Dim sql As String =
            "SELECT TOP (1) AccountId, UserName, DisplayName, Email, PasswordHash, RoleKey, " &
            "       TokenGeneration, IsActive " &
            "  FROM Accounts WHERE AccountId = @Id;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)
                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Return ReadRecord(reader)
                End Using

            End Using
        End Using

    End Function


    ' アカウントが1つでもあるか
    '
    ' 【「最初の管理人を作る画面」を開いてよいかの判定に使う】
    ' ただし**これだけで INSERT を守ってはいけない**（下の CreateFirstAdministrator）
    Public Shared Function AnyAccountExists() As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("SELECT TOP (1) 1 FROM Accounts;", conn)

                conn.Open()

                Return cmd.ExecuteScalar() IsNot Nothing

            End Using
        End Using

    End Function


    ' 最初の管理人を作る
    '
    ' 戻り値 … 作れたら True、作れなかったら False（＝もうアカウントが在る）
    '
    ' 【「0件か」の確認と INSERT を、1つの文の中でやっている】
    ' 分けると、2回押されたときに管理人が2人できる。
    ' **部屋を作るとき（Lounge.CreateRoom の「もう1部屋持っていないか」）と、まったく同じ罠である。**
    ' WITH (UPDLOCK, HOLDLOCK) を付けてあるのも同じ理由で、
    ' ほぼ同時に来た2つが、どちらも「まだ0件」と読むのを止めるため
    '
    ' 【重複は、DB の一意索引でも止まる】
    ' この画面は0件のときしか開かないので重複は起きないが、
    ' 索引は将来（MVC版の登録画面）の守りとして先に張ってある
    Public Shared Function CreateFirstAdministrator(userName As String, displayName As String,
                                                    email As String, passwordHash As String) As Boolean

        Dim sql As String =
            "INSERT INTO Accounts (UserName, DisplayName, Email, PasswordHash, RoleKey) " &
            "SELECT @UserName, @DisplayName, @Email, @Hash, @Role " &
            " WHERE NOT EXISTS (SELECT 1 FROM Accounts WITH (UPDLOCK, HOLDLOCK));"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, UserNameMaxLength).Value = userName
                cmd.Parameters.Add("@DisplayName", SqlDbType.NVarChar, DisplayNameMaxLength).Value = displayName
                cmd.Parameters.Add("@Email", SqlDbType.NVarChar, EmailMaxLength).Value = email
                cmd.Parameters.Add("@Hash", SqlDbType.NVarChar, 256).Value = passwordHash
                cmd.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = RoleKeys.Administrator

                conn.Open()

                Return cmd.ExecuteNonQuery() = 1

            End Using
        End Using

    End Function


    ' 最後にログインした時刻を記録する（記録だけ。失敗してもログインは止めない）
    Public Shared Sub TouchLastLogin(accountId As Integer)

        Try

            Using conn As New SqlConnection(ConnectionString())
                Using cmd As New SqlCommand(
                    "UPDATE Accounts SET LastLoginAt = SYSDATETIME() WHERE AccountId = @Id;", conn)

                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId

                    conn.Open()
                    cmd.ExecuteNonQuery()

                End Using
            End Using

        Catch ex As Exception

            ' 【ログインそのものは止めない】記録が1行書けないことより、
            ' ログインできないことのほうが深刻である（ChatLog.Write と同じ判断）。
            ' 握りつぶしたままにしないよう、知らせる口は付けてある
            ErrorReport.Report(ex, "AccountStore.TouchLastLogin")

        End Try

    End Sub


    ' ============================================================
    ' アカウント設定
    ' ============================================================
    '
    ' 【何を確かめてから呼ぶか】は Accounts/AccountSettings.vb が決める（今のパスワード・形の検査）。
    ' ここは書き換えるだけ。DB が読めなければ例外を投げる（呼び出し側が握って知らせる）。
    ' **ユーザー名を書き換える口は作らない**（記事の URL に使うので、変えると記事の URL が全部変わる）

    ' 表示名を書き換える。戻り値 … 書き換えたら True（行が無ければ False）
    Public Shared Function UpdateDisplayName(accountId As Integer, displayName As String) As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "UPDATE Accounts SET DisplayName = @Name WHERE AccountId = @Id;", conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, DisplayNameMaxLength).Value = displayName

                conn.Open()

                Return cmd.ExecuteNonQuery() = 1

            End Using
        End Using

    End Function


    ' メールアドレスを書き換えた結果
    Public Enum EmailUpdateResult
        Updated
        ' ほかのアカウントが同じアドレスを使っている
        Taken
        ' そのアカウントが無い
        NotFound
    End Enum

    ' メールアドレスを書き換える
    '
    ' 【「ほかのアカウントが使っていないか」を、書き換える文の中で確かめる】
    ' 読んでから書く2段にすると、その間に同じアドレスで別のアカウントが作られたとき（MVC版の登録画面）、
    ' 一意索引に当たって例外になる。WITH (UPDLOCK, HOLDLOCK) は、最初の管理人を作るときと同じ理由
    Public Shared Function UpdateEmail(accountId As Integer, email As String) As EmailUpdateResult

        Dim sql As String =
            "DECLARE @n INT; " &
            "UPDATE Accounts SET Email = @Email " &
            " WHERE AccountId = @Id " &
            "   AND NOT EXISTS (SELECT 1 FROM Accounts AS o WITH (UPDLOCK, HOLDLOCK) " &
            "                    WHERE o.Email = @Email AND o.AccountId <> @Id); " &
            "SET @n = @@ROWCOUNT; " &
            "IF @n = 1 DELETE FROM AccountPasswordResets WHERE AccountId = @Id; " &
            "SELECT @n, " &
            "       (SELECT COUNT(*) FROM Accounts WHERE AccountId = @Id);"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Email", SqlDbType.NVarChar, EmailMaxLength).Value = email

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return EmailUpdateResult.NotFound
                    End If

                    If reader.GetInt32(0) = 1 Then
                        Return EmailUpdateResult.Updated
                    End If

                    Return If(reader.GetInt32(1) = 0, EmailUpdateResult.NotFound, EmailUpdateResult.Taken)

                End Using

            End Using
        End Using

    End Function


    ' パスワードを書き換え、鍵の世代を1つ進める
    '
    ' 戻り値 … 新しい世代（書き換えられなかったら -1）
    '
    ' 【出してある再設定の合言葉も、同じ文で消す】パスワードが変わったら、前に送ったリンクは要らない
    '
    ' 【世代を条件に入れている】（WHERE TokenGeneration = @Old）
    ' ほぼ同時に2つの端末から変えたとき、あとのほうは0行になって断られる
    ' （前の変更を黙って上書きしない）。**世代を進める＝それまでのログインの札は、全部その場で無効になる**
    Public Shared Function UpdatePassword(accountId As Integer, currentGeneration As Integer,
                                          passwordHash As String) As Integer

        Dim sql As String =
            "UPDATE Accounts " &
            "   SET PasswordHash = @Hash, TokenGeneration = TokenGeneration + 1 " &
            "OUTPUT inserted.TokenGeneration " &
            " WHERE AccountId = @Id AND TokenGeneration = @Old; " &
            "IF @@ROWCOUNT = 1 DELETE FROM AccountPasswordResets WHERE AccountId = @Id;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Old", SqlDbType.Int).Value = currentGeneration
                cmd.Parameters.Add("@Hash", SqlDbType.NVarChar, 256).Value = passwordHash

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                If value Is Nothing OrElse Convert.IsDBNull(value) Then
                    Return -1
                End If

                Return CInt(value)

            End Using
        End Using

    End Function


    ' ============================================================
    ' パスワードの再設定の合言葉
    ' ============================================================
    '
    ' 表は AccountPasswordResets（1アカウントに1行。合言葉そのものは置かず、SHA-256 だけ）。
    ' 何を確かめてから呼ぶかは Accounts/AccountReset.vb。

    ' 合言葉を出す（同じアカウントの前の合言葉は上書き＝使えなくなる）
    '
    ' 戻り値 … 書いたら True。**前に出してから minInterval たっていなければ、書かずに False**
    ' （同じアカウントへは数分に1通まで。確かめと書き込みを1つの文でやる＝2回同時に来ても1通だけ）
    Public Shared Function SaveResetToken(accountId As Integer, tokenHash As Byte(),
                                          lifetime As TimeSpan, minInterval As TimeSpan) As Boolean

        Dim sql As String =
            "DECLARE @now DATETIME2(3) = SYSDATETIME(); " &
            "UPDATE AccountPasswordResets WITH (UPDLOCK, HOLDLOCK) " &
            "   SET TokenHash = @Hash, RequestedAt = @now, ExpiresAt = DATEADD(SECOND, @Life, @now) " &
            " WHERE AccountId = @Id AND RequestedAt <= DATEADD(SECOND, -@Gap, @now); " &
            "IF @@ROWCOUNT = 1 BEGIN SELECT 1; RETURN; END " &
            "IF EXISTS (SELECT 1 FROM AccountPasswordResets WITH (UPDLOCK, HOLDLOCK) WHERE AccountId = @Id) " &
            "BEGIN SELECT 0; RETURN; END " &
            "INSERT INTO AccountPasswordResets (AccountId, TokenHash, RequestedAt, ExpiresAt) " &
            "VALUES (@Id, @Hash, @now, DATEADD(SECOND, @Life, @now)); " &
            "SELECT 1;"

        Using conn As New SqlConnection(ConnectionString())
            conn.Open()
            Using tx As SqlTransaction = conn.BeginTransaction()
                Using cmd As New SqlCommand(sql, conn, tx)

                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                    cmd.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = tokenHash
                    cmd.Parameters.Add("@Life", SqlDbType.Int).Value = CInt(lifetime.TotalSeconds)
                    cmd.Parameters.Add("@Gap", SqlDbType.Int).Value = CInt(minInterval.TotalSeconds)

                    Dim written As Boolean = (CInt(cmd.ExecuteScalar()) = 1)
                    tx.Commit()

                    Return written

                End Using
            End Using
        End Using

    End Function


    ' 合言葉が、今も使えるか（使えればアカウントの番号、使えなければ 0）。**消さない**（リンクを開いたときの画面の出し分け）
    Public Shared Function FindResetAccountId(tokenHash As Byte()) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (1) AccountId FROM AccountPasswordResets " &
                " WHERE TokenHash = @Hash AND ExpiresAt > SYSDATETIME();", conn)

                cmd.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = tokenHash

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                Return If(value Is Nothing OrElse Convert.IsDBNull(value), 0, CInt(value))

            End Using
        End Using

    End Function


    ' 合言葉を使う（使えればアカウントの番号、使えなければ 0）
    '
    ' 【取り出すのと消すのを1つの文で】DELETE ... OUTPUT。2回押されても、番号が返るのは1回だけ
    Public Shared Function ConsumeResetToken(tokenHash As Byte()) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "DELETE FROM AccountPasswordResets OUTPUT deleted.AccountId " &
                " WHERE TokenHash = @Hash AND ExpiresAt > SYSDATETIME();", conn)

                cmd.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = tokenHash

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                Return If(value Is Nothing OrElse Convert.IsDBNull(value), 0, CInt(value))

            End Using
        End Using

    End Function


    ' ============================================================
    ' 一般ユーザーの登録
    ' ============================================================
    '
    ' 表は AccountRegistrations（仮登録。1アドレスに1行・合言葉は SHA-256 だけ・期限つき）。
    ' 何を確かめてから呼ぶかは Accounts/AccountRegistration.vb。

    ' ユーザー名が使えるか
    Public Enum UserNameAvailability
        ' 使える
        Available
        ' もう誰かが使っている
        Taken
        ' 退会した人が使っていた（もう誰にも使わせない）
        Retired
    End Enum


    ' ユーザー名が使えるかを、1回の問い合わせで確かめる
    '
    ' 【「使っている」と「退会した人の名前」を分けて返す理由】
    ' 画面の文言を分けたいから。どちらも「使えません」で断るが、
    ' 「もう使われています」と「その名前は使えません」では、利用者の次の一手が違う
    ' （前者は似た名前を試す、後者はきっぱり別の名前にする）。
    '
    ' 【ユーザー名の有無は、外に見せてよい】
    ' /articles/{ユーザー名} を開けば誰でも分かる、もともと公開の情報である
    ' （**メールアドレスの有無は、逆に絶対に見せない**＝AccountRegistration の
    '   「在っても無くても、同じ答え」）。
    '
    ' 【予約語（AccountUserNames）は、ここでは見ない】あちらは DB を引かずに分かるので、
    ' 呼ぶ側が先に落としている（DB へ行く前に断るほうが安い）
    Public Shared Function CheckUserName(userName As String) As UserNameAvailability

        Dim sql As String =
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM Accounts WHERE UserName = @Name) THEN 1 " &
            "            WHEN EXISTS (SELECT 1 FROM RetiredUserNames WHERE UserName = @Name) THEN 2 " &
            "            ELSE 0 END;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, UserNameMaxLength).Value = userName

                conn.Open()

                Select Case CInt(cmd.ExecuteScalar())
                    Case 1
                        Return UserNameAvailability.Taken
                    Case 2
                        Return UserNameAvailability.Retired
                    Case Else
                        Return UserNameAvailability.Available
                End Select

            End Using
        End Using

    End Function


    ' 仮登録を1行書く（同じアドレスの前の行は上書き＝前のリンクは使えなくなる）
    '
    ' 戻り値 … 書いたら True。前に書いてから minInterval たっていなければ、書かずに False
    '
    ' 【AccountStore.SaveResetToken と、まったく同じ形にしてある】
    ' 確かめと書き込みを1つの文でやる（2回同時に来ても1通だけ）。片方を直したら、もう片方も見ること
    Public Shared Function SaveRegistration(userName As String, displayName As String, email As String,
                                            passwordHash As String, tokenHash As Byte(),
                                            termsAgreedAt As DateTime, ipAddress As String,
                                            lifetime As TimeSpan, minInterval As TimeSpan) As Boolean

        Dim sql As String =
            "DECLARE @now DATETIME2(3) = SYSDATETIME(); " &
            "UPDATE AccountRegistrations WITH (UPDLOCK, HOLDLOCK) " &
            "   SET UserName = @UserName, DisplayName = @DisplayName, PasswordHash = @Hash, " &
            "       TokenHash = @Token, TermsAgreedAt = @Terms, IpAddress = @Ip, " &
            "       RequestedAt = @now, ExpiresAt = DATEADD(SECOND, @Life, @now) " &
            " WHERE Email = @Email AND RequestedAt <= DATEADD(SECOND, -@Gap, @now); " &
            "IF @@ROWCOUNT = 1 BEGIN SELECT 1; RETURN; END " &
            "IF EXISTS (SELECT 1 FROM AccountRegistrations WITH (UPDLOCK, HOLDLOCK) WHERE Email = @Email) " &
            "BEGIN SELECT 0; RETURN; END " &
            "INSERT INTO AccountRegistrations " &
            "       (UserName, DisplayName, Email, PasswordHash, TokenHash, TermsAgreedAt, IpAddress, " &
            "        RequestedAt, ExpiresAt) " &
            "VALUES (@UserName, @DisplayName, @Email, @Hash, @Token, @Terms, @Ip, " &
            "        @now, DATEADD(SECOND, @Life, @now)); " &
            "SELECT 1;"

        Using conn As New SqlConnection(ConnectionString())
            conn.Open()
            Using tx As SqlTransaction = conn.BeginTransaction()
                Using cmd As New SqlCommand(sql, conn, tx)

                    cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, UserNameMaxLength).Value = userName
                    cmd.Parameters.Add("@DisplayName", SqlDbType.NVarChar, DisplayNameMaxLength).Value = displayName
                    cmd.Parameters.Add("@Email", SqlDbType.NVarChar, EmailMaxLength).Value = email
                    cmd.Parameters.Add("@Hash", SqlDbType.NVarChar, 256).Value = passwordHash
                    cmd.Parameters.Add("@Token", SqlDbType.Binary, 32).Value = tokenHash
                    cmd.Parameters.Add("@Terms", SqlDbType.DateTime2).Value = termsAgreedAt
                    cmd.Parameters.Add("@Ip", SqlDbType.NVarChar, 45).Value =
                        If(String.IsNullOrEmpty(ipAddress), CObj(DBNull.Value), CObj(ipAddress))
                    cmd.Parameters.Add("@Life", SqlDbType.Int).Value = CInt(lifetime.TotalSeconds)
                    cmd.Parameters.Add("@Gap", SqlDbType.Int).Value = CInt(minInterval.TotalSeconds)

                    Dim written As Boolean = (CInt(cmd.ExecuteScalar()) = 1)
                    tx.Commit()

                    Return written

                End Using
            End Using
        End Using

    End Function


    ' 仮登録の1行（確認の画面に出すぶんだけ）
    '
    ' 【パスワードのハッシュは持たせない】画面に渡すものなので、要らない値は読まない
    ' （AccountRecord が PasswordHash を持っているのは、照合に要るからである）
    Public Class RegistrationRecord
        Public Property UserName As String
        Public Property DisplayName As String
        Public Property Email As String
    End Class


    ' 合言葉が今も使えるか（使えれば1行、使えなければ Nothing）。消さない
    ' （確認の画面を出すためだけ。消すのは下の CompleteRegistration）
    Public Shared Function FindRegistration(tokenHash As Byte()) As RegistrationRecord

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (1) UserName, DisplayName, Email FROM AccountRegistrations " &
                " WHERE TokenHash = @Token AND ExpiresAt > SYSDATETIME();", conn)

                cmd.Parameters.Add("@Token", SqlDbType.Binary, 32).Value = tokenHash

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Return New RegistrationRecord With {
                        .UserName = reader.GetString(0),
                        .DisplayName = reader.GetString(1),
                        .Email = reader.GetString(2)}

                End Using

            End Using
        End Using

    End Function


    ' 本登録の結果
    Public Enum RegistrationCompletion
        Created = 0
        ' 合言葉が使えない（期限切れ・もう使われた・新しく出し直した）
        LinkDead = 1
        ' 待っている間に、そのユーザー名が取られた（か、退会した人の名前になった）
        UserNameTaken = 2
        ' 待っている間に、そのメールアドレスでアカウントが作られた
        EmailTaken = 3
    End Enum

    Public Class RegistrationResult
        Public Property Completion As RegistrationCompletion
        Public Property AccountId As Integer
    End Class


    ' 本登録する（仮登録の行を取り出して消し、Accounts へ1行入れる）
    '
    ' 【1つのトランザクションの中でやる】
    ' 「読む → 空いているか確かめる → 入れる」を分けると、
    '   ・2回押されたときに、同じ人のアカウントが2つできる
    '   ・ほぼ同時に同じユーザー名で本登録した2人が、どちらも「空いている」と読む
    ' （最初のサイト管理者を作るとき・部屋を作るときと、まったく同じ罠である）。
    '
    ' 【ユーザー名の確かめが2本ある】Accounts（もう誰かが持っている）と
    ' RetiredUserNames（退会した人が持っていた）。仮登録の時点では押さえていないので、
    ' 待っている24時間のあいだに取られることがありうる（めったに無いが、ここで断れる形にしてある）。
    '
    ' 【使えなかったときも、仮登録の行は消す】もう絶対に成功しないので、残しても邪魔なだけである
    ' （利用者には「もう一度、別の名前で登録してください」と出す）。
    ' 合言葉が見つからなかったとき（LinkDead）だけは、消す行が無い
    Public Shared Function CompleteRegistration(tokenHash As Byte()) As RegistrationResult

        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @Code INT = 0, @NewId INT = 0; " &
            "DECLARE @UserName NVARCHAR(30), @DisplayName NVARCHAR(50), @Email NVARCHAR(256), " &
            "        @Hash NVARCHAR(256), @Terms DATETIME2(3); " &
            "SELECT @UserName = UserName, @DisplayName = DisplayName, @Email = Email, " &
            "       @Hash = PasswordHash, @Terms = TermsAgreedAt " &
            "  FROM AccountRegistrations WITH (UPDLOCK, HOLDLOCK) " &
            " WHERE TokenHash = @Token AND ExpiresAt > SYSDATETIME(); " &
            "IF @UserName IS NULL " &
            "    SET @Code = 1; " &
            "ELSE IF EXISTS (SELECT 1 FROM Accounts WITH (UPDLOCK, HOLDLOCK) WHERE UserName = @UserName) " &
            "     OR EXISTS (SELECT 1 FROM RetiredUserNames WITH (UPDLOCK, HOLDLOCK) WHERE UserName = @UserName) " &
            "    SET @Code = 2; " &
            "ELSE IF EXISTS (SELECT 1 FROM Accounts WITH (UPDLOCK, HOLDLOCK) WHERE Email = @Email) " &
            "    SET @Code = 3; " &
            "ELSE " &
            "BEGIN " &
            "    DECLARE @Ids TABLE (Id INT); " &
            "    INSERT INTO Accounts (UserName, DisplayName, Email, PasswordHash, RoleKey, TermsAgreedAt) " &
            "    OUTPUT INSERTED.AccountId INTO @Ids " &
            "    VALUES (@UserName, @DisplayName, @Email, @Hash, @Role, @Terms); " &
            "    SET @NewId = (SELECT TOP (1) Id FROM @Ids); " &
            "END " &
            "IF @Code <> 1 DELETE FROM AccountRegistrations WHERE TokenHash = @Token; " &
            "COMMIT; " &
            "SELECT @Code, @NewId;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Token", SqlDbType.Binary, 32).Value = tokenHash
                cmd.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = RoleKeys.General

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        ' ここへは来ない（SELECT が必ず1行返る）。来たら、使えなかった扱いにする
                        Return New RegistrationResult With {.Completion = RegistrationCompletion.LinkDead}
                    End If

                    Return New RegistrationResult With {
                        .Completion = CType(reader.GetInt32(0), RegistrationCompletion),
                        .AccountId = reader.GetInt32(1)}

                End Using

            End Using
        End Using

    End Function


    ' 期限が切れた仮登録を消す（1日1回の片付け。Core/LogCleanup.vb から呼ぶ）
    '
    ' 戻り値 … 消した行の数
    '
    ' 【まとめて1回で消す】1日に数件の表なので、区切る必要が無い
    ' （問い合わせ ContactMessages の削除と同じ判断）
    Public Shared Function DeleteExpiredRegistrations() As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "DELETE FROM AccountRegistrations WHERE ExpiresAt < SYSDATETIME();", conn)

                conn.Open()

                Return cmd.ExecuteNonQuery()

            End Using
        End Using

    End Function


    ' ============================================================
    ' 退会
    ' ============================================================

    ' アカウントを丸ごと消す（物理削除）
    '
    ' 戻り値 … 消せたら True（行が無い・世代が合わない → False）
    '
    ' 【消す順番が決まっている】外部キーが前を指しているので、後ろから消す。
    '   ⓪ ChatMessages の UserId を NULL に戻す（行は消さない。下の「ChatMessages は…」）
    '   ① ArticleDrafts（記事を消せば CASCADE で消えるが、明示しておく）
    '   ② Articles          → ArticleDrafts・ArticleImageUses が CASCADE で消える
    '   ③ ArticleImageFiles（②で ArticleImageUses が消えてから。あちらが ImageName を指している）
    '   ④ ArticleCategories（②のあと。Articles.CategoryId が指している）
    '   ⑤ AccountRegistrations（同じアドレスの仮登録が在れば、道連れに消す）
    '   ⑥ RetiredUserNames へ、ユーザー名を1行入れる（なりすまし避け。ここが要）
    '   ⑦ Accounts          → AccountProfiles・AccountPasswordResets が CASCADE で消える
    ' 順番を入れ替えると、外部キーで止まる。
    '
    ' 【全部ひとつのトランザクションでやる】途中で落ちると、
    ' 「記事だけ消えて、アカウントは残っている」という直しようのない形になる。
    '
    ' 【世代（TokenGeneration）を条件に入れている】UpdatePassword と同じ。
    ' ほかの端末でパスワードを変えた直後の「退会」は、0行になって断られる
    ' （その端末の札はもう無効なので、退会させる筋合いが無い）。
    '
    ' 【ChatMessages は、行を消さずに UserId だけ NULL に戻す】（チャットとアカウントの紐づけ）
    ' アカウントID を表示して書いた行には、UserId にアカウントの番号が入っている（ChatLog.Write）。
    ' 退会したら、その結び付きを外す（⓪）。
    '   ・**行は消さない**。消すと、ほかの人から見た会話が抜け落ちる（相手の返事だけが残る）。
    '     発言は、名前（その時点のニックネーム）だけの、ふつうの参加者の行として残る＝Cookie ユーザーの行と同じ扱い
    '   ・**アカウントID は、過去ログからも消える**。読むときに Accounts から引く作りなので（ChatLogReader）、
    '     行が無くなった時点で出なくなるが、番号を残しておく理由も無い（残すと、誰のものでもない番号がログに残る）
    '   ・外部キーは張っていない（ログの表に制約を付けない、という最初の決まり）ので、消す順番には関わらない。
    '     同じトランザクションの中に入れてあるのは、「アカウントは消えたのに、番号だけ残った」を作らないため
    '   ・索引 IX_ChatMessages_UserId で引けるので、その人の行だけを触る（ログ全体を舐めない）
    Public Shared Function DeleteAccountCompletely(accountId As Integer, currentGeneration As Integer,
                                                   userName As String, email As String) As Boolean

        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "UPDATE ChatMessages SET UserId = NULL WHERE UserId = @Id; " &
            "DELETE FROM ArticleDrafts WHERE AccountId = @Id; " &
            "DELETE FROM Articles WHERE AccountId = @Id; " &
            "DELETE FROM ArticleImageFiles WHERE AccountId = @Id; " &
            "DELETE FROM ArticleCategories WHERE AccountId = @Id; " &
            "DELETE FROM AccountRegistrations WHERE Email = @Email; " &
            "IF NOT EXISTS (SELECT 1 FROM RetiredUserNames WHERE UserName = @UserName) " &
            "    INSERT INTO RetiredUserNames (UserName) VALUES (@UserName); " &
            "DELETE FROM Accounts WHERE AccountId = @Id AND TokenGeneration = @Gen; " &
            "DECLARE @n INT = @@ROWCOUNT; " &
            "IF @n = 1 COMMIT; ELSE ROLLBACK; " &
            "SELECT @n;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Gen", SqlDbType.Int).Value = currentGeneration
                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, UserNameMaxLength).Value = userName
                cmd.Parameters.Add("@Email", SqlDbType.NVarChar, EmailMaxLength).Value = email

                conn.Open()

                Return CInt(cmd.ExecuteScalar()) = 1

            End Using
        End Using

    End Function
    ' ロールの日本語名（無ければ Nothing）。DB が読めなければ例外を投げる
    Public Shared Function FindRoleDisplayName(roleKey As String) As String

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand("SELECT TOP (1) DisplayName FROM Roles WHERE RoleKey = @Key;", conn)

                cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 20).Value = roleKey

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                If value Is Nothing OrElse Convert.IsDBNull(value) Then
                    Return Nothing
                End If

                Return CStr(value)

            End Using
        End Using

    End Function


    Private Shared Function ReadRecord(reader As SqlDataReader) As AccountRecord

        Return New AccountRecord With {
            .AccountId = reader.GetInt32(0),
            .UserName = reader.GetString(1),
            .DisplayName = reader.GetString(2),
            .Email = reader.GetString(3),
            .PasswordHash = reader.GetString(4),
            .RoleKey = reader.GetString(5),
            .TokenGeneration = reader.GetInt32(6),
            .IsActive = reader.GetBoolean(7)
        }

    End Function


    ' ============================================================
    ' 「新しいメンバー」の枠（サイト全体の右の枠）
    ' ============================================================

    ' 枠に出す1人ぶん（出すための値だけ。守る列＝メールアドレス・パスワードのハッシュは読まない）
    Public Class NewMemberRecord
        Public Property AccountId As Integer
        Public Property UserName As String
        Public Property DisplayName As String
        Public Property AvatarImageName As String
        ' 文字のアイコン（Nothing＝最初のまま。Accounts/AccountAvatar.vb）
        Public Property AvatarLetter As String
        Public Property AvatarColor As String
    End Class


    ' 新しく登録した人を、新しい順に top 人
    '
    ' 【出す人】登録を終えた人（Accounts に行がある＝本登録が済んでいる）のうち、
    '   ・止めていない（IsActive = 1）
    '   ・管理人が「紹介しない」の印を付けていない（ExcludeFromNewMembers = 0）
    '   ・サイト管理者ではない（枠は「このサイトに来てくれた人」の紹介なので、
    '     運営側を並べない。管理人は右上のメニューの「このサイトについて」と、お知らせで出ている）
    ' **記事が0本でも出す**（登録した時点でプロフィールのページがあるので、押した先が必ずある）。
    ' 退会した人は、行そのものが消えるので出ない。
    '
    ' 【読むのは Accounts と AccountProfiles の、出すための列だけ】
    ' 公開のプロフィールページ（FindPublicByUserName）と同じ考え方＝守る列を1つも読まない
    Public Shared Function GetNewMembers(top As Integer) As List(Of NewMemberRecord)

        Dim list As New List(Of NewMemberRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (@Top) a.UserName, a.DisplayName, p.AvatarImageName, " &
                "       a.AccountId, p.AvatarLetter, p.AvatarColor " &
                "  FROM Accounts a " &
                "  LEFT JOIN AccountProfiles p ON p.AccountId = a.AccountId " &
                " WHERE a.IsActive = 1 AND a.ExcludeFromNewMembers = 0 AND a.RoleKey <> @Admin " &
                " ORDER BY a.CreatedAt DESC, a.AccountId DESC;", conn)

                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top
                cmd.Parameters.Add("@Admin", SqlDbType.NVarChar, 20).Value = RoleKeys.Administrator

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(New NewMemberRecord With {
                            .UserName = reader.GetString(0),
                            .DisplayName = reader.GetString(1),
                            .AvatarImageName = If(reader.IsDBNull(2), Nothing, reader.GetString(2)),
                            .AccountId = reader.GetInt32(3),
                            .AvatarLetter = If(reader.IsDBNull(4), Nothing, reader.GetString(4)),
                            .AvatarColor = If(reader.IsDBNull(5), Nothing, reader.GetString(5))})
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class


' ロールの名前（DB の Roles.RoleKey に入る値）
'
' 【文字列を直接書かないこと】打ち間違えたロールをアカウントに入れると、
' 外部キーで弾かれるか、弾かれなければ上限を1つも引けない人ができる
' （SiteSettings が設定の名前を定数にしているのと、同じ理由）
Public NotInheritable Class RoleKeys

    Private Sub New()
    End Sub

    Public Const General As String = "general"
    Public Const Premium As String = "premium"
    Public Const Moderator As String = "moderator"
    Public Const Viewer As String = "viewer"
    Public Const Administrator As String = "administrator"

End Class
