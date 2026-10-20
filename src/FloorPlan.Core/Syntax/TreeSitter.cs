using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace FloorPlan.Core;

// The tree-sitter runtime and grammars, compiled into tree-sitter.dll by native\build.ps1. FloorPlan
// runs inside folders it does not trust, so the library loads only from FloorPlan's own folders and
// never through the system's search path.

[StructLayout(LayoutKind.Sequential)]
struct TSNode
{
    public uint Context0, Context1, Context2, Context3;
    public IntPtr Id;
    public IntPtr Tree;
}

[StructLayout(LayoutKind.Sequential)]
struct TSPoint
{
    public uint Row, Column;
}

[StructLayout(LayoutKind.Sequential)]
struct TSTreeCursor
{
    public IntPtr Tree, Id;
    public uint Context0, Context1, Context2;
}

[StructLayout(LayoutKind.Sequential)]
struct TSQueryCapture
{
    public TSNode Node;
    public uint Index;
}

[StructLayout(LayoutKind.Sequential)]
struct TSQueryMatch
{
    public uint Id;
    public ushort PatternIndex;
    public ushort CaptureCount;
    public IntPtr Captures;
}

[StructLayout(LayoutKind.Sequential)]
struct TSQueryPredicateStep
{
    /// <summary>0 ends a predicate, 1 is a capture, 2 a string.</summary>
    public int Type;
    public uint ValueId;
}

static class TreeSitter
{
    const string Library = "tree-sitter";
    static IntPtr handle;
    static readonly Lazy<bool> loaded = new(Load);
    static readonly ConcurrentDictionary<IntPtr, string> types = new();

    static TreeSitter()
    {
        NativeLibrary.SetDllImportResolver(typeof(TreeSitter).Assembly, (name, _, _) =>
            name != Library ? IntPtr.Zero
            : loaded.Value ? handle
            : throw new DllNotFoundException("This FloorPlan was built without its parsers"));
    }

    /// <summary>True when this build has its parsers.</summary>
    public static bool Available => loaded.Value;

