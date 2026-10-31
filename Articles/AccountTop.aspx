<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="AccountTop.aspx.vb" Inherits="vb_chat.ArticleAccountTop" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- ブログのトップ（/articles/{user}）。作り、**作り替えた**。

         【作り替える前】固定表示 → カテゴリごとに数本ずつ → そのほかの記事、という「目次のような」並びだった。
         【作り替えたあと】上に帯（アイコン・ブログ名・説明・投稿数）→ 新しい記事を**タイトル＋出だし**で数本 → ページ送り。
         **、本文ごと → タイトル＋出だしに変えた**（指摘：PC でも記事を全文出すのはやりすぎ）。
＝よくあるブログのトップと同じ形にする。
         **カテゴリは右の枠へ回した**（③）ので、ここからは消してある——同じものを2か所に出さない。
         **固定表示は、「おすすめの記事」の枠をやめて、一覧の先頭に「固定」の印で並べる形にした**
         （指摘：固定の記事はどのみちいちばん上に出るので、重複になる）。

         【コメントは、必ずサーバー側コメントで書くこと】発行の前に 【発行前チェック】の grep を。
         【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】そこで囲みが閉じる。
         【<header> 要素を使わないこと】Site.css の header の規則が当たる --%>

    <asp:PlaceHolder ID="TopView" runat="server" Visible="False">

        <div class="article-page article-account">

            <%-- === 上の帯（アイコン・ブログ名・説明・投稿数）===

                 【PC とスマホで、同じ markup を使っている】
は「スマホは上に帯」「PC はブログ名と説明」だったが、
                 **出す中身は同じ**なので、markup を1つにして並べ方だけ中身に決めさせている
                 （イントリンシック・デザイン。@media で中身を出し分けると、
                   隠したほうもブラウザが読み込む＝保留にした話と同じ形になる）。
                 幅があればアイコンの右に文字が並び、無ければアイコンの下へ回る --%>
            <div class="blog-band">

                <%-- 【アイコンは共通の部品】（Accounts/AccountAvatar.vb）
                     写真が無ければ「色の付いた丸に1文字」。alt は空（すぐ隣にブログ名が文字で出ている）。
                     中身はコードビハインドの AvatarHtml（エスケープ済み）。<%= %> で出す --%>
                <%= AvatarHtml %>

                <div class="blog-band-text">

                    <%-- 【ブログ名】プロフィールで決めていなければ「〇〇の記事」になる（コードビハインドが決める）。
                         **ここがページの <h1> である**（記事のページの <h1> は記事のタイトル） --%>
                    <h1 class="blog-band-title"><%: BlogTitle %></h1>

                    <asp:PlaceHolder ID="BandDescription" runat="server" Visible="False">
                        <%-- 【説明はプレーンテキスト】エスケープしてから、改行だけを br に直してある
                             （組み立てるのは AccountTop.aspx.vb の BuildDescriptionHtml の1か所） --%>
                        <p class="blog-band-desc"><%= BlogDescriptionHtml %></p>
                    </asp:PlaceHolder>

                    <p class="blog-band-meta">
                        <%-- 【投稿数】読み上げソフトにも意味が通る形で出す（数字だけにしない） --%>
                        <span class="blog-band-count">記事 <%: ArticleCountText %> 本</span>
                        <span class="blog-band-sep" aria-hidden="true">｜</span>
                        <a href="<%: ProfileUrl %>">プロフィールを見る</a>
                    </p>

                </div>

            </div>

            <%-- === 新しい記事（タイトル＋出だし）===

                 【出だしは、サーバーが本文から文字だけを抜いて短くしたもの】（ArticleHtml.ExcerptText）
                 **HTML ではないので、<%#: %> でエスケープして出す**（本文の生の HTML は、ここには1文字も来ない）。
                 本文も画像も送らないので、ページが軽い。

                 【固定の記事は、いちばん上に「固定」の印】（ArticleStore.GetPublishedPage が先頭に並べる）
                 印は文字で書いてある（色だけで伝えない）。読み上げソフトにもそのまま読まれる。

                 【見出しは h2、全体は ul ではなく article の並び】1本ずつが独立した記事の案内なので --%>
            <asp:Repeater ID="ArticleRepeater" runat="server">
                <ItemTemplate>
                    <article class="blog-entry">

                        <h2 class="blog-entry-title">
                            <asp:PlaceHolder runat="server" Visible='<%# CBool(Eval("IsPinned")) %>'>
                                <span class="blog-entry-pin">固定</span>
                            </asp:PlaceHolder>
                            <a href='<%#: Eval("Url") %>'><%#: Eval("Title") %></a>
                        </h2>

                        <p class="blog-entry-meta">
                            <span class="blog-entry-date"><%# Eval("DateHtml") %></span>
                            <asp:PlaceHolder runat="server" Visible='<%# Not String.IsNullOrEmpty(CStr(Eval("CategoryName"))) %>'>
                                <span class="blog-band-sep" aria-hidden="true">｜</span>
                                <a href='<%#: Eval("CategoryUrl") %>'><%#: Eval("CategoryName") %></a>
                            </asp:PlaceHolder>
                        </p>

                        <asp:PlaceHolder runat="server" Visible='<%# Not String.IsNullOrEmpty(CStr(Eval("Excerpt"))) %>'>
                            <p class="blog-entry-excerpt"><%#: Eval("Excerpt") %></p>
                        </asp:PlaceHolder>

                        <%-- 【読み上げ用の名前にタイトルを入れる】「続きを読む」だけが並ぶと、耳ではどの記事か分からない --%>
                        <p class="blog-entry-more"><a href='<%#: Eval("Url") %>' aria-label='<%#: "「" & CStr(Eval("Title")) & "」の続きを読む" %>'>続きを読む</a></p>

                    </article>
                </ItemTemplate>
            </asp:Repeater>

            <%-- === ページ送り ===

                 【nav に名前を付ける】どのナビゲーションか、読み上げで分かるように。
                 【前＝新しいほう、次＝古いほう】ブログのトップは新しい順なので、
                 「次のページ」は過去へ進む。**記事のページの「前の記事｜次の記事」とは向きが逆**
                 （あちらは「前＝古い」。どちらも日本のブログの決まりどおりで、混ぜないこと） --%>
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

            <asp:PlaceHolder ID="EmptyView" runat="server" Visible="False">
                <p class="article-empty">まだ公開している記事はありません。</p>
            </asp:PlaceHolder>

        </div>

    </asp:PlaceHolder>

    <%-- 無いアカウント・止めているアカウント --%>
    <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
        <div class="article-page article-not-found">
            <h1 class="article-title">ページが見つかりません</h1>
            <p>URL が間違っている可能性があります。</p>
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
