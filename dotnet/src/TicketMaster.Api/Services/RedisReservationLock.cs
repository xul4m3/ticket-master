using StackExchange.Redis;

namespace TicketMaster.Api.Services;

internal sealed class RedisReservationLock : IAsyncDisposable
{
    private const int LockTimeoutSeconds = 30;
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end");

    private readonly IDatabase _database;
    private readonly RedisKey _key;
    private readonly RedisValue _token;
    private bool _disposed;

    private RedisReservationLock(IDatabase database, RedisKey key, RedisValue token)
    {
        _database = database;
        _key = key;
        _token = token;
    }

    public static async Task<RedisReservationLock> AcquireAsync(IDatabase database, RedisKey key, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await database.StringSetAsync(key, token, TimeSpan.FromSeconds(LockTimeoutSeconds), When.NotExists))
            {
                return new RedisReservationLock(database, key, token);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _database.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { _key }, new RedisValue[] { _token });
    }
}
