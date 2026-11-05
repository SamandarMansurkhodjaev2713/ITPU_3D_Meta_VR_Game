"""Native material/light successor of _005; preserves production geometry and anchors."""
import bpy, hashlib, json, sys
from pathlib import Path
from mathutils import Vector
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_006.blend'
REPORT = ROOT / 'reports/local/WO-31'
TEXTURES = ROOT / 'unity/ITPU_VR_Unity/Assets/ITPU/Art/VisualUpgrade001/Textures'

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def geometry():
    return {o.name: {'parent': o.parent.name if o.parent else None,
        'matrix': [float(v) for row in o.matrix_world for v in row],
        'vertices': len(o.data.vertices) if o.type == 'MESH' else None}
        for o in bpy.data.collections['ITPU_GEN_V6'].all_objects}

def image(name, data=False):
    img = bpy.data.images.load(str(TEXTURES / name), check_existing=True)
    if data: img.colorspace_settings.name = 'Non-Color'
    return img

def material(name, rgb, roughness=0.65, metallic=0):
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*[v ** 2.2 for v in rgb], 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    return mat

def maps(mat, asset, tint, variation, normal_strength):
    nodes = mat.node_tree.nodes; links = mat.node_tree.links
    bsdf = nodes.get('Principled BSDF')
    for link in list(links):
        if link.to_node == bsdf and link.to_socket.name in {'Base Color', 'Roughness', 'Normal'}:
            links.remove(link)
    src = image(asset+'_diff_1k.jpg')
    values = np.empty(len(src.pixels), dtype=np.float32); src.pixels.foreach_get(values)
    values = values.reshape((-1,4)); gray = values[:,:3].mean(axis=1)
    gray = (gray - gray.mean()) * variation
    for channel in range(3): values[:,channel] = np.clip(tint[channel] ** 2.2 + gray, 0,1)
    values[:,3] = 1
    graded = bpy.data.images.new(asset+'_stylized_base_blender', width=src.size[0],height=src.size[1])
    graded.pixels.foreach_set(values.ravel()); graded.pack()
    color = nodes.new('ShaderNodeTexImage'); color.image = graded
    links.new(color.outputs['Color'], bsdf.inputs['Base Color'])
    rough = nodes.new('ShaderNodeTexImage'); rough.image = image(asset+'_rough_1k.jpg', True)
    links.new(rough.outputs['Color'], bsdf.inputs['Roughness'])
    normal = nodes.new('ShaderNodeTexImage'); normal.image = image(asset+'_nor_gl_1k.jpg', True)
    convert = nodes.new('ShaderNodeNormalMap'); convert.inputs['Strength'].default_value=normal_strength
    links.new(normal.outputs['Color'], convert.inputs['Color']); links.new(convert.outputs['Normal'],bsdf.inputs['Normal'])

