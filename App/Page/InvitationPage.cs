using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Base;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public class InvitationPage : StackPage
    {
        readonly SelectionRow<TodoList> _listSelection;
        readonly SubmitAccountButtonRow<GroupSubmitAccount> _submitAccount;
        readonly EntryRow _accountId;
        readonly EntryRow _password;

        readonly ProfileButtonRow _profile;

        async Task Profile(ProfileButtonRow button)
        {
            if(long.TryParse(_accountId.Edit.Text, out var id))
                await Navigation.PushAsync(new ViewProfilePage(id));
        }

        async Task Submit(ButtonRow button)
        {
            var todoList = _listSelection.Selection;
            var submitAccount = _submitAccount.SubmitAccount;

            if (!await TodoPage.CheckSecretKey(todoList, submitAccount, this))
                return;

            IsBusy = true;

            try
            {
                var list = _listSelection.Selection;
                var accountid = long.Parse(_accountId.Edit.Text);

                UIApp.Run(() => TodoApp.Current.InviteToList(submitAccount, todoList, accountid));
            }
            catch(Exception ex)
            {
                Log.IgnoreException(ex);
            }
        }

        async Task InvitationResult(TodoListInvitationSentEvent arg)
        {
            IsBusy = false;

            var result = arg.Result;

            var featureError = Feature.GetFeatureError<GroupAdministrationError>(result.UserCode);
            if(result.TransactionResult == TransactionResultTypes.Ok || featureError == GroupAdministrationError.GroupAlreadyPending)
            {
                await Navigation.PushAsync(new InvitationResultPage(GetRequestUrl()));
            }
            else
            {
                await ErrorTextAsync(result.GetErrorMessage());
            }
        }

        string GetRequestUrl()
        {
            try
            {
                var todoList = _listSelection.Selection;
                var submitAccount = _submitAccount.SubmitAccount;
                var accountId = long.Parse(_accountId.Edit.Text);
                var senderAccountId = submitAccount.ServiceNode.AccountId;
                var secretKey = submitAccount.DefaultSecretKey;

                var password = _password.Edit.Text;
                var encrypted = !string.IsNullOrWhiteSpace(password);

                string secretKeyHex;
                if (encrypted)
                    secretKeyHex = secretKey.ExportSecretKey(password + todoList.ListId + accountId + senderAccountId).HexString;
                else
                    secretKeyHex = secretKey.ExportSecretKey(todoList.ListId.ToString() + accountId + senderAccountId).HexString;

                return TodoApp.Current.GetRequestCode(submitAccount.ServiceNode,
                    TodoServiceInfo.GroupChainIndex,
                    InvitationSchemeAction.ActionName,
                    todoList.ListId,
                    accountId,
                    submitAccount.ServiceNode.AccountId,
                    encrypted ? 1 : 0,
                    secretKeyHex);
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
            }

            return string.Empty;
        }

        public InvitationPage(ServiceNode serviceNode, long todoListId, long accountId = 0) : base("InvitationPage")
        {
            Subscribe<TodoListInvitationSentEvent>(InvitationResult);

            EnableStatus();

            AddTitleRow("Title");

            var todo = TodoApp.Current.GetTodo(serviceNode);
            if(todo.TodoLists.Count == 0)
            {
                AddInfoRow("NoLists");
                return;
            }

            AddHeaderRow("List");

            TodoList @default = null;
            var listSelection = new SelectionItemList<TodoList>();
            foreach (var list in todo.TodoLists)
            {
                listSelection.Add(new SelectionItem<TodoList>(list, TodoApp.GetTodoListName(list)));
                if (list.ListId == todoListId)
                    @default = list;
            }

            _listSelection = AddSelectionRows(listSelection, @default);
            _listSelection.SelectionChanged = SelectionChanged;

            Status.Add(T("ListStatus"), (sv) => {
                return _listSelection.Selection != null;
            });

            AddFooterRow();

            AddHeaderRow("AccountId");

            _accountId = AddEntryRow(accountId > 0 ? accountId.ToString() : null, "AccountId");
            Status.Add(_accountId.Edit, T("AccountStatus"), (sv, edit, n, o) =>
            {
                var valid = StatusValidators.PositiveNumberValidator(sv, edit, n, o);
                if (_profile != null)
                {
                    _profile.IsEnabled = valid;
                    if (valid)
                        _profile.AccountId = long.Parse(n);
                    else
                        _profile.AccountId = 0;
                }
                return valid;
            });

            _profile = AddRow(new ProfileButtonRow(0, Profile));
            _profile.IsEnabled = accountId > 0;
            Status.AddBusyView(_profile);

            AddFooterRow();

            AddIndex = AddSubmitRow("Submit", Submit);
            AddIndexBefore = true;

            _password = AddPasswordRow("", "Password");
            Status.Add(_password.Edit, T("PasswordStatus"), (sv, edit, newtext, oldtext) => true);

            AddIndex = null;
            AddIndexBefore = false;

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow<GroupSubmitAccount>(null, this, SelectSubmitAccount));
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();

            Status.Add(T("SubmitAccountStatus"), (sv) =>
            {
                return _submitAccount.SubmitAccount != null;
            });

            SelectionChanged(@default);
        }

        Task SelectionChanged(TodoList todoList)
        {
            if(todoList != null)
            {
                _submitAccount.SubmitAccount = TodoApp.Current.GetLastUsedSubmitAccount<GroupSubmitAccount>(todoList.ListId.ToString());
            }
            else
            {
                _submitAccount.SubmitAccount = null;
            }
            Status.ReValidate();

            return Task.CompletedTask;
        }

        async Task SelectSubmitAccount(SubmitAccountButtonRow<GroupSubmitAccount> arg)
        {
            var todoList = _listSelection.Selection;
            if (todoList != null)
            {
                await Navigation.PushAsync(new SubmitAccountsPage<GroupSubmitAccount>(todoList.ServiceNode.GetSubmitAccounts<GroupSubmitAccount>(), (submitAccount) =>
                {
                    arg.SubmitAccount = submitAccount;
                    Status.ReValidate();
                }));
            }
        }
    }
}
