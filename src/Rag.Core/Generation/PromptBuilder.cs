using System.Text;
using Rag.Core.Retrieval;

namespace Rag.Core.Generation;

/// <summary>
/// Builds the DYNAMIC part of the prompt: retrieved context + the user's question.
/// The STATIC part (role, rules, examples) lives in the Modelfile's SYSTEM and MESSAGE lines.
///
/// This fixes the course's "raw tuples" problem: the LLM sees clean sentences grouped by
/// source with one numbered citation per block, never IDs, commas and parentheses.
/// </summary>
public static class PromptBuilder
{
    /// <summary>"Human_rights_in_Germany.txt" -> "Human rights in Germany".</summary>
    public static string SourceTitle(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();

    public static string FormatContext(IReadOnlyList<ContextWindow> windows)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < windows.Count; i++)
        {
            sb.AppendLine($"[Source {i + 1}: {SourceTitle(windows[i].FileName)}]");
            sb.AppendLine(string.Join(" ", windows[i].Sentences));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The question goes LAST: if a prompt is ever too long, Ollama cuts from the START,
    /// so the question must never be the part that gets cut.
    /// </summary>
    public static string BuildPrompt(string question, IReadOnlyList<ContextWindow> windows) =>
        $"""
        <context>
        {FormatContext(windows)}
        </context>

        Question: {question}
        """;
}
