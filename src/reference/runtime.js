/* ITPU semantic reference, not the Unity shipping runtime. No dependencies/network.
   The browser lab and Node tests use this exact file. C# parity is a separate gate. */
(function(root,factory){const api=factory(); if(typeof module==='object'&&module.exports)module.exports=api;else root.ITPU=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';
const num=(x,name='number')=>{if(typeof x!=='number'||!Number.isFinite(x))throw new Error(name+': finite number required');return x;};
const integer=(x,min,max,name)=>{num(x,name);if(!Number.isInteger(x)||x<min||x>max)throw new Error(name+': out of range');return x;};
const ident=x=>{if(typeof x!=='string'||!(/^[A-Za-z0-9_.-]{1,80}$/).test(x))throw new Error('Invalid identifier');return x;};
const clamp=(x,a,b)=>Math.min(b,Math.max(a,x));
const clone=x=>JSON.parse(JSON.stringify(x));
const deepFreeze=x=>{if(x&&typeof x==='object'){Object.values(x).forEach(deepFreeze);Object.freeze(x);}return x;};
class TraceBuffer{
 constructor(capacity=512,now=null){integer(capacity,1,4096,'capacity');this.capacity=capacity;this.entries=[];this.sequence=0;this.now=now||(()=>globalThis.performance&&typeof globalThis.performance.now==='function'?globalThis.performance.now():Date.now());this.origin=this.now();this.lastRelative=0;}
 add(node,event,outcome,session=1,run=1){this.lastRelative=Math.max(this.lastRelative,Math.max(0,Math.floor(this.now()-this.origin)));const entry=Object.freeze({sequence:++this.sequence,node:ident(node),event:ident(event),outcome:String(outcome).slice(0,100),session,run,relative_time_ms:this.lastRelative});if(this.entries.length===this.capacity)this.entries.shift();this.entries.push(entry);return entry;}
 snapshot(){return this.entries.slice();} clear(){this.entries=[];this.sequence=0;this.origin=this.now();this.lastRelative=0;}
}
class Session{
 constructor(){this.epoch=1;this._token=Object.freeze({epoch:this.epoch});this.pauses=new Set();this.completed=new Set();this.assisted=false;}
 // Opaque identity prevents callbacks from a different Session sharing epoch=1.
 token(){return this._token;} valid(t){return t===this._token;} get paused(){return this.pauses.size>0;}
 setPause(reason,on){ident(reason);if(on)this.pauses.add(reason);else this.pauses.delete(reason);}
 isCompleted(id){if(!['C01','C02','C03'].includes(id))throw new Error('Unknown challenge');return this.completed.has(id);}
 finish(id,token){if(!['C01','C02','C03'].includes(id))throw new Error('Unknown challenge');if(!this.valid(token)||this.paused)return false;if(id==='C02'&&!this.completed.has('C01'))return false;if(id==='C03'&&(!this.completed.has('C01')||!this.completed.has('C02')))return false;this.completed.add(id);return true;}
 get ready(){return ['C01','C02','C03'].every(x=>this.completed.has(x));}
 invalidateFrom(id){const order=['C01','C02','C03'],i=order.indexOf(id);if(i<0)throw Error('Unknown challenge');for(let j=i;j<3;j++)this.completed.delete(order[j]);}
 reset(){this.epoch++;this._token=Object.freeze({epoch:this.epoch});this.pauses.clear();this.completed.clear();this.assisted=false;}
}
const expected={
 C01:{nodes:{press:'OnPressed',selected:'SelectedLocation',route:'ShowRoute'},edges:[['selected','location','route','location'],['press','event','route','trigger']]},
 C02:{nodes:{move:'MoveToWaypoint',wait:'WaitForGuest',advance:'AdvanceWaypoint'},edges:[['move','flow','wait','flow'],['wait','flow','advance','flow']]},
 C03:{nodes:{receive:'ReceiveDemoRequest',check:'RequirePermission',execute:'ExecuteDemoOperation',reject:'RejectDemoOperation'},edges:[['receive','request','check','request'],['check','allow','execute','decision'],['check','deny','reject','decision']]}
};
function graph(challenge,correct=true){
 if(!expected[challenge])throw new Error('Unknown challenge');const g={challenge,nodes:clone(expected[challenge].nodes),edges:clone(expected[challenge].edges),permissions:{guest_view:true,guest_edit:false,team_edit:true}};
 if(!correct){if(challenge==='C01')g.edges=g.edges.filter(e=>e[0]!=='press');if(challenge==='C02'){delete g.nodes.wait;g.edges=[['move','flow','advance','flow']];}if(challenge==='C03'){delete g.nodes.check;delete g.nodes.reject;g.edges=[['receive','request','execute','request']];}}
 return g;
}
function compile(input){
 // Tiny allow-listed templates, NOT a universal visual programming language.
 if(!input||!expected[input.challenge])throw new Error('Unknown graph');const g=clone(input),template=expected[g.challenge];
 if(!g.nodes||!Array.isArray(g.edges))throw new Error('Bad graph shape');if(Object.keys(g.nodes).length>12||g.edges.length>16)throw new Error('Graph budget');
 for(const [id,type]of Object.entries(g.nodes)){ident(id);if(template.nodes[id]!==type)throw new Error('Unknown or changed node');}
 const fixed=graph(g.challenge,true),broken=graph(g.challenge,false),canonical=es=>es.map(e=>JSON.stringify(e)).sort().join('|');
 const forms=[fixed,broken];const exact=forms.find(f=>JSON.stringify(Object.entries(f.nodes).sort())===JSON.stringify(Object.entries(g.nodes).sort())&&canonical(f.edges)===canonical(g.edges));
 if(!exact)throw new Error('Unsupported links, missing nodes, duplicate edges or cycle');
 const baseline=canonical(g.edges)===canonical(broken.edges);const p=g.permissions||{};
 for(const k of ['guest_view','guest_edit','team_edit'])if(typeof p[k]!=='boolean')throw new Error('Invalid permission');
 return Object.freeze({challenge:g.challenge,linked:g.challenge==='C01'&&!baseline,wait:g.challenge==='C02'&&!baseline,requiresPermission:g.challenge==='C03'&&!baseline,baseline,permissions:Object.freeze({...p}),definition:deepFreeze(g)});
}
class Topology{
 constructor(nodes,edges){if(!Array.isArray(nodes)||!Array.isArray(edges))throw new Error('Topology arrays required');this.nodes=new Set(nodes.map(ident));if(this.nodes.size!==nodes.length)throw new Error('Duplicate node');this.links=new Map(nodes.map(n=>[n,[]]));for(const e of edges){if(e.length!==2||!this.nodes.has(e[0])||!this.nodes.has(e[1])||e[0]===e[1])throw new Error('Bad edge');if(this.links.get(e[0]).includes(e[1]))throw new Error('Duplicate edge');this.links.get(e[0]).push(e[1]);this.links.get(e[1]).push(e[0]);}}
 route(from,to){if(!this.nodes.has(from)||!this.nodes.has(to))throw new Error('Destination outside scope');const q=[[from]],seen=new Set([from]);while(q.length){const p=q.shift(),n=p[p.length-1];if(n===to)return p;for(const next of this.links.get(n)){if(!seen.has(next)){seen.add(next);q.push(p.concat(next));}}}throw new Error('Unreachable destination');}
}
class ButtonRouter{
 constructor(session,topology,origin,compiled){if(compiled.challenge!=='C01')throw new Error('Wrong graph');this.s=session;this.topology=topology;this.origin=origin;this.config=compiled;this.trace=new TraceBuffer();this.last=-1;this.current=[];this.epoch=session.token();this.run=0;}
 setGraph(compiled,token=this.epoch){if(!compiled||compiled.challenge!=='C01')throw new Error('Wrong graph');if(!this.s.valid(token)||token!==this.epoch||this.s.paused)return false;this.config=compiled;this.current=[];this.trace.add('link','HandlerAttached',compiled.linked?'linked':'unlinked',token,0);return true;}
 activate(destination,sequence,token){integer(sequence,0,Number.MAX_SAFE_INTEGER,'sequence');if(!this.s.valid(token)||this.s.paused)return false;if(this.epoch!==token){this.current=[];this.last=-1;this.epoch=token;this.trace.clear();}
 if(sequence<=this.last)return false;this.last=sequence;this.run++;this.trace.add('press','OnPressed','accepted',token,this.run);
 if(!this.config.linked){this.trace.add('route','NoHandler','no route',token,this.run);return false;}
 const r=this.topology.route(this.origin,destination);this.current=r.slice();this.trace.add('selected','SelectedLocation',destination,token,this.run);this.trace.add('route','ShowRoute',destination,token,this.run);return true;}
}
class C01RouteChallenge{
 constructor(session,topology,origin,firstDestination,secondDestination){this.s=session;this.topology=topology;this.origin=ident(origin);this.first=ident(firstDestination);this.second=ident(secondDestination);if(this.first===this.second)throw Error('Distinct destinations required');topology.route(this.origin,this.first);topology.route(this.origin,this.second);this.resetForCurrentSession();}
 resetForCurrentSession(){this.epoch=this.s.token();this.router=new ButtonRouter(this.s,this.topology,this.origin,compile(graph('C01',false)));this.handlerLinked=false;this.completed=false;this.progress=0;this.selectedDestination=this.first;this.stage='MissingHandler';this.s.invalidateFrom('C01');}
 active(t){return t===this.epoch&&this.s.valid(t)&&!this.s.paused;}
 selectDestination(destination,t=this.epoch){if(!this.active(t))return false;destination=ident(destination);if(destination!==this.first&&destination!==this.second)return false;this.selectedDestination=destination;return true;}
 attachHandler(t=this.epoch){if(!this.active(t)||this.handlerLinked)return false;this.s.invalidateFrom('C01');if(!this.router.setGraph(compile(graph('C01',true)),t))return false;this.handlerLinked=true;this.completed=false;this.progress=0;this.stage='HandlerLinked';return true;}
 press(sequence,t=this.epoch){if(!this.active(t))return this.snapshot(false,false);const accepted=this.router.activate(this.selectedDestination,sequence,t);if(accepted&&!this.completed)this.observe(this.selectedDestination,t);return this.snapshot(accepted,!this.handlerLinked);}
 observe(destination,t){const expected=this.progress===1?this.second:this.first;if(destination===expected)this.progress++;else this.progress=destination===this.first?1:0;if(this.progress>=3){this.completed=this.s.finish('C01',t);this.stage=this.completed?'Verified':'SecondRoute';}else this.stage=this.progress===2?'SecondRoute':this.progress===1?'FirstRoute':'HandlerLinked';}
 snapshot(accepted,noHandler){return Object.freeze({accepted,noHandler,completed:this.completed,destination:this.selectedDestination,route:Object.freeze(this.router.current.slice()),stage:this.stage});}
 traceSnapshot(){return this.router.trace.snapshot();}
}
class Guide{
 constructor(session,points,compiled){if(compiled.challenge!=='C02'||!Array.isArray(points)||!points.length)throw new Error('Invalid guide');this.s=session;this.points=points.map(ident);this.wait=compiled.wait;this.trace=new TraceBuffer();this.reset();}
 reset(){this.epoch=this.s.token();this.index=0;this.state='moving';this.trace.clear();}
 step({markerArrived=false,guestArrived=false,tracking=true}={},token=this.epoch){if(!this.s.valid(token)||token!==this.epoch||this.s.paused||!tracking)return this.state;
 if(this.state==='moving'){if(!markerArrived)return this.state;this.trace.add('move','MoveToWaypoint',this.points[this.index],token);this.state=this.wait?'waiting':'advance';return this.state;}
 if(this.state==='waiting'){if(!guestArrived)return this.state;this.trace.add('wait','WaitForGuest','arrived',token);this.state='advance';return this.state;}
 if(this.state==='advance'){this.trace.add('advance','AdvanceWaypoint',this.points[this.index],token);if(this.index+1>=this.points.length)this.state='complete';else{this.index++;this.state='moving';}return this.state;}return this.state;
 }
 // Called ONLY by adapter after a successful permitted teleport, never by arbitrary UI.
 confirmedTeleport(point,token=this.epoch){if(!this.s.valid(token)||token!==this.epoch||this.s.paused)return false;const i=this.points.indexOf(point);if(i<this.index||i<0)return false;this.index=i;this.state=this.wait?'waiting':'advance';this.trace.add('move','TeleportResync',point,token);return true;}
}
class C02WaitChallenge{
 constructor(session,waypoints){if(!session||!Array.isArray(waypoints)||!waypoints.length)throw Error('Waypoints required');this.s=session;this.waypoints=waypoints.map(ident);this.trace=new TraceBuffer();this.resetForCurrentSession();}
 get prerequisiteMet(){return this.s.isCompleted('C01');}
 resetForCurrentSession(){this.epoch=this.s.token();this.s.invalidateFrom('C02');this.guide=new Guide(this.s,this.waypoints,compile(graph('C02',false)));this.waitRuleInstalled=false;this.completed=false;this.stage='BrokenReady';this.trace.clear();}
 active(t){return t===this.epoch&&this.s.valid(t)&&!this.s.paused;}
 traceSnapshot(){return this.trace.snapshot();}
 step(markerArrived,guestArrived,tracking,t=this.epoch){if(!this.active(t)||!this.prerequisiteMet||!tracking||this.completed)return false;if(!this.waitRuleInstalled&&this.stage==='BrokenGuideLeft')return false;const before=this.guide.state,after=this.guide.step({markerArrived,guestArrived,tracking},t);
  if(!this.waitRuleInstalled){if(before==='moving'&&after==='advance'&&markerArrived&&!guestArrived){this.stage='BrokenGuideLeft';this.trace.add('move','GuideLeftWithoutGuest',this.waypoints[this.guide.index],t,1);return true;}return before!==after;}
  if(before==='moving'&&after==='waiting'){this.stage='WaitingForGuest';this.trace.add('wait','WaitForGuest','holding',t,2);return true;}
  if(before==='waiting'&&after==='waiting'&&!guestArrived){this.stage='WaitingForGuest';this.trace.add('wait','WaitForGuest','still-waiting',t,2);return true;}
  if(before==='waiting'&&after==='advance'&&guestArrived){this.stage='GuestConfirmed';this.trace.add('guest','GuestArrived',this.waypoints[this.guide.index],t,2);return true;}
  if(after==='complete'){this.completed=this.s.finish('C02',t);if(this.completed){this.stage='Verified';this.trace.add('advance','C02Verified','complete',t,2);}return this.completed;}return before!==after;
 }
 installWaitRule(t=this.epoch){if(!this.active(t)||!this.prerequisiteMet||this.stage!=='BrokenGuideLeft'||this.completed)return false;this.s.invalidateFrom('C02');this.guide=new Guide(this.s,this.waypoints,compile(graph('C02',true)));this.waitRuleInstalled=true;this.stage='WaitRuleInstalled';this.trace.add('wait','WaitForGuestInstalled','ready-to-replay',t,2);return true;}
 confirmedTeleport(waypoint,tracking,t=this.epoch){if(!this.active(t)||!this.prerequisiteMet||!this.waitRuleInstalled||!tracking||this.completed)return false;waypoint=ident(waypoint);if(!this.guide.confirmedTeleport(waypoint,t))return false;this.stage='WaitingForGuest';this.trace.add('move','TeleportResync',waypoint,t,2);return true;}
}
class ArrivalSensor{
 constructor(enter=1,exit=1.4,dwell=.2){num(enter);num(exit);num(dwell);if(enter<=0||exit<=enter||dwell<0)throw new Error('Invalid hysteresis');this.enter=enter;this.exit=exit;this.dwell=dwell;this.reset();}
 reset(){this.time=0;this.inside=false;}
 sample(distance,dt,tracking=true,paused=false){num(distance);num(dt);if(distance<0||dt<0)throw new Error('Invalid sample');if(!tracking){this.reset();return false;}if(paused)return this.inside;if(distance>=this.exit){this.reset();return false;}if(this.inside)return true;if(distance<=this.enter){this.time+=Math.min(dt,.25);if(this.time+1e-9>=this.dwell)this.inside=true;}else this.time=0;return this.inside;}
}
function evaluatePolicy(compiled,role,operation){if(compiled.challenge!=='C03')throw new Error('Wrong policy');if(!['guest','team'].includes(role)||!['view_public_project','edit_route'].includes(operation))return false;
 if(!compiled.requiresPermission)return true;const p=compiled.permissions;if(operation==='view_public_project')return p.guest_view;return role==='guest'?p.guest_edit:p.team_edit;}
class AuthSandbox{
 constructor(compiled){if(compiled.challenge!=='C03')throw new Error('Wrong graph');this.config=compiled;this.trace=new TraceBuffer();this.reset();}
 reset(){this.routeVersion=0;this.trace.clear();}
 execute(role,operation){this.trace.add('receive','ReceiveDemoRequest',role);const allow=evaluatePolicy(this.config,role,operation);if(this.config.requiresPermission)this.trace.add('check','RequirePermission',allow?'ALLOW':'DENY');if(!allow){this.trace.add('reject','RejectDemoOperation','DENY');return{allowed:false,mutated:false};}const mutated=operation==='edit_route';if(mutated)this.routeVersion++;this.trace.add('execute','ExecuteDemoOperation',operation);return{allowed:true,mutated};}
}
function verifyPolicy(compiled){const cases=[['guest','view_public_project',true,false],['guest','edit_route',false,false],['team','edit_route',true,true]];return compiled.requiresPermission&&cases.every(([r,a,allow,change])=>{const box=new AuthSandbox(compiled),x=box.execute(r,a);return x.allowed===allow&&x.mutated===change&&box.routeVersion===(change?1:0);});}
class C03AuthorizationChallenge{
 constructor(session){if(!session)throw Error('Session required');this.s=session;this.trace=new TraceBuffer();this.policyRevision=0;this.resetForCurrentSession();}
 get prerequisiteMet(){return this.s.isCompleted('C01')&&this.s.isCompleted('C02');}
 active(t){return t===this.epoch&&this.s.valid(t)&&!this.s.paused;}
 traceSnapshot(){return this.trace.snapshot();}
 resetForCurrentSession(){this.epoch=this.s.token();this.s.invalidateFrom('C03');this.policy=null;this.guestViewPassed=this.guestEditDenied=this.teamEditPassed=false;this.completed=false;this.stage='BaselineReady';this.policyRevision++;this.trace.clear();}
 demonstrateBaselineBypass(tracking,t=this.epoch){if(!this.active(t)||!tracking||!this.prerequisiteMet||this.stage!=='BaselineReady')return this.rejected('guest','edit_route');const box=new AuthSandbox(compile(graph('C03',false))),before=box.routeVersion,result=box.execute('guest','edit_route'),after=box.routeVersion,proven=result.allowed&&result.mutated&&before===0&&after===1;if(proven){this.stage='BaselineBypassProven';this.trace.add('baseline','DemoBypass','ALLOW_0_TO_1',t,3);}return Object.freeze({accepted:proven,allowed:result.allowed,mutated:result.mutated,completed:false,versionBefore:before,versionAfter:after,role:'guest',operation:'edit_route',stage:this.stage});}
 installGate(flags,t=this.epoch){if(!this.active(t)||!this.prerequisiteMet||this.stage==='BaselineReady')return false;const definition=graph('C03',true);definition.permissions={...flags};this.policy=compile(definition);this.s.invalidateFrom('C03');this.policyRevision++;this.guestViewPassed=this.guestEditDenied=this.teamEditPassed=false;this.completed=false;this.stage='GateInstalled';this.trace.add('check','RequirePermissionInstalled','policy-revision',t,3);return true;}
 execute(role,operation,tracking,expectedRevision,t=this.epoch){if(!this.active(t)||!tracking||expectedRevision!==this.policyRevision||!this.prerequisiteMet||!this.policy||this.completed||!['guest','team'].includes(role)||!['view_public_project','edit_route'].includes(operation))return this.rejected(role,operation);const box=new AuthSandbox(this.policy),before=box.routeVersion,result=box.execute(role,operation),after=box.routeVersion,accepted=this.observe(role,operation,result,before,after,t);return Object.freeze({accepted,allowed:result.allowed,mutated:result.mutated,completed:this.completed,versionBefore:before,versionAfter:after,role,operation,stage:this.stage});}
 observe(role,operation,result,before,after,t){let accepted,outcome;if(role==='guest'&&operation==='view_public_project'){accepted=result.allowed&&!result.mutated&&before===0&&after===0;this.guestViewPassed=accepted;if(accepted)this.stage='GuestViewPassed';outcome=accepted?'ALLOW_0_TO_0':'AUTH01_FAIL';}else if(role==='guest'&&operation==='edit_route'){accepted=!result.allowed&&!result.mutated&&before===0&&after===0;this.guestEditDenied=accepted;if(accepted)this.stage='GuestEditDenied';outcome=accepted?'DENY_0_TO_0':'AUTH02_FAIL';}else if(role==='team'&&operation==='edit_route'){accepted=result.allowed&&result.mutated&&before===0&&after===1;this.teamEditPassed=accepted;outcome=accepted?'ALLOW_0_TO_1':'AUTH03_FAIL';}else{accepted=false;outcome='UNKNOWN_DENY';}this.trace.add('request','AuthorizationCase',outcome,t,3);if(this.guestViewPassed&&this.guestEditDenied&&this.teamEditPassed&&verifyPolicy(this.policy)){this.completed=this.s.finish('C03',t);if(this.completed){this.stage='Verified';this.trace.add('complete','C03Verified','ALLOW_DENY_ALLOW',t,3);}}return accepted;}
 rejected(role,operation){return Object.freeze({accepted:false,allowed:false,mutated:false,completed:this.completed,versionBefore:0,versionAfter:0,role,operation,stage:this.stage});}
}
function sealGuide(ui,logic,policy){if(!ui.linked||!logic.wait||!verifyPolicy(policy))throw new Error('Unvalidated guide configuration');return Object.freeze({ui,logic,policy});}
class Coffee{
 constructor(seconds=6){num(seconds);if(seconds<=0)throw new Error('Invalid brew duration');this.seconds=seconds;this.reset();}
 reset(){this.cup=false;this.fill=0;this.state='idle';}
 dock(){this.cup=true;if(this.fill>=1)this.state='ready';else if(this.state!=='brewing')this.state='cup_ready';}
 remove(){this.cup=false;if(this.state==='brewing')this.state='paused';else if(this.state!=='paused')this.state='idle';}
 press(){if(!this.cup||this.fill>=1||this.state==='brewing')return false;this.state='brewing';return true;}
 tick(dt,paused=false){num(dt);if(dt<0)throw new Error('Negative time');if(paused||!this.cup||this.state!=='brewing')return;this.fill=Math.min(1,this.fill+Math.min(dt,.25)/this.seconds);if(this.fill>1-1e-9){this.fill=1;this.state='ready';}}
 // An explicit empty-cup operation, never while held; used by reset/refill stand only.
 empty(held=false){if(held)return false;this.fill=0;this.state=this.cup?'cup_ready':'idle';return true;}
}
class NpcStation{
 constructor(anchor,bodyLimit=25,headLimit=35,speed=70){if(!Array.isArray(anchor)||anchor.length!==3)throw new Error('Anchor required');anchor.forEach(x=>num(x));this.anchor=Object.freeze(anchor.slice());this.bodyLimit=num(bodyLimit);this.headLimit=num(headLimit);this.speed=num(speed);if(bodyLimit<0||headLimit<0||speed<=0)throw new Error('Invalid pose limits');this.reset();}
 reset(){this.bodyYaw=0;this.headYaw=0;this.gesture='idle';}
 look(relativeYaw,dt,tracked=true,paused=false){num(relativeYaw);num(dt);if(dt<0)throw new Error('Negative time');if(paused)return;const y=tracked&&Math.abs(relativeYaw)<=110?relativeYaw:0;const b=clamp(y*.45,-this.bodyLimit,this.bodyLimit),h=clamp(y-b,-this.headLimit,this.headLimit),step=this.speed*Math.min(dt,.1);this.bodyYaw+=clamp(b-this.bodyYaw,-step,step);this.headYaw+=clamp(h-this.headYaw,-step,step);}
 react(gesture){if(!['idle','greet','explain','point','acknowledge','humor'].includes(gesture))throw new Error('Unknown gesture');this.gesture=gesture;}
}
class Ownership{
 constructor(){this.owner=null;this.tracking=false;}
 grab(owner){ident(owner);if(this.owner!==null)return false;this.owner=owner;this.tracking=true;return true;}
 release(owner,reason='intentional'){if(owner!==this.owner)return{released:false,throw:false};const throwing=reason==='intentional'&&this.tracking;this.owner=null;this.tracking=false;return{released:true,throw:throwing};}
 trackingLost(){if(this.owner===null)return false;this.tracking=false;this.owner=null;return true;}
 reset(){this.owner=null;this.tracking=false;}
}
class ReactionGate{
 constructor(cooldown=12){num(cooldown);if(cooldown<0)throw new Error('Invalid cooldown');this.cooldown=cooldown;this.reset();}
 reset(){this.last=-Infinity;}
 tryReact(now,mainSpeaking=false,visible=true){num(now);if(mainSpeaking||!visible||now-this.last<this.cooldown)return false;this.last=now;return true;}
}
return{TraceBuffer,Session,graph,compile,Topology,ButtonRouter,C01RouteChallenge,Guide,C02WaitChallenge,ArrivalSensor,evaluatePolicy,AuthSandbox,C03AuthorizationChallenge,verifyPolicy,sealGuide,Coffee,NpcStation,Ownership,ReactionGate};
});
