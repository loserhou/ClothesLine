using Clothesline.Core.Models;
using Clothesline.Core.Storage;
using Xunit;

namespace Clothesline.Tests;

/// <summary>
/// 截屏捕获、复制文件、拖入等入口会并发写入状态文件；读-改-写必须整体原子，否则后写覆盖先写导致卡片丢失
/// </summary>
[Collection("StateStore")]
public class StateStoreConcurrencyTests
{
    [Fact]
    public async Task ConcurrentAdds_ShouldNotLoseItems()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Clothesline_Concurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = Enumerable.Range(0, 24).Select(i =>
            {
                var p = Path.Combine(dir, $"file_{i}.txt");
                File.WriteAllText(p, "x");
                return p;
            }).ToList();

            var store = new StateStore(dir);
            await Task.WhenAll(files.Select(f => Task.Run(() => store.AddItemAsync(RopeItem.FromFile(f)))));

            var loaded = await store.LoadAsync();
            Assert.Equal(files.Count, loaded.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentBasketAdds_ShouldNotLoseItems()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Clothesline_Concurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = Enumerable.Range(0, 24).Select(i =>
            {
                var p = Path.Combine(dir, $"b_{i}.txt");
                File.WriteAllText(p, "x");
                return p;
            }).ToList();

            var store = new StateStore(dir);
            await Task.WhenAll(files.Select(f => Task.Run(() => store.AddToBasketAsync(RopeItem.FromFile(f)))));

            var loaded = await store.LoadBasketAsync();
            Assert.Equal(files.Count, loaded.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
