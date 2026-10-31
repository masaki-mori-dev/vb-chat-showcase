<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Recover.aspx.vb" Inherits="vb_chat.AccountRecoverPage" Title="サイト管理者のパスワードを作り直す" %>

<%-- 【ValidateRequest="false" にしてある】パスワードは「<」を普通に含む（Login.aspx の先頭のコメント）。
     この画面が画面へ出すのは、こちらで決めた文言だけ（HttpUtility.HtmlEncode を通す） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         非常口。メールが送れない日に、サイト管理者がパスワードを忘れたとき。
         【この画面は、ふだんは404である】App_Data\account-recover.txt（8文字以上）が置いてあって、
         ?key= がその中身と合う間だけ開く。最初の管理人を作る画面（Setup.aspx）とまったく同じ形。
         判断は Accounts/AccountReset.vb の Recover --%>

    <div class="account-box">

        <h1>サイト管理者のパスワードを作り直す</h1>

        <%-- 【いつも出す】消し忘れると、合言葉を知っている人がいつでもパスワードを作り直せる --%>
        <%-- 【作り直したあとは、この説明を出さない】（パスワードを忘れたときの画面と同じ形）
             結果の文が、同じこと（ファイルを消す）をもう言っている --%>
        <asp:PlaceHolder ID="LeadArea" runat="server">
        <p class="account-note account-lead">
            メールが送れない日のための非常口です。
            <strong>使い終わったら、App_Data\account-recover.txt を FTP で消してください。</strong>
        </p>
        </asp:PlaceHolder>

        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="FormArea" runat="server">

            <div class="form-row">
                <asp:Label ID="LoginIdLabel" runat="server" AssociatedControlID="LoginIdBox"
                           CssClass="account-label" Text="アカウントID<br />またはメール" />
                <asp:TextBox ID="LoginIdBox" runat="server" CssClass="account-input"
                             MaxLength="256" autocomplete="username" required="required" />
            </div>
            <p class="account-hint">
                作り直せるのは、サイト管理者のアカウントだけです。
            </p>

            <div class="form-row">
                <asp:Label ID="NewPasswordLabel" runat="server" AssociatedControlID="NewPasswordBox"
                           CssClass="account-label" Text="新しい<br />パスワード" />
                <asp:TextBox ID="NewPasswordBox" runat="server" CssClass="account-input"
                             TextMode="Password" MaxLength="128" autocomplete="new-password" required="required" />
            </div>

            <div class="form-row">
                <asp:Label ID="ConfirmPasswordLabel" runat="server" AssociatedControlID="ConfirmPasswordBox"
                           CssClass="account-label" Text="新しいパスワード<br />（確認）" />
                <asp:TextBox ID="ConfirmPasswordBox" runat="server" CssClass="account-input"
                             TextMode="Password" MaxLength="128" autocomplete="new-password" required="required" />
            </div>
            <p class="account-hint">
                12文字以上・128文字以内。作り直すと、ほかの端末のログインは全部切れます。
            </p>

            <div class="account-actions">
                <asp:Button ID="RecoverButton" runat="server" Text="パスワードを作り直す" CssClass="account-submit" />
            </div>

        </asp:PlaceHolder>

    </div>

</asp:Content>
