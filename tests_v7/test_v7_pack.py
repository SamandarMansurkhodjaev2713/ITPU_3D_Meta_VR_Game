import json,re,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class V7PackTests(unittest.TestCase):
 def test_version_state(self):
  s=json.loads((ROOT/'state/project_state.json').read_text());self.assertEqual(s['package_version'],json.loads((ROOT/'config/document_order.json').read_text())['version'])
 def test_auto_sources_exist(self):
  for p in ['src/reference/auto_acceptance.js','tests_v7/test_auto_acceptance.cjs','prototype/LOGIC_LAB.template.html','prototype/LOGIC_LAB_MANUAL.template.html','tools/browser_v7_checks.py','docs/product/29_AUTO_ACCEPTANCE.md','work_orders/WO-18.json']:
   self.assertTrue((ROOT/p).is_file(),p)
 def test_main_lab_is_auto(self):
  t=(ROOT/'prototype/LOGIC_LAB.html').read_text();self.assertIn('window.logicAcceptance',t);self.assertIn('Логика проверяется сама',t)
 def test_manual_lab_separate(self):
  t=(ROOT/'prototype/LOGIC_LAB_MANUAL.html').read_text();self.assertIn('labState',t);self.assertNotIn('window.logicAcceptance=last',t)
 def test_no_owner_manual_lab_requirement(self):
  for p in ['state/project_state.json','OWNER_NEXT_STEPS.md','docs/product/29_AUTO_ACCEPTANCE.md']:
   t=(ROOT/p).read_text().lower();self.assertNotIn('владелец обязан вручную',t)
 def test_skill_registered(self):
  d=json.loads((ROOT/'config/skills.json').read_text());self.assertIn('itpu-auto-behavior-acceptance',[x['name'] for x in d['skills']])
 def test_wo_schema(self):
  w=json.loads((ROOT/'work_orders/WO-18.json').read_text());
  for k in ['id','role','skill','depends_on','read_paths','allowed_write_paths','acceptance','stop_conditions']:self.assertIn(k,w)
  self.assertEqual(w['id'],'WO-18');self.assertGreaterEqual(w['context_limit_bytes'],60000)
 def test_doc_order_v7(self):
  d=json.loads((ROOT/'config/document_order.json').read_text());self.assertEqual(d['version'],json.loads((ROOT/'state/project_state.json').read_text())['package_version']);self.assertTrue(any(x['path'].endswith('29_AUTO_ACCEPTANCE.md') for x in d['sections']))
 def test_auto_lab_offline(self):
  t=(ROOT/'prototype/LOGIC_LAB.html').read_text();self.assertNotRegex(t,r'<script[^>]+src=');self.assertNotRegex(t,r'<link[^>]+href=["\']https?://')
 def test_builder_has_manual_and_auto(self):
  t=(ROOT/'tools/build_logic_lab.py').read_text();self.assertIn('LOGIC_LAB_MANUAL.html',t);self.assertIn('auto_acceptance.js',t)
 def test_start_prompt_no_manual_owner(self):
  t=(ROOT/'prompts/START_V7.txt').read_text().lower();self.assertIn('не проси владельца вручную',t)
 def test_engine_claims_unchanged(self):
  s=json.loads((ROOT/'state/project_state.json').read_text());self.assertFalse(s['runtime_implemented']);self.assertFalse(s['device_tested']);self.assertEqual(s['evidence']['Unity'],'NOT_RUN');self.assertEqual(s['evidence']['Quest'],'NOT_RUN')
if __name__=='__main__':unittest.main()
