using System.Collections.ObjectModel;

namespace DisplayPad.Host.Services;

internal static class ListReorder
{
    public static bool Move<T>(ObservableCollection<T> items, T source, int insertionIndex)
    {
        int from = items.IndexOf(source);
        if (from < 0 || insertionIndex < 0 || insertionIndex > items.Count) return false;
        int to = insertionIndex > from ? insertionIndex - 1 : insertionIndex;
        if (from == to) return false;
        items.Move(from, to);
        return true;
    }
}
