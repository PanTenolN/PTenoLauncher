"""Run an opt-in real integration check with durable streaming logs and a completion record."""
import argparse,json,os,subprocess,sys
from datetime import datetime,timezone
from pathlib import Path
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument("--check",choices=["quilt-mod-inspection","quilt-offline","quilt","fabric","fabric-mods","mods-offline","local-deps-offline","optional-offline","modrinth","java","metadata"],required=True)
parser.add_argument("--root",type=Path,required=True)
parser.add_argument("--cached-mods-root",type=Path)
args=parser.parse_args()
data=args.root.resolve()
if not data.is_relative_to((root/".local").resolve()):raise SystemExit("Live tests require a private .local directory.")
env=os.environ.copy()
env.update(APPDATA=str(root/".local/appdata"),USERPROFILE=str(root/".local/user"),LOCALAPPDATA=str(root/".local/localappdata"),
    DOTNET_CLI_HOME=str(root/".local/cli"),NUGET_PACKAGES=str(root/".local/nuget"),DOTNET_ROOT=str(root/".tools/dotnet"),
    DOTNET_CLI_TELEMETRY_OPTOUT="1",AVALONIA_TELEMETRY_OPTOUT="1",MSBUILDDISABLENODEREUSE="1")
log=root/"artifacts/0.2.0"/("live-"+args.check+".log")
status=log.with_suffix(".status.json")
status.write_text(json.dumps({"state":"running","started":datetime.now(timezone.utc).isoformat()}),encoding="utf8")
command=[str(root/".tools/dotnet/dotnet.exe"),str(root/"tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll"),str(data)]
if args.check!="metadata":command+=["--"+args.check]
if args.check in ("mods-offline","local-deps-offline","optional-offline"):
    if args.cached_mods_root is None or not args.cached_mods_root.resolve().is_relative_to((root/".local").resolve()):raise SystemExit("Offline mod check requires an existing private cache under .local.")
    command+=[str(args.cached_mods_root.resolve())]
with log.open("w",encoding="utf8") as output:
    child=subprocess.Popen(command,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding="utf8",errors="replace")
    for line in child.stdout:
        output.write(line);output.flush();sys.stdout.write(line);sys.stdout.flush()
    code=child.wait()
status.write_text(json.dumps({"state":"finished","exitCode":code,"completed":datetime.now(timezone.utc).isoformat(),"gameLaunched":False}),encoding="utf8")
raise SystemExit(code)
