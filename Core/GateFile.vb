Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Hosting

' 「合言葉のファイルが置いてある間だけ開く画面」の、合言葉の判定（1つにまとめた）
'
' 使っている画面は3つ。
'   ・Admin/MailTest.aspx       … App_Data\mail-test.txt（本番でメールが届くかの確認口）
'   ・Accounts/Setup.aspx      … App_Data\account-setup.txt（最初のサイト管理者を作る）
'   ・Accounts/Recover.aspx    … App_Data\account-recover.txt（サイト管理者のパスワードを作り直す非常口）
' それまでは MailTest と Setup に同じ関数の写しがあり、「片方を直したら、もう片方も見ること」と書いてあった。
' 3つ目を足すところで、写しを増やさずに1つにした。
'
' 【ファイルが無い・読めない・短すぎる・合わない は、どれも同じ「閉じている」】
' 外から見て区別が付くと、「ファイルは在る」ことが分かってしまう。
'
' 【合言葉のファイルは、どれも本番にしか無い使い捨て】確かめ終わったら FTP で消す。
' .gitignore と発行プロファイルで除外し、.vbproj には登録しない（「接続情報の置き場所」の章）
Public NotInheritable Class GateFile

    Private Sub New()
    End Sub

    ' 合言葉として認める最低の長さ（「1」「test」のような短い値で開かないように）
    Public Const MinKeyLength As Integer = 8


    ' 合言葉が合っているか
    '
    ' relativePath … "~/App_Data/…txt"
    ' givenKey     … URL の ?key= の値
    Public Shared Function IsOpen(relativePath As String, givenKey As String) As Boolean

        If String.IsNullOrEmpty(givenKey) Then
            Return False
        End If

        Try

            Dim path As String = HostingEnvironment.MapPath(relativePath)

            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                Return False
            End If

            Dim expected As String = File.ReadAllText(path).Trim()

            If expected.Length < MinKeyLength Then
                Return False
            End If

            Return FixedTimeEquals(expected, givenKey)

        Catch
            Return False
        End Try

    End Function


    ' 2つの文字列が同じかを、どこで食い違ったかで時間が変わらない形で比べる
    ' （先頭から比べて違ったところで止める比べ方だと、時間の差から1文字ずつ当てられる）。
    ' 長さの違いも隠すため、いったんハッシュにそろえてから比べる
    Private Shared Function FixedTimeEquals(a As String, b As String) As Boolean

        Dim hashA As Byte()
        Dim hashB As Byte()

        Using sha As SHA256 = SHA256.Create()
            hashA = sha.ComputeHash(Encoding.UTF8.GetBytes(a))
            hashB = sha.ComputeHash(Encoding.UTF8.GetBytes(b))
        End Using

        Return RoomPassword.FixedTimeEquals(hashA, hashB)

    End Function

End Class
