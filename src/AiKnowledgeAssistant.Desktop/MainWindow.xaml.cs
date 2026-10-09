using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Desktop.Viewer;

namespace AiKnowledgeAssistant.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private void SaveSearchKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Ai?.SaveWebKey(WebSearchKey.Password);
        WebSearchKey.Clear();
    }
    private void OpenWebSource(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.IsLoopback || uri.UserInfo.Length > 0) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, "无法启动默认浏览器。请复制链接后打开。", "打开网页"); }
    }
    private void OpenCitationPdf(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AiCitationListItem item } && item.IsPdf)
            new PdfOriginalWindow(item.FilePath, item.PageNumber!.Value, item.PageCount!.Value, item.FileName) { Owner = this }.Show();
    }
    private void OpenSearchPdf(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { Search: not null } vm) return;
        if (vm.Search.Source is null || vm.Search.Source.DocumentId != vm.Search.SelectedResult?.Hit.DocumentId)
            vm.Search.OpenSourceCommand.Execute(null);
        var source = vm.Search.Source;
        if (source?.CurrentPage is int page && source.PageCount is int count)
            new PdfOriginalWindow(source.ManagedFilePath, page, count, source.FileName) { Owner = this }.Show();
        else MessageBox.Show(this, "请先选择 PDF 搜索结果。", "原始 PDF");
    }
}
