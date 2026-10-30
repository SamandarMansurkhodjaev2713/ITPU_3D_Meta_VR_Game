/* Semantic request gate, NOT a Meta hand tracker or XR Origin mover. No network. */
(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.ITPULocomotion=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';let serial=0;
const STATES=Object.freeze({IDLE:'IDLE',AIM:'AIM',ARMED:'ARMED',PENDING:'PENDING',COOLDOWN:'COOLDOWN',NEUTRAL:'NEUTRAL'});
const BOOLS=['tracked','focused','paused','uiBusy','holding','systemGesture','pathClear','landingClear','headClear','targetEnabled','headTracked'];
function finite(n){if(typeof n!=='number'||!Number.isFinite(n))throw new TypeError('Finite number required');return n;}
class TeleportGate{
 constructor(options={}){this.minHold=finite(options.minHold??.12);this.stableAim=finite(options.stableAim??.15);this.cooldown=finite(options.cooldown??.3);this.maxRange=finite(options.maxRange??6);this.maxPending=finite(options.maxPending??1);if(this.minHold<0||this.stableAim<0||this.cooldown<0||this.maxRange<=0||this.maxPending<=0)throw new RangeError('Invalid limits');this.session=++serial;this.epoch=0;this.sequence=0;this.lastTime=-Infinity;this.lastCommit=-Infinity;this.pending=null;this.state=STATES.IDLE;this.anchor=null;this.owner=null;this.reason='';}
 time(now){if(typeof now!=='number'||!Number.isFinite(now)){this.cancel('INVALID_TIME');throw new TypeError('Finite clock required');}if(now<0||now<this.lastTime){this.cancel('NON_MONOTONIC_TIME');throw new RangeError('Time must be monotonic');}this.lastTime=now;}
 valid(c){return !!c&&BOOLS.every(k=>typeof c[k]==='boolean')&&c.tracked&&c.headTracked&&c.focused&&!c.paused&&!c.uiBusy&&!c.holding&&!c.systemGesture&&c.pathClear&&c.landingClear&&c.headClear&&c.targetEnabled&&typeof c.anchor==='string'&&c.anchor.length>0&&typeof c.revision==='string'&&c.revision.length>0&&typeof c.source==='string'&&c.source.length>0&&typeof c.distance==='number'&&Number.isFinite(c.distance)&&c.distance>=.4&&c.distance<=this.maxRange;}
 same(c){return this.valid(c)&&c.anchor===this.anchor&&c.revision===this.revision&&c.source===this.owner;}
 cancel(reason='CANCELLED'){this.state=STATES.NEUTRAL;this.anchor=null;this.owner=null;this.pending=null;this.reason=reason;}
 neutral(now){this.time(now);if(now-this.lastCommit<this.cooldown)return false;this.state=STATES.IDLE;this.anchor=null;this.owner=null;this.pending=null;this.reason='';return true;}
 reset(now){this.time(now);this.epoch++;this.cancel('RESET');}
 tick(now,c){this.time(now);if(this.state===STATES.PENDING&&now-this.pendingSince>this.maxPending){this.cancel('EXPIRED_INTENT');return false;}if([STATES.AIM,STATES.ARMED,STATES.PENDING].includes(this.state)&&!this.same(c)){this.cancel('CONTEXT_CHANGED');return false;}return true;}
 aim(now,c){this.time(now);if(!this.valid(c)){if(this.state!==STATES.IDLE)this.cancel('INVALID_TARGET');return false;}if([STATES.NEUTRAL,STATES.COOLDOWN,STATES.PENDING,STATES.ARMED].includes(this.state))return false;
 if(this.state!==STATES.AIM||!this.same(c)){this.state=STATES.AIM;this.anchor=c.anchor;this.owner=c.source;this.revision=c.revision;this.aimSince=now;}return true;}
 press(now,c){this.time(now);if(this.state!==STATES.AIM||!this.same(c)||now-this.aimSince+1e-9<this.stableAim){this.cancel('UNARMED_PRESS');return false;}this.state=STATES.ARMED;this.pressSince=now;return true;}
 release(now,c){this.time(now);if(this.state!==STATES.ARMED||!this.same(c)||now-this.pressSince+1e-9<this.minHold){this.cancel('INVALID_RELEASE');return null;}const request=Object.freeze({session:this.session,epoch:this.epoch,sequence:++this.sequence,anchor:this.anchor,revision:this.revision,source:this.owner});this.pending=request;this.pendingSince=now;this.state=STATES.PENDING;return request;}
 commit(now,request,c){this.time(now);if(this.state!==STATES.PENDING||request!==this.pending||now-this.pendingSince>this.maxPending||!this.same(c)||request.session!==this.session||request.epoch!==this.epoch){this.cancel('INVALID_COMMIT');return false;}this.pending=null;this.state=STATES.COOLDOWN;this.lastCommit=now;return true;}
}
return{TeleportGate,STATES,BOOLS};});
