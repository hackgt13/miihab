"""Run the shipped MediaPipe model on every decoded video frame, locally."""
import argparse, json, time, uuid
from pathlib import Path
import cv2
import mediapipe as mp

p = argparse.ArgumentParser()
p.add_argument('video'); p.add_argument('output')
p.add_argument('--model', default='spikes/pose-capture/models/pose_landmarker_lite.task')
args = p.parse_args()
cap = cv2.VideoCapture(args.video)
if not cap.isOpened(): raise RuntimeError('Cannot decode video')
fps = cap.get(cv2.CAP_PROP_FPS)
w, h = int(cap.get(3)), int(cap.get(4))
out = Path(args.output); out.parent.mkdir(parents=True, exist_ok=True)
writer = cv2.VideoWriter(str(out.with_suffix('.overlay.mp4')), cv2.VideoWriter_fourcc(*'mp4v'), fps, (w,h))
edges = [(11,12),(11,13),(13,15),(12,14),(14,16),(11,23),(12,24),(23,24),(23,25),(25,27),(24,26),(26,28),(27,31),(28,32)]
frames=[]
def points(ps):
    return [dict(index=i,x=q.x,y=q.y,z=q.z,visibility=q.visibility,presence=q.presence) for i,q in enumerate(ps)]
opts = mp.tasks.vision.PoseLandmarkerOptions(base_options=mp.tasks.BaseOptions(model_asset_path=args.model),running_mode=mp.tasks.vision.RunningMode.VIDEO,num_poses=1)
with mp.tasks.vision.PoseLandmarker.create_from_options(opts) as model:
    while True:
        ok, bgr = cap.read()
        if not ok: break
        idx=len(frames); ms=round(idx*1000/fps); start=time.perf_counter()*1000
        result=model.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB,data=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB)),ms)
        duration=time.perf_counter()*1000-start
        img=points(result.pose_landmarks[0]) if result.pose_landmarks else []
        world=points(result.pose_world_landmarks[0]) if result.pose_world_landmarks else []
        frames.append(dict(frameID=idx,source='recorded-video',sourceMediaTimeMs=ms,observedAtMonotonicMs=start,inferenceDurationMs=duration,subjectDetected=bool(img),imageLandmarks=img,worldLandmarks=world))
        def xy(i): return (round(img[i]['x']*w),round(img[i]['y']*h))
        if img:
            for a,b in edges:
                color=(110,230,110) if min(img[a]['visibility'],img[b]['visibility'])>=.5 else (0,170,255)
                cv2.line(bgr,xy(a),xy(b),color,3)
            for i in range(11,33):
                cv2.circle(bgr,xy(i),5,(255,255,255),-1)
                cv2.putText(bgr,str(i),xy(i),cv2.FONT_HERSHEY_SIMPLEX,.45,(0,0,0),2)
        cv2.putText(bgr,f'{ms/1000:.2f}s | green: visible | orange: uncertain',(25,40),cv2.FONT_HERSHEY_SIMPLEX,.8,(255,255,255),2)
        writer.write(bgr)
cap.release();writer.release()
data=dict(schemaVersion='kinesthetic.pose-capture.v1',sessionID=str(uuid.uuid4()),model=Path(args.model).name,delegate='CPU',runtime='MediaPipe Python '+mp.__version__,source=dict(kind='recorded-video',name=Path(args.video).name),width=w,height=h,frameCount=len(frames),frames=frames)
out.write_text(json.dumps(data))
print(json.dumps(dict(output=str(out),frames=len(frames),detected=sum(f['subjectDetected'] for f in frames),fps=fps)))
