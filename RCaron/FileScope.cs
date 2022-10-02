namespace RCaron;

public class FileScope
{
    public List<string>? UsedNamespaces { get; set; }
    public Dictionary<string, Type>? TypeCache { get; set; }
    public List<string>? UsedNamespacesForExtensionMethods { get; set; }
    public List<IIndexerImplementation>? IndexerImplementations { get; set; }
}