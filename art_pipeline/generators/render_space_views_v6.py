"""Run only in approved installed Blender, with an imported v6 .blend loaded.
Creates diagnostic lights/camera in memory and renders new files. Does not save changes to .blend.
"""
from __future__ import annotations
import argparse,json,math,sys
from pathlib import Path

def main():
 import bpy
 from mathutils import Vector
 root=Path(__file__).resolve().parents[2]
 p=argparse.ArgumentParser();p.add_argument('--output',default='reports/local/WO-16/control_views');p.add_argument('--view',default='coffee');a=p.parse_args(sys.argv[sys.argv.index('--')+1:]if '--'in sys.argv else [])
 out=(root/a.output).resolve()
 if not out.is_relative_to(root)or '..'in Path(a.output).parts:raise ValueError('Workspace only')
 if out.exists():raise FileExistsError('Choose new output directory; do not overwrite accepted images')
 if not bpy.data.filepath or not Path(bpy.data.filepath).resolve().is_relative_to(root):raise RuntimeError('Load reviewed workspace .blend first')
 if 'ITPU_GEN_V6'not in bpy.data.collections:raise RuntimeError('Expected hierarchy missing')
 data=json.loads((root/'data/control_views_v6.json').read_text());views=data['views']if a.view=='all'else [v for v in data['views']if v['id']==a.view]
 if not views:raise ValueError('Unknown view')
 col=bpy.data.collections.new('ITPU_DIAGNOSTIC_RENDER');bpy.context.scene.collection.children.link(col)
 def area(name,pos,power,size):
  l=bpy.data.lights.new(name,'AREA');l.energy=power;l.shape='DISK';l.size=size;ob=bpy.data.objects.new(name,l);col.objects.link(ob);ob.location=pos
 lights=[('403',[2,13,2.9],350,3),('corr_left',[5,19,2.9],250,2),('niches',[10.3,25.7,2.9],250,3),('kitchen',[18,16,2.9],350,3),('probe',[24,19.2,2.8],500,2)]
 for name,pos,power,size in lights:area('ITPU_DIAG_'+name,pos,power,size)
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
 scene.render.resolution_x=960;scene.render.resolution_y=640;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 scene.world=bpy.data.worlds.new('ITPU_DIAGNOSTIC_WORLD');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.2
 camdata=bpy.data.cameras.new('ITPU_DIAGNOSTIC_CAMERA');cam=bpy.data.objects.new('ITPU_DIAGNOSTIC_CAMERA',camdata);col.objects.link(cam);scene.camera=cam
 scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=0
 cup=bpy.data.objects.get('K_CUP');cup_home=cup.location.copy() if cup else None
 production=[o for o in bpy.context.scene.objects if o.name.startswith('VIS__')]
 probe=[]
 for i,material in enumerate(['oak','fabric','warm_white','ceramic','steel']):
  bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,radius=.24,location=(23.0+i*.5,20.4,.28))
  ob=bpy.context.object;ob.name='ITPU_PROBE_'+material
  for collection in list(ob.users_collection):collection.objects.unlink(ob)
  col.objects.link(ob);ob.data.materials.append(bpy.data.materials['ITPU_'+material]);probe.append(ob)
 bpy.ops.mesh.primitive_cube_add(size=1,location=(24,20.45,-.03));backdrop=bpy.context.object;backdrop.name='ITPU_PROBE_BACKDROP'
 for collection in list(backdrop.users_collection):collection.objects.unlink(backdrop)
 col.objects.link(backdrop);backdrop.scale=(1.6,.8,.04);backdrop.data.materials.append(bpy.data.materials['ITPU_warm_white']);probe.append(backdrop)
 stream_material=bpy.data.materials.new('ITPU_DIAGNOSTIC_STREAM');stream_material.diffuse_color=(.18,.55,.72,1);stream_material.use_nodes=True
 stream_bsdf=stream_material.node_tree.nodes.get('Principled BSDF');stream_bsdf.inputs['Base Color'].default_value=(.027,.263,.479,1);stream_bsdf.inputs['Roughness'].default_value=.18
 stream_objects=[]
 for i in range(2):
  bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=.004,depth=1,location=(0,0,0))
  ob=bpy.context.object;ob.name='ITPU_DIAGNOSTIC_STREAM_'+str(i)
  for collection in list(ob.users_collection):collection.objects.unlink(ob)
  col.objects.link(ob);ob.data.materials.append(stream_material);ob.hide_render=True;stream_objects.append(ob)
 out.mkdir(parents=True);outputs=[];view_rows=[]
 for v in views:
  if cup:cup.location=cup_home
  isolate=v.get('isolate_prefix')
  for ob in production:ob.hide_render=bool(v.get('material_probe'))or bool(isolate and not(ob.name.startswith('VIS__'+isolate+'__')or ob.name.startswith('VIS__FLOOR_')))
  for ob in probe:ob.hide_render=not bool(v.get('material_probe'))
  if v.get('dock_cup'):
   socket=bpy.data.objects.get('K_COFFEE__cup_socket')
   if cup is None or socket is None:raise RuntimeError('Docked render requires cup root and socket anchor')
   bpy.context.view_layer.update();cup.location=socket.matrix_world.translation;bpy.context.view_layer.update()
   rim=bpy.data.objects.get('K_CUP__rim_center')
   if rim is None:raise RuntimeError('Docked render requires cup rim anchor')
   for i,ob in enumerate(stream_objects):
    top=bpy.data.objects.get('K_COFFEE__stream_'+str(i))
    if top is None:raise RuntimeError('Docked render requires stream anchors')
    top=top.matrix_world.translation;bottom_z=rim.matrix_world.translation.z;height=top.z-bottom_z
    if height<=0:raise RuntimeError('Stream must start above cup rim')
    ob.location=(top.x,top.y,bottom_z+height/2);ob.scale=(1,1,height);ob.hide_render=False
  else:
   for ob in stream_objects:ob.hide_render=True
  cam.location=v['position_m'];cam.rotation_euler=(Vector(v['target_m'])-cam.location).to_track_quat('-Z','Y').to_euler();camdata.lens=v['lens_mm'];camdata.clip_start=.02;camdata.clip_end=100;scene.render.filepath=str(out/(v['id']+'.png'));bpy.ops.render.render(write_still=True);outputs.append(v['id']+'.png')
  view_rows.append({'id':v['id'],'position_m':v['position_m'],'target_m':v['target_m'],'rotation_euler_rad':list(cam.rotation_euler),'lens_mm':camdata.lens,'docked_cup':bool(v.get('dock_cup')),'diagnostic_stream_overlay':bool(v.get('dock_cup')),'material_probe':bool(v.get('material_probe')),'isolate_prefix':v.get('isolate_prefix'),'file':v['id']+'.png'})
 if cup:cup.location=cup_home
 (out/'render_report.json').write_text(json.dumps({'status':'DIAGNOSTIC_RENDER_EXECUTED','blender':bpy.app.version_string,'render_engine':scene.render.engine,'device':scene.cycles.device,'samples':scene.cycles.samples,'resolution_px':[scene.render.resolution_x,scene.render.resolution_y],'color_transform':scene.view_settings.view_transform,'look':scene.view_settings.look,'exposure':scene.view_settings.exposure,'world_strength':.2,'lights':[{'name':name,'position_m':pos,'power_w':power,'size_m':size}for name,pos,power,size in lights],'views':view_rows,'probe_material_order_left_to_right':['oak','fabric','warm_white','ceramic','steel'],'diagnostic_stream_overlay':'Only coffee_docked; generated in memory from stream/rim anchors; not saved to production .blend','outputs':outputs,'production_file_saved':False,'game_lighting':False,'device_test':False},indent=2))
 print('Diagnostic images saved; original .blend was not overwritten')
if __name__=='__main__':main()
