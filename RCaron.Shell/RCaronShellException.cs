namespace RCaron.Shell;

public class RCaronShellException : Exception
{
    public int? ErrorCode { get; }

    public RCaronShellException(string message, int? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }

    public RCaronShellException(string message, Exception innerException, int? errorCode = null) : base(message,
        innerException)
    {
        ErrorCode = errorCode;
    }
}