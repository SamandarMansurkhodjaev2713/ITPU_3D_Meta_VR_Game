/* v6 executable companion; not Unity runtime. Browser and Node run the same code. */
(function(root,f){if(typeof module==='object'&&module.exports)module.exports=f(require('./runtime.js'));else root.ITPUV6=f(root.ITPU);})(typeof globalThis!=='undefined'?globalThis:this,function(R){
'use strict';
const finite=x=>{if(typeof x!=='number'||!Number.isFinite(x))throw Error('Finite number required');return x;};
const int=(x,l,h)=>{finite(x);if(!Number.isInteger(x)||x<l||x>h)throw Error('Integer outside range');return x;};
const clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
const ids=['C01','C02','C03'];
function scenePolicy(profile,npcs,sessions,coffee,materials,releaseEvidence=false){
 if(!['environment','interaction_slice','complete_game','release'].includes(profile))throw Error('Unknown profile');int(npcs,0,100);int(sessions,0,100);
 const errors=[];if(!materials)errors.push('missing_material');if(profile==='environment'&&npcs!==0)errors.push('npc_deferred_in_environment');
 if(profile!=='environment'&&sessions!==1)errors.push('exactly_one_session_required');if(['complete_game','release'].includes(profile)&&npcs!==3)errors.push('three_npc_required');
 if(profile!=='environment'&&!coffee)errors.push('coffee_required');if(profile==='release'&&!releaseEvidence)errors.push('external_release_evidence_required');return errors;
}
class GuideProject{
 constructor(session,topology,origin){this.session=session;this.topology=topology;this.origin=origin;this.reset();}
 reset(){this.epoch=this.session.token();this.graphs=ids.map(id=>R.compile(R.graph(id,false)));this.revisions=[1,1,1];this.verified=[0,0,0];this.session.invalidateFrom('C01');}
 active(t){return this.session.valid(t)&&t===this.epoch&&!this.session.paused;}
 setGraph(def,t=this.epoch){if(!this.active(t))return false;const g=R.compile(def),i=ids.indexOf(g.challenge);this.graphs[i]=g;this.revisions[i]++;for(let j=i;j<3;j++)this.verified[j]=0;this.session.invalidateFrom(g.challenge);return true;}
 check(id,t=this.epoch,a=null,b=null,assisted=false){
 const i=ids.indexOf(id);if(i<0)throw Error('Unknown challenge');if(!this.active(t))return false;
 for(let j=0;j<i;j++)if(this.verified[j]!==this.revisions[j])return false;
 let pass=false;
 if(id==='C01'){
  if(!a||!b||a===b)return false;const p=new R.ButtonRouter(this.session,this.topology,this.origin,this.graphs[0]);
   const x=p.activate(a,1,t),pa=p.current.slice(),y=p.activate(b,2,t),pb=p.current.slice(),z=p.activate(a,3,t),pc=p.current.slice();pass=x&&y&&z&&pa.at(-1)===a&&pb.at(-1)===b&&pc.at(-1)===a;
 }else if(id==='C02'){
  const p=new R.Guide(this.session,['probe_a','probe_b'],this.graphs[1]);p.step({markerArrived:true},t);const waits=p.state==='waiting';p.step({},t);const stays=p.state==='waiting'&&p.index===0;p.step({guestArrived:true},t);p.step({},t);pass=waits&&stays&&p.index===1;
 }else pass=R.verifyPolicy(this.graphs[2]);
 this.verified[i]=pass?this.revisions[i]:0;
 if(!pass){for(let j=i;j<3;j++)this.verified[j]=0;this.session.invalidateFrom(id);return false;}this.session.assisted=this.session.assisted||assisted;return this.session.finish(id,t);
 }
 isVerified(id){const i=ids.indexOf(id);return i>=0&&this.session.valid(this.epoch)&&this.verified[i]===this.revisions[i];}
 seal(t=this.epoch){if(!this.active(t)||!this.session.ready||!ids.every(id=>this.isVerified(id)))throw Error('Configuration tests stale or incomplete');return R.sealGuide(...this.graphs);}
}
class RecoverableProp{
 constructor(s){this.s=s;this.reset();}reset(){this.epoch=this.s.token();this.held=false;this.trackingValid=true;this.returnCount=0;}
 hold(t=this.epoch){if(!this.s.valid(t)||t!==this.epoch||this.s.paused||this.held)return false;this.held=true;this.trackingValid=true;return true;}
 lostTracking(){this.trackingValid=false;}
 release(t=this.epoch){if(!this.s.valid(t)||t!==this.epoch||!this.held)return false;this.held=false;return true;}
 tryReturn(clear,t=this.epoch){if(!this.s.valid(t)||t!==this.epoch||this.s.paused||this.held||!clear)return false;this.returnCount++;this.trackingValid=true;return true;}
}
class DrawingSurface{
 constructor(width=256,height=128,maxUndo=12){this.width=int(width,8,512);this.height=int(height,8,512);this.maxUndo=int(maxUndo,1,32);this.pixels=new Uint8Array(width*height);this.history=[];this.revision=0;this.active=false;}
 begin(){if(this.active)return false;this.before=this.pixels.slice();this.active=true;return true;}
 line(x0,y0,x1,y1,r,color){if(!this.active)return false;int(color,0,5);finite(r);if(r<.5||r>32)throw Error('Invalid radius');
 const [ax,ay,bx,by]=[clamp(finite(x0),0,1)*(this.width-1),clamp(finite(y0),0,1)*(this.height-1),clamp(finite(x1),0,1)*(this.width-1),clamp(finite(y1),0,1)*(this.height-1)];
 const dx=bx-ax,dy=by-ay,len=dx*dx+dy*dy;let changed=false;
 for(let y=Math.max(0,Math.floor(Math.min(ay,by)-r));y<=Math.min(this.height-1,Math.ceil(Math.max(ay,by)+r));y++)for(let x=Math.max(0,Math.floor(Math.min(ax,bx)-r));x<=Math.min(this.width-1,Math.ceil(Math.max(ax,bx)+r));x++){
  const t=len?clamp(((x-ax)*dx+(y-ay)*dy)/len,0,1):0,ex=x-(ax+t*dx),ey=y-(ay+t*dy),i=y*this.width+x;
  if(ex*ex+ey*ey<=r*r&&this.pixels[i]!==color){this.pixels[i]=color;changed=true;}
 }if(changed)this.revision++;return changed;}
 end(){if(!this.active)return false;this.active=false;const changed=this.pixels.some((c,i)=>c!==this.before[i]);if(changed){if(this.history.length===this.maxUndo)this.history.shift();this.history.push(this.before);}this.before=null;return changed;}
 cancel(){if(!this.active)return false;this.pixels.set(this.before);this.before=null;this.active=false;this.revision++;return true;}
 undo(){if(this.active||!this.history.length)return false;this.pixels.set(this.history.pop());this.revision++;return true;}
 snapshot(){return this.pixels.slice();}count(){let n=0;for(const c of this.pixels)if(c)n++;return n;}
 reset(){this.pixels.fill(0);this.history=[];this.before=null;this.active=false;this.revision++;}
}
class PaperBasketProbe{
 constructor(dwell=.15){finite(dwell);if(dwell<0)throw Error('Invalid dwell');this.dwell=dwell;this.reset();}
 reset(){this.entered=this.scored=false;this.time=0;}
 enterMouth(down,trackedRelease){this.entered=down&&trackedRelease;this.time=0;}
 tick(inside,dt,paused=false){finite(dt);if(dt<0)throw Error('Negative time');if(paused||this.scored||!this.entered)return false;if(!inside){this.entered=false;this.time=0;return false;}this.time+=Math.min(dt,.25);if(this.time+1e-9<this.dwell)return false;this.scored=true;return true;}
}
return{scenePolicy,GuideProject,RecoverableProp,DrawingSurface,PaperBasketProbe};});
