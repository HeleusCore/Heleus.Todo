using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Heleus.Apps.Todo;
using Heleus.TodoService;
using Heleus.Transactions;

namespace Heleus.Apps.Shared
{
    public class TodoListSecretErrorView : RowView
    {
        public TodoListSecretErrorView() : base("TodoListSecretErrorView")
        {
            AddLastRow("ImportInfo", Tr.Get("TodoListSecretErrorView.Import"), false);
        }
    }

    public class TodoListDownloadErrorView : RowView
    {
        public TodoListDownloadErrorView() : base("TodoErrorView")
        {
            AddLastRow("RetryInfo", Tr.Get("TodoErrorView.Retry"), false);
        }
    }

    public class TodoListPage : StackPage
    {
        public readonly TodoList TodoList;

        ButtonViewRow<TodoListSecretErrorView> _importSecretButton;
        ButtonViewRow<TodoListDownloadErrorView> _downloadErrorButton;

        readonly List<ButtonRow> _openTasks = new List<ButtonRow>();
        readonly List<ButtonRow> _closedTasks = new List<ButtonRow>();

        readonly bool _edit;
        readonly SubmitAccountButtonRow<GroupSubmitAccount> _submitAccount;
        readonly EntryRow _nameEntry;
        readonly ButtonRow _nameButton;

        async Task NewTask(ButtonRow button)
        {
            await Navigation.PushAsync(new AddTodoTaskPage(TodoList));
        }

        async Task Edit(ButtonRow arg)
        {
            await Navigation.PushAsync(new TodoListPage(TodoList, true));
        }

        public static async Task TaskAction(ExtContentPage page, TodoTask todoTask, TodoList todoList)
        {
            var submitAccount = TodoApp.Current.GetLastUsedSubmitAccount<SubmitAccount>(todoList.ListId.ToString());

            if (submitAccount == null || !todoList.IsLastUsedSecretKey(submitAccount) || todoTask.Status != TodoTaskStatusTypes.Open)
            {
                await page.Navigation.PushAsync(new TodoTaskPage(todoList, todoTask));
            }
            else
            {
                var editItem = Tr.Get("TodoListView.EditItem");
                var markDone = Tr.Get("TodoListView.MarkDone");

                var result = await page.DisplayActionSheet(Tr.Get("TodoListView.ItemTitle"), Tr.Get("Common.Cancel"), null, markDone, editItem);
                if (result == markDone)
                {
                    if (await page.ConfirmTextAsync(Tr.Get("TodoListView.ConfirmStatus")))
                    {
                        page.IsBusy = true;
                        UIApp.Run(() => TodoApp.Current.UpdateTodoItemStatus(submitAccount, TodoTaskStatusTypes.Closed, todoList, todoTask));
                    }
                }
                else if (result == editItem)
                {
                    await page.Navigation.PushAsync(new TodoTaskPage(todoList, todoTask));
                }
            }
        }

        async Task TaskAction(ButtonRow button)
        {
            var task = button.Tag as TodoTask;
            await TaskAction(this, task, TodoList);
        }

        async Task Import(ButtonViewRow<TodoListSecretErrorView> button)
        {
            await Navigation.PushAsync(new MissingSecretKeysPage<GroupSubmitAccount>(TodoList.MissingSecretKeys.Values, TodoList.ServiceNode.GetSubmitAccounts<GroupSubmitAccount>(TodoList.Index)));
        }

        async Task Reload(ButtonViewRow<TodoListDownloadErrorView> arg)
        {
            IsBusy = true;
            await TodoList.DownloadTransactions();
            IsBusy = false;
        }

        async Task Reload(ButtonRow arg)
        {
            IsBusy = true;
            await TodoList.DownloadTransactions();
            IsBusy = false;
        }

        async Task Users(ButtonRow button)
        {
            await Navigation.PushAsync(new TodoListUsersPage(TodoList));
        }

        async Task Invite(ButtonRow button)
        {
            await Navigation.PushAsync(new InvitationPage(TodoList.ServiceNode, TodoList.ListId));
        }

        async Task Delete(ButtonRow button)
        {
            if (await ConfirmAsync("ConfirmDelete"))
            {
                IsBusy = true;
                UIApp.Run(() => TodoApp.Current.DeleteList(_submitAccount.SubmitAccount, TodoList));
            }
        }

