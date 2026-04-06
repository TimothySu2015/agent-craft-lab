using AgentCraftLab.Cleaner;

namespace AgentCraftLab.Tests.Cleaner;

public class MimeTypeHelperTests
{
    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".xls", "application/vnd.ms-excel")]
    [InlineData(".html", "text/html")]
    [InlineData(".htm", "text/html")]
    [InlineData(".md", "text/markdown")]
    [InlineData(".csv", "text/csv")]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".cs", "text/x-csharp")]
    [InlineData(".py", "text/x-python")]
    [InlineData(".js", "text/javascript")]
    [InlineData(".ts", "text/x-typescript")]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".webp", "image/webp")]
    public void FromExtension_MapsCorrectly(string ext, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.FromExtension($"file{ext}"));
    }

    [Theory]
    [InlineData(".unknown")]
    [InlineData(".abc")]
    [InlineData("")]
    public void FromExtension_UnknownType_ReturnsOctetStream(string ext)
    {
        Assert.Equal("application/octet-stream", MimeTypeHelper.FromExtension($"file{ext}"));
    }

    [Fact]
    public void FromExtension_CaseInsensitive()
    {
        Assert.Equal("application/pdf", MimeTypeHelper.FromExtension("FILE.PDF"));
    }
}
