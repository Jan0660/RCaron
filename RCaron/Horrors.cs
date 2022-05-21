namespace RCaron;

public static class Horrors
{
    public static object Sum(object a, object b)
    {
        switch (a)
        {
            case sbyte sbyte1 when b is sbyte sbyte2:
                return (sbyte) (sbyte1 + sbyte2);
            case byte byte1 when b is byte byte2:
                return (byte) (byte1 + byte2);
            case short short1 when b is short short2:
                return (short) (short1 + short2);
            case ushort ushort1 when b is ushort ushort2:
                return (ushort) (ushort1 + ushort2);
            case int int1 when b is int int2:
                return (int) (int1 + int2);
            case uint uint1 when b is uint uint2:
                return (uint) (uint1 + uint2);
            case long long1 when b is long long2:
                return (long) (long1 + long2);
            case ulong ulong1 when b is ulong ulong2:
                return (ulong) (ulong1 + ulong2);
            case nint nint1 when b is nint nint2:
                return (nint) (nint1 + nint2);
            case nuint nuint1 when b is nuint nuint2:
                return (nuint) (nuint1 + nuint2);
            case float float1 when b is float float2:
                return (float) (float1 + float2);
            case double double1 when b is double double2:
                return (double) (double1 + double2);
            case decimal decimal1 when b is decimal decimal2:
                return (decimal) (decimal1 + decimal2);
            case string string1 when b is string string2:
                return (string) (string1 + string2);
        }

        throw new Exception("Unsupported operands for +");
    }

    public static object Subtract(object a, object b)
    {
        switch (a)
        {
            case sbyte sbyte1 when b is sbyte sbyte2:
                return (sbyte)(sbyte1 - sbyte2);
            case byte byte1 when b is byte byte2:
                return (byte)(byte1 - byte2);
            case short short1 when b is short short2:
                return (short)(short1 - short2);
            case ushort ushort1 when b is ushort ushort2:
                return (ushort)(ushort1 - ushort2);
            case int int1 when b is int int2:
                return (int)(int1 - int2);
            case uint uint1 when b is uint uint2:
                return (uint)(uint1 - uint2);
            case long long1 when b is long long2:
                return (long)(long1 - long2);
            case ulong ulong1 when b is ulong ulong2:
                return (ulong)(ulong1 - ulong2);
            case nint nint1 when b is nint nint2:
                return (nint)(nint1 - nint2);
            case nuint nuint1 when b is nuint nuint2:
                return (nuint)(nuint1 - nuint2);
            case float float1 when b is float float2:
                return (float)(float1 - float2);
            case double double1 when b is double double2:
                return (double)(double1 - double2);
            case decimal decimal1 when b is decimal decimal2:
                return (decimal)(decimal1 - decimal2);
        }
        
        throw new Exception("Unsupported operands for -");
    }

    public static object Multiply(object a, object b)
    {
        switch (a)
        {
            case sbyte sbyte1 when b is sbyte sbyte2:
                return (sbyte) (sbyte1 * sbyte2);
            case byte byte1 when b is byte byte2:
                return (byte) (byte1 * byte2);
            case short short1 when b is short short2:
                return (short) (short1 * short2);
            case ushort ushort1 when b is ushort ushort2:
                return (ushort) (ushort1 * ushort2);
            case int int1 when b is int int2:
                return (int) (int1 * int2);
            case uint uint1 when b is uint uint2:
                return (uint) (uint1 * uint2);
            case long long1 when b is long long2:
                return (long) (long1 * long2);
            case ulong ulong1 when b is ulong ulong2:
                return (ulong) (ulong1 * ulong2);
            case nint nint1 when b is nint nint2:
                return (nint) (nint1 * nint2);
            case nuint nuint1 when b is nuint nuint2:
                return (nuint) (nuint1 * nuint2);
            case float float1 when b is float float2:
                return (float) (float1 * float2);
            case double double1 when b is double double2:
                return (double) (double1 * double2);
            case decimal decimal1 when b is decimal decimal2:
                return (decimal) (decimal1 * decimal2);
        }
        
        throw new Exception("Unsupported operands for *");
    }
    public static bool IsGreater(object a, object b)
    {
        switch (a)
        {
            case sbyte sbyte1 when b is sbyte sbyte2:
                return (sbyte1 > sbyte2);
            case byte byte1 when b is byte byte2:
                return (byte1 > byte2);
            case short short1 when b is short short2:
                return (short1 > short2);
            case ushort ushort1 when b is ushort ushort2:
                return (ushort1 > ushort2);
            case int int1 when b is int int2:
                return (int1 > int2);
            case uint uint1 when b is uint uint2:
                return (uint1 > uint2);
            case long long1 when b is long long2:
                return (long1 > long2);
            case ulong ulong1 when b is ulong ulong2:
                return (ulong1 > ulong2);
            case nint nint1 when b is nint nint2:
                return (nint1 > nint2);
            case nuint nuint1 when b is nuint nuint2:
                return (nuint1 > nuint2);
            case float float1 when b is float float2:
                return (float1 > float2);
            case double double1 when b is double double2:
                return (double1 > double2);
            case decimal decimal1 when b is decimal decimal2:
                return (decimal1 > decimal2);
        }
        
        throw new Exception("Unsupported operands for *");
    }
}