using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Transactions;

namespace Heleus.Apps.Todo
{
    public class AddTodoTaskPage : StackPage
    {
        readonly EditorRow _editor;
        readonly SubmitAccountButtonRow<TodoListSubmitAccount> _submitAccount;

        readonly SelectionRow<TodoList> _listSelection;

        async Task Submit(ButtonRow button)
        {
            var todoList = _listSelection.Selection;

            if (!await TodoPage.CheckSecretKey(todoList, _submitAccount.SubmitAccount, this))
                return;

            if (!await ConfirmAsync("Confirm"))
                return;

            IsBusy = true;
            var text = _editor.Edit.Text;
            UIApp.Run(() => TodoApp.Current.AddUpdateTodoItem(_submitAccount.SubmitAccount, text, todoList, null));
        }

        async Task TodoTask(NewTodoTaskEvent arg)
        {
            IsBusy = false;

            if(arg.Result.TransactionResult == TransactionResultTypes.Ok)
            {
                await MessageAsync("Success");
                await Navigation.PopAsync();
            }
            else
            {
                await ErrorTextAsync(arg.Result.GetErrorMessage());
            }
        }

        public AddTodoTaskPage(TodoList todoList) : base("AddTodoTaskPage")
        {
            Subscribe<NewTodoTaskEvent>(TodoTask);

            AddTitleRow("Title");

            AddHeaderRow("TodoList");

            var selectionList = new SelectionItemList<TodoList>();
            foreach(var serviceNode in ServiceNodeManager.Current.ServiceNodes)
            {
                var todo = TodoApp.Current.GetTodo(serviceNode);
                if(todo != null)
                {
                    foreach(var list in todo.TodoLists)
                    {
                        selectionList.Add(new SelectionItem<TodoList>(list, TodoApp.GetTodoListName(list)));
                    }
                }
            }

            _listSelection = AddSelectionRows(selectionList, todoList);
            _listSelection.SelectionChanged = SelectionChanged;

            Status.Add(T("ListStatus"), (sv) =>
            {
                return _listSelection.Selection != null;
            });

            AddFooterRow();

            _editor = AddEditorRow("", "Text");
            _editor.SetDetailViewIcon(Icons.Pencil);

            FocusElement = _editor.Edit;

            Status.Add(_editor.Edit, T("TextStatus"), (sv, edit, newText, oldText) =>
            {
                return !string.IsNullOrWhiteSpace(newText) && newText.Length >= 2;
            });


            AddSubmitRow("Submit", Submit);

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow<TodoListSubmitAccount>(null, this, SelectSubmitAccount));
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();
            SelectionChanged(todoList);

            Status.Add(T("SubmitAccountStatus"), (sv) =>
            {
                return _submitAccount.SubmitAccount != null;
            });
        }

        Task SelectionChanged(TodoList todoList)
        {
            if (todoList != null)
            {
                _submitAccount.SubmitAccount = TodoApp.Current.GetLastUsedSubmitAccount<TodoListSubmitAccount>(todoList.ListId.ToString());
            }
            else
            {
                _submitAccount.SubmitAccount = null;
            }
            Status.ReValidate();

            return Task.CompletedTask;
        }

        async Task SelectSubmitAccount(SubmitAccountButtonRow<TodoListSubmitAccount> arg)
        {
            var todoList = _listSelection.Selection;
            if (todoList != null)
            {
                await Navigation.PushAsync(new SubmitAccountsPage<TodoListSubmitAccount>(todoList.ServiceNode.GetSubmitAccounts<TodoListSubmitAccount>(todoList.Index), (submitAccount) =>
                {
                    arg.SubmitAccount = submitAccount;
                    TodoApp.Current.SetLastUsedSubmitAccount(submitAccount, todoList.ListId.ToString());
                    Status.ReValidate();
                }));
            }
        }
    }
}
