<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Forgot.aspx.vb" Inherits="vb_chat.AccountForgotPage" Title="パスワードを忘れたとき" %>

<%-- 【ValidateRequest="false" にしてある】アカウントの画面はそろえてある（Login.aspx の先頭のコメント）。
     この画面が画面へ出すのは、こちらで決めた文言だけ（HttpUtility.HtmlEncode を通す） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）

         パスワードを忘れたとき。判断は Accounts/AccountReset.vb の RequestReset。
         【在っても無くても、同じ答え】アカウントを引くのは裏の作業で、画面は引く前に答えを返す。
         --%>

    <div class="account-box">

        <h1>パスワードを忘れたとき</h1>

        <%-- 【送ったあとは、この説明を出さない】（指摘：送ったあとも残っていて、くどい）
             送ったあと（?done=sent）に出すのは、結果の文と、ログインの画面へ戻るリンクだけ（Forgot.aspx.vb の Page_Load）。
             「これから何をするか」の説明は、もう済んだ人には要らない --%>
        <asp:PlaceHolder ID="LeadArea" runat="server">
        <p class="account-note account-lead">
            メールアドレスかアカウントIDを入れてください。新しいパスワードを決めるためのリンクを、メールで送ります。
        </p>
        </asp:PlaceHolder>

        <%-- 受け付けたときの答え（在っても無くても同じ文言） --%>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="FormArea" runat="server">

            <div class="form-row">
                <asp:Label ID="LoginIdLabel" runat="server" AssociatedControlID="LoginIdBox"
                           CssClass="account-label" Text="メールアドレス<br />かアカウントID" />
                <asp:TextBox ID="LoginIdBox" runat="server" CssClass="account-input"
                             MaxLength="256" autocomplete="username" required="required" />
            </div>

            <div class="account-actions">
                <asp:Button ID="SendButton" runat="server" Text="リンクを送る" CssClass="account-submit" />
            </div>

        </asp:PlaceHolder>

        <p class="account-note">
            <a href="<%: LoginLinkUrl %>">ログインの画面へ戻る</a>
        </p>

    </div>

</asp:Content>
