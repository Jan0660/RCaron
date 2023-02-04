using System.Buffers;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using ZSpitz.Util;

namespace RCaron.Jit.Binders;

public class RCaronGetIndexBinder : GetIndexBinder
{
    public FileScope FileScope { get; }
    public Motor Motor { get; }

    public RCaronGetIndexBinder(CallInfo callInfo, FileScope fileScope, Motor motor) : base(callInfo)
    {
        FileScope = fileScope;
        Motor = motor;
    }

    public override DynamicMetaObject FallbackGetIndex(DynamicMetaObject target, DynamicMetaObject[] indexes,
        DynamicMetaObject? errorSuggestion)
    {
        if (target.Expression.Type == typeof(IDynamicMetaObjectProvider))
        {
            return new DynamicMetaObject(
                Expression.Call(target.Expression, "GetMetaObject", Array.Empty<Type>(), Expression.Constant(null)),
                BindingRestrictions.GetTypeRestriction(target.Expression, target.LimitType));
        }
        // todo(feat-fullness): if target is IDynamicMetaObjectProvider
        if (target.LimitType.IsArray)
        {
            Expression ix = Expression.ArrayIndex(Expression.Convert(target.Expression, target.LimitType),
                indexes.Select(x =>
                        (Expression)Expression.Convert(Expression.Convert(x.Expression, x.LimitType), typeof(int)))
                    .ToArray());
            if (ix.Type != ReturnType)
                ix = Expression.Convert(ix, ReturnType);
            return new DynamicMetaObject(ix, target.Restrictions);
        }

        var indexers = target.LimitType.GetIndexers(true, BindingFlags.Public | BindingFlags.Instance);

        // using stackalloc here makes this die with exit code -1073741819 when debugging
        // Span<int> scores = stackalloc int[indexers.Length];
        Span<int> scores = ArrayPool<int>.Shared.Rent(indexers.Length).AsSpan()[..indexers.Length];
        foreach (var indexer in indexers)
        {
            // shouldn't happen but just in case
            if (indexer.GetMethod is null)
                continue;
            var parameters = indexer.GetMethod.GetParameters();
            if (parameters.Length != indexes.Length)
            {
                continue;
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (param.ParameterType.IsAssignableFrom(indexes[i].LimitType))
                {
                    scores[i] += 100;
                }
                else if (param.ParameterType.IsGenericType
                         && ListEx.IsAssignableToGenericType(indexes[i].RuntimeType,
                             param.ParameterType.GetGenericTypeDefinition()))
                    // parameters[j].ParameterType.GetGenericParameterConstraints()
                {
                    scores[i] += 10;
                }
                else if (param.ParameterType.IsInstanceOfType(indexes[i].RuntimeType))
                {
                    scores[i] += 10;
                }
                else if (param.ParameterType.IsNumeric() && indexes[i].LimitType.IsNumeric())
                {
                    scores[i] += 10;
                    // needsNumericConversions[i] = true;
                }
                else
                {
                    scores[i] = 0;
                    break;
                }
            }
        }

        var bestIndexerIndex = 0;
        var bestIndexerScore = 0;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i] > bestIndexerScore)
            {
                bestIndexerIndex = i;
                bestIndexerScore = scores[i];
            }
        }

        if (bestIndexerScore == 0)
        {
            // custom indexers
            if (FileScope.IndexerImplementations != null && indexes.Length == 1)
            {
                foreach (var indexer in FileScope.IndexerImplementations)
                {
                    var val = target.Value;
                    var type = target.LimitType;
                    if (indexer.Do(Motor, indexes[0].Value, ref val, ref type))
                    {
                        Expression expr = Expression.Constant(val);
                        expr = expr.Type == ReturnType ? expr : Expression.Convert(expr, ReturnType);
                        return new DynamicMetaObject(expr,
                            // todo: make another interface that allows the indexer to specify its own restrictions
                            BindingRestrictions.GetInstanceRestriction(target.Expression, target.Value)
                                .Merge(BindingRestrictions.GetExpressionRestriction(
                                    Expression.Equal(indexes[0].Expression, Expression.Constant(indexes[0].Value)))));
                    }
                }
            }

            throw new RCaronException("No suitable indexer found", RCaronExceptionCode.NoSuitableIndexerImplementation);
        }

        var bestIndexer = indexers[bestIndexerIndex];
        var @params = bestIndexer.GetMethod.GetParameters();
        var args = new Expression[@params.Length];
        for (var i = 0; i < @params.Length; i++)
        {
            if (@params[i].ParameterType == indexes[i].LimitType)
            {
                args[i] = indexes[i].Expression;
            }
            else
            {
                args[i] = Expression.Convert(indexes[i].Expression, @params[i].ParameterType);
            }
        }

        return new DynamicMetaObject(
            Expression.MakeIndex(
                target.Expression.Type == target.LimitType
                    ? target.Expression
                    : Expression.Convert(target.Expression, target.LimitType), bestIndexer, args), target.Restrictions);
    }
}