using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Partitioners;
using ClosedXML.Excel;

namespace AgentCraftLab.Tests.Cleaner;

public class XlsxPartitionerTests
{
    [Fact]
    public async Task Xlsx_BasicSheet_ProducesTableElement()
    {
        var data = CreateXlsx(wb =>
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Age";
            ws.Cell(2, 1).Value = "Alice";
            ws.Cell(2, 2).Value = 30;
            ws.Cell(3, 1).Value = "Bob";
            ws.Cell(3, 2).Value = 25;
        });

        var partitioner = new XlsxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "data.xlsx");

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
        Assert.Contains("Name", elements[0].Text);
        Assert.Contains("Alice", elements[0].Text);
        Assert.Contains("---", elements[0].Text); // Markdown separator
        Assert.Contains("|", elements[0].Text);
    }

    [Fact]
    public async Task Xlsx_MultipleSheets_ProducesMultipleElements()
    {
        var data = CreateXlsx(wb =>
        {
            var ws1 = wb.AddWorksheet("Revenue");
            ws1.Cell(1, 1).Value = "Q1";
            ws1.Cell(1, 2).Value = 100;

            var ws2 = wb.AddWorksheet("Costs");
            ws2.Cell(1, 1).Value = "Item";
            ws2.Cell(1, 2).Value = "Amount";
        });

        var partitioner = new XlsxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "multi.xlsx");

        Assert.Equal(2, elements.Count);
        Assert.All(elements, e => Assert.Equal(ElementType.Table, e.Type));
        Assert.Equal("Revenue", elements[0].Metadata[MetadataKeys.SheetName]);
        Assert.Equal("Costs", elements[1].Metadata[MetadataKeys.SheetName]);
    }

    [Fact]
    public async Task Xlsx_EmptySheet_Skipped()
    {
        var data = CreateXlsx(wb =>
        {
            wb.AddWorksheet("Empty");
            var ws2 = wb.AddWorksheet("HasData");
            ws2.Cell(1, 1).Value = "Data";
        });

        var partitioner = new XlsxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "mixed.xlsx");

        Assert.Single(elements);
    }

    [Fact]
    public async Task Xlsx_PipeInCell_Escaped()
    {
        var data = CreateXlsx(wb =>
        {
            var ws = wb.AddWorksheet("Test");
            ws.Cell(1, 1).Value = "A|B";
        });

        var partitioner = new XlsxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "pipe.xlsx");

        Assert.Contains("\\|", elements[0].Text);
    }

    [Fact]
    public async Task Xlsx_MetadataContainsSheetCount()
    {
        var data = CreateXlsx(wb =>
        {
            var ws = wb.AddWorksheet("S1");
            ws.Cell(1, 1).Value = "X";
            wb.AddWorksheet("S2");
        });

        var partitioner = new XlsxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.xlsx");

        Assert.Equal("2", elements[0].Metadata[MetadataKeys.SheetCount]);
    }

    [Fact]
    public void Xlsx_CanPartition_CorrectMimeTypes()
    {
        var p = new XlsxPartitioner();
        Assert.True(p.CanPartition("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.True(p.CanPartition("application/vnd.ms-excel"));
        Assert.False(p.CanPartition("text/plain"));
    }

    private static byte[] CreateXlsx(Action<XLWorkbook> configure)
    {
        using var wb = new XLWorkbook();
        configure(wb);
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
