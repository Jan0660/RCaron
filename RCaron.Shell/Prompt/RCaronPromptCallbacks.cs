using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;
using RCaron.AutoCompletion;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace RCaron.Shell.Prompt;

public class RCaronPromptCallbacks : PromptCallbacks
{
    public AnsiColor[] PairColors { get; set; } =
    {
        AnsiColor.Yellow,
        AnsiColor.Magenta,
        AnsiColor.Cyan,
        AnsiColor.BrightGreen,
    };

    public ConsoleFormat PairErrorFormat { get; set; } = new(AnsiColor.BrightRed, Underline: true);
    public int NestedPairLimit { get; set; } = 10;
    public bool ColorizeBracketsOutOfLimit { get; set; } = false;
    public bool HighlightBracketPairs { get; set; } = true;

    private IGrammar? Grammar { get; set; }
    private Registry? Registry { get; set; }

    [MemberNotNullWhen(true, nameof(Grammar), nameof(Registry))]
    public bool UseTextMateHighlighting { get; private set; }

    public int MaxCompletions { get; set; } = 40;
    public bool ThrowOnCompletionError { get; set; } = false;
    public bool EnableAutoCompletions { get; set; } = true;
    public bool ColorizeCompletions { get; set; } = false;
    public AnsiColor DefaultTextColor { get; set; } = AnsiColor.White;
    public Motor? ForMotor { get; set; }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text,
        CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();
        if (UseTextMateHighlighting)
        {
            var result = Grammar.TokenizeLine(text);
            var theme = Registry.GetTheme();
            foreach (var token in result.Tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var themeRules = theme.Match(token.Scopes);
                var preferred = themeRules.FirstOrDefault();
                if (preferred == null)
                    continue;
                var color = theme.GetColor(preferred.foreground);
                if (!color.StartsWith("#"))
                    continue;
                var real = ColorTranslator.FromHtml(color);
                var span = new FormatSpan(token.StartIndex, token.EndIndex - token.StartIndex,
                    AnsiColor.Rgb(real.R, real.G, real.B));
                spans.Add(span);
            }
        }

