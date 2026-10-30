"""Independent local validation of actual GLB/OBJ using an already installed trimesh.
No installation; do not use this result as a Unity/Blender/Quest acceptance.
"""
from pathlib import Path
import hashlib,json,sys
ROOT=Path(__file__).resolve().parents[1]
def main():
 try:import trimesh
 except ImportError:
  print('NOT_RUN: trimesh not installed; no automatic installation');return 2
 path=ROOT/'art_pipeline/environment_v5';results=[]
 for name in ['ITPU_Space_cutaway.glb','ITPU_Space_full.glb','ITPU_Space.obj']:
  scene=trimesh.load(path/name,force='scene');n=sum(len(g.faces)for g in scene.geometry.values());bad=[k for k,g in scene.geometry.items()if not g.is_winding_consistent or float(g.volume)<=0]
  row={'file':name,'loaded':True,'geometry_count':len(scene.geometry),'triangles':n,'bounds':scene.bounds.tolist(),'winding_or_volume_errors':bad,'sha256':hashlib.sha256((path/name).read_bytes()).hexdigest()};results.append(row)
  if bad:raise ValueError(str(row))
 glb=trimesh.load(path/'ITPU_Space_cutaway.glb',force='scene');obj=trimesh.load(path/'ITPU_Space.obj',force='scene')
 assert sum(len(g.faces)for g in glb.geometry.values())==sum(len(g.faces)for g in obj.geometry.values())
 r={'checks':results,'trimesh_version':trimesh.__version__,'engine_status':'NOT_RUN_BLENDER_UNITY_QUEST','same_triangle_count_obj_glb':True}
 dest=ROOT/'reports/environment_v5/independent_import.json';dest.parent.mkdir(exist_ok=True,parents=True);dest.write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2));return 0
if __name__=='__main__':sys.exit(main())
