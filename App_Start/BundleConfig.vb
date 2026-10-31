Imports System.Collections.Generic
Imports System.Linq
Imports System.Web
Imports System.Web.Optimization

Public Class BundleConfig
    ' バンドルの詳細については、https://go.microsoft.com/fwlink/?LinkID=303951 を参照してください
    Public Shared Sub RegisterBundles(ByVal bundles As BundleCollection)
        bundles.Add(New ScriptBundle("~/bundles/WebFormsJs").Include(
                        "~/Scripts/lib/WebForms/WebForms.js",
                        "~/Scripts/lib/WebForms/WebUIValidation.js",
                        "~/Scripts/lib/WebForms/MenuStandards.js",
                        "~/Scripts/lib/WebForms/Focus.js",
                        "~/Scripts/lib/WebForms/GridView.js",
                        "~/Scripts/lib/WebForms/DetailsView.js",
                        "~/Scripts/lib/WebForms/TreeView.js",
                        "~/Scripts/lib/WebForms/WebParts.js"))

        ' これらのファイルには明示的な依存関係があり、ファイルが動作するためには順序が重要です
        bundles.Add(New ScriptBundle("~/bundles/MsAjaxJs").Include(
                "~/Scripts/lib/WebForms/MsAjax/MicrosoftAjax.js",
                "~/Scripts/lib/WebForms/MsAjax/MicrosoftAjaxApplicationServices.js",
                "~/Scripts/lib/WebForms/MsAjax/MicrosoftAjaxTimer.js",
                "~/Scripts/lib/WebForms/MsAjax/MicrosoftAjaxWebForms.js"))

        ' Modernizr のバンドルは削除した。
        ' Site.Master が配っていただけで、参照している箇所が1件も無かった
        ' （Modernizr.* を呼ぶ行が0件、.no-js などのクラスも0件）。
        ' Scripts/modernizr-2.8.3.js ごと消してある。
        '
        ' 上の2つ（WebFormsJs・MsAjaxJs）は、今はどこからも Scripts.Render されていないが、
        ' 残してある。管理画面で Validator を使う回に関わる可能性があるため
        ' （宿題5の「ScriptManager」の節を参照）。登録してあるだけでは配られない
    End Sub
End Class
