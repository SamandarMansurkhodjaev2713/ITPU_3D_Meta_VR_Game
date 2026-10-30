"""Review cameras for an existing asset .blend, not a Quest performance or art-approval test.
Approved command: blender --background ASSET.blend --python render_review.py -- --output NEW_DIR
Creates four PNGs and camera metadata; never saves or overwrites the original .blend.
Source authored here; bpy/Cycles execution requires local Blender validation.
"""
from __future__ import annotations
import argparse,json,math,sys
from pathlib import Path

def render(output:Path,resolution=768,samples=32):
    import bpy
    from mathutils import Vector
    if not bpy.app.background or not bpy.data.filepath:raise ValueError('Run background with an explicit source .blend')
    if not 128<=resolution<=2048 or not 8<=samples<=256:raise ValueError('Review budget outside permitted range')
    if output.exists():raise ValueError('Use a new output directory, never overwrite accepted renders')
    source=Path(bpy.data.filepath)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.hide_render]
    if not meshes:raise ValueError('No visible mesh to review')
    corners=[o.matrix_world @ Vector(p)for o in meshes for p in o.bound_box]
    low=Vector([min(p[i]for p in corners)for i in range(3)]);high=Vector([max(p[i]for p in corners)for i in range(3)])
    center=(low+high)/2;radius=max((high-low).length/2,.01)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=samples
    scene.render.resolution_x=resolution;scene.render.resolution_y=resolution;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('ITPU_REVIEW_WORLD');scene.world.use_nodes=True
    background=scene.world.node_tree.nodes.get('Background');background.inputs['Color'].default_value=(.72,.75,.78,1);background.inputs['Strength'].default_value=.3
    coll=bpy.data.collections.new('ITPU_REVIEW_RIG');scene.collection.children.link(coll)
    def look(obj,target):obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
    # Existing model materials retained; review lights standardized, not baked into game textures.
    for obj in scene.objects:
        if obj.type=='LIGHT':obj.hide_render=True
    for name,direction,power,size in [('KEY',(-2,-3,4),700,3),('FILL',(3,-1,2),350,3),('RIM',(1,3,3),500,2)]:
        data=bpy.data.lights.new('ITPU_REVIEW_'+name,'AREA');data.energy=power*radius*radius;data.shape='DISK';data.size=size*radius
        obj=bpy.data.objects.new(data.name,data);coll.objects.link(obj);obj.location=center+Vector(direction)*radius;look(obj,center)
    camera_data=bpy.data.cameras.new('ITPU_REVIEW_CAMERA');camera=bpy.data.objects.new(camera_data.name,camera_data);coll.objects.link(camera);scene.camera=camera
    camera_data.type='ORTHO';camera_data.ortho_scale=radius*2.7;camera_data.clip_start=max(.0001,radius*.001);camera_data.clip_end=radius*50
    output.mkdir(parents=True);views=[]
    for name,direction in [('front',(0,-4,0)),('side',(4,0,0)),('three_quarter',(3,-4,2)),('back',(-2,4,1))]:
        camera.location=center+Vector(direction).normalized()*radius*5;look(camera,center)
        scene.render.filepath=str(output/(name+'.png'));bpy.ops.render.render(write_still=True)
        views.append({'id':name,'camera_location':list(camera.location),'target':list(center),'orthographic_scale':camera_data.ortho_scale,'file':name+'.png'})
    result={'status':'OFFLINE_ASSET_REVIEW_NOT_UNITY_QUEST','source_name':source.name,'blender_version':bpy.app.version_string,'engine':'CYCLES_CPU','resolution':resolution,'samples':samples,'bounds_min':list(low),'bounds_max':list(high),'views':views,'source_saved':False}
    (output/'render_report.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    return result
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--resolution',type=int,default=768);parser.add_argument('--samples',type=int,default=32)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]if'--'in sys.argv else [])
    render(args.output.resolve(),args.resolution,args.samples)
