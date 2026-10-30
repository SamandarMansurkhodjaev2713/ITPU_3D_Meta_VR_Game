"""Analyze explicitly normalized Quest samples; never guesses vendor CSV column names."""
from __future__ import annotations
import argparse,csv,json,math
from pathlib import Path
REQUIRED=('elapsed_s','cpu_ms','gpu_ms','dropped_frames')
def number(v):
    if isinstance(v,bool):raise ValueError('Boolean is not a metric')
    x=float(v)
    if not math.isfinite(x) or x<0:raise ValueError('Metric must be finite and nonnegative')
    return x
def percentile(xs,p):
    ys=sorted(xs);i=(len(ys)-1)*p;k=int(i);return ys[k]+(ys[min(k+1,len(ys)-1)]-ys[k])*(i-k)
def analyze(rows,refresh=90):
    refresh=number(refresh)
    if refresh<=0 or refresh>200:raise ValueError('Invalid refresh rate')
    if len(rows)<2:raise ValueError('Need at least two samples, not a screenshot')
    parsed=[]
    for row in rows:
        if not all(k in row for k in REQUIRED):raise ValueError('Normalized schema missing columns')
        q={k:number(row[k])for k in REQUIRED}
        if q['dropped_frames']!=int(q['dropped_frames']):raise ValueError('Dropped frames must be per-sample nonnegative integers')
        parsed.append(q)
    if any(b['elapsed_s']<=a['elapsed_s']for a,b in zip(parsed,parsed[1:])):raise ValueError('Times must strictly increase')
    budget=1000/refresh
    return {'status':'METRICS_SUMMARY_NOT_RELEASE_APPROVAL','refresh_hz':refresh,'budget_ms':budget,'samples':len(parsed),'duration_s':parsed[-1]['elapsed_s']-parsed[0]['elapsed_s'],'dropped_frames_sum':int(sum(x['dropped_frames']for x in parsed)),'cpu':{k:percentile([x['cpu_ms']for x in parsed],p)for k,p in [('p50_ms',.5),('p95_ms',.95),('p99_ms',.99)]},'gpu':{k:percentile([x['gpu_ms']for x in parsed],p)for k,p in [('p50_ms',.5),('p95_ms',.95),('p99_ms',.99)]},'sampled_cpu_over_budget':sum(x['cpu_ms']>budget for x in parsed),'sampled_gpu_over_budget':sum(x['gpu_ms']>budget for x in parsed),'limitations':'Samples are not guaranteed to be every frame. Percentiles describe provided samples only; CPU and GPU are not added. Record source mapping, build, tool, thermal conditions and sample period separately.'}
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('csv',type=Path);p.add_argument('--refresh',type=float,default=90);a=p.parse_args()
    with a.csv.open(encoding='utf-8-sig',newline='')as f:r=list(csv.DictReader(f))
    print(json.dumps(analyze(r,a.refresh),ensure_ascii=False,indent=2))
