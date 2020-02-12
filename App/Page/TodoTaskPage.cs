using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Base;
using Heleus.Network.Client.Record;
using Heleus.TodoService;
using Heleus.Transactions;

namespace Heleus.Apps.Todo
{
    public class TodoItemHistoryView : RowView
    {
        public TodoItemHistoryView(IRecordStorage storage) : base("TodoHistoryView")
        {
            if(storage.GetRecordType() == TodoRecordTypes.Task)
            {
                AddRow("Item", (storage as TodoRecordStorage<TodoTaskRecord>).Record.Text);
            }
            else if (storage.GetRecordType() == TodoRecordTypes.TaskStatus)
            {
                AddRow("Status", Tr.Get("ItemStatusTypes." + (storage as TodoRecordStorage<TodoTaskStatusRecord>).Record.Status));
            }

            AddLastRow("Timestamp", Time.DateTimeString(storage.Timestamp));
        }
    }

    public class TodoTaskPage : StackPage
    {
        readonly ButtonRow _textButton;
        readonly EditorRow _text;

        readonly ButtonRow _statusButton;
        readonly SelectionRow<TodoTaskStatusTypes> _status;

        readonly TodoList _todoList;
        readonly TodoTask _task;

        readonly SubmitAccountButtonRow<GroupSubmitAccount> _submitAccount;

        readonly HeaderRow _history;
        readonly HeaderRow _transactionInfo;

        async Task BuildHistory()
        {
            var items = _task.GetAllTransactionIds();

            AddIndex = _history;

            foreach(var id in items)
            {
                var transactionData = await _todoList.ServiceNode.GetTransactionDownloadManager(TodoServiceInfo.TodoDataChainIndex).DownloadTransaction(id);
                if(transactionData.Ok && transactionData.Transactions.Count == 1)
                {
                    var storage = _todoList.LoadTodoStorage(transactionData.Transactions[0]);
                    if(storage != null)
                    {
                        var b = AddViewRow(new TodoItemHistoryView(storage));
                        AddIndex = b;
                    }
                }
            }

            AddIndex = null;
            _ = BuildTransactionInfo();
        }

        async Task BuildTransactionInfo()
        {
            AddIndex = _transactionInfo;

            var transactionData = await _todoList.ServiceNode.GetTransactionDownloadManager(TodoServiceInfo.TodoDataChainIndex).DownloadTransaction(_task.CurrentTaskStorage.TransactionId);
            if(transactionData.Ok && transactionData.Transactions.Count == 1)
            {
                var transaction = transactionData.Transactions[0].Transaction;
                AddIndex = AddViewRow(new DataTransactionView(transaction));

                var encryptedRecord = TodoList.GetEncrytpedTodoRedord<TodoTaskRecord>(transaction);
                if(encryptedRecord != null)
                {
                    AddIndex = AddFooterRow();

                    AddIndex = AddHeaderRow("SecretKeyInfo");
                    AddViewRow(new SecretKeyView(encryptedRecord.KeyInfo, true));
                }
            }

            AddIndex = null;
        }

        async Task Submit(ButtonRow button)
        {
            if (await ConfirmAsync("ConfirmText"))
            {
                if (!await TodoPage.CheckSecretKey(_todoList, _submitAccount.SubmitAccount, this))
                    return;

                IsBusy = true;

                var text = _text.Edit.Text;
                UIApp.Run(() => TodoApp.Current.AddUpdateTodoItem(_submitAccount.SubmitAccount, text, _todoList, _task));
            }
        }

        async Task TodoItem(NewTodoTaskEvent arg)
        {
            IsBusy = false;

            if (arg.Result.TransactionResult == TransactionResultTypes.Ok)
            {
                _textButton.IsEnabled = false;
                await MessageAsync("TextSuccess");
            }
            else
            {
                await ErrorTextAsync(arg.Result.GetErrorMessage());
            }
        }

        async Task StatusButton(ButtonRow button)
        {
            if (await ConfirmAsync("ConfirmStatus"))
            {
                IsBusy = true;
                UIApp.Run(() => TodoApp.Current.UpdateTodoItemStatus(_submitAccount.SubmitAccount, _status.Selection, _todoList, _task));
            }
        }

