import { createServer } from 'node:http';
import { mkdirSync, createWriteStream } from 'node:fs';
import { resolve } from 'node:path';
import { WebSocket, WebSocketServer } from 'ws';

// Separate from the already-running pose proof. Native hosts connect here.
const port=Number(process.env.KINESTHETIC_GOLF_PORT ?? 8767);
const received=new Map<string,{at:number,sequence:number,sourceId:string}>();
const loopback=(address?:string)=>['127.0.0.1','::1','::ffff:127.0.0.1'].includes(address??'');
let stateHost:WebSocket|null=null;
const stateClients=new Set<WebSocket>();
const server=createServer((req,res)=>{
  if(!loopback(req.socket.remoteAddress)){res.writeHead(403).end();return;}
  res.writeHead(200,{'Content-Type':'application/json'}).end(JSON.stringify({
    ready:true,players:[...producers.keys()],viewers:viewers.size,stateHost:!!stateHost,stateClients:stateClients.size, samples:Object.fromEntries([...received].map(([id,s])=>[id,{ageMs:Date.now()-s.at,sequence:s.sequence,sourceId:s.sourceId}]))}));
});
const sockets=new WebSocketServer({noServer:true,maxPayload:8192});
// Game-state channel for the Quest client. Unity on the Mac is the only host; headsets only render.
// Sensor channels stay loopback-only; a LAN client needs KINESTHETIC_PAIR_TOKEN.
const stateSockets=new WebSocketServer({noServer:true,maxPayload:256*1024});
const pairToken=process.env.KINESTHETIC_PAIR_TOKEN??'';
let lastState:string|null=null;
stateSockets.on('connection',(ws,role)=>{
  if(role==='host'){
    if(stateHost){ws.close(1008,'A game host is already connected');return;}
    stateHost=ws;
    ws.on('message',bytes=>{
      const text=bytes.toString();
      try{if(JSON.parse(text).type!=='golf.state')throw Error();}catch{ws.close(1008,'Invalid game state');return;}
      lastState=text;for(const c of stateClients)if(c.readyState===WebSocket.OPEN&&c.bufferedAmount<512*1024)c.send(text);
    });
    ws.on('close',()=>{if(stateHost===ws){stateHost=null;lastState=null;for(const c of stateClients)c.send(JSON.stringify({type:'golf.host-disconnected'}));}});
  } else {
    stateClients.add(ws);if(lastState)ws.send(lastState);
    ws.on('close',()=>stateClients.delete(ws));
  }
  ws.on('error',()=>ws.close());
});
const viewers=new Set<WebSocket>();
const producers=new Map<string,WebSocket>();
server.on('upgrade',(req,socket,head)=>{
  const u=new URL(req.url??'/','http://localhost');
  const role=u.searchParams.get('role'),player=u.searchParams.get('player');
  if(u.pathname==='/state'){
    const ok=!req.headers.origin && (role==='host'?loopback(req.socket.remoteAddress):
      role==='client' && (loopback(req.socket.remoteAddress) || (pairToken!=='' && u.searchParams.get('token')===pairToken)));
    if(!ok){socket.destroy();return;}
    stateSockets.handleUpgrade(req,socket,head,ws=>stateSockets.emit('connection',ws,role));return;
  }
  if(!loopback(req.socket.remoteAddress)){socket.destroy();return;}
  if(u.pathname!='/golf' || !['producer','viewer'].includes(role??'') ||
    (role==='producer' && !['patient','friend'].includes(player??'')) || req.headers.origin){socket.destroy();return;}
  sockets.handleUpgrade(req,socket,head,ws=>sockets.emit('connection',ws,role,player));
});
function broadcast(p:unknown){const text=JSON.stringify(p);for(const ws of viewers)if(ws.readyState===WebSocket.OPEN && ws.bufferedAmount<16384)ws.send(text);}
const finiteArray=(x:unknown,n:number):x is number[]=>Array.isArray(x)&&x.length===n&&x.every(Number.isFinite);
const dir=resolve(process.env.KINESTHETIC_GOLF_RECORDINGS ?? resolve(import.meta.dirname,'../local-data/golf'));mkdirSync(dir,{recursive:true});
sockets.on('connection',(ws,role,player)=>{
  if(role==='viewer'){viewers.add(ws);ws.on('close',()=>viewers.delete(ws));ws.on('error',()=>ws.close());return;}
  if(producers.has(player)){ws.close(1008,'This player already has a motion source');return;}
  producers.set(player,ws);
  let session='',sequence=-1,time=-1;
  const log=createWriteStream(resolve(dir,`${player}-${Date.now()}.jsonl`));
  log.on('error',e=>console.error('Golf recording:',e.message));
  ws.on('message',bytes=>{
    try{
      const p=JSON.parse(bytes.toString());
      if(p.type!=='club.motion' || p.playerId!==player || !/^[\w-]{1,80}$/.test(p.sessionId) ||
        !['Left','Right'].includes(p.sourceId) || !Number.isSafeInteger(p.sequence) || p.sequence<0 ||
        !Number.isFinite(p.sensorTime) || !finiteArray(p.quaternion,4) || !finiteArray(p.rotationRate,3))throw Error();
      if(session!==p.sessionId){session=p.sessionId;sequence=-1;time=-1;}
      if(p.sequence<=sequence || p.sensorTime<=time)return;
      const norm=p.quaternion.reduce((s:number,v:number)=>s+v*v,0);
      if(norm<.5 || norm>1.5 || p.rotationRate.some((v:number)=>Math.abs(v)>100))throw Error();
      sequence=p.sequence;time=p.sensorTime;received.set(player,{at:Date.now(),sequence,sourceId:p.sourceId});
      const sample={type:'club.motion',playerId:player,sourceId:p.sourceId,sessionId:session,
        sequence,sensorTime:time,quaternion:p.quaternion,rotationRate:p.rotationRate};
      broadcast(sample);log.write(JSON.stringify({...sample,receivedAt:Date.now()})+'\n');
    }catch{ws.close(1008,'Invalid club motion');}
  });
  ws.on('close',()=>{if(producers.get(player)===ws){producers.delete(player);received.delete(player);broadcast({type:'club.disconnected',playerId:player});}log.end();});
  ws.on('error',()=>ws.close());
});
// KINESTHETIC_GOLF_HOST=0.0.0.0 exposes only the /state channel to the LAN (token required); sensors stay loopback.
const bindHost=process.env.KINESTHETIC_GOLF_HOST??'127.0.0.1';
if(bindHost!=='127.0.0.1'&&!pairToken)throw Error('Set KINESTHETIC_PAIR_TOKEN before exposing the relay to the network');
server.listen(port,bindHost,()=>console.log(`Kinesthetic golf relay: ws://127.0.0.1:${port}/golf`));
function shutdown(){for(const ws of sockets.clients)ws.close();for(const ws of stateSockets.clients)ws.close();sockets.close();server.close();}
process.on('SIGINT',shutdown);process.on('SIGTERM',shutdown);
