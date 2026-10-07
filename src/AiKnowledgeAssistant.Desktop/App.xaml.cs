using System.IO;
using System.Windows;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Infrastructure.Storage;

namespace AiKnowledgeAssistant.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var paths = UserDataPaths.ForCurrentUser();
            paths.EnsureDirectories();
            var window = new MainWindow { DataContext = new MainViewModel(paths) };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Do not expose exception text, user paths or private content in logs.
            MessageBox.Show("无法创建本地数据目录。请检查当前用户的磁盘空间与目录访问权限，然后重新打开软件。",
                "AI 知识库助手", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
