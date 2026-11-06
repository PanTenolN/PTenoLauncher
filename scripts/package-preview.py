"""Package clean preview binaries/source; never include user data or Minecraft cache."""
import argparse, hashlib, zipfile, json
import xml.etree.ElementTree as ET
from pathlib import Path

root=Path(__file__).resolve().parents[1]
version=ET.parse(root/"Directory.Build.props").findtext("PropertyGroup/Version")
parser=argparse.ArgumentParser()
parser.add_argument("--artifacts",type=Path,default=root/"artifacts"/version)
artifacts=parser.parse_args().artifacts
publish=artifacts/"win-x64"
if not (publish/"PTenoLauncher.exe").is_file():
    raise SystemExit("Publish the Windows app first.")
portable=artifacts/f"PTenoLauncher-Portable-{version}-win-x64.zip"
source=artifacts/f"PTenoLauncher-Source-{version}.zip"
binary_files=[p for p in publish.rglob("*") if p.is_file() and p.suffix.lower()!=".pdb"]
for path in binary_files:
    relative=path.relative_to(publish)
    if any(part in ("portable-data","secrets","cache","instances") for part in relative.parts) or path.suffix in (".dpapi",".db"):
        raise SystemExit("Unexpected data file in publish output.")
with zipfile.ZipFile(portable.with_suffix(".zip.tmp"),"w",zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for path in sorted(binary_files): archive.write(path,path.relative_to(publish).as_posix())
    archive.writestr("portable.flag","")
portable.with_suffix(".zip.tmp").replace(portable)
source_files=[]
for top in ("src","tests","scripts","installer","docs",".github"):
    for path in (root/top).rglob("*"):
        if path.is_file() and not any(part in ("bin","obj","__pycache__") for part in path.relative_to(root/top).parts):
            source_files.append(path)
for top in (".gitignore",".editorconfig","global.json","Directory.Build.props","NuGet.Config","PTenoLauncher.sln",
            "README.md","THIRD_PARTY_NOTICES.md","DEVELOPMENT_STATUS.md","TEST_REPORT.md","RELEASE_NOTES.md","REQUIREMENTS_TRACEABILITY.md","MICROSOFT_AUTH_SETUP.md","SESSION_CHECKPOINT.md","PROJECT_PROGRESS_SUMMARY.md"):
    source_files.append(root/top)
with zipfile.ZipFile(source.with_suffix(".zip.tmp"),"w",zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for path in sorted(set(source_files)): archive.write(path,path.relative_to(root).as_posix())
source.with_suffix(".zip.tmp").replace(source)
hashes=[]
for path in [portable, source, *artifacts.glob(f"PTenoLauncher-Setup-{version}-win-x64.exe")]:
    with path.open("rb") as stream:
        digest=hashlib.file_digest(stream,"sha256").hexdigest()
    hashes.append(f"{digest}  {path.relative_to(artifacts).as_posix()}")
(artifacts/"SHA256SUMS.txt").write_text("\n".join(hashes)+"\n",encoding="utf-8")
print(json.dumps({"portable":str(portable),"portableBytes":portable.stat().st_size,
                  "source":str(source),"sourceBytes":source.stat().st_size,"sourceFiles":len(set(source_files))},ensure_ascii=False,indent=2))
