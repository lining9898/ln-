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
triggers = w.findall('.//w:DataTrigger', ns)
check([t.attrib['Value'] for t in triggers] == ['AI问答','知识库','文档搜索','文件管理','设置'], '五个页面可见性绑定')
check(all(b.attrib.get('IsEnabled') == 'False' for b in w.findall('.//w:Button', ns)), '未实现操作禁用')
check(w.getroot().attrib['Language'] == 'zh-CN', '中文界面语言')
check(w.find('.//w:ScrollViewer', ns) is not None, '小窗口内容可滚动')
p = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj')
check(p.findtext('.//OutputType') == 'WinExe' and p.findtext('.//UseWPF') == 'true', '保持 WPF 桌面路线')
core = ET.parse(r/'src/AiKnowledgeAssistant.Core/AiKnowledgeAssistant.Core.csproj')
check(not core.findall('.//ProjectReference'), 'Core 无实现层依赖')
for f in ['data/private.txt','.env','models/model.onnx','tests/AiKnowledgeAssistant.Batch1Checks/bin/test.dll']:
    check(subprocess.run(['git','check-ignore','-q',f],cwd=r).returncode == 0,'忽略 '+f)
print(f'{n} 项静态检查通过；XAML 编译和运行需独立验证。')
