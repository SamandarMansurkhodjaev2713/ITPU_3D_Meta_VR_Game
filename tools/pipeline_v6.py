"""Bounded CHAT-source preparation runner. Never installs, starts editors or accesses devices.
No command from JSON is executed. Only the in-code allowlisted steps below can run.
Cache requires matching inputs AND verified outputs. Exits nonzero on missing output.
"""
from __future__ import annotations
import argparse,hashlib,json,os,subprocess,sys,time
from pathlib import Path
from packlib import ROOT,resolve_inside,sha256,write_json
STEPS={
 'geometry':{'cmd':['tools/build_space_v6.py'],'inputs':['tools/build_space_v6.py','tools/build_space_v5.py','art_pipeline/generators/recipe_plan.py','data/pbr_materials.json','data/space_parameters_v5.json','data/space_layout_v5.json'],'outputs':['art_pipeline/environment_v6/scene_snapshot.json','art_pipeline/environment_v6/ITPU_Space_full.glb','art_pipeline/environment_v6/ITPU_Space_cutaway.glb','art_pipeline/environment_v6/ITPU_Space.obj','art_pipeline/environment_v6/ITPU_Space.mtl','art_pipeline/environment_v6/scene_recipe.json','art_pipeline/environment_v6/textures/oak_base.png','art_pipeline/environment_v6/textures/oak_mr.png','art_pipeline/environment_v6/textures/oak_urp_mask.png','art_pipeline/environment_v6/textures/fabric_base.png','art_pipeline/environment_v6/textures/fabric_mr.png','art_pipeline/environment_v6/textures/fabric_urp_mask.png','art_pipeline/environment_v6/textures/warm_white_base.png','art_pipeline/environment_v6/textures/warm_white_mr.png','art_pipeline/environment_v6/textures/warm_white_urp_mask.png','art_pipeline/environment_v6/unity_transfer_manifest.json','art_pipeline/environment_v6/viewer_snapshot.json','art_pipeline/environment_v6/geometry_report.json']},
 'space_checks':{'cmd':['tools/check_space_v6.py'],'inputs':['tools/check_space_v6.py','tools/build_space_v6.py','art_pipeline/environment_v6/scene_snapshot.json','art_pipeline/environment_v6/scene_recipe.json'],'outputs':['art_pipeline/environment_v6/validation_report.json']},
 'lab':{'cmd':['tools/build_logic_lab.py'],'inputs':['tools/build_logic_lab.py','prototype/LOGIC_LAB.template.html','src/reference/runtime.js','src/reference/production.js'],'outputs':['prototype/LOGIC_LAB.html']},
 'viewer':{'cmd':['tools/build_space_viewer_v6.py'],'inputs':['tools/build_space_viewer_v6.py','art_pipeline/environment_v6/viewer_snapshot.json','data/pbr_materials.json'],'outputs':['SPACE_PREVIEW.html']},
 'reader':{'cmd':['tools/build_reader.py'],'inputs':['tools/build_reader.py','config/document_order.json','state/project_state.json','data/requirements.json','data/backlog.json','data/test_cases.json'],'outputs':['PRODUCT_READER.html','PRODUCT_SPEC.md','PROGRESS.md','TRACEABILITY.md']}
}

def safe(root,rel):
 p=resolve_inside(root,rel)
 # Refuse even internal symlinks, not merely escaping links.
 q=root
 for part in Path(rel).parts:
  q=q/part
  if q.is_symlink():raise ValueError('Symlink rejected: '+rel)
 return p

def fingerprints(root,paths):
 return {r:sha256(safe(root,r))for r in sorted(set(paths))}

def cache_valid(root,receipt,input_hashes,outputs):
 if not isinstance(receipt,dict):return False
 if receipt.get('status')!='PASS'or receipt.get('inputs')!=input_hashes:return False
 try:return receipt.get('outputs')==fingerprints(root,outputs)
 except(FileNotFoundError,ValueError):return False

