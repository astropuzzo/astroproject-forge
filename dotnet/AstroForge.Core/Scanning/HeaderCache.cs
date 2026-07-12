namespace AstroForge.Core.Scanning;

public interface IHeaderCache
{
    bool TryGet(string path, long length, long lastWriteUtcTicks, out Dictionary<string, object?> headers);
    void Put(string path, long length, long lastWriteUtcTicks, Dictionary<string, object?> headers);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
