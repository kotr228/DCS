using System.IO.Compression;
using System.Text;
using AsmodayCat.Agent.Documents;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AsmodayCat.Tests;

public class DocumentReaderTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    static DocumentReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public async Task Reads_utf8_and_windows_1251_text()
    {
        File.WriteAllText(_dir.File("utf8.txt"), "Привіт, світ", new UTF8Encoding(false));
        File.WriteAllBytes(_dir.File("cp1251.txt"), Encoding.GetEncoding(1251).GetBytes("Звіт за грудень"));

        Assert.Equal("Привіт, світ", (await DocumentReader.ReadAsync(_dir.File("utf8.txt"), 1000, default)).Text);
        Assert.Equal("Звіт за грудень", (await DocumentReader.ReadAsync(_dir.File("cp1251.txt"), 1000, default)).Text);
    }

    [Fact]
    public async Task Long_text_is_truncated()
    {
        File.WriteAllText(_dir.File("long.txt"), new string('а', 5000));
        var doc = await DocumentReader.ReadAsync(_dir.File("long.txt"), 1000, default);
        Assert.True(doc.Truncated);
        Assert.Equal(1000, doc.Text.Length);
    }

    [Fact]
    public async Task Binary_files_give_metadata_only()
    {
        File.WriteAllBytes(_dir.File("data.bin"), [1, 0, 2, 0, 3, 0, 255]);
        var doc = await DocumentReader.ReadAsync(_dir.File("data.bin"), 1000, default);
        Assert.Equal(DocumentKind.Binary, doc.Kind);
        Assert.Contains("data.bin", doc.Text);
    }

    [Fact]
    public async Task Images_are_recognized()
    {
        File.WriteAllBytes(_dir.File("photo.png"), [137, 80, 78, 71]);
        Assert.Equal(DocumentKind.Image, (await DocumentReader.ReadAsync(_dir.File("photo.png"), 1000, default)).Kind);
    }

    [Fact]
    public async Task Reads_docx_paragraphs()
    {
        var path = _dir.File("letter.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                <w:p><w:r><w:t>Перший абзац</w:t></w:r></w:p>
                <w:p><w:r><w:t>Другий</w:t></w:r><w:r><w:tab/><w:t>рядок</w:t></w:r></w:p>
                </w:body></w:document>
                """);
        }

        var doc = await DocumentReader.ReadAsync(path, 1000, default);
        Assert.Equal(DocumentKind.Text, doc.Kind);
        Assert.Contains("Перший абзац", doc.Text);
        Assert.Contains("Другий\tрядок", doc.Text);
    }

    [Fact]
    public async Task Reads_pdf_text()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Quarterly report 2026", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(_dir.File("report.pdf"), builder.Build());

        var doc = await DocumentReader.ReadAsync(_dir.File("report.pdf"), 1000, default);
        Assert.Equal(DocumentKind.Text, doc.Kind);
        Assert.Contains("Quarterly report 2026", doc.Text);
    }
}
