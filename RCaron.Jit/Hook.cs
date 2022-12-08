using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Linq.Expressions;
using ExpressionTreeToString;

namespace RCaron.Jit;

public static class Hook
{
    public static Motor Run(RCaronRunnerContext ctx, MotorOptions? options = null, Motor? fakeMotor = null)
    {
        fakeMotor ??= new Motor(new RCaronRunnerContext(ctx.FileScope));
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