using Avalonia.Controls;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        OpenFileButton.Click += async (_, _) => await PickFileAsync();
        OpenFolderButton.Click += async (_, _) => await PickFolderAsync();
        OpenInViewerButton.Click += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.OpenFromLibraryAsync(vm.SelectedLibraryEntry);
        };
        RemoveButton.Click += (_, _) => ViewModel?.RemoveFromLibrary(ViewModel.SelectedLibraryEntry);
        ChapterList.DoubleTapped += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.OpenFromLibraryAsync(vm.SelectedLibraryEntry);
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async Task PickFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || ViewModel is not { } vm) return;
        var path = await FilePickers.PickComicFileAsync(top);
        if (path is not null) await vm.OpenChapterPathAsync(path);
    }

    private async Task PickFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || ViewModel is not { } vm) return;
        var path = await FilePickers.PickComicFolderAsync(top);
        if (path is not null) await vm.OpenChapterPathAsync(path);
    }
}
