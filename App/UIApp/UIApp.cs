using System;
using Xamarin.Forms;
using Heleus.Base;
using SkiaSharp;
using System.Threading.Tasks;
using Heleus.Apps.Todo;
using Heleus.TodoService;
using Heleus.ProfileService;
using Heleus.Network.Client;
#if !(GTK || CLI)
using SkiaSharp.Views.Forms;
#endif

namespace Heleus.Apps.Shared
{
    partial class UIApp : Application
	{
        public MenuPage MenuPage { get; private set; }
        readonly UnlockedServiceState _unlockState = new UnlockedServiceState();

        public static void NewContentPage(ExtContentPage contentPage)
		{
			if (IsGTK)
				return;

			if (!(contentPage is UWPMenuPage || contentPage is DesktopMenuPage || contentPage is IOSMenuPage))
				contentPage.EnableSkiaBackground();
		}

		public static void UpdateBackgroundCanvas(SKCanvas canvas, int width, int height)
		{
			try
			{
#if !(GTK || CLI)
				var colors = new SKColor[] { Theme.PrimaryColor.Color.ToSKColor(), Theme.SecondaryColor.Color.ToSKColor() };
				var positions = new float[] { 0.0f, 1.0f };

				var gradient = SKShader.CreateLinearGradient(new SKPoint(0, height / 2), new SKPoint(width, height / 2), colors, positions, SKShaderTileMode.Mirror);
				var paint = new SKPaint { Shader = gradient, IsAntialias = true };

				canvas.DrawPaint(paint);
#endif
			}
			catch (Exception ex)
			{
				Log.IgnoreException(ex);
			}
		}

        public static bool UIAppUsesPushNotifications = false;

        void Init()
        {
            SchemeAction.SchemeParser = (host, segments) =>
            {
                var action = string.Empty;
                var startIndex = 0;

                if (host == "heleuscore.com" && segments[1] == "todo/")
                {
                    if (segments[2] == "request/")
                    {
                        action = SchemeAction.GetString(segments, 3);
                        startIndex = 4;
                    }
                }

                return new Tuple<string, int>(action, startIndex);
            };

            SchemeAction.RegisterSchemeAction<RequestInvitationSchemeAction>();
            SchemeAction.RegisterSchemeAction<InvitationSchemeAction>();

            var sem = new ServiceNodeManager(TodoServiceInfo.ChainId, TodoServiceInfo.EndPoint, TodoServiceInfo.Version, TodoServiceInfo.Name, _currentSettings, _currentSettings, PubSub, TransactionDownloadManagerFlags.DecrytpedTransactionStorage);
            TodoApp.Current.Init();
            _ = new ProfileManager(new ClientBase(sem.HasDebugEndPoint ? sem.DefaultEndPoint : ProfileServiceInfo.EndPoint, ProfileServiceInfo.ChainId), sem.CacheStorage, PubSub);

            var masterDetail = new ExtMasterDetailPage();
            var navigation = new ExtNavigationPage(new TodoPage());

            if (IsAndroid)
                MenuPage = new AndroidMenuPage(masterDetail, navigation);
            else if (IsUWP)
                MenuPage = new UWPMenuPage(masterDetail, navigation);
            else if (IsDesktop)
                MenuPage = new DesktopMenuPage(masterDetail, navigation);
            else if (IsIOS)
                MenuPage = new IOSMenuPage(masterDetail, navigation);

            MenuPage.AddPage(typeof(TodoPage), "TodoPage.Title", Icons.CircleCheck);
            MenuPage.AddPage(typeof(SettingsPage), "SettingsPage.Title", Icons.Slider);

            //menu.AddButton(TodoApp.Current.AddGroup, "Menu.AddGroup", Icons.Plus);
            //menu.AddButton(TodoApp.Current.Reload, "Menu.Reload", Icons.Sync);

            masterDetail.Master = MenuPage;
            masterDetail.Detail = navigation;

            MainPage = MainMasterDetailPage = masterDetail;

            PubSub.Subscribe<ServiceNodeChangedEvent>(this, ServiceNodeChanged);
            PubSub.Subscribe<ServiceNodesLoadedEvent>(this, ServiceNodesLoaded);
            PubSub.Subscribe<TodoListRegistrationEvent>(this, TodoListRegistration); ;

            PubSub.Subscribe<QueryTodoEvent>(this, QueryTodo);
            PubSub.Subscribe<QueryTodoListEvent>(this, QueryTodoList);

            SetupTodoSection();
            SetupTodoList(MenuPage, ViewTodoList);
        }

