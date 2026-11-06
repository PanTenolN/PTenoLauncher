"""Verify shipped hashes/archives and optionally smoke-test a fresh isolated Windows portable extraction."""
import argparse,hashlib,json,os,shutil,subprocess,uuid,zipfile
import xml.etree.ElementTree as ET
from pathlib import Path,PurePosixPath
root=Path(__file__).resolve().parents[1]
version=ET.parse(root/"Directory.Build.props").findtext("PropertyGroup/Version")
parser=argparse.ArgumentParser()
parser.add_argument("--smoke",action="store_true")
parser.add_argument("--artifacts",type=Path,default=root/"artifacts"/version)
parser.add_argument("--smoke-root",type=Path,default=root/".local")
args=parser.parse_args()
artifacts=args.artifacts
records=[]
for line in (artifacts/"SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines():
    if not line.strip():continue
    digest,relative=line.split("  ",1)
    path=(artifacts/relative).resolve()
    if not path.is_relative_to(artifacts.resolve()):raise ValueError("Checksum path outside artifacts")
    with path.open("rb") as stream:actual=hashlib.file_digest(stream,"sha256").hexdigest()
    if actual.lower()!=digest.lower():raise ValueError("Checksum mismatch: "+relative)
    records.append({"path":relative,"bytes":path.stat().st_size,"sha256":actual})
required={f"PTenoLauncher-Portable-{version}-win-x64.zip",f"PTenoLauncher-Source-{version}.zip",f"PTenoLauncher-Setup-{version}-win-x64.exe"}
if {r["path"] for r in records}!=required:raise ValueError("Incomplete release checksum set")
archives=[];portable_name=None
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
            if not {"PROJECT_PROGRESS_SUMMARY.md","DEVELOPMENT_STATUS.md","docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md"}.issubset(names):raise ValueError("Portable checkpoint documentation is incomplete")
            if not {"portable.flag","PTenoLauncher.exe","coreclr.dll","hostpolicy.dll"}.issubset(names):raise ValueError("Incomplete self-contained portable")
            config=json.loads(archive.read("PTenoLauncher.runtimeconfig.json"))["runtimeOptions"]
            if "framework" in config or "frameworks" in config:raise ValueError("Portable requires a separately installed framework")
            portable_name=name
        else:
            expected={"src/PTenoLauncher.Infrastructure/Mods/InstalledModReadiness.cs","src/PTenoLauncher.Infrastructure/Mods/InstalledModInventory.cs","src/PTenoLauncher.Infrastructure/Mods/LocalModManager.Baseline.cs","src/PTenoLauncher.Core/FabricVersionConstraints.cs","tests/PTenoLauncher.Tests/InstalledModReadinessTests.cs","docs/OFFLINE_MODS.md","SESSION_CHECKPOINT.md","src/PTenoLauncher.Infrastructure/Mods/RemoteModInstaller.LocalFiles.cs","tests/PTenoLauncher.Tests/LocalDependencyTests.cs","docs/LOCAL_MOD_DEPENDENCIES.md","docs/HOME_TEST_CHECKLIST.md","docs/HOME_TEST_RESULTS.md","docs/OPTIONAL_DEPENDENCIES.md","src/PTenoLauncher.Infrastructure/Mods/RemoteDependencyResolver.Optional.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.ModFolders.cs","src/PTenoLauncher.Infrastructure/Minecraft/QuiltInstaller.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.Quilt.cs","tests/PTenoLauncher.Tests/QuiltInstallerTests.cs","scripts/prepare-quilt-fixture.py","docs/QUILT.md"}
            expected.update({"PROJECT_PROGRESS_SUMMARY.md","DEVELOPMENT_STATUS.md","docs/DEVELOPMENT_BRIEF_2026-10-28.md","docs/requirements-baseline.json","scripts/verify-requirements.py","docs/PROCESS_LIFECYCLE.md","docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.Game.cs","src/PTenoLauncher.Desktop/Dialogs/StopGameDialog.axaml","src/PTenoLauncher.Infrastructure/AI/OpenAiClient.cs","src/PTenoLauncher.Infrastructure/AI/TranslationWorkspace.cs","tests/PTenoLauncher.Tests/ProcessLifecycleTests.cs","tests/PTenoLauncher.Tests/GameStopUiTests.cs"})
            expected.update(f"src/PTenoLauncher.Desktop/Localization/Locales/{language}.json" for language in ("pl","de","es","fr","pt-BR","ru"))
            if not expected.issubset(names):raise ValueError("Source checkpoint is incomplete")
            language_files={"src/PTenoLauncher.Desktop/Dialogs/LanguageSelectionDialog.axaml","src/PTenoLauncher.Desktop/Dialogs/LanguageSelectionDialog.axaml.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.Onboarding.cs","tests/PTenoLauncher.Tests/LanguageOnboardingTests.cs","docs/FIRST_RUN_LANGUAGE.md","docs/FIRST_RUN_LANGUAGE_CHECKPOINT.md"}
            if not language_files.issubset(names):raise ValueError("First-run language source checkpoint is incomplete")
            java_files={"src/PTenoLauncher.Infrastructure/Minecraft/TemurinPackage.cs","src/PTenoLauncher.Infrastructure/Minecraft/ManagedJavaValidation.cs","tests/PTenoLauncher.Tests/JavaRuntimeIntegrityTests.cs","tests/PTenoLauncher.IntegrationSmoke/LiveJavaMatrix.cs","docs/JAVA_RUNTIME_VERIFICATION.md","docs/JAVA_RUNTIME_CHECKPOINT.md"}
            if not java_files.issubset(names):raise ValueError("Java runtime source checkpoint is incomplete")
            historical_files={"src/PTenoLauncher.Infrastructure/Minecraft/AssetIndexPlan.cs","src/PTenoLauncher.Infrastructure/Minecraft/AssetManager.cs","src/PTenoLauncher.Infrastructure/Minecraft/LaunchArgumentExpansion.cs","tests/PTenoLauncher.Tests/HistoricalAssetTests.cs","tests/PTenoLauncher.IntegrationSmoke/LiveHistoricalAssets.cs","docs/HISTORICAL_ASSETS_CHECKPOINT.md"}
            if not historical_files.issubset(names):raise ValueError("Historical assets source checkpoint is incomplete")
            forge_files={"src/PTenoLauncher.Infrastructure/Minecraft/ForgeCatalog.cs","src/PTenoLauncher.Infrastructure/Minecraft/ForgeMaven.cs","src/PTenoLauncher.Infrastructure/Minecraft/ForgeInstallerMetadata.cs","src/PTenoLauncher.Infrastructure/Minecraft/ForgeComponentPreparer.cs","src/PTenoLauncher.Desktop/ViewModels/MainViewModel.Forge.cs","src/PTenoLauncher.Desktop/Localization/TextCatalog.Forge.cs","tests/PTenoLauncher.Tests/ForgeComponentTests.cs","tests/PTenoLauncher.IntegrationSmoke/LiveForgeComponents.cs","docs/FORGE_COMPONENTS_CHECKPOINT.md"}
            if not forge_files.issubset(names):raise ValueError("Forge component source checkpoint is incomplete")
            forge_execution_files={"src/PTenoLauncher.Infrastructure/Minecraft/ForgeInstaller.cs","src/PTenoLauncher.Infrastructure/Minecraft/ForgeProcessorPlan.cs","src/PTenoLauncher.Infrastructure/Minecraft/ForgeProcessorHost.cs","tests/PTenoLauncher.Tests/ForgeExecutionTests.cs","tests/PTenoLauncher.Tests/MavenQualifierTests.cs","tests/PTenoLauncher.IntegrationSmoke/LiveForgeInstallation.cs","docs/FORGE_EXECUTION_CHECKPOINT.md"}
            if not forge_execution_files.issubset(names):raise ValueError("Forge execution source checkpoint is incomplete")
            native_nested_files={"src/PTenoLauncher.Core/QuiltSemanticOrder.cs","src/PTenoLauncher.Infrastructure/Mods/LocalModManager.NativeNested.cs","src/PTenoLauncher.Infrastructure/Mods/NativeNestedResolver.cs","src/PTenoLauncher.Infrastructure/Mods/NativeNestedResolver.Quilt.cs","tests/PTenoLauncher.Tests/NativeNestedModTests.cs","tests/PTenoLauncher.IntegrationSmoke/LiveNativeNestedArtifact.cs","docs/NATIVE_NESTED_CHECKPOINT.md","docs/NATIVE_NESTED_SPEC_REFERENCES.json"}
            if not native_nested_files.issubset(names):raise ValueError("Native nested source checkpoint is incomplete")
        archives.append({"path":name,"entries":len(names),"crc":"PASS","privateDataExcluded":True})
portable_smoke=None
if args.smoke:
    if os.name!="nt":raise RuntimeError("Native portable smoke requires Windows")
    if portable_name is None:raise RuntimeError("Missing portable archive")
    parent=args.smoke_root
    parent.mkdir(exist_ok=True)
    if parent.resolve(strict=True)!=parent or parent.is_symlink() or parent.is_junction():
        raise RuntimeError("Portable smoke parent is redirected")
    run=uuid.uuid4().hex
    portable_directory=parent/("portable-preview-"+run)
    portable_directory.mkdir()
    ownership=portable_directory/".owned-smoke"
    ownership.write_text(run,encoding="ascii")
    try:
        with zipfile.ZipFile(artifacts/portable_name) as archive:
            archive.extractall(portable_directory)
        launcher=portable_directory/"PTenoLauncher.exe"
        child=subprocess.run([str(launcher),"--smoke-test","--skip-update-check"],cwd=portable_directory,capture_output=True,text=True,timeout=60)
        if child.returncode:raise RuntimeError("Portable exited with "+str(child.returncode))
        marker=(portable_directory/"portable-data/gui-smoke.txt").read_text(encoding="utf8")
        if "Window opened" not in marker or "initialized=True" not in marker:raise RuntimeError("Portable did not initialize")
        portable_smoke={"exitCode":child.returncode,"marker":marker,"directory":str(portable_directory),
                        "exeSha256":hashlib.sha256(launcher.read_bytes()).hexdigest(),
                        "appSha256":hashlib.sha256((portable_directory/"PTenoLauncher.dll").read_bytes()).hexdigest()}
    finally:
        if portable_directory.resolve(strict=True)!=portable_directory or ownership.is_symlink() or ownership.read_text(encoding="ascii")!=run:
            raise RuntimeError("Portable smoke ownership check failed")
        pending=[portable_directory]
        size=0
        while pending:
            candidate=pending.pop()
            if candidate.is_symlink() or candidate.is_junction():
                raise RuntimeError("Portable smoke payload contains a redirected path")
            if candidate.is_dir(): pending.extend(candidate.iterdir())
            else: size+=candidate.stat().st_size
        shutil.rmtree(portable_directory)
        if portable_smoke is not None: portable_smoke["removedBytes"]=size
result={"checksums":records,"archives":archives,"portableSmoke":portable_smoke,"gameLaunched":False}
(artifacts/"final-artifact-verification.json").write_text(json.dumps(result,indent=2),encoding="utf8")
print(json.dumps(result,indent=2))
