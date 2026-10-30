"""Conservative sampled circle-vs-AABB route clearance for the DRAFT mesh.
Not Unity colliders, navigation or a human comfort test. Main route and kitchen probe only.
"""
from pathlib import Path
import json,math
from build_space_v5 import ROOT,scene_plan,mesh

def check():
 p=scene_plan();obs=[]
 for o in p['parts']:
  if o['shape']=='empty'or o['layer']in('floor','ceiling','services'):continue
  v,_=mesh(o);a=[min(x[k]for x in v)for k in range(3)];b=[max(x[k]for x in v)for k in range(3)]
  if b[2]>.08 and a[2]<1.80:obs.append((o['name'],a,b))
 routes={'main':p['layout']['route_points_m'],'403_entry':[[3.35,10.4,0],[5.01,10.4,0]],'kitchen_coffee':[[15.43,16.88,0],[17.1,16.88,0]]};issues=[];clearance=100.;samples=0;radius=.28
 for name,pts in routes.items():
  for aa,bb in zip(pts,pts[1:]):
   steps=max(1,math.ceil(math.dist(aa[:2],bb[:2])/.10))
   for j in range(steps+1):
    xy=[aa[k]+(bb[k]-aa[k])*j/steps for k in range(2)];samples+=1
    for id,a,b in obs:
     dx=max(a[0]-xy[0],xy[0]-b[0],0);dy=max(a[1]-xy[1],xy[1]-b[1],0);d=math.hypot(dx,dy);clearance=min(clearance,d)
     if d<radius-1e-7:issues.append({'route':name,'point':xy,'object':id,'distance':d})
 return {'status':'PASS'if not issues else'FAIL','sampled_points':samples,'circle_radius_m':radius,'min_center_to_obstacle_m':clearance,'issues':issues[:30],'limitations':['sampled paths, not continuous sweep','AABB approximation, not authored collider','niche NPC/panel fit deliberately excluded and pending','not human reach or Quest acceptance']}
if __name__=='__main__':
 r=check();(ROOT/'reports/environment_v5/route_check.json').write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2));raise SystemExit(0 if r['status']=='PASS'else 1)
