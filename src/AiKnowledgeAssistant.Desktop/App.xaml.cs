using System.IO;
using System.Windows;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Desktop.UI;
using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Viewer;
using AiKnowledgeAssistant.Core.Documents;
using Microsoft.Data.Sqlite;

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
            var window = new MainWindow();
            var database = new SqliteDatabase(paths);
            database.Initialize();
            new LegacyJsonMigration(database).MigrateIfNeeded();
            var knowledgeBaseStore = new SqliteKnowledgeBaseStore(database);
            var documentRepository = new SqliteDocumentRepository(database);
            documentRepository.RecoverInterruptedParsing();
            var parsers = new IDocumentParser[] {
                new PdfDocumentParser(new TesseractPdfPageOcr()),
                new DocxDocumentParser(), new TextDocumentParser(), new MarkdownDocumentParser()
            };
            var parsing = new DocumentParsingService(documentRepository, documentRepository, parsers);
            var documents = new DocumentsViewModel(documentRepository,
                new DocumentImportService(knowledgeBaseStore, documentRepository, paths),
                parsing, documentRepository);
            var knowledgeBases = new KnowledgeBasesViewModel(knowledgeBaseStore,
                new KnowledgeBaseDialogs(window)) { Documents = documents };
            var search = new DocumentSearchViewModel(new SqliteDocumentSearch(database),
                () => knowledgeBases.Selected?.Id, new LocalSourceViewer(documentRepository, documentRepository));
            window.DataContext = new MainViewModel(paths, knowledgeBases, documents, search);
            MainWindow = window;
            window.Show();
            knowledgeBases.Refresh();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or SqliteException)
        {
            // Do not expose exception text, user paths or private content in logs.
            MessageBox.Show("本地数据无法安全打开或迁移。请检查磁盘空间与目录权限，并保留原数据库和旧 JSON 备份。",
                "AI 知识库助手", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
