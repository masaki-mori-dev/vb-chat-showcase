'------------------------------------------------------------------------------
' <自動生成>
'     このコードはツールによって生成されました。
'
'     このファイルへの変更は、正しくない動作の原因になる可能性があり、
'     コードが再生成されるときに損失したりします。
' </自動生成>
'------------------------------------------------------------------------------

Option Strict On
Option Explicit On


Partial Public Class ArticleEditPage

    '''<summary>
    '''HeadingText コントロール。
    '''</summary>
    Protected WithEvents HeadingText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''SlugLabel コントロール。
    '''</summary>
    Protected WithEvents SlugLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''SlugBox コントロール。
    '''</summary>
    Protected WithEvents SlugBox As Global.System.Web.UI.WebControls.TextBox

    '''<summary>
    '''SlugFormat コントロール。
    '''</summary>
    Protected WithEvents SlugFormat As Global.System.Web.UI.WebControls.RegularExpressionValidator

    '''<summary>
    '''ViewLinkRow コントロール。
    '''</summary>
    Protected WithEvents ViewLinkRow As Global.System.Web.UI.WebControls.PlaceHolder

    '''<summary>
    '''ViewLink コントロール。
    '''</summary>
    Protected WithEvents ViewLink As Global.System.Web.UI.WebControls.HyperLink

    '''<summary>
    '''DraftPreviewLink コントロール。
    '''</summary>
    Protected WithEvents DraftPreviewLink As Global.System.Web.UI.WebControls.HyperLink

    '''<summary>
    '''DoneText コントロール。
    '''</summary>
    Protected WithEvents DoneText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''DraftNotice コントロール。
    '''</summary>
    Protected WithEvents DraftNotice As Global.System.Web.UI.WebControls.PlaceHolder

    '''<summary>
    '''DraftNoticeText コントロール。
    '''</summary>
    Protected WithEvents DraftNoticeText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''ApplyDraftButton コントロール。
    '''</summary>
    Protected WithEvents ApplyDraftButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''DiscardDraftButton コントロール。
    '''</summary>
    Protected WithEvents DiscardDraftButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''DiscardDraftBarButton コントロール。
    '''</summary>
    Protected WithEvents DiscardDraftBarButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''ErrorText コントロール。
    '''</summary>
    Protected WithEvents ErrorText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''TitleLabel コントロール。
    '''</summary>
    Protected WithEvents TitleLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''TitleBox コントロール。
    '''</summary>
    Protected WithEvents TitleBox As Global.System.Web.UI.WebControls.TextBox

    '''<summary>
    '''TitleRequired コントロール。
    '''</summary>
    Protected WithEvents TitleRequired As Global.System.Web.UI.WebControls.RequiredFieldValidator

    '''<summary>
    '''CategoryLabel コントロール。
    '''</summary>
    Protected WithEvents CategoryLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''CategoryList コントロール。
    '''</summary>
    Protected WithEvents CategoryList As Global.System.Web.UI.WebControls.DropDownList

    '''<summary>
    '''SwitchButton コントロール。
    '''</summary>
    Protected WithEvents SwitchButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''SortOrderLabel コントロール。
    '''</summary>
    Protected WithEvents SortOrderLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''SortOrderBox コントロール。
    '''</summary>
    Protected WithEvents SortOrderBox As Global.System.Web.UI.WebControls.TextBox

    '''<summary>
    '''SortOrderRange コントロール。
    '''</summary>
    Protected WithEvents SortOrderRange As Global.System.Web.UI.WebControls.RangeValidator

    '''<summary>
    '''PinOrderLabel コントロール。
    '''</summary>
    Protected WithEvents PinOrderLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''PinOrderBox コントロール。
    '''</summary>
    Protected WithEvents PinOrderBox As Global.System.Web.UI.WebControls.TextBox

    '''<summary>
    '''PinOrderRange コントロール。
    '''</summary>
    Protected WithEvents PinOrderRange As Global.System.Web.UI.WebControls.RangeValidator

    '''<summary>
    '''TocLabel コントロール。
    '''</summary>
    Protected WithEvents TocLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''TocList コントロール。
    '''</summary>
    Protected WithEvents TocList As Global.System.Web.UI.WebControls.DropDownList

    '''<summary>
    '''HeadingNumberLabel コントロール。
    '''</summary>
    Protected WithEvents HeadingNumberLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''HeadingNumberList コントロール。
    '''</summary>
    Protected WithEvents HeadingNumberList As Global.System.Web.UI.WebControls.DropDownList

    '''<summary>
    '''BodyLabel コントロール。
    '''</summary>
    Protected WithEvents BodyLabel As Global.System.Web.UI.WebControls.Label

    '''<summary>
    '''BodyBox コントロール。
    '''</summary>
    Protected WithEvents BodyBox As Global.System.Web.UI.WebControls.TextBox

    '''<summary>
    '''BodyRequired コントロール。
    '''</summary>
    Protected WithEvents BodyRequired As Global.System.Web.UI.WebControls.RequiredFieldValidator

    '''<summary>
    '''SaveButton コントロール。
    '''</summary>
    Protected WithEvents SaveButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''SaveStatusText コントロール。
    '''</summary>
    '''<remarks>
    '''自動生成されたフィールド。
    '''変更するには、フィールドの宣言をデザイナー ファイルから分離コード ファイルに移動します。
    '''</remarks>
    Protected WithEvents SaveStatusText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''SavedStatusText コントロール。
    '''</summary>
    '''<remarks>
    '''自動生成されたフィールド。
    '''変更するには、フィールドの宣言をデザイナー ファイルから分離コード ファイルに移動します。
    '''</remarks>
    Protected WithEvents SavedStatusText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''DeleteButton コントロール。
    '''</summary>
    Protected WithEvents DeleteButton As Global.System.Web.UI.WebControls.Button

    '''<summary>
    '''MetaText コントロール。
    '''</summary>
    Protected WithEvents MetaText As Global.System.Web.UI.WebControls.Literal

    '''<summary>
    '''SlugFormatPublish コントロール（公開する保存の組の、URL の名前の検査）。
    '''</summary>
    Protected WithEvents SlugFormatPublish As Global.System.Web.UI.WebControls.RegularExpressionValidator

    '''<summary>
    '''SortOrderRangePublish コントロール。
    '''</summary>
    Protected WithEvents SortOrderRangePublish As Global.System.Web.UI.WebControls.RangeValidator

    '''<summary>
    '''PinOrderRangePublish コントロール。
    '''</summary>
    Protected WithEvents PinOrderRangePublish As Global.System.Web.UI.WebControls.RangeValidator

End Class
