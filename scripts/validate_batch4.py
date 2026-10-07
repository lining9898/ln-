from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile
r = Path(__file__).resolve().parents[1]
checks = 0
def check(ok, label):
    global checks
    assert ok, label
    checks += 1
    print('PASS:', label)
for p in list(r.glob('src/**/*.csproj')) + list(r.glob('tests/**/*.csproj')) + list(r.glob('src/**/*.xaml')):
    ET.parse(p)
    check(True, 'XML 语法 ' + str(p.relative_to(r)))
parsers = ['PdfDocumentParser.cs', 'DocxDocumentParser.cs', 'TextDocumentParser.cs', 'MarkdownDocumentParser.cs']
for p in parsers:
    f = r/'src/AiKnowledgeAssistant.Infrastructure/Parser'/p
    check(f.is_file() and 'IDocumentParser' in f.read_text(), '独立 Parser ' + p)
project = ET.parse(r/'src/AiKnowledgeAssistant.Infrastructure/AiKnowledgeAssistant.Infrastructure.csproj')
packages = {p.attrib['Include']: p.attrib['Version'] for p in project.findall('.//PackageReference')}
check(packages.get('PdfPig') == '0.1.16', 'PdfPig 版本固定')
check(packages.get('DocumentFormat.OpenXml') == '3.5.1', 'Open XML 版本固定')
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
panel = ET.parse(r/'src/AiKnowledgeAssistant.Desktop/UI/DocumentsPanel.xaml')
check(any(b.attrib.get('Click') == 'ReparseClick' for b in panel.findall('.//w:Button', ns)), '重新解析 UI 按钮')
check(panel.find('.//w:ListView[@ItemsSource="{Binding ParsedUnits}"]', ns) is not None,
      '来源预览 UI 绑定')
check(ET.parse(r/'src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj').findtext('.//UseWPF') == 'true',
      '保持 WPF 桌面技术路线')
fixtures = r/'tests/fixtures/batch4'
for name in ['text_pages.pdf', 'mixed_scan.pdf', 'scan_only.pdf']:
    f = fixtures/name
    check(f.read_bytes().startswith(b'%PDF-') and f.stat().st_size < 100_000, '真实小型 PDF 样本 ' + name)
for name in ['headings_table.docx', 'empty.docx']:
    f = fixtures/name
    check(zipfile.is_zipfile(f) and f.stat().st_size < 100_000, '真实小型 DOCX 样本 ' + name)
for name in ['lines 中文.txt', 'headings 中文.md']:
    f = fixtures/name
    check('中文' in f.read_text(encoding='utf-8') and f.stat().st_size < 100_000,
          '中英文文本样本 ' + name)
source = '\n'.join(f.read_text() for f in (r/'src').rglob('*.cs'))
check(not any(term in source for term in ['GB 50010','结构规范','条文号']), '代码无专业写死字段')
for p in ['models/chi_sim.traineddata', 'data/databases/documents.json', 'cache/page.png']:
    check(subprocess.run(['git','check-ignore','-q',p],cwd=r).returncode == 0, '运行数据未进 Git '+p)
print(f'{checks} 项静态检查通过；Windows 实机操作未验证。')