        async Task Name(ButtonRow button)
        {
            if (!await TodoPage.CheckSecretKey(TodoList, _submitAccount.SubmitAccount, this))
                return;

            var name = _nameEntry.Edit.Text;
            if (!string.IsNullOrEmpty(name) && !(name == TodoList.Name) && await ConfirmAsync("ConfirmName"))
            {
                IsBusy = true;
                UIApp.Run(() => TodoApp.Current.UpdateListName(_submitAccount.SubmitAccount, TodoList, name));
            }
        }

        public TodoListPage(TodoList todoList, bool edit) : base("TodoListPage")
        {
            Subscribe<QueryTodoListEvent>(QueryTodoList);

            if (edit)
            {
                Subscribe<TodoListNameChangedEvent>(ListName);
                Subscribe<TodoListDeletetEvent>(GroupDeleted);
            }

            TodoList = todoList;
            _edit = edit;


            var header = AddTitleRow(null);
            header.Identifier = "Title";
            var title = TodoApp.GetTodoListName(todoList);
            header.Label.Text = title;
            SetTitle(title);

            UpdateSecretKeyButton();

            var items = todoList.GetTasks(TodoTaskStatusTypes.Open, TodoListSortMethod.ByTimestampDesc);

            AddHeaderRow("OpenTasks");

            if (items.Count > 0)
            {
                foreach (var item in items)
                {
                    var b = AddTaskButtonRow(item);
                    _openTasks.Add(b);
                }
            }
            else
            {
                AddInfoRow("NoOpenTasks");
            }

            AddFooterRow();

            if (!_edit)
            {
                AddHeaderRow("More");

                var button = AddButtonRow("TodoListView.Add", NewTask);
                //add.Margin = new Thickness(40, 0, 0, 0);
                button.RowStyle = Theme.SubmitButton;
                button.FontIcon.IsVisible = false;
                button.SetDetailViewIcon(Icons.Plus);
                //add.IsEnabled = !todoList.HasMissingSecretKeys;

                button = AddButtonRow("Edit", Edit);
                button.SetDetailViewIcon(Icons.Pencil);

                button = AddButtonRow("Reload", Reload);
                button.SetDetailViewIcon(Icons.Sync);
                AddFooterRow();

            }

            if (_edit)
            {
                items = todoList.GetTasks(TodoTaskStatusTypes.Closed, TodoListSortMethod.ByTimestampDesc);

                AddHeaderRow("ClosedTasks");
                if (items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        var b = AddTaskButtonRow(item);
                        _closedTasks.Add(b);
                    }
                }
                else
                {
                    AddInfoRow("NoClosedTasks");
                }
                AddFooterRow();

                AddHeaderRow("UsersSection");

                var button = AddButtonRow("ViewUsers", Users);
                button.SetDetailViewIcon(Icons.Users);
                button = AddButtonRow("Invite", Invite);
                button.SetDetailViewIcon(Icons.UserPlus);

                AddFooterRow();

                AddHeaderRow("NameHeader");

                _nameEntry = AddEntryRow(todoList.Name, "Name");
                _nameEntry.SetDetailViewIcon(Icons.Pencil);
                _nameButton = AddSubmitButtonRow("NameButton", Name);
                _nameButton.RowStyle = Theme.SubmitButton;

                Status.AddBusyView(_nameEntry.Edit);
                Status.AddBusyView(_nameButton);
                AddFooterRow();

                AddHeaderRow("Common.SubmitAccount");
                _submitAccount = AddRow(new SubmitAccountButtonRow<GroupSubmitAccount>(this, () => todoList.ServiceNode.GetSubmitAccounts<GroupSubmitAccount>(todoList.Index), todoList.ListId.ToString()));
                AddInfoRow("Common.SubmitAccountInfo");
                AddFooterRow();

                AddHeaderRow("DeleteHeader");

                var del = AddButtonRow("DeleteButton", Delete);
                del.RowStyle = Theme.CancelButton;
                del.SetDetailViewIcon(Icons.TrashAlt);

                Status.AddBusyView(del);

                AddFooterRow();
            }
        }

        async Task GroupDeleted(TodoListDeletetEvent arg)
        {
            if (arg.ListId == TodoList.ListId)
            {
                IsBusy = false;

                var result = arg.Result;
                if (result.TransactionResult == TransactionResultTypes.Ok)
                {
                    await MessageAsync("SuccessDelete");
                    await Navigation.PopAsync();
                }
                else
                {
                    await ErrorTextAsync(result.GetErrorMessage());
                }
            }
        }


        async Task ListName(TodoListNameChangedEvent arg)
        {
            if (arg.ListId == TodoList.ListId)
            {
                IsBusy = false;

                var result = arg.Result;
                if (result.TransactionResult == TransactionResultTypes.Ok)
                {
                    UpdateList();
                    await MessageAsync("SuccessName");
                }
                else
                {
                    await ErrorTextAsync(result.GetErrorMessage());
                }
            }
        }

