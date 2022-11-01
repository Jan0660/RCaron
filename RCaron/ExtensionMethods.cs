namespace RCaron;

// https://stackoverflow.com/a/18159076/12520276
static class ListEx
{
    public static void RemoveFrom<T>(this List<T> lst, int from)
    {
        lst.RemoveRange(from, lst.Count - from);
    }

    internal static List<T> GetRange<T>(this List<T> list, Range range)
    {
        var (start, length) = range.GetOffsetAndLength(list.Count);
        return list.GetRange(start, length);
    }

    internal static T[] GetRangeAsArray<T>(this List<T> list, Range range)
    {
        var (start, length) = range.GetOffsetAndLength(list.Count);
        var arr = new T[length];
        list.CopyTo(start, arr, 0, length);
        return arr;
    }

    internal static ArraySegment<T> Segment<T>(this T[] array, Range range)
    {
        var (start, length) = range.GetOffsetAndLength(array.Length);
        return new ArraySegment<T>(array, start, length);
    }

    internal static ArraySegment<T> Segment<T>(this ArraySegment<T> segment, Range range)
    {
        var (start, length) = range.GetOffsetAndLength(segment.Count);
        return new ArraySegment<T>(segment.Array, segment.Offset + start, length);
    }
    
    #region IList<T> stuffs
    
    public static int FindIndex<T>(IList<T> array, Predicate<T> match)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        return FindIndex(array, 0, array.Count, match);
    }
    
    public static int FindIndex<T>(IList<T> array, int startIndex, Predicate<T> match)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        return FindIndex(array, startIndex, array.Count - startIndex, match);
    }

    public static int FindIndex<T>(IList<T> array, int startIndex, int count, Predicate<T> match)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (startIndex < 0 || startIndex > array.Count)
        {
            throw new IndexOutOfRangeException();
        }

        if (count < 0 || startIndex > array.Count - count)
        {
            throw new IndexOutOfRangeException();
        }

        if (match == null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (match(array[i]))
                return i;
        }
        return -1;
    }
    
    public static int IndexOf<T>(IList<T> array, Predicate<T> match)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (match(array[i]))
                return i;
        }

        return -1;
    }
    #endregion
    
    // from https://stackoverflow.com/a/5461399/12520276
    public static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        var interfaceTypes = givenType.GetInterfaces();

        foreach (var it in interfaceTypes)
        {
            if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                return true;
        }

        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
            return true;

        Type baseType = givenType.BaseType;
        if (baseType == null) return false;

        return IsAssignableToGenericType(baseType, genericType);
    }

    public static T Expect<T>(this object? obj)
    {
        if (obj is T o)
            return o;
        throw new RCaronException($"expected type '{typeof(T)}' from '{obj}'", RCaronExceptionCode.TypeMismatch);
    }

    public static object NotNull(this object? obj)
    {
        if (obj == null)
            throw new RCaronException("expected not null", RCaronExceptionCode.ExpectedNotNull);
        return obj;
    }

    public static bool IsNumericType(this Type type)
        => type != typeof(bool) &&
            (type.IsPrimitive ||
             // type.IsEnum ||
             type == typeof(decimal));
}