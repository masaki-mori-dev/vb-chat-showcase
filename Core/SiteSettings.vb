Imports System.Configuration
Imports System.Data.SqlClient
Imports System.Threading

' コードのあちこちに直接書かれていた設定値を、一か所へ集めるためのクラス
'
' 【真実はDB側にあり、メモリはただのキャッシュ】
' IconCatalogとまったく同じ考え方で作ってある。値はSiteSettingsテーブルが持ち、
' このクラスは読んだ結果を抱えているだけ。Reload()で捨てて、次に読まれたときに読み直す
'
' 【なぜ「Chat」ではなく「Site」なのか】
' このクラスが持つのは、チャットの値だけではない。
' SiteName・CopyrightNameのようにSite.Master（ルート）が使う値も、
' ニックネームの上限のようにDefault.aspx（ルート）が使う値も入る。
' とくにサイト名は変わりうる（今は「VBチャット（仮）」）。
' チャット専用の名前を付けてはいけないクラスである
' （初回はChatSettingsという名前で作り、改名した）
'
' 【なぜ、Core/ に置いているのか】
' Chat/はチャット機能のためのフォルダ（Feature Folders）なので、
' 機能をまたぐものをそこへ置くと、ルートのページがチャットのフォルダを参照する形になる。
' common.vbと同じ「サイト共通」の棚に置くのが素直。
' フォルダの並べ直しまでは、その棚がルート直下だった
' （「フォルダ構成（Feature Folders方式）」の章）
'
' 【なぜ、Application_Startで読まないのか（遅延読み込み）】
' 共用サーバーはアクセスが無いとアプリを寝かせ、次のアクセスで起こす。
' つまり起動のタイミングをこちらで選べない。Application_Startで読むと、
' たまたまそのときDBが詰まっていればアプリごと起動できない。
' 遅延なら、次に読まれたときにやり直せて、勝手に回復する
'
' 【DBが読めなくても、止まらない（フェイルオープン）】
' 読めなければ、下のBuildDefaults()が返す既定値で動き続ける。
' このクラスを入れると、トップページ（Default.aspx）が初めてDBを引くようになるため、
' ここで止まる作りにすると、DB障害がそのままサイト全体の停止になる。
' JoinRoomのTryGetRoomStateが「問い合わせの成否」と「行の有無」を分けているのと同じ向き
'
' 【DBの値と、コードの既定値は、二重管理ではない】
' DBは「運用者が変えた値」、コードの既定は「変えられなかったときの安全な値」で、役割が別。
' IconCatalogのFallbackIconTypeを設定に入れないと決めたのと同じ考え方
'
' 【罠】SyncLockの中から読まないこと
' このクラスは、キャッシュが空のときにDBへ問い合わせる。ChatHubのSyncLockを
' 握ったまま呼ぶと、DBが詰まっている間、全部屋のチャットが止まる。
' 設定はロックを取る前に、いったんローカル変数へ読んでおくこと
' （ChatHub.SendMessageが、正規化をロックの外で済ませているのと同じ形）
Public Class SiteSettings

    ' 接続文字列の名前（ChatLog.vbなど、他の箇所と揃えている）
    Private Const ConnectionName As String = "VbChatConnection"


    ' === 設定の名前 ===
    '
    ' DBのSettingKey列に入る文字列。打ち間違いが実行時まで分からないと困るため、
    ' 文字列を直接書かず、必ずこの定数を通す

    ' 発言（SendMessage）の検証
    Private Const KeyMaxMessageLength As String = "MaxMessageLength"
    Private Const KeyMessageInterval As String = "MessageIntervalSeconds"
    Private Const KeyDuplicateWindow As String = "DuplicateWindowSeconds"

    ' 在室・退室
    Private Const KeySilentTimeout As String = "SilentTimeoutSeconds"
    Private Const KeyLeaveGrace As String = "LeaveGraceSeconds"
    Private Const KeyTimeoutCheckInterval As String = "TimeoutCheckIntervalSeconds"

    ' 満員で断ったことの記録
    Private Const KeyRejectLogCooldown As String = "RejectLogCooldownSeconds"
    Private Const KeyRejectLogTrackingLimit As String = "RejectLogTrackingLimit"

    ' プロフィール・アイコン
    Private Const KeyNicknameMaxLength As String = "NicknameMaxLength"
    Private Const KeyDefaultIconType As String = "DefaultIconType"
    Private Const KeyDefaultIconColor As String = "DefaultIconColor"

    ' 過去ログの表示
    Private Const KeyDefaultMessageCount As String = "DefaultMessageCount"
    Private Const KeyLogFetchRatio As String = "LogFetchRatio"

    ' 画面（JavaScript）だけが使う値
    Private Const KeyCounterWarnPercent As String = "CounterWarnPercent"
    Private Const KeyReconnectDelays As String = "ReconnectDelaysSeconds"
    Private Const KeyKeepAliveInterval As String = "KeepAliveIntervalSeconds"
    Private Const KeyWarningDisplay As String = "WarningDisplaySeconds"
    Private Const KeyProfileCookieDays As String = "ProfileCookieDays"
    Private Const KeyNotificationSoundDefault As String = "NotificationSoundDefault"
    Private Const KeyNotificationSoundVolumeDefault As String = "NotificationSoundVolumeDefault"

    ' 画像アップロード（Eの回で追加）
    '
    ' 【3つとも「画面が使う値」だが、上限だけはサーバーも使う】
    ' ImageMaxBytes は、画面側では「大きすぎるファイルを、読み込む前に断る」ために、
    ' サーバー側では **最後の砦** として使う。
    ' 縮小はブラウザがやるが、**縮小は親切であって防御ではない**——
    ' アップロードの受け口は、開発者ツールから直接叩ける
    Private Const KeyImageMaxBytes As String = "ImageMaxBytes"

    ' ImageMaxBytes をどこまで上げてよいか（3MB）
    '
    ' 【ASP.NET の maxRequestLength（既定 4096KB）と噛み合っている】
    ' リクエスト全体がこれを超えると、ASP.NET が受け口のコードに届く前に断る。
    ' 利用者には「保存できませんでした」としか出ず、理由が分からない。
    ' multipart の境界や見出しのぶんも本文に乗るので、4MBちょうどでは足りない。
    ' 余裕を持って3MBで止めてある
    '
    ' 【10MB から直した】
    ' 初版は10MBまで上げられる形になっていた。既定の2MBでは起きないが、
    ' 運用者が4MB以上にした日に、エラーも出さずに壊れるところだった。
    ' **これを上げたいときは、Web.config の httpRuntime maxRequestLength も
    ' 一緒に上げること**（片方だけ上げても効かない）
    '
    ' 【Public にしてある】記事の画像の受け口（Articles/UploadImage.aspx）も、同じ天井で止める
    Public Const ImageMaxBytesCeiling As Integer = 3 * 1024 * 1024
    Private Const KeyImageMaxEdge As String = "ImageMaxEdge"
    Private Const KeyImageJpegQuality As String = "ImageJpegQualityPercent"


    ' ログの保存期間（古いログの自動削除＝14番の LogCleanup が使う。既定は90日）
    Private Const KeyLogRetention As String = "LogRetentionSeconds"

    ' サイトへの出入りの記録（種別50〜52）の、1日の上限（SiteVisitLog）
    Private Const KeyVisitLogDailyLimit As String = "VisitLogDailyLimit"

    ' 部屋（追加。利用者が作る一時部屋のための3つ）
    '
    ' 【定員の「選べる一覧」は、ここには入れない】
    ' あれは白リスト＝防御なので、コード側（ChatRoomRules.AllowedMaxUsers）が持つ。
    ' AllowedMessageCounts を SiteSettings に入れないと決めたのと、まったく同じ理由である
    ' （DBが読めないときも丸めが効かなければならない）。
    ' ここに置くのは「一覧のどれを最初に選んでおくか」だけ
    ' （DefaultMessageCount と AllowedMessageCounts の関係と、同じ形にしてある）
    Private Const KeyRoomDefaultMaxUsers As String = "RoomDefaultMaxUsers"
    Private Const KeyRoomEmptyClose As String = "RoomEmptyCloseSeconds"
    Private Const KeyRoomEmptyCloseStartup As String = "RoomEmptyCloseStartupSeconds"

    ' 部屋のパスワードの総当たりの歯止め（RoomPassword.vb）
    Private Const KeyRoomPasswordMaxFailures As String = "RoomPasswordMaxFailures"
    Private Const KeyRoomPasswordLockout As String = "RoomPasswordLockoutSeconds"

    ' パスワードの照合（と作成）を、サイト全体で1分に何回まで行うか（RoomPassword.vb）
    Private Const KeyRoomPasswordChecksPerMinute As String = "RoomPasswordChecksPerMinute"

    ' ノック（Chat/Hubs/ChatHub.Knock.vb）
    Private Const KeyRoomKnockDecision As String = "RoomKnockDecisionSeconds"
    Private Const KeyRoomKnockSeatHold As String = "RoomKnockSeatHoldSeconds"
    Private Const KeyRoomKnockExtraSeats As String = "RoomKnockExtraSeats"
    Private Const KeyRoomKnockInterval As String = "RoomKnockIntervalSeconds"

    ' 部屋に居ながら、自分のアイコンと色を変える間隔（ChatHub.ChangeAppearance）
    Private Const KeyAppearanceChangeInterval As String = "AppearanceChangeIntervalSeconds"

    ' 管理人が部屋の設定を続けて変えられるまでの間隔（ChatHub.ChangeRoomSettings）
    Private Const KeyRoomSettingsChangeInterval As String = "RoomSettingsChangeIntervalSeconds"

    ' アカウントのログイン（Accounts/AccountSession.vb・AccountPassword.vb）
    '
    ' 【パスワードの長さ（12文字以上・128文字以内）は、ここに入れない】
    ' あれは設定ではなく防御なので、コードの定数（AccountPassword.MinLength／MaxLength）が持つ。
    ' DB に置くと管理画面から弱められる＝「設定にすると『壊せる箱』ができる」そのものである
    Private Const KeyAccountLoginCookieDays As String = "AccountLoginCookieDays"
    Private Const KeyAccountLoginMaxFailures As String = "AccountLoginMaxFailures"
    Private Const KeyAccountLoginLockout As String = "AccountLoginLockoutSeconds"

    ' 問い合わせページ
    '
    ' 【人には見えない入力欄（ロボット避け）と、3秒の連打止めと、文字数の上限は、ここに入れない】
    ' あれは設定ではなく防御なので、コードの定数（Contacts/ContactRules.vb）が持つ。
    ' DB に置くと、管理画面から弱められることになる（AccountPassword.MinLength と同じ判断）。
    ' ここに置くのは「1時間に何件まで受け付けるか」と「いつまで残すか」の、運用で変える2つだけ
    Private Const KeyContactMaxPerHour As String = "ContactMaxPerHour"
    Private Const KeyContactRetention As String = "ContactRetentionSeconds"
    Private Const KeyContactDuplicateWindow As String = "ContactDuplicateWindowSeconds"

    ' 右の枠の一覧に出す数（基本ルール T）
    ' 時間とともに増えていく一覧は、出す数を決めて「すべて見る」へつなぐ。出す数は管理画面（Accounts/SiteDisplay.aspx）で変える。
    ' 【控え】どの一覧もメモリに控えてあるので、変えたら Accounts/SiteDisplay.aspx.vb が CacheReset.ResetAll を呼ぶ
    Private Const KeySidebarNoticeCount As String = "SidebarNoticeCount"
    ' （SidebarHistoryCount＝右の枠の更新履歴の数は、更新履歴ごとやめた。DB の行は残してあるが、読まない）
    Private Const KeySidebarNewMemberCount As String = "SidebarNewMemberCount"
    Private Const KeyBlogRecentCount As String = "BlogRecentCount"

    ' 出す数の範囲（管理画面の欄の範囲にも使う）
    Public Const ListCountMin As Integer = 1
    Public Const ListCountMax As Integer = 20

    ' サイト情報
    Private Const KeySiteName As String = "SiteName"
    Private Const KeyCopyrightName As String = "CopyrightName"

    ' メール（追加。エラー報告メール）
    '
    ' 【SMTP の設定（ホスト・パスワードなど）は、ここに入れない】
    ' 秘密なので分類B（トリップのソルトと同じ）。mail.config（本番にしか無いファイル）が持つ。
    ' ここに置くのは「送るかどうか」「どこへ送るか」「どれくらい絞るか」の、運用で変える値だけ
    Private Const KeyMailEnabled As String = "MailEnabled"
    Private Const KeyMailTo As String = "MailTo"
    Private Const KeyErrorMailQuiet As String = "ErrorMailQuietSeconds"
    Private Const KeyErrorMailMaxPerHour As String = "ErrorMailMaxPerHour"

    ' 1日1回の集計メールを送るか（DailyReport）
    Private Const KeyDailyReportMailEnabled As String = "DailyReportMailEnabled"

    ' 届け先の既定（サイトの公式アドレス。動かすときは管理画面の設定 MailTo で自分のアドレスにする）
    Private Const DefaultMailTo As String = "info@example.com"


    ' === 通知音の大きさ（5段階）の、白リスト ===
    '
    ' 【これは設定の「名前」ではなく、設定に入れてよい「値」の一覧である】
    ' AllowedMessageCountsと同じ考え方で、白リスト＝防御なのでコード側に持つ。
    ' DBへ出すと、管理画面から「特大」のような知らない値を足せることになり、
    ' それがそのまま画面へ渡っていく。
    '
    ' 倍率そのもの（0.25 / 0.5 / 1.0 / 1.4 / 2.0）は画面側（Soundsの中）が持つ。
    ' サーバーが決めるのは「どれを初期選択にするか」だけである
    '
    ' 【倍率ではなく、段階の名前を持たせている理由】
    ' 倍率をCookieや設定に持たせると、あとで倍率を変えた日に、
    ' 既にその段階を選んでいる人だけが古い倍率のまま取り残される。
    ' 名前で持てば、倍率を変えた瞬間に全員へ効く
    '
    ' 【重要】3段階から5段階へ増やしたとき、名前を全部付け替えた。
    ' 前は low(0.5) / normal(1.0) / high(2.0) だった。
    '
    ' **同じ名前のまま中身が変わる状態を、1つも作らないため**である。
    ' たとえば新しい5段階を lowest/low/normal/high/highest と名付けると、
    ' high の倍率が 2.0 から 1.4 へ黙って変わる。
    ' 既に high を選んでいた人は、設定を触っていないのに音が小さくなり、
    ' しかもエラーも出ないので理由が分からない。
    '
    ' 付け替えたことで、古い low / high は白リストに無い値になり、
    ' NormalizeVolumeLevel が既定（normal）へ落とす。
    ' **「読めない値は既定へ」という既にある1つの規則が、そのまま移行の処理になる**
    ' （古い名前を新しい名前へ読み替える対応表は作らない。
    '   作ると、いつ消してよいか誰にも分からないものが1つ増える）。
    '
    ' normal だけは同じ名前を残してある。倍率が 1.0 のままで、意味が変わらないため
    Private Const VolumeVeryQuiet As String = "very-quiet"
    Private Const VolumeQuiet As String = "quiet"
    Private Const VolumeNormal As String = "normal"
    Private Const VolumeLoud As String = "loud"
    Private Const VolumeVeryLoud As String = "very-loud"

    ' 白リストの本体（小さいほうから順に並べてある）。
    ' 段階を増やすときは、上の定数とこの並びの2か所を直す
    Private Shared ReadOnly VolumeLevels As String() = {
        VolumeVeryQuiet, VolumeQuiet, VolumeNormal, VolumeLoud, VolumeVeryLoud
    }


    ' === キャッシュ ===
    '
    ' 【なぜ、値をひとつのオブジェクト（Snapshot）にまとめているのか】
    ' 設定を「値ごとの変数」で持つと、Reload()のときに全部をNothingへ戻す必要があり、
    ' 値を1つ足すたびにReload()へ1行足す作業が発生する（そして、いつか忘れる）。
    ' まとめて1つの参照にしておけば、Reload()は「その参照をNothingにする」だけで済み、
    ' 値を何個足しても手を入れなくてよい。
    ' 将来ここへClientSettingsJson（JavaScriptへ渡すJSON）を足すときも同じで、
    ' JSONは値と一緒に作られ、値と一緒に捨てられる
    '
    ' 【スレッドの安全について】
    ' Snapshotは中身を書き換えないオブジェクトとして作る（下のクラス定義を参照）。
    ' 完全に組み立ててから、この変数へ1回の代入で公開する形にしてあるため、
    ' 鍵の外から読んだ他のスレッドは「古い値」か「完成した新しい値」のどちらかしか見ない。
    ' 作りかけの状態を見ることはない
    Private Shared Cached As Snapshot = Nothing

    ' キャッシュを触るときの排他制御用
    Private Shared ReadOnly CacheLock As New Object()

    ' DBが読めなかったあと、次に読み直しを試みるまでの間隔
    '
    ' 【なぜ、失敗もキャッシュするのか】
    ' 失敗をキャッシュしないと、DBが落ちている間、発言のたびに問い合わせに行っては
    ' タイムアウトを待つことになる（＝落ちているDBを叩き続ける）。
    '
    ' 【なぜ、失敗を永久にキャッシュしないのか】
    ' 一度の瞬間的な詰まりで既定値に落ちたまま戻らなくなり、しかもエラーが出ないため、
    ' 誰も気づけない。「設定を変えたのに反映されない」という形でだけ表に出る。
    ' そこで、失敗したときだけ短い間隔で読み直す（エラー通知メールで採る予定の
    ' サーキットブレーカーと、考え方は同じ）
    Private Shared ReadOnly RetryAfterFailure As TimeSpan = TimeSpan.FromMinutes(1)


    ' === 公開する設定値 ===
    '
    ' 外に出すのはPropertyだけにしてある。DBを読む処理をPrivateに閉じ込めておけば、
    ' 呼び出し側は「キャッシュされているのか」「DBが読めなかったのか」を
    ' 一切気にせず、ただの値として使える

    ' 発言1件あたりの文字数の上限
    '
    ' 【上限が900なのは、DBの列幅が理由】
    ' ChatMessages.MessageTextはNVARCHAR(1000)で、ChatLog.vbが1000文字で切り詰める。
    ' ここを1000より大きくすると、利用者は送信できたのに、DBへ書く手前で黙って
    ' 切られる（エラーは出ない）。900で頭打ちにして、1000へ届かないようにしている
    '
    ' 【数え方をUTF-16の単位で揃えている】
    ' JavaScriptの .length、VBの String.Length、SQL ServerのNVARCHARは、
    ' いずれもUTF-16の単位で数える。絵文字1つが2文字と数えられるが、
    ' 書記素（見た目の1文字）単位にすると3か所すべてで数え方を合わせる必要が生じ、
    ' 「画面の表示と、サーバーの判定が食い違う」という罠を自分で作ることになる
    Public Shared ReadOnly Property MaxMessageLength As Integer
        Get
            Return GetSnapshot().MaxMessageLength
        End Get
    End Property


    ' 発言と発言の間に、最低限あけてもらう時間（レート制限）
    Public Shared ReadOnly Property MessageInterval As TimeSpan
        Get
            Return GetSnapshot().MessageInterval
        End Get
    End Property


    ' 直前と同じ内容の発言を断る、時間の幅
    ' （この時間を過ぎれば、同じ内容でも改めて発言できる。
    '   「はい」「うん」のような短い相づちが、永久に打てなくなっては困るため）
    '
    ' 【罠】MessageIntervalより短くすると、この判定は一度も働かなくなる
    ' ChatHub.SendMessageは、先に連投（MessageInterval）を弾いてから、
    ' ElseIfで「直前と同じ内容か」を見る。つまりここへ来る時点で、
    ' 前の発言からMessageInterval以上あいていることが確定している。
    ' この幅がそれ以下だと、条件を満たす瞬間が存在しない
    ' （エラーも出ず、ただ「同じ発言の連投が通るようになる」）。
    ' 下限をMessageIntervalの既定値（3秒）に合わせてあるのはそのため
    Public Shared ReadOnly Property DuplicateWindow As TimeSpan
        Get
            Return GetSnapshot().DuplicateWindow
        End Get
    End Property


    ' 無言タイムアウト（最後の発言からこの時間が過ぎたら退室させる）
    '
    ' 【罠】1分より短くすると、退室理由の文言が壊れる
    ' ChatHub.GetSilentTimeoutTextは、1時間未満のとき
    ' CInt(TotalMinutes) & "分" という形で文言を組み立てる。
    ' 60秒未満だとここが0になり、「0分発言がなかったため、退室しました」になる。
    ' 下限を60秒にしてあるのはそのため
    Public Shared ReadOnly Property SilentTimeout As TimeSpan
        Get
            Return GetSnapshot().SilentTimeout
        End Get
    End Property


    ' 退室予告（ページを閉じた・F5した）から、退室を確定させるまでの猶予
    '
    ' 【罠】TimeoutCheckIntervalより短くしても意味がない
    ' 猶予切れの判定は、この見回りのタイミングでしか行われない。
    ' 猶予1秒・見回り2秒にすると、実際の猶予は2秒になる（1秒にはならない）。
    ' 下限をTimeoutCheckIntervalの既定値（2秒）の倍にしてある
    Public Shared ReadOnly Property LeaveGrace As TimeSpan
        Get
            Return GetSnapshot().LeaveGrace
        End Get
    End Property


    ' 退室させるべき参加者がいないか、見回る間隔（Global.asaxのタイマー）
    '
    ' 【罠】LeaveGraceより長くすると、猶予が効かなくなる
    ' 上のLeaveGraceのコメントと、対の関係にある。
    ' 上限をLeaveGraceの既定値（5秒）以下に抑えてあるのはそのため
    '
    ' 走査するのはメモリ上の参加者リストだけで、DBへの書き込みは実際に退室が
    ' 発生したときにしか起きないため、短くしても負荷にはならない
    Public Shared ReadOnly Property TimeoutCheckInterval As TimeSpan
        Get
            Return GetSnapshot().TimeoutCheckInterval
        End Get
    End Property


    ' 満員で断ったことを、同じ相手（部屋＋BrowserId）について記録する間隔
    Public Shared ReadOnly Property RejectLogCooldown As TimeSpan
        Get
            Return GetSnapshot().RejectLogCooldown
        End Get
    End Property


    ' 「記録済みの相手」を覚えておく表の、件数の上限
    '
    ' この表が埋まっている間は、新しい相手の拒否も記録しない
    ' （＝「既にたくさん断っている」状態では、記録自体を止める）
    Public Shared ReadOnly Property RejectLogTrackingLimit As Integer
        Get
            Return GetSnapshot().RejectLogTrackingLimit
        End Get
    End Property


    ' ニックネームの文字数の上限
    '
    ' 【罠】50より大きくすると、ログが黙って書けなくなる
    ' ChatMessages.NicknameはNVARCHAR(50)で、ChatLog.vbは切り詰めをしていない。
    ' 51文字以上のニックネームでログを書こうとするとSQL Serverが例外を投げ、
    ' それはChatLog.Writeのtry/catchに握りつぶされる。
    ' つまり画面上は何ごとも無く進み、**その人のログだけが1行も残らない**。
    ' 上限を50にしてあるのはそのため
    Public Shared ReadOnly Property NicknameMaxLength As Integer
        Get
            Return GetSnapshot().NicknameMaxLength
        End Get
    End Property


    ' 既定のアイコン種別（不正なアイコン種別が送られてきた時に落とす先）
    '
    ' Content/icon-sprite.svg にある名前（idの "icon-" を除いた部分）を書く。
    ' ここに無い名前だった場合は、IconCatalogが保険としてスプライトの先頭へ落とす
    Public Shared ReadOnly Property DefaultIconType As String
        Get
            Return GetSnapshot().DefaultIconType
        End Get
    End Property


    ' 既定のアイコン色（アイコンの初期色）
    '
    ' 【設定の名前を、ThemeColorから変えた】
    ' 旧appSettingsでは「ThemeColor」という名前だったが、用途は「アイコンの初期色」で
    ' あって、画面全体の配色（Content/theme.css）とは別物である。
    ' 名前が実態と食い違っていたため、移行にあわせてDefaultIconColorへ改めた
    '
    ' なお、値そのものはtheme.cssの--color-accentと同じにしておくこと
    ' （意味が違うので変数は分けるが、見た目が揃っていないと不自然になる）
    Public Shared ReadOnly Property DefaultIconColor As String
        Get
            Return GetSnapshot().DefaultIconColor
        End Get
    End Property


    ' 過去ログの表示行数の、既定値（Cookie ChatLogCount が無いときに使う）
    '
    ' 【罠】ChatLogReader.AllowedMessageCountsに無い値にしてはいけない
    ' 選択肢の一覧（白リスト）はコード側が持っている。そこに無い値をここに入れると、
    ' セレクトボックスのどの選択肢とも一致せず、先頭が選ばれているように見える。
    ' 一覧をDBへ出さなかった理由は、ChatLogReader.AllowedMessageCountsのコメントを参照
    Public Shared ReadOnly Property DefaultMessageCount As Integer
        Get
            Return GetSnapshot().DefaultMessageCount
        End Get
    End Property


    ' 過去ログをDBから読むとき、表示したい行数の何倍を取ってくるか
    '
    ' 【罠】1未満にすると、指定した行数より少ない行しか出なくなる
    ' 画面に出ない行（種別33とその直後の入室）を落としたあとでN行に切り詰めるため、
    ' 取る量が表示したい量を下回ると、そもそも足りない
    Public Shared ReadOnly Property LogFetchRatio As Integer
        Get
            Return GetSnapshot().LogFetchRatio
        End Get
    End Property


    ' ログの保存期間（これを過ぎたログと画像を、1日1回の仕事 LogCleanup が消す。14番）
    '
    ' 既定は 7776000秒＝90日（約3か月）。86400〜315360000秒（1日〜10年）。
    '
    ' 【既定を1年から3か月にした】
    ' Cookie ユーザーのサイトで、3か月前の過去ログを読み返しに来る人はいない。
    ' 将来アカウント機能で「自分の過去ログを見る」を作るなら、そのとき見直す。
    '
    ' 【既定を変えても、既にある DB の行は変わらない】
    ' Database/InsertData/SiteSettings.sql は、既にある行の SettingValue を上書きしない（運用中の値を守るため）。
    ' 既にある DB を3か月にするには、SettingValue を手で UPDATE すること。ここの既定は「DB が読めないとき」の落とし先
    '
    ' 【1日にしても、昨日の分は消えない】消すのは「今から保存期間を引いた日の0時より前」の行。
    ' 集計メール（DailyReport）は、同じ見回りで古いログの削除より先に走る（DailyJobs.Jobs の並び）
    Public Shared ReadOnly Property LogRetention As TimeSpan
        Get
            Return GetSnapshot().LogRetention
        End Get
    End Property


    ' 新しい部屋を作るときに、最初に選んでおく定員
    '
    ' 既定は10人。2〜20人。
    '
    ' 【選べる一覧そのものは、コード側が持つ】（ChatRoomRules.AllowedMaxUsers）
    ' ここの値が一覧に無ければ、ChatRoomRules.NormalizeMaxUsers が一覧の既定へ落とす。
    ' DefaultMessageCount（既定の表示行数）と AllowedMessageCounts の関係と、まったく同じ形である
    Public Shared ReadOnly Property RoomDefaultMaxUsers As Integer
        Get
            Return GetSnapshot().RoomDefaultMaxUsers
        End Get
    End Property


    ' 部屋が空になってから、閉じるまでの猶予
    '
    ' 既定は0秒（＝誰も居なくなったら、次の見回りで閉じる。要望：すぐに消える）。
    ' 0〜86400秒（0秒〜1日）。
    '
    ' 【下の RoomEmptyCloseAfterStartup と、必ず分けておくこと】
    ' 1つの値にまとめると、「空になったら速攻」を0にした瞬間に、
    ' **アプリが再起動して、まだ繋ぎ直していないだけの部屋（人は居る）まで消える**。
    ' 在室者の一覧はサーバーのメモリにしか無いので、アプリが再起動すると全員分が飛ぶ
    ' （部屋の行はDBにあるので残る）。繋がっていた人のブラウザは自動で繋ぎ直しに来るが、
    ' その十数秒〜数十秒は「誰も居ない部屋」に見える
    Public Shared ReadOnly Property RoomEmptyClose As TimeSpan
        Get
            Return GetSnapshot().RoomEmptyClose
        End Get
    End Property


    ' アプリが起き直してから、空の部屋を閉じ始めるまでの猶予
    '
    ' 既定は180秒（3分）。0〜3600秒（0秒〜1時間）。
    '
    ' 【2つのことに効く】（どちらも「まだ誰も入っていない部屋」の猶予である）
    '   ・アプリが再起動した直後 … 在室者の一覧が飛んでいるので、繋ぎ直しを待つ
    '   ・部屋が作られた直後     … 作った本人が部屋の画面を開いて JoinRoom を呼ぶまでの数秒を待つ。
    '     **上の猶予（既定0秒）をここに使うと、作った瞬間に部屋が消える**
    '
    ' 【0にしてよい】そのときは「アプリが再起動したら、一時部屋は全部消える」形になる。
    ' そういう運用も選べるようにしてあるので、**自動再接続の待ち時間（最大30秒）より長く、
    ' のような2段目の丸めは入れていない**（入れると、上の形が選べなくなる）
    Public Shared ReadOnly Property RoomEmptyCloseAfterStartup As TimeSpan
        Get
            Return GetSnapshot().RoomEmptyCloseAfterStartup
        End Get
    End Property


    ' 部屋のパスワードを、続けて何回まで間違えてよいか
    '
    ' 既定は5回。1〜100回。この回数に届いたら、下の RoomPasswordLockout のあいだ受け付けない
    ' （同じ人＝同じ部屋で、同じブラウザか同じIPアドレス。数え方は RoomPassword.vb）。
    '
    ' 【2段目の丸めは無い】下の秒数との間に、壊れる組み合わせが無いため
    ' （どちらをどう変えても「N回で、M秒待つ」になるだけ。0秒・0回は1段目で塞いである）
    Public Shared ReadOnly Property RoomPasswordMaxFailures As Integer
        Get
            Return GetSnapshot().RoomPasswordMaxFailures
        End Get
    End Property


    ' パスワードを間違えすぎたときに、受け付けない秒数
    '
    ' 既定は60秒。1〜3600秒（1秒〜1時間）。
    ' 【0にさせない】0だと歯止めそのものが無くなり、総当たりが止まらない
    Public Shared ReadOnly Property RoomPasswordLockout As TimeSpan
        Get
            Return GetSnapshot().RoomPasswordLockout
        End Get
    End Property


    ' パスワードの計算（照合・部屋を作るときのハッシュ）を、サイト全体で1分に何回まで行うか
    '
    ' 既定は60回。5〜6000回。
    '
    ' 【なぜ要るのか】PBKDF2 の計算は1回で約0.18秒、CPUを1本使う（RoomPassword.vb の Iterations）。
    ' 上の2つ（人ごとの回数）は、Cookie と IP の両方を作り直せる相手には効かない。
    ' そういう相手に叩き続けられると、**サイト全体が重くなる**。
    ' そこで、人を問わずサイト全体で数え、多すぎる間は計算そのものをしない（「混み合っています」と断る）。
    ' 60回なら、最悪でも1分に約11秒ぶん（CPU1本の約18%）で頭打ちになる。
    '
    ' 【副作用】叩かれている間は、正しいパスワードを持っている人も入れない（最大1分）。
    ' サイト全体が重くなるより、パスワード付きの部屋にだけ少し待ってもらうほうを選んだ
    Public Shared ReadOnly Property RoomPasswordChecksPerMinute As Integer
        Get
            Return GetSnapshot().RoomPasswordChecksPerMinute
        End Get
    End Property


    ' ノックされた部屋の管理人が、入れるかどうかを決められる時間
    '
    ' 既定は60秒。10〜600秒。この時間を過ぎたら、そのノックは無かったことになる（ノックした人に「返事がありませんでした」）。
    ' 画面へは JSON では渡さない（ノックが届いたときに、サーバーが残りの秒数を一緒に送る）
    Public Shared ReadOnly Property RoomKnockDecision As TimeSpan
        Get
            Return GetSnapshot().RoomKnockDecision
        End Get
    End Property


    ' ノックを許可したあと、その人のために席を取り置く時間
    '
    ' 既定は30秒。10〜600秒。ノックした人が「入室する」を押すまでの時間。
    ' 押したときに1回だけ、部屋が開くまでの間（ChatHub の RoomPassLifetime＝60秒）ぶん延ばす
    ' （パスワード付きの部屋では、そのあと小窓で打つ時間が要るため。ChatHub.Knock.vb の ExtendKnockSeat）
    Public Shared ReadOnly Property RoomKnockSeatHold As TimeSpan
        Get
            Return GetSnapshot().RoomKnockSeatHold
        End Get
    End Property


    ' ノックで入れる「臨時の席」の上限（1部屋あたり）
    '
    ' 既定は5人。1〜20人。部屋の定員（ChatRooms.MaxUsers）は変えず、定員を超えて入れる人数をここで決める
    ' （表示は (21/20) の形になる）。
    '
    ' 【定員そのものを増やす形にしなかった理由】定員の「選べる一覧」（ChatRoomRules.AllowedMaxUsers）は
    ' 設定ではなく防御の白リストなので、21人のような一覧に無い定員は作れない
    Public Shared ReadOnly Property RoomKnockExtraSeats As Integer
        Get
            Return GetSnapshot().RoomKnockExtraSeats
        End Get
    End Property


    ' 同じ人が、続けてノックできるまでの間隔
    '
    ' 既定は3秒。1〜60秒。「同じ人」は、同じブラウザか同じIPアドレス（パスワードの歯止めと同じ数え方）。
    ' 発言の連投の間隔（MessageInterval）と同じ考え方で、別の値にしてある（発言と違ってノックは部屋の外から来るため）
    Public Shared ReadOnly Property RoomKnockInterval As TimeSpan
        Get
            Return GetSnapshot().RoomKnockInterval
        End Get
    End Property


    ' 部屋に居ながら、自分のアイコンと色を続けて変えられるまでの間隔
    '
    ' 既定は3秒。1〜60秒。変えるたびに部屋の全員とラウンジへ知らせが飛ぶので、その連打を止める。
    ' 【発言の連投（MessageInterval）とは別の時計】Participant.LastAppearanceAt で数える。
    ' LastActiveAt・LastMessageAt に相乗りさせると、「入った直後だけ変えられない」「色を変えた直後に発言できない」が起きる。
    ' 画面へは渡さない（断ったときに、サーバーが残りの秒数を一緒に返す）
    Public Shared ReadOnly Property AppearanceChangeInterval As TimeSpan
        Get
            Return GetSnapshot().AppearanceChangeInterval
        End Get
    End Property


    ' 管理人が、部屋の設定を続けて変えられるまでの間隔
    '
    ' 既定は3秒。1〜60秒。保存のたびに DB の書き込み・記録（61）・部屋とラウンジへの知らせが1つずつ起きるので、その連打を止める。
    ' 時計は Participant.RoomSettingChangedAt（別の時計。項目ごと＝同じ項目を続けて変えるときだけ止める）。画面へは渡さない（断ったときに残りの秒数を返す）
    Public Shared ReadOnly Property RoomSettingsChangeInterval As TimeSpan
        Get
            Return GetSnapshot().RoomSettingsChangeInterval
        End Get
    End Property


    ' アカウントのログインが切れるまでの長さ
    '
    ' 既定は7日。1〜90日。**使っている間に延びる形にはしない**（開発者と）。
    ' 7日で必ず入り直す。この長さは、Cookie の Expires と、札の中の期限の両方に同じ値が入る
    ' （Accounts/AccountAuth.vb）。画面へは渡さない。
    '
    ' 【名前・アイコンの Cookie（ProfileCookieDays＝既定365日）とは、わざと別にしてある】
    ' あちらは「同じ人だと分かるための目印」で、切れると過去ログの自分判定がずれる。
    ' こちらは「記事を書ける状態」なので、短いほうが安全である。
    ' **2段目の丸め（設定どうしの関係）は無い**——突き合わせる相手が無いため
    Public Shared ReadOnly Property AccountLoginCookieLifetime As TimeSpan
        Get
            Return TimeSpan.FromDays(GetSnapshot().AccountLoginCookieDays)
        End Get
    End Property


    ' ログインを、同じIPアドレスから続けて何回まで間違えてよいか
    '
    ' 既定は5回。1〜100回。この回数に届いたIPアドレスは、下の秒数のあいだ受け付けない。
    ' **アカウントの単位では、受け付けない状態にしない**（遅らせるだけ）——
    ' ロックにすると、管理人のユーザー名をわざと間違えるだけで本人を締め出せてしまう。
    ' 遅らせ方（1回につき0.4秒・上限2秒）はコードの定数（Accounts/AccountPassword.vb）。画面へは渡さない。
    ' **2段目の丸めは無い**（どう組み合わせても「N回で、M秒待つ」になるだけで、壊れる組み合わせが無い。
    ' 部屋のパスワードの2つと同じ）
    Public Shared ReadOnly Property AccountLoginMaxFailures As Integer
        Get
            Return GetSnapshot().AccountLoginMaxFailures
        End Get
    End Property


    ' ログインを間違えすぎたIPアドレスを、受け付けない秒数
    '
    ' 既定は60秒。1〜3600秒（1秒〜1時間）。0にはできない（総当たりの歯止めが無くなるため）。
    ' 待たせている間は、画面に残りの秒数が出る。画面へは渡さない（断ったときに文言と一緒に返す）
    Public Shared ReadOnly Property AccountLoginLockout As TimeSpan
        Get
            Return GetSnapshot().AccountLoginLockout
        End Get
    End Property


    ' サイト名（ページタイトル・ヘッダーに出る）
    '
    ' 今は「VBチャット（仮）」。変えるときは DB の値を変える。
    ' このクラスがSiteSettingsという名前である、いちばんの理由がこの値
    ' 同じIPアドレスから、1時間に何件まで問い合わせを受け付けるか（問い合わせページ）
    '
    ' 既定は5件。1〜100件。
    '
    ' 【1時間という長さは、設定にしていない】コード側（Contacts/ContactRequest.vb の CountWindow）が持つ。
    ' 「何件まで」と「どれくらいの間」の両方を設定にすると、噛み合わせ（2段目の丸め）が1つ増える。
    ' 運用で触りたくなるのは件数のほうなので、そちらだけを設定にしてある。
    '
    ' 【数はサーバーのメモリにだけある】DBの表を作っていないので、アプリを再起動すると0に戻る
    ' （AccountReset・AccountPassword の歯止めと同じ割り切り）。
    '
    ' 【0 にはできない】0 にすると誰も問い合わせを送れなくなる（＝連絡の口が閉じる）。
    ' 止めたいときは、問い合わせのページごと止めることになるが、その口は作っていない
    Public Shared ReadOnly Property ContactMaxPerHour As Integer
        Get
            Return GetSnapshot().ContactMaxPerHour
        End Get
    End Property


    ' 同じ内容の問い合わせを、もう一度受け付けない時間
    '
    ' 既定は600秒＝10分。0〜86400秒（0〜1日）。**0 にすると、この守りを切れる**。
    '
    ' 【何のためか】確認の画面まで「戻る」で戻って、もう一度「送信する」を押すと、
    ' 同じ内容がもう1件入っていた（本番の実機テストで開発者が踏んだ）。
    ' 送信のときに DB を1回引いて、**同じIPから同じ内容が、この時間の中にもう入っていないか**を見る。
    '
    ' 【なぜ「防御」ではなく設定なのか】
    ' これは荒らしへの守りではなく、**誤って二度押した人を助ける親切**である
    ' （荒らしは連打の歯止めと1時間の上限が止める）。だから運用で長くも短くもできてよいし、0 で切れてよい。
    ' 荒らしへの守り（連打の3秒・パスワードの長さなど）をコードの定数にしてあるのとは、立場が違う。
    '
    ' 【長すぎる値にしないこと】長くすると、「同じ文をもう一度送りたい」正当な人まで断ることになる。
    ' 上限の1日は、その歯止めである
    Public Shared ReadOnly Property ContactDuplicateWindow As TimeSpan
        Get
            Return GetSnapshot().ContactDuplicateWindow
        End Get
    End Property


    ' === 右の枠の一覧に出す数（既定はどれも5。1〜20） ===
    ' サイト全体とブログの右の枠の「管理人からのお知らせ」
    Public Shared ReadOnly Property SidebarNoticeCount As Integer
        Get
            Return GetSnapshot().SidebarNoticeCount
        End Get
    End Property

    ' サイト全体の右の枠の「新しいメンバー」
    Public Shared ReadOnly Property SidebarNewMemberCount As Integer
        Get
            Return GetSnapshot().SidebarNewMemberCount
        End Get
    End Property

    ' ブログの右の枠の「最新の記事」
    Public Shared ReadOnly Property BlogRecentCount As Integer
        Get
            Return GetSnapshot().BlogRecentCount
        End Get
    End Property

    ' 右の枠の一覧に出す数を、まとめて保存する（管理画面 Accounts/SiteDisplay.aspx から）
    '
    ' 【このサイトで初めての「設定を書く」処理】書くのは SettingValue の列だけ（ほかの列はスクリプトが持ち主。
    ' Database/InsertData/SiteSettings.sql の説明のとおり）。範囲の外の値は、ここでも端へ寄せる（画面の検査の2枚目）。
    ' 【行が無ければ書かない】行を入れる SQL（Database/InsertData/SiteSettings.sql）を
    ' 流す前だと、UPDATE が0行になる。そのときは False を返す（画面が理由を出す）。
    ' 【保存したあと】呼んだ側が CacheReset.ResetAll を呼ぶ（設定の控えと、一覧の控えの両方を捨てる）
    Public Shared Function SaveListCounts(notice As Integer, newMember As Integer, blogRecent As Integer) As Boolean

        Dim pairs As New Dictionary(Of String, Integer) From {
            {KeySidebarNoticeCount, notice},
            {KeySidebarNewMemberCount, newMember},
            {KeyBlogRecentCount, blogRecent}
        }

        Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString

        Using conn As New SqlConnection(connStr)
            conn.Open()
            Using tx As SqlTransaction = conn.BeginTransaction()
                For Each kv In pairs
                    Dim v As Integer = Math.Max(ListCountMin, Math.Min(ListCountMax, kv.Value))
                    Using cmd As New SqlCommand("UPDATE SiteSettings SET SettingValue = @Value WHERE SettingKey = @Key", conn, tx)
                        cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 50).Value = kv.Key
                        cmd.Parameters.Add("@Value", SqlDbType.NVarChar, 200).Value = v.ToString(Globalization.CultureInfo.InvariantCulture)
                        If cmd.ExecuteNonQuery() <> 1 Then
                            tx.Rollback()
                            Return False
                        End If
                    End Using
                Next
                tx.Commit()
            End Using
        End Using

        Return True

    End Function


    ' 問い合わせの保存期間（これを過ぎた ContactMessages の行を、LogCleanup が消す）
    '
    ' 既定は 31536000秒＝365日（1年）。86400〜315360000秒（1日〜10年）。
    '
    ' 【チャットのログ（既定90日）より長くしてある】
    ' 問い合わせは「対応の記録」として残す値打ちがある（同じ人が何度も同じ不具合を報告していないか、など）。
    ' それでも無期限にしないのは、**お名前・メールアドレス・IPアドレスという個人情報が入る表**だから。
    '
    ' 【消す仕事は、古いログの削除に相乗りしている】（Core/LogCleanup.vb）
    ' 1日1回の仕事を2つに分けると、「どの日付まで済んだか」の覚え書きが1つ増える。
    ' 数が少ない表（1日に数件）なので、同じ仕事の中で消しきれる
    Public Shared ReadOnly Property ContactRetention As TimeSpan
        Get
            Return GetSnapshot().ContactRetention
        End Get
    End Property


    Public Shared ReadOnly Property SiteName As String
        Get
            Return GetSnapshot().SiteName
        End Get
    End Property


    ' フッターの著作権表示に出す名前
    Public Shared ReadOnly Property CopyrightName As String
        Get
            Return GetSnapshot().CopyrightName
        End Get
    End Property


    ' 文字数カウンタを「残り少ない」表示に切り替える、上限からの割合（％）
    '
    ' 【なぜ、割合なのか】
    ' 「残り50文字」と数で持つと、上限を変えた日にここだけ古いまま残る。
    ' 割合なら上限が変わっても自動で付いてくる
    '
    ' 【なぜ、0.1ではなく10（％）なのか】
    ' 小数にすると、DBの文字列から戻すときに小数点の扱いが絡む
    ' （カルチャによって小数点が "." でなくなる環境がある）。
    ' 整数の％にしておけば、その心配が要らない。
    ' JavaScriptへ渡すときに0.1へ直す
    Public Shared ReadOnly Property CounterWarnPercent As Integer
        Get
            Return GetSnapshot().CounterWarnPercent
        End Get
    End Property


    ' 自動再接続の待ち時間（何度目で何秒待つか）
    '
    ' だんだん間隔を空けていく。サーバーが本当に落ちている場合に、
    ' 短い間隔で叩き続けないため。配列の長さが、そのまま試行回数になる
    Public Shared ReadOnly Property ReconnectDelays As Integer()
        Get
            Return GetSnapshot().ReconnectDelays
        End Get
    End Property


    ' キープアライブ（Sessionのタイムアウトを延ばすための、空の要求）の間隔
    '
    ' 【罠】Sessionのタイムアウト（既定20分）より長くしてはいけない
    ' 長くすると、キープアライブが届く前にSessionが切れ、F5でラウンジへ飛ばされる。
    ' 上限を900秒（15分）にして、20分に対して余裕を持たせてある。
    '
    ' 【この噛み合わせだけは、2段目の丸め（AtLeastLongerThan）で書けない】
    ' 相手のSessionのタイムアウトが設定ではなく、Web.configにすら書かれていない
    ' （ASP.NETの既定値をそのまま使っている）ため、突き合わせる相手が存在しない。
    ' 宿題でWeb.configへ20分を明示したら、そちらも設定にして
    ' 2段目の関係として書き直すこと
    Public Shared ReadOnly Property KeepAliveInterval As TimeSpan
        Get
            Return GetSnapshot().KeepAliveInterval
        End Get
    End Property


    ' 画面上部の警告メッセージ（showWarning）を出しておく時間
    '
    ' 【「最低これだけは出す」の意味】画面は、これと8秒の長いほうに、
    ' 文字数に応じた時間を足して出す（上限20秒。この値がそれより長ければ、この値）。
    ' 8秒はコード（site.js の warningDuration）が持つ。×で閉じられる。部屋のノックの小窓の知らせも同じ計算
    Public Shared ReadOnly Property WarningDisplay As TimeSpan
        Get
            Return GetSnapshot().WarningDisplay
        End Get
    End Property


    ' プロフィールのCookie（名前・アイコン・色・ブラウザ識別ID）の有効期間（日）
    '
    ' 【短くすると、利用者の「同じ人であること」が失われる】
    ' ChatBrowserIdは、同一ブラウザの2窓目の判定・過去ログの自分判定・
    ' （将来の）一時部屋のオーナー権に使われる。切れると別人として扱われる
    Public Shared ReadOnly Property ProfileCookieDays As Integer
        Get
            Return GetSnapshot().ProfileCookieDays
        End Get
    End Property


    ' 通知音を、はじめから鳴らすかどうか（利用者がまだ設定パネルで選んでいないときの値）
    '
    ' 【既定はオフ】
    ' ウェブサイトを開いた瞬間に音が鳴るのは、初見の人には驚きのほうが大きい。
    ' 設定パネルに行があるので、欲しい人は自分で見つけてオンにできる
    '
    ' 【この値が効くのは、Cookieが無いときだけ】
    ' 利用者が一度でも設定パネルで切り替えると、以後はCookie（ChatSoundEnabled）が優先される。
    ' つまりここを変えても、既に選んだことのある人の設定は変わらない
    Public Shared ReadOnly Property NotificationSoundDefault As Boolean
        Get
            Return GetSnapshot().NotificationSoundDefault
        End Get
    End Property


    ' 通知音の大きさの、既定（利用者がまだ設定パネルで選んでいないときの値）
    '
    ' "very-quiet" / "quiet" / "normal" / "loud" / "very-loud" のいずれか。既定は "normal"。
    ' 倍率（0.25 / 0.5 / 1.0 / 1.4 / 2.0）は画面側（Soundsの中）が持つ
    '
    ' 【端末によって、聞こえ方がまるで違う】
    ' 実測で、ChromeOS機はWindows機の約2倍でちょうど良かった。
    ' スピーカーの差なので、サイト側で1つの値に決められない。
    ' そのため「既定をどれにするか」だけを設定にして、選ぶのは利用者に任せている
    Public Shared ReadOnly Property NotificationSoundVolumeDefault As String
        Get
            Return GetSnapshot().NotificationSoundVolumeDefault
        End Get
    End Property


    ' 画像1枚の上限（バイト）
    '
    ' 【サーバー側が見る、ただ1つの画像の設定である】
    ' 残り2つ（ImageMaxEdge・ImageJpegQuality）は、縮小と変換のための値なので
    ' 画面側にしか渡らない。ここだけは、受け口（Core/SiteImage.vb）も見る
    Public Shared ReadOnly Property ImageMaxBytes As Integer
        Get
            Return GetSnapshot().ImageMaxBytes
        End Get
    End Property


    ' 縮めるときの長辺と、JPEG の画質（％）。A-2c
    '
    ' 【記事の画像の書く画面へ、data-* で渡すため】部屋の画面へは ClientSettingsJson で渡しているが、
    ' 記事の編集画面は vbChatSettings を持たない。**記事用の設定は増やさず、部屋の画像と同じ値を使う**
    Public Shared ReadOnly Property ImageMaxEdge As Integer
        Get
            Return GetSnapshot().ImageMaxEdge
        End Get
    End Property

    Public Shared ReadOnly Property ImageJpegQualityPercent As Integer
        Get
            Return GetSnapshot().ImageJpegQuality
        End Get
    End Property


    ' メールを送るかどうか（エラーの知らせも、将来の集計メールも、まとめて止まる）
    '
    ' 【既定はオン】DB が読めないとき（＝いちばん知らせが要るとき）に、黙らないため。
    ' 【何のための口か】何かがおかしくなったとき（メールが大量に届く、など）に、
    ' **発行せずに止められる口**。SSMS で 0 にして、アプリを再起動させれば止まる
    ' （管理画面ができるまでは、ほかの設定と同じく再起動が要る）。
    ' 止めている間のメールは、あとで送り直さない（SiteMail のコメント）
    Public Shared ReadOnly Property MailEnabled As Boolean
        Get
            Return GetSnapshot().MailEnabled
        End Get
    End Property


    ' メールの届け先（1つ）
    '
    ' メールアドレスとして読めない値なら、既定（info@example.com）へ落として知らせる
    Public Shared ReadOnly Property MailTo As String
        Get
            Return GetSnapshot().MailTo
        End Get
    End Property


    ' 同じエラー（場所と種類が同じもの）を、1通送ったあと送らない時間
    '
    ' 既定1時間。300〜86400秒（5分〜1日）。その間に起きた回数は数えておき、
    ' この時間が過ぎた時点で1回以上あれば、続報を1通送る（ErrorReport）
    '
    ' 【下限を5分にしている理由】短くすると、壊れ続けているエラーが5分おきに届き、
    ' 1時間あたりの上限（下）をすぐ使い切って、ほかのエラーが止められる
    Public Shared ReadOnly Property ErrorMailQuietPeriod As TimeSpan
        Get
            Return GetSnapshot().ErrorMailQuietPeriod
        End Get
    End Property


    ' エラーのメールを、全体で1時間に何通まで送るか
    '
    ' 既定10通。1〜100通。続報・まとめのメールも、この数に数える。
    '
    ' 【上限を100にしている理由】メールのホスティング先の送信の上限は
    ' 「15分ごとに100通程度（1時間400通の換算）」で、同じ契約のほかの送信とも分け合う。
    ' 100なら、どう設定しても上限の4分の1に収まる
    '
    ' 【2段目の丸め（設定どうしの関係）は無い】
    ' 上の ErrorMailQuietPeriod とは噛み合っていない。こちらの「1時間」は設定ではなく、
    ' 決まった60分（1時間あたりの数、という意味）なので、突き合わせる相手が無い
    Public Shared ReadOnly Property ErrorMailMaxPerHour As Integer
        Get
            Return GetSnapshot().ErrorMailMaxPerHour
        End Get
    End Property


    ' 1日1回の集計メール（ログイン・ラウンジ・入退室・発言の数）を送るか
    '
    ' 既定はオン（1）。**集計メールの設定は、この1つだけ**（種類ごとには分けない）。
    '
    ' 【オフにしている間も、「済んだ日付」は進める】（DailyReport・DailyJobs）
    ' 進めないと、オンに戻した日に、止めていた間の日の分がまとめて届く。
    ' 集計の SQL も走らせない（送らないのに数える理由が無い）。
    '
    ' 【MailEnabled（メール全体の口）とは別】MailEnabled が 0 なら、こちらが 1 でも送られない。
    ' そのときも済んだ日付は進む（止めている間のメールは、あとで送り直さない＝SiteMail と同じ考え方）
    Public Shared ReadOnly Property DailyReportMailEnabled As Boolean
        Get
            Return GetSnapshot().DailyReportMailEnabled
        End Get
    End Property


    ' サイトへの出入りの記録（ログイン・ラウンジでのログアウト・ラウンジに来た）を、
    ' 1日に何人（ブラウザの数）まで書くか。種類ごとに数える（SiteVisitLog）
    '
    ' 既定1000人。1〜10000人。
    '
    ' 【何のための上限か】満員拒否の RejectLogTrackingLimit と同じ、2段構えの2段目。
    ' 1段目の「1つの身元につき1日1回」は、ChatBrowserId を毎回変えて叩かれると効かない
    ' （Cookie の値は利用者が自由に決められる）。そこで「今日もう書いた身元」の表が
    ' この数に達したら、その日は新しい身元を書かない。**書かないだけで、ログインもラウンジも普通に使える**
    '
    ' 【上限を10000にしている理由】「今日もう書いた身元」はメモリに持つ（種類ごとに1つの表）。
    ' 1万人×3種類でも1MB前後に収まる。共用サーバーに1日1万人を超える身元が来るなら、
    ' そのときは覚え方ごと考え直す
    '
    ' 【上限に達したかは、集計のほうで分かる】その日の人数がこの数に並んでいたら、達している
    Public Shared ReadOnly Property VisitLogDailyLimit As Integer
        Get
            Return GetSnapshot().VisitLogDailyLimit
        End Get
    End Property


    ' 画面（JavaScript）へ渡す設定の、JSON
    '
    ' 【値と一緒に作り、値と一緒に捨てる】
    ' Snapshotのフィールドとして持っているため、リクエストのたびに組み立て直さない。
    ' Reload()で値と一緒に捨てられるので、「値だけ新しくなってJSONが古いまま」も起きない
    '
    ' 【渡す値は選別している。全部は出さない】
    ' 詳しくはBuildClientJsonのコメント
    Public Shared ReadOnly Property ClientSettingsJson As String
        Get
            Return GetSnapshot().ClientSettingsJson
        End Get
    End Property


    ' キャッシュを捨てる（次に読まれたときに、DBから読み直される）
    '
    ' 【必ず呼ぶこと】将来、管理画面から設定を保存する処理を書いたら、
    ' 保存の直後にこれを呼ぶ。呼び忘れると「変えたのに反映されない」となり、
    ' しかもエラーが出ないため原因が分かりにくい
    ' （IconCatalogはファイルの最終更新時刻で自動的に読み直せるが、
    '   DBにはそれに当たる仕掛けが無いため、こちらは明示的に呼ぶ必要がある）
    Public Shared Sub Reload()

        SyncLock CacheLock
            Cached = Nothing
        End SyncLock

    End Sub


    ' キャッシュを返す。まだ無ければ、DBから読んで作る
    '
    ' 【二重チェックにしている理由】
    ' 設定はあらゆる場面で読まれるため、毎回SyncLockを取ると、
    ' 実際には何もしない待ち合わせが積み重なる。
    ' 既に出来上がっている場合は、鍵を取らずにそのまま返す
    '
    ' 【最初の1回と、読み直しでは、鍵の取り方を変えている】
    ' 待ってよいのは「まだ何も持っていないとき」だけである。詳しくは下のコメント
    Private Shared Function GetSnapshot() As Snapshot

        ' 1度目のチェック（鍵の外）
        Dim snapshot As Snapshot = Cached

        If snapshot IsNot Nothing AndAlso Not snapshot.ShouldRetry() Then
            Return snapshot
        End If

        If snapshot Is Nothing Then

            ' === まだ一度も読めていない ===
            '
            ' 返せるものが何も無いため、ここは鍵が空くまで待つ。
            ' 待つのは、アプリが起きた直後の最初の1回だけである
            SyncLock CacheLock

                ' 2度目のチェック（鍵の中）
                ' 鍵を待っている間に、別のスレッドが作り終えている可能性がある
                If Cached IsNot Nothing Then
                    Return Cached
                End If

                Cached = Load()
                Return Cached

            End SyncLock

        End If

        ' === 読み直し（前回の読み込みに失敗しており、RetryAfterFailureを過ぎた） ===
        '
        ' 【罠】ここでSyncLockを使ってはいけない。
        ' 読み直しはDBへ問い合わせに行くが、DBが落ちていれば接続のタイムアウト
        ' （既定15秒）まで返ってこない。SyncLockにすると、その間に設定を読もうとした
        ' 他のリクエストが全部この鍵の前に並ぶ。
        ' つまりDB障害中、RetryAfterFailure（1分）ごとに15秒ほどサイト全体が止まり、
        ' 「トップページを落とさない」というフェイルオープンの狙いが打ち消される。
        '
        ' そこでTryEnterにして、鍵が取れなければ読み直しをあきらめ、
        ' 今持っている（＝既定値で組み立てた）Snapshotをそのまま返す。
        ' 読み直しを試みるのは常に1本だけで、他のリクエストは待たずに通り抜ける。
        ' あきらめた側も、次に呼ばれたときにまた試すため、回復は遅れない
        If Not Monitor.TryEnter(CacheLock) Then
            Return snapshot
        End If

        Try

            ' 2度目のチェック（鍵の中）
            If Cached IsNot Nothing AndAlso Not Cached.ShouldRetry() Then
                Return Cached
            End If

            Cached = Load()
            Return Cached

        Finally

            ' 【必ずFinallyで解放すること】
            ' TryEnterはSyncLockと違い、抜けるときの解放を自動ではやってくれない。
            ' Loadの中で予期しない例外が出た場合に、鍵が握られたままになると、
            ' 以降このクラスは二度と読み直せなくなる
            Monitor.Exit(CacheLock)

        End Try

    End Function


    ' DBから設定を読んで、Snapshotを組み立てる
    '
    ' 【必ずSyncLockの中から呼ぶこと】
    Private Shared Function Load() As Snapshot

        Dim values As Dictionary(Of String, String) = Nothing

        Try
            values = ReadFromDatabase()
        Catch ex As Exception

            ' 【ここで例外を投げないこと】
            ' 投げると、設定を読んだページがそのまま500になる。
            ' このクラスを使うページにはトップページも含まれるため、
            ' DBの一瞬の詰まりが、そのままサイト全体の停止になってしまう
            values = Nothing

            ' 【エラー報告メールへ繋いだ】
            ' それまでは Trace.WriteLine だけで、本番には出力先が無かった。
            ' フェイルオープンは「静かに既定値で動き続ける」ので、失敗しても誰も気づけず、
            ' 表に出るのは「設定を変えたのに反映されない」という形だけだった。
            '
            ' 【ここは鍵（CacheLock）の中である。それでも呼んでよい理由】
            ' ErrorReport.Report は「覚えて」と積むだけで、SiteSettings を読まない。
            ' 積んだものを数える裏の作業は SiteSettings を読むが、別のスレッドなので、
            ' この鍵が空くのを待つだけで済む（同じスレッドで読み直すと、無限の読み直しになる。
            ' ErrorReport.vb の先頭のコメント）。
            ' DB が落ちている間は、読み直し（RetryAfterFailure＝1分おき）のたびにここへ来るが、
            ' 同じエラーは1時間に1通にまとめられる
            ErrorReport.Report(ex, "SiteSettings.Load", "既定値で動作します")

        End Try

        If values Is Nothing Then

            ' 読めなかった。**空の表**を渡して組み立てる
            '
            ' 【なぜ、既定値だけを組み立てる別の関数を作らないのか】
            ' 空の表を渡せば、どの設定もGetInt側の「行が無い」経路に落ち、
            ' 既定値で埋まる。つまり「行が1つも無いDB」と同じ扱いになる。
            ' 既定値を書く場所がBuildSnapshotの1か所だけで済み、
            ' 設定を足すときに直す場所も1か所になる
            ' （最初は既定値専用の関数を別に持っていたが、値が15個に増えた時点で
            '   同じ数字を2か所に書く形になり、片方だけ直す事故が確実に起きるため改めた）
            '
            ' 「これは失敗の結果である」という印を付けて返す
            ' （印が付いていると、RetryAfterFailure後に読み直しが試みられる）
            Return BuildSnapshot(EmptyValues, isFailed:=True)

        End If

        ' 読めた。行が無い設定は、それぞれの既定値で埋まる
        Return BuildSnapshot(values, isFailed:=False)

    End Function


    ' DBが読めなかったときに渡す、空の対応表
    ' （毎回作らずに済むよう、1つだけ持っておく。中身は変えない）
    Private Shared ReadOnly EmptyValues As New Dictionary(Of String, String)(
        StringComparer.OrdinalIgnoreCase)


    ' 読み込んだ対応表から、Snapshotを組み立てる
    '
    ' === ここが、設定の一覧である ===
    '
    ' 設定を1つ足すときは、
    '   ①上のKeyXxx定数 ②上の公開プロパティ ③ここの1行 ④Snapshotのフィールド
    ' の4か所を足す。**既定値と丸めの範囲を書くのは、ここの1行だけ。**
    '
    ' 【丸めの範囲は、必ずコード側に持つこと】
    ' 丸めは「設定を変えても壊れないようにするための防御」であって、設定ではない。
    ' 範囲までDBに置くと、管理画面から範囲を広げられることになり、防御にならない。
    ' 個々の範囲の根拠は、上の公開プロパティのコメントに書いてある
    '
    ' 【重要】値ごとの下限・上限だけでは、防ぎきれない噛み合わせがある
    ' 「AがBより長いこと」のような**2つの設定の関係**は、片方だけを見る丸めでは表せない。
    ' そのため下でAdjustPairsを通し、両方を読んだあとで整えている。
    ' 実際、範囲を入れただけの状態で試したところ、
    ' 「退室猶予4秒・見回り5秒」という、猶予が効かない組み合わせが素通りした
    ' （実測して気づき、この仕組みを足した）
    Private Shared Function BuildSnapshot(values As Dictionary(Of String, String),
                                          isFailed As Boolean) As Snapshot

        Dim messageInterval As TimeSpan = GetSeconds(values, KeyMessageInterval,
                                                     fallback:=3, minValue:=0, maxValue:=60)
        Dim duplicateWindow As TimeSpan = GetSeconds(values, KeyDuplicateWindow,
                                                     fallback:=30, minValue:=3, maxValue:=3600)
        Dim leaveGrace As TimeSpan = GetSeconds(values, KeyLeaveGrace,
                                                fallback:=5, minValue:=4, maxValue:=60)
        Dim timeoutCheckInterval As TimeSpan = GetSeconds(values, KeyTimeoutCheckInterval,
                                                          fallback:=2, minValue:=1, maxValue:=5)

        ' 真偽値は 0 か 1 の整数として持ち、GetIntの丸め（0〜1）をそのまま門番にする。
        ' 2や-1を入れられても0か1に丸められ、「True」「はい」のように数値として
        ' 読めない値なら既定値（0＝鳴らさない）に落ちる。
        ' 専用のGetBoolを作らないのは、「はい」「on」「有効」…をどこまで認めるかという
        ' 判断を新しく増やすことになり、そのぶん取りこぼしが生まれるため
        Dim notificationSoundDefault As Boolean =
            GetInt(values, KeyNotificationSoundDefault,
                   fallback:=0, minValue:=0, maxValue:=1) <> 0

        ' 通知音の大きさは、白リストで丸める。
        ' ここを通したあとの値は、必ず上の3つのConstのどれかになっている
        ' （＝DBに書かれた文字列が、そのまま画面へ流れていく経路が存在しない）
        Dim notificationSoundVolumeDefault As String =
            NormalizeVolumeLevel(GetString(values, KeyNotificationSoundVolumeDefault,
                                           fallback:=VolumeNormal))

        ' メール。真偽値の持ち方は、上の通知音と同じ（0 か 1 の整数）。
        ' 既定は1＝送る（MailEnabled のコメント。DB が読めないときこそ知らせが要る）
        Dim mailEnabled As Boolean =
            GetInt(values, KeyMailEnabled,
                   fallback:=1, minValue:=0, maxValue:=1) <> 0

        Dim mailTo As String =
            NormalizeMailAddress(GetString(values, KeyMailTo, fallback:=DefaultMailTo))

        ' 集計メール。持ち方は上の MailEnabled と同じ（0 か 1 の整数。既定は1＝送る）
        Dim dailyReportMailEnabled As Boolean =
            GetInt(values, KeyDailyReportMailEnabled,
                   fallback:=1, minValue:=0, maxValue:=1) <> 0

        ' 2つの設定の関係を整える（上の【重要】を参照）
        duplicateWindow = AtLeastLongerThan(duplicateWindow, messageInterval,
                                            KeyDuplicateWindow, KeyMessageInterval)
        leaveGrace = AtLeastLongerThan(leaveGrace, timeoutCheckInterval,
                                       KeyLeaveGrace, KeyTimeoutCheckInterval)

        Return New Snapshot(
            maxMessageLength:=GetInt(values, KeyMaxMessageLength,
                                     fallback:=500, minValue:=1, maxValue:=900),
            counterWarnPercent:=GetInt(values, KeyCounterWarnPercent,
                                       fallback:=10, minValue:=1, maxValue:=50),
            reconnectDelays:=GetSecondsList(values, KeyReconnectDelays,
                                            fallback:="3,10,30",
                                            minValue:=1, maxValue:=300, maxCount:=10),
            keepAliveInterval:=GetSeconds(values, KeyKeepAliveInterval,
                                          fallback:=240, minValue:=30, maxValue:=900),
            warningDisplay:=GetSeconds(values, KeyWarningDisplay,
                                       fallback:=3, minValue:=1, maxValue:=30),
            profileCookieDays:=GetInt(values, KeyProfileCookieDays,
                                      fallback:=365, minValue:=1, maxValue:=3650),
            notificationSoundDefault:=notificationSoundDefault,
            notificationSoundVolumeDefault:=notificationSoundVolumeDefault,
            imageMaxBytes:=GetInt(values, KeyImageMaxBytes,
                                  fallback:=2097152, minValue:=65536, maxValue:=ImageMaxBytesCeiling),
            imageMaxEdge:=GetInt(values, KeyImageMaxEdge,
                                 fallback:=1280, minValue:=320, maxValue:=4096),
            imageJpegQuality:=GetInt(values, KeyImageJpegQuality,
                                     fallback:=85, minValue:=30, maxValue:=100),
            messageInterval:=messageInterval,
            duplicateWindow:=duplicateWindow,
            silentTimeout:=GetSeconds(values, KeySilentTimeout,
                                      fallback:=3600, minValue:=60, maxValue:=86400),
            leaveGrace:=leaveGrace,
            timeoutCheckInterval:=timeoutCheckInterval,
            rejectLogCooldown:=GetSeconds(values, KeyRejectLogCooldown,
                                          fallback:=600, minValue:=60, maxValue:=86400),
            rejectLogTrackingLimit:=GetInt(values, KeyRejectLogTrackingLimit,
                                           fallback:=500, minValue:=1, maxValue:=100000),
            nicknameMaxLength:=GetInt(values, KeyNicknameMaxLength,
                                      fallback:=12, minValue:=1, maxValue:=50),
            defaultIconType:=GetString(values, KeyDefaultIconType,
                                       fallback:="male"),
            defaultIconColor:=GetString(values, KeyDefaultIconColor,
                                        fallback:="#2563EB"),
            defaultMessageCount:=GetInt(values, KeyDefaultMessageCount,
                                        fallback:=50, minValue:=1, maxValue:=1000),
            logFetchRatio:=GetInt(values, KeyLogFetchRatio,
                                  fallback:=2, minValue:=1, maxValue:=10),
            logRetention:=GetSeconds(values, KeyLogRetention,
                                     fallback:=7776000, minValue:=86400, maxValue:=315360000),
            siteName:=GetString(values, KeySiteName,
                                fallback:="VBチャット(仮)"),
            copyrightName:=GetString(values, KeyCopyrightName,
                                     fallback:="example"),
            mailEnabled:=mailEnabled,
            mailTo:=mailTo,
            errorMailQuietPeriod:=GetSeconds(values, KeyErrorMailQuiet,
                                             fallback:=3600, minValue:=300, maxValue:=86400),
            errorMailMaxPerHour:=GetInt(values, KeyErrorMailMaxPerHour,
                                        fallback:=10, minValue:=1, maxValue:=100),
            visitLogDailyLimit:=GetInt(values, KeyVisitLogDailyLimit,
                                       fallback:=1000, minValue:=1, maxValue:=10000),
            dailyReportMailEnabled:=dailyReportMailEnabled,
            roomDefaultMaxUsers:=GetInt(values, KeyRoomDefaultMaxUsers,
                                        fallback:=10, minValue:=2, maxValue:=20),
            roomEmptyClose:=GetSeconds(values, KeyRoomEmptyClose,
                                       fallback:=0, minValue:=0, maxValue:=86400),
            roomEmptyCloseAfterStartup:=GetSeconds(values, KeyRoomEmptyCloseStartup,
                                                   fallback:=180, minValue:=0, maxValue:=3600),
            roomPasswordMaxFailures:=GetInt(values, KeyRoomPasswordMaxFailures,
                                            fallback:=5, minValue:=1, maxValue:=100),
            roomPasswordLockout:=GetSeconds(values, KeyRoomPasswordLockout,
                                            fallback:=60, minValue:=1, maxValue:=3600),
            roomPasswordChecksPerMinute:=GetInt(values, KeyRoomPasswordChecksPerMinute,
                                                fallback:=60, minValue:=5, maxValue:=6000),
            roomKnockDecision:=GetSeconds(values, KeyRoomKnockDecision,
                                          fallback:=60, minValue:=10, maxValue:=600),
            roomKnockSeatHold:=GetSeconds(values, KeyRoomKnockSeatHold,
                                          fallback:=30, minValue:=10, maxValue:=600),
            roomKnockExtraSeats:=GetInt(values, KeyRoomKnockExtraSeats,
                                        fallback:=5, minValue:=1, maxValue:=20),
            roomKnockInterval:=GetSeconds(values, KeyRoomKnockInterval,
                                          fallback:=3, minValue:=1, maxValue:=60),
            appearanceChangeInterval:=GetSeconds(values, KeyAppearanceChangeInterval,
                                                 fallback:=3, minValue:=1, maxValue:=60),
            roomSettingsChangeInterval:=GetSeconds(values, KeyRoomSettingsChangeInterval,
                                                   fallback:=3, minValue:=1, maxValue:=60),
            accountLoginCookieDays:=GetInt(values, KeyAccountLoginCookieDays,
                                           fallback:=7, minValue:=1, maxValue:=90),
            accountLoginMaxFailures:=GetInt(values, KeyAccountLoginMaxFailures,
                                            fallback:=5, minValue:=1, maxValue:=100),
            accountLoginLockout:=GetSeconds(values, KeyAccountLoginLockout,
                                            fallback:=60, minValue:=1, maxValue:=3600),
            contactMaxPerHour:=GetInt(values, KeyContactMaxPerHour,
                                      fallback:=5, minValue:=1, maxValue:=100),
            contactRetention:=GetSeconds(values, KeyContactRetention,
                                         fallback:=31536000, minValue:=86400, maxValue:=315360000),
            contactDuplicateWindow:=GetSeconds(values, KeyContactDuplicateWindow,
                                               fallback:=600, minValue:=0, maxValue:=86400),
            sidebarNoticeCount:=GetInt(values, KeySidebarNoticeCount, fallback:=5, minValue:=ListCountMin, maxValue:=ListCountMax),
            sidebarNewMemberCount:=GetInt(values, KeySidebarNewMemberCount, fallback:=5, minValue:=ListCountMin, maxValue:=ListCountMax),
            blogRecentCount:=GetInt(values, KeyBlogRecentCount, fallback:=5, minValue:=ListCountMin, maxValue:=ListCountMax),
            isFailed:=isFailed)

    End Function


    ' SiteSettingsテーブルを全部読んで、名前と値の対応表にする
    '
    ' 行数は数十行にしかならないため、必要な設定だけを1件ずつ引くのではなく、
    ' まとめて1回で取る（問い合わせの回数を、設定の数だけ増やさないため）
    Private Shared Function ReadFromDatabase() As Dictionary(Of String, String)

        Dim connStr As String = ConfigurationManager.ConnectionStrings(ConnectionName).ConnectionString

        ' 名前の大文字・小文字は区別しない。
        ' DB側（SQL Serverの既定の照合順序）が区別しないため、
        ' 読み込んだあとの引き当てだけが区別する、という食い違いを避ける
        Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Using conn As New SqlConnection(connStr)
            Using cmd As New SqlCommand("SELECT SettingKey, SettingValue FROM SiteSettings", conn)

                conn.Open()

                Using reader As SqlDataReader = cmd.ExecuteReader()
                    While reader.Read()

                        Dim key As String = reader.GetString(0)

                        ' SettingValueはNOT NULL列だが、読む側では念のため確かめる
                        ' （列の定義は、あとから変えられてしまうことがあるため）
                        If Not reader.IsDBNull(1) Then
                            values(key) = reader.GetString(1)
                        End If

                    End While
                End Using

            End Using
        End Using

        Return values

    End Function


    ' 設定を整数として取り出し、決められた範囲へ丸める
    '
    ' fallback  … 行が無い・数値として読めないときに使う値
    ' minValue  … これより小さい値は、この値まで引き上げる
    ' maxValue  … これより大きい値は、この値まで引き下げる
    '
    ' 【なぜ、範囲をコード側に持つのか】
    ' 丸めは「設定を変えても壊れないようにするための防御」であって、設定ではない。
    ' 範囲までDBに置くと、管理画面から範囲を広げられることになり、防御にならない。
    ' 範囲を呼び出し側の1行に書いているのは、既定値と範囲を並べて読めるようにするため
    Private Shared Function GetInt(values As Dictionary(Of String, String),
                                   key As String,
                                   fallback As Integer,
                                   minValue As Integer,
                                   maxValue As Integer) As Integer

        ' 既定値そのものが範囲から外れていたら、コードの書き間違いである。
        ' 気づかずに範囲外の値が使われることのないよう、ここでも丸めておく
        Dim safeFallback As Integer = Clamp(fallback, minValue, maxValue)

        Dim raw As String = Nothing

        If Not values.TryGetValue(key, raw) Then
            ' 行が無い。まだ移行していない設定は、この経路で既定値になる
            Return safeFallback
        End If

        Dim parsed As Integer

        If Not Integer.TryParse(raw, parsed) Then

            ' 数値として読めない値が入っていた（管理画面の作りが甘い、手で書き換えた、など）。
            ' 既定値で動き続けるが、黙っていると「変えたのに効かない」としか見えないので知らせる。
            ' 場所に設定の名前を入れておくと、別の設定の誤りは別のメールとして届く
            ErrorReport.ReportProblem("SiteSettings." & key,
                "SiteSettings の " & key & " が数値として読めません（" & raw & "）。既定値を使います。")

            Return safeFallback

        End If

        Return Clamp(parsed, minValue, maxValue)

    End Function


    ' 設定を「秒」として取り出し、TimeSpanに直す
    '
    ' 【なぜ、DBには秒で入れるのか】
    ' 移行前のコードはFromSeconds・FromMinutes・FromHoursが混ざっていた
    ' （3秒・30秒・5秒・10分・1時間）。DBの列を秒で統一しておけば、
    ' 管理画面に「単位」という余計な概念を持ち込まずに済む。
    ' 1時間なら3600、10分なら600と書く
    '
    ' 丸めはGetIntに任せる（範囲も秒で書く）
    Private Shared Function GetSeconds(values As Dictionary(Of String, String),
                                       key As String,
                                       fallback As Integer,
                                       minValue As Integer,
                                       maxValue As Integer) As TimeSpan

        Return TimeSpan.FromSeconds(GetInt(values, key, fallback, minValue, maxValue))

    End Function


    ' 設定を文字列として取り出す
    '
    ' 【空文字は、行が無いのと同じ扱いにする】
    ' 管理画面で誤って空にされたとき、サイト名やアイコン色が空になると
    ' 画面が壊れる（ページタイトルが消える、色指定が不正になる）。
    ' 空なら既定値へ落とすほうが安全である。
    ' 「空にしたい設定」は今のところ存在しない
    '
    ' 値そのものの妥当性（アイコン色が#＋16進6桁か、アイコン種別が実在するか）は、
    ' ここでは見ない。ChatProfileValidatorとIconCatalogが、
    ' 利用者から来た値とまったく同じ検査に通している
    Private Shared Function GetString(values As Dictionary(Of String, String),
                                      key As String,
                                      fallback As String) As String

        Dim raw As String = Nothing

        If Not values.TryGetValue(key, raw) Then
            Return fallback
        End If

        If raw Is Nothing OrElse raw.Trim().Length = 0 Then
            Return fallback
        End If

        Return raw.Trim()

    End Function


    ' 通知音の大きさを、決められた3つのどれかに丸める
    '
    ' 【これは値ではなく、白リスト＝防御である】
    ' 一覧をDBに出さないのは、AllowedMessageCountsを設定にしなかったのと同じ理由。
    ' 管理画面から知らない値を足せる形にすると、それがそのまま画面へ渡っていく。
    '
    ' 【この関数が、JSONへ文字列を出してよい根拠になっている】
    ' ここを通った値は必ずVolumeLevelsに並んでいるもののいずれか＝
    ' このファイルに書かれた文字列そのものである。DBの中身が出ていくことはないため、
    ' BuildClientJsonがエスケープなしで組み立ててよい
    '
    ' 大文字・小文字は区別しない（管理画面で "Normal" と書かれても通す）
    Private Shared Function NormalizeVolumeLevel(raw As String) As String

        If raw IsNot Nothing Then

            Dim trimmed As String = raw.Trim()

            ' 段階が増えても、ここへ足すのは1行だけで済むように、並びで回している
            ' （If文を段階の数だけ書き並べると、足し忘れが起きる）
            For Each level As String In VolumeLevels

                If String.Equals(trimmed, level, StringComparison.OrdinalIgnoreCase) Then
                    Return level
                End If

            Next

        End If

        ErrorReport.ReportProblem("SiteSettings." & KeyNotificationSoundVolumeDefault,
            "SiteSettings の " & KeyNotificationSoundVolumeDefault & " が " &
            String.Join(" / ", VolumeLevels) &
            " のいずれでもありません（" & If(raw, "(なし)") & "）。" & VolumeNormal & " を使います。")

        Return VolumeNormal

    End Function


    ' 届け先を、メールアドレスとして読める形にそろえる
    '
    ' 読めなければ既定（info@example.com）へ落として知らせる。
    ' 【落とす先を持っている理由】届け先が壊れていると、**エラーの知らせそのものが1通も届かない**。
    ' 壊れていることを知らせるメールも届かないので、ここだけは既定の宛先で送り続ける
    '
    ' 「名前 <アドレス>」の形で書かれていても、アドレスの部分だけを使う
    Private Shared Function NormalizeMailAddress(raw As String) As String

        Try
            Return New System.Net.Mail.MailAddress(raw).Address
        Catch ex As Exception

            ErrorReport.ReportProblem("SiteSettings." & KeyMailTo,
                "SiteSettings の " & KeyMailTo & " がメールアドレスとして読めません（" & If(raw, "(なし)") &
                "）。" & DefaultMailTo & " へ送ります。")

            Return DefaultMailTo

        End Try

    End Function


    ' 設定を「秒の並び」として取り出し、それぞれを丸める
    '
    ' 書き方は "3,10,30"（カンマ区切り）。自動再接続の待ち時間のように、
    ' 「1度目は3秒、2度目は10秒…」と段階が要るものに使う
    '
    ' minValue・maxValue … 1つ1つの値を丸める範囲
    ' maxCount           … 並びの長さの上限（＝試行回数の上限）
    '
    ' 読めない値が混ざっていたら、その1つだけを飛ばす。
    ' 結果が1つも残らなかった場合は、既定の並びへ落とす
    ' （空の並びを返すと、自動再接続が一度も行われなくなる）
    '
    ' 【AllowedMessageCountsを設定にしなかったのと、矛盾しない】
    ' あちらは白リスト＝防御なのでコード側に置く必要があった。
    ' こちらは「どれくらい待つか」という、ただの調整値である
    Private Shared Function GetSecondsList(values As Dictionary(Of String, String),
                                           key As String,
                                           fallback As String,
                                           minValue As Integer,
                                           maxValue As Integer,
                                           maxCount As Integer) As Integer()

        Dim raw As String = Nothing

        If Not values.TryGetValue(key, raw) OrElse raw Is Nothing OrElse raw.Trim().Length = 0 Then
            raw = fallback
        End If

        Dim result As New List(Of Integer)

        For Each part In raw.Split(","c)

            Dim parsed As Integer

            If Integer.TryParse(part.Trim(), parsed) Then
                result.Add(Clamp(parsed, minValue, maxValue))
            End If

            If result.Count >= maxCount Then
                Exit For
            End If

        Next

        If result.Count = 0 Then

            ErrorReport.ReportProblem("SiteSettings." & key,
                "SiteSettings の " & key & "（" & raw &
                "）から、数値をひとつも読み取れませんでした。既定の並び（" & fallback & "）を使います。")

            ' 既定の並びは、このメソッドの呼び出し側が必ず読める形で書いているため、
            ' もう一度通せば必ず1つ以上になる（無限には回らない）
            For Each part In fallback.Split(","c)
                Dim parsed As Integer
                If Integer.TryParse(part.Trim(), parsed) Then
                    result.Add(Clamp(parsed, minValue, maxValue))
                End If
            Next

        End If

        Return result.ToArray()

    End Function


    ' 「この設定は、あの設定より必ず長い」という関係を守らせる
    '
    ' value      … 長いほうであるべき設定
    ' other      … 短いほうであるべき設定
    ' 短いか同じだった場合は、otherの1秒後まで引き上げる
    '
    ' 【なぜ、同じ長さでも駄目なのか】
    ' どちらの噛み合わせも「otherを過ぎてから、valueまでの間」で判定が行われる。
    ' 同じ長さだと、その間が一瞬も存在しない（＝判定が一度も働かない）。
    '
    '   ・DuplicateWindow … SendMessageは、先にMessageIntervalで連投を弾いてから、
    '     ElseIfで「直前と同じ内容か」を見る。つまりここへ来る時点で
    '     MessageInterval以上あいていることが確定している。
    '     同じ長さだと、同一発言の判定が一度も働かない
    '   ・LeaveGrace … 猶予切れの判定は、見回りのタイミングでしか行われない。
    '     同じか短いと、設定した猶予より実際の猶予のほうが長くなる（設定が嘘になる）
    '
    ' 【なぜ、長いほうを引き上げるのか（短いほうを下げないのか）】
    ' 短いほうを下げると、利用者が明示的に設定した値を、
    ' 別の設定の都合で黙って弱めることになる（レート制限が緩む・見回りが粗くなる）。
    ' 引き上げるほうが、どちらの設定も弱めない
    Private Shared Function AtLeastLongerThan(value As TimeSpan,
                                              other As TimeSpan,
                                              valueKey As String,
                                              otherKey As String) As TimeSpan

        If value > other Then
            Return value
        End If

        Dim adjusted As TimeSpan = other + TimeSpan.FromSeconds(1)

        ErrorReport.ReportProblem("SiteSettings." & valueKey,
            "SiteSettings の " & valueKey & "（" & CInt(value.TotalSeconds) &
            "秒）が " & otherKey & "（" & CInt(other.TotalSeconds) &
            "秒）以下のため、" & CInt(adjusted.TotalSeconds) & "秒へ引き上げました。")

        Return adjusted

    End Function


    ' 画面（JavaScript）へ渡す設定の、JSONを組み立てる
    '
    ' === 【重要】ここに出す値は、選別すること。全部を出してはいけない ===
    '
    ' 出すのは「画面が実際に使う値」だけ。
    ' RejectLogTrackingLimit・RejectLogCooldown・SilentTimeout・LeaveGraceのような
    ' **サーバー内部の都合**は出さない。F12で内部の仕組みを読まれるだけ損である。
    ' （SiteSettingsテーブルのScope列がServerのものは、ここに出さない。
    '   ただし判断はこのコードが持つ。あの列を条件にDBから引く形にはしない。
    '   そうすると丸めも単位変換も型も、全部すり抜けてしまうため）
    '
    ' === 単位の変換は、ここでまとめて行う ===
    '
    ' DBは秒、JavaScriptはミリ秒。変換をJS側に散らすと、いつか片方を直し忘れる。
    ' 割合も、ここで％から小数へ直す
    '
    ' === 組み立て方 ===
    '
    ' 出す値は数値・数値の配列・真偽値・白リストを通した文字列だけなので、
    ' 文字列を組み立てて作っている。
    ' 真偽値は If(値, "true", "false") で、こちらが書いた2つの文字列のどちらかにしかならない。
    ' notificationSoundVolumeDefaultも、NormalizeVolumeLevelを通っているため、
    ' 必ずこのファイルに書かれたConstのどれかである
    ' （つまり、どちらもDBの中身がそのまま出ていく経路にはなっていない）。
    '
    ' 【罠】「文字列を1つ出したから、次も出してよい」ではない。
    ' 白リストを通していない文字列（サイト名など、運用者が自由に書ける値）を出すなら、
    ' そのときはエスケープが要る
    ' （</script> や " が値に入ると、その場でページが壊れる）。
    ' JavaScriptSerializerかJson.NETを使うこと。**自前でエスケープを書かないこと**
    '
    ' 【messageIntervalMs を出すようにした】
    ' それまで MessageInterval は「サーバー内部の都合」として出していなかった。
    ' スタンプの一覧は、送ったあとこの間だけボタンを休ませる（押しっぱなしの連打を、
    ' 断られる前に止めて、見た目でも分かるようにする。開発者の指定）。
    ' 秘密の値ではない——2回続けて送れば、断られる文言で誰でも分かる。
    ' あわせて SiteSettings テーブルの Scope を Server → Both に直してある
    Private Shared Function BuildClientJson(maxMessageLength As Integer,
                                            messageInterval As TimeSpan,
                                            nicknameMaxLength As Integer,
                                            counterWarnPercent As Integer,
                                            reconnectDelays As Integer(),
                                            keepAliveInterval As TimeSpan,
                                            warningDisplay As TimeSpan,
                                            profileCookieDays As Integer,
                                            notificationSoundDefault As Boolean,
                                            notificationSoundVolumeDefault As String,
                                            imageMaxBytes As Integer,
                                            imageMaxEdge As Integer,
                                            imageJpegQuality As Integer) As String

        ' 秒 → ミリ秒（自動再接続の待ち時間）
        Dim delays As String = String.Join(",",
            reconnectDelays.Select(Function(s) (s * 1000).ToString(InvariantCulture)))

        ' 数値は、必ずInvariantCultureで文字列にする。
        ' 共用サーバーのカルチャ次第で小数点が "," になると、JSONとして壊れるため
        ' （ChatLogReaderが日付を自前で連結しているのと、同じ用心）
        '
        ' 【画像の画質だけ、DBは「％の整数」で持ち、ここで 0〜1 の小数へ直す】
        ' counterWarnRatio と、まったく同じ扱いである。
        '
        ' 【罠】この初期化子の中に、コメントだけの行を置いてはいけない。
        ' VBは「From { … }」の途中の行継続を、コメント行で切ってしまう
        ' （実際にビルドが止まった）。説明はここへ書くこと
        Dim parts As New List(Of String) From {
            Quote("maxMessageLength") & ":" & maxMessageLength.ToString(InvariantCulture),
            Quote("messageIntervalMs") & ":" & CLng(messageInterval.TotalMilliseconds).ToString(InvariantCulture),
            Quote("nicknameMaxLength") & ":" & nicknameMaxLength.ToString(InvariantCulture),
            Quote("counterWarnRatio") & ":" & (counterWarnPercent / 100.0).ToString("0.####", InvariantCulture),
            Quote("reconnectDelaysMs") & ":[" & delays & "]",
            Quote("keepAliveIntervalMs") & ":" & CLng(keepAliveInterval.TotalMilliseconds).ToString(InvariantCulture),
            Quote("warningDisplayMs") & ":" & CLng(warningDisplay.TotalMilliseconds).ToString(InvariantCulture),
            Quote("cookieMaxAgeSeconds") & ":" & (CLng(profileCookieDays) * 86400L).ToString(InvariantCulture),
            Quote("notificationSoundDefault") & ":" & If(notificationSoundDefault, "true", "false"),
            Quote("notificationSoundVolumeDefault") & ":" & Quote(notificationSoundVolumeDefault),
            Quote("imageMaxBytes") & ":" & imageMaxBytes.ToString(InvariantCulture),
            Quote("imageMaxEdge") & ":" & imageMaxEdge.ToString(InvariantCulture),
            Quote("imageJpegQuality") & ":" & (imageJpegQuality / 100.0).ToString("0.##", InvariantCulture)
        }

        Return "{" & String.Join(",", parts) & "}"

    End Function


    ' JSONのキーを、ダブルクォートで囲む
    Private Shared Function Quote(name As String) As String
        Return """" & name & """"
    End Function


    ' 数値を文字列にするときの、カルチャ
    Private Shared ReadOnly InvariantCulture As Globalization.CultureInfo =
        Globalization.CultureInfo.InvariantCulture


    ' 値を、決められた範囲へ収める
    Private Shared Function Clamp(value As Integer, minValue As Integer, maxValue As Integer) As Integer

        If value < minValue Then
            Return minValue
        End If

        If value > maxValue Then
            Return maxValue
        End If

        Return value

    End Function


    ' 読み込んだ設定の、ひとかたまり
    '
    ' 【中身を書き換えないこと】
    ' フィールドはすべてReadOnlyで、コンストラクタでしか値が入らない。
    ' こうしておくと、組み立て終わってからCachedへ代入する形になるため、
    ' 鍵を取らずに読んでいる他のスレッドが、作りかけの状態を見ることがない。
    ' 設定を変えるときは、このオブジェクトを書き換えるのではなく、
    ' Reload()で捨てて作り直す
    '
    ' 【値を足すときは、ここへフィールドを1つ足す】
    ' 将来ClientSettingsJson（JavaScriptへ渡すJSON）を足す場合も、ここへ足す。
    ' そうすればJSONは値と一緒に作られ、Reload()で値と一緒に捨てられるため、
    ' 「値だけ新しくなって、JSONが古いまま」という食い違いが起きない
    Private NotInheritable Class Snapshot

        Public ReadOnly MaxMessageLength As Integer
        Public ReadOnly MessageInterval As TimeSpan
        Public ReadOnly DuplicateWindow As TimeSpan
        Public ReadOnly SilentTimeout As TimeSpan
        Public ReadOnly LeaveGrace As TimeSpan
        Public ReadOnly TimeoutCheckInterval As TimeSpan
        Public ReadOnly RejectLogCooldown As TimeSpan
        Public ReadOnly RejectLogTrackingLimit As Integer
        Public ReadOnly NicknameMaxLength As Integer
        Public ReadOnly DefaultIconType As String
        Public ReadOnly DefaultIconColor As String
        Public ReadOnly DefaultMessageCount As Integer
        Public ReadOnly LogFetchRatio As Integer
        Public ReadOnly LogRetention As TimeSpan
        Public ReadOnly SiteName As String
        Public ReadOnly CopyrightName As String
        Public ReadOnly CounterWarnPercent As Integer
        Public ReadOnly ReconnectDelays As Integer()
        Public ReadOnly KeepAliveInterval As TimeSpan
        Public ReadOnly WarningDisplay As TimeSpan
        Public ReadOnly ProfileCookieDays As Integer
        Public ReadOnly NotificationSoundDefault As Boolean
        Public ReadOnly NotificationSoundVolumeDefault As String
        Public ReadOnly ImageMaxBytes As Integer
        Public ReadOnly ImageMaxEdge As Integer
        Public ReadOnly ImageJpegQuality As Integer
        Public ReadOnly MailEnabled As Boolean
        Public ReadOnly MailTo As String
        Public ReadOnly ErrorMailQuietPeriod As TimeSpan
        Public ReadOnly ErrorMailMaxPerHour As Integer
        Public ReadOnly VisitLogDailyLimit As Integer
        Public ReadOnly DailyReportMailEnabled As Boolean
        Public ReadOnly RoomDefaultMaxUsers As Integer
        Public ReadOnly RoomEmptyClose As TimeSpan
        Public ReadOnly RoomEmptyCloseAfterStartup As TimeSpan
        Public ReadOnly RoomPasswordMaxFailures As Integer
        Public ReadOnly RoomPasswordLockout As TimeSpan
        Public ReadOnly RoomPasswordChecksPerMinute As Integer
        Public ReadOnly RoomKnockDecision As TimeSpan
        Public ReadOnly RoomKnockSeatHold As TimeSpan
        Public ReadOnly RoomKnockExtraSeats As Integer
        Public ReadOnly RoomKnockInterval As TimeSpan
        Public ReadOnly AppearanceChangeInterval As TimeSpan
        Public ReadOnly RoomSettingsChangeInterval As TimeSpan
        Public ReadOnly AccountLoginCookieDays As Integer
        Public ReadOnly AccountLoginMaxFailures As Integer
        Public ReadOnly AccountLoginLockout As TimeSpan
        Public ReadOnly ContactMaxPerHour As Integer
        Public ReadOnly ContactRetention As TimeSpan
        Public ReadOnly ContactDuplicateWindow As TimeSpan
        Public ReadOnly SidebarNoticeCount As Integer
        Public ReadOnly SidebarNewMemberCount As Integer
        Public ReadOnly BlogRecentCount As Integer

        ' 画面へ渡すJSON
        '
        ' 【値と一緒に、ここで1回だけ作る】
        ' リクエストのたびに組み立て直さない。選別も単位の変換もここで済む。
        ' Reload()でこのSnapshotごと捨てられるため、値とJSONがずれることがない
        Public ReadOnly ClientSettingsJson As String

        ' このSnapshotが「DBを読めなかった結果」であるかどうか
        Private ReadOnly IsFailed As Boolean

        ' 組み立てた時刻（読み直しを試みる判断に使う）
        Private ReadOnly CreatedAt As DateTime

        Public Sub New(maxMessageLength As Integer,
                       messageInterval As TimeSpan,
                       duplicateWindow As TimeSpan,
                       silentTimeout As TimeSpan,
                       leaveGrace As TimeSpan,
                       timeoutCheckInterval As TimeSpan,
                       rejectLogCooldown As TimeSpan,
                       rejectLogTrackingLimit As Integer,
                       nicknameMaxLength As Integer,
                       defaultIconType As String,
                       defaultIconColor As String,
                       defaultMessageCount As Integer,
                       logFetchRatio As Integer,
                       logRetention As TimeSpan,
                       siteName As String,
                       copyrightName As String,
                       counterWarnPercent As Integer,
                       reconnectDelays As Integer(),
                       keepAliveInterval As TimeSpan,
                       warningDisplay As TimeSpan,
                       profileCookieDays As Integer,
                       notificationSoundDefault As Boolean,
                       notificationSoundVolumeDefault As String,
                       imageMaxBytes As Integer,
                       imageMaxEdge As Integer,
                       imageJpegQuality As Integer,
                       mailEnabled As Boolean,
                       mailTo As String,
                       errorMailQuietPeriod As TimeSpan,
                       errorMailMaxPerHour As Integer,
                       visitLogDailyLimit As Integer,
                       dailyReportMailEnabled As Boolean,
                       roomDefaultMaxUsers As Integer,
                       roomEmptyClose As TimeSpan,
                       roomEmptyCloseAfterStartup As TimeSpan,
                       roomPasswordMaxFailures As Integer,
                       roomPasswordLockout As TimeSpan,
                       roomPasswordChecksPerMinute As Integer,
                       roomKnockDecision As TimeSpan,
                       roomKnockSeatHold As TimeSpan,
                       roomKnockExtraSeats As Integer,
                       roomKnockInterval As TimeSpan,
                       appearanceChangeInterval As TimeSpan,
                       roomSettingsChangeInterval As TimeSpan,
                       accountLoginCookieDays As Integer,
                       accountLoginMaxFailures As Integer,
                       accountLoginLockout As TimeSpan,
                       contactMaxPerHour As Integer,
                       contactRetention As TimeSpan,
                       contactDuplicateWindow As TimeSpan,
                       sidebarNoticeCount As Integer,
                       sidebarNewMemberCount As Integer,
                       blogRecentCount As Integer,
                       isFailed As Boolean)

            Me.MaxMessageLength = maxMessageLength
            Me.MessageInterval = messageInterval
            Me.DuplicateWindow = duplicateWindow
            Me.SilentTimeout = silentTimeout
            Me.LeaveGrace = leaveGrace
            Me.TimeoutCheckInterval = timeoutCheckInterval
            Me.RejectLogCooldown = rejectLogCooldown
            Me.RejectLogTrackingLimit = rejectLogTrackingLimit
            Me.NicknameMaxLength = nicknameMaxLength
            Me.DefaultIconType = defaultIconType
            Me.DefaultIconColor = defaultIconColor
            Me.DefaultMessageCount = defaultMessageCount
            Me.LogFetchRatio = logFetchRatio
            Me.LogRetention = logRetention
            Me.SiteName = siteName
            Me.CopyrightName = copyrightName
            Me.CounterWarnPercent = counterWarnPercent
            Me.ReconnectDelays = reconnectDelays
            Me.KeepAliveInterval = keepAliveInterval
            Me.WarningDisplay = warningDisplay
            Me.ProfileCookieDays = profileCookieDays
            Me.NotificationSoundDefault = notificationSoundDefault
            Me.NotificationSoundVolumeDefault = notificationSoundVolumeDefault
            Me.ImageMaxBytes = imageMaxBytes
            Me.ImageMaxEdge = imageMaxEdge
            Me.ImageJpegQuality = imageJpegQuality

            ' メールの4つは、画面へは渡さない（サーバー内部の都合。JSONには入れない）
            Me.MailEnabled = mailEnabled
            Me.MailTo = mailTo
            Me.ErrorMailQuietPeriod = errorMailQuietPeriod
            Me.ErrorMailMaxPerHour = errorMailMaxPerHour

            ' サーバーの中だけで使う（画面へは渡さない）
            Me.VisitLogDailyLimit = visitLogDailyLimit
            Me.DailyReportMailEnabled = dailyReportMailEnabled

            ' 部屋。3つとも画面へは渡さない
            ' （定員の選択肢はサーバーがHTMLを組み立てて出し、猶予はサーバーの中だけで使う）
            Me.RoomDefaultMaxUsers = roomDefaultMaxUsers
            Me.RoomEmptyClose = roomEmptyClose
            Me.RoomEmptyCloseAfterStartup = roomEmptyCloseAfterStartup

            ' 部屋のパスワードの歯止め。2つとも画面へは渡さない
            ' （待つ秒数は、断ったときにサーバーが文言と一緒に返す）
            Me.RoomPasswordMaxFailures = roomPasswordMaxFailures
            Me.RoomPasswordLockout = roomPasswordLockout

            ' パスワードの計算のサイト全体の上限と、ノック。どれも画面へは渡さない
            ' （ノックの秒数は、ノックが届いたとき・許可されたときに、サーバーが残りの秒数を一緒に送る）
            Me.RoomPasswordChecksPerMinute = roomPasswordChecksPerMinute
            Me.RoomKnockDecision = roomKnockDecision
            Me.RoomKnockSeatHold = roomKnockSeatHold
            Me.RoomKnockExtraSeats = roomKnockExtraSeats
            Me.RoomKnockInterval = roomKnockInterval

            ' 見た目を変える間隔。画面へは渡さない（断ったときに残りの秒数を返す）
            Me.AppearanceChangeInterval = appearanceChangeInterval

            ' 部屋の設定を変える間隔。画面へは渡さない
            Me.RoomSettingsChangeInterval = roomSettingsChangeInterval

            ' アカウントのログイン。3つとも画面へは渡さない
            ' （待たせる秒数は、断ったときにサーバーが文言と一緒に返す）
            Me.AccountLoginCookieDays = accountLoginCookieDays
            Me.AccountLoginMaxFailures = accountLoginMaxFailures
            Me.AccountLoginLockout = accountLoginLockout

            ' 問い合わせ。2つとも画面へは渡さない
            ' （断ったときの理由と残りの秒数は、サーバーが文言で返す。保存期間は裏の仕事だけが使う）
            Me.ContactMaxPerHour = contactMaxPerHour
            Me.ContactRetention = contactRetention
            Me.ContactDuplicateWindow = contactDuplicateWindow

            ' 右の枠の一覧に出す数。画面へは渡さない（サーバーが一覧を組み立てる）
            Me.SidebarNoticeCount = sidebarNoticeCount
            Me.SidebarNewMemberCount = sidebarNewMemberCount
            Me.BlogRecentCount = blogRecentCount

            ' JSONは、すべての値が入ったあとに1回だけ作る
            Me.ClientSettingsJson = BuildClientJson(
                maxMessageLength, messageInterval, nicknameMaxLength, counterWarnPercent,
                reconnectDelays, keepAliveInterval, warningDisplay, profileCookieDays,
                notificationSoundDefault, notificationSoundVolumeDefault,
                imageMaxBytes, imageMaxEdge, imageJpegQuality)

            Me.IsFailed = isFailed
            Me.CreatedAt = DateTime.Now

        End Sub

        ' DBから読み直すべきかどうか
        '
        ' 読めているSnapshotは、Reload()が呼ばれるまでそのまま使い続ける。
        ' 読めなかったSnapshotだけ、しばらく経ったら読み直しを試みる
        Public Function ShouldRetry() As Boolean

            If Not IsFailed Then
                Return False
            End If

            Return DateTime.Now - CreatedAt >= RetryAfterFailure

        End Function

    End Class

End Class
