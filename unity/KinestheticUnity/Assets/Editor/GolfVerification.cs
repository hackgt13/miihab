using System;
using Kinesthetic.Golf;
using UnityEngine;

public static class GolfVerification
{
    public static string StrikeZoneChecks()
    {
        VirtualClubStrike Setup()
        {var z=new VirtualClubStrike();z.Calibrate(Vector3.zero,Quaternion.identity,Quaternion.identity,Vector3.down,.12f);return z;}
        void Point(VirtualClubStrike z,Vector3 head,double t)=>z.Sample(head+Vector3.up,Quaternion.identity,t);
        var hit=Setup();Point(hit,Vector3.zero,0);Point(hit,Vector3.right*.3f,.02);Point(hit,Vector3.right*.2f,.04);Point(hit,Vector3.left*.2f,.06);
        if(!hit.CrossedNear(.06))throw new Exception("Swept clubhead crossing missed the virtual ball.");
        var high=Setup();Point(high,Vector3.zero,0);Point(high,new Vector3(.2f,.3f,0),.02);Point(high,new Vector3(.2f,.3f,0),.04);Point(high,new Vector3(-.2f,.3f,0),.06);
        if(high.CrossedNear(.06))throw new Exception("High miss counted as a hit.");
        var wide=Setup();Point(wide,Vector3.zero,0);Point(wide,new Vector3(.2f,0,.3f),.02);Point(wide,new Vector3(.2f,0,.3f),.04);Point(wide,new Vector3(-.2f,0,.3f),.06);
        if(wide.CrossedNear(.06))throw new Exception("Side miss counted as a hit.");
        var idle=Setup();Point(idle,Vector3.zero,0);Point(idle,Vector3.zero,.02);
        if(idle.CrossedNear(.02))throw new Exception("Resting at address counted as impact.");
        var gap=Setup();Point(gap,Vector3.zero,0);Point(gap,Vector3.right*.3f,.02);Point(gap,Vector3.left*.2f,.5);
        if(gap.CrossedNear(.5))throw new Exception("Stale segment created a hit.");
        var jump=Setup();Point(jump,Vector3.zero,0);Point(jump,Vector3.right*.3f,.02);Point(jump,Vector3.left*2,.04);
        if(jump.CrossedNear(.04))throw new Exception("Tracking jump created a hit.");
        hit.BreakTrace();if(hit.CrossedNear(.06))throw new Exception("Tracking loss retained a hit.");
        var frozen=Setup();var displaced=frozen.Head(Vector3.up+Vector3.right*.3f,Quaternion.identity);
        if(Vector3.Distance(displaced,Vector3.right*.3f)>.0001f)throw new Exception("Club was steered back to the ball.");
        return "PASS: swept crossing; high/side misses; stationary address ignored; stale/jump rejection; tracking-loss reset; frozen club geometry.";
    }
    public static string SoundChecks()
    {
        var gate=new ContactCueGate();
        gate.ObserveAudio(1);
        if(gate.Commit(1,out _,out _,out _))throw new Exception("Noise alone launched.");
        gate.Reset();gate.Arm(2,3,true);
        if(gate.Commit(2.02,out _,out _,out _))throw new Exception("Did not wait for optional sound.");
        gate.ObserveAudio(2.04);
        if(!gate.Commit(2.05,out var speed,out var heard,out var offset) || !heard || Math.Abs(offset-40)>.001 || speed!=3)
            throw new Exception("Following sound did not corroborate.");
        if(gate.Commit(2.2,out _,out _,out _))throw new Exception("Duplicate shot.");
        gate.Reset();gate.ObserveAudio(2.95);gate.Arm(3,2,true);
        if(!gate.Commit(3,out _,out heard,out _) || !heard)throw new Exception("Preceding sound was lost.");
        gate.Reset();gate.ObserveAudio(3);gate.Arm(4,2,true);
        if(!gate.Commit(4.09,out _,out heard,out _) || heard)throw new Exception("Stale sound corroborated or quiet swing blocked.");
        gate.Reset();gate.Arm(5,2,false);
        if(!gate.Commit(5,out _,out heard,out _) || heard)throw new Exception("No microphone fallback failed.");
        gate.Reset();gate.Arm(6,2,true);gate.Reset();gate.ObserveAudio(6.02);
        if(gate.Commit(6.1,out _,out _,out _))throw new Exception("Canceled swing launched.");
        var detector=new AudioTransientDetector();int pulses=0;
        for(int i=0;i<150;i++)
            if(detector.Sample(i==100||i==105?.15f:.002f,i==100||i==105?.05f:.001f,i*.01))pulses++;
        if(pulses!=1)throw new Exception("Audio warmup/echo cooldown failed.");
        return "PASS: noise alone rejected; preceding/following onset matched; single commit; stale audio rejected; quiet/no-mic fallback; canceled swing rejected; onset cooldown.";
    }

