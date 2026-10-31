' プロフィールの画像（アイコン・カバー）の置き場所と、差し替え（ブログ型の①）
'
' 【記事の画像（Articles/ArticleImages.vb）とは、根を分けてある】
' 記事の画像は、1日1回の片付けが「どの記事の本文からも使われていない画像」を消しにいく。
' プロフィールの画像は記事の本文に出てこないので、**同じフォルダに置いた瞬間、翌日に全部消える**。
' 置き場所は Uploads/profiles/アカウントの番号/GUID.拡張子（ImagePlace.Profile）。
'
' 【台帳（ArticleImageFiles のような表）は作っていない】
' 記事の画像に台帳が要ったのは、「どの本文から使われているか」が本文を読まないと分からなかったため。
' プロフィールの画像は**1人につき2枚だけ**で、使われている場所も列が2つあるだけなので、探す必要が無い。
'
' 【片付けの仕事も作っていない】差し替えた瞬間に、古い1枚をその場で消す。
' 消し損ねても、増えるのは1人あたり数枚で、伸び続ける形ではない
Public NotInheritable Class AccountProfileImages

    Private Sub New()
    End Sub


    ' ============================================================
    ' 大きさ
    ' ============================================================
    '
    ' 【RoleLimits に項目を足していない】（ロールごとに変える値ではない）
    ' 1人につき2枚しか置けず、差し替えると古いものが消えるので、**容量が伸びない**。
    ' RoleLimits は「将来ロールごとに変えたい値」の置き場所である（決まり）。
    '
    ' 【SiteSettings にも入れていない】管理画面から変えたい値ではなく、
    ' ASP.NET の maxRequestLength（4096KB）に収める形を守るための線である

    ' 1枚の上限（バイト）。ASP.NET の天井（SiteSettings.ImageMaxBytesCeiling）と、小さいほう
    Private Const PreferredMaxBytes As Integer = 2 * 1024 * 1024

    Public Shared ReadOnly Property MaxBytes As Integer
        Get
            Return Math.Min(PreferredMaxBytes, SiteSettings.ImageMaxBytesCeiling)
        End Get
    End Property


    ' 画面が縮めるときの、長辺の目安
    '
    ' 【アイコンは、画面側で正方形に切ってから送る】
    ' アイコンは、プロフィール・③の右の枠・記事の署名と、**出す場所が多い**。
    ' 元の縦横比のまま持つと、置く場所ごとに切り方を決めることになる。
    ' 1回切って保存すれば、出すところは全部同じでよい。
    '
    ' 【サーバーは、正方形かどうかを確かめられない】（確かめない）
    ' このサーバーは画像を復号しない、という決まり（Core/SiteImage.vb の DetectExtension）を守るため。
    ' **出すときに CSS の object-fit: cover で正方形にする**ので、切られていない画像が
    ' 開発者ツールから送られても、見た目は崩れない
    Public Const AvatarMaxEdge As Integer = 512

    ' カバーは横長のまま（縦横比は自由）。長辺だけを縮める
    Public Const CoverMaxEdge As Integer = 1600


    ' ============================================================
    ' URL
    ' ============================================================

    ' その画像の <img src>（名前が形に合っていなければ Nothing）
    '
    ' 【組み立てるのは、ここ1か所だけ】画面でもコードビハインドでも、文字列を繋がないこと
    Public Shared Function SrcOf(imageName As String) As String

        If Not SiteImage.IsValidName(imageName, ImagePlace.Profile) Then
            Return Nothing
        End If

        Return SiteImage.ProfileUrlPrefix & imageName

    End Function


    ' 画面から来た「どちらの画像か」を、決まった2つへ
    '
    ' 【当てはまらなければ False】受け口は、そのまま断る
    Public Shared Function TryParseKind(value As String, ByRef kind As ProfileImageKind) As Boolean

        Select Case If(value, "").Trim().ToLowerInvariant()

            Case "avatar"
                kind = ProfileImageKind.Avatar
                Return True

            Case "cover"
                kind = ProfileImageKind.Cover
                Return True

            Case Else
                Return False

        End Select

    End Function


    ' その種類の画像の、長辺の目安
    Public Shared Function MaxEdgeOf(kind As ProfileImageKind) As Integer
        Return If(kind = ProfileImageKind.Avatar, AvatarMaxEdge, CoverMaxEdge)
    End Function


    ' ============================================================
    ' 差し替え・取り外し
    ' ============================================================

    ' 画像を差し替える（imageName が Nothing なら、外すだけ）
    '
    ' 【順番が決まっている】① 新しい画像は、もう保存してある ② DB の列を書き換える
    ' ③ 書き換えが済んでから、古いファイルを消す。
    ' **③を先にやってはいけない**——DB の書き換えに失敗したら、
    ' DB が指しているファイルだけが消えて、絵の出ない画像になる。
    '
    ' 【古いファイルを消せなくても、止めない】残るだけで、害は無い（SiteImage.DeleteQuietly）
    Public Shared Sub Replace(accountId As Integer, kind As ProfileImageKind, imageName As String)

        Dim old As String = AccountProfileStore.UpdateImage(accountId, kind, imageName)

        ' 【「新しいメンバー」の枠の控えを捨てる】アイコンがそこにも出るため。
        ' 【下の「古い名前が無ければ戻る」より前へ移した】
        ' それまでは古い写真を差し替えたときにしか捨てておらず、**はじめて写真を置いた人は、枠に最大10分出なかった**
        If kind = ProfileImageKind.Avatar Then
            NewMembers.Invalidate()
        End If

        If String.IsNullOrEmpty(old) Then
            Return
        End If

        ' 【同じ名前なら消さない】ふつう起きないが（名前は毎回あたらしい GUID）、
        ' 消してしまうと「DB は指しているのにファイルが無い」になる
        If String.Equals(old, imageName, StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        SiteImage.DeleteQuietly(old, ImagePlace.Profile)

    End Sub

End Class
