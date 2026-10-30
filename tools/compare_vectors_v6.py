"""Compare actual JSON outputs of run_vectors.cjs and C# --vectors runner. No fake C# result."""
import argparse,json
from pathlib import Path

def compare(a,b):
 if {a.get('implementation'),b.get('implementation')}!={'JS','CSharp'}:raise ValueError('Need actual outputs from both implementations')
 aa={x['id']:x for x in a['results']};bb={x['id']:x for x in b['results']}
 if len(aa)!=len(a['results'])or len(bb)!=len(b['results']):raise ValueError('Duplicate result ID')
 if aa.keys()!=bb.keys():raise ValueError('Result set mismatch')
 issues=[k for k in aa if aa[k]['actual']!=bb[k]['actual']or aa[k]['status']!='PASS'or bb[k]['status']!='PASS']
 return {'status':'PASS'if not issues else'FAIL','cases':len(aa),'mismatches':issues,'scope':'These supplied cases only, not complete engine or runtime parity'}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('js',type=Path);p.add_argument('csharp',type=Path);a=p.parse_args();r=compare(json.loads(a.js.read_text()),json.loads(a.csharp.read_text()));print(json.dumps(r,indent=2));raise SystemExit(r['status']!='PASS')
