<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="ArticleDetail.aspx.vb" Inherits="vb_chat.ArticleDetail" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- 記事のページ（/articles/{user}/{id}）。
         誰に見せるか・404 の出し方・転送は Articles/ArticleDetail.aspx.vb。

         【コメントは、必ずサーバー側コメントで書くこと】
         「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く。
         発行の前に、【発行前チェック】の grep を走らせること。

         【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】
         そこで囲みが閉じ、残りが利用者のブラウザにそのまま出る（踏んだ） --%>

    <asp:PlaceHolder ID="ArticleView" runat="server" Visible="False">

        <%-- 【article-page--measure】この箱ごと、1行の長さの上限（Content/Site.css の --article-measure）に収める。
             ブログのトップ・カテゴリの一覧・404 も .article-page を使うので、記事のページだけに付けてある --%>
        <article class="article-page article-page--measure">

            <%-- 【プレビューの帯】下書き・非公開の記事を、持ち主がログインして開いたときだけ出る。
                 ほかの人には、この記事は404になる（帯つきのページを見られるのは持ち主だけ）。
                 role="status"＝読み上げソフトにも、ページを開いたときに伝わる --%>
            <asp:PlaceHolder ID="PreviewBanner" runat="server" Visible="False">
                <div class="article-preview-banner" role="status">
                    <p><strong>プレビューです。</strong><%: PreviewText %></p>
                    <p><a href="<%: EditUrl %>">この記事を編集する</a></p>
                </div>
            </asp:PlaceHolder>

            <%-- 【<header> 要素にしないこと】Content/Site.css の「header { display: flex; … }」はサイトのヘッダーのための規則で、
                 要素の名前で当たる。<header> にすると、タイトルと署名の行が横に並んでしまう（開発機で踏んだ） --%>
            <div class="article-head">

                <%-- 【タイトル・表示名・カテゴリ名は本文ではない。出口でエスケープする（<%: %>）】 --%>
                <h1 class="article-title"><%: ArticleTitle %></h1>

                <p class="article-meta">
                    <%-- 書いた人とカテゴリを、リンクにした。
                         【ブログ型への作り替えの②】書いた人の行き先を、
                         アカウントのトップページから**公開のプロフィール**へ変えた --%>
                    <span class="article-meta-item"><span class="article-meta-label">書いた人</span><a href="<%: AuthorUrl %>"><%: AuthorName %></a></span>
                    <span class="article-meta-item"><span class="article-meta-label">公開した日</span><%= PublishedDateHtml %></span>
                    <%-- 【更新した日】右の枠の更新履歴が「最後に更新した日」なので、
                         記事の中に公開した日しか無いと、同じ記事の日付が2つに見えていた。
                         公開した日と同じ日のときは、この箱ごと出さない（コードビハインドが決める） --%>
                    <asp:PlaceHolder ID="UpdatedMeta" runat="server">
                        <span class="article-meta-item"><span class="article-meta-label">更新した日</span><%= UpdatedDateHtml %></span>
                    </asp:PlaceHolder>
                    <asp:PlaceHolder ID="CategoryMeta" runat="server">
                        <span class="article-meta-item"><span class="article-meta-label">カテゴリ</span><a href="<%: CategoryUrl %>"><%: CategoryName %></a></span>
                    </asp:PlaceHolder>
                </p>

            </div>

            <%-- 【本文だけは、生の HTML で出す】（<%= %>）
                 保存のときと表示の前の2回、HtmlSanitizer を通してある（Articles/ArticleHtml.vb の
                 Sanitize と SanitizeForDisplay）。**ここに、通していない値を出さないこと**。
                 見た目は Content/Site.css の「記事の本文の書式」（書く画面と同じ規則を .article-body にも当ててある） --%>
            <%-- 【class は BodyCssClass が決める】見出しの番号を出す記事だけ
                 `article-numbered` が増える（番号は CSS のカウンターで出す）。中身は決まった2つの文字列だけ --%>
            <div class="<%: BodyCssClass %>"><%= BodyHtml %></div>

            <%-- === 前の記事へ｜一覧｜次の記事へ（ブログ型への作り替えの④）===

                 【どの記事が前・次か】（ArticleStore.FindNeighbors）
                 カテゴリに入っている記事は、そのカテゴリの中だけで、カテゴリの並べ方どおり（指定した順なら番号の小さいほうが前。
                 新しい順・古い順のカテゴリは、前＝ひとつ古い／次＝ひとつ新しい）。カテゴリに入っていない記事は、入っていない記事どうしで公開した順。
                 **ブログのトップのページ送りとは向きが逆**（あちらは「次のページ」で過去へ進む）。
                 どちらもその場所での決まりどおりなので、そろえないこと。

                 【真ん中の「一覧」は、いつも出す】前も次も無い記事（1本しか無いブログ）でも、
                 ブログのトップへ戻る道は要る。
                 【nav に名前を付ける】どのナビゲーションか、読み上げで分かるように。
                 【<%: %> でエスケープする】タイトルは利用者が決めた文字である --%>
            <nav class="article-nav" aria-label="記事の移動">

                <div class="article-nav-side article-nav-side--prev">
                    <asp:PlaceHolder ID="PrevLink" runat="server" Visible="False">
                        <a href="<%: PrevUrl %>">
                            <span class="article-nav-label">前の記事</span>
                            <span class="article-nav-title"><%: PrevTitle %></span>
                        </a>
                    </asp:PlaceHolder>
                </div>

                <%-- 【「一覧」→「ブログのトップ」】（行き先は今のまま） --%>
                <div class="article-nav-center">
                    <a href="<%: BlogUrl %>">ブログのトップ</a>
                </div>

                <div class="article-nav-side article-nav-side--next">
                    <asp:PlaceHolder ID="NextLink" runat="server" Visible="False">
                        <a href="<%: NextUrl %>">
                            <span class="article-nav-label">次の記事</span>
                            <span class="article-nav-title"><%: NextTitle %></span>
                        </a>
                    </asp:PlaceHolder>
                </div>

            </nav>

        </article>

    </asp:PlaceHolder>

    <%-- 【見つからないとき】「無い記事」と「在るけど見せない記事（下書き・非公開・持ち主でない）」は、
         まったく同じこの画面・同じ404にする。分けると、その番号の記事が在るかを外から確かめられる --%>
    <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
        <div class="article-page article-not-found">
            <h1 class="article-title">記事が見つかりません</h1>
            <p>URL が間違っているか、記事が削除された・公開をやめた可能性があります。</p>
            <p><a href="/">トップページへ</a></p>
        </div>
    </asp:PlaceHolder>

    <%-- 【DB が読めなかったとき】理由は利用者に見せない（エラー報告メールで開発者に届く） --%>
    <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
        <div class="article-page article-not-found">
            <h1 class="article-title">記事を表示できませんでした</h1>
            <p>ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </div>
    </asp:PlaceHolder>

</asp:Content>

<%-- 【目次の道具】。PC の右の枠の「ついてくる目次」と、スマホの浮かぶ「目次」ボタン。
     中身は Scripts/article-toc.js が、**出来上がった本文の見出しから**組み立てる
     （サーバーが付けた id を、そのまま使う＝作る場所を2つに増やさない）。

     【目次を入れる記事のときだけ読み込む】（コードビハインドが Visible を決める）。
     読み込まれなくても、本文の頭の目次はそのまま使える --%>
<asp:Content ID="ArticleScripts" ContentPlaceHolderID="ScriptsContent" runat="server">

    <asp:PlaceHolder ID="TocScript" runat="server" Visible="False">
        <script src="<%: Asset("~/Scripts/article-toc.js") %>"></script>
    </asp:PlaceHolder>

</asp:Content>
