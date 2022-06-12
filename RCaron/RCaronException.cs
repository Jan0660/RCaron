namespace RCaron;

public class RCaronException : Exception
{
    public RCaronExceptionTime Time { get; }

    public RCaronException(string message, RCaronExceptionTime time = RCaronExceptionTime.Unknown) : base(message)
    {
        Time = time;
    }
    
    public static RCaronException VariableNotFound(string name)
        => new($"variable '{name}' does not exist in this scope", RCaronExceptionTime.Runtime);
    
    public static RCaronException NullInTokens(in Span<PosToken> tokens, string raw, int index)
        => new(
                $"null resolved in '{tokens[index].ToString(raw)}'(index={index}) in '{raw[tokens[0].Position.Start..tokens[^1].Position.End]}'",
                RCaronExceptionTime.Runtime);
}

public enum RCaronExceptionTime : byte
{
    Unknown,
    Runtime,
    Parsetime,
}