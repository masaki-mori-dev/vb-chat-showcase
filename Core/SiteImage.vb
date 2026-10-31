Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Web.Hosting

' ============================================================
' 画像アップロードの、決まりごとを1か所へ集めたクラス
' ============================================================
'
' E（画像アップロード）の回で作った。
'
' 【何をここに置き、何を置かないか】
' 置くのは「画像そのものについての判断」だけである——
' 受け付けてよい中身か／名前はどう作るか／どこへ置くか／本文の印はどう書くか。
'
' 置かないのは「誰が送れるか」「連投していないか」といった**発言としての判断**で、
' そちらは今までどおり ChatHub が持つ。
' 画像も普通の発言も、まったく同じ関所（AcceptAndDeliver）を通る。
'
' 【なぜ、判断を1か所に集めるのか】
' 開発者の発案（「判定のロジックは1か所のクラスにまとめる」）。
' ただし**「チェックが必要なことを全部このファイルに入れる」のは別の話**である。
' 発言の入力チェック・入室の可否・画像の可否は、判定に使う材料がまるで違う。
' 1つに集めると条件分岐が育って、いずれ「この経路だけ抜けていた」が起きる
' ============================================================
' 画像をどこへ置くか（真偽値 isPrivate から変えた）
' ============================================================
'
'   Room        … 部屋あての画像（Uploads/images/年/月/日/GUID.拡張子。IIS がそのまま返す。90日で消える）
'   PrivateTalk … こっそりの画像（App_Data/private-images/年/月/日/…。Chat/PrivateImage.aspx だけが返す。90日で消える）
'   Article     … 記事の画像（Uploads/articles/アカウントの番号/GUID.拡張子。IIS がそのまま返す。**自動では消さない**）
'   Profile     … プロフィールの画像（Uploads/profiles/アカウントの番号/GUID.拡張子。IIS がそのまま返す。**自動では消さない**）
'
' 【3つ目ができたので、真偽値をやめた】isPrivate のままだと「こっそりではない＝部屋」としか言えず、
' 記事を足した日に、どこかの呼び出しが記事の画像を部屋の置き場所へ置く（またはその逆）ことが、エラーも出ずに起きる
'
' 【プロフィールの画像を、記事の置き場所に相乗りさせてはいけない】（ブログ型の①）
' 記事の画像は1日1回の片付け（ArticleImages.CleanupUnused）が、
' 「どの記事の本文からも使われていない画像」を消しにいく。
' プロフィールの画像は記事の本文に出てこないので、**同じフォルダに置いた瞬間、翌日に全部消える**。
' 根を分けてあるので、あちらの片付けはここを1枚も見ない
Public Enum ImagePlace
    Room
    PrivateTalk
    Article
    Profile
End Enum


