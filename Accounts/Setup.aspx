<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Setup.aspx.vb" Inherits="vb_chat.AccountSetupPage" Title="最初のサイト管理者を作る" %>

<%-- 【ValidateRequest="false" にしてある】
     ASP.NET は既定で「<」のすぐ後ろに英字が来る入力（「<a」など）を、ページに届く前に 500 で止める。
     **パスワードは、パスワード管理ソフトが作ると「<」を普通に含む**。切らないと、そういうパスワードでは
     ログインも・作ることも・変えることもできない（開発機のログインの画面で 500 になるのを確かめた）。
     切ってよい理由＝この画面が画面へ出す値は、全部エスケープしている（<%: %>・HttpUtility.HtmlEncode・asp:TextBox の既定）。
     アカウントの画面の3つ（Login・Setup・Settings）で、そろえてある。**Web.config でサイト全体を切らないこと** --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）

         【この画面は、ふだんは404である】
         開くのは「アカウントが1つも無い」ことと「App_Data\account-setup.txt の合言葉が合う」ことの
         **両方**がそろっている間だけ。片方でも欠けたら、存在しないページとまったく同じ404を返す。
         Admin/MailTest.aspx とまったく同じ形である（コードビハインドの IsGateOpen）。 --%>

    <div class="account-box">

        <h1>最初のサイト管理者を作る</h1>

        <p class="account-note account-lead">
            この画面は、アカウントが1つも無いときにしか開きません。
            作り終えたら、<strong>App_Data\account-setup.txt を FTP で消してください。</strong>
        </p>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <div class="form-row">
            <asp:Label ID="UserNameLabel" runat="server" AssociatedControlID="UserNameBox"
                       CssClass="account-label" Text="アカウントID" />
            <asp:TextBox ID="UserNameBox" runat="server" CssClass="account-input"
                         MaxLength="30" autocomplete="username" required="required" />
        </div>
        <p class="account-hint">
            半角の英数字とハイフン・アンダースコアで、3〜30文字。「@」は使えません。
            <strong>記事のURL（/articles/アカウントID/番号）に、そのまま出ます。</strong>
        </p>

        <div class="form-row">
            <asp:Label ID="DisplayNameLabel" runat="server" AssociatedControlID="DisplayNameBox"
                       CssClass="account-label" Text="名前<br />（表示名）" />
            <asp:TextBox ID="DisplayNameBox" runat="server" CssClass="account-input" MaxLength="50" />
        </div>
        <p class="account-hint">
            記事に出す名前です（日本語でよい）。空にすると、アカウントIDと同じになります。
        </p>

        <div class="form-row">
            <asp:Label ID="EmailLabel" runat="server" AssociatedControlID="EmailBox"
                       CssClass="account-label" Text="メール" />
            <asp:TextBox ID="EmailBox" runat="server" CssClass="account-input"
                         TextMode="Email" MaxLength="256" autocomplete="email" required="required" />
        </div>
        <p class="account-hint">
            パスワードを忘れたときの再設定のリンクが、ここへ届きます。
        </p>

        <div class="form-row">
            <asp:Label ID="PasswordLabel" runat="server" AssociatedControlID="PasswordBox"
                       CssClass="account-label" Text="パスワード" />
            <asp:TextBox ID="PasswordBox" runat="server" CssClass="account-input"
                         TextMode="Password" MaxLength="128"
                         autocomplete="new-password" required="required" />
        </div>

        <div class="form-row">
            <asp:Label ID="PasswordConfirmLabel" runat="server" AssociatedControlID="PasswordConfirmBox"
                       CssClass="account-label" Text="パスワード<br />（確認）" />
            <asp:TextBox ID="PasswordConfirmBox" runat="server" CssClass="account-input"
                         TextMode="Password" MaxLength="128"
                         autocomplete="new-password" required="required" />
        </div>
        <p class="account-hint">
            12文字以上・128文字以内。ほかで使っているパスワードは使わないでください。
        </p>

        <div class="account-actions">
            <asp:Button ID="CreateButton" runat="server" Text="サイト管理者を作る" CssClass="account-submit" />
        </div>

    </div>

</asp:Content>
