<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="MyChatLogView.aspx.vb" Inherits="vb_chat.MyChatLogViewPage" Title="チャット履歴" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         自分が参加したチャット履歴の、1回ぶんの中身。URL は /account/chatlogs/view?s=（入室の行の番号）。

         【番号を変えても、ほかの人の記録は読めない】
         ?s= の番号は、Chat/AccountChatLog.vb の FindSession が「ログインしている人の入室の行か」を DB で確かめる。
         合わなければ、在るか無いかも言わずに「見つかりません」（404）を出す。
         そのあと読む範囲（部屋・どこからどこまで・そのときのブラウザ）は、確かめた行から取る。URL の値は使わない。

         【見せるもの】その部屋に居た間の、部屋の発言・入退室のお知らせと、**自分が送った／自分宛ての**こっそり。
         ほかの人どうしのこっそりは、SQL の時点で読んでいない（画面で隠しているのではない）。

         【行の中身は、コードビハインドが組み立てる】（RowsHtml）。日付の区切りを行の間に挟むため。
         名前・本文・部屋の名前は、どれも利用者が決めた文字なので、組み立てる所で必ずエスケープしている --%>

    <div class="admin-box">

        <asp:PlaceHolder ID="LogView" runat="server" Visible="False">

            <h1><%: RoomNameText %></h1>

            <p class="admin-note"><%: TimeRangeText %></p>

            <p class="account-error" role="alert"><asp:Literal ID="ErrorText" runat="server" /></p>

            <asp:PlaceHolder ID="EmptyView" runat="server" Visible="False">
                <p class="admin-note">このページに出す発言はありません。</p>
            </asp:PlaceHolder>

            <%-- 1行ずつ。時刻・名前（と印）・本文。並べ方は中身任せ（flex-wrap）。
                 【<%= %> で出している】中身は RowsHtml が、値を1つずつエスケープして組み立てた HTML である --%>
            <ol class="chatlog-rows">
                <%= RowsHtml %>
            </ol>

            <%-- ページ送り。ここは古い順に読むので、「前のページ」＝古いほう、「次のページ」＝新しいほう --%>
            <asp:PlaceHolder ID="PagerSection" runat="server" Visible="False">
                <nav class="blog-pager" aria-label="ページ送り">
                    <asp:PlaceHolder ID="PagerPrev" runat="server" Visible="False">
                        <a class="blog-pager-link" href="<%: PrevPageUrl %>">前のページ</a>
                    </asp:PlaceHolder>
                    <span class="blog-pager-now"><%: PageText %></span>
                    <asp:PlaceHolder ID="PagerNext" runat="server" Visible="False">
                        <a class="blog-pager-link" href="<%: NextPageUrl %>">次のページ</a>
                    </asp:PlaceHolder>
                </nav>
            </asp:PlaceHolder>

        </asp:PlaceHolder>

        <%-- 見つからない（番号が違う・自分のものではない・保存期間を過ぎて消えた）。どれなのかは言わない --%>
        <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
            <h1>履歴が見つかりません</h1>
            <p class="admin-note">保存期間を過ぎて消えたか、開けない履歴です。</p>
        </asp:PlaceHolder>

        <%-- DB が読めなかった（理由は出さない。エラー報告メールで届く） --%>
        <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
            <h1>チャット履歴</h1>
            <p class="account-error" role="alert">ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </asp:PlaceHolder>

        <%-- 【一覧へ戻る】記事の編集画面の「一覧へ戻る」と同じ見た目。
             【右寄せ】決まりが「ボタンと、枠のいちばん下のリンクは右寄せ」に広がった（前は「フォームを終わらせるボタン」だけだったので、左のままだった） --%>
        <div class="admin-actions admin-actions--end">
            <a href="/account/chatlogs" class="admin-link-btn">一覧へ戻る</a>
        </div>

    </div>

</asp:Content>
