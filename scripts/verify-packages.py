"""Verify shipped hashes/archives and optionally smoke-test a fresh isolated Windows portable extraction."""
import argparse,hashlib,json,os,subprocess,uuid,zipfile
from pathlib import Path,PurePosixPath
root=Path(__file__).resolve().parents[1]
artifacts=root/"artifacts"
parser=argparse.ArgumentParser();parser.add_argument("--smoke",action="store_true");args=parser.parse_args()
records=[]
for line in (artifacts/"SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines():
    if not line.strip():continue
    digest,relative=line.split("  ",1)
    path=(artifacts/relative).resolve()
    if not path.is_relative_to(artifacts.resolve()):raise ValueError("Checksum path outside artifacts")
    with path.open("rb") as stream:actual=hashlib.file_digest(stream,"sha256").hexdigest()
    if actual.lower()!=digest.lower():raise ValueError("Checksum mismatch: "+relative)
    records.append({"path":relative,"bytes":path.stat().st_size,"sha256":actual})
required={"PTenoLauncher-Portable-0.2.0-win-x64.zip","PTenoLauncher-Source-0.2.0.zip","PTenoLauncher-Setup-0.2.0-win-x64.exe","0.2.0/win-x64/PTenoLauncher.exe"}
if {r["path"] for r in records}!=required:raise ValueError("Incomplete release checksum set")
archives=[];portable_directory=None
for name in sorted(required):
    if not name.endswith(".zip"):continue
    with zipfile.ZipFile(artifacts/name) as archive:
        if archive.testzip() is not None:raise ValueError("ZIP CRC failure")
        names=archive.namelist()
        if len(names)!=len({n.lower() for n in names}):raise ValueError("Duplicate ZIP path")
        for name_in_zip in names:
            relative=PurePosixPath(name_in_zip)
            if relative.is_absolute() or ".." in relative.parts or ":" in name_in_zip or "\\" in name_in_zip:raise ValueError("Unsafe ZIP path")
            if any(p in (".local",".tools","portable-data","secrets","cache","instances","bin","obj") for p in relative.parts):raise ValueError("Private data bundled")
            if relative.suffix.lower() in (".db",".dpapi",".jar"):raise ValueError("Database/secret/game data bundled")
        if "Portable" in name:
            if not {"portable.flag","PTenoLauncher.exe","coreclr.dll","hostpolicy.dll"}.issubset(names):raise ValueError("Incomplete self-contained portable")
            config=json.loads(archive.read("PTenoLauncher.runtimeconfig.json"))["runtimeOptions"]
            if "framework" in config or "frameworks" in config:raise ValueError("Portable requires a separately installed framework")
            if args.smoke:
                portable_directory=root/".local"/("portable-offline-mods-"+uuid.uuid4().hex)
                portable_directory.mkdir(parents=True,exist_ok=False)
                archive.extractall(portable_directory)
        else:
            expected={"src/PTenoLauncher.Infrastructure/Mods/InstalledModReadiness.cs","src/PTenoLauncher.Infrastructure/Mods/InstalledModInventory.cs","src/PTenoLauncher.Infrastructure/Mods/LocalModManager.Baseline.cs","src/PTenoLauncher.Core/FabricVersionConstraints.cs","tests/PTenoLauncher.Tests/InstalledModReadinessTests.cs","docs/OFFLINE_MODS.md","SESSION_CHECKPOINT.md","src/PTenoLauncher.Infrastructure/Mods/RemoteModInstaller.LocalFiles.cs","tests/PTenoLauncher.Tests/LocalDependencyTests.cs","docs/LOCAL_MOD_DEPENDENCIES.md","docs/HOME_TEST_CHECKLIST.md","docs/HOME_TEST_RESULTS.md","docs/OPTIONAL_DEPENDENCIES.md","src/PTenoLauncher.Infrastructure/Mods/RemoteDependencyResolver.Optional.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.ModFolders.cs","src/PTenoLauncher.Infrastructure/Minecraft/QuiltInstaller.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.Quilt.cs","tests/PTenoLauncher.Tests/QuiltInstallerTests.cs","scripts/prepare-quilt-fixture.py","docs/QUILT.md"}
            if not expected.issubset(names):raise ValueError("Source checkpoint is incomplete")
        archives.append({"path":name,"entries":len(names),"crc":"PASS","privateDataExcluded":True})
portable_smoke=None
if args.smoke:
    if os.name!="nt":raise RuntimeError("Native portable smoke requires Windows")
    child=subprocess.run([str(portable_directory/"PTenoLauncher.exe"),"--smoke-test"],cwd=root,capture_output=True,text=True,timeout=60)
    if child.returncode:raise RuntimeError("Portable exited with "+str(child.returncode))
    marker=(portable_directory/"portable-data/gui-smoke.txt").read_text(encoding="utf8")
    if "Window opened" not in marker or "initialized=True" not in marker:raise RuntimeError("Portable did not initialize")
    portable_smoke={"exitCode":child.returncode,"marker":marker,"directory":str(portable_directory)}
result={"checksums":records,"archives":archives,"portableSmoke":portable_smoke,"gameLaunched":False}
(artifacts/"0.2.0/final-artifact-verification.json").write_text(json.dumps(result,indent=2),encoding="utf8")
print(json.dumps(result,indent=2))
