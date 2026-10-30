using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using ITPU.Domain;
internal static class VectorCases
{
    public static int Run(string path)
    {
        using(JsonDocument doc=JsonDocument.Parse(File.ReadAllText(path)))
        {
            int failed=0;var results=new List<object>();
            foreach(JsonElement v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                string kind=v.GetProperty("kind").GetString();object actual;
                if(kind=="profile")
                {
                    string id=v.GetProperty("profile").GetString();SceneProfile p=id=="environment"?SceneProfile.Environment:id=="interaction_slice"?SceneProfile.InteractionSlice:id=="complete_game"?SceneProfile.CompleteGame:SceneProfile.Release;
                    actual=ScenePolicy.Validate(p,v.GetProperty("npcs").GetInt32(),v.GetProperty("sessions").GetInt32(),v.GetProperty("coffee").GetBoolean(),v.GetProperty("materials").GetBoolean()).Length==0;
                }
                else if(kind=="auth")
                {
                    var b=new AuthSandbox(GraphCompiler.Compile(GraphCompiler.Template(Challenge.C03,v.GetProperty("correct").GetBoolean())));
                    string r=v.GetProperty("role").GetString();DemoRole role=r=="guest"?DemoRole.Guest:r=="team"?DemoRole.Team:(DemoRole)99;
                    string o=v.GetProperty("operation").GetString();DemoOperation op=o=="view_public_project"?DemoOperation.ViewPublicProject:o=="edit_route"?DemoOperation.EditRoute:(DemoOperation)99;
                    var result=b.Execute(role,op);actual=new{allowed=result.Allowed,version=b.RouteVersion};
                }
                else if(kind=="draw")
                {
                    var d=new DrawingSurface(v.GetProperty("width").GetInt32(),v.GetProperty("height").GetInt32());
                    foreach(var line in v.GetProperty("strokes").EnumerateArray()){d.Begin();d.Line(line[0].GetDouble(),line[1].GetDouble(),line[2].GetDouble(),line[3].GetDouble(),line[4].GetDouble(),line[5].GetByte());d.End();}
                    if(v.GetProperty("undo").GetBoolean())d.Undo();actual=d.NonEmptyPixels();
                }
                else if(kind=="c02_actual")
                {
                    var s=new Session();var c=new C02WaitChallenge(s,new[]{"A"});var token=s.Token;
                    bool locked=c.Step(true,false,true,token);s.Complete(Challenge.C01,token);
                    c.Step(true,false,true,token);string brokenStage=c.Stage.ToString();c.InstallWaitRule(token);
                    c.Step(true,false,true,token);string waitingStage=c.Stage.ToString();c.Step(true,false,true,token);
                    int heldIndex=c.WaypointIndex;c.Step(false,true,true,token);c.Step(false,false,true,token);
                    bool completed=c.Completed;var old=token;s.Reset();c.ResetForCurrentSession();
                    bool staleAfterReset=c.Step(true,false,true,old);string resetStage=c.Stage.ToString();
                    actual=new{locked,brokenStage,waitingStage,heldIndex,completed,staleAfterReset,resetStage};
                }
                else if(kind=="c03_actual")
                {
                    var s=new Session();var c=new C03AuthorizationChallenge(s);var token=s.Token;
                    bool locked=c.DemonstrateBaselineBypass(true,token).Accepted;
                    s.Complete(Challenge.C01,token);s.Complete(Challenge.C02,token);
                    var baseline=c.DemonstrateBaselineBypass(true,token);c.InstallGate(PermissionFlags.Correct,token);
                    long revision=c.PolicyRevision;
                    var viewResult=c.Execute(DemoRole.Guest,DemoOperation.ViewPublicProject,true,revision,token);
                    var denyResult=c.Execute(DemoRole.Guest,DemoOperation.EditRoute,true,revision,token);
                    var editResult=c.Execute(DemoRole.Team,DemoOperation.EditRoute,true,revision,token);
                    bool completed=c.Completed,ready=s.Ready;c.InstallGate(PermissionFlags.Correct,token);
                    bool staleRevisionAccepted=c.Execute(DemoRole.Guest,DemoOperation.ViewPublicProject,true,revision,token).Accepted;
                    s.Reset();c.ResetForCurrentSession();
                    actual=new{locked,baselineStage=baseline.Stage.ToString(),baselineAllowed=baseline.Allowed,
                        baselineVersion=baseline.VersionAfter,
                        view=new{accepted=viewResult.Accepted,allowed=viewResult.Allowed,mutated=viewResult.Mutated,version=viewResult.VersionAfter},
                        deny=new{accepted=denyResult.Accepted,allowed=denyResult.Allowed,mutated=denyResult.Mutated,version=denyResult.VersionAfter},
                        edit=new{accepted=editResult.Accepted,allowed=editResult.Allowed,mutated=editResult.Mutated,version=editResult.VersionAfter},
                        completed,ready,staleRevisionAccepted,resetStage=c.Stage.ToString(),resetPrerequisite=c.PrerequisiteMet};
                }
                else throw new ArgumentException("Unknown vector kind");
                // Compare normalized JSON, not formatting whitespace in the input file.
                string expected=JsonSerializer.Serialize(v.GetProperty("expected"));bool ok=JsonSerializer.Serialize(actual)==expected;
                if(!ok)failed++;results.Add(new{id=v.GetProperty("id").GetString(),actual,expected=v.GetProperty("expected").Clone(),status=ok?"PASS":"FAIL"});
            }
            Console.WriteLine(JsonSerializer.Serialize(new{implementation="CSharp",results,failed}));return failed==0?0:1;
        }
    }
}
