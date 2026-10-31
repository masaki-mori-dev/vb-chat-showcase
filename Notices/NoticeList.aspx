<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="NoticeList.aspx.vb" Inherits="vb_chat.SiteNoticeListPage" Title="管理人からのお知らせ" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- 【管理人からのお知らせを、全部並べるページ】（/news。基本ルール T）
         右の枠には新しいものから決まった数だけ出し（数は管理画面の「表示」で変える）、その下の「お知らせをすべて見る」でここへ来る。
         並びは右の枠と同じ新しい順、1件の形も同じ（Notices/SiteNotices.vb の ItemsHtml）。多ければページ送り（?p=2）。
         【URL を /notices にしなかった】Notices\ というフォルダが在るので、1区切りの「notices」は 403.14 になる（URL構成の罠） --%>

    <div class="notice-list-page">

        <h1>管理人からのお知らせ</h1>

        <%= ListHtml %>

        <% If HasPager Then %>
        <nav class="blog-pager" aria-label="ページ送り">
            <% If NewerPageUrl IsNot Nothing Then %><a class="blog-pager-link" href="<%: NewerPageUrl %>">前のページ</a><% End If %>
            <span class="blog-pager-now"><%: PageText %></span>
            <% If OlderPageUrl IsNot Nothing Then %><a class="blog-pager-link" href="<%: OlderPageUrl %>">次のページ</a><% End If %>
        </nav>
        <% End If %>

    </div>

</asp:Content>
