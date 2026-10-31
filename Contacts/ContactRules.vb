Imports System.Text.RegularExpressions

' 問い合わせの決まり（問い合わせページ）
'
' 【文字数の上限・種類の3択・メールアドレスの形を、ここ1か所に持つ】
' Articles/ArticleRules.vb と同じ立ち位置。画面（Contacts/Contact.aspx）も、
' 受け口（Contacts/ContactRequest.vb）も、ここを通して確かめる。
' C#／ASP.NET Core MVC へ移るとき、画面は書き直しになるが、ここはそのまま持っていける。
'
' 【SiteSettings に入れないもの】
' 文字数の上限と、人には見えない入力欄と、3秒の連打止めは、**設定ではなく防御**なので
' コードの定数にしてある（AccountPassword.MinLength・ArticleRules.BodyMaxLength と同じ判断）。
' DB に置くと、管理画面から弱められることになる。
' SiteSettings に置いたのは「1時間に何件まで」と「いつまで残すか」の2つだけ
Public NotInheritable Class ContactRules

    Private Sub New()
    End Sub


    ' === 長さ ===
    '
    ' 【DB の列幅と同じ数にしてある】（Database/CreateTable/ContactMessages.sql）
    ' ここを広くすると、画面が通した長さで SQL 例外になり、その1件が丸ごと失われる
    ' （NicknameMaxLength と ChatMessages.Nickname で踏んだ罠と、同じ形）。
    ' 逆に狭くするのは安全（DB に入りきる）

    ' 内容（必須）
    Public Const BodyMaxLength As Integer = 2000

    ' お名前（任意）
    Public Const NameMaxLength As Integer = 50

    ' 返信先のメールアドレス（任意）
    Public Const EmailMaxLength As Integer = 256


    ' 問い合わせの種類
    '
    ' 【10刻みで採番してある】あとから間に差し込めるように
    ' （ChatMessageType・ArticleCategories.SortMode と同じ決まり）。
    ' 【DB の CK_ContactMessages_Kind と同じ3つにそろえること】片方だけ増やすと、
    ' 画面では選べるのに保存で SQL 例外になる
    Public Enum ContactKind
        BugReport = 10
        Feedback = 20
        Other = 30
    End Enum


    ' 選べる種類の一覧（白リスト）＝画面の選択肢と、丸めの正
    '
    ' 【DB（SiteSettings）に出さない】白リストは防御なので、コード側が持つ
    ' （AllowedMessageCounts・ChatRoomRules.AllowedMaxUsers と同じ判断）。
    ' **並び順が、画面の並び順そのものである**（読み上げソフトもこの順に読む）。
    '
    ' 【マークアップに <asp:ListItem> をベタ書きしないこと】
    ' 画面（Contacts/Contact.aspx.vb の BindKinds）が、ここから <asp:DropDownList> へ流し込む。
    ' ベタ書きすると、種類を1つ増やした日に、白リストと画面が食い違う
    ' （Articles/ArticleRules.vb の AllSortModes と、まったく同じ形にそろえてある）
    Public Shared ReadOnly Property AllowedKinds As ContactKind()
        Get
            Return New ContactKind() {ContactKind.BugReport, ContactKind.Feedback, ContactKind.Other}
        End Get
    End Property


    ' 画面のいちばん上に置く「まだ選んでいない」の行
    '
    ' 【値は空文字】選べる一覧（AllowedKinds）には入れない。**保存もしない**
    ' （DB の Kind は 10/20/30 のまま、CHECK 制約も変えていない）。
    ' 選ばずに進もうとしたら、画面の RequiredFieldValidator（InitialValue=""）と
    ' サーバー側の ValidateKind の両方が「種類を選んでください。」と断る
    Public Const KindPromptText As String = "選んでください"


    ' 画面とメールに出す、種類の名前
    '
    ' 【1か所で持つ】画面の <option>・メールの件名と本文・（将来の）管理画面の一覧が、同じ文字列を使う
    Public Shared Function KindLabel(kind As ContactKind) As String

        Select Case kind
            Case ContactKind.BugReport
                Return "不具合の報告"
            Case ContactKind.Feedback
                Return "ご意見・ご要望"
            Case ContactKind.Other
                Return "その他"
            Case Else
                ' ここへは来ない（NormalizeKind を通っている）。来たら、DB に知らない値が入っている
                Return "（不明）"
        End Select

    End Function


    ' 画面から来た文字列を、3択のどれかに読む（読めなければ False）
    '
    ' 【、「その他へ落とす」形から変えた】
    ' それまでは、一覧に無ければ黙って「その他」にしていた。
    ' 「選んでください」を足して**選ぶことを必須にした**ので、落とし先があると
    ' 「選ばずに送ったのに、その他として届く」ことになり、必須にした意味が消える。
    ' 一覧に無いものは、空でも知らない値でも、まとめて断る（下の ValidateKind）
    Public Shared Function TryGetKind(raw As String, ByRef kind As ContactKind) As Boolean

        Dim number As Integer

        If Integer.TryParse(If(raw, "").Trim(), number) Then
            For Each candidate As ContactKind In AllowedKinds
                If CInt(candidate) = number Then
                    kind = candidate
                    Return True
                End If
            Next
        End If

        Return False

    End Function


    ' 種類（必須）
    '
    ' 【空でも、知らない値でも、同じ文言で断る】
    ' 空は「選ばずに進んだ」とき。知らない値は、画面が壊れているか開発者ツールで書き換えたときで、
    ' どちらも利用者にできることは「選び直す」ことだけである
    Public Shared Function ValidateKind(raw As String) As String

        Dim kind As ContactKind

        If Not TryGetKind(raw, kind) Then
            Return "種類を選んでください。"
        End If

        Return Nothing

    End Function


    ' === 入力の確かめ ===
    '
    ' どれも「駄目な理由」を返し、問題が無ければ Nothing を返す。
    ' 【画面に出すときは必ずエスケープする】ここが返すのはこちらで決めた文言だけだが、決まりは決まり


    ' 内容（必須）
    Public Shared Function ValidateBody(body As String) As String

        Dim text As String = Normalize(body)

        If text.Length = 0 Then
            Return "内容を入力してください。"
        End If

        If text.Length > BodyMaxLength Then
            Return "内容が長すぎます（" & BodyMaxLength.ToString("#,0") & "文字まで）。"
        End If

        Return Nothing

    End Function


    ' お名前（任意）
    Public Shared Function ValidateName(name As String) As String

        Dim text As String = Normalize(name)

        If text.Length > NameMaxLength Then
            Return "お名前が長すぎます（" & NameMaxLength & "文字まで）。"
        End If

        Return Nothing

    End Function


    ' 返信先のメールアドレス（任意）
    '
    ' 【ゆるく見る。厳しくしすぎない】メールアドレスの正しさを完全に判定する形は存在しない
    ' （RFC のとおりに書くと、実在するアドレスまで弾く）。ここで見るのは
    '   ・長すぎないか
    '   ・@ がちょうど1つあり、その前後が空でないか
    '   ・@ の後ろに「.」があるか
    '   ・**改行・制御文字・空白が混ざっていないか** ← これがいちばん大事（次の節）
    ' だけにして、あとは開発者が目で見る（返事を書くのは人である）。
    '
    ' 【なぜ改行を止めるのか＝見出しの差し込み（ヘッダーインジェクション）】
    ' この値はメールの Reply-To に入る。改行が通ると
    '     someone@example.com(改行)Bcc: victim@example.com
    ' のような値で、メールの見出しを1行増やされる＝このサイトが迷惑メールの踏み台になる。
    ' System.Net.Mail の MailAddress も改行入りを例外にするので守りは2枚あるが、
    ' **手前で断るほうが、理由を利用者に返せる**
    Public Shared Function ValidateEmail(email As String) As String

        Dim text As String = Normalize(email)

        If text.Length = 0 Then
            ' 任意なので、空は正しい
            Return Nothing
        End If

        If text.Length > EmailMaxLength Then
            Return "メールアドレスが長すぎます（" & EmailMaxLength & "文字まで）。"
        End If

        If Not EmailPattern.IsMatch(text) Then
            Return "メールアドレスの形が正しくないようです。返事が不要なら、空のままにしてください。"
        End If

        Return Nothing

    End Function


    ' 上の ValidateEmail が使う形
    '
    ' 【^ と $ ではなく \A と \z にしてある】.NET の $ は「末尾の改行1つ」の手前にも当たるため、
    ' "a@b.com(改行)Bcc: ..." のうち最後が改行で終わる値を通してしまう道が残る。
    ' \z は本当の末尾しか指さない。**メールの見出しに入る値なので、そこを曖昧にしない**。
    ' [^\s@]＝空白（改行・タブを含む）と @ 以外。制御文字は、あわせて Normalize が落としている
    Private Shared ReadOnly EmailPattern As New Regex(
        "\A[^\s@]+@[^\s@]+\.[^\s@]+\z", RegexOptions.Compiled)


    ' 入力を、保存・判定に使う形にそろえる
    '
    ' ① 3つの改行（\r\n・\r・\n）を \n に寄せる
    ' ② 改行とタブ以外の制御文字を落とす
    ' ③ 3つ以上続く改行を2つ（＝空行1つ）にまとめる
    ' ④ 前後の空白を落とす
    '
    ' 【\r だけを送られる道がある】画面の JavaScript は \r\n しか見ていないので、
    ' 直接 POST すれば \r を千個並べられる（チャットの発言で実際に塞いだ罠と同じ形）。
    ' そのため①を先に必ず通す。
    '
    ' 【制御文字を落とすのは、メールの本文と画面の両方のため】
    ' 保存する値・メールに入れる値・画面へ返す値は、すべてこれを通った文字列にそろえてある
    Public Shared Function Normalize(text As String) As String

        If String.IsNullOrEmpty(text) Then
            Return ""
        End If

        Dim unified As String = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)

        Dim sb As New Text.StringBuilder(unified.Length)

        For Each ch As Char In unified

            If ch = ChrW(10) OrElse ch = ChrW(9) Then
                sb.Append(ch)
            ElseIf Not Char.IsControl(ch) Then
                sb.Append(ch)
            End If

        Next

        Return Regex.Replace(sb.ToString(), "\n{3,}", vbLf & vbLf).Trim()

    End Function


    ' 1行にそろえる（お名前・メールアドレス用。改行そのものを許さない欄）
    '
    ' 【Normalize と分けてある】内容の欄は改行を残したいが、この2つは1行である。
    ' 改行が残っていると、メールの見出し（件名・Reply-To）と、DB の1行の見た目が崩れる
    Public Shared Function NormalizeOneLine(text As String) As String
        Return Normalize(text).Replace(vbLf, " ").Replace(vbTab, " ").Trim()
    End Function

End Class
