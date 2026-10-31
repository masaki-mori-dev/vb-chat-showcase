<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="LogReplay.aspx.vb" Inherits="vb_chat.LogReplayPage" Title="ログを再生" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】

         ログを調べる道具の、再生の画面（/account/logs/view?m=発言の番号。サイト管理者だけ）。
         探す画面で押した発言を、部屋と同じ LINE 風の吹き出しで、真ん中に色付きで出す。

         【最初はその前後30分】上下の「もっと前／もっと後」で、さらに読み込む（LINE で上へさかのぼるのと同じ。ページは読み込み直さない＝WebMethod の LoadMore）。
         【吹き出しを押すと小窓】IP・ポート番号・トリップ・アカウントID・ブラウザの印・ミリ秒までの時刻。
         全部の吹き出しの下には出さない（会話の流れが読めなくなるため）。中身は行の data-* から Scripts/admin-logs.js が組み立てる。
         【在室者】その時刻に部屋にいた人（入室・退室の記録から割り出す）。PC は横、スマホは上の「在室者」ボタン（Content/admin-logs.css）。
         【こっそりも混ぜる】スイッチを入れると、こっそりの発言が別の色で時刻順に混ざる（送り手 → 宛先）。こっそりの吹き出しの小窓から「この2人のこっそりだけ」に絞れる。
         【この範囲を書き出す】今出ている範囲（最初の行〜最後の行）を CSV で（/account/logs/csv）。さかのぼると、範囲も広がる。

         【開く・さかのぼる・書き出すたびに、見た記録を残す】（ChatLogAudit。書けなかったら出さない）

         【部屋の描画（Scripts/chat-room.js）は使い回していない】理由は Admin/LogToolView.vb の頭 --%>

    <link href="<%: Asset("~/Content/admin-logs.css") %>" rel="stylesheet" type="text/css" />

    <div class="admin-box logtool logview">

        <div class="admin-actions logview-back">
            <a href="<%: SearchBackUrl %>" class="admin-link-btn" id="logviewBack">探す画面へ戻る</a>
        </div>

        <asp:PlaceHolder ID="LogView" runat="server" Visible="False">

            <h1><%: RoomTitle %></h1>
            <p class="admin-note logview-center-note"><%: CenterText %></p>

            <div class="logview-tools">

                <div class="logview-switch">
                    <label for="logviewPrivate">こっそりも混ぜる</label>
                    <label class="toggle-switch">
                        <input type="checkbox" id="logviewPrivate"<%= If(IncludePrivate, " checked=""checked""", "") %> />
                        <span class="toggle-slider"></span>
                    </label>
                </div>

                <%-- 「この2人のこっそりだけ」に絞っているとき --%>
                <asp:PlaceHolder ID="PairChip" runat="server" Visible="False">
                    <span class="logsearch-chip">
                        <span class="logsearch-chip-text">こっそり：<%: PairNames %> だけ</span>
                        <a class="logsearch-chip-clear" href="<%: PairClearUrl %>" aria-label="2人のこっそりだけ、を外す">×</a>
                    </span>
                </asp:PlaceHolder>

                <div class="logview-tools-end">
                    <button type="button" class="admin-link-btn logview-people-btn" id="logviewPeopleBtn"
                            aria-expanded="false" aria-controls="logviewPeople">在室者（<%: PresenceCount %>）</button>
                    <a href="<%: ExportUrl %>" class="admin-link-btn" id="logviewCsv">この範囲を書き出す</a>
                </div>

            </div>

            <div class="logview-layout">

                <div class="logview-log" id="logviewLog">

                    <button type="button" class="logview-more" id="logviewOlder" data-dir="older"<%= If(HasBefore, "", " hidden=""hidden""") %>>もっと前</button>

                    <%-- 行の中身は Admin/LogToolView.vb が作る（値は1つずつエスケープしてある）。
                         data-* は「もっと前／もっと後」と「書き出す」が使う（中心の番号・今出ている端・絞り込み） --%>
                    <ol class="logview-rows" id="logviewRows"
                        data-center="<%: CenterId %>" data-first="<%: FirstId %>" data-last="<%: LastId %>"
                        data-private="<%= If(IncludePrivate, "1", "0") %>" data-pair="<%: PairValue %>"
                        data-more-url="/Admin/LogReplay.aspx/LoadMore" data-export-url="<%: ExportBaseUrl %>">
                        <%= RowsHtml %>
                    </ol>

                    <button type="button" class="logview-more" id="logviewNewer" data-dir="newer"<%= If(HasAfter, "", " hidden=""hidden""") %>>もっと後</button>

                    <p class="logview-status" id="logviewStatus" role="status"></p>

                </div>

                <section class="logview-side" id="logviewPeople" aria-labelledby="logviewPeopleHead">
                    <h2 class="admin-subhead" id="logviewPeopleHead">在室者（<%: CenterTimeText %> の時点）</h2>
                    <p class="admin-note">入室・退室の記録から割り出したものです。</p>
                    <asp:PlaceHolder ID="PresenceEmpty" runat="server" Visible="False">
                        <p class="admin-note">記録からは、居た人が見つかりませんでした。</p>
                    </asp:PlaceHolder>
                    <ul class="logview-people">
                        <%= PresenceHtml %>
                    </ul>
                </section>

            </div>

            <%-- 吹き出しを押すと出る小窓（中身は Scripts/admin-logs.js が、押した行の data-* から textContent で入れる） --%>
            <div class="logview-pop" id="logviewPop" role="dialog" aria-modal="false" aria-labelledby="logviewPopTitle" hidden="hidden">
                <div class="logview-pop-head">
                    <h2 class="logview-pop-title" id="logviewPopTitle">この行の記録</h2>
                    <button type="button" class="logview-pop-close" id="logviewPopClose" aria-label="閉じる">×</button>
                </div>
                <dl class="logview-pop-list" id="logviewPopList"></dl>
                <div class="logview-pop-links" id="logviewPopLinks"></div>
            </div>

        </asp:PlaceHolder>

        <%-- 番号が違う・保存期間を過ぎて消えた --%>
        <asp:PlaceHolder ID="NotFoundView" runat="server" Visible="False">
            <h1>記録が見つかりません</h1>
            <p class="admin-note">保存期間を過ぎて消えたか、番号が違います。</p>
        </asp:PlaceHolder>

        <%-- DB が読めなかった・見た記録が書けなかった（理由は出さない。エラー報告メールで届く） --%>
        <asp:PlaceHolder ID="TroubleView" runat="server" Visible="False">
            <h1>ログを再生</h1>
            <p class="account-error" role="alert">ただいま混み合っています。少し待ってから、もう一度お試しください。</p>
        </asp:PlaceHolder>

    </div>

</asp:Content>

<asp:Content ID="ScriptsContent" ContentPlaceHolderID="AdminScripts" runat="server">
    <script src="<%: Asset("~/Scripts/admin-logs.js") %>"></script>
</asp:Content>
