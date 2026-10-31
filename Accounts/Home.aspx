<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" CodeBehind="Home.aspx.vb" Inherits="vb_chat.AccountHomePage" Title="アカウント" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く） --%>

    <div class="account-box">

        <h1>アカウント</h1>

        <%-- 【出口でエスケープすること】
             表示名（DisplayName）は日本語が入り、将来は一般の人が自分で決める値になる。<%: %> がそのエスケープ。
             【ユーザー名ではなく表示名を出す】ヘッダーの「表示名（ロール名）」とそろえた。
             ユーザー名はログインと記事の URL に使う名前で、見せるのは設定の画面だけ --%>
        <p class="account-who">
            ログイン中：<strong><%: CurrentDisplayName %></strong>（<%: CurrentRoleName %>）
        </p>

        <%-- 【メニューの項目ごとの説明】（中身は Home.aspx.vb の MenuGuideHtml。並びは管理メニューと同じ） --%>
        <%= MenuGuideHtml %>

        <%-- 【ログアウトのボタンは、ここから外した】
             管理画面のメニュー（Accounts/AdminMaster.Master）の右端に、どの画面からでも押せる形で在る。
             同じ画面に2つ置くと、どちらを押すか迷わせるだけになる --%>

    </div>

</asp:Content>
