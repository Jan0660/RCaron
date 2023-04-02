namespace RCaron.Shell.Tests;

public class ExecutableRunTests
{
    [Theory]
    [InlineData("cmd.exe", "cmd.exe", new string[0])]
    [InlineData("cmd /c echo hello", "cmd", new[] { "/c", "echo", "hello" })]
    [InlineData("cmd /c 'echo hello'", "cmd", new[] { "/c", "echo hello" })]
    [InlineData("hello -fw 123 --something=1", "hello", new[] { "-fw", "123", "--something=1" })]
    [InlineData("hello -fw 123 (1 + 3)", "hello", new[] { "-fw", "123", "4" })]
    [InlineData("$h = 1; hello $h.ToString()", "hello", new[] { "1" })]
    [InlineData("./hello #string", "./hello", new[] { "#string" })]
    [InlineData("./hello (1 + 3)", "./hello", new[] { "4" })]
    [InlineData("./hello 1 + 3", "./hello", new[] { "1", "+", "3" })]
    [InlineData("$h = 1; hello $h", "hello", new[] { "1" })]
    [InlineData("cd ..", "cd", new[] { ".." })]
    public void ArgumentsAreParsedProperly(string code, string name, string[] args)
    {
        var motor = new Motor(RCaronRunner.Parse(code));
        var ran = false;
        motor.InvokeRunExecutable = (m, n, a, f, _, _) =>
        {
            ran = true;
            var startInfo = RunExecutable.ParseArgs(m, n, a, f.Raw);
            Assert.Equal(name, startInfo.FileName);
            Assert.Equal(args, startInfo.ArgumentList);
            return null;
        };
        motor.Run();
        Assert.True(ran, "RunExecutable was not invoked");
    }
}