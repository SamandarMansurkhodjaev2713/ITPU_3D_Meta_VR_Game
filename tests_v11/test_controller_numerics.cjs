'use strict';
const assert=require('node:assert/strict');
const {WalkPolicy}=require('../src/reference/controller_walk_v9.js');
let passed=0,failed=0;
const sample=(time,yaw=0)=>({time,x:0,y:1,turn:0,yaw,headTracked:true,moveTracked:true,turnTracked:true,focused:true,paused:false,modal:false,precisionBusy:false,teleporting:false,carrying:false});
function command(yaw){const p=new WalkPolicy();p.update({...sample(0),y:0},.02,0);return p.update(sample(.02,yaw),.02,.02);}
function test(name,fn){try{fn();passed++;}catch(e){failed++;console.error(name,e.message);}}
const angles=[Number.MAX_VALUE,-Number.MAX_VALUE,1e308,-1e308,1e200,-1e200,Number.MAX_SAFE_INTEGER,-Number.MAX_SAFE_INTEGER,360,-360,720,-720,0,90,-90,450,-450,1e-10,-1e-10,180,-180];
for(const a of angles)test('finite bounded yaw '+a,()=>{const c=command(a),ref=command(a%360);assert.ok(c);for(const k of ['dx','dz','speed','turnDegrees'])assert.ok(Number.isFinite(c[k]),k+' nonfinite');assert.ok(Math.abs(c.dx-ref.dx)<1e-12);assert.ok(Math.abs(c.dz-ref.dz)<1e-12);assert.ok(Math.hypot(c.dx,c.dz)<=1.2*.02+1e-12);});
for(const a of [NaN,Infinity,-Infinity])test('reject invalid yaw '+a,()=>assert.equal(command(a),null));
for(let i=0;i<20;i++)test('periodic ordinary yaw '+i,()=>{const a=-170+i*17.25,c=command(a),r=command(a+360*1000);assert.ok(Math.abs(c.dx-r.dx)<1e-10);assert.ok(Math.abs(c.dz-r.dz)<1e-10);});
console.log(JSON.stringify({suite:'v11_controller_numerics',passed,failed,scope:'JS policy math, not CSharp/physics/Quest'}));process.exitCode=failed?1:0;
