"""v4 preparation regressions. No Blender/Unity/Quest claims. Uses temp fixtures only."""
from __future__ import annotations
import ast, copy, hashlib, json, math, shutil, struct, sys, tempfile, unittest, zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'tools'),str(ROOT/'art_pipeline/generators')]
from pbr_contract import load_library,validate_library,build_contract,packed_pixel,png_rgba,srgb_to_linear,linear_to_srgb,write_probe
from intake_contract import validate_intake,coffee_dimensions_wdh
from prepare_task import capsule
from verify_task_capsule import verify
from recipe_plan import build_plan

class MaterialsTests(unittest.TestCase):
    def setUp(self):self.lib=load_library()
    def test_material_schema_not_boolean(self):
        d=copy.deepcopy(self.lib);d['schema_version']=True
        with self.assertRaises(ValueError):validate_library(d)
    def test_shared_library_loads(self):self.assertGreaterEqual(len(self.lib['materials']),9)
    def test_painted_frame_is_dielectric(self):self.assertEqual(self.lib['materials']['frame']['metallic'],0)
    def test_bare_steel_explicit(self):self.assertEqual(self.lib['materials']['steel']['metallic'],1)
    def test_semantics_reject_half_metal(self):
        d=copy.deepcopy(self.lib);d['materials']['frame']['metallic']=.5
        with self.assertRaises(ValueError):validate_library(d)
    def test_bad_scalars_rejected(self):
        for value in [True,None,'0.5',-1,1.1,float('inf'),float('nan')]:
            with self.subTest(value=str(value)):
                d=copy.deepcopy(self.lib);d['materials']['oak']['roughness']=value
                with self.assertRaises(ValueError):validate_library(d)
    def test_hex_must_be_string(self):
        d=copy.deepcopy(self.lib);d['materials']['oak']['srgb']=123456
        with self.assertRaises(ValueError):validate_library(d)
    def test_hex_and_id_validation(self):
        d=copy.deepcopy(self.lib);d['materials']['oak']['srgb']='bad'
        with self.assertRaises(ValueError):validate_library(d)
        d=copy.deepcopy(self.lib);d['materials']['../escape']=d['materials'].pop('oak')
        with self.assertRaises(ValueError):validate_library(d)
    def test_smoothness_complement(self):
        for e in build_contract(self.lib)['entries']:
            self.assertAlmostEqual(e['roughness']+e['unity_smoothness_scalar_without_map'],1)
            self.assertEqual(e['unity_smoothness_multiplier_with_map'],1)
    def test_mask_channels_endpoints(self):
        self.assertEqual(packed_pixel(0,0),(0,255,0,255));self.assertEqual(packed_pixel(1,1,0),(255,0,0,0))
    def test_mask_midpoint_no_gamma(self):self.assertEqual(packed_pixel(0,.5)[3],128)
    def test_srgb_roundtrip_all_8bit_values(self):
        for i in range(256):self.assertAlmostEqual(linear_to_srgb(srgb_to_linear(i/255)),i/255,places=7)
    def test_middle_grey_linear_reference(self):self.assertAlmostEqual(srgb_to_linear(.5),.21404114048223255,places=9)
    def test_contract_does_not_mutate(self):
        old=copy.deepcopy(self.lib);build_contract(self.lib);self.assertEqual(old,self.lib)
    def test_contract_status_not_engine(self):
        c=build_contract(self.lib);self.assertEqual(c['unity'],'NOT_RUN');self.assertFalse(c['data_texture_srgb'])
    def test_numeric_png_pixels_independent_decompression(self):
        data=png_rgba(2,1,[(1,2,3,4),(5,6,7,8)]);self.assertEqual(data[:8],b'\x89PNG\r\n\x1a\n');offset=8;compressed=b''
        while offset<len(data):
            size=struct.unpack('!I',data[offset:offset+4])[0];tag=data[offset+4:offset+8];payload=data[offset+8:offset+8+size];crc=struct.unpack('!I',data[offset+8+size:offset+12+size])[0]
            self.assertEqual(crc,zlib.crc32(tag+payload)&0xffffffff)
            if tag==b'IDAT':compressed+=payload
            offset+=12+size
        self.assertEqual(zlib.decompress(compressed),bytes([0,1,2,3,4,5,6,7,8]))
    def test_png_bad_dimensions_count_byte(self):
        for w,h,p in [(0,1,[]),(True,1,[(0,0,0,0)]),(1,1,[]),(1,1,[(0,0,0,256)])]:
            with self.subTest(w=w,h=h),self.assertRaises(ValueError):png_rgba(w,h,p)
    def test_probe_refuses_overwrite(self):
        with tempfile.TemporaryDirectory()as tmp:
            p=Path(tmp)/'probe';write_probe(p,self.lib)
            self.assertTrue((p/'calibration_data.png').is_file())
            with self.assertRaises(ValueError):write_probe(p,self.lib)
    def test_recipes_use_same_library(self):
        r=json.loads((ROOT/'art_pipeline/recipes/probe_cup.json').read_text());p=build_plan(r,True)
        self.assertEqual(p['materials'],self.lib['materials'])

