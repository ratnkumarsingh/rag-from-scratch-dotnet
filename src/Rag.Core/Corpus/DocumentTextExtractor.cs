using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Pres = DocumentFormat.OpenXml.Presentation;

namespace Rag.Core.Corpus;

/// <summary>
/// Pulls plain text out of PDF, Word and PowerPoint files: the .NET stand-in for Docling.
///   PDF        -> PdfPig        (Apache 2.0)
///   DOCX, PPTX -> Open XML SDK  (MIT)
/// The output is written as .txt into the corpus folder so the ingestion step can treat
/// every source the same way (the course never connected Docling to its ingestion script).
/// </summary>
public static class DocumentTextExtractor
{
    public static readonly string[] SupportedExtensions = [".pdf", ".docx", ".pptx", ".txt", ".md"];

    public static string ExtractText(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => FromPdf(path),
        ".docx" => FromDocx(path),
        ".pptx" => FromPptx(path),
        ".txt" or ".md" => File.ReadAllText(path),
        var ext => throw new NotSupportedException($"File type '{ext}' is not supported.")
    };

    /// <summary>Extracts one file (or every supported file in a folder) into <paramref name="outputFolder"/>.</summary>
    public static int ExtractToFolder(string input, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var files = Directory.Exists(input)
            ? Directory.EnumerateFiles(input).Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            : [input];

        int count = 0;
        foreach (var file in files)
        {
            try
            {
                var text = ExtractText(file);
                var name = WikipediaCorpusGenerator.SanitizeFileName(Path.GetFileNameWithoutExtension(file)) + ".txt";
                File.WriteAllText(Path.Combine(outputFolder, name), text);
                Console.WriteLine($"  {Path.GetFileName(file)} -> {name} ({text.Length:N0} chars)");
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error extracting {file}: {ex.Message}");
            }
        }
        return count;
    }

    /// <summary>PDF: read the words on each page; one paragraph per page.</summary>
    private static string FromPdf(string path)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(path);
        foreach (var page in pdf.GetPages())
        {
            // page.Text often glues words together; joining the detected words is more reliable.
            sb.AppendLine(string.Join(" ", page.GetWords().Select(w => w.Text)));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>DOCX: paragraphs and tables, in document order. Table rows become "cell | cell | cell".</summary>
    private static string FromDocx(string path)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Word.Paragraph p:
                    if (!string.IsNullOrWhiteSpace(p.InnerText)) sb.AppendLine(p.InnerText).AppendLine();
                    break;
                case Word.Table table:
                    foreach (var row in table.Elements<Word.TableRow>())
                    {
                        var cells = row.Elements<Word.TableCell>().Select(c => c.InnerText.Trim());
                        sb.AppendLine(string.Join(" | ", cells) + ".");
                    }
                    sb.AppendLine();
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>PPTX: text of every slide, in slide order.</summary>
    private static string FromPptx(string path)
    {
        var sb = new StringBuilder();
        using var doc = PresentationDocument.Open(path, false);
        var presentationPart = doc.PresentationPart;
        var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<Pres.SlideId>();
        if (presentationPart is null || slideIds is null) return "";

        int number = 0;
        foreach (var slideId in slideIds)
        {
            number++;
            var relId = slideId.RelationshipId?.Value;
            if (relId is null) continue;
            var slidePart = (SlidePart)presentationPart.GetPartById(relId);

            var lines = slidePart.Slide?.Descendants<Drawing.Paragraph>()
                .Select(p => p.InnerText.Trim())
                .Where(t => t.Length > 0) ?? [];

            sb.AppendLine($"Slide {number}.").AppendLine();
            // A blank line after each text line keeps bullet points as separate "sentences".
            foreach (var line in lines) sb.AppendLine(line).AppendLine();
        }
        return sb.ToString();
    }
}
