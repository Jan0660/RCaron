using RCaron.Classes;

namespace RCaron;

public class FileScope
{
    public List<ClassDefinition>? ClassDefinitions { get; set; }
    public Dictionary<string, Function>? Functions { get; set; }
    public List<string>? UsedNamespaces { get; set; }
    public Dictionary<string, Type>? TypeCache { get; set; }
    public List<string>? UsedNamespacesForExtensionMethods { get; set; }
    public List<IIndexerImplementation>? IndexerImplementations { get; set; }
    public List<IPropertyAccessor>? PropertyAccessors { get; set; }
}

public record Function(CodeBlockToken CodeBlock, FunctionArgument[]? Arguments, FileScope FileScope);

public class FunctionArgument
{
    public FunctionArgument(string Name)
    {
        this.Name = Name;
    }

    public string Name { get; }
    public object? DefaultValue { get; set; } = RCaronInsideEnum.NoDefaultValue;
}