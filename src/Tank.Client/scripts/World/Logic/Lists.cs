namespace Tank.Client.World;

/// <summary>List helpers for the views' working lists, whose order doesn't matter.</summary>
public static class Lists
{
    /// <summary>Removes item <paramref name="index"/> by moving the last one into its place: O(1), the order not kept.</summary>
    public static void RemoveSwapAt<T>(this List<T> list, int index)
    {
        list[index] = list[^1];
        list.RemoveAt(list.Count - 1);
    }
}
