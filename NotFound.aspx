<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="NotFound.aspx.vb" Inherits="vb_chat.NotFoundPage" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- 「ページが見つかりません」（住所のルール）。
         無い住所・実物の住所（.aspx）で開かれて正しい住所が決められないページ・中身を見せないフォルダの住所のときに、
         本番の IIS が httpErrors（Web.Release.config）でこのページを中で開く。状態の番号は 404 のまま（NotFound.aspx.vb）。
         記事・プロフィール・カテゴリの「見つかりません」は、それぞれのページが自分で出している（同じ見た目にそろえてある）。

         【コメントは、必ずサーバー側コメントで書くこと】【<header> 要素を使わないこと】Site.css の header の規則が当たる --%>

    <div class="article-page article-not-found">
        <h1 class="article-title">ページが見つかりません</h1>
        <p>URL が間違っているか、このページは今はありません。</p>
        <p><a href="/">トップページへ</a></p>
    </div>

</asp:Content>