        async Task TodoItemStatus(TodoTaskStatusEvent arg)
        {
            IsBusy = false;
            var result = arg.Result;

            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                if (arg.ItemStatus == TodoTaskStatusTypes.Deleted)
                {
                    await MessageAsync("StatusDelete");
                    await Navigation.PopAsync();
                }
                else
                {
                    _statusButton.IsEnabled = false;
                    await MessageAsync("StatusSuccess");
                }
            }
            else
            {
                await ErrorTextAsync(arg.Result.GetErrorMessage());
            }
        }

        async Task Delete(ButtonRow button)
        {
            if (await ConfirmAsync("ConirmDelete"))
            {
                IsBusy = true;

                UIApp.Run(() => TodoApp.Current.UpdateTodoItemStatus(_submitAccount.SubmitAccount, TodoTaskStatusTypes.Deleted, _todoList, _task));
            }
        }

        Task StatusChanged(TodoTaskStatusTypes obj)
        {
            _statusButton.IsEnabled = obj != _task.Status;

            return Task.CompletedTask;
        }

        public TodoTaskPage(TodoList todoList, TodoTask task) : base("TodoTaskPage")
        {
            Subscribe<NewTodoTaskEvent>(TodoItem);
            Subscribe<TodoTaskStatusEvent>(TodoItemStatus);

            _todoList = todoList;
            _task = task;

            AddTitleRow("Title");

            AddHeaderRow("StatusHeader");

            var statusItems = new SelectionItemList<TodoTaskStatusTypes>
            {
                new SelectionItem<TodoTaskStatusTypes>(TodoTaskStatusTypes.Open, Tr.Get("ItemStatusTypes.Open")),
                new SelectionItem<TodoTaskStatusTypes>(TodoTaskStatusTypes.Closed, Tr.Get("ItemStatusTypes.Closed"))
            };

            _status = AddSelectionRows(statusItems, task.Status);
            _status.SelectionChanged = StatusChanged;

            _statusButton = AddSubmitButtonRow("SubmitStatus", StatusButton);
            _statusButton.RowStyle = Theme.SubmitButton;
            _statusButton.IsEnabled = false;

            _status.Buttons[0].SetDetailViewIcon(Icons.Circle);
            _status.Buttons[1].SetDetailViewIcon(Icons.CircleCheck);

            foreach (var b in _status.Buttons)
                Status.AddBusyView(b);
            Status.AddBusyView(_statusButton);

            AddFooterRow();

            if (task.Status == TodoTaskStatusTypes.Open)
            {
                AddHeaderRow("ItemHeader");

                _text = AddEditorRow(task.Text, "Text");
                _text.SetDetailViewIcon(Icons.Pencil);
                _text.Edit.TextChanged += Edit_TextChanged;

                _textButton = AddSubmitButtonRow("SubmitText", Submit);
                _textButton.RowStyle = Theme.SubmitButton;
                _textButton.IsEnabled = false;

                Status.AddBusyView(_text.Edit);
                Status.AddBusyView(_textButton);

                AddFooterRow();
            }

            _history = AddHeaderRow("HistoryHeader");
            AddFooterRow();

            _transactionInfo = AddHeaderRow("TransactionInfo");
            AddFooterRow();

            AddHeaderRow("Common.SubmitAccount");
            _submitAccount = AddRow(new SubmitAccountButtonRow<GroupSubmitAccount>(this, () => todoList.ServiceNode.GetSubmitAccounts<GroupSubmitAccount>(todoList.Index), todoList.ListId.ToString()));
            AddInfoRow("Common.SubmitAccountInfo");
            AddFooterRow();

            AddHeaderRow("DeleteHeader");

            var delete = AddButtonRow("SubmitDelete", Delete);
            delete.RowStyle = Theme.CancelButton;
            delete.SetDetailViewIcon(Icons.TrashAlt);

            Status.AddBusyView(delete);

            AddFooterRow();

            _ = BuildHistory();
        }

        void Edit_TextChanged(object sender, Xamarin.Forms.TextChangedEventArgs e)
        {
            var newText = e.NewTextValue;
            _textButton.IsEnabled = !string.IsNullOrWhiteSpace(newText) && newText.Length >= 2 && !(_task.Text == newText);
        }
    }
}
