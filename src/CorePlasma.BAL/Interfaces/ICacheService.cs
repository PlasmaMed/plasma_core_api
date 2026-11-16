namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Cache abstraction. Default implementation uses IMemoryCache; can be swapped for Redis
/// (IDistributedCache) without touching business services.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
