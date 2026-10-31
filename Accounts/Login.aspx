<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Login.aspx.vb" Inherits="vb_chat.AccountLoginPage" Title="ログイン" %>

<%-- 【ValidateRequest="false" にしてある】
     ASP.NET は既定で「<」のすぐ後ろに英字が来る入力（「<a」など）を、ページに届く前に 500 で止める。
     **パスワードは、パスワード管理ソフトが作ると「<」を普通に含む**。切らないと、そういうパスワードでは
     ログインも・作ることも・変えることもできない（開発機のログインの画面で 500 になるのを確かめた）。
     切ってよい理由＝この画面が画面へ出す値は、全部エスケープしている（<%: %>・HttpUtility.HtmlEncode・asp:TextBox の既定）。
     アカウントの画面の3つ（Login・Setup・Settings）で、そろえてある。**Web.config でサイト全体を切らないこと** --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）
         発行の前に、【発行前チェック】の grep を走らせること

         【この囲みの中に「--」＋「%」＋「>」と書かないこと】（実際に踏んだ）
         そこで囲みが閉じ、**残りが利用者のブラウザにそのまま出る**。
         【発行前チェック】に、この罠が書いてある

         【Validator（asp:RequiredFieldValidator など）は、あえて使っていない】
         非侵入型検証は "jquery" という名前の ScriptResourceMapping を要求するが、
         それを登録していたパッケージは宿題4で外してある。登録しないまま置くと、このページが500で止まる
         （「管理画面を作る回に、必ず戻ってくる場所」）。
         登録そのものは Application_Start の数行で済むが、
           ・本当の検査はコードビハインドにある（Validator の画面側は親切であって、防御ではない）
           ・そのためだけに、このページへ jQuery を1本増やすことになる
         ので、入れなかった。**記事を書く画面で、入力の多いフォームを作るときに、
         ScriptResourceMapping の登録ごと入れること。** --%>

    <div class="account-box">

        <%-- 【退会したあとの画面】（開発者が了承）
             見出し「退会しました」＋1行＋「トップページへ」だけ。**ログインの欄は出さない**
             （退会した人に「ログイン」を見せても、使えるアカウントがもう無い）。
             問い合わせの「送信完了」（Contacts/Contact.aspx）と同じ形＝同じ URL のまま、中身を差し替える。
             合図は ?retired=done（Accounts/Settings.aspx.vb が付ける。文言はこちらが持つ） --%>
        <asp:PlaceHolder ID="RetiredView" runat="server" Visible="false">
            <h1>退会しました</h1>
            <p class="account-note account-lead">ご利用ありがとうございました。</p>
            <p class="account-note"><a href="/">トップページへ</a></p>
        </asp:PlaceHolder>

        <asp:PlaceHolder ID="LoginView" runat="server">

        <h1>ログイン</h1>

        <%-- 断った理由。
             【空のときは高さを持たせない】（.account-error:empty）
             理由が出たときだけ行が生まれると、そのぶん下の入力欄が押し下げられて画面が跳ねる
             （部屋を作るエリアの .create-room-error と同じ考え方）。

             【role="alert"】ポストバックでページごと入れ替わるので、
             読み上げソフトは、新しいページを読むときにここを先に読む。
             aria-live は付けない（role="alert" が既にその意味を持っている） --%>
        <%-- パスワードを変えたあとの知らせ（?reset=done の合図から、こちらが文言を決める） --%>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <div class="form-row">
            <asp:Label ID="LoginIdLabel" runat="server" AssociatedControlID="LoginIdBox"
                       CssClass="account-label" Text="アカウントID<br />またはメール" />
            <%-- 【autocomplete】パスワード管理ソフトに、ここがログインの欄だと伝える。
                 【maxlength】長すぎる入力を、サーバーへ送る前に止める（サーバーでも断っている）。
                 【required】ブラウザが空のまま送るのを止めるだけの親切で、**防御ではない**
                 （開発者ツールから外せる。本当の検査はコードビハインドにある） --%>
            <asp:TextBox ID="LoginIdBox" runat="server" CssClass="account-input"
                         MaxLength="256" autocomplete="username" required="required" />
        </div>

        <div class="form-row">
            <asp:Label ID="PasswordLabel" runat="server" AssociatedControlID="PasswordBox"
                       CssClass="account-label" Text="パスワード" />
            <asp:TextBox ID="PasswordBox" runat="server" CssClass="account-input"
                         TextMode="Password" MaxLength="128"
                         autocomplete="current-password" required="required" />
        </div>

        <div class="account-actions">
            <asp:Button ID="SignInButton" runat="server" Text="ログイン" CssClass="account-submit" />
        </div>

        <%-- 「再設定は、まだ作っていません」から直した --%>
        <p class="account-note">
            <a href="<%: ForgotLinkUrl %>">パスワードを忘れたとき</a>
        </p>

        <%-- 新規登録への道。**足し忘れていたのを、発行後の確かめの道具が見つけた**
             （検査の行だけ先に書いて、リンクを書いていなかった）。
             右上のメニューからは行けていたが、**ログインの画面からは行けなかった**
             ——「ログインしようとしたら、まだ登録していなかった」人が、いちばん通る道である。
             【文言は Accounts/Register.aspx と対にしてある】あちらは「ログインはこちら」
             （に短くした） --%>
        <p class="account-note">
            <a href="<%: RegisterLinkUrl %>">新規登録はこちら</a>
        </p>

        </asp:PlaceHolder>

    </div>

</asp:Content>
