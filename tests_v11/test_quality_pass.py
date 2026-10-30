"""Synthetic fixtures only: no live models, native editors or owner approvals."""
import copy, hashlib, json, os, sys, tempfile, tomllib, unittest
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'tools'));sys.path.insert(0,str(ROOT))
import night_shift_v10 as n
from tests_v10.test_night_shift import Fixture
from review_bootstrap_v11 import validate_setup
from render_review_agent_v10 import render
import stage_unity_sources as st
from work_session import publish_alias

class Bootstrap(Fixture):
    def setup_plan(self):
        return dict(schema_version=11,host_binding=self.binding,checked_at=self.now,permission='STAGE_ONLY',
          model_mode='SOL_WITH_ASTRA_REVIEW',owner_setup_proof=self.probe('owner_reviewer_setup',scope='STAGE_LOCAL_CONFIG_ONLY'),
          model_catalog_proof=self.probe('model_catalog',available=[{'id':'fixture-reviewer','efforts':['medium']}]),
          models={'reviewer':{'id':'fixture-reviewer','effort':'medium'}})
    def test_stage_before_native_probe(self):
        p=self.setup_plan();self.assertNotIn('subagent_smoke',p)
        d=tomllib.loads(render(validate_setup(self.root,p,self.now,self.binding),['blender','unity']))
        self.assertEqual(d['model'],'fixture-reviewer');self.assertEqual(d['sandbox_mode'],'read-only')
        self.assertFalse(d['agents']['enabled']);self.assertFalse(d['mcp_servers']['blender']['enabled'])
    def test_staging_is_not_execution_ready(self):
        with self.assertRaises(ValueError):n.validate_ready(self.root,self.setup_plan(),self.now,self.binding)
    def test_permission_required(self):
        p=self.setup_plan();p['permission']='NOT_GRANTED'
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_wrong_scope(self):
        p=self.setup_plan();p['owner_setup_proof']=self.probe('owner_reviewer_setup',scope='SOMETHING_ELSE')
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_model_must_be_discovered(self):
        p=self.setup_plan();p['models']['reviewer']['id']='guessed-model'
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_effort_must_be_discovered(self):
        p=self.setup_plan();p['models']['reviewer']['effort']='ultra-unknown'
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_current_host(self):
        with self.assertRaises(ValueError):validate_setup(self.root,self.setup_plan(),self.now,'b'*64)
    def test_old_setup(self):
        with self.assertRaises(ValueError):validate_setup(self.root,self.setup_plan(),self.now+3601,self.binding)
    def test_future_setup(self):
        p=self.setup_plan();p['checked_at']=self.now+1
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_bool_schema(self):
        p=self.setup_plan();p['schema_version']=True
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_changed_catalog_hash(self):
        p=self.setup_plan();(self.root/p['model_catalog_proof']['path']).write_text('{}')
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_null_model(self):
        p=self.setup_plan();p['models']['reviewer']['id']=None
        with self.assertRaises(ValueError):validate_setup(self.root,p,self.now,self.binding)
    def test_names_are_toml_literals(self):
        p=self.setup_plan();d=tomllib.loads(render(p,['server.with.dot','server"quote']))
        self.assertFalse(d['mcp_servers']['server.with.dot']['enabled'])
        self.assertFalse(d['mcp_servers']['server"quote']['enabled'])

