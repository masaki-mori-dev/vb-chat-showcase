<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Register.aspx.vb" Inherits="vb_chat.AccountRegisterPage" Title="新規登録" %>

<%-- 【ValidateRequest="false" にしてある】アカウントの画面はそろえてある（Login.aspx の先頭のコメント）。
     この画面はパスワードを受け取るので、切らないと「<」を含むパスワードで 500 になる。
     画面へ出すのは、こちらで決めた文言と、打たれた文字を asp:TextBox に戻すぶんだけ（どちらもエスケープされる） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）

         一般ユーザーの登録。判断は Accounts/AccountRegistration.vb の Begin。
         【この画面は、ログインしていない人が開く】AccountPageBase は継承しない。
         【ダブルオプトイン】ここで「登録する」を押しても、まだアカウントは作られない
         （確認のメールのリンクを押してはじめて作られる）。
         --%>

    <div class="account-box">

        <h1>新規登録</h1>

        <%-- 【上の説明文は置かない】（消した）
             「ブログを書けるようになる」は、右上のメニューから来た人には分かっている。
             確認のメールを送ることは、送ったあとの文（AccountRegistration の SentMessage）で伝わる --%>

        <%-- 受け付けたときの答え（そのアドレスが登録済みでも、同じ文言） --%>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="FormArea" runat="server">

            <div class="form-row">
                <asp:Label ID="UserNameLabel" runat="server" AssociatedControlID="UserNameBox"
                           CssClass="account-label" Text="アカウントID" />
                <%-- 【pattern・maxlength は、ブラウザに早く教えるための親切】
                     本当の検査はサーバー（AccountStore.ValidateUserName）にある。
                     【autocomplete="username"】パスワード管理ソフトに、ここが名前の欄だと伝える --%>
                <asp:TextBox ID="UserNameBox" runat="server" CssClass="account-input"
                             MaxLength="30" autocomplete="username" required="required"
                             pattern="[A-Za-z0-9_\-]{3,30}"
                             data-error-pattern="アカウントIDは、半角の英数字とハイフン・アンダースコアで、3〜30文字にしてください。" />
            </div>

            <%-- 【この1行だけは、どうしても要る】
                 「あとから変えられない」は、**押したあとでは取り返しがつかない**ことなので、
                 見ただけでは分からない。アカウントID の欄のすぐ下に置く。
                 【字の種類・字数は書かない】外れたときの断る理由で伝わる --%>
            <p class="account-hint">
                このサイトであなたを表すIDです。チャットの @ID や、ブログのアドレスにも使います。あとから変えられません。
            </p>

            <div class="form-row">
                <asp:Label ID="DisplayNameLabel" runat="server" AssociatedControlID="DisplayNameBox"
                           CssClass="account-label" Text="名前（表示名）" />
                <%-- 【呼び名は「名前（表示名）」でそろえる】
                     設定の画面・プロフィールの画面と、同じ言葉にしてある --%>
                <asp:TextBox ID="DisplayNameBox" runat="server" CssClass="account-input"
                             MaxLength="50" autocomplete="nickname" required="required" />
            </div>

            <p class="account-hint">ブログやプロフィールに出る名前です。あとから変えられます。</p>

            <div class="form-row">
                <asp:Label ID="EmailLabel" runat="server" AssociatedControlID="EmailBox"
                           CssClass="account-label" Text="メールアドレス" />
                <asp:TextBox ID="EmailBox" runat="server" CssClass="account-input" TextMode="Email"
                             MaxLength="256" autocomplete="email" required="required" />
            </div>

            <div class="form-row">
                <asp:Label ID="PasswordLabel" runat="server" AssociatedControlID="PasswordBox"
                           CssClass="account-label" Text="パスワード" />
                <asp:TextBox ID="PasswordBox" runat="server" CssClass="account-input"
                             TextMode="Password" MaxLength="128"
                             autocomplete="new-password" required="required" />
            </div>

            <%-- 【何文字以上かは、コードから出す】（AccountPassword.MinLength）
                 画面に数字を書くと、決まりを変えた日にここだけ古いまま残る --%>
            <p class="account-hint"><asp:Literal ID="PasswordHintText" runat="server" /></p>

            <div class="form-row">
                <asp:Label ID="ConfirmLabel" runat="server" AssociatedControlID="ConfirmBox"
                           CssClass="account-label" Text="パスワード<br />（確認）" />
                <asp:TextBox ID="ConfirmBox" runat="server" CssClass="account-input"
                             TextMode="Password" MaxLength="128"
                             autocomplete="new-password" required="required" />
            </div>

            <%-- 【利用規約への同意】
                 【リンクを label の中に入れないこと】（開発機で読み上げの木を見て直した）
                 最初は `<label><a>利用規約</a>に同意します</label>` と書いていたが、
                   ・チェックボックスの名前が **「に同意します」** になった（「利用規約」が落ちた）
                   ・label の中のリンクを押すと、チェックも一緒に切り替わる
                 という2つの困りごとが起きる。
                 **label は素の文だけにして、リンクはその外へ出してある。**

                 【リンクは、記事の「URL の名前」（terms）から組み立てる】（Articles/SiteArticles.vb）
                 その名前の記事が公開されていなければ、リンクごと出ない
                 （押しても「見つかりません」になるリンクを置かない、という決まり）。
                 **そのときも「利用規約に同意します」の文は残る**——同意そのものは、記事が在るかと関係ない --%>
            <div class="account-terms">
                <asp:CheckBox ID="AgreeBox" runat="server" CssClass="account-terms-check" />
                <asp:Label ID="AgreeLabel" runat="server" AssociatedControlID="AgreeBox"
                           CssClass="account-terms-label" Text="利用規約に同意します" />
                <span class="account-terms-link"><asp:Literal ID="TermsLinkText" runat="server" /></span>
            </div>

            <%-- 【人には見えない入力欄（ロボット避け）】
                 問い合わせページ（Contacts/Contact.aspx）と、まったく同じ形・同じ理由。
                 ここに何か入っていたら、その1件は「送りました」と出したうえで黙って捨てる。
                 **欄の名前を「email」「name」のような、自動入力が狙う名前にしないこと**
                 （狙われると、ふつうの利用者の1件が黙って捨てられる）。
                 **reCAPTCHA は使わない** --%>
            <div class="contact-trap" aria-hidden="true">
                <asp:TextBox ID="TrapBox" runat="server" TabIndex="-1" autocomplete="off" />
            </div>

            <%-- 【区切りを終わらせるボタンは、右寄せ】 --%>
            <div class="account-actions">
                <asp:Button ID="RegisterButton" runat="server" Text="登録する" CssClass="account-submit" />
            </div>

        </asp:PlaceHolder>

        <%-- 【文言は Accounts/Login.aspx と対にしてある】あちらは「新規登録はこちら」 --%>
        <p class="account-note">
            <a href="<%: LoginLinkUrl %>">ログインはこちら</a>
        </p>

    </div>

</asp:Content>
