namespace RCaron;

public interface IRCaronModule
{
    public object? RCaronModuleRun(ReadOnlySpan<char> name, Motor motor, in ArraySegment<PosToken> arguments, CallLikePosToken? callToken);
}