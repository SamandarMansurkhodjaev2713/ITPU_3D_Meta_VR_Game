using System;
using ITPU.Domain;
internal static class V6Cases
{
    private static void Assert(bool b){if(!b)throw new Exception("V6 assertion failed");}
    private static void Reject(Action f){try{f();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
    private static GuideProject Project(Session s){return new GuideProject(s,new Topology(new[]{"A","B","C"},new[]{Tuple.Create("A","B"),Tuple.Create("B","C")}),"A");}
    private static void Finish(Session s,GuideProject p){foreach(Challenge c in new[]{Challenge.C01,Challenge.C02,Challenge.C03}){p.SetGraph(GraphCompiler.Template(c),s.Token);Assert(p.Check(c,s.Token,"B","C"));}}
    public static void Run(Action<string,Action> test)
    {
        test("v6 environment without NPC valid",()=>Assert(ScenePolicy.Validate(SceneProfile.Environment,0,0,false,true).Length==0));
        test("v6 complete without NPC invalid",()=>Assert(ScenePolicy.Validate(SceneProfile.CompleteGame,0,1,true,true).Length>0));
        test("v6 release needs external evidence",()=>Assert(ScenePolicy.Validate(SceneProfile.Release,3,1,true,true).Length>0));
        test("v6 environment cannot hide missing material",()=>Assert(ScenePolicy.Validate(SceneProfile.Environment,0,0,false,false).Length>0));
        test("v6 project all real checks seal",()=>{var s=new Session();var p=Project(s);Finish(s,p);Assert(p.Seal(s.Token).Ui.Linked);});
        foreach(Challenge c in new[]{Challenge.C01,Challenge.C02,Challenge.C03}){var id=c;test("v6 edit invalidates "+id,()=>{var s=new Session();var p=Project(s);Finish(s,p);p.SetGraph(GraphCompiler.Template(id),s.Token);Reject(()=>p.Seal(s.Token));Assert(!s.Ready);});}
        test("v6 test needs two destinations",()=>{var s=new Session();var p=Project(s);p.SetGraph(GraphCompiler.Template(Challenge.C01),s.Token);Assert(!p.Check(Challenge.C01,s.Token,"B","B"));});
        test("v6 different session token rejected",()=>{var a=new Session();var b=new Session();Assert(!b.Valid(a.Token));});
        test("v6 stale project token",()=>{var s=new Session();var p=Project(s);var t=s.Token;s.Reset();Assert(!p.SetGraph(GraphCompiler.Template(Challenge.C01),t));Reject(()=>p.Seal(t));});
        test("v6 held prop cannot return",()=>{var s=new Session();var p=new RecoverableProp(s);p.Hold(s.Token);p.LostTracking();Assert(!p.TryReturn(true,s.Token));p.Release(s.Token);Assert(p.TryReturn(true,s.Token));});
        test("v6 blocked destination",()=>{var s=new Session();Assert(!new RecoverableProp(s).TryReturn(false,s.Token));});
        test("v6 golden drawing nine pixels",()=>{var d=new DrawingSurface(9,9);d.Begin();d.Line(0,0,1,0,.5,1);d.End();Assert(d.NonEmptyPixels()==9);});
        test("v6 partial eraser undo",()=>{var d=new DrawingSurface(9,9);d.Begin();d.Line(0,0,1,0,.5,1);d.End();d.Begin();d.Line(.5,0,.5,0,.5,0);d.End();Assert(d.NonEmptyPixels()==8);d.Undo();Assert(d.NonEmptyPixels()==9);});
        test("v6 cancelled stroke",()=>{var d=new DrawingSurface();d.Begin();d.Line(0,0,1,1,1,1);d.Cancel();Assert(d.NonEmptyPixels()==0);});
        test("v6 snapshot immutable",()=>{var d=new DrawingSurface();var p=d.Snapshot();p[0]=2;Assert(d.NonEmptyPixels()==0);});
        test("v6 no per-frame allocation copy",()=>{var d=new DrawingSurface(9,9);var p=new byte[81];d.CopyPixels(p);Assert(p[0]==0);Reject(()=>d.CopyPixels(new byte[1]));});
        test("v6 invalid raster values",()=>{var d=new DrawingSurface();d.Begin();Reject(()=>d.Line(double.NaN,0,1,1,1,1));Reject(()=>d.Line(0,0,1,1,1,9));});
        test("v6 basket needs entry",()=>Assert(!new PaperBasketProbe().Tick(true,1,false)));
        test("v6 basket scores once",()=>{var b=new PaperBasketProbe();b.EnterMouth(true,true);Assert(b.Tick(true,.2,false));Assert(!b.Tick(true,.2,false));});
    }
}