def configure_lighting():
    col=bpy.data.collections.new('ITPU_VISUAL006_LIGHTS'); bpy.context.scene.collection.children.link(col)
    for obj in list(bpy.data.collections['ITPU_RENDER'].objects):
        if not ('diffuser' in obj.name.lower() or ('PENDANT_' in obj.name and 'globe' in obj.name.lower())): continue
        data=bpy.data.lights.new('VISUAL006_'+obj.name, 'AREA'); data.energy=95; data.shape='RECTANGLE'; data.size=1.25; data.size_y=.35
        data.color=(1,.93,.83); light=bpy.data.objects.new(data.name,data); col.objects.link(light)
        light.location=obj.matrix_world.translation + Vector((0,0,-.12))
    for name,pos,target,power,size in [
        ('403_DAYLIGHT',(4.7,12,2.2),(1.5,13,1),250,3),
        ('KITCHEN_DAYLIGHT',(19.7,15.5,2.2),(17.5,17,1),300,3)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.size=size
        ob=bpy.data.objects.new(name,data); col.objects.link(ob); ob.location=pos
        ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene; scene.world=bpy.data.worlds.new('ITPU_VISUAL006_WORLD'); scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.25
    scene.render.engine='CYCLES'; scene.cycles.samples=48; scene.cycles.use_denoising=True
    preferences=bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type='OPTIX'; preferences.get_devices()
    for device in preferences.devices: device.use=device.type=='OPTIX'
    scene.cycles.device='GPU'
    scene.render.resolution_x=1440; scene.render.resolution_y=960; scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'; scene.view_settings.look='AgX - Medium High Contrast'

if '--reopen' not in sys.argv:
    if OUT.exists(): raise RuntimeError('Immutable successor already exists')
    source=Path(bpy.data.filepath); before=geometry()
    wood=bpy.data.materials['ITPU_oak']; maps(wood,'wood_table_001',(.68,.49,.29),.22,.12)
    floor=material('ITPU_VISUAL006_floor',(.63,.62,.59)); maps(floor,'concrete_floor_02',(.63,.62,.59),.07,.10)
    wall=material('ITPU_VISUAL006_wall',(.79,.78,.74),.88)
    fabric=bpy.data.materials['ITPU_fabric']; nodes=fabric.node_tree.nodes; links=fabric.node_tree.links
    bsdf=nodes.get('Principled BSDF')
    for link in list(links):
        if link.to_node==bsdf: links.remove(link)
    bsdf.inputs['Base Color'].default_value=(.012,.015,.019,1); bsdf.inputs['Roughness'].default_value=.95
    normal=nodes.new('ShaderNodeTexImage'); normal.image=image('fabric_pattern_05_nor_gl_1k.jpg',True)
    convert=nodes.new('ShaderNodeNormalMap'); convert.inputs['Strength'].default_value=.15
    links.new(normal.outputs['Color'],convert.inputs['Color']); links.new(convert.outputs['Normal'],bsdf.inputs['Normal'])
    for obj in bpy.data.collections['ITPU_RENDER'].objects:
        if obj.type!='MESH': continue
        if obj.name.startswith('VIS__FLOOR_'):
            obj.data.materials.clear(); obj.data.materials.append(floor)
        elif obj.name.startswith(('VIS__WALL_','VIS__ARCH_','VIS__NICHE_SIDE_','VIS__SPANDREL_')):
            for slot in obj.material_slots:
                if slot.material and slot.material.name=='ITPU_warm_white': slot.material=wall
    configure_lighting()
    if before!=geometry(): raise RuntimeError('Geometry/anchors changed during material upgrade')
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
    (REPORT/'blender_visual006_build.json').write_text(json.dumps({'status':'PASS_SAVED_REOPEN_NOT_RUN',
        'source':source.relative_to(ROOT).as_posix(),'source_sha256':digest(source),
        'output':OUT.relative_to(ROOT).as_posix(),'output_sha256':digest(OUT),
        'geometry_and_anchors_unchanged':True,'objects':len(before),'license':'Powered by Poly Haven; CC0',
        'unity':'NOT_RUN'},indent=2))
else:
    assert Path(bpy.data.filepath).resolve()==OUT.resolve()
    preferences=bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type='OPTIX'; preferences.get_devices()
    for device in preferences.devices: device.use=device.type=='OPTIX'
    if not any(device.use and device.type=='OPTIX' for device in preferences.devices):
        raise RuntimeError('No enabled OptiX device for this render')
    bpy.context.scene.cycles.device='GPU'
    views=json.loads((ROOT/'data/control_views_v6.json').read_text())['views']
    col=bpy.data.collections.new('ITPU_VISUAL006_CONTROL'); bpy.context.scene.collection.children.link(col)
    data=bpy.data.cameras.new('VISUAL006_CAMERA'); cam=bpy.data.objects.new(data.name,data); col.objects.link(cam)
    scene=bpy.context.scene; scene.camera=cam; output=REPORT/'blender_control_006_gpu'; output.mkdir(exist_ok=True)
    rendered=[]
    for view in views:
        if view['id'] not in {'work_entry','work_context','coffee','kitchen'}: continue
        cam.location=view['position_m']; cam.rotation_euler=(Vector(view['target_m'])-cam.location).to_track_quat('-Z','Y').to_euler()
        data.lens=view['lens_mm']; scene.render.filepath=str(output/(view['id']+'.png'))
        bpy.ops.render.render(write_still=True); rendered.append(view['id'])
    missing=[img.name for img in bpy.data.images if img.source=='FILE' and not img.packed_file and not Path(bpy.path.abspath(img.filepath)).is_file()]
    if missing: raise RuntimeError(str(missing))
    (REPORT/'blender_visual006_reopen_gpu.json').write_text(json.dumps({'status':'PASS_REOPENED_RENDERED',
        'blend_sha256':digest(OUT),'rendered':rendered,'device':'OPTIX_GPU','blender':bpy.app.version_string,
        'enabled_devices':[d.name for d in preferences.devices if d.use],
        'missing_dependencies':missing,'visual_inspection':'NOT_RUN','unity':'NOT_RUN','quest':'NOT_RUN'},indent=2))
