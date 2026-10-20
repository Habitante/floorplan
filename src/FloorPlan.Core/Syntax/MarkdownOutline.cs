using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>
/// A Markdown document's sections, from its headings outside fenced code. Each section runs to the
/// next heading of its level or above; its id is the heading's anchor as GitHub makes it, so links
/// such as "DESIGN.md#milestones" name it. A skeleton folds each section's own text.
/// </summary>
static partial class MarkdownOutline
{
    [GeneratedRegex(@"^ {0,3}(?<marks>#{1,6})(?:[ \t]+(?<title>.*?))?(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex Atx();
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})")]
    private static partial Regex Fence();
    [GeneratedRegex(@"^ {0,3}(?<under>=+|-+)[ \t]*$")]
    private static partial Regex Underline();
    [GeneratedRegex(@"!?\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    sealed class Section
    {
        public required int Level;
        public required string Title;
        public required int Line;
        public int TextFrom;
        public int To;
        public List<Section> Children = [];
    }

    public static FileOutline Of(string text, int lines)
    {
        var rows = text.Split('\n').Select(r => r.TrimEnd('\r')).ToArray();
        var headings = Headings(rows);
        var top = new List<Section>();
        var open = new Stack<Section>();
        foreach (var heading in headings)
        {
            while (open.Count > 0 && open.Peek().Level >= heading.Level) open.Pop();
            (open.Count > 0 ? open.Peek().Children : top).Add(heading);
            open.Push(heading);
        }
        // A section ends where the next heading of its level or above starts.
        for (int i = 0; i < headings.Count; i++)
        {
            var end = lines;
            for (int k = i + 1; k < headings.Count; k++)
                if (headings[k].Level <= headings[i].Level) { end = headings[k].Line - 1; break; }
            headings[i].To = Math.Max(end, headings[i].Line);
        }

        var folds = new List<LineRange>();
        foreach (var section in headings)
        {
            var textTo = section.Children.Count > 0 ? section.Children[0].Line - 1 : section.To;
            if (textTo - section.TextFrom + 1 >= Outlines.FoldMin) folds.Add(new LineRange(section.TextFrom, textTo));
        }

        var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<OutlineSymbol> Freeze(List<Section> sections) => sections.Select(s => new OutlineSymbol
        {
            Id = Anchor(s.Title, anchors),
            Kind = "section",
            Name = s.Title,
            From = s.Line,
            Line = s.Line,
            To = s.To,
            Children = s.Children.Count > 0 ? Freeze(s.Children) : null,
        }).ToList();
        // Anchors count repeats in document order, as GitHub does.
        return new FileOutline { Language = "markdown", Lines = lines, Symbols = Freeze(top), Folds = folds };
    }

    static List<Section> Headings(string[] rows)
    {
        var headings = new List<Section>();
        string? fence = null;
        int start = 0;
        // Front matter is not part of the document.
        if (rows.Length > 0 && rows[0].Trim() == "---")
            for (int i = 1; i < rows.Length; i++)
                if (rows[i].Trim() is "---" or "...") { start = i + 1; break; }
        for (int i = start; i < rows.Length; i++)
        {
            var row = rows[i];
            if (Fence().Match(row) is { Success: true } f)
            {
                var mark = f.Groups["fence"].Value;
                if (fence is null) fence = mark;
                else if (mark[0] == fence[0] && mark.Length >= fence.Length) fence = null;
                continue;
            }
            if (fence is not null) continue;
            if (Atx().Match(row) is { Success: true } atx)
            {
                headings.Add(new Section { Level = atx.Groups["marks"].Length, Title = atx.Groups["title"].Value.Trim(), Line = i + 1, TextFrom = i + 2 });
                continue;
            }
            // A paragraph's single line underlined with = or - (a lone "---" is a rule, not a heading).
            if (i + 1 < rows.Length && row.Trim().Length > 0 && !row.StartsWith("    ") && Underline().Match(rows[i + 1]) is { Success: true } under
                && (i == start || rows[i - 1].Trim().Length == 0) && !row.TrimStart().StartsWith('-') && !row.TrimStart().StartsWith('|') && !row.TrimStart().StartsWith('>'))
            {
                headings.Add(new Section { Level = under.Groups["under"].Value[0] == '=' ? 1 : 2, Title = row.Trim(), Line = i + 1, TextFrom = i + 3 });
                i++;
            }
        }
        return headings;
    }

    /// <summary>GitHub's anchor for a heading: its text lowercased, punctuation dropped, spaces as hyphens, repeats numbered.</summary>
    internal static string Anchor(string title, Dictionary<string, int> seen)
    {
        var text = Tag().Replace(Link().Replace(title, m => m.Groups["text"].Value), "");
        var builder = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsLetterOrDigit(c) || c is '-' or '_' || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) builder.Append(c);
            else if (c == ' ') builder.Append('-');
        }
        var slug = builder.ToString();
        var count = seen.GetValueOrDefault(slug);
        seen[slug] = count + 1;
        return count == 0 ? slug : $"{slug}-{count}";
    }
}
