<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Verify.aspx.vb" Inherits="vb_chat.AccountVerifyPage" Title="登録の確認" %>

<%-- 【ValidateRequest="false" にしてある】アカウントの画面はそろえてある（Login.aspx の先頭のコメント） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         確認のメールのリンクの行き先（一般ユーザーの登録）。
         判断は Accounts/AccountRegistration.vb の CheckToken・Complete。

         【開いただけでは本登録しない。ボタン（POST）で本登録する】
         理由は2つ（AccountRegistration.CheckToken のコメント）。
           ① メールを配る途中の機械が、本人より先にリンクを開くことがある
           ② 何が起きるかを1枚見せてから確かめるほうが、親切
         Accounts/Reset.aspx が「リンクを開いたら、新しいパスワードの欄を出す」のと、同じ形である。

         【この画面は、ログインしていない人が開く】AccountPageBase は継承しない --%>

    <div class="account-box">

        <h1>登録の確認</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="FormArea" runat="server">

            <p class="account-note account-lead">
                次のアカウントIDで登録します。押すと登録が終わり、そのままログインします。
            </p>

            <%-- 【出すのは、こちらが DB から読んだ値だけ】（出口でエスケープする）
                 【dl にしてある】「項目名と値の組」が、読み上げソフトにそのまま伝わる
                 （公開のプロフィールの基本情報と、同じ形） --%>
            <dl class="account-verify-facts">
                <div>
                    <dt>アカウントID</dt>
                    <dd><asp:Literal ID="UserNameText" runat="server" /></dd>
                </div>
                <div>
                    <dt>名前（表示名）</dt>
                    <dd><asp:Literal ID="DisplayNameText" runat="server" /></dd>
                </div>
                <div>
                    <dt>メールアドレス</dt>
                    <dd><asp:Literal ID="EmailText" runat="server" /></dd>
                </div>
            </dl>

            <%-- 【区切りを終わらせるボタンは、右寄せ】 --%>
            <div class="account-actions">
                <asp:Button ID="VerifyButton" runat="server" Text="登録を終わらせる" CssClass="account-submit" />
            </div>

        </asp:PlaceHolder>

        <%-- リンクが使えなかったときだけ出す案内（もう一度はじめから登録してもらう） --%>
        <asp:PlaceHolder ID="RetryArea" runat="server" Visible="false">
            <p class="account-note">
                <a href="/account/register">もう一度登録する</a>
            </p>
        </asp:PlaceHolder>

    </div>

</asp:Content>
