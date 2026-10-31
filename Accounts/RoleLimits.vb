Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Threading

' ロールごとの上限（DB の RoleLimits テーブル）の、読み出しの窓口
'
' 【何のための仕組みか】
' 「このロールは記事を書けるか」「記事を何件まで持てるか」「画像は1枚何バイトまでか」を、
' **コードではなく DB の行が決める**。将来ロールごとの違いを、コードを直さずに値で変えるため。
'
' 【SiteSettings と、わざと逆にしてあるところが1つある】
'
' | | SiteSettings | ここ |
' |---|---|---|
' | 形 | キー × 値 の行 | **ロール × キー × 値** の行 |
' | キャッシュ | Snapshot を1つ持ち、Reload() で捨てる | 同じ |
' | **読めなかったら** | **コードの既定値で通す**（フェイルオープン） | **させない・0**（フェイルクローズ） |
'
' フェイルオープンにすると「上限が読めない＝無制限」になりかねない。
' サイトが止まらないことより、**上限が外れないこと**のほうが大事な表である。
' （SiteSettings がフェイルオープンなのは、あれが読めないとトップページまで落ちるから。
'   こちらは記事を書く画面でしか読まないので、読めないときに止まっても、サイトは動き続ける）
'
' 【読めなかったことは、必ず知らせる】
' フェイルクローズは「静かに何もできなくなる」形なので、気づけないと原因を探すのが難しい。
' 同じエラーは1時間に1通にまとめられる（エラー報告メールの絞り方）。
'
' 【罠】SyncLock の中から読まないこと
' このクラスは、キャッシュが空のとき DB へ問い合わせる（SiteSettings と同じ）。
' 値は、鍵を取る前にローカル変数へ読んでおくこと
Public NotInheritable Class RoleLimits

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"

    ' 読み込みに失敗したとき、次に読み直すまで（SiteSettings.RetryAfterFailure と同じ考え方）
    '
    ' 失敗をキャッシュしないと、DB が落ちている間、読むたびにタイムアウトを待つことになる。
    ' 永久にキャッシュすると、一瞬の詰まりで「何もできない」まま戻らなくなる
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)


    ' === 項目の名前（DB の LimitKey 列に入る文字列） ===
    '
    ' 打ち間違いが実行時まで分からないと困るため、文字列を直接書かず、必ずこの定数を通す
    ' （SiteSettings の設定の名前と同じ決まり）

    ' 記事を書けるか（1 / 0）
    Public Const KeyCanWriteArticles As String = "CanWriteArticles"

    ' 記事の数の上限（件。-1 は無制限）
    Public Const KeyArticleMaxCount As String = "ArticleMaxCount"

    ' 記事の画像1枚の上限（バイト）
    Public Const KeyArticleImageMaxBytes As String = "ArticleImageMaxBytes"

    ' 記事の画像の合計の上限（バイト。-1 は無制限）
    Public Const KeyArticleStorageMaxBytes As String = "ArticleStorageMaxBytes"


    ' 「無制限」を表す値
    '
    ' 【読めなかったときに、この値を返さないこと】
    ' フェイルクローズは 0（＝させない）である。-1 は「DB にそう書いてあった」ときだけ
    Public Const Unlimited As Integer = -1


    Private Shared ReadOnly CacheLock As New Object()
    Private Shared Cached As Snapshot


    ' オン/オフの項目を読む（読めなければ False ＝ させない）
    Public Shared Function GetBoolean(roleKey As String, limitKey As String) As Boolean

        Dim raw As String = GetRaw(roleKey, limitKey)

        Return raw = "1"

    End Function


    ' 数の項目を読む（読めなければ 0 ＝ させない。-1 は無制限）
    '
    ' 【0 に落とすのは、上限として意味が通る向きだから】
    ' 「何件まで持てるか」が 0 なら1件も持てない。「何バイトまでか」が 0 なら1バイトも上げられない。
    ' どちらも「させない」になる
    Public Shared Function GetInt(roleKey As String, limitKey As String) As Integer

        Dim raw As String = GetRaw(roleKey, limitKey)
        Dim value As Integer

        If Not Integer.TryParse(raw, Globalization.NumberStyles.AllowLeadingSign,
                                Globalization.CultureInfo.InvariantCulture, value) Then
            Return 0
        End If

        ' -1（無制限）より小さい値は、書き間違いとして 0 に落とす
        If value < Unlimited Then
            Return 0
        End If

        Return value

    End Function


    ' 上限に収まっているか（Unlimited なら必ず True）
    '
    ' 【この形にしてある理由】呼ぶ側に「-1 は無制限」を毎回書かせると、
    ' いつか1か所だけ書き忘れて、**そこだけ無制限が「0件まで」になる**
    Public Shared Function IsWithin(roleKey As String, limitKey As String, amount As Long) As Boolean

        Dim limit As Integer = GetInt(roleKey, limitKey)

        If limit = Unlimited Then
            Return True
        End If

        Return amount <= limit

    End Function


    ' キャッシュを捨てて、次に読まれたときに読み直す（管理画面から値を変えたときに呼ぶ）
    Public Shared Sub Reload()

        SyncLock CacheLock
            Cached = Nothing
        End SyncLock

    End Sub


    Private Shared Function GetRaw(roleKey As String, limitKey As String) As String

        If String.IsNullOrEmpty(roleKey) OrElse String.IsNullOrEmpty(limitKey) Then
            Return Nothing
        End If

        Dim values As Dictionary(Of String, String) = GetSnapshot().Values
        Dim raw As String = Nothing

        If values Is Nothing OrElse Not values.TryGetValue(CacheKey(roleKey, limitKey), raw) Then
            Return Nothing
        End If

        Return raw

    End Function


    ' 【鍵の取り方が2通りある理由は、SiteSettings.GetSnapshot とまったく同じ】
    '   まだ一度も読めていない … SyncLock（待つ）。返せるものが無いため
    '   読み直し               … Monitor.TryEnter（待たない）。
    '                            DB が落ちていると接続のタイムアウトまで返ってこないので、
    '                            SyncLock にすると、その間に読もうとした要求が全部並ぶ
    Private Shared Function GetSnapshot() As Snapshot

        Dim current As Snapshot = Cached

        If current IsNot Nothing AndAlso Not current.ShouldRetry() Then
            Return current
        End If

        If current Is Nothing Then

            SyncLock CacheLock

                If Cached Is Nothing Then
                    Cached = Load()
                End If

                Return Cached

            End SyncLock

        End If

        ' 読み直し。取れなかったら、今の（失敗した）Snapshot をそのまま使う
        Dim taken As Boolean = False

        Try

            Monitor.TryEnter(CacheLock, taken)

            If Not taken Then
                Return current
            End If

            If Cached Is current Then
                Cached = Load()
            End If

            Return Cached

        Finally

            ' 【TryEnter は、必ず Finally で解放すること】
            ' SyncLock と違って自動では解放されない。握ったままになると、
            ' このクラスは以降二度と読み直せなくなる
            If taken Then
                Monitor.Exit(CacheLock)
            End If

        End Try

    End Function


    Private Shared Function Load() As Snapshot

        Try

            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand("SELECT RoleKey, LimitKey, LimitValue FROM RoleLimits", conn)

                    conn.Open()

                    Using reader As SqlDataReader = cmd.ExecuteReader()
                        While reader.Read()

                            If Not reader.IsDBNull(2) Then
                                values(CacheKey(reader.GetString(0), reader.GetString(1))) = reader.GetString(2)
                            End If

                        End While
                    End Using

                End Using
            End Using

            Return New Snapshot(values)

        Catch ex As Exception

            ' 【必ず知らせる】フェイルクローズは静かに効くので、知らせないと誰も気づけない
            ErrorReport.Report(ex, "RoleLimits.Load")

            ' 空の対応表＝どの項目も読めない＝すべて「させない」
            Return New Snapshot(Nothing)

        End Try

    End Function


    Private Shared Function CacheKey(roleKey As String, limitKey As String) As String
        Return roleKey & "|" & limitKey
    End Function


    ' 読んだ結果のひとかたまり
    '
    ' 【フィールドは ReadOnly。中身を書き換えないこと】
    ' 組み立て終わってから Cached へ1回で代入するので、鍵を取らずに読んでいる他のスレッドは
    ' 「古いもの」か「完成した新しいもの」のどちらかしか見ない（SiteSettings.Snapshot と同じ）
    Private NotInheritable Class Snapshot

        ' 読めなかったときは Nothing（＝すべて「させない」）
        Public ReadOnly Values As Dictionary(Of String, String)

        Private ReadOnly IsFailed As Boolean
        Private ReadOnly CreatedAt As DateTime

        Public Sub New(values As Dictionary(Of String, String))

            Me.Values = values
            Me.IsFailed = (values Is Nothing)
            Me.CreatedAt = DateTime.Now

        End Sub

        Public Function ShouldRetry() As Boolean

            If Not IsFailed Then
                Return False
            End If

            Return DateTime.Now - CreatedAt >= RetryAfterFailure

        End Function

    End Class

End Class
