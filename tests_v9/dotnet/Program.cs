using System;using ITPU.Domain;
class Program{
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static WalkSample S(double t,double y=0,double turn=0){return new WalkSample{Time=t,Y=y,Turn=turn,HeadTracked=true,MoveTracked=true,TurnTracked=true,Focused=true};}
 static int Main(){try{
 var p=new ControllerWalkPolicy();Check(p.Update(S(.01,1),.01,.01)==null,"neutral needed");p.Update(S(.02),.01,.02);
 var c=p.Update(S(.04,1),.02,.04);Check(c!=null&&c.Dz>0,"forward");Check(p.Consume(c,.05,true)==c,"consume");Check(p.Consume(c,.06,true)==null,"duplicate rejected");
 p.Update(S(.07),.01,.07);c=p.Update(S(.08,0,1),.01,.08);Check(c!=null&&c.TurnDegrees==30,"snap");Check(p.Update(S(.09,0,1),.01,.09)==null,"no repeat");
 var paused=S(.1,1);paused.Paused=true;Check(p.Update(paused,.01,.1)==null,"pause");Check(p.Update(S(.11,1),.01,.11)==null,"rearm after pause");
 p.Update(S(.12),.01,.12);Check(p.Update(S(.13,double.NaN),.01,.13)==null,"NaN");p.Update(S(.14),.01,.14);Check(p.Update(S(1,1),1,1)==null,"spike");
 p.SetMode(false,false);Check(p.Update(S(1.1,1),.02,1.1)==null,"hands blocks stick");Console.WriteLine("C# smoke passed; Unity/Quest NOT_RUN");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
