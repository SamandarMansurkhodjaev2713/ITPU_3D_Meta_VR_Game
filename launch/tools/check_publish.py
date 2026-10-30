"""Read-only, conservative first-publication check for ITPU. NEVER pushes.

Use on a trusted local checkout after reading this file. This checks Git's resolved
origin and reviewed Git blobs, not the network transport or the user's identity.
No hook is installed. No configuration, index or working file is changed.
"""
from __future__ import annotations
import argparse, hashlib, json, os, re, subprocess, sys
from pathlib import Path, PurePosixPath
from urllib.parse import urlsplit

REPOSITORY = 'SamandarMansurkhodjaev2713/ITPU_3D_Meta_VR_Game'
MAX_BLOB = 2_000_000
SAFE_NAMES = {'.gitignore', '.gitattributes', '.editorconfig'}
SAFE_EXT = {'.md','.txt','.json','.toml','.yaml','.yml','.py','.cs','.csproj','.sln',
            '.asmdef','.asmref','.js','.cjs','.mjs','.ps1','.sh','.hlsl','.shader',
            '.shadergraph','.compute','.uss','.uxml','.meta','.prefab','.unity','.asset'}
DENIED_PARTS = {'references','private','.git','.ssh','node_modules','.venv','library',
                'temp','logs','obj','usersettings','builds','recordings','__pycache__'}
EMBEDDED_MEDIA = re.compile(rb'data:(?:image|audio|video)/[\w.+-]+(?:;[^,\s]*)?;base64,[A-Za-z0-9+/]{32,}', re.I)
TOKEN = re.compile(rb'(?:ghp_|github_pat_|sk-proj-)[A-Za-z0-9_-]{24,}')
KEY = re.compile(rb'-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----')

class Rejected(ValueError): pass

def allowed_url(value: str) -> bool:
    if not isinstance(value,str) or value != value.strip() or re.search(r'[\s\\%?#]',value): return False
    if value.startswith('git@github.com:'):
        tail=value[len('git@github.com:'):]
    else:
        try:
            u=urlsplit(value)
            if u.hostname != 'github.com' or u.query or u.fragment: return False
            if u.scheme=='https':
                if u.username is not None or u.password is not None or u.port not in (None,443): return False
            elif u.scheme=='ssh':
                if u.username!='git' or u.password is not None or u.port not in (None,22): return False
            else:return False
            tail=u.path.removeprefix('/')
        except ValueError:return False
    if tail.endswith('.git'):tail=tail[:-4]
    return tail.casefold()==REPOSITORY.casefold()

def git(repo:Path,*args:str, optional=False) -> bytes:
    # Do not echo stderr/config values: they can contain personal paths or URLs.
    if any(k in os.environ for k in ('GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_CONFIG_COUNT')):
        raise Rejected('Git override environment requires separate review')
    env=os.environ.copy();env.update(GIT_TERMINAL_PROMPT='0',GIT_OPTIONAL_LOCKS='0',GIT_NO_REPLACE_OBJECTS='1')
    p=subprocess.run(['git','-c','core.fsmonitor=false','-C',str(repo),*args],capture_output=True,env=env,timeout=30)
    if optional and p.returncode==1:return b''
    if p.returncode:raise Rejected('Git read failed: '+args[0]+'; inspect locally without disclosing credentials')
    return p.stdout

def check_origin(repo:Path) -> dict:
    if git(repo,'rev-parse','--is-inside-work-tree').strip()!=b'true':raise Rejected('Not a working tree')
    for kind,args in [('fetch',('remote','get-url','--all','origin')),
                      ('push',('remote','get-url','--push','--all','origin'))]:
        values=git(repo,*args).decode('utf-8').splitlines()
        if len(values)!=1 or not allowed_url(values[0]):raise Rejected('origin '+kind+' is not the single allowed repository')
    if git(repo,'config','--bool','--get','remote.origin.mirror',optional=True).strip()==b'true':
        raise Rejected('Mirror push configuration is prohibited')
    if git(repo,'config','--get-all','remote.origin.push',optional=True).strip():
        raise Rejected('Implicit push refspecs need removal/review; use an explicit branch')
    push_default=git(repo,'config','--get','remote.pushDefault',optional=True).strip()
    if push_default not in (b'',b'origin'):raise Rejected('Different default push remote')
    branch=git(repo,'symbolic-ref','--quiet','--short','HEAD',optional=True).decode().strip()
    if branch:
        push_remote=git(repo,'config','--get',f'branch.{branch}.pushRemote',optional=True).strip()
        if push_remote not in (b'',b'origin'):raise Rejected('Different branch push remote')
    # Default LFS endpoint derives from origin. Any custom endpoint requires its own audit.
    custom_lfs=git(repo,'config','--name-only','--get-regexp',r'^(lfs\.(url|pushurl)|remote\..*\.lfs(push)?url)$',optional=True)
    if custom_lfs.strip():raise Rejected('Custom LFS endpoint requires explicit validation')
    if (repo/'.lfsconfig').exists():raise Rejected('.lfsconfig requires an explicit endpoint audit')
    return {'repository':REPOSITORY,'branch':branch or None,'result':'ALLOWED_LOCAL_ORIGIN',
            'not_proven':['server visibility','authorization','GitHub delivery','SSH configuration or HTTP redirects']}

