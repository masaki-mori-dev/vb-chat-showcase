Imports System.Linq

' ============================================================
' チャットとアカウントの紐づけのうち、ハブのメモリを見る・直す部分
' ============================================================
'
' ChatHub の続き（Partial）。部屋の参加者リスト（RoomParticipants）・ラウンジの顔の控え（LoungeProfiles）と、
' 鍵（SyncRoot）をそのまま使う。
'
' 【ここに在るのは2つだけ】
'   IsAccountInRoom … 「自分が参加したチャット履歴」（Chat/AccountChatLog.vb）が、今もその部屋に居るかを聞く
'   DetachAccount   … 退会したとき、部屋・ラウンジに出ているアカウントID を外す
'
' 【「誰がアカウントID を表示しているか」を決める処理は、ここには無い】
' それは Chat/ChatAccountLink.vb の1か所（2つの札と DB）。ここは、その結果が入ったメモリを読む・消すだけ
'
' 【どちらも DB を引かない】鍵の中はメモリだけ（鍵を握ったまま DB を引くと、詰まっている間、全部屋が止まる）
Partial Public Class ChatHub


    ' そのブラウザが、そのアカウントとして、今その部屋に居るか
    '
    ' 【3つとも合うときだけ True】部屋・ブラウザ・アカウントの番号。
    ' ブラウザだけで見ないのは、同じブラウザが「名乗らずに」入り直していることがあるため
    ' （そのときは、アカウントの記録としては「もう居ない」である）
    '
    ' 【BrowserId は、Guid に直してから比べる】参加者リストが持っているのは Cookie の生の文字列（TryGetRoomIdByBrowserId と同じ罠）
    Public Shared Function IsAccountInRoom(roomId As Integer, browserId As Guid, accountId As Integer) As Boolean

        If accountId <= 0 OrElse browserId = Guid.Empty Then
            Return False
        End If

        SyncLock SyncRoot

            Dim list As List(Of Participant) = Nothing

            If Not RoomParticipants.TryGetValue(roomId, list) Then
                Return False
            End If

            For Each p In list

                Dim stored As Guid

                If p.AccountId = accountId AndAlso
                   Guid.TryParse(p.BrowserId, stored) AndAlso stored = browserId Then
                    Return True
                End If

            Next

        End SyncLock

        Return False

    End Function


    ' 退会したアカウントを、部屋とラウンジのメモリから外す
    '
    ' 呼び出し元は Accounts/AccountRetire.vb（DB から消したあと）。
    '
    ' 【何をするか】そのアカウントとして参加している人の、番号とユーザー名を消す。**席はそのまま**
    ' （チャットの身元は別物なので、部屋から出すことはしない。以後は、名乗っていない参加者として居続ける）。
    '   ・このあと書かれる行（発言・退室）には、アカウントの番号が入らない（DB 側も、退会で UserId を NULL に戻してある）
    '   ・このあと入ってくる人の参加者一覧・ラウンジの顔には、アカウントID が出ない
    '
    ' 【ほかの人の画面に、もう出ているぶんは消えない】（承知のうえ）
    ' 配り直してはいない。入り直す・読み込み直すと消える。それまでのあいだ、押すと「ページが見つかりません」になる
    ' （公開のプロフィールが、もう無いため）。退会はめったに起きず、そのために配信の種類を1つ足すほどではない、と判断した
    '
    ' userName … ラウンジの顔の控えは番号を持っていないので、ユーザー名で探す（ユーザー名は重ならない）
    Public Shared Sub DetachAccount(accountId As Integer, userName As String)

        If accountId <= 0 Then
            Return
        End If

        SyncLock SyncRoot

            For Each roomEntry In RoomParticipants
                For Each p In roomEntry.Value
                    If p.AccountId = accountId Then
                        p.AccountId = 0
                        p.AccountName = Nothing
                    End If
                Next
            Next

            If Not String.IsNullOrEmpty(userName) Then
                ' 【変数名を profile にしないこと】System.Web.Profile（名前空間）とぶつかって、ビルドが止まる（変数名を mail にしたときと同じ落とし穴）
                For Each face In LoungeProfiles.Values
                    If String.Equals(face.AccountName, userName, StringComparison.OrdinalIgnoreCase) Then
                        face.AccountName = Nothing
                    End If
                Next
            End If

        End SyncLock

    End Sub


End Class
