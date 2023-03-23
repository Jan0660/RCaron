using System.Diagnostics.CodeAnalysis;
using System.Drawing;
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
            var h = new CompletionProvider().GetCompletions(text, caret, MaxCompletions);
            foreach (var item in h)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var displayText = item.Thing.Deprecated
                    ? new FormattedString(item.Thing.Word, new FormatSpan(0, item.Thing.Word.Length, AnsiColor.Red))
                    : default;
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
}