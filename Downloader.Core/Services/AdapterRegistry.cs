using Downloader.Core.Interfaces;

namespace Downloader.Core.Services;

public sealed class AdapterRegistry
{
    private readonly List<ISiteAdapter> _adapters;

    public AdapterRegistry(IEnumerable<ISiteAdapter> adapters)
    {
        _adapters = adapters.ToList();
    }

    public IReadOnlyList<ISiteAdapter> Adapters => _adapters;

    public ISiteAdapter? Resolve(Uri url) => _adapters.FirstOrDefault(a => a.CanHandle(url));
}
