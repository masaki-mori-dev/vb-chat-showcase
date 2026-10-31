<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Contact.aspx.vb" Inherits="vb_chat.ContactPage" Title="お問い合わせ" %>

<%-- 【ValidateRequest="false" にしてある】（問い合わせページ）

     ASP.NET は既定で、「<」のすぐ後ろに英字などが来る入力を、**ページに届く前に** 500 で止める。
     問い合わせは誰でも自由に書く欄で、**不具合の報告にタグやコードを書く人がいちばん多い**ので、
     そのままだと「コードの話を書いた人だけ 500 になる」ことになる
     （ログインのパスワードで踏んだのと、同じ罠）。

     切ったので、**この画面が出すものは、全部エスケープしてある**
       ・断った理由・受け付けた文言 … こちらで決めた文言だけ（HttpUtility.HtmlEncode を通す）
       ・入力欄に入れ直す値　　　　 … asp:TextBox が自分でエスケープする
       ・確認の画面に出す値　　　　 … asp:Literal に HttpUtility.HtmlEncode を通して入れる
     サイト全体（Web.config）では切らないこと（チャットの発言も部屋名も検証を通らなくなる）。

     【3つの画面を、1つのページで切り替える】（MultiView）
       入力 → 確認 → 送信完了
     URL は /contact のまま。**contact という名前のフォルダを作らないこと**（403.14 の罠。
     置き場所は Contacts\ ＝ Accounts\ と /account の関係と同じ）。

     【送ったあとは PRG】送信が済んだら /contact?done=sent へ転送して、完了の画面は GET で出す。
     完了の画面で F5 を押しても、もう一度送られない。

     【コメントは、必ずサーバー側コメントで書くこと】
     「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く。
     発行の前に、【発行前チェック】の grep を走らせること。

     【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】
     そこで囲みが閉じ、残りが利用者のブラウザにそのまま出る --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <div class="contact-box">

        <%-- 【見出しは画面ごとに変わる】
             入力＝お問い合わせ／確認＝入力内容の確認／完了＝送信完了。
             文言は Contacts/Contact.aspx.vb が持つ（ContactPage.Heading*）。
             **MultiView の外に置いてある**ので、3つの画面で同じ場所・同じ見た目になる --%>
        <h1><asp:Literal ID="HeadingText" runat="server" /></h1>

        <%-- 受け付けたときの答え（完了の画面だけで使う）。
             role="status"＝読み上げソフトにも、ページを開いたときに伝わる --%>
        <p class="contact-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- 断った理由。入力の画面でも確認の画面でも出したいので、MultiView の外に置いてある。
             【空のときは高さを持たせない】理由が出たときだけ行が生まれると、画面が跳ねる
             （.account-error・.create-room-error と同じ考え方。Content/Site.css） --%>
        <p class="contact-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <%-- 包んだ内容が入る、見えない欄。**MultiView の外**（どの画面でも送られる）。

             【入力の画面では空、確認の画面では入っている】
               入力 → 確認 … JavaScript が送る直前に入れる（動かなければ空のまま＝下の BodyBox を使う）
               確認 → 送信 … サーバーが入れて描き出す（内容の欄は、確認の画面には無いため）

             こうしてあるので、**確認から送信へ進むときも、生の「<script>」が POST に乗らない**
             （乗ると本番の WAF に 403 で止められる）。仕組みは Core/BodyTransport.vb --%>
        <asp:HiddenField ID="BodyEncoded" runat="server" />

        <asp:MultiView ID="Steps" runat="server" ActiveViewIndex="0">

            <%-- ============================================================
                 ① 入力
                 ============================================================ --%>
            <asp:View ID="InputView" runat="server">

                <p class="contact-lead">
                    不具合の報告・ご意見・ご要望などをお送りください。
                    返事がほしいときは、メールアドレスを入れてください
                    （すべてにお返事できるとは限りません）。
                </p>

                <%-- 【「（任意）」は、項目名から外して入力欄の中へ】
                     項目名をすっきりさせるため。**ただし placeholder は、読み上げソフトに伝わらないことがある**ので、
                     読み上げのための「（任意）」を、**見えない字で項目名の中に**置いてある
                     （.visually-hidden。項目名は入力欄の「名前」として読まれるので、確実に伝わる）。
                     placeholder の色は、薄すぎて読めないことのないよう Content/Site.css で決めてある --%>
                <div class="form-row">
                    <asp:Label ID="NameLabel" runat="server" AssociatedControlID="NameBox"
                               CssClass="contact-label"
                               Text="お名前<span class=&quot;visually-hidden&quot;>（任意）</span>" />
                    <asp:TextBox ID="NameBox" runat="server" CssClass="account-input"
                                 MaxLength="50" autocomplete="name" placeholder="任意" />
                </div>

                <%-- 【TextMode="Email"】<input type="email"> になり、ブラウザが自分で形を見てくれる。
                     **それは親切であって、防御ではない**——本当の確かめは Contacts/ContactRules.vb の
                     ValidateEmail（とくに改行を許さないこと＝メールの見出しの差し込み避け） --%>
                <%-- 【項目名は「メールアドレス」】（開発者が決定。前は「返信先のメールアドレス」）
                     項目名の列を狭くして、360 幅でもどの欄も項目名の横に並び、左端がそろうようにした。
                     返事に使うことは、上の文と、欄の中の薄い字で伝わる。列の幅は Content/Site.css の .contact-label --%>
                <div class="form-row">
                    <asp:Label ID="EmailLabel" runat="server" AssociatedControlID="EmailBox"
                               CssClass="contact-label"
                               Text="メールアドレス<span class=&quot;visually-hidden&quot;>（任意）</span>" />
                    <asp:TextBox ID="EmailBox" runat="server" CssClass="account-input" TextMode="Email"
                                 MaxLength="256" autocomplete="email"
                                 placeholder="任意（返事が要るとき）" />
                </div>

                <%-- 【選択肢は、マークアップに書かない】Contacts/Contact.aspx.vb の BindKinds が、
                     Contacts/ContactRules.vb の白リスト（AllowedKinds）から流し込む。
                     ベタ書きすると、種類を1つ増やした日に画面と白リストが食い違う。

                     【いちばん上は「選んでください」（値は空）】
                     最初から「不具合の報告」が選ばれていると、選んだつもりが無いまま送られる。
                     空の値は**保存しない**（DB の Kind は 10/20/30 のまま）ので、
                     下の RequiredFieldValidator（InitialValue=""）とサーバー側の両方で断る --%>
                <div class="form-row">
                    <asp:Label ID="KindLabel" runat="server" AssociatedControlID="KindList"
                               CssClass="contact-label" Text="種類" />
                    <asp:DropDownList ID="KindList" runat="server" CssClass="contact-select" />
                </div>

                <asp:RequiredFieldValidator ID="KindRequired" runat="server"
                                            ControlToValidate="KindList"
                                            InitialValue=""
                                            ValidationGroup="contact"
                                            Display="Dynamic"
                                            CssClass="contact-validator"
                                            ErrorMessage="種類を選んでください。"
                                            Text="種類を選んでください。" />

                <%-- 内容。
                     【maxlength を付けない】名前欄と違って長文を書く場所で、黙って途中で切られると理由が分からない
                     （チャットの発言欄と同じ判断。「サーバー側の検証（発言）」）。
                     長さは、サーバー側（ContactRules.ValidateBody）が理由を添えて断る。

                     【data-encode-on-submit】送る直前に、この欄の値を包む（Scripts/site.js の「7」）。
                     本番の手前の WAF が「<script>」などを含む POST を 403 で止めるため。
                     属性の値は、**包んだ値を入れる見えない欄（上の BodyEncoded）の id**。
                     先頭の印は data-encoded-prefix で渡す（サーバーの BodyTransport.EncodedPrefix と1か所で持つ）。
                     どちらも、コードビハインドから Attributes に入れている
                     （runat="server" の要素の属性に「<%」で始まる式を書くとページが止まるため）。

                     【この欄の値は、JavaScript が書き換えない】
                     書き換えていたころは、「戻る」で戻ると欄に "vbenc1:…" が出て、そのまま送ると
                     **本文が "vbenc1:…" のまま DB に入った**。今は包んだ値を上の hidden へ入れ、
                     この欄は送る瞬間だけ disabled にして POST から外している。
                     理由の全文は Scripts/site.js の「7」。

                     【EnableViewState="False"】2000文字ぶんを ViewState でも運ぶと、
                     ポストバックのたびに同じ中身を2つ送ることになる。内容は上の hidden が運ぶので、
                     こちらは持たなくてよい（Page_Load の DecodeBody が、毎回入れ直す）。

                     【包むのは、この欄だけ】お名前・メールアドレス・種類は包まない（WAF に守らせる） --%>
                <div class="form-row form-row--body">
                    <asp:Label ID="BodyLabel" runat="server" AssociatedControlID="BodyBox"
                               CssClass="contact-label" Text="内容" />
                    <asp:TextBox ID="BodyBox" runat="server" CssClass="contact-body-input"
                                 TextMode="MultiLine" Rows="10" EnableViewState="False" />
                </div>

                <%-- 【画面側の検査は、ここでは「内容が空か」だけ】
                     長さは、包んで送るので画面側では数えられない（包んだあとの長さは、元の文字数と一致しない）。
                     本当の判断はサーバー側（Contacts/ContactRules.vb）がする。
                     **置き場所は、その欄のすぐ下**——ボタンのそばの1行（下の contact-form-status）とは役目が違う。
                     あちらは「押しても何も起きない」を防ぐためのもので、こちらは「どの欄が駄目か」を示す --%>
                <asp:RequiredFieldValidator ID="BodyRequired" runat="server"
                                            ControlToValidate="BodyBox"
                                            ValidationGroup="contact"
                                            Display="Dynamic"
                                            CssClass="contact-validator"
                                            ErrorMessage="内容を入力してください。"
                                            Text="内容を入力してください。" />

                <p class="contact-hint">
                    <asp:Literal ID="BodyHintText" runat="server" />
                </p>

                <%-- 【人には見えない入力欄（ロボット避け）】
                     ロボットは、見つけた入力欄を片端から埋める。ここに何か入っていたら、
                     その1件は「送信しました」と出したうえで捨てる（Contacts/ContactRequest.vb の【ロボット避け】）。

                     【人が埋めてしまわないように、4つ重ねてある】
                       ・.contact-trap で画面の外へ飛ばす（display:none ではない＝消すと埋めないロボットがいる）
                       ・tabindex="-1"　　… Tab で入らない
                       ・aria-hidden="true" … 読み上げソフトが読まない
                       ・autocomplete="off" … ブラウザの自動入力に埋めさせない

                     【reCAPTCHA は使わない】Google へ訪問者の情報が送られるうえ、
                     読み上げソフトを使う人・年配の人にとって難しい。

                     【欄の名前を「email」「name」のような、自動入力が狙う名前にしないこと】
                     狙われると、ふつうの利用者の1件が黙って捨てられる --%>
                <div class="contact-trap" aria-hidden="true">
                    <asp:TextBox ID="TrapBox" runat="server" TabIndex="-1" autocomplete="off" />
                </div>

                <div class="contact-actions">

                    <%-- 【検査で止まったときの理由は、その欄の上に出る】
                         画面の検査（Validator）で止めたときは Scripts/site.js の「6」、サーバーで断ったときは「9」が、
                         理由を**その欄の上**へ出して、そこへカーソルを置く。
                         ボタンのそばの1行（data-validation-status）は外した——理由の場所を2つにしない --%>

                    <asp:Button ID="ConfirmButton" runat="server" Text="確認する" CssClass="account-submit"
                                ValidationGroup="contact" />

                </div>

                <p class="contact-note">
                    <asp:Literal ID="PrivacyNoteText" runat="server" />
                </p>

            </asp:View>

            <%-- ============================================================
                 ② 確認
                 ============================================================
                 【出す値は、全部 HttpUtility.HtmlEncode を通してある】（Contacts/Contact.aspx.vb）
                 この画面は ValidateRequest="false" なので、「<script>」も素通りして届く。
                 **文字として見えること**が、ここでの正しい姿である。
                 内容は改行を残したいので、CSS の white-space: pre-wrap で出す（.contact-review-body） --%>
            <asp:View ID="ConfirmView" runat="server">

                <p class="contact-lead">
                    この内容で送ります。よろしければ「送信する」を押してください。
                </p>

                <%-- 【並び順は、入力の画面とまったく同じにすること】
                     お名前 → メールアドレス → 種類 → 内容。
                     順番が違うと、確かめる人が1つずつ目で探し直すことになる。
                     **入力の欄を並べ替えるときは、ここも一緒に直すこと** --%>
                <dl class="contact-review">
                    <dt>お名前</dt>
                    <dd><asp:Literal ID="ReviewName" runat="server" /></dd>

                    <dt>メールアドレス</dt>
                    <dd><asp:Literal ID="ReviewEmail" runat="server" /></dd>

                    <dt>種類</dt>
                    <dd><asp:Literal ID="ReviewKind" runat="server" /></dd>

                    <dt>内容</dt>
                    <dd class="contact-review-body"><asp:Literal ID="ReviewBody" runat="server" /></dd>
                </dl>

                <%-- 【どちらも CausesValidation="False"】この画面に検査する欄は無い。
                     付けたままだと、入力の画面の Validator（別の View に居て描かれていない）を見に行く --%>
                <div class="contact-actions contact-actions--confirm">
                    <asp:Button ID="BackButton" runat="server" Text="戻って直す"
                                CssClass="contact-back-btn" CausesValidation="False" />
                    <asp:Button ID="SendButton" runat="server" Text="送信する"
                                CssClass="account-submit" CausesValidation="False" />
                </div>

                <p class="contact-note">
                    <asp:Literal ID="PrivacyNoteText2" runat="server" />
                </p>

            </asp:View>

            <%-- ============================================================
                 ③ 送信完了
                 ============================================================
                 【出すのは、上の見出しと、青い字の1行だけ】
                 入力欄・ボタン・説明文は出さない。文言は DoneText（この箱の上）に入る --%>
            <asp:View ID="DoneView" runat="server">
            </asp:View>

        </asp:MultiView>

    </div>

</asp:Content>
