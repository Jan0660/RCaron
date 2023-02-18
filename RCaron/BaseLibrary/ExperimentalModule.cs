using RCaron.LibrarySourceGenerator;

namespace RCaron.BaseLibrary;

[Module("ExperimentalModule")]
public partial class ExperimentalModule : IRCaronModule
{
    [Method("Open-File")]
    public void OpenFile(Motor motor, string path, object[]? functions = null, object[]? classes = null,
        bool noRun = false)
    {
        OpenFromString(motor, File.ReadAllText(path), Path.GetFullPath(path), functions, classes, noRun);
    }

    [Method("Open-FromString")]
    public void OpenFromString(Motor motor, string code, string? fileName = null, object[]? functions = null,
        object[]? classes = null,
        bool noRun = false)
    {
        var fileScope = motor.GetFileScope();
        var p = RCaronRunner.Parse(code);
        p.FileScope.FileName = fileName;
        if (!noRun)
        {
            var s = new Motor.StackThing(false, true, null, p.FileScope);
            motor.BlockStack.Push(s);
            motor.RunLinesList(p.FileScope.Lines);
            if (motor.BlockStack.Peek() == s)
                motor.BlockStack.Pop();
        }

        if (functions == null && classes == null)
        {
            fileScope.ImportedFileScopes ??= new();
            fileScope.ImportedFileScopes.Add(p.FileScope);
        }

        if (functions != null)
        {
            fileScope.ImportedFunctions ??= new(StringComparer.InvariantCultureIgnoreCase);
            foreach (var function in functions)
            {
                var functionName = function.ToString()!;
                if (!(p.FileScope.Functions?.TryGetValue(functionName, out var f) ?? false))
                    throw RCaronException.FunctionToImportNotFound(functionName);
                fileScope.ImportedFunctions.Add(functionName, f);
            }
        }

        if (classes != null)
        {
            fileScope.ImportedClassDefinitions ??= new();
            foreach (var @class in classes)
            {
                var className = @class.ToString()!;
                if (Motor.TryGetClassDefinition(p.FileScope.ClassDefinitions, className, out var classDef))
                    fileScope.ImportedClassDefinitions.Add(classDef);
                else
                    throw RCaronException.ClassToImportNotFound(className);
            }
        }
    }
}