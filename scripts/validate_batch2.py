from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
r = Path(__file__).resolve().parents[1]
n = 0
def check(ok, label):
    global n
    assert ok, label
    n += 1
    print('PASS:', label)
for p in list(r.glob('src/**/*.csproj')) + list(r.glob('tests/**/*.csproj')) + list(r.glob('src/**/*.xaml')):
    ET.parse(p)
    check(True, 'XML 语法 ' + str(p.relative_to(r)))
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
w = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/MainWindow.xaml')
triggers = [t for t in w.findall('.//w:DataTrigger', ns) if t.attrib['Binding'] == '{Binding SelectedPage}']
check([t.attrib['Value'] for t in triggers] == ['AI问答','知识库','文档搜索','文件管理','设置'], '保持五个入口')
commands = [b.attrib.get('Command') for b in w.findall('.//w:Button', ns) if 'Command' in b.attrib]
check(commands == ['{Binding CreateCommand}','{Binding RenameCommand}','{Binding DeleteCommand}','{Binding RefreshCommand}'], '只启用知识库管理命令')
check(all(b.attrib.get('IsEnabled') == 'False' for b in w.findall('.//w:Button', ns) if 'Command' not in b.attrib), '其他批次操作禁用')
check(w.getroot().attrib['Language'] == 'zh-CN', '中文界面语言')
check(w.find('.//w:ListBox[@ItemsSource="{Binding Items}"]', ns) is not None, '知识库列表绑定')
p = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj')
check(p.findtext('.//OutputType') == 'WinExe' and p.findtext('.//UseWPF') == 'true', 'WPF 桌面路线')
check(not ET.parse(r/'src/AiKnowledgeAssistant.Core/AiKnowledgeAssistant.Core.csproj').findall('.//ProjectReference'), 'Core 无实现层依赖')
confirm = (r/'src/AiKnowledgeAssistant.Desktop/UI/KnowledgeBaseDialogs.cs').read_text()
check('MessageBoxButton.YesNo' in confirm and 'MessageBoxResult.No' in confirm, '删除确认默认否（静态）')
store = (r/'src/AiKnowledgeAssistant.Infrastructure/Storage/JsonKnowledgeBaseStore.cs').read_text()
check('Directory.Delete' not in store, '存储层无目录递归删除')
for f in ['data/databases/knowledge-bases.json','.env','tests/AiKnowledgeAssistant.Batch2Checks/bin/test.dll']:
    check(subprocess.run(['git','check-ignore','-q',f],cwd=r).returncode == 0, '忽略 '+f)
print(f'{n} 项静态检查通过，不代表 Windows 实机验证。')
