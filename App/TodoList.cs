using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Base;
using Heleus.Chain;
using Heleus.Cryptography;
using Heleus.Network.Client;
using Heleus.Network.Client.Record;
using Heleus.Operations;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public enum TodoListSortMethod
    {
        Ignored,
        ByTransactionId,
        ByTransactionIdDesc,
        ByTimestamp,
        ByTimestampDesc
    }

    public class TodoList : IPackable, IUnpackerKey<long>
    {
        public readonly long ListId;
        public readonly Chain.Index Index;
        public readonly ServiceNode ServiceNode;
        public readonly Todo Todo;

        public long UnpackerKey => ListId;

        public SecretKeyInfo LastUsedSecretKeyInfo { get; private set; }

        internal readonly Dictionary<ulong, SecretKeyInfo> MissingSecretKeys = new Dictionary<ulong, SecretKeyInfo>();
        public bool HasMissingSecretKeys => MissingSecretKeys.Count > 0;

        public long LastProcessedTransactionId { get; private set; } = Operations.Operation.InvalidTransactionId;

        public TransactionDownloadResult<Transaction> LastDownloadResult { get; private set; }

        public string Name
        {
            get
            {
                return CurrentListNameRecord?.Record?.Name;
            }
        }

        public TodoRecordStorage<TodoListNameRecord> CurrentListNameRecord { get; private set; }

        readonly HashSet<long> _historyTransactionIds = new HashSet<long>();
        readonly Dictionary<long, TodoTask> _items = new Dictionary<long, TodoTask>();

        public static Chain.Index BuildIndex(long listId)
        {
            return new Chain.Index.IndexBuilder().Add(listId).Build();
        }

        public bool IsLastUsedSecretKey(SubmitAccount submitAccount)
        {
            if (LastUsedSecretKeyInfo == null)
                return true;

            var keyInfo = submitAccount?.DefaultSecretKey?.KeyInfo;
            if(keyInfo != null)
            {
                return keyInfo.SecretId == LastUsedSecretKeyInfo.SecretId;
            }
            return false;
        }

        public List<TodoTask> GetTasks(TodoTaskStatusTypes status, TodoListSortMethod sortMethod)
        {
            var result = new List<TodoTask>();

            foreach (var item in _items.Values)
            {
                if (item.Status == status)
                {
                    result.Add(item);
                }
            }

            if (sortMethod == TodoListSortMethod.ByTransactionId)
                result.Sort((a, b) => a.Id.CompareTo(b.Id));
            else if (sortMethod == TodoListSortMethod.ByTransactionIdDesc)
                result.Sort((a, b) => b.Id.CompareTo(a.Id));
            else if (sortMethod == TodoListSortMethod.ByTimestamp)
                result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            else if (sortMethod == TodoListSortMethod.ByTimestampDesc)
                result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

            return result;
        }

        public TodoList(long listId, long transactionId, Todo todo, ServiceNode serviceNode)
        {
            ListId = listId;
            LastProcessedTransactionId = transactionId;
            Todo = todo;

            if (LastProcessedTransactionId != Operation.InvalidTransactionId)
                _historyTransactionIds.Add(transactionId);

            ServiceNode = serviceNode;
            Index = BuildIndex(listId);
        }

        public TodoList(Unpacker unpacker, Todo todo, ServiceNode serviceNode)
        {
            unpacker.Unpack(out byte dataVersion);
            unpacker.Unpack(out ListId);
            LastProcessedTransactionId = unpacker.UnpackLong();
            if(unpacker.UnpackBool())
                LastUsedSecretKeyInfo = SecretKeyInfo.Restore(unpacker);
            unpacker.Unpack(_items, (u) => new TodoTask(u));
            unpacker.Unpack(_historyTransactionIds);
            if (unpacker.UnpackBool())
                CurrentListNameRecord = new TodoRecordStorage<TodoListNameRecord>(unpacker);
            Index = BuildIndex(ListId);
            ServiceNode = serviceNode;
            Todo = todo;
        }

        public void Pack(Packer packer)
        {
            packer.Pack(TodoServiceInfo.DataVersion);
            packer.Pack(ListId);
            packer.Pack(LastProcessedTransactionId);
            if(packer.Pack(LastUsedSecretKeyInfo != null))
                packer.Pack(LastUsedSecretKeyInfo);
            packer.Pack(_items);
            packer.Pack(_historyTransactionIds);
            if (packer.Pack(CurrentListNameRecord != null))
                packer.Pack(CurrentListNameRecord);
        }

        public async Task DownloadTransactions()
        {
            if(HasMissingSecretKeys)
            {
                var missingIds = MissingSecretKeys.Keys.ToArray();
                foreach(var missingId in missingIds)
                {
                    if (ServiceNode.GetSecretKeys(Index, missingId).Count > 0)
                        MissingSecretKeys.Remove(missingId);
                }
            }

            await UIApp.PubSub.PublishAsync(new QueryTodoListEvent(QueryTodoResult.DownloadingData, this));

            var lastId = Math.Max(LastProcessedTransactionId, Operation.FirstTransactionId);

            var download = new GroupTransactionDownload(ListId, ServiceNode.GetTransactionDownloadManager(TodoServiceInfo.TodoDataChainIndex))
            {
                MinimalTransactionId = lastId,
                DownloadAttachements = false
            };

            LastDownloadResult = await download.DownloadTransactions();

            if (LastDownloadResult.Ok)
            {
                LastDownloadResult.Transactions.Sort((a, b) => a.TransactionId.CompareTo(b.TransactionId));
                foreach (var transaction in LastDownloadResult.Transactions)
                {
                    transaction.Tag = ServiceNode;
                    await ProcessTransaction(transaction);
                }
                await Todo.SaveAsync();
                await UIApp.PubSub.PublishAsync(new QueryTodoListEvent(QueryTodoResult.LiveData, this));
            }
            else
            {
                await UIApp.PubSub.PublishAsync(new QueryTodoListEvent(QueryTodoResult.DownloadError, this));
            }
        }

        async Task ProcessTransaction(TransactionDownloadData<Transaction> transactionDownload)
        {
            try
            {
                var transaction = transactionDownload.Transaction;

                var data = transaction.GetFeature<Data>(Data.FeatureId);
                if (data == null) // Group Stuff
                    return;

                var group = transaction.GetFeature<Group>(Group.FeatureId);
                var groupId = group.GroupId;
                var index = group.GroupIndex;

                var storage = LoadTodoStorage(transactionDownload);
                if (storage == null)
                {
                    using (var unpacker = new Unpacker(data.Items[0].Data))
                    {
                        var targetedTransactionId = Operation.InvalidTransactionId;

                        var transactionTarget = transaction.GetFeature<TransactionTarget>(TransactionTarget.FeatureId);
                        if (transactionTarget != null)
                            targetedTransactionId = transactionTarget.Targets[0];

                        if (index == TodoServiceInfo.TodoTaskStatusIndex)
                        {
                            storage = new TodoRecordStorage<TodoTaskStatusRecord>(new TodoTaskStatusRecord(unpacker), transaction.TransactionId, transaction.AccountId, transaction.Timestamp, targetedTransactionId, groupId);
                        }
                        else if (index == TodoServiceInfo.TodoTaskIndex)
                        {
                            storage = await GetDecryptedRecord<TodoTaskRecord>(transaction, targetedTransactionId, groupId, unpacker);
                        }
                        else if (index == TodoServiceInfo.TodoListNameIndex)
                        {
                            storage = await GetDecryptedRecord<TodoListNameRecord>(transaction, targetedTransactionId, groupId, unpacker);
                        }

                        if (storage != null)
                            await SaveTodoStorage(transactionDownload, storage);
                    }
                }

                if (storage != null && MissingSecretKeys.Count == 0)
                    Process(storage);
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
            }
        }

        public static EncrytpedRecord<T> GetEncrytpedTodoRedord<T>(Transaction transaction) where T : Record
        {
            try
            {
                var data = transaction.GetFeature<Data>(Data.FeatureId);
                if(data != null)
                {
                    using (var unpacker = new Unpacker(data.Items[0].Data))
                    {
                        return new EncrytpedRecord<T>(unpacker);
                    }
                }
            }
            catch { };

            return null;
        }

        async Task<TodoRecordStorage<T>> GetDecryptedRecord<T>(Transaction transaction, long targetedTransactionId, long groupId, Unpacker unpacker) where T : Record
        {
            var encryptedRecord = new EncrytpedRecord<T>(unpacker);
            var secretKeyInfo = encryptedRecord.KeyInfo;
            var secretKeys = ServiceNode.GetSecretKeys(Index, secretKeyInfo.SecretId);

            foreach (var secretKey in secretKeys)
            {
                var record = await encryptedRecord.GetRecord(secretKey);
                if (record != null)
                {
                    LastUsedSecretKeyInfo = secretKeyInfo;
                    return new TodoRecordStorage<T>(record, transaction.TransactionId, transaction.AccountId, transaction.Timestamp, targetedTransactionId, groupId);
                }
            }

            if (!MissingSecretKeys.ContainsKey(secretKeyInfo.SecretId))
                MissingSecretKeys[secretKeyInfo.SecretId] = secretKeyInfo;

            return null;
        }

        public IRecordStorage LoadTodoStorage(TransactionDownloadData<Transaction> transactionDownload)
        {
            try
            {
                var data = transactionDownload.GetDecryptedData("todo");
                if (data != null)
                {
                    using (var unpacker = new Unpacker(data))
                    {
                        var recordType = (TodoRecordTypes)RecordStorage.ReadRecordType(unpacker);

                        Type type = null;
                        if (recordType == TodoRecordTypes.ListName)
                            type = typeof(TodoRecordStorage<TodoListNameRecord>);
                        else if (recordType == TodoRecordTypes.Task)
                            type = typeof(TodoRecordStorage<TodoTaskRecord>);
                        else if (recordType == TodoRecordTypes.TaskStatus)
                            type = typeof(TodoRecordStorage<TodoTaskStatusRecord>);

                        if (type != null)
                            return (IRecordStorage)Activator.CreateInstance(type, unpacker);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
            }

            return null;
        }

        async Task SaveTodoStorage(TransactionDownloadData<Transaction> transactionDownload, IRecordStorage todoStorage)
        {
            try
            {
                transactionDownload.AddDecryptedAttachement("todo", todoStorage.ToByteArray());
                await transactionDownload.TransactionManager.StoreDecryptedTransactionData(transactionDownload);
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
            }
        }

        void Process(IRecordStorage storage)
        {
            LastProcessedTransactionId = Math.Max(storage.TransactionId, LastProcessedTransactionId);
            _historyTransactionIds.Add(storage.TransactionId);

            if (storage.GetRecordType() == TodoRecordTypes.ListName)
            {
                var nameRecord = storage as TodoRecordStorage<TodoListNameRecord>;
                if (CurrentListNameRecord != null)
                {
                    if (storage.TransactionId > CurrentListNameRecord.TransactionId)
                        CurrentListNameRecord = nameRecord;
                }
                else
                {
                    CurrentListNameRecord = nameRecord;
                }
            }
            else if (storage.GetRecordType() == TodoRecordTypes.Task)
            {
                var itemRecord = storage as TodoRecordStorage<TodoTaskRecord>;

                _items.TryGetValue(itemRecord.TargetedTransactionId, out var item);
                if (item == null)
                    _items[itemRecord.TransactionId] = new TodoTask(itemRecord);
                else
                    item.Update(itemRecord);
            }
            else if (storage.GetRecordType() == TodoRecordTypes.TaskStatus)
            {
                var statusRecord = storage as TodoRecordStorage<TodoTaskStatusRecord>;
                if (_items.TryGetValue(statusRecord.TargetedTransactionId, out var item))
                    item.Update(statusRecord);
            }
        }
    }
}
