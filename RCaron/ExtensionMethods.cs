namespace RCaron;

// https://stackoverflow.com/a/18159076/12520276
static class ListEx
{
    public static void RemoveFrom<T>(this List<T> lst, int from)
    {
        lst.RemoveRange(from, lst.Count - from);
    }
}