using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Desktop;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Desktop.Viewer;
using AiKnowledgeAssistant.Infrastructure.Storage;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        var ai = new AiAssistantViewModel(new NoCredentials(), "test-only", null!, null!, null!, () => null);
        ai.Citations.Add(new AiCitationListItem("S1", "真实 PDF 验收", "物理第 10 页", "UI TEST ONLY", args[0], 10, 58));
        window.DataContext = new MainViewModel(new UserDataPaths(System.IO.Path.GetTempPath()), ai: ai);
        app.MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(400);
                var button = Descendants(window).OfType<Button>().First(b => Equals(b.Content, "查看原始 PDF") && b.IsVisible);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                PdfOriginalWindow? pdf = null;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    await Task.Delay(200);
                    pdf = app.Windows.OfType<PdfOriginalWindow>().FirstOrDefault();
                    if (pdf is not null && Descendants(pdf).OfType<TextBlock>().Any(t => t.Text == "PDF 物理第 10 页 / 共 58 页 · 原始页面")) break;
                }
                if (pdf is null || !Descendants(pdf).OfType<TextBlock>().Any(t => t.Text.Contains("第 10 页") && t.Text.Contains("原始页面")))
                    throw new Exception("Citation PDF did not render expected physical page.");
                var image = Descendants(pdf).OfType<Image>().Single();
                if (image.Source is not System.Windows.Media.Imaging.BitmapSource bitmap || bitmap.PixelWidth < 500)
                    throw new Exception("PDF image is blank or too small.");
                Console.WriteLine("PASS real WPF AI citation button opens original PDF physical page 10");
                var next = Descendants(pdf).OfType<Button>().First(b => Equals(b.Content, "下一页"));
                ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    await Task.Delay(200);
                    if (Descendants(pdf).OfType<TextBlock>().Any(t => t.Text == "PDF 物理第 11 页 / 共 58 页 · 原始页面")) break;
                }
                if (!Descendants(pdf).OfType<TextBlock>().Any(t => t.Text == "PDF 物理第 11 页 / 共 58 页 · 原始页面"))
                    throw new Exception("PDF next-page action failed.");
                Console.WriteLine("PASS original PDF next-page button renders page 11");
                pdf.Close();
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); Environment.ExitCode = 1; }
            finally { window.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Descendants(child)) yield return node; }
    }
    private sealed class NoCredentials : ICredentialStore
    {
        public string? ReadSecret(string target) => null;
        public void SaveSecret(string target, string secret) => throw new NotSupportedException();
        public void DeleteSecret(string target) => throw new NotSupportedException();
    }
}
