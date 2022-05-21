namespace RCaron;

public class RCaronException : Exception
{
    public RCaronExceptionTime Time { get; }

    public RCaronException(string message, RCaronExceptionTime time = RCaronExceptionTime.Unknown) : base(message)
    {
        Time = time;
    }
}

public enum RCaronExceptionTime : byte
{
    Unknown,
    Runtime,
    Parsetime,
}