using Avalonia.Controls;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

public partial class ViewerView : UserControl
{
    public ViewerView()
    {
        InitializeComponent();
        ViewerOpenFile.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || ViewModel is not { } vm) return;
            var path = await FilePickers.PickComicFileAsync(top);
            if (path is not null) await vm.OpenChapterPathAsync(path);
        };
        ViewerOpenFolder.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || ViewModel is not { } vm) return;
            var path = await FilePickers.PickComicFolderAsync(top);
            if (path is not null) await vm.OpenChapterPathAsync(path);
        };
        ViewerGoDetect.Click += (_, _) => ViewModel?.ShowDetect();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
}
