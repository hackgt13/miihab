import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { networkInterfaces, tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';
import { ready, stop } from './test-process.ts';

test('golf relay preserves player identity and rejects stale, duplicate and malformed motion',{timeout:40000},async()=>{
  const dir=mkdtempSync(join(tmpdir(),'golf-test-'));
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18767',KINESTHETIC_GOLF_RECORDINGS:dir}});
  const clients:WebSocket[]=[];
  try {
    await ready(proc);
    async function connect(query:string){const ws=new WebSocket('ws://127.0.0.1:18767/golf?'+query);clients.push(ws);await once(ws,'open');return ws;}
    const viewer=await connect('role=viewer');const received:any[]=[];
    viewer.on('message',b=>received.push(JSON.parse(b.toString())));
    const patient=await connect('role=producer&player=patient');
    const friend=await connect('role=producer&player=friend');
    const packet=(playerId:string,sequence:number,time:number)=>({type:'club.motion',playerId,sourceId:'Left',
      sessionId:'verification-fixture',sequence,sensorTime:time,quaternion:[0,0,0,1],rotationRate:[0,1,0]});
    patient.send(JSON.stringify(packet('patient',1,1)));
    patient.send(JSON.stringify(packet('patient',1,1)));
    patient.send(JSON.stringify(packet('patient',2,.5)));
    friend.send(JSON.stringify(packet('friend',1,1)));
    await new Promise(r=>setTimeout(r,80));
    assert.equal(received.filter(p=>p.type==='club.motion').length,2);
    assert.deepEqual(received.filter(p=>p.type==='club.motion').map(p=>p.playerId).sort(),['friend','patient']);
    const closed=once(patient,'close');patient.send(JSON.stringify(packet('friend',3,3)));
    const [code]=await closed;assert.equal(code,1008);
    const closeFriend=once(friend,'close');friend.send(JSON.stringify({...packet('friend',2,2),quaternion:[0,0,0,0]}));
    const [badQuaternion]=await closeFriend;assert.equal(badQuaternion,1008);
  }finally{for(const ws of clients)ws.terminate();await stop(proc);rmSync(dir,{recursive:true,force:true});}
});

test('only the local capture page may watch motion from a browser, and never produce it',{timeout:40000},async()=>{
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18774',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-origin-'))}});
  const clients:WebSocket[]=[];
  const open=(path:string,origin:string)=>{const ws=new WebSocket(`ws://127.0.0.1:18774${path}`,{origin});clients.push(ws);
    return new Promise<boolean>(r=>{ws.once('open',()=>r(true));ws.once('error',()=>r(false));});};
  try {
    await ready(proc);
    assert.equal(await open('/golf?role=viewer','http://127.0.0.1:8766'),true);
    assert.equal(await open('/bowling-motion?role=viewer','http://localhost:8766'),true);
    assert.equal(await open('/golf?role=viewer','https://example.com'),false);
    assert.equal(await open('/golf?role=producer&player=patient','http://127.0.0.1:8766'),false);
  }finally{for(const ws of clients)ws.terminate();await stop(proc);}
});

test('a second Mac may send motion with the pairing token; nothing else reaches the relay from the network', {timeout:10000}, async()=>{
  const lan=Object.values(networkInterfaces()).flat().find(a=>a && a.family==='IPv4' && !a.internal)?.address;
  if(!lan) return;   // no network interface on this machine: nothing to test
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18785',KINESTHETIC_GOLF_HOST:'0.0.0.0',KINESTHETIC_PAIR_TOKEN:'pair-secret',
      KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-lan-'))}});
  const clients:WebSocket[]=[];
  const open=(query:string)=>{const ws=new WebSocket(`ws://${lan}:18785/bowling-motion?${query}`);clients.push(ws);
    return new Promise<boolean>(r=>{ws.once('open',()=>r(true));ws.once('error',()=>r(false));});};
  try {
    await ready(proc);
    assert.equal(await open('role=producer&player=patient&token=pair-secret'),true,'wrist AirPod on the teammate\'s Mac');
    assert.equal(await open('role=producer&player=friend'),false,'no token');
    assert.equal(await open('role=viewer&token=pair-secret'),false,'watching motion stays on this Mac');
    assert.equal((await fetch(`http://${lan}:18785/?token=pair-secret`)).status,200);
    assert.equal((await fetch(`http://${lan}:18785/`)).status,403);
  } finally { for(const ws of clients)ws.terminate(); await stop(proc); }
});