class ReviewCompletion(Fixture):
    def setUp(self):
        super().setUp();self.enable_review();self.init()
        self.ctx=self.put('reports/local/context.json',{'input':'immutable snapshot'})
        self.row=n.request_review(self.root,self.s,'FIRST_ENVIRONMENT_REFERENCE',[self.ctx['path']],self.now,self.binding)
    def receipt(self,status='DONE',**patches):
        response=self.put('reports/local/findings.json',{'findings':[],'scope':'synthetic test'})
        data=dict(schema_version=11,ticket_id=self.row['id'],host_binding=self.binding,disposition=status,
          observed_at=self.now+1,child_state='COMPLETED',actual_model_id=self.r['models']['reviewer']['id'],
          actual_effort=self.r['models']['reviewer']['effort'],response=response)
        data.update(patches);return self.put('reports/local/review-terminal.json',data)
    def close(self,ref,status='DONE',now=None):
        n.close_review(self.s,self.row['id'],status,self.now+1 if now is None else now,'synthetic observation',self.root,ref,self.binding)
    def test_valid_completion(self):
        self.close(self.receipt());self.assertEqual(self.row['status'],'DONE');self.assertTrue(self.row['advice_only'])
    def test_bare_done_is_rejected(self):
        with self.assertRaises(ValueError):n.close_review(self.s,self.row['id'],'DONE',self.now+1,'no evidence')
        self.assertEqual(self.row['status'],'OPEN')
    def test_still_running_cannot_release(self):
        with self.assertRaises(ValueError):self.close(self.receipt(child_state='RUNNING'))
        self.assertEqual(self.row['status'],'OPEN')
    def test_timeout_request_not_cancellation(self):
        with self.assertRaises(ValueError):self.close(self.receipt('TIMED_OUT',child_state='CANCEL_REQUESTED'),'TIMED_OUT')
    def test_timeout_confirmed_terminal(self):
        self.close(self.receipt('TIMED_OUT',child_state='CANCELLED'),'TIMED_OUT');self.assertEqual(self.row['status'],'TIMED_OUT')
    def test_identity_mismatch(self):
        with self.assertRaises(ValueError):self.close(self.receipt(actual_model_id='parent-sol'))
    def test_effort_mismatch(self):
        with self.assertRaises(ValueError):self.close(self.receipt(actual_effort='unsupported'))
    def test_changed_context_is_rejected(self):
        ref=self.receipt();(self.root/self.ctx['path']).write_text('{}')
        with self.assertRaises(ValueError):self.close(ref)
    def test_missing_response(self):
        with self.assertRaises(ValueError):self.close(self.receipt(response=None))
    def test_whitespace_response(self):
        p=self.root/'reports/local/empty.txt';p.write_text('  \n')
        with self.assertRaises(ValueError):self.close(self.receipt(response={'path':'reports/local/empty.txt','sha256':n.digest(p)}))
    def test_bool_ticket_is_rejected(self):
        with self.assertRaises(ValueError):self.close(self.receipt(ticket_id=True))
    def test_wrong_host(self):
        with self.assertRaises(ValueError):self.close(self.receipt(host_binding='x'))
    def test_old_observation(self):
        with self.assertRaises(ValueError):self.close(self.receipt(),now=self.now+302)
    def test_late_done_not_success(self):
        with self.assertRaises(ValueError):self.close(self.receipt(observed_at=self.now+1201),now=self.now+1201)
    def test_reclose_refused(self):
        ref=self.receipt();self.close(ref)
        with self.assertRaises(ValueError):self.close(ref)
    def test_findings_modified_after_completion_stale(self):
        self.close(self.receipt());(self.root/self.row['response']['path']).write_text('{}')
        n.check(self.root,self.s,self.now+2,self.binding);self.assertEqual(self.row['status'],'STALE')
    def test_unavailable_no_fake_model_needed(self):
        self.close(self.receipt('UNAVAILABLE',child_state='NOT_STARTED',actual_model_id=None,actual_effort=None,response=None),'UNAVAILABLE')
        self.assertEqual(self.row['status'],'UNAVAILABLE')
    def test_blender_profile_does_not_request_unity_review(self):
        self.r['profile']='BLENDER_ENVIRONMENT'
        self.r['launch_proof']=self.probe('owner_launch',profile=self.r['profile'],duration_minutes=420,model_mode=self.r['model_mode'])
        self.init()
        with self.assertRaises(ValueError):n.request_review(self.root,self.s,'FIRST_UNITY_XR',[self.ctx['path']],self.now,self.binding)
    def test_second_xr_reason_supported(self):
        self.close(self.receipt());p=n.request_review(self.root,self.s,'FIRST_UNITY_XR',[self.ctx['path']],self.now+2,self.binding)
        self.assertEqual(p['reason'],'FIRST_UNITY_XR')

