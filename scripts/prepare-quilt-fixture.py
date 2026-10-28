"""Opt-in official Quilt artifacts for isolated tests when native .NET network access is unavailable.
This seeds only a private cache; it is not the production downloader or a game-launch test.
"""
import argparse,base64,hashlib,json,re,urllib.request,urllib.error
from pathlib import Path
from datetime import datetime,timezone
from urllib.parse import urlsplit
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--minecraft',default='1.21.1');args=parser.parse_args()
data=args.root.resolve()
local_root=(root/'.local').resolve()
if not local_root.is_relative_to(root.resolve()) or not data.is_relative_to(local_root):raise SystemExit('Fixture cache must be inside workspace .local')
if not re.fullmatch(r'[A-Za-z0-9._+-]{1,128}',args.minecraft):raise SystemExit('Invalid game version')
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,*a,**kw):return None
opener=urllib.request.build_opener(NoRedirect)
def get(url,maximum):
 u=urlsplit(url)
 if u.scheme!='https' or u.port not in (None,443) or u.username or u.password or not (u.hostname=='meta.quiltmc.org' and u.path.startswith('/v3/') or u.hostname=='maven.quiltmc.org' and u.path.startswith('/repository/release/') or u.hostname=='maven.fabricmc.net'):raise ValueError('Unapproved source')
 with opener.open(urllib.request.Request(url,headers={'User-Agent':'PTenoLauncher-test-fixture/0.2.0'}),timeout=30) as response:body=response.read(maximum+1)
 if len(body)>maximum:raise ValueError('Response too large')
 return body
def metadata(url,body):
 key=hashlib.sha256(url.encode()).hexdigest().upper();file=data/'cache/quilt/metadata'/(key+'.json');file.parent.mkdir(parents=True,exist_ok=True)
 envelope={'at':datetime.now(timezone.utc).isoformat(),'sha256':hashlib.sha256(body).hexdigest().upper(),'body':base64.b64encode(body).decode()}
 file.write_text(json.dumps(envelope),encoding='utf8')
def coordinate(value):
 p=value.split(':')
 if len(p) not in (3,4) or any(not re.fullmatch(r'[A-Za-z0-9._+-]{1,128}',s) or s in ('.','..') for s in p) or any(not s for s in p[0].split('.')):raise ValueError('Unsafe Maven coordinate')
 return p[0].replace('.','/')+'/'+p[1]+'/'+p[2]+'/'+p[1]+'-'+p[2]+('-'+p[3] if len(p)==4 else '')+'.jar'
url='https://meta.quiltmc.org/v3/versions/loader/'+args.minecraft
body=get(url,8*1024*1024);catalog=json.loads(body);metadata(url,body)
releases=[v['loader']['version'] for v in catalog if re.fullmatch(r'[0-9]+(?:\.[0-9]+){1,3}',v['loader']['version'])]
version=max(releases,key=lambda s:tuple(map(int,s.split('.'))))
url+='/' + version + '/profile/json';body=get(url,2*1024*1024);profile=json.loads(body);metadata(url,body)
if profile['inheritsFrom']!=args.minecraft or profile['mainClass']!='org.quiltmc.loader.impl.launch.knot.KnotClient':raise ValueError('Wrong profile')
if not 0<len(profile['libraries'])<=64:raise ValueError('Invalid library count')
artifacts=[]
for lib in profile['libraries']:
 relative=coordinate(lib['name']);origin=lib['url']
 if origin not in ('https://maven.fabricmc.net/','https://maven.quiltmc.org/repository/release/'):raise ValueError('Unapproved Maven root')
 url=origin+relative
 try:sidecar=get(url+'.sha512',1024);algorithm='sha512'
 except urllib.error.HTTPError as e:
  if e.code!=404:raise
  sidecar=get(url+'.sha256',1024);algorithm='sha256'
 expected=sidecar.decode('ascii').split()[0].lower()
 if not re.fullmatch('[0-9a-f]{'+str(128 if algorithm=='sha512' else 64)+'}',expected):raise ValueError('Invalid digest')
 metadata(url+'.'+algorithm,sidecar)
 content=get(url,32*1024*1024)
 if hashlib.new(algorithm,content).hexdigest()!=expected:raise ValueError('Bad library hash')
 path=data/'cache/libraries/quilt'/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content)
 artifacts.append({'name':lib['name'],'source':url,'bytes':len(content),'algorithm':algorithm,'hash':expected})
 print('Verified official fixture: '+lib['name'],flush=True)
report={'minecraft':args.minecraft,'quilt':version,'officialArtifacts':artifacts,'nativeDotnetOnlineVerified':False,'gameLaunched':False}
(root/'artifacts/0.2.0/quilt-official-fixture.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps({'quilt':version,'libraries':len(artifacts),'nativeDotnetOnlineVerified':False,'gameLaunched':False},indent=2))
