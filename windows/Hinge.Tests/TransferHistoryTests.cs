using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public sealed class TransferHistoryTests
{
    [Fact]
    public void DeleteOperations_RemoveHistoryOnly_AndKeepActiveTransfers()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            $"hinge-transfer-history-{Guid.NewGuid():N}.json");
        var completed = new TransferHistoryRecord
        {
            Id = "completed",
            FileName = "done.apk",
            State = TransferState.Completed
        };
        var failed = new TransferHistoryRecord
        {
            Id = "failed",
            FileName = "failed.apk",
            State = TransferState.Failed
        };
        var active = new TransferHistoryRecord
        {
            Id = "active",
            FileName = "sending.apk",
            State = TransferState.Transferring,
            TotalBytes = 100,
            BytesTransferred = 40
        };

        try
        {
            var store = new TransferHistoryStore(storagePath);
            store.Upsert(completed);
            store.Upsert(failed);
            store.Upsert(active);

            Assert.True(store.Delete(completed.Id));
            Assert.False(store.Delete(completed.Id));
            Assert.DoesNotContain(store.GetAll(), record => record.Id == completed.Id);

            var deleted = store.DeleteWhere(record => record.CanDelete);

            Assert.Equal(1, deleted);
            var remaining = store.GetAll();
            Assert.Contains(remaining, record => record.Id == active.Id);
            Assert.DoesNotContain(remaining, record => record.Id == failed.Id);
            Assert.False(store.DeleteWhere(record => record.CanDelete) > 0);
        }
        finally
        {
            if (File.Exists(storagePath)) File.Delete(storagePath);
            if (File.Exists(storagePath + ".tmp")) File.Delete(storagePath + ".tmp");
        }
    }
}
