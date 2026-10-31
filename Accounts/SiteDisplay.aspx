<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="SiteDisplay.aspx.vb" Inherits="vb_chat.SiteDisplayPage" Title="表示" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【サイトの表示の設定】（/account/display。基本ルール T）
         右の枠の一覧に、新しいものから何件出すか。サイト管理者だけ（守りは SiteDisplay.aspx.vb の RequireAdministrator）。
         要望：出す件数を管理画面で変えられるようにする。
         値は DB の SiteSettings（Core/SiteSettings.vb の SaveListCounts）。保存したら、全部の控えを捨てる（CacheReset.ResetAll）＝すぐ反映 --%>

    <div class="admin-box">

        <h1>表示</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <h2 class="admin-subhead">右の枠に出す数</h2>

        <%-- 【数字の欄】全角・空白・数字でない値は、離れたときと送る直前に直る（Scripts/site.js の「16」・Core/NumberInput.vb） --%>
        <div class="form-row">
            <asp:Label ID="NoticeLabel" runat="server" AssociatedControlID="NoticeBox" CssClass="admin-label" Text="管理人からのお知らせ" />
            <asp:TextBox ID="NoticeBox" runat="server" CssClass="admin-num-input" MaxLength="2" inputmode="numeric"
                         data-number-field="1" data-number-min="1" data-number-max="20" data-number-default="5" />
            <span class="admin-unit">件</span>
        </div>

        <div class="form-row">
            <asp:Label ID="NewMemberLabel" runat="server" AssociatedControlID="NewMemberBox" CssClass="admin-label" Text="新しいメンバー" />
            <asp:TextBox ID="NewMemberBox" runat="server" CssClass="admin-num-input" MaxLength="2" inputmode="numeric"
                         data-number-field="1" data-number-min="1" data-number-max="20" data-number-default="5" />
            <span class="admin-unit">人</span>
        </div>

        <div class="form-row">
            <asp:Label ID="BlogRecentLabel" runat="server" AssociatedControlID="BlogRecentBox" CssClass="admin-label" Text="ブログの最新の記事" />
            <asp:TextBox ID="BlogRecentBox" runat="server" CssClass="admin-num-input" MaxLength="2" inputmode="numeric"
                         data-number-field="1" data-number-min="1" data-number-max="20" data-number-default="5" />
            <span class="admin-unit">件</span>
            <%= HelpButton("display.blogRecent") %>
        </div>

        <%-- 【区切りを終わらせるボタンは、右寄せ】 --%>
        <div class="admin-actions admin-actions--end">
            <a href="/account" class="admin-link-btn">ホームへ戻る</a>
            <asp:Button ID="SaveButton" runat="server" Text="保存する" CssClass="account-submit" />
        </div>

    </div>

</asp:Content>
