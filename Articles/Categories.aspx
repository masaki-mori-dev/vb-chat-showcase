<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="Categories.aspx.vb" Inherits="vb_chat.ArticleCategoriesPage" Title="カテゴリ" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         発行の前に、【発行前チェック】の grep を走らせること。

         【GridView をその場で直せる形（インライン編集）で使っている】
         記事の一覧は Repeater、こちらは GridView にしてある。
         2世代目（C#／ASP.NET Core MVC）への移行を、レガシー移行の予行演習にする、という方針どおり
         ——**移行で本当に手間がかかるのは、この GridView の編集まわりである**
         （「管理画面とブログCMSでは、Web Forms を『あえて』使い倒す」）。

         【SqlDataSource は使わない】
         SQL をマークアップに書く形で、移行時に対応物がまったく無い。
         データは Articles/ArticleCategoryStore.vb から読んで、コードビハインドで DataBind している --%>

    <div class="admin-box">

        <%-- 【ブログの切り替え】（Articles/BlogTabs.vb） --%>
        <%= BlogTabsHtml %>

        <h1>カテゴリ</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- 【並べ方の説明は、「カテゴリを追加」の「並べ方」の「?」へ移した】
             説明は、それが関係する欄の「?」へ寄せる（基本ルールの「スマホは縦を節約する」の7）。文は前のまま --%>

        <%-- 【追加の欄は、一覧より上】（基本ルール S。お知らせの画面と同じ形）
             カテゴリが増えるほど、下に置いた追加の欄は遠くなる。
             【一覧の行の中で直すのは、このままでよい】名前（50文字）・並べ方・順番は、どれも短い値（基本ルール Q） --%>
        <%-- 【理由は、操作したところの欄の上に出る】
             画面の検査（Validator）で止めたときは Scripts/site.js の「6」、サーバーで断ったときは「9」が、
             理由を**その欄の上**（欄と言えないときは、その行・「追加」のボタンのすぐ上）へ出す。
             前は、理由の場所がページのいちばん上に1か所しかなく、下の「追加」で断られると上へ飛んでいた。
             ボタンのそばの1行（data-validation-status）は外した——理由の場所を2つにしない --%>

        <%-- 【この区切りの欄だけを確かめる】（site.js の「8」）。開いている編集の行の欄で、追加が止められないように --%>
        <div data-validate-scope="1">

        <h2 class="admin-subhead">カテゴリを追加</h2>

        <%-- 【ValidationGroup を分けてある】
             分けないと、下の「追加」の空欄が、上の行を編集するときの検査に引っかかる
             （逆も同じ）。片方を押したら、もう片方の入力欄に赤が出る形になる --%>

        <div class="form-row">
            <asp:Label ID="NewNameLabel" runat="server" AssociatedControlID="NewNameBox"
                       CssClass="admin-label" Text="名前" />
            <asp:TextBox ID="NewNameBox" runat="server" CssClass="account-input" MaxLength="50" />
            <asp:RequiredFieldValidator ID="NewNameRequired" runat="server"
                                        ControlToValidate="NewNameBox"
                                        ValidationGroup="categoryNew"
                                        Display="Dynamic"
                                        CssClass="admin-validator"
                                        ErrorMessage="名前を入力してください。" />
        </div>

        <div class="form-row">
            <asp:Label ID="NewSortModeLabel" runat="server" AssociatedControlID="NewSortModeList"
                       CssClass="admin-label" Text="並べ方" />
            <div class="form-row-main">
                <asp:DropDownList ID="NewSortModeList" runat="server" CssClass="admin-select" />
                <%-- 【見える1行は置かず、「?」の中に全文】（部品は Scripts/site.js の「4」） --%>
                <%= HelpButton("categories.sortMode") %>
            </div>
        </div>

        <div class="form-row">
            <asp:Label ID="NewSortOrderLabel" runat="server" AssociatedControlID="NewSortOrderBox"
                       CssClass="admin-label" Text="カテゴリの順番" />
            <asp:TextBox ID="NewSortOrderBox" runat="server" CssClass="admin-num-input"
                         MaxLength="10" inputmode="numeric" data-number-field="1" data-number-min="0" data-number-max="9999" data-number-default="0" Text="0" />
            <%= HelpButton("categories.order") %>
            <asp:RangeValidator ID="NewSortOrderRange" runat="server"
                                ControlToValidate="NewSortOrderBox"
                                ValidationGroup="categoryNew"
                                Display="Dynamic"
                                CssClass="admin-validator"
                                Type="Integer" MinimumValue="0" MaximumValue="9999"
                                ErrorMessage="カテゴリの順番は0〜9999の数で入力してください。" />
        </div>

        <%-- 【区切りを終わらせるボタンは、右寄せ】（お知らせの画面と同じ形） --%>
        <div class="admin-actions admin-actions--end">
            <%-- 【［記事一覧へ戻る］は消した】上のブログの切り替えで戻れる --%>
            <asp:Button ID="AddButton" runat="server" Text="追加" formnovalidate="formnovalidate"
                        CssClass="account-submit" ValidationGroup="categoryNew" />
        </div>

        </div>

        <h2 class="admin-subhead">カテゴリの一覧</h2>

        <asp:GridView ID="CategoryGrid" runat="server"
                      AutoGenerateColumns="False"
                      DataKeyNames="CategoryId"
                      CssClass="admin-table admin-table--categories"
                      EditRowStyle-CssClass="is-editing"
                      GridLines="None"
                      UseAccessibleHeader="True"
                      EmptyDataText="カテゴリは、まだありません。">

            <Columns>

                <%-- 【ItemStyle-CssClass】短い言葉を途中で割らないための印（Content/Site.css の .admin-table の節）。
                     名前＝利用者が付けたもの（6文字ぶんは取り、長ければ折り返す）／並べ方＝決まった短い言葉（折り返さない） --%>
                <asp:TemplateField HeaderText="名前" ItemStyle-CssClass="admin-free-cell">
                    <ItemTemplate>
                        <%-- 【カテゴリの名前は本文ではない。出口でエスケープする】
                             生の HTML で出してよいのは記事の本文だけである。
                             <%#: %> が、そのエスケープ付きのデータバインドである --%>
                        <%#: Eval("Name") %>
                    </ItemTemplate>
                    <EditItemTemplate>
                        <%-- 【Text は <%# %> で入れる（「:」を付けない）】
                             asp:TextBox は、描くときに自分で Text をエスケープする。
                             ここで <%#: %> を使うと**2回エスケープされ**、「Q&A」が欄に「Q&amp;A」と出て、
                             そのまま保存すると**名前そのものが「Q&amp;A」に変わる**（お知らせの画面で実際に起きた）。
                             **サーバーコントロールの属性には <%# %>、素の HTML に出すときは <%#: %>** --%>
                        <asp:TextBox ID="NameBox" runat="server" CssClass="account-input"
                                     MaxLength="50" Text='<%# Eval("Name") %>' aria-label="名前" />
                        <asp:RequiredFieldValidator ID="NameRequired" runat="server"
                                                    ControlToValidate="NameBox"
                                                    ValidationGroup="categoryEdit"
                                                    Display="Dynamic"
                                                    CssClass="admin-validator"
                                                    ErrorMessage="名前を入力してください。" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="並べ方" ItemStyle-CssClass="admin-nowrap">
                    <ItemTemplate><%#: SortModeText(Container.DataItem) %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:DropDownList ID="SortModeList" runat="server" CssClass="admin-select" aria-label="並べ方" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="カテゴリの順番" ItemStyle-CssClass="admin-num-cell">
                    <ItemTemplate><span class="admin-cat-order-prefix" aria-hidden="true">順番 </span><%#: Eval("SortOrder") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="SortOrderBox" runat="server" CssClass="admin-num-input"
                                     MaxLength="10" inputmode="numeric" data-number-field="1" data-number-min="0" data-number-max="9999" data-number-default="0" Text='<%# Eval("SortOrder") %>' aria-label="カテゴリの順番" />
                        <asp:RangeValidator ID="SortOrderRange" runat="server"
                                            ControlToValidate="SortOrderBox"
                                            ValidationGroup="categoryEdit"
                                            Display="Dynamic"
                                            CssClass="admin-validator"
                                            Type="Integer" MinimumValue="0" MaximumValue="9999"
                                            ErrorMessage="カテゴリの順番は0〜9999の数で入力してください。" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="記事" ItemStyle-CssClass="admin-num-cell">
                    <ItemTemplate><%#: Eval("ArticleCount") %></ItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField>
                    <HeaderTemplate><span class="visually-hidden">操作</span></HeaderTemplate>
                    <ItemTemplate>
                        <asp:LinkButton ID="EditLink" runat="server" CommandName="Edit"
                                        CssClass="admin-link-btn" CausesValidation="False"
                                        Text="編集" />
                        <%-- 【消す前に確かめる】Scripts/site.js の「5」。窓にはカテゴリ名を出す（記事の一覧の削除と同じ形。文は DeleteConfirmText）。
                             ブラウザ標準の confirm は、このサイトでは使わない決まりである --%>
                        <asp:LinkButton ID="DeleteLink" runat="server" CommandName="Delete"
                                        CssClass="admin-link-btn admin-link-btn--danger"
                                        CausesValidation="False"
                                        data-confirm='<%# DeleteConfirmText(Container.DataItem) %>'
                                        data-confirm-ok="削除する"
                                        Text="削除" />
                    </ItemTemplate>
                    <EditItemTemplate>
                        <asp:LinkButton ID="UpdateLink" runat="server" CommandName="Update"
                                        CssClass="admin-link-btn"
                                        ValidationGroup="categoryEdit"
                                        Text="保存" />
                        <asp:LinkButton ID="CancelLink" runat="server" CommandName="Cancel"
                                        CssClass="admin-link-btn" CausesValidation="False"
                                        Text="やめる" />
                    </EditItemTemplate>
                </asp:TemplateField>

            </Columns>

        </asp:GridView>


    </div>

</asp:Content>
