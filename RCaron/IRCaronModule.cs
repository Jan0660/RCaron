namespace RCaron;

public interface IRCaronModule
{
    public object? RCaronModuleRun(ReadOnlySpan<char> name, Motor motor, in ReadOnlySpan<PosToken> arguments, CallLikePosToken callToken);
}