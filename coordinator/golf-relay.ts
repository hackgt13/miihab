import { createServer } from 'node:http';
import { mkdirSync, createWriteStream } from 'node:fs';
import { resolve } from 'node:path';
import { WebSocket, WebSocketServer } from 'ws';
import { hostMonotonicMs } from './hostclock.ts';

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
const channels=new Map(['/state','/bowling-state','/rehab-state'].map(path=>[path,{
  host:null as WebSocket|null,clients:new Set<WebSocket>(),last:null as string|null,
  type:({'/state':'golf','/bowling-state':'bowling','/rehab-state':'rehab'} as Record<string,string>)[path]
}]));
const golfState=channels.get('/state')!;
// A motion app on another Mac (a second AirPod pair, e.g. on a wrist strap) may send motion here, and read this
// status to confirm it, when it carries the pairing token. Viewing motion stays on this Mac.
const paired=(req:import('node:http').IncomingMessage)=>pairToken!=='' &&
  new URL(req.url??'/','http://relay').searchParams.get('token')===pairToken;
const server=createServer((req,res)=>{
  if(!loopback(req.socket.remoteAddress) && !paired(req)){res.writeHead(403).end();return;}
  res.writeHead(200,{'Content-Type':'application/json'}).end(JSON.stringify({
    ready:true,players:[...golfMotion.producers.keys()],viewers:golfMotion.viewers.size,stateHost:!!golfState.host,stateClients:golfState.clients.size,
    bowlingPlayers:[...bowlingMotion.producers.keys()],bowlingViewers:bowlingMotion.viewers.size,
    bowlingHost:!!channels.get('/bowling-state')!.host,
    samples:Object.fromEntries([...golfMotion.received].map(([id,s])=>[id,{ageMs:Date.now()-s.at,sequence:s.sequence,sourceId:s.sourceId}])),
    bowlingSamples:Object.fromEntries([...bowlingMotion.received].map(([id,s])=>[id,{ageMs:Date.now()-s.at,sequence:s.sequence,sourceId:s.sourceId}]))}));
});
const sockets=new WebSocketServer({noServer:true,maxPayload:8192});
// Game-state channel for the Quest client. Unity on the Mac is the only host; headsets only render.
// Sensor channels stay loopback-only; a LAN client needs KINESTHETIC_PAIR_TOKEN.
const stateSockets=new WebSocketServer({noServer:true,maxPayload:256*1024});
const pairToken=process.env.KINESTHETIC_PAIR_TOKEN??'';
stateSockets.on('connection',(ws,role,path)=>{
  const channel=channels.get(path)!;
  if(role==='host'){
    if(channel.host){ws.close(1008,'A game host is already connected');return;}
    channel.host=ws;
    ws.on('message',bytes=>{
      const text=bytes.toString();
      try{if(JSON.parse(text).type!==channel.type+'.state')throw Error();}catch{ws.close(1008,'Invalid game state');return;}
      channel.last=text;for(const c of channel.clients)if(c.readyState===WebSocket.OPEN&&c.bufferedAmount<512*1024)c.send(text);
    });
    ws.on('close',()=>{if(channel.host===ws){channel.host=null;channel.last=null;for(const c of channel.clients)if(c.readyState===WebSocket.OPEN)c.send(JSON.stringify({type:channel.type+'.host-disconnected'}));}});
  } else {
    channel.clients.add(ws);if(channel.last)ws.send(channel.last);
    ws.on('close',()=>channel.clients.delete(ws));
  }
  ws.on('error',()=>ws.close());
});
server.on('upgrade',(req,socket,head)=>{
  const u=new URL(req.url??'/','http://localhost');
  const role=u.searchParams.get('role'),player=u.searchParams.get('player');
  if(channels.has(u.pathname)){
    const ok=!req.headers.origin && (role==='host'?loopback(req.socket.remoteAddress):
      role==='client' && (loopback(req.socket.remoteAddress) || (pairToken!=='' && u.searchParams.get('token')===pairToken)));
    if(!ok){socket.destroy();return;}
    stateSockets.handleUpgrade(req,socket,head,ws=>stateSockets.emit('connection',ws,role,u.pathname));return;
  }
  if(!loopback(req.socket.remoteAddress) && !(role==='producer' && paired(req))){socket.destroy();return;}
  // Browsers always send Origin. Only the local capture page may watch motion, read-only, from the Mac itself.
  const browserViewer=role==='viewer' && loopback(req.socket.remoteAddress) && viewerOrigins.has(req.headers.origin??'');
  if(!motions.has(u.pathname) || !['producer','viewer'].includes(role??'') ||
    (role==='producer' && !['patient','friend'].includes(player??'')) || (req.headers.origin && !browserViewer)){socket.destroy();return;}
  sockets.handleUpgrade(req,socket,head,ws=>sockets.emit('connection',ws,role,player,u.pathname));
});
function broadcast(viewers:Set<WebSocket>,p:unknown){const text=JSON.stringify(p);for(const ws of viewers)if(ws.readyState===WebSocket.OPEN && ws.bufferedAmount<16384)ws.send(text);}
const finiteArray=(x:unknown,n:number):x is number[]=>Array.isArray(x)&&x.length===n&&x.every(Number.isFinite);
sockets.on('connection',(ws,role,player,path)=>{
  const {viewers,producers,received,type,dir}=motions.get(path)!;
  if(role==='viewer'){viewers.add(ws);ws.on('close',()=>viewers.delete(ws));ws.on('error',()=>ws.close());return;}
  if(producers.has(player)){ws.close(1008,'This player already has a motion source');return;}
  producers.set(player,ws);
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
      sequence=p.sequence;time=p.sensorTime;received.set(player,{at:Date.now(),sequence,sourceId:p.sourceId});
      const sample={type:type+'.motion',playerId:player,sourceId:p.sourceId,sessionId:session,
        sequence,sensorTime:time,quaternion:p.quaternion,rotationRate:p.rotationRate,
        hostMonotonicMs:hostMonotonicMs()};
      broadcast(viewers,sample);log.write(JSON.stringify({...sample,receivedAt:Date.now()})+'\n');
    }catch{ws.close(1008,'Invalid motion');}
  });
  ws.on('close',()=>{if(producers.get(player)===ws){producers.delete(player);received.delete(player);broadcast(viewers,{type:type+'.disconnected',playerId:player});}log.end();});
  ws.on('error',()=>ws.close());
});
// LAN game-state channels require a pairing token; sensor streams stay loopback-only.
const bindHost=process.env.KINESTHETIC_GOLF_HOST??'127.0.0.1';
if(bindHost!=='127.0.0.1'&&!pairToken)throw Error('Set KINESTHETIC_PAIR_TOKEN before exposing the relay to the network');
server.listen(port,bindHost,()=>console.log(`Kinesthetic golf relay: ws://127.0.0.1:${port}/golf`));
// Shutdown must actually terminate. A graceful ws.close() waits for a closing handshake, and a peer
// that vanished without one (a terminated test client, a Quest that dropped off the network) holds its
// handle open, so server.close() never completes and the process hangs instead of exiting.
function shutdown(){
  for(const ws of sockets.clients)ws.terminate();for(const ws of stateSockets.clients)ws.terminate();
  sockets.close();stateSockets.close();server.close(()=>process.exit(0));
  setTimeout(()=>process.exit(0),500).unref();
}
process.on('SIGINT',shutdown);process.on('SIGTERM',shutdown);
