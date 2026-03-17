using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.TestDoubles;

public static class TestOnnxProvider
{
    public static OnnxProviderDetector Instance { get; } =
        new(NullLogger<OnnxProviderDetector>.Instance);
}
