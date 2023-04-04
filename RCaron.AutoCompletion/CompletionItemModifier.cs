namespace RCaron.AutoCompletion;

[Flags]
public enum CompletionItemModifier
{
    None = 0,
    /// <summary>
    /// Only for built-in functions.
    /// </summary>
    BuiltIn = 1,
    Control = 2 << 1,
}