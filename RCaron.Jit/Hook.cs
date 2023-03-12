using System.Diagnostics;
using System.Linq.Expressions;
using ExpressionTreeToString;
using Microsoft.Scripting.Generation;
using RCaron.Parsing;

namespace RCaron.Jit;

public static class Hook
{
    public static Delegate MakeInterpretedWithNoMotor(string code, int compilationThreshold = -1)
    {
        var block = Compiler.CompileToBlock(RCaronRunner.Parse(code));
        var lambda = Expression.Lambda(block);
        return lambda.LightCompile(compilationThreshold);
    }
    public static Delegate CompileWithNoMotor(string code)
    {
        var block = Compiler.CompileToBlock(RCaronRunner.Parse(code));
        var lambda = Expression.Lambda(block);
        return lambda.Compile();
    }
    public static void RunWithNoMotor(string code)
    {
        var compiled = CompileWithNoMotor(code);
        compiled.DynamicInvoke();
    }
    public static Motor Run(RCaronParserContext ctx, MotorOptions? options = null, Motor? fakeMotor = null)
    {
        fakeMotor ??= new Motor(new RCaronParserContext(ctx.FileScope), options);
        var block = Compiler.CompileToBlock(ctx, fakeMotor);
        var lambda = Expression.Lambda(block);
        var compiled = lambda.Compile();
        Debug.WriteLine(lambda.ToString("C#"));
        compiled.DynamicInvoke();
        return fakeMotor;
    }

    public static void EmptyMethod()
    {
        Debug.Assert(true);
#if !RCARONJIT
        throw new("RCARONJIT is not set for RCaron.Jit");
#endif
    }
}