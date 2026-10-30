using System;using ITPU.Domain;
internal static class Program{
 static TeleportFacts F(bool tracked=true,bool paused=false,bool clear=true){return new TeleportFacts("HALL_A","right","scene1",2,tracked,true,true,paused,false,false,false,clear,true,true,true);}
 static void A(bool condition){if(!condition)throw new Exception("Probe assertion failed");}
 static void Main(){var g=new TeleportIntentGate();A(g.Aim(0,F()));A(g.Press(.2,F()));var r=g.Release(.4,F());A(r!=null);A(g.Commit(.5,r,F()));A(!g.Commit(.6,r,F()));
 var h=new TeleportIntentGate();h.Aim(0,F());h.Press(.2,F());h.Tick(.3,F(tracked:false));A(h.Release(.4,F())==null);
 var j=new TeleportIntentGate();j.Aim(0,F());j.Press(.2,F());var q=j.Release(.4,F());A(!j.Commit(.5,q,F(clear:false)));
 var k=new TeleportIntentGate();k.Aim(0,F());k.Press(.2,F());var t=k.Release(.4,F());k.Reset(.5);A(!k.Commit(.6,t,F()));
 Console.WriteLine("PASS semantic C# smoke only; no SDK/device proof");}}
