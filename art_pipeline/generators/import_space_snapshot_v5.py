"""Import the actual v5 mesh snapshot into a NEW Blender scene and save a NEW .blend.
NOT executed in the preparation container. Does not download anything or enable MCP.
Example (inside package, existing approved Blender):
blender --background --factory-startup --python art_pipeline/generators/import_space_snapshot_v5.py -- --output art_pipeline/environment_v5/ITPU_Space_review.blend
"""
from pathlib import Path
import argparse,json,math,sys

def main():
 if '--factory-startup' not in sys.argv:raise RuntimeError('Use an isolated --factory-startup session; never overwrite a working scene')
 import bpy
 root=Path(__file__).resolve().parents[2]
 ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);a=ap.parse_args(sys.argv[sys.argv.index('--')+1:] if '--'in sys.argv else[])
 output=(root/a.output).resolve()
 if not output.is_relative_to(root)or output.suffix.lower()!='.blend':raise ValueError('New .blend path must be inside package')
 if output.exists():raise FileExistsError('Refusing to overwrite an existing .blend')
 src=root/'art_pipeline/environment_v5/scene_snapshot.json';s=json.loads(src.read_text())
 if s['units']!='m'or s['up']!='Z'or s['status']!='APPROXIMATE_ENVIRONMENT_BLOCKOUT':raise ValueError('Unsupported snapshot')
 scene=bpy.data.scenes.new('ITPU_APPROX_SPACE_V5');scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.0
 if bpy.context.window:bpy.context.window.scene=scene
 mats={};cols={}
 for key,m in s['materials'].items():
  mat=bpy.data.materials.new('ITPU_'+key);mat.use_nodes=True;node=mat.node_tree.nodes.get('Principled BSDF');c=[int(m['srgb'][i:i+2],16)/255 for i in(0,2,4)];lin=[x/12.92 if x<=.04045 else((x+.055)/1.055)**2.4 for x in c]
  node.inputs['Base Color'].default_value=(*lin,1);node.inputs['Roughness'].default_value=m['roughness'];node.inputs['Metallic'].default_value=m['metallic'];mat.diffuse_color=(*lin,1);mats[key]=mat
 for m in s['meshes']:
  group=m['zone']+'__'+m['layer']
  if group not in cols:
   col=bpy.data.collections.new(group);scene.collection.children.link(col);cols[group]=col
   if m['layer']=='ceiling':col.hide_viewport=True # inspection default; still available as an authored layer
  me=bpy.data.meshes.new(m['name']);me.from_pydata(m['vertices'],[],m['faces']);me.update();ob=bpy.data.objects.new(m['name'],me);cols[group].objects.link(ob);ob.data.materials.append(mats[m['material']]);ob['geometry_status']='OWNER_AUTHORIZED_ESTIMATE';ob['zone']=m['zone'];ob['layer']=m['layer'];ob['source_snapshot']='scene_snapshot.json'
 for a in s['anchors']:
  ob=bpy.data.objects.new(a['name'],None);ob.empty_display_size=.045;ob.location=a['location_m'];ob.rotation_euler=[math.radians(x)for x in a['rotation_deg']];scene.collection.objects.link(ob);ob['type']='PROVISIONAL_INTERACTION_ANCHOR'
 scene['status']='APPROXIMATE_ONLY_NOT_ACCEPTED_ART';scene['npc_generated']=False;scene['unity_imported']=False
 output.parent.mkdir(parents=True,exist_ok=True);bpy.ops.wm.save_as_mainfile(filepath=str(output))
 report={'status':'SAVED_BY_BLENDER_NOT_UNITY_TEST','blender':bpy.app.version_string,'objects':len(scene.objects),'file':str(output.name),'npc_count':0,'units':'m','approximate':True}
 output.with_suffix('.report.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
if __name__=='__main__':main()
