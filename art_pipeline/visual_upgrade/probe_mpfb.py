"""Probe installed MPFB offline in an isolated technical sandbox, never a portrait."""
import bpy, importlib, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'reports/local/WO-31/mpfb_sandbox.blend'
REPORT = ROOT / 'reports/local/WO-31/mpfb_probe.json'
if bpy.app.online_access:
    raise RuntimeError('Probe requires --offline-mode')
import addon_utils
module = 'bl_ext.user_default.mpfb'
if module not in bpy.context.preferences.addons:
    addon_utils.enable(module, default_set=True, persistent=False)
mpfb = importlib.import_module(module)
from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.targetservice import TargetService

if '--reopen' in sys.argv:
    body = bpy.data.objects['WO31_TECHNICAL_BASE_NOT_PORTRAIT']
    assert len(body.data.vertices) > 10000
    assert body.get('itpu_probe') == 'MPFB_2_0_18'
    assert bpy.data.objects['WO31_TECHNICAL_BASE_NOT_PORTRAIT.rig'].type == 'ARMATURE'
    rows = json.loads(REPORT.read_text())
    rows['reopen'] = 'PASS_SEPARATE_PROCESS'
    rows['status'] = 'PASS_INSTALLED_CREATE_EDIT_SAVE_REOPEN'
    REPORT.write_text(json.dumps(rows, indent=2)+'\n')
else:
    if OUT.exists():
        raise RuntimeError('Do not overwrite existing sandbox')
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    macros = TargetService.get_default_macro_info_dict()
    macros['gender'] = 1.0
    macros['age'] = 0.3
    body = HumanService.create_human(macro_detail_dict=macros)
    body.name = 'WO31_TECHNICAL_BASE_NOT_PORTRAIT'
    body['itpu_probe'] = 'MPFB_2_0_18'
    rig = HumanService.add_builtin_rig(body, 'game_engine', import_weights=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
    REPORT.write_text(json.dumps({'status': 'PASS_CREATED_SAVED_REOPEN_NOT_RUN',
        'tool': 'MPFB', 'version': list(mpfb.VERSION), 'blender': bpy.app.version_string,
        'offline': not bpy.app.online_access, 'vertices': len(body.data.vertices),
        'bones': len(rig.data.bones), 'blend': OUT.relative_to(ROOT).as_posix(),
        'source_zip_sha256': 'ada7c4ff11e79cf6ed9b441bccabc52c9359f2848b988f66e89c14c30b21fed7',
        'portrait_likeness': 'N/A_TECHNICAL_TOOL_PROBE', 'reopen': 'NOT_RUN'}, indent=2)+'\n')
