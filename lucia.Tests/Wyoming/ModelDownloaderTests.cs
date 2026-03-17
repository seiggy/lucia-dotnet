using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCompress.Common;

namespace lucia.Tests.Wyoming;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadModelAsync_SingleOnnxFile_PlacedWithoutExtraction()
    {
        // Arrange
        var modelId = "single-file-model";
        var expectedBytes = new byte[] { 0x4F, 0x4E, 0x4E, 0x58 }; // fake ONNX magic bytes
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition(
            modelId,
            downloadUrl: "https://models.example.com/tiny-model.onnx");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act
        var result = await downloader.DownloadModelAsync(model, targetBasePath);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(modelId, result.ModelId);

        var expectedFile = Path.Combine(targetBasePath, modelId, "tiny-model.onnx");
        Assert.True(File.Exists(expectedFile), $"Expected file at {expectedFile}");
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(expectedFile));
    }

    [Fact]
    public async Task DownloadModelAsync_SingleOnnxFile_ReportsProgress()
    {
        // Arrange
        var expectedBytes = new byte[1024];
        Random.Shared.NextBytes(expectedBytes);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
            {
                Headers = { ContentLength = expectedBytes.Length },
            },
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("progress-model",
            downloadUrl: "https://models.example.com/model.onnx");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        var progressReports = new List<ModelDownloadProgress>();
        var progress = new Progress<ModelDownloadProgress>(p => progressReports.Add(p));

        // Act
        await downloader.DownloadModelAsync(model, targetBasePath, progress);

        // Assert — at minimum the final 100% report must be present
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.PercentComplete >= 100d);
    }

    [Fact]
    public async Task DownloadModelAsync_ArchiveUrl_FailsAtExtractionNotDownload()
    {
        // Arrange — the handler returns garbage bytes so download succeeds
        // but SharpCompress extraction will fail with InvalidFormatException.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("not-a-real-archive"u8.ToArray()),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("archive-model",
            downloadUrl: "https://models.example.com/model.tar.bz2");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act & Assert — download succeeds, but extraction throws InvalidFormatException
        // proving the archive code path was taken (not the single-file copy path).
        await Assert.ThrowsAsync<InvalidFormatException>(
            () => downloader.DownloadModelAsync(model, targetBasePath));
    }

    [Fact]
    public async Task DownloadModelAsync_TarGzUrl_TreatedAsArchive()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x00, 0x01, 0x02]),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("targz-model",
            downloadUrl: "https://models.example.com/model.tar.gz");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act & Assert — InvalidFormatException proves the archive extraction path was taken
        await Assert.ThrowsAsync<InvalidFormatException>(
            () => downloader.DownloadModelAsync(model, targetBasePath));
    }

    [Fact]
    public async Task DownloadModelAsync_ZipUrl_TreatedAsArchive()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x00]),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("zip-model",
            downloadUrl: "https://models.example.com/model.zip");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act & Assert — InvalidFormatException proves the archive extraction path was taken
        await Assert.ThrowsAsync<InvalidFormatException>(
            () => downloader.DownloadModelAsync(model, targetBasePath));
    }

    [Fact]
    public async Task DownloadModelAsync_AlreadyExists_ReturnsEarlyWithAlreadyExisted()
    {
        // Arrange — pre-create the model directory with an .onnx file
        var modelId = "existing-model";
        var modelDir = Path.Combine(_tempRoot, "models", modelId);
        Directory.CreateDirectory(modelDir);
        await File.WriteAllBytesAsync(Path.Combine(modelDir, "model.onnx"), [0x01]);

        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP should not be called"));

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition(modelId,
            downloadUrl: "https://models.example.com/model.onnx");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act
        var result = await downloader.DownloadModelAsync(model, targetBasePath);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.AlreadyExisted);
        Assert.Equal(modelId, result.ModelId);
    }

    [Fact]
    public async Task DownloadModelAsync_HttpFailure_ReturnsFailureResult()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("missing-model",
            downloadUrl: "https://models.example.com/nope.onnx");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act
        var result = await downloader.DownloadModelAsync(model, targetBasePath);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("missing-model", result.ModelId);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task DownloadModelAsync_UrlWithNoFileName_FallsBackToModelIdArchive()
    {
        // Arrange — a URL whose path is "/" so GetDownloadFileName returns "{modelId}.tar.bz2"
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x00]),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("fallback-model",
            downloadUrl: "https://models.example.com/");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act & Assert — the fallback name is "{modelId}.tar.bz2" which is an archive extension,
        // so extraction is attempted and fails with InvalidFormatException on the garbage data.
        await Assert.ThrowsAsync<InvalidFormatException>(
            () => downloader.DownloadModelAsync(model, targetBasePath));
    }

    [Fact]
    public async Task DownloadModelAsync_StagingDirectoryCleanedUpOnFailure()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("bad-archive"u8.ToArray()),
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition("cleanup-model",
            downloadUrl: "https://models.example.com/model.tar.bz2");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        var stagingRoot = Path.Combine(Path.GetTempPath(), "lucia-wyoming-models");
        var dirsBefore = Directory.Exists(stagingRoot)
            ? Directory.GetDirectories(stagingRoot).ToHashSet()
            : [];

        // Act — extraction will throw, but the finally block should clean up staging
        await Assert.ThrowsAsync<InvalidFormatException>(
            () => downloader.DownloadModelAsync(model, targetBasePath));

        // Assert — no NEW staging directories remain after the finally block runs
        var dirsAfter = Directory.Exists(stagingRoot)
            ? Directory.GetDirectories(stagingRoot).ToHashSet()
            : [];

        dirsAfter.ExceptWith(dirsBefore);
        Assert.Empty(dirsAfter);
    }

    [Fact]
    public async Task IsModelDirectoryReady_SingleOnnxFile_DetectedAsReady()
    {
        // Arrange — download a single .onnx file, then re-download to prove it's detected
        var modelId = "ready-model";
        var expectedBytes = new byte[] { 0xAA, 0xBB };
        var callCount = 0;

        var handler = new FakeHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes),
            };
        });

        var downloader = CreateDownloader(handler);
        var model = CreateModelDefinition(modelId,
            downloadUrl: "https://models.example.com/model.onnx");
        var targetBasePath = Path.Combine(_tempRoot, "models");

        // Act — first download places the file
        var firstResult = await downloader.DownloadModelAsync(model, targetBasePath);
        Assert.True(firstResult.Success);
        Assert.False(firstResult.AlreadyExisted);

        // Second call should detect the existing .onnx and short-circuit
        var secondResult = await downloader.DownloadModelAsync(model, targetBasePath);

        // Assert
        Assert.True(secondResult.Success);
        Assert.True(secondResult.AlreadyExisted);
        Assert.Equal(1, callCount); // HTTP was only called once
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static ModelDownloader CreateDownloader(FakeHttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var hfDownloader = new HuggingFaceModelDownloader(
            new OptionsMonitorStub<HuggingFaceOptions>(new HuggingFaceOptions()),
            NullLogger<HuggingFaceModelDownloader>.Instance);
        return new ModelDownloader(factory, hfDownloader, NullLogger<ModelDownloader>.Instance);
    }

    private static WyomingModelDefinition CreateModelDefinition(string id, string downloadUrl) =>
        new()
        {
            Id = id,
            Name = $"Test Model ({id})",
            EngineType = EngineType.Stt,
            Languages = ["en"],
            SizeBytes = 1024,
            Description = "Test model definition",
            DownloadUrl = downloadUrl,
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
