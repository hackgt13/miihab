import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { networkInterfaces, tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';
import { createSocket } from 'node:dgram';
import { createHmac } from 'node:crypto';
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

test('on the network the relay announces itself with a proof only the pairing token can make', {timeout:10000}, async()=>{
  const lan=Object.values(networkInterfaces()).flat().find(a=>a && a.family==='IPv4' && !a.internal)?.address;
  if(!lan) return;   // no network interface on this machine: nothing to announce on
  const listener=createSocket({type:'udp4',reuseAddr:true});
  await new Promise<void>(r=>listener.bind(18786,r));
  const heard=new Promise<{msg:any,from:string}>(r=>listener.on('message',(m,info)=>{
    const msg=JSON.parse(String(m)); if(msg.host===info.address) r({msg,from:info.address});}));
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18787',KINESTHETIC_BEACON_PORT:'18786',KINESTHETIC_GOLF_HOST:'0.0.0.0',
      KINESTHETIC_PAIR_TOKEN:'pair-secret',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-beacon-'))}});
  try {
    await ready(proc);
    const {msg,from}=await heard;
    assert.equal(msg.service,'rehabmii-relay'); assert.equal(msg.port,18787);
    assert.equal(msg.proof,createHmac('sha256','pair-secret').update(from).digest('hex'),'verifiable with the token, for the sender address');
    assert.notEqual(msg.proof,createHmac('sha256','other-token').update(from).digest('hex'));
    assert.ok(!JSON.stringify(msg).includes('pair-secret'),'the token itself is never broadcast');
  } finally { listener.close(); await stop(proc); }
});

test('the second Mac is read by the activity: golf makes it the friend, anything else the patient\'s second AirPod',{timeout:40000},async()=>{
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18791',KINESTHETIC_PAIR_TOKEN:'pair-secret',
      KINESTHETIC_COORDINATOR_URL:'http://127.0.0.1:9',   // no coordinator: never in a group
      KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-route-'))}});
  const clients:WebSocket[]=[];
  const connect=async(path:string)=>{const ws=new WebSocket('ws://127.0.0.1:18791'+path);clients.push(ws);await once(ws,'open');return ws;};
  const settle=()=>new Promise(r=>setTimeout(r,80));
  try {
    await ready(proc);
    const club:any[]=[],wrist:any[]=[];
    (await connect('/golf?role=viewer')).on('message',b=>club.push(JSON.parse(b.toString())));
    (await connect('/bowling-motion?role=viewer')).on('message',b=>wrist.push(JSON.parse(b.toString())));
    // Whatever its picker says — here "friend", on the club path — the paired Mac's stream is placed by the activity.
    const second=await connect('/golf?role=producer&player=friend&token=pair-secret');
    let n=0;const send=()=>{n++;second.send(JSON.stringify({type:'club.motion',playerId:'friend',sourceId:'Left',
      sessionId:'route-fixture',sequence:n,sensorTime:n,quaternion:[0,0,0,1],rotationRate:[0,1,0]}));};

    send();await settle();   // nothing running: one person, so this is the patient's second AirPod
    assert.deepEqual(wrist.filter(p=>p.type==='bowling.motion').map(p=>p.playerId),['patient']);
    assert.equal(club.filter(p=>p.type==='club.motion').length,0);

    const golf=await connect('/state?role=host');   // golf is up: two people
    send();await settle();
    assert.deepEqual(club.filter(p=>p.type==='club.motion').map(p=>p.playerId),['friend']);
    assert.ok(wrist.some(p=>p.type==='bowling.disconnected'&&p.playerId==='patient'),'the old reading is told it ended');

    golf.close();await once(golf,'close');await settle();   // back to the studio
    send();await settle();
    assert.equal(wrist.filter(p=>p.type==='bowling.motion').length,2);

    // A stream from this Mac is untouched: its own path, its own player.
    const local=await connect('/golf?role=producer&player=patient');
    local.send(JSON.stringify({type:'club.motion',playerId:'patient',sourceId:'Right',sessionId:'local',sequence:1,sensorTime:1,quaternion:[0,0,0,1],rotationRate:[0,0,0]}));
    await settle();
    assert.deepEqual(club.filter(p=>p.type==='club.motion').map(p=>p.playerId),['friend','patient']);

    // …and if this Mac's own AirPod is the wrist one, the second Mac's reading takes the club path instead.
    const localWrist=await connect('/bowling-motion?role=producer&player=patient');
    localWrist.send(JSON.stringify({type:'bowling.motion',playerId:'patient',sourceId:'Right',sessionId:'local-wrist',sequence:1,sensorTime:1,quaternion:[0,0,0,1],rotationRate:[0,0,0]}));
    send();await settle();
    assert.deepEqual(club.filter(p=>p.type==='club.motion').map(p=>p.playerId),['friend','patient','patient']);
    assert.equal(club.filter(p=>p.type==='club.motion').at(-1).sessionId,'route-fixture');

    // Both Macs on the same app with the same picker is fine: the second is registered apart, and still reports
    // as "patient" on the status page its app reads.
    const twin=await connect('/golf?role=producer&player=patient&token=pair-secret');
    twin.send(JSON.stringify({type:'club.motion',playerId:'patient',sourceId:'Left',sessionId:'twin',sequence:1,sensorTime:1,quaternion:[0,0,0,1],rotationRate:[0,0,0]}));
    await settle();
    assert.equal(twin.readyState,WebSocket.OPEN);
    const health=await (await fetch('http://127.0.0.1:18791/')).json();
    assert.ok(health.players.includes('patient') && !health.players.some((p:string)=>p.includes('@')));
  } finally { for(const ws of clients)ws.terminate(); await stop(proc); }
});

test('in a group session the second Mac is the other person, whatever is running',{timeout:40000},async()=>{
  // A stand-in coordinator that says the patient is in a group.
  const { createServer } = await import('node:http');
  let group:unknown={id:'room'};
  const coordinator=createServer((_q,r)=>r.writeHead(200,{'Content-Type':'application/json'}).end(JSON.stringify({group})));
  coordinator.listen(18799);await once(coordinator,'listening');
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18792',KINESTHETIC_PAIR_TOKEN:'pair-secret',
      KINESTHETIC_COORDINATOR_URL:'http://127.0.0.1:18799',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-group-'))}});
  const clients:WebSocket[]=[];
  const connect=async(path:string)=>{const ws=new WebSocket('ws://127.0.0.1:18792'+path);clients.push(ws);await once(ws,'open');return ws;};
  const wait=(ms:number)=>new Promise(r=>setTimeout(r,ms));
  try {
    await ready(proc);await wait(300);   // the first answer from the coordinator
    const club:any[]=[],wrist:any[]=[];
    (await connect('/golf?role=viewer')).on('message',b=>club.push(JSON.parse(b.toString())));
    (await connect('/bowling-motion?role=viewer')).on('message',b=>wrist.push(JSON.parse(b.toString())));
    await connect('/rehab-state?role=host');   // the studio, an activity for one
    const second=await connect('/bowling-motion?role=producer&player=patient&token=pair-secret');
    let n=0;const send=()=>{n++;second.send(JSON.stringify({type:'bowling.motion',playerId:'patient',sourceId:'Left',
      sessionId:'group-fixture',sequence:n,sensorTime:n,quaternion:[0,0,0,1],rotationRate:[0,1,0]}));};
    send();await wait(80);
    assert.deepEqual(club.filter(p=>p.type==='club.motion').map(p=>p.playerId),['friend']);
    assert.equal(wrist.filter(p=>p.type==='bowling.motion').length,0,'never read as the patient\'s own sensor');

    group=null;await wait(1300);   // left the group: back to the patient's second AirPod
    send();await wait(80);
    assert.deepEqual(wrist.filter(p=>p.type==='bowling.motion').map(p=>p.playerId),['patient']);
  } finally { for(const ws of clients)ws.terminate(); await stop(proc); coordinator.close(); }
});

test('a late client is caught up from the host\'s last state only while it is fresh',{timeout:40000},async()=>{
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18790',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-fresh-'))}});
  const clients:WebSocket[]=[];
  const connect=async(query:string)=>{const ws=new WebSocket('ws://127.0.0.1:18790/state?'+query);clients.push(ws);await once(ws,'open');return ws;};
  const firstMessage=(ws:WebSocket,ms:number)=>new Promise<string|null>(r=>{const t=setTimeout(()=>r(null),ms);ws.once('message',b=>{clearTimeout(t);r(b.toString());});});
  try {
    await ready(proc);
    const host=await connect('role=host');
    host.send(JSON.stringify({type:'golf.state',hole:1}));
    await new Promise(r=>setTimeout(r,50));
    // Joining right after a frame: caught up at once.
    const prompt=await connect('role=client');
    assert.deepEqual(JSON.parse((await firstMessage(prompt,500))!),{type:'golf.state',hole:1});
    // Joining after the host has been silent past the freshness window, without closing: nothing replayed.
    await new Promise(r=>setTimeout(r,2200));
    const late=await connect('role=client');
    assert.equal(await firstMessage(late,300),null);
    // The host publishing again catches everyone up live, as before.
    host.send(JSON.stringify({type:'golf.state',hole:2}));
    assert.deepEqual(JSON.parse((await firstMessage(late,500))!),{type:'golf.state',hole:2});
  }finally{for(const ws of clients)ws.terminate();await stop(proc);}
});
