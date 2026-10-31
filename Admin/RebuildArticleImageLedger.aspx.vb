Imports System.Web.UI

' 記事の画像の台帳を作り直す
'
' 【何のためにあるか】1日1回の片付け（Core/LogCleanup.vb の4つ目）は、
' **台帳（ArticleImageFiles・ArticleImageUses）だけ**を見る。本文を丸ごと読まないので軽いが、
' そのぶん台帳とディスクがずれると自分では直せない。ずれを直すのが、このページである。
'
' 押すのは、次の2つのときだけ。
'   ・台帳を作った日の、一度きりの手当て（それまでに在った画像を、台帳に載せる）
'   ・1日1回の集計メールに「台帳に無いファイルが◯枚ある」と出たとき
'
' 【ファイルは1枚も消さない】載せる・作り直すだけ。消すのは1日1回の片付けの役目である
'
' 【Admin/GenerateIconSprite.aspx と同じ立ち位置・同じ守り方】
' 新しいサーバーへ移したときに1回叩く道具で、利用者は使わない。**サイト管理者のログインが要る**。
' ログインの画面からここへは戻ってこないので、/account/login を通ってから開き直すこと
' （AccountReturnUrl が許す戻り先は /account の下だけ）
'
' 【、AccountPageBase に乗せた】それまでは Page を継承して、ログインの確かめを自分で書いていた
' （GenerateIconSprite と「同じ形」と書いてあったのに、あちらだけが土台に乗っていた）。
' 土台に乗せたので、ログインの確かめと no-store（ログインの画面へ返す応答にも付く）は、ここに書かなくてよい
Public Class RebuildArticleImageLedger
    Inherits AccountPageBase

    ' ログインの確認は AccountPageBase がやる。ここでは「サイト管理者かどうか」だけを見る（GenerateIconSprite と同じ形）
    '
    ' 【ロールの名前を、コードにべた書きしている】画面に出す日本語の名前は DB（Roles.DisplayName）から
    ' 引く決まりだが、「誰に許すか」は**動いてよい値ではない**ので、定数（RoleKeys.Administrator）で持つ
    Protected Overrides Sub OnInit(e As EventArgs)

        MyBase.OnInit(e)

        Dim account As AccountStore.AccountRecord = AccountSession.Current(Context)

        If account Is Nothing OrElse
           Not String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal) Then

            ' この Redirect は既定（endResponse = True）で呼ぶ。Try/Catch の中に置かないこと
            Response.Redirect(AccountPageBase.HomeUrl)

        End If

    End Sub


    Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load

        ' 控えさせない（no-store）は、AccountPageBase の OnInit が付けている（ここには書かない＝毎回の基本の8）

    End Sub


    Protected Sub RunButton_Click(sender As Object, e As EventArgs)

        Try

            Dim result As ArticleImageRebuildResult = ArticleImages.Rebuild()

            ResultText.Text =
                "<p style=""color: #15803D;""><strong>記事の画像の台帳を作り直しました。</strong></p>" &
                "<ul>" &
                "<li>台帳へ載せた画像：" & result.Adopted & " 枚</li>" &
                "<li>読み直した本文：" & result.Bodies & " 件（記事＋編集中の控え）</li>" &
                "</ul>"

        Catch ex As Exception

            ErrorReport.Report(ex, "RebuildArticleImageLedger")

            ResultText.Text = "<p style=""color: #B91C1C;""><strong>作り直せませんでした。</strong>" &
                              "エラー報告のメールを見てください。</p>"

        End Try

    End Sub

End Class
