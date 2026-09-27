import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { connect } from 'node:net';
import { networkInterfaces, tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';
import { ready, stop } from './test-process.ts';

type Peer={ws:WebSocket,messages:any[]};
function relay(port:string,extra:Record<string,string>={}){
  const dir=mkdtempSync(join(tmpdir(),'ui-test-'));
  const proc=spawn(process.execPath,['golf-relay.ts'],{cwd:import.meta.dirname,
    env:{...process.env,KINESTHETIC_GOLF_PORT:port,KINESTHETIC_GOLF_RECORDINGS:dir,...extra}});
  const clients:WebSocket[]=[];
  async function open(role:string,query=''):Promise<Peer>{
    const ws=new WebSocket(`ws://127.0.0.1:${port}/ui?role=${role}${query}`);clients.push(ws);ws.on('error',()=>{});
    const messages:any[]=[];ws.on('message',b=>messages.push(JSON.parse(b.toString())));
    await once(ws,'open');return {ws,messages};
  }
  // Wait until a peer has heard at least n messages; a message that already arrived counts.
  async function heard(peer:Peer,n:number){while(peer.messages.length<n)await once(peer.ws,'message');}
  const settle=(ms=80)=>new Promise(r=>setTimeout(r,ms));
  const status=async()=>(await fetch(`http://127.0.0.1:${port}/`)).json();
  const close=async()=>{for(const ws of clients)ws.terminate();await stop(proc);rmSync(dir,{recursive:true,force:true});};
  return {proc,clients,open,heard,settle,status,close};
}
const press=(seq:number,extra={})=>JSON.stringify({type:'ui.press',seq,board:'golf.hud',path:'0/3',name:'swing',...extra});
const snapshot=(bytes:number,rev=1)=>({type:'ui.snapshot',proto:1,vocab:'v',rev,boards:[{id:'golf.hud',tree:{t:'VisualElement',n:'x'.repeat(bytes)}}]});

test('ui: one host, unique client ids, resync both ways, broadcast trees, acks to one client, presses stamped and checked',{timeout:40000},async()=>{
  const r=relay('18820');
  try {
    await ready(r.proc);
    const c1=await r.open('client');await r.heard(c1,1);
    assert.deepEqual(c1.messages[0],{type:'ui.welcome',client:'c1'},'a loopback client needs no token');
    const host=await r.open('host');await r.heard(host,1);
    assert.deepEqual(host.messages[0],{type:'ui.resync',client:'c1'},'a host joining finds the waiting client');
    const c2=await r.open('client');await r.heard(c2,1);await r.heard(host,2);
    assert.deepEqual(c2.messages[0],{type:'ui.welcome',client:'c2'});
    assert.deepEqual(host.messages[1],{type:'ui.resync',client:'c2'},'a client joining a live host is resynced');
    const duplicate=new WebSocket('ws://127.0.0.1:18820/ui?role=host');r.clients.push(duplicate);
    assert.equal((await once(duplicate,'close'))[0],1008,'one host');
    assert.deepEqual(await r.status().then(s=>[s.uiHost,s.uiClients]),[true,2]);
    // Trees reach every client, and may be far larger than a game-state message.
    const big=snapshot(300*1024);
    host.ws.send(JSON.stringify(big));await r.heard(c1,2);await r.heard(c2,2);
    assert.equal(c1.messages[1].boards[0].tree.n.length,300*1024);assert.deepEqual(c2.messages[1],big);
    const patch={type:'ui.patch',proto:1,vocab:'v',rev:2,base:1,ops:[{op:'set',p:'0',f:{x:'Hi'}}]};
    host.ws.send(JSON.stringify(patch));await r.heard(c1,3);await r.heard(c2,3);
    assert.deepEqual(c1.messages[2],patch);assert.deepEqual(c2.messages[2],patch);
    // An ack goes to its client only; one for a client that is gone is dropped without closing the host.
    host.ws.send(JSON.stringify({type:'ui.ack',client:'c2',seq:4}));await r.heard(c2,4);
    host.ws.send(JSON.stringify({type:'ui.ack',client:'c9',seq:1}));await r.settle();
    assert.deepEqual(c2.messages[3],{type:'ui.ack',client:'c2',seq:4});assert.equal(c1.messages.length,3);
    assert.equal(host.ws.readyState,WebSocket.OPEN);
    // A client asks for a resync; a press is forwarded stamped with the relay's id, and only its known fields.
    c1.ws.send(JSON.stringify({type:'ui.resync',client:'c2'}));await r.heard(host,3);
    assert.deepEqual(host.messages[2],{type:'ui.resync',client:'c1'});
    c1.ws.send(press(7,{client:'c2',extra:'smuggled',path:''}));await r.heard(host,4);
    assert.deepEqual(host.messages[3],{type:'ui.press',client:'c1',seq:7,board:'golf.hud',path:'',name:'swing'});
    // Malformed presses are dropped, not fatal: the next good one still arrives on the same socket.
    for(const bad of [press(-1),press(1.5),press('3' as any),press(2**53),press(1,{board:'x/y'}),press(1,{name:'a b'}),
      press(1,{name:'n'.repeat(81)}),press(1,{path:'a/b'}),press(1,{path:'0/'}),press(1,{path:'12345'}),
      press(1,{path:Array(65).fill('1').join('/')}),JSON.stringify({type:'ui.snapshot'}),'{"type":"ui.press"','null','[]'])c1.ws.send(bad);
    c1.ws.send(press(8,{path:'12/3/4'}));await r.heard(host,5);
    assert.equal(host.messages.length,5);assert.deepEqual(host.messages[4],{type:'ui.press',client:'c1',seq:8,board:'golf.hud',path:'12/3/4',name:'swing'});
    assert.equal(c1.ws.readyState,WebSocket.OPEN);
    // Exactly 1 KiB is still a message; one byte more is refused by the socket itself, before anything is buffered.
    c1.ws.send(press(9,{pad:'x'.repeat(1024-press(9,{pad:''}).length)}));await r.heard(host,6);
    assert.equal(host.messages[5].seq,9);
    const fat=await r.open('client');fat.ws.send(press(1,{pad:'x'.repeat(1100)}));
    assert.equal((await once(fat.ws,'close'))[0],1009);
    // A host that speaks anything else is cut off, and every client hears it; the next host resyncs them all.
    const hostClosed=once(host.ws,'close');host.ws.send(JSON.stringify({type:'golf.state'}));
    assert.equal((await hostClosed)[0],1008);await r.heard(c1,4);await r.heard(c2,5);
    assert.deepEqual(c1.messages[3],{type:'ui.host-disconnected'});assert.deepEqual(c2.messages[4],{type:'ui.host-disconnected'});
    assert.deepEqual(await r.status().then(s=>[s.uiHost,s.uiClients]),[false,2]);
    const replacement=await r.open('host');await r.heard(replacement,2);
    assert.deepEqual(replacement.messages.map(m=>m.client).sort(),['c1','c2']);
    c2.ws.close();await r.settle();
    assert.deepEqual(await r.status().then(s=>[s.uiHost,s.uiClients]),[true,1]);
    const c4=await r.open('client');await r.heard(c4,1);
    assert.equal(c4.messages[0].client,'c4','ids are never reused');
  } finally { await r.close(); }
});

test('ui: a cue from the host reaches every client as sent; a cue with no kind cuts the host off',{timeout:40000},async()=>{
  const r=relay('18825');
  try {
    await ready(r.proc);
    const c1=await r.open('client'),c2=await r.open('client');await r.heard(c1,1);await r.heard(c2,1);
    const host=await r.open('host');await r.heard(host,2);
    const leave={type:'ui.cue',kind:'scene',scene:'Rehab',venue:'studio',phase:'leave'};
    host.ws.send(JSON.stringify(leave));await r.heard(c1,2);await r.heard(c2,2);
    assert.deepEqual(c1.messages[1],leave);assert.deepEqual(c2.messages[1],leave);
    const face={type:'ui.cue',kind:'face',slot:'gallery'};
    host.ws.send(JSON.stringify(face));await r.heard(c1,3);
    assert.deepEqual(c1.messages[2],face);
    // A client cannot forge one: it is not a press, so it is dropped and the host never hears it.
    c1.ws.send(JSON.stringify(leave));await r.settle();
    assert.equal(host.messages.length,2);
    const closed=once(host.ws,'close');host.ws.send(JSON.stringify({type:'ui.cue',scene:'Rehab'}));
    assert.equal((await closed)[0],1008);
  } finally { await r.close(); }
});

test('ui: a client that floods is throttled, then disconnected; a burst refills; rare drops are forgiven',{timeout:40000},async()=>{
  const r=relay('18821');
  try {
    await ready(r.proc);
    const host=await r.open('host'),flood=await r.open('client'),calm=await r.open('client');await r.heard(host,2);
    const closed=once(flood.ws,'close');
    for(let i=0;i<400;i++)flood.ws.send(press(i));
    assert.equal((await closed)[0],1008);await r.settle();
    const presses=host.messages.filter(m=>m.type==='ui.press');
    assert.ok(presses.length>=20 && presses.length<30,`the first 20 or so pass: ${presses.length}`);
    assert.ok(presses.every(m=>m.client==='c1'));
    // The other client is untouched: a burst of 25 lets 20 through, and a second's wait refills the bucket.
    for(let i=0;i<25;i++)calm.ws.send(press(i));await r.settle();
    const burst=host.messages.filter(m=>m.client==='c2');
    assert.ok(burst.length>=20 && burst.length<25,`${burst.length} of the burst passed`);
    await r.settle(1100);calm.ws.send(press(99));await r.heard(host,host.messages.length+1);
    assert.deepEqual(host.messages.at(-1),{type:'ui.press',client:'c2',seq:99,board:'golf.hud',path:'0/3',name:'swing'});
    assert.equal(calm.ws.readyState,WebSocket.OPEN);
    // The drop count forgives one drop a second: 199 drops, a 1.5 s pause, two more and a good press keep the
    // socket open; the drop after that is the 200th.
    const rare=await r.open('client');await r.heard(rare,1);
    for(let i=0;i<199;i++)rare.ws.send('x');await r.settle(1500);
    rare.ws.send('x');rare.ws.send('x');rare.ws.send(press(5));await r.heard(host,host.messages.length+1);
    assert.deepEqual(host.messages.at(-1),{type:'ui.press',client:'c3',seq:5,board:'golf.hud',path:'0/3',name:'swing'});
    assert.equal(rare.ws.readyState,WebSocket.OPEN);
    const cut=once(rare.ws,'close');rare.ws.send('x');assert.equal((await cut)[0],1008);
  } finally { await r.close(); }
});

test('ui: a host still closing gives way to the next one, and a client that cannot keep up is told to come back',{timeout:40000},async()=>{
  const r=relay('18824');
  try {
    await ready(r.proc);
    const host=await r.open('host'),c1=await r.open('client');await r.heard(host,1);
    // The old host stops reading, then earns a 1008: the relay's side sits in CLOSING, waiting for a reply that never comes.
    host.ws.pause();host.ws.send(JSON.stringify({type:'golf.state'}));await r.settle();
    assert.equal((await r.status()).uiHost,true);
    const next=await r.open('host');await r.heard(next,1);
    assert.deepEqual(next.messages[0],{type:'ui.resync',client:'c1'},'the new host is accepted and resynced');
    host.ws.resume();await once(host.ws,'close');await r.settle();
    assert.equal((await r.status()).uiHost,true,'the old host going does not unseat the new one');
    assert.deepEqual(c1.messages.map(m=>m.type),['ui.welcome'],'no host-disconnected for a takeover');
    next.ws.send(JSON.stringify(snapshot(10)));await r.heard(c1,2);assert.equal(c1.messages[1].rev,1);
    // A client that stops reading fills its buffer; rather than skipping trees it is closed with 1013 to reconnect.
    const slow=await r.open('client'),quick=await r.open('client');await r.heard(next,3);
    // Paced on the healthy client's receipt, so only the paused one backs up (20 MiB, well past any loopback buffer).
    slow.ws.pause();
    for(let i=0;i<40;i++){next.ws.send(JSON.stringify(snapshot(512*1024,i)));await r.heard(quick,i+2);}
    slow.ws.resume();assert.equal((await once(slow.ws,'close'))[0],1013);
    await r.settle();assert.deepEqual(await r.status().then(s=>[s.uiHost,s.uiClients]),[true,2]);
    assert.equal(next.ws.readyState,WebSocket.OPEN);
  } finally { await r.close(); }
});

test('ui: a duplicate host that misbehaves while being turned away cannot crash the relay',{timeout:40000},async()=>{
  const r=relay('18825');let raw:import('node:net').Socket|undefined;
  try {
    await ready(r.proc);
    const host=await r.open('host');
    raw=connect(18825,'127.0.0.1');await once(raw,'connect');
    raw.write('GET /ui?role=host HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n'+
      'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n');
    let got=Buffer.alloc(0);
    for(;;){got=Buffer.concat([got,(await once(raw,'data'))[0]]);const end=got.indexOf('\r\n\r\n');if(end>=0&&got.length>end+4){assert.equal(got[end+4],0x88,'a close frame');break;}}
    // An unmasked frame is a protocol error, which ws reports on the socket's 'error' event.
    raw.write(Buffer.from([0x81,0x00]));await r.settle();
    assert.equal(r.proc.exitCode,null);assert.equal((await r.status()).uiHost,true);
    assert.equal(host.ws.readyState,WebSocket.OPEN);
  } finally { raw?.destroy();await r.close(); }
});

test('ui: no browser may join, in either role',{timeout:40000},async()=>{
  const r=relay('18822');
  const open=(query:string,origin:string)=>{const ws=new WebSocket(`ws://127.0.0.1:18822/ui?${query}`,{origin});r.clients.push(ws);
    return new Promise<boolean>(r=>{ws.once('open',()=>r(true));ws.once('error',()=>r(false));});};
  try {
    await ready(r.proc);
    assert.equal(await open('role=host','http://127.0.0.1:8766'),false);
    assert.equal(await open('role=client','http://127.0.0.1:8766'),false);
    assert.equal(await open('role=client','https://example.com'),false);
    assert.equal(await open('role=viewer',''),false,'no other role');
    assert.equal(await open('role=client',''),true);
  } finally { await r.close(); }
});

test('ui: over the network a headset needs the pairing token, and may never be the host',{timeout:10000},async()=>{
  const lan=Object.values(networkInterfaces()).flat().find(a=>a && a.family==='IPv4' && !a.internal)?.address;
  if(!lan) return;   // no network interface on this machine: nothing to test
  const r=relay('18823',{KINESTHETIC_GOLF_HOST:'0.0.0.0',KINESTHETIC_PAIR_TOKEN:'pair-secret'});
  const open=(query:string)=>{const ws=new WebSocket(`ws://${lan}:18823/ui?${query}`);r.clients.push(ws);
    return new Promise<boolean>(r=>{ws.once('open',()=>r(true));ws.once('error',()=>r(false));});};
  try {
    await ready(r.proc);
    assert.equal(await open('role=client&token=pair-secret'),true);
    assert.equal(await open('role=client'),false,'no token');
    assert.equal(await open('role=client&token=wrong'),false);
    assert.equal(await open('role=host&token=pair-secret'),false,'the host is the Mac itself');
  } finally { await r.close(); }
});
