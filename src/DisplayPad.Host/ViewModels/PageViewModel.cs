using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.ViewModels;

public partial class PageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    public ObservableCollection<KeyViewModel> Keys { get; } = new();

    public PageViewModel(PageConfig model)
    {
        _name = model.Name;
        foreach (var key in model.Keys)
            Keys.Add(new KeyViewModel(key));
    }

    public bool CanMoveKey(KeyViewModel source, KeyViewModel target, bool isFolder) =>
        source != target && Keys.Contains(source) && Keys.Contains(target) &&
        (!isFolder || (source.KeyIndex != AppConfig.FolderBackKeyIndex &&
                       target.KeyIndex != AppConfig.FolderBackKeyIndex));

    public bool MoveKey(KeyViewModel source, KeyViewModel target, bool isFolder)
    {
        if (!CanMoveKey(source, target, isFolder)) return false;

        int sourceIndex = Keys.IndexOf(source);
        int targetIndex = Keys.IndexOf(target);
        source.KeyIndex = targetIndex;
        target.KeyIndex = sourceIndex;
        // Keep the objects so open folders and their runtime references follow the binding.
        Keys[sourceIndex] = target;
        Keys[targetIndex] = source;
        return true;
    }

    public PageConfig ToModel() => new()
    {
        Name = Name,
        Keys = Keys.Select(k => k.ToModel()).ToList()
    };
}
