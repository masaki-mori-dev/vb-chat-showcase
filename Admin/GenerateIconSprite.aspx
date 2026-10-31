<%@ Page Language="vb" AutoEventWireup="false" CodeBehind="GenerateIconSprite.aspx.vb" Inherits="vb_chat.GenerateIconSprite" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>アイコンスプライト生成ツール</title>
</head>
<body>
    <form id="form1" runat="server">
        <div>
            <h2>アイコンスプライト生成ツール</h2>
            <p>IconMasterテーブルの内容をもとに、icon-sprite.svgを再生成します。</p>
            <asp:Button ID="btnGenerate" runat="server" Text="生成する" OnClick="btnGenerate_Click" />
            <p><asp:Label ID="lblResult" runat="server" Text=""></asp:Label></p>
        </div>
    </form>
</body>
</html>