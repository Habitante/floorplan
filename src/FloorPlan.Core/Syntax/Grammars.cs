using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>A language FloorPlan outlines with tree-sitter: its grammar and the query that finds its declarations.</summary>
sealed class Grammar
{
    readonly Lazy<IntPtr> handle;
    readonly Lazy<OutlineQuery> outline;

    public Grammar(string name, Func<IntPtr> language, string query)
    {
        Name = name;
        // The library loads by its full path before any grammar's entry point is called.
        handle = new(() => TreeSitter.Available ? language() : throw new DllNotFoundException("This FloorPlan was built without its parsers"));
        outline = new(() => OutlineQuery.Load(name, handle.Value, query));
    }

    /// <summary>The language's name in outlines: gdscript, python, csharp, ...</summary>
    public string Name { get; }
    public IntPtr Language => handle.Value;
    public OutlineQuery Query => outline.Value;
}

static class Grammars
{
    static readonly Dictionary<string, Grammar> ByExtension = Build();

    static Dictionary<string, Grammar> Build()
    {
        var map = new Dictionary<string, Grammar>(StringComparer.OrdinalIgnoreCase);
        void Add(Grammar grammar, params string[] extensions)
        {
            foreach (var extension in extensions) map[extension] = grammar;
        }
        Add(new Grammar("gdscript", TreeSitter.tree_sitter_gdscript, "gdscript"), ".gd");
        Add(new Grammar("gdshader", TreeSitter.tree_sitter_gdshader, "gdshader"), ".gdshader", ".gdshaderinc");
        Add(new Grammar("python", TreeSitter.tree_sitter_python, "python"), ".py", ".pyw");
        Add(new Grammar("javascript", TreeSitter.tree_sitter_javascript, "javascript"), ".js", ".mjs", ".cjs", ".jsx");
        Add(new Grammar("typescript", TreeSitter.tree_sitter_typescript, "typescript"), ".ts", ".mts", ".cts");
        Add(new Grammar("tsx", TreeSitter.tree_sitter_tsx, "typescript"), ".tsx");
        Add(new Grammar("csharp", TreeSitter.tree_sitter_c_sharp, "csharp"), ".cs");
        return map;
    }

    public static Grammar? For(string path) => ByExtension.GetValueOrDefault(Path.GetExtension(path));
}

/// <summary>
/// A grammar's outline query, from Syntax/Queries: each pattern captures a declaration as
/// @definition.&lt;kind&gt;, with @name and, where it folds, @body. Text predicates (#eq?, #any-of?,
/// #match?) are checked here, since tree-sitter leaves them to the caller.
/// </summary>
sealed class OutlineQuery
{
    readonly ConcurrentDictionary<uint, string> captures = new();
    readonly ConcurrentDictionary<uint, Predicate[]> predicates = new();

    OutlineQuery(IntPtr handle) => Handle = handle;

    public IntPtr Handle { get; }

    sealed record Predicate(string Operator, uint Capture, string[] Values);

    public static OutlineQuery Load(string grammar, IntPtr language, string name)
    {
        using var stream = typeof(OutlineQuery).Assembly.GetManifestResourceStream($"outline/{name}.scm")
            ?? throw new InvalidOperationException($"The {name} outline query is not embedded");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var source = memory.ToArray();
        var query = TreeSitter.ts_query_new(language, source, (uint)source.Length, out var offset, out var error);
        if (query == IntPtr.Zero)
        {
            var at = Encoding.UTF8.GetString(source, (int)offset, Math.Min(60, source.Length - (int)offset));
            throw new InvalidOperationException($"The {grammar} outline query does not fit its grammar (error {error}) at: {at}");
        }
        return new OutlineQuery(query);
    }

    public string CaptureName(uint id) =>
        captures.GetOrAdd(id, i => Marshal.PtrToStringUTF8(TreeSitter.ts_query_capture_name_for_id(Handle, i, out var length), (int)length));

    /// <summary>Whether a match's text predicates hold.</summary>
    public bool Holds(uint pattern, IReadOnlyDictionary<uint, TSNode> nodes, SyntaxTree tree)
    {
        foreach (var predicate in predicates.GetOrAdd(pattern, Read))
        {
            if (!nodes.TryGetValue(predicate.Capture, out var node)) continue;
            var text = tree.TextOf(node);
            bool holds = predicate.Operator switch
            {
                "eq?" => text == predicate.Values[0],
                "not-eq?" => text != predicate.Values[0],
                "any-of?" => predicate.Values.Contains(text),
                "match?" => Regex.IsMatch(text, predicate.Values[0]),
                _ => true,
            };
            if (!holds) return false;
        }
        return true;
    }

    Predicate[] Read(uint pattern)
    {
        var steps = TreeSitter.ts_query_predicates_for_pattern(Handle, pattern, out var count);
        var size = Marshal.SizeOf<TSQueryPredicateStep>();
        var result = new List<Predicate>();
        var current = new List<TSQueryPredicateStep>();
        for (int i = 0; i < count; i++)
        {
            var step = Marshal.PtrToStructure<TSQueryPredicateStep>(steps + i * size);
            if (step.Type != 0)
            {
                current.Add(step);
                continue;
            }
            string Text(uint id) => Marshal.PtrToStringUTF8(TreeSitter.ts_query_string_value_for_id(Handle, id, out var length), (int)length);
            if (current.Count >= 2 && current[0].Type == 2 && current[1].Type == 1)
                result.Add(new Predicate(Text(current[0].ValueId), current[1].ValueId, current.Skip(2).Where(s => s.Type == 2).Select(s => Text(s.ValueId)).ToArray()));
            current.Clear();
        }
        return [.. result];
    }
}