    // Beside the assemblies, or where a single-file executable unpacks its native libraries.
    static bool Load()
    {
        var folders = new List<string> { AppContext.BaseDirectory };
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string search)
            folders.AddRange(search.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var folder in folders.Where(Path.IsPathFullyQualified))
        {
            var file = Path.Combine(folder, Library + ".dll");
            if (File.Exists(file) && NativeLibrary.TryLoad(file, out handle)) return true;
        }
        return false;
    }

    /// <summary>A node's type, interned: the grammar's names are static strings.</summary>
    public static string Type(TSNode node)
    {
        var name = ts_node_type(node);
        return types.GetOrAdd(name, p => Marshal.PtrToStringUTF8(p) ?? "");
    }

    public static TSNode Field(TSNode node, string field)
    {
        var bytes = Encoding.UTF8.GetBytes(field);
        return ts_node_child_by_field_name(node, bytes, (uint)bytes.Length);
    }

    public static int StartLine(TSNode node) => (int)ts_node_start_point(node).Row + 1;

    /// <summary>The last line a node covers; a node that ends at the start of a line ends on the line before.</summary>
    public static int EndLine(TSNode node)
    {
        var (start, end) = (ts_node_start_point(node), ts_node_end_point(node));
        return end.Column == 0 && end.Row > start.Row ? (int)end.Row : (int)end.Row + 1;
    }

    const CallingConvention Cdecl = CallingConvention.Cdecl;

    [DllImport(Library, CallingConvention = Cdecl)] public static extern uint ts_language_abi_version(IntPtr language);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_parser_new();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_parser_delete(IntPtr parser);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_parser_set_language(IntPtr parser, IntPtr language);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_parser_parse_string_encoding(IntPtr parser, IntPtr oldTree, byte[] text, uint length, int encoding);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_tree_delete(IntPtr tree);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSNode ts_tree_root_node(IntPtr tree);

    [DllImport(Library, CallingConvention = Cdecl)] static extern IntPtr ts_node_type(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern uint ts_node_start_byte(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern uint ts_node_end_byte(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSPoint ts_node_start_point(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSPoint ts_node_end_point(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_node_is_null(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_node_is_named(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_node_is_error(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_node_is_missing(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_node_has_error(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSNode ts_node_parent(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern uint ts_node_child_count(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSNode ts_node_child(TSNode node, uint index);
    [DllImport(Library, CallingConvention = Cdecl)] static extern TSNode ts_node_child_by_field_name(TSNode node, byte[] name, uint length);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSNode ts_node_prev_named_sibling(TSNode node);

    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSTreeCursor ts_tree_cursor_new(TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_tree_cursor_delete(ref TSTreeCursor cursor);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern TSNode ts_tree_cursor_current_node(ref TSTreeCursor cursor);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_tree_cursor_goto_first_child(ref TSTreeCursor cursor);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_tree_cursor_goto_next_sibling(ref TSTreeCursor cursor);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_tree_cursor_goto_parent(ref TSTreeCursor cursor);

    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_query_new(IntPtr language, byte[] source, uint length, out uint errorOffset, out int errorType);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_query_delete(IntPtr query);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_query_capture_name_for_id(IntPtr query, uint index, out uint length);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_query_string_value_for_id(IntPtr query, uint index, out uint length);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_query_predicates_for_pattern(IntPtr query, uint pattern, out uint stepCount);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr ts_query_cursor_new();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_query_cursor_delete(IntPtr cursor);
    [DllImport(Library, CallingConvention = Cdecl)] public static extern void ts_query_cursor_exec(IntPtr cursor, IntPtr query, TSNode node);
    [DllImport(Library, CallingConvention = Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool ts_query_cursor_next_match(IntPtr cursor, out TSQueryMatch match);

    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_gdscript();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_gdshader();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_python();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_javascript();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_typescript();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_tsx();
    [DllImport(Library, CallingConvention = Cdecl)] public static extern IntPtr tree_sitter_c_sharp();
}

/// <summary>A parsed file. Trees are native memory: dispose of them.</summary>
sealed class SyntaxTree : IDisposable
{
    IntPtr tree;

    SyntaxTree(IntPtr tree, byte[] text) => (this.tree, Text) = (tree, text);

    /// <summary>The UTF-8 text the tree was parsed from.</summary>
    public byte[] Text { get; }

    public TSNode Root => TreeSitter.ts_tree_root_node(tree);

    public static SyntaxTree Parse(Grammar grammar, byte[] text)
    {
        var parser = TreeSitter.ts_parser_new();
        try
        {
            if (!TreeSitter.ts_parser_set_language(parser, grammar.Language))
                throw new InvalidOperationException($"The {grammar.Name} grammar's version {TreeSitter.ts_language_abi_version(grammar.Language)} does not match the parser's");
            var tree = TreeSitter.ts_parser_parse_string_encoding(parser, IntPtr.Zero, text, (uint)text.Length, 0);
            if (tree == IntPtr.Zero) throw new InvalidOperationException($"The {grammar.Name} parser gave no tree");
            return new SyntaxTree(tree, text);
        }
        finally
        {
            TreeSitter.ts_parser_delete(parser);
        }
    }

    public string TextOf(TSNode node)
    {
        int from = (int)TreeSitter.ts_node_start_byte(node), to = (int)TreeSitter.ts_node_end_byte(node);
        return Encoding.UTF8.GetString(Text, from, Math.Max(0, to - from));
    }

    /// <summary>True when only spaces and tabs come before the node on its line.</summary>
    public bool StartsLine(TSNode node)
    {
        for (int i = (int)TreeSitter.ts_node_start_byte(node) - 1; i >= 0 && Text[i] != '\n'; i--)
            if (Text[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r')) return false;
        return true;
    }

    /// <summary>Visits nodes in document order; returning false from the visit skips a node's children.</summary>
    public void Walk(Func<TSNode, bool> visit)
    {
        var cursor = TreeSitter.ts_tree_cursor_new(Root);
        try
        {
            while (true)
            {
                if (visit(TreeSitter.ts_tree_cursor_current_node(ref cursor)) && TreeSitter.ts_tree_cursor_goto_first_child(ref cursor)) continue;
                while (!TreeSitter.ts_tree_cursor_goto_next_sibling(ref cursor))
                    if (!TreeSitter.ts_tree_cursor_goto_parent(ref cursor)) return;
            }
        }
        finally
        {
            TreeSitter.ts_tree_cursor_delete(ref cursor);
        }
    }

    /// <summary>Places the grammar could not read: error and missing nodes.</summary>
    public int Errors()
    {
        if (!TreeSitter.ts_node_has_error(Root)) return 0;
        int count = 0;
        Walk(node =>
        {
            if (TreeSitter.ts_node_is_error(node) || TreeSitter.ts_node_is_missing(node)) count++;
            return TreeSitter.ts_node_has_error(node);
        });
        return count;
    }

    public void Dispose()
    {
        if (tree != IntPtr.Zero) TreeSitter.ts_tree_delete(tree);
        tree = IntPtr.Zero;
    }
}
