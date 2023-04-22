using RCaron.Parsing;

namespace RCaron.Shell.Tests;

public class NativePipelineTests
{
    [Theory]
    [InlineData("1 | 2;", 1, 1)]
    [InlineData("ls | grep 'h';", 1, 2)]
    [InlineData("1 | 2", 1, 1)]
    [InlineData("ls | grep 'h'", 1, 2)]
    public void Simple(string code, int leftCount, int rightCount)
    {
        var parsed = RCaronParser.Parse(code);
        var line = Assert.IsType<SingleTokenLine>(parsed.FileScope.Lines[0]);
        var pipeline = Assert.IsType<NativePipelineValuePosToken>(line.Token);
        Assert.Equal(leftCount, pipeline.Left.Length);
        Assert.Equal(rightCount, pipeline.Right.Length);
    }
}