class AtomicStaging(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.top=Path(self.tmp.name)
        self.root=self.top/'package';self.unity=self.top/'unity';(self.root/'src/domain').mkdir(parents=True)
        for name in ('A.cs','B.cs'):(self.root/'src/domain'/name).write_text('// fixture '+name)
        (self.unity/'Assets').mkdir(parents=True);(self.unity/'ProjectSettings').mkdir();(self.unity/'ProjectSettings/ProjectVersion.txt').write_text('fixture not an Editor')
        (self.unity/'Assets/existing.meta').write_text('keep me');self.dest=self.unity/'Assets/ITPUPrepared'
    def assert_clean(self):
        self.assertFalse(self.dest.exists());self.assertFalse((self.unity/'.itpu-source-stage.lock').exists())
        self.assertEqual(list(self.unity.glob('.itpu-stage-*')),[]);self.assertEqual((self.unity/'Assets/existing.meta').read_text(),'keep me')
    def test_dry_run_writes_nothing(self):
        self.assertEqual(st.stage(self.root,self.unity)['status'],'DRY_RUN');self.assert_clean()
    def test_no_authorization(self):
        with self.assertRaises(PermissionError):st.stage(self.root,self.unity,True,False)
        self.assert_clean()
    def test_full_stage_exact(self):
        self.assertEqual(st.stage(self.root,self.unity,True,True)['status'],'STAGED_NOT_COMPILED')
        for p in (self.root/'src/domain').glob('*.cs'):self.assertEqual(p.read_bytes(),(self.dest/'Domain'/p.name).read_bytes())
        self.assertEqual((self.unity/'Assets/existing.meta').read_text(),'keep me')
    def test_failure_second_copy_not_partial_final(self):
        original=st.shutil.copy2;count=0
        def fail(s,d):
            nonlocal count
            count+=1
            if count==2:raise OSError('simulated disk failure')
            return original(s,d)
        with patch.object(st.shutil,'copy2',side_effect=fail):
            with self.assertRaises(OSError):st.stage(self.root,self.unity,True,True)
        self.assert_clean()
    def test_late_source_mutation_aborts(self):
        original=st.shutil.copy2;count=0
        def mutate(s,d):
            nonlocal count
            result=original(s,d);count+=1
            if count==2:(self.root/'src/domain/A.cs').write_text('// late change')
            return result
        with patch.object(st.shutil,'copy2',side_effect=mutate):
            with self.assertRaises(ValueError):st.stage(self.root,self.unity,True,True)
        self.assert_clean()
    def test_editor_opening_during_copy_aborts(self):
        original=st.shutil.copy2
        def start_editor(s,d):
            result=original(s,d);(self.unity/'Temp').mkdir(exist_ok=True);(self.unity/'Temp/UnityLockfile').write_text('synthetic');return result
        with patch.object(st.shutil,'copy2',side_effect=start_editor):
            with self.assertRaises(ValueError):st.stage(self.root,self.unity,True,True)
        self.assert_clean()
    def test_existing_destination_not_overwritten(self):
        self.dest.mkdir();(self.dest/'keep.txt').write_text('keep')
        with self.assertRaises(ValueError):st.stage(self.root,self.unity,True,True)
        self.assertEqual((self.dest/'keep.txt').read_text(),'keep')
    def test_foreign_lock_not_removed(self):
        lock=self.unity/'.itpu-source-stage.lock';lock.write_text('other writer')
        with self.assertRaises(FileExistsError):st.stage(self.root,self.unity,True,True)
        self.assertEqual(lock.read_text(),'other writer');self.assertFalse(self.dest.exists())
    def test_parent_source_symlink_refused(self):
        outside=self.top/'outside';(self.root/'src/domain').rename(outside);(self.root/'src/domain').symlink_to(outside,target_is_directory=True)
        with self.assertRaises(ValueError):st.stage(self.root,self.unity,True,True)
        self.assert_clean()
    def test_reserved_windows_name_rejected(self):
        for name in ('CON','NUL','LPT1','COM9'):
            with self.subTest(name=name):
                with self.assertRaises(ValueError):st.staging_plan(self.root,self.unity,name)
    def test_no_sources_no_stage(self):
        for p in (self.root/'src/domain').glob('*.cs'):p.unlink()
        with self.assertRaises(ValueError):st.stage(self.root,self.unity,True,True)
        self.assert_clean()

class EntryAndReports(unittest.TestCase):
    def test_active_skills_read_current_instructions(self):
        skills=json.loads((ROOT/'config/skills.json').read_text())['skills']
        for row in skills:
            if row['name'] in ('itpu-night-shift','itpu-bounded-model-review'):
                self.assertFalse(any(p in ('docs/process/NIGHT_SHIFT_V10.md','docs/process/MODEL_ROUTING_V10.md') for p in row['read_paths']))
    def test_day_contract(self):
        from audit_v11 import audit
        r=audit(ROOT);self.assertEqual(r['failed'],0,[x for x in r['checks'] if x['status']=='FAIL'])
    def test_day_prompt_size(self):self.assertLess(len((ROOT/'prompts/GOAL_DAY_V11.txt').read_text()),4000)
    def test_neutral_report_alias(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);p=root/n.LOCAL/'MORNING_REPORT.md';p.parent.mkdir(parents=True);p.write_text('# Утренний результат\nfixture')
            self.assertTrue(publish_alias(root));self.assertIn('Результат рабочей сессии',(root/'reports/local/SESSION_REPORT.md').read_text())
    def test_alias_refuses_symlink(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);p=root/n.LOCAL/'MORNING_REPORT.md';p.parent.mkdir(parents=True);p.write_text('report')
            target=root/'other';target.write_text('keep');(root/'reports/local/SESSION_REPORT.md').symlink_to(target)
            with self.assertRaises(ValueError):publish_alias(root)
            self.assertEqual(target.read_text(),'keep')
    def test_missing_report_no_dummy(self):
        with tempfile.TemporaryDirectory() as d:self.assertFalse(publish_alias(Path(d)))

if __name__=='__main__':unittest.main()
