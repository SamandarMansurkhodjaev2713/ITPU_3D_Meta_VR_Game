"""Source-only Blender calibration scene. Run only in an agreed new background process:
blender --background --factory-startup --python build_material_probe.py -- --output NEW_DIRECTORY
NOT executed in the preparation chat. Synthetic dimensions, never a campus asset.
"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
from pbr_contract import load_library, build_contract

def build(out:Path):
    import bpy
    if not bpy.app.background or bpy.data.filepath:raise ValueError('New background factory-startup scene required')
    if out.exists():raise ValueError('New output directory required')
    contract=build_contract(load_library());out.mkdir(parents=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    objects=[]
    for i,e in enumerate(contract['entries']):
        mat=bpy.data.materials.new(e['blender_name']);mat.use_nodes=True
        bsdf=mat.node_tree.nodes.get('Principled BSDF')
        if bsdf is None:raise RuntimeError('Pinned Blender node API mismatch')
        bsdf.inputs['Base Color'].default_value=tuple(e['base_color_linear_rgb'])+(1,)
        bsdf.inputs['Roughness'].default_value=e['roughness'];bsdf.inputs['Metallic'].default_value=e['metallic']
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,radius=.13,location=((i%5)*.36,(i//5)*.36,.13))
        o=bpy.context.object;o.name='ITPU_PROBE_'+e['id'];o.data.materials.append(mat)
        o['itpu_synthetic_probe']=True
        for poly in o.data.polygons:poly.use_smooth=True
        objects.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'PBR_PROBE_SYNTHETIC.blend'))
    bpy.ops.export_scene.fbx(filepath=str(out/'PBR_PROBE_SYNTHETIC.fbx'),use_selection=True,object_types={'MESH'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    contract['blender']='GENERATED_NOT_VISUALLY_ACCEPTED';contract['blender_version']=bpy.app.version_string
    (out/'material_transfer.json').write_text(json.dumps(contract,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    # No camera/light shipped as production content. Use render_review only after agreed lighting setup.
    print('Saved synthetic material probe. Unity and Quest NOT_RUN.')
if __name__=='__main__':
    argv=sys.argv[sys.argv.index('--')+1:]if'--'in sys.argv else sys.argv[1:]
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args(argv);build(a.output.resolve())
