' プロフィールの「選ぶ」欄の、選択肢の一覧（ブログ型の①）
'
' 【ここに1か所だけ持つ】マークアップに <asp:ListItem> をベタ書きしないこと。
' 部屋の定員の選択肢（Chat/ChatRoomRules.vb）・記事の状態の3択（Articles/ArticleRules.vb）と、同じ決まりである。
' ベタ書きすると、選択肢を1つ足した日に「画面には出るが、サーバーが受け付けない」（またはその逆）が起きる。
'
' 【値は10刻み。ただし都道府県だけは違う】
' 10刻みにしてあるのは「あとから間に足せるように」（ChatMessageType・Articles.Status と同じ決まり）。
' 都道府県は**こちらが決めた選択肢ではなく**、JIS の番号（北海道=1 … 沖縄=47）をそのまま使う。
' 間に足されることが無いので、10刻みにする意味が無い。48（海外）だけがこちらで決めた値である。
'
' 【日本語の名前を変えても、DB の中身は変わらない】画面に出す文字だけがここに在る。
' ロールの日本語名を変えたときと同じ形で、あとから言い換えられる
Public NotInheritable Class ProfileChoice

    Public Sub New(value As Integer, text As String)
        _value = value
        _text = text
    End Sub

    Private ReadOnly _value As Integer
    Private ReadOnly _text As String

    ' DB に入る値
    Public ReadOnly Property Value As Integer
        Get
            Return _value
        End Get
    End Property

    ' 画面に出す文字（出口でエスケープすること）
    Public ReadOnly Property Text As String
        Get
            Return _text
        End Get
    End Property

End Class


