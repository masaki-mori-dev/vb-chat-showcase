<%@ Page Language="vb" AutoEventWireup="false" CodeBehind="RebuildArticleImageLedger.aspx.vb" Inherits="vb_chat.RebuildArticleImageLedger" %>
<!DOCTYPE html>
<html>
<head runat="server">
    <title>記事の画像の台帳を作り直す</title>
    <meta name="viewport" content="width=device-width, initial-scale=1" />
</head>
<body style="font-family: sans-serif; padding: 16px; line-height: 1.8;">
    <form id="form1" runat="server">

        <h1 style="font-size: 20px;">記事の画像の台帳を作り直す</h1>

        <p>
            ディスクに在って台帳に無い画像を台帳へ載せ、そのあと、すべての記事と編集中の控えの本文から
            「どの記事が、どの画像を使っているか」を作り直します。<strong>ファイルは1枚も消しません。</strong>
        </p>

        <p>押すのは、次の2つのときだけです。</p>
        <ul>
            <li>台帳を作った日の、一度きりの手当て（それまでに在った画像を、台帳に載せる）</li>
            <li>1日1回の集計メールに「台帳に無いファイルが◯枚ある」と出たとき</li>
        </ul>

        <p>
            <asp:Button ID="RunButton" runat="server" Text="台帳を作り直す" OnClick="RunButton_Click"
                        style="font-size: 16px; padding: 8px 16px;" />
        </p>

        <asp:Literal ID="ResultText" runat="server" />

    </form>
</body>
</html>