class IntakeTests(unittest.TestCase):
    def setUp(self):
        self.d=json.loads((ROOT/'data/measurements_to_fill.json').read_text());self.p=json.loads((ROOT/'data/measurement_policy.json').read_text())
    def filled(self,mid='coffee_machine_whd_m',value=None):
        d=copy.deepcopy(self.d);r=next(x for x in d['measurements']if x['id']==mid)
        r.update(value=[.3,.55,.45]if value is None else value,source='SYNTHETIC_TEST_FIXTURE_NOT_REAL_MEASUREMENT',method='test_only',checked_by_owner=True);return d
    def test_empty_receipt_incomplete_no_geometry(self):
        r=validate_intake(self.d,self.p);self.assertTrue(r['missing_ids']);self.assertFalse(r['auto_generate_real_geometry'])
    def test_coffee_axes_not_silent_swap(self):
        r=validate_intake(self.filled(),self.p);self.assertEqual(coffee_dimensions_wdh(r),[.3,.45,.55])
    def test_coffee_missing_is_blocked(self):
        with self.assertRaises(ValueError):coffee_dimensions_wdh(validate_intake(self.d,self.p))
    def test_unverified_value_not_autoapproved(self):
        d=self.filled();next(x for x in d['measurements']if x['id']=='coffee_machine_whd_m')['checked_by_owner']=False
        r=validate_intake(d,self.p)
        with self.assertRaises(ValueError):coffee_dimensions_wdh(r)
    def test_filled_requires_source_and_method(self):
        for key in ['source','method']:
            d=self.filled();next(x for x in d['measurements']if x['id']=='coffee_machine_whd_m')[key]=None
            with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_bad_values(self):
        for v in [0,-1,True,float('nan'),float('inf'),'3.5']:
            with self.subTest(v=str(v)),self.assertRaises(ValueError):validate_intake(self.filled('ceiling_height_m',v),self.p)
    def test_vector_must_have_three_values(self):
        with self.assertRaises(ValueError):validate_intake(self.filled(value=[.3,.55]),self.p)
    def test_units_never_guessed(self):
        d=self.filled();d['measurements'][0]['unit']='cm'
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_unknown_cannot_be_approved(self):
        d=copy.deepcopy(self.d);d['measurements'][0]['checked_by_owner']=True
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_duplicate_unknown_id(self):
        d=copy.deepcopy(self.d);d['measurements'].append(copy.deepcopy(d['measurements'][0]))
        with self.assertRaises(ValueError):validate_intake(d,self.p)
        d=copy.deepcopy(self.d);d['measurements'][0]['id']='made_up_room'
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_tolerance_not_larger_than_value(self):
        d=self.filled('ceiling_height_m',3);d['measurements'][0]['tolerance_m']=4
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_input_not_mutated(self):
        before=copy.deepcopy(self.d);validate_intake(self.d,self.p);self.assertEqual(before,self.d)
    def test_cross_field_conflict_rejected(self):
        d=self.filled('ceiling_height_m',3)
        r=next(x for x in d['measurements']if x['id']=='lowest_beam_height_m')
        r.update(value=4,source='SYNTHETIC_TEST',method='test',checked_by_owner=True)
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_schema_bool_not_version_one(self):
        d=copy.deepcopy(self.d);d['schema_version']=True
        with self.assertRaises(ValueError):validate_intake(d,self.p)
    def test_leaf_and_opening_have_different_ids(self):
        self.assertIn('door_401_opening_width_m',self.p['known_ids']);self.assertIn('door_401_leaf_width_m',self.p['known_ids'])

