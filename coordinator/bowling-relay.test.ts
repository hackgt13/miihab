import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';

test('bowling has an isolated state channel, late join, host exclusivity and disconnect recovery', {timeout:10000}, async()=>{
  const dir=mkdtempSync(join(tmpdir(),'bowling-test-'));
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18772',KINESTHETIC_GOLF_RECORDINGS:dir}});
  const clients:WebSocket[]=[];
  try {
    await once(proc.stdout,'data');
    async function open(path:string,role:string) {
      const ws=new WebSocket(`ws://127.0.0.1:18772/${path}?role=${role}`);clients.push(ws);
      const messages:any[]=[]; ws.on('message',b=>messages.push(JSON.parse(b.toString())));
      await once(ws,'open');return {ws,messages};
    }
    const golf=await open('state','host'), golfQuest=await open('state','client');
    const host=await open('bowling-state','host'), quest=await open('bowling-state','client');
    const got=once(quest.ws,'message');host.ws.send(JSON.stringify({type:'bowling.state',seq:7,phase:'Rolling'}));await got;
    assert.equal(quest.messages[0].seq,7);assert.equal(golfQuest.messages.length,0);
    const gotGolf=once(golfQuest.ws,'message');golf.ws.send(JSON.stringify({type:'golf.state',seq:1}));await gotGolf;
    assert.equal(quest.messages.length,1);
    const late=await open('bowling-state','client');if(!late.messages.length)await once(late.ws,'message');
    assert.equal(late.messages[0].seq,7);
    const duplicate=new WebSocket('ws://127.0.0.1:18772/bowling-state?role=host');clients.push(duplicate);
    assert.equal((await once(duplicate,'close'))[0],1008);
    const bye=once(quest.ws,'message'), closed=once(host.ws,'close');host.ws.send('{"type":"golf.state"}');
    assert.equal((await closed)[0],1008);assert.equal(JSON.parse((await bye)[0].toString()).type,'bowling.host-disconnected');
    const replacement=await open('bowling-state','host');
    const resumed=once(quest.ws,'message');replacement.ws.send('{"type":"bowling.state","seq":1}');
    assert.equal(JSON.parse((await resumed)[0].toString()).seq,1);
  } finally { for(const ws of clients)ws.terminate();const exit=once(proc,'exit');proc.kill('SIGTERM');await exit;rmSync(dir,{recursive:true,force:true}); }
});

test('wrist motion and club motion cannot cross activity channels', {timeout:10000}, async()=>{
  const dir=mkdtempSync(join(tmpdir(),'bowling-motion-test-'));
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:'18773',KINESTHETIC_GOLF_RECORDINGS:dir}});
  const clients:WebSocket[]=[];
  try {
    await once(proc.stdout,'data');
    async function open(path:string,role:string) {
      const ws=new WebSocket(`ws://127.0.0.1:18773/${path}?role=${role}&player=patient`);clients.push(ws);
      const messages:any[]=[];ws.on('message',b=>messages.push(JSON.parse(b.toString())));await once(ws,'open');return {ws,messages};
    }
    const golf=await open('golf','viewer'),bowling=await open('bowling-motion','viewer');
    const club=await open('golf','producer'),wrist=await open('bowling-motion','producer');
    const packet=(type:string,sequence=1)=>({type,playerId:'patient',sourceId:'Right',sessionId:'activity-separation-fixture',
      sequence,sensorTime:sequence*.04,quaternion:[0,0,0,1],rotationRate:[1,0,0]});
    const clubReceived=once(golf.ws,'message');club.ws.send(JSON.stringify(packet('club.motion')));await clubReceived;
    const wristReceived=once(bowling.ws,'message');wrist.ws.send(JSON.stringify(packet('bowling.motion')));await wristReceived;
    wrist.ws.send(JSON.stringify(packet('bowling.motion'))); // stale duplicate is ignored
    const next=once(bowling.ws,'message');wrist.ws.send(JSON.stringify(packet('bowling.motion',2)));await next;
    assert.deepEqual(golf.messages.map(p=>p.type),['club.motion']);
    assert.deepEqual(bowling.messages.map(p=>p.type),['bowling.motion','bowling.motion']);
    const health=await (await fetch('http://127.0.0.1:18773/')).json();
    assert.deepEqual(health.players,['patient']);assert.deepEqual(health.bowlingPlayers,['patient']);
    const invalid=once(wrist.ws,'close'),disconnected=once(bowling.ws,'message');
    wrist.ws.send(JSON.stringify(packet('club.motion',3)));
    assert.equal((await invalid)[0],1008);assert.equal(JSON.parse((await disconnected)[0].toString()).type,'bowling.disconnected');
    assert.equal(golf.messages.length,1,'bowling disconnect must not stop golf');
  } finally {for(const ws of clients)ws.terminate();const exit=once(proc,'exit');proc.kill('SIGTERM');await exit;rmSync(dir,{recursive:true,force:true});}
});
