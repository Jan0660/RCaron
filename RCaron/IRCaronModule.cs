namespace RCaron;

public interface IRCaronModule
{
    public string Name { get; }
    public object? RCaronModuleRun(ReadOnlySpan<char> name, Motor motor, in ArraySegment<PosToken> arguments,
        CallLikePosToken? callToken, Pipeline? pipeline, bool isLeftOfPipeline);
}