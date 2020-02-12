using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Chain.Data;
using Heleus.TodoService;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public class NewTodoListPage : StackPage
    {
        readonly SubmitAccountButtonRow _submitAccount;

        public NewTodoListPage() : base("NewTodoListPage")
        {
            Subscribe<TodoListRegistrationEvent>(TodoListRegistration);

            AddTitleRow("Title");

            AddInfoRow("TodoPage.Info", Tr.Get("App.FullName"));

            AddSubmitRow("Submit", NewTodoList);

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow(this, () => ServiceNodeManager.Current.GetSubmitAccounts<SubmitAccount>(TodoServiceInfo.TodoSubmitIndex)));
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();
        }

        async Task TodoListRegistration(TodoListRegistrationEvent arg)
        {
            IsBusy = false;

            if (arg.ListId != GroupAdministrationInfo.InvalidGroupId)
            {
                await MessageAsync("Success");
                await Navigation.PopAsync();
            }
            else
            {
                await ErrorTextAsync(arg.Result.GetErrorMessage());
            }
        }

        async Task NewTodoList(ButtonRow arg)
        {
            if (!await ConfirmAsync("Confirm"))
                return;

            IsBusy = true;
            UIApp.Run(() => TodoApp.Current.RegisterNewList(_submitAccount.SubmitAccount));
        }
    }
}