        if (HighlightBracketPairs)
        {
            var inString = false;
            var inSingleLineComment = false;
            var inMultiLineComment = false;
            short roundDepth = -1;
            short squareDepth = -1;
            short curlyDepth = -1;
            StackStack<int> roundStack = new(stackalloc int[NestedPairLimit]);
            StackStack<int> squareStack = new(stackalloc int[NestedPairLimit]);
            StackStack<int> curlyStack = new(stackalloc int[NestedPairLimit]);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\'')
                    inString = !inString;
                else if (text[i] == '\\' && i < text.Length - 1)
                    i++;
                else if (!inString)
                {
                    if (inSingleLineComment && text[i] == '\n')
                        inSingleLineComment = false;
                    else if (inSingleLineComment)
                        continue;
                    // make sure we skip comments
                    if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '/')
                        inSingleLineComment = true;
                    else if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '#')
                        inMultiLineComment = true;
                    else if (i < text.Length - 1 && text[i] == '#' && text[i + 1] == '/')
                        inMultiLineComment = false;
                    if (inMultiLineComment)
                        continue;

                    if (text[i] == '(')
                        AddPairSpan(false, i, ref roundDepth, ref roundStack, spans);
                    else if (text[i] == ')')
                        AddPairSpan(true, i, ref roundDepth, ref roundStack, spans);
                    else if (text[i] == '[')
                        AddPairSpan(false, i, ref squareDepth, ref squareStack, spans);
                    else if (text[i] == ']')
                        AddPairSpan(true, i, ref squareDepth, ref squareStack, spans);
                    else if (text[i] == '{')
                        AddPairSpan(false, i, ref curlyDepth, ref curlyStack, spans);
                    else if (text[i] == '}')
                        AddPairSpan(true, i, ref curlyDepth, ref curlyStack, spans);
                }
            }

            AddPairErrors(ref roundStack, spans);
            AddPairErrors(ref squareStack, spans);
            AddPairErrors(ref curlyStack, spans);
        }

        return Task.FromResult((IReadOnlyCollection<FormatSpan>)spans.AsReadOnly());
    }

    void AddPairSpan(bool closing, int pos, ref short depth, ref StackStack<int> stack, List<FormatSpan> spans)
    {
        if (!closing)
        {
            depth++;
            if (depth >= 0 && !(stack.IsFull))
                stack.Push(pos);
        }

        if (ColorizeBracketsOutOfLimit && stack.IsFull && depth >= 0)
            spans.Add(new FormatSpan(pos, 1, PairColors[depth % PairColors.Length]));
        if (depth < 0)
            spans.Add(new FormatSpan(pos, 1, PairErrorFormat));
        if (closing)
        {
            if (depth >= 0 && !stack.IsEmpty)
            {
                if ((ColorizeBracketsOutOfLimit && stack.IsFull) || !stack.IsFull)
                    spans.Add(new FormatSpan(pos, 1, PairColors[depth % PairColors.Length]));
                if (!(depth > stack.Count - 1))
                    spans.Add(new FormatSpan(stack.Pop(), 1, PairColors[depth % PairColors.Length]));
            }

            depth--;
        }
    }

    void AddPairErrors(ref StackStack<int> stack, List<FormatSpan> spans)
    {
        while (!stack.IsEmpty)
        {
            var pos = stack.Pop();
            spans.Add(new FormatSpan(pos, 1, PairErrorFormat));
        }
    }

    protected override Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress,
        CancellationToken cancellationToken)
        => Task.FromResult(EnableAutoCompletions && (!char.IsWhiteSpace(text[caret - 1])));

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(string text, int caret,
        CancellationToken cancellationToken)
    {
        if (caret == 0)
            return Task.FromResult(TextSpan.FromBounds(0, 0));
        var start = caret - 1;
        int? firstDot = null;
        var isType = false;
        while (start >= 0)
        {
            var c = text[start];
            if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}')
                break;
            if (c == '.')
                firstDot ??= start + 1;

            start--;

            if (c == '$')
                break;
            if (c == '#')
            {
                isType = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        start += 1;

        if (!isType)
            start = firstDot ?? start;

        return Task.FromResult(TextSpan.FromBounds(start, caret));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret,
        TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        if (!EnableAutoCompletions)
            return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());
        try
        {
            var items = new List<CompletionItem>();
            var h = new CompletionProvider().GetCompletions(text, caret, MaxCompletions, ForMotor?.GlobalScope,
                ForMotor?.MainFileScope.Modules);
            foreach (var item in h)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FormattedString displayText;
                if (item.Thing.Deprecated)
                    displayText = new FormattedString(item.Thing.Word,
                        new FormatSpan(0, item.Thing.Word.Length, AnsiColor.Red));
                else
                {
                    var color = ColorizeCompletions
                        ? item.Thing.Kind switch
                        {
                            CompletionItemKind.Constant => _getColorForScope("constant.language.rcaron"),
                            CompletionItemKind.Variable => _getColorForScope("variable"),
                            CompletionItemKind.Function => _getColorForScope("support.function.rcaron"),
                            CompletionItemKind.Method when item.Thing.Modifier.HasFlag(CompletionItemModifier.BuiltIn)
                                => _getColorForScope("support.function.builtin.rcaron"),
                            CompletionItemKind.Method => _getColorForScope("support.function.rcaron"),
                            CompletionItemKind.Keyword when item.Thing.Modifier.HasFlag(CompletionItemModifier.Control)
                                => _getColorForScope("keyword.control.rcaron"),
                            CompletionItemKind.Keyword => _getColorForScope("keyword.rcaron"),
                            _ => DefaultTextColor,
                        }
                        : DefaultTextColor;
                    displayText = new FormattedString(item.Thing.Word,
                        new FormatSpan(0, item.Thing.Word.Length, color));
                }

                items.Add(new CompletionItem(item.Thing.Word, displayText: displayText,
                    getExtendedDescription: _ =>
                        Task.FromResult(item.Thing.Detail != null
                            ? new FormattedString(item.Thing.Detail + "\n" + item.Thing.Documentation,
                                new FormatSpan(0, item.Thing.Detail.Length, AnsiColor.BrightBlack))
                            : new FormattedString(item.Thing.Documentation))));
            }

            return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
        }
        catch
        {
            if (ThrowOnCompletionError)
                throw;
            return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());
        }
    }

    private AnsiColor _getColorForScope(string scope)
    {
        Debug.Assert(Registry != null, nameof(Registry) + " != null");
        var theme = Registry.GetTheme();
        foreach (var match in theme.Match(new[] { scope }))
        {
            var color = theme.GetColor(match.foreground);
            if (color != null && color.StartsWith('#'))
            {
                var real = ColorTranslator.FromHtml(color);
                return AnsiColor.Rgb(real.R, real.G, real.B);
            }
        }

        return DefaultTextColor;
    }

    public void UseTextMate(LocalRegistryOptions registryOptions)
    {
        Registry = new Registry(registryOptions);
        Grammar = Registry.LoadGrammar("source.rcaron");
        UseTextMateHighlighting = true;
    }

    public void DisableTextMate()
    {
        Registry = null;
        Grammar = null;
        UseTextMateHighlighting = false;
    }

    [Obsolete("Already using fork of PrettyPrompt with settable MaxCompletionItemsCount")]
    public void SetProportionOfWindowHeightForCompletionPane(PrettyPrompt.Prompt prompt, double value)
    {
        var configuration = (PromptConfiguration)
            typeof(PrettyPrompt.Prompt).GetField("configuration", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(prompt)!;
        typeof(PromptConfiguration).GetField("<ProportionOfWindowHeightForCompletionPane>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(configuration, value);
    }

    [Obsolete("Already using fork of PrettyPrompt with settable MaxCompletionItemsCount")]
    public void SetMaxCompletionItemsCount(PrettyPrompt.Prompt prompt, int value)
    {
        var configuration = (PromptConfiguration)
            typeof(PrettyPrompt.Prompt).GetField("configuration", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(prompt)!;
        typeof(PromptConfiguration).GetField("<MaxCompletionItemsCount>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(configuration, value);
        MaxCompletions = value;
    }
}