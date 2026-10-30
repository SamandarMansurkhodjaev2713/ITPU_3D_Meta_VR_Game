"use strict";
const assert=require('node:assert/strict');
const {WalkPolicy,radial}=require('../src/reference/controller_walk_v9.js');
let passed=0;function test(name,fn){try{fn();passed++;}catch(e){console.error('FAIL',name,e);process.exitCode=1;}}
const sample=(time,patch={})=>({time,x:0,y:0,turn:0,yaw:0,headTracked:true,moveTracked:true,turnTracked:true,focused:true,paused:false,modal:false,precisionBusy:false,teleporting:false,carrying:false,...patch});
const near=(a,b)=>assert.ok(Math.abs(a-b)<1e-9,`${a} != ${b}`);
function walk(patch={}){const p=new WalkPolicy();p.update(sample(0),.02,0);return p;}
test('requires neutral at start',()=>assert.equal(new WalkPolicy().update(sample(.02,{y:1}),.02,.02),null));
test('radial deadzone',()=>assert.deepEqual(radial(.1,.1,.18),[0,0]));
test('forward after neutral',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);assert.ok(c.dz>0);near(c.dx,0);});
test('head yaw only',()=>{const p=walk(),c=p.update(sample(.02,{y:1,yaw:90}),.02,.02);assert.ok(c.dx>0);near(c.dz,0);});
test('diagonal normalized capped',()=>{const p=walk();let c;for(let k=1;k<=20;k++)c=p.update(sample(k*.02,{x:1,y:1}),.02,k*.02);assert.ok(c.speed<=1.2);near(Math.hypot(c.dx,c.dz),c.speed*.02);});
test('maximum forward speed',()=>{const p=walk();let c;for(let k=1;k<=20;k++)c=p.update(sample(k*.02,{y:1}),.02,k*.02);near(c.speed,1.2);});
test('backward reduced',()=>{const p=walk();let c;for(let k=1;k<=20;k++)c=p.update(sample(k*.02,{y:-1}),.02,k*.02);near(c.speed,1.2*.65);assert.ok(c.dz<0);});
test('strafe reduced',()=>{const p=walk();let c;for(let k=1;k<=20;k++)c=p.update(sample(k*.02,{x:1}),.02,k*.02);near(c.speed,1.2*.85);});
test('held object slow walk allowed',()=>{const p=walk();let c;for(let k=1;k<=20;k++)c=p.update(sample(k*.02,{y:1,carrying:true}),.02,k*.02);near(c.speed,.8);});
test('neutral stops immediately',()=>{const p=walk();p.update(sample(.02,{y:1}),.02,.02);assert.equal(p.update(sample(.04),.02,.04),null);near(p.speed,0);});
for(const [key,value] of [['paused',true],['modal',true],['precisionBusy',true],['teleporting',true],['focused',false],['headTracked',false],['moveTracked',false],['turnTracked',false]]){
 test('stop and rearm '+key,()=>{const p=walk();p.update(sample(.02,{y:1}),.02,.02);assert.equal(p.update(sample(.04,{y:1,[key]:value}),.02,.04),null);assert.equal(p.update(sample(.06,{y:1}),.02,.06),null);p.update(sample(.08),.02,.08);assert.ok(p.update(sample(.1,{y:1}),.02,.1));});
}
for(const key of ['x','y','turn','yaw','time'])for(const value of [NaN,Infinity])test('invalid '+key+value,()=>{const p=walk();assert.equal(p.update(sample(.02,{[key]:value}),.02,.02),null);});
for(const key of ['x','y','turn'])test('out-of-range '+key,()=>assert.equal(walk().update(sample(.02,{[key]:2}),.02,.02),null));
test('nonboolean paused',()=>assert.equal(walk().update(sample(.02,{paused:0}),.02,.02),null));
test('old sample',()=>assert.equal(walk().update(sample(0,{y:1}),.02,1),null));
test('future sample',()=>assert.equal(walk().update(sample(1,{y:1}),.02,.02),null));
test('long frame no jump',()=>assert.equal(walk().update(sample(1,{y:1}),1,1),null));
test('backward clock cancels',()=>{const p=walk();p.update(sample(1,{y:1}),.02,1);assert.equal(p.update(sample(.5,{y:1}),.02,.5),null);});
test('snap once per tilt',()=>{const p=walk();near(p.update(sample(.02,{turn:1}),.02,.02).turnDegrees,30);assert.equal(p.update(sample(1,{turn:1}),.02,1),null);p.update(sample(1.02),.02,1.02);near(p.update(sample(1.04,{turn:-1}),.02,1.04).turnDegrees,-30);});
test('snap cooldown',()=>{const p=walk();p.update(sample(.02,{turn:1}),.02,.02);p.update(sample(.04),.02,.04);assert.equal(p.update(sample(.06,{turn:1}),.02,.06),null);});
test('smooth optional and neutral on switch',()=>{const p=walk();p.setMode('controllers','smooth');assert.equal(p.update(sample(.02,{turn:1}),.02,.02),null);p.update(sample(.04),.02,.04);near(p.update(sample(.06,{turn:1}),.02,.06).turnDegrees,.9);});
test('hands ignores sticks',()=>{const p=walk();p.setMode('hands');assert.equal(p.update(sample(.02,{y:1}),.02,.02),null);});
test('command can be consumed only once',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);assert.equal(p.consume(c,.03,true),c);assert.equal(p.consume(c,.04,true),null);});
test('expired command',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);assert.equal(p.consume(c,1,true),null);});
test('reset invalidates',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);p.reset();assert.equal(p.consume(c,.03,true),null);});
test('newer frame invalidates pending',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);p.update(sample(.04,{y:1}),.02,.04);assert.equal(p.consume(c,.04,true),null);});
test('cross session rejected',()=>{const p=walk(),q=walk(),c=p.update(sample(.02,{y:1}),.02,.02);assert.equal(q.consume(c,.03,true),null);});
test('blocked at commit',()=>{const p=walk(),c=p.update(sample(.02,{y:1}),.02,.02);assert.equal(p.consume(c,.03,false),null);});
test('config validation',()=>assert.throws(()=>new WalkPolicy({deadzone:1})));
test('frame-rate independent acceleration window',()=>{function sim(dt){const p=walk();let t=0,c;for(let k=0;k<Math.round(.5/dt);k++){t+=dt;c=p.update(sample(t,{y:1}),dt,t);}return c.speed;}near(sim(1/72),sim(1/90));});
console.log(JSON.stringify({suite:'controller_policy_v9',passed,failed:process.exitCode?1:0,scope:'JS policy only; no colliders, XR or headset'}));
