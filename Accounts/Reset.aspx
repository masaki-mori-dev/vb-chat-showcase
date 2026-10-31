<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Reset.aspx.vb" Inherits="vb_chat.AccountResetPage" Title="新しいパスワードを決める" %>

<%-- 【ValidateRequest="false" にしてある】パスワードは「<」を普通に含む（Login.aspx の先頭のコメント）。
     この画面が画面へ出すのは、こちらで決めた文言だけ（HttpUtility.HtmlEncode を通す） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         メールの再設定のリンクの行き先。判断は Accounts/AccountReset.vb。
         【合言葉は URL に残さない】リンク（?t=合言葉）を開いたら、合言葉を HttpOnly の Cookie へ移し、
         この画面へ 302 で移り直している（アドレスバー・閲覧の履歴・Referer に残らない）。
         【検索エンジンに載せない・キャッシュさせない】X-Robots-Tag・no-store・Referrer-Policy（コードビハインド） --%>

    <div class="account-box">

        <h1>新しいパスワードを決める</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="FormArea" runat="server">

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
                12文字以上・128文字以内。変えると、ほかの端末のログインは全部切れます。
            </p>

            <div class="account-actions">
                <asp:Button ID="SaveButton" runat="server" Text="パスワードを変える" CssClass="account-submit" />
            </div>

        </asp:PlaceHolder>

        <asp:PlaceHolder ID="AgainArea" runat="server" Visible="false">
            <p class="account-note">
                <a href="/account/forgot">リンクを送り直す</a>
            </p>
        </asp:PlaceHolder>

    </div>

</asp:Content>
