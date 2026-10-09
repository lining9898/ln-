using System.IO;
using System.Net.Http;
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
using AiKnowledgeAssistant.Infrastructure.Embedding;
using AiKnowledgeAssistant.Infrastructure.AI;
using AiKnowledgeAssistant.Infrastructure.Security;
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
            var fullTextSearch = new SqliteDocumentSearch(database);
            var semanticSearch = new SqliteSemanticDocumentSearch(database, new HashingTextEmbedder());
            var sourceViewer = new LocalSourceViewer(documentRepository, documentRepository);
            var search = new DocumentSearchViewModel(fullTextSearch, () => knowledgeBases.Selected?.Id, sourceViewer);
            var credentials = new WindowsCredentialStore();
            const string credentialTarget = "AIKnowledgeAssistant/DeepSeek/APIKey";
            var deepSeek = new DeepSeekChatProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(45) },
                credentials, new DeepSeekOptions(credentialTarget));
            var ai = new AiAssistantViewModel(credentials, credentialTarget, deepSeek,
                new RagAnswerService(new HybridDocumentSearch(fullTextSearch, semanticSearch), deepSeek,
                    new TavilyWebSearch(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, credentials)),
                new CitationVerifier(sourceViewer), () => knowledgeBases.Selected);
            window.DataContext = new MainViewModel(paths, knowledgeBases, documents, search, ai);
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
