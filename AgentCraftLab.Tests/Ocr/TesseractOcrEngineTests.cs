using AgentCraftLab.Ocr;

namespace AgentCraftLab.Tests.Ocr;

public class TesseractOcrEngineTests
{
    [Fact]
    public async Task RecognizeAsync_EmptyByteArray_ReturnsEmptyResult()
    {
        using var engine = CreateEngine();

        var result = await engine.RecognizeAsync([]);

        Assert.Equal("", result.Text);
        Assert.Equal(0, result.Confidence);
        Assert.False(result.HasContent);
    }

    [Fact]
    public async Task RecognizeAsync_NullImageData_ThrowsArgumentNullException()
    {
        using var engine = CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.RecognizeAsync(null!));
    }

    [Fact]
    public async Task RecognizeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var engine = CreateEngine();
        engine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.RecognizeAsync([1, 2, 3]));
    }

    [Fact]
    public void Constructor_InvalidTessDataPath_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new TesseractOcrEngine("/nonexistent/path/tessdata"));
    }

    /// <summary>建立 Engine 指向暫存目錄（不含 traineddata，僅供不觸發 Tesseract 的測試用）。</summary>
    private static TesseractOcrEngine CreateEngine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ocr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return new TesseractOcrEngine(tempDir);
    }
}
