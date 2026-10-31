' チャットルームの中にいる、参加者1人分の情報を表すクラス
' ChatHub内のShared Dictionaryで、部屋ごとにこのクラスのリストを管理する
Public Class Participant

    ' 部屋の中で一意に振られる番号（入室した順の連番。退室しても番号は再利用しない）
    Public Property LocalId As Integer

    ' ニックネーム（Cookieに保存されている値を、入室時にクライアントから受け取ったもの）
    Public Property Nickname As String

    ' アイコン種類（Cookieに保存されている値を、入室時にクライアントから受け取ったもの）
    '
    ' 【、部屋に居ながら変えられる】（ChatHub.ChangeAppearance）。
    ' 名前は変えられない（名前は身元そのもの。変えたら別人になる）。アイコンと色は見た目だけ
    Public Property IconType As String

    ' アイコン色（Cookieに保存されている値を、入室時にクライアントから受け取ったもの）
    ' IconType と同じく、部屋に居ながら変えられる
    Public Property IconColor As String

    ' トリップ（16進10桁。合言葉を入れていない人・トリップ機能が止まっているときは Nothing）
    '
    ' 入室の時点で、サーバーが ChatTripKey Cookie から計算して入れる（TripCode を参照）。
    ' 名前・アイコンと同じく、ログの行へそのまま焼き込む。
    '
    ' 【判定に使わないこと】表示するだけの値である。
    ' 無視・強制退室・入室禁止などの判定には、この値を一切使わない
    Public Property Trip As String

    ' アカウントの番号とユーザー名（チャットとアカウントの紐づけ）
    '
    ' アカウントID を表示して入った人だけ、番号（1以上）とユーザー名が入る。そうでない人は 0 と Nothing。
    ' 入れるのは ChatHub.JoinRoom で、値は Chat/ChatAccountLink.vb が**2つの札（名前の札・ログインの札）と DB** から決めたもの。
    ' クライアントが送ってきた値は、1つも入らない。
    '
    '   AccountId   … ログの ChatMessages.UserId に入れる（ChatLog.Write）。
    '                 「自分が参加したチャット履歴」（/account/chatlogs）は、この列で自分の入室・退室の行を探す
    '   AccountName … 画面に「@ユーザー名」と出す（トリップと同じ場所）。押すと公開のプロフィールが開く
    '
    ' 【トリップとは、どちらか一方】AccountId が入っている人の Trip は、必ず Nothing にする（JoinRoom）。
    '
    ' 【この部屋に「新しい1人」として入ったときに決まり、F5・再接続・同じ部屋での置き換えでは変えない】
    ' 名前と同じ扱い（JoinRoom の (b) 引き継ぎは同じ Participant を使い回し、(a) 同じ部屋での置き換えは古いほうから移す）。
    ' 途中で変わると、入室の行には番号があるのに退室の行には無い、という記録ができ、
    ' 「部屋に居た間」を入室と退室の行から組み立てている記録の画面が、区間を閉じられなくなる。
    ' 退会したときだけ、居る間でも外す（ChatHub.DetachAccount）
    '
    ' 【判定に使わないこと】トリップと同じく、表示と記録のための値である。
    ' 無視・強制退室・入室禁止・管理人の判定は、今までどおり ChatBrowserId で決まる
    Public Property AccountId As Integer
    Public Property AccountName As String

    ' ブラウザ識別用ID（ChatBrowserId Cookieの値。同じブラウザからの多重入室を検知するために使う）
    Public Property BrowserId As String

    ' SignalRの接続ID（発言者の特定に使う）
    ' 接続が切れている間はNothingになる（その接続IDはもう使えないため）。
    ' 再接続してきたときは、BrowserIdで本人を特定して、新しい接続IDを入れ直す
    Public Property ConnectionId As String

    ' 今このユーザーとSignalRでつながっているかどうか
    ' Falseでも部屋からは退室させない（スマホの画面消灯などで一時的に切れているだけの
    ' 可能性があるため）。退室の判断は、LastActiveAtとLeavePendingAtで行う
    Public Property IsConnected As Boolean

    ' 「退室予告」の時刻。Nothingなら予告なし
    '
    ' 【なぜ、IsConnectedとは別に必要なのか】
    ' IsConnected = False には、意味の違う2種類がある。
    '   ・通信が切れただけ（スマホの画面消灯など）
    '       → 在室を維持し、最終発言から1時間の無言タイムアウトまで待つ
    '   ・ページを閉じた／F5した（退室ビーコンが届いた）
    '       → 短い猶予（ChatHub.LeaveGrace）で退室を確定させる
    ' この2つを区別するのが、このプロパティである。
    ' つまり「閉じた」と「切れた」の違いは、その場で削除するかどうかではなく、
    ' 待つ時間の長さで表している
    '
    ' 【なぜ、その場で退室させないのか】
    ' ビーコンが届いた時点では、それが「タブを閉じた」のか「F5した」のかを
    ' サーバー側から区別できない。その場で退室させると、F5のたびに入室(20)と
    ' 切断(31)のログが対で書かれ続ける（本番のログで、発言106件に対して
    ' イベント行が402件という状態が実際に起きていた）。
    ' 猶予のうちに同じブラウザが同じ部屋へ戻ってくれば、JoinRoomの(b)引き継ぎが
    ' 自然に働き、退室そのものが無かったことになる
    Public Property LeavePendingAt As Date?

    ' 最後に発言した時刻（入室時と、発言が受け付けられた時に更新する。
    ' 無言タイムアウトの判定に使う）
    '
    ' 【弾いた発言では更新しない】
    ' 連投で断られ続けている間も、無言タイムアウトの時計は進み続ける、という向きにしてある。
    ' 断った発言で更新してしまうと、送信を連打しているだけで在室し続けられることになる
    Public Property LastActiveAt As DateTime

    ' 最後に受け付けた発言の時刻（発言のレート制限・連続同一発言の判定に使う）
    '
    ' 【なぜ、LastActiveAtに相乗りさせないのか】
    ' LastActiveAtは入室した時点でも更新される。レート制限に流用すると、
    ' 入室した直後の数秒だけ発言できない、という副作用が出る。
    ' 「最後に部屋で動きがあった時刻」と「最後に発言した時刻」は、別の話である
    '
    ' まだ一度も発言していない間はNothing（＝1度目の発言は必ず通る）
    Public Property LastMessageAt As Date?

    ' 最後に見た目（アイコン・色）を変えた時刻（ChatHub.ChangeAppearance）
    '
    ' 【LastActiveAt・LastMessageAt に相乗りさせない】
    ' LastActiveAt は入室した時点でも進むので、流用すると「入った直後だけ色を変えられない」になる。
    ' LastMessageAt に乗せると、「色を変えた直後に発言できない」「発言した直後に色を変えられない」になる。
    ' 見た目の連打と発言の連打は別の話なので、別の時計で持つ。
    '
    ' 【無言タイムアウトの時計（LastActiveAt）も進めない】見た目を変えるのは発言ではない。
    ' 色を変え続けているだけで在室し続けられる、にしないため（弾いた発言で時計を進めないのと同じ向き）。
    '
    ' まだ一度も変えていない間は Nothing（＝1度目は必ず通る）。
    ' 受け付けたときだけ進める（断ったときに進めると、押し続けている人がいつまでも変えられない）
    Public Property LastAppearanceAt As Date?

    ' 最後に部屋の設定を変えた時刻を、項目ごとに（管理人だけ。ChatHub.ChangeRoomSettings）
    ' LastAppearanceAt と同じく別の時計（受け付けたときだけ進める。無言タイムアウトの時計も進めない）。
    ' キーは項目の名前（name・maxUsers・knock・password・image）。
    ' 【項目ごとにした】15-3a-2 は「保存」ボタンで5つまとめて変える形で、時計は1本（LastRoomSettingsAt）だった。
    ' その場で反映する形にすると、5つを続けて触るのは普通の操作になるので、止めるのは「同じ項目を続けて変える」ときだけにした。
    ' 【SyncRoot の中でしか触らないこと】
    Public ReadOnly RoomSettingChangedAt As New Dictionary(Of String, Date)(StringComparer.Ordinal)

    ' 宛先ごとの「直前に受け付けた発言」（本文と、その時刻）
    '
    ' キーは ChatHub.DestinationKey が作る。部屋あては空文字、
    ' こっそりは宛先のBrowserIdを決まった書式に直したもの。
    '
    ' 【なぜ、1つではなく宛先ごとに持つのか】
    ' 1つにすると、**Aさんに「こんにちは」と送ったあと、Bさんに同じ挨拶が送れない**。
    ' 平行して複数の人と話すのは、こっそりの使い方そのものである。
    ' 「連投の間隔」のほうは1本の時計（LastMessageAt）で見てよい——
    ' あちらは「打つ速さ」の話で、宛先が変わっても速さは変わらないため
    '
    ' 【時刻も宛先ごとに持つ】
    ' 判定は「同じ本文が、直近◯秒以内に来たか」である。時刻だけ共通にすると、
    ' 別の宛先へ送った発言が窓を延長して、**関係のない宛先で「直前と同じ内容です」**
    ' と断られる
    '
    ' 【断った発言では書き換えない】
    ' 比較の相手は、あくまで「最後にその宛先へ流れた発言」である。
    ' 断った発言で上書きすると、判定の基準が本人の連打に合わせて動いてしまう
    '
    ' 【溜まり続けない】部屋の在室者ぶんしか鍵が増えず、
    ' Participant ごと退室時に捨てられる。掃除の仕組みは要らない
    Public ReadOnly Property LastTextByDestination As New Dictionary(Of String, LastAcceptedText)

    ' この部屋に入った時刻（追加）
    '
    ' 【何に使うか】部屋の管理人が、指名せずに退室したときの引き継ぎ先
    ' （「いちばん古くから居る人」）を決めるのに使う。
    '
    ' 【LocalId で「いちばん古くから居る人」を決めてはいけない】
    ' LocalId は入室のたびに新しい番号が振られる欠番方式なので、
    ' **ずっと居た人がF5しただけで、新参あつかいになる**（番号が大きくなる）。
    ' オーナーを LocalId で持たないと決めたのと、まったく同じ理由である
    ' （「【罠】一時部屋のオーナーを、LocalIdで識別してはいけない」）。
    '
    ' 【F5・再接続では、書き換えないこと】
    ' JoinRoom の (b) 引き継ぎは、この Participant をそのまま再利用する。
    ' そこでこの値を今の時刻に更新すると、F5した人がいちばん新しい人になってしまう。
    ' （新しい Participant を作るときにだけ入れる、という形で守ってある）
    Public Property JoinedAt As DateTime

    ' 入室した順の番号（追加）
    '
    ' 【何に使うか】参加者一覧（部屋・ラウンジの顔）を、入室した順に並べる。
    ' 要望：人数パネルをその順番で並べる。F5 などで順番が変わってしまうのはよくない。
    '
    ' 【JoinedAt を画面へ渡さない理由】時刻の Ticks は JavaScript の数（2^53）に収まらない。
    ' サイト全体で1つずつ増える番号（ChatHub.NextJoinSeq）なら、並べる用途にはこれで足りる。
    '
    ' 【JoinedAt と同じく、F5・再接続・同じ部屋での置き換えでは変えない】
    ' (b) 引き継ぎは同じ Participant を使い回す。(a) 同じ部屋での置き換えは、新しい Participant に
    ' 古いほうの JoinedAt と JoinSeq をそのまま移す（ChatHub.JoinRoom）
    Public Property JoinSeq As Long

    ' 入室時に取得したIPアドレス（ログ記録用）
    ' 退室ログを書くRemoveParticipantAndNotifyはSharedメソッドで、
    ' そこからは接続情報にアクセスできないため、入室時に取得してここへ保持しておく
    Public Property IpAddress As String

    ' 入室時に取得した、接続元のポート番号（ログ記録用）
    ' IpAddress と同じ場所で読み、同じ扱いで持つ（取れなければ Nothing）。読み方は Core/ClientEndpoint.vb
    Public Property RemotePort As Integer?

End Class


' 「直前に受け付けた発言」1件分（本文と、その時刻）
' Participant.LastTextByDestination の値として使う
Public Class LastAcceptedText

    ' 正規化したあとの本文
    Public Property Text As String

    ' それを受け付けた時刻
    Public Property At As DateTime

End Class
