using JetBrains.Annotations;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace RCaron.Shell.Prompt;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LocalRegistryOptions : IRegistryOptions
{
    public Dictionary<string, string> GrammarPaths { get; } = new();
    public Dictionary<string, string> ThemePaths { get; } = new();
    public required string DefaultThemePath { get; set; }

    public ICollection<string> GetInjections(string scopeName)
    {
        return null!;
    }

    public IRawGrammar GetGrammar(string scopeName)
        => GrammarPaths.TryGetValue(scopeName, out var path) ? GetRawGrammar(path) : null!;

    public IRawTheme GetTheme(string scopeName)
        => ThemePaths.TryGetValue(scopeName, out var path) ? GetRawTheme(path) : null!;

    public IRawTheme GetDefaultTheme()
        => GetRawTheme(DefaultThemePath);

    public IRawTheme GetRawTheme(string themePath)
    {
        using var reader = new StreamReader(themePath);
        return ThemeReader.ReadThemeSync(reader);
    }

    public IRawGrammar GetRawGrammar(string grammarPath)
    {
        using var reader = new StreamReader(grammarPath);
        return GrammarReader.ReadGrammarSync(reader);
    }
}