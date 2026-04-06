using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Partitioners;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AgentCraftLab.Tests.Cleaner;

public class ImagePreprocessorTests
{
    [Fact]
    public void TooSmallImage_ReturnsNull()
    {
        var imageData = CreateTestImage(30, 30);
        var options = new PartitionOptions { MinImageWidth = 50, MinImageHeight = 50 };

        var result = ImagePreprocessor.Process(imageData, options);

        Assert.Null(result);
    }

    [Fact]
    public void NormalImage_ReturnsProcessedResult()
    {
        var imageData = CreateTestImage(200, 100);
        var options = new PartitionOptions();

        var result = ImagePreprocessor.Process(imageData, options);

        Assert.NotNull(result);
        Assert.Equal(200, result.Width);
        Assert.Equal(100, result.Height);
        Assert.False(result.WasResized);
        Assert.Equal("image/png", result.MimeType);
        Assert.NotEmpty(result.Hash);
    }

    [Fact]
    public void LargeImage_GetsResized()
    {
        var imageData = CreateTestImage(4000, 3000);
        var options = new PartitionOptions { MaxImageDimension = 2048 };

        var result = ImagePreprocessor.Process(imageData, options);

        Assert.NotNull(result);
        Assert.True(result.WasResized);
        Assert.True(result.Width <= 2048);
        Assert.True(result.Height <= 2048);
        Assert.Equal(4000, result.OriginalWidth);
        Assert.Equal(3000, result.OriginalHeight);
    }

    [Fact]
    public void HashIsDeterministic()
    {
        var imageData = CreateTestImage(100, 100);
        var options = new PartitionOptions();

        var result1 = ImagePreprocessor.Process(imageData, options);
        var result2 = ImagePreprocessor.Process(imageData, options);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(result1.Hash, result2.Hash);
    }

    [Fact]
    public void DifferentImages_DifferentHashes()
    {
        var image1 = CreateTestImage(100, 100, Rgba32.ParseHex("FF0000"));
        var image2 = CreateTestImage(100, 100, Rgba32.ParseHex("0000FF"));
        var options = new PartitionOptions();

        var result1 = ImagePreprocessor.Process(image1, options);
        var result2 = ImagePreprocessor.Process(image2, options);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotEqual(result1.Hash, result2.Hash);
    }

    [Fact]
    public void ExactlyMinSize_IsProcessed()
    {
        var imageData = CreateTestImage(50, 50);
        var options = new PartitionOptions { MinImageWidth = 50, MinImageHeight = 50 };

        var result = ImagePreprocessor.Process(imageData, options);

        Assert.NotNull(result);
    }

    [Fact]
    public void ExactlyMaxDimension_NotResized()
    {
        var imageData = CreateTestImage(2048, 1024);
        var options = new PartitionOptions { MaxImageDimension = 2048 };

        var result = ImagePreprocessor.Process(imageData, options);

        Assert.NotNull(result);
        Assert.False(result.WasResized);
        Assert.Equal(2048, result.Width);
    }

    private static byte[] CreateTestImage(int width, int height, Rgba32? color = null)
    {
        using var image = new Image<Rgba32>(width, height, color ?? Rgba32.ParseHex("808080"));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
