"""Package a named, tested development checkpoint without overwriting standard release artifacts.
No authentication, game files, user data, installer compilation or installer-lifecycle claim.
"""
import argparse, hashlib, json, re, shutil, xml.etree.ElementTree as ET, zipfile
from pathlib import Path, PurePosixPath

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--tag',required=True)
parser.add_argument('--publish',type=Path,required=True)
parser.add_argument('--trx',type=Path,required=True)
parser.add_argument('--native-marker',type=Path,required=True)
args=parser.parse_args()
if not re.fullmatch(r'[a-z0-9]+(?:-[a-z0-9]+)*',args.tag):raise SystemExit('Invalid checkpoint tag')
artifacts=root/'artifacts'
publish=args.publish.resolve(); trx=args.trx.resolve(); marker=args.native_marker.resolve()
if not publish.is_relative_to(artifacts.resolve()) or not trx.is_relative_to(artifacts.resolve()):raise SystemExit('Publish/report must be inside workspace artifacts')
if not marker.is_relative_to((root/'.local').resolve()):raise SystemExit('Smoke marker must use an isolated .local root')
if not (publish/'PTenoLauncher.exe').is_file():raise SystemExit('Publish the Windows executable first')
counts=ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
total=int(counts['total'])
if total==0 or int(counts['passed'])!=total or int(counts['failed'])!=0:raise SystemExit('Expected a complete passing source suite')
if 'initialized=True' not in marker.read_text(encoding='utf8'):raise SystemExit('Expected completed native startup check')
for name in ('README.md','THIRD_PARTY_NOTICES.md','DEVELOPMENT_STATUS.md','RELEASE_NOTES.md','TEST_REPORT.md','REQUIREMENTS_TRACEABILITY.md','MICROSOFT_AUTH_SETUP.md','SESSION_CHECKPOINT.md'):
    shutil.copyfile(root/name,publish/name)
shutil.copytree(root/'docs',publish/'docs',dirs_exist_ok=True)
portable=artifacts/f'PTenoLauncher-Portable-0.2.0-{args.tag}-win-x64.zip'
source=artifacts/f'PTenoLauncher-Source-0.2.0-{args.tag}.zip'
forbidden={'.local','.tools','portable-data','secrets','cache','instances','bin','obj','__pycache__'}
def validate_name(name):
    path=PurePosixPath(name)
    if path.is_absolute() or '..' in path.parts or ':' in name or '\\' in name or any(p in forbidden for p in path.parts):raise ValueError('Unsafe/private archive path')
    if path.suffix.lower() in ('.db','.dpapi','.jar') or '.db-' in path.name.lower():raise ValueError('User/game data must not be packaged')
payload=sorted(p for p in publish.rglob('*') if p.is_file() and p.suffix.lower()!='.pdb')
sources=[]
for top in ('src','tests','scripts','installer','docs','.github'):
    sources.extend(p for p in (root/top).rglob('*') if p.is_file() and not any(part in ('bin','obj','__pycache__') for part in p.relative_to(root/top).parts))
sources.extend(root/name for name in ('.gitignore','.editorconfig','global.json','Directory.Build.props','NuGet.Config','PTenoLauncher.sln','README.md','THIRD_PARTY_NOTICES.md','DEVELOPMENT_STATUS.md','TEST_REPORT.md','RELEASE_NOTES.md','REQUIREMENTS_TRACEABILITY.md','MICROSOFT_AUTH_SETUP.md','SESSION_CHECKPOINT.md'))
for archive,files,base in ((portable,payload,publish),(source,sorted(set(sources)),root)):
    with zipfile.ZipFile(archive.with_suffix('.zip.tmp'),'w',zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for path in files:
            relative=path.relative_to(base).as_posix();validate_name(relative);output.write(path,relative)
        if archive==portable:output.writestr('portable.flag','')
    with zipfile.ZipFile(archive.with_suffix('.zip.tmp')) as check:
        if check.testzip() is not None:raise ValueError('Archive CRC failure')
        names=check.namelist()
        if len(names)!=len(set(n.lower() for n in names)):raise ValueError('Duplicate archive path')
        for name in names:validate_name(name)
        if archive==portable:
            if not {'portable.flag','PTenoLauncher.exe','coreclr.dll','hostpolicy.dll'}.issubset(names):raise ValueError('Incomplete self-contained portable')
            config=json.loads(check.read('PTenoLauncher.runtimeconfig.json'))['runtimeOptions']
            if 'framework' in config or 'frameworks' in config:raise ValueError('Framework-dependent payload')
    archive.with_suffix('.zip.tmp').replace(archive)
records=[]
for path in (portable,source,publish/'PTenoLauncher.exe',publish/'PTenoLauncher.Core.dll',publish/'PTenoLauncher.Infrastructure.dll',publish/'PTenoLauncher.dll'):
    with path.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
    records.append({'path':path.relative_to(artifacts).as_posix(),'bytes':path.stat().st_size,'sha256':digest})
(artifacts/(args.tag.upper().replace('-','_')+'_SHA256SUMS.txt')).write_text(''.join(f"{r['sha256']}  {r['path']}\n" for r in records),encoding='utf8')
report={'tag':args.tag,'tests':{'total':total,'passed':total,'failed':0,'skipped':0},'checksums':records,'sourceFiles':len(set(sources)),
        'nativeStartup':'PASS','archivesCrc':'PASS','privateDataExcluded':True,'portableStartup':'NOT RUN',
        'liveQuiltModInspection':'BLOCKED - api.modrinth.com unreachable (10051)',
        'microsoftAuthentication':'BLOCKED - user observed Minecraft Services HTTP 403 after browser OAuth',
        'gameLaunched':False,'standardSetupPortable':'Unchanged earlier payloads; installer not rebuilt in this checkpoint'}
(artifacts/'0.2.0'/f'{args.tag}-checkpoint.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(report,indent=2))
