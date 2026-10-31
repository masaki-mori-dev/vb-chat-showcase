<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="LogAudit.aspx.vb" Inherits="vb_chat.LogAuditPage" Title="見た記録" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         ログを調べる道具の「見た記録」（/account/logs/audit。サイト管理者だけ＝守りは Admin/LogToolPageBase.vb）。
         管理人がログの道具で、探した・開いた・もっと前／後を読んだ・書き出したことの一覧（ChatLogAudit 表）、見た記録も残す。

         【新しい順・50件ずつ】期間と「何をした」で絞れる（探す画面と同じ「新しい／古い」の区切り）。条件は URL（GET）。［絞る］は PRG。
         【行を押すと、そのときの探す条件・再生の場所をもう一度開く】（ChatLogAudit.OpenPath）。開き直した先の画面が、また見た記録に1行書く。
         住所を残す前の行（4度目に書いた行）は押せない。
         【この画面を見たことは、記録しない】（の開発者の答え） --%>

    <link href="<%: Asset("~/Content/admin-logs.css") %>" rel="stylesheet" type="text/css" />

    <div class="admin-box logtool">

        <%= ToolTabsHtml %>

        <h1>見た記録</h1>

        <asp:Panel ID="FilterPanel" runat="server" DefaultButton="FilterButton" CssClass="logsearch-detail logaudit-filter">

            <div class="form-row">
                <asp:Label ID="FromLabel" runat="server" AssociatedControlID="FromBox" CssClass="admin-label" Text="期間" />
                <span class="logsearch-period">
                    <asp:TextBox ID="FromBox" runat="server" TextMode="DateTimeLocal" CssClass="account-input logsearch-date" aria-label="いつから" />
                    <span aria-hidden="true">〜</span>
                    <asp:TextBox ID="ToBox" runat="server" TextMode="DateTimeLocal" CssClass="account-input logsearch-date" aria-label="いつまで" />
                </span>
            </div>

            <%-- 「何をした」と［条件を消す］［絞る］を、入る限り1行に（基本ルール A）。
                 入らなければボタンの組だけが次の行へ回り、そこでも右に寄る --%>
            <div class="form-row logaudit-actrow">
                <asp:Label ID="ActionLabel" runat="server" AssociatedControlID="ActionList" CssClass="admin-label" Text="何をした" />
                <asp:DropDownList ID="ActionList" runat="server" CssClass="admin-select" />
                <span class="logaudit-buttons">
                    <a href="<%: ResetUrl %>" class="admin-link-btn">条件を消す</a>
                    <asp:Button ID="FilterButton" runat="server" Text="絞る" CssClass="account-submit" />
                </span>
            </div>

        </asp:Panel>

        <asp:PlaceHolder ID="ResultView" runat="server" Visible="False">

            <p class="logsearch-summary" role="status"><%: SummaryText %></p>

            <ol class="logsearch-list">
                <%= RowsHtml %>
            </ol>

            <asp:PlaceHolder ID="PagerSection" runat="server" Visible="False">
                <nav class="blog-pager" aria-label="ページ送り">
                    <asp:PlaceHolder ID="PagerNewer" runat="server" Visible="False">
                        <a class="blog-pager-link" href="<%: NewerPageUrl %>">新しい</a>
                    </asp:PlaceHolder>
                    <span class="blog-pager-now"><%: PageText %></span>
                    <asp:PlaceHolder ID="PagerOlder" runat="server" Visible="False">
                        <a class="blog-pager-link" href="<%: OlderPageUrl %>">古い</a>
                    </asp:PlaceHolder>
                </nav>
            </asp:PlaceHolder>

        </asp:PlaceHolder>

        <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
            <p class="account-error" role="alert">ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </asp:PlaceHolder>

    </div>

</asp:Content>
