' 退会（一般ユーザーの登録の回）
'
' 要望：**ログアウトだけでなく、退会（アカウントの削除）も要る**
'
' 【物理削除にした】
' 「30日は取り消せる論理削除」は採らない。**押したその場で、全部消える。**
'   ・アカウント／プロフィール／記事／編集中の控え／カテゴリ／画像のファイル
' 理由＝取り消せる仕組みは、**「消えていない期間」が長いほど嘘になる**。
' 「消えます」と書いて30日残すなら、そう書かないといけない。
' 一時部屋を「誰も居なくなったら閉じる」にしたのと、同じ割り切りである。
'
' 【サイト管理者は退会できない】
' 記事（使い方・利用規約・このサイトについて）を書いているアカウントなので、
' 消えるとサイトのメニューから行き先が全部消える。
' **画面にも区切りごと出さない**（Accounts/Settings.aspx）が、
' **本当の守りはここ**（URL を直に叩かれても断る）。
'
' 【ユーザー名は、ほかの人に使わせない】
' 消したあとに RetiredUserNames へ1行入れる（AccountStore.DeleteAccountCompletely の中で、
' 同じトランザクション）。**メールアドレスは残さない**（同じ人が、また登録できるように）。
' 理由の詳しいことは Database/CreateTable/RetiredUserNames.sql の先頭。
'
' 【何を、どの順でやるか】をここに持つ（AccountSettings・AccountReset と同じ立ち位置）
Module AccountRetire


    Public Class RetireResult

        Public Property Ok As Boolean

        ' 断った理由（受け付けたときは Nothing）。画面にそのまま出す
        Public Property Message As String

        ' 今のパスワードで断った（画面が、理由をその欄の上に出す）
        Public Property PasswordRejected As Boolean

    End Class


    Private Const TroubleMessage As String =
        "ただいま混み合っています。少し待ってから、もう一度お試しください。"


    ' 退会する
    '
    ' 【順番が決まっている】
    '   1. サイト管理者かを見る（DB もパスワードも触らない）
    '   2. 今のパスワードを確かめる（ログインの画面と同じ歯止めを通る）
    '   3. 消すファイルの名前を、**消す前に**読んでおく
    '   4. お知らせのメールを**積む**（消す前。宛先はアカウントの行のアドレス）
    '   5. DB を消す（ひとつのトランザクション）
    '   6. ファイルを消す（DB が消えてから）
    '   7. 控えを捨てて、ログアウトする
    '
    ' 【4を5より先にやる理由】SiteMail.EnqueueToAccount は、宛先の文字列を受け取って控えるので、
    ' **積んだあとに行が消えても、ちゃんと送られる。** 逆（消したあとに積む）にすると、
    ' 宛先を読む相手が居ない。
    ' 【ただし「積んだが、消せなかった」ことは起こりうる】
    ' そのときは「退会しました」のメールだけが届いて、アカウントは残る。
    ' **そちらのほうが安全な壊れ方である**（消えたのに知らせが届かない、の逆）。
    ' 画面には「退会できませんでした」と出るので、利用者は気づける。
    '
    ' 【6を5より後にやる理由】プロフィールの画像の差し替え（AccountProfileImages.Replace）と
    ' 同じ考え方。ファイルを先に消すと、DB の削除に失敗したときに
    ' **「DB は画像を指しているのに、ファイルが無い」**が残る
    Public Function Retire(context As HttpContext, currentPassword As String) As RetireResult

        If context Is Nothing Then
            Return Fail(TroubleMessage)
        End If

        Dim account As AccountStore.AccountRecord = AccountSession.Current(context)

        If account Is Nothing Then
            ' 画面（AccountPageBase）が先に弾いているので、ここへは来ない
            Return Fail(TroubleMessage)
        End If

        ' === 1. サイト管理者は退会できない ===
        If String.Equals(account.RoleKey, RoleKeys.Administrator, StringComparison.Ordinal) Then
            Return Fail("サイト管理者のアカウントは、退会できません。")
        End If

        ' === 2. 今のパスワード ===
        '
        ' 【ログインの画面と、同じ入れ物の歯止めを通る】（AccountSettings.CheckCurrentPassword）
        ' 数えるキーがそのアカウントのユーザー名なので、**入口を増やしても、試せる回数は増えない**
        Dim reason As String = AccountSettings.CheckCurrentPassword(context, account, currentPassword)

        If reason IsNot Nothing Then
            ' 【今のパスワードの欄が悪い】画面が、理由をその欄の上に出す
            Return New RetireResult With {.Ok = False, .Message = reason, .PasswordRejected = True}
        End If

        ' === 3. 消すファイルを、消す前に読んでおく ===
        '
        ' 【プロフィールの画像は、DB の列にしか名前が無い】行を消したあとでは、もう分からない。
        ' 【記事の画像は、フォルダごと消せる】名前は Uploads/articles/<アカウントの番号>/ の下に
        ' 揃っているので、台帳（ArticleImageFiles）を読まなくてよい
        Dim avatarName As String = Nothing
        Dim coverName As String = Nothing

        Try

            Dim profile As AccountProfileStore.ProfileRecord =
                AccountProfileStore.Find(account.AccountId)

            If profile IsNot Nothing Then
                avatarName = profile.AvatarImageName
                coverName = profile.CoverImageName
            End If

        Catch ex As Exception

            ' 【止めない】画像の名前が読めなくても、退会そのものは進めてよい
            ' （残るのはファイル2枚で、DB からは切り離される＝誰にも出てこない）
            ErrorReport.Report(ex, "AccountRetire.Retire（プロフィールの画像の名前を読む）")

        End Try

        ' === 4. お知らせのメールを積む（消す前） ===
        AccountMail.NotifyRetired(account)

        ' === 5. DB を消す ===
        Try

            If Not AccountStore.DeleteAccountCompletely(account.AccountId, account.TokenGeneration,
                                                        account.UserName, account.Email) Then

                ' 世代が合わなかった（ほかの端末でパスワードが変わった）か、行がもう無い
                Return Fail("ほかの画面で先に何かが変わりました。お手数ですが、ログインし直してからもう一度お試しください。")

            End If

        Catch ex As Exception

            ' 【知らせる】ここで失敗すると、記事だけ消えて行が残る……ことは無い
            ' （ひとつのトランザクションなので、丸ごと戻る）。それでも原因は見たい
            ErrorReport.Report(ex, "AccountRetire.Retire（DB を消す）",
                               "アカウント " & account.AccountId)

            Return Fail(TroubleMessage)

        End Try

        ' === 6. ファイルを消す（DB が消えてから） ===
        '
        ' 【消せなくても、退会は成功である】残るだけで、どこからも出てこない
        ' （DB に名前が1つも残っていないので、URL を知っている人にしか見えない）。
        ' それでも黙らないよう、中で ErrorReport が呼ばれる形にしてある
        SiteImage.DeleteQuietly(avatarName, ImagePlace.Profile)
        SiteImage.DeleteQuietly(coverName, ImagePlace.Profile)

        Try
            ArticleImages.DeleteAccountFolder(account.AccountId)
        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRetire.Retire（記事の画像のフォルダを消す）")
        End Try

        ' === 7. 控えを捨てて、ログアウトする ===
        '
        ' 【SiteArticles の控えも捨てる】記事が消えたので、
        ' 右上のメニューと更新履歴を組み直させる（消えた記事へのリンクを出さないため）
        SiteArticles.Invalidate()

        ' 【「新しいメンバー」の枠から消す】退会した人が、控えの寿命の間だけ残らないように
        NewMembers.Invalidate()

        ' 【右上の角のアイコンの控えも捨てる】（Accounts/AccountAvatar.vb）
        AccountAvatar.Invalidate(account.AccountId)

        ' 【チャットに出ているアカウントID を外す】（チャットとアカウントの紐づけ）
        ' いま部屋やラウンジに、このアカウントとして居るなら、番号とユーザー名をメモリから消す（席はそのまま）。
        ' DB のログの側は、上の DeleteAccountCompletely が UserId を NULL に戻してある。
        ' 外せなくても、退会そのものは済んでいる（知らせだけ送る）
        Try
            ChatHub.DetachAccount(account.AccountId, account.UserName)
        Catch ex As Exception
            ErrorReport.Report(ex, "AccountRetire.Retire（チャットのアカウントID を外す）",
                               "AccountId=" & account.AccountId.ToString(Globalization.CultureInfo.InvariantCulture))
        End Try

        AccountSession.SignOut(context)

        Return New RetireResult With {.Ok = True}

    End Function


    Private Function Fail(message As String) As RetireResult
        Return New RetireResult With {.Ok = False, .Message = message}
    End Function

End Module
