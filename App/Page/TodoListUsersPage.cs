using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Chain;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public class TodoListUsersPage : StackPage
    {
        readonly TodoList _todoList;

        async Task User(ProfileButtonRow button)
        {
            var accountId = button.AccountId;
            var pending = (bool)button.Tag;

            var profile = T("Profile");
            var remove = T("Remove");
            var invite = T("Reinvite");

            var items = new List<string> { profile };

            if (accountId != _todoList.ServiceNode.AccountId)
                items.Add(remove);
            if (pending)
                items.Add(invite);

            var result = await DisplayActionSheet(Tr.Get("Common.Action"), Tr.Get("Common.Cancel"), null, items.ToArray());
            if(result == profile)
            {
                await Navigation.PushAsync(new ViewProfilePage(accountId));
            }
            else if (result == remove)
            {
                if(await ConfirmAsync("DeleteConfirm"))
                {
                    IsBusy = true;
                    UIApp.Run(() => TodoApp.Current.DeleteListUser(_todoList.ServiceNode.GetSubmitAccounts<SubmitAccount>(TodoServiceInfo.TodoSubmitIndex).FirstOrDefault(), _todoList, accountId));
                    //_ = HeleusApp.DeleteListUser(_todoList.ListId, user.AccountId);
                }
            }
            else if (result == invite)
            {
                await Navigation.PushAsync(new InvitationPage(_todoList.ServiceNode, _todoList.ListId, accountId));
            }
        }

        async Task AccountDeleted(TodoListAccountDeleteEvent arg)
        {
            IsBusy = false;
            var result = arg.Result;

            if(result.TransactionResult == TransactionResultTypes.Ok)
            {
                var rows = GetHeaderSectionRows("ActiveUsers");
                rows.AddRange(GetHeaderSectionRows("PendingUsers"));
                foreach(var row in rows)
                {
                    if(row is ProfileButtonRow buttonRow)
                    {
                        if(buttonRow.AccountId == arg.AccountId)
                        {
                            RemoveView(row);
                            break;
                        }
                    }
                }

                await MessageAsync("DeleteSuccess");
            }
            else
            {
                await ErrorTextAsync(result.GetErrorMessage());
            }
        }

        async Task Invite(ButtonRow button)
        {
            await Navigation.PushAsync(new InvitationPage(_todoList.ServiceNode, _todoList.ListId));
        }

        public TodoListUsersPage(TodoList todoList) : base("TodoListUsersPage")
        {
            Subscribe<TodoListAccountDeleteEvent>(AccountDeleted);
            _todoList = todoList;

            AddTitleRow("Title");

            IsBusy = true;
        }

        public override async Task InitAsync()
        {
            var accounts = await GroupAdministration.DownloadAccounts(_todoList.ServiceNode.Client, ChainType.Data, _todoList.ServiceNode.ChainId, TodoServiceInfo.GroupChainIndex, _todoList.ListId);
            var pendingAccounts = await GroupAdministration.DownloadPendingAccounts(_todoList.ServiceNode.Client, ChainType.Data, _todoList.ServiceNode.ChainId, TodoServiceInfo.GroupChainIndex, _todoList.ListId);

            IsBusy = false;

            if(accounts?.Item == null || pendingAccounts?.Item == null)
            {
                InfoFrame("DownloadFailed");
                return;
            }

            if (accounts.Item.Count > 0)
            {
                AddHeaderRow("ActiveUsers");
                foreach (var account in accounts.Item)
                {
                    var b = AddRow(new ProfileButtonRow(account.Key, User));
                    b.Tag = false;
                    Status.AddBusyView(b);
                }
                AddFooterRow();
            }

            if(pendingAccounts.Item.Count > 0)
            {
                AddHeaderRow("PendingUsers");
                foreach (var account in pendingAccounts.Item)
                {
                    var b = AddRow(new ProfileButtonRow(account.Key, User));
                    b.Tag = true;
                    Status.AddBusyView(b);
                }

                AddFooterRow();
            }

            AddHeaderRow("Invite");

            var invite = AddButtonRow("InviteButton", Invite);
            invite.RowStyle = Theme.SubmitButton;

            Status.AddBusyView(invite);

            AddFooterRow();
        }
    }
}
