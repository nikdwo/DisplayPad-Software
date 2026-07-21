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

    public PageConfig ToModel() => new()
    {
        Name = Name,
        Keys = Keys.Select(k => k.ToModel()).ToList()
    };
}
