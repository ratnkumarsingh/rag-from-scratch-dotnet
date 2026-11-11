using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rag.Core.Corpus;

/// <summary>
/// Downloads Wikipedia articles as plain text: the C# version of the course's generate_corpus.py.
/// Uses the free MediaWiki API directly with HttpClient (no extra package needed).
/// </summary>
public sealed partial class WikipediaCorpusGenerator : IDisposable
{
    private const string Api = "https://en.wikipedia.org/w/api.php";

    // Sections that are lists of links/citations, not knowledge. We cut the article there.
    private static readonly string[] JunkSections =
        ["See also", "References", "Notes", "Further reading", "External links", "Bibliography", "Sources", "Citations"];

    private readonly HttpClient _http = new();

    public WikipediaCorpusGenerator()
    {
        // Wikimedia asks every client to identify itself with a descriptive User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RagFromScratchTutorial/1.0 (learning project)");
    }

    /// <summary>Search Wikipedia for <paramref name="topic"/> and save up to <paramref name="maxArticles"/> articles as .txt files.</summary>
    public async Task<int> GenerateAsync(string topic, int maxArticles, string outputFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);

        var titles = await SearchTitlesAsync(topic, maxArticles, ct);
        Console.WriteLine($"Found {titles.Count} matching titles for '{topic}'.");

        int saved = 0;
        foreach (var title in titles)
        {
            var path = Path.Combine(outputFolder, SanitizeFileName(title) + ".txt");
            // Deviation from the book: re-running fills gaps instead of downloading everything again.
            if (File.Exists(path)) { saved++; Console.WriteLine($"  [{saved,3}] {title} (already on disk)"); continue; }

            try
            {
                var text = await GetPlainTextAsync(title, ct);
                if (text.Length < 500) { Console.WriteLine($"  skip (too short): {title}"); continue; }

                await File.WriteAllTextAsync(path, text, ct);
                saved++;
                Console.WriteLine($"  [{saved,3}] {title}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad article must not stop the whole run (same idea as the course's try/except).
                Console.WriteLine($"  Error processing '{title}': {ex.Message}");
            }

            await Task.Delay(500, ct);   // be polite to Wikipedia's servers (the book's 100 ms now triggers HTTP 429)
        }

        Console.WriteLine($"Done. Saved {saved} articles to '{outputFolder}'.");
        return saved;
    }

    /// <summary>Pages through the search API (max 50 results per request).</summary>
    private async Task<List<string>> SearchTitlesAsync(string topic, int max, CancellationToken ct)
    {
        var titles = new List<string>();
        int offset = 0;
        while (titles.Count < max)
        {
            int limit = Math.Min(50, max - titles.Count);
            var url = $"{Api}?action=query&list=search&format=json&formatversion=2" +
                      $"&srsearch={Uri.EscapeDataString(topic)}&srlimit={limit}&sroffset={offset}&srnamespace=0";

            using var doc = JsonDocument.Parse(await GetWithRetryAsync(url, ct));
            var results = doc.RootElement.GetProperty("query").GetProperty("search");
            foreach (var r in results.EnumerateArray()) titles.Add(r.GetProperty("title").GetString()!);

            // "continue.sroffset" tells us where the next page starts; no "continue" = no more results.
            if (!doc.RootElement.TryGetProperty("continue", out var cont)) break;
            offset = cont.GetProperty("sroffset").GetInt32();
        }
        return titles;
    }

    /// <summary>Fetches one article as plain text and removes the reference/link sections.</summary>
    private async Task<string> GetPlainTextAsync(string title, CancellationToken ct)
    {
        var url = $"{Api}?action=query&prop=extracts&explaintext=1&exsectionformat=wiki&redirects=1" +
                  $"&format=json&formatversion=2&titles={Uri.EscapeDataString(title)}";

        using var doc = JsonDocument.Parse(await GetWithRetryAsync(url, ct));
        var page = doc.RootElement.GetProperty("query").GetProperty("pages")[0];
        var extract = page.TryGetProperty("extract", out var e) ? e.GetString() ?? "" : "";
        return Clean(extract);
    }

    /// <summary>
    /// GET with a back-off on HTTP 429 (Too Many Requests). Deviation from the book: Wikimedia now
    /// rate-limits anonymous clients, so we wait (honouring Retry-After when sent) and try again.
    /// </summary>
    private async Task<string> GetWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var response = await _http.GetAsync(url, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt == 5)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
            Console.WriteLine($"  (rate limited, waiting {wait.TotalSeconds:F0}s)");
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>Real-world data is never clean: cut junk sections and tidy whitespace.</summary>
    internal static string Clean(string text)
    {
        // Headings arrive as "== References ==" (any number of '=').
        foreach (Match m in Heading().Matches(text))
        {
            if (JunkSections.Contains(m.Groups[1].Value.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                text = text[..m.Index];
                break;
            }
        }
        return ExtraBlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>Replace anything that is not a letter or digit with '_' (the course's sanitize_filename).</summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"^=+\s*(.+?)\s*=+\s*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
