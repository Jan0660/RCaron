namespace RCaron;


public ref struct ArgumentEnumerator
{
    public string? CurrentName { get; private set; }
    public ArraySegment<PosToken> CurrentTokens { get; private set; }
    private CallLikePosToken? _callToken;
    public ArraySegment<PosToken> _argumentTokens;
    public string? _rawText;
    public int Index { get; private set; }
    public bool HitNamedArgument { get; private set; }

    public ArgumentEnumerator(CallLikePosToken callToken)
    {
        this._callToken = callToken;
        _argumentTokens = default;
        this.CurrentName = default;
        CurrentTokens = default;
        Index = -1;
        HitNamedArgument = false;
    }

    public ArgumentEnumerator(ArraySegment<PosToken> argumentTokens, string rawText)
    {
        this._callToken = null;
        _argumentTokens = argumentTokens;
        _rawText = rawText;
        this.CurrentName = default;
        CurrentTokens = default;
        Index = -1;
        HitNamedArgument = false;
    }

    public bool MoveNext()
    {
        Index++;
        if (_callToken != null)
        {
            if (Index >= _callToken.Arguments.Length)
            {
                return false;
            }

            if (_callToken.Arguments[Index].Length > 2 && _callToken.Arguments[Index][1].Type == TokenType.Colon)
            {
                HitNamedArgument = true;
                CurrentName = ((KeywordToken)_callToken.Arguments[Index][0]).String;
                CurrentTokens = _callToken.Arguments[Index].Segment(2..);
            }
            else
            {
                CurrentName = null;
                CurrentTokens = _callToken.Arguments[Index];
            }
        }
        else
        {
            if (Index >= _argumentTokens.Count)
            {
                return false;
            }

            if (_argumentTokens[Index].Type == TokenType.MathOperator && _argumentTokens[Index].EqualsString(_rawText!, "-") &&
                _argumentTokens[Index + 1].Type == TokenType.Keyword)
            {
                HitNamedArgument = true;
                CurrentName = ((KeywordToken)_argumentTokens[Index + 1]).String;
                CurrentTokens = _argumentTokens[(Index + 2)..(Index + 3)];
                Index += 2;
            }
            else
            {
                CurrentName = null;
                CurrentTokens = _argumentTokens[Index..(Index+1)];
            }
        }
        
        return true;
    }
}