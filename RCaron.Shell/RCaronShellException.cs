namespace RCaron.Shell;

public class RCaronShellException : Exception
{
    public RCaronShellException(string message) : base(message)
    {
    }

    public RCaronShellException(string message, Exception innerException) : base(message, innerException)
    {
    }
}