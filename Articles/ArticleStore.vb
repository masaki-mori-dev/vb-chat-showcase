Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient

' 記事の表（Articles）への出入り
'
' 【SQL をここ1か所に閉じ込めてある】
' Accounts/AccountStore.vb・Articles/ArticleCategoryStore.vb と同じ考え方。
'
' 【本文は、必ず Articles/ArticleHtml.vb を通ってから来ること】
' この表の BodyHtml だけは、出口で生の HTML として出す。
' **通し忘れると、そのまま XSS になる。** 保存の口は Save 1つだけにして、
' その中で必ず通すようにしてある（呼ぶ側が通すのを忘れられない形）。
'
' 【どの関数も、必ず accountId で絞っている】
' ArticleId だけで UPDATE／DELETE しないこと——番号を打ち替えるだけで他人の記事を触れてしまう
Public NotInheritable Class ArticleStore

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"


    ' 1行ぶんの記事
    Public Class ArticleRecord

        Public Property ArticleId As Integer
        Public Property AccountId As Integer
        Public Property CategoryId As Integer?
        Public Property CategoryName As String
        Public Property Title As String
        Public Property BodyHtml As String
        Public Property Status As ArticleRules.ArticleStatus
        Public Property SortOrder As Integer?
        Public Property PinOrder As Integer?
        Public Property FirstPublishedAt As DateTime?
        Public Property CreatedAt As DateTime
        Public Property UpdatedAt As DateTime

        ' 記事の「URL の名前」（無ければ Nothing＝番号の URL）
        Public Property Slug As String

        ' 目次の3択（既定は「入れない」）
        Public Property TocMode As ArticleRules.ArticleTocMode = ArticleRules.ArticleTocMode.None

        ' 見出しの番号の2択（既定は「付けない」）。**目次とは別の軸**
        Public Property HeadingNumberMode As ArticleRules.HeadingNumberMode = ArticleRules.HeadingNumberMode.None

        ' 公開ページにまだ反映していない「編集中の控え」（ArticleDrafts）があるか。
        ' **GetList だけが入れる**（ほかの読み方では、いつも False のまま）
        Public Property HasDraft As Boolean

    End Class


    ' 「編集中の控え」1つぶん（ArticleDrafts の1行）
    '
    ' 【状態（Status）は持たない】状態の切り替えは「保存する」を押したときだけ（Database/CreateTable/ArticleDrafts.sql）
    Public Class DraftRecord

        Public Property ArticleId As Integer
        Public Property CategoryId As Integer?
        Public Property Title As String
        Public Property BodyHtml As String
        Public Property SortOrder As Integer?
        Public Property PinOrder As Integer?
        Public Property Slug As String

        ' 目次の3択。**状態と違って、控えにも持たせてある**——
        ' 状態は「保存する」を押したときだけ変わるものだが、目次は打っている途中で選び直すもので、
        ' 控えで開き直したときに消えると、選んだことが無かったことになる
        Public Property TocMode As ArticleRules.ArticleTocMode = ArticleRules.ArticleTocMode.None

        ' 見出しの番号の2択。目次と同じく、控えにも持たせてある
        Public Property HeadingNumberMode As ArticleRules.HeadingNumberMode = ArticleRules.HeadingNumberMode.None

        Public Property SavedAt As DateTime

    End Class


    ' 自動保存の結果
    Public Class AutoSaveResult

        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）
        Public Property Message As String

        ' 保存した記事の番号（新しい記事なら、最初の自動保存で採番されたもの）。何も保存しなかったときは受け取ったまま
        Public Property ArticleId As Integer

        ' どこへ書いたか
        '   "article" … 本体（下書き・非公開の記事。新しい記事もここ）
        '   "draft"   … 編集中の控え（公開中の記事）
        '   "none"    … 何も書いていない（新しい記事で、まだ何も打っていない）
        Public Property Target As String

        ' 保存したあと、公開ページに反映していない控えが残っているか。
        ' 控えに書いた内容が公開中のものと同じに戻ったときは、控えを消して False になる
        Public Property HasDraft As Boolean

        ' 保存した時刻（画面に「自動保存しました 21:45」と出す）
        Public Property SavedAt As DateTime

        ' サニタイザーが何かを取り除いたか（取り除いて保存した）
        Public Property RemovedUnsafe As Boolean


        ' 二重送信だったので足さなかった（AlreadySavedMessage）。画面は、これを見て自動保存を止める
        Public Property AlreadySaved As Boolean
    End Class


    ' 保存するときに受け取るもの（新規も編集も同じ形）
    Public Class SaveRequest

        ' 0 なら新規、それ以外なら編集
        Public Property ArticleId As Integer

        Public Property AccountId As Integer
        Public Property CategoryId As Integer?
        Public Property Title As String

        ' 【画面から来たままの HTML でよい】Save の中で Sanitize を通す
        Public Property BodyHtml As String

        Public Property Status As ArticleRules.ArticleStatus
        Public Property SortOrder As Integer?
        Public Property PinOrder As Integer?

        ' 【画面から来たままの文字でよい】Save の中で NormalizeSlug → ValidateSlug を通す
        Public Property Slug As String

        ' 目次の3択。**組み立てる側で NormalizeTocMode を通してから入れること**
        Public Property TocMode As ArticleRules.ArticleTocMode = ArticleRules.ArticleTocMode.None

        ' 見出しの番号の2択。**同じく NormalizeHeadingNumberMode を通してから入れること**
        Public Property HeadingNumberMode As ArticleRules.HeadingNumberMode = ArticleRules.HeadingNumberMode.None


        ' 二重送信を防ぐ、使い捨ての目印（Core/FormOnce.vb）
        ' **見るのは、新しい記事を足すときだけ**（Insert）。番号のある記事の保存は「直す」なので見ない。
        ' 入れるのは画面の側（FormOnce.TokenOf）。無ければ Nothing＝確かめない
        Public Property OnceToken As String
    End Class


    ' 保存の結果
    Public Class SaveResult

        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す
        Public Property Message As String

        ' 断った理由が、どの欄の話か（FieldTitle など。どの欄とも言えないときは Nothing）。
        Public Property Field As String

        ' 保存された記事の番号（新規のときは、採番されたもの）
        Public Property ArticleId As Integer

        ' 保存のときに、サニタイザーが何かを取り除いたか。
        ' True なら、画面で「使えない書き方を取り除きました」と知らせる（黙って消さない）
        Public Property RemovedUnsafe As Boolean


        ' 二重送信だったので足さなかった（AlreadySavedMessage）。画面は、これを見て自動保存を止める
        Public Property AlreadySaved As Boolean
    End Class


    ' ============================================================
    ' 読む
    ' ============================================================
    '
    ' 【一覧では本文を引いていない】
    ' BodyHtml は NVARCHAR(MAX) で、記事が増えるほど重くなる。
    ' 一覧に出すのはタイトル・状態・カテゴリ・日付だけなので、本文は要らない
    ' （過去ログの SQL が「取った行の半分を捨てている」＝宿題15 と、同じ種類の無駄をここで作らない）

    ' このアカウントの記事の一覧。
    '
    ' categoryId … Nothing なら全部。指定すると、そのカテゴリの記事だけを
    '              **そのカテゴリの並べ方（SortMode）で**並べる
    '
    ' 【カテゴリを選んだときに、実際の並び順で出している理由】
    ' 「指定した順」に番号を振っても、A-3 ができるまで結果を確かめる手段が無い。
    ' 管理画面の一覧をその順で出しておけば、**振った番号が効いているかを、その場で目で見られる**
    Public Shared Function GetList(accountId As Integer, categoryId As Integer?) As List(Of ArticleRecord)

        Dim sql As String =
            "SELECT a.ArticleId, a.AccountId, a.CategoryId, c.Name, a.Title, " &
            "       a.Status, a.SortOrder, a.PinOrder, a.FirstPublishedAt, a.CreatedAt, a.UpdatedAt, a.Slug, a.TocMode, a.HeadingNumberMode, " &
            "       CASE WHEN EXISTS (SELECT 1 FROM ArticleDrafts d WHERE d.ArticleId = a.ArticleId) THEN 1 ELSE 0 END " &
            "  FROM Articles a " &
            "  LEFT JOIN ArticleCategories c ON c.CategoryId = a.CategoryId " &
            " WHERE a.AccountId = @AccountId AND a.DeletedAt IS NULL"   ' 【ゴミ箱の記事は出さない】（ゴミ箱は GetTrash）

        Dim sortMode As ArticleRules.CategorySortMode = ArticleRules.CategorySortMode.Specified

        If categoryId.HasValue Then

            sql &= " AND a.CategoryId = @CategoryId"

            Dim category As ArticleCategoryStore.CategoryRecord =
                ArticleCategoryStore.Find(accountId, categoryId.Value)

            If category IsNot Nothing Then
                sortMode = category.SortMode
            End If

            sql &= ArticleRules.OrderByClause(sortMode)

        Else

            ' カテゴリを選んでいないときは、新しく作ったものを上に出す
            ' （管理画面は「さっき書いたもの」を探すことが多いため）
            sql &= " ORDER BY a.ArticleId DESC"

        End If

        sql &= ";"

        Dim list As New List(Of ArticleRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                If categoryId.HasValue Then
                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId.Value
                End If

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        Dim record As ArticleRecord = ReadListRow(reader)
                        record.HasDraft = (reader.GetInt32(14) = 1)

                        list.Add(record)

                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' 1つ引く（本文まで。他人の記事なら Nothing）
    '
    ' 【ゴミ箱の記事は Nothing】書く画面・保存の道は、ゴミ箱の記事を「無い」として扱う。
    ' ゴミ箱の記事も引きたいとき（完全に消す前に画像の名前を控える）は FindIncludingTrash
    Public Shared Function Find(accountId As Integer, articleId As Integer) As ArticleRecord
        Return FindCore(accountId, articleId, False)
    End Function

    Public Shared Function FindIncludingTrash(accountId As Integer, articleId As Integer) As ArticleRecord
        Return FindCore(accountId, articleId, True)
    End Function

    Private Shared Function FindCore(accountId As Integer, articleId As Integer, includeTrash As Boolean) As ArticleRecord

        Dim sql As String =
            "SELECT TOP (1) a.ArticleId, a.AccountId, a.CategoryId, c.Name, a.Title, " &
            "       a.Status, a.SortOrder, a.PinOrder, a.FirstPublishedAt, a.CreatedAt, a.UpdatedAt, a.Slug, a.TocMode, a.HeadingNumberMode, " &
            "       a.BodyHtml " &
            "  FROM Articles a " &
            "  LEFT JOIN ArticleCategories c ON c.CategoryId = a.CategoryId " &
            " WHERE a.ArticleId = @ArticleId AND a.AccountId = @AccountId" &
            If(includeTrash, ";", " AND a.DeletedAt IS NULL;")

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Dim record As ArticleRecord = ReadListRow(reader)
                    record.BodyHtml = reader.GetString(14)

                    Return record

                End Using

            End Using
        End Using

    End Function


    ' 記事のページ（/articles/{user}/{id}）に出すときの1行
    '
    ' ArticleRecord に、書いた人の名前2つを足したもの
    Public Class ArticlePageRecord
        Inherits ArticleRecord

        ' URL に出す名前（DB に入っている綴りのまま。大文字小文字の違う URL から来たら、これへ転送する）
        Public Property UserName As String

        ' 記事の署名に出す名前（出口でエスケープすること）
        Public Property DisplayName As String

    End Class


    ' 記事のページのために、ユーザー名と番号で1つ引く（無ければ Nothing）
    '
    ' 【状態（下書き・公開・非公開）では絞らない】誰に見せるかは、呼ぶ側（Articles/ArticleDetail.aspx.vb）が決める。
    ' 持ち主のプレビューのために、下書きも引く必要があるため。
    '
    ' 【ユーザー名は、DB の照合順序で比べる】Japanese_CI_AS＝大文字小文字を区別しない。
    ' ユーザー名は同じ照合順序の一意の列なので、大文字小文字違いの2人は作れない＝どちらで来ても1人に決まる。
    ' 正しい綴りは戻り値の UserName に入っているので、URL と違えば呼ぶ側が転送する。
    '
    ' 【止めているアカウント（IsActive = 0）の記事は引かない】止めた人の記事が外に出続けないように
    Public Shared Function FindForPage(userName As String, articleId As Integer) As ArticlePageRecord
        Return FindForPageCore(userName, articleId, Nothing)
    End Function


    ' 同じく、記事の「URL の名前」で引く（/articles/{user}/guide）
    '
    ' 【名前は、DB の照合順序で比べる】大文字小文字を区別しない。正しい綴り（小文字）は戻り値の Slug に入っているので、
    ' URL と違えば呼ぶ側が転送する
    Public Shared Function FindForPageBySlug(userName As String, slug As String) As ArticlePageRecord
        Return FindForPageCore(userName, 0, slug)
    End Function


    ' 【WHERE の違いは2つの決まった文字列だけ】利用者の入力は、どちらもパラメータで渡す
    Private Shared Function FindForPageCore(userName As String, articleId As Integer, slug As String) As ArticlePageRecord

        Dim keyCondition As String =
            If(slug Is Nothing, "a.ArticleId = @ArticleId", "a.Slug = @Slug")

        Dim sql As String =
            "SELECT TOP (1) a.ArticleId, a.AccountId, a.CategoryId, c.Name, a.Title, " &
            "       a.Status, a.SortOrder, a.PinOrder, a.FirstPublishedAt, a.CreatedAt, a.UpdatedAt, a.Slug, a.TocMode, a.HeadingNumberMode, " &
            "       a.BodyHtml, u.UserName, u.DisplayName " &
            "  FROM Articles a " &
            "  JOIN Accounts u ON u.AccountId = a.AccountId " &
            "  LEFT JOIN ArticleCategories c ON c.CategoryId = a.CategoryId " &
            " WHERE " & keyCondition & " AND u.UserName = @UserName AND u.IsActive = 1 AND a.DeletedAt IS NULL;"   ' ゴミ箱の記事は持ち主にも 404

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@Slug", SqlDbType.NVarChar, ArticleRules.SlugMaxLength).Value =
                    If(slug Is Nothing, CObj(DBNull.Value), CObj(slug))
                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, AccountStore.UserNameMaxLength).Value = userName

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Dim row As ArticleRecord = ReadListRow(reader)

                    Return New ArticlePageRecord With {
                        .ArticleId = row.ArticleId,
                        .AccountId = row.AccountId,
                        .CategoryId = row.CategoryId,
                        .CategoryName = row.CategoryName,
                        .Title = row.Title,
                        .Status = row.Status,
                        .SortOrder = row.SortOrder,
                        .PinOrder = row.PinOrder,
                        .FirstPublishedAt = row.FirstPublishedAt,
                        .CreatedAt = row.CreatedAt,
                        .UpdatedAt = row.UpdatedAt,
                        .Slug = row.Slug,
                        .TocMode = row.TocMode,
                        .HeadingNumberMode = row.HeadingNumberMode,
                        .BodyHtml = reader.GetString(14),
                        .UserName = reader.GetString(15),
                        .DisplayName = reader.GetString(16)}

                End Using

            End Using
        End Using

    End Function


    ' ============================================================
    ' 公開の記事の一覧（アカウントのトップページ・カテゴリの一覧）
    ' ============================================================
    '
    ' 【公開の記事（Status = 20）だけ】持ち主がログインしていても、下書き・非公開は出さない
    ' （一覧はプレビューの入口にしない。プレビューは管理画面の一覧から）。
    ' 【本文は引かない】一覧に出すのはタイトルと日付だけ（GetList と同じ理由）。
    ' 【止めているアカウントの記事は、呼ぶ側がアカウントを確かめてから呼ぶ】（ページが先に Accounts を見て404にする）

    Private Const PublishedColumns As String =
        "SELECT {0} a.ArticleId, a.AccountId, a.CategoryId, c.Name, a.Title, " &
        "       a.Status, a.SortOrder, a.PinOrder, a.FirstPublishedAt, a.CreatedAt, a.UpdatedAt, a.Slug, a.TocMode, a.HeadingNumberMode " &
        "  FROM Articles a " &
        "  LEFT JOIN ArticleCategories c ON c.CategoryId = a.CategoryId " &
        " WHERE a.AccountId = @AccountId AND a.Status = 20 "


    ' そのカテゴリの公開記事を、カテゴリの並べ方で（top が Nothing なら全部）
    '
    ' 【並べ方は ArticleRules.OrderByClause の1か所】管理画面の一覧でカテゴリを選んだときと同じ順になる
    Public Shared Function GetPublishedInCategory(accountId As Integer, categoryId As Integer,
                                                  sortMode As ArticleRules.CategorySortMode,
                                                  top As Integer?) As List(Of ArticleRecord)

        Dim sql As String =
            String.Format(PublishedColumns, If(top.HasValue, "TOP (@Top)", "")) &
            "   AND a.CategoryId = @CategoryId" &
            ArticleRules.OrderByClause(sortMode) & ";"

        Return ReadPublished(sql, accountId, categoryId, top)

    End Function


    ' 【固定表示だけを引く GetPublishedPinned は、消した】
    ' ブログのトップの「おすすめの記事」の枠をやめ、GetPublishedPage が固定の記事を先頭に並べるようになったため

    ' カテゴリの無い公開記事を、新しい順に（最初に公開した日）
    Public Shared Function GetPublishedUncategorized(accountId As Integer, top As Integer) As List(Of ArticleRecord)

        Dim sql As String =
            String.Format(PublishedColumns, "TOP (@Top)") &
            "   AND a.CategoryId IS NULL " &
            " ORDER BY a.FirstPublishedAt DESC, a.ArticleId DESC;"

        Return ReadPublished(sql, accountId, Nothing, top)

    End Function


    ' その人の公開記事を「最後に更新した順」に（ブログ型の③）
    '
    ' 【ブログのページの右の枠に出す「最新の記事」】カテゴリをまたいで、新しいものから並べる。
    '
    ' 【並べるのは UpdatedAt（最後に更新した日）】
    ' サイト全体の更新履歴（Articles/SiteArticles.vb の Load）と、同じ値・同じ向きにそろえてある
    ' ——**右の枠の同じ場所に出るものなので、片方だけ「最初に公開した順」にすると、
    ' サイトのトップとブログで並びが違う理由を、誰も説明できなくなる。**
    '
    ' 【UpdatedAt に索引は張っていない】公開記事は数十本の世界で、
    ' AccountId で絞ったあとの並べ替えなので、行数がごく少ない（SiteArticles の同じ判断）
    Public Shared Function GetPublishedRecent(accountId As Integer, top As Integer) As List(Of ArticleRecord)

        Dim sql As String =
            String.Format(PublishedColumns, "TOP (@Top)") &
            " ORDER BY a.UpdatedAt DESC, a.ArticleId DESC;"

        Return ReadPublished(sql, accountId, Nothing, top)

    End Function


    ' カテゴリごとの公開記事の数（公開記事の無いカテゴリは入らない）。カテゴリ無しの記事は、キー 0 に入れる
    Public Shared Function CountPublishedByCategory(accountId As Integer) As Dictionary(Of Integer, Integer)

        Dim counts As New Dictionary(Of Integer, Integer)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT ISNULL(CategoryId, 0), COUNT(*) FROM Articles " &
                " WHERE AccountId = @AccountId AND Status = 20 GROUP BY CategoryId;", conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        counts(reader.GetInt32(0)) = reader.GetInt32(1)
                    End While
                End Using

            End Using
        End Using

        Return counts

    End Function


    ' ============================================================
    ' ブログのトップ（本文ごと数本・ページ送り）と、記事のページの前後（ブログ型の④）
    ' ============================================================

    ' ブログのトップに出す1本（本文の頭を持つ）
    '
    ' 【ArticleRecord を使い回さない理由】あちらは本文を持たない
    ' （一覧に出すためのクラスなので、わざと読んでいない）。
    ' **本文まで読む問い合わせと、読まない問い合わせを、同じクラスで返すと
    '   「本文が空なのは、本当に空なのか、読んでいないのか」が分からなくなる。**
    '
    ' 【BodyHead は本文の頭だけ】トップは「出だし」だけを出す形になったので、
    ' 頭の 4000 文字しか読まない（ArticleHtml.ExcerptText が文字だけを抜く）。
    ' **これを本文として画面へ出さないこと**（途中で切れている＝タグも閉じていない）
    Public Class TopItem
        Public Property ArticleId As Integer
        Public Property Title As String
        Public Property Slug As String
        Public Property FirstPublishedAt As DateTime?
        Public Property UpdatedAt As DateTime
        Public Property CategoryId As Integer?
        Public Property CategoryName As String
        Public Property BodyHead As String
        ' 固定表示（ピン留め）の番号。固定していなければ Nothing
        Public Property PinOrder As Integer?
    End Class

    ' 本文の頭を何文字読むか（出だしの 100 文字を取るには、画像や見出しのタグを挟んでも足りる長さ）
    Public Const TopBodyHeadLength As Integer = 4000


    ' 1ページぶん（と、全部で何本あるか）
    Public Class TopPage
        Public Property Items As List(Of TopItem)
        ' そのアカウントの公開記事の数（ページ送りを出すかを決める）
        Public Property Total As Integer
    End Class


    ' ブログのトップの1ページぶんを引く（新しい順）
    '
    ' skip … 何本飛ばすか（1ページ目は0）
    ' take … 何本取るか
    '
    ' 【固定表示（PinOrder のある記事）を、いちばん上に並べる】（B）
    ' 前は「おすすめの記事」の枠を別に出し、同じ記事が一覧にも出ていた
    ' （指摘：固定記事はどのみち一番上に出るので、重複している）。
    ' 枠をやめて、一覧そのものの先頭に固定の番号の順で並べる（画面は「固定」の印を付ける）。
    ' ページ送りをまたいでも重ならない（1つの並びの中の順番なので）。
    ' **記事のページの前後（FindNeighbors）は、固定を見ない**＝公開した日の順のまま（読み進める順は時間の順）。
    '
    ' 【固定のあとは FirstPublishedAt（最初に公開した日）】
    ' ブログのトップは「新しい記事」を並べる場所なので、**公開した順**である。
    ' 右の枠の「最新の記事」（GetPublishedRecent）は UpdatedAt で、**わざと違えてある**——
    ' あちらは「最近どこが動いたか」、こちらは「新しく書かれたもの」。
    ' **どちらかに寄せないこと**（寄せると、古い記事を1文字直すだけでトップのいちばん上に来る）。
    '
    ' 【OFFSET ／ FETCH NEXT を使っている】SQL Server 2012 から在る書き方で、
    ' 本番は SQL Server 2022 である（「技術スタック・ホスティング」）。
    '
    ' 【数と中身を、1回の往復でまとめて取る】結果を2つ返す（SiteArticles.Load と同じ形）。
    ' 数を別に引くと、その間に記事が増えて「5本と言ったのに6本目が在る」ことが起きうる
    ' （害は無いが、往復を1回減らせるなら減らす）
    Public Shared Function GetPublishedPage(accountId As Integer, skip As Integer, take As Integer) As TopPage

        Dim sql As String =
            "SELECT a.ArticleId, a.Title, a.Slug, a.FirstPublishedAt, a.UpdatedAt, " &
            "       a.CategoryId, c.Name, LEFT(a.BodyHtml, @HeadLength), a.PinOrder " &
            "  FROM Articles a " &
            "  LEFT JOIN ArticleCategories c ON c.CategoryId = a.CategoryId " &
            " WHERE a.AccountId = @AccountId AND a.Status = 20 " &
            " ORDER BY CASE WHEN a.PinOrder IS NULL THEN 1 ELSE 0 END, a.PinOrder ASC, " &
            "          CASE WHEN a.FirstPublishedAt IS NULL THEN 1 ELSE 0 END, " &
            "          a.FirstPublishedAt DESC, a.ArticleId DESC " &
            " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY; " &
            "SELECT COUNT(*) FROM Articles WHERE AccountId = @AccountId AND Status = 20;"

        Dim result As New TopPage With {.Items = New List(Of TopItem)(), .Total = 0}

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Skip", SqlDbType.Int).Value = Math.Max(0, skip)
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = Math.Max(1, take)
                cmd.Parameters.Add("@HeadLength", SqlDbType.Int).Value = TopBodyHeadLength

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    While reader.Read()

                        result.Items.Add(New TopItem With {
                            .ArticleId = reader.GetInt32(0),
                            .Title = reader.GetString(1),
                            .Slug = If(reader.IsDBNull(2), Nothing, reader.GetString(2)),
                            .FirstPublishedAt = If(reader.IsDBNull(3), Nothing, CType(reader.GetDateTime(3), DateTime?)),
                            .UpdatedAt = reader.GetDateTime(4),
                            .CategoryId = If(reader.IsDBNull(5), Nothing, CType(reader.GetInt32(5), Integer?)),
                            .CategoryName = If(reader.IsDBNull(6), Nothing, reader.GetString(6)),
                            .BodyHead = If(reader.IsDBNull(7), "", reader.GetString(7)),
                            .PinOrder = If(reader.IsDBNull(8), Nothing, CType(reader.GetInt32(8), Integer?))})

                    End While

                    reader.NextResult()

                    If reader.Read() Then
                        result.Total = reader.GetInt32(0)
                    End If

                End Using

            End Using
        End Using

        Return result

    End Function


    ' 記事のページの「前の記事へ｜次の記事へ」の相手（無ければ Nothing）
    Public Class Neighbor
        Public Property ArticleId As Integer
        Public Property Title As String
        Public Property Slug As String
    End Class


    ' 前後の記事を、1回の往復で両方引く（決まりを変えた。決定：ブログというより記事の公開に近いので、追加した順より並び順を優先する）
    '
    ' 【カテゴリに入っている記事】そのカテゴリの公開記事の中だけで、そのカテゴリの並べ方どおりに前・次を出す。
    '   ・指定した順 … カテゴリの一覧と同じ並び（ArticleRules.OrderByClause）。番号の小さいほうが「前」。
    '                  番号の無い記事は、番号付きのあとに新しい順（一覧と同じ）
    '   ・新しい順／古い順 … どちらも「前＝ひとつ古い／次＝ひとつ新しい」（日本のブログの決まり。カテゴリの中だけで）。
    '                  一覧の向き（新しい順）に合わせて「前＝ひとつ新しい」にすると、日付で読み進める人には逆に感じるため
    ' 【カテゴリに入っていない記事】カテゴリに入っていない公開記事どうしだけで、最初に公開した日の順（前＝ひとつ古い）
    '
    ' 【前と次が食い違わない】1本の並び（ROW_NUMBER）の中で、ひとつ前とひとつ後ろを取るので、A の「次」が B なら B の「前」は必ず A。
    ' 【同じアカウントの中だけ】ほかの人のブログへは飛ばさない。
    ' 【公開記事の中だけ】下書き・非公開・ゴミ箱は、持ち主がプレビューで見ていても相手にしない
    ' （持ち主にだけ見える道を作ると、そのURLを人に貼ったときに404になる）。プレビュー中の記事は並びに居ないので、前後は出ない
    ' 【固定表示は見ない】前後のリンクは「読み進める」道なので、トップの固定の並びとは別
    Public Shared Sub FindNeighbors(accountId As Integer, articleId As Integer, categoryId As Integer?,
                                    ByRef prevItem As Neighbor, ByRef nextItem As Neighbor)

        prevItem = Nothing
        nextItem = Nothing

        Dim scope As String
        Dim order As String

        If categoryId.HasValue Then

            Dim mode As ArticleRules.CategorySortMode = ArticleRules.CategorySortMode.Specified
            Dim category As ArticleCategoryStore.CategoryRecord = ArticleCategoryStore.Find(accountId, categoryId.Value)

            If category IsNot Nothing Then
                mode = category.SortMode
            End If

            scope = " AND a.CategoryId = @CategoryId"
            order = ArticleRules.ReadingOrderClause(mode)

        Else

            scope = " AND a.CategoryId IS NULL"
            order = ArticleRules.ReadingOrderClause(ArticleRules.CategorySortMode.Oldest)

        End If

        ' 【order は決まった文字列だけ】ArticleRules.ReadingOrderClause が返す3つのどれか（利用者の値は入らない）
        Dim sql As String =
            "WITH L AS (" &
            "  SELECT a.ArticleId, a.Title, a.Slug, ROW_NUMBER() OVER (" & order & ") AS Rn " &
            "    FROM Articles a " &
            "   WHERE a.AccountId = @AccountId AND a.Status = 20 AND a.DeletedAt IS NULL" & scope & ") " &
            "SELECT n.ArticleId, n.Title, n.Slug, CASE WHEN n.Rn < c.Rn THEN 0 ELSE 1 END " &
            "  FROM L c JOIN L n ON n.Rn IN (c.Rn - 1, c.Rn + 1) " &
            " WHERE c.ArticleId = @Id;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = articleId

                If categoryId.HasValue Then
                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId.Value
                End If

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()

                    While reader.Read()
                        If reader.GetInt32(3) = 0 Then
                            prevItem = ReadNeighbor(reader)
                        Else
                            nextItem = ReadNeighbor(reader)
                        End If
                    End While

                End Using

            End Using
        End Using

    End Sub


    Private Shared Function ReadNeighbor(reader As SqlDataReader) As Neighbor

        Return New Neighbor With {
            .ArticleId = reader.GetInt32(0),
            .Title = reader.GetString(1),
            .Slug = If(reader.IsDBNull(2), Nothing, reader.GetString(2))}

    End Function
    Private Shared Function ReadPublished(sql As String, accountId As Integer,
                                          categoryId As Integer?, top As Integer?) As List(Of ArticleRecord)

        Dim list As New List(Of ArticleRecord)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                If categoryId.HasValue Then
                    cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId.Value
                End If

                If top.HasValue Then
                    cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top.Value
                End If

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        list.Add(ReadListRow(reader))
                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' このアカウントが今いくつ記事を持っているか（上限の判定に使う）
    '
    ' 【下書きも非公開も数に入れる】
    ' 上限は「置いておける量」の話なので、見えているかどうかは関係ない
    Public Shared Function CountByAccount(accountId As Integer) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT COUNT(*) FROM Articles WHERE AccountId = @AccountId AND DeletedAt IS NULL;", conn)   ' ゴミ箱の記事は上限に数えない

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Return CInt(cmd.ExecuteScalar())

            End Using
        End Using

    End Function


    ' ============================================================
    ' 書く
    ' ============================================================

    ' 新規も編集も、ここ1つ
    '
    ' 【ロールごとの上限も、ここで確かめている】
    ' 画面（Articles/Edit.aspx）でも確かめているが、**画面から消しただけでは守りにならない**
    ' ——URLを直接叩けば素通りする。Accounts/Home.aspx のコメントに書いたとおりの形にしてある。
    '
    ' 【RoleLimits が読めなければ、書かせない（フェイルクローズ）】
    ' GetBoolean は読めなければ False、IsWithin は上限を 0 とみなすので、
    ' ここで特別な分岐を書かなくても、自動的にそうなる（Accounts/RoleLimits.vb）
    Public Shared Function Save(request As SaveRequest, roleKey As String) As SaveResult

        If request Is Nothing Then
            Return Rejected("保存する内容がありませんでした。")
        End If

        ' === 書いてよいロールか ===
        If Not RoleLimits.GetBoolean(roleKey, RoleLimits.KeyCanWriteArticles) Then
            Return Rejected("このアカウントでは、記事を書けません。")
        End If

        ' === タイトルと本文 ===
        '
        ' 【公開するときだけ、タイトルと本文が要る】（下書きの保存には本文もタイトルも要らない）
        ' 下書き・非公開の保存は、どちらも空でよい（自動保存と同じ。一覧には「（タイトル未入力）」と出る）。
        ' 長さの上限は、どの状態でも見る
        Dim needsContent As Boolean = (request.Status = ArticleRules.ArticleStatus.Published)

        Dim reason As String = If(needsContent,
                                  ArticleRules.ValidateTitle(request.Title),
                                  ArticleRules.ValidateTitleLength(request.Title))

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldTitle)
        End If

        ' 【ここが唯一の砦】本文は、必ずここを通ってからDBへ入る
        Dim removedUnsafe As Boolean
        Dim body As String = ArticleHtml.Sanitize(request.BodyHtml, removedUnsafe)

        reason = If(needsContent,
                    ArticleRules.ValidateBody(body),
                    ArticleRules.ValidateBodyLength(body))

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldBody)
        End If

        ' === カテゴリは、自分のものか ===
        '
        ' 【画面の選択肢を信じない】<select> の value は開発者ツールから書き換えられる。
        ' 確かめないと、他人のカテゴリに自分の記事を入れられる
        If request.CategoryId.HasValue Then

            If ArticleCategoryStore.Find(request.AccountId, request.CategoryId.Value) Is Nothing Then
                Return Rejected("そのカテゴリは見つかりませんでした。", FieldCategory)
            End If

        End If

        ' === URL の名前 ===
        ' 画面の欄から来たままの文字を、ここでそろえて確かめる（小文字にする・空なら名前なし）
        request.Slug = ArticleRules.NormalizeSlug(request.Slug)

        reason = ArticleRules.ValidateSlug(request.Slug)

        If reason IsNot Nothing Then
            Return Rejected(reason, FieldSlug)
        End If

        Dim title As String = If(request.Title, "").Trim()

        Dim result As SaveResult

        If request.ArticleId = 0 Then
            result = Insert(request, roleKey, title, body)
        Else
            result = Update(request, title, body)
        End If

        If result IsNot Nothing AndAlso result.Ok Then

            ' 【編集中の控えを消す】画面の中身（控えから開いたなら控えの中身）を本体へ書いたので、控えは役目を終えた。
            ' 「反映する（保存）」は、この普通の保存そのものである
            If request.ArticleId <> 0 Then
                DeleteDraft(request.AccountId, request.ArticleId)
            End If

            result.RemovedUnsafe = removedUnsafe

            ' 【メニュー・更新履歴などの控えを捨てる】（Articles/SiteArticles.vb）
            ' 公開した・名前を付けた・タイトルを変えた、が次に開いた一般向けのページからすぐ出るように
            SiteArticles.Invalidate()

        End If

        Return result

    End Function


    Private Shared Function Insert(request As SaveRequest, roleKey As String,
                                   title As String, body As String) As SaveResult

        ' === 記事の数の上限 ===
        '
        ' 【新しく作るときだけ見る】
        ' 編集のたびに見ると、上限を下げた日に**既にある記事を直せなくなる**。
        ' 直せないと「非公開にして引っ込める」こともできないので、そちらのほうが困る
        Dim current As Integer = CountByAccount(request.AccountId)

        If Not RoleLimits.IsWithin(roleKey, RoleLimits.KeyArticleMaxCount, current + 1L) Then

            Dim max As Integer = RoleLimits.GetInt(roleKey, RoleLimits.KeyArticleMaxCount)

            Return Rejected("記事の数が上限（" & max & "件）に達しています。" &
                            "要らない記事を削除してから、もう一度お試しください。")

        End If

        ' 【本文の保存と、使っている表の書き換えを、同じトランザクションで】
        ' 分けると、途中で落ちたときに「本文には画像が在るのに、使っている表には無い」が残り、
        ' その画像が1日後の片付けで消える（記事から絵が消える）。
        ' 採番した番号は @Ids で受け取って、そのまま使っている表に渡す
        '
        ' 【二重送信を防ぐ目印も、同じトランザクションで】（毎回の基本の6。Core/FormOnce.vb）
        ' 保存すると番号つきの画面へ移り直すが、戻るボタンで「新しく書く」画面へ戻ると、ブラウザが打った中身と
        ' 目印を欄へ戻す。そのまま保存（か自動保存）すると、同じ記事がもう1本できていた（開発機で再現した）。
        '   ・目印がもう使われていたら、足さずに -1 を返す（→ AlreadySavedMessage）
        '   ・**足せたときにだけ**、目印を使ったことにする（URL の名前の重なりで断ったときは、使わない）
        ' **記事を足す道は、手の保存も自動保存もここ1か所を通る**ので、守りもここ1か所に置いてある
        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @Ids TABLE (Id INT); " &
            "DECLARE @NewId INT; " &
            "IF " & FormOnce.UsedCheckSql & " " &
            "    SET @NewId = " & AlreadySavedMarker & "; " &
            "ELSE " &
            "BEGIN " &
            "    INSERT INTO Articles (AccountId, CategoryId, Title, BodyHtml, Status, " &
            "                          SortOrder, PinOrder, FirstPublishedAt, Slug, TocMode, HeadingNumberMode) " &
            "    OUTPUT INSERTED.ArticleId INTO @Ids " &
            "    SELECT @AccountId, @CategoryId, @Title, @Body, @Status, " &
            "           @SortOrder, @PinOrder, " &
            "           CASE WHEN @Status = 20 THEN SYSDATETIME() ELSE NULL END, @Slug, @TocMode, @HeadingNumberMode " &
            "     WHERE " & SlugFreeCondition & "; " &
            "    SET @NewId = (SELECT TOP 1 Id FROM @Ids); " &
            "    IF @NewId IS NOT NULL " &
            "    BEGIN " &
            "        DECLARE @SyncArticleId INT = @NewId, @SyncOwnerKind TINYINT = " & ArticleImageLedger.OwnerArticle & "; " &
            ArticleImageLedger.SyncUsesSql &
            FormOnce.MarkUsedSql &
            "    END " &
            "END " &
            "COMMIT; " &
            "SELECT @NewId;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                AddCommonParameters(cmd, request, title, body)
                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = 0
                ArticleImageLedger.AddNamesParameter(cmd, ArticleImages.NamesIn(body))
                FormOnce.AddTokenParameter(cmd, request.OnceToken)

                conn.Open()

                Dim inserted As Object = cmd.ExecuteScalar()

                ' 【行が返らない＝同じアカウントに、同じ URL の名前の記事がもうある】（SlugFreeCondition）
                If inserted Is Nothing OrElse Convert.IsDBNull(inserted) Then
                    Return Rejected(SlugTakenMessage, FieldSlug)
                End If

                ' 【目印が、もう使われていた】（戻るボタンで戻って、もう一度保存した）
                If CInt(inserted) = AlreadySavedMarker Then
                    Return New SaveResult With {.Ok = False, .Message = AlreadySavedMessage, .AlreadySaved = True}
                End If

                Return New SaveResult With {.Ok = True, .ArticleId = CInt(inserted)}

            End Using
        End Using

    End Function


    Private Shared Function Update(request As SaveRequest, title As String, body As String) As SaveResult

        ' 【最初に公開した日は、まだ入っていないときだけ入れる】
        ' 下書き→公開の瞬間に一度だけ入り、以後は書き換えない。
        ' そのため、非公開にして戻しても、カテゴリの中での順番は元の位置のままになる。
        '
        ' 【この判定を、読んでから書く2段にしないこと】
        ' 「今の値を読む → 空なら入れる」に分けると、ほとんど同時に2回押されたときに
        ' 2回とも「空だ」と読んで、あとの回で日付が上書きされる。
        ' **CASE の中で今の列の値を見て、1つの文の中で決めている**
        ' （部屋を作るときの「もう1部屋持っていないか」と同じ形の罠である）
        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @Rows INT; " &
            "UPDATE Articles " &
            "   SET CategoryId = @CategoryId, " &
            "       Title = @Title, " &
            "       BodyHtml = @Body, " &
            "       Status = @Status, " &
            "       SortOrder = @SortOrder, " &
            "       PinOrder = @PinOrder, " &
            "       FirstPublishedAt = CASE " &
            "           WHEN FirstPublishedAt IS NOT NULL THEN FirstPublishedAt " &
            "           WHEN @Status = 20 THEN SYSDATETIME() " &
            "           ELSE NULL END, " &
            "       Slug = @Slug, " &
            "       TocMode = @TocMode, " &
            "       HeadingNumberMode = @HeadingNumberMode, " &
            "       UpdatedAt = SYSDATETIME() " &
            " WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NULL " &
            "   AND " & SlugFreeCondition & "; " &
            "SET @Rows = @@ROWCOUNT; " &
            "IF @Rows = 1 " &
            "BEGIN " &
            "    DECLARE @SyncArticleId INT = @ArticleId, @SyncOwnerKind TINYINT = " & ArticleImageLedger.OwnerArticle & "; " &
            ArticleImageLedger.SyncUsesSql &
            "END " &
            "COMMIT; " &
            "SELECT @Rows;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                AddCommonParameters(cmd, request, title, body)
                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = request.ArticleId
                ArticleImageLedger.AddNamesParameter(cmd, ArticleImages.NamesIn(body))

                conn.Open()

                If CInt(cmd.ExecuteScalar()) <> 1 Then

                    ' 【0行の理由は2つある】記事が無い（他人の記事を含む）か、URL の名前がほかの記事と重なったか。
                    ' 名前が重なっていたなら、その理由で断る（「見つかりません」は的外れになる）
                    If request.Slug IsNot Nothing AndAlso Find(request.AccountId, request.ArticleId) IsNot Nothing Then
                        Return Rejected(SlugTakenMessage, FieldSlug)
                    End If

                    Return Rejected("その記事は見つかりませんでした。")

                End If

            End Using
        End Using

        Return New SaveResult With {.Ok = True, .ArticleId = request.ArticleId}

    End Function


    ' ============================================================
    ' 自動保存と「編集中の控え」
    ' ============================================================
    '
    ' 【行き先は2つ】
    '   ・下書き・非公開の記事 … 本体（Articles）へそのまま（誰にも見えていないので、分ける意味が無い）
    '   ・公開中の記事       … 編集中の控え（ArticleDrafts）。**公開ページは変わらない**
    '   ・新しい記事         … 最初の自動保存で、本体に下書きとして作る（採番した番号を画面へ返す）
    ' 控えが既にある記事（公開中に控えを作ったあと、SSMS などで状態が変わった）も、控えへ書く
    ' （画面は控えの中身で開いているので、行き先を途中で変えない）。
    '
    ' 【自動保存では、状態（Status）を変えない】状態の切り替えは「保存する」を押したときだけ。
    ' 下書きの画面で状態を「公開」にしただけで、打っている途中の記事が公開されてはいけない。
    '
    ' 【手で押す保存と同じ関所を通す】ロール・Sanitize（ここが唯一の砦）・長さ・カテゴリが自分のものか・URL の名前の形。
    ' 違うのは2つだけ——①タイトルが空でもよい（一覧には「（タイトル未入力）」で出る）②本文が空でもよい。
    ' どちらも「反映する（保存）」「保存する」のときは、今までどおり必須である（Save が ValidateTitle・ValidateBody を通す）。
    '
    ' 【記事の数の上限】新しい記事を自動保存で作るときも、Insert の中で数える（数えないと、上限を自動保存で越えられる）
    Public Shared Function AutoSave(request As SaveRequest, roleKey As String) As AutoSaveResult

        If request Is Nothing Then
            Return AutoRejected("保存する内容がありませんでした。")
        End If

        If Not RoleLimits.GetBoolean(roleKey, RoleLimits.KeyCanWriteArticles) Then
            Return AutoRejected("このアカウントでは、記事を書けません。")
        End If

        Dim title As String = If(request.Title, "").Trim()

        ' 【空は許す。長さだけ見る】
        If title.Length > 0 Then

            Dim titleReason As String = ArticleRules.ValidateTitle(title)

            If titleReason IsNot Nothing Then
                Return AutoRejected(titleReason)
            End If

        End If

        ' 【ここも唯一の砦】手で押す保存と同じ Sanitize を通してから、DB へ入る
        Dim removedUnsafe As Boolean
        Dim body As String = ArticleHtml.Sanitize(request.BodyHtml, removedUnsafe)

        If body.Length > ArticleRules.BodyMaxLength Then
            Return AutoRejected("本文が長すぎます。短くしてください。")
        End If

        If request.CategoryId.HasValue AndAlso
           ArticleCategoryStore.Find(request.AccountId, request.CategoryId.Value) Is Nothing Then

            Return AutoRejected("そのカテゴリは見つかりませんでした。")

        End If

        request.Slug = ArticleRules.NormalizeSlug(request.Slug)

        Dim slugReason As String = ArticleRules.ValidateSlug(request.Slug)

        If slugReason IsNot Nothing Then
            Return AutoRejected(slugReason)
        End If

        Dim result As AutoSaveResult

        If request.ArticleId = 0 Then
            result = AutoSaveNew(request, roleKey, title, body)
        Else
            result = AutoSaveExisting(request, title, body)
        End If

        result.RemovedUnsafe = result.Ok AndAlso removedUnsafe

        ' 【メニュー・更新履歴の控え（SiteArticles）は捨てない】自動保存は、公開中の記事の本体を書き換えない
        ' （書き換えるのは下書き・非公開の本体と、公開中の記事の控えだけ。どちらも一般向けのページには出ない）

        Return result

    End Function


    ' 新しい記事の、最初の自動保存
    Private Shared Function AutoSaveNew(request As SaveRequest, roleKey As String,
                                        title As String, body As String) As AutoSaveResult

        ' 【まだ何も打っていないなら、作らない】画面を開いただけ・欄を押しただけで、空の下書きが増えないように
        If title.Length = 0 AndAlso body.Trim().Length = 0 Then
            Return New AutoSaveResult With {.Ok = True, .ArticleId = 0, .Target = "none", .SavedAt = DateTime.Now}
        End If

        ' 【いつも下書きで作る】画面の「状態」の欄が何であっても（状態を変えるのは「保存する」だけ）
        request.Status = ArticleRules.ArticleStatus.Draft

        Dim inserted As SaveResult = Insert(request, roleKey, title, body)

        If Not inserted.Ok Then

            Dim rejected As AutoSaveResult = AutoRejected(inserted.Message)
            rejected.AlreadySaved = inserted.AlreadySaved

            Return rejected

        End If

        Return New AutoSaveResult With {
            .Ok = True, .ArticleId = inserted.ArticleId, .Target = "article", .SavedAt = DateTime.Now}

    End Function


    ' 既にある記事の自動保存
    Private Shared Function AutoSaveExisting(request As SaveRequest, title As String, body As String) As AutoSaveResult

        Dim current As ArticleRecord = Find(request.AccountId, request.ArticleId)

        If current Is Nothing Then
            Return AutoRejected("その記事は見つかりませんでした。")
        End If

        If current.Status <> ArticleRules.ArticleStatus.Published AndAlso
           FindDraft(request.AccountId, request.ArticleId) Is Nothing Then

            ' === 下書き・非公開 → 本体へ ===
            '
            ' 【状態が公開でなく、控えも無いときだけ書く】を、UPDATE の WHERE に入れて1つの文で決める。
            ' 読んでから書く2段にすると、その間に別の画面で「公開」にされたとき、公開中の記事の本体を自動保存が書き換えてしまう
            Dim updated As Integer = UpdateBodyIfNotPublished(request, title, body)

            If updated = 1 Then
                Return New AutoSaveResult With {
                    .Ok = True, .ArticleId = request.ArticleId, .Target = "article", .SavedAt = DateTime.Now}
            End If

            ' 0行＝その間に公開された・控えができた・名前が重なった・消された。読み直して決め直す
            current = Find(request.AccountId, request.ArticleId)

            If current Is Nothing Then
                Return AutoRejected("その記事は見つかりませんでした。")
            End If

            If current.Status <> ArticleRules.ArticleStatus.Published AndAlso
               FindDraft(request.AccountId, request.ArticleId) Is Nothing Then

                ' 公開でも控えでもないのに書けなかった＝URL の名前が、ほかの記事と重なった
                Return AutoRejected(SlugTakenMessage)

            End If

        End If

        ' === 公開中（か、控えが既にある）→ 編集中の控えへ ===
        '
        ' 【公開中の中身と同じに戻ったら、控えを消す】書いて・消して、元に戻した場合など。
        ' 控えを残すと「未反映の編集あり」が、何も変えていないのに出続ける。
        ' （手で押した保存の直後に、途中まで走っていた自動保存が遅れて届いたときも、ここで消える）
        If SameAsArticle(current, request, title, body) Then

            DeleteDraft(request.AccountId, request.ArticleId)

            Return New AutoSaveResult With {
                .Ok = True, .ArticleId = request.ArticleId, .Target = "draft", .HasDraft = False, .SavedAt = DateTime.Now}

        End If

        Dim savedAt As DateTime? = UpsertDraft(request, title, body)

        If Not savedAt.HasValue Then
            Return AutoRejected("その記事は見つかりませんでした。")
        End If

        Return New AutoSaveResult With {
            .Ok = True, .ArticleId = request.ArticleId, .Target = "draft", .HasDraft = True, .SavedAt = savedAt.Value}

    End Function


    ' 下書き・非公開の本体を、状態は変えずに書き換える（書いた行数を返す）
    Private Shared Function UpdateBodyIfNotPublished(request As SaveRequest, title As String, body As String) As Integer

        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @Rows INT; " &
            "UPDATE Articles " &
            "   SET CategoryId = @CategoryId, " &
            "       Title = @Title, " &
            "       BodyHtml = @Body, " &
            "       SortOrder = @SortOrder, " &
            "       PinOrder = @PinOrder, " &
            "       Slug = @Slug, " &
            "       TocMode = @TocMode, " &
            "       HeadingNumberMode = @HeadingNumberMode, " &
            "       UpdatedAt = SYSDATETIME() " &
            " WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NULL " &
            "   AND Status <> 20 " &
            "   AND NOT EXISTS (SELECT 1 FROM ArticleDrafts WHERE ArticleId = @ArticleId) " &
            "   AND " & SlugFreeCondition & "; " &
            "SET @Rows = @@ROWCOUNT; " &
            "IF @Rows = 1 " &
            "BEGIN " &
            "    DECLARE @SyncArticleId INT = @ArticleId, @SyncOwnerKind TINYINT = " & ArticleImageLedger.OwnerArticle & "; " &
            ArticleImageLedger.SyncUsesSql &
            "END " &
            "COMMIT; " &
            "SELECT @Rows;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                AddCommonParameters(cmd, request, title, body)
                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = request.ArticleId
                ArticleImageLedger.AddNamesParameter(cmd, ArticleImages.NamesIn(body))

                conn.Open()

                Return CInt(cmd.ExecuteScalar())

            End Using
        End Using

    End Function


    ' 控えを書く（無ければ作る）。記事が無ければ Nothing
    '
    ' 【UPDATE → 無ければ INSERT を、1つのトランザクションで】UPDLOCK・HOLDLOCK＝ほとんど同時に2回届いても、
    ' 2回とも「まだ無い」と読んで INSERT し、主キーでぶつかる、が起きないように。
    ' 【URL の名前の重なりは、ここでは見ない】反映するとき（Save）に見る。控えの段階で断ると、打っている途中で止まってしまう
    Private Shared Function UpsertDraft(request As SaveRequest, title As String, body As String) As DateTime?

        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "DECLARE @Now DATETIME2(3) = SYSDATETIME(); " &
            "BEGIN TRAN; " &
            "UPDATE ArticleDrafts WITH (UPDLOCK, HOLDLOCK) " &
            "   SET CategoryId = @CategoryId, Title = @Title, BodyHtml = @Body, " &
            "       SortOrder = @SortOrder, PinOrder = @PinOrder, Slug = @Slug, TocMode = @TocMode, HeadingNumberMode = @HeadingNumberMode, SavedAt = @Now " &
            " WHERE ArticleId = @ArticleId AND AccountId = @AccountId; " &
            "IF @@ROWCOUNT = 0 " &
            "    INSERT INTO ArticleDrafts (ArticleId, AccountId, CategoryId, Title, BodyHtml, SortOrder, PinOrder, Slug, TocMode, HeadingNumberMode, SavedAt) " &
            "    SELECT @ArticleId, @AccountId, @CategoryId, @Title, @Body, @SortOrder, @PinOrder, @Slug, @TocMode, @HeadingNumberMode, @Now " &
            "     WHERE EXISTS (SELECT 1 FROM Articles WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NULL); " &
            "IF EXISTS (SELECT 1 FROM ArticleDrafts WHERE ArticleId = @ArticleId AND AccountId = @AccountId) " &
            "BEGIN " &
            "    DECLARE @SyncArticleId INT = @ArticleId, @SyncOwnerKind TINYINT = " & ArticleImageLedger.OwnerDraft & "; " &
            ArticleImageLedger.SyncUsesSql &
            "END " &
            "COMMIT; " &
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM ArticleDrafts WHERE ArticleId = @ArticleId AND AccountId = @AccountId) " &
            "            THEN @Now END;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                AddCommonParameters(cmd, request, title, body)
                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = request.ArticleId
                ArticleImageLedger.AddNamesParameter(cmd, ArticleImages.NamesIn(body))

                conn.Open()

                Dim value As Object = cmd.ExecuteScalar()

                If value Is Nothing OrElse Convert.IsDBNull(value) Then
                    Return Nothing
                End If

                Return CDate(value)

            End Using
        End Using

    End Function


    ' 画面の中身が、公開中の本体と同じか（同じなら、控えは要らない）
    Private Shared Function SameAsArticle(current As ArticleRecord, request As SaveRequest,
                                          title As String, body As String) As Boolean

        ' 【本文は、まずそのまま比べる。違えば、保存してある本文も Sanitize に通してから、もう一度比べる】
        ' body は、いま Sanitize を通ったもの。保存してある本文は、**保存したときの** Sanitize を通ったもので、
        ' そのあと Sanitize の決まりが変わっていると（リンクの開き方をそろえる、など）、中身が同じでも1文字単位では食い違う。
        ' 食い違うと、何も変えていないのに控えができて「未反映の編集あり」が付く。
        ' 【それでも食い違うことはある】書く欄（Quill）を通っただけで形が変わる本文。そちらは画面の側が、
        ' 送る前に「開いたときの中身と同じか」を比べて止めている（Scripts/article-editor.js の lastSavedSnapshot）
        Return String.Equals(current.Title, title, StringComparison.Ordinal) AndAlso
               (String.Equals(current.BodyHtml, body, StringComparison.Ordinal) OrElse
                String.Equals(ArticleHtml.Sanitize(current.BodyHtml), body, StringComparison.Ordinal)) AndAlso
               Nullable.Equals(current.CategoryId, request.CategoryId) AndAlso
               Nullable.Equals(current.SortOrder, request.SortOrder) AndAlso
               Nullable.Equals(current.PinOrder, request.PinOrder) AndAlso
               String.Equals(If(current.Slug, ""), If(request.Slug, ""), StringComparison.Ordinal) AndAlso
               current.TocMode = request.TocMode AndAlso
               current.HeadingNumberMode = request.HeadingNumberMode

    End Function


    ' 編集中の控えを1つ引く（無い・他人の記事なら Nothing）
    Public Shared Function FindDraft(accountId As Integer, articleId As Integer) As DraftRecord

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (1) ArticleId, CategoryId, Title, BodyHtml, SortOrder, PinOrder, Slug, TocMode, HeadingNumberMode, SavedAt " &
                "  FROM ArticleDrafts WHERE ArticleId = @ArticleId AND AccountId = @AccountId;", conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader(CommandBehavior.SingleRow)

                    If Not reader.Read() Then
                        Return Nothing
                    End If

                    Return New DraftRecord With {
                        .ArticleId = reader.GetInt32(0),
                        .CategoryId = If(reader.IsDBNull(1), CType(Nothing, Integer?), reader.GetInt32(1)),
                        .Title = reader.GetString(2),
                        .BodyHtml = reader.GetString(3),
                        .SortOrder = If(reader.IsDBNull(4), CType(Nothing, Integer?), reader.GetInt32(4)),
                        .PinOrder = If(reader.IsDBNull(5), CType(Nothing, Integer?), reader.GetInt32(5)),
                        .Slug = If(reader.IsDBNull(6), Nothing, reader.GetString(6)),
                        .TocMode = ArticleRules.NormalizeTocMode(CInt(reader.GetByte(7))),
                        .HeadingNumberMode = ArticleRules.NormalizeHeadingNumberMode(CInt(reader.GetByte(8))),
                        .SavedAt = reader.GetDateTime(9)}

                End Using

            End Using
        End Using

    End Function


    ' 編集中の控えを消す（「この編集を捨てる」・反映したあと）。無くても何もしない
    Public Shared Sub DeleteDraft(accountId As Integer, articleId As Integer)

        ' 【控えの「使っている」行も、同じトランザクションで消す】
        ' 記事はまだ在るので、外部キーの ON DELETE CASCADE は働かない。
        ' 消し忘れると、控えでしか使っていなかった画像が、いつまでも「使われている」ことになって片付かない
        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DELETE FROM ArticleDrafts WHERE ArticleId = @ArticleId AND AccountId = @AccountId; " &
            "IF @@ROWCOUNT > 0 " &
            ArticleImageLedger.DeleteDraftUsesSql() &
            "COMMIT;"

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()
                cmd.ExecuteNonQuery()

            End Using
        End Using

    End Sub


    Private Shared Function AutoRejected(message As String) As AutoSaveResult
        Return New AutoSaveResult With {.Ok = False, .Message = message, .Target = "none"}
    End Function


    ' 消す。戻り値は断る理由（消せたら Nothing）
    '
    ' 【編集中の控え（ArticleDrafts）は、外部キーの ON DELETE CASCADE で一緒に消える】
    '
    ' 【画像の後片付け】消す前に、その記事（と控え）に使われていた画像の名前を控えておき、消したあとで
    ' ArticleImages.CleanupAfterDelete に渡す（ほかの記事にも使われている画像は残る）。後片付けに失敗しても、記事の削除は取り消さない
    Public Shared Function Delete(accountId As Integer, articleId As Integer) As String

        Dim imageNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Dim before As ArticleRecord = FindIncludingTrash(accountId, articleId)

        If before IsNot Nothing Then

            imageNames.UnionWith(ArticleImages.NamesIn(before.BodyHtml))

            Dim draft As DraftRecord = FindDraft(accountId, articleId)

            If draft IsNot Nothing Then
                imageNames.UnionWith(ArticleImages.NamesIn(draft.BodyHtml))
            End If

        End If

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "DELETE FROM Articles WHERE ArticleId = @ArticleId AND AccountId = @AccountId;", conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                If cmd.ExecuteNonQuery() <> 1 Then
                    Return "その記事は見つかりませんでした。"
                End If

            End Using
        End Using

        ' メニュー・更新履歴などの控えを捨てる（消した記事へのリンクを残さない）
        SiteArticles.Invalidate()

        Try
            ArticleImages.CleanupAfterDelete(accountId, imageNames)
        Catch ex As Exception
            ' 画像が残るだけ（容量に数えられる）。次に記事を削除したときに、また消しにいく
            ErrorReport.Report(ex, "ArticleStore.Delete（画像の後片付け）", "アカウント " & accountId)
        End Try

        Return Nothing

    End Function


    ' ============================================================
    ' ゴミ箱（設計は開発者が一任）
    ' ============================================================
    ' 一覧・書く画面の「削除」は、まずゴミ箱へ移す（論理削除）。30日たったら 1日1回の仕事（Core/LogCleanup.vb）が
    ' 上の Delete で完全に消す（画像もそのとき）。
    '
    ' 【移すと「非公開」になる】公開側の問い合わせは全部 Status = 20 で絞っているので、それだけで公開側のどこにも出ない
    ' （記事のページ・ブログのトップ・カテゴリの一覧・前後・右の枠・更新履歴・メニュー）。持ち主のプレビューも 404
    ' （FindForPageCore が DeletedAt で絞る）。戻す前の状態は TrashedStatus に持つ。
    ' 【記事のアドレスは空ける】Slug を TrashedSlug へ移して NULL にする＝新しい記事でその名前を使える（WordPress と同じ考え方）。
    ' 元に戻すときに、ほかの記事が使っていなければ戻す。使われていたら空のまま戻し、そう知らせる。
    ' 【上限】ゴミ箱の記事は記事の数の上限に数えない（CountByAccount）。画像の量は完全に消えるまで数える（消す前に戻せるため）。
    ' 【編集中の控え（ArticleDrafts）はそのまま】戻したら、控えも一緒に戻る。完全に消すときは CASCADE で消える
    '
    ' 列は Database/CreateTable/Articles.sql の【ゴミ箱】

    ' ゴミ箱に置いておく日数（過ぎたら完全に消す）。コードの定数（設定にしない＝画面の「あと n 日」の文と一緒に変わるように）
    Public Const TrashKeepDays As Integer = 30


    ' ゴミ箱へ移す。戻り値は断る理由（移せたら Nothing）
    Public Shared Function MoveToTrash(accountId As Integer, articleId As Integer) As String

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "UPDATE Articles " &
                "   SET TrashedStatus = Status, " &
                "       TrashedSlug = Slug, " &
                "       Slug = NULL, " &
                "       Status = CASE WHEN Status = 20 THEN 30 ELSE Status END, " &
                "       DeletedAt = SYSDATETIME() " &
                " WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NULL;", conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                If cmd.ExecuteNonQuery() <> 1 Then
                    Return "その記事は見つかりませんでした。"
                End If

            End Using
        End Using

        ' メニュー・更新履歴などの控えを捨てる（公開中だった記事へのリンクを残さない）
        SiteArticles.Invalidate()

        Return Nothing

    End Function


    ' 元に戻した結果
    Public Class RestoreResult
        Public Property Reason As String          ' 断る理由（戻せたら Nothing）
        Public Property SlugCleared As Boolean    ' 記事のアドレスがほかの記事で使われていたので、空にして戻した
    End Class


    ' 元に戻す（ゴミ箱へ移す前の状態へ）。
    ' 【記事の数の上限を見る】ゴミ箱の記事は数えていないので、戻すと上限を超えうる（新しく書くときと同じ決まり）
    Public Shared Function Restore(accountId As Integer, roleKey As String, articleId As Integer) As RestoreResult

        Dim max As Integer = RoleLimits.GetInt(roleKey, RoleLimits.KeyArticleMaxCount)

        If Not RoleLimits.IsWithin(roleKey, RoleLimits.KeyArticleMaxCount, CountByAccount(accountId) + 1L) Then
            Return New RestoreResult With {.Reason = "記事の数が上限（" & max & "件）に達しているので、元に戻せません。要らない記事をゴミ箱へ移してから、もう一度押してください。"}
        End If

        ' 【1つの文で】アドレスが空いているかの確かめと書き戻しを分けない（間に別の保存がそのアドレスを取りうる）
        Dim sql As String =
            "SET XACT_ABORT ON; " &
            "BEGIN TRAN; " &
            "DECLARE @Cleared BIT = 0; " &
            "IF EXISTS (SELECT 1 FROM Articles a WITH (UPDLOCK, HOLDLOCK) " &
            "             JOIN Articles t ON t.ArticleId = @ArticleId AND t.AccountId = @AccountId AND t.DeletedAt IS NOT NULL " &
            "            WHERE a.AccountId = @AccountId AND a.Slug = t.TrashedSlug AND a.ArticleId <> @ArticleId) " &
            "    SET @Cleared = 1; " &
            "UPDATE Articles " &
            "   SET Status = ISNULL(TrashedStatus, Status), " &
            "       Slug = CASE WHEN @Cleared = 1 THEN NULL ELSE TrashedSlug END, " &
            "       DeletedAt = NULL, TrashedStatus = NULL, TrashedSlug = NULL, " &
            "       UpdatedAt = SYSDATETIME() " &
            " WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NOT NULL; " &
            "SELECT @@ROWCOUNT, @Cleared; " &
            "COMMIT;"

        Dim rows As Integer
        Dim cleared As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(sql, conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    reader.Read()
                    rows = reader.GetInt32(0)
                    cleared = reader.GetBoolean(1)
                End Using

            End Using
        End Using

        If rows <> 1 Then
            Return New RestoreResult With {.Reason = "その記事は、ゴミ箱に見つかりませんでした。"}
        End If

        SiteArticles.Invalidate()

        Return New RestoreResult With {.SlugCleared = cleared}

    End Function


    ' ゴミ箱から、今すぐ完全に消す。**ゴミ箱に入っている記事だけ**（一覧からいきなり完全に消す道は作らない）
    Public Shared Function PurgeFromTrash(accountId As Integer, articleId As Integer) As String

        If Not IsInTrash(accountId, articleId) Then
            Return "その記事は、ゴミ箱に見つかりませんでした。"
        End If

        Return Delete(accountId, articleId)

    End Function


    Private Shared Function IsInTrash(accountId As Integer, articleId As Integer) As Boolean

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT COUNT(*) FROM Articles WHERE ArticleId = @ArticleId AND AccountId = @AccountId AND DeletedAt IS NOT NULL;", conn)

                cmd.Parameters.Add("@ArticleId", SqlDbType.Int).Value = articleId
                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()
                Return CInt(cmd.ExecuteScalar()) = 1

            End Using
        End Using

    End Function


    ' ゴミ箱の1行（一覧に出すもの）
    Public Class TrashItem
        Public Property ArticleId As Integer
        Public Property Title As String
        Public Property DeletedAt As DateTime
        Public Property DaysLeft As Integer       ' あと何日で完全に消えるか（0＝次の片付けで消える）
    End Class


    ' このアカウントのゴミ箱（新しく移した順）
    Public Shared Function GetTrash(accountId As Integer) As List(Of TrashItem)

        Dim list As New List(Of TrashItem)()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT ArticleId, Title, DeletedAt, SYSDATETIME() FROM Articles " &
                " WHERE AccountId = @AccountId AND DeletedAt IS NOT NULL " &
                " ORDER BY DeletedAt DESC, ArticleId DESC;", conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        Dim movedAt As DateTime = reader.GetDateTime(2)
                        Dim nowAt As DateTime = reader.GetDateTime(3)
                        Dim daysLeftRaw As Integer = CInt(Math.Ceiling((movedAt.AddDays(TrashKeepDays) - nowAt).TotalDays))

                        list.Add(New TrashItem With {
                            .ArticleId = reader.GetInt32(0),
                            .Title = reader.GetString(1),
                            .DeletedAt = movedAt,
                            .DaysLeft = Math.Max(0, daysLeftRaw)})

                    End While
                End Using

            End Using
        End Using

        Return list

    End Function


    ' ゴミ箱の数（記事一覧の「ゴミ箱 (n)」）
    Public Shared Function CountTrash(accountId As Integer) As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT COUNT(*) FROM Articles WHERE AccountId = @AccountId AND DeletedAt IS NOT NULL;", conn)

                cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = accountId

                conn.Open()
                Return CInt(cmd.ExecuteScalar())

            End Using
        End Using

    End Function


    ' 置いておく日数を過ぎたゴミ箱の記事を、完全に消す（1日1回の仕事。Core/LogCleanup.vb）。戻り値は消した数。
    ' 【1本ずつ Delete を通す】画像の後片付け（ほかの記事が使っている画像は残す）を、ふだんの削除と同じ道でやるため。
    ' 1回に limit 本まで（残りは次の日）
    Public Shared Function PurgeExpired(limit As Integer) As Integer

        Dim targets As New List(Of Tuple(Of Integer, Integer))()

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "SELECT TOP (@Limit) AccountId, ArticleId FROM Articles " &
                " WHERE DeletedAt IS NOT NULL AND DeletedAt < DATEADD(DAY, -@Days, SYSDATETIME()) " &
                " ORDER BY DeletedAt;", conn)

                cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit
                cmd.Parameters.Add("@Days", SqlDbType.Int).Value = TrashKeepDays

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        targets.Add(Tuple.Create(reader.GetInt32(0), reader.GetInt32(1)))
                    End While
                End Using

            End Using
        End Using

        Dim purged As Integer = 0

        For Each target In targets
            If Delete(target.Item1, target.Item2) Is Nothing Then
                purged += 1
            End If
        Next

        Return purged

    End Function


    ' ============================================================
    ' 中で使うもの
    ' ============================================================

    ' 【ロールは、引数で最後まで持ち回っている】
    ' ロールはアカウントのもので、画面から来る値ではないので、SaveRequest（画面から組み立てる箱）
    ' には混ぜていない。**Shared のフィールドに置くのも駄目である**——
    ' このクラスはアプリ全体で1つなので、2人が同時に保存したときに
    ' 片方のロールでもう片方の上限が判定される（同じ形の罠を、ここで書きかけた）。
    ' 引数で渡せば、その心配そのものが無くなる


    Private Shared Sub AddCommonParameters(cmd As SqlCommand, request As SaveRequest,
                                           title As String, body As String)

        cmd.Parameters.Add("@AccountId", SqlDbType.Int).Value = request.AccountId
        cmd.Parameters.Add("@CategoryId", SqlDbType.Int).Value = NullableValue(request.CategoryId)
        cmd.Parameters.Add("@Title", SqlDbType.NVarChar, ArticleRules.TitleMaxLength).Value = title

        ' 【長さに -1 を渡すこと】NVARCHAR(MAX) の列は、これで渡す。
        ' 既定のまま渡すと、値の長さから型が推測されて、長い本文で切られることがある
        cmd.Parameters.Add("@Body", SqlDbType.NVarChar, -1).Value = body

        cmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = CInt(request.Status)
        cmd.Parameters.Add("@SortOrder", SqlDbType.Int).Value = NullableValue(request.SortOrder)
        cmd.Parameters.Add("@PinOrder", SqlDbType.Int).Value = NullableValue(request.PinOrder)

        cmd.Parameters.Add("@Slug", SqlDbType.NVarChar, ArticleRules.SlugMaxLength).Value =
            If(request.Slug Is Nothing, CObj(DBNull.Value), CObj(request.Slug))

        ' 目次の3択。Status と同じ TINYINT
        cmd.Parameters.Add("@TocMode", SqlDbType.TinyInt).Value = CInt(request.TocMode)

        ' 見出しの番号の2択
        cmd.Parameters.Add("@HeadingNumberMode", SqlDbType.TinyInt).Value = CInt(request.HeadingNumberMode)

    End Sub


    ' === URL の名前が、同じアカウントのほかの記事と重なっていないか ===
    '
    ' 【確かめと書き込みを、1つの文にする】INSERT／UPDATE の WHERE に、この条件を入れて使う。
    ' 「先に確かめてから書く」に分けると、ほとんど同時に2回保存されたときに、2つの記事が同じ名前になる
    ' （最初の管理人を作るとき・部屋を作るときと同じ形の罠）。
    ' UPDLOCK・HOLDLOCK＝確かめた範囲を、文が終わるまでほかの保存に触らせない。
    '
    ' 【一意の索引は張っていない】Slug は NULL の行が何本もあるので、ふつうの一意の索引は使えない。
    ' フィルター付き索引にすると、sqlcmd から手で DML を流すときに QUOTED_IDENTIFIER の罠が増える
    ' （Database/CreateTable/Articles.sql の Slug の説明）。記事を書く道はこの Save だけなので、守りをここに置いてある。
    ' **記事を書く道を増やしたら、そこでもこの条件を使うこと**
    Private Const SlugFreeCondition As String =
        "(@Slug IS NULL OR NOT EXISTS (" &
        "    SELECT 1 FROM Articles WITH (UPDLOCK, HOLDLOCK) " &
        "     WHERE AccountId = @AccountId AND Slug = @Slug AND ArticleId <> @ArticleId))"

    Private Const SlugTakenMessage As String =
        "その記事のアドレスは、ほかの記事で使っています。別の名前にしてください。"

    ' Insert の SQL が「目印が、もう使われていた」を返すときの値（記事の番号は1から振られるので、重ならない）
    Private Const AlreadySavedMarker As Integer = -1

    ' 戻るボタンで「新しく書く」画面へ戻って、もう一度保存した（か、自動保存が走った）ときの文
    '
    ' 【「保存できませんでした」と書かない】さっきの分は、保存してある。
    ' 【どうすればよいかを言う】続きを書くなら一覧から。別の記事として足すなら、もう一度押す
    ' （断った画面には新しい目印が入る＝FormOnce のいつもの決まり。自動保存は、その画面では止めてある）
    Public Const AlreadySavedMessage As String =
        "この記事は、もう保存してあります（戻って、もう一度保存したため）。" &
        "続きは、記事一覧から開いてください。別の記事として保存するときは、もう一度押してください。"


    ' Integer? を、DB へ渡せる形にする（Nothing は DBNull）
    Private Shared Function NullableValue(value As Integer?) As Object

        If value.HasValue Then
            Return value.Value
        End If

        Return DBNull.Value

    End Function


    Private Shared Function ReadListRow(reader As SqlDataReader) As ArticleRecord

        Return New ArticleRecord With {
            .ArticleId = reader.GetInt32(0),
            .AccountId = reader.GetInt32(1),
            .CategoryId = If(reader.IsDBNull(2), CType(Nothing, Integer?), reader.GetInt32(2)),
            .CategoryName = If(reader.IsDBNull(3), Nothing, reader.GetString(3)),
            .Title = reader.GetString(4),
            .Status = ArticleRules.NormalizeStatus(CInt(reader.GetByte(5))),
            .SortOrder = If(reader.IsDBNull(6), CType(Nothing, Integer?), reader.GetInt32(6)),
            .PinOrder = If(reader.IsDBNull(7), CType(Nothing, Integer?), reader.GetInt32(7)),
            .FirstPublishedAt = If(reader.IsDBNull(8), CType(Nothing, DateTime?), reader.GetDateTime(8)),
            .CreatedAt = reader.GetDateTime(9),
            .UpdatedAt = reader.GetDateTime(10),
            .Slug = If(reader.IsDBNull(11), Nothing, reader.GetString(11)),
            .TocMode = ArticleRules.NormalizeTocMode(CInt(reader.GetByte(12))),
            .HeadingNumberMode = ArticleRules.NormalizeHeadingNumberMode(CInt(reader.GetByte(13)))}

    End Function


    Private Shared Function Rejected(message As String, Optional field As String = Nothing) As SaveResult
        Return New SaveResult With {.Ok = False, .Message = message, .Field = field}
    End Function

    ' 断った理由が「どの欄の話か」（SaveResult.Field）。画面は、これで理由を出す欄を決める（基本ルールの⑧⑨）
    Public Const FieldTitle As String = "title"
    Public Const FieldBody As String = "body"
    Public Const FieldSlug As String = "slug"
    Public Const FieldCategory As String = "category"


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
