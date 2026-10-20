using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>A declaration in a file, or a section that a banner comment or a heading starts.</summary>
public sealed record OutlineSymbol
{
    /// <summary>
    /// Its name qualified by the declarations around it, unique in the file, so "path#id" names it
    /// across edits elsewhere in the file. A document's sections use their heading's anchor;
    /// sections of code have none.
    /// </summary>
    public string? Id { get; init; }
    /// <summary>class, function, method, variable, constant, section, ...</summary>
    public required string Kind { get; init; }
    public required string Name { get; init; }
    /// <summary>Its first line, with the comments and decorators that belong to it.</summary>
    public required int From { get; init; }
    /// <summary>The line the declaration or heading starts on.</summary>
    public required int Line { get; init; }
    public required int To { get; init; }
    public IReadOnlyList<OutlineSymbol>? Children { get; init; }
}

public readonly record struct LineRange(int From, int To);

/// <summary>What a file declares, and the lines its skeleton folds away.</summary>
public sealed record FileOutline
{
    /// <summary>gdscript, gdshader, python, javascript, typescript, tsx, csharp or markdown.</summary>
    public required string Language { get; init; }
    public required int Lines { get; init; }
    public required IReadOnlyList<OutlineSymbol> Symbols { get; init; }
    /// <summary>The lines a skeleton hides: bodies, long data, long comments and sections' text; outermost only, in order.</summary>
    public required IReadOnlyList<LineRange> Folds { get; init; }
    /// <summary>Places the grammar could not read, where the outline may miss declarations.</summary>
    public int? Errors { get; init; }

    public IEnumerable<OutlineSymbol> All() => Symbols.SelectMany(Flatten);

    static IEnumerable<OutlineSymbol> Flatten(OutlineSymbol symbol) => (symbol.Children ?? []).SelectMany(Flatten).Prepend(symbol);
}

/// <summary>
/// Outlines files: declarations from tree-sitter grammars, sections from banner comments, and a
/// document's sections from its headings. Read-only and on demand, one file at a time.
/// </summary>
public static class Outlines
{
    /// <summary>The largest file outlined, the reader's limit.</summary>
    public const int MaxBytes = 1024 * 1024;
    /// <summary>A fold hides at least this many lines; shorter bodies stay in view.</summary>
    public const int FoldMin = 4;

