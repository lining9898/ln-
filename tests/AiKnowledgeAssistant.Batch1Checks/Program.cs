using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Desktop.ViewModels;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
    checks++;
}
var testRoot = Path.Combine(Path.GetTempPath(), "aka-batch1-" + Guid.NewGuid().ToString("N"), "中文用户 空格");
try
{
    var paths = new UserDataPaths(testRoot);
    Check(paths.Root == Path.Combine(testRoot, "AIKnowledgeAssistant", "data"), "独立用户数据路径");
    paths.EnsureDirectories();
    Check(new[] { paths.Root, paths.Databases, paths.Documents, paths.Indexes, paths.Cache, paths.Config }
        .All(Directory.Exists), "全部数据目录创建");
    var sentinel = Path.Combine(paths.Documents, "保留资料.txt");
    File.WriteAllText(sentinel, "existing content");
    var restarted = new UserDataPaths(testRoot);
    restarted.EnsureDirectories();
    Check(File.ReadAllText(sentinel) == "existing content", "重复初始化保留数据");
    Check(restarted.Root == paths.Root, "重建服务路径稳定");
    foreach (var invalid in new[] { "", "relative/path" })
    {
        var rejected = false;
        try { _ = new UserDataPaths(invalid); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "非法基础路径被拒绝");
    }
    var blockedBase = Path.Combine(testRoot, "不是目录");
    File.WriteAllText(blockedBase, "file");
    var failed = false;
    try { new UserDataPaths(blockedBase).EnsureDirectories(); }
    catch (IOException) { failed = true; }
    Check(failed, "创建失败向调用方报告");
    var vm = new MainViewModel(paths);
    Check(vm.NavigationItems.SequenceEqual(new[] { "AI问答", "知识库", "文档搜索", "文件管理", "设置" }), "五个导航入口");
    Check(vm.SelectedPage == "AI问答", "默认问答页");
    var notifications = 0;
    vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SelectedPage") notifications++; };
    foreach (var page in vm.NavigationItems) vm.SelectedPage = page;
    Check(vm.SelectedPage == "设置" && notifications == 4, "导航切换与绑定通知");
    vm.SelectedPage = "设置";
    vm.SelectedPage = "未知页面";
    Check(vm.SelectedPage == "设置" && notifications == 4, "无效与重复导航无副作用");
    Check(vm.DataRoot == paths.Root, "设置页显示实际目录");
    Console.WriteLine($"完成 {checks} 项检查；不代表 Windows 或 WPF 运行测试。");
}
finally
{
    Directory.Delete(Path.GetDirectoryName(testRoot)!, true);
}
