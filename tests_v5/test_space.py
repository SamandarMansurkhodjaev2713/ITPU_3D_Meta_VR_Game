import ast,copy,json,math,struct,sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'tools'))
from build_space_v5 import scene_plan,load,mesh,flatten,inside,validate_inputs,rgb_01
from recipe_plan import validate,build_plan
class Params(unittest.TestCase):
 def setUp(self):self.p=load('data/space_parameters_v5.json');self.l=load('data/space_layout_v5.json')
 def test_inputs(self):self.assertTrue(validate_inputs(self.p,self.l))
 def test_bool_schema(self):self.p['schema_version']=True;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_false_measurement(self):self.p['parameters']['ceiling']['measured']=True;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_nan(self):self.p['parameters']['ceiling']['value_m']=math.nan;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_negative(self):self.p['parameters']['counter_height']['value_m']=-1;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_bool_value(self):self.p['parameters']['counter_height']['value_m']=True;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_higher_beam(self):self.p['parameters']['beam_clearance']['value_m']=3.1;self.p['parameters']['beam_clearance']['design_range_m']=None;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_tall_desk(self):self.p['parameters']['worktable_height']['value_m']=1.2;self.p['parameters']['worktable_height']['design_range_m']=None;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_extra_room(self):self.l['zones'].append({'id':'436','rect_xyxy_m':[6,19,13,22]});self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_no_npc(self):self.l['npc_stations_generated']=True;self.assertRaises(ValueError,validate_inputs,self.p,self.l)
 def test_wdh(self):p=scene_plan();r=next(r['recipe']for r in p['recipes']if r['recipe']['asset_id']=='K_COFFEE');self.assertEqual(r['dimensions_m'],[.30,.45,.40])
 def test_owner_ambiguity_retained(self):a=load('state/intake_20260917_space.json')['ambiguous_input'];self.assertTrue(all(x['axis']is None for x in a))
class Mesh(unittest.TestCase):
 @classmethod
 def setUpClass(cls):cls.plan=scene_plan();cls.parts=[p for p in cls.plan['parts']if p['shape']!='empty']
 def test_determinism(self):self.assertEqual(scene_plan(),scene_plan())
 def test_unique_names(self):n=[p['name']for p in self.plan['parts']];self.assertEqual(len(n),len(set(n)))
 def test_no_npc_parts(self):self.assertFalse(any('NPC' in p['name']for p in self.plan['parts']))
 def test_finite(self):
  for p in self.parts:
   v,f=mesh(p);self.assertTrue(all(math.isfinite(x)for a in v for x in a));self.assertTrue(all(0<=i<len(v)for t in f for i in t))
 def test_no_degenerate(self):
  for p in self.parts:v,f=mesh(p);a,n,idx=flatten(v,f);self.assertEqual(len(a),len(n));self.assertEqual(len(idx),3*len(f))
 def test_normals_unit(self):
  for p in self.parts:
   v,f=mesh(p);_,n,_=flatten(v,f)
   for i in range(0,len(n),3):self.assertAlmostEqual(sum(x*x for x in n[i:i+3]),1,places=7)
 def test_outward_volume(self):
  for p in self.parts:
   v,f=mesh(p);vol=0
   for t in f:
    a,b,c=[v[i]for i in t];vol+=a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0])
   self.assertGreater(vol,0,p['name'])
 def test_real_palette(self):m=load('data/pbr_materials.json')['materials'];self.assertNotEqual(rgb_01(m['yellow']),rgb_01(m['dark']))
 def test_estimate_guard(self):r=self.plan['recipes'][0]['recipe'];self.assertRaises(ValueError,validate,r)
 def test_estimate_allowed(self):r=self.plan['recipes'][0]['recipe'];self.assertEqual(validate(r,allow_estimated=True)['dimensions_provenance'],'OWNER_AUTHORIZED_DESIGN_ESTIMATE')
 def test_estimate_not_release(self):r=copy.deepcopy(self.plan['recipes'][0]['recipe']);r['level']='production';self.assertRaises(ValueError,validate,r,allow_estimated=True)
 def test_source_required(self):r=copy.deepcopy(self.plan['recipes'][0]['recipe']);r['source_ids']=[];self.assertRaises(ValueError,validate,r,allow_estimated=True)
 def test_floor_excludes_core(self):
  rects=[z['rect_xyxy_m']for z in self.plan['layout']['zones']]
  for p in[(10,20),(10,23),(10,7),(1,6),(19,6)]:self.assertFalse(any(inside(p,r)for r in rects),p)
 def test_main_route_floor_coverage(self):
  rects=[z['rect_xyxy_m']for z in self.plan['layout']['zones']];pts=self.plan['layout']['route_points_m']
  for a,b in zip(pts,pts[1:]):
   for j in range(101):p=[a[i]+(b[i]-a[i])*j/100 for i in range(2)];self.assertTrue(any(inside(p,r)for r in rects),p)
 def test_floors_within_allowed_zones(self):
  rr=[z['rect_xyxy_m']for z in self.plan['layout']['zones']]
  for p in self.parts:
   if p['layer']=='floor':self.assertTrue(any(inside(p['location_m'],r)for r in rr))
 def test_table_parameter_used(self):r=next(r['recipe']for r in self.plan['recipes']if r['recipe']['asset_id']=='DESK_0_0');self.assertEqual(r['dimensions_m'],[1.2,.7,.75])
 def test_blender_script_parses_not_runs(self):ast.parse((ROOT/'art_pipeline/generators/import_space_snapshot_v5.py').read_text())
 def test_generated_glb_header(self):
  b=(ROOT/'art_pipeline/environment_v5/ITPU_Space_cutaway.glb').read_bytes();magic,v,n=struct.unpack('<4sII',b[:12]);self.assertEqual((magic,v,n),(b'glTF',2,len(b)))
 def test_glb_data_bounds(self):
  b=(ROOT/'art_pipeline/environment_v5/ITPU_Space_cutaway.glb').read_bytes();n=struct.unpack('<I',b[12:16])[0];j=json.loads(b[20:20+n]);binary_len=struct.unpack('<I',b[20+n:24+n])[0]
  for v in j['bufferViews']:self.assertLessEqual(v['byteOffset']+v['byteLength'],binary_len)
  self.assertEqual(j['extras']['up'],'Y');self.assertTrue(j['extras']['not_quest_tested'])
 def test_import_script_no_overwrite(self):s=(ROOT/'art_pipeline/generators/import_space_snapshot_v5.py').read_text();self.assertIn('if output.exists()',s);self.assertIn('--factory-startup',s)
if __name__=='__main__':unittest.main()
