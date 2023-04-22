using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace RCaron.LibrarySourceGenerator
{
    /// <summary>
    /// Created on demand before each generation pass
    /// </summary>
    class SyntaxReceiver : ISyntaxContextReceiver
    {
        public List<ITypeSymbol> Classes { get; } = new();

        /// <summary>
        /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
        /// </summary>
        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            // any field with at least one attribute is a candidate for property generation
            if (context.Node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } classDeclarationSyntax)
            {
                var g = (ITypeSymbol)context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax)!;
                foreach (var att in g.GetAttributes())
                {
                    if (att.AttributeClass?.ToDisplayString() == "RCaron.LibrarySourceGenerator.ModuleAttribute")
                    {
                        Classes.Add(g);
                    }
                }
            }
        }
    }

    [Generator]
    public class ModuleSourceGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
                return;
            foreach (var classSymbol in receiver.Classes)
            {
                var source = new StringBuilder();
                source.AppendLine($@"
using RCaron;
using System.Linq;
#nullable enable
#nullable disable warnings
#pragma warning disable

namespace {classSymbol.ContainingNamespace.ToDisplayString()};

public partial class {classSymbol.Name}{{");
                var moduleAttribute = classSymbol.GetAttributes().First(att =>
                    att.AttributeClass?.ToDisplayString() == "RCaron.LibrarySourceGenerator.ModuleAttribute");
                if (moduleAttribute.NamedArguments
                    .Any(pair => pair is { Key: "ImplementModuleRun", Value.Value: false }))
                    goto afterImplementModuleRun;
                source.AppendLine(
                    """
[System.CodeDom.Compiler.GeneratedCode("RCaron.LibrarySourceGenerator", null)]
public object? RCaronModuleRun(ReadOnlySpan<char> name, Motor motor, in ArraySegment<PosToken> arguments, CallLikePosToken? callToken,
    IPipeline? pipeline, bool isLeftOfPipeline)
{
switch(name){
""");

                void AppendArgumentGet(IParameterSymbol param)
                {
                    bool AssignableToPosToken(ITypeSymbol typeSymbol)
                    {
                        if (typeSymbol.BaseType == null)
                            return false;
                        var str = typeSymbol.BaseType.ToDisplayString();
                        if (str == "RCaron.PosToken")
                            return true;
                        if (str == "System.Object")
                            return false;
                        return AssignableToPosToken(typeSymbol.BaseType);
                    }

                    if (AssignableToPosToken(param.Type) || param.Type.ToDisplayString() == "RCaron.PosToken")
                        source.AppendLine($"enumerator.CurrentTokens[0];");
                    else
                        source.AppendLine($"motor.EvaluateExpressionHigh(enumerator.CurrentTokens);");
                }

                foreach (var member in classSymbol.GetMembers())
                {
                    if (member is not IMethodSymbol methodSymbol)
                        continue;
                    var att = GetMethodAttribute(member);
                    if (att == null)
                        continue;
                    var name = (string)att.ConstructorArguments[0].Value!;

                    source.AppendLine(@$"case ""{name.ToLowerInvariant()}"":
{{");

                    if (methodSymbol.Parameters.Length == 2 &&
                        methodSymbol.Parameters[0].Type.ToDisplayString() == "RCaron.Motor" &&
                        methodSymbol.Parameters[1].Type.ToDisplayString() == "System.ReadOnlySpan<RCaron.PosToken>")
                    {
                        if (!methodSymbol.ReturnsVoid)
                            source.Append("return ");
                        source.AppendLine($@"{methodSymbol.Name}(motor, arguments);");
                    }
                    else
                    {
                        // todo(error checking): make sure no 2 parameters have the same name except casing differences
                        var parameters = methodSymbol.Parameters.Skip(1).ToArray();
                        IParameterSymbol? pipelineParam = null;
                        bool pipelineParamIsIPipeline = false;
                        // variable definitions
                        foreach (var param in parameters)
                        {
                            if (param.GetAttributes().Any(t =>
                                    t.AttributeClass?.ToDisplayString() ==
                                    "RCaron.LibrarySourceGenerator.FromPipelineAttribute"))
                            {
                                pipelineParam = param;
                                pipelineParamIsIPipeline = param.Type.ToDisplayString() == "RCaron.IPipeline";
                            }

                            source.AppendLine($"bool {param.Name}_hasValue = false;");
                            source.Append($"{param.Type.ToDisplayString()} {param.Name}_value = ");
                            source.Append(GetDefaultValueString(param));
                            source.AppendLine(";");

                            if (ReferenceEquals(pipelineParam, param))
                            {
                                source.AppendLine($$"""
if (pipeline != null)
{
    {{param.Name}}_hasValue = true;
""");
                                if (pipelineParamIsIPipeline)
                                    source.AppendLine($"    {param.Name}_value = pipeline;");
                                source.AppendLine("}");
                            }
                        }

                        // actual arguments parsing
                        if (parameters.Length != 0)
                        {
                            source.AppendLine(@"var enumerator = callToken != null
                                ? new ArgumentEnumerator(callToken)
                                : new ArgumentEnumerator(arguments);");

                            source.AppendLine(@"while (enumerator.MoveNext())");
                            source.AppendLine("{");

                            // do named arguments
                            source.AppendLine(@"if (enumerator.CurrentName != null)
{");
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var param = parameters[i];
                                if (i != 0)
                                    source.Append("else ");
                                source.AppendLine(
                                    $@"if (enumerator.CurrentName.Equals(""{param.Name.ToLowerInvariant()}"", StringComparison.InvariantCultureIgnoreCase))");
                                source.AppendLine("{");

                                source.AppendLine($"{param.Name}_hasValue = true;");

                                source.Append($"{param.Name}_value = ({param.Type.ToDisplayString()})");
                                AppendArgumentGet(param);

                                source.AppendLine("}");
                            }

                            source.AppendLine(
                                "else { throw RCaronException.NamedArgumentNotFound(enumerator.CurrentName); }");
                            source.AppendLine("}");

                            // do positional arguments
                            source.AppendLine(@"else if(!enumerator.HitNamedArgument)
{");
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var param = parameters[i];
                                if (i != 0)
                                    source.Append("else ");
                                source.AppendLine($@"if(!{param.Name}_hasValue)
{{");
                                source.Append($"{param.Name}_value = ({param.Type.ToDisplayString()})");
                                AppendArgumentGet(param);
                                source.AppendLine($@"{param.Name}_hasValue = true;");
                                source.AppendLine("}");
                            }

                            source.AppendLine("else { throw RCaronException.LeftOverPositionalArgument(); }");

                            source.AppendLine("}");
                            source.AppendLine("""
else
    throw RCaronException.PositionalArgumentAfterNamedArgument();
""");
                            source.AppendLine("}");
                            // check all parameters without a default value are assigned
                            if (Array.FindIndex(parameters, p => !p.HasExplicitDefaultValue) != -1)
                            {
                                source.AppendLine("if(true");
                                foreach (var param in parameters)
                                {
                                    if (param.HasExplicitDefaultValue)
                                        continue;
                                    source.AppendLine($"&& !{param.Name}_hasValue");
                                }

                                source.AppendLine(@"){
    throw RCaronException.ArgumentsLeftUnassigned(); }");
                            }
                        }

                        void WriteCall(bool useReturnsNoReturnValue = false)
                        {
                            source.Append($@"{methodSymbol.Name}");
                            if (useReturnsNoReturnValue)
                                source.Append("_ReturnsNoReturnValue");
                            source.Append("(motor");
                            foreach (var param in parameters)
                                source.Append($", {param.Name}: {param.Name}_value");
                            source.Append(")");
                        }

                        // method accepts an IPipeline, just pass it
                        if (pipelineParam == null || pipelineParamIsIPipeline)
                        {
                            if (!methodSymbol.ReturnsVoid)
                                source.Append("return ");
                            WriteCall();
                            source.AppendLine(";");
                        }
                        else
                        {
                            source.AppendLine("if (pipeline != null) {");
                            // method accepts an IEnumerator, just pass it
                            if (pipelineParam.Type.ToDisplayString() == "System.Collections.IEnumerator")
                            {
                                source.AppendLine($"{pipelineParam.Name}_value = pipeline.GetEnumerator();");
                                if (!methodSymbol.ReturnsVoid)
                                    source.Append("return ");
                                WriteCall();
                                source.AppendLine(";");
                            }
                            // method accepts an object
                            else
                            {
                                // when we are left of a pipeline, we return a FuncEnumerator
                                // closure allocations :yum:
                                source.AppendLine("if (isLeftOfPipeline) {");
                                source.Append("return new FuncEnumerator((current) => {");
                                source.AppendLine($$"""
{{pipelineParam.Name}}_value = ({{pipelineParam.Type.ToDisplayString()}})current;
""");
                                source.Append("return ");
                                WriteCall(methodSymbol.ReturnsVoid);
                                source.AppendLine(";");
                                source.AppendLine("}, pipeline.GetEnumerator());");
                                source.AppendLine("}");
                                // when we are NOT left of a pipeline, we run for each value from the pipeline and return it as a list
                                source.AppendLine("else {");
                                if (!methodSymbol.ReturnsVoid)
                                    source.Append("var ls = new List<object?>();");

                                source.AppendLine(@"
var pipelineEnumerator = pipeline.GetEnumerator();
while (pipelineEnumerator.MoveNext()) {");
                                source.AppendLine($$"""
{{pipelineParam.Name}}_value = ({{pipelineParam.Type.ToDisplayString()}})pipelineEnumerator.Current;
""");
                                if (!methodSymbol.ReturnsVoid)
                                    source.Append("ls.Add(");
                                WriteCall(methodSymbol.ReturnsVoid);
                                if (!methodSymbol.ReturnsVoid)
                                    source.Append(")");
                                source.AppendLine(";");
                                source.AppendLine("}");

                                if (methodSymbol.ReturnsVoid)
                                    source.AppendLine("return RCaronInsideEnum.NoReturnValue;");
                                else source.AppendLine("return ls;");
                                source.AppendLine("}");
                            }

                            source.AppendLine("}");
                            // when we don't have an incoming pipeline
                            source.AppendLine("else {");
                            if (!methodSymbol.ReturnsVoid)
                                source.Append("return ");
                            WriteCall();
                            source.AppendLine(";");
                            if (methodSymbol.ReturnsVoid)
                                source.AppendLine("return RCaronInsideEnum.NoReturnValue;");
                            source.AppendLine("}");
                        }
                    }

                    if (methodSymbol.ReturnsVoid)
                        source.AppendLine("return RCaronInsideEnum.NoReturnValue;");
                    source.AppendLine("}");
                }

                // switch case
                source.AppendLine("}");

                source.AppendLine("return RCaronInsideEnum.MethodNotFound;");
                // method
                source.AppendLine("}");
                afterImplementModuleRun: ;

                foreach (var member in classSymbol.GetMembers())
                {
                    if (member is not IMethodSymbol { ReturnsVoid: true } methodSymbol)
                        continue;
                    if (GetMethodAttribute(member) == null)
                        continue;
                    source.Append(
                        $"""
[System.CodeDom.Compiler.GeneratedCode("RCaron.LibrarySourceGenerator", null)]
public object {methodSymbol.Name}_ReturnsNoReturnValue(
""");
                    for (var i = 0; i < methodSymbol.Parameters.Length; i++)
                    {
                        var param = methodSymbol.Parameters[i];
                        if (i != 0)
                            source.Append(", ");
                        source.Append($"{param.Type.ToDisplayString()} {param.Name}");
                        if (param.HasExplicitDefaultValue)
                            source.Append($" = {GetDefaultValueString(param)}");
                    }

                    source.Append(")");
                    source.AppendLine("{");
                    source.Append($@"{methodSymbol.Name}(");
                    for (var i = 0; i < methodSymbol.Parameters.Length; i++)
                    {
                        var param = methodSymbol.Parameters[i];
                        if (i != 0)
                            source.Append(", ");
                        source.Append($"{param.Name}: {param.Name}");
                    }

                    source.AppendLine(");");
                    source.AppendLine("return (object)RCaronInsideEnum.NoReturnValue;");
                    source.AppendLine("}");
                }

                // class
                source.AppendLine("}");
                context.AddSource($"{classSymbol.Name}.g.cs", source.ToString());
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public static AttributeData GetMethodAttribute(ISymbol symbol)
            => symbol.GetAttributes().FirstOrDefault(att =>
                att.AttributeClass?.ToDisplayString() == "RCaron.LibrarySourceGenerator.MethodAttribute");

        public static string GetDefaultValueString(IParameterSymbol param)
        {
            if (param.HasExplicitDefaultValue)
            {
                if (param.ExplicitDefaultValue is string str)
                    return '"' + str + '"';
                if (param.ExplicitDefaultValue is bool boolean)
                    return boolean.ToString().ToLowerInvariant();
                else
                    return param.ExplicitDefaultValue?.ToString() ?? "default";
            }

            return "default";
        }
    }
}