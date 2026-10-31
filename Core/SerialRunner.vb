Imports System.Threading
Imports System.Web.Hosting

' 裏の作業を「1本だけ、順番に」回す小さな道具（追加）
'
' 使っているのは2か所。
'   ・SiteMail    … 並べたメールを1通ずつ送る
'   ・ErrorReport … 届いたエラーを数えて、送るメールを決める
'
' 【なぜ要るのか】
' どちらも「呼ぶ側は頼んだらすぐ戻る」形にしたい（SMTP の応答を、チャットの処理に待たせない）。
' かといって、頼まれるたびに裏の作業を1本ずつ起こすと、同時に何本も走り、
' 送る順番も、数えた記録も、ばらばらに書き換えられる。
' そこで「今1本走っていれば、それに任せる。走っていなければ1本だけ起こす」形にしてある。
'
' 【仕事の側は、溜まっている分をまとめて片付けること】
' Kick() が何回呼ばれても、走るのは1本である。1回の仕事で、並んでいる分を全部処理する作りにする
' （SiteMail の DrainOutbox、ErrorReport の Process がそうなっている）
'
' 【仕事の中で起きた例外は、Trace に書くだけ。ErrorReport へは報告しない】
' ErrorReport 自身もこの道具の上で動いている。報告すると、自分へ報告→また失敗→…の堂々巡りになる
Friend NotInheritable Class SerialRunner

    Private ReadOnly _work As Action
    Private ReadOnly _name As String

    ' 0＝走っていない、1＝走っている
    Private _running As Integer

    ' 0＝頼まれていない、1＝まだ片付けていない頼みがある
    Private _pending As Integer

    Public Sub New(work As Action, name As String)
        _work = work
        _name = name
    End Sub


    ' 仕事を頼む（すぐ戻る）
    Public Sub Kick()

        Interlocked.Exchange(_pending, 1)

        ' 走っていなければ、1本だけ起こす。走っていれば、そちらが片付ける
        If Interlocked.CompareExchange(_running, 1, 0) = 0 Then
            Start()
        End If

    End Sub


    Private Sub Start()

        ' 【QueueBackgroundWorkItem を使う理由】
        ' ASP.NET が「裏で仕事をしている最中だ」と知っている形で走らせる。
        ' アプリを止めるときに、少しだけ（最大30秒）終わるのを待ってもらえる
        ' （ただの ThreadPool だと、何も言わずに途中で打ち切られる）
        Try
            HostingEnvironment.QueueBackgroundWorkItem(Sub(ct As CancellationToken) Run())
            Return
        Catch ex As Exception
            ' ASP.NET の外で呼ばれた、アプリが止まりかけている、など。下の ThreadPool へ落とす
        End Try

        Try
            ThreadPool.QueueUserWorkItem(Sub(state As Object) Run())
        Catch ex As Exception
            ' 起こせなかった。次の Kick でまた試せるよう、「走っていない」に戻す
            Interlocked.Exchange(_running, 0)
            SafeTrace(_name & " の裏の作業を起こせませんでした： " & ex.ToString())
        End Try

    End Sub


    Private Sub Run()

        Do

            Try

                ' 頼みが残っている間は、続けて片付ける
                Do While Interlocked.Exchange(_pending, 0) = 1

                    Try
                        _work()
                    Catch ex As Exception
                        SafeTrace(_name & " の裏の作業で例外が起きました（ここからは報告しません）： " & ex.ToString())
                    End Try

                Loop

            Finally
                Interlocked.Exchange(_running, 0)
            End Try

            ' 【取りこぼしを防ぐ】
            ' 上のループを抜けてから「走っていない」に戻すまでの一瞬に Kick されると、
            ' Kick の側は「まだ走っている」と見て何もせずに戻っている。
            ' その頼みを、ここで拾い直す
        Loop While Interlocked.CompareExchange(_pending, 0, 0) = 1 AndAlso
                   Interlocked.CompareExchange(_running, 1, 0) = 0

    End Sub


    ' Trace に書く（書くことに失敗しても、何もしない）
    Friend Shared Sub SafeTrace(text As String)
        Try
            System.Diagnostics.Trace.WriteLine(text)
        Catch
        End Try
    End Sub

End Class