    public static bool IsMarkdown(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether FloorPlan outlines this kind of file.</summary>
    public static bool Supports(string path) => IsMarkdown(path) || Grammars.For(path) is not null;

    /// <summary>The outline of a file's content; null when FloorPlan does not outline its kind or it looks binary.</summary>
    public static FileOutline? Of(string path, byte[] content)
    {
        var text = content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? content[3..] : content;
        if (LineCounter.Count(text) is not int lines) return null;
        if (IsMarkdown(path)) return MarkdownOutline.Of(System.Text.Encoding.UTF8.GetString(text), lines);
        return Grammars.For(path) is { } grammar ? CodeOutline.Of(grammar, text, lines) : null;
    }

    /// <summary>Reads and outlines a file; the note says why there is no outline when it matters.</summary>
    public static async Task<(FileOutline? Outline, string? Note)> ReadAsync(string fullPath, string path, CancellationToken cancellationToken)
    {
        if (!Supports(path)) return (null, null);
        if (!IsMarkdown(path) && !TreeSitter.Available) return (null, "This FloorPlan was built without its parsers");
        var content = await LineCounter.ReadAsync(fullPath, MaxBytes + 1, cancellationToken);
        if (content.Length > MaxBytes) return (null, $"Too large to outline: over {MaxBytes / (1024 * 1024)} MiB");
        try
        {
            return (await Task.Run(() => Of(path, content), cancellationToken), null);
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
        {
            return (null, e.Message);
        }
    }
}

/// <summary>A source file's outline from its tree-sitter grammar and outline query.</summary>
static partial class CodeOutline
{
    // Kinds whose body folds; kinds that are data, whose long declarations fold after their first line;
    // kinds that hold others without naming them.
    static readonly HashSet<string> Bodies = ["function", "method", "constructor", "operator", "property", "test", "main"];
    static readonly HashSet<string> Data = ["variable", "constant", "field", "uniform", "varying", "type", "event", "delegate", "enum", "struct", "signal"];
    static readonly HashSet<string> Transparent = ["namespace", "section"];
    // Parents that wrap a declaration, and what belongs to the declaration that follows.
    static readonly HashSet<string> Wrappers = ["export_statement", "decorated_definition"];
    static readonly HashSet<string> Attached = ["decorator", "annotation", "annotations", "attribute_list"];

    sealed class Item
    {
        public required string Kind;
        public required string Name;
        public int From, Line, To;
        public LineRange? Fold;
        public List<Item> Children = [];
    }

    /// <summary>A banner starts a section; one without a title ends the section before it.</summary>
    readonly record struct Banner(int Line, string? Title);

    public static FileOutline Of(Grammar grammar, byte[] text, int lines)
    {
        using var tree = SyntaxTree.Parse(grammar, text);
        var items = Declarations(grammar, tree);
        var top = Nest(items);
        var bodies = items.Where(i => Bodies.Contains(i.Kind)).Select(i => (From: i.Line + 1, i.To)).ToList();
        AddSections(top, Banners(tree).Where(b => !bodies.Any(r => r.From <= b.Line && b.Line <= r.To)).ToList(), lines);

        var hidden = new bool[lines + 2];
        var folds = new List<LineRange>();
        void Hide(LineRange range)
        {
            folds.Add(range);
            for (int l = range.From; l <= range.To && l <= lines; l++) hidden[l] = true;
        }
        // Data that holds methods, and tests that hold tests, stay open so what they hold reads.
        foreach (var item in top.SelectMany(Flatten))
            if (item.Fold is { } fold && fold.To - fold.From + 1 >= Outlines.FoldMin && !hidden[fold.From]
                && !(item.Children.Count > 0 && (Data.Contains(item.Kind) || item.Kind == "test"))) Hide(fold);
        if (grammar.Name == "python")
            foreach (var doc in Docstrings(tree))
                if (doc.To - doc.From + 1 >= Outlines.FoldMin && !hidden[doc.From]) Hide(doc);
        foreach (var run in CommentRuns(tree, hidden))
            if (run.To - run.From >= Outlines.FoldMin) Hide(new LineRange(run.From + 1, run.To));
        folds.Sort((a, b) => a.From.CompareTo(b.From));

        var errors = tree.Errors();
        return new FileOutline
        {
            Language = grammar.Name,
            Lines = lines,
            Symbols = Freeze(top, "", new Dictionary<string, int>(StringComparer.Ordinal)),
            Folds = folds,
            Errors = errors > 0 ? errors : null,
        };
    }

    static IEnumerable<Item> Flatten(Item item) => item.Children.SelectMany(Flatten).Prepend(item);

    static List<Item> Declarations(Grammar grammar, SyntaxTree tree)
    {
        var query = grammar.Query;
        var found = new Dictionary<(uint, uint), (uint Pattern, string Kind, TSNode Node, TSNode? Name, TSNode? Body)>();
        var size = Marshal.SizeOf<TSQueryCapture>();
        var cursor = TreeSitter.ts_query_cursor_new();
        try
        {
            TreeSitter.ts_query_cursor_exec(cursor, query.Handle, tree.Root);
            var nodes = new Dictionary<uint, TSNode>();
            while (TreeSitter.ts_query_cursor_next_match(cursor, out var match))
            {
                nodes.Clear();
                TSNode? definition = null, name = null, body = null;
                string kind = "";
                for (int i = 0; i < match.CaptureCount; i++)
                {
                    var capture = Marshal.PtrToStructure<TSQueryCapture>(match.Captures + i * size);
                    nodes[capture.Index] = capture.Node;
                    var captureName = query.CaptureName(capture.Index);
                    if (captureName.StartsWith("definition.", StringComparison.Ordinal)) (definition, kind) = (capture.Node, captureName["definition.".Length..]);
                    else if (captureName == "name") name = capture.Node;
                    else if (captureName == "body") body = capture.Node;
                }
                if (definition is not { } node || !query.Holds(match.PatternIndex, nodes, tree)) continue;
                // A node several patterns match keeps the one that found its body, then the first.
                var key = (TreeSitter.ts_node_start_byte(node), TreeSitter.ts_node_end_byte(node));
                if (found.TryGetValue(key, out var had))
                {
                    bool keep = (had.Body is not null) == (body is not null) ? had.Pattern <= match.PatternIndex : had.Body is not null;
                    if (keep) continue;
                }
                found[key] = (match.PatternIndex, kind, node, name, body);
            }
        }
        finally
        {
            TreeSitter.ts_query_cursor_delete(cursor);
        }

        var items = new List<Item>();
        foreach (var (_, f) in found.OrderBy(x => x.Key.Item1).ThenByDescending(x => x.Key.Item2))
        {
            var anchor = f.Node;
            var parent = TreeSitter.ts_node_parent(f.Node);
            if (!TreeSitter.ts_node_is_null(parent) && Wrappers.Contains(TreeSitter.Type(parent))) anchor = parent;
            var line = TreeSitter.StartLine(anchor);
            var item = new Item
            {
                Kind = f.Kind,
                Name = f.Name is { } n ? OneLine(tree.TextOf(n)) : $"({f.Kind})",
                Line = line,
                From = Leading(tree, anchor, line),
                To = TreeSitter.EndLine(anchor),
            };
            if (f.Body is { } b && Bodies.Contains(f.Kind)) item.Fold = BodyFold(grammar, tree, b);
            else if (Data.Contains(f.Kind) && item.To - line >= Outlines.FoldMin) item.Fold = new LineRange(line + 1, item.To);
            items.Add(item);
        }
        return items;
    }

    static string OneLine(string text) => WhiteSpace().Replace(text.Trim(), " ");

    static bool IsComment(string type) => type == "comment" || type.EndsWith("_comment", StringComparison.Ordinal);

    // Comments right above a declaration, and its decorators, belong to it; a blank line or a banner ends them.
    static int Leading(SyntaxTree tree, TSNode anchor, int from)
    {
        for (var prev = TreeSitter.ts_node_prev_named_sibling(anchor); !TreeSitter.ts_node_is_null(prev); prev = TreeSitter.ts_node_prev_named_sibling(prev))
        {
            var type = TreeSitter.Type(prev);
            bool comment = IsComment(type);
            if (!comment && !Attached.Contains(type)) break;
            if (TreeSitter.EndLine(prev) < from - 1) break;
            if (comment && (!tree.StartsLine(prev) || BannerOf(tree.TextOf(prev)) is not null || IsRule(tree.TextOf(prev)))) break;
            from = TreeSitter.StartLine(prev);
        }
        return from;
    }

    static LineRange? BodyFold(Grammar grammar, SyntaxTree tree, TSNode body)
    {
        int from = tree.StartsLine(body) ? TreeSitter.StartLine(body) : TreeSitter.StartLine(body) + 1;
        // A Python docstring's first line stays in view: it says what the function does.
        if (grammar.Name == "python" && Docstring(body) is { } doc) from = TreeSitter.StartLine(doc) + 1;
        int to = TreeSitter.EndLine(body);
        return to >= from ? new LineRange(from, to) : null;
    }

    static TSNode? Docstring(TSNode block)
    {
        if (TreeSitter.ts_node_is_null(block) || TreeSitter.ts_node_child_count(block) == 0) return null;
        var first = TreeSitter.ts_node_child(block, 0);
        return TreeSitter.Type(first) == "expression_statement" && TreeSitter.ts_node_child_count(first) > 0
            && TreeSitter.Type(TreeSitter.ts_node_child(first, 0)) == "string" ? first : null;
    }

    // Module and class docstrings fold to their first line, the summary.
    static IEnumerable<LineRange> Docstrings(SyntaxTree tree)
    {
        var holders = new List<TSNode> { tree.Root };
        tree.Walk(node =>
        {
            if (TreeSitter.Type(node) == "class_definition") holders.Add(TreeSitter.Field(node, "body"));
            return true;
        });
        foreach (var holder in holders)
            if (Docstring(holder) is { } doc && TreeSitter.EndLine(doc) > TreeSitter.StartLine(doc))
                yield return new LineRange(TreeSitter.StartLine(doc) + 1, TreeSitter.EndLine(doc));
    }

    static List<Item> Nest(List<Item> flat)
    {
        var top = new List<Item>();
        var open = new Stack<Item>();
        foreach (var item in flat.OrderBy(i => i.Line).ThenByDescending(i => i.To))
        {
            while (open.Count > 0 && !(open.Peek().Line <= item.Line && item.To <= open.Peek().To)) open.Pop();
            (open.Count > 0 ? open.Peek().Children : top).Add(item);
            open.Push(item);
        }
        return top;
    }

    // A banner starts a section that runs to the next banner at its level, or to the end of what holds it.
    static void AddSections(List<Item> top, List<Banner> banners, int lines)
    {
        void Level(List<Item> list, int from, int to, Func<int, bool> free)
        {
            foreach (var item in list.Where(i => i.Children.Count > 0))
                Level(item.Children, item.Line, item.To, l => !item.Children.Any(c => c.From <= l && l <= c.To));
            var mine = banners.Where(b => b.Line >= from && b.Line <= to && free(b.Line)).ToList();
            if (!mine.Any(b => b.Title is not null)) return;
            var result = list.Where(i => i.From < mine[0].Line).ToList();
            for (int i = 0; i < mine.Count; i++)
            {
                var next = i + 1 < mine.Count ? mine[i + 1] : (Banner?)null;
                var end = next is { } n ? (n.Title is null ? n.Line : n.Line - 1) : to;
                var inside = list.Where(item => item.From > mine[i].Line && item.From <= end).ToList();
                if (mine[i].Title is not { } title)
                {
                    result.AddRange(inside);
                    continue;
                }
                result.Add(new Item { Kind = "section", Name = title, Line = mine[i].Line, From = mine[i].Line, To = Math.Max(end, inside.Count > 0 ? inside[^1].To : end), Children = inside });
            }
            list.Clear();
            list.AddRange(result);
        }
        Level(top, 1, lines, l => !top.Any(i => i.From <= l && l <= i.To));
    }

    static IReadOnlyList<OutlineSymbol> Freeze(List<Item> items, string scope, Dictionary<string, int> seen)
    {
        var result = new List<OutlineSymbol>();
        foreach (var item in items)
        {
            string? id = null;
            IReadOnlyList<OutlineSymbol>? children = null;
            if (Transparent.Contains(item.Kind))
            {
                // Sections and namespaces hold symbols without naming them, so moving one between sections keeps its id.
                if (item.Children.Count > 0) children = Freeze(item.Children, scope, seen);
            }
            else
            {
                var qualified = scope.Length == 0 ? item.Name : scope + "." + item.Name;
                var count = seen[qualified] = seen.GetValueOrDefault(qualified) + 1;
                id = count == 1 ? qualified : $"{qualified}~{count}";
                if (item.Children.Count > 0) children = Freeze(item.Children, id, new Dictionary<string, int>(StringComparer.Ordinal));
            }
            result.Add(new OutlineSymbol { Id = id, Kind = item.Kind, Name = item.Name, From = item.From, Line = item.Line, To = item.To, Children = children });
        }
        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
    [GeneratedRegex(@"^\s*(?://+|#+|/\*+|;+|--)\s*(?:#?region\b|MARK:)\s*(?<title>.+?)\s*(?:\*/)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RegionStart();
    [GeneratedRegex(@"^\s*(?://+|#+|/\*+|;+|--)\s*#?endregion\b", RegexOptions.IgnoreCase)]
    private static partial Regex RegionEnd();
    [GeneratedRegex(@"^\s*(?://+|#+|/\*+|;+|--)\s*[-=*#~]{3,}\s*(?<title>[^-=*#~\s].*?)\s*[-=*#~]*\s*(?:\*/)?\s*$")]
    private static partial Regex TitledRule();
    [GeneratedRegex(@"^\s*(?://+|#+|/\*+|;+|--)?\s*[-=*#~]{3,}\s*(?:\*/)?\s*$")]
    private static partial Regex RuleOnly();
    [GeneratedRegex(@"^\s*(?://+|#+|;+|--)\s*(?<title>\S.{0,60}?)\s*$")]
    private static partial Regex PlainComment();

    /// <summary>The title of a one-line banner: a titled rule ("# --- Saving ---") or a region marker.</summary>
    static string? BannerOf(string text)
    {
        if (text.Contains('\n')) return null;
        var match = RegionStart().Match(text);
        if (!match.Success) match = TitledRule().Match(text);
        return match.Success && match.Groups["title"].Value.Any(char.IsLetter) ? match.Groups["title"].Value : null;
    }

    static bool IsRule(string text) => !text.Contains('\n') && RuleOnly().IsMatch(text);

    // Banners: titled rules, region markers, a title between two rules, C# regions and a shader's uniform groups.
    static List<Banner> Banners(SyntaxTree tree)
    {
        var banners = new List<Banner>();
        var comments = new List<(int Line, string Text)>();
        tree.Walk(node =>
        {
            var type = TreeSitter.Type(node);
            switch (type)
            {
                case "preproc_region":
                    var content = TreeSitter.Field(node, "content");
                    banners.Add(new Banner(TreeSitter.StartLine(node), TreeSitter.ts_node_is_null(content) ? "(region)" : OneLine(tree.TextOf(content))));
                    return false;
                case "preproc_endregion":
                    banners.Add(new Banner(TreeSitter.StartLine(node), null));
                    return false;
                case "group_uniforms_declaration":
                    var group = TreeSitter.Field(node, "group_name");
                    banners.Add(new Banner(TreeSitter.StartLine(node), TreeSitter.ts_node_is_null(group) ? null : tree.TextOf(group)));
                    return false;
            }
            if (!IsComment(type)) return true;
            if (tree.StartsLine(node)) comments.Add((TreeSitter.StartLine(node), tree.TextOf(node)));
            return false;
        });
        for (int i = 0; i < comments.Count; i++)
        {
            var (line, text) = comments[i];
            if (!text.Contains('\n') && RegionEnd().IsMatch(text))
            {
                banners.Add(new Banner(line, null));
                continue;
            }
            if (BannerOf(text) is { } title)
            {
                banners.Add(new Banner(line, title));
                continue;
            }
            // A title between two rules, or a rule and a title: the banner starts at the first rule.
            if (IsRule(text) && i + 1 < comments.Count && comments[i + 1].Line == line + 1 && !IsRule(comments[i + 1].Text)
                && PlainComment().Match(comments[i + 1].Text) is { Success: true } plain && plain.Groups["title"].Value.Any(char.IsLetter)
                && (i + 2 >= comments.Count || comments[i + 2].Line != line + 2 || IsRule(comments[i + 2].Text)))
            {
                banners.Add(new Banner(line, plain.Groups["title"].Value));
                i += 2;
            }
        }
        banners.Sort((a, b) => a.Line.CompareTo(b.Line));
        return banners;
    }

    // Runs of whole-line comments outside what is already hidden, banners aside.
    static List<LineRange> CommentRuns(SyntaxTree tree, bool[] hidden)
    {
        var lines = new SortedSet<int>();
        tree.Walk(node =>
        {
            if (!IsComment(TreeSitter.Type(node))) return true;
            var text = tree.TextOf(node);
            if (tree.StartsLine(node) && !IsRule(text) && BannerOf(text) is null && !RegionEnd().IsMatch(text))
                for (int l = TreeSitter.StartLine(node); l <= TreeSitter.EndLine(node) && l < hidden.Length; l++)
                    if (!hidden[l]) lines.Add(l);
            return false;
        });
        var runs = new List<LineRange>();
        foreach (var l in lines)
            if (runs.Count > 0 && runs[^1].To == l - 1) runs[^1] = runs[^1] with { To = l };
            else runs.Add(new LineRange(l, l));
        return runs;
    }
}
