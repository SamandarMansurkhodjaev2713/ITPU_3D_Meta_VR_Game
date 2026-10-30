"""v4 data/decision integrity. Engine and hardware work remain separate."""
from pathlib import Path
import json,sys,re
from packlib import ROOT
from pbr_contract import load_library,build_contract
from intake_contract import validate_intake
from prepare_task import capsule

def validate(root):
    errors=[]
    def read(p):return json.loads((root/p).read_text(encoding='utf-8'))
    try:
        state=read('state/project_state.json');auth=read('state/approvals.json');direction=read('data/art_direction.json')
        if not isinstance(state['package_version'],str) or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+-[a-z0-9-]+',state['package_version']) or state['package_version']!=read('config/document_order.json')['version']:errors.append('Unexpected/mismatched version')
        if 'Stylized PBR'not in direction['style']:errors.append('Owner art direction not recorded')
        if auth['branding_and_likeness']['status']!='OWNER_REPORTED_AUTHORIZED_IN_PROJECT':errors.append('In-project consent state lost')
        if state['device_tested'] or state['unity_integration_complete']:errors.append('No device/integration evidence in this preparation pass')
        build_contract(read('data/pbr_materials.json'))
        receipt=validate_intake(read('data/measurements_to_fill.json'),read('data/measurement_policy.json'))
        for p in (root/'work_orders').glob('WO-*.json'):capsule(root,p.stem)
        zones=read('data/spatial_scope.json')
        if zones['active_niche']['id']!='NICHE_413':errors.append('Designer placement baseline changed')
    except (ValueError,KeyError,TypeError,OSError)as e:errors.append(str(e))
    return errors
if __name__=='__main__':
    errors=validate(ROOT);print('FAIL'if errors else'PASS: v4 decisions/material/intake/capsule contracts — no engine claim')
    for e in errors:print(e)
    raise SystemExit(int(bool(errors)))