Public Class SiteImage

    ' ============================================================
    ' 受け付ける中身（＝保存する形式）は、3つだけ
    ' ============================================================
    '
    ' 【「受け付ける形式」と「保存する形式」は、別のものである】
    '
    '   画面が受け付ける … PNG / JPG / GIF / BMP / ICO / WebP の6つ
    '   ここが受け取る   … JPEG / PNG / GIF の3つだけ
    '
    ' 画面側が、GIF以外をすべて canvas で JPEG か PNG に変換してから送るためである。
    ' BMP・ICO・WebP は、**サーバーまで届かない**。
    '
    ' 【3つに絞っていることに、実利がある】
    '   ・IISのMIME対応表を心配しなくてよい。.jpg / .png / .gif は、
    '     どの版のIISにも最初から入っている（.webp は版によっては入っていない）
    '   ・置き場所に置かれうる拡張子が3つに固定されるので、
    '     「.aspx が紛れ込む」形の事故が原理的に起きない
    '
    ' 【拡張子は信じない。中身の先頭バイトで判定する】
    ' 利用者が名前を付けられる以上、拡張子は言い値である。
    ' 下の DetectExtension は、名前をいっさい見ない
    Public Const ExtensionJpeg As String = "jpg"
    Public Const ExtensionPng As String = "png"
    Public Const ExtensionGif As String = "gif"


    ' 画面へ見せる、受け付ける形式の並び
    '
    ' 【画面の文言と、ここを揃えること】
    ' 6つのうちGIF以外は変換されて届くため、上の3つとは意図的に食い違っている
    Public Const AcceptedFormatsText As String = "PNG / JPG / GIF / BMP / ICO / WebP"


    ' 画像の名前（＝本文の印に入る文字列）の形
    '
    '     2026/09/10/0f3c8a1b2d4e5f60718293a4b5c6d7e8.jpg
    '
    ' 【日付でフォルダを切っている理由】
    '   ・保存期間が過ぎたら、**フォルダごと消せる**
    '     （ログは古い行から、画像は古いフォルダごと。DBの行とファイルを
    '       突き合わせる必要が無く、片方が失敗しても、もう片方は進む）
    '   ・1つのフォルダにファイルが何万も溜まらない
    '
    ' 【この正規表現が、パス外への書き込み・読み出しを止める唯一の壁である】
    ' 名前は本文の印から来る＝**利用者の言い値**として扱わなければならない。
    ' 「..」も「\」も、この形には当てはまらないので通らない。
    ' **ファイルを触る処理は、必ずここを通してから呼ぶこと**
    Private Shared ReadOnly NamePattern As New Regex(
        "^[0-9]{4}/[0-9]{2}/[0-9]{2}/[0-9a-f]{32}\.(jpg|png|gif)$",
        RegexOptions.Compiled)

    ' 記事の画像の名前の形
    '
    '     2/0f3c8a1b2d4e5f60718293a4b5c6d7e8.jpg      （アカウントの番号 ／ GUID ／ 拡張子）
    '
    ' 【日付のフォルダにしない】日付で切るのは「フォルダごと消す」ためだった。記事の画像は消さないので、その理由が無い。
    ' かわりにアカウントで切ると、そのアカウントが置いている量（容量の上限）を、フォルダ1つを数えるだけで出せる。
    ' 【上と同じく、この正規表現がパス外への書き込み・読み出しを止める壁である】
    Private Shared ReadOnly ArticleNamePattern As New Regex(
        "^[1-9][0-9]{0,8}/[0-9a-f]{32}\.(jpg|png|gif)$",
        RegexOptions.Compiled)

    ' プロフィールの画像の名前の形（ブログ型の①）
    '
    '     2/0f3c8a1b2d4e5f60718293a4b5c6d7e8.jpg      （アカウントの番号 ／ GUID ／ 拡張子）
    '
    ' 【記事の画像と同じ形だが、別の正規表現にしてある】同じ物なら1つにまとめたくなるが、
    ' **置き場所ごとに形を変えられること自体が、この仕組みの要**である
    ' （IsValidName が置き場所を受け取るのは、そのため）。
    ' 1つにまとめると、片方の形を変えた日に、もう片方の壁まで一緒に動く
    Private Shared ReadOnly ProfileNamePattern As New Regex(
        "^[1-9][0-9]{0,8}/[0-9a-f]{32}\.(jpg|png|gif)$",
        RegexOptions.Compiled)


    ' 本文に入る印
    '
    '     [img:2026/09/10/0f3c….jpg:1280x960]
    '
    ' 【画像の発言は、本文がこの印だけである】
    ' 文章と一緒には送れない（決定：LINE などと同じ形でよい）。
    ' だから前後に何も許さない形にしてある。
    ' 画面側も、本文がまるごとこの形のときだけ画像として描く
    '
    ' 【寸法を印に入れている理由】
    ' 画像が届く前に場所を確保して、**画面を跳ねさせない**ため。
    ' アイコンをファイルにしたときに1188px跳ねたのと、同じ形の穴を先に塞いである
    Private Shared ReadOnly TokenPattern As New Regex(
        "^\[img:([0-9]{4}/[0-9]{2}/[0-9]{2}/[0-9a-f]{32}\.(?:jpg|png|gif)):([0-9]{1,4})x([0-9]{1,4})\]$",
        RegexOptions.Compiled)


    ' 本文のどこかに印らしきものが混ざっているか
    '
    ' 【何のためにあるのか】
    ' 画像の発言は、**利用者に組み立てさせない。サーバーが作る。**
    ' 受け口（Chat/UploadImage.aspx）が保存に成功したときだけ、その場で印を組み立てる。
    ' そのうえで、普通の発言が「[img:」を含んでいたら断る。
    '
    ' 【これが無いと、どうなるか】
    ' 誰でも「[img:2026/09/10/(他人の画像).jpg:100x100]」と打てば、
    ' 他人の画像を自分の発言として貼れる。こっそりの画像は認可で守られるが、
    ' 部屋の画像は静的ファイルなので、そのまま出てしまう
    '
    ' 【前後に何かあっても捕まえること】
    ' 「これは [img:…] です」のように文章へ紛れ込ませる形も断る。
    ' 上の TokenPattern（前後を許さない）とは、わざと形が違う
    Public Shared Function ContainsToken(text As String) As Boolean

        If String.IsNullOrEmpty(text) Then
            Return False
        End If

        Return text.IndexOf("[img:", StringComparison.OrdinalIgnoreCase) >= 0

    End Function


    ' 本文が、画像の印そのものかどうか（＝画像の発言か）
    '
    ' 【今は、テストのために置いてある】古いログの掃除（14番の LogCleanup）は、本文を読まずに
    ' 日付のフォルダごと消すので、これを使っていない（確かめた）
    ' 画面へ出すための判定は、画面側（Scripts/chat-room.js）が同じ形で持っている——
    ' あちらは本文しか受け取らないため、サーバーへ聞きに行けない
    Public Shared Function TryParseToken(text As String,
                                         ByRef imageName As String,
                                         ByRef width As Integer,
                                         ByRef height As Integer) As Boolean

        imageName = Nothing
        width = 0
        height = 0

        If String.IsNullOrEmpty(text) Then
            Return False
        End If

        Dim m As Match = TokenPattern.Match(text)

        If Not m.Success Then
            Return False
        End If

        imageName = m.Groups(1).Value
        width = Integer.Parse(m.Groups(2).Value, CultureInfo.InvariantCulture)
        height = Integer.Parse(m.Groups(3).Value, CultureInfo.InvariantCulture)

        Return True

    End Function


    ' 印を組み立てる（サーバーだけが呼ぶ）
    Public Shared Function BuildToken(imageName As String,
                                      width As Integer,
                                      height As Integer) As String

        Return "[img:" & imageName & ":" &
               width.ToString(CultureInfo.InvariantCulture) & "x" &
               height.ToString(CultureInfo.InvariantCulture) & "]"

    End Function


    ' 名前が、上の形に当てはまるか
    '
    ' 【ファイルを触る前に、必ず通すこと】
    Public Shared Function IsValidName(imageName As String) As Boolean

        If String.IsNullOrEmpty(imageName) Then
            Return False
        End If

        Return NamePattern.IsMatch(imageName)

    End Function


    ' 置き場所ごとの名前の形に当てはまるか（記事の画像は形が違う）
    Public Shared Function IsValidName(imageName As String, place As ImagePlace) As Boolean

        If String.IsNullOrEmpty(imageName) Then
            Return False
        End If

        If place = ImagePlace.Article Then
            Return ArticleNamePattern.IsMatch(imageName)
        End If

        If place = ImagePlace.Profile Then
            Return ProfileNamePattern.IsMatch(imageName)
        End If

        Return NamePattern.IsMatch(imageName)

    End Function


    ' 名前から、DBの ImageName 列に入れるGUIDを取り出す
    '
    ' 【なぜ、列にも持つのか】
    ' こっそりの画像を1枚開くたびに、その画像がどの発言のものかを引く。
    ' 本文から引くと LIKE になって索引が使えない
    ' （詳しくは Database/CreateTable/ChatMessages.sql の ImageName）
    Public Shared Function TryGetGuid(imageName As String, ByRef result As Guid) As Boolean

        result = Guid.Empty

        If Not IsValidName(imageName) Then
            Return False
        End If

        ' 「2026/09/10/(32桁).jpg」の、最後の / と . のあいだ
        Dim slash As Integer = imageName.LastIndexOf("/"c)
        Dim dot As Integer = imageName.LastIndexOf("."c)

        Return Guid.TryParseExact(imageName.Substring(slash + 1, dot - slash - 1), "N", result)

    End Function


    ' 新しい画像の名前を作る
    '
    ' 【名前は利用者の言い値を使わず、必ずGUIDにすること】
    ' 元のファイル名をそのまま使うと、名前そのものが攻撃の入口になる
    ' （パスの区切り・拡張子の細工・他人のファイルの上書き）。
    ' 元の名前は、どこにも保存しない——**画像を出すのに要らないため**
    Public Shared Function NewName(extension As String) As String

        Dim now As DateTime = DateTime.Now

        Return now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) & "/" &
               Guid.NewGuid().ToString("N") & "." & extension

    End Function


    ' 記事の画像の新しい名前。アカウントの番号のフォルダの下に GUID
    Public Shared Function NewArticleName(accountId As Integer, extension As String) As String

        Return accountId.ToString(CultureInfo.InvariantCulture) & "/" &
               Guid.NewGuid().ToString("N") & "." & extension

    End Function


    ' プロフィールの画像の新しい名前（ブログ型の①）。形は記事の画像と同じ
    '
    ' 【差し替えるたびに、新しい GUID にする】同じ名前で上書きしないのは、
    ' ブラウザが前の画像をキャッシュしていて、**差し替えたのに古い絵が出る**のを避けるため
    ' （`?v=` を付ける手もあるが、名前を変えれば URL ごと変わる）。
    ' 古い画像は、差し替えが済んだあとに消す（Accounts/AccountProfileImages.vb）
    Public Shared Function NewProfileName(accountId As Integer, extension As String) As String

        Return accountId.ToString(CultureInfo.InvariantCulture) & "/" &
               Guid.NewGuid().ToString("N") & "." & extension

    End Function


    ' ============================================================
    ' 中身の先頭バイトから、形式を見分ける
    ' ============================================================
    '
    ' 見分けられなければ Nothing を返す（＝受け付けない）。
    '
    ' 【拡張子も、ブラウザが名乗る Content-Type も見ない】
    ' どちらも利用者が自由に決められる。信用できるのは中身だけである
    '
    ' 【全部を検査しているわけではない】
    ' ここが見ているのは「先頭がその形式らしいか」だけで、
    ' ファイル全体が壊れていないことまでは保証しない。
    ' それでよいのは、**このサーバーは画像を復号しないため**である
    ' （復号するのは、それを見る人のブラウザ）。
    ' サーバー側で System.Drawing に読ませる形にすると、
    ' 壊れた画像を解析する処理が1つ増えることになるので、そうしていない
    Public Shared Function DetectExtension(bytes As Byte()) As String

        If bytes Is Nothing OrElse bytes.Length < 12 Then
            Return Nothing
        End If

        ' JPEG … FF D8 FF
        If bytes(0) = &HFF AndAlso bytes(1) = &HD8 AndAlso bytes(2) = &HFF Then
            Return ExtensionJpeg
        End If

        ' PNG … 89 50 4E 47 0D 0A 1A 0A
        If bytes(0) = &H89 AndAlso bytes(1) = &H50 AndAlso bytes(2) = &H4E AndAlso
           bytes(3) = &H47 AndAlso bytes(4) = &HD AndAlso bytes(5) = &HA AndAlso
           bytes(6) = &H1A AndAlso bytes(7) = &HA Then
            Return ExtensionPng
        End If

        ' GIF … GIF87a または GIF89a
        If bytes(0) = &H47 AndAlso bytes(1) = &H49 AndAlso bytes(2) = &H46 AndAlso
           bytes(3) = &H38 AndAlso (bytes(4) = &H37 OrElse bytes(4) = &H39) AndAlso
           bytes(5) = &H61 Then
            Return ExtensionGif
        End If

        Return Nothing

    End Function


    ' ============================================================
    ' 置き場所
    ' ============================================================
    '
    ' 【こっそりの画像だけ、外から直接取れない場所へ置く】
    '
    ' 部屋あての画像は Uploads/images/ に置き、IISが静的ファイルとして返す。
    ' **アプリを1行も通らない**ので、いちばん軽い。
    '
    ' こっそりの画像は App_Data/private-images/ に置く。
    ' ASP.NETがこのフォルダへの直接の要求を拒むため、URLを打っても取れない。
    ' 取り出せるのは Chat/PrivateImage.aspx だけで、あちらが毎回
    ' 「この人は当事者か」を確かめる。
    '
    ' 【置き場所を分けないと、認可そのものが意味を失う】
    ' こっそりの画像を Uploads/ に置いたまま .aspx を用意しても、
    ' 元のURLを直接叩けばIISがそのまま返す。
    ' **確かめる入口の横に、確かめない入口が開いたまま**になる
    Private Const PublicRoot As String = "~/Uploads/images/"
    Private Const PrivateRoot As String = "~/App_Data/private-images/"

    ' 記事の画像。**LogCleanup の自動削除は、ここを触らない**（根が違う＋DeleteDayFoldersBefore が断る）
    Private Const ArticleRoot As String = "~/Uploads/articles/"

    ' 記事の画像の、ブラウザから見えるURLの頭（本文の <img src> に入る。ArticleHtml が src をこの形だけに絞る）
    Public Const ArticleUrlPrefix As String = "/Uploads/articles/"


    ' プロフィールの画像（ブログ型の①）。**自動では消さない**
    ' （差し替えたときに、その場で古い1枚を消すだけ。1人につき2枚しか置かないので、片付けの仕事を作っていない）
    Private Const ProfileRoot As String = "~/Uploads/profiles/"

    ' プロフィールの画像の、ブラウザから見えるURLの頭（<img src> に入る。組み立てるのは AccountProfileImages.SrcOf の1か所）
    Public Const ProfileUrlPrefix As String = "/Uploads/profiles/"


    ' 部屋あての画像の、ブラウザから見えるURLの頭
    '
    ' 【画面側にも同じ文字列がある】
    ' 置き場所を変えるときは、Scripts/chat-room.js の IMAGE_URL_PREFIX も一緒に直すこと
    Public Const PublicUrlPrefix As String = "/Uploads/images/"


    ' こっそりの画像を取り出すURL（画面側にも同じ文字列がある。上と同じ注意）
    Public Const PrivateUrlPath As String = "/chat/private-image"


    ' その画像の、サーバー上の置き場所
    '
    ' 【必ず IsValidName を通してから呼ぶこと】
    ' 通していない名前を渡すと、パスの外へ出られる
    Public Shared Function MapPath(imageName As String, place As ImagePlace) As String

        If Not IsValidName(imageName, place) Then
            Throw New ArgumentException("画像の名前の形が違います。", NameOf(imageName))
        End If

        Dim root As String = RootOf(place)

        ' 【HostingEnvironment.MapPath を使うこと。Server.MapPath ではない】
        ' ハブの中では HttpContext.Current が Nothing になることがある
        ' （IconCatalog が同じ理由で、こちらを使っている）
        Return Path.Combine(HostingEnvironment.MapPath(root),
                            imageName.Replace("/"c, Path.DirectorySeparatorChar))

    End Function


    ' 画像を保存する
    '
    ' 失敗したら False を返す（例外は投げない）。理由は errorText に入る。
    '
    ' 【なぜ、例外を投げないのか】
    ' 呼び出し側（受け口）は、どんな失敗でも「保存できませんでした」と
    ' 利用者へ返すしかない。例外にすると、その1点のために Try/Catch が増える
    Public Shared Function TrySave(bytes As Byte(),
                                   imageName As String,
                                   place As ImagePlace,
                                   ByRef errorText As String) As Boolean

        errorText = Nothing

        Try
            Dim fullPath As String = MapPath(imageName, place)

            ' 日付のフォルダは、その日の1枚目で作られる。
            ' 既にあれば何も起きない（CreateDirectory は、そういう作りになっている）
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath))

            ' 【CreateNew にしてあること】
            ' 名前はGUIDなので、ぶつかることは現実には起こらない。
            ' それでも上書きを許さない形にしてあるのは、**万一ぶつかったときに
            ' 他人の画像を静かに消してしまうのを防ぐ**ため
            Using stream As New FileStream(fullPath, FileMode.CreateNew,
                                           FileAccess.Write, FileShare.None)
                stream.Write(bytes, 0, bytes.Length)
            End Using

            Return True

        Catch ex As Exception

            ' 【ここは握りつぶさない】
            ' 保存できていないのに発言だけが流れると、みんなの画面に
            ' 壊れた画像が並ぶ。呼び出し側は、必ず利用者へ断りを返すこと
            errorText = ex.Message

            ' 利用者には断りが返るが、置き場所に書けない（権限・容量）なら全員が送れなくなっている。
            ' 黙らずに知らせる（それまでは Trace だけ）
            ErrorReport.Report(ex, "SiteImage.TrySave", PlaceText(place))

            Return False

        End Try

    End Function


    ' 保存した画像を消す
    '
    ' 【いつ使うのか】
    ' 保存したあとに発言が断られたとき（連投など）。
    ' 消し損ねても実害は無い——保存期間が過ぎれば、日付のフォルダごと消えるため。
    ' そのため、失敗しても黙って進む
    Public Shared Sub DeleteQuietly(imageName As String, place As ImagePlace)

        Try
            If Not IsValidName(imageName, place) Then
                Return
            End If

            Dim fullPath As String = MapPath(imageName, place)

            If File.Exists(fullPath) Then
                File.Delete(fullPath)
            End If

        Catch
            ' 消せなくても、次の掃除で消える
        End Try

    End Sub


    ' ============================================================
    ' 古い日付のフォルダを消す（14番。LogCleanup から呼ぶ）
    ' ============================================================
    '
    ' keepFrom より前の日付のフォルダ（yyyy/MM/dd）を、中身ごと消す。空になった月・年のフォルダも消す。
    '
    ' 【画像はログの行と結びつけず、日付のフォルダごと消す】
    ' 1枚ずつ DB と突き合わせない。どの日のフォルダを消してよいかは、呼ぶ側（LogCleanup）が
    ' 「まだ残っているいちばん古いログの日付」から決めて keepFrom に渡す。
    '
    ' 【触るのは、年4桁・月2桁・日2桁の名前のフォルダだけ】
    ' 置き場所の直下には Uploads/images/web.config（実行を殺す設定）がある。
    ' 形の違う名前のフォルダ・ファイルには、いっさい手を付けない
    '
    ' 【失敗しても止まらない】1つのフォルダが消せなければ ErrorReport へ知らせて、残りを続ける
    ' （消せなかったフォルダは、次の日の掃除でまた消しにいく）
    '
    ' 【記事の画像を渡したら、例外で止める】記事の画像は日付のフォルダではなく、自動では消さない。
    ' 間違って呼んでも1つも消えない形にしてある
    Friend Shared Function DeleteDayFoldersBefore(keepFrom As Date, place As ImagePlace) As FolderCleanupResult

        If place = ImagePlace.Article Then
            Throw New ArgumentException("記事の画像は、日付のフォルダで消しません。", NameOf(place))
        End If

        ' 【プロフィールの画像も、日付のフォルダで消さない】
        ' 記事の画像と同じ理由。間違って呼んでも1つも消えない形にしてある
        If place = ImagePlace.Profile Then
            Throw New ArgumentException("プロフィールの画像は、日付のフォルダで消しません。", NameOf(place))
        End If

        Dim result As New FolderCleanupResult()
        Dim where As String = PlaceText(place)

        Dim root As String = HostingEnvironment.MapPath(RootOf(place))

        If root Is Nothing OrElse Not Directory.Exists(root) Then
            Return result
        End If

        For Each yearDir As String In Directory.GetDirectories(root)

            Dim yearName As String = Path.GetFileName(yearDir)

            If Not YearFolderPattern.IsMatch(yearName) Then
                Continue For
            End If

            For Each monthDir As String In Directory.GetDirectories(yearDir)

                Dim monthName As String = Path.GetFileName(monthDir)

                If Not TwoDigitFolderPattern.IsMatch(monthName) Then
                    Continue For
                End If

                For Each dayDir As String In Directory.GetDirectories(monthDir)

                    Dim dayName As String = Path.GetFileName(dayDir)
                    Dim folderDate As Date

                    If Not TwoDigitFolderPattern.IsMatch(dayName) OrElse
                       Not Date.TryParseExact(yearName & monthName & dayName, "yyyyMMdd",
                                              CultureInfo.InvariantCulture, DateTimeStyles.None, folderDate) Then
                        Continue For
                    End If

                    If folderDate >= keepFrom Then
                        Continue For
                    End If

                    Try
                        Dim files As Integer = Directory.GetFiles(dayDir, "*", SearchOption.AllDirectories).Length
                        Directory.Delete(dayDir, recursive:=True)
                        result.Folders += 1
                        result.Files += files
                    Catch ex As Exception
                        result.Failed += 1
                        ErrorReport.Report(ex, "SiteImage.DeleteDayFoldersBefore", where & "／" & yearName & "/" & monthName & "/" & dayName)
                    End Try

                Next

                DeleteIfEmpty(monthDir, where)

            Next

            DeleteIfEmpty(yearDir, where)

        Next

        Return result

    End Function


    ' 空になったフォルダ（月・年）を消す。中身があれば何もしない
    Private Shared Sub DeleteIfEmpty(folder As String, where As String)

        Try
            If Directory.Exists(folder) AndAlso Not Directory.EnumerateFileSystemEntries(folder).Any() Then
                Directory.Delete(folder)
            End If
        Catch ex As Exception
            ' 空のフォルダが残るだけで害は無い（次の日にまた試す）。それでも黙らない
            ErrorReport.Report(ex, "SiteImage.DeleteIfEmpty", where)
        End Try

    End Sub

    ' 置き場所の根（~/…）
    Private Shared Function RootOf(place As ImagePlace) As String

        Select Case place
            Case ImagePlace.PrivateTalk
                Return PrivateRoot
            Case ImagePlace.Article
                Return ArticleRoot
            Case ImagePlace.Profile
                Return ProfileRoot
            Case Else
                Return PublicRoot
        End Select

    End Function


    ' 記事の画像の置き場所の根（サーバー上の場所）。無ければ Nothing
    '
    ' 【アカウントごとのフォルダを、まとめて見たいときに使う】使われていない画像の後片付け
    ' （ArticleImages.CleanupUnused）が、この下のフォルダを順に見る。
    ' **DeleteDayFoldersBefore は、ここを触らない**（記事の画像は日付で消さない。あちらは Article を渡すと例外）
    Friend Shared Function ArticleRootPath() As String
        Return HostingEnvironment.MapPath(ArticleRoot)
    End Function


    ' 記事の画像のフォルダ（アカウントごと）の、サーバー上の場所（容量を数える・使われていない画像を消すときに使う）
    Friend Shared Function ArticleFolderOf(accountId As Integer) As String

        Dim root As String = HostingEnvironment.MapPath(ArticleRoot)

        If root Is Nothing OrElse accountId <= 0 Then
            Return Nothing
        End If

        Return Path.Combine(root, accountId.ToString(CultureInfo.InvariantCulture))

    End Function


    ' 知らせ（エラー報告メール）に書く、置き場所の名前
    Private Shared Function PlaceText(place As ImagePlace) As String

        Select Case place
            Case ImagePlace.PrivateTalk
                Return "こっそりの画像"
            Case ImagePlace.Article
                Return "記事の画像"
            Case ImagePlace.Profile
                Return "プロフィールの画像"
            Case Else
                Return "部屋の画像"
        End Select

    End Function


    ' 年のフォルダ（4桁）と、月・日のフォルダ（2桁）の名前の形
    Private Shared ReadOnly YearFolderPattern As New Regex("^[0-9]{4}$", RegexOptions.CultureInvariant)
    Private Shared ReadOnly TwoDigitFolderPattern As New Regex("^[0-9]{2}$", RegexOptions.CultureInvariant)


    ' 拡張子から、ブラウザへ名乗る Content-Type を決める
    '
    ' 【こっそりの画像だけ、これが要る】
    ' 部屋あての画像はIISが対応表から決めてくれるが、
    ' こっそりのほうは .aspx が自分で流すので、自分で名乗る必要がある
    Public Shared Function ContentTypeOf(imageName As String) As String

        If imageName.EndsWith("." & ExtensionPng, StringComparison.Ordinal) Then
            Return "image/png"
        End If

        If imageName.EndsWith("." & ExtensionGif, StringComparison.Ordinal) Then
            Return "image/gif"
        End If

        Return "image/jpeg"

    End Function


    ' 寸法を、まともな範囲へ丸める
    '
    ' 【画面から来る言い値である】
    ' 縮小しているのはブラウザなので、寸法もあちらが数えている。
    ' 嘘をつかれても、**場所の取り方がずれるだけ**で何も壊れない
    ' （宛先をクライアントに名乗らせてよいのと、同じ性質の値である）。
    ' それでも丸めるのは、印の形（1〜4桁）を保つためと、
    ' 極端な値がそのままCSSへ流れないようにするため
    Public Shared Function ClampEdge(value As Integer) As Integer

        If value < 1 Then
            Return 1
        End If

        If value > 9999 Then
            Return 9999
        End If

        Return value

    End Function

End Class


' SiteImage.DeleteDayFoldersBefore の結果（消した日付のフォルダの数・その中のファイルの数・消せなかったフォルダの数）
Friend NotInheritable Class FolderCleanupResult
    Public Folders As Integer
    Public Files As Integer
    Public Failed As Integer
End Class
