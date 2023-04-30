using System.Text;

namespace RCaron.Shell;

public class PathResolver
{
    public static PathResolver Instance { get; } = new();
    public ISpecialFolderGetter SpecialFolderGetter { get; set; }
    public char DirectorySeparatorChar { get; set; } = Path.DirectorySeparatorChar;

    public PathResolver(ISpecialFolderGetter? specialFolderGetter = null)
    {
        SpecialFolderGetter = specialFolderGetter ?? new SpecialFolderGetter();
    }

    public string Resolve(string path, string? currentPath = null)
    {
        currentPath ??= Environment.CurrentDirectory;
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            return _doDotsResolving(
                Path.Combine(SpecialFolderGetter.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]));
        }
        else if (path == "~")
        {
            return SpecialFolderGetter.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (path.StartsWith('.'))
        {
            return _doDotsResolving(Path.Combine(currentPath, path));
        }
        else
        {
            return _doDotsResolving(path);
        }
    }

    /// <summary>
    /// /home/jan/... (-> /home/..) -> /
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private string _doDotsResolving(string path)
    {
        if (!path.Contains(".."))
            return path;
        // please try to use spans
        var parts = path.Split('/', '\\');
        var stack = new Stack<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (stack.Count == 0)
                    throw new RCaronShellException("Path is invalid.");
                stack.Pop();
            }
            else if (part == ".")
            {
                // do nothing
            }
            else if (part.StartsWith("..."))
            {
                var count = 0;
                foreach (var c in part)
                {
                    if (c == '.')
                        count++;
                    else
                    {
                        count = -1;
                        break;
                    }
                }

                if (count == -1)
                    stack.Push(part);
                else
                {
                    for (var i = 1; i < count; i++)
                    {
                        if (stack.Count == 0)
                            throw new RCaronShellException("Path is invalid.");
                        stack.Pop();
                    }
                }
            }
            else if (part == "")
            {
                // do nothing
            }
            else
            {
                stack.Push(part);
            }
        }

        var builder = new StringBuilder();
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            builder.Insert(0, item);
            if (!(stack.Count == 0/* && OperatingSystem.IsWindows()*/ && item is [_, ':']))
                builder.Insert(0, DirectorySeparatorChar);
        }

        if (builder.Length == 0)
            builder.Append(DirectorySeparatorChar);

        return builder.ToString();
    }
}

public interface ISpecialFolderGetter
{
    string GetFolderPath(Environment.SpecialFolder folder);
}

public class SpecialFolderGetter : ISpecialFolderGetter
{
    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}