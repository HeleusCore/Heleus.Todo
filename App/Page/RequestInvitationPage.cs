using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.TodoService;

namespace Heleus.Apps.Todo
{
    public class RequestInvitationPage : StackPage
    {
        readonly SubmitAccountButtonRow _submitAccount;
        readonly EditorRow _request;
        readonly ButtonRow _copy;
        readonly ButtonRow _open;
        readonly ButtonRow _share;
        readonly MonoSpaceLabel _id;

        string _uri;

        Task Copy(ButtonRow button)
        {
            UIApp.CopyToClipboard(_uri);
            return Task.CompletedTask;
        }

        Task Share(ButtonRow button)
        {
            UIApp.Share(_uri);
            return Task.CompletedTask;
        }


        Task Open(ButtonRow arg)
        {
            UIApp.OpenUrl(new Uri(_uri));
            return Task.CompletedTask;
        }

        Task Update()
        {
            var submitAccount = _submitAccount.SubmitAccount;
            if(submitAccount == null)
            {
                _copy.IsEnabled = false;
                _open.IsEnabled = false;
                if (_share != null)
                    _share.IsEnabled = false;
                _request.Edit.Text = null;
                _id.Text = "-";
            }
            else
            {
                _copy.IsEnabled = true;
                _open.IsEnabled = true;
                if (_share != null)
                    _share.IsEnabled = true;

                _uri = TodoApp.Current.GetRequestCode(submitAccount.ServiceNode, TodoServiceInfo.GroupChainIndex, RequestInvitationSchemeAction.ActionName, submitAccount.AccountId);
                _id.Text = T("Id", submitAccount.AccountId.ToString());
                _request.Edit.Text = _uri;
            }

            return Task.CompletedTask;
        }

        public RequestInvitationPage() : base("RequestInvitationPage")
        {
            AddTitleRow("Title");


            AddHeaderRow("Request");

            _request = AddEditorRow(_uri, null);
            _request.Edit.IsReadOnly = true;

            _copy = AddButtonRow("Copy", Copy);
            _open = AddButtonRow("Open", Open);

            if (UIApp.CanShare)
            {
                _share = AddButtonRow("Share", Share);
                _share.RowStyle = Theme.SubmitButton;
            }

            AddFooterRow();

            _id = new MonoSpaceLabel { FontSize = 20, HorizontalTextAlignment = Xamarin.Forms.TextAlignment.Center };
            AddViewRow(_id);

            AddTextRow("Info");

            AddFooterRow();

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow(this, () => ServiceNodeManager.Current.GetSubmitAccounts<SubmitAccount>(TodoServiceInfo.TodoSubmitIndex)));
            _submitAccount.SelectionChanged = (sa) => Update();
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();


            Update();
        }
    }
}
