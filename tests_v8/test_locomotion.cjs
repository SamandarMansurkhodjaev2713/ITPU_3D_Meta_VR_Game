'use strict';const assert=require('node:assert/strict');const fs=require('node:fs');const{TeleportGate:G,STATES:S}=require('../src/reference/locomotion_v8.js');let results=[];
function ctx(p={}){return{tracked:true,headTracked:true,focused:true,paused:false,uiBusy:false,holding:false,systemGesture:false,pathClear:true,landingClear:true,headClear:true,targetEnabled:true,anchor:'HALL_A',source:'right',revision:'scene1',distance:2,...p};}
function test(name,f){try{f();results.push({name,status:'PASS'});}catch(e){results.push({name,status:'FAIL',error:e.stack});}}
function armed(){let g=new G();let c=ctx();assert(g.aim(0,c));assert(g.press(.2,c));return[g,c];}
test('Explicit hand sequence -> one request -> one commit',()=>{const[g,c]=armed();const r=g.release(.4,c);assert(r);assert(g.commit(.5,r,c));assert.equal(g.commit(.6,r,c),false);});
test('Controller semantic source uses same guard',()=>{let g=new G(),c=ctx({source:'controller_left'});assert(g.aim(0,c));assert(g.press(.2,c));let r=g.release(.4,c);assert(g.commit(.5,r,c));});
for(const field of ['tracked','headTracked','focused','pathClear','landingClear','headClear','targetEnabled']){
 test(field+' false cannot arm',()=>{const g=new G();assert.equal(g.aim(0,ctx({[field]:false})),false);});
 test(field+' lost while pressed cancels, recovery release rejected',()=>{const[g,c]=armed();g.tick(.25,ctx({[field]:false}));assert.equal(g.release(.4,c),null);});
 test(field+' invalid before rig commit cancels',()=>{const[g,c]=armed();const r=g.release(.4,c);assert.equal(g.commit(.5,r,ctx({[field]:false})),false);});
}
for(const field of ['paused','holding','uiBusy','systemGesture']){
 test(field+' blocks aim',()=>{const g=new G();assert.equal(g.aim(0,ctx({[field]:true})),false);});
 test(field+' during hold cancels',()=>{const[g,c]=armed();g.tick(.25,ctx({[field]:true}));assert.equal(g.release(.4,c),null);});
}
for(const p of [{anchor:'ROOM_401'}, {source:'left'}, {revision:'scene2'}])test('Context change '+JSON.stringify(p),()=>{const[g,c]=armed();assert.equal(g.release(.4,ctx(p)),null);});
test('No release without press',()=>{const g=new G();g.aim(0,ctx());assert.equal(g.release(.2,ctx()),null);});
test('Tiny tap not intentional hold',()=>{const[g,c]=armed();assert.equal(g.release(.21,c),null);});
test('No stale replay after reset',()=>{const[g,c]=armed(),r=g.release(.4,c);g.reset(.5);assert.equal(g.commit(.6,r,c),false);});
test('Cross instance request rejected',()=>{const[g,c]=armed(),r=g.release(.4,c);const[h,d]=armed();h.release(.4,d);assert.equal(h.commit(.5,r,d),false);});
test('Rebuilt equivalent object is not internal request',()=>{const[g,c]=armed(),r=g.release(.4,c);assert.equal(g.commit(.5,{...r},c),false);});
test('Cooldown requires neutral then fresh aim',()=>{const[g,c]=armed(),r=g.release(.4,c);assert(g.commit(.5,r,c));assert.equal(g.neutral(.6),false);assert(g.neutral(.81));assert(g.aim(.82,c));assert(g.press(1.0,c));});
test('Second hand cannot override armed target',()=>{const[g,c]=armed();assert.equal(g.aim(.3,ctx({source:'left'})),false);assert(g.release(.4,c));});
test('Stable aim needed',()=>{const g=new G();g.aim(0,ctx());assert.equal(g.press(.01,ctx()),false);});
for(const x of [NaN,Infinity,-1,.1,8,'2'])test('Bad target distance '+String(x),()=>{assert.equal(new G().aim(0,ctx({distance:x})),false);});
for(const x of [NaN,Infinity,-1,'2'])test('Bad clock '+String(x),()=>{assert.throws(()=>new G().aim(x,ctx()));});
test('Clock rollback cancels',()=>{const[g,c]=armed();assert.throws(()=>g.release(.1,c));assert.equal(g.state,S.NEUTRAL);});
test('Missing context fails closed',()=>{assert.equal(new G().aim(0,{}),false);});
test('Non boolean permissive flag rejected',()=>{assert.equal(new G().aim(0,ctx({pathClear:1})),false);});
test('No pause release can trigger on recovery without fresh press',()=>{const[g,c]=armed();g.tick(.3,ctx({paused:true}));g.neutral(.5);assert(g.aim(.6,c));assert.equal(g.release(.8,c),null);});
test('Invalid clock clears armed intent',()=>{const[g,c]=armed();assert.throws(()=>g.release(NaN,c));assert.equal(g.state,S.NEUTRAL);assert.equal(g.release(.4,c),null);});
test('Immutable request',()=>{const[g,c]=armed();let r=g.release(.4,c);assert(Object.isFrozen(r));assert.throws(()=>r.anchor='elsewhere');});
test('Uncommitted request expires instead of delayed jump',()=>{const[g,c]=armed(),r=g.release(.4,c);assert.equal(g.commit(1.5,r,c),false);});
test('Tick detects pending expiry',()=>{const[g,c]=armed();g.release(.4,c);assert.equal(g.tick(1.5,c),false);assert.equal(g.state,S.NEUTRAL);});
test('No negative maxPending',()=>{assert.throws(()=>new G({maxPending:-1}));});
console.log(JSON.stringify({level:'JS semantic gate; not device gesture recognition',passed:results.filter(r=>r.status==='PASS').length,failed:results.filter(r=>r.status==='FAIL').length,results},null,2));if(results.some(r=>r.status==='FAIL'))process.exit(1);
