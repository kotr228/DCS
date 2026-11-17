using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace AsmodayCat.Agent.Documents;

public enum DocumentKind { Text, Image, Binary }

public sealed record DocumentContent(DocumentKind Kind, string Text, bool Truncated);

// Turns a file into something an LLM can read: text, an image, or a metadata summary.
public static class DocumentReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    static DocumentReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static async Task<DocumentContent> ReadAsync(string path, int maxChars, CancellationToken ct)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (IsImage(path))
            return new DocumentContent(DocumentKind.Image, Metadata(path), false);

        try
        {
            if (ext == ".docx") return Limit(ReadDocx(path), maxChars);
            if (ext == ".pdf")  return Limit(ReadPdf(path, maxChars), maxChars);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DocumentContent(DocumentKind.Binary,
                $"{Metadata(path)}\n(Could not extract text: {ex.Message})", false);
        }

        // Plain text: read at most ~4 bytes per allowed char
        var maxBytes = Math.Max(maxChars * 4, 16 * 1024);
        byte[] bytes;
        long   totalLength;
        await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true))
        {
            totalLength = fs.Length;
            bytes = new byte[(int)Math.Min(totalLength, maxBytes)];
            var read = 0;
            while (read < bytes.Length)
            {
                var n = await fs.ReadAsync(bytes.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read < bytes.Length) Array.Resize(ref bytes, read);
        }

        if (LooksBinary(bytes))
            return new DocumentContent(DocumentKind.Binary, Metadata(path), false);

        var text = Decode(bytes, cutAtEnd: totalLength > bytes.Length);
        var doc  = Limit(text, maxChars);
        return totalLength > bytes.Length ? doc with { Truncated = true } : doc;
    }

    public static string Metadata(string path)
    {
        var info = new FileInfo(path);
        return $"File: {info.Name}\nType: {info.Extension}\nSize: {info.Length:N0} bytes\n" +
               $"Created: {info.CreationTime:yyyy-MM-dd HH:mm}\nModified: {info.LastWriteTime:yyyy-MM-dd HH:mm}";
    }

    private static DocumentContent Limit(string text, int maxChars) =>
        text.Length > maxChars
            ? new DocumentContent(DocumentKind.Text, text[..maxChars], true)
            : new DocumentContent(DocumentKind.Text, text, false);

    private static bool LooksBinary(byte[] bytes)
    {
        // UTF-16 text has a BOM; otherwise NUL bytes mean binary
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return false;
        var probe = Math.Min(bytes.Length, 8000);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    private static string Decode(byte[] bytes, bool cutAtEnd)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // A truncated read can split a multi-byte char at the end; drop up to 3 trailing bytes
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (var trim = 0; trim <= (cutAtEnd ? 3 : 0) && trim < bytes.Length; trim++)
        {
            try { return strict.GetString(bytes, 0, bytes.Length - trim); }
            catch (DecoderFallbackException) { }
        }

        // Not UTF-8: assume the Cyrillic Windows code page
        return Encoding.GetEncoding(1251).GetString(bytes);
    }

    private static string ReadDocx(string path)
    {
        using var zip   = ZipFile.OpenRead(path);
        var entry       = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Not a Word document.");
        using var s     = entry.Open();
        var xml         = XDocument.Load(s);
        XNamespace w    = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var sb = new StringBuilder();
        foreach (var p in xml.Descendants(w + "p"))
        {
            foreach (var node in p.Descendants())
            {
                if (node.Name == w + "t")   sb.Append(node.Value);
                else if (node.Name == w + "tab") sb.Append('\t');
                else if (node.Name == w + "br")  sb.Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string ReadPdf(string path, int maxChars)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine(string.Join(' ', page.GetWords().Select(w => w.Text)));
            sb.AppendLine();
            if (sb.Length > maxChars) break;
        }
        return sb.ToString();
    }
}
