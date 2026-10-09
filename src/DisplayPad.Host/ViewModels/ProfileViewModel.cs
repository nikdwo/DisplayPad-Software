using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayPad.Host.Services;
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

    public bool MovePage(PageViewModel source, int insertionIndex)
    {
        var before = Pages.ToArray();
        if (!ListReorder.Move(Pages, source, insertionIndex)) return false;
        var newNumbers = before.Select(page => Pages.IndexOf(page) + 1).ToArray();
        foreach (var page in Pages) UpdatePageTargets(page, newNumbers);
        return true;
    }

    private static void UpdatePageTargets(PageViewModel page, int[] newNumbers)
    {
        foreach (var key in page.Keys)
        {
            if (key.ActionType == KeyActionType.SwitchPage && key.PageSwitchMode == PageSwitchMode.GoTo &&
                key.TargetPage >= 1 && key.TargetPage <= newNumbers.Length)
                key.TargetPage = newNumbers[key.TargetPage - 1];
            if (key.FolderPage is not null) UpdatePageTargets(key.FolderPage, newNumbers);
        }
    }

    public ProfileConfig ToModel() => new()
    {
        Name = Name,
        Pages = Pages.Select(p => p.ToModel()).ToList()
    };
}
