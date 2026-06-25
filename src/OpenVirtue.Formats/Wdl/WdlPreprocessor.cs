// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wdl;

/// <summary>
/// Flattens a WDL "program": starting from a level's main script it resolves
/// <c>INCLUDE</c> directives (recursively, via a caller-supplied module resolver) and
/// evaluates the <c>IFDEF</c>/<c>IFELSE</c>/<c>ENDIF</c> and <c>DEFINE</c> preprocessor,
/// producing a single ordered list of active declaration items.
/// </summary>
/// <remarks>
/// Derived from the observed preprocessor behaviour (e.g. APATHY.WDL's nested
/// resolution block that defaults to defining <c>HIRES</c>). See <c>PROVENANCE.md</c>.
/// </remarks>
public sealed class WdlPreprocessor
{
    /// <summary>Resolves a module name (e.g. <c>globals.wdl</c>) to its source text, or <c>null</c> if not found.</summary>
    public delegate string? ModuleResolver(string moduleName);

    private readonly ModuleResolver _resolve;
    private readonly HashSet<string> _symbols;
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
                    bool parentActive = Active();
                    string symbol = item.Header.Count > 0 ? item.Header[0].Text : string.Empty;
                    bool matched = parentActive && _symbols.Contains(symbol);
                    frames.Add(new Frame(parentActive, matched, matched));
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
                            _symbols.Add(item.Header[0].Text);
                        }

                        _output.Add(item); // keep — a DEFINE may also be a named constant
                    }

                    break;

                default:
                    if (Active())
                    {
                        _output.Add(item);
                    }

                    break;
            }
        }
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

    /// <summary>Symbols defined during preprocessing (predefined plus any <c>DEFINE</c>d).</summary>
    public IReadOnlySet<string> DefinedSymbols { get; }
}
