' 「ログを調べる」の3枚（探す・再生・CSV）の土台
'
' 【サイト管理者だけ】Page_Load より前（OnLoad の頭）で RequireAdministrator を通す。
' 継承すれば守りが付く＝3枚それぞれに書かない（「毎回の基本」の8）。
' ログインしていない人は、その前の OnInit（AccountPageBase）でログインの画面へ。
' 外の人に見せる用の見るだけの権限（viewer）など、サイト管理者でない人は /account（ホーム）へ返す（メニューにも出ない＝Accounts/AdminMenu.vb）。
'
' 【no-store】AccountPageBase が付ける（利用者の発言・IP アドレスが出る画面なので、ブラウザに覚えさせない）
'
' 【WebMethod はここを通らない】LogReplay.aspx の LoadMore は、中で LogTool.CurrentAdministrator を呼んで確かめる
Public Class LogToolPageBase
    Inherits AccountPageBase


    Protected Overrides Sub OnLoad(ByVal e As EventArgs)

        ' 【Try/Catch の中に置かない】Redirect（endResponse = True）は ThreadAbortException を投げる
        RequireAdministrator()

        MyBase.OnLoad(e)

    End Sub


    ' 静的ファイルの住所（?v= 付き）。markup から common.AssetUrl を直接呼ばない（別のコンパイル単位で500になりうる）
    Protected Function Asset(ByVal virtualPath As String) As String
        Return common.AssetUrl(virtualPath)
    End Function


    ' 上の切り替え「探す｜見た記録」（Admin/LogToolView.vb の TabsHtml。探す画面と見た記録の画面に置く）
    Protected ReadOnly Property ToolTabsHtml As String
        Get
            Return LogToolView.TabsHtml(AppRelativeVirtualPath)
        End Get
    End Property


    ' 管理人の IP アドレス（見た記録に残す）
    Protected ReadOnly Property ClientIp As String
        Get
            Try
                Return Request.UserHostAddress
            Catch ex As Exception
                Return Nothing
            End Try
        End Get
    End Property

End Class
