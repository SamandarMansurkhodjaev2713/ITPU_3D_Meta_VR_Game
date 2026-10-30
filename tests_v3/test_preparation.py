"""Preparation tests, not bpy/C#/Unity/device tests. Pure stdlib, isolated temporary fixtures."""
from __future__ import annotations
import ast,copy,hashlib,importlib.util,json,math,sys,tempfile,unittest,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'tools'),str(ROOT/'art_pipeline/generators')]
from recipe_plan import SUPPORTED,build_plan,validate
from prepare_task import capsule
from stage_unity_sources import stage,staging_plan
from analyze_metrics import analyze
from packlib import resolve_inside
class PreparationTests(unittest.TestCase):
    def sample(self,kind='cup'):
        return json.loads((ROOT/f'art_pipeline/recipes/probe_{kind}.json').read_text())
    def test_synthetic_needs_flag(self):
        with self.assertRaises(ValueError):build_plan(self.sample())
    def test_no_synthetic_production(self):
        x=self.sample();x['level']='production'
        with self.assertRaises(ValueError):build_plan(x,True)
    def test_measured_recipe_requires_source(self):
        x=self.sample();x['dimensions_provenance']='OWNER_MEASURED_OR_VERIFIED_PLAN';x['source_ids']=[]
        with self.assertRaises(ValueError):build_plan(x)
    def test_measured_recipe_with_evidence(self):
        x=self.sample();x['dimensions_provenance']='OWNER_MEASURED_OR_VERIFIED_PLAN';x['source_ids']=['TEST_ONLY'];x['dimensions_evidence']='SYNTHETIC TEST FIXTURE NOT REAL SURVEY'
        self.assertTrue(build_plan(x)['objects'])
    def test_source_recipe_not_mutated(self):
        x=self.sample();before=copy.deepcopy(x);build_plan(x,True);self.assertEqual(before,x)
    def test_null_real_recipes_block(self):
        for name in ['coffee_itpu','facade_401','facade_423','niche_413']:
            with self.subTest(name=name),self.assertRaises(ValueError):build_plan(json.loads((ROOT/f'art_pipeline/recipes/{name}.json').read_text()))
    def test_bad_kind(self):
        x=self.sample();x['generator_type']='exec_code'
        with self.assertRaises(ValueError):build_plan(x,True)
    def test_identifier_escape(self):
        x=self.sample();x['asset_id']='../../evil'
        with self.assertRaises(ValueError):build_plan(x,True)
    def test_huge_bevel(self):
        x=self.sample();x['bevel_m']=10
        with self.assertRaises(ValueError):build_plan(x,True)
    def test_nan_bool_inf_dims(self):
        for bad in [float('nan'),float('inf'),True,None,-1,0]:
            x=self.sample();x['dimensions_m'][0]=bad
            with self.subTest(bad=str(bad)),self.assertRaises(ValueError):build_plan(x,True)
    def test_arch_top_matches_supplied_height(self):
        x=self.sample('niche_arch');p=build_plan(x,True);o=next(o for o in p['objects']if o['shape']=='arch')
        self.assertAlmostEqual(o['location_m'][2]+o['inner_radius_m']+o['thickness_m'],x['dimensions_m'][2])
    def test_paper_thickness_allowed(self):
        self.assertTrue(build_plan(self.sample('paper_set'),True)['objects'])
    def test_every_python_parses(self):
        for path in ROOT.rglob('*.py'):ast.parse(path.read_text(encoding='utf-8'),filename=str(path))
    def test_project_xml_parses(self):
        for path in ROOT.rglob('*.csproj'):ET.parse(path)
    def test_asmdef_json(self):
        for path in ROOT.rglob('*.asmdef'):self.assertTrue(json.loads(path.read_text())['name'])
    def test_blender_guard_before_factory_reset(self):
        text=(ROOT/'art_pipeline/generators/build_blender.py').read_text()
        self.assertLess(text.index('not bpy.app.background'),text.index('bpy.ops.wm.read_factory_settings'))
        self.assertNotIn('normals_make_consistent',text)
    def test_npc_fixed_source_contract(self):
        text=(ROOT/'src/unity/Runtime/FixedNpcStation.cs').read_text()
        self.assertIn('applyRootMotion=false',text);self.assertIn('anchor',text)
        self.assertNotIn('NavMeshAgent',text)
    def test_spatial_exclusions(self):
        d=json.loads((ROOT/'data/spatial_scope.json').read_text());self.assertFalse(d['metric_generation_allowed']);self.assertFalse(d['red_point_is_start'])
        ids=[z['id']for z in d['zones']];self.assertNotIn('ROOM_401',ids);self.assertNotIn('ROOM_423',ids);self.assertNotIn('436',ids)
    def test_one_active_niche(self):
        d=json.loads((ROOT/'data/spatial_scope.json').read_text());self.assertEqual(d['active_niche']['id'],'NICHE_413')
    def test_wo_ids_dependencies_acyclic(self):
        orders={p.stem:json.loads(p.read_text())for p in (ROOT/'work_orders').glob('WO-*.json')};vis=set();done=set()
        def visit(i):
            self.assertNotIn(i,vis);self.assertIn(i,orders)
            if i in done:return
            vis.add(i)
            for dep in orders[i]['depends_on']:visit(dep)
            vis.remove(i);done.add(i)
        for i in orders:visit(i)
    def test_all_capsules_reproducible(self):
        for path in (ROOT/'work_orders').glob('WO-*.json'):
            a,m=capsule(ROOT,path.stem);b,n=capsule(ROOT,path.stem);self.assertEqual(a,b);self.assertEqual(m,n)
            self.assertLessEqual(m['input_bytes'],json.loads(path.read_text())['context_limit_bytes'])
    def test_capsule_invalid_id(self):
        with self.assertRaises(ValueError):capsule(ROOT,'../../key')
    def test_capsule_hash_tracks_actual_file(self):
        _,m=capsule(ROOT,'WO-00')
        for f in m['files']:self.assertEqual(f['sha256'],hashlib.sha256((ROOT/f['path']).read_bytes()).hexdigest())
    def test_resolve_rejects_parent(self):
        for p in ['../x','C:/secret','a\\b']:
            with self.assertRaises(ValueError):resolve_inside(ROOT,p)
    def test_staging_dry_run(self):
        with tempfile.TemporaryDirectory()as t:
            p=Path(t);(p/'Assets').mkdir();(p/'ProjectSettings').mkdir();(p/'ProjectSettings/ProjectVersion.txt').write_text('probe')
            report=stage(ROOT,p);self.assertEqual(report['status'],'DRY_RUN');self.assertFalse((p/'Assets/ITPUPrepared').exists());self.assertGreater(len(report['files']),5)
    def test_staging_authority_required(self):
        with tempfile.TemporaryDirectory()as t:
            p=Path(t);(p/'Assets').mkdir();(p/'ProjectSettings').mkdir();(p/'ProjectSettings/ProjectVersion.txt').write_text('probe')
            with self.assertRaises(PermissionError):stage(ROOT,p,True,False)
    def test_staging_never_overwrites(self):
        with tempfile.TemporaryDirectory()as t:
            p=Path(t);(p/'Assets/ITPUPrepared').mkdir(parents=True);(p/'ProjectSettings').mkdir();(p/'ProjectSettings/ProjectVersion.txt').write_text('probe')
            with self.assertRaises(ValueError):staging_plan(ROOT,p)
    def test_staging_open_project_rejected(self):
        with tempfile.TemporaryDirectory()as t:
            p=Path(t);(p/'Assets').mkdir();(p/'ProjectSettings').mkdir();(p/'ProjectSettings/ProjectVersion.txt').write_text('probe');(p/'Temp').mkdir();(p/'Temp/UnityLockfile').write_text('probe')
            with self.assertRaises(ValueError):staging_plan(ROOT,p)
    def test_staging_isolated_apply_has_no_metadata(self):
        with tempfile.TemporaryDirectory()as t:
            p=Path(t);(p/'Assets').mkdir();(p/'ProjectSettings').mkdir();(p/'ProjectSettings/ProjectVersion.txt').write_text('SYNTHETIC TEST FIXTURE')
            r=stage(ROOT,p,True,True);self.assertEqual(r['status'],'STAGED_NOT_COMPILED');self.assertFalse(list(p.rglob('*.meta')));self.assertFalse(list((p/'Assets').rglob('*.csproj')))
    def rows(self):return [{'elapsed_s':i,'cpu_ms':8,'gpu_ms':9,'dropped_frames':0}for i in range(10)]
    def test_metric_budget_independent(self):
        r=analyze(self.rows());self.assertAlmostEqual(r['budget_ms'],1000/90);self.assertEqual(r['sampled_cpu_over_budget'],0);self.assertEqual(r['sampled_gpu_over_budget'],0)
    def test_metric_spike_visible(self):
        rows=self.rows();rows[-1]['gpu_ms']=40;r=analyze(rows);self.assertGreater(r['gpu']['p99_ms'],35);self.assertEqual(r['sampled_gpu_over_budget'],1)
    def test_metric_unknown_schema_rejected(self):
        with self.assertRaises(ValueError):analyze([{'fps':90}]*2)
    def test_metric_nonmonotonic_rejected(self):
        rows=self.rows();rows[-1]['elapsed_s']=0
        with self.assertRaises(ValueError):analyze(rows)
    def test_metric_fractional_dropped_rejected(self):
        rows=self.rows();rows[1]['dropped_frames']=.5
        with self.assertRaises(ValueError):analyze(rows)
    def test_metric_single_sample_rejected(self):
        with self.assertRaises(ValueError):analyze(self.rows()[:1])
    def test_metric_nan_rejected(self):
        rows=self.rows();rows[0]['gpu_ms']='NaN'
        with self.assertRaises(ValueError):analyze(rows)
    def test_metric_no_false_release_claim(self):
        self.assertIn('NOT_RELEASE',analyze(self.rows())['status'])
# One test per generator type: repeated deterministic semantic plans, not 30 .blend renders.
def generator_case(kind):
    def check(self):
        r=self.sample(kind);a=build_plan(r,True);b=build_plan(r,True);self.assertEqual(a,b)
        names=[o['name']for o in a['objects']];self.assertEqual(len(names),len(set(names)))
        for o in a['objects']:
            if o.get('parent'):self.assertIn(o['parent'],names)
            if o['shape']!='empty':self.assertIn(o['material'],a['materials']);self.assertTrue(all(v>0 for v in o['dimensions_m']))
    return check
for kind in SUPPORTED:setattr(PreparationTests,'test_generator_'+kind,generator_case(kind))
if __name__=='__main__':unittest.main()
