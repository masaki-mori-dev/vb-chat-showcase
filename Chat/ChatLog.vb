Imports System.Configuration
Imports System.Data.SqlClient

' チャットのログ種別
' 数字に意味を持たせ、種別が増えたときに間へ差し込めるよう、10刻みを基本に採番している
Public Enum ChatMessageType

    Message = 10          ' 発言

    ' こっそりの発言（部屋の全員ではなく、その部屋に居る1人だけに宛てた発言）。
    ' 宛先は ChatMessages.RecipientBrowserId 列に入る。
    '
    ' 【なぜ10の隣なのか】
    ' どちらも「人が打った文章」で、性質が同じだからである。
    ' 10刻みの採番は、まさにこう間へ差し込むためにしてある
    '
    ' 【本文を持つ種別である】
    ' 下の HasMessageText に、この番号が入っていること。
    ' 入れ忘れると、**本文がNULLで保存されて、F5するまで気づけない**
    ' （実際に踏んだ）
    PrivateMessage = 11

    Join = 20             ' 入室

    ' --- 30番台：退室系 ---
    ' 退室の理由を、あえて別々の種別に分けている。
    ' 30番台にまとめてあるのは、後から在室期間を復元する際に
    ' 「30番台ならどれでも在室終了」と範囲で判定できるようにするため。
    ' 新しい退室理由が増えても、判定条件を書き換えずに済む
    Leave = 30            ' 退室（退室ボタンを押した）
    Disconnected = 31     ' 切断退室（ブラウザ・タブを閉じた、通信が切れた）
    Timeout = 32          ' 無言タイムアウト退室（既定1時間、発言が無いまま経過）
    Replaced = 33         ' 強制退室：システム（同一ブラウザの別接続に置き換えられた）
    KickedByOwner = 34    ' 強制退室：部屋の管理人が「退室させる」を押した（その時限り＝すぐ入り直せる）
    KickedByAdmin = 35    ' 強制退室：サイト管理者によるキック（将来実装）
    NoResponse = 36       ' 応答なし（現在は未使用。番号の予約）

    ' ログアウト（追加）
    '
    ' 【なぜ Leave（30）を使い回さないのか】
    ' どちらも本人の意思による退室だが、**起きたことの重さが違う。**
    ' 30は「この部屋から出た」だけで、その人は同じ身元のまま別の部屋へ行ける。
    ' 37は「この身元そのものを終わらせた」——ChatBrowserId が新しくなるので、
    ' 以後その人は別人として現れる。
    ' 退室の理由を分けているのは、荒らしの挙動の分析や設定調整の材料にするため
    ' （30番台の設計方針そのもの）で、身元の作り直しはまさに見たい材料である。
    '
    ' 番号を足すだけで済むのは、30番台を範囲で判定する作りにしてあるおかげである。
    ' ChatEventText の Case Else も、ChatLogReader.GetLastLeaveType の
    ' 「MessageType >= 30 AND MessageType < 40」も、書き換えずに済んでいる
    Logout = 37

    ' 強制退室：部屋の管理人が「入室禁止にする」を押した
    '
    ' 【なぜ 34（退室させた）を使い回さないのか】起きたことの重さが違う（37 を 30 と分けたのと同じ考え方）。
    ' 34 はその時限りで、すぐ入り直せる。38 は、この部屋には（管理人が解除するまで）もう入れない。
    ' 部屋の過去ログの文言も違う（「管理人により退室させられました」／「管理人により入室禁止になりました」）。
    ' 再接続で席が無かったときの理由（ChatHub.BuildSeatLostReason）も、この2つで分ける。
    ' 記録する人は、退室させられた人（ほかの退室と同じ。在室期間の復元に使う）。
    ' 禁止そのものは ChatRoomBans 表が持つ（この記録は「いつ禁止にしたか」の記録であって、状態ではない）
    BannedByOwner = 38

    ' ※35は、まだ機能自体が存在しない（34 は 使い始めた）。番号を予約し、意味を明確にしておくために
    '   先に定義している。管理人によるキックとサイト管理者によるキックを分けているのは、
    '   荒らし対応の記録として、両者の重みが大きく異なるため

    ' --- 40番台：入室できなかった記録 ---
    ' 「部屋にいた記録」ではなく「入ろうとして断られた記録」であり、
    ' 在室期間の復元には使わない。荒らしの挙動の分析と、定員の調整の材料にする。
    ' そのためLocalIdは常にNULL（番号が発行される前に断っているため）
    JoinRejectedFull = 40  ' 入室拒否：満員だった

    ' 入室拒否：パスワードが違った・無かった
    '
    ' 書くのは2か所。どちらも満員（40）と同じ歯止め（同じ相手は10分に1回・覚える相手の上限）を通す
    ' （ChatHub.ShouldLogJoinReject。キーは種別ごとに分けてある）。
    '   ・ラウンジでパスワードを間違えた（Lounge.EnterRoom）… いちばん多い経路
    '   ・鍵を通らずに JoinRoom を呼ばれた（ChatHub.JoinRoom）… ハブを直接叩かれたか、印の期限が切れた
    ' 間違えすぎて待たせている間（RoomPassword の歯止め）は、書かない（試してもいないため）。
    ' **打たれたパスワードは記録しない**（本文の列は空のまま。ChatLog.HasMessageText に入れていない）
    JoinRejectedPassword = 41

    ' ノック（Chat/Hubs/ChatHub.Knock.vb）
    '
    ' 満室の部屋へ「入れてほしい」と伝えたこと、その行く末の記録。
    ' どれも「入ろうとした記録」なので、40番台に並べてある（在室期間の復元には使わない。LocalId は NULL）。
    ' 記録する人（Participant）は、どれも**ノックした人**（管理人ではない。誰のノックかを追えるように）。
    '
    ' 【満員（40）と同じ歯止めを通す】（ChatHub.ShouldLogJoinReject。キーは種別ごとに別）
    ' 同じ人の同じ種別は、RejectLogCooldown（既定10分）に1回しか書かない。
    ' **だから行の数は「ノックの回数」ではない**（集計メールにもそう書いてある）。
    '
    ' 【部屋の過去ログには出さない】ChatEventText.Build が Nothing を返す。
    ' 「〇〇さんがノックしました」の行は、その場の配信（ChatHub.Knock.vb）でだけ出す
    ' （ノックはその場限りの頼みごとで、あとから入った人には要らない。記録も間引いているので、過去ログに出すと数が合わない）
    KnockRequested = 42    ' ノックした
    KnockApproved = 43     ' 管理人が許可した（席を取り置いた）
    KnockDenied = 44       ' 管理人が断った
    KnockExpired = 45      ' 管理人が時間内に決めなかった
    KnockSeatExpired = 46  ' 許可されたが、取り置きの時間内に入らなかった

    ' 入室拒否：その部屋で入室禁止になっている。
    ' 書くのは ChatHub.JoinRoom で断ったときだけ（ラウンジの入室ボタン・ノックで断ったときは書かない。
    ' 部屋の画面まで来て断られた＝最後の砦の記録）。満員（40）と同じ歯止めを通す（同じ相手は10分に1回）
    JoinRejectedBanned = 47

    ' ※48以降は、他の入室拒否の理由のために空けてある

    ' --- 50番台：サイトへの出入り（部屋の外の出来事） ---
    ' どれも部屋の中の出来事ではないので、RoomId は 0（SiteVisitLog.NoRoom）で書く。
    ' 部屋のログを読む4か所（ChatLogReader の3本・PrivateImage）は、どれも RoomId か種別で
    ' 絞っているので、0番の行が部屋に出ることは無い（ChatRooms の RoomId は1から振られる）。
    '
    ' 【1つの身元（ChatBrowserId）につき、1日1回まで】（SiteVisitLog の歯止め）
    ' 外から叩けば行を増やせるので、満員拒否（40）と同じ2段構えで止めてある。
    ' 覚えておくのはメモリなので、アプリが起き直すと同じ日にもう1回書くことがある。
    ' 数えるときは SenderBrowserId の DISTINCT で数えること
    '
    ' 【在室期間の復元には使わない】LocalId は常に NULL（部屋の番号が無い）
    Login = 50            ' ログイン（トップで名前を決めて、ラウンジに来た）
    LoungeLogout = 51     ' ログアウト（ラウンジのボタン。部屋に席があれば、その部屋に37も書く）
    LoungeVisit = 52      ' ラウンジに来た（部屋に入らずに帰った人も数えるため）

    ' --- 60番台：部屋の出来事 ---
    ' 入退室でも入室拒否でもない、部屋そのものに起きたこと。
    '
    ' 管理人が替わった（指名せずに退室したときの自動の引き継ぎ。指名もここへ入れる予定）。
    ' 記録する人は**新しい管理人**（その時点の名前を焼き込む）。部屋の過去ログに「管理人が〇〇さんに替わりました」と出る。
    ' LocalId は新しい管理人の部屋の番号（在室している人なので、番号がある）。
    ' 歯止めは通さない（外から叩いて増やせる記録ではない）
    OwnerChanged = 60

    ' 管理人が部屋の設定を変えた（ChatHub.RoomSettings.vb）。
    ' 部屋名・定員・ノック受付・パスワード・画像の可否の、どれか（いくつでも）。**1回の保存で1行**。
    ' 記録する人は**変えた管理人**（その時点の名前を焼き込む。荒らしを追う材料）。LocalId は管理人の部屋の番号。
    ' **本文の列（MessageText）に、何をどう変えたか**を入れる（例：部屋名「雑談」・定員 3人・パスワードを外した）。
    ' パスワードそのものは入れない（「かけた／かけ直した／外した」だけ）。
    ' 部屋の過去ログにも「管理人が部屋の設定を変えました（…）」と出る（ChatLogReader が本文から組み立てる）。
    ' 歯止めは通さない（管理人しか書けず、保存の間隔は SiteSettings.RoomSettingsChangeInterval で止まる）
    RoomSettingsChanged = 61

    ' 管理人が、参加者一覧から相手を選んで管理人をゆずった（指名）。
    ' 【60（自動の引き継ぎ）と分けてある】「本人がゆずった」と「出ていったので替わった」は、荒らし対応の記録として別の話。
    ' 部屋の過去ログの文言は 60 と同じ「管理人が〇〇さんに替わりました」。記録する人は**新しい管理人**（60 と同じ）。
    ' 前の管理人は、その直前の 60・62（か、部屋を作った人）から分かる。歯止めは通さない（管理人しか書けない）
    OwnerHandedOver = 62

    ' 管理人が入室禁止を解除した。記録する人は**解除された人**（禁止にした時点の見た目。38 と対にするため）。
    ' **部屋の過去ログには出さない**（ChatEventText.Build が Nothing）。禁止は部屋の全員に出したが、解除は管理人の手直しで、
    ' 知らせると「間違えて禁止にした人」がかえって目立つ。記録だけ残す（荒らし対応の材料）
    RoomBanLifted = 63

    ' ※64以降は、部屋の出来事のために空けてある

