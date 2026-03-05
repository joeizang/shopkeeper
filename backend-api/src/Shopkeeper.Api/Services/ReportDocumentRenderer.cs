using System.Text;

namespace Shopkeeper.Api.Services;

public sealed class ReportDocumentRenderer
{
    public byte[] RenderCsv(IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] RenderSimplePdf(string title, IEnumerable<string> lines)
    {
        var textLines = new List<string> { title, "Generated: " + DateTime.UtcNow.ToString("u"), string.Empty };
        textLines.AddRange(lines);

        var y = 800;
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 11 Tf");
        foreach (var line in textLines.Take(70))
        {
            content.AppendLine($"1 0 0 1 40 {y} Tm ({EscapePdf(line)}) Tj");
            y -= 12;
        }
        content.AppendLine("ET");

        var stream = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<string>
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >> endobj",
            "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj",
            $"5 0 obj << /Length {stream.Length} >> stream\n{Encoding.ASCII.GetString(stream)}\nendstream endobj"
        };

        var pdf = new StringBuilder();
        pdf.AppendLine("%PDF-1.4");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.AppendLine(obj);
        }

        var xrefPos = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.AppendLine($"xref\n0 {objects.Count + 1}");
        pdf.AppendLine("0000000000 65535 f ");
        for (var i = 1; i <= objects.Count; i++)
        {
            pdf.AppendLine($"{offsets[i]:0000000000} 00000 n ");
        }

        pdf.AppendLine($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>");
        pdf.AppendLine($"startxref\n{xrefPos}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string EscapeCsv(string value)
    {
        var text = value ?? string.Empty;
        var mustQuote = text.Contains(',') || text.Contains('"') || text.Contains('\n');
        if (!mustQuote)
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string EscapePdf(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }
}
