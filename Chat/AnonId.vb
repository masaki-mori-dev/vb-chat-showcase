Imports System.Security.Cryptography
Imports System.Text

' 「同じ人かどうか」だけが分かる、使い捨ての短い印
'
' 【何のためにあるか】
' 無視機能（11番のC）で、クライアントが「この行はあの人の行か」を判定するために使う。
' 参加者一覧・リアルタイムの発言・入退室・過去ログの各行に、同じ値が付く。
'
' 【なぜ、生のBrowserIdを渡さないのか】
' ChatBrowserId Cookie は本人証明そのものである。他人の値を知れば、自分のCookieへ
' 入れてなりすませる（2窓目の置き換えでその人を追い出せる／将来は OwnerBrowserId で
' 部屋のオーナー権も奪える）。**クライアントへ渡してよい値ではない。**
'
' 【なぜ、LocalId ではないのか】
' LocalId は在室中こそ固定だが、アプリが再起動すると1番から振り直される。
' 「昨日のログの5番」と「今いる5番」が別人になるため、過去ログの行に印を付けられない
' （isSelf を LocalId で判定してはいけない、という既存の罠とまったく同じもの）。
Public Class AnonId

    ' 印の長さ（16進の桁数）
    '
    ' 【短くしすぎないこと】衝突すると「Aを無視したらBも消えた」が、
    ' エラーも出さずに起きる。48ビットあれば、部屋の人数の規模ではまず当たらない。
    ' 長くしても、1行あたり数バイトしか増えない
    Private Const HexLength As Integer = 12

    ' アプリの起動ごとに作り直される、使い捨てのソルト
    '
    ' 【なぜ使い捨てでよいのか】
    ' 無視リストは sessionStorage にあり、部屋を出れば消える。
    ' アプリが再起動すれば全員が切断されるので、そのとき印が全部変わっても実害が無い。
    ' むしろ**古い無視リストが誰にも当たらなくなる**ため、取り違えが原理的に起きない。
    '
    ' 【永続させないことが、そのまま守りになっている】
    ' 永続する秘密にすると、置き場所（gitにも発行物にも載せない）の管理が1つ増える。
    ' トリップのソルトは永続が要るので connections.config と同じ扱いになるが、
    ' こちらはその必要が無い。**増える秘密はゼロ**
    '
    ' 【Application_Start に書かないのは、意図してのこと】
    ' Shared フィールドの初期化子は、このクラスが最初に使われた1回だけ走る。
    ' 効果は同じで、Global.asax に初期化処理が溜まらない
    ' （SiteSettings を遅延読み込みにしたのと、同じ考え方）
    Private Shared ReadOnly Salt As String = Guid.NewGuid().ToString("N")


    ' その部屋での、そのブラウザの印を計算する
    '
    ' 【なぜ roomId を混ぜるのか】
    ' 混ぜないと、**部屋Aで「太郎」・部屋Bで「花子」を名乗っている人が、
    ' 同一ブラウザだと分かってしまう**。印は全員に配るので、F12を開ける人なら誰でも
    ' 突き合わせられる。匿名チャットは「別の部屋では違う自分でいられる」ことが本質であり、
    ' そこを崩してはいけない。無視リストは部屋ごとなので、混ぜても機能は1ミリも損なわれない
    '
    ' 【何も溜めない】覚えておかず、必要になった時点で計算し直す。
    ' そのため掃除の仕組みも、件数の上限も要らない。
    ' 計算量は問題にならない（200行ぶん毎回ハッシュし直して1.5ms。実測）
    Public Shared Function Compute(roomId As Integer, browserId As Guid) As String

        ' 【必ず、決まった書式に直してから混ぜること】
        ' ここへ来る値の出どころは2つあり、表記が揃っていない。
        '   ・ハブ側      … Cookieの生の文字列（加工しない方針のため）
        '   ・過去ログ側  … DBから読んだ Guid 型
        ' 生の文字列のまま混ぜると、"{A1B2...}" と "a1b2..." が別の印になり、
        ' **同じ人なのに印が2つできる**。症状は「リアルタイムの発言は消えるのに、
        ' その人の過去ログだけ消えない」で、エラーは1つも出ない。
        ' 引数を Guid 型に限っているのは、呼び出し側に必ず変換させるためである
        Dim source As String = Salt & "|" & roomId.ToString(
            Globalization.CultureInfo.InvariantCulture) & "|" & browserId.ToString("N")

        Using sha As SHA256 = SHA256.Create()

            Dim bytes As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(source))

            Dim sb As New StringBuilder(HexLength)
            For i As Integer = 0 To (HexLength \ 2) - 1
                sb.Append(bytes(i).ToString("x2"))
            Next

            Return sb.ToString()

        End Using

    End Function


    ' 文字列のBrowserIdから計算する（Guidとして読めなければ Nothing）
    '
    ' 【Nothing を返す場合があること】
    ' 印が無い行は、クライアント側では「誰の行でもない」＝隠されない扱いになる。
    ' 入室時に JoinRoom が Cookie を検査して断っているため、在室者の印が欠けることは
    ' 無いはずだが、**欠けても画面が壊れないほうへ倒してある**
    Public Shared Function ComputeFromString(roomId As Integer, browserId As String) As String

        Dim parsed As Guid

        If String.IsNullOrEmpty(browserId) OrElse Not Guid.TryParse(browserId, parsed) Then
            Return Nothing
        End If

        Return Compute(roomId, parsed)

    End Function

End Class
