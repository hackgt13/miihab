import { createServer } from 'node:http';
import { createSocket } from 'node:dgram';
import { createHmac } from 'node:crypto';
import { networkInterfaces } from 'node:os';
import { mkdirSync, createWriteStream } from 'node:fs';
import { resolve } from 'node:path';
import { WebSocket, WebSocketServer } from 'ws';
import { hostMonotonicMs } from './hostclock.ts';
import { DominantMotion, type Mac } from './motion-fuse.ts';

// Separate from the already-running pose proof. Native hosts connect here.
const port=Number(process.env.KINESTHETIC_GOLF_PORT ?? 8767);
function motionChannel(type:string,dir:string){
  mkdirSync(dir,{recursive:true});
  return {type,dir,viewers:new Set<WebSocket>(),producers:new Map<string,WebSocket>(),
    received:new Map<string,{at:number,sequence:number,sourceId:string}>()};
}
const golfDir=resolve(process.env.KINESTHETIC_GOLF_RECORDINGS ?? resolve(import.meta.dirname,'../local-data/golf'));
const bowlingDir=resolve(process.env.KINESTHETIC_BOWLING_RECORDINGS ??
  (process.env.KINESTHETIC_GOLF_RECORDINGS ? resolve(golfDir,'bowling') : resolve(import.meta.dirname,'../local-data/bowling')));
const motions=new Map([['/golf',motionChannel('club',golfDir)],['/bowling-motion',motionChannel('bowling',bowlingDir)]]);
const golfMotion=motions.get('/golf')!,bowlingMotion=motions.get('/bowling-motion')!;
const viewerOrigins=new Set(['http://127.0.0.1:8766','http://localhost:8766']);
const loopback=(address?:string)=>['127.0.0.1','::1','::ffff:127.0.0.1'].includes(address??'');
const channels=new Map(['/state','/bowling-state','/rehab-state','/tutorial-state'].map(path=>[path,{
  host:null as WebSocket|null,clients:new Set<WebSocket>(),last:null as string|null,lastAt:0,
  type:({'/state':'golf','/bowling-state':'bowling','/rehab-state':'rehab','/tutorial-state':'tutorial'} as Record<string,string>)[path]
}]));
const golfState=channels.get('/state')!;


// One or two (AGENTS.md, Sensors). Mac 1 is this Mac's pair, Mac 2 the pair on the second Mac, which reaches this
// relay over the tailnet with the pairing token. Solo, both are the patient's. In a group session each person has
// one: Mac 2 is the other person, on /golf as `friend`, and never reaches the patient's record. Which app or path
// either arrives on is transport and nothing else — both apps and both paths feed the same two slots.
//
//   /motion                    raw: every sample of both pairs, tagged mac1/mac2 (the coordinator measures from it)
//   /golf, /bowling-motion     the games: one stream as `patient`, the more active pair, re-based so it never jumps
//                              (motion-fuse.ts). Golf holds both pairs in the grip; bowling wears one on the wrist.
//
// So the second Mac needs no setting at all and no game knows there are two. A local app whose picker says `friend`
// is still a friend on its own path. KINESTHETIC_REMOTE_MOTION=tagged takes each app's picker at its word instead.
const remoteMotion=process.env.KINESTHETIC_REMOTE_MOTION??'auto';
const remoteProducers=new WeakSet<WebSocket>();
// The second Mac is registered apart from this one, so both may run the same app with the same picker; the status
// page still names it by its picker, which is what that app checks to say it is streaming.
const remoteKey='@second-mac',named=(key:string)=>key.replace(remoteKey,'');
// Whether the patient is in a group session, asked of the coordinator (groups.ts), which is the authority on it.
// Polled rather than pushed so neither process needs the other to start first; an unreachable coordinator is
// "not in a group", the one-person reading.
const coordinator=process.env.KINESTHETIC_COORDINATOR_URL??'http://127.0.0.1:8766';
let inGroup=false;
const askGroup=async()=>{
  try{const r=await fetch(coordinator+'/api/groups/current',{signal:AbortSignal.timeout(800)});
    inGroup=r.ok&&!!(await r.json())?.group?.id;}catch{inGroup=false;}
};
if(remoteMotion!=='tagged'){askGroup();setInterval(askGroup,1000).unref();}
function route(path:string,player:string,remote:boolean):{path:string,player:string}{
  if(!remote||remoteMotion==='tagged')return {path,player};
  // Multiplayer is a group session: one pair each. Solo, the second pair is the patient's.
  return inGroup?{path:'/golf',player:'friend'}:{path,player:'patient'};
}
// A motion app on another Mac (a second AirPod pair, e.g. on a wrist strap) may send motion here, and read this
// status to confirm it, when it carries the pairing token. Viewing motion stays on this Mac.
const paired=(req:import('node:http').IncomingMessage)=>pairToken!=='' &&
  new URL(req.url??'/','http://relay').searchParams.get('token')===pairToken;
