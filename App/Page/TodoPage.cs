using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.TodoService;
using Xamarin.Forms;

namespace Heleus.Apps.Todo
{
    public class UnlockedServiceState
    {
        enum States
        {
            Unknown,
            NoUnlocked,
            HasUnlocked
        }

        States _state = States.Unknown;

        void UpdateState(bool hasUnlockedAccount)
        {
            if (hasUnlockedAccount)
                _state = States.HasUnlocked;
            else
                _state = States.NoUnlocked;
        }

        public bool HasUnlockedState => _state == States.HasUnlocked;

        public bool HasNewState()
        {
            var hasUnlockedAccount = ServiceNodeManager.Current.HadUnlockedServiceNode;

            if(_state == States.Unknown)
            {
                UpdateState(hasUnlockedAccount);
                return true;
            }

            if(_state == States.NoUnlocked && hasUnlockedAccount)
            {
                UpdateState(hasUnlockedAccount);
                return true;
            }

            if(_state == States.HasUnlocked && !hasUnlockedAccount)
            {
                UpdateState(hasUnlockedAccount);
                return true;
            }

            return false;
        }
    }


    public class TodoPage : StackPage
    {
        public static async Task<bool> CheckSecretKey(TodoList todoList, SubmitAccount submitAccount, ExtContentPage page)
        {
            if (todoList == null)
                return false;

            if (!todoList.IsLastUsedSecretKey(submitAccount))
            {
                if (!await page.ConfirmAsync("TodoPage.DifferentSecretKeyConfirm"))
                    return false;
            }

            return true;
        }

        readonly UnlockedServiceState _unlockState = new UnlockedServiceState();

        public TodoPage() : base("TodoPage")
        {
            Subscribe<ServiceNodesLoadedEvent>(ServiceNodesLoaded);
            Subscribe<ServiceNodeChangedEvent>(ServiceNodeChanged);
            Subscribe<QueryTodoEvent>(QueryTodo);
            Subscribe<QueryTodoListEvent>(QueryTodoList);
            Subscribe<TodoListRegistrationEvent>(GroupRegistration);
            Subscribe<TodoTaskStatusEvent>(TaskStatusChanged);

            SetupPage();
        }

        Task TaskStatusChanged(TodoTaskStatusEvent arg)
        {
            SetupRecentTasks();
            return Task.CompletedTask;
        }

        async Task NewTodoList(ButtonRow arg)
        {
            await Navigation.PushAsync(new NewTodoListPage());
        }

        async Task ViewTodoList(ButtonRow arg)
        {
            var todoList = arg.Tag as TodoList;

            await Navigation.PushAsync(new TodoListPage(todoList, false));
        }

        void SetupPage()
        {
            if (!_unlockState.HasNewState())
                return;

            StackLayout.Children.Clear();

            AddTitleRow("Title");

            if (!_unlockState.HasUnlockedState)
            {
                AddInfoRow("Info", Tr.Get("App.FullName"));
                AddFooterRow();

                ServiceNodesPage.AddAuthorizeSection(ServiceNodeManager.Current.NewDefaultServiceNode, this, false);
            }
            else
            {
                ToolbarItems.Add(new ExtToolbarItem(Tr.Get("Common.Refresh"), null, async () =>
                {
                    await TodoApp.Current.QueryAllTodoLists();
                }));

                if (!UIAppSettings.AppReady)
                {
                    AddInfoRow("Info", Tr.Get("App.FullName"));
                }

                AddHeaderRow("TodoLists");
                var b = AddButtonRow("NewTodoList", NewTodoList);
                b.SetDetailViewIcon(Icons.Tasks);
                b.RowStyle = Theme.SubmitButton;
                AddFooterRow();

                SetupRecentTasks();
                UIApp.SetupTodoList(this, ViewTodoList);
            }
        }

