"""Pure-Python planning for deterministic Blender recipes. NO Blender execution here.
One plan drives the Blender builder. Synthetic dimensions may test tools, never replace survey data.
"""
from __future__ import annotations
import copy, math, re
SUPPORTED = ('wall_segment','door_facade','worktable','niche_table','niche_arch','niche_seat',
             'cabinet_bank','yellow_cabinet','island','window_bar','coffee_machine','cup','flipchart',
             'marker_set','paper_set','bin','column','linear_light','pendant','badge','guide_panel',
             'signage','glass_front','bar_stool','office_chair','wall_panel','reception_desk','sink','microwave','guide_marker')
# Shared contract, not a second copy of palette values.
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools'))
from pbr_contract import load_library
MATERIALS = load_library()['materials']

def finite(value,name):
 if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):raise ValueError(f'{name}: finite numeric value required')
 return float(value)
def ident(x):
 if not isinstance(x,str) or not re.fullmatch(r'[A-Za-z][A-Za-z0-9_-]{0,63}',x):raise ValueError('Invalid recipe id/name')
 return x

def validate(recipe, allow_synthetic=False, allow_estimated=False):
 if not isinstance(recipe,dict):raise ValueError('Recipe must be an object')
 ident(recipe.get('asset_id'));typ=recipe.get('generator_type')
 if typ not in SUPPORTED:raise ValueError('Unsupported generator: '+str(typ))
 provenance=recipe.get('dimensions_provenance')
 if provenance=='SYNTHETIC_TOOLCHAIN_PROBE':
  if not allow_synthetic:raise ValueError('Synthetic dimensions require explicit --allow-synthetic; not campus geometry')
 elif provenance=='OWNER_AUTHORIZED_DESIGN_ESTIMATE':
  if not allow_estimated:raise ValueError('Design estimates require explicit allow_estimated')
  if not recipe.get('source_ids') or not recipe.get('dimensions_evidence'):raise ValueError('Estimated dimensions require evidence and owner instruction')
  if recipe.get('level','prototype')!='prototype':raise ValueError('Unaccepted estimate is prototype-only')
 elif provenance!='OWNER_MEASURED_OR_VERIFIED_PLAN':raise ValueError('Metric generation blocked: unverified dimensions')
 if provenance=='OWNER_MEASURED_OR_VERIFIED_PLAN' and (not recipe.get('source_ids') or not recipe.get('dimensions_evidence')):raise ValueError('Missing measured source/evidence')
 dims=recipe.get('dimensions_m')
 if not isinstance(dims,list) or len(dims)!=3:raise ValueError('dimensions_m must be [width, depth, height]')
 for i,x in enumerate(dims):
  if not .0001<=finite(x,f'dimension {i}')<=100:raise ValueError('Dimension outside safe range')
 if recipe.get('level','prototype') not in ('prototype','production'):raise ValueError('Invalid level')
 if recipe.get('level')=='production' and provenance=='SYNTHETIC_TOOLCHAIN_PROBE':raise ValueError('Synthetic production forbidden')
 bevel=finite(recipe.get('bevel_m',.002),'bevel')
 if not 0<=bevel<=min(dims)*.2:raise ValueError('Bevel too large')
 return copy.deepcopy(recipe)

