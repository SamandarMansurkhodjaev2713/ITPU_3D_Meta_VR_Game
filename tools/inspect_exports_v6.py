"""Optional independent GLB/OBJ check. Requires an ALREADY installed trimesh/numpy.
Does not install dependencies, execute Blender/Unity or claim physical/visual acceptance.
"""
from __future__ import annotations
import argparse,hashlib,json
from pathlib import Path
from packlib import ROOT,resolve_inside

def inspect():
    import numpy as np
    import trimesh
    result=[]
    for name in ['ITPU_Space_full.glb','ITPU_Space_cutaway.glb','ITPU_Space.obj']:
        path=ROOT/'art_pipeline/environment_v6'/name
        scene=trimesh.load(path,force='scene',process=False);count=0
        for instance in scene.graph.nodes_geometry:
            transform,geometry=scene.graph[instance];mesh=scene.geometry[geometry]
            if not np.isfinite(mesh.vertices).all() or not np.isfinite(transform).all():raise ValueError('Nonfinite geometry')
            if mesh.faces.min()<0 or mesh.faces.max()>=len(mesh.vertices) or not np.all(mesh.area_faces>0):raise ValueError('Invalid faces')
            uv=getattr(mesh.visual,'uv',None)
            if uv is None or len(uv)!=len(mesh.vertices) or not np.isfinite(uv).all():raise ValueError('Missing UV0')
            count+=len(mesh.faces)
        result.append({'file':name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'parser':'trimesh '+trimesh.__version__,'geometry_count':len(scene.geometry),'mesh_instances':len(scene.graph.nodes_geometry),'triangles':count,'bounds':scene.bounds.tolist(),'status':'PARSED_NOT_ENGINE_TESTED'})
    return result

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--out',default='reports/local/independent_export_v6.json');a=p.parse_args()
    out=resolve_inside(ROOT,a.out)
    if out.exists():raise SystemExit('Refuse to overwrite an existing evidence file; choose a new path.')
    try:r=inspect()
    except ImportError as e:raise SystemExit('NOT_RUN: optional parser dependency is not installed: '+str(e))
    out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(r,indent=2)+'\n',encoding='utf8');print('Independent parser PASS; NOT Blender/Unity/Quest. Report: '+a.out)
if __name__=='__main__':main()
