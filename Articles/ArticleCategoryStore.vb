Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' カテゴリの表（ArticleCategories）への出入り
'
' 【SQL をここ1か所に閉じ込めてある】
' Accounts/AccountStore.vb・Chat/ChatLog.vb と同じ考え方。
' C#／ASP.NET Core MVC へ移るとき、画面は書き直しになるが、ここは中身を入れ替えるだけで移せる。
'
' 【DBが読めないときは、例外を投げる（フェイルクローズ）】
' 受け取った画面側が握って、利用者には「混み合っています」とだけ出し、ErrorReport で知らせる。
' SiteSettings（既定値で通す）とは逆である
' ——あちらが読めないとトップページまで落ちるが、ここは管理人の画面でしか読まないため。
'
' 【どの関数も、必ず accountId で絞っている】
' カテゴリはアカウントごとのもので、他人のカテゴリを引いたり書き換えたりしてはいけない。
' **CategoryId だけで UPDATE／DELETE しないこと**——番号を打ち替えるだけで他人のものを触れてしまう
' （こっそりの画像 Chat/PrivateImage.aspx が、毎回「送り主か宛先か」を確かめているのと同じ形）
Public NotInheritable Class ArticleCategoryStore

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' 1行ぶんのカテゴリ
    Public Class CategoryRecord

        Public Property CategoryId As Integer
        Public Property AccountId As Integer
        Public Property Name As String
        Public Property SortMode As ArticleRules.CategorySortMode
        Public Property SortOrder As Integer

        ' このカテゴリに入っている記事の数（一覧に出すためだけの値。表の列ではない）
        Public Property ArticleCount As Integer

    End Class


    ' このアカウントのカテゴリを、並び順で全部引く
    Public Shared Function GetAll(accountId As Integer) As List(Of CategoryRecord)

        Dim sql As String =
            "SELECT c.CategoryId, c.AccountId, c.Name, c.SortMode, c.SortOrder, " &
            "       (SELECT COUNT(*) FROM Articles a WHERE a.CategoryId = c.CategoryId AND a.DeletedAt IS NULL) AS ArticleCount " &
            "  FROM ArticleCategories c " &
            " WHERE c.AccountId = @AccountId " &
            " ORDER BY c.SortOrder ASC, c.CategoryId ASC;"

        Dim list As New List(Of CategoryRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(New CategoryRecord With {
                            .CategoryId = reader.GetInt32(0),
                            .AccountId = reader.GetInt32(1),
                            .Name = reader.GetString(2),
                            .SortMode = ArticleRules.NormalizeSortMode(CInt(reader.GetByte(3))),
                            .SortOrder = reader.GetInt32(4),
                            .ArticleCount = reader.GetInt32(5)})
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' 1つ引く（他人のものなら Nothing）
    Public Shared Function Find(accountId As Integer, categoryId As Integer) As CategoryRecord

        For Each c As CategoryRecord In GetAll(accountId)
            If c.CategoryId = categoryId Then
                Return c
            End If
        Next

        Return Nothing

    End Function


    ' 作る。戻り値は断る理由（作れたら Nothing）
    '
    ' 【名前の重複は、先に見てから入れる。そのうえでDBの一意索引でも止まる】
    ' 見るだけだと、ほとんど同時に2回押されたときに両方通る。
    ' 索引で止まったときは、同じ文言で断る（利用者から見て、どちらの経路でも同じに見える）
    ' 【名前の話ではない理由】画面は、これと見分けて「理由をどこに出すか」を決める
    ' （名前の話なら名前の欄の上、そうでなければ、その行・そのボタンのそば）
    Public Const NotFoundMessage As String = "そのカテゴリは見つかりませんでした。"

    Public Shared Function Create(accountId As Integer, name As String,
                                  sortMode As ArticleRules.CategorySortMode,
                                  sortOrder As Integer) As String

        Dim ignored As Integer
        Return Create(accountId, name, sortMode, sortOrder, ignored)

    End Function

    ' 作って、新しい番号も返す（記事を書く画面から、その場でカテゴリを作るため）。
    ' 決まり（名前の検査・同じ名前を断る）は上と同じ1本の道である
    Public Shared Function Create(accountId As Integer, name As String,
                                  sortMode As ArticleRules.CategorySortMode,
                                  sortOrder As Integer, ByRef newCategoryId As Integer) As String

        newCategoryId = 0

        Dim reason As String = ArticleRules.ValidateCategoryName(name)

        If reason IsNot Nothing Then
            Return reason
        End If

        Dim trimmed As String = name.Trim()

        Dim sql As String =
            "INSERT INTO ArticleCategories (AccountId, Name, SortMode, SortOrder) " &
            "OUTPUT inserted.CategoryId " &
            "SELECT @AccountId, @Name, @Mode, @Order " &
            " WHERE NOT EXISTS (SELECT 1 FROM ArticleCategories WITH (UPDLOCK, HOLDLOCK) " &
            "                    WHERE AccountId = @AccountId AND Name = @Name);"

        Try

            Using conn As New SqlConnection(ConnectionString())
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                    cmd.Parameters.Add("@Name", SqlDbType.NVarChar, ArticleRules.CategoryNameMaxLength).Value = trimmed
                    cmd.Parameters.Add("@Mode", SqlDbType.TinyInt).Value = CInt(sortMode)
                    cmd.Parameters.Add("@Order", SqlDbType.Int).Value = sortOrder

                    conn.Open()

                    ' 【OUTPUT で番号を受け取る】入らなかった（同じ名前があった）ときは、行が返らない
                    Dim value As Object = cmd.ExecuteScalar()

                    If value Is Nothing OrElse value Is DBNull.Value Then
                        Return "その名前のカテゴリは、すでにあります。"
                    End If

                    newCategoryId = CInt(value)

                End Using
            End Using

        Catch ex As SqlException When IsDuplicateKey(ex)

            Return "その名前のカテゴリは、すでにあります。"

        End Try

        Return Nothing

    End Function


    ' 直す。戻り値は断る理由（直せたら Nothing）
    Public Shared Function Update(accountId As Integer, categoryId As Integer, name As String,
                                  sortMode As ArticleRules.CategorySortMode,
                                  sortOrder As Integer) As String

        Dim reason As String = ArticleRules.ValidateCategoryName(name)

        If reason IsNot Nothing Then
            Return reason
        End If

        Dim trimmed As String = name.Trim()

        Dim sql As String =
            "UPDATE ArticleCategories " &
            "   SET Name = @Name, SortMode = @Mode, SortOrder = @Order, UpdatedAt = SYSDATETIME() " &
            " WHERE CategoryId = @CategoryId AND AccountId = @AccountId;"

        Try

            Using conn As New SqlConnection(ConnectionString())
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.Add("@Name", SqlDbType.NVarChar, ArticleRules.CategoryNameMaxLength).Value = trimmed
                    cmd.Parameters.Add("@Mode", SqlDbType.TinyInt).Value = CInt(sortMode)
                    cmd.Parameters.Add("@Order", SqlDbType.Int).Value = sortOrder
                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId
                    cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                    conn.Open()

                    If cmd.ExecuteNonQuery() <> 1 Then
                        Return NotFoundMessage
                    End If

                End Using
            End Using

        Catch ex As SqlException When IsDuplicateKey(ex)

            Return "その名前のカテゴリは、すでにあります。"

        End Try

        Return Nothing

    End Function


    ' 消す。戻り値は断る理由（消せたら Nothing）
    '
    ' 【記事は消さない。カテゴリだけを外す】
    ' カテゴリを消したら記事も消える、という作りにはしない
    ' （「カテゴリを整理するつもりが、記事が消えた」は取り返しがつかない）。
    '
    ' 【1つのトランザクションの中でやること】
    ' 先に記事のカテゴリを外し、そのあとカテゴリの行を消す。
    ' 途中で失敗すると「カテゴリの無い記事が残るが、カテゴリも残っている」という
    ' 中途半端な形になるので、まとめて取り消せるようにしてある。
    ' （外部キーを ON DELETE SET NULL にしていないのは、
    '   **黙って外れるより、手順として書いてあるほうが読めば分かる**ため）
    Public Shared Function Delete(accountId As Integer, categoryId As Integer) As String

        Using conn As New SqlConnection(ConnectionString())

            conn.Open()

            Using tx As SqlTransaction = conn.BeginTransaction()

                Using cmd As New SqlCommand(
                    "UPDATE Articles SET CategoryId = NULL, UpdatedAt = SYSDATETIME() " &
                    " WHERE CategoryId = @CategoryId AND AccountId = @AccountId;", conn, tx)

                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId
                    cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                    cmd.ExecuteNonQuery()

                End Using

                ' 【編集中の控え（ArticleDrafts）のカテゴリも外す】控えのカテゴリには外部キーを付けていないので、
                ' 外し忘れると、消したカテゴリの番号が控えに残る（反映するときに「そのカテゴリは見つかりませんでした」で断られる）
                Using cmd As New SqlCommand(
                    "UPDATE ArticleDrafts SET CategoryId = NULL " &
                    " WHERE CategoryId = @CategoryId AND AccountId = @AccountId;", conn, tx)

                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId
                    cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                    cmd.ExecuteNonQuery()

                End Using

                Using cmd As New SqlCommand(
                    "DELETE FROM ArticleCategories " &
                    " WHERE CategoryId = @CategoryId AND AccountId = @AccountId;", conn, tx)

                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId
                    cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                    If cmd.ExecuteNonQuery() <> 1 Then
                        tx.Rollback()
                        Return NotFoundMessage
                    End If

                End Using

                tx.Commit()

            End Using

        End Using

        Return Nothing

    End Function


    ' 一意索引に当たったか（同じ名前のカテゴリを入れようとした）
    '
    ' 2601 … 一意索引に重複を入れようとした
    ' 2627 … 一意制約（PRIMARY KEY / UNIQUE 制約）に当たった
    Private Shared Function IsDuplicateKey(ex As SqlException) As Boolean
        Return ex.Number = 2601 OrElse ex.Number = 2627
    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