def build_plan(recipe,allow_synthetic=False,allow_estimated=False):
 r=validate(recipe,allow_synthetic,allow_estimated);aid=r['asset_id'];w,d,h=map(float,r['dimensions_m']);typ=r['generator_type'];objects=[];bevel=r.get('bevel_m',.002)
 def part(name,shape,size,loc,mat,**extra):
  ident(name)
  if mat not in MATERIALS:raise ValueError('Unknown material')
  for v in list(size)+list(loc):finite(v,'object coordinate')
  if any(x<=0 for x in size):raise ValueError('Degenerate component')
  o={'name':aid+'__'+name,'shape':shape,'dimensions_m':list(size),'location_m':list(loc),'rotation_deg':[0,0,0],'material':mat,'bevel_m':min(bevel,min(size)*.1),'purpose':'render','parent':None}
  o.update(extra);objects.append(o);return o
 def box(n,s,l,m,**kw):return part(n,'box',s,l,m,**kw)
 def cyl(n,radius,depth,l,m,**kw):
  segments=kw.pop('segments',24);return part(n,'cylinder',[radius*2,radius*2,depth],l,m,segments=segments,**kw)
 def empty(n,l,**kw):
  o={'name':aid+'__'+n,'shape':'empty','location_m':list(l),'rotation_deg':[0,0,0],'purpose':'anchor','parent':None};o.update(kw);objects.append(o);return o
 if typ in ('wall_segment','column','wall_panel'):
  box('body',[w,d,h],[0,0,h/2],'oak' if typ=='wall_panel' else 'warm_white')
 elif typ in ('worktable','niche_table','island','window_bar','reception_desk'):
  thick=min(.05,h*.1);box('top',[w,d,thick],[0,0,h-thick/2],'oak' if typ=='worktable' else 'warm_white')
  if typ in ('island','reception_desk'):box('base',[w*.9,d*.85,h-thick],[0,0,(h-thick)/2],'oak' if typ=='reception_desk' else 'warm_white')
  else:
   leg=min(.055,w*.1,d*.1)
   for ix,x in enumerate([-w*.42,w*.42]):
    for iy,y in enumerate([-d*.4,d*.4]):box(f'leg_{ix}_{iy}',[leg,leg,h-thick],[x,y,(h-thick)/2],'frame')
  empty('surface',[0,0,h]);empty('interaction_front',[0,-d/2-.15,h])
 elif typ=='door_facade':
  jamb=min(.065,w*.07,h*.05);leafw=w-2*jamb;leafh=h-jamb
  box('jamb_L',[jamb,d,h],[-(w-jamb)/2,0,h/2],'frame');box('jamb_R',[jamb,d,h],[(w-jamb)/2,0,h/2],'frame');box('header',[w,d,jamb],[0,0,h-jamb/2],'frame')
  hinge=empty('hinge',[-leafw/2,0,0]);box('leaf',[leafw,d*.4,leafh],[0,0,leafh/2],'frosted_proxy',parent=hinge['name']);box('handle',[.11,.025,.025],[leafw*.32,-d*.45,h*.47],'frame',parent=hinge['name']);empty('handle_anchor',[leafw*.32,-d*.48,h*.47]);
 elif typ=='glass_front':
  rail=min(.045,w*.05,h*.04);box('mullion_L',[rail,d,h],[-(w-rail)/2,0,h/2],'frame');box('mullion_R',[rail,d,h],[(w-rail)/2,0,h/2],'frame');box('frosted_band',[w-rail*2,d*.3,h*.48],[0,0,h*.46],'frosted_proxy');
  # No hidden office interior, and no invented transparent shader during this probe.
  box('upper_proxy',[w-rail*2,d*.2,h*.28],[0,0,h*.82],'frosted_proxy');box('lower_proxy',[w-rail*2,d*.2,h*.22],[0,0,h*.11],'frosted_proxy')
 elif typ=='niche_arch':
  t=min(w*.10,.12);radius=(w-2*t)/2;spring=h-radius-t
  box('pier_L',[t,d,spring],[-(w-t)/2,0,spring/2],'warm_white');box('pier_R',[t,d,spring],[(w-t)/2,0,spring/2],'warm_white')
  o=part('arch','arch',[w,d,radius+t],[0,0,spring],'warm_white',inner_radius_m=radius,thickness_m=t,segments=24)
  empty('niche_origin',[0,0,0])
 elif typ=='niche_seat':
  box('base',[w,d,h*.35],[0,0,h*.175],'dark');box('seat',[w,d,h*.16],[0,-d*.03,h*.43],'fabric');box('back',[w,d*.13,h*.52],[0,d*.44,h*.74],'yellow')
 elif typ in ('cabinet_bank','yellow_cabinet'):
  mat='yellow'if typ=='yellow_cabinet'else'oak';box('body',[w,d,h],[0,0,h/2],mat)
  for i in range(3):box(f'front_{i}',[w/3*.98,.015,h*.96],[-w/3+i*w/3,-d/2-.008,h/2],mat)
 elif typ=='coffee_machine':
  box('body',[w,d*.92,h*.87],[0,d*.02,h*.51],'dark',bevel_m=min(.014,w*.025));box('upper',[w*.93,d*.94,h*.16],[0,0,h*.91],'frame',bevel_m=min(.012,w*.02));box('panel',[w*.88,.012,h*.30],[0,-d*.49,h*.72],'frame',bevel_m=.004);box('display',[w*.48,.006,h*.11],[0,-d*.52,h*.76],'dark',bevel_m=.002)
  box('tray',[w*.91,d*.54,h*.05],[0,-d*.21,h*.035],'steel');box('spout',[w*.35,d*.22,h*.17],[0,-d*.46,h*.39],'frame',bevel_m=.005)
  for i,x in enumerate([-w*.09,w*.09]):cyl(f'nozzle_{i}',w*.018,h*.10,[x,-d*.52,h*.27],'frame')
  empty('cup_socket',[0,-d*.26,h*.07]);empty('stream_origin',[0,-d*.52,h*.24]);empty('button_anchor',[w*.29,-d*.54,h*.7])
 elif typ=='cup':
  radius=min(w*.36,d*.48);wall=min(.006,radius*.12);part('vessel','vessel',[radius*2,radius*2,h],[0,0,0],'ceramic',radius_m=radius,wall_m=wall,segments=32)
  part('handle','torus',[h*.58,h*.58,wall*2.5],[radius+h*.16,0,h*.54],'ceramic',major_radius_m=h*.22,minor_radius_m=wall*1.25,rotation_deg=[90,0,0])
  empty('liquid_surface',[0,0,h*.84]);empty('grip_left',[-radius,0,h*.5]);empty('grip_right',[radius+h*.14,0,h*.5])
 elif typ=='flipchart':
  boardh=h*.6;box('board',[w,.03,boardh],[0,0,h-boardh/2],'warm_white');box('frame',[w*1.06,.022,boardh*1.05],[0,.018,h-boardh/2],'frame');
  for i,x in enumerate([-w*.3,w*.3]):box(f'leg_{i}',[.025,.025,h-boardh],[x,0,(h-boardh)/2],'frame')
  box('base',[w*.9,d,.04],[0,0,.02],'frame');empty('drawing_plane',[0,-.02,h-boardh/2]);empty('marker_tray',[0,-.08,h-boardh])
 elif typ=='marker_set':
  cyl('marker',min(w,d)*.25,h,[0,0,h/2],'warm_white');cyl('tip',min(w,d)*.12,h*.08,[0,0,h*1.04],'dark');box('eraser',[w,d,h*.22],[w*1.7,0,h*.11],'fabric');empty('tip_contact',[0,0,h*1.08])
 elif typ=='paper_set':
  box('sheet',[w,d,min(h,.0015)],[0,0,.001],'warm_white');part('crumple','sphere',[w*.25,w*.23,w*.26],[w*.8,0,w*.13],'warm_white',segments=12,rings=8)
 elif typ=='bin':
  radius=min(w,d)/2;part('container','vessel',[radius*2,radius*2,h],[0,0,0],'frame',radius_m=radius,wall_m=min(.008,radius*.08),segments=24);empty('mouth',[0,0,h]);empty('inside',[0,0,h*.35])
 elif typ in ('linear_light','pendant'):
  if typ=='linear_light':box('housing',[w,d,h],[0,0,h/2],'frame');box('diffuser',[w*.98,d*.98,h*.10],[0,0,h*.04],'warm_white')
  else:
   diameter=min(w,d,h*.7);part('globe','sphere',[diameter]*3,[0,0,diameter/2],'warm_white',segments=24,rings=12);cyl('stem',.004,max(.01,h-diameter),[0,0,diameter+(h-diameter)/2],'frame')
 elif typ in ('badge','guide_panel','signage'):
  box('frame',[w,d,h],[0,0,h/2],'frame');box('face',[w*.94,d*.12,h*.93],[0,-d*.57,h/2],'accent' if typ=='guide_panel'else'warm_white');empty('ui_plane',[0,-d*.65,h/2])
 elif typ in ('office_chair','bar_stool'):
  seat=h*.55 if typ=='office_chair'else h*.8;cyl('stem',min(w,d)*.07,seat*.92,[0,0,seat*.46],'frame')
  if typ=='office_chair':
   sw,sd,sh=w*.94,d*.88,h*.08
   seat_profile=[[-sw*.42,-sh*.50],[sw*.42,-sh*.50],[sw*.50,-sh*.22],[sw*.46,sh*.30],[sw*.37,sh*.50],[-sw*.37,sh*.50],[-sw*.46,sh*.30],[-sw*.50,-sh*.22]]
   part('seat','convex_profile',[sw,sd,sh],[0,-d*.015,seat],'fabric',profile_xz=seat_profile)
   bw,bd,bh=w*.86,d*.105,h*.38
   back_profile=[[-bw*.42,-bh*.50],[bw*.42,-bh*.50],[bw*.50,-bh*.34],[bw*.46,bh*.40],[bw*.36,bh*.50],[-bw*.36,bh*.50],[-bw*.46,bh*.40],[-bw*.50,-bh*.34]]
   part('back','convex_profile',[bw,bd,bh],[0,d*.40,seat+h*.205],'fabric',profile_xz=back_profile,rotation_deg=[-8,0,0])
   for side,x in [('L',-w*.43),('R',w*.43)]:
    box('arm_support_'+side,[.032,.032,h*.20],[x,-d*.02,seat+h*.10],'frame',bevel_m=.003)
    box('armrest_'+side,[.045,d*.46,.034],[x,-d*.02,seat+h*.205],'frame',bevel_m=.006)
  else:box('seat',[w,d,h*.08],[0,0,seat],'fabric',bevel_m=min(.018,h*.025))
  for i in range(5):
   a=i*2*math.pi/5;leg=box(f'base_{i}',[w*.48,.025,.02],[math.cos(a)*w*.22,math.sin(a)*w*.22,.04],'frame');leg['rotation_deg'][2]=math.degrees(a)
   if typ=='office_chair':cyl(f'caster_{i}',w*.033,.026,[math.cos(a)*w*.43,math.sin(a)*w*.43,.028],'frame',rotation_deg=[90,0,math.degrees(a)],segments=12)
 elif typ=='sink':
  box('sink_back',[w,d*.1,h*.3],[0,d*.45,h*.15],'frame');box('sink_floor',[w,d,.02],[0,0,.01],'frame');box('faucet_stem',[.025,.025,h],[0,d*.35,h/2],'frame');box('faucet_spout',[.025,d*.45,.025],[0,d*.14,h-.0125],'frame')
 elif typ=='microwave':
  box('body',[w,d,h],[0,0,h/2],'frame');box('front',[w*.78,.02,h*.8],[-w*.08,-d*.51,h/2],'dark');box('controls',[w*.14,.023,h*.8],[w*.4,-d*.52,h/2],'dark')
 elif typ=='guide_marker':part('marker','sphere',[w,d,h],[0,0,h/2],'accent',segments=16,rings=8)
 else:raise ValueError('Unimplemented generator branch')
 names=[o['name']for o in objects]
 if len(names)!=len(set(names)):raise ValueError('Duplicate generated object names')
 for o in objects:
  if o.get('parent')and o['parent']not in names:raise ValueError('Missing parent')
 return {'schema_version':1,'asset_id':aid,'generator_type':typ,'namespace':'ITPU_GEN_'+aid,'provenance':r['dimensions_provenance'],'intended_dimensions_m':[w,d,h],'materials':copy.deepcopy(MATERIALS),'objects':objects,'status':'PLAN_ONLY_NOT_A_BLEND_OR_RENDER','coordinate_system':'Blender Z up; front faces -Y','seed':0,'notes':['Dimensions of composite props may extend for handles/anchors; compare intended footprint separately.','Prototype geometry needs human visual approval; no claim of final likeness or measured fit.']}
