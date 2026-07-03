// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wdl;

/// <summary>
/// Flattens a WDL "program": starting from a level's main script it resolves
/// <c>INCLUDE</c> directives (recursively, via a caller-supplied module resolver) and
/// evaluates the <c>IFDEF</c>/<c>IFNDEF</c>/<c>IFELSE</c>/<c>ENDIF</c>,
/// <c>DEFINE</c>, and <c>UNDEF</c> preprocessor, applies active <c>DEFINE</c>
/// aliases, and produces a single ordered list of active declaration items.
/// </summary>
/// <remarks>
/// Derived from the public Acknex 3 manual's preprocessor description plus observed
/// game behaviour (e.g. APATHY.WDL's resolution block defaults to defining
/// <c>HIRES</c>). See <c>PROVENANCE.md</c>.
/// </remarks>
public sealed class WdlPreprocessor
{
    /// <summary>Resolves a module name (e.g. <c>globals.wdl</c>) to its source text, or <c>null</c> if not found.</summary>
    public delegate string? ModuleResolver(string moduleName);

    private readonly ModuleResolver _resolve;
    private readonly HashSet<string> _symbols;
    private readonly Dictionary<string, IReadOnlyList<WdlToken>> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _included;
    private readonly List<WdlItem> _output = [];

    private WdlPreprocessor(ModuleResolver resolve, HashSet<string> symbols)
    {
        _resolve = resolve;
        _symbols = symbols;
        _included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Flattens <paramref name="mainSource"/> into its active declarations, inlining
    /// included modules and applying preprocessor conditionals.
    /// </summary>
    /// <param name="mainSource">The level's main WDL source.</param>
    /// <param name="resolveModule">Resolves an <c>INCLUDE</c>d module name to its source.</param>
    /// <param name="predefinedSymbols">Symbols already defined (e.g. a forced resolution); may be null.</param>
    public static WdlProgram Flatten(string mainSource, ModuleResolver resolveModule, IEnumerable<string>? predefinedSymbols = null)
    {
        ArgumentNullException.ThrowIfNull(mainSource);
        ArgumentNullException.ThrowIfNull(resolveModule);

        var symbols = new HashSet<string>(predefinedSymbols ?? [], StringComparer.OrdinalIgnoreCase);
        var preprocessor = new WdlPreprocessor(resolveModule, symbols);
        preprocessor.ProcessSource(mainSource);
        return new WdlProgram(preprocessor._output, symbols);
    }

    private void ProcessSource(string source) => ProcessItems(WdlParser.Parse(source).Items);

    private void ProcessItems(IReadOnlyList<WdlItem> items)
    {
        // A stack of conditional frames; the program emits while the top frame emits
        // (or the stack is empty).
        var frames = new List<Frame>();
        bool Active() => frames.Count == 0 || frames[^1].Emitting;

        foreach (WdlItem item in items)
        {
            switch (item.Keyword.ToUpperInvariant())
            {
                case "IFDEF":
                    StartConditional(item, isNegated: false);
                    break;

                case "IFNDEF":
                    StartConditional(item, isNegated: true);
                    break;

                case "IFELSE":
                    if (frames.Count > 0)
                    {
                        Frame top = frames[^1];
                        frames[^1] = top with { Emitting = top.ParentActive && !top.Matched };
                    }

                    break;

                case "ENDIF":
                    if (frames.Count > 0)
                    {
                        frames.RemoveAt(frames.Count - 1);
                    }

                    break;

                case "INCLUDE":
                    if (Active() && item.Header.Count > 0)
                    {
                        Include(item.Header[0].Text);
                    }

                    break;

                case "DEFINE":
                    if (Active())
                    {
                        if (item.Header.Count > 0)
                        {
                            string symbol = item.Header[0].Text;
                            _symbols.Add(symbol);
                            if (DefineReplacement(item.Header) is { } replacement)
                            {
                                _aliases[symbol] = replacement;
                            }
                        }

                        _output.Add(item); // keep — a DEFINE may also be a named constant
                    }

                    break;

                case "UNDEF":
                    if (Active() && item.Header.Count > 0)
                    {
                        string symbol = item.Header[0].Text;
                        _symbols.Remove(symbol);
                        _aliases.Remove(symbol);
                    }

                    break;

                default:
                    if (Active())
                    {
                        _output.Add(ApplyDefines(item));
                    }

                    break;
            }
        }

        void StartConditional(WdlItem item, bool isNegated)
        {
            bool parentActive = Active();
            string symbol = item.Header.Count > 0 ? item.Header[0].Text : string.Empty;
            bool isDefined = _symbols.Contains(symbol);
            bool matched = parentActive && (isNegated ? !isDefined : isDefined);
            frames.Add(new Frame(parentActive, matched, matched));
        }
    }

    private static List<WdlToken>? DefineReplacement(IReadOnlyList<WdlToken> header)
    {
        int comma = IndexOfKind(header, WdlTokenKind.Comma);
        if (comma < 0 || comma == header.Count - 1)
        {
            return null;
        }

        return Slice(header, comma + 1, header.Count);
    }

    private WdlItem ApplyDefines(WdlItem item)
    {
        string keyword = item.Keyword;
        if (!item.IsLabel &&
            _aliases.TryGetValue(item.Keyword, out IReadOnlyList<WdlToken>? keywordReplacement) &&
            keywordReplacement.Count == 1 &&
            keywordReplacement[0].Kind == WdlTokenKind.Identifier)
        {
            keyword = keywordReplacement[0].Text;
        }

        WdlBlock? body = item.Body is null ? null : new WdlBlock(ApplyDefines(item.Body.Items));
        return new WdlItem(keyword, ApplyDefines(item.Header), body, item.IsLabel);
    }

    private List<WdlItem> ApplyDefines(IReadOnlyList<WdlItem> items)
    {
        var result = new List<WdlItem>(items.Count);
        foreach (WdlItem item in items)
        {
            result.Add(ApplyDefines(item));
        }

        return result;
    }

    private List<WdlToken> ApplyDefines(IReadOnlyList<WdlToken> tokens)
    {
        var result = new List<WdlToken>(tokens.Count);
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier &&
                _aliases.TryGetValue(token.Text, out IReadOnlyList<WdlToken>? replacement))
            {
                foreach (WdlToken replacementToken in replacement)
                {
                    result.Add(new WdlToken(replacementToken.Kind, replacementToken.Text, token.Line, token.Column));
                }
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }

    private void Include(string moduleName)
    {
        // Include-once: also guards against cyclic includes.
        if (!_included.Add(moduleName))
        {
            return;
        }

        string? source = _resolve(moduleName);
        if (source is not null)
        {
            ProcessSource(source);
        }
    }

    private readonly record struct Frame(bool ParentActive, bool Matched, bool Emitting);

    private static int IndexOfKind(IReadOnlyList<WdlToken> tokens, WdlTokenKind kind)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<WdlToken> Slice(IReadOnlyList<WdlToken> tokens, int start, int end)
    {
        var slice = new List<WdlToken>(end - start);
        for (int i = start; i < end; i++)
        {
            slice.Add(tokens[i]);
        }

        return slice;
    }
}

/// <summary>The result of flattening a WDL program: its active declarations and the defined symbols.</summary>
public sealed class WdlProgram
{
    public WdlProgram(IReadOnlyList<WdlItem> items, IReadOnlySet<string> definedSymbols)
    {
        Items = items;
        DefinedSymbols = definedSymbols;
    }

    /// <summary>The flattened, in-order active declaration items (INCLUDEs inlined, conditionals applied).</summary>
    public IReadOnlyList<WdlItem> Items { get; }

    /// <summary>Symbols defined after preprocessing (predefined plus <c>DEFINE</c>s, minus active <c>UNDEF</c>s).</summary>
    public IReadOnlySet<string> DefinedSymbols { get; }
}
