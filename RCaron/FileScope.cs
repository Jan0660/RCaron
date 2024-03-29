using RCaron.Classes;

namespace RCaron;

public class FileScope
{
    public string? FileName { get; set; }
    public required string Raw { get; set; }
    public required IList<Line> Lines { get; set; }
    public List<IRCaronModule>? Modules { get; set; }
    public List<ClassDefinition>? ClassDefinitions { get; set; }
    public Dictionary<string, Function>? Functions { get; set; }
    public List<string>? UsedNamespaces { get; set; }
    public Dictionary<string, Type>? TypeCache { get; set; }
    public List<string>? UsedNamespacesForExtensionMethods { get; set; }
    public List<IIndexerImplementation>? IndexerImplementations { get; set; }
    public List<IPropertyAccessor>? PropertyAccessors { get; set; }
    public List<ClassDefinition>? ImportedClassDefinitions { get; set; }
    public Dictionary<string, Function>? ImportedFunctions { get; set; }
    public List<FileScope>? ImportedFileScopes { get; set; }
}

public record Function(CodeBlockToken CodeBlock, FunctionArgument[]? Arguments, FileScope FileScope);

public record FunctionArgument(string Name)
{
    public object? DefaultValue { get; set; } = RCaronInsideEnum.NoDefaultValue;
}