        void SetupRecentTasks()
        {
            {
                var tasks = new List<Tuple<TodoList, TodoTask>>();
                var ids = new HashSet<long>();
                foreach (var serviceNode in ServiceNodeManager.Current.ServiceNodes)
                {
                    var todo = TodoApp.Current.GetTodo(serviceNode);
                    if (todo != null)
                    {
                        foreach (var todoList in todo.TodoLists)
                        {
                            var listTasks = todoList.GetTasks(TodoTaskStatusTypes.Open, TodoListSortMethod.Ignored);
                            foreach(var task in listTasks)
                            {
                                if(!ids.Contains(task.CurrentTaskStorage.TransactionId))
                                {
                                    tasks.Add(Tuple.Create(todoList, task));
                                    ids.Add(task.CurrentTaskStorage.TransactionId);
                                }
                            }
                        }
                    }
                }

                tasks.Sort((a, b) => b.Item2.CurrentTaskStorage.TransactionId.CompareTo(a.Item2.CurrentTaskStorage.TransactionId));

                if(tasks.Count > 0)
                {
                    var header = GetRow<HeaderRow>("RecentTasks");
                    if (header == null)
                    {
                        AddIndex = GetRow("TodoLists");
                        AddIndexBefore = true;

                        header = AddHeaderRow("RecentTasks");
                        var b = AddButtonRow("NewTask", NewTask);
                        b.SetDetailViewIcon(Icons.Plus);
                        b.RowStyle = Theme.SubmitButton;
                        AddFooterRow();
                    }

                    var rows = GetHeaderSectionRows(header);
                    var rowIndex = 0;

                    AddIndexBefore = false;
                    AddIndex = header;

                    foreach (var task in tasks)
                    {
                        if (!(rowIndex < rows.Count && rows[rowIndex] is ButtonRow button && button.Tag is Tuple<TodoList, TodoTask>))
                        {
                            button = AddButtonRow(null, TaskAction);
                            button.SetDetailViewIcon(Icons.Circle);
                        }

                        if (button.Label.Text != task.Item2.Text)
                            button.Label.Text = task.Item2.Text;
                        button.Tag = task;
                        TodoListPage.UpdateTaskIcon(button, task.Item2);

                        AddIndex = button;
                        ++rowIndex;

                        if (rowIndex >= 4)
                            break;
                    }

                    for (var i = rowIndex; i < rows.Count; i++)
                    {
                        if (rows[i] is ButtonRow button && button.Tag is Tuple<TodoList, TodoTask>)
                        {
                            RemoveView(button);
                        }
                    }
                }
            }
        }

        async Task TaskAction(ButtonRow arg)
        {
            var item = arg.Tag as Tuple<TodoList, TodoTask>;

            await TodoListPage.TaskAction(this, item.Item2, item.Item1);
        }

        async Task NewTask(ButtonRow arg)
        {
            await Navigation.PushAsync(new AddTodoTaskPage(null));
        }

        void Update()
        {
            SetupPage();
            SetupRecentTasks();
            if(UIApp.SetupTodoList(this, ViewTodoList))
            {
                RemoveView(GetRow("Info"));
                if (!UIAppSettings.AppReady)
                {
                    UIAppSettings.AppReady = true;
                    UIApp.Current.SaveSettings();
                }
            }
        }

        Task QueryTodo(QueryTodoEvent arg)
        {
            Update();
            return Task.CompletedTask;
        }

        Task ServiceNodesLoaded(ServiceNodesLoadedEvent arg)
        {
            Update();
            return Task.CompletedTask;
        }

        Task ServiceNodeChanged(ServiceNodeChangedEvent arg)
        {
            Update();
            return Task.CompletedTask;
        }

        Task GroupRegistration(TodoListRegistrationEvent arg)
        {
            Update();
            return Task.CompletedTask;
        }

        Task QueryTodoList(QueryTodoListEvent arg)
        {
            IsBusy = false;

            if(arg.Result == QueryTodoResult.StoredData || arg.Result == QueryTodoResult.LiveData)
            {
                Update();
            }

            return Task.CompletedTask;
        }
    }
}
