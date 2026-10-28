"""Collect dependency license metadata and available license texts from restored packages."""
import json, shutil, xml.etree.ElementTree as ET
from pathlib import Path

root=Path(__file__).resolve().parents[1]
assets=json.loads((root/"src/PTenoLauncher.Desktop/obj/project.assets.json").read_text(encoding="utf-8"))
packages=root/".local/nuget"
destination=root/"docs/licenses"
rows=[]
for identity, data in sorted(assets["libraries"].items()):
    if data["type"]!="package":
        continue
    name, version=identity.rsplit("/",1)
    package=packages/name.lower()/version
    nuspec=next(package.glob("*.nuspec"),None)
    if nuspec is None:
        rows.append(f"| {name} | {version} | metadata unavailable |")
        continue
    tree=ET.parse(nuspec)
    metadata=next(x for x in tree.getroot() if x.tag.split("}")[-1]=="metadata")
    fields={x.tag.split("}")[-1]:x for x in metadata}
    license_node=fields.get("license")
    license_text=(license_node.text or "").strip() if license_node is not None else "see upstream licenseUrl"
    rows.append(f"| {name} | {version} | {license_text.replace('|','/')} |")
    candidates=[p for p in package.iterdir() if p.is_file() and any(t in p.name.lower() for t in ("license","licence","notice","copying"))]
    if license_node is not None and license_node.get("type")=="file":
        candidate=(package/license_text).resolve()
        if package.resolve() in candidate.parents and candidate.is_file(): candidates.append(candidate)
    for source in set(candidates):
        target=destination/(name+"-"+version)/source.name
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,target)
# The .NET SDK distributes its runtime license and consolidated notices.
for filename in ("LICENSE.txt","ThirdPartyNotices.txt"):
    source=root/".tools/dotnet"/filename
    if source.is_file():
        target=destination/"dotnet"/filename
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,target)
text="# Third-party notices\n\nDependency metadata from restored NuGet packages. Original available license texts are in docs/licenses (ThirdPartyLicenses in binary packages).\nMinecraft game files are not redistributed. The PTenoLauncher application license has not yet been selected by its owner.\n\n| Package | Version | Declared license |\n| --- | --- | --- |\n"+"\n".join(rows)+"\n"
(root/"THIRD_PARTY_NOTICES.md").write_text(text,encoding="utf-8")
print(f"Collected license metadata for {len(rows)} packages.")
