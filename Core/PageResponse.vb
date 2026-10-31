' 応答そのものに印を付ける道具（404・503・控えさせない）。
'
' 【、Articles/ArticleDisplay.vb から Core/ へ移した】
' 公開のプロフィールページ（ブログ型の②）からも呼ぶようになり、**呼び元が2つのフォルダにまたがった**ため
' （「何をどこに置くか」＝2つ以上の機能から使うものは Core/）。
' 中身は1行も変えていない。
'
' 【ここに画面の組み立てを足さないこと】ここに在るのは「応答のヘッダーを決める」ことだけである。
' 一覧の HTML を組み立てるような、記事だけの仕事は Articles/ArticleDisplay.vb に残してある
Public Class PageResponse

    ' ログアウトしたあと、戻るボタンで見えないようにする（Articles/Edit.aspx.vb と同じ3行）
    Public Shared Sub SetNoStore(response As HttpResponse)

        response.Cache.SetCacheability(HttpCacheability.NoCache)
        response.Cache.SetNoStore()
        response.Cache.SetExpires(DateTime.UtcNow.AddDays(-1))

    End Sub


    ' 404 にする（ページの本文はそれぞれが出す）
    '
    ' 【TrySkipIisCustomErrors】本番の IIS は、ページが返した404をエラー画面に差し替えることがある
    ' （「本文が空の404は、本番のIISが…」の落とし穴）。
    ' 【控えさせない】持ち主があとで公開したとき、古い404が出ないように
    Public Shared Sub MarkNotFound(response As HttpResponse)

        response.StatusCode = 404
        response.TrySkipIisCustomErrors = True
        SetNoStore(response)

    End Sub


    ' DB が読めなかったとき（理由は出さない。エラー報告メールで届く）
    Public Shared Sub MarkTrouble(response As HttpResponse)

        response.StatusCode = 503
        response.TrySkipIisCustomErrors = True
        SetNoStore(response)

    End Sub

End Class
