using System.Threading.Tasks;
using Heleus.Apps.Shared;

namespace Heleus.Apps.Todo
{
    public class InvitationResultPage : StackPage
    {
        readonly string _request;

        Task Share(ButtonRow button)
        {
            UIApp.Share(_request);
            return Task.CompletedTask;
        }

        Task Copy(ButtonRow button)
        {
            UIApp.CopyToClipboard(_request);
            return Task.CompletedTask;
        }

        public InvitationResultPage(string request) : base("ListInvitationResultPage")
        {
            _request = request;

            AddTitleRow("Title");

            AddHeaderRow("RequestCode");

            AddInfoRow("Info");

            var code = AddEditorRow(null, "RequestCode");
            code.Edit.Text = request;
            code.Edit.IsReadOnly = true;

            AddButtonRow("Copy", Copy);

            if (UIApp.CanShare)
            {
                var share = AddButtonRow("Share", Share);
                share.RowStyle = Theme.SubmitButton;
            }

            AddFooterRow();
        }
    }
}
