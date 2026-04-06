using AgentCraftLab.Cleaner.Partitioners;

namespace AgentCraftLab.Tests.Cleaner;

public class OcrEngineAdapterTests
{
    [Fact]
    public async Task Adapter_ConvertsLanguageFormat()
    {
        IReadOnlyList<string>? capturedLangs = null;

        var adapter = new OcrEngineAdapter((data, langs, ct) =>
        {
            capturedLangs = langs;
            return Task.FromResult(("OCR text", 0.9f));
        });

        var result = await adapter.RecognizeAsync([0xFF], "chi_tra+eng+jpn");

        Assert.Equal("OCR text", result.Text);
        Assert.Equal(0.9f, result.Confidence);
        Assert.NotNull(capturedLangs);
        Assert.Equal(3, capturedLangs!.Count);
        Assert.Equal("chi_tra", capturedLangs[0]);
        Assert.Equal("eng", capturedLangs[1]);
        Assert.Equal("jpn", capturedLangs[2]);
    }

    [Fact]
    public async Task Adapter_EmptyLanguages_PassesNull()
    {
        IReadOnlyList<string>? capturedLangs = null;

        var adapter = new OcrEngineAdapter((data, langs, ct) =>
        {
            capturedLangs = langs;
            return Task.FromResult(("text", 0.5f));
        });

        await adapter.RecognizeAsync([0xFF], "");

        Assert.Null(capturedLangs);
    }

    [Fact]
    public async Task Adapter_IntegratesWithImagePartitioner()
    {
        var adapter = new OcrEngineAdapter((data, langs, ct) =>
            Task.FromResult(("Recognized: Hello World", 0.95f)));

        var partitioner = new ImagePartitioner(adapter);
        var elements = await partitioner.PartitionAsync([0xFF, 0xD8], "scan.png");

        Assert.Single(elements);
        Assert.Equal("Recognized: Hello World", elements[0].Text);
    }
}
