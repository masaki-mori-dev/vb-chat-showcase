' 「ページが見つかりません」（住所のルール。決まりは Core/CanonicalUrl.vb の先頭）
'
' 【状態の番号は 404】本番の IIS が httpErrors でこのページを中で開くとき、元の番号が 403（中身を見せないフォルダ）でも、
' 利用者と検索エンジンへの合図は「無い」にそろえる（開発者の決まり＝合図は 404 のまま）。
' 【TrySkipIisCustomErrors】このページ自身の 404 を、IIS にもう一度差し替えさせない
' （「本文が空の404は、本番のIISが…」の落とし穴）
Public Class NotFoundPage
    Inherits System.Web.UI.Page

    Private Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load

        Title = "ページが見つかりません"
        PageResponse.MarkNotFound(Response)

    End Sub


    ' ポストバックとして扱わない（送られてきた本文を読まない）
    '
    ' IIS がこのページを中で開くとき、元の要求が POST なら本文ごと渡ってくることがある。
    ' よそのページの __VIEWSTATE を自分のものとして読もうとすると 500 になるので、何が来ても「初めて開いた」扱いにする
    Protected Overrides Function DeterminePostBackMode() As System.Collections.Specialized.NameValueCollection
        Return Nothing
    End Function

End Class