class Lock:
 def __init__(self,path):self.path=path
 def __enter__(self):
  self.path.parent.mkdir(parents=True,exist_ok=True)
  try:self.fd=os.open(self.path,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
  except FileExistsError:raise RuntimeError('Workspace busy or stale lock; inspect process before manual removal')
  os.write(self.fd,f'{os.getpid()}\n'.encode());return self
 def __exit__(self,*exc):os.close(self.fd);self.path.unlink(missing_ok=True)

def run_step(name,root=ROOT,dry=False,force=False):
 if name not in STEPS:raise ValueError('Step not allowlisted')
 spec=STEPS[name];paths=list(spec['inputs'])
 paths += ['tools/pipeline_v6.py','tools/packlib.py','state/project_state.json']
 if name=='geometry':paths += [p.relative_to(root).as_posix()for p in(root/'art_pipeline/generators').glob('*.py')] + ['tools/pbr_contract.py']
 if name in ('viewer','space_checks'):paths += ['tools/build_space_v5.py']
 if name=='reader':
  paths += [p.relative_to(root).as_posix()for p in(root/'docs/product').glob('*.md')]
  paths += [p.relative_to(root).as_posix()for p in(root/'references/curated').glob('*.jpg')] if (root/'references/curated').exists()else []
 hashes=fingerprints(root,paths)
 outputs=[safe(root,p)for p in spec['outputs']]
 report=safe(root,'reports/local/pipeline_v6/'+name+'.json');log=safe(root,'reports/local/pipeline_v6/'+name+'.log')
 if dry:return {'step':name,'status':'DRY_RUN','command':[sys.executable,*spec['cmd']],'outputs':spec['outputs'],'input_count':len(hashes)}
 if report.exists()and not force:
  try:old=json.loads(report.read_text())
  except (ValueError,OSError):old={}  # incomplete receipt never grants a cache hit
  if cache_valid(root,old,hashes,spec['outputs']):return {'step':name,'status':'CACHED','receipt':report.relative_to(root).as_posix()}
 report.parent.mkdir(parents=True,exist_ok=True);start=time.monotonic()
 result={'step':name,'status':'FAIL','inputs':hashes,'command':[sys.executable,*spec['cmd']],'outputs':{},'elapsed_seconds':0}
 try:
  with log.open('w',encoding='utf8')as f:
   proc=subprocess.run([sys.executable,*spec['cmd']],cwd=root,stdout=f,stderr=subprocess.STDOUT,timeout=120,check=False)
  if proc.returncode:raise RuntimeError('Command exit '+str(proc.returncode))
  if fingerprints(root,paths)!=hashes:raise RuntimeError('Input changed while executing step')
  result['outputs']=fingerprints(root,spec['outputs']);result['status']='PASS'
 except(Exception)as e:result['error']=str(e)
 result['elapsed_seconds']=round(time.monotonic()-start,3);write_json(report,result)
 if result['status']!='PASS':raise RuntimeError(result.get('error','Missing output')+'; log: '+log.relative_to(root).as_posix())
 return {'step':name,'status':'PASS','receipt':report.relative_to(root).as_posix()}

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--step',choices=[*STEPS,'all'],default='all');p.add_argument('--dry-run',action='store_true');p.add_argument('--force',action='store_true');a=p.parse_args()
 state=json.loads((ROOT/'state/project_state.json').read_text(encoding='utf-8'))
 if not state.get('source_preparation_authorized'):raise SystemExit('Source preparation not authorized')
 names=list(STEPS)if a.step=='all'else[a.step]
 if a.dry_run:print(json.dumps([run_step(n,dry=True)for n in names],ensure_ascii=False,indent=2));return
 with Lock(safe(ROOT,'reports/local/pipeline_v6/workspace.lock')):
  for n in names:print(json.dumps(run_step(n,force=a.force),ensure_ascii=False))
if __name__=='__main__':main()