End Enum


' チャットの発言ログ・入退室ログを、ChatMessagesテーブルへ書き込むためのクラス
'
' ログの書き込み処理を、意図的にこの1か所へ閉じ込めている。
' 将来「非同期にしたい」「まとめ書き（バッファ）にしたい」となった際に、
' 呼び出し側（ChatHub）を一切変更せず、このクラスの中身だけを差し替えられるようにするため
Public Class ChatLog

    ' ログを1行書き込む
    ' roomId       … どの部屋のログか
    ' messageType  … ログの種別（発言・入室・退室など）
    ' participant  … 記録する人の情報。ニックネームやアイコン、トリップは、
    '                 後から参照するのではなく、この時点の値をそのまま焼き込む
    ' messageText  … 発言の本文。入退室などのイベントでは指定しない
    ' recipientBrowserId … こっそりの発言（種別11）の宛先。それ以外では指定しない
    ' imageName    … 画像の発言なら、その画像の名前（"2026/09/10/(32桁).jpg"）。
    '                 文章の発言では指定しない
    '
    ' 【なぜ、宛先だけ文字列で受け取るのか】
    ' 送り主は participant からまるごと取れるが、宛先は「その人が誰か」だけが分かればよい。
    ' ニックネームやアイコンは焼き込まない（往復すれば、相手の名前は相手自身の行にある）。
    ' Participant を丸ごと受け取る形にすると、焼き込む値が増えたときに
    ' 「どちらの人の値なのか」が読みにくくなる
    Public Shared Sub Write(roomId As Integer,
                            messageType As ChatMessageType,
                            participant As Participant,
                            Optional messageText As String = Nothing,
                            Optional recipientBrowserId As String = Nothing,
                            Optional imageName As String = Nothing)

        Try

            ' 接続文字列の取得方法は、ChatRoom.aspx.vbなど他の箇所と揃えている
            Dim connStr As String = ConfigurationManager.ConnectionStrings("VbChatConnection").ConnectionString

            ' CreatedAtは、列のDEFAULT（SYSDATETIME()）に任せるため、INSERT文には含めない
            Dim sql As String =
                "INSERT INTO ChatMessages " &
                "(RoomId, MessageType, UserId, LocalId, SenderBrowserId, RecipientBrowserId, Nickname, IconType, IconColor, Trip, MessageText, ImageName, IpAddress, RemotePort) " &
                "VALUES " &
                "(@RoomId, @MessageType, @UserId, @LocalId, @SenderBrowserId, @RecipientBrowserId, @Nickname, @IconType, @IconColor, @Trip, @MessageText, @ImageName, @IpAddress, @RemotePort)"

            Using conn As New SqlConnection(connStr)
                Using cmd As New SqlCommand(sql, conn)

                    cmd.Parameters.AddWithValue("@RoomId", roomId)
                    cmd.Parameters.AddWithValue("@MessageType", CByte(messageType))

                    ' UserId（アカウントの番号）。**アカウントID を表示して参加している人の行にだけ入る**
                    ' （チャットとアカウントの紐づけ。それまでは全行 NULL だった）。
                    ' ログインしていても、名乗らずに入った人の行は NULL のまま＝結び付けない（名乗らずに入る自由）。
                    '
                    ' 【引数を増やさず、participant から取る】トリップと同じ理由（呼び出しごとの渡し忘れを作らない）。
                    ' participant.AccountId を入れているのは ChatHub.JoinRoom だけで、値は2つの札と DB から決めたもの
                    ' （Chat/ChatAccountLink.vb）。クライアントの言い値は入らない。
                    '
                    ' 【何に使うか】「自分が参加したチャット履歴」（/account/chatlogs）が、この列で自分の入室・退室の行を探す。
                    ' 外部キーは張っていない（ログの表に制約を付けない、という最初の決まりのまま）。
                    ' 退会したら、その人の行の UserId は NULL に戻す（AccountStore.DeleteAccountCompletely）
                    If participant.AccountId > 0 Then
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = participant.AccountId
                    Else
                        cmd.Parameters.AddWithValue("@UserId", DBNull.Value)
                    End If

                    ' LocalIdは1から発行されるため、0以下は「番号が無い」の意味になる。
                    ' 入室を断った記録（40番台）は、番号が発行される前の出来事のため、
                    ' 0のまま渡ってくる。そのままINSERTすると「LocalId = 0の人」という
                    ' 実在しない参加者がログに生まれるので、NULLにしておく
                    If participant.LocalId > 0 Then
                        cmd.Parameters.AddWithValue("@LocalId", participant.LocalId)
                    Else
                        cmd.Parameters.AddWithValue("@LocalId", DBNull.Value)
                    End If

                    ' 発言した人のBrowserId（DB側の列名は SenderBrowserId）。
                    ' Participant側ではStringだが、DB側はUNIQUEIDENTIFIER。
                    ' 変換できない値（Cookieが壊れている等）が来ても落ちないよう、TryParseで判定する
                    '
                    ' 【列名だけがSenderで、Participant側はBrowserIdのまま】
                    ' Participant.BrowserId は「その参加者のブラウザの印」であって、
                    ' 送り主・宛先という向きを持たない。向きが要るのはログの行のほうだけである
                    Dim browserGuid As Guid
                    If Guid.TryParse(participant.BrowserId, browserGuid) Then
                        cmd.Parameters.AddWithValue("@SenderBrowserId", browserGuid)
                    Else
                        cmd.Parameters.AddWithValue("@SenderBrowserId", DBNull.Value)
                    End If

                    ' 宛先（こっそりの発言のときだけ入る。それ以外は必ずNULL）
                    '
                    ' 【読めない値が来たら、NULLにする】
                    ' 送り主とまったく同じ用心である。ここで落とすと、
                    ' こっそりが1行も残らない（そして Try/Catch に飲まれて気づけない）
                    Dim recipientGuid As Guid
                    If Guid.TryParse(recipientBrowserId, recipientGuid) Then
                        cmd.Parameters.AddWithValue("@RecipientBrowserId", recipientGuid)
                    Else
                        cmd.Parameters.AddWithValue("@RecipientBrowserId", DBNull.Value)
                    End If

                    ' NicknameはNOT NULL列のため、値が無い場合は空文字に置き換える
                    cmd.Parameters.AddWithValue("@Nickname", If(participant.Nickname, ""))

                    cmd.Parameters.AddWithValue("@IconType", ToDbValue(participant.IconType))
                    cmd.Parameters.AddWithValue("@IconColor", ToDbValue(participant.IconColor))

                    ' トリップ（追加）。名前・アイコンと同じく、
                    ' この時点の値を焼き込む。全種別の行に入れる（入退室の行にも）。
                    '
                    ' 【引数を増やさず、participant から取る】
                    ' 名前・アイコン・色と同じ「その人の、その時点の見た目」なので、
                    ' 同じ入れ物（Participant）に乗せて運ぶ。引数にすると、
                    ' 入室・退室・拒否・発言の呼び出し6か所それぞれで渡し忘れを起こしうる
                    ' （退室の理由が1つ増えるたびに、呼び出しも増える）
                    cmd.Parameters.AddWithValue("@Trip", ToDbValue(participant.Trip))

                    cmd.Parameters.AddWithValue("@IpAddress", ToDbValue(participant.IpAddress))

                    ' 接続元のポート番号。IP と同じく participant から（入室のときに読んで取っておいた値）。
                    ' 取れなかった人は NULL。**この列が無い DB へ書くと INSERT ごと失敗する**ので、発行は必ず DB が先
                    ' （Database/CreateTable/ChatMessages.sql の RemotePort）
                    If participant.RemotePort.HasValue Then
                        cmd.Parameters.Add("@RemotePort", SqlDbType.Int).Value = participant.RemotePort.Value
                    Else
                        cmd.Parameters.Add("@RemotePort", SqlDbType.Int).Value = DBNull.Value
                    End If

                    ' 画像の発言なら、その画像のGUID（それ以外の行では必ずNULL）
                    '
                    ' 【本文にも同じ値が入っているのに、列にも持つ理由】
                    ' こっそりの画像を1枚開くたびに、その画像がどの発言のものかを引く。
                    ' 本文から引くと LIKE になって索引が使えず、ログが溜まるほど重くなる。
                    ' **本文は画面へ出すための形、列は引くための形**であり、役割が違う
                    ' （RecipientBrowserId を足したときと、同じ判断）。
                    '
                    ' 【部屋あての画像にも入れる】
                    ' 認可には使わないが、古いログを消すとき（14番）に
                    ' 「消すべきファイル」を引ける。索引はフィルター付きなので、
                    ' 文章だけの行はこの索引をいっさい触らない
                    Dim imageGuid As Guid

                    If SiteImage.TryGetGuid(imageName, imageGuid) Then
                        cmd.Parameters.AddWithValue("@ImageName", imageGuid)
                    Else
                        cmd.Parameters.AddWithValue("@ImageName", DBNull.Value)
                    End If

                    ' 本文は「人が打った文章」の種別のときだけ記録する。
                    ' 入退室などのイベント行はNULL（本文という概念そのものが無いため）
                    If HasMessageText(messageType) Then
                        cmd.Parameters.AddWithValue("@MessageText", ToMessageTextDbValue(messageText))
                    Else
                        cmd.Parameters.AddWithValue("@MessageText", DBNull.Value)
                    End If

                    conn.Open()
                    cmd.ExecuteNonQuery()

                End Using
            End Using

        Catch ex As Exception

            ' ログの書き込み失敗は、チャット本体の動作を妨げてはならないため、ここで握りつぶしている。
            ' （ログが1行残らないことより、チャットが動かなくなることのほうが深刻なため）
            '
            ' 【ただし、黙っては握りつぶさない（宿題10）】
            ' それまではここに何も無く、INSERT が失敗しても**発言が1行も残らないのに、誰も気づけなかった**
            ' （名前の上限を列幅より大きくした日、トリップの桁を列幅より長くした日、DB が落ちた日）。
            ' ErrorReport は「覚えて」と渡すだけなので、ここで待たされることは無い。
            ' DB が落ちて発言のたびにここへ来ても、同じエラーは1時間に1通にまとめられる
            ' （場所と種類が同じ＝このメソッドの SqlException。番号が違えば別物として届く）。
            '
            ' 【補足に、発言の本文を入れないこと】
            ' メールに載る。こっそり（種別11）の本文は、宛先の1人にしか見せてはいけない
            ErrorReport.Report(ex, "ChatLog.Write", "種別 " & CInt(messageType) & "／部屋 " & roomId)

        End Try

    End Sub


    ' その種別は、本文（MessageText）を持つか
    '
    ' 【白リストであることに意味がある】
    ' ここに無い種別は、本文を渡されてもNULLで書く。入退室のイベント行に
    ' 文章が紛れ込まないための守りであって、書き忘れの防止ではない。
    ' だから「Messageではないか」ではなく、持つものを並べる形にしてある
    '
    ' 【この関数が無かったせいで、実際に踏んだ】
    ' こっそり（種別11）を足したとき、条件が
    ' 「messageType = Message」のべた書きだったため、**本文がNULLで保存されていた**。
    ' しかも画面では気づけない——リアルタイムの配信はDBを通らないので、
    ' 送った瞬間は普通に表示される。**F5して過去ログを読んだときだけ、空になる。**
    ' 本文を持つ種別を足すときは、必ずここへ1行足すこと
    Private Shared Function HasMessageText(messageType As ChatMessageType) As Boolean

        ' 部屋の設定の変更（61）は、何を変えたかを本文の列に入れる（パスワードそのものは入れない）
        Return messageType = ChatMessageType.Message OrElse
               messageType = ChatMessageType.PrivateMessage OrElse
               messageType = ChatMessageType.RoomSettingsChanged

    End Function


    ' 空文字・Nothingを、DBのNULLに変換するための小さなヘルパー
    Private Shared Function ToDbValue(value As String) As Object

        If String.IsNullOrEmpty(value) Then
            Return DBNull.Value
        End If

        Return value

    End Function


    ' 発言本文を、DBへ入れられる形に整えるためのヘルパー
    ' MessageText列はNVARCHAR(1000)のため、超過分は切り詰める
    ' （切り詰めずにINSERTすると例外になり、そのログが丸ごと残らなくなってしまうため）
    Private Shared Function ToMessageTextDbValue(messageText As String) As Object

        If String.IsNullOrEmpty(messageText) Then
            Return DBNull.Value
        End If

        ' 【ここは、サロゲートペアの真ん中で切りうる。それでも直していない理由】
        '
        ' 絵文字はUTF-16では2つの単位（サロゲートペア）で1文字を表すため、
        ' 1000番目がその前半だと、そのまま切ると片割れだけが残る。
        ' ChatProfileValidator.SafeCutLength では、そこを手当てしてある。
        '
        ' こちらを直していないのは、**この行に到達しないため**である。
        ' SiteSettings が MaxMessageLength を上限900で丸めており、
        ' ChatHub.SendMessage はそれを超える発言を、DBへ書く手前で断っている。
        ' つまり、ここへ1000単位を超える本文が来ることは無い。
        '
        ' 【この丸めを外す・広げるときは、必ずここへ戻ること】
        ' 900の丸めが無くなった瞬間に、この行は「絵文字を割る行」になる。
        ' そのときは ChatProfileValidator.SafeCutLength と同じ手当てを入れること。
        ' 壊れた値が入っても例外は出ず、過去ログに「�」が出るだけなので、
        ' 気づけない壊れ方をする
        If messageText.Length > 1000 Then
            Return messageText.Substring(0, 1000)
        End If

        Return messageText

    End Function

End Class
