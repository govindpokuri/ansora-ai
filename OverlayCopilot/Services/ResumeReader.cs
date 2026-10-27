using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OverlayCopilot.Services;

public static class ResumeReader
{
    public const int MaxCharacters = 30000;
    public static string Read(string path)
    {
        if (new FileInfo(path).Length > 5 * 1024 * 1024) throw new ArgumentException("Choose a resume smaller than 5 MB.");
        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" => File.ReadAllText(path),
            ".docx" => ReadDocx(path),
            ".pdf" => ReadPdf(path),
            _ => throw new ArgumentException("Choose a PDF, DOCX or TXT resume. Save older DOC files as DOCX first.")
        };
        text = Regex.Replace(text, @"[\p{Cc}-[\r\n\t]]", "").Trim();
        if (text.Length < 30) throw new ArgumentException("No readable resume text found. For scanned PDFs, use a text-based PDF, DOCX or TXT file.");
        if (text.Length > MaxCharacters) throw new ArgumentException("Resume text exceeds 30,000 characters. Please upload a shorter resume.");
        return text;
    }
    private static string ReadDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new ArgumentException("This DOCX has no readable document.");
        if (entry.Length > 2 * 1024 * 1024) throw new ArgumentException("This DOCX is too large to analyse.");
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var document = XDocument.Load(reader); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", document.Descendants(w + "p").Select(p => string.Concat(p.Descendants().Select(n =>
            n.Name == w + "t" ? n.Value : n.Name == w + "tab" ? "\t" : n.Name == w + "br" ? "\n" : ""))));
    }
    private static string ReadPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        if (document.NumberOfPages > 20) throw new ArgumentException("Choose a resume with 20 pages or fewer.");
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var content = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("A PDF page has no readable text. Use a text-based PDF or DOCX; scanned pages need OCR first.");
            text.AppendLine(content);
            if (text.Length > MaxCharacters) throw new ArgumentException("Resume text exceeds 30,000 characters. Please upload a shorter resume.");
        }
        return text.ToString();
    }
}
