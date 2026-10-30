from __future__ import annotations
import ast,copy,importlib.util,json,math,struct,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'tools'))
from build_space_v6 import rot,add,sub,chamfer_box,urp_mask_from_mr_pixel
from check_space_v6 import validate_snapshot,contact_report,route_report,segment_rect_distance
from pipeline_v6 import safe,cache_valid,fingerprints,Lock,run_step
from compare_vectors_v6 import compare

class Geometry(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.s=json.loads((ROOT/'art_pipeline/environment_v6/scene_snapshot.json').read_text());cls.plan=json.loads((ROOT/'art_pipeline/environment_v6/scene_recipe.json').read_text());cls.roots={r['id']:r for r in cls.s['roots']}
 def test_all_geometry(self):self.assertEqual(validate_snapshot(self.s)['status'],'PASS')
 def test_contacts(self):self.assertEqual(contact_report(self.s)['status'],'PASS')
 def test_both_streams(self):self.assertEqual(len(contact_report(self.s)['two_streams']),2)
 def test_no_npc_geometry(self):self.assertFalse(any(r.startswith('NPC')for r in self.roots))
 def test_cup_children(self):self.assertEqual(set(self.roots['K_CUP']['members']),{'K_CUP__vessel','K_CUP__handle'})
 def test_cup_grips(self):self.assertTrue({'K_CUP__grip_left','K_CUP__grip_right','K_CUP__dock_base','K_CUP__rim_center'}.issubset(self.roots['K_CUP']['anchors']))
 def test_local_movement(self):
  r=self.roots['K_CUP'];p=next(a for a in self.s['anchors']if a['name']=='K_CUP__rim_center');pos=add(rot(p['local_position_m'],90),[2,3,4]);self.assertAlmostEqual(pos[2],4.105)
 def test_rotation_inverse(self):
  for yaw in [-179,-90,-1,0,47,90,180]:
   v=[.3,.5,-.2];self.assertLess(math.dist(rot(rot(v,yaw),-yaw),v),1e-12)
 def test_uv_nonzero(self):self.assertGreater(validate_snapshot(self.s)['min_uv_triangle_area'],0)
 def test_uv1_not_faked(self):self.assertTrue(all(n['uv1']is None for n in self.s['nodes']))
 def test_texture_types(self):self.assertTrue(all(v['base_space']=='sRGB'and v['mr_space']=='linear'for v in self.s['textures'].values()))
 def test_texture_paths_present(self):
  for t in self.s['textures'].values():
   for k in ['base','mr','unity_mask']:self.assertTrue((ROOT/'art_pipeline/environment_v6'/t[k]).is_file())
 def test_urp_mask_channel_contract(self):
  self.assertEqual(urp_mask_from_mr_pixel([255,71,255]),[255,255,0,184]);self.assertEqual(urp_mask_from_mr_pixel([255,255,0]),[0,255,0,0])
  m=json.loads((ROOT/'art_pipeline/environment_v6/unity_transfer_manifest.json').read_text());self.assertEqual(m['uv1'],'NOT_GENERATED');self.assertEqual(m['unity'],'NOT_RUN')
 def test_work_desks_are_oak_but_cable_boxes_are_not(self):
  desks=[p for p in self.plan['parts']if p['name'].startswith('DESK_')and p['name'].endswith('__top')]
  cables=[p for p in self.plan['parts']if p['name'].startswith('DESK_CABLE_')]
  self.assertEqual(len(desks),6);self.assertTrue(all(p['material']=='oak'for p in desks));self.assertTrue(all(p['material']=='warm_white'for p in cables))
 def test_office_chair_silhouette_parts(self):
  for chair in ['CHAIR_0_0','CHAIR_2_1']:
   names={p['name'].split('__',1)[1] for p in self.plan['parts']if p['name'].startswith(chair+'__')}
   self.assertTrue({'back','armrest_L','armrest_R','caster_0','caster_4'}.issubset(names))
   back=next(p for p in self.plan['parts']if p['name']==chair+'__back');self.assertEqual(back['rotation_deg'][0],-8)
 def test_coffee_accent_is_function_only(self):
  coffee=[p for p in self.plan['parts']if p['name'].startswith('K_COFFEE__')and p['shape']!='empty']
  self.assertEqual([p['name'] for p in coffee if p['material']=='accent'],['K_COFFEE__button'])
  self.assertTrue({'K_COFFEE__top_deck','K_COFFEE__nozzle_bridge','K_COFFEE__control_icon_0'}.issubset({p['name'] for p in coffee}))
 def test_bin_not_solid_box(self):self.assertEqual(self.roots['BIN_403']['colliders'][0]['shape'],'static_mesh_proxy')
 def test_arch_opening_not_box(self):
  for n in ['412','413','414']:
   c=next(c for c in self.roots['ARCH_'+n]['colliders']if c.get('mesh_source')=='ARCH_'+n+'__arch');self.assertFalse(c['convex'])
 def test_routes(self):self.assertEqual(route_report(self.s,self.plan['layout'])['status'],'PASS')
 def test_missing_uv_rejected(self):
  s=copy.deepcopy(self.s);s['nodes'][0]['uv0']=[];self.assertIn('missing_uv:'+s['nodes'][0]['name'],validate_snapshot(s)['errors'])
 def test_zero_uv_rejected(self):
  s=copy.deepcopy(self.s);s['nodes'][0]['uv0']=[[0,0]for v in s['nodes'][0]['uv0']];self.assertEqual(validate_snapshot(s)['status'],'FAIL')
 def test_bad_index_rejected(self):
  s=copy.deepcopy(self.s);s['nodes'][0]['faces'][0]=[0,1,999999];self.assertEqual(validate_snapshot(s)['status'],'FAIL')
 def test_duplicate_root_rejected(self):
  s=copy.deepcopy(self.s);s['roots'].append(s['roots'][0]);self.assertIn('duplicate_root',validate_snapshot(s)['errors'])
 def test_moved_socket_fails(self):
  s=copy.deepcopy(self.s);a=next(a for a in s['anchors']if a['name']=='K_COFFEE__cup_socket');a['location_m'][0]+=.117;self.assertEqual(contact_report(s)['status'],'FAIL')
 def test_lowered_tray_fails_contact(self):
  s=copy.deepcopy(self.s);tray=next(m for m in s['meshes']if m['name']=='K_COFFEE__tray');tray['vertices']=[[x,y,z-.02]for x,y,z in tray['vertices']];self.assertEqual(contact_report(s)['status'],'FAIL')
 def test_lowered_panel_fails_contact(self):
  s=copy.deepcopy(self.s);panel=next(m for m in s['meshes']if m['name']=='K_COFFEE__panel');panel['vertices']=[[x,y,z-.30]for x,y,z in panel['vertices']];self.assertEqual(contact_report(s)['status'],'FAIL')
 def test_chamfer_bounds(self):
  p={'dimensions_m':[1,2,3],'location_m':[0,0,1.5],'rotation_deg':[0,0,0],'shape':'box','bevel_m':.01};v,f=chamfer_box(p);self.assertEqual(len(f),44);self.assertAlmostEqual(min(q[2]for q in v),0);self.assertAlmostEqual(max(q[2]for q in v),3)
 def test_glb_format(self):
  b=(ROOT/'art_pipeline/environment_v6/ITPU_Space_full.glb').read_bytes();magic,version,size=struct.unpack_from('<4sII',b);self.assertEqual((magic,version,size),(b'glTF',2,len(b)));length,kind=struct.unpack_from('<I4s',b,12);d=json.loads(b[20:20+length]);self.assertEqual(kind,b'JSON');self.assertEqual(len(d['images']),6);self.assertTrue(all('TEXCOORD_0'in p['attributes']for m in d['meshes']for p in m['primitives']))
 def test_glb_root_coordinates(self):
  b=(ROOT/'art_pipeline/environment_v6/ITPU_Space_full.glb').read_bytes();length=struct.unpack_from('<I',b,12)[0];d=json.loads(b[20:20+length]);r=next(n for n in d['nodes']if n['name']=='K_CUP');self.assertEqual(r['translation'],[17.46,.9,-17.37]);self.assertEqual(len(r['children']),7)
  self.assertEqual(r['extras']['collider_coordinate_frame'],'ROOT_LOCAL_Y_UP_METRES_GLTF');source=self.roots['K_CUP']['colliders'][0];target=r['extras']['colliders'][0];self.assertEqual(target['size_m'],[source['size_m'][0],source['size_m'][2],source['size_m'][1]])

class ContinuousClearance(unittest.TestCase):
 def test_crosses_between_endpoints(self):self.assertEqual(segment_rect_distance([-2,0],[2,0],[-.1,-.1],[.1,.1]),0)
 def test_parallel(self):self.assertAlmostEqual(segment_rect_distance([-2,2],[2,2],[-1,-1],[1,1]),1)
 def test_corner(self):self.assertAlmostEqual(segment_rect_distance([2,2],[3,3],[-1,-1],[1,1]),math.sqrt(2))
 def test_degenerate_point(self):self.assertAlmostEqual(segment_rect_distance([2,0],[2,0],[-1,-1],[1,1]),1)
 def test_on_boundary(self):self.assertEqual(segment_rect_distance([-1,-2],[-1,2],[-1,-1],[1,1]),0)
 def test_endpoint_inside(self):self.assertEqual(segment_rect_distance([0,0],[9,9],[-1,-1],[1,1]),0)
 def test_reversal(self):self.assertEqual(segment_rect_distance([4,3],[2,1],[-1,-1],[1,1]),segment_rect_distance([2,1],[4,3],[-1,-1],[1,1]))

class Safety(unittest.TestCase):
 def test_no_parent_path(self):
  with self.assertRaises(ValueError):safe(ROOT,'../elsewhere')
 def test_no_absolute_path(self):
  with self.assertRaises(ValueError):safe(ROOT,'/tmp/x')
 def test_symlink_rejected(self):
  with tempfile.TemporaryDirectory()as d:
   p=Path(d);(p/'target').mkdir()
   try:(p/'link').symlink_to(p/'target',target_is_directory=True)
   except (OSError,NotImplementedError):self.skipTest('OS does not allow creating a test symlink without extra rights')
   with self.assertRaises(ValueError):safe(p,'link/x')
 def test_lock_exclusive(self):
  with tempfile.TemporaryDirectory()as d:
   p=Path(d)/'lock'
   with Lock(p):
    with self.assertRaises(RuntimeError):
     with Lock(p):pass
   self.assertFalse(p.exists())
 def test_failed_receipt_no_cache(self):self.assertFalse(cache_valid(ROOT,{'status':'FAIL','inputs':{}},{},[]))
 def test_malformed_receipt_type_no_cache(self):
  for receipt in [None,[],42,'PASS']:
   self.assertFalse(cache_valid(ROOT,receipt,{},[]))
 def test_changed_inputs_no_cache(self):self.assertFalse(cache_valid(ROOT,{'status':'PASS','inputs':{'x':'a'}},{'x':'b'},[]))
 def test_output_hash_checked(self):
  with tempfile.TemporaryDirectory()as d:
   p=Path(d);(p/'out').write_text('a');r={'status':'PASS','inputs':{},'outputs':fingerprints(p,['out'])};self.assertTrue(cache_valid(p,r,{},['out']));(p/'out').write_text('b');self.assertFalse(cache_valid(p,r,{},['out']))
 def test_missing_output_no_cache(self):self.assertFalse(cache_valid(ROOT,{'status':'PASS','inputs':{},'outputs':{}},{},['nonexistent-output']))
 def test_unknown_step_never_executes(self):
  with self.assertRaises(ValueError):run_step('install_blender')
 def test_geometry_dryrun_has_no_editor(self):
  r=run_step('geometry',dry=True);self.assertEqual(r['status'],'DRY_RUN');self.assertNotIn('blender',r['command'])
 def test_duplicate_parity_ids(self):
  a={'implementation':'JS','results':[{'id':'1'},{'id':'1'}]};b={'implementation':'CSharp','results':[]}
  with self.assertRaises(ValueError):compare(a,b)
 def test_csharp_not_forged_from_js(self):
  with self.assertRaises(ValueError):compare({'implementation':'JS'},{'implementation':'JS'})
 def test_parity_mismatch(self):
  a={'implementation':'JS','results':[{'id':'x','status':'PASS','actual':1}]};b={'implementation':'CSharp','results':[{'id':'x','status':'PASS','actual':2}]};self.assertEqual(compare(a,b)['status'],'FAIL')
 def test_blender_script_parse_only(self):ast.parse((ROOT/'art_pipeline/generators/import_space_snapshot_v6.py').read_text())
 def test_blender_namespace_contract(self):
  p=ROOT/'art_pipeline/generators/import_space_snapshot_v6.py';spec=importlib.util.spec_from_file_location('import_space_snapshot_v6',p);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
  self.assertEqual(module.blender_object_name('root','K_CUP'),'K_CUP');self.assertEqual(module.blender_object_name('render','K_CUP'),'VIS__K_CUP');self.assertNotEqual(module.blender_object_name('root','K_CUP'),module.blender_object_name('render','K_CUP'))
  with self.assertRaises(ValueError):module.blender_object_name('unknown','K_CUP')
if __name__=='__main__':unittest.main()
