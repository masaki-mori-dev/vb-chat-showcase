<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" CodeBehind="Manage.aspx.vb" Inherits="vb_chat.SiteNoticeManagePage" Title="お知らせ" %>

<%-- 【ValidateRequest="false" にしてある】お知らせの文に「<」を書けるようにするため
     （「<script> と書いたら」のような説明を、お知らせに書きうる）。
     この画面が画面へ出す値は、全部エスケープしている（<%#: %>・<%: %>・asp:TextBox の既定）。
     **Web.config でサイト全体を切らないこと** --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         発行の前に、【発行前チェック】の grep を走らせること。

         管理人からのお知らせ（ブログ型への作り替えの⑤）。

         【サイト管理者だけの画面】ログインの確かめ（AccountPageBase）のうえに、
         RequireAdministrator を通してある。**メニューに出さないだけでは守りにならない**
         （URL を直に打てる）。

         【カテゴリの画面（Articles/Categories.aspx）と、同じ形にそろえてある】
         GridView の一覧＋上に「追加」（上へ移した）。編集は行の場所に横いっぱいの欄で開く --%>

    <div class="admin-box">

        <h1>お知らせ</h1>

        <p class="account-error" role="alert" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>
        <p class="admin-done" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

        <%-- 【「サイトの『管理人からのお知らせ』に、新しいものからN件まで出ます。」は消した】
             【「『表示』を外すと、行を残したまま画面から消えます。」は、追加の「表示」の横の「?」へ移した】（
             何度も使う画面なので低くする＝基本ルールB） --%>

        <%-- 【追加の欄は、一覧より上】（基本ルール S。指摘：追加の欄はいちばん上に置かないと、あとで困る）
             お知らせが増えるほど、下に置いた追加の欄は遠くなる --%>
        <%-- 【理由は、操作したところの欄の上に出る】
             画面の側で止めたときも（Scripts/site.js の「8」「6」）、サーバーで断ったときも（「9」）、
             理由は**その欄の上**に出る（欄と言えないときは、その行・「追加」のボタンのすぐ上）。
             ボタンのそばの1行（data-validation-status）は外した——理由の場所を2つにしない --%>

        <%-- 【この区切りの欄だけを確かめる】（site.js の「8」）。開いている編集の行の欄で、追加が止められないように --%>
        <div data-validate-scope="1">

        <h2 class="admin-subhead">お知らせを追加</h2>

        <%-- 【ValidationGroup を分けてある】
             分けないと、下の「追加」の空欄が、上の行を編集するときの検査に引っかかる（逆も同じ） --%>

        <div class="form-row">
            <asp:Label ID="NewDateLabel" runat="server" AssociatedControlID="NewDateBox"
                       CssClass="admin-label" Text="日付" />
            <asp:TextBox ID="NewDateBox" runat="server" CssClass="admin-date-input" TextMode="Date" />
            <asp:RequiredFieldValidator ID="NewDateRequired" runat="server"
                                        ControlToValidate="NewDateBox"
                                        ValidationGroup="noticeNew"
                                        Display="Dynamic"
                                        CssClass="admin-validator"
                                        ErrorMessage="日付を入力してください。" />
        </div>

        <div class="form-row">
            <asp:Label ID="NewBodyLabel" runat="server" AssociatedControlID="NewBodyBox"
                       CssClass="admin-label" Text="お知らせ" />
            <%-- 【複数行で、中身に合わせて伸びる欄】（Scripts/site.js の「18」＝data-autogrow）。
                 TextMode="MultiLine" の asp:TextBox は MaxLength を書き出さないので、maxlength を属性で直に書く。
                 改行を打っても、保存のときに1行へまとめる（SiteNoticeRules.NormalizeBody。右の枠には1行で出す） --%>
            <asp:TextBox ID="NewBodyBox" runat="server" CssClass="account-input notice-body-input" TextMode="MultiLine" Rows="2"
                         maxlength="200" data-autogrow="1" />
            <asp:HiddenField ID="NewBodyEncoded" runat="server" />
            <asp:RequiredFieldValidator ID="NewBodyRequired" runat="server"
                                        ControlToValidate="NewBodyBox"
                                        ValidationGroup="noticeNew"
                                        Display="Dynamic"
                                        CssClass="admin-validator"
                                        ErrorMessage="お知らせの文を入力してください。" />
        </div>

        <div class="form-row form-row--wide">
            <asp:Label ID="NewLinkLabel" runat="server" AssociatedControlID="NewLinkBox"
                       CssClass="admin-label" Text="行き先" />
            <%-- 【よそのサイトも入れられるようにした】（Notices/SiteNoticeRules.vb の ValidateLink）
                 形は欄の中のグレーの字で見せてあるので、説明は「空でよい」と「どう開くか」だけ。
                 このサイトの住所を https:// から書いても、保存のときに「/…」の形へ直る（書き方の説明は要らない）。
                 欄の下から、欄の横の「入りきらないときだけ『?』」へ（何度も使う画面＝基本ルールB）
                 欄の横には何も出さず、全部「?」に入れた（文は開発者が決めたもの。勝手に言い換えないこと）。
                 基本は「https から始まるアドレスをコピーして貼る」。このサイトのページだけは「/」からでもよい --%>
            <div class="form-row-main" id="noticeLinkMain">
                <asp:TextBox ID="NewLinkBox" runat="server" CssClass="account-input" MaxLength="400"
                             placeholder="https://…" data-url-field="1"
                             aria-describedby="helpNoticeLink" data-error-anchor="noticeLinkMain" />
                <%= HelpButton("notices.link") %>
            </div>
            <asp:HiddenField ID="NewLinkEncoded" runat="server" />
        </div>

        <div class="form-row">
            <asp:Label ID="NewVisibleLabel" runat="server" AssociatedControlID="NewVisibleBox"
                       CssClass="admin-label" Text="表示" />
            <asp:CheckBox ID="NewVisibleBox" runat="server" Checked="true" />
            <%= HelpButton("notices.visible") %>
        </div>

        <%-- 【区切りを終わらせるボタンは、右寄せ】 --%>
        <div class="admin-actions admin-actions--end">
            <a href="/account" class="admin-link-btn">ホームへ戻る</a>
            <asp:Button ID="AddButton" runat="server" Text="追加" formnovalidate="formnovalidate"
                        CssClass="account-submit" ValidationGroup="noticeNew" />
        </div>

        </div>

        <h2 class="admin-subhead">お知らせの一覧</h2>

        <asp:GridView ID="NoticeGrid" runat="server"
                      AutoGenerateColumns="False"
                      DataKeyNames="NoticeId"
                      CssClass="admin-table admin-table--notices"
                      GridLines="None"
                      UseAccessibleHeader="True"
                      EditRowStyle-CssClass="is-editing"
                      EmptyDataText="お知らせは、まだありません。">

            <Columns>

                <%-- 【admin-date-cell】日付を折り返さない（Content/Site.css）。まで、CSS だけ在って印を付け忘れていた --%>
                <asp:TemplateField HeaderText="日付" ItemStyle-CssClass="admin-date-cell">
                    <ItemTemplate>
                        <%#: DateText(Container.DataItem) %>
                    </ItemTemplate>
                    <EditItemTemplate>
                        <%-- 【編集は、行の場所に横いっぱいの欄で開く】（基本ルール Q）
                             前は、それぞれの列のマスの中に欄を置いていた（GridView の行の編集）。お知らせの文の欄が列の幅に押し込まれて、
                             小さすぎて直せなかった（指摘：文章の欄が小さすぎて使えない）。
                             **欄は全部この1つのマスに入れ、Manage.aspx.vb の RowDataBound がこのマスを表の幅いっぱいに広げる**（ほかのマスは隠す）。
                             形は上の「お知らせを追加」とそろえてある。欄の名前（DateBox など）は前のまま＝保存の処理（RowUpdating）は FindControl で拾う --%>
                        <div class="notice-edit-form">
                        <h2 class="admin-subhead">お知らせを編集</h2>
                        <div class="form-row">
                            <asp:Label ID="DateLabel" runat="server" AssociatedControlID="DateBox" CssClass="admin-label" Text="日付" />
                        <%-- 【TextMode="Date" にしてある】ブラウザの日付の選び手が出る。
                             受け取る文字列は yyyy-MM-dd に決まるので、文化の違いで年が和暦になる罠に当たらない --%>
                        <asp:TextBox ID="DateBox" runat="server" CssClass="admin-date-input"
                                     TextMode="Date" Text='<%# DateValue(Container.DataItem) %>' />
                        <asp:RequiredFieldValidator ID="DateRequired" runat="server"
                                                    ControlToValidate="DateBox"
                                                    ValidationGroup="noticeEdit"
                                                    Display="Dynamic"
                                                    CssClass="admin-validator"
                                                    ErrorMessage="日付を入力してください。" />
                        </div>
                        <div class="form-row">
                            <asp:Label ID="BodyLabel" runat="server" AssociatedControlID="BodyBox" CssClass="admin-label" Text="お知らせ" />
                        <%-- 【包んで送る】お知らせの文に「<script>」のような説明を書くと、
                             本番の WAF が 403 で止める。送る直前に BodyEncoded へ包んで入れる（Scripts/site.js の「7」）。
                             つなぐのは Manage.aspx.vb の NoticeGrid_RowDataBound --%>
                        <%-- 【Text は <%# %> で入れる（「:」を付けない）】
                             asp:TextBox は、描くときに自分で Text をエスケープする。
                             ここで <%#: %> を使っていたので**2回エスケープされ**、欄に「&lt;script&gt;」と出て、
                             そのまま「保存」を押すと**保存される文そのものが「&lt;script&gt;」に変わっていた**
                             （本番で、右の枠に「&lt;script&gt;alert(1)&lt;/script&gt;」と記号のまま出た）。
                             「包んで送る」のせいではない——包む前に、もう欄の中身が変わっていた。
                             **サーバーコントロールの属性には <%# %>、素の HTML に出すときは <%#: %>** --%>
                        <asp:TextBox ID="BodyBox" runat="server" CssClass="account-input notice-body-input" TextMode="MultiLine" Rows="2"
                                     maxlength="200" data-autogrow="1" Text='<%# Eval("Body") %>' />
                        <asp:HiddenField ID="BodyEncoded" runat="server" />
                        <asp:RequiredFieldValidator ID="BodyRequired" runat="server"
                                                    ControlToValidate="BodyBox"
                                                    ValidationGroup="noticeEdit"
                                                    Display="Dynamic"
                                                    CssClass="admin-validator"
                                                    ErrorMessage="お知らせの文を入力してください。" />
                        </div>
                        <div class="form-row form-row--wide">
                            <asp:Label ID="LinkLabel" runat="server" AssociatedControlID="LinkBox" CssClass="admin-label" Text="行き先" />
                        <%-- 【data-url-field】javascript: などを、画面の側で先に止める（Scripts/site.js の「10」）。
                             **本体はサーバーの SiteNoticeRules.ValidateLink**（JS を切られても通さない）。
                             【包んで送る】「javascript:」を含む POST も WAF が 403 で止めるので、包んでアプリまで届かせ、
                             サーバーで理由を出して断る --%>
                        <%-- 【ここも <%# %>】「:」を付けると、行き先の「&」が「&amp;」になって保存される --%>
                        <asp:TextBox ID="LinkBox" runat="server" CssClass="account-input"
                                     MaxLength="400" Text='<%# Eval("LinkUrl") %>' data-url-field="1" placeholder="https://…" />
                        <asp:HiddenField ID="LinkEncoded" runat="server" />
                        </div>
                        <div class="form-row">
                            <asp:Label ID="VisibleLabel" runat="server" AssociatedControlID="VisibleBox" CssClass="admin-label" Text="表示" />
                            <asp:CheckBox ID="VisibleBox" runat="server" Checked='<%# Eval("IsVisible") %>' />
                        </div>
                        <%-- 【区切りを終わらせるボタンは、右寄せ】 --%>
                        <div class="admin-actions admin-actions--end">
                        <%-- 【asp:Button にした】（前は LinkButton）
                             LinkButton は __doPostBack（JS の form.submit()）で送るので、
                             送る直前に包む仕組み（site.js の「7」）の合図（submit）が飛ばない。
                             【formnovalidate】ブラウザには、この行の欄だけを確かめさせる（site.js の「8」。行に data-validate-scope） --%>
                        <asp:Button ID="UpdateLink" runat="server" CommandName="Update"
                                    CssClass="account-submit" formnovalidate="formnovalidate"
                                    ValidationGroup="noticeEdit"
                                    Text="保存" />
                        <asp:LinkButton ID="CancelLink" runat="server" CommandName="Cancel"
                                        CssClass="admin-link-btn" CausesValidation="False"
                                        Text="やめる" />
                        </div>
                        </div>
                    </EditItemTemplate>
                </asp:TemplateField>

                <%-- 【admin-free-cell】利用者が書く文なので、6文字ぶんは取ったうえで長ければ折り返す（Content/Site.css の .admin-table の節） --%>
                <asp:TemplateField HeaderText="お知らせ" ItemStyle-CssClass="admin-free-cell">
                    <ItemTemplate>
                        <%-- 【お知らせの文は本文ではない。出口でエスケープする】
                             生の HTML で出してよいのは記事の本文だけである --%>
                        <%#: Eval("Body") %>
                    </ItemTemplate>

                </asp:TemplateField>

                <asp:TemplateField HeaderText="行き先" ItemStyle-CssClass="admin-free-cell">
                    <ItemTemplate>
                        <%-- 【狭い画面では1行で、長ければ「…」】全体は title で読める --%>
                        <span class="notice-link-text" title='<%#: Eval("LinkUrl") %>'><%#: Eval("LinkUrl") %></span>
                    </ItemTemplate>

                </asp:TemplateField>

                <asp:TemplateField HeaderText="表示">
                    <ItemTemplate>
                        <%-- 【押せないチェックをやめて、文字の印にした】
                             押せないチェックは「押せそうで押せない飾り」に見えた。
                             **文字で書いてある**ので、色が見分けにくい人にも、読み上げソフトにもそのまま伝わる --%>
                        <%# VisibleMarkHtml(Container.DataItem) %>
                    </ItemTemplate>

                </asp:TemplateField>

                <asp:TemplateField>
                    <HeaderTemplate><span class="visually-hidden">操作</span></HeaderTemplate>
                    <ItemTemplate>
                        <asp:LinkButton ID="EditLink" runat="server" CommandName="Edit"
                                        CssClass="admin-link-btn" CausesValidation="False"
                                        Text="編集" />
                        <%-- 【消す前に確かめる】Scripts/site.js の「5」。
                             ブラウザ標準の confirm は、このサイトでは使わない決まりである --%>
                        <asp:LinkButton ID="DeleteLink" runat="server" CommandName="Delete"
                                        CssClass="admin-link-btn admin-link-btn--danger"
                                        CausesValidation="False"
                                        data-confirm="このお知らせを削除します。よろしいですか？"
                                        data-confirm-ok="削除する"
                                        Text="削除" />
                    </ItemTemplate>

                </asp:TemplateField>

            </Columns>

        </asp:GridView>


    </div>

</asp:Content>
