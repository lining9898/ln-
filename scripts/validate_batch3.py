from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
r = Path(__file__).resolve().parents[1]
checks = 0
def check(condition, label):
    global checks
    assert condition, label
    checks += 1
    print('PASS:', label)
for p in list(r.glob('src/**/*.csproj')) + list(r.glob('tests/**/*.csproj')) + list(r.glob('src/**/*.xaml')):
    ET.parse(p)
    check(True, 'XML 语法 ' + str(p.relative_to(r)))
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
window = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/MainWindow.xaml')
triggers = [t for t in window.findall('.//w:DataTrigger', ns) if t.attrib['Binding'] == '{Binding SelectedPage}']
check([t.attrib['Value'] for t in triggers] == ['AI问答','知识库','文档搜索','文件管理','设置'], '五个一级入口保持不变')
check(len(window.findall('.//{clr-namespace:AiKnowledgeAssistant.Desktop.UI}DocumentsPanel')) == 2,
      '知识库页与文件管理页共用文档面板')
check(window.find('.//w:ComboBox[@DisplayMemberPath="Name"]', ns) is not None, '文件管理页选择知识库')
check(all(b.attrib.get('IsEnabled') == 'False' for b in window.findall('.//w:Button', ns) if 'Command' not in b.attrib),
      '未进入批次的按钮仍禁用')
panel = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/UI/DocumentsPanel.xaml')
check(panel.getroot().attrib.get('AllowDrop') == 'True' and panel.getroot().attrib.get('Drop') == 'FilesDrop',
      '文档面板注册文件拖拽')
headers = [c.attrib['Header'] for c in panel.findall('.//w:GridViewColumn', ns)]
check(headers == ['文件名','类型','大小','导入时间','解析状态','索引状态'], '文档列表字段')
check(window.getroot().attrib['Language'] == 'zh-CN' and panel.getroot().attrib['Language'] == 'zh-CN',
      '中文桌面界面')
project = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj')
check(project.findtext('.//UseWPF') == 'true' and project.findtext('.//OutputType') == 'WinExe', 'WPF 桌面技术路线')
check(not ET.parse(r/'src/AiKnowledgeAssistant.Core/AiKnowledgeAssistant.Core.csproj').findall('.//ProjectReference'),
      'Core 无实现层依赖')
code = (r/'src/AiKnowledgeAssistant.Infrastructure/Storage/JsonKnowledgeBaseStore.cs').read_text()
check('documents.HasDocuments(id)' in code and 'Directory.Delete' not in code, '非空库删除保护且无目录递归删除')
for f in ['data/databases/documents.json','data/documents/abc/file.pdf','.env',
          'tests/AiKnowledgeAssistant.Batch3Checks/bin/test.dll']:
    check(subprocess.run(['git','check-ignore','-q',f],cwd=r).returncode == 0, '忽略 '+f)
print(f'{checks} 项静态检查通过；不等于 Windows 实机验证。')
