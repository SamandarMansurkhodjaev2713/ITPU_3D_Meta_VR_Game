// Prepared C# regression; execution must be recorded separately from the JS results.
using System;using ITPU.Domain;
class Program{
 static WalkSample S(double t,double yaw,bool move){return new WalkSample{Time=t,Y=move?1:0,Yaw=yaw,HeadTracked=true,MoveTracked=true,TurnTracked=true,Focused=true};}
 static WalkCommand? Cmd(double yaw){var p=new ControllerWalkPolicy();p.Update(S(0,0,false),.02,0);return p.Update(S(.02,yaw,true),.02,.02);}
 static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
 static int Main(){try{int count=0;foreach(double a in new[]{double.MaxValue,-double.MaxValue,1e308,-1e308,1e200,-1e200,360d,-360d,90d,-90d,450d,-450d,0d,180d,-180d}){
 var c=Cmd(a);var r=Cmd(a%360);if(!c.HasValue||!r.HasValue||!Finite(c.Value.Dx)||!Finite(c.Value.Dz)||Math.Abs(c.Value.Dx-r.Value.Dx)>1e-12||Math.Abs(c.Value.Dz-r.Value.Dz)>1e-12)throw new Exception("Invalid finite-yaw command: "+a);count++;}
 foreach(double a in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){if(Cmd(a).HasValue)throw new Exception("Invalid yaw accepted");count++;}
 Console.WriteLine("PASS "+count+" C# numerical cases; this does NOT test Unity or Quest.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