// Whether the second Mac is streaming right now, on either path: the coordinator reads this to seat it in a group
// as a person (groups.ts `peerPresent`).
const secondMac=()=>({live:[...golfMotion.received,...bowlingMotion.received]
  .some(([key,s])=>key.endsWith(remoteKey)&&Date.now()-s.at<3000)});
const server=createServer((req,res)=>{
  if(!loopback(req.socket.remoteAddress) && !paired(req)){res.writeHead(403).end();return;}
  res.writeHead(200,{'Content-Type':'application/json'}).end(JSON.stringify({
    ready:true,head:headLast?{connected:true,ageMs:Date.now()-headLast.at,seq:headLast.seq}:{connected:!!headProducer},players:[...golfMotion.producers.keys()].map(named),viewers:golfMotion.viewers.size,stateHost:!!golfState.host,stateClients:golfState.clients.size,
    secondMac:secondMac(),
    bowlingPlayers:[...bowlingMotion.producers.keys()].map(named),bowlingViewers:bowlingMotion.viewers.size,
    bowlingHost:!!channels.get('/bowling-state')!.host,uiHost:!!ui.host,uiClients:ui.clients.size,
    macs:Object.fromEntries([...macSlots].map(([mac,slot])=>[mac,{remote:slot.remote,via:motions.get(slot.path)!.type,
      ...(slot.last?{ageMs:Date.now()-slot.last.at,sequence:slot.last.sequence,sourceId:slot.last.sourceId}:{})}])),
    following:fuse.picked,
    samples:Object.fromEntries([...golfMotion.received].map(([id,s])=>[named(id),{ageMs:Date.now()-s.at,sequence:s.sequence,sourceId:s.sourceId}])),
    bowlingSamples:Object.fromEntries([...bowlingMotion.received].map(([id,s])=>[named(id),{ageMs:Date.now()-s.at,sequence:s.sequence,sourceId:s.sourceId}]))}));
});
const sockets=new WebSocketServer({noServer:true,maxPayload:8192});
// Game-state channel for the Quest client. Unity on the Mac is the only host; headsets only render.
// Sensor channels stay loopback-only; a LAN client needs KINESTHETIC_PAIR_TOKEN.
const stateSockets=new WebSocketServer({noServer:true,maxPayload:256*1024});
const pairToken=process.env.KINESTHETIC_PAIR_TOKEN??'';
// A client that joins mid-activity is caught up from the host's last frame — but only a fresh one. Every host
// publishes at 30 Hz for as long as its scene is up, so a frame older than this is a host that stopped without
// closing (a scene torn down, a Mac asleep), and a headset booting into the plaza must not be sent into its game.
const stateFreshMs=2000;
stateSockets.on('connection',(ws,role,path)=>{
  const channel=channels.get(path)!;
  if(role==='host'){
    if(channel.host){ws.close(1008,'A game host is already connected');return;}
    channel.host=ws;
    ws.on('message',bytes=>{
      const text=bytes.toString();
      try{if(JSON.parse(text).type!==channel.type+'.state')throw Error();}catch{ws.close(1008,'Invalid game state');return;}
      channel.last=text;channel.lastAt=Date.now();for(const c of channel.clients)if(c.readyState===WebSocket.OPEN&&c.bufferedAmount<512*1024)c.send(text);
    });
    ws.on('close',()=>{if(channel.host===ws){channel.host=null;channel.last=null;for(const c of channel.clients)if(c.readyState===WebSocket.OPEN)c.send(JSON.stringify({type:channel.type+'.host-disconnected'}));}});
  } else {
    channel.clients.add(ws);if(channel.last&&Date.now()-channel.lastAt<stateFreshMs)ws.send(channel.last);
    ws.on('close',()=>channel.clients.delete(ws));
  }
  ws.on('error',()=>ws.close());
});
// World-space UI mirror. Unity on the Mac (the one host) broadcasts board trees; headsets send presses back.
// The relay checks shapes, stamps the client id and routes; it never reads a tree. Same auth as game state,
// but its own servers, one per role: a snapshot may be tens of KB (1 MiB cap) while a client may say at most 1 KiB,
// and ws enforces each cap itself (1009) before a byte is buffered.
const uiHostSockets=new WebSocketServer({noServer:true,maxPayload:1024*1024}),uiClientSockets=new WebSocketServer({noServer:true,maxPayload:1024});
const ui={host:null as WebSocket|null,clients:new Map<WebSocket,string>(),issued:0};
const toUiHost=(p:unknown)=>{if(ui.host?.readyState===WebSocket.OPEN)ui.host.send(JSON.stringify(p));};
const uiName=/^[\w.-]{1,80}$/,uiPath=/^(\d{1,4}(\/\d{1,4}){0,63})?$/;
uiHostSockets.on('connection',ws=>{
  ws.on('error',()=>ws.close());
  if(ui.host?.readyState===WebSocket.OPEN){ws.close(1008,'A UI host is already connected');return;}
  // A host still closing, or gone without a handshake, gives way: Unity restarting must not wait 30 s for it.
  const old=ui.host;ui.host=ws;old?.terminate();
  for(const client of ui.clients.values())ws.send(JSON.stringify({type:'ui.resync',client}));
  ws.on('message',bytes=>{
    const text=bytes.toString();let p:any;
    // A cue is the host telling every headset that something happened outside any board tree — the Mac is
    // leaving a scene for a venue, or the menu ring turned — so it rides the same broadcast as a patch. Only
    // its kind is checked; the rest is the host's, and the host is loopback-only.
    try{p=JSON.parse(text);if(!['ui.snapshot','ui.patch','ui.ack','ui.cue'].includes(p.type))throw Error();
      if(p.type==='ui.cue'&&!(typeof p.kind==='string'&&uiName.test(p.kind)))throw Error();}catch{ws.close(1008,'Invalid UI message');return;}
    if(p.type==='ui.ack'){for(const [c,client] of ui.clients)if(client===p.client&&c.readyState===WebSocket.OPEN)c.send(text);return;}
    // A client too far behind to take a tree would stay stale for good; closing it makes it reconnect and resync.
    for(const c of ui.clients.keys())if(c.readyState===WebSocket.OPEN){if(c.bufferedAmount<512*1024)c.send(text);else c.close(1013,'UI client fell behind');}
  });
  ws.on('close',()=>{if(ui.host===ws){ui.host=null;for(const c of ui.clients.keys())if(c.readyState===WebSocket.OPEN)c.send(JSON.stringify({type:'ui.host-disconnected'}));}});
});
uiClientSockets.on('connection',ws=>{
  ws.on('error',()=>ws.close());
  const client=`c${++ui.issued}`;ui.clients.set(ws,client);
  ws.send(JSON.stringify({type:'ui.welcome',client}));toUiHost({type:'ui.resync',client});
  // 20 messages/s (a bucket of 20, refilled one per 50 ms). Everything over that, and everything malformed, is
  // dropped and counted; the count forgives one drop a second, so only a client that keeps it up is disconnected.
  let tokens=20,drops=0,at=Date.now();
  ws.on('message',bytes=>{
    const now=Date.now();tokens=Math.min(20,tokens+(now-at)/50);drops=Math.max(0,drops-(now-at)/1000);at=now;
    const drop=()=>{if(++drops>=200)ws.close(1008,'Too many dropped UI messages');};
    if(tokens<1)return drop();
    tokens--;let p:any;try{p=JSON.parse(bytes.toString());}catch{return drop();}
    if(p?.type==='ui.resync')return toUiHost({type:'ui.resync',client});
    if(p?.type!=='ui.press' || !Number.isSafeInteger(p.seq) || p.seq<0 || typeof p.board!=='string' || !uiName.test(p.board) ||
      typeof p.name!=='string' || !uiName.test(p.name) || typeof p.path!=='string' || !uiPath.test(p.path))return drop();
    toUiHost({type:'ui.press',client,seq:p.seq,board:p.board,path:p.path,name:p.name});
  });
  ws.on('close',()=>ui.clients.delete(ws));
});
server.on('upgrade',(req,socket,head)=>{
  const u=new URL(req.url??'/','http://localhost');
  const role=u.searchParams.get('role'),player=u.searchParams.get('player');
  if(channels.has(u.pathname) || u.pathname==='/ui'){
    const ok=!req.headers.origin && (role==='host'?loopback(req.socket.remoteAddress):
      role==='client' && (loopback(req.socket.remoteAddress) || (pairToken!=='' && u.searchParams.get('token')===pairToken)));
    if(!ok){socket.destroy();return;}
    const wss=u.pathname!=='/ui'?stateSockets:role==='host'?uiHostSockets:uiClientSockets;
    wss.handleUpgrade(req,socket,head,ws=>wss.emit('connection',ws,role,u.pathname));return;
  }
  if(u.pathname==='/head'){
    const local=loopback(req.socket.remoteAddress);
    const ok=role==='producer' ? !req.headers.origin && (local || paired(req))
      : role==='viewer' && local && (!req.headers.origin || viewerOrigins.has(req.headers.origin));
    if(!ok){socket.destroy();return;}
    headSockets.handleUpgrade(req,socket,head,ws=>headSockets.emit('connection',ws,role));return;
  }
  if(u.pathname==='/motion'){
    const ok=role==='viewer' && loopback(req.socket.remoteAddress) && (!req.headers.origin || viewerOrigins.has(req.headers.origin));
    if(!ok){socket.destroy();return;}
    sockets.handleUpgrade(req,socket,head,ws=>{rawViewers.add(ws);ws.on('close',()=>rawViewers.delete(ws));ws.on('error',()=>ws.close());});return;
  }
  if(!loopback(req.socket.remoteAddress) && !(role==='producer' && paired(req))){socket.destroy();return;}
  // Browsers always send Origin. Only the local capture page may watch motion, read-only, from the Mac itself.
  const browserViewer=role==='viewer' && loopback(req.socket.remoteAddress) && viewerOrigins.has(req.headers.origin??'');
  if(!motions.has(u.pathname) || !['producer','viewer'].includes(role??'') ||
    (role==='producer' && !['patient','friend'].includes(player??'')) || (req.headers.origin && !browserViewer)){socket.destroy();return;}
  const remote=role==='producer'&&paired(req);
  sockets.handleUpgrade(req,socket,head,ws=>sockets.emit('connection',ws,role,player,u.pathname,remote));
});
// The headset's head pose: position and orientation relative to the patient's seated eye point, in the seat's own
// frame (x right, y up, z forward), sent by the headset about 30 times a second. The Mac leans and turns the torso
// with it and measures trunk lean from it — the headset is the third sensor, beside the two AirPods. One headset
// sends (from this Mac over the USB cable, or from the network with the pairing token); only this Mac reads.
const headDir=resolve(process.env.KINESTHETIC_HEAD_RECORDINGS ?? resolve(import.meta.dirname,'../local-data/head'));
mkdirSync(headDir,{recursive:true});
const headSockets=new WebSocketServer({noServer:true,maxPayload:1024});
const headViewers=new Set<WebSocket>();
let headProducer:WebSocket|null=null,headLast:{at:number,seq:number}|null=null;
headSockets.on('connection',(ws,role)=>{
  if(role==='viewer'){headViewers.add(ws);ws.on('close',()=>headViewers.delete(ws));ws.on('error',()=>ws.close());return;}
  if(headProducer){ws.close(1008,'A headset is already sending its head pose');return;}
  headProducer=ws;let seq=-1;
  const log=createWriteStream(resolve(headDir,`head-${Date.now()}.jsonl`));
  log.on('error',e=>console.error('head recording:',e.message));
  ws.on('message',bytes=>{
    try{
      const p=JSON.parse(bytes.toString());
      if(p.type!=='head.pose' || !Number.isSafeInteger(p.seq) || !finiteArray(p.p,3) || !finiteArray(p.q,4) ||
        p.p.some((v:number)=>Math.abs(v)>5))throw Error();
      const norm=p.q.reduce((a:number,v:number)=>a+v*v,0);
      if(norm<.5 || norm>1.5)throw Error();
      if(p.seq<=seq)return;
      seq=p.seq;headLast={at:Date.now(),seq};
      const pose={type:'head.pose',seq,p:p.p,q:p.q,hostMonotonicMs:hostMonotonicMs()};
      broadcast(headViewers,pose);log.write(JSON.stringify({...pose,receivedAt:Date.now()})+'\n');
    }catch{ws.close(1008,'Invalid head pose');}
  });
  ws.on('close',()=>{if(headProducer===ws){headProducer=null;headLast=null;broadcast(headViewers,{type:'head.disconnected'});}log.end();});
  ws.on('error',()=>ws.close());
});