def safe_path(path:str) -> bool:
    if not isinstance(path,str) or not path or '\\' in path or ':' in path or any(ord(c)<32 for c in path):return False
    p=PurePosixPath(path)
    if p.is_absolute() or '..' in p.parts or '.' in path.split('/') or '//' in path:return False
    low=[x.casefold() for x in p.parts]
    if any(x in DENIED_PARTS for x in low):return False
    if len(low)>=2 and low[0] in ('reports','state') and low[1]=='local':return False
    if p.name.casefold().startswith('.env') or p.name in {'.gitmodules','.lfsconfig'}:return False
    if p.name.casefold().startswith(('review_bundle','product_reader','private_content_manifest')):return False
    return p.name in SAFE_NAMES or p.suffix.casefold() in SAFE_EXT

def public_content_problem(blob:bytes) -> str | None:
    if len(blob)>MAX_BLOB:return 'oversized source; separate audit required'
    if b'\x00' in blob:return 'binary content'
    try:blob.decode('utf-8')
    except UnicodeDecodeError:return 'non-UTF8 content'
    if blob.startswith(b'version https://git-lfs.github.com/spec/v1'):return 'LFS pointer is not an inspected public asset'
    if EMBEDDED_MEDIA.search(blob):return 'embedded image/audio/video'
    if TOKEN.search(blob) or KEY.search(blob):return 'possible secret'
    return None

def load_manifest(path:Path) -> set[tuple[str,str]]:
    d=json.loads(path.read_text(encoding='utf-8'))
    if type(d.get('schema_version')) is not int or d['schema_version']!=1 or d.get('repository')!=REPOSITORY:
        raise Rejected('Invalid public review manifest')
    rows=d.get('reviewed_blobs')
    if not isinstance(rows,list) or not rows:raise Rejected('No reviewed public blobs; do not auto-approve every file')
    allowed=set()
    for row in rows:
        if not isinstance(row,dict):raise Rejected('Invalid review row')
        p=row.get('path');h=row.get('sha256')
        if not safe_path(p) or not isinstance(h,str) or not re.fullmatch('[0-9a-f]{64}',h):raise Rejected('Invalid reviewed path/hash')
        if row.get('classification')!='PUBLIC_SOURCE_REVIEWED' or not str(row.get('review_note','')).strip():raise Rejected('Missing content review')
        if (p,h) in allowed:raise Rejected('Duplicate review row')
        allowed.add((p,h))
    return allowed

def rows_index(repo:Path):
    for row in git(repo,'ls-files','--stage','-z').split(b'\0'):
        if not row:continue
        info,name=row.split(b'\t',1);mode,oid,stage=info.decode().split()
        if stage!='0':raise Rejected('Unmerged index')
        yield mode,oid,name.decode('utf-8')

def rows_tree(repo:Path,rev:str):
    for row in git(repo,'ls-tree','-r','-z','--full-tree',rev).split(b'\0'):
        if not row:continue
        info,name=row.split(b'\t',1);mode,kind,oid=info.decode().split()
        if kind!='blob':raise Rejected('Submodule or non-blob entry is not allowed by first-publication policy')
        yield mode,oid,name.decode('utf-8')

def scan(repo:Path, manifest:Path, history:bool=False) -> dict:
    allowed=load_manifest(manifest);checked=set();errors=[];count=0
    rows=list(rows_index(repo))
    if history:
        if git(repo,'rev-parse','--is-shallow-repository').strip()==b'true':raise Rejected('Shallow history is not fully auditable')
        revisions=git(repo,'rev-list','--max-count=1001','HEAD').decode().splitlines()
        if len(revisions)>1000:raise Rejected('History budget exceeded; targeted documented review required')
        for revision in revisions:rows.extend(rows_tree(repo,revision))
    for mode,oid,path in rows:
        key=(mode,oid,path)
        if key in checked:continue
        checked.add(key);count+=1
        if mode not in ('100644','100755') or not safe_path(path):
            errors.append({'path':path,'reason':'restricted path/type; stays local'});continue
        size=int(git(repo,'cat-file','-s',oid))
        if size>MAX_BLOB:errors.append({'path':path,'reason':'oversized blob'});continue
        blob=git(repo,'cat-file','blob',oid)
        problem=public_content_problem(blob)
        digest=hashlib.sha256(blob).hexdigest()
        if problem:errors.append({'path':path,'reason':problem})
        elif (path,digest) not in allowed:errors.append({'path':path,'reason':'this exact Git blob has not been reviewed'})
    return {'status':'PASS' if not errors else 'FAIL','inspected_unique_git_entries':count,
            'index_and_reachable_history':history,'errors':errors,
            'scope':'Conservative public SOURCE-only policy; no publish performed, not a complete secret detector.'}

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--repo',type=Path,required=True)
    ap.add_argument('--manifest',type=Path);ap.add_argument('--history',action='store_true')
    a=ap.parse_args()
    try:
        r={'origin':check_origin(a.repo.resolve()),'published':False}
        if a.manifest:r['content']=scan(a.repo.resolve(),a.manifest,a.history)
        elif a.history:raise Rejected('--history requires --manifest')
        r['status']='FAIL' if r.get('content',{}).get('status')=='FAIL' else 'PASS'
    except (Rejected,OSError,ValueError,KeyError,TypeError,subprocess.TimeoutExpired) as e:
        r={'status':'FAIL','reason':str(e),'published':False}
    print(json.dumps(r,ensure_ascii=False,indent=2));return 0 if r['status']=='PASS' else 1
if __name__=='__main__':sys.exit(main())
