using Hinge.Core;

namespace Hinge.Tests;

public class PendingFileSendTests
{
    [Fact]
    public void Enqueue_PersistsAndCanReplaceRemainingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hinge_pending_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "first.txt");
        var secondPath = Path.Combine(directory, "second.txt");
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");
        var queuePath = Path.Combine(directory, "queue.json");

        try
        {
            var store = new PendingFileSendStore(queuePath);
            var item = store.Enqueue("phone-1", new[] { firstPath, secondPath, firstPath });

            var reloaded = new PendingFileSendStore(queuePath).GetAll();
            var loaded = Assert.Single(reloaded);
            Assert.Equal(item.Id, loaded.Id);
            Assert.Equal(new[] { firstPath, secondPath }, loaded.FilePaths);

            Assert.True(store.ReplacePaths(item.Id, new[] { secondPath }));
            var remaining = Assert.Single(new PendingFileSendStore(queuePath).GetAll());
            Assert.Equal(new[] { secondPath }, remaining.FilePaths);

            Assert.True(store.Remove(item.Id));
            Assert.Empty(new PendingFileSendStore(queuePath).GetAll());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Enqueue_DoesNotPersistMissingFiles()
    {
        var queuePath = Path.Combine(
            Path.GetTempPath(),
            $"hinge_pending_{Guid.NewGuid():N}.json");
        var store = new PendingFileSendStore(queuePath);

        Assert.Throws<ArgumentException>(() => store.Enqueue(
            "phone-1",
            new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }));
        Assert.Empty(store.GetAll());
    }
}
