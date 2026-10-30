/* Pure input policy. NOT a device reader, collision solver, comfort guarantee, or XR mover. */
(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.ITPUControllerWalk=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';let sessionCounter=0;
const DEFAULTS=Object.freeze({"speed":1.2,"carrySpeed":0.8,"backward":0.65,"strafe":0.85,"deadzone":0.18,"acceleration":6.0,"maxDt":0.1,"maxAge":0.2,"turnOn":0.65,"turnOff":0.25,"snap":30,"turnCooldown":0.25,"smoothDps":45});
const finite=n=>typeof n==='number'&&Number.isFinite(n);
function radial(x,y,d){const m=Math.hypot(x,y);if(m<=d)return [0,0];const v=(Math.min(m,1)-d)/(1-d);return [x/m*v,y/m*v];}
class WalkPolicy{
 constructor(options={}){this.cfg=Object.freeze({...DEFAULTS,...options});const c=this.cfg;
  if(Object.keys(c).some(k=>!finite(c[k]))||c.speed<=0||c.carrySpeed<=0||c.carrySpeed>c.speed||c.deadzone<0||c.deadzone>=1||c.backward<=0||c.backward>1||c.strafe<=0||c.strafe>1||c.acceleration<=0||c.maxDt<=0||c.maxDt>.2||c.maxAge<=0||c.turnOff<0||c.turnOn<=c.turnOff||c.turnOn>1||c.snap<=0||c.snap>90||c.turnCooldown<0||c.smoothDps<=0)throw new RangeError('Invalid walk config');
  this.session=++sessionCounter;this.epoch=0;this.seq=0;this.lastNow=-Infinity;this.lastSample=-Infinity;this.mode='controllers';this.turn='snap';this.lastTurn=-Infinity;this.cancel('INITIAL_NEUTRAL_REQUIRED');}
 cancel(reason){this.ready=false;this.speed=0;this.turnReady=false;this.pending=null;this.reason=reason;}
 reset(){this.epoch++;this.cancel('RESET');this.lastSample=-Infinity;}
 setMode(mode,turn='snap'){if(!['controllers','hands'].includes(mode)||!['snap','smooth'].includes(turn))throw new RangeError('Invalid mode');if(mode!==this.mode||turn!==this.turn){this.mode=mode;this.turn=turn;this.reset();}}
 update(i,dt,now){this.pending=null;
  if(!i||![dt,now,i.time,i.x,i.y,i.turn,i.yaw].every(finite)||dt<=0||dt>this.cfg.maxDt||now<0||now<this.lastNow||i.time>now||now-i.time>this.cfg.maxAge||i.time<this.lastSample||Math.abs(i.x)>1||Math.abs(i.y)>1||Math.abs(i.turn)>1||['headTracked','moveTracked','turnTracked','focused','paused','modal','precisionBusy','teleporting','carrying'].some(k=>typeof i[k]!=='boolean')){this.cancel('INVALID_OR_STALE_INPUT');return null;}
  this.lastNow=now;this.lastSample=i.time;
  if(this.mode!=='controllers'||!i.headTracked||!i.moveTracked||!i.turnTracked||!i.focused||i.paused||i.modal||i.precisionBusy||i.teleporting){this.cancel('CONTEXT_BLOCKED');return null;}
  const c=this.cfg,m=Math.hypot(i.x,i.y),neutral=m<=c.deadzone&&Math.abs(i.turn)<=c.turnOff;
  if(!this.ready){if(neutral){this.ready=true;this.turnReady=true;this.reason='READY';}return null;}
  const a=radial(i.x,i.y,c.deadzone),sx=a[0]*c.strafe,sz=a[1]*(a[1]<0?c.backward:1),mag=Math.hypot(sx,sz);
  const target=Math.min(c.speed,i.carrying?c.carrySpeed:c.speed)*mag;
  // Neutral is an immediate stop. Only acceleration is ramped; no inertial skating.
  this.speed=target<=0?0:Math.min(target,this.speed+c.acceleration*dt);
  const x=mag>0?sx/mag*this.speed:0,z=mag>0?sz/mag*this.speed:0,yaw=(i.yaw%360)*Math.PI/180;
  const vx=x*Math.cos(yaw)+z*Math.sin(yaw),vz=z*Math.cos(yaw)-x*Math.sin(yaw);
  let turnDelta=0;
  if(Math.abs(i.turn)<=c.turnOff)this.turnReady=true;
  if(this.turn==='snap'){if(Math.abs(i.turn)>=c.turnOn&&this.turnReady&&now-this.lastTurn>=c.turnCooldown){turnDelta=Math.sign(i.turn)*c.snap;this.turnReady=false;this.lastTurn=now;}}
  else {const t=Math.max(-1,Math.min(1,i.turn));if(Math.abs(t)>c.turnOff)turnDelta=Math.sign(t)*(Math.abs(t)-c.turnOff)/(1-c.turnOff)*c.smoothDps*dt;}
  this.reason='READY';
  if(this.speed===0&&turnDelta===0)return null;
  this.pending=Object.freeze({session:this.session,epoch:this.epoch,sequence:++this.seq,time:now,dx:vx*dt,dz:vz*dt,turnDegrees:turnDelta,speed:this.speed});return this.pending;
 }
 consume(cmd,now,contextSafe){if(cmd!==this.pending||!cmd||cmd.session!==this.session||cmd.epoch!==this.epoch||!finite(now)||now<cmd.time||now-cmd.time>this.cfg.maxAge||contextSafe!==true){this.cancel('REJECTED_COMMIT');return null;}this.pending=null;return cmd;}
}
return {WalkPolicy,DEFAULTS,radial};});
