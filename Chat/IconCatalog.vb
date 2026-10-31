Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Web
Imports System.Web.Hosting

' Content/icon-sprite.svg に入っているアイコン名（icon-xxx の xxx）の一覧を保持するクラス
'
' 【なぜ IconMaster テーブルではなく、スプライトを照合元にするのか】
'   1. 入室のたびにDBへ問い合わせずに済む
'   2. 判定の基準が「実際に画面へ描画できるか」と一致する。
'      DBに行があってもスプライトに無ければ <use href="#icon-xxx"> は何も描かないため、
'      DBを基準にすると「検査は通ったのに、アイコンが表示されない」という食い違いが起きる
'
' 【キャッシュの捨てかた】
' 読み込んだ一覧は、スプライトファイルの最終更新時刻とセットで持っている。
' ファイルが作り直されると時刻が変わるため、次の呼び出しで自動的に読み直される
' （GenerateIconSprite.aspx による生成でも、FTPでの差し替えでも同じように効く）。
' 加えて Clear() を用意してある。将来、管理画面からアイコンを追加する処理を書くときは、
' 書き出しの直後にこれを呼べばよい（ファイルの時刻の粒度に左右されず、確実に捨てられる）
Public Class IconCatalog

    ' スプライトの置き場所（Scripts/icon-sprite.js がブラウザ側で取りに行くのと同じファイル）
    Private Const SpriteVirtualPath As String = "~/Content/icon-sprite.svg"

    ' 既定のアイコンを書いてある appSettings のキー名
    Private Const DefaultIconTypeKey As String = "DefaultIconType"

    ' 設定も読めず、スプライトも1件も読めなかったときの、最後の受け皿
    '
    ' スプライトが無い状態では、そもそも画面にアイコンが1つも描かれない
    ' （icon-sprite.js が取りに行っても中身が無いため）。つまりこの値が使われる場面では、
    ' 何を返しても見た目は変わらない。それでも既定値を空にしないのは、
    ' ログの IconType 列が空になって「あとから見返したときに何も分からない」のを避けるため
    Private Const FallbackIconType As String = "male"

    ' <symbol id="icon-dog" viewBox="0 0 512 512" data-label="イヌ"> の開きタグ
    ' （スプライトは GenerateIconSprite.aspx が組み立てた、この形しか出力しない）
    Private Shared ReadOnly SymbolTagPattern As New Regex(
        "<symbol\b[^>]*>", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    ' その開きタグから、アイコン名を取り出すためのパターン
    Private Shared ReadOnly IconIdPattern As New Regex(
        "id\s*=\s*""icon-(?<name>[^""<>]+)""", RegexOptions.Compiled)

    ' 同じく、日本語の名前
    '
    ' 【なぜ <title> ではなく data-label から読むのか】
    ' <symbol> の中に <title> を置くと、<use> で描いたアイコンにマウスを乗せたときに
    ' ブラウザが名前のポップアップを出す（部屋の発言の行にも、参加者一覧にも、ラウンジの顔にも）。
    ' data-label は描画に一切影響しない
    Private Shared ReadOnly IconLabelPattern As New Regex(
        "data-label\s*=\s*""(?<label>[^""]*)""", RegexOptions.Compiled)

    ' キャッシュを触るときの排他制御用
    Private Shared ReadOnly CacheLock As New Object()

    ' 読み込み済みのアイコン名。まだ一度も読んでいなければ Nothing
    Private Shared CachedNames As HashSet(Of String) = Nothing

    ' アイコン名 → 日本語の名前（無ければ入っていない）
    Private Shared CachedLabels As Dictionary(Of String, String) = Nothing

    ' 同じ中身を、スプライトに書かれている順で持ったもの
    '
    ' 【なぜ2つ持つのか】
    ' 照合（IsValid）には HashSet が要るが、**HashSet には順番が無い**。
    ' トップページのアイコン選択は、この順にそのまま並ぶため、
    ' HashSet を並べると**ページを開くたびに並び順が変わりうる**。
    ' 順番が要る用途のために、読み込んだ順のまま別に持っておく
    Private Shared CachedOrderedNames As List(Of String) = Nothing

    ' スプライトの先頭にあるアイコン名（設定が使えなかったときの保険）
    Private Shared CachedFirstName As String = Nothing

    ' キャッシュを作ったときの、スプライトの最終更新時刻（UTC）
    ' ファイルが無い場合は DateTime.MinValue
    Private Shared CachedStamp As DateTime = DateTime.MinValue


    ' キャッシュを捨てる（次の呼び出しで読み直される）
    '
    ' 通常は最終更新時刻の比較で自動的に読み直されるため、呼ぶ必要はない。
    ' スプライトを作り直した直後に、確実に反映させたい場合だけ呼ぶ
    Public Shared Sub Clear()

        SyncLock CacheLock
            CachedNames = Nothing
            CachedOrderedNames = Nothing
            CachedLabels = Nothing
            CachedFirstName = Nothing
            CachedStamp = DateTime.MinValue
        End SyncLock

    End Sub


    ' そのアイコン名が、スプライトに実在するかどうか
    Public Shared Function IsValid(iconType As String) As Boolean

        If String.IsNullOrEmpty(iconType) Then
            Return False
        End If

        EnsureLoaded()

        SyncLock CacheLock
            Return CachedNames IsNot Nothing AndAlso CachedNames.Contains(iconType)
        End SyncLock

    End Function


    ' 既定のアイコン名
    '
    ' 【appSettingsからSiteSettingsへ移した】
    ' 値はSiteSettings.DefaultIconType（既定 male）が持つ。
    '
    ' 【なぜ「スプライトの先頭」にしないのか】
    ' 先頭を既定にすると、管理画面からアイコンを1つ追加・削除しただけで、
    ' 既定のアイコンが黙って別のものへ変わる。エラーは出ないため誰も気づけない。
    ' 既定の色（SiteSettings の DefaultIconColor）を明示しているのと、考え方を揃えている
    '
    ' 設定の名前がスプライトに無かった場合は、保険としてスプライトの先頭へ落とす
    ' （その場合でも、Traceに警告を出して気づけるようにしておく）
    '
    ' 【罠】設定は、CacheLockの外で読むこと。
    ' SiteSettingsはキャッシュが空のときDBへ問い合わせに行く。
    ' 鍵を握ったままDBを待つと、その間ほかのスレッドのアイコン検査が全部止まる
    Public Shared ReadOnly Property DefaultIconType As String
        Get
            EnsureLoaded()

            Dim configured As String = SiteSettings.DefaultIconType

            SyncLock CacheLock

                If Not String.IsNullOrEmpty(configured) AndAlso
                   CachedNames IsNot Nothing AndAlso CachedNames.Contains(configured) Then
                    Return configured
                End If

                ' 鍵（CacheLock）の中だが、ErrorReport は積むだけなので待たされない
                ErrorReport.ReportProblem("IconCatalog.DefaultIconType",
                    "SiteSettings の " & DefaultIconTypeKey & "（" & If(configured, "未設定") &
                    "）が、Content/icon-sprite.svg に見つかりません。スプライトの先頭のアイコンで代用します。")

                Return If(CachedFirstName, FallbackIconType)

            End SyncLock
        End Get
    End Property


    ' アイコン名の一覧を、スプライトに書かれている順で返す
    '
    ' 【何に使うか】トップページのアイコン選択を、サーバー側で組み立てるため。
    '
    ' 【なぜサーバー側で組み立てるのか】
    ' 以前は、ページに埋め込んだスプライトから JavaScript が <symbol> を拾って
    ' 選択肢を作っていた。スプライトをファイルとして取りに行く形に変えたため、
    ' そのままだと**届くまで選択肢が0個**になり、
    '   ・アイコンの並ぶ場所が高さ0から一気に伸びて、画面が大きく跳ねる（実測で1188px）
    '   ・取れなかったときは、1つも選べない
    ' という2つが起きる。名前さえ分かれば枠は先に置けるので、そこはサーバーが出す。
    ' 中身の絵は、スプライトが届いた時点で <use> が自動的に解決してくれる
    '
    ' 【返すのは複製】呼び出し側が中身を書き換えても、キャッシュが壊れないようにするため
    Public Shared Function GetOrderedIconTypes() As List(Of String)

        EnsureLoaded()

        SyncLock CacheLock

            If CachedOrderedNames Is Nothing Then
                Return New List(Of String)
            End If

            Return New List(Of String)(CachedOrderedNames)

        End SyncLock

    End Function


    ' そのアイコンの日本語の名前（無ければ空文字）
    '
    ' 【何に使うか】トップページと部屋の「見た目」の、読み上げ用の名前（aria-label）。
    ' 入っていなければ、呼び出し側が「アイコン7」のような番号へ落とす
    ' （＝Label 列が空でも、スプライトが古くても、画面は壊れない）
    Public Shared Function GetLabel(iconType As String) As String

        If String.IsNullOrEmpty(iconType) Then
            Return ""
        End If

        EnsureLoaded()

        SyncLock CacheLock

            If CachedLabels Is Nothing Then
                Return ""
            End If

            Dim label As String = Nothing
            If CachedLabels.TryGetValue(iconType, label) Then
                Return label
            End If

            Return ""

        End SyncLock

    End Function


    ' 必要ならスプライトを読み直して、キャッシュを最新にする
    Private Shared Sub EnsureLoaded()

        ' 物理パスの解決に HttpContext を使わないこと。
        ' この処理はSignalRのHubから呼ばれるが、WebSocket接続の場合、
        ' HttpContext.Current が Nothing になることがあるため
        Dim spritePath As String = HostingEnvironment.MapPath(SpriteVirtualPath)

        Dim stamp As DateTime = DateTime.MinValue
        If spritePath IsNot Nothing AndAlso File.Exists(spritePath) Then
            stamp = File.GetLastWriteTimeUtc(spritePath)
        End If

        SyncLock CacheLock

            ' 前回読んだときから、ファイルが作り直されていなければ、そのまま使う
            If CachedNames IsNot Nothing AndAlso CachedStamp = stamp Then
                Return
            End If

            Dim names As New HashSet(Of String)(StringComparer.Ordinal)
            Dim ordered As New List(Of String)
            Dim labels As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Dim firstName As String = Nothing

            If stamp <> DateTime.MinValue Then

                Dim svg As String

                Try
                    svg = File.ReadAllText(spritePath)
                Catch ex As Exception
                    ' 一時的に読めなかった（書き出しの最中など）。
                    ' ここでキャッシュを空で塗り替えると、全員のアイコンが既定値へ
                    ' 落ちてしまうため、何もせずに戻り、次の呼び出しでもう一度試す
                    Return
                End Try

                ' <symbol ...> の開きタグを1つずつ見て、名前と日本語の名前を取り出す
                For Each tag As Match In SymbolTagPattern.Matches(svg)

                    Dim idMatch As Match = IconIdPattern.Match(tag.Value)
                    If Not idMatch.Success Then
                        Continue For
                    End If

                    Dim name As String = idMatch.Groups("name").Value

                    If names.Add(name) Then

                        ordered.Add(name)

                        ' 日本語の名前（属性の値なので、実体参照を戻してから持つ）。
                        ' 画面へ出すときは、出口で改めてエスケープする
                        Dim labelMatch As Match = IconLabelPattern.Match(tag.Value)
                        If labelMatch.Success Then
                            Dim label As String = HttpUtility.HtmlDecode(labelMatch.Groups("label").Value)
                            If Not String.IsNullOrEmpty(label) Then
                                labels(name) = label
                            End If
                        End If

                        If firstName Is Nothing Then
                            ' 設定が使えなかったときの保険として、先頭のものを覚えておく
                            firstName = name
                        End If
                    End If

                Next

            End If

            CachedNames = names
            CachedOrderedNames = ordered
            CachedLabels = labels
            CachedFirstName = firstName
            CachedStamp = stamp

        End SyncLock

    End Sub

End Class
