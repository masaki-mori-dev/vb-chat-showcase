' 「新しいメンバー」の枠の控えと、HTML の組み立て
'
' 【どこに出すか】サイト全体の右の枠（トップとラウンジ）。並びは
' 「管理人からのお知らせ → 新しいメンバー → 更新履歴」（Site.Master の SiteSidebar）。
' **ブログのページの右の枠には出さない**（あちらはその人のブログの枠＝BlogSidebar）。
'
' 【毎回 DB を読まない】トップとラウンジは、人がいちばん来る画面なので、10分の控えを持つ
' （Notices/SiteNotices.vb・Articles/SiteArticles.vb と同じ形。**片方を直したら、ほかも見ること**）。
' 控えを捨てるのは、枠の中身が変わるとき＝登録が終わった・退会した・名前（表示名）やアイコンが変わった。
' **SSMS で「紹介しない」の印（Accounts.ExcludeFromNewMembers）を立てたときは、最大10分で反映**（捨てる口が無いため）。
'
' 【名前・アイコンは利用者が入れたもの】出すときに必ずエスケープする（名前は HtmlEncode、画像の行き先は HtmlAttributeEncode）
Public NotInheritable Class NewMembers

    Private Sub New()
    End Sub

    ' 出す人数
    ' 【、管理画面で変えられる】（SiteSettings.SidebarNewMemberCount。既定5・1〜20。基本ルール T）
    Public Shared ReadOnly Property ShowCount As Integer
        Get
            Return SiteSettings.SidebarNewMemberCount
        End Get
    End Property

    Private Shared ReadOnly Lifetime As TimeSpan = TimeSpan.FromMinutes(10)

    ' DB が読めなかったとき、次に読み直すまで（読めないのに毎回読みに行って、ページを重くしない）
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)

    Private Shared ReadOnly SyncRoot As New Object()
    Private Shared _items As List(Of AccountStore.NewMemberRecord)
    Private Shared _expiresAt As DateTime = DateTime.MinValue

    ' 【世代】読んでいる途中に Invalidate されたら、読み終えた古い中身を控えにしない（SiteNotices と同じ）
    Private Shared _generation As Integer


    Public Shared Function Current() As List(Of AccountStore.NewMemberRecord)

        Dim generation As Integer

        SyncLock SyncRoot

            If _items IsNot Nothing AndAlso DateTime.UtcNow < _expiresAt Then
                Return _items
            End If

            generation = _generation

        End SyncLock

        ' 【DB は鍵の外で読む】鍵を持ったまま読むと、DB が詰まっている間、ほかのページが全部待つ
        Dim loaded As List(Of AccountStore.NewMemberRecord)
        Dim lifetimeToUse As TimeSpan = Lifetime

        Try
            loaded = AccountStore.GetNewMembers(ShowCount)
        Catch ex As Exception

            ' 【見せるだけの値なので、ページは止めない】（フェイルオープン）枠が出ないだけ
            ErrorReport.Report(ex, "NewMembers.Current")

            loaded = New List(Of AccountStore.NewMemberRecord)()
            lifetimeToUse = RetryAfterFailure

        End Try

        SyncLock SyncRoot

            If generation = _generation Then
                _items = loaded
                _expiresAt = DateTime.UtcNow + lifetimeToUse
            End If

        End SyncLock

        Return loaded

    End Function


    ' 控えを捨てる（登録が終わった・退会した・名前やアイコンが変わった）
    Public Shared Sub Invalidate()

        SyncLock SyncRoot
            _items = Nothing
            _expiresAt = DateTime.MinValue
            _generation += 1
        End SyncLock

    End Sub


    ' 右の枠の HTML（1人も居なければ空文字＝見出しごと出さない）
    '
    ' 【1人ぶん】アイコン（無ければ灰色の丸）＋名前。押すとプロフィールへ。
    ' 【アイコンの alt は空】すぐ隣に名前が文字で出ている（読み上げソフトが2回言わないように）
    Public Shared Function Html() As String

        Dim items As List(Of AccountStore.NewMemberRecord) = Current()

        If items Is Nothing OrElse items.Count = 0 Then
            Return ""
        End If

        Dim sb As New Text.StringBuilder("<div class=""new-members""><h3>新しいメンバー</h3><ul>")

        For Each m As AccountStore.NewMemberRecord In items

            sb.Append("<li><a href=""")
            sb.Append(HttpUtility.HtmlAttributeEncode(AccountProfileRules.PublicUrl(m.UserName)))
            sb.Append(""">")

            ' 【アイコンは共通の部品】写真が無ければ「色の付いた丸に1文字」（Accounts/AccountAvatar.vb）
            sb.Append(AccountAvatar.Html(
                AccountAvatar.Resolve(m.AccountId, m.UserName, m.DisplayName,
                                      m.AvatarImageName, m.AvatarLetter, m.AvatarColor),
                "sm", "new-member-avatar"))

            sb.Append("<span class=""new-member-name"">").Append(HttpUtility.HtmlEncode(m.DisplayName)).Append("</span>")
            sb.Append("</a></li>")

        Next

        sb.Append("</ul></div>")

        Return sb.ToString()

    End Function

End Class