        Task QueryTodo(QueryTodoEvent arg)
        {
            SetupTodoSection();
            SetupTodoList(MenuPage, ViewTodoList);

            return Task.CompletedTask;
        }

        Task QueryTodoList(QueryTodoListEvent arg)
        {
            if (arg.Result == QueryTodoResult.StoredData || arg.Result == QueryTodoResult.LiveData)
            {
                SetupTodoSection();
                SetupTodoList(MenuPage, ViewTodoList);
            }

            return Task.CompletedTask;
        }

        Task ServiceNodesLoaded(ServiceNodesLoadedEvent arg)
        {
            SetupTodoSection();
            SetupTodoList(MenuPage, ViewTodoList);

            return Task.CompletedTask;
        }

        Task TodoListRegistration(TodoListRegistrationEvent arg)
        {
            SetupTodoSection();
            SetupTodoList(MenuPage, ViewTodoList);

            return Task.CompletedTask;
        }

        Task ServiceNodeChanged(ServiceNodeChangedEvent arg)
        {
            SetupTodoSection();
            SetupTodoList(MenuPage, ViewTodoList);

            return Task.CompletedTask;
        }

        void SetupTodoSection()
        {
            if (!_unlockState.HasNewState())
                return;

            MenuPage.RemoveHeaderSection("TodoLists");

            MenuPage.AddIndexBefore = false;
            MenuPage.AddIndex = null;

            if(_unlockState.HasUnlockedState)
            {
                MenuPage.AddHeaderRow("TodoLists");

                var button = MenuPage.AddButtonRow("NewTodoList", NewTodoList);
                button.SetDetailViewIcon(Icons.Tasks);
                button.RowStyle = Theme.SubmitButton;

                /*
                button = MenuPage.AddButtonRow("NewTask", NewTask);
                button.SetDetailViewIcon(Icons.Plus);
                button.RowStyle = Theme.SubmitButton;
                */
                MenuPage.AddFooterRow();
            }
        }

        async Task NewTask(ButtonRow arg)
        {
            await MenuPage.ShowPage((page) => page is AddTodoTaskPage, () => new AddTodoTaskPage(null));
        }

        public async Task ShowListViewPage(TodoList todoList)
        {
            await MenuPage.ShowPage((page) => page is TodoListPage todoListPage && todoListPage.TodoList == todoList, () => new TodoListPage(todoList, false));
            //await MenuPage.NavigationPage.PushAsync(new TodoListPage(todoList, false));
        }

        public static bool SetupTodoList(StackPage stackPage, Func<ButtonRow, Task> action)
        {
            var hasTodoList = false;
            var header = stackPage.GetRow<HeaderRow>("TodoLists");
            if (header != null)
            {
                var rows = stackPage.GetHeaderSectionRows(header);
                var rowIndex = 0;

                stackPage.AddIndexBefore = false;
                stackPage.AddIndex = header;

                foreach (var serviceNode in ServiceNodeManager.Current.ServiceNodes)
                {
                    var todo = TodoApp.Current.GetTodo(serviceNode);
                    if (todo != null)
                    {
                        foreach(var todoList in todo.TodoLists)
                        {
                            hasTodoList = true;
                            if(!(rowIndex < rows.Count && rows[rowIndex] is ButtonRow button && button.Tag is TodoList))
                            {
                                button = stackPage.AddButtonRow(null, action);
                            }

                            var name = TodoApp.GetTodoListName(todoList);

                            if (button.Label.Text != name)
                                button.Label.Text = name;

                            button.RowLayout.SetAccentColor(serviceNode.AccentColor);
                            button.Tag = todoList;

                            stackPage.AddIndex = button;
                            ++rowIndex;
                        }
                    }
                }

                for(var i = rowIndex; i < rows.Count; i++)
                {
                    if(rows[i] is ButtonRow button && button.Tag is TodoList)
                    {
                        stackPage.RemoveView(button);
                    }
                }
            }

            return hasTodoList;
        }

        async Task ViewTodoList(ButtonRow arg)
        {
            var todoList = arg.Tag as TodoList;
            await ShowListViewPage(todoList);
        }

        async Task NewTodoList(ButtonRow arg)
        {
            await MenuPage.ShowPage(typeof(NewTodoListPage));
        }

        void Start()
        {

        }

        void Resume()
        {

        }

        void Sleep()
        {

        }

        void RestoreSettings(ChunkReader reader)
        {
			
        }

        void StoreSettings(ChunkWriter writer)
        {
			
        }

        public void Activated()
        {

        }

        public void Deactivated()
        {

        }
    }
}
