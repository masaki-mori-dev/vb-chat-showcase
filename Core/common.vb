Imports System.IO

Public Module common






    ' 【GetIconSpriteHtml() を削除した】
    '
    ' Content/icon-sprite.svg を File.ReadAllText で読み、HTMLへそのまま貼り付ける関数だった。
    ' 呼び出しは3か所（Default.aspx.vb / Chat/ChatMaster.Master / Chat/ChatRoom.aspx）。
    '
    ' .aspx はキャッシュされないため、**どのページを開いても毎回86KBを送り直していた**
    ' （実測で、部屋のページは gzip 後の約7割がこれだった）。
    ' さらに、ページを描くたびにディスクから86KBを読み直してもいた。
    '
    ' 今は Scripts/icon-sprite.js が、ファイルとして1回だけ取ってきて DOM へ差し込む。
    ' ブラウザが30日キャッシュするため、2ページ目以降は通信が発生しない。
    '
    ' サーバー側がスプライトの中身を読む必要は、まだある（アイコン名の照合）。
    ' そちらは IconCatalog が自前で読んでいるので、この関数とは無関係に動く


    ' ============================================================
    ' 静的ファイル（CSS・JS）のURLに、更新のたびに変わる印を付けて返す
    ' ============================================================
    '
    ' 使い方 ： common.AssetUrl("~/Content/Site.css")
    ' 戻り値 ： /Content/Site.css?v=20260903095743
    '
    ' 【なぜ必要か】
    ' Web.Release.config で「このファイルは長くためてよい」と本番に伝えるようにした。
    ' URLが同じままだと、こちらがファイルを直して発行しても、利用者の手元には
    ' いつまでも古いものが表示され続ける。そこで、ファイルを直したら
    ' URLのほうも自動で変わるようにする。
    '
    ' 【印に、ファイルの最終更新時刻を使う理由】
    ' 手で「?v=3」と書き換える方式は採らなかった。書き忘れが起きるうえ、
    ' 起きたことに誰も気づけないため（宿題9とまったく同じ壊れ方をする）。
    ' 最終更新時刻なら、ファイルを直せば必ず変わる。
    ' 形式を yyyyMMddHHmmss にしてあるのは、HTMLのソースを見たときに
    ' 「いつ発行したものが配られているか」を人間の目で読めるようにするため。
    '
    ' 【毎回ファイルの時刻を読んでいる理由（起動時に1回だけ、にしない）】
    ' CSSだけを発行した場合、bin が変わらないためアプリは再起動しない。
    ' 起動時に1回だけ読む形にすると、そのとき古い印を配り続けることになり、
    ' 「直したのに届かない」がそのまま再発する。
    ' 読んでいるのはファイルの中身ではなく更新時刻（メタ情報）だけで、
    ' OSが持っている情報のため、DBへの問い合わせとは桁が違うほど軽い。
    ' 1ページあたり、多くて4回。
    '
    ' 【InvariantCulture を指定している理由】
    ' 書式に "yyyy" を含むため、サーバーの暦の設定によっては西暦以外が
    ' 入り込む余地がある。印は機械が比べるだけの文字列なので、
    ' 環境で揺れないように固定しておく。
    '
    Public Function AssetUrl(ByVal virtualPath As String) As String

        ' "~/Content/Site.css" を "/Content/Site.css" に直す（ブラウザが使える形にする）
        Dim url As String = VirtualPathUtility.ToAbsolute(virtualPath)

        Try
            Dim filePath As String = HttpContext.Current.Server.MapPath(virtualPath)

            If File.Exists(filePath) Then

                Dim stamp As String =
                    File.GetLastWriteTimeUtc(filePath).ToString(
                        "yyyyMMddHHmmss",
                        Globalization.CultureInfo.InvariantCulture)

                Return url & "?v=" & stamp

            End If

        Catch
            ' 何があっても画面は止めない。印の無いURLを返せば、今までと同じ動きになる
        End Try

        ' ファイルが見つからないときは、印を付けずにそのまま返す
        Return url

    End Function



    ' ============================================================
    ' このアプリのDLLを見分ける、短い印
    ' ============================================================
    '
    ' bin/vb-chat.dll のSHA-256の先頭12文字。
    ' Site.Master.vb が応答のヘッダー（X-VbChat-Build）として出し、
    ' 発行後の確かめの道具が手元のDLLと突き合わせる。
    '
    ' 【なぜ要るのか】
    ' 発行漏れを見つける道具が2つあるが、どちらもDLLを見られなかった。
    '   ・静的ファイルの突き合わせ … Scripts と Content の .js / .css だけ
    '   ・ページの確かめ       … ページの応答（DLLの中身は応答に現れない）
    '
    ' ログアウトの回では、直しの本体が5本の .vb（＝DLLの中）にあり、
    ' **本当に届いたかを外から確かめる手が1つも無かった。**
    ' 結局、実機で動いたことから逆に「届いていた」と分かった。
    ' それでは、届かなかったときに気づけない。
    '
    ' 【なぜ、日付や版番号ではなくハッシュなのか】
    ' 「手元のDLLと同じものが本番に在るか」を知りたいので、
    ' **中身から決まる値**でないと答えにならない。
    '   ・版番号 … 手で上げるものは、いつか上げ忘れる
    '   ・日付   … ASP.NETはDLLを別の場所へ複製してから読むため、
    '              ファイルの日時は「複製した時刻」になる。再起動のたびに変わる
    ' ハッシュなら、複製されても中身が同じなので値も同じになる。
    ' 発行後の確かめの道具が静的ファイルをSHA-256で突き合わせているのと、同じ考え方
    '
    ' 【12文字にしてある理由】
    ' 目で見比べられる長さにするため。取り違えを心配する場面ではない
    ' （偽装を防ぐ値ではなく、同じものかを見るだけの値）
    '
    ' 【外から見えて困る値ではない】
    ' ハッシュは元に戻せないので、中身は分からない。
    ' 静的ファイルのURLに付いている ?v= の日時と、同じ程度のものである
    Private BuildIdCache As String

    Public Function BuildId() As String

        ' 【鍵を取らないこと】
        ' 同時に呼ばれて2回計算されることはあるが、**必ず同じ値になる**ので害が無い。
        ' 文字列の代入そのものは分割されない。
        ' SiteSettings が Snapshot を1回の代入で公開しているのと、同じ考え方
        If BuildIdCache IsNot Nothing Then
            Return BuildIdCache
        End If

        Dim value As String = "unknown"

        Try
            ' Location は、ASP.NETが複製したDLLを指す。中身は bin のものと同じ
            Dim dllPath As String = Reflection.Assembly.GetExecutingAssembly().Location

            If Not String.IsNullOrEmpty(dllPath) AndAlso File.Exists(dllPath) Then

                Using stream As FileStream = File.OpenRead(dllPath)
                    Using sha = Security.Cryptography.SHA256.Create()

                        value = BitConverter.ToString(sha.ComputeHash(stream)) _
                                    .Replace("-", "") _
                                    .Substring(0, 12) _
                                    .ToLowerInvariant()

                    End Using
                End Using

            End If

        Catch
            ' 【何があっても画面は止めない】
            ' これは確認のための値であって、利用者には何の関係も無い。
            ' 読めなければ "unknown" のままにしておけば、
            ' 発行後の確かめの道具が「目印が読めていない」と教えてくれる
        End Try

        BuildIdCache = value

        Return BuildIdCache

    End Function



End Module




