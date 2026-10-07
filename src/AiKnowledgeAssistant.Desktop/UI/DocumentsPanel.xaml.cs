using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AiKnowledgeAssistant.Desktop.ViewModels;

namespace AiKnowledgeAssistant.Desktop.UI;

public partial class DocumentsPanel : UserControl
{
    public DocumentsPanel() => InitializeComponent();

    private async void ChooseFilesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DocumentsViewModel model || !model.CanImport) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的资料",
            Filter = "支持的资料|*.pdf;*.docx;*.txt;*.md|PDF|*.pdf|Word 文档|*.docx|文本文件|*.txt|Markdown|*.md",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
            await model.ImportFilesAsync(dialog.FileNames);
    }

    private void FilesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is DocumentsViewModel { CanImport: true } &&
            e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FilesDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not DocumentsViewModel { CanImport: true } model ||
            !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await model.ImportFilesAsync(paths);
    }
}
