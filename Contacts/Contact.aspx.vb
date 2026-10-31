' 問い合わせの画面
'
' 【判断は Contacts/ContactRequest.vb】ここは入力を渡して、答えを出すだけ。
' ログインしていなくても開ける（誰でも使える口である）。
'
' ============================================================
' 【3つの画面を、1つのページで切り替える】
' ============================================================
'   ① 入力（InputView）　… 書く　　　　　　　　見出し「お問い合わせ」
'   ② 確認（ConfirmView）… 書いたものを見せて、  見出し「入力内容の確認」
'                            「戻って直す」か「送信する」
'   ③ 完了（DoneView）　 … 送れたことだけを出す  見出し「送信完了」
'
' 【送ったあとは PRG（Post / Redirect / Get）】
' 送信が済んだら /contact?done=sent へ転送し、完了の画面は GET で出す。
' こうすると、完了の画面で F5 を押しても、もう一度送られない。
'
' 【確認の画面を通らずに「送信」を POST されても大丈夫】
' 送信のときに、サーバー側でもう一度ぜんぶ確かめる（ContactRequest.Submit）。
' 確認の画面は親切であって、防御ではない。
'
' 【歯止め（3秒・1時間に5件）は、実際に送ったときだけ数える】
' 確認の画面へ進むだけでは数えない。数えるのは ContactRequest.Submit の中だけで、
' 確認のときに呼ぶ ContactRequest.Validate は、1つも数えない。
'
' 【画面に数字をべた書きしないこと】文字数の上限は ContactRules から出す。
' 書くと、上限を変えた日にここだけ古いまま残る（発言の文字数のカウンタと同じ決まり）
Public Class ContactPage
    Inherits System.Web.UI.Page


    ' 完了の画面を出すための合図（PRG の転送先に付ける）
    '
    ' 【文言そのものをクエリ文字列で運ばない】（決まり）
    ' 「?msg=〇〇」にすると、誰でもその文字列を作れて、リンクを送りつけるだけで
    ' 他人の画面に好きな文を出せる。ここで運ぶのは「送れた」という合図だけで、文言はこちらが持つ
    Private Const DoneQueryName As String = "done"
    Private Const DoneQueryValue As String = "sent"

    ' 同じ内容を、もう受け付けていたときの合図
    '
    ' 【別の合図にしてある理由】完了の画面で出す文言が違うため。
    ' **文言そのものはクエリ文字列で運ばない**（上と同じ決まり）ので、合図だけを分けている
    Private Const DuplicateQueryValue As String = "dup"

    ' 画面ごとの見出し
    Private Const HeadingInput As String = "お問い合わせ"
    Private Const HeadingConfirm As String = "入力内容の確認"
    Private Const HeadingDone As String = "送信完了"

    ' MultiView の番号（マークアップの View の並びと同じにすること）
    Private Const StepInput As Integer = 0
    Private Const StepConfirm As Integer = 1
    Private Const StepDone As Integer = 2


    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        ' 【送ったあとに戻る・F5 で、もう一度送られないように】
        ' 問い合わせは「押したら1件増える」操作なので、キャッシュに残さない
        ' （アカウントの画面と同じ形。あちらは中身が秘密だから、こちらは二重送信を避けるため）
        ' 控えさせない（中身は Core/PageResponse.vb の1か所。ここに3行を書き写さないこと＝毎回の基本の8）
        PageResponse.SetNoStore(Response)

        ' 【包む仕掛けを、画面へつなぐ】（Scripts/site.js の「7」）
        '
        '   data-encode-on-submit … 包んだ値を入れる、見えない欄の id
        '   data-encoded-prefix　 … 包んだものの先頭の印（サーバーと1か所で持つ）
        '
        ' 【マークアップの属性に「<%」で始まる式を書かないこと】
        ' runat="server" を付けた要素の属性にコードブロックを書くと、
        ' 「コードブロックはこのコンテキストではサポートされていません」でページが止まる
        ' 。コードビハインドから Attributes に入れる
        BodyBox.Attributes("data-encode-on-submit") = BodyEncoded.ClientID
        BodyBox.Attributes("data-encoded-prefix") = BodyTransport.EncodedPrefix

        If IsPostBack Then

            ' 【包んで送られてきた内容を、ここで元に戻す】
            ' Page_Load は、送られてきた値が入ったあと・検査（Validator）と押されたボタンの処理の**前**に走る。
            ' ここで戻して BodyBox へ入れておけば、そのあとの検査も保存も、今までどおり
            ' 「内容＝ふつうの文字列」として扱える（Articles/Edit.aspx.vb の DecodeBody と、まったく同じ形）。
            '
            ' 【ここで入れないと、検査が「内容を入力してください」で止まる】
            ' 見えている欄は、送る瞬間に disabled にしてあるので POST に乗らない
            ' （＝サーバーから見ると空）。RequiredFieldValidator はその欄を見ている
            DecodeBody()

            ' 【前の回に付けた「この欄が悪い」の印を消す】ViewState に残るため（Core/FormErrors.vb）
            FormErrors.Clear(NameBox, EmailBox, KindList, BodyBox, ConfirmButton)

            Return

        End If

        HeadingText.Text = HeadingInput

        BindKinds()

        BodyHintText.Text = HttpUtility.HtmlEncode(
            ContactRules.BodyMaxLength.ToString("#,0") &
            "文字まで。不具合の報告のときは、どの画面で・何をしたら・どうなったかを書いていただけると助かります。")

        Dim note As String = HttpUtility.HtmlEncode(
            "いただいた内容は、管理人への連絡と、返事を書くためだけに使います。" &
            "ほかの人に見せることはありません。")

        PrivacyNoteText.Text = note
        PrivacyNoteText2.Text = note

        ' 【完了の画面は、GET で出す】（PRG）
        ' 送信のあとの転送で、ここへ来る。直接このURLを打っても出るが、
        ' 出るのは1行だけで、中身は何も見えない
        Dim doneSignal As String = Request.QueryString(DoneQueryName)

        If String.Equals(doneSignal, DoneQueryValue, StringComparison.Ordinal) Then
            ShowDone(False)
        ElseIf String.Equals(doneSignal, DuplicateQueryValue, StringComparison.Ordinal) Then
            ShowDone(True)
        End If

    End Sub


    ' ============================================================
    ' ① 入力 →「確認する」
    ' ============================================================
    Protected Sub ConfirmButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles ConfirmButton.Click

        ' 【ロボット避けは、いちばん先に見る】（Contacts/ContactRequest.vb の【ロボット避け】）
        ' 何か入っていたら、**完了の画面へ送って黙って捨てる**。
        ' 断る理由を返すと、ロボットの作り手に「この欄が罠だ」と教えることになる。
        ' ここでは数えも保存もしないので、ロボットの送信で本人の枠が減ることもない
        If Not String.IsNullOrWhiteSpace(TrapBox.Text) Then
            RedirectToDone(False)
            Return
        End If

        ' 【画面側の検査を、サーバー側でも必ず見る】
        ' Validator の画面側は親切であって、防御ではない（JavaScript を切れば素通りする）。
        '
        ' 【ここへ来るのは、JavaScript が動かないときだけ】
        ' 動いていれば Page_ClientValidate が止めるので、ポストバックそのものが起きない。
        ' そのとき画面の上には何も出ないので、**断った理由を、ここでも出す**
        ' （Articles/Edit.aspx.vb の「断った理由を、2か所に出す」と同じ考え方）
        If Not Page.IsValid Then
            ShowError(FirstValidatorMessage())
            Return
        End If

        ' 【包みをほどけなかったときは、ここで断る】（Page_Load の DecodeBody）
        If BodyDecodeFailed() Then
            Return
        End If

        ' 【確認へ進むだけなので、歯止めは数えない】（Validate は1つも数えない）
        Dim result As ContactRequest.ContactResult =
            ContactRequest.Validate(NameBox.Text, EmailBox.Text, KindList.SelectedValue, BodyBox.Text)

        If Not result.Ok Then
            ShowError(result.Message, result.Field)
            Return
        End If

        ShowConfirm(result)

    End Sub


    ' ============================================================
    ' ② 確認 →「戻って直す」
    ' ============================================================
    Protected Sub BackButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles BackButton.Click

        ' 【書いた内容は、そのまま残る】
        ' お名前・返信先・種類は ViewState が、内容は Page_Load の DecodeBody が、
        ' 見えない欄（BodyEncoded）から入れ直している
        ShowInput()

    End Sub


    ' ============================================================
    ' ② 確認 →「送信する」
    ' ============================================================
    Protected Sub SendButton_Click(ByVal sender As Object, ByVal e As System.EventArgs) _
        Handles SendButton.Click

        ' 【確認の画面を通らずに POST されても大丈夫なように、ここでぜんぶ確かめ直す】
        ' ロボット避けの欄・種類・長さ・メールアドレス・歯止めは、すべて ContactRequest.Submit の中にある
        If BodyDecodeFailed() Then
            ShowInput()
            Return
        End If

        ' 【二重送信を防ぐ】（Core/FormOnce.vb）送ったあと、戻るボタンで確認の画面まで戻って
        ' もう一度「送信する」を押したときは、ここで止まる。
        ' 【前からの「同じ内容を10分は受け付けない」も、そのまま残してある】（ContactRequest の IsDuplicate）
        ' あちらは**中身**で見るので、画面を開き直して同じ文を送った場合・JavaScript が動かない環境でも効く。
        ' こちらは**どの画面から送ったか**で見るので、10分を過ぎていても・文を少し直していても効く。
        ' 【ロボット避けの欄が埋まっているときは、見ない】黙って捨てる道（Submit の中）を、そのまま通す
        If String.IsNullOrWhiteSpace(TrapBox.Text) AndAlso
           ContactRequest.Validate(NameBox.Text, EmailBox.Text, KindList.SelectedValue, BodyBox.Text).Ok AndAlso
           Not FormOnce.TryUse(Request) Then

            ' 【入力の画面へ戻して、そこに出す】打った内容は残っている。もう一度「確認する」→「送信する」で送れる
            ShowError(FormOnce.AlreadySentMessage)
            FormErrors.MarkHere(ConfirmButton)
            ShowInput()
            Return

        End If

        Dim result As ContactRequest.ContactResult =
            ContactRequest.Submit(Context, NameBox.Text, EmailBox.Text,
                                  KindList.SelectedValue, BodyBox.Text, TrapBox.Text)

        If result.Ok Then

            ' 【送れたら、転送してから完了の画面を出す】（PRG）
            ' ここで画面を描いてしまうと、F5 で同じ問い合わせがもう1件入る。
            '
            ' 【endResponse は既定（True）のまま呼ぶ】False にするとページが最後まで走ってしまう
            ' 。**この呼び出しを Try/Catch の中に置かないこと**
            ' ——ThreadAbortException を捕まえて、嘘のエラー記録が出る
            '
            ' 【同じ内容をもう受け付けていたときも、完了の画面へ行く】
            ' 利用者から見れば「さっき送ったものが届いている」＝送れている。
            ' 「送れませんでした」と出すと、届いていないと思ってもう一度押す。文言だけが変わる
            RedirectToDone(result.AlreadyReceived)
            Return

        End If

        ' 断られた。**入力の画面へ戻して、直してもらう**
        ' （確認の画面のままだと、直す手段が「戻って直す」しか無くて遠回りになる）
        ShowError(result.Message, result.Field)
        ShowInput()

    End Sub


    ' ============================================================
    ' 画面の切り替え
    ' ============================================================

    Private Sub ShowInput()

        Steps.ActiveViewIndex = StepInput
        HeadingText.Text = HeadingInput

        ' 【包んだ値は、画面へ返さない】
        ' 見えている欄に生の文が入っているので、そのままにすると、
        ' JavaScript が動かない環境で「古い包んだ値」のほうが送られ、書き直しが消える。
        ' JavaScript が動く環境では、次に送るときに入れ直される
        BodyEncoded.Value = ""

    End Sub


    Private Sub ShowConfirm(result As ContactRequest.ContactResult)

        Steps.ActiveViewIndex = StepConfirm
        HeadingText.Text = HeadingConfirm
        ErrorText.Text = ""

        ' 【出す値は、全部エスケープする】この画面は ValidateRequest="false" なので、
        ' 「<script>」もそのまま届く。**文字として見えること**が正しい姿である
        ' 【並びは、画面（入力の欄）と同じ順に書いてある】読むときに目で追いやすいように
        ReviewName.Text = HttpUtility.HtmlEncode(OrNotEntered(result.Name))
        ReviewEmail.Text = HttpUtility.HtmlEncode(OrNotEntered(result.Email))
        ReviewKind.Text = HttpUtility.HtmlEncode(ContactRules.KindLabel(result.Kind))
        ReviewBody.Text = HttpUtility.HtmlEncode(result.Body)

        ' 【確認の画面から送るときも、内容は包んだまま運ぶ】
        ' 生のまま POST に乗せると、本番の WAF が「<script>」などで 403 にする。
        ' 内容の欄（BodyBox）は、この画面には無いので、ここが唯一の運び手である
        BodyEncoded.Value = BodyTransport.Encode(result.Body)

    End Sub


    ' alreadyReceived … 同じ内容を、少し前にもう受け付けていた（文言だけが変わる）
    Private Sub ShowDone(alreadyReceived As Boolean)

        Steps.ActiveViewIndex = StepDone
        HeadingText.Text = HeadingDone
        ErrorText.Text = ""

        ' 【出すのは、この1行と見出しだけ】
        DoneText.Text = HttpUtility.HtmlEncode(
            If(alreadyReceived, ContactRequest.AlreadyReceivedMessage, ContactRequest.DoneMessage))

    End Sub


    ' 送信が済んだら、GET のURLへ転送する（PRG）
    Private Sub RedirectToDone(alreadyReceived As Boolean)

        Dim signal As String = If(alreadyReceived, DuplicateQueryValue, DoneQueryValue)

        Response.Redirect(ResolveUrl("~/contact") & "?" & DoneQueryName & "=" & signal)

    End Sub


    ' field … どの欄が悪いか（ContactRequest.Field〇〇。どの欄とも言えなければ Nothing）
    '
    ' 【理由は、その欄の上に出る】ここで付けた印を見て、
    ' Scripts/site.js の「9」が、理由をその欄の上へ移してカーソルを置く。
    ' 印が無い理由（歯止め・混み合っている）は、欄より上の、いまの場所（見出しのすぐ下）に出る
    Private Sub ShowError(message As String, Optional field As String = Nothing)

        ErrorText.Text = HttpUtility.HtmlEncode(If(message, ""))
        DoneText.Text = ""

        Select Case field
            Case ContactRequest.FieldKind : FormErrors.MarkField(KindList)
            Case ContactRequest.FieldName : FormErrors.MarkField(NameBox)
            Case ContactRequest.FieldEmail : FormErrors.MarkField(EmailBox)
            Case ContactRequest.FieldBody : FormErrors.MarkField(BodyBox)
        End Select

    End Sub


    ' 空なら「（入力なし）」（確認の画面に出す）
    Private Shared Function OrNotEntered(text As String) As String
        Return If(String.IsNullOrEmpty(text), "（入力なし）", text)
    End Function


    ' 画面側の検査が止めた理由のうち、いちばん上のもの
    '
    ' 【JavaScript が動かない環境のためだけにある】
    ' 動いていれば、押した時点で止まってポストバックが起きない
    Private Function FirstValidatorMessage() As String

        For Each v As IValidator In Validators

            If Not v.IsValid Then
                Return v.ErrorMessage
            End If

        Next

        Return "入力を確かめてください。"

    End Function


    ' ============================================================
    ' 内容の包みをほどく
    ' ============================================================

    ' 送られてきた内容の包みを、ほどいて BodyBox へ入れる
    '
    ' 【hidden が先、見えている欄はあと】
    ' JavaScript が動いていれば、包んだ値が hidden に入っている（見えている欄は disabled で送られない）。
    ' 動いていなければ hidden は空で、見えている欄に生の文が入っている
    ' （そのときは WAF に止められうるが、受け取らないと JavaScript の無い環境で何も送れなくなる）。
    '
    ' 【ほどくのは、ここ1か所だけ】
    ' Contacts/ContactRequest.vb では、もうほどかない。2か所でほどくと、
    ' **利用者が本当に「vbenc1:」で始まる文を書いたとき**に、2度目のほどきがその文を壊す
    '
    ' 【戻せなかったときは、欄を空にしない】
    ' 空にすると、検査が「内容を入力してください」という的外れな理由で断る。
    ' 包んだままの値を残し、押されたボタンの処理が本当の理由を出して断る
    Private Sub DecodeBody()

        If String.IsNullOrEmpty(BodyEncoded.Value) Then
            ' JavaScript が動かなかった。見えている欄の生の文を、そのまま使う
            Return
        End If

        _bodyDecode = BodyTransport.Decode(BodyEncoded.Value, ContactRules.BodyMaxLength,
                                           "内容", "送っていません。")

        If _bodyDecode.Ok Then
            BodyBox.Text = _bodyDecode.Html
        Else
            BodyBox.Text = BodyEncoded.Value
        End If

    End Sub

    ' ほどいた結果（ポストバックで、包まれて届いたときだけ入る）
    Private _bodyDecode As BodyTransport.DecodeResult


    ' ほどけなかったか（ほどけなかったときは、理由を画面に出してある）
    Private Function BodyDecodeFailed() As Boolean

        If _bodyDecode Is Nothing OrElse _bodyDecode.Ok Then
            Return False
        End If

        ShowError(_bodyDecode.Message, ContactRequest.FieldBody)
        BodyEncoded.Value = ""

        Return True

    End Function


    ' ============================================================
    ' 種類の選択肢
    ' ============================================================

    ' 【マークアップに <asp:ListItem> をベタ書きしないこと】
    ' 白リスト（ContactRules.AllowedKinds）と画面が食い違うため。
    ' Articles/Categories.aspx.vb の BindSortModes と、まったく同じ形にそろえてある。
    '
    ' 【いちばん上は「選んでください」（値は空）】
    ' 最初から「不具合の報告」が選ばれていると、選んだつもりが無いまま送られる。
    ' 空の値は保存しない——画面の RequiredFieldValidator（InitialValue=""）と、
    ' サーバー側の ContactRules.ValidateKind の、両方で断る
    Private Sub BindKinds()

        KindList.Items.Clear()
        KindList.Items.Add(New ListItem(ContactRules.KindPromptText, ""))

        For Each kind As ContactRules.ContactKind In ContactRules.AllowedKinds

            KindList.Items.Add(New ListItem(
                ContactRules.KindLabel(kind),
                CInt(kind).ToString(Globalization.CultureInfo.InvariantCulture)))

        Next

        KindList.SelectedValue = ""

    End Sub


End Class
