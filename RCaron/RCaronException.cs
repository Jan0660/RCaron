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
}

public enum RCaronExceptionTime : byte
{
    Unknown,
    Runtime,
    Parsetime,
}