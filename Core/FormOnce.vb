Imports System.Data.SqlClient
Imports System.Security.Cryptography

' 二重送信を防ぐ「使い捨ての目印」
'
' ============================================================
' 【何を防ぐのか】
' ============================================================
' 送信が済んだあと、**戻るボタンで入力の画面に戻って、もう一度押す**と、同じものがもう1件入る。
' F5 の二重送信は PRG（送ったら開き直す）で防げるが、戻ってからの押し直しは PRG では防げない。
' 決定：これも防ぐ。基本ルールにも入れる
' （「毎回の基本」の6）。
'
' ============================================================
' 【仕組み】（一般には「ワンタイムトークン」。同じ操作を2回送っても1回ぶんしか起きない性質を「冪等（べきとう）性」と呼ぶ）
' ============================================================
'   ① 画面を描くたびに、目印（でたらめな32文字）を1つ作って、フォームの見えない欄に入れる（Site.Master）
'   ② 送信を受け付ける直前に、TryUse でその目印を「使った」と DB に書く
'   ③ 同じ目印がもう書いてあれば、**2度目の送信**である。何もせず、「もう送信済みです」と断る
'
' 【戻るボタンで戻った画面には、前と同じ目印が入っている】だから③で止まる。
' ブラウザは、戻ったときに画面を取り直すことがあり（このサイトのアカウントの画面は no-store）、
' 取り直した画面には①の新しい目印が入ってしまう。そこで Scripts/site.js の「11」が、
' **その履歴の1件に目印を覚えさせておき、戻ってきたときは覚えていたほうを入れ直す**（history.state）。
'
' 【わざと2件目を送ることは、できる】
'   ・画面を開き直す（メニューから入り直す・F5）→ 新しい目印
'   ・「もう送信済みです」と断られた画面には、新しい目印が入っている → もう一度押せば送れる
'     （断るときに開き直さないので、打った内容は残っている。理由の文にも、そう書いてある）
'
' ============================================================
' 【使い方】送信する画面は、これを呼ぶだけ
' ============================================================
'       ' 入力の検査が全部通ったあと・実際に保存／送信する直前に
'       If Not FormOnce.TryUse(Request) Then
'           ErrorText.Text = HttpUtility.HtmlEncode(FormOnce.AlreadySentMessage)
'           FormErrors.MarkHere(AddButton)      ' 理由は、押したボタンのすぐ上に出す
'           Return
'       End If
'
' 【検査より後に呼ぶこと】先に呼ぶと、入力の間違いで断ったときにも目印を使ってしまい、
' 直して押し直した人が「もう送信済みです」と言われる（断った画面には新しい目印が入るので実害は小さいが、順番は守る）。
'
' 【使うのは「足す」操作だけ】同じものを2回やっても結果が変わらない操作（直す・消す）には要らない。
' 今あるのは：お知らせの追加／カテゴリの追加／問い合わせの送信／新規登録／パスワードを忘れたとき。
' **新しい記事の保存だけは、TryUse ではなく下の UsedCheckSql・MarkUsedSql を使う**
' （ArticleStore.Insert。手の保存と自動保存の2つの口が、同じ1か所を通る）
'
' ============================================================
' 【使った目印は、DB に置く】（UsedFormTokens 表）
' ============================================================
' メモリに置く形にしなかった理由＝**共用サーバーは、人が居ないとアプリが休んで起き直す**。
' メモリは起き直すと消えるので、「送信 → しばらく放置 → 戻って押し直す」が素通りする
' （人の少ないこのサイトでは、それがいちばん起きやすい場面である）。DB なら、起き直しても覚えている。
' そのうえ、**ほとんど同時に2回届いたとき**（ボタンの連打）も、主キーが片方だけを通す。
'
' 【署名はしていない】目印は本人確認ではない。でたらめな目印を自分で作って送っても、
' 起きるのは「画面を開き直したのと同じこと」だけである（よその人の送信は止められない・通せない）。
'
' 【目印が無い・形が違うときは、通す】発行の瞬間に開いていた古い画面・JavaScript が動かない環境のため。
' これは**押し間違いを防ぐ親切**で、防御ではない（連打や荒らしは、各画面の歯止めが止める）。
'
' 【DB に書けなかったときも、通す（フェイルオープン）】ここで止めると、DB が詰まっている間、
' 問い合わせの「メールだけでも届ける」道まで塞いでしまう。ErrorReport で知らせる。
'
' 【古い目印は、1日1回の仕事が消す】（Core/LogCleanup.vb。KeepDays 日より前）。
' 履歴の1件が生きているのは、そのタブが開いている間だけなので、それより長く持つ意味が無い
Public NotInheritable Class FormOnce

    Private Sub New()
    End Sub

    ' フォームの見えない欄の名前（Site.Master と Scripts/site.js の「11」と、同じ文字列にすること）
    Public Const FieldName As String = "vbFormOnce"

    ' 使った目印を残しておく日数（防御ではないので SiteSettings に入れない）
    Public Const KeepDays As Integer = 30

    ' 断るときの文
    '
    ' 【「送れませんでした」と書かない】利用者から見れば、さっきの分は届いている。
    ' 【どうすればもう1件送れるかを、その場で言う】断った画面には新しい目印が入っているので、もう一度押せば送れる
    Public Const AlreadySentMessage As String =
        "もう送信済みです（戻って、もう一度押したため）。" &
        "もう1件送るときは、もう一度押してください。"

    Private Const ConnectionName As String = "VbChatConnection"

    Private Const TokenLength As Integer = 32

    ' フォームでない送信（記事の自動保存＝fetch）が、目印を運ぶヘッダーの名前
    ' （Scripts/article-editor.js の FORM_ONCE_HEADER と、同じ文字列にすること）
    Public Const HeaderName As String = "X-VbChat-FormOnce"


    ' ============================================================
    ' 「足す」文と同じトランザクションの中で、目印を確かめて使う形（記事の保存が使う）
    ' ============================================================
    '
    ' TryUse は「先に目印を使ってから、足す」形である。ふつうの画面はそれで足りる
    ' （足す前の検査が全部済んでいて、断った画面には新しい目印が入るため）。
    '
    ' 記事だけは足りなかった。**足す口が2つある**（手で押す保存と、自動保存）うえに、
    ' 自動保存は画面を描き直さない（＝同じ目印を持ったまま、何度でも送ってくる）。
    ' 先に目印を使う形だと、足す文が断ったとき（URL の名前の重なりなど）に、目印だけが使われて残り、
    ' 次の自動保存が「もう保存してあります」と、嘘の理由で断られ続ける。
    ' そこで、**足せたときにだけ、同じトランザクションの中で目印を使う**。
    '
    '   UsedCheckSql … 「この目印は、もう使われているか」（WHERE や IF に入れる式）。
    '                  UPDLOCK・HOLDLOCK で、同じ目印の2つの送信が同時に来ても、片方を待たせる
    '   MarkUsedSql  … 目印を使ったことにする文（足せたあとに置く）
    '   どちらも @OnceToken が要る（AddTokenParameter）。**目印が無い・形が違うときは NULL が入り、何も確かめない**
    '   （TryUse の「無ければ通す」と同じ。古い画面・JavaScript が動かない画面を締め出さない）
    '
    ' 【この形は、フェイルオープンではない】UsedFormTokens が読めなければ、足す文ごと失敗する
    ' （同じ文の中に在るため）。記事の INSERT は、画像の台帳（ArticleImageUses）も同じ文で書いているので、
    ' 「同じトランザクションの表が1つ増えた」だけである
    Public Const UsedCheckSql As String =
        "(@OnceToken IS NOT NULL AND EXISTS (SELECT 1 FROM UsedFormTokens WITH (UPDLOCK, HOLDLOCK) WHERE Token = @OnceToken))"

    Public Const MarkUsedSql As String =
        "IF @OnceToken IS NOT NULL INSERT INTO UsedFormTokens (Token) VALUES (@OnceToken); "

    Public Shared Sub AddTokenParameter(cmd As SqlCommand, token As String)

        cmd.Parameters.Add("@OnceToken", SqlDbType.Char, TokenLength).Value =
            If(LooksLikeToken(token), CType(token, Object), DBNull.Value)

    End Sub


    ' 送られてきた目印を読む（フォームの見えない欄 → 無ければヘッダー）。使ったことにはしない
    Public Shared Function TokenOf(request As HttpRequest) As String

        If request Is Nothing Then
            Return Nothing
        End If

        Dim token As String = Nothing

        Try
            token = request.Form(FieldName)
        Catch ex As Exception
            ' application/json の POST（WebMethod）では、フォームとして読めないことがある。ヘッダーを見る
        End Try

        If Not LooksLikeToken(token) Then
            token = request.Headers(HeaderName)
        End If

        Return If(LooksLikeToken(token), token, Nothing)

    End Function

    ' SQL Server の「もうその値がある」（主キー・一意索引）
    Private Const DuplicateKeyError As Integer = 2627
    Private Const DuplicateIndexError As Integer = 2601


    ' 新しい目印を1つ作る（画面を描くたびに、Site.Master が呼ぶ）
    '
    ' 【DB には触らない】作るだけなら、どのページを開いても DB の問い合わせは増えない
    Public Shared Function NewToken() As String

        Dim bytes(15) As Byte

        Using rng As New RNGCryptoServiceProvider()
            rng.GetBytes(bytes)
        End Using

        Dim sb As New Text.StringBuilder(TokenLength)

        For Each b As Byte In bytes
            sb.Append(b.ToString("x2"))
        Next

        Return sb.ToString()

    End Function


    ' 送られてきた目印を「使った」ことにする
    '
    ' 戻り値 … 受け付けてよければ True。**同じ目印が、もう使われていれば False**（2度目の送信）
    Public Shared Function TryUse(request As HttpRequest) As Boolean

        If request Is Nothing Then
            Return True
        End If

        Dim token As String = request.Form(FieldName)

        ' 【無い・形が違う → 通す】上のコメントのとおり
        If Not LooksLikeToken(token) Then
            Return True
        End If

        Try

            Using conn As New SqlConnection(ConnectionString())
                Using cmd As New SqlCommand("INSERT INTO UsedFormTokens (Token) VALUES (@Token);", conn)

                    cmd.Parameters.Add("@Token", SqlDbType.Char, TokenLength).Value = token

                    conn.Open()
                    cmd.ExecuteNonQuery()

                End Using
            End Using

            Return True

        Catch ex As SqlException When ex.Number = DuplicateKeyError OrElse ex.Number = DuplicateIndexError

            ' もう使われている＝2度目の送信。**異常ではない**ので、ErrorReport へは送らない
            Return False

        Catch ex As Exception

            ' 【書けなかった → 通す】目印の値は、知らせに入れない
            ErrorReport.Report(ex, "FormOnce.TryUse")

            Return True

        End Try

    End Function


    ' 古い目印を消す（1日1回。Core/LogCleanup.vb が呼ぶ）。消した行数を返す
    Public Shared Function DeleteOld() As Integer

        Using conn As New SqlConnection(ConnectionString())
            Using cmd As New SqlCommand(
                "DELETE FROM UsedFormTokens WHERE UsedAt < DATEADD(DAY, -@Days, SYSDATETIME());", conn)

                cmd.Parameters.Add("@Days", SqlDbType.Int).Value = KeepDays

                conn.Open()

                Return cmd.ExecuteNonQuery()

            End Using
        End Using

    End Function


    ' 目印の形か（小文字の16進で32文字）。形の合わないものを、そのまま DB へ渡さない
    Private Shared Function LooksLikeToken(value As String) As Boolean

        If value Is Nothing OrElse value.Length <> TokenLength Then
            Return False
        End If

        For Each c As Char In value
            If Not ((c >= "0"c AndAlso c <= "9"c) OrElse (c >= "a"c AndAlso c <= "f"c)) Then
                Return False
            End If
        Next

        Return True

    End Function


    Private Shared Function ConnectionString() As String
        Return ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString
    End Function

End Class
