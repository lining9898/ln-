using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using AiKnowledgeAssistant.Core.KnowledgeBase;
using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Desktop.ViewModels;

if (args.Length > 0 && args[0] == "--probe")
{
    var reopened = new JsonKnowledgeBaseStore(new UserDataPaths(args[1])).List();
    if (reopened.Count != 2 || reopened[0].Id != Guid.Parse(args[2]) || reopened[0].Name != "公司资料 新名称")
        return 2;
    Console.WriteLine("独立新进程读取成功");
    return 0;
}
int checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    bool rejected = false;
    try { action(); } catch (T) { rejected = true; }
    Check(rejected, name);
}
var root = Path.Combine(Path.GetTempPath(), "aka-batch2-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new UserDataPaths(Path.Combine(root, "中文用户 空格"));
    paths.EnsureDirectories();
    var store = new JsonKnowledgeBaseStore(paths);
    var file = Path.Combine(paths.Databases, "knowledge-bases.json");
    Check(store.List().Count == 0 && !File.Exists(file), "初始空列表不写空库覆盖文件");
    var a = store.Create("  公司资料  ");
    var b = store.Create("项目 A");
    Check(a.Name == "公司资料" && b.Name == "项目 A", "中文名称、首尾及内部空格");
    Check(a.Id != b.Id && a.Id != Guid.Empty, "唯一 knowledge_base_id");
    Check(store.List().Count == 2, "知识库列表");
    var before = File.ReadAllBytes(file);
    foreach (var name in new[] { "", "   ", "\t", "\n", new string('中', 81), "坏\n名称", "公司资料", " 公司资料 " })
        Reject<ArgumentException>(() => store.Create(name), "拒绝空/超长/控制字符/重复名称");
    Check(before.SequenceEqual(File.ReadAllBytes(file)), "非法新建不改变持久化数据");
    Reject<ArgumentException>(() => store.Rename(b.Id, " 公司资料 "), "重命名重复拒绝");
    Reject<ArgumentException>(() => store.Rename(b.Id, " "), "重命名空名称拒绝");
    Check(before.SequenceEqual(File.ReadAllBytes(file)), "非法重命名不改变原数据");
    var renamed = store.Rename(a.Id, "公司资料 新名称");
    Check(renamed.Id == a.Id && renamed.CreatedAt == a.CreatedAt && renamed.Name == "公司资料 新名称", "重命名保持 ID 和创建时间");
    Check(store.List().Single(k => k.Id == b.Id) == b, "重命名不改变其他库");
    Check(new JsonKnowledgeBaseStore(paths).List().SequenceEqual(store.List()), "重新建立服务后持久化一致");
    var process = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
        process.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    process.ArgumentList.Add("--probe"); process.ArgumentList.Add(Path.Combine(root, "中文用户 空格"));
    process.ArgumentList.Add(a.Id.ToString());
    using (var child = Process.Start(process)!)
    {
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        Check(child.WaitForExit(30000) && child.ExitCode == 0, "独立进程重启读取 ID/名称/列表: " + output + error);
    }
    var max = store.Create(new string('中', 80));
    Check(max.Name.Length == 80, "80 字符边界接受"); store.Delete(max.Id);
    var unicode = store.Create(string.Concat(Enumerable.Repeat("😀", 80)));
    Check(unicode.Name.Length == 160, "Unicode 按字符而非 UTF-16 长度计数"); store.Delete(unicode.Id);
    var caseItem = store.Create("Manual");
    Reject<ArgumentException>(() => store.Create("manual"), "重复名称不区分大小写"); store.Delete(caseItem.Id);
    var normalized = store.Create("é");
    Reject<ArgumentException>(() => store.Create("e\u0301"), "Unicode NFC 等价名称去重"); store.Delete(normalized.Id);
    var pathName = store.Create("../不是文件路径");
    Check(pathName.Name == "../不是文件路径", "显示名称不作为文件路径"); store.Delete(pathName.Id);
    Reject<InvalidOperationException>(() => store.Delete(Guid.NewGuid()), "未知 ID 删除拒绝");
    Reject<InvalidOperationException>(() => store.Rename(Guid.Empty, "其他"), "未知 ID 重命名拒绝");
    before = File.ReadAllBytes(file);
    using (var held = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Reject<IOException>(() => new JsonKnowledgeBaseStore(paths).Create("不能写入"), "其他实例持锁时禁止覆盖");
    Check(before.SequenceEqual(File.ReadAllBytes(file)), "锁冲突保留原数据");
    var dialogs = new FakeDialogs();
    var vm = new KnowledgeBasesViewModel(store, dialogs);
    vm.Refresh();
    Check(vm.Items.Count == 2 && vm.HasItems, "ViewModel 加载真实列表");
    Check(!vm.DeleteCommand.CanExecute(null) && !vm.RenameCommand.CanExecute(null), "未选库禁用删除与重命名");
    vm.Selected = vm.Items.Single(k => k.Id == a.Id);
    dialogs.Confirm = false; vm.DeleteCommand.Execute(null);
    Check(dialogs.Confirmations == 1 && dialogs.LastId == a.Id && store.List().Count == 2,
        "取消二次确认不删除，确认指定 ID");
    dialogs.Name = "取消不会保存"; dialogs.CancelEdit = true; vm.CreateCommand.Execute(null);
    Check(store.List().Count == 2, "取消新建不写入");
    dialogs.CancelEdit = false; dialogs.Name = "普通手册"; vm.CreateCommand.Execute(null);
    var createdId = vm.Selected!.Id;
    Check(vm.Items.Count == 3 && vm.Selected.Name == "普通手册", "新建命令更新列表和选中项");
    dialogs.Name = "个人学习资料"; vm.RenameCommand.Execute(null);
    Check(vm.Selected!.Id == createdId && vm.Selected.Name == "个人学习资料", "重命名命令更新列表并保持 ID");
    dialogs.Confirm = true; vm.DeleteCommand.Execute(null);
    Check(vm.Items.Count == 2 && store.List().All(k => k.Id != createdId), "确认删除更新持久化和列表");
    var sentinel = Path.Combine(paths.Documents, b.Id.ToString("N"));
    Directory.CreateDirectory(sentinel); File.WriteAllText(Path.Combine(sentinel, "其他库资料.txt"), "retain");
    vm.Selected = vm.Items.Single(k => k.Id == a.Id); vm.DeleteCommand.Execute(null);
    Check(store.List().Single() == b, "删除仅匹配 ID，其他库完整不变");
    Check(File.ReadAllText(Path.Combine(sentinel, "其他库资料.txt")) == "retain", "删除不得误删其他库文件");
    Check(!vm.DeleteCommand.CanExecute(null), "删除后清除选中项");
    var stable = File.ReadAllBytes(file);
    foreach (var corrupt in new[] { "{", "{}", "null", "{\"schema_version\":99,\"knowledge_bases\":[]}", "{\"schema_version\":1,\"knowledge_bases\":null}" })
    {
        File.WriteAllText(file, corrupt);
        Reject<InvalidDataException>(() => store.List(), "损坏/未知版本读取拒绝");
        Reject<InvalidDataException>(() => store.Create("不覆盖"), "损坏/未知版本禁止写入");
        Check(File.ReadAllText(file) == corrupt, "损坏文件原样保留");
    }
    File.WriteAllBytes(file, stable);
    var json = JsonNode.Parse(stable)!;
    json["knowledge_bases"]!.AsArray().Add(json["knowledge_bases"]![0]!.DeepClone());
    File.WriteAllText(file, json.ToJsonString());
    Reject<InvalidDataException>(() => store.List(), "重复 ID 元数据拒绝");
    File.WriteAllBytes(file, stable);
    var events = 0; vm.PropertyChanged += (_, _) => events++;
    vm.Refresh(); Check(events > 0 && vm.Summary == "共 1 个知识库", "列表刷新绑定通知");
    File.WriteAllText(file, "{"); vm.Refresh();
    Check(vm.Items.Count == 1 && dialogs.Errors.Count == 1, "读取失败保留可见列表并报告错误");
    Check(!Directory.EnumerateFiles(paths.Databases, "*.tmp").Any(), "正常操作无残留临时文件");
    Console.WriteLine($"完成 {checks} 项检查；Windows 实机验证仍受阻。");
    return 0;
}
finally { Directory.Delete(root, true); }

sealed class FakeDialogs : IKnowledgeBaseDialogs
{
    public bool Confirm { get; set; }
    public bool CancelEdit { get; set; }
    public string Name { get; set; } = "";
    public int Confirmations { get; private set; }
    public Guid LastId { get; private set; }
    public List<string> Errors { get; } = new();
    public void EditName(string title, string initialName, Action<string> save) { if (!CancelEdit) save(Name); }
    public bool ConfirmDelete(KnowledgeBase item) { Confirmations++; LastId = item.Id; return Confirm; }
    public void ShowError(string message) => Errors.Add(message);
}
