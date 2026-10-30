using System;
namespace ITPU.Domain
{
    public enum DemoRole { Guest,Team }
    public enum DemoOperation { ViewPublicProject,EditRoute }
    public readonly struct DemoResult
    {
        public readonly bool Allowed,Mutated;public DemoResult(bool allowed,bool mutated){Allowed=allowed;Mutated=mutated;}
    }
    // Local teaching sandbox only. No file/network/developer/operator operations exist in this API.
    public sealed class AuthSandbox
    {
        private readonly CompiledGraph config;private readonly SessionToken token;
        public int RouteVersion{get;private set;}public readonly TraceBuffer Trace=new TraceBuffer();
        public AuthSandbox(CompiledGraph config){if(config==null||config.Challenge!=Challenge.C03)throw new ArgumentException("Wrong graph");this.config=config;token=new SessionToken(Guid.NewGuid(),1);}
        public void Reset(){RouteVersion=0;Trace.Clear();}
        public static bool Allowed(CompiledGraph c,DemoRole role,DemoOperation op)
        {
            if(c==null||c.Challenge!=Challenge.C03)throw new ArgumentException("Wrong graph");
            if(!Enum.IsDefined(typeof(DemoRole),role)||!Enum.IsDefined(typeof(DemoOperation),op))return false;
            if(!c.RequiresPermission)return true;
            if(op==DemoOperation.ViewPublicProject)return c.Permissions.GuestView;
            return role==DemoRole.Guest?c.Permissions.GuestEdit:c.Permissions.TeamEdit;
        }
        public DemoResult Execute(DemoRole role,DemoOperation op)
        {
            Trace.Add(token,1,"receive","ReceiveDemoRequest",role.ToString());bool allow=Allowed(config,role,op);
            if(config.RequiresPermission)Trace.Add(token,1,"check","RequirePermission",allow?"ALLOW":"DENY");
            if(!allow){Trace.Add(token,1,"reject","RejectDemoOperation","DENY");return new DemoResult(false,false);}
            bool change=op==DemoOperation.EditRoute;if(change)RouteVersion++;Trace.Add(token,1,"execute","ExecuteDemoOperation",op.ToString());return new DemoResult(true,change);
        }
        public static bool Verify(CompiledGraph c)
        {
            if(c==null||!c.RequiresPermission)return false;
            var a=new AuthSandbox(c);var view=a.Execute(DemoRole.Guest,DemoOperation.ViewPublicProject);if(!view.Allowed||view.Mutated||a.RouteVersion!=0)return false;
            var b=new AuthSandbox(c);var deny=b.Execute(DemoRole.Guest,DemoOperation.EditRoute);if(deny.Allowed||deny.Mutated||b.RouteVersion!=0)return false;
            var d=new AuthSandbox(c);var edit=d.Execute(DemoRole.Team,DemoOperation.EditRoute);return edit.Allowed&&edit.Mutated&&d.RouteVersion==1;
        }
    }
    public sealed class CompiledGuideConfig
    {
        public CompiledGraph Ui{get;}public CompiledGraph Logic{get;}public CompiledGraph Policy{get;}
        private CompiledGuideConfig(CompiledGraph ui,CompiledGraph logic,CompiledGraph policy){Ui=ui;Logic=logic;Policy=policy;}
        public static CompiledGuideConfig Seal(CompiledGraph ui,CompiledGraph logic,CompiledGraph policy)
        {if(ui==null||logic==null||policy==null||!ui.Linked||!logic.Wait||!AuthSandbox.Verify(policy))throw new InvalidOperationException("Guide is not behaviourally validated");return new CompiledGuideConfig(ui,logic,policy);}
    }
}
