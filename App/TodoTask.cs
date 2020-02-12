using System;
using System.Collections.Generic;
using Heleus.Base;
using Heleus.TodoService;

namespace Heleus.Apps.Todo
{
    public class TodoTask : IPackable, IUnpackerKey<long>
    {
        readonly TodoRecordStorage<TodoTaskRecord> _firsItemStorage;

        public TodoRecordStorage<TodoTaskRecord> CurrentTaskStorage { get; private set; }
        public TodoRecordStorage<TodoTaskStatusRecord> CurrentStatusStorage { get; private set; }

        public long Id => _firsItemStorage.TransactionId;
        public long GroupId => _firsItemStorage.GroupId;

        public long UnpackerKey => Id;

        public string Text => CurrentTaskStorage?.Record?.Text ?? string.Empty;

        public long Timestamp
        {
            get
            {
                var ts = 0L;
                if (CurrentStatusStorage != null)
                    ts = CurrentStatusStorage.Timestamp;

                return Math.Max(ts, CurrentTaskStorage.Timestamp);
            }
        }

        readonly HashSet<long> _historyTransactionIds = new HashSet<long>();

        public List<long> GetAllTransactionIds()
        {
            var result = new List<long>(_historyTransactionIds);
            result.Sort((a, b) => b.CompareTo(a));

            return result;
        }

        public TodoTaskStatusTypes Status
        {
            get
            {
                if (CurrentStatusStorage == null)
                    return TodoTaskStatusTypes.Open;
                return CurrentStatusStorage.Record.Status;
            }
        }

        public TodoTask(TodoRecordStorage<TodoTaskRecord> itemRecord)
        {
            _historyTransactionIds.Add(itemRecord.TransactionId);
            _firsItemStorage = itemRecord;
            CurrentTaskStorage = itemRecord;
        }

        public TodoTask(Unpacker unpacker)
        {
            _firsItemStorage = new TodoRecordStorage<TodoTaskRecord>(unpacker);
            CurrentTaskStorage = new TodoRecordStorage<TodoTaskRecord>(unpacker);

            if (unpacker.UnpackBool())
                CurrentStatusStorage = new TodoRecordStorage<TodoTaskStatusRecord>(unpacker);

            unpacker.Unpack(_historyTransactionIds);
        }

        public void Pack(Packer packer)
        {
            packer.Pack(_firsItemStorage);
            packer.Pack(CurrentTaskStorage);

            if (packer.Pack(CurrentStatusStorage != null))
                packer.Pack(CurrentStatusStorage);

            packer.Pack(_historyTransactionIds);
        }

        public void Update(TodoRecordStorage<TodoTaskRecord> itemRecord)
        {
            _historyTransactionIds.Add(itemRecord.TransactionId);
            if (itemRecord.TransactionId > CurrentTaskStorage.TransactionId)
            {
                CurrentTaskStorage = itemRecord;
            }
        }

        public void Update(TodoRecordStorage<TodoTaskStatusRecord> statusRecord)
        {
            _historyTransactionIds.Add(statusRecord.TransactionId);
            if (CurrentStatusStorage == null)
            {
                CurrentStatusStorage = statusRecord;
            }
            else
            {
                if (statusRecord.TransactionId > CurrentStatusStorage.TransactionId)
                {
                    CurrentStatusStorage = statusRecord;
                }
            }
        }
    }
}
