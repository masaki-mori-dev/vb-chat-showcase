<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="Manage.aspx.vb" Inherits="vb_chat.ArticleManagePage" Title="ブログの記事一覧" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く。
         発行の前に、【発行前チェック】の grep を走らせること。

         【この囲みの中に「-」を2つ並べて「%」「>」と続けないこと】
         そこで囲みが閉じ、残りが利用者のブラウザにそのまま出る（踏んだ） --%>

    <div class="admin-box">

        <%-- 【ブログの切り替え】記事一覧｜カテゴリ｜ブログの設定。書けないアカウントには出ない（Articles/BlogTabs.vb） --%>
        <%= BlogTabsHtml %>

        <%-- 【見出しと［新しい記事を書く］を同じ行に】見出しは左・ボタンは右寄せ。
             入らなければボタンが次の行へ回る（中身任せ。幅の数字は書いていない）。
             「ブログ：〇〇」の1行はやめた（公開のブログへは右上の角の「ブログを見る」がある。何の一覧かは見出しで分かる） --%>
        <div class="admin-heading-row admin-heading-row--action">
            <h1>ブログの記事一覧</h1>
            <asp:HyperLink ID="NewArticleLink" runat="server" CssClass="account-submit admin-new-btn"
                           NavigateUrl="/account/articles/edit" Text="新しい記事を書く" />
        </div>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- ============================================================
             【ゴミ箱】（作りは実装に一任）一覧の「削除」は、まずゴミ箱へ移す。
             30日たつと、1日1回の片付け（Core/LogCleanup.vb）が完全に消す（画像もそのとき）。
             ゴミ箱は、カテゴリのドロップダウンに混ぜず「ゴミ箱 (n)」として別に置く（/account/articles?view=trash）。
             決まりは Articles/ArticleStore.vb の「ゴミ箱」の節
             ============================================================ --%>
        <asp:PlaceHolder ID="TrashView" runat="server" Visible="False">

            <h2 class="admin-subhead">ゴミ箱</h2>
            <p class="admin-note">ゴミ箱の記事は、移してから <%: TrashKeepDays %> 日たつと完全に消えます。それまでは元に戻せます。</p>

            <asp:Repeater ID="TrashList" runat="server">
                <HeaderTemplate><ul class="admin-trash-list"></HeaderTemplate>
                <ItemTemplate>
                    <li class="admin-trash-item"<%# TrashErrorHereAttribute(Container.DataItem) %>>
                        <span class="admin-trash-title"><%#: TrashTitle(Container.DataItem) %></span>
                        <span class="admin-trash-days"><%#: DaysLeftText(Container.DataItem) %></span>
                        <span class="admin-trash-actions">
                            <%-- 【POST】削除と同じ理由（GET で戻す・消す口を作らない）。持ち主の突き合わせは ArticleStore（AccountId でも絞る） --%>
                            <asp:Button ID="RestoreButton" runat="server" Text="元に戻す"
                                        CssClass="admin-link-btn" CausesValidation="False"
                                        CommandName="RestoreArticle"
                                        CommandArgument='<%# Eval("ArticleId") %>'
                                        aria-label='<%# TrashActionLabel(Container.DataItem, "を元に戻す") %>' />
                            <asp:Button ID="PurgeButton" runat="server" Text="今すぐ完全に削除"
                                        CssClass="admin-link-btn admin-link-btn--danger" CausesValidation="False"
                                        CommandName="PurgeArticle"
                                        CommandArgument='<%# Eval("ArticleId") %>'
                                        aria-label='<%# TrashActionLabel(Container.DataItem, "を今すぐ完全に削除") %>'
                                        data-confirm='<%# PurgeConfirmText(Container.DataItem) %>'
                                        data-confirm-ok="完全に削除する" />
                        </span>
                    </li>
                </ItemTemplate>
                <FooterTemplate></ul></FooterTemplate>
            </asp:Repeater>

            <p class="admin-empty"><asp:Literal ID="TrashEmptyText" runat="server" /></p>

            <%-- 【［記事一覧へ戻る］は消した】（カテゴリの画面とそろえた）上のブログの切り替えの「記事一覧」で戻れる --%>

        </asp:PlaceHolder>

        <asp:PlaceHolder ID="ListView" runat="server">

        <div class="admin-toolbar">

            <div class="admin-filter">
                <asp:Label ID="CategoryFilterLabel" runat="server"
                           AssociatedControlID="CategoryFilter" Text="カテゴリ" CssClass="admin-filter-label" />
                <%-- 【AutoPostBack】選んだ瞬間に一覧を出し直す。
                     絞り込むだけでなく、**そのカテゴリの並べ方（指定した順／新しい順／古い順）で**
                     並べ替えて出す。振った番号が効いているかを、その場で目で見られるようにするため --%>
                <asp:DropDownList ID="CategoryFilter" runat="server" AutoPostBack="True"
                                  CssClass="admin-select" />
                <%= HelpButton("articles.filter") %>
            </div>

            <%-- 【ゴミ箱 (n)】カテゴリの絞り込みとは別の入口（中身の数は Manage.aspx.vb の BindTrashLink） --%>
            <asp:HyperLink ID="TrashLink" runat="server" CssClass="admin-link-btn admin-trash-link"
                           NavigateUrl="/account/articles?view=trash" />

            <%-- 【［新しい記事を書く］は、見出しの上の「ブログ：〇〇」の横へ移した】
                 カテゴリはブログの切り替え（上）にある --%>

        </div>

        <%-- 「?」＝表の項目の説明（文は Core/HelpTexts.vb の "articles.columns"）。記事の数の1行の後ろに続ける --%>
        <p class="admin-note"><asp:Literal ID="LimitText" runat="server" /><%= HelpButton("articles.columns") %></p>

        <%-- 記事の一覧。
             【Repeater で組み立てている】
             見出しの行・状態の色・ボタンの並びを、こちらで決めたい形に作れるため。
             カテゴリのほうは GridView（その場で直せる形）にしてあり、
             管理画面まわりで Web Forms を使い倒す、という方針どおり両方を使っている --%>
        <asp:Repeater ID="ArticleList" runat="server">

            <HeaderTemplate>
                <%-- 【admin-table--articles】記事の一覧だけの形（狭い画面で1件2行にする。Content/Site.css）。
                     カテゴリの表（GridView）は、今までどおり札の形 --%>
                <table class="admin-table admin-table--articles">
                    <thead>
                        <tr>
                            <th scope="col">タイトル</th>
                            <th scope="col">状態</th>
                            <th scope="col">カテゴリ</th>
                            <th scope="col">順番</th>
                            <th scope="col">トップに固定</th>
                            <th scope="col" class="admin-date-cell">最初に公開した日</th>
                            <th scope="col"><span class="visually-hidden">操作</span></th>
                        </tr>
                    </thead>
                    <tbody>
            </HeaderTemplate>

            <ItemTemplate>
                <tr<%# ErrorHereAttribute(Container.DataItem) %>>
                    <%-- 【タイトルは本文ではない。出口でエスケープする】
                         生の HTML で出してよいのは記事の本文だけである（Articles/ArticleHtml.vb）。
                         <%#: %> が、そのエスケープ付きのデータバインドである --%>
                    <%-- 【data-label は、狭い画面のためにある】
                         700px 以下では見出しの行を隠して、1行を「札のような箱」にする。
                         そのとき各マスの前に、この文字が項目名として出る（Content/Site.css の ::before）。
                         **見出しと同じ言葉を書くこと**——違うと、広い画面と狭い画面で言葉が変わる --%>
                    <%-- 【タイトルが空の下書き】自動保存で作られた下書きは、タイトルが空のことがある。「（タイトル未入力）」と出す。
                         【未反映の編集あり】公開中の記事に、まだ反映していない編集中の控えがあるときの印（色だけで伝えず、文字で書く） --%>
                    <th scope="row" class="admin-title-cell" data-label="タイトル"><span class='<%# TitleCssClass(Container.DataItem) %>'><%#: TitleText(Container.DataItem) %></span><%# DraftMarkHtml(Container.DataItem) %></th>
                    <%-- 【admin-nowrap／admin-free-cell】短い言葉を途中で割らないための印（Content/Site.css の .admin-table の節）。
                         状態は決まった短い言葉なので折り返さない。カテゴリは利用者が付けた名前なので、6文字ぶんは取ったうえで長ければ折り返す --%>
                    <td class="admin-nowrap" data-label="状態"><span class='<%# StatusCssClass(Container.DataItem) %>'><%#: StatusText(Container.DataItem) %></span></td>
                    <td class="admin-free-cell" data-label="カテゴリ"><%#: CategoryText(Container.DataItem) %></td>
                    <td class="admin-num-cell" data-label="カテゴリの中の順番"><%#: OrderText(Eval("SortOrder")) %></td>
                    <td class="admin-num-cell" data-label="トップに固定"><%#: OrderText(Eval("PinOrder")) %></td>
                    <td class="admin-date-cell" data-label="最初に公開した日"><%#: DateText(Eval("FirstPublishedAt")) %></td>
                    <%-- 【狭い画面の2行目】（お試し）状態・カテゴリ・最初に公開した日を小さく横に並べる。
                         並び順と固定は、番号が入っているときだけ札で出す。「状態：」のような項目名は出さない。
                         **広い画面では出さない**（同じことを上の列が出している。Content/Site.css の .admin-meta-cell）。
                         狭い画面では逆に、上の列のほうを出さない＝読み上げソフトが同じことを2回読むことはない --%>
                    <td class="admin-meta-cell"><%# MetaLineHtml(Container.DataItem) %></td>
                    <td class="admin-row-actions">
                        <div class="admin-row-actions-inner">
                        <%-- 【aria-label で、どの記事の操作かを名乗る】
                             読み上げソフトで Tab を送ると「編集」「削除」だけが並び、どの記事か分からないため --%>
                        <%-- 【記事のページへのリンク】公開中なら「公開ページを見る」、下書き・非公開なら「プレビュー」
                             （プレビューを見られるのは持ち主だけ。ほかの人には404）。
                             【新しいタブで開く】管理の画面を開いたまま、実際の見え方を確かめられるように。
                             rel="noopener"＝開いた先のページから、この画面を操作させない。
                             新しいタブで開くことは、読み上げ用の名前（aria-label）にも入れてある --%>
                        <%-- 【見える文字は、狭い画面では短くする】「公開ページを見る」→「見る」。
                             読み上げ用の名前（aria-label）は、どちらの幅でも長い形のまま --%>
                        <a href='<%# ViewUrl(Container.DataItem) %>' class="admin-link-btn"
                           target="_blank" rel="noopener"
                           aria-label='<%#: ViewLabel(Container.DataItem) %>'><span class="admin-label-long"><%#: ViewText(Container.DataItem) %></span><span class="admin-label-short" aria-hidden="true"><%#: ViewShortText(Container.DataItem) %></span></a>

                        <%-- 【公開中で、編集中の控えがある記事だけ】「編集中の内容をプレビュー」（見られるのは持ち主だけ） --%>
                        <asp:PlaceHolder runat="server" Visible='<%# ShowDraftPreview(Container.DataItem) %>'>
                        <a href='<%# DraftPreviewUrl(Container.DataItem) %>' class="admin-link-btn"
                           target="_blank" rel="noopener"
                           aria-label='<%#: ActionLabel(Container.DataItem, "の未反映の変更をプレビュー（新しいタブで開きます）") %>'><span class="admin-label-long">未反映の変更をプレビュー</span><span class="admin-label-short" aria-hidden="true">未反映を見る</span></a>
                        </asp:PlaceHolder>

                        <a href='<%# EditUrl(Eval("ArticleId")) %>' class="admin-link-btn"
                           aria-label='<%#: ActionLabel(Container.DataItem, "を編集") %>'>編集</a>

                        <%-- 【削除は POST（ボタンのポストバック）にしてある。GET のリンクにしないこと】
                             ログインの Cookie は SameSite=Lax で、Lax は**よそのサイトからの GET のページ移動には
                             Cookie を付けて送る**。GET で消せる形だと、よそのページに置いたリンク1つで
                             管理人に記事を削除させられる。POST なら、よそのサイトからは Cookie が付かない。
                             持ち主の突き合わせは ArticleStore.Delete（ArticleId と AccountId の両方で絞る）。
                             【data-confirm】押す前に確認の窓（Scripts/site.js の「5」）。編集の画面の削除と同じ形 --%>
                        <%-- 【ゴミ箱へ移す】すぐには消えない。30日のうちなら元に戻せる --%>
                        <asp:Button ID="DeleteButton" runat="server" Text="削除"
                                    CssClass="admin-link-btn admin-link-btn--danger"
                                    CausesValidation="False"
                                    CommandName="DeleteArticle"
                                    CommandArgument='<%# Eval("ArticleId") %>'
                                    aria-label='<%# ActionLabel(Container.DataItem, "をゴミ箱へ移す") %>'
                                    data-confirm='<%# DeleteConfirmText(Container.DataItem) %>'
                                    data-confirm-ok="ゴミ箱へ移す" />
                        </div>
                    </td>
                </tr>
            </ItemTemplate>

            <FooterTemplate>
                    </tbody>
                </table>
            </FooterTemplate>

        </asp:Repeater>

        <p class="admin-empty"><asp:Literal ID="EmptyText" runat="server" /></p>

        </asp:PlaceHolder>

    </div>

</asp:Content>
