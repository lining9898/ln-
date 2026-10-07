from pathlib import Path
import json
import subprocess
import xml.etree.ElementTree as ET
r=Path(__file__).resolve().parents[1]
n=0
def check(ok,label):
 global n
 assert ok,label
 n+=1
 print('PASS:',label)
check(json.loads((r/'global.json').read_text())['sdk']['version']=='10.0.100','SDK 基线')
projects=ET.parse(r/'AiKnowledgeAssistant.slnx').findall('Project')
check(len(projects)==3,'三个项目')
for p in projects:
 path=r/p.attrib['Path']; check(path.is_file(),path.name)
 root=ET.parse(path)
 for ref in root.findall('.//ProjectReference'):
  check((path.parent/ref.attrib['Include']).resolve().is_file(),'项目引用有效')
 if '.Core' in path.name: check(not root.findall('.//ProjectReference'),'Core 无实现层依赖')
check(ET.parse(r/'Directory.Build.props').findtext('.//Nullable')=='enable','可空引用检查')
for p in ['data/documents/private.pdf','private/key.txt','models/model.onnx','cache/test.tmp','.env','artifacts/Setup.exe','example.db','src/AiKnowledgeAssistant.Core/bin/test.dll']:
 check(subprocess.run(['git','check-ignore','-q',p],cwd=r).returncode==0,'忽略 '+p)
check(subprocess.run(['git','check-ignore','-q','README.md'],cwd=r).returncode==1,'源文档可提交')
for p in ['architecture.md','batches.md','batch-0-report.md']: check((r/'docs'/p).is_file(),'文档 '+p)
check(not list(r.rglob('*.cs')),'无提前功能实现')
print(f'{n} 项静态检查通过；不代表编译或 Windows 验收。')
