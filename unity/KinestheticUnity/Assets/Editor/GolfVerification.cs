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
