using System.Dynamic;

namespace RCaron.Jit.Binders;

public class RCaronInvokeMemberBinder : InvokeMemberBinder
{
    public RCaronInvokeMemberBinder(string name, bool ignoreCase, CallInfo callInfo) : base(name, ignoreCase, callInfo)
    {
    }

    public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        throw new NotImplementedException();
    }

    public override DynamicMetaObject FallbackInvokeMember(DynamicMetaObject target, DynamicMetaObject[] args,
        DynamicMetaObject? errorSuggestion)
    {
        throw new NotImplementedException();
    }
}