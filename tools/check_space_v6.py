"""Continuous planar segment/AABB clearance + mesh/UV/contact/hierarchy checks.
Neither world-space AABBs nor a cylinder radius establish headset comfort or real colliders.
"""
from __future__ import annotations
import argparse,json,math
from pathlib import Path
from build_space_v6 import ROOT,OUT,rot,add,sub,normal

def point_segment(p,a,b):
 d=sub(b,a);den=sum(x*x for x in d);t=0 if not den else max(0,min(1,sum(x*y for x,y in zip(sub(p,a),d))/den))
 return math.dist(p,[x+t*y for x,y in zip(a,d)])
def segment_intersects_rect(a,b,lo,hi):
 t0,t1=0.,1.
 for k in range(2):
  d=b[k]-a[k]
  if abs(d)<1e-12:
   if a[k]<lo[k]or a[k]>hi[k]:return False
  else:
   x,y=(lo[k]-a[k])/d,(hi[k]-a[k])/d
   if x>y:x,y=y,x
   t0=max(t0,x);t1=min(t1,y)
   if t0>t1:return False
 return True

def segment_rect_distance(a,b,lo,hi):
 a,b=a[:2],b[:2];lo,hi=lo[:2],hi[:2]
 if segment_intersects_rect(a,b,lo,hi):return 0.
 def pd(p):return math.hypot(max(lo[0]-p[0],p[0]-hi[0],0),max(lo[1]-p[1],p[1]-hi[1],0))
 corners=[[lo[0],lo[1]],[lo[0],hi[1]],[hi[0],lo[1]],[hi[0],hi[1]]]
 return min(pd(a),pd(b),*(point_segment(c,a,b)for c in corners))

def validate_snapshot(s):
 errors=[];roots={r['id']:r for r in s['roots']};ids=[];min_uv=float('inf');reconstruction=0.
 if len(roots)!=len(s['roots']):errors.append('duplicate_root')
 world={m['name']:m for m in s['meshes']}
 for m in s['nodes']:
  ids.append(m['name']);r=roots.get(m['root'])
  if r is None:errors.append('orphan:'+m['name']);continue
  if len(m['vertices'])!=len(m.get('uv0',[])):
   errors.append('missing_uv:'+m['name']);continue
  for v in m['vertices']+m['uv0']:
   if any(type(x)not in(int,float)or not math.isfinite(x)for x in v):errors.append('nonfinite:'+m['name'])
  for f in m['faces']:
   if len(f)!=3 or any(type(i)is not int or i<0 or i>=len(m['vertices'])for i in f):errors.append('bad_index:'+m['name']);continue
   try:normal(*(m['vertices'][i]for i in f))
   except ValueError:errors.append('degenerate_triangle:'+m['name']);continue
   a,b,c=[m['uv0'][i]for i in f];area=abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))/2;min_uv=min(min_uv,area)
   if area<1e-14:errors.append('zero_uv_area:'+m['name'])
  for v,w in zip(m['vertices'],world[m['name']]['vertices']):reconstruction=max(reconstruction,math.dist(add(rot(v,r['yaw_deg']),r['position_m']),w))
 for a in s['anchors']:
  ids.append(a['name']);r=roots[a['root']];reconstruction=max(reconstruction,math.dist(add(rot(a['local_position_m'],r['yaw_deg']),r['position_m']),a['location_m']))
 if len(ids)!=len(set(ids)):errors.append('duplicate_object_id')
 if reconstruction>1e-8:errors.append('local_world_mismatch')
 return {'status':'PASS'if not errors else'FAIL','errors':errors,'max_local_to_world_error_m':reconstruction,'min_uv_triangle_area':min_uv,'nodes_checked':len(s['nodes']),'roots_checked':len(roots)}

def contact_report(s):
 anchors={a['name']:a for a in s['anchors']};roots={r['id']:r for r in s['roots']}
 cup=roots['K_CUP'];socket=anchors['K_COFFEE__cup_socket']['location_m'];rim=anchors['K_CUP__rim_center']['local_position_m'];outer_radius=s['contact_spec']['cup_outer_radius_m'];inner_radius=outer_radius-s['contact_spec']['cup_wall_m']
 mouth=add(socket,rim);streams=[anchors['K_COFFEE__stream_'+str(i)]['location_m']for i in range(2)]
 results=[]
 for st in streams:results.append({'xy_offset_m':math.dist(st[:2],mouth[:2]),'height_above_rim_m':st[2]-mouth[2],'inside_opening':math.dist(st[:2],mouth[:2])+s['contact_spec']['stream_radius_m']<inner_radius})
 center_error=math.dist(socket[:2],anchors['K_COFFEE__stream_origin']['location_m'][:2])
 by_name={m['name']:m for m in s['meshes']}
 def bounds(name):
  vertices=by_name[name]['vertices'];return [min(v[i]for v in vertices)for i in range(3)],[max(v[i]for v in vertices)for i in range(3)]
 body=by_name['K_COFFEE__body'];front=min(v[1]for v in body['vertices']);cavity_clear=mouth[1]+outer_radius<front
 cup_vertices=[add(socket,v) for n in s['nodes']if n['root']=='K_CUP'for v in n['vertices']]
 cup_lo=[min(v[i]for v in cup_vertices)for i in range(3)];cup_hi=[max(v[i]for v in cup_vertices)for i in range(3)]
 tray_lo,tray_hi=bounds('K_COFFEE__tray');tray_gap=socket[2]-tray_hi[2];tray_support=0<=tray_gap<=.003 and tray_lo[0]<=socket[0]<=tray_hi[0] and tray_lo[1]<=socket[1]<=tray_hi[1]
 solid_clearances={}
 for name in ['K_COFFEE__body','K_COFFEE__pillar_0','K_COFFEE__pillar_1']:
  lo,hi=bounds(name);separations=[max(lo[i]-cup_hi[i],cup_lo[i]-hi[i]) for i in range(3)];solid_clearances[name]=max(separations)
 overhead={}
 for name in ['K_COFFEE__panel','K_COFFEE__display','K_COFFEE__spout','K_COFFEE__nozzle_bridge','K_COFFEE__nozzle_0','K_COFFEE__nozzle_1']:
  lo,_=bounds(name);overhead[name]=lo[2]-cup_hi[2]
 clear=all(v>=0 for v in solid_clearances.values())and all(v>.015 for v in overhead.values())
 passed=center_error<1e-9 and cavity_clear and tray_support and clear and all(r['inside_opening']and r['height_above_rim_m']>.025 for r in results)
 return {'status':'PASS'if passed else'FAIL','socket_center_xy_error_m':center_error,'two_streams':results,'cup_rear_body_clearance_m':front-(mouth[1]+outer_radius),'tray_top_to_cup_base_gap_m':tray_gap,'tray_support':tray_support,'solid_clearance_m':solid_clearances,'overhead_clearance_m':overhead,'note':'Cup evaluated in docked pose, not its separate home position; no liquid or physics simulated.'}

def route_report(s,layout):
 obstacles=[]
 for m in s['meshes']:
  if m['layer']in('floor','services','ceiling'):continue
  lo=[min(v[k]for v in m['vertices'])for k in range(3)];hi=[max(v[k]for v in m['vertices'])for k in range(3)]
  if hi[2]>.08 and lo[2]<1.8:obstacles.append((m['name'],lo,hi))
 routes={'main':layout['route_points_m'],'403_entry':[[3.35,10.4,0],[5.01,10.4,0]],'kitchen_coffee':[[15.43,16.88,0],[17.1,16.88,0]]}
 issues=[];minimum=float('inf');pairs=0;radius=.28
 for rid,points in routes.items():
  for n,(a,b)in enumerate(zip(points,points[1:])):
   for name,lo,hi in obstacles:
    d=segment_rect_distance(a,b,lo,hi);minimum=min(minimum,d);pairs+=1
    if d<radius-1e-8:issues.append({'route':rid,'segment':n,'object':name,'distance_m':d})
 return {'status':'PASS'if not issues else'FAIL','continuous_segment_obstacle_pairs':pairs,'radius_m':radius,'minimum_center_clearance_m':minimum,'issues':issues,'limitations':['2D continuous cylinder vs render AABB, not Unity colliders','height band0.08..1.8m only','does not prove free floor support, hand reach or physical booth safety','niche fitting and teleport validity require local acceptance']}

def check(folder=OUT):
 s=json.loads((folder/'scene_snapshot.json').read_text());p=json.loads((folder/'scene_recipe.json').read_text());return {'geometry':validate_snapshot(s),'coffee':contact_report(s),'routes':route_report(s,p['layout'])}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--folder',type=Path,default=OUT);a=p.parse_args();r=check(a.folder);(a.folder/'validation_report.json').write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2));raise SystemExit(0 if all(x['status']=='PASS'for x in r.values())else 1)
