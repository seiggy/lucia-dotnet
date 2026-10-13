using lucia.Agents.Abstractions;
using lucia.Data.InMemory;
using Microsoft.Extensions.Caching.Memory;

namespace lucia.Tests.Data.Providers;

public sealed class InMemoryKeyValueCacheTests : KeyValueCacheContract
{
    protected override IKeyValueCache CreateCache()
        => new InMemoryKeyValueCache(new MemoryCache(new MemoryCacheOptions()));
}