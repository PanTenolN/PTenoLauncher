"""Opt-in real Windows installer lifecycle, isolated app/data; refuses an existing installation."""
import argparse, hashlib, json, os, sqlite3, subprocess, time, uuid, winreg
from datetime import datetime, timezone
from pathlib import Path
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument("--setup",type=Path,default=root/"artifacts/PTenoLauncher-Setup-0.2.0-win-x64.exe")
parser.add_argument("--older-setup",type=Path)
args=parser.parse_args()
uninstall_key=r"Software\Microsoft\Windows\CurrentVersion\Uninstall\{CF2E0B60-B0F5-4E74-9481-E0CE4ABBE030}_is1"
app_key=r"Software\PTenoLauncher"
def regvalue(key,value,hive=winreg.HKEY_CURRENT_USER):
    try:
        with winreg.OpenKey(hive,key,0,winreg.KEY_READ|winreg.KEY_WOW64_64KEY) as k: return winreg.QueryValueEx(k,value)[0]
    except FileNotFoundError: return None
try:
    existing=regvalue(uninstall_key,"UninstallString") or regvalue(uninstall_key,"UninstallString",winreg.HKEY_LOCAL_MACHINE) or regvalue(app_key,"UninstallDataRoot")
except PermissionError as error:
    blocked={"state":"blocked","phase":"registry-preflight","error":str(error),"installationStarted":False,"systemChanges":False}
    (root/"artifacts/0.2.0/installer-lifecycle-blocked.json").write_text(json.dumps(blocked,indent=2),encoding="utf8")
    print(json.dumps(blocked,indent=2),flush=True)
    raise SystemExit(77)
if existing:
    raise SystemExit("An existing installation/registered data root exists. Use a disposable Windows account; no existing installation was modified.")
stamp=datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")+"-"+uuid.uuid4().hex[:6]
workspace=(root/".local"/("installer-lifecycle-"+stamp)).resolve()
assert workspace.is_relative_to((root/".local").resolve())
workspace.mkdir(parents=True)
application=workspace/"application"
data=workspace/"PTenoLauncher"
results=[]
report=root/"artifacts/0.2.0"/("installer-lifecycle-"+stamp+".json")
def record(name,**details):
    results.append({"check":name,"result":"PASS",**details})
    report.write_text(json.dumps({"workspace":str(workspace),"checks":results},indent=2),encoding="utf8")
    print(name+": PASS",flush=True)
def run(exe,argv,label):
    proc=subprocess.run([str(exe),*map(str,argv)],cwd=root,timeout=90,capture_output=True,text=True)
    (workspace/(label+".stdout.log")).write_text(proc.stdout+proc.stderr,encoding="utf8")
    if proc.returncode: raise RuntimeError(label+" exit code "+str(proc.returncode))
def install(setup,label):
    run(setup.resolve(),["/CURRENTUSER","/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/NOICONS","/TASKS=","/DIR="+str(application),"/INTEGRATIONDATAROOT="+str(data),"/LOG="+str(workspace/(label+".log"))],label)
    assert (application/"PTenoLauncher.exe").is_file()
    assert Path(regvalue(app_key,"UninstallDataRoot")).resolve()==data
def smoke(label):
    run(application/"PTenoLauncher.exe",["--data-root",data,"--smoke-test"],label)
    assert "Window opened" in (data/"gui-smoke.txt").read_text(encoding="utf8")
def dbcheck():
    with sqlite3.connect(data/"settings.db") as db:
        assert db.execute("SELECT name FROM profiles WHERE id=?",(profile_id,)).fetchone()[0]=="Synthetic installer profile"
        assert db.execute("SELECT value FROM settings WHERE key='installer.synthetic'").fetchone()[0]=="preserve"
    db.close()
def preserved():
    assert world.read_bytes()==b"SYNTHETIC TEST DATA - NOT A REAL MINECRAFT WORLD"
    assert backup.read_bytes()==b"SYNTHETIC BACKUP"
    assert unrelated.read_text()=="Leave unknown root files alone"
    dbcheck()
def uninstall(purge,label):
    argv=["/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/LOG="+str(workspace/(label+".log"))]
    if purge: argv+=["/PURGEUSERDATA=1"]
    uninstaller=Path(regvalue(uninstall_key,"UninstallString").strip('"')).resolve()
    assert uninstaller.parent==application.resolve() and uninstaller.name.startswith("unins")
    run(uninstaller,argv,label)
    deadline=time.monotonic()+30
    log=workspace/(label+".log")
    while time.monotonic()<deadline:
        if log.exists() and "Log closed." in log.read_text(encoding="utf-8-sig",errors="replace") and not regvalue(uninstall_key,"UninstallString"): break
        time.sleep(.2)
    else: raise RuntimeError("Uninstaller helper did not finish")
    assert not (application/"PTenoLauncher.exe").exists()
    assert regvalue(uninstall_key,"UninstallString") is None
    assert regvalue(app_key,"UninstallDataRoot") is None
try:
    install(args.older_setup or args.setup,"initial-install")
    smoke("initial-gui")
    profile_id=uuid.uuid4().hex
    with sqlite3.connect(data/"settings.db") as db:
        db.execute("INSERT INTO profiles VALUES(?,?,?,?,?,?,?,?)",(profile_id,"Synthetic installer profile","1.21.1",0,2048,None,datetime.now(timezone.utc).isoformat(),0))
        db.execute("INSERT INTO settings VALUES('installer.synthetic','preserve')")
    db.close()
    world=data/"instances"/profile_id/"game/saves/synthetic-world/sentinel.dat"
    world.parent.mkdir(parents=True);world.write_bytes(b"SYNTHETIC TEST DATA - NOT A REAL MINECRAFT WORLD")
    backup=data/"backups/synthetic-backup.zip";backup.write_bytes(b"SYNTHETIC BACKUP")
    unrelated=data/"unrelated-note.txt";unrelated.write_text("Leave unknown root files alone")
    record("initial-per-user-install-and-gui",version=regvalue(uninstall_key,"DisplayVersion"))
    install(args.setup,"upgrade-install");preserved();smoke("upgraded-gui")
    with sqlite3.connect(data/"settings.db") as db: assert db.execute("PRAGMA user_version").fetchone()[0]==2
    db.close()
    assert regvalue(uninstall_key,"DisplayVersion")=="0.2.0"
    record("upgrade-preserves-profile-settings-world-backup",olderPayload=bool(args.older_setup),schema=2)
    install(args.setup,"reinstall");preserved()
    record("same-version-reinstall-preserves-data")
    uninstall(False,"default-uninstall");preserved()
    record("default-uninstall-preserves-data-removes-registration-and-app")
    install(args.setup,"cleanup-reinstall");preserved()
    uninstall(True,"opt-in-cleanup")
    assert not (data/"settings.db").exists()
    assert not (data/"instances").exists()
    assert not (data/"backups").exists()
    assert not (data/"pteno-managed-data.marker").exists()
    assert unrelated.read_text()=="Leave unknown root files alone"
    record("opt-in-cleanup-removes-owned-data-preserves-unknown")
    print(json.dumps({"report":str(report),"checks":len(results),"actualGameLaunched":False},indent=2))
except Exception as error:
    results.append({"check":"lifecycle","result":"FAIL","error":str(error)})
    report.write_text(json.dumps({"workspace":str(workspace),"checks":results},indent=2),encoding="utf8")
    raise