// The two pairs. A slot is held by one producer socket for as long as it is open; this Mac's app prefers Mac 1 and
// the paired one Mac 2, and either takes the other slot when its own is taken (a simulator, a test, one Mac running
// both apps). A third pair is refused: there are two.
const macSlots=new Map<Mac,{ws:WebSocket,remote:boolean,path:string,last:{at:number,sequence:number,sourceId:string}|null}>();
const rawViewers=new Set<WebSocket>();
const fuse=new DominantMotion();
let fusedSession=`fused-${Date.now()}`;
function claimMac(ws:WebSocket,remote:boolean,path:string):Mac|null{
  for(const [mac,slot] of macSlots)if(slot.ws===ws)return mac;
  const order:Mac[]=remote?['mac2','mac1']:['mac1','mac2'];
  const free=order.find(mac=>!macSlots.has(mac));
  if(!free)return null;
  macSlots.set(free,{ws,remote,path,last:null});return free;
}
function releaseMac(ws:WebSocket){
  for(const [mac,slot] of macSlots)if(slot.ws===ws){
    macSlots.delete(mac);fuse.drop(mac);broadcast(rawViewers,{type:'motion.disconnected',mac});
    // The games keep going on the other pair; only when neither is left has their stream ended.
    if(macSlots.size===0){
      for(const ch of motions.values())broadcast(ch.viewers,{type:ch.type+'.disconnected',playerId:'patient'});
      fusedSession=`fused-${Date.now()}`;
    }
  }
}
function broadcast(viewers:Set<WebSocket>,p:unknown){const text=JSON.stringify(p);for(const ws of viewers)if(ws.readyState===WebSocket.OPEN && ws.bufferedAmount<16384)ws.send(text);}
const finiteArray=(x:unknown,n:number):x is number[]=>Array.isArray(x)&&x.length===n&&x.every(Number.isFinite);
sockets.on('connection',(ws,role,player,path,remote)=>{
  const {viewers,producers,received,type,dir}=motions.get(path)!;
  let routedTo:{path:string,player:string}|null=null;   // where this producer's samples went last, to say goodbye there
  if(role==='viewer'){viewers.add(ws);ws.on('close',()=>viewers.delete(ws));ws.on('error',()=>ws.close());return;}
  const key=remote?player+remoteKey:player;
  if(producers.has(key)){ws.close(1008,'This player already has a motion source');return;}
  producers.set(key,ws);if(remote)remoteProducers.add(ws);
  let session='',sequence=-1,time=-1;
  const log=createWriteStream(resolve(dir,`${player}-${Date.now()}.jsonl`));
  log.on('error',e=>console.error(type+' motion recording:',e.message));
  ws.on('message',bytes=>{
    try{
      const p=JSON.parse(bytes.toString());
      if(p.type!==type+'.motion' || p.playerId!==player || !/^[\w-]{1,80}$/.test(p.sessionId) ||
        !['Left','Right'].includes(p.sourceId) || !Number.isSafeInteger(p.sequence) || p.sequence<0 ||
        !Number.isFinite(p.sensorTime) || !finiteArray(p.quaternion,4) || !finiteArray(p.rotationRate,3))throw Error();
      if(session!==p.sessionId){session=p.sessionId;sequence=-1;time=-1;}
      if(p.sequence<=sequence || p.sensorTime<=time)return;
      const norm=p.quaternion.reduce((s:number,v:number)=>s+v*v,0);
      if(norm<.5 || norm>1.5 || p.rotationRate.some((v:number)=>Math.abs(v)>100))throw Error();
      sequence=p.sequence;time=p.sensorTime;received.set(key,{at:Date.now(),sequence,sourceId:p.sourceId});
      // Routed per sample, not per connection: the second Mac keeps streaming across a walk from golf to the studio.
      const to=route(path,player,remote),target=motions.get(to.path)!;
      // A reading that changed (only KINESTHETIC_REMOTE_MOTION=people does that) says goodbye where it was.
      if(routedTo&&(routedTo.path!==to.path||routedTo.player!==to.player)){
        if(routedTo.player==='patient')releaseMac(ws);
        else broadcast(motions.get(routedTo.path)!.viewers,{type:motions.get(routedTo.path)!.type+'.disconnected',playerId:routedTo.player});
      }
      routedTo=to;
      if(to.player==='patient'){
        const mac=claimMac(ws,remote,path);
        if(!mac){ws.close(1008,'Two AirPod pairs are already streaming');return;}
        const t=hostMonotonicMs(),slot=macSlots.get(mac)!;slot.last={at:Date.now(),sequence,sourceId:p.sourceId};
        const raw={type:'motion.sample',mac,via:type,sourceId:p.sourceId,sessionId:session,sequence,sensorTime:time,
          quaternion:p.quaternion,rotationRate:p.rotationRate,hostMonotonicMs:t};
        broadcast(rawViewers,raw);log.write(JSON.stringify({...raw,receivedAs:{path,player},receivedAt:Date.now()})+'\n');
        const out=fuse.push(mac,{quaternion:p.quaternion,rotationRate:p.rotationRate,sourceId:p.sourceId},Date.now());
        if(out)for(const ch of motions.values())broadcast(ch.viewers,{type:ch.type+'.motion',playerId:'patient',mac,
          sourceId:out.sourceId,sessionId:fusedSession,sequence:out.sequence,sensorTime:t/1000,
          quaternion:out.quaternion,rotationRate:out.rotationRate,hostMonotonicMs:t});
        return;
      }
      const sample={type:target.type+'.motion',playerId:to.player,sourceId:p.sourceId,sessionId:session,
        sequence,sensorTime:time,quaternion:p.quaternion,rotationRate:p.rotationRate,
        hostMonotonicMs:hostMonotonicMs()};
      broadcast(target.viewers,sample);log.write(JSON.stringify({...sample,receivedAs:{path,player},receivedAt:Date.now()})+'\n');
    }catch{ws.close(1008,'Invalid motion');}
  });
  ws.on('close',()=>{if(producers.get(key)===ws){producers.delete(key);received.delete(key);releaseMac(ws);
    const last=routedTo??{path,player};if(last.player!=='patient')broadcast(motions.get(last.path)!.viewers,{type:motions.get(last.path)!.type+'.disconnected',playerId:last.player});}log.end();});
  ws.on('error',()=>ws.close());
});
// LAN game-state channels require a pairing token; sensor streams stay loopback-only.
const bindHost=process.env.KINESTHETIC_GOLF_HOST??'127.0.0.1';
if(bindHost!=='127.0.0.1'&&!pairToken)throw Error('Set KINESTHETIC_PAIR_TOKEN before exposing the relay to the network');
server.listen(port,bindHost,()=>console.log(`Kinesthetic golf relay: ws://127.0.0.1:${port}/golf`));
// On the network, announce this relay once a second so a headset finds the Mac after the network changes (venue
// Wi-Fi to a hotspot) without a rebuild. The proof is an HMAC of the announcing address under the pairing token:
// a headset only follows an announcement it can verify, so another machine cannot lure it (and its token) away.
const beaconPort=Number(process.env.KINESTHETIC_BEACON_PORT ?? 8768);
if(bindHost!=='127.0.0.1'){
  const beacon=createSocket('udp4');
  const announce=()=>{
    for(const list of Object.values(networkInterfaces()))for(const a of list??[]){
      if(a.family!=='IPv4'||a.internal)continue;
      const ip=a.address.split('.').map(Number),mask=a.netmask.split('.').map(Number);
      const directed=ip.map((o,i)=>(o|(~mask[i]&255))).join('.');
      const proof=createHmac('sha256',pairToken).update(a.address).digest('hex');
      beacon.send(JSON.stringify({service:'rehabmii-relay',host:a.address,port,proof}),beaconPort,directed,()=>{});
    }
  };
  beacon.bind(()=>{beacon.setBroadcast(true);announce();setInterval(announce,1000).unref();});
  beacon.unref();
}
// Shutdown must actually terminate. A graceful ws.close() waits for a closing handshake, and a peer
// that vanished without one (a terminated test client, a Quest that dropped off the network) holds its
// handle open, so server.close() never completes and the process hangs instead of exiting.
function shutdown(){
  for(const ws of sockets.clients)ws.terminate();for(const ws of stateSockets.clients)ws.terminate();for(const ws of headSockets.clients)ws.terminate();
  for(const ws of uiHostSockets.clients)ws.terminate();for(const ws of uiClientSockets.clients)ws.terminate();
  sockets.close();stateSockets.close();uiHostSockets.close();uiClientSockets.close();server.close(()=>process.exit(0));
  setTimeout(()=>process.exit(0),500).unref();
}
process.on('SIGINT',shutdown);process.on('SIGTERM',shutdown);