Public NotInheritable Class ProfileChoices

    Private Sub New()
    End Sub


    ' ============================================================
    ' 見せるか（値と、見せるかを別の列で持つ。理由は Database/CreateTable/AccountProfiles.sql）
    ' ============================================================
    '
    ' 【真偽値にしていない】MVC版で「フォロワーだけ」のような3つ目が来たときに、間へ入れられるようにするため

    Public Const VisibilityPublic As Integer = 10
    Public Const VisibilityPrivate As Integer = 20

    ' 画面のチェックボックス（「非公開」）は真偽値なので、ここで行き来させる
    Public Shared Function VisibilityOf(isPrivate As Boolean) As Integer
        Return If(isPrivate, VisibilityPrivate, VisibilityPublic)
    End Function

    Public Shared Function IsPrivate(visibility As Integer) As Boolean
        Return visibility = VisibilityPrivate
    End Function

    ' 決めた値の外なら、公開へ落とす
    '
    ' 【公開へ落とす向きでよいのか】よい。ここへ来るのは画面のチェックボックスの値だけで、
    ' DB から読んだ値は CHECK 制約が守っている。既定（何も触っていない）が公開なので、そこへ寄せる
    Public Shared Function NormalizeVisibility(visibility As Integer) As Integer
        Return If(visibility = VisibilityPrivate, VisibilityPrivate, VisibilityPublic)
    End Function


    ' ============================================================
    ' 生年月日の出しかた（2択）
    ' ============================================================
    '
    ' 【3択から2択へ】（基本ルール「同じ種類の設定は、どの項目でも同じ形で出す」）
    ' それまでは「出さない／月日だけ／年月日」の3択で、「出すか」と「どこまで出すか」を1つの値に乗せていた。
    ' 公開・非公開は、ほかの項目と同じ「非公開」のチェック（AccountProfiles.BirthDateVisibility）へ分けた。
    ' **非公開にしても、ここで選んだほうは覚えておく**（チェックを外せば、元の出しかたに戻る）。
    '
    ' 【BirthHidden（10）は古い値】DB の CHECK と既定値に残っている。読んだら「非公開＋年月日」として扱う
    ' （AccountProfileStore.ReadRecord）。新しく書くことは無い
    Public Const BirthHidden As Integer = 10
    Public Const BirthMonthDay As Integer = 20
    Public Const BirthFull As Integer = 30

    ' 【並び】年月日が先（既定）。前の3択の既定「出さない」は、非公開のチェックに置き換わった
    ' 【例は外した】「非公開」のチェックと1行に並べると、
    ' 360・412 幅で選ぶ欄の字が切れたため。すぐ左に年・月・日の欄があるので、言葉だけで分かる
    Private Shared ReadOnly _birthModes As ProfileChoice() = {
        New ProfileChoice(BirthFull, "年月日"),
        New ProfileChoice(BirthMonthDay, "月日だけ")
    }

    Public Shared Function BirthDateModes() As ProfileChoice()
        Return _birthModes
    End Function

    Public Shared Function IsBirthDateMode(value As Integer) As Boolean
        Return Contains(_birthModes, value)
    End Function


    ' ============================================================
    ' 性別
    ' ============================================================
    '
    ' 【「その他」「回答しない」を入れてある】
    ' 「回答しない」と「非公開」と「空欄」は、それぞれ違うことを意味する

    Public Const GenderMale As Integer = 10
    Public Const GenderFemale As Integer = 20
    Public Const GenderOther As Integer = 30
    Public Const GenderNoAnswer As Integer = 40

    Private Shared ReadOnly _genders As ProfileChoice() = {
        New ProfileChoice(GenderMale, "男性"),
        New ProfileChoice(GenderFemale, "女性"),
        New ProfileChoice(GenderOther, "その他"),
        New ProfileChoice(GenderNoAnswer, "回答しない")
    }

    Public Shared Function Genders() As ProfileChoice()
        Return _genders
    End Function

    Public Shared Function GenderName(value As Integer?) As String
        Return NameOfValue(_genders, value)
    End Function


    ' ============================================================
    ' 血液型
    ' ============================================================

    Private Shared ReadOnly _bloodTypes As ProfileChoice() = {
        New ProfileChoice(10, "A型"),
        New ProfileChoice(20, "B型"),
        New ProfileChoice(30, "O型"),
        New ProfileChoice(40, "AB型")
    }

    Public Shared Function BloodTypes() As ProfileChoice()
        Return _bloodTypes
    End Function

    Public Shared Function BloodTypeName(value As Integer?) As String
        Return NameOfValue(_bloodTypes, value)
    End Function


    ' ============================================================
    ' ステータス
    ' ============================================================

    Private Shared ReadOnly _relationships As ProfileChoice() = {
        New ProfileChoice(10, "独身"),
        New ProfileChoice(20, "恋人あり"),
        New ProfileChoice(30, "婚約中"),
        New ProfileChoice(40, "既婚"),
        New ProfileChoice(50, "その他")
    }

    Public Shared Function Relationships() As ProfileChoice()
        Return _relationships
    End Function

    Public Shared Function RelationshipName(value As Integer?) As String
        Return NameOfValue(_relationships, value)
    End Function


    ' ============================================================
    ' 職業（選ぶ ＋ 自由記入。自由記入は AccountProfiles.OccupationText）
    ' ============================================================

    Private Shared ReadOnly _occupations As ProfileChoice() = {
        New ProfileChoice(10, "会社員"),
        New ProfileChoice(20, "公務員"),
        New ProfileChoice(30, "会社役員"),
        New ProfileChoice(40, "自営業"),
        New ProfileChoice(50, "自由業"),
        New ProfileChoice(60, "専業主婦・主夫"),
        New ProfileChoice(70, "パート・アルバイト"),
        New ProfileChoice(80, "学生"),
        New ProfileChoice(90, "無職"),
        New ProfileChoice(100, "その他")
    }

    Public Shared Function Occupations() As ProfileChoice()
        Return _occupations
    End Function

    Public Shared Function OccupationName(value As Integer?) As String
        Return NameOfValue(_occupations, value)
    End Function


    ' ============================================================
    ' 都道府県（JIS の番号。1=北海道 … 47=沖縄。48=海外）
    ' ============================================================
    '
    ' 【番号で持ち、名前はここにだけ置く】名前を DB に入れると、言い換えた日に
    ' 既に入っている行だけ古い名前で残る（ロールの日本語名と同じ判断）

    Public Const PrefectureOverseas As Integer = 48

    Private Shared ReadOnly _prefectureNames As String() = {
        "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県",
        "茨城県", "栃木県", "群馬県", "埼玉県", "千葉県", "東京都", "神奈川県",
        "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県",
        "岐阜県", "静岡県", "愛知県", "三重県",
        "滋賀県", "京都府", "大阪府", "兵庫県", "奈良県", "和歌山県",
        "鳥取県", "島根県", "岡山県", "広島県", "山口県",
        "徳島県", "香川県", "愛媛県", "高知県",
        "福岡県", "佐賀県", "長崎県", "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県",
        "海外"
    }

    Private Shared ReadOnly _prefectures As ProfileChoice() = BuildPrefectures()

    Private Shared Function BuildPrefectures() As ProfileChoice()

        Dim list As New List(Of ProfileChoice)()

        For i As Integer = 0 To _prefectureNames.Length - 1
            list.Add(New ProfileChoice(i + 1, _prefectureNames(i)))
        Next

        Return list.ToArray()

    End Function

    Public Shared Function Prefectures() As ProfileChoice()
        Return _prefectures
    End Function

    Public Shared Function PrefectureName(value As Integer?) As String
        Return NameOfValue(_prefectures, value)
    End Function


    ' ============================================================
    ' 共通
    ' ============================================================

    ' その値が一覧に在るか（画面から来た値を、そのまま DB へ入れないため）
    Public Shared Function Contains(choices As ProfileChoice(), value As Integer) As Boolean

        For Each choice As ProfileChoice In choices
            If choice.Value = value Then
                Return True
            End If
        Next

        Return False

    End Function


    ' 一覧に無い値・Nothing なら Nothing を返す（＝選んでいない）
    '
    ' 【画面から来た番号を、必ずここへ通すこと】
    ' 通さずに DB へ入れると、DB の CHECK 制約で例外になる（＝保存が失敗する）。
    ' ここで Nothing へ落としておけば、「選んでいない」として静かに受け止められる
    Public Shared Function NormalizeOrNothing(choices As ProfileChoice(), value As Integer?) As Integer?

        If Not value.HasValue Then
            Return Nothing
        End If

        If Not Contains(choices, value.Value) Then
            Return Nothing
        End If

        Return value

    End Function


    ' 値に対応する日本語名（無ければ空文字）
    Private Shared Function NameOfValue(choices As ProfileChoice(), value As Integer?) As String

        If Not value.HasValue Then
            Return ""
        End If

        For Each choice As ProfileChoice In choices
            If choice.Value = value.Value Then
                Return choice.Text
            End If
        Next

        Return ""

    End Function

End Class
