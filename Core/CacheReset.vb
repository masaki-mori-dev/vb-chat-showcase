Imports System.Configuration
Imports System.Data.SqlClient

' DB を直接書き換えたあとに、サーバーのメモリの控えを全部捨てる
'
' 【何のためか】
' 控え（新しいメンバー・アイコン・メニュー・お知らせ・設定・ロールごとの上限・スタンプの目次・ロールの名前）は、
' 画面から直したときにしか捨てられない。SQL で DB を直接書き換えると、最大10分（設定などは起き直すまで）古いまま残る。
' 実際に、ダミーデータを流したあと本番のトップの「新しいメンバー」が古い表示名とアイコンのままだった。
'
' 【仕組み＝DB の合図の1行】（：「これをやったら、あれもやる」というセットの作業は、人や AI が覚えるのではなく道具に組み込む）
'   ・DB を直接書き換える道具が、流し終わったら CacheResets.RequestedAt を今の時刻にする
'   ・アプリは見回りのタイマー（2秒おき。Global.asax）に相乗りして、30秒に1回だけこの1行を読む
'   ・RequestedAt が AppliedAt より新しければ、全部の控えを捨てて AppliedAt を書く（道具はこれを見て「捨てた」を確かめる）
'   ・アプリが起きた直後も、同じ比べ方で AppliedAt を書く（起きた直後は控えが空なので、捨てても害は無い）
'
' 【却下した形】
'   ・アプリを起き直させる（FTP で Web.config を上げ直す）… 在室者が全員いったん切れる。上げ損ねるとサイト全体が止まる
'   ・管理用の URL を叩く … 道具が本番にログインする必要があり、守る口が1つ増える
'   ・控えを使うたびに DB で合図を見る … 控えの意味が無くなる
'
' 【負荷】30秒に1回、主キーで1行を読むだけ（書くのは合図があったときだけ）。DB を触るのは裏の作業（SerialRunner）で、タイマーは待たない
' 【表が無いとき・読めないとき】黙って何もしない（控えは今までどおり時間で新しくなる）。報告メールは出さない（表を作る前に発行しても、毎回メールにならないように）
Public NotInheritable Class CacheReset

    Private Sub New()
    End Sub

    Private Const ConnectionName As String = "VbChatConnection"

    ' 見に行く間隔
    Private Shared ReadOnly CheckInterval As TimeSpan = TimeSpan.FromSeconds(30)

    Private Shared ReadOnly Runner As New SerialRunner(AddressOf CheckOnce, "CacheReset")
    Private Shared _nextCheckAt As DateTime = DateTime.MinValue
    Private Shared ReadOnly SyncRoot As New Object()


    ' 見回りのタイマーから呼ぶ。時刻を比べるだけですぐ戻る
    Public Shared Sub Tick()

        Dim now As DateTime = DateTime.UtcNow

        SyncLock SyncRoot
            If now < _nextCheckAt Then
                Return
            End If
            _nextCheckAt = now + CheckInterval
        End SyncLock

        Runner.Kick()

    End Sub


    ' 全部の控えを捨てる（ここに無い控えを足したら、ここにも1行足すこと）
    Public Shared Sub ResetAll()
        NewMembers.Invalidate()
        SiteArticles.Invalidate()
        SiteNotices.Invalidate()
        AccountAvatar.InvalidateAll()
        AccountSession.InvalidateRoleNames()
        SiteSettings.Reload()
        RoleLimits.Reload()
        StampCatalog.Reload()
    End Sub


    ' 裏の作業：合図を読んで、新しければ捨てる
    Private Shared Sub CheckOnce()

        Try

            Using conn As New SqlConnection(ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString)
                conn.Open()

                Dim pending As Boolean

                Using cmd As New SqlCommand(
                    "IF OBJECT_ID(N'CacheResets', N'U') IS NULL SELECT CAST(0 AS BIT) " &
                    "ELSE SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM CacheResets WHERE Id = 1 AND RequestedAt > AppliedAt) THEN 1 ELSE 0 END AS BIT);", conn)
                    pending = CBool(cmd.ExecuteScalar())
                End Using

                If Not pending Then
                    Return
                End If

                ResetAll()

                ' 【捨てたあとに書く】道具はこの時刻を見て「捨て終わった」と判断する
                Using cmd As New SqlCommand("UPDATE CacheResets SET AppliedAt = SYSDATETIME() WHERE Id = 1;", conn)
                    cmd.ExecuteNonQuery()
                End Using

            End Using

        Catch ex As Exception
            ' 黙って何もしない（上の【表が無いとき・読めないとき】）
            Trace.WriteLine("CacheReset: " & ex.Message)
        End Try

    End Sub

End Class
