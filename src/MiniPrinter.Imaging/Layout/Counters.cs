using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniPrinter.Imaging;

/// <summary>Persistent numbering for <c>{{counter}}</c> (design.md D6).</summary>
public interface ICounterSource
{
    /// <summary>The number the next print will use, without consuming it (previews).</summary>
    long Peek(string name);

    /// <summary>Returns the next number and advances the counter (prints).</summary>
    long Next(string name);
}

/// <summary>
/// Counters stored as <c>{"name": next}</c> in a JSON file. Writes are atomic (temp file + replace)
/// and guarded by a named mutex so the service and the CLI never hand out the same number.
/// </summary>
public sealed class FileCounterStore : ICounterSource
{
    private readonly string _path;
    private readonly string _mutexName;
    private readonly object _gate = new();

    public FileCounterStore(string path)
    {
        _path = Path.GetFullPath(path);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToLowerInvariant())))[..16];
        _mutexName = $"MiniPrinter.Counters.{hash}"; // local to the session; the service has its own
    }

    public long Peek(string name) => WithLock(data => data.GetValueOrDefault(name, 1), save: false);

    public long Next(string name) => WithLock(data =>
    {
        var value = data.GetValueOrDefault(name, 1);
        data[name] = value + 1;
        return value;
    }, save: true);

    private long WithLock(Func<Dictionary<string, long>, long> action, bool save)
    {
        lock (_gate)
        {
            using var mutex = new Mutex(false, "Global\\" + _mutexName);
            var owned = false;
            try
            {
                try
                {
                    owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }
                var data = Load();
                var result = action(data);
                if (save)
                    Save(data);
                return result;
            }
            finally
            {
                if (owned)
                    mutex.ReleaseMutex();
            }
        }
    }

    private Dictionary<string, long> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, long> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>In-memory counters (tests, previews without a store).</summary>
public sealed class MemoryCounterStore : ICounterSource
{
    private readonly Dictionary<string, long> _next = [];

    public long Peek(string name) => _next.GetValueOrDefault(name, 1);

    public long Next(string name)
    {
        var value = Peek(name);
        _next[name] = value + 1;
        return value;
    }
}
