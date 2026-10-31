' 入退室などのイベント行に表示する文言を、組み立てるためのクラス
'
' 【なぜ、この1か所にまとめるのか】
' イベント行は、次の2つの経路で画面に出る。
'   ・過去ログ    … ChatMessagesテーブル → ChatLogReader → ページに埋め込み
'   ・リアルタイム … ChatHub → SignalRの放送
' クライアント側でも文言を組み立てると、同じ日本語を2か所で管理することになり、
' 片方だけ直して食い違う事故が必ず起きる。そのため、文言はサーバー側のここだけで作り、
' クライアントは受け取って表示するだけにしている。
'
' 種別32（無言タイムアウト）から、理由の文言を外した。
' それまでは「（1時間発言がなかったため）」と時間を入れており、
' その数字を嘘にしないため ChatHub.GetSilentTimeoutText() をここから呼んでいたが、
' **理由そのものを会話に流さないと決めた**ので、その必要も無くなった。
' 時間の入った文言は、本人へ届く Kicked のほうにだけ残っている
' （ChatHub.RemoveParticipantAndNotify の kickedMessage）
Public Class ChatEventText

    ' イベント行の文言を組み立てる
    '
    ' messageType … ログの種別（ChatMessageType）
    ' nickname    … その時点のニックネーム。過去ログの場合は、ログに焼き込まれた
    '                当時の値をそのまま渡すこと（今のCookieの値ではない）
    '
    ' 戻り値：表示する文言。イベント行として表示すべきでない種別ではNothingを返す
    '         （呼び出し側は、Nothingなら行を作らない）
    ' 部屋の設定の変更（61）の行
    '
    ' detail … 何をどう変えたか（ChatHub.RoomSettings.vb が組み立て、ログの本文の列にもそのまま入れる）。
    '          例：部屋名「雑談」・定員 3人・パスワードを外した
    ' 決定：変えた項目は全部出してよい。**1回の保存で1行**（3つ変えて3行並べない）。
    ' 変えた人の名前は出さない（「管理人が」。誰が管理人かは参加者一覧の札で分かる。記録には名前が焼き込まれる）。
    ' 【部屋名が入る】画面は、この文言を textContent で入れる（イベント行はどれも同じ）
    Public Shared Function BuildRoomSettingsChanged(detail As String) As String

        If String.IsNullOrEmpty(detail) Then
            Return "管理人が部屋の設定を変えました"
        End If

        Return "管理人が部屋の設定を変えました（" & detail & "）"

    End Function


    Public Shared Function Build(messageType As ChatMessageType, nickname As String) As String

        ' ニックネームが空の場合でも、文言が「さんが入室しました」と欠けた形に
        ' ならないようにしておく（ログのNickname列はNOT NULLだが、空文字はありえる）
        Dim name As String = If(String.IsNullOrEmpty(nickname), "名無しさん", nickname)

        Select Case messageType

            Case ChatMessageType.Message
                ' 発言は吹き出しで表示するため、イベント行にはしない
                Return Nothing

            Case ChatMessageType.PrivateMessage

                ' 【記録はするが、部屋のログには絶対に出さない種別】
                '
                ' こっそりの発言は、宛先の1人にしか見せてはいけない。
                ' 発言と同じく吹き出しで表示するもので、イベント行にはならない。
                '
                ' 【Case Elseに任せず、明示的に書いてある理由】
                ' 下のCase Elseは「30番台なら退室、それ以外はNothing」なので、
                ' 書かなくても今はNothingに落ちる。それでも書いてあるのは、
                ' 将来Case Elseの既定を変えた日に、こっそりの本文が
                ' 部屋の全員に見える形へ黙って転ぶのを防ぐため。
                ' ここは「たまたま安全」で済ませてよい場所ではない
                Return Nothing

            Case ChatMessageType.Join
                Return name & "さんが入室しました"

            Case ChatMessageType.Leave
                ' 退室ボタン、および別の部屋への移動。
                ' 利用者にとっては、どちらも単なる退室のため、理由は添えない
                Return name & "さんが退室しました"

            Case ChatMessageType.Disconnected

                ' タブ・ブラウザを閉じた、あるいは通信が切れたまま猶予を過ぎた場合。
                '
                ' 【なぜ、理由を添えないのか】
                ' 上のLeaveと同じで、利用者にとっては単なる退室だからである。
                ' 加えて、以前の「（ブラウザを閉じました）」という文言は事実として正しくない。
                ' この種別には、電波が切れた・回線が落ちた・タブが落ちた場合も含まれる。
                ' サーバー側からは、そのどれであったかを区別できない。
                ' 区別できないことを、断定した文言で会話に流してはいけない
                Return name & "さんが退室しました"

            Case ChatMessageType.Timeout

                ' 無言タイムアウト（既定1時間）。
                '
                ' 【なぜ、理由を添えないのか】
                ' 上のLeave・Disconnectedと同じで、**内部の都合を会話に流さない**ため
                ' （「（1時間発言がなかったため）」を外した。
                '   種別31から「（ブラウザを閉じました）」を外したのと同じ判断）。
                ' 周りの人にとっては単なる退室であり、なぜ居なくなったかは要らない。
                '
                ' 【本人には、ちゃんと理由が伝わる】
                ' 退室させられた本人には Kicked で
                ' 「〇〇発言がなかったため、退室しました」が届き、ラウンジで帯として出る。
                ' あちらは本人向けなので、時間を含んだ文言のまま残してある
                ' （RemoveParticipantAndNotify の kickedMessage）。
                ' **こちらを消しても、本人が理由を知る手段は減らない**
                Return name & "さんが退室しました"

            Case ChatMessageType.Replaced

                ' 【記録はするが、表示はしない種別】
                '
                ' 同一ブラウザの2窓目に置き換えられたケース。
                ' 別のタブを開いたのは本人の都合であって、同じ部屋にいる他の人にとっては
                ' 何の意味も無い。外から見れば、その人はずっとそこにいる。
                ' 「退室しました」の直後に「入室しました」が流れるのは、
                ' 内部の都合が会話に漏れているだけになるため、行を作らない。
                '
                ' F5を連打したときに画面へ流れていた2行も、中身はこれとまったく同じ
                ' 「33＋20」である。ここを出さないことで、退室ビーコンがJoinRoomとの
                ' 競争に勝っても負けても画面は静かなままになり、
                ' ビーコンの到着順に依存しない作りになる。
                '
                ' 【ここをNothingにしても、ログは消えない】
                ' ChatMessagesへの記録（ChatLog.Write）は、これとは別の経路で
                ' これまで通り行われる。荒らしへの対応など、後から分析する材料としての
                ' 価値があるため、記録そのものは残すこと。
                ' ここが決めているのは「画面に出すかどうか」だけである。
                '
                ' なお、33の直後に続く同じ人の入室(20)も、あわせて表示しない。
                ' そちらは行の並びを見ないと判断できないため、ここではなく
                ' ChatHub.JoinRoom（リアルタイム）とChatLogReader（過去ログ）で対応している
                Return Nothing

            Case ChatMessageType.Logout

                ' ログアウト（自分の身元ごと終わらせた）。
                '
                ' 【なぜ、理由を添えないのか】
                ' 上のLeave・Disconnected・Timeoutとまったく同じで、
                ' **内部の都合を会話に流さない**ため。
                ' 周りの人にとっては単なる退室であり、その人が身元を作り直したことまで
                ' 知らせる必要は無い。むしろ知らせないほうがよい——
                ' 「あの人が別人になった」と分かると、
                ' **別人になれること自体の意味が失われる**
                '
                ' 【Case Elseに任せず、明示的に書いてある理由】
                ' 下のCase Elseが30番台を「退室しました」に丸めるので、
                ' 書かなくても今は正しく出る。それでも書いてあるのは、
                ' **ここに理由を足したくなる人が必ず出るから**である
                ' （「ログアウトしました」と書きたくなる。上の判断がその答え）
                Return name & "さんが退室しました"

            Case ChatMessageType.KickedByOwner

                ' 管理人が退室させた。
                ' 【ふつうの退室と違い、理由を書く】部屋の人にとって意味のある情報のため。
                ' 「内部の都合を会話に流さない」（30〜32・37）とは向きが違う＝これは部屋の中で起きた出来事そのもの
                Return name & "さんが管理人により退室させられました"

            Case ChatMessageType.BannedByOwner

                ' 管理人が入室禁止にした。理由を書くのは上と同じ
                Return name & "さんが管理人により入室禁止になりました"

            Case ChatMessageType.JoinRejectedFull, ChatMessageType.JoinRejectedPassword, ChatMessageType.JoinRejectedBanned

                ' 入室を断った記録（40番台）。入っていない人なので、部屋のログには出さない。
                ' Case Else でも Nothing に落ちるが、既定に任せずに書いておく
                Return Nothing

            Case ChatMessageType.KnockRequested, ChatMessageType.KnockApproved, ChatMessageType.KnockDenied,
                 ChatMessageType.KnockExpired, ChatMessageType.KnockSeatExpired

                ' ノックの記録。**部屋の過去ログには出さない**。
                ' 「〇〇さんがノックしました」は、その場の配信でだけ出す（下の BuildKnock）。
                ' 理由は ChatMessageType.KnockRequested のコメント
                Return Nothing

            Case ChatMessageType.Login, ChatMessageType.LoungeLogout, ChatMessageType.LoungeVisit

                ' 部屋の外の出来事（50番台。RoomId = 0）。どの部屋のログにも出さない。
                ' 今はこの種別で Build を呼ぶところが無く、Case Else でも Nothing に落ちるが、
                ' こっそり（11）と同じく、既定に任せずに書いておく
                Return Nothing

            Case ChatMessageType.RoomSettingsChanged
                ' 部屋の設定の変更。何を変えたかは本文の列にあるので、
                ' ふつうは BuildRoomSettingsChanged で組み立てる（ChatLogReader・ChatHub.RoomSettings.vb）。
                ' ここは本文が読めなかったときの受け皿
                Return "管理人が部屋の設定を変えました"

            Case ChatMessageType.RoomBanLifted

                ' 入室禁止を解除した。**部屋のログには出さない**（記録だけ。理由は ChatMessageType のコメント）
                Return Nothing

            Case ChatMessageType.OwnerChanged, ChatMessageType.OwnerHandedOver

                ' 62（指名でゆずった）も、部屋の人から見れば同じ出来事なので、同じ文言

                ' 管理人が替わった。nickname は**新しい管理人**の名前。
                ' 決定：「管理人が〇〇さんに替わりました」は全員に出す。
                ' 【なぜ、前の管理人の名前を入れないのか】直前に「〇〇さんが退室しました」の行が出ているため。
                ' 過去ログでも、その行のすぐあとに並ぶ
                Return "管理人が" & name & "さんに替わりました"

            Case ChatMessageType.KickedByAdmin
                ' ※機能自体は将来実装。文言だけ先に用意してある
                Return name & "さんが退室しました（サイト管理者による強制退室）"

            Case Else

                ' 【なぜ、ここで「退室しました」に丸めるのか】
                ' 退室の種別は30番台にまとめてあり、将来また増える可能性がある
                ' （ChatMessageType の設計方針）。新しい種別を足したときに
                ' このSelect Caseへの追加を忘れても、行が丸ごと消えるのではなく、
                ' 理由なしの「退室しました」として最低限は表示されるようにしておく
                If messageType >= ChatMessageType.Leave AndAlso
                   messageType < CType(40, ChatMessageType) Then
                    Return name & "さんが退室しました"
                End If

                ' 30番台でも発言でもない、想定外の種別。行を作らない
                Return Nothing

        End Select

    End Function


    ' 「〇〇さんがノックしました」
    '
    ' ノックが届いたときに、部屋の中へその場で配る行（ChatHub.Knock.vb）。
    ' 過去ログには出さないので、Build（種別から作る）とは分けてある。
    ' 文言をサーバーで作るのは、ほかのお知らせ行と同じ理由（クライアントに日本語を持たせない）
    Public Shared Function BuildKnock(nickname As String) As String

        Dim name As String = If(String.IsNullOrEmpty(nickname), "名無しさん", nickname)

        Return name & "さんがノックしました"

    End Function

    ' 管理人の小窓が消えるときに、その小窓の中へ出す理由
    '
    ' 指摘：管理人から見ると、なぜ小窓が消えたのかが分からない
    ' 「ログじゃなく小窓でいいんじゃないの？」。**部屋のログには残さない**（管理人だけの話で、部屋の会話ではない）。
    ' 画面は、ノックの小窓をこの文言に差し替え、少し置いてから消す（×でも閉じられる）。
    '
    ' 【出さない場面】その人が、空いた席から普通に入ってきたとき（「入室しました」の行で分かる）。
    ' そのときは呼び出し側が Nothing を渡す
    Public Shared Function BuildKnockWithdrawn(reason As KnockWithdrawReason, nickname As String) As String

        Dim name As String = If(String.IsNullOrEmpty(nickname), "名無しさん", nickname)

        Select Case reason

            Case KnockWithdrawReason.Expired
                Return name & "さんのノックは、時間切れで取り消されました"

            Case KnockWithdrawReason.LoggedOut
                ' 【「ログアウト」と言わない】アカウントの「ログアウト」と区別がつかなくなるため
                '。**Enum の名前（LoggedOut）はそのまま**
                Return name & "さんがチャットを出たため、ノックは取り消されました"

            Case KnockWithdrawReason.SeatExpired
                Return name & "さんは入室しませんでした（取り置きを外しました）"

            Case KnockWithdrawReason.SeatReleased
                Return name & "さんは入室をやめました（取り置きを外しました）"

            Case KnockWithdrawReason.SeatLoggedOut
                Return name & "さんがチャットを出たため、取り置きを外しました"

            Case Else
                Return Nothing

        End Select

    End Function

End Class


' ノックの小窓が消えた理由（ChatEventText.BuildKnockWithdrawn）
Public Enum KnockWithdrawReason
    Expired = 1        ' 管理人が決めないまま、時間が過ぎた
    LoggedOut = 2      ' 返事待ちの間に、ノックした人がログアウトした
    SeatExpired = 3    ' 許可したが、取り置きの時間内に入ってこなかった
    SeatReleased = 4   ' 許可したが、ノックした人が「やめる」を押した
    SeatLoggedOut = 5  ' 許可したが、ノックした人がログアウトした
End Enum
