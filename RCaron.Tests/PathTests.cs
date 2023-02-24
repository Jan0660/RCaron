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

    public static readonly object[] GetPathTestPaths =
    {
        ".", "..",
        "/", @"\", "/file", @"\file",
        "/dir/", @"\dir\", "/dir/file", @"\dir\file",
        new[] { "/file` spaced", "/file spaced" }, new[] { "/file`,escaped", "/file,escaped" },
        @"C:\", "C:/", @"C:\file", "C:/file",
    };

    public static IEnumerable<object[]> GetPathTestData()
    {
        foreach (var template in GetPathTestTemplates)
        {
            foreach (var path in GetPathTestPaths)
            {
                if (path is string)
                    yield return new object[] { template, path, path };
                else if (path is string[] arr)
                    yield return new object[] { template, arr[0], arr[1] };
                else
                    throw new();
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetPathTestData))]
    public void PathTest(string template, string path, string expected)
    {
        var m = RCaronRunner.Run(string.Format(template, path));
        m.AssertVariableEquals("h", expected);
    }
}