import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';

test('golf relay preserves player identity and rejects stale, duplicate and malformed motion',{timeout:10000},async()=>{
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18767',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-test-'))}});
  const clients:WebSocket[]=[];
  try {
    await once(proc.stdout,'data');
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
  }finally{for(const ws of clients)ws.terminate();proc.kill('SIGTERM');}
});

test('game state: one host fans out to Quest clients, late joiners get the latest frame, junk is rejected',{timeout:10000},async()=>{
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18768',KINESTHETIC_GOLF_RECORDINGS:mkdtempSync(join(tmpdir(),'golf-state-'))}});
  const clients:WebSocket[]=[];
  try{
    await once(proc.stdout,'data');
    const open=async(q:string)=>{const ws=new WebSocket('ws://127.0.0.1:18768/state?'+q);clients.push(ws);await once(ws,'open');return ws;};
    const host=await open('role=host');const quest=await open('role=client');
    const got=once(quest,'message');host.send(JSON.stringify({type:'golf.state',seq:1,phase:'Address'}));
    assert.equal(JSON.parse((await got)[0].toString()).seq,1);
    const late=await open('role=client');const lateMsg=await once(late,'message');
    assert.equal(JSON.parse(lateMsg[0].toString()).seq,1,'late joiner receives the latest state');
    const second=new WebSocket('ws://127.0.0.1:18768/state?role=host');clients.push(second);
    assert.equal((await once(second,'close'))[0],1008,'only one host');
    const bye=once(quest,'message');const closed=once(host,'close');host.send('{"type":"nope"}');
    assert.equal((await closed)[0],1008);
    assert.equal(JSON.parse((await bye)[0].toString()).type,'golf.host-disconnected');
  }finally{for(const ws of clients)ws.terminate();proc.kill('SIGTERM');}
});
