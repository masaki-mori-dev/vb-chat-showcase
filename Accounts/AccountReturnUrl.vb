' ログインしたあと、元のページへ戻すための「戻り先」の決まり
'
' 【何のためか】
' ログインせずに /account/articles を開くと、ログインの画面へ飛ばされる。
' 以前はログインしてもそこに留まり、もう一度開き直す必要があった。
' 飛ばすときに「元のページ」を ?returnUrl= で持たせ、ログインできたらそこへ戻す。
'
' 【いちばんの罠＝オープンリダイレクト】
' ?returnUrl= は、URL を作る人が自由に書ける。そのまま Redirect すると、
'   https://vb-chat.example.com/account/login?returnUrl=https://偽物のサイト/
' というリンクを送りつけるだけで、**本物のログインの画面を通したあと、偽物へ連れて行ける**
' （利用者から見ると「本物でログインした直後」なので、偽物のログインの画面を疑わない）。
'
' 【だから、判定はここ1か所だけにする】
' 飛ばす側（AccountPageBase）も、受け取る側（Login.aspx.vb）も、必ずこの Normalize を通す。
' 2か所に書くと、片方だけ緩めた日に穴が開く。
'
' 【許すのは「このサイトの中の /account の下」と、決まった2つの画面だけ】（白リストの考え方）
' 「危ない形を並べて弾く」のではなく「許す形を決めて、それ以外は全部 /account へ」。
' 危ない形の書き方は、思いつかないものが必ずある（下の一覧も、弾いた理由の説明であって、網羅ではない）。
'
' 【ラウンジ（/chat/lounge）とトップ（/）を足した】（基本ルールC＝
' ログインへ誘うリンクは、ログインが済んだら押した元の画面へ戻す）。ラウンジの「ログインすると、アカウントIDを紐づけられます」と、
' トップの同じ1行・右上の「ログイン」から来た人を、元の画面へ戻すため。
' **この2つは完全一致だけ**（AllowedExactTargets）。/chat/room などを前方一致で許さないこと
' （部屋は入室の手順を通さないと開けない画面で、戻しても行き止まりになる。許す形を広げるほど、穴を探す場所が増える）
Public NotInheritable Class AccountReturnUrl

    Private Sub New()
    End Sub

    ' クエリ文字列の名前
    Public Const QueryName As String = "returnUrl"

    ' 戻り先が使えないときの行き先
    Private Const Fallback As String = AccountPageBase.HomeUrl   ' "/account"

    ' 長すぎるものは使わない（URL を延々と伸ばされても、判定の手間が膨らまないように）
    Private Const MaxLength As Integer = 512

    ' /account の外で、戻り先に許す画面（完全一致だけ。クエリ文字列が付いたものも許さない）
    Private Shared ReadOnly AllowedExactTargets As String() = {"/chat/lounge", "/"}


    ' 戻り先を確かめる。使ってよければそのまま、だめなら "/account" を返す
    '
    ' 弾くもの（理由つき）：
    '   ・空・長すぎる                  … 何も無いのと同じ
    '   ・"/account" で始まらない        … /account の外。"https:" などスキーム付きも、ここで落ちる
    '   ・"/accountX" のような続き方     … 前方一致だけだと "/accounts-evil" を通してしまう。
    '                                      "/account" のあとは「終わり」「/」「?」のどれかに限る
    '   ・"//" で始まる                 … ブラウザは「//偽物.example/」を別のサイトとして扱う
    '                                      （"/account" で始まる時点で起きないが、念のため明示）
    '   ・"\" を含む                    … ブラウザによっては "\" を "/" と読み替える（"/\偽物.example"）
    '   ・".." を含む                   … "/account/../" で /account の外へ出られる
    '   ・%2f %5c %2e を含む           … 上の3つを、%エンコードで隠したもの
    '   ・制御文字（改行など）を含む     … 応答のヘッダーに改行を混ぜる攻撃（ASP.NET も止めるが、ここでも止める）
    '   ・ログイン・ログアウト・作る画面 … 戻った瞬間にまたログインの画面・ログアウトになり、行き止まりになる
    Public Shared Function Normalize(value As String) As String

        If String.IsNullOrEmpty(value) OrElse value.Length > MaxLength Then
            Return Fallback
        End If

        For Each ch As Char In value
            If Char.IsControl(ch) Then
                Return Fallback
            End If
        Next

        If value.StartsWith("//", StringComparison.Ordinal) Then
            Return Fallback
        End If

        If value.IndexOf("\"c) >= 0 OrElse value.Contains("..") Then
            Return Fallback
        End If

        Dim lower As String = value.ToLowerInvariant()

        If lower.Contains("%2f") OrElse lower.Contains("%5c") OrElse lower.Contains("%2e") Then
            Return Fallback
        End If

        ' 決まった2つの画面（完全一致。上の制御文字などの検査は、もう通っている）
        If Array.IndexOf(AllowedExactTargets, value) >= 0 Then
            Return value
        End If

        ' "/account" そのもの、または "/account/…"・"/account?…"
        ' 【大文字・小文字は区別する】ルートは小文字で登録してあるので、
        ' 正しいリンクから来たものは必ず小文字で始まる
        If Not (value = "/account" OrElse
                value.StartsWith("/account/", StringComparison.Ordinal) OrElse
                value.StartsWith("/account?", StringComparison.Ordinal)) Then
            Return Fallback
        End If

        ' 行き止まりになる画面
        Dim path As String = lower
        Dim q As Integer = path.IndexOf("?"c)

        If q >= 0 Then
            path = path.Substring(0, q)
        End If

        If path = AccountPageBase.LoginUrl OrElse path = "/account/logout" OrElse path = "/account/setup" Then
            Return Fallback
        End If

        Return value

    End Function


    ' 決まったページの URL に、戻り先を付けて返す
    ' ログイン・新規登録・忘れたときの画面の間で、?returnUrl= を持ち回すために使う。
    ' 【必ず Normalize を通す】受け取った値をそのまま付けない。使えない値（と /account そのもの）なら、戻り先を付けずに pageUrl だけ。
    ' 【メールを通る流れには運ばない】新規登録の確認メール・再設定のメールを開いたあとは、今までどおり /account（設計の担当と決めた）
    Public Shared Function WithReturn(pageUrl As String, returnValue As String) As String

        Dim target As String = Normalize(returnValue)

        If target = Fallback Then
            Return pageUrl
        End If

        Return pageUrl & "?" & QueryName & "=" & HttpUtility.UrlEncode(target)

    End Function


    ' ログインの画面の URL を、戻り先つきで組み立てる（AccountPageBase が使う）
    '
    ' 【渡す前にも Normalize を通す】
    ' 受け取る側でも通すので、守りとしては要らないが、
    ' 使えない戻り先を URL に載せて持ち回らないため
    Public Shared Function LoginUrlFor(currentUrl As String) As String
        ' 行き先が /account そのもの（か、使えない値）なら付けない（URL を短く保つ）。組み立ては WithReturn の1か所
        Return WithReturn(AccountPageBase.LoginUrl, currentUrl)
    End Function

End Class
