using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;

namespace AiKnowledgeAssistant.Desktop.Viewer;

public sealed class PdfOriginalWindow : Window
{
    private readonly string path;
    private readonly int pageCount;
    private int currentPage;
    private bool rendering;
    private readonly Image pageImage = new() { Stretch = Stretch.Uniform, Width = 850 };
    private readonly TextBox pageInput = new() { Width = 64, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock status = new() { Margin = new Thickness(12, 8, 12, 8) };
    private readonly CancellationTokenSource lifetime = new();

    public PdfOriginalWindow(string path, int page, int pageCount, string? displayName = null)
    {
        this.path = path;
        this.pageCount = pageCount;
        currentPage = page;
        Title = "原始 PDF · " + (displayName ?? Path.GetFileName(path));
        Width = 1000; Height = 800; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        Button Add(string text, Action click)
        {
            var button = new Button { Content = text, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(10, 6, 10, 6) };
            button.Click += (_, _) => click(); toolbar.Children.Add(button); return button;
        }
        Add("上一页", () => _ = RenderAsync(currentPage - 1));
        toolbar.Children.Add(pageInput);
        AutomationProperties.SetName(pageInput, "PDF 原始页码");
        Add("跳转", () => { if (int.TryParse(pageInput.Text, out var target)) _ = RenderAsync(target); else status.Text = "请输入有效物理页码。"; });
        Add("下一页", () => _ = RenderAsync(currentPage + 1));
        Add("放大", () => pageImage.Width = Math.Min(2500, pageImage.Width * 1.2));
        Add("缩小", () => pageImage.Width = Math.Max(300, pageImage.Width / 1.2));
        Add("系统阅读器", () =>
        {
            try
            {
                if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new IOException();
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            { status.Text = "无法打开系统 PDF 阅读器，请检查文件和默认程序。"; }
        });
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        AutomationProperties.SetName(pageImage, "PDF 原始页面图像");
        layout.Children.Add(new ScrollViewer { Content = pageImage, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        Loaded += async (_, _) => await RenderAsync(currentPage);
        Closed += (_, _) => lifetime.Cancel();
    }

    private async Task RenderAsync(int page)
    {
        if (rendering || lifetime.IsCancellationRequested) return;
        if (page < 1 || page > pageCount) { status.Text = "页码超出范围。"; return; }
        rendering = true;
        status.Text = "正在加载原始 PDF 页面…";
        var prefix = Path.Combine(Path.GetTempPath(), "aka-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "tools", "ocr", "pdftoppm.exe");
            if (!File.Exists(executable) || !File.Exists(path)) throw new IOException();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-f", page.ToString(), "-l", page.ToString(), "-scale-to", "1800", "-png", "-singlefile", path, prefix })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
            await Task.WhenAll(stderr, stdout);
            if (process.ExitCode != 0) throw new IOException();
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(prefix + ".png"); bitmap.EndInit(); bitmap.Freeze();
            pageImage.Source = bitmap;
            currentPage = page; pageInput.Text = page.ToString();
            status.Text = $"PDF 物理第 {page} 页 / 共 {pageCount} 页 · 原始页面";
        }
        catch (OperationCanceledException) { status.Text = "页面加载已取消或超时。"; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
        { status.Text = "无法显示 PDF 原始页面，请检查文件及随程序安装的 PDF 工具。"; }
        finally
        {
            rendering = false;
            try { File.Delete(prefix + ".png"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
