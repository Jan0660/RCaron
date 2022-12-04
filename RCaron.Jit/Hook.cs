using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Linq.Expressions;
using ExpressionTreeToString;

namespace RCaron.Jit;

public static class Hook
{
    public static Motor Run(RCaronRunnerContext ctx, MotorOptions? options = null, Motor? fakeMotor = null)
    {
        var block = Compiler.CompileToBlock(ctx, true);
        var lambda = Expression.Lambda(block.blockExpression, parameters: new[] { block.fakedMotor! });
        var compiled = lambda.Compile();
        fakeMotor ??= new Motor(new RCaronRunnerContext(ctx.FileScope));
        Debug.WriteLine(lambda.ToString("C#"));
        compiled.DynamicInvoke(fakeMotor);
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