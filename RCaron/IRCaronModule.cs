namespace RCaron;

public interface IRCaronModule
{
    public object? RCaronModuleRun(string name, Motor motor, in ReadOnlySpan<PosToken> arguments);
}