<%@ Page Language="vb" MasterPageFile="~/Site.Master" AutoEventWireup="false" CodeBehind="PublicProfile.aspx.vb" Inherits="vb_chat.PublicProfilePage" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <%-- 公開のプロフィールページ（/profile/{ユーザー名}）。ブログ型への作り替えの②。

         【コメントは、必ずサーバー側コメントで書くこと】発行の前に 【発行前チェック】の grep を。
         【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】そこで囲みが閉じる。
         【<header> 要素を使わないこと】Site.css の header の規則（display: flex）が要素の名前で当たり、
         中身が横に並んでしまう（記事のページで踏んだ）。

         【非公開にした項目と、空の項目は、ここに1文字も出ない】
         CSS で隠すのではなく、**そもそも組み立てない**（コードビハインドが PlaceHolder の Visible と、
         〜Html の中身を決める）。F12 でページのソースを開いても出てこない＝それが「非公開」の意味である。

         【生の HTML を出すのは、名前が「〜Html」のものだけ】それ以外は必ず <%: %> でエスケープする。
         〜Html の中身は、コードビハインドがエスケープしてから組み立てている --%>

    <asp:PlaceHolder ID="ProfileView" runat="server" Visible="False">

        <div class="profile-page">

            <%-- カバー画像。無ければ、この箱ごと出さない --%>
            <asp:PlaceHolder ID="CoverBlock" runat="server" Visible="False">
                <div class="profile-cover">
                    <%-- 【alt=""】飾りの画像なので、読み上げソフトには読ませない
                         （説明できる中身が無く、読ませると名前の前に雑音が入る） --%>
                    <img class="profile-cover-image" src="<%: CoverSrc %>" alt="" />
                </div>
            </asp:PlaceHolder>

            <div class="profile-head">

                <%-- アイコン。**写真が無いときは「色の付いた丸に1文字」**（前は灰色の丸）。
                     組み立ては共通の部品（Accounts/AccountAvatar.vb）。中身はコードビハインドの AvatarHtml（エスケープ済み） --%>
                <%= AvatarHtml %>

                <div class="profile-head-text">

                    <h1 class="profile-head-name"><%: DisplayName %></h1>

                    <%-- ブログへの入口。ブログ名が空のときは「〇〇の記事」になる（コードビハインドが決める）。
                         **同じサイトの中なので、同じタブで開く**（記事の中のリンクと同じ決まり） --%>
                    <p class="profile-head-blog"><a href="<%: BlogUrl %>"><%: BlogTitle %></a></p>

                </div>

            </div>

            <asp:PlaceHolder ID="IntroSection" runat="server" Visible="False">
                <section class="profile-section" aria-labelledby="profileIntroHeading">
                    <h2 id="profileIntroHeading" class="profile-section-title">自己紹介</h2>
                    <%-- 【自己紹介はプレーンテキスト】エスケープしてから、改行だけを br に直してある
                         （組み立てるのは PublicProfile.aspx.vb の BuildIntroductionHtml の1か所） --%>
                    <p class="profile-intro"><%= IntroductionHtml %></p>
                </section>
            </asp:PlaceHolder>

            <asp:PlaceHolder ID="FactsSection" runat="server" Visible="False">
                <section class="profile-section" aria-labelledby="profileFactsHeading">
                    <h2 id="profileFactsHeading" class="profile-section-title">基本情報</h2>
                    <%-- 【dl にしてある】「項目名と値の組」が、読み上げソフトにそのまま伝わる形だから。
                         1組ずつ div で包んであるのは、並べ方（横に並ぶか、折り返すか）を組ごとに決めるため --%>
                    <dl class="profile-facts"><%= FactsHtml %></dl>
                </section>
            </asp:PlaceHolder>

            <asp:PlaceHolder ID="SnsSection" runat="server" Visible="False">
                <section class="profile-section" aria-labelledby="profileSnsHeading">
                    <h2 id="profileSnsHeading" class="profile-section-title">SNS</h2>
                    <ul class="profile-sns"><%= SnsHtml %></ul>
                </section>
            </asp:PlaceHolder>

        </div>

    </asp:PlaceHolder>

    <%-- 【見つからないとき】無いユーザー名も、止めているアカウントも、まったく同じこの画面・同じ404にする
         （分けると、そのユーザー名が在るかを外から確かめられる。記事のページと同じ決まり） --%>
    <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
        <div class="profile-page profile-not-found">
            <h1 class="profile-head-name">ページが見つかりません</h1>
            <p>URL が間違っている可能性があります。</p>
            <p><a href="/">トップページへ</a></p>
        </div>
    </asp:PlaceHolder>

    <%-- 【DB が読めなかったとき】理由は利用者に見せない（エラー報告メールで開発者に届く） --%>
    <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
        <div class="profile-page profile-not-found">
            <h1 class="profile-head-name">表示できませんでした</h1>
            <p>ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </div>
    </asp:PlaceHolder>

</asp:Content>
