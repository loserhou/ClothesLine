using System.Text.Json;
using Clothesline.Core.Models;

namespace Clothesline.Core.Storage;

/// <summary>
/// 绳索状态安全持久化存储器（原子写入，崩溃安全）
/// </summary>
public class StateStore : IStateStore
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _stateFilePath;
    private readonly string _basketFilePath;
    private readonly string _pendingCleanupFilePath;

    public StateStore(string? customBaseDirectory = null)
    {
        if (!string.IsNullOrEmpty(customBaseDirectory))
        {
            Directory.CreateDirectory(customBaseDirectory);
            _stateFilePath = Path.Combine(customBaseDirectory, "state.json");
            _basketFilePath = Path.Combine(customBaseDirectory, "basket_history.json");
            _pendingCleanupFilePath = Path.Combine(customBaseDirectory, "pending_cleanups.json");
        }
        else
        {
            AppPaths.EnsureDirectories();
            _stateFilePath = AppPaths.StateFilePath;
            _basketFilePath = AppPaths.BasketFilePath;
            _pendingCleanupFilePath = AppPaths.PendingCleanupFilePath;
        }
    }

    // ===== 公共操作 =====
    // 每个操作在 _lock 内完成完整的「读 - 改 - 写」。截屏捕获、复制文件、拖入等入口会并发调用，
    // 若读和写分别加锁，后写入者会用旧快照覆盖先写入者的结果，导致卡片丢失。

    public async Task<List<RopeItem>> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadListAsync(_stateFilePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<RopeItem> items)
    {
        await _lock.WaitAsync();
        try
        {
            await WriteListAsync(_stateFilePath, items);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddItemAsync(RopeItem item)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await ReadListAsync(_stateFilePath);
            // 按照全路径去重：如果绳上已经有了，不重复添加，更新最后访问时间
            var existing = list.FirstOrDefault(x => string.Equals(x.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.LastAccessedTime = DateTime.Now;
            }
            else
            {
                // 新卡片插入到最左侧（最新添加）
                list.Insert(0, item);
            }

            await WriteListAsync(_stateFilePath, list);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveItemAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await ReadListAsync(_stateFilePath);
            var target = list.FirstOrDefault(x => x.Id == id);
            if (target == null) return;

            list.Remove(target);

            // 铁律：只从列表解挂，绝不删除源文件！
            // 如果是衍生临时副本，登记到待清理列表以便后台后续安全回收
            if (target.IsTemporaryCopy && File.Exists(target.FilePath))
            {
                await RegisterPendingCleanupAsync(target.FilePath);
            }

            await WriteListAsync(_stateFilePath, list);

            // 同时自动备份一份到收纳篮子中，确保历史可追溯
            var basket = await ReadListAsync(_basketFilePath);
            UpsertFront(basket, target);
            await WriteListAsync(_basketFilePath, basket);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 彻底从晾衣绳清除文件，不移入收纳篮；若为临时截图/转码件，安全删除物理磁盘文件
    /// </summary>
    public async Task PurgeItemAsync(string id, bool deletePhysicalTemporaryFile = true)
    {
        RopeItem? target;
        await _lock.WaitAsync();
        try
        {
            var list = await ReadListAsync(_stateFilePath);
            target = list.FirstOrDefault(x => x.Id == id);
            if (target == null) return;

            list.Remove(target);
            await WriteListAsync(_stateFilePath, list);
        }
        finally
        {
            _lock.Release();
        }

        if (deletePhysicalTemporaryFile && target.IsTemporaryCopy)
        {
            TryDelete(target.FilePath);
        }
    }

    public async Task<List<RopeItem>> LoadBasketAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadListAsync(_basketFilePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddToBasketAsync(RopeItem item)
    {
        await _lock.WaitAsync();
        try
        {
            var list = await ReadListAsync(_basketFilePath);
            UpsertFront(list, item);
            await WriteListAsync(_basketFilePath, list);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveFromBasketAsync(string id)
    {
        RopeItem? target;
        await _lock.WaitAsync();
        try
        {
            var list = await ReadListAsync(_basketFilePath);
            target = list.FirstOrDefault(x => x.Id == id);
            if (target == null) return;

            list.Remove(target);
            await WriteListAsync(_basketFilePath, list);
        }
        finally
        {
            _lock.Release();
        }

        // 若为临时文件，移出衣篮时彻底释放磁盘空间
        if (target.IsTemporaryCopy)
        {
            TryDelete(target.FilePath);
        }
    }

    public async Task ClearBasketAsync()
    {
        List<RopeItem> list;
        await _lock.WaitAsync();
        try
        {
            list = await ReadListAsync(_basketFilePath);
            await WriteListAsync(_basketFilePath, []);
        }
        finally
        {
            _lock.Release();
        }

        foreach (var item in list.Where(x => x.IsTemporaryCopy))
        {
            TryDelete(item.FilePath);
        }
    }

    // ===== 内部读写（调用方必须已持有 _lock） =====

    private static async Task<List<RopeItem>> ReadListAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];

            var json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json)) return [];

            var items = JsonSerializer.Deserialize<List<RopeItem>>(json, JsonOptions) ?? [];
            // 自动净化：过滤磁盘上物理文件已不存在的孤儿记录
            return items.Where(x => File.Exists(x.FilePath)).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteListAsync(string targetPath, IEnumerable<RopeItem> items)
    {
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json);

        // 原子替换，防止意外掉电损坏状态文件
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, null);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static void UpsertFront(List<RopeItem> list, RopeItem item)
    {
        var existing = list.FirstOrDefault(x => string.Equals(x.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LastAccessedTime = DateTime.Now;
        }
        else
        {
            list.Insert(0, item);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略占用等删除异常
        }
    }

    private async Task RegisterPendingCleanupAsync(string filePath)
    {
        try
        {
            var cleanupPath = _pendingCleanupFilePath;
            List<string> cleanups = [];
            if (File.Exists(cleanupPath))
            {
                var content = await File.ReadAllTextAsync(cleanupPath);
                cleanups = JsonSerializer.Deserialize<List<string>>(content) ?? [];
            }

            if (!cleanups.Contains(filePath))
            {
                cleanups.Add(filePath);
                await File.WriteAllTextAsync(cleanupPath, JsonSerializer.Serialize(cleanups, JsonOptions));
            }
        }
        catch
        {
            // 忽略非关键清理异常
        }
    }
}
