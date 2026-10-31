Imports Microsoft.AspNet.SignalR
Imports Microsoft.AspNet.SignalR.Hubs

' SignalR のハブのメソッドで起きた未処理のエラーを、ErrorReport へ知らせる
'
' 【なぜ、これが要るのか】
' ハブのメソッド（JoinRoom・SendMessage など）で例外が漏れても、Application_Error には来ない。
' SignalR が自分で捕まえて、呼んだ画面へ「失敗しました」と返すだけで終わる。
' つまり、ここに口を付けないと、ハブの中の不具合は画面の側でしか分からない。
'
' 登録は Startup.vb の1行（GlobalHost.HubPipeline.AddModule）。MapSignalR より前に書くこと
'
' 【動きは変えない】
' 知らせたあと、既定の処理（MyBase.OnIncomingError）へそのまま渡す。
' 画面の側から見た振る舞い（呼び出しが失敗として返る）は、今までと同じ
Public Class ErrorReportHubModule
    Inherits HubPipelineModule

    Protected Overrides Sub OnIncomingError(exceptionContext As ExceptionContext,
                                            invokerContext As IHubIncomingInvokerContext)

        Try

            Dim ex As Exception = exceptionContext.Error

            ' HubException は「画面へ理由を伝えるために、わざと投げる」ためのもの。不具合ではない
            ' （今は使っていないが、将来使ったときにメールが飛ばないように）
            If ex IsNot Nothing AndAlso Not TypeOf ex Is HubException Then

                Dim methodName As String = "（不明）"
                Dim path As String = Nothing

                Try
                    methodName = invokerContext.MethodDescriptor.Hub.Name & "." & invokerContext.MethodDescriptor.Name
                Catch
                End Try

                Try
                    path = invokerContext.Hub.Context.Request.Url.AbsolutePath
                Catch
                End Try

                ErrorReport.ReportUnhandled(ex, "SignalR のハブ（" & methodName & "）", path, methodName)

            End If

        Catch inner As Exception
            SerialRunner.SafeTrace("ハブのエラーを知らせられませんでした： " & inner.ToString())
        End Try

        MyBase.OnIncomingError(exceptionContext, invokerContext)

    End Sub

End Class
