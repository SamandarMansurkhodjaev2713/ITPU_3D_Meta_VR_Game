"""Run in an approved Blender 4.5+/compatible process with --background --factory-startup.
Source snapshot is Z-up metres. Does NOT install, run XR or grant phase approval.
Writes a NEW .blend only; texture paths become relative. UV0 != lightmap UV1.
"""
from __future__ import annotations
import argparse,json,math,sys
from pathlib import Path

def blender_object_name(kind,source_id):
 if kind=='root':return source_id
 if kind=='render':return 'VIS__'+source_id
 if kind=='anchor':return source_id
 raise ValueError('Unknown Blender object namespace kind: '+str(kind))

def main():
 import bpy
 root=Path(__file__).resolve().parents[2]
 p=argparse.ArgumentParser();p.add_argument('--snapshot',default='art_pipeline/environment_v6/scene_snapshot.json');p.add_argument('--output',default='art_pipeline/environment_v6/blender/ITPU_environment_v6.blend');args=p.parse_args(sys.argv[sys.argv.index('--')+1:]if '--'in sys.argv else [])
 def inside(rel):
  q=Path(rel)
  if q.is_absolute()or '..'in q.parts:raise ValueError('Relative workspace paths only')
  q=(root/q).resolve()
  if not q.is_relative_to(root):raise ValueError('Escaping path')
  return q
 src,out=inside(args.snapshot),inside(args.output)
 if out.exists():raise FileExistsError('Refuse overwrite: choose a new .blend filename')
 if out.suffix!='.blend':raise ValueError('Expected .blend')
 if not bpy.app.background:raise RuntimeError('Dedicated background process required')
 if bpy.data.filepath:raise RuntimeError('Use a separate unsaved --factory-startup process')
 s=json.loads(src.read_text(encoding='utf-8'))
 if s.get('up')not in(None,'Z'):raise ValueError('Snapshot must be Z-up')
 # Do not delete user objects: this operator requires a factory process. Remove
 # factory-startup datablocks so the new file has no default Collection/Material.
 for ob in list(bpy.data.objects):bpy.data.objects.remove(ob,do_unlink=True)
 for blocks in [bpy.data.collections,bpy.data.meshes,bpy.data.materials,bpy.data.images,bpy.data.cameras,bpy.data.lights]:
  for block in list(blocks):blocks.remove(block)
 bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1.0
 parent=bpy.data.collections.new('ITPU_GEN_V6');bpy.context.scene.collection.children.link(parent)
 groups={}
 for key in ['ROOTS','RENDER','ANCHORS','COLLIDER_PROXIES']:
  c=bpy.data.collections.new('ITPU_'+key);parent.children.link(c);groups[key]=c
 def link(ob,group):groups[group].objects.link(ob);ob['itpu_generated']=True;return ob
 def rgba(hexcode):
  c=[int(hexcode[i:i+2],16)/255 for i in(1,3,5)]
  return [v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in c]+[1]
 materials={}
 for key,definition in s['materials'].items():
  mat=bpy.data.materials.new('ITPU_'+key);mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=nodes.get('Principled BSDF')
  color=definition.get('srgb',definition.get('base_color_srgb',definition.get('baseColor',definition.get('base_color_hex'))))
  if color is None:color=definition.get('hex')
  if not isinstance(color,str):raise ValueError('Unrecognized material schema '+key)
  if not color.startswith('#'):color='#'+color
  bsdf.inputs['Base Color'].default_value=rgba(color);bsdf.inputs['Roughness'].default_value=definition['roughness'];bsdf.inputs['Metallic'].default_value=definition['metallic']
  if key in s['textures']:
   tx=s['textures'][key]
   for kind,space in [('base','sRGB'),('mr','Non-Color')]:
    imagepath=(src.parent/tx[kind]).resolve()
    if not imagepath.is_relative_to(root):raise ValueError('Unsafe texture reference')
    img=bpy.data.images.load(str(imagepath),check_existing=True);img.colorspace_settings.name=space
    node=nodes.new('ShaderNodeTexImage');node.image=img;node.extension='REPEAT'
    if kind=='base':links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
    else:
     separate=nodes.new('ShaderNodeSeparateColor');separate.mode='RGB';links.new(node.outputs['Color'],separate.inputs['Color']);links.new(separate.outputs['Green'],bsdf.inputs['Roughness']);links.new(separate.outputs['Blue'],bsdf.inputs['Metallic'])
  materials[key]=mat
 roots={}
 for r in s['roots']:
  ob=link(bpy.data.objects.new(blender_object_name('root',r['id']),None),'ROOTS');ob.location=r['position_m'];ob.rotation_euler.z=math.radians(r['yaw_deg']);ob['itpu_asset_id']=r['id'];ob['itpu_zone']=r['zone'];ob['itpu_movable']=r['movable'];roots[r['id']]=ob
 for m in s['nodes']:
  me=bpy.data.meshes.new(m['name']);me.from_pydata(m['vertices'],[],m['faces']);me.update()
  uv=me.uv_layers.new(name='UV0')
  for loop in me.loops:uv.data[loop.index].uv=m['uv0'][loop.vertex_index]
  ob=link(bpy.data.objects.new(blender_object_name('render',m['name']),me),'RENDER');ob.parent=roots[m['root']];ob.location=(0,0,0);ob.data.materials.append(materials[m['material']]);ob['itpu_node_id']=m['name'];ob['itpu_material_id']=m['material'];ob['itpu_uv1']='NOT_GENERATED';ob['itpu_layer']=m['layer']
  # Faceted bevel geometry is intentional; do not add a second global bevel modifier.
 for a in s['anchors']:
  ob=link(bpy.data.objects.new(blender_object_name('anchor',a['name']),None),'ANCHORS');ob.parent=roots[a['root']];ob.location=a['local_position_m'];ob.empty_display_size=.015;ob['itpu_anchor_id']=a['name'];ob['itpu_role']='anchor_position';ob['grip_orientation']='PENDING_HEADSET_ACCEPTANCE'
 # Proxies are authored intent, not Unity Collider components or collision-proof geometry.
 for r in s['roots']:
  for i,c in enumerate(r.get('colliders',[])):
   ob=link(bpy.data.objects.new(r['id']+'__COLLIDER_'+str(i),None),'COLLIDER_PROXIES');ob.parent=roots[r['id']];ob.hide_render=True;ob['collider_json']=json.dumps(c);ob['coordinate_frame']='ROOT_LOCAL_Z_UP_METRES';ob.empty_display_type='CUBE';ob.empty_display_size=.5
   ob.location=c.get('center_m',[0,0,0]);ob.scale=c.get('size_m',[.1,.1,.1])
 groups['COLLIDER_PROXIES'].hide_render=True
 out.parent.mkdir(parents=True,exist_ok=True)
 import os
 for img in bpy.data.images:
  if img.filepath and img.source=='FILE':img.filepath='//'+os.path.relpath(img.filepath,out.parent).replace('\\','/')
 bpy.ops.wm.save_as_mainfile(filepath=str(out),check_existing=False)
 report={'status':'BLENDER_IMPORT_EXECUTED_NOT_UNITY_TESTED','blender_version':bpy.app.version_string,'object_namespace':'roots=asset_id; render=VIS__node_id plus itpu_node_id; anchors=anchor_id','mesh_objects':len(s['nodes']),'roots':len(roots),'uv0':True,'uv1':False,'output':out.relative_to(root).as_posix(),'saved':out.exists()}
 out.with_suffix('.report.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))
if __name__=='__main__':main()
