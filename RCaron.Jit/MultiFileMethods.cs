using System.Linq.Expressions;
using RCaron.LibrarySourceGenerator;

namespace RCaron.Jit;

[Module("JIT Multi-File overrides", ImplementModuleRun = false)]
public partial class MultiFileMethods
{
    [Method("Open-File")]
    public void OpenFile(CompiledContext compiledContext, string path, object[]? functions = null, object[]? classes = null,
        bool noRun = false)
    {
        OpenFromString(compiledContext, File.ReadAllText(path), Path.GetFullPath(path), functions, classes, noRun);
    }
    
    [Method("Open-FromString")]
    public void OpenFromString(CompiledContext compiledContext, string code, string? fileName = null, object[]? functions = null, object[]? classes = null,
        bool noRun = false)
    {
        var p = RCaronRunner.Parse(code);
        p.FileScope.FileName = fileName;
        var compiled = Compiler.Compile(p);
        if (!noRun)
        {
            var lambda = Expression.Lambda(compiled.blockExpression);
            // todo: add option to not compile and use light compile
            lambda.Compile().DynamicInvoke();
        }
        
        if (functions == null && classes == null)
        {
            compiledContext.FileScope.ImportedFileScopes ??= new();
            compiledContext.FileScope.ImportedFileScopes.Add(p.FileScope);
            compiledContext.ImportedContexts ??= new();
            compiledContext.ImportedContexts.Add(compiled.compiledContext);
        }
        
        if (functions != null)
        {
            compiledContext.FileScope.ImportedFunctions ??= new(StringComparer.InvariantCultureIgnoreCase);
            compiledContext.ImportedFunctions ??= new(StringComparer.InvariantCultureIgnoreCase);
            foreach (var function in functions)
            {
                var functionName = function.ToString()!;
                if(!(p.FileScope.Functions?.TryGetValue(functionName, out var f) ?? false))
                    throw RCaronException.FunctionToImportNotFound(functionName);
                compiledContext.FileScope.ImportedFunctions.Add(functionName, f);
                compiledContext.ImportedFunctions.Add(functionName, compiled.compiledContext.Functions[functionName]);
            }
        }

        if (classes != null)
        {
            compiledContext.FileScope.ImportedClassDefinitions ??= new();
            compiledContext.ImportedClasses ??= new();
            foreach (var @class in classes)
            {
                var className = @class.ToString()!;
                if (Motor.TryGetClassDefinition(p.FileScope.ClassDefinitions, className, out var classDef))
                {
                    compiledContext.FileScope.ImportedClassDefinitions.Add(classDef);
                    compiledContext.ImportedClasses.Add(compiled.compiledContext.GetClass(classDef)!);
                }
                else
                    throw RCaronException.ClassToImportNotFound(className);
            }
        }
    }
}