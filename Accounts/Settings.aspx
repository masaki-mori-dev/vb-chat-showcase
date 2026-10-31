<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Settings.aspx.vb" Inherits="vb_chat.AccountSettingsPage" Title="設定" %>

<%-- 【ValidateRequest="false" にしてある】
     ASP.NET は既定で「<」のすぐ後ろに英字が来る入力（「<a」など）を、ページに届く前に 500 で止める。
     **パスワードは、パスワード管理ソフトが作ると「<」を普通に含む**。切らないと、そういうパスワードでは
     ログインも・作ることも・変えることもできない（開発機のログインの画面で 500 になるのを確かめた）。
     切ってよい理由＝この画面が画面へ出す値は、全部エスケープしている（<%: %>・HttpUtility.HtmlEncode・asp:TextBox の既定）。
     アカウントの画面の3つ（Login・Setup・Settings）で、そろえてある。**Web.config でサイト全体を切らないこと** --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）

         アカウント設定。何を、どの順で確かめるかは Accounts/AccountSettings.vb。
         
         【1つの form に、区切りが3つある】（ASP.NET Web Forms はページに form が1つだけ）
           ・Validator も required も使わない。required を付けると、表示名の「保存」でも
             パスワードの欄が空だと止まる。検査はサーバーで行い、理由はその区切りのボタンのそばに出す
           ・Enter で押されるボタンを、区切りごとに決める（asp:Panel の DefaultButton）。
             決めないと、パスワードの欄で Enter を押したとき、いちばん上のボタン（表示名の保存）が押される
         【パスワードの欄の値は、画面へ戻さない】TextMode="Password" は既定で戻さない --%>

    <div class="admin-box account-settings">

        <h1>設定</h1>

        <%-- うまくいったときの知らせ（?done=… の合図から、こちらが文言を決める） --%>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- 【区切りを、中身が決める並びにする】（指摘：PC なのに設定の欄の横幅が狭い）
             箱を .account-box（520px）から .admin-box（管理画面の箱）へ移し、区切り4つをこの grid に入れた。
             **幅があれば横に2つ、無ければ縦に1つ**。幅の数字（@media）は1つも書いていない
             ——プロフィールの編集の画面（.profile-grid）と、同じ考え方である。
             【ログイン・最初の管理人を作る画面は、今までどおり狭い箱のまま】
             あちらは項目が少なく、広げる意味が無いため --%>
        <div class="account-settings-grid">

            <%-- === ユーザー名（見るだけ）=== --%>
            <section class="account-section" aria-labelledby="settingsUserNameHeading">

                <h2 id="settingsUserNameHeading">アカウントID</h2>

                <%-- 【項目名を置いていない】この区切りの中に欄は1つだけで、
                     見出し（h2）と同じ言葉になるため。読み上げソフトには aria-labelledby で伝えている --%>
                <div class="form-row">
                    <%-- 【readonly の input にしてある】見た目は「変えられない欄」、そして
                         autocomplete="username" で、パスワード管理ソフトに「どのアカウントのパスワードか」を伝える。
                         name を付けていないので送られない。出口でエスケープ（<%: %>） --%>
                    <input type="text" id="settingsUserName" class="account-input" readonly="readonly"
                           autocomplete="username" value="<%: UserNameText %>"
                           aria-labelledby="settingsUserNameHeading" aria-describedby="settingsUserNameHint" />
                </div>
                <%-- 【短くした理由】（「常設の観点」の【画面の言葉】）
                     消したのは「記事の URL『/articles/ユーザー名/…』に使っているので、
                     変えると記事の URL が全部変わるため」という理由の説明。
                     **変えられないことだけ分かればよく、理由まで読ませる必要は無い** --%>
                <p id="settingsUserNameHint" class="account-hint">
                    あとから変えられません。
                </p>

            </section>

            <%-- === 名前（表示名）===
                 【呼び名を、プロフィールの画面とそろえてある】
                 決定：どちらも「名前（表示名）」のように呼び名をそろえる。
                 **書き換える道は AccountSettings.ChangeDisplayName の1本**で、プロフィールの画面もそこを通る --%>
            <asp:Panel ID="DisplayNamePanel" runat="server" data-validate-scope="1" DefaultButton="DisplayNameButton"
                       CssClass="account-section" aria-labelledby="settingsDisplayNameHeading">

                <h2 id="settingsDisplayNameHeading">名前（表示名）</h2>

                <%-- 【理由の要素は、区切りの欄より上に置く】（決まり＝理由は入力欄の上）
                     サーバーが「この欄が悪い」と印を付けたときは、Scripts/site.js の「9」が、理由をその欄の上へ移す。
                     どの欄とも言えない理由（混み合っています、など）は、ここに出る。
                     **欄より下へ戻さないこと**——スマホでは、キーボードが開くと欄の下は見えない --%>
                <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="DisplayNameError" runat="server" /></p>

                <%-- 項目名を置いていない理由は、上の「ユーザー名」と同じ --%>
                <div class="form-row">
                    <asp:TextBox ID="DisplayNameBox" runat="server" CssClass="account-input"
                                 MaxLength="50" autocomplete="nickname"
                                 aria-labelledby="settingsDisplayNameHeading" />
                </div>
                <%-- 【見た目の位置で指さないこと】（「常設の観点」）
                     「この画面の右上に出る名前です」と書いていたが、スマホではヘッダーが3行になり、右上ではない。
                     【同じ日に、さらに短くした】消したのは2つ。
                       ・「（50文字まで）」… 欄の MaxLength で止まるので、書かなくても入れられない
                       ・「プロフィールの画面と、同じものです。」
                         … 同じことは**プロフィールの画面の側の説明に残してある**（片方で足りる） --%>
                <p class="account-hint">
                    ブログやプロフィール、自分のアイコンのメニューに出る名前です。
                </p>


                <div class="account-actions">
                    <asp:Button ID="DisplayNameButton" runat="server" formnovalidate="formnovalidate" Text="名前（表示名）を保存する" CssClass="account-submit" />
                </div>

            </asp:Panel>

            <%-- === メールアドレス === --%>
            <asp:Panel ID="EmailPanel" runat="server" data-validate-scope="1" DefaultButton="EmailButton"
                       CssClass="account-section" aria-labelledby="settingsEmailHeading">

                <h2 id="settingsEmailHeading">メールアドレス</h2>

                <p class="account-current">今のメールアドレス：<strong><%: EmailText %></strong></p>

                <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="EmailError" runat="server" /></p>

                <div class="form-row">
                    <asp:Label ID="NewEmailLabel" runat="server" AssociatedControlID="NewEmailBox"
                               CssClass="account-label" Text="新しい<wbr />メールアドレス" />
                    <asp:TextBox ID="NewEmailBox" runat="server" CssClass="account-input"
                                 TextMode="Email" MaxLength="256" autocomplete="email" />
                </div>

                <%-- 【パスワード管理ソフトへの目印】（T の直し。下の「区切りごとの確かめ」も）
                     Chrome は、パスワードの欄のすぐ前の文字の欄を「ユーザー名の欄」とみなして、
                     保存してあるユーザー名を入れる。この区切りでは、それが「新しいメールアドレス」になり、
                     **testuser1 が入って、メールアドレスの形の確かめで退会まで止められた**。
                     目に見えない autocomplete="username" の欄を、パスワードの欄の直前に置いて、そちらへ入れさせる
                     （name が無いので送られない。tabindex="-1" と aria-hidden で、キーボードと読み上げソフトからも外してある） --%>
                <input type="text" class="visually-hidden" tabindex="-1" aria-hidden="true" readonly="readonly"
                       autocomplete="username" value="<%: UserNameText %>" />

                <div class="form-row">
                    <asp:Label ID="EmailPasswordLabel" runat="server" AssociatedControlID="EmailPasswordBox"
                               CssClass="account-label" Text="今のパスワード" />
                    <asp:TextBox ID="EmailPasswordBox" runat="server" CssClass="account-input"
                                 TextMode="Password" MaxLength="128" autocomplete="current-password" />
                </div>
                <%-- 【短くした理由】消したのは「パスワードの再設定に使うアドレスなので」。
                     **今のパスワードが要ることだけ分かればよい**。
                     見出し（「メールアドレス」）の下なので、主語も要らない --%>
                <p class="account-hint">
                    変えるには、今のパスワードが要ります。
                </p>


                <div class="account-actions">
                    <asp:Button ID="EmailButton" runat="server" formnovalidate="formnovalidate" Text="メールアドレスを変える" CssClass="account-submit" />
                </div>

            </asp:Panel>

            <%-- === パスワード === --%>
            <asp:Panel ID="PasswordPanel" runat="server" data-validate-scope="1" DefaultButton="PasswordButton"
                       CssClass="account-section" aria-labelledby="settingsPasswordHeading">

                <h2 id="settingsPasswordHeading">パスワード</h2>

                <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="PasswordError" runat="server" /></p>

                <%-- パスワード管理ソフトへの目印（メールアドレスの区切りのコメント） --%>
                <input type="text" class="visually-hidden" tabindex="-1" aria-hidden="true" readonly="readonly"
                       autocomplete="username" value="<%: UserNameText %>" />

                <div class="form-row">
                    <asp:Label ID="CurrentPasswordLabel" runat="server" AssociatedControlID="CurrentPasswordBox"
                               CssClass="account-label" Text="今のパスワード" />
                    <asp:TextBox ID="CurrentPasswordBox" runat="server" CssClass="account-input"
                                 TextMode="Password" MaxLength="128" autocomplete="current-password" />
                </div>

                <div class="form-row">
                    <asp:Label ID="NewPasswordLabel" runat="server" AssociatedControlID="NewPasswordBox"
                               CssClass="account-label" Text="新しい<wbr />パスワード" />
                    <asp:TextBox ID="NewPasswordBox" runat="server" CssClass="account-input"
                                 TextMode="Password" MaxLength="128" autocomplete="new-password" />
                </div>

                <div class="form-row">
                    <asp:Label ID="ConfirmPasswordLabel" runat="server" AssociatedControlID="ConfirmPasswordBox"
                               CssClass="account-label" Text="新しいパスワード<wbr />（確認）" />
                    <asp:TextBox ID="ConfirmPasswordBox" runat="server" CssClass="account-input"
                                 TextMode="Password" MaxLength="128" autocomplete="new-password" />
                </div>
                <%-- 【短くした理由】消したのは3つ。
                       ・「・128文字以内」… 欄の MaxLength で止まる
                       ・「（スマホなど）」… 例を出さなくても伝わる
                       ・「（この端末はログインしたまま）」
                         … 押した本人がログアウトされないのは当たり前で、書くと逆に不安にさせる。
                           **仕組みとしては、変えた端末だけ札を出し直している**（AccountAuth.Issue） --%>
                <p class="account-hint">
                    12文字以上。変えると、<strong>ほかの端末のログインは切れます</strong>。
                </p>


                <div class="account-actions">
                    <asp:Button ID="PasswordButton" runat="server" formnovalidate="formnovalidate" Text="パスワードを変える" CssClass="account-submit" />
                </div>

            </asp:Panel>

            <%-- === 退会（いちばん下。一般ユーザーの登録の回）===

                 【サイト管理者には、この区切りごと出さない】（Settings.aspx.vb の Page_Load）
                 出さないだけでは守りにならないので、AccountRetire.Retire も必ず断る。

                 【なぜ、いちばん下なのか】取り返しのつかない操作なので、
                 ふだん使う項目（名前・メールアドレス・パスワード）を押し分ける途中に置かない。

                 【押す前に確かめる】data-confirm（Scripts/site.js の「5」）。
                 ブラウザ標準の confirm は使わない（見た目がブラウザ任せ・背面のタブでは抑制される） --%>
            <asp:Panel ID="RetirePanel" runat="server" data-validate-scope="1" DefaultButton="RetireButton"
                       CssClass="account-section account-section--danger" aria-labelledby="settingsRetireHeading">

                <h2 id="settingsRetireHeading">退会</h2>

                <%-- 【ここは、短くしてはいけないところである】
                     「何が消えるか」「戻せないか」「名前が再び使えないか」は、
                     **押したあとでは取り返しがつかない**ので、見ただけでは分からない。
                     「説明文は短く」の決まりは、
                     「見れば分かることを書かない」という意味であって、これは見れば分からない --%>
                <%-- 【に直した】
                     ・前は「ユーザー名も、ほかの人には使えなくなります」と書いていたが、**本人も使えない**ので誤りだった
                     ・「ユーザー名」という言葉は一般の人には分からないので、**その人の実際の値を括弧で出す**
                     ・「（同じメールアドレスで、別の名前で登録し直せます）」は外した（退会しようとしている人に要らない） --%>
                <p class="account-hint account-hint--danger">
                    退会すると、<strong>記事・画像・プロフィールがすべて消え、元に戻せません。</strong>
                    このアカウントID（<%: UserNameText %>）は、あなたも含めて二度と使えなくなります。
                </p>

                <%-- 【理由の要素は、欄より上】（名前（表示名）の区切りのコメント） --%>
                <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="RetireError" runat="server" /></p>

                <%-- パスワード管理ソフトへの目印（メールアドレスの区切りのコメント） --%>
                <input type="text" class="visually-hidden" tabindex="-1" aria-hidden="true" readonly="readonly"
                       autocomplete="username" value="<%: UserNameText %>" />

                <div class="form-row">
                    <asp:Label ID="RetirePasswordLabel" runat="server" AssociatedControlID="RetirePasswordBox"
                               CssClass="account-label" Text="今のパスワード" />
                    <asp:TextBox ID="RetirePasswordBox" runat="server" CssClass="account-input"
                                 TextMode="Password" MaxLength="128" autocomplete="current-password" />
                </div>

                <div class="account-actions">
                    <%-- 【data-confirm の文言は、site.js が textContent で入れる】
                         （HTML として出さないので、ここに書いた文字がそのまま読まれる） --%>
                    <%-- 【確認の窓は1行だけ】
                         何が消えるかは、すぐ上の説明にある。窓で同じことを繰り返さない。
                         【formnovalidate】押したとき、ブラウザにページ全体の欄を確かめさせない。
                         確かめるのは、この区切りの欄だけ（Scripts/site.js の「8」）。
                         それまでは、別の区切りの「新しいメールアドレス」の形で、退会の送信まで止められていた --%>
                    <asp:Button ID="RetireButton" runat="server" Text="退会する" formnovalidate="formnovalidate"
                                CssClass="account-submit account-submit--danger"
                                data-confirm="本当に退会しますか？（元に戻せません）"
                                data-confirm-ok="退会する" />
                </div>

            </asp:Panel>

        </div>

    </div>

</asp:Content>
