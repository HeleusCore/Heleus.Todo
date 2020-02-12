using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Chain;
using Heleus.Chain.Data;
using Heleus.Cryptography;
using Heleus.Network.Client;
using Heleus.Network.Client.Record;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public enum QueryTodoResult
    {
        StoredData,
        DownloadingData,
        LiveData,
        DownloadError
    }

    public class QueryTodoEvent
    {
        public readonly QueryTodoResult Result;
        public readonly Todo Todo;

        public QueryTodoEvent(QueryTodoResult result, Todo todo)
        {
            Result = result;
            Todo = todo;
        }
    }

    public class QueryTodoListEvent
    {
        public readonly QueryTodoResult Result;
        public readonly TodoList TodoList;

        public QueryTodoListEvent(QueryTodoResult result, TodoList todoList)
        {
            Result = result;
            TodoList = todoList;
        }
    }

    public class TodoListEvent : ClientResponseEvent
    {
        public readonly ServiceNode ServiceNode;

        protected TodoListEvent(HeleusClientResponse result, ServiceNode serviceNode) : base(result)
        {
            ServiceNode = serviceNode;
        }
    }

    public class TodoListRegistrationEvent : ClientResponseEvent
    {
        public readonly ServiceNode ServiceNode;
        public readonly long ListId;

        public TodoListRegistrationEvent(HeleusClientResponse result, long listId) : base(result)
        {
            ListId = listId;
        }
    }

    public class TodoListDeletetEvent : ClientResponseEvent
    {
        public readonly long ListId;

        public TodoListDeletetEvent(HeleusClientResponse result, long listId) : base(result)
        {
            ListId = listId;
        }
    }

    public class TodoListAccountDeleteEvent : ClientResponseEvent
    {
        public readonly long ListId;
        public readonly long AccountId;

        public TodoListAccountDeleteEvent(HeleusClientResponse result, long listId, long accountId) : base(result)
        {
            ListId = listId;
            AccountId = accountId;
        }
    }

    public class TodoListInvitationSentEvent : ClientResponseEvent
    {
        public readonly long ListId;

        public TodoListInvitationSentEvent(HeleusClientResponse result, long listId) : base(result)
        {
            ListId = listId;
        }
    }

    public class TodoListInvitationAcceptedEvent : ClientResponseEvent
    {
        public readonly long ListId;

        public TodoListInvitationAcceptedEvent(HeleusClientResponse result, long listId) : base(result)
        {
            ListId = listId;
        }
    }

    public class TodoListNameChangedEvent : ClientResponseEvent
    {
        public readonly long ListId;

        public TodoListNameChangedEvent(HeleusClientResponse result, long listId) : base(result)
        {
            ListId = listId;
        }
    }

    public class NewTodoTaskEvent : ClientResponseEvent
    {
        public NewTodoTaskEvent(HeleusClientResponse result) : base(result)
        {
        }
    }

    public class TodoTaskStatusEvent : ClientResponseEvent
    {
        public readonly TodoTaskStatusTypes ItemStatus;

        public TodoTaskStatusEvent(HeleusClientResponse result, TodoTaskStatusTypes itemStatus) : base(result)
        {
            ItemStatus = itemStatus;
        }
    }

    public class TodoListSubmitAccount : GroupSubmitAccount
    {
        readonly TodoList _todoList;

        public TodoListSubmitAccount(ServiceNode serviceNode, TodoList todoList, short keyIndex) : base(serviceNode, todoList.ListId, keyIndex, todoList.Index, true)
        {
            _todoList = todoList;
        }

        public override string Name
        {
            get
            {
                return $"{TodoApp.GetTodoListName(_todoList)} / {base.Name}";
            }
        }
    }

    public class TodoApp : AppBase<TodoApp>
    {
        public const int MinPasswordLength = 5;
        readonly Dictionary<string, Todo> _todos = new Dictionary<string, Todo>();

        bool _groupBusy;

        public override void Init()
        {
            base.Init();

            UIApp.PubSub.Subscribe<TodoListRegistrationEvent>(this, TodoListRegistration);

            UIApp.PubSub.Subscribe<QueryTodoEvent>(this, QueryTodo);
            UIApp.PubSub.Subscribe<NewSecretKeyEvent>(this, NewSecretKey);
        }

        protected override async Task ServiceNodesLoaded(ServiceNodesLoadedEvent arg)
        {
            await base.ServiceNodesLoaded(arg);
            await UIApp.Current.SetFinishedLoading();
            UIApp.Run(QueryAllTodoLists);
        }

        Task NewSecretKey(NewSecretKeyEvent arg)
        {
            var secretKey = arg.SecretKey;
            if (secretKey == null)
                return Task.CompletedTask;

            foreach (var todo in _todos.Values)
            {
                foreach (var todoList in todo.TodoLists)
                {
                    if (todoList.MissingSecretKeys.ContainsKey(secretKey.KeyInfo.SecretId))
                        UIApp.Run(todoList.DownloadTransactions);
                }
            }

            return Task.CompletedTask;
        }

        Task QueryTodo(QueryTodoEvent arg)
        {
            UpdateSubmitAccounts();
            return Task.CompletedTask;
        }

        Task TodoListRegistration(TodoListRegistrationEvent arg)
        {
            UpdateSubmitAccounts();
            return Task.CompletedTask;
        }

        protected override Task AccountAuthorized(ServiceAccountAuthorizedEvent evt)
        {
            UIApp.Run(QueryAllTodoLists);
            return base.AccountAuthorized(evt);
        }

        protected override Task AccountImport(ServiceAccountImportEvent evt)
        {
            UIApp.Run(QueryAllTodoLists);
            return base.AccountImport(evt);
        }

        public override void UpdateSubmitAccounts()
        {
            foreach (var serviceNode in ServiceNodeManager.Current.ServiceNodes)
            {
                var todo = GetTodo(serviceNode);
                if (todo != null)
                {
                    foreach (var todoList in todo.TodoLists)
                    {
                        var index = todoList.Index;
                        var groupId = todoList.ListId;

                        foreach (var serviceAccount in serviceNode.ServiceAccounts.Values)
                        {
                            var keyIndex = serviceAccount.KeyIndex;

                            if (!serviceNode.HasSubmitAccount(keyIndex, index))
                            {
                                serviceNode.AddSubmitAccount(new TodoListSubmitAccount(serviceNode, todoList, keyIndex));
                            }
                        }
                    }
                }

                foreach (var serviceAccount in serviceNode.ServiceAccounts.Values)
                {
                    var keyIndex = serviceAccount.KeyIndex;
                    var index = TodoServiceInfo.TodoSubmitIndex;

                    if (!serviceNode.HasSubmitAccount(keyIndex, index))
                    {
                        serviceNode.AddSubmitAccount(new SubmitAccount(serviceNode, keyIndex, index, false));
                    }
                }
            }

            UIApp.Run(GenerateDefaultSecretKeys);
        }

        async Task GenerateDefaultSecretKeys()
        {
            var submitAccounts = ServiceNodeManager.Current.GetSubmitAccounts<GroupSubmitAccount>();

            foreach (var submitAccount in submitAccounts)
            {
                var serviceAccount = submitAccount.ServiceAccount;
                var secretKeyManager = submitAccount.SecretKeyManager;
                var index = submitAccount.Index;

                if (!secretKeyManager.HasSecretKeyType(index, SecretKeyInfoTypes.GroupSignedPublicKey))
                {
                    var secretKey = await GroupSignedPublicKeySecretKeyInfo.NewGroupSignedPublicKeySecretKey(submitAccount.GroupId, (serviceAccount as ServiceAccountKeyStore).SignedPublicKey, serviceAccount.DecryptedKey);
                    secretKeyManager.AddSecretKey(index, secretKey, true);
                    await UIApp.PubSub.PublishAsync(new NewSecretKeyEvent(submitAccount, secretKey));
                }
            }
        }

        public override ServiceNode GetLastUsedServiceNode(string key = "default")
        {
            var node = base.GetLastUsedServiceNode(key);
            if (node != null)
                return node;

            return ServiceNodeManager.Current.FirstServiceNode;
        }

        public override T GetLastUsedSubmitAccount<T>(string key = "default")
        {
            var account = base.GetLastUsedSubmitAccount<T>(key);
            if (account != null)
                return account;

            if (key == "default")
            {
                var node = GetLastUsedServiceNode();
                if (node != null)
                {
                    var accounts = node.GetSubmitAccounts<T>();
                    foreach (var acc in accounts)
                    {
                        if (!(acc is TodoListSubmitAccount))
                            return acc;
                    }
                }
            }
            else if (long.TryParse(key, out var groupId))
            {
                foreach (var todo in _todos.Values)
                {
                    foreach (var todoList in todo.TodoLists)
                    {
                        if (todoList.ListId == groupId)
                        {
                            return todoList.ServiceNode.GetSubmitAccounts<T>(todoList.Index).FirstOrDefault();
                        }
                    }
                }
            }

            return null;
        }

        public static string GetTodoListName(TodoList list)
        {
            return (list?.Name ?? Tr.Get("Common.MyTodoList"));
        }

        public async Task QueryTodoLists(ServiceNode serviceNode)
        {
            var todo = GetTodo(serviceNode);
            if (todo != null)
                await todo.QueryTodoLists();
        }

        public async Task QueryAllTodoLists()
        {
            foreach (var serviceNode in ServiceNodeManager.Current.ServiceNodes)
            {
                await QueryTodoLists(serviceNode);
            }
        }

        public Todo GetTodo(ServiceNode serviceNode)
        {
            if (serviceNode.Active)
            {
                if (serviceNode.HasUnlockedServiceAccount && serviceNode.Active)
                {
                    if (!_todos.TryGetValue(serviceNode.Id, out var todo))
                    {
                        todo = new Todo(serviceNode);
                        _todos[serviceNode.Id] = todo;
                    }

                    return todo;
                }
            }

            return null;
        }

        public async Task<HeleusClientResponse> RegisterNewList(SubmitAccount submitAccount)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var groupId = GroupAdministrationInfo.InvalidGroupId;

            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;


            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var groupReg = new FeatureRequestDataTransaction(submitAccount.AccountId, submitAccount.ChainId, TodoServiceInfo.GroupChainIndex);
            groupReg.SetFeatureRequest(new GroupRegistrationRequest(GroupFlags.AdminOnlyInvitation));

            result = await serviceNode.Client.SendDataTransaction(groupReg, true);

            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                groupId = (result.Transaction as Transaction).GetFeature<GroupAdministration>(GroupAdministration.FeatureId).NewGroupId;

                var todo = GetTodo(serviceNode);
                var todoList = todo.AddGroupId(groupId);
                await todo.SaveAsync();

                UIApp.Run(todoList.DownloadTransactions);

                UpdateSubmitAccounts();
            }

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListRegistrationEvent(result, groupId));

            return result;
        }

        (FeatureRequestDataTransaction, GroupAdministrationRequest) GetGroupUpdateTransaction(long accountId, int chainId, long groupId)
        {
            var groupUpdate = new FeatureRequestDataTransaction(accountId, chainId, TodoServiceInfo.GroupChainIndex);
            var request = new GroupAdministrationRequest(groupId);
            groupUpdate.SetFeatureRequest(request);

            return (groupUpdate, request);
        }

        public async Task<HeleusClientResponse> InviteToList(SubmitAccount submitAccount, TodoList todoList, long accountId)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;

            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var (groupUpdate, request) = GetGroupUpdateTransaction(submitAccount.AccountId, submitAccount.ChainId, todoList.ListId);
            request.ApproveAccountAsAdmin(accountId, GroupAccountFlags.Admin);

            result = await serviceNode.Client.SendDataTransaction(groupUpdate, true);

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListInvitationSentEvent(result, todoList.ListId));

            return result;
        }

        public async Task<HeleusClientResponse> AcceptListInvitation(SubmitAccount submitAccount, long listId, SecretKey secretKey)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;

            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var (groupUpdate, request) = GetGroupUpdateTransaction(submitAccount.AccountId, submitAccount.ChainId, listId);
            request.AddSelf();

            result = await serviceNode.Client.SendDataTransaction(groupUpdate, true);

            UpdateSubmitAccounts();
            submitAccount.SecretKeyManager.AddSecretKey(TodoList.BuildIndex(listId), secretKey);
            var todo = GetTodo(serviceNode);
            UIApp.Run(todo.QueryTodoLists);

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListInvitationAcceptedEvent(result, listId));

            return result;
        }

        public async Task<HeleusClientResponse> DeleteListUser(SubmitAccount submitAccount, TodoList todoList, long accountId)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;

            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var (groupUpdate, request) = GetGroupUpdateTransaction(submitAccount.AccountId, submitAccount.ChainId, todoList.ListId);
            request.RemoveAccount(accountId);

            result = await serviceNode.Client.SendDataTransaction(groupUpdate, true);

            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                UIApp.Run(async () => await todoList.DownloadTransactions());
            }

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListAccountDeleteEvent(result, todoList.ListId, accountId));

            return result;
        }

        public async Task<HeleusClientResponse> DeleteList(SubmitAccount submitAccount, TodoList todoList)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;

            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var (groupUpdate, request) = GetGroupUpdateTransaction(submitAccount.AccountId, submitAccount.ChainId, todoList.ListId);
            request.RemoveSelf();

            result = await serviceNode.Client.SendDataTransaction(groupUpdate, true);

            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                UIApp.Run(async () => await todoList.DownloadTransactions());
            }

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListDeletetEvent(result, todoList.ListId));

            return result;
        }

        public async Task<HeleusClientResponse> UpdateListName(SubmitAccount submitAccount, TodoList todoList, string name)
        {
            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, true);
            if (result != null)
                goto end;

            if (_groupBusy)
            {
                result = new HeleusClientResponse(HeleusClientResultTypes.Busy);
                goto end;
            }

            _groupBusy = true;

            var secretKey = submitAccount.DefaultSecretKey;
            var record = await EncrytpedRecord<TodoListNameRecord>.EncryptRecord(secretKey, new TodoListNameRecord(name));

            var transaction = new DataTransaction(submitAccount.AccountId, submitAccount.ChainId, TodoServiceInfo.TodoDataChainIndex);
            var group = transaction.EnableFeature<Group>(Group.FeatureId);
            group.GroupId = todoList.ListId;
            group.GroupIndex = TodoServiceInfo.TodoListNameIndex;

            var data = transaction.EnableFeature<Data>(Data.FeatureId);
            data.AddBinary(TodoServiceInfo.TodoDataItemIndex, record.ToByteArray());

            result = await serviceNode.Client.SendDataTransaction(transaction, true);

            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                UIApp.Run(todoList.DownloadTransactions);
            }

        end:

            if (result.ResultType != HeleusClientResultTypes.Busy)
                _groupBusy = false;

            await UIApp.PubSub.PublishAsync(new TodoListNameChangedEvent(result, todoList.ListId));

            return result;
        }

        public async Task<HeleusClientResponse> AddUpdateTodoItem(SubmitAccount submitAccount, string text, TodoList todoList, TodoTask targetedTodoItem)
        {
            if (todoList == null)
                throw new ArgumentException("Todolist is null", nameof(todoList));

            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, true);
            if (result != null)
                goto end;

            var secretKey = submitAccount.DefaultSecretKey;
            var record = await EncrytpedRecord<TodoTaskRecord>.EncryptRecord(secretKey, new TodoTaskRecord(text));

            var transaction = new DataTransaction(submitAccount.AccountId, submitAccount.ChainId, TodoServiceInfo.TodoDataChainIndex);
            var group = transaction.EnableFeature<Group>(Group.FeatureId);
            group.GroupId = todoList.ListId;
            group.GroupIndex = TodoServiceInfo.TodoTaskIndex;

            if (targetedTodoItem != null)
                transaction.EnableFeature<TransactionTarget>(TransactionTarget.FeatureId).AddTransactionTarget(targetedTodoItem.Id);

            var data = transaction.EnableFeature<Data>(Data.FeatureId);
            data.AddBinary(TodoServiceInfo.TodoDataItemIndex, record.ToByteArray());

            result = await serviceNode.Client.SendDataTransaction(transaction, true);
            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                UIApp.Run(todoList.DownloadTransactions);
            }

        end:

            await UIApp.PubSub.PublishAsync(new NewTodoTaskEvent(result));

            return result;
        }

        public async Task<HeleusClientResponse> UpdateTodoItemStatus(SubmitAccount submitAccount, TodoTaskStatusTypes itemStatus, TodoList todoList, TodoTask targetedTodoItem)
        {
            if (targetedTodoItem == null)
                throw new ArgumentException("Item is null", nameof(targetedTodoItem));

            var serviceNode = submitAccount?.ServiceNode;
            var result = await SetSubmitAccount(submitAccount, false);
            if (result != null)
                goto end;

            var record = new TodoTaskStatusRecord(itemStatus);

            var transaction = new DataTransaction(submitAccount.AccountId, submitAccount.ChainId, TodoServiceInfo.TodoDataChainIndex);

            var group = transaction.EnableFeature<Group>(Group.FeatureId);
            group.GroupId = targetedTodoItem.GroupId;
            group.GroupIndex = TodoServiceInfo.TodoTaskStatusIndex;

            transaction.EnableFeature<TransactionTarget>(TransactionTarget.FeatureId).AddTransactionTarget(targetedTodoItem.Id);

            var data = transaction.EnableFeature<Data>(Data.FeatureId);
            data.AddBinary(TodoServiceInfo.TodoDataItemIndex, record.ToByteArray());

            result = await serviceNode.Client.SendDataTransaction(transaction, true);
            if (result.TransactionResult == TransactionResultTypes.Ok)
            {
                UIApp.Run(async () => await todoList.DownloadTransactions());
            }

        end:

            await UIApp.PubSub.PublishAsync(new TodoTaskStatusEvent(result, itemStatus));

            return result;
        }
    }
}
