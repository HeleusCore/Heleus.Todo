using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Base;
using Heleus.Operations;
using Heleus.TodoService;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public class Todo : IPackable
    {
        public IReadOnlyCollection<TodoList> TodoLists => _todoLists.Values;
        readonly Dictionary<long, TodoList> _todoLists = new Dictionary<long, TodoList>();

        readonly ServiceNode _serviceNode;

        public static Task<Todo> LoadAsync(ServiceNode serviceNode)
        {
            return Task.Run(() => new Todo(serviceNode));
        }

        public void Save()
        {
            using (var packer = new Packer())
            {
                Pack(packer);

                var data = packer.ToByteArray();
                _serviceNode.CacheStorage.WriteFileBytes("todo", data);
            }
        }

        public Task SaveAsync()
        {
            return Task.Run(Save);
        }

        public Todo(ServiceNode serviceNode)
        {
            _serviceNode = serviceNode;

            try
            {
                var data = serviceNode.CacheStorage.ReadFileBytes("todo");
                if (data != null)
                {
                    using (var unpacker = new Unpacker(data))
                    {
                        unpacker.Unpack(_todoLists, (u) => new TodoList(u, this, serviceNode));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
                _todoLists.Clear();
            }
        }

        public void Pack(Packer packer)
        {
            packer.Pack(_todoLists);
        }

        public TodoList GetTodoList(long groupId)
        {
            _todoLists.TryGetValue(groupId, out var todoList);
            return todoList;
        }

        public async Task<bool> QueryTodoListIds()
        {
            var groupData = await GroupAdministration.DownloadGroups(_serviceNode.Client, Chain.ChainType.Data, _serviceNode.ChainId, TodoServiceInfo.GroupChainIndex, _serviceNode.AccountId);
            var groupIds = groupData?.Value;
            if (groupIds != null)
            {
                foreach(var groupId in groupIds)
                {
                    AddGroupId(groupId);
                }

                await SaveAsync();
                return true;
            }

            await UIApp.PubSub.PublishAsync(new QueryTodoEvent(QueryTodoResult.DownloadError, this));
            return false;
        }

        internal TodoList AddGroupId(long groupId)
        {
            if(!_todoLists.TryGetValue(groupId, out var todoList))
            {
                todoList = new TodoList(groupId, Operation.InvalidTransactionId, this, _serviceNode);
                _todoLists[groupId] = todoList;
            }
            return todoList;
        }

        public async Task QueryTodoLists()
        {
            if (!await QueryTodoListIds())
                return;

            foreach (var todoList in _todoLists.Values)
            {
                await todoList.DownloadTransactions();
            }

            await UIApp.PubSub.PublishAsync(new QueryTodoEvent(QueryTodoResult.LiveData, this));
        }
    }
}
