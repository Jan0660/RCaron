using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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
            if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                && classDeclarationSyntax.AttributeLists.Count > 0)
            {
                var g = (ITypeSymbol)context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax);
                foreach (var att in g.GetAttributes())
                {
                    if (att.AttributeClass.ToDisplayString() == "RCaron.LibrarySourceGenerator.ModuleAttribute")
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
#nullable enable

namespace {classSymbol.ContainingNamespace.ToDisplayString()};

public partial class {classSymbol.Name}{{");
                source.AppendLine(
                    @"public object? RCaronModuleRun(string name, Motor motor, in ReadOnlySpan<PosToken> arguments){
switch(name){");

                void AppendArgumentThing(bool isPositional, IParameterSymbol param)
                {
                    bool InheritsPosToken(ITypeSymbol typeSymbol)
                    {
                        if (typeSymbol.BaseType == null)
                            return false;
                        var str = typeSymbol.BaseType.ToDisplayString();
                        if (str == "RCaron.PosToken")
                            return true;
                        if (str == "System.Object")
                            return false;
                        return InheritsPosToken(typeSymbol.BaseType);
                    }

                    var g = isPositional ? string.Empty : " + 2";
                    if(InheritsPosToken(param.Type))
                        source.AppendLine($"arguments[i{g}];");
                    else
                        source.AppendLine($"motor.SimpleEvaluateExpressionSingle(arguments[i{g}]);");
                }
                foreach (var member in classSymbol.GetMembers())
                {
                    if (member is not IMethodSymbol methodSymbol)
                        continue;
                    var att = member.GetAttributes().FirstOrDefault(att =>
                        att.AttributeClass?.ToDisplayString() == "RCaron.LibrarySourceGenerator.MethodAttribute");
                    if (att == null)
                        continue;
                    var name = (string)att.ConstructorArguments[0].Value;

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
                        // variable definitions
                        foreach (var param in parameters)
                        {
                            if (param.Name == "arguments")
                                context.ReportDiagnostic(Diagnostic.Create(
                                    new DiagnosticDescriptor(
                                        "RCLG0001",
                                        "Forbidden parameter name",
                                        "Parameter cannot have the name 'arguments'. Complain to me and I'll unforbid it.",
                                        "yeet",
                                        DiagnosticSeverity.Error,
                                        true), param.Locations.FirstOrDefault()));
                            source.AppendLine($"bool {param.Name}_hasValue = false;");
                            source.Append($"{param.Type.ToDisplayString()} {param.Name} = ");
                            
                            if (param.HasExplicitDefaultValue)
                            {
                                if (param.ExplicitDefaultValue is string str)
                                    source.Append('"' + str + '"');
                                else
                                    source.Append(param.ExplicitDefaultValue?.ToString() ?? "default");
                            }
                            else
                                source.Append("default");

                            source.AppendLine(";");
                        }

                        source.AppendLine(@"for (var i = 0; i < arguments.Length; i++)
{
    if (arguments[i].Type == TokenType.Operator && arguments[i].EqualsString(motor.Raw, ""-"") &&
                        arguments[i + 1].Type == TokenType.Keyword)
    {
        var argName = arguments[i + 1].ToSpan(motor.Raw);");
                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var param = parameters[i];
                            if (i != 0)
                                source.Append("else ");
                            source.AppendLine(
                                $@"if(argName.Equals(""{param.Name.ToLowerInvariant()}"", StringComparison.InvariantCultureIgnoreCase))
{{");
                            source.AppendLine($"{param.Name}_hasValue = true;");

                            source.Append($"{param.Name} = ({param.Type.ToDisplayString()})");
                            AppendArgumentThing(false, param);
                            source.AppendLine("i += 2;");
                            source.AppendLine("}");
                        }
                        if (parameters.Length != 0)
                            source.AppendLine(@"else { throw RCaronException.NamedArgumentNotFound(argName); }");

                        source.AppendLine("}");

                        if (parameters.Length == 0)
                            source.AppendLine(
                                @"if(arguments.Length != 0){ throw RCaronException.LeftOverPositionalArgument(); }");
                        if (parameters.Length != 0)
                        {
                            source.AppendLine("else{");
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var param = parameters[i];
                                if (i != 0)
                                    source.Append("else ");
                                source.AppendLine($"if(!{param.Name}_hasValue){{");
                                source.Append($"{param.Name} = ({param.Type.ToDisplayString()})");
                                AppendArgumentThing(true, param);
                                source.AppendLine($@"{param.Name}_hasValue = true;
}}");
                            }

                            source.AppendLine(@"else { throw RCaronException.LeftOverPositionalArgument(); }");
                            source.AppendLine("}");
                        }

                        source.AppendLine("}");
                        // check all parameters without a default value are assigned
                        if (parameters.Length != 0)
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

                        if (!methodSymbol.ReturnsVoid)
                            source.Append("return ");
                        source.Append($@"{methodSymbol.Name}(motor");
                        foreach (var param in parameters)
                            source.Append($", {param.Name}: {param.Name}");
                        source.AppendLine(");");
                    }

                    if (methodSymbol.ReturnsVoid)
                        source.AppendLine("return RCaronInsideEnum.NoReturnValue;");
                    source.AppendLine("}");
                }

                // switch case
                source.AppendLine("}");

                source.AppendLine("return RCaronInsideEnum.MethodNotFound;");
                // method, class
                source.AppendLine("}");
                source.AppendLine("}");
                context.AddSource($"{classSymbol.Name}.g.cs", source.ToString());
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register the attribute sources
            context.RegisterForPostInitialization((i) =>
            {
                void AddSource(string name)
                {
                    var assembly = typeof(ModuleSourceGenerator).Assembly;
                    using var resource = assembly.GetManifestResourceStream($"RCaron.LibrarySourceGenerator.{name}.cs");
                    using var reader = new StreamReader(resource);
                    i.AddSource($"{name}.g.cs", reader.ReadToEnd());
                }

                AddSource("ModuleAttribute");
                AddSource("MethodAttribute");
            });

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }
    }
}