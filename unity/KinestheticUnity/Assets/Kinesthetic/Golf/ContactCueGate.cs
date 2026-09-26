using System;

namespace Kinesthetic.Golf
{
    // Runtime arms this only after motion AND virtual strike-zone qualification.
    // Times are seconds on Unity's host monotonic clock, not shared hardware timestamps.
    public sealed class ContactCueGate
    {
        public const double Window=.12, MaximumWait=.08;
        bool pending;
        public bool Pending=>pending;
        double motionAt,deadline,audioAt=double.NegativeInfinity;
        float speed;
        public void Reset(){pending=false;audioAt=double.NegativeInfinity;}
        public void ObserveAudio(double time)
        {if(!double.IsNaN(time) && !double.IsInfinity(time) && time>audioAt)audioAt=time;}
        public void Arm(double time,float angularSpeed,bool listen)
        {
            if(pending || double.IsNaN(time) || double.IsInfinity(time) || float.IsNaN(angularSpeed) || float.IsInfinity(angularSpeed))return;
            motionAt=time;speed=angularSpeed;pending=true;deadline=time+(listen?MaximumWait:0);
            if(!listen)audioAt=double.NegativeInfinity;
        }
        public bool Commit(double now,out float angularSpeed,out bool corroborated,out double offsetMs)
        {
            angularSpeed=0;corroborated=false;offsetMs=0;
            if(!pending)return false;
            bool match=audioAt<=now && Math.Abs(audioAt-motionAt)<=Window;
            if(!match && now<deadline)return false;
            pending=false;angularSpeed=speed;corroborated=match;
            if(match)offsetMs=(audioAt-motionAt)*1000;
            return true;
        }
    }
    public sealed class AudioTransientDetector
    {
        double started=-1,last=-1;
        float floor=.003f,previous=.001f;
        public bool Sample(float peak,float rms,double time)
        {
            if(started<0)started=time;
            bool hit=time-started>.5 && time-last>.18 && peak>.035f && rms>Math.Max(.008f,Math.Max(floor*3,previous*1.8f));
            floor=.995f*floor+.005f*Math.Min(rms,floor*2);previous=rms;
            if(hit)last=time;
            return hit;
        }
    }
}
