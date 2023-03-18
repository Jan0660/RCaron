namespace RCaron.Tests;

public class PathTests
{
    public static readonly string[] GetPathTestTemplates =
    {
        "$h = {0};",
        "$h = string({0});",
        @"func f($h) {{ return $h; }} $h = f({0});",
        @"func f($h) {{ return $h; }} $h = f(h: {0});",
        @"func f($h, $opt = 2) {{ return $h; }} $h = f({0}, 3);",
    };

    public static IEnumerable<object> GetPathTestPathsAll =>
        GetPathTestPathsExecutable.Concat(GetPathTestPathsNotExecutable);

    public static readonly object[] GetPathTestPathsNotExecutable =
    {
        "/", @"\",
        ".", "..",
        "/dir/", @"\dir\",
        @"C:\", "C:/",
        ".dir/",
        "~", "~/dir/",
        "*", "*.*", "*.ext", "./file*.*", "/*",
    };

    public static readonly object[] GetPathTestPathsExecutable =
    {
        "/file", @"\file", "/file.ext",
        "/dir/file", @"\dir\file",
        new[] { "/file` spaced", "/file spaced" }, new[] { "/file`,escaped", "/file,escaped" },
        @"C:\file", "C:/file", @"C:\file.ext",
        "dir/", "file", "file.ext",
        "./file", "../file",
        ".file",
        "~/file",
    };

    public static IEnumerable<object[]> GetPathTestDataExecutable()
    {
        foreach (var path in GetPathTestPathsExecutable)
        {
            if (path is string)
                yield return new[] { path, path };
            else if (path is string[] arr)
                yield return new object[] { arr[0], arr[1] };
            else
                throw new();
        }
    }

    public static IEnumerable<object[]> GetPathTestDataWithTemplates()
    {
        foreach (var template in GetPathTestTemplates)
        {
            foreach (var path in GetPathTestPathsExecutable)
            {
                if (path is string)
                    yield return new[] { template, path, path };
                else if (path is string[] arr)
                    yield return new object[] { template, arr[0], arr[1] };
                else
                    throw new();
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetPathTestDataWithTemplates))]
    public void PathInUsage(string template, string path, string expected)
    {
        var m = RCaronRunner.Run(string.Format(template, path));
        m.AssertVariableEquals("h", expected);
    }

    [Theory]
    [MemberData(nameof(GetPathTestDataExecutable))]
    public void PathAsLine(string path, string expected)
    {
        var parsed = RCaronRunner.Parse(path);
        var tokenLine = Assert.IsType<TokenLine>(parsed.FileScope.Lines[0]);
        Assert.Single(tokenLine.Tokens);
        Assert.Equal(expected, tokenLine.Tokens[0] switch
        {
            ConstToken constToken => constToken.Value,
            KeywordToken keywordToken => keywordToken.String,
            _ => tokenLine.Tokens[0].ToString(),
        });
    }
}