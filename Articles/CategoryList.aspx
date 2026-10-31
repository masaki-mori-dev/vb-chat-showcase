<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="CategoryList.aspx.vb" Inherits="vb_chat.ArticleCategoryList" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- カテゴリの一覧（/articles/{user}/category/{番号}）。
         そのカテゴリの**公開の記事だけ**を、カテゴリの並べ方（指定した順／新しい順／古い順）で全部並べる。
         公開の記事が1本も無いカテゴリは404（下書きだけのカテゴリの名前を外に出さない）。

         【コメントは、必ずサーバー側コメントで書くこと】【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】
         【<header> 要素を使わないこと】Site.css の header の規則が当たる --%>

    <asp:PlaceHolder ID="CategoryView" runat="server" Visible="False">

        <div class="article-page article-category">

            <div class="article-head">
                <%-- 【カテゴリ名・表示名は本文ではない。出口でエスケープする】 --%>
                <h1 class="article-title"><%: CategoryName %></h1>
                <p class="article-meta">
                    <span class="article-meta-item"><span class="article-meta-label">書いた人</span><a href="<%: AuthorUrl %>"><%: AuthorName %></a></span>
                    <span class="article-meta-item"><span class="article-meta-label">記事</span><%: ArticleCount %>本</span>
                </p>
            </div>

            <%= ListHtml %>

        </div>

    </asp:PlaceHolder>

    <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
        <div class="article-page article-not-found">
            <h1 class="article-title">ページが見つかりません</h1>
            <p>URL が間違っているか、このカテゴリに公開している記事がありません。</p>
            <p><a href="/">トップページへ</a></p>
        </div>
    </asp:PlaceHolder>

    <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
        <div class="article-page article-not-found">
            <h1 class="article-title">表示できませんでした</h1>
            <p>ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </div>
    </asp:PlaceHolder>

</asp:Content>
