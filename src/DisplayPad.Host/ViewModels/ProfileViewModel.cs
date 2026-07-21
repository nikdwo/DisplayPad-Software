using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    public ObservableCollection<PageViewModel> Pages { get; } = new();

    public ProfileViewModel(ProfileConfig model)
    {
        _name = model.Name;
        foreach (var page in model.Pages)
            Pages.Add(new PageViewModel(page));
    }

    public ProfileConfig ToModel() => new()
    {
        Name = Name,
        Pages = Pages.Select(p => p.ToModel()).ToList()
    };
}
