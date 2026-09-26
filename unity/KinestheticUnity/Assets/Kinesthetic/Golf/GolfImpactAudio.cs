using System;
using System.Diagnostics;
using UnityEngine;

namespace Kinesthetic.Golf
{
    // Local amplitude/onset analysis only: no playback, audio files, or network upload.
    public sealed class GolfImpactAudio : MonoBehaviour
    {
        public event Action<double,float> Transient;
        public bool Ready=>clip && Microphone.IsRecording(null) && Now-started>.5 && Now-lastSampleAt<.25;
        public string Status {get;private set;}="Sound assist waiting for camera";
        public static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        AudioClip clip;
        AudioTransientDetector detector;
        float[] block;
        int cursor,blockFrames;
        double started,lastPoll;
        double lastSampleAt=double.NegativeInfinity;
        bool attempted;
        public void Listen(bool wanted)
        {
            if(!wanted){StopCapture();return;}
            if(attempted)return;
            attempted=true;
            try
            {
                if(Microphone.devices.Length==0){Status="No microphone · motion still works";return;}
                clip=Microphone.Start(null,true,2,48000);
                if(!clip){Status="Microphone unavailable · motion still works";return;}
                blockFrames=Math.Max(1,clip.frequency/100);block=new float[blockFrames*clip.channels];
                detector=new AudioTransientDetector();cursor=0;started=lastPoll=Now;lastSampleAt=double.NegativeInfinity;
                Status="Microphone starting…";
            }
            catch(Exception){Status="Microphone unavailable · motion still works";}
        }
        public void Poll()
        {
            if(!clip)return;
            double now=Now;int end=Microphone.GetPosition(null);
            if(end<0 || !Microphone.IsRecording(null)){Status="Microphone unavailable · motion still works";return;}
            if(now-started>2 && end==0){Status="Microphone unavailable · motion still works";return;}
            // Drop backlog after an editor stall; old sounds must not confirm a new swing.
            if(now-lastPoll>.25){cursor=end;detector=new AudioTransientDetector();}
            lastPoll=now;
            int available=(end-cursor+clip.samples)%clip.samples;
            while(available>=blockFrames)
            {
                if(!clip.GetData(block,cursor))break;
                lastSampleAt=now;
                float peak=0,sum=0;
                foreach(float sample in block){peak=Math.Max(peak,Math.Abs(sample));sum+=sample*sample;}
                // Estimate the captured block's end time from its distance behind the write head.
                // Device buffering and camera/network delay remain uncalibrated.
                double at=now-(available-blockFrames)/(double)clip.frequency;
                if(detector.Sample(peak,Mathf.Sqrt(sum/block.Length),at))Transient?.Invoke(at,peak);
                cursor=(cursor+blockFrames)%clip.samples;available-=blockFrames;
            }
            Status=Ready?"Sound assist listening · local only":"Sound assist warming up…";
        }
        public void StopCapture()
        {
            if(clip){Microphone.End(null);Destroy(clip);clip=null;}
            attempted=false;Status="Sound assist off";
        }
        void OnDisable()=>StopCapture();
    }
}