class CapsuleTests(unittest.TestCase):
    def fixture(self,folder):
        root=Path(folder)/'repo';root.mkdir();(root/'work_orders').mkdir();(root/'state').mkdir()
        (root/'AGENTS.md').write_text('Root instructions\n');(root/'state/project_state.json').write_text('{}');(root/'state/approvals.json').write_text('{}');(root/'needed.txt').write_text('input\n')
        task={'id':'WO-00','title':'Проверка','read_paths':['needed.txt'],'context_limit_bytes':5000,'allowed_write_paths':['safe/']}
        (root/'work_orders/WO-00.json').write_text(json.dumps(task));return root
    def save(self,root,tmp):
        text,m=capsule(root,'WO-00');out=Path(tmp)/'capsule';out.mkdir();(out/'TASK.md').write_text(text,encoding='utf-8',newline='');(out/'INPUT_HASHES.json').write_text(json.dumps(m),encoding='utf-8',newline='');return out,m
    def test_exact_capsule_passes(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);out,_=self.save(r,t);self.assertEqual(verify(r,out),[])
    def test_work_order_is_hashed(self):
        _,m=capsule(ROOT,'WO-00');self.assertIn('work_orders/WO-00.json',[x['path']for x in m['files']])
    def test_approvals_are_hashed(self):
        _,m=capsule(ROOT,'WO-00');self.assertIn('state/approvals.json',[x['path']for x in m['files']])
    def test_whole_payload_budget(self):
        for p in (ROOT/'work_orders').glob('WO-*.json'):
            text,m=capsule(ROOT,p.stem);self.assertEqual(m['capsule_bytes'],len(text.encode('utf-8')))
            self.assertLessEqual(m['capsule_bytes'],json.loads(p.read_text(encoding='utf-8'))['context_limit_bytes'])
    def test_work_order_mutation_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);out,_=self.save(r,t);p=r/'work_orders/WO-00.json';d=json.loads(p.read_text());d['allowed_write_paths']=['other/'];p.write_text(json.dumps(d));self.assertTrue(verify(r,out))
    def test_dependency_mutation_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);out,_=self.save(r,t);(r/'needed.txt').write_text('different');self.assertTrue(verify(r,out))
    def test_capsule_text_mutation_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);out,_=self.save(r,t);(out/'TASK.md').write_text('changed task');self.assertTrue(verify(r,out))
    def test_capsule_manifest_mutation_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);out,m=self.save(r,t);m['files']=[];(out/'INPUT_HASHES.json').write_text(json.dumps(m));self.assertTrue(verify(r,out))
    def test_too_small_limit_raises_without_truncation(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);p=r/'work_orders/WO-00.json';d=json.loads(p.read_text());d['context_limit_bytes']=10;p.write_text(json.dumps(d))
            with self.assertRaises(ValueError):capsule(r,'WO-00')
    def test_work_order_wrong_id_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t);p=r/'work_orders/WO-00.json';d=json.loads(p.read_text());d['id']='WO-01';p.write_text(json.dumps(d))
            with self.assertRaises(ValueError):capsule(r,'WO-00')
    def test_inroot_symlink_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            r=self.fixture(t)
            try:(r/'linked.txt').symlink_to(r/'needed.txt')
            except (OSError,NotImplementedError):self.skipTest('OS does not allow creating a test symlink without extra rights')
            p=r/'work_orders/WO-00.json';d=json.loads(p.read_text());d['read_paths']=['linked.txt'];p.write_text(json.dumps(d))
            with self.assertRaises(ValueError):capsule(r,'WO-00')

class OwnerDecisionTests(unittest.TestCase):
    def test_no_self_approval(self):
        a=json.loads((ROOT/'state/approvals.json').read_text(encoding='utf-8'));s=json.loads((ROOT/'state/project_state.json').read_text(encoding='utf-8'))
        self.assertEqual(a['package']['status'],'APPROVED');self.assertEqual(a['package']['version'],s['package_version']);self.assertEqual(s['owner_approved_package_version'],s['package_version'])
        self.assertTrue(a['package']['evidence'].strip());self.assertEqual(a['install']['status'],'APPROVED');self.assertTrue(a['install']['evidence'].strip())
    def test_style_and_hardware_unknowns_explicit(self):
        a=json.loads((ROOT/'data/art_direction.json').read_text());self.assertIn('Stylized PBR',a['style'])
        c=json.loads((ROOT/'state/capabilities.json').read_text());self.assertIsNone(c['known_owner_hardware']['gpu'])
    def test_no_blanket_model_switch(self):
        c=json.loads((ROOT/'config/model_routing.json').read_text());self.assertIsNone(c['actual_model_ids']);self.assertFalse(c['performance_or_model_equivalence_guarantee'])
    def test_sources_and_builders_are_parseable(self):
        for p in ROOT.rglob('*.py'):
            if '__pycache__'not in str(p):ast.parse(p.read_text(encoding='utf-8'),filename=str(p))

if __name__=='__main__':unittest.main()
