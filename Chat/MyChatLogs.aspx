<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="MyChatLogs.aspx.vb" Inherits="vb_chat.MyChatLogsPage" Title="チャット履歴" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         発行の前に、【発行前チェック】の grep を走らせること。

         自分が参加したチャット履歴の一覧（チャットとアカウントの紐づけ）。URL は /account/chatlogs。

         【見られるのは、自分のぶんだけ】
         AccountPageBase がログインを確かめ、読み出し（Chat/AccountChatLog.vb）が、アカウントの番号で絞る。
         画面から番号を受け取る口は、ページ番号（?p=）だけである。

         【1行＝1回の参加】（部屋に入ってから、出るまで）。押すと、その間のログが開く。
         出るのは、**アカウントID を表示して入ったとき**のぶんだけ（名乗らずに入ったときは、結び付けていない）。

         【置き場所が Chat\ なのは、読むのがチャットのログだから】URL（/account/…）と置き場所は、一致していなくてよい
         （記事の管理画面が Articles\ に在るのと同じ）。

         【並べ方は中身任せ】部屋の名前・時間・印は flex-wrap で、入るなら横・入らなければ折り返す（幅の数字を書いていない） --%>

    <div class="admin-box">

        <h1>チャット履歴</h1>

        <p class="account-error" role="alert"><asp:Literal ID="ErrorText" runat="server" /></p>

        <p class="admin-note">
            アカウントIDを紐づけて入った部屋の、居た間のログです。<%: RetentionText %>たつと消えます。
        </p>

        <asp:PlaceHolder ID="EmptyView" runat="server" Visible="False">
            <p class="admin-note">まだ履歴はありません。</p>
        </asp:PlaceHolder>

        <%-- 【Repeater】1回の参加を1行ずつ。値は全部 <%#: %>（エスケープ付き）で出す。
             部屋の名前は利用者が決めた文字なので、ここでエスケープしないと保存型の XSS になる --%>
        <asp:Repeater ID="SessionList" runat="server">
            <HeaderTemplate>
                <ul class="chatlog-sessions">
            </HeaderTemplate>
            <ItemTemplate>
                <li>
                    <a class="chatlog-session-room" href="<%#: ViewUrlOf(Container.DataItem) %>"><%#: RoomNameOf(Container.DataItem) %></a>
                    <span class="chatlog-session-time"><%#: TimeRangeOf(Container.DataItem) %></span>
                    <asp:PlaceHolder runat="server" Visible="<%# IsStillIn(Container.DataItem) %>">
                        <span class="status-mark status-mark--on">入室中</span>
                    </asp:PlaceHolder>
                </li>
            </ItemTemplate>
            <FooterTemplate>
                </ul>
            </FooterTemplate>
        </asp:Repeater>

        <%-- ページ送り（1ページに収まるときは、何も出さない）。
             「前のページ」＝新しいほう、「次のページ」＝古いほう（ブログのトップと同じ向き） --%>
        <asp:PlaceHolder ID="PagerSection" runat="server" Visible="False">
            <nav class="blog-pager" aria-label="ページ送り">
                <asp:PlaceHolder ID="PagerNewer" runat="server" Visible="False">
                    <a class="blog-pager-link" href="<%: NewerPageUrl %>">前のページ</a>
                </asp:PlaceHolder>
                <span class="blog-pager-now"><%: PageText %></span>
                <asp:PlaceHolder ID="PagerOlder" runat="server" Visible="False">
                    <a class="blog-pager-link" href="<%: OlderPageUrl %>">次のページ</a>
                </asp:PlaceHolder>
            </nav>
        </asp:PlaceHolder>

    </div>

</asp:Content>
