using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Operations;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public class InvitationView : RowView
    {
        public InvitationView(string name, long ownerId) : base("InvitationView")
        {
            AddRow("Name", name);
            AddLastRow("AccountId", ownerId.ToString());
        }
    }

    public class HandleInvitationPage : StackPage
    {
        readonly InvitationSchemeAction _invitation;
        readonly ServiceNode _serviceNode;
        SubmitAccountButtonRow _submitAccount;

        async Task Profile(ButtonRow button)
        {
             await Navigation.PushAsync(new ViewProfilePage(_invitation.SenderAccountId));
        }

        async Task Submit(ButtonRow button)
        {
            if (await ConfirmAsync("SubmitConfirm"))
            {
                IsBusy = true;
                var submitAccount = _submitAccount.SubmitAccount;

                submitAccount.SecretKeyManager.AddSecretKey(TodoList.BuildIndex(_invitation.ListId), _invitation.SecretKey);
                UIApp.Run(() => TodoApp.Current.AcceptListInvitation(submitAccount, _invitation.ListId, _invitation.SecretKey));
            }
        }

        async Task InvitationAccepted(TodoListInvitationAcceptedEvent arg)
        {
            IsBusy = false;

            var result = arg.Result;

            var featureError = Feature.GetFeatureError<GroupAdministrationError>(result.UserCode);
            if (result.TransactionResult == TransactionResultTypes.Ok || featureError == GroupAdministrationError.GroupAlreadyAdded)
            {
                await MessageAsync("Success");
                await Navigation.PopAsync();
            }
            else
            {
                await ErrorTextAsync(result.GetErrorMessage());
            }
        }

        public HandleInvitationPage(ServiceNode serviceNode, InvitationSchemeAction invitation) : base("HandleInvitationPage")
        {
            Subscribe<TodoListInvitationAcceptedEvent>(InvitationAccepted);

            _invitation = invitation;
            _serviceNode = serviceNode;

            AddTitleRow("Title");

            IsBusy = true;
        }

        public override async Task InitAsync()
        {
            if (!IsBusy)
                return;

            var name = TodoApp.GetTodoListName(null);

            var nameId = await Group.DownloadIndexLastTransactionInfo(_serviceNode.Client, Chain.ChainType.Data, _serviceNode.ChainId, TodoServiceInfo.TodoDataChainIndex, _invitation.ListId, TodoServiceInfo.TodoListNameIndex);
            if(nameId?.Item?.TransactionId != Operation.InvalidTransactionId)
            {
                var nameTransaction = (await _serviceNode.Client.DownloadDataTransactionItem(_serviceNode.ChainId, TodoServiceInfo.TodoDataChainIndex, nameId.Item.TransactionId)).Data?.Transaction;
                if (nameTransaction != null)
                {
                    var encryptedRecord = TodoList.GetEncrytpedTodoRedord<TodoListNameRecord>(nameTransaction);
                    var record = await encryptedRecord.GetRecord(_invitation.SecretKey);

                    if (record != null)
                    {
                        name = record.Name;
                    }
                }
            }

            IsBusy = false;

            AddViewRow(new InvitationView(name, _invitation.SenderAccountId));
            AddButtonRow("Profile", Profile);

            AddSubmitRow("Submit", Submit);

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow(this, () => _serviceNode.GetSubmitAccounts<SubmitAccount>(TodoServiceInfo.TodoSubmitIndex)));
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();
        }
    }
}
