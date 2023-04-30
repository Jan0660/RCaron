namespace RCaron.Shell.Tests;

public class ResolvePathTests
{
    public class TestSpecialFolderGetter : ISpecialFolderGetter
    {
        public string GetFolderPath(Environment.SpecialFolder folder)
            => folder switch
            {
                Environment.SpecialFolder.UserProfile => "/home/jan",
                _ => throw new(),
            };
    }

    [Theory]
    [InlineData("../test.txt", "/home/jan", "/home/test.txt")]
    [InlineData(".../test.txt", "/home/jan", "/test.txt")]
    [InlineData("..../test.txt", "/home/jan/dir", "/test.txt")]
    [InlineData("...", "/home/jan", "/")]
    [InlineData("....", "/home/jan/dir", "/")]
    [InlineData("~", "/home/jan", "/home/jan")]
    // [InlineData("dir/...", "/home/jan", "/home/")]
    [InlineData("~/test.txt", "/home/jan", "/home/jan/test.txt")]
    public void Resolve(string path, string currentPath, string expected)
    {
        var resolved = new PathResolver(new TestSpecialFolderGetter()).Resolve(path, currentPath);
        Assert.Equal(expected, resolved.Replace("\\", "/"));
    }
}