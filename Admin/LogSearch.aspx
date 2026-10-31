<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="LogSearch.aspx.vb" Inherits="vb_chat.LogSearchPage" Title="ログを調べる" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         ログを調べる道具の、探す画面（/account/logs。サイト管理者だけ＝守りは Admin/LogToolPageBase.vb）。
         通報や警察からの依頼があったとき、該当する記録をすぐ探すための画面。

         【Gmail の検索の真似】（基本ルール Z）
         上に検索の箱が1つ。言葉を入れたら本文と名前から探す。箱の横の「▼」で詳しい条件が開く。
         何も付けずに開いたときは、期間に「直近24時間」が入っている。
         結果は新しい順。1行押すと、その発言を真ん中にして再生の画面（/account/logs/view）へ。

         【探した結果は GET の住所】［探す］はいったんサーバーへ送り、条件を URL に入れて開き直す（PRG）。
         戻る・ブックマーク・「この人のほかの記録」のリンクが、同じ形の住所で効く。

         【探すたびに、見た記録を残す】（ChatLogAudit。書けなかったら結果を出さない）

         【行の中身は、コードビハインドが組み立てる】（Admin/LogToolView.vb。値は1つずつエスケープしてある） --%>

    <link href="<%: Asset("~/Content/admin-logs.css") %>" rel="stylesheet" type="text/css" />

    <div class="admin-box logtool">

        <%-- 【切り替え】探す｜見た記録（Admin/LogToolView.vb の TabsHtml） --%>
        <%= ToolTabsHtml %>

        <h1>ログを調べる</h1>

        <%-- 検索の箱。Enter で［探す］（DefaultButton） --%>
        <asp:Panel ID="SearchPanel" runat="server" DefaultButton="SearchButton" CssClass="logsearch-form">

            <div class="logsearch-bar" role="search">
                <asp:TextBox ID="QueryBox" runat="server" CssClass="account-input logsearch-q" MaxLength="100"
                             placeholder="発言・名前で探す" aria-label="探す言葉（発言・名前）" />
                <button type="button" class="logsearch-toggle" id="logsearchToggle"
                        aria-expanded="<%= If(DetailOpen, "true", "false") %>" aria-controls="logsearchDetail">
                    <span aria-hidden="true">▼</span><span class="visually-hidden">詳しい条件</span>
                </button>
                <asp:Button ID="SearchButton" runat="server" Text="探す" CssClass="account-submit logsearch-go" />
            </div>

            <%-- 詳しい条件（▼）。条件が1つでも入っていれば、開いた形で出す --%>
            <div class="logsearch-detail" id="logsearchDetail"<%= If(DetailOpen, "", " hidden=""hidden""") %>>

                <div class="form-row">
                    <asp:Label ID="FromLabel" runat="server" AssociatedControlID="FromBox" CssClass="admin-label" Text="期間" />
                    <span class="logsearch-period">
                        <asp:TextBox ID="FromBox" runat="server" TextMode="DateTimeLocal" CssClass="account-input logsearch-date" aria-label="いつから" />
                        <span aria-hidden="true">〜</span>
                        <asp:TextBox ID="ToBox" runat="server" TextMode="DateTimeLocal" CssClass="account-input logsearch-date" aria-label="いつまで" />
                    </span>
                </div>

                <div class="form-row">
                    <asp:Label ID="RoomLabel" runat="server" AssociatedControlID="RoomList" CssClass="admin-label" Text="部屋" />
                    <asp:DropDownList ID="RoomList" runat="server" CssClass="admin-select logsearch-room-select" />
                </div>

                <div class="form-row">
                    <asp:Label ID="NameLabel" runat="server" AssociatedControlID="NameBox" CssClass="admin-label" Text="名前" />
                    <asp:TextBox ID="NameBox" runat="server" CssClass="account-input" MaxLength="100" />
                </div>

                <div class="form-row">
                    <asp:Label ID="TripLabel" runat="server" AssociatedControlID="TripBox" CssClass="admin-label" Text="トリップ" />
                    <asp:TextBox ID="TripBox" runat="server" CssClass="account-input logsearch-short" MaxLength="10" autocomplete="off" />
                </div>

                <div class="form-row">
                    <asp:Label ID="IpLabel" runat="server" AssociatedControlID="IpBox" CssClass="admin-label" Text="IP" />
                    <asp:TextBox ID="IpBox" runat="server" CssClass="account-input" MaxLength="45" autocomplete="off" placeholder="途中まででも探せます" />
                </div>

                <div class="form-row">
                    <asp:Label ID="AccountLabel" runat="server" AssociatedControlID="AccountBox" CssClass="admin-label" Text="アカウントID" />
                    <asp:TextBox ID="AccountBox" runat="server" CssClass="account-input logsearch-short" MaxLength="40" autocomplete="off" />
                </div>

                <%-- 種類（いくつでも）。最初は「発言」と「こっそり」だけに印。
                     チェックボックスは素の input（name="kinds"）。［探す］で送られた値を、コードビハインドが Request.Form から読む --%>
                <div class="form-row">
                    <span class="admin-label" id="logsearchKindsLabel">種類</span>
                    <span class="logsearch-kinds" role="group" aria-labelledby="logsearchKindsLabel"><%= KindChecksHtml %></span>
                </div>

                <%-- 「この人のほかの記録」で来たとき（ブラウザの印で絞っている）。外すと、ほかの条件のまま探し直す --%>
                <asp:PlaceHolder ID="SenderRow" runat="server" Visible="False">
                    <div class="form-row">
                        <span class="admin-label">ブラウザの印</span>
                        <span class="logsearch-chip">
                            <span class="logsearch-chip-text"><%: SenderText %></span>
                            <asp:LinkButton ID="ClearSenderButton" runat="server" CssClass="logsearch-chip-clear" aria-label="ブラウザの印の条件を外す">×</asp:LinkButton>
                        </span>
                    </div>
                </asp:PlaceHolder>

                <div class="admin-actions admin-actions--end">
                    <a href="<%: ResetUrl %>" class="admin-link-btn">条件を消す</a>
                </div>

            </div>

        </asp:Panel>

        <p class="account-error" role="alert"><asp:Literal ID="ErrorText" runat="server" /></p>

        <asp:PlaceHolder ID="ResultView" runat="server" Visible="False">

            <p class="logsearch-summary" role="status"><%: SummaryText %></p>

            <ol class="logsearch-list">
                <%= RowsHtml %>
            </ol>

            <%-- ページ送り（Gmail と同じ＝「新しい」「古い」） --%>
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

        <%-- DB が読めなかった・見た記録が書けなかった（理由は出さない。エラー報告メールで届く） --%>
        <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
            <p class="account-error" role="alert">ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </asp:PlaceHolder>

    </div>

</asp:Content>

<asp:Content ID="ScriptsContent" ContentPlaceHolderID="AdminScripts" runat="server">
    <script src="<%: Asset("~/Scripts/admin-logs.js") %>"></script>
</asp:Content>
