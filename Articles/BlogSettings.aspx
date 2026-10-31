<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="BlogSettings.aspx.vb" Inherits="vb_chat.BlogSettingsPage" Title="ブログの設定" %>

<%-- 【ブログの設定】（URL は /account/blog/settings）
     ブログのタイトルと説明を、プロフィールの画面からここへ移した。DB の列（AccountProfiles.BlogTitle・BlogDescription）はそのまま。
     保存は AccountProfileStore.SaveBlog（この2列だけを書く。プロフィールの保存はこの2列に触らない）。

     【ValidateRequest="false"】説明は自由に書く欄で、タグの話を書く人もいる（「<」のすぐ後ろに英字が来ると、ページに届く前に 500）。
     切ってよい理由＝この画面が出す値は全部エスケープしている（asp:TextBox の既定）。生の HTML を出す場所は無い。
     **Web.config でサイト全体を切らないこと**（切ったページは、これで10枚目）

     【説明は「送る直前に包む」】本番の WAF が、タグを含む POST を 403 で止めるため（Scripts/site.js の「7」・Core/BodyTransport.vb。
     プロフィールの自己紹介と同じ形）。タイトルは短い欄なので包んでいない（記事のタイトルと同じ線引き） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <div class="admin-box">

        <%= BlogTabsHtml %>

        <h1>ブログの設定</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <asp:HiddenField ID="DescriptionEncoded" runat="server" />

        <asp:Panel ID="SettingsPanel" runat="server" DefaultButton="SaveButton">

            <%-- 【form-row--wide】タイトルは長い中身の欄なので、項目名の横に 13em 取れない幅では欄が次の行へ回る
                 （Content/Site.css の「入るなら横・入らなければ、その欄だけ積む」） --%>
            <div class="form-row form-row--wide">
                <asp:Label ID="TitleLabel" runat="server" AssociatedControlID="TitleBox"
                           CssClass="admin-label" Text="タイトル" />
                <div class="form-row-main" id="blogTitleMain">
                    <asp:TextBox ID="TitleBox" runat="server" CssClass="account-input" MaxLength="60"
                                 aria-describedby="blogTitleHint" data-error-anchor="blogTitleMain" />
                    <div class="fit-hint fit-hint--show"><span class="fit-hint-text" id="blogTitleHint">ブログのトップの見出しです。空欄なら「<%: DefaultTitleText %>」になります。</span></div>
                </div>
            </div>

            <div class="form-row form-row--body">
                <asp:Label ID="DescriptionLabel" runat="server" AssociatedControlID="DescriptionBox"
                           CssClass="admin-label" Text="説明" />
                <%-- 【説明は項目名のすぐ後ろ】横いっぱいの欄なので、項目名の横に1行で出す。
                     欄の下には落とさない（Content/Site.css の .form-row--body > .fit-hint）。
                     たまにしか使わない画面なので「?」に畳まない（fit-hint--show。基本ルールB）。
                     項目名の横に入らなければ、項目名と欄のあいだの行に全文 --%>
                <div class="fit-hint fit-hint--show"><span class="fit-hint-text" id="blogDescriptionHint">ブログのトップの見出しの下に出ます。</span></div>
                <asp:TextBox ID="DescriptionBox" runat="server" CssClass="admin-body-input blog-description-input"
                             TextMode="MultiLine" Rows="4" EnableViewState="False" aria-describedby="blogDescriptionHint" />
            </div>

            <div class="account-actions blog-settings-actions">
                <asp:HyperLink ID="ViewLink" runat="server" CssClass="admin-link-btn" Target="_blank" rel="noopener"
                               Text="ブログを見る" aria-label="ブログを見る（新しいタブで開きます）" />
                <asp:Button ID="SaveButton" runat="server" Text="保存する" CssClass="account-submit" />
            </div>

        </asp:Panel>

    </div>

</asp:Content>