        Task QueryTodoList(QueryTodoListEvent arg)
        {
            if(arg.TodoList == TodoList && (arg.Result == QueryTodoResult.StoredData || arg.Result == QueryTodoResult.LiveData))
            {
                UpdateList();
            }
            else
            {
                UpdateButtons();
            }

            return Task.CompletedTask;
        }

        ButtonRow AddTaskButtonRow(TodoTask task)
        {
            var i = AddButtonRow(null, TaskAction);

            //i.Margin = new Thickness(40, 0, 0, 0);
            i.Label.Text = task.Text;
            i.SetDetailViewIcon(Icons.CircleCheck);
            i.Tag = task;

            UpdateTaskIcon(i, task);

            return i;
        }

        void UpdateButtons()
        {
            foreach (var button in _openTasks)
            {
                var task = (button.Tag as TodoTask);
                if (task != null)
                {
                    if (button.Label.Text != task.Text)
                        button.Label.Text = task.Text;
                    UpdateTaskIcon(button, task);
                }
            }

            UpdateSecretKeyButton();
        }

        void UpdateSecretKeyButton()
        {
            if(!TodoList.LastDownloadResult.Ok)
            {
                if (_downloadErrorButton == null)
                {
                    AddIndex = GetRow("Title");
                    _downloadErrorButton = AddButtonViewRow(new TodoListDownloadErrorView(), Reload);
                    _downloadErrorButton.RowStyle = Theme.CancelButton;
                    AddIndex = null;
                }
            }
            else
            {
                if(_downloadErrorButton != null)
                {
                    RemoveView(_downloadErrorButton);
                    _downloadErrorButton = null;
                }
            }

            var secretKeyMissing = TodoList.HasMissingSecretKeys;
            if (secretKeyMissing && _importSecretButton == null)
            {
                AddIndex = GetRow("Title");
                _importSecretButton = AddButtonViewRow(new TodoListSecretErrorView(), Import);
                _importSecretButton.RowStyle = Theme.CancelButton;
                AddIndex = null;
            }

            if (!secretKeyMissing && _importSecretButton != null)
            {
                RemoveView(_importSecretButton);
                _importSecretButton = null;
            }
        }

        public static void UpdateTaskIcon(ButtonRow button, TodoTask item)
        {
            var icon = button.DetailView as FontIcon;
            if (icon != null)
            {
                var newIcon = Icons.Circle;
                if (item.Status == TodoTaskStatusTypes.Closed)
                    newIcon = Icons.CircleCheck;
                else if (item.Status == TodoTaskStatusTypes.Deleted)
                    newIcon = Icons.CircleTimes;

                if (icon.Icon != newIcon)
                    icon.Icon = newIcon;
            }
        }

        void UpdateList()
        {
            UpdateButtons();

            var header = GetRow<HeaderRow>("Title");
            header.Label.Text = TodoApp.GetTodoListName(TodoList);

            UpdateTasks(TodoTaskStatusTypes.Open, "OpenTasks", _openTasks, "NoOpenTasks");
            if(_edit)
                UpdateTasks(TodoTaskStatusTypes.Closed, "ClosedTasks", _closedTasks, "NoClosedTasks");
        }

        void UpdateTasks(TodoTaskStatusTypes status, string headerName, List<ButtonRow> taskButtons, string noIdentifier)
        {
            var header = GetRow<HeaderRow>(headerName);
            var tasks = TodoList.GetTasks(status, TodoListSortMethod.ByTimestampDesc);

            if (tasks.Count > 0)
            {
                RemoveView(GetRow(noIdentifier));

                var diff = ListDiff.Compute(taskButtons, tasks, (a, b) => (a.Tag as TodoTask).Id == b.Id);
                diff.Process(taskButtons, tasks,
                (row) =>
                {
                    RemoveView(row);
                    return true;
                },
                (idx, item) =>
                {
                    AddIndexBefore = false;
                    if (idx == 0)
                        AddIndex = header;
                    else
                        AddIndex = taskButtons[idx - 1];

                    var b = AddTaskButtonRow(item);
                    taskButtons.Insert(idx, b);
                });
            }
            else
            {
                taskButtons.Clear();
                ClearHeaderSection(headerName);

                AddIndex = header;
                AddIndexBefore = false;
                AddInfoRow(noIdentifier);
            }
        }
    }
}