    public static string RecordedAudioChecks(string path)
    {
        // The fixtures are mono PCM16 WAV files extracted locally with ffmpeg.
        // Use a direct RIFF reader here so this check does not depend on an async player loop.
        using var r=new System.IO.BinaryReader(System.IO.File.OpenRead(path));
        if(new string(r.ReadChars(4))!="RIFF")throw new Exception("Expected RIFF");
        r.ReadInt32();if(new string(r.ReadChars(4))!="WAVE")throw new Exception("Expected WAV");
        int sampleRate=0;short channels=0,bits=0;byte[] data=null;
        while(r.BaseStream.Position+8<=r.BaseStream.Length)
        {
            string kind=new string(r.ReadChars(4));int length=r.ReadInt32();long next=r.BaseStream.Position+length+(length%2);
            if(kind=="fmt "){if(r.ReadInt16()!=1)throw new Exception("Expected PCM");channels=r.ReadInt16();sampleRate=r.ReadInt32();r.ReadInt32();r.ReadInt16();bits=r.ReadInt16();}
            if(kind=="data")data=r.ReadBytes(length);
            r.BaseStream.Position=next;
        }
        if(channels!=1 || bits!=16 || data==null || sampleRate<=0)throw new Exception("Expected mono PCM16");
        var detector=new AudioTransientDetector();var times=new System.Collections.Generic.List<double>();
        int size=sampleRate/100;
        for(int start=0;start+size<=data.Length/2;start+=size)
        {
            float peak=0,sum=0;
            for(int n=0;n<size;n++){float v=BitConverter.ToInt16(data,(start+n)*2)/32768f;peak=Mathf.Max(peak,Mathf.Abs(v));sum+=v*v;}
            double at=(start+size)/(double)sampleRate;
            if(detector.Sample(peak,Mathf.Sqrt(sum/size),at))times.Add(Math.Round(at,3));
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new {file=System.IO.Path.GetFileName(path),candidateSoundTimesSeconds=times,meaning="sound onset only; no physical-impact classification"});
    }
    public static string SwingChecks()
    {
        int Count(float[] angles,string otherSource=null,double gap=0)
        {
            var gate=new ClubSwingGate();var reference=Quaternion.Euler(17,65,-12);
            gate.Calibrate(reference,"Left",0);
            int shots=0;
            for(int i=0;i<angles.Length;i++)
                if(gate.Sample(reference*Quaternion.AngleAxis(angles[i],Vector3.right),
                    Vector3.right*2,i>=4 && otherSource!=null?otherSource:"Left",
                    (i+1)*.02+(i>=4?gap:0),out _))shots++;
            return shots;
        }
        var swing=new[]{0f,10,26,45,30,16,4,0,30,4};
        if(Count(swing)!=1)throw new Exception("One swing must commit once.");
        if(Count(new[]{0f,5,10,15,8,0,12,0})!=0)throw new Exception("Waggle triggered a shot.");
        if(Count(swing,"Right")!=0)throw new Exception("Source handover triggered a shot.");
        if(Count(swing,null,.5)!=0)throw new Exception("Gap triggered a shot.");
        return "PASS: nonidentity calibration; one shot per swing; waggles rejected; source switch disarms; stale gap disarms.";
    }
}
