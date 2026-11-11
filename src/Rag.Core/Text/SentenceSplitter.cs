using System.Text;
using System.Text.RegularExpressions;

namespace Rag.Core.Text;

/// <summary>
/// Splits text into sentences: the C# stand-in for NLTK's sent_tokenize.
/// A rule-based splitter is not perfect, but it handles normal prose,
/// common abbreviations ("Dr.", "e.g.", "U.S."), initials ("J. Smith"),
/// decimal numbers ("3.5"), headings and hard-wrapped lines.
/// </summary>
public static partial class SentenceSplitter
{
    // Abbreviations that end with a dot but do NOT end a sentence.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "mt", "vs", "etc", "e.g", "i.e",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        "v", "no", "vol", "fig", "approx", "inc", "ltd", "co", "corp", "u.s", "u.k", "u.n", "e.u",
    };

    /// <param name="minLength">Fragments shorter than this (stray headings, page numbers) are dropped.</param>
    public static List<string> Split(string text, int minLength = 15)
    {
        var sentences = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return sentences;

        foreach (var paragraph in ToParagraphs(text))
        {
            // Inside a paragraph, look at every ". ", "! " or "? " followed by a likely sentence start.
            int start = 0;
            foreach (Match m in CandidateBoundary().Matches(paragraph))
            {
                if (paragraph[m.Index] == '.' && IsAbbreviation(paragraph, m.Index)) continue;
                int boundary = m.Index + m.Length;          // includes punctuation, closing quote and spaces
                Add(sentences, paragraph[start..boundary], minLength);
                start = boundary;
            }
            Add(sentences, paragraph[start..], minLength);
        }
        return sentences;
    }

    /// <summary>
    /// Groups lines into paragraphs. A line break ends a paragraph when the line ends a sentence,
    /// when a blank line follows, or when the next line is a heading or bullet. Otherwise the
    /// line was just wrapped and gets joined to the next one.
    /// </summary>
    private static IEnumerable<string> ToParagraphs(string text)
    {
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = Whitespace().Replace(raw, " ").Trim();

            if (line.Length == 0 || IsHeading(line) || IsBullet(line))
            {
                if (current.Length > 0) { yield return current.ToString().Trim(); current.Clear(); }
                if (IsBullet(line)) yield return line.TrimStart('-', '*', '•', ' ');
                continue;   // blank lines and headings ("== History ==") are dropped
            }

            current.Append(line).Append(' ');
            if (".!?:\"”')".Contains(line[^1]))
            {
                yield return current.ToString().Trim();
                current.Clear();
            }
        }
        if (current.Length > 0) yield return current.ToString().Trim();
    }

    private static bool IsHeading(string line) => line.StartsWith('=') || line.StartsWith('#');
    private static bool IsBullet(string line) => line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith('•');

    private static void Add(List<string> list, string candidate, int minLength)
    {
        var s = candidate.Trim();
        if (s.Length >= minLength) list.Add(s);
    }

    /// <summary>True when the word right before the dot is a known abbreviation or a single initial ("J. Smith").</summary>
    private static bool IsAbbreviation(string text, int dotIndex)
    {
        int i = dotIndex - 1;
        while (i >= 0 && (char.IsLetter(text[i]) || text[i] == '.')) i--;
        var word = text[(i + 1)..dotIndex];
        if (word.Length == 0) return false;
        if (word.Length == 1 && char.IsUpper(word[0])) return true;   // initials: "J. R. R. Tolkien"
        return Abbreviations.Contains(word);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // ". " / "! " / "? " (optionally followed by a closing quote/bracket) then an upper-case letter, digit or opening quote.
    [GeneratedRegex(@"[.!?][""'”’)\]]?\s+(?=[A-Z0-9""“‘(\[])")]
    private static partial Regex CandidateBoundary();
}
