Imports Microsoft.AspNet.SignalR
Imports Microsoft.Owin
Imports Owin

<Assembly: OwinStartup(GetType(vb_chat.Startup))>

Public Class Startup
    Public Sub Configuration(app As IAppBuilder)

        ' ハブのメソッドで起きた未処理のエラーを、ErrorReport へ知らせる。
        ' ハブの例外は Application_Error には来ないので、ここに口を付けないと気づけない。
        ' 【MapSignalR より前に書くこと】ハブの配線（パイプライン）ができる前に足すのが、SignalR の決まり
        GlobalHost.HubPipeline.AddModule(New ErrorReportHubModule())

        app.MapSignalR()
    End Sub
End Class
