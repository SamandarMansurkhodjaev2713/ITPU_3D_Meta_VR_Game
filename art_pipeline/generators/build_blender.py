"""Run with approved Blender: blender --background --factory-startup --python build_blender.py -- --recipe FILE --output DIR [--allow-synthetic].
Creates .blend + selected FBX + plan/report. This source is NOT Blender-executed in the chat environment.
Do not run this in an open production .blend; use background factory startup.
"""
from __future__ import annotations
import argparse,json,math,sys,os
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from recipe_plan import build_plan

def srgb(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4

def build(recipe_path:Path,out:Path,allow_synthetic=False):
 import bpy
 import bmesh
 if not bpy.app.background or bpy.data.filepath:
  raise ValueError("Use a new background factory-startup process, not an existing .blend or GUI session")
 plan=build_plan(json.loads(recipe_path.read_text(encoding='utf-8')),allow_synthetic)
 # Outputs isolated in a newly created directory. Never wipe existing source.
 if out.exists()and any(out.iterdir()):raise ValueError('Output must be empty/new: never overwrite an approved asset')
 out.mkdir(parents=True,exist_ok=True)
 bpy.ops.wm.read_factory_settings(use_empty=True)
 scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.0
 collection=bpy.data.collections.new(plan['namespace']);scene.collection.children.link(collection)
 mats={}
 for name,m in plan['materials'].items():
  mat=bpy.data.materials.new('ITPU_'+name);mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF');rgba=tuple(srgb(int(m['srgb'][i:i+2],16)/255)for i in (0,2,4))+(1,)
  bsdf.inputs['Base Color'].default_value=rgba;bsdf.inputs['Roughness'].default_value=m['roughness'];bsdf.inputs['Metallic'].default_value=m['metallic'];mat.diffuse_color=rgba;mats[name]=mat
 def mesh(name,verts,faces):
  me=bpy.data.meshes.new(name+'_mesh');me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);collection.objects.link(o);return o
 made={};report=[]
 for spec in plan['objects']:
  shape=spec['shape'];name=spec['name'];size=spec.get('dimensions_m');o=None
  if shape=='empty':o=bpy.data.objects.new(name,None);collection.objects.link(o)
  elif shape in ('box','cylinder','sphere','torus'):
   if shape=='box':bpy.ops.mesh.primitive_cube_add(size=1)
   elif shape=='cylinder':bpy.ops.mesh.primitive_cylinder_add(vertices=spec['segments'],radius=.5,depth=1)
   elif shape=='sphere':bpy.ops.mesh.primitive_uv_sphere_add(segments=spec['segments'],ring_count=spec['rings'],radius=.5)
   else:bpy.ops.mesh.primitive_torus_add(major_segments=32,minor_segments=10,major_radius=spec['major_radius_m'],minor_radius=spec['minor_radius_m'])
   o=bpy.context.object;o.name=name
   for c in list(o.users_collection):c.objects.unlink(o)
   collection.objects.link(o)
   if shape!='torus':o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  elif shape=='vessel':
   n=spec['segments'];r=spec['radius_m'];t=spec['wall_m'];height=size[2];verts=[];faces=[]
   # Four rings: outer bottom/top, inner top/bottom. Bottom is solid, interior is genuinely open.
   for radius,z in [(r,0),(r,height),(r-t,height),(r-t,t)]:
    verts.extend((radius*math.cos(2*math.pi*i/n),radius*math.sin(2*math.pi*i/n),z)for i in range(n))
   for ring in range(3):
    for i in range(n):j=(i+1)%n;faces.append((ring*n+i,ring*n+j,(ring+1)*n+j,(ring+1)*n+i))
   faces.append(tuple(reversed(range(n))));faces.append(tuple(3*n+i for i in range(n)))
   o=mesh(name,verts,faces)
  elif shape=='arch':
   n=spec['segments'];r=spec['inner_radius_m'];R=r+spec['thickness_m'];depth=size[1];verts=[];faces=[]
   for y in [-depth/2,depth/2]:
    for radius in [r,R]:verts.extend((radius*math.cos(math.pi*i/n),y,radius*math.sin(math.pi*i/n))for i in range(n+1))
   stride=n+1
   for i in range(n):
    faces.extend([(i,i+1,stride+i+1,stride+i),(2*stride+i,3*stride+i,3*stride+i+1,2*stride+i+1),(i,2*stride+i,2*stride+i+1,i+1),(stride+i,stride+i+1,3*stride+i+1,3*stride+i)])
   faces.extend([(0,stride,3*stride,2*stride),(n,2*stride+n,3*stride+n,stride+n)])
   o=mesh(name,verts,faces)
  else:raise ValueError('Unsupported geometry '+shape)
  o.location=spec['location_m'];o.rotation_euler=[math.radians(x)for x in spec['rotation_deg']];o['itpu_asset_id']=plan['asset_id'];o['itpu_generated']=True;o['itpu_purpose']=spec['purpose'];made[name]=o
  if o.type=='MESH':
   o.data.materials.append(mats[spec['material']]);bpy.context.view_layer.objects.active=o;o.select_set(True)
   # Normalize normals before UV generation. Runtime shader conversion is a Unity step.
   bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
   if spec.get('bevel_m',0)>0 and shape=='box':m=o.modifiers.new('SoftEdges','BEVEL');m.width=spec['bevel_m'];m.segments=2
   bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
   o.select_set(False)
 bpy.context.view_layer.update()
 # Preserve world transforms when parenting moving parts to a dedicated pivot.
 for spec in plan['objects']:
  if spec.get('parent'):
   o=made[spec['name']];matrix=o.matrix_world.copy();o.parent=made[spec['parent']];o.matrix_world=matrix
 bpy.context.view_layer.update()
 for o in made.values():
  r={'name':o.name,'type':o.type,'world_location_m':list(o.matrix_world.translation),'local_scale':list(o.scale)}
  if o.type=='MESH':
   evaluated=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=evaluated.to_mesh();me.calc_loop_triangles();r.update(triangles=len(me.loop_triangles),uv_layers=len(me.uv_layers),materials=len(o.data.materials));evaluated.to_mesh_clear()
  report.append(r)
 # Cameras/render rigs deliberately excluded from deliverable FBX.
 bpy.ops.object.select_all(action='DESELECT')
 for o in made.values():o.select_set(True)
 bpy.context.view_layer.objects.active=next(iter(made.values()))
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(plan['asset_id']+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(out/(plan['asset_id']+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='AUTO')
 (out/'recipe_plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 (out/'asset_report.json').write_text(json.dumps({'status':'BLENDER_GENERATED_REQUIRES_UNITY_IMPORT_AND_VISUAL_REVIEW','blender_version':bpy.app.version_string,'asset_id':plan['asset_id'],'provenance':plan['provenance'],'objects':report,'unity_status':'NOT_RUN','quest_status':'NOT_RUN'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 return report

def main():
 args=sys.argv[sys.argv.index('--')+1:]if'--'in sys.argv else sys.argv[1:];p=argparse.ArgumentParser(description=__doc__);p.add_argument('--recipe',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--allow-synthetic',action='store_true');a=p.parse_args(args)
 build(a.recipe.resolve(),a.output.resolve(),a.allow_synthetic)
if __name__=='__main__':main()
