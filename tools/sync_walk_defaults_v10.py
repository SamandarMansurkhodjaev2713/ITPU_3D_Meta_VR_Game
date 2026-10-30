"""One source for JS/C# controller defaults. --check never changes source.
Generated constants do not prove compiled C# or Quest comfort/performance.
"""
from __future__ import annotations
import argparse,json,math,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
MAP=[('speed_mps','speed','Speed'),('carrying_speed_mps','carrySpeed','CarrySpeed'),
 ('backward_scale','backward','Backward'),('strafe_scale','strafe','Strafe'),
 ('radial_deadzone','deadzone','Deadzone'),('acceleration_mps2','acceleration','Acceleration'),
 ('max_frame_seconds','maxDt','MaxDt'),('input_max_age_seconds','maxAge','MaxAge'),
 ('turn_trigger','turnOn','TurnOn'),('turn_release','turnOff','TurnOff'),
 ('snap_degrees','snap','Snap'),('turn_cooldown_seconds','turnCooldown','TurnCooldown'),
 ('smooth_turn_degrees_per_second','smoothDps','SmoothDps')]

def values(d):
    if type(d.get('schema_version')) is not int or d['schema_version']!=1:raise ValueError('bad schema')
    v={}
    for key,js,cs in MAP:
        x=d.get(key)
        if type(x) not in (int,float) or not math.isfinite(x):raise ValueError('invalid '+key)
        v[js]=x
    if not (0<v['carrySpeed']<=v['speed']<=3 and 0<v['backward']<=1 and 0<v['strafe']<=1 and 0<=v['deadzone']<1 and 0<v['acceleration']<=30 and 0<v['maxDt']<=.2 and 0<v['maxAge']<=1 and 0<=v['turnOff']<v['turnOn']<=1 and 0<v['snap']<=90 and 0<=v['turnCooldown']<=5 and 0<v['smoothDps']<=180):raise ValueError('unsafe/invalid walk profile')
    return v

def expected(root):
    d=json.loads((root/'data/controller_locomotion_v9.json').read_text(encoding='utf-8'));v=values(d)
    jsfile=root/'src/reference/controller_walk_v9.js';old=jsfile.read_text(encoding='utf-8')
    pat=r'const DEFAULTS=Object\.freeze\(\{[^\n]*?\}\);'
    if len(re.findall(pat,old))!=1:raise ValueError('expected exactly one JS defaults declaration')
    js=re.sub(pat,'const DEFAULTS=Object.freeze('+json.dumps(v,separators=(',',':'))+');',old)
    cs=['// Generated from data/controller_locomotion_v9.json by sync_walk_defaults_v10.py.',
      '// Do not edit constants by hand. Regenerate, compile C#, run parity and device checks.',
      'namespace ITPU.Domain','{','    public static class ControllerWalkDefaults','    {']
    for key,j,c in MAP:cs.append('        public const double '+c+' = '+repr(float(v[j]))+';')
    cs+=['    }','}']
    return {jsfile:js,root/'src/domain/ControllerWalkDefaults.Generated.cs':'\n'.join(cs)+'\n'}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=ROOT);p.add_argument('--check',action='store_true');a=p.parse_args()
    try:
        out=expected(a.root.resolve());drift=[]
        for path,txt in out.items():
            if not path.exists() or path.read_text(encoding='utf-8')!=txt:
                drift.append(str(path.relative_to(a.root.resolve())))
                if not a.check:path.write_text(txt,encoding='utf-8')
        if a.check and drift:print('FAIL: defaults drift '+', '.join(drift));return 1
        print('PASS: JS/C# defaults match JSON' if a.check else 'Generated shared defaults: '+', '.join(drift));return 0
    except (ValueError,OSError,KeyError,TypeError) as e:print('FAIL: '+str(e));return 2
if __name__=='__main__':raise SystemExit(main())
