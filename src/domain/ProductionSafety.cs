using System;
using System.Collections.Generic;
namespace ITPU.Domain
{
    // Stage is explicit: successful geometry review does not imply release readiness.
    public enum SceneProfile { Environment, InteractionSlice, CompleteGame, Release }
    public static class ScenePolicy
    {
        public static string[] Validate(SceneProfile profile,int npcCount,int sessionCount,bool hasCoffee,
            bool requiredMaterialsPresent,bool releaseEvidencePresent=false)
        {
            if(!Enum.IsDefined(typeof(SceneProfile),profile)||npcCount<0||sessionCount<0)throw new ArgumentException("Invalid scene evidence");
            var errors=new List<string>();
            if(!requiredMaterialsPresent)errors.Add("missing_material");
            if(profile==SceneProfile.Environment&&npcCount!=0)errors.Add("npc_deferred_in_environment");
            if(profile!=SceneProfile.Environment&&sessionCount!=1)errors.Add("exactly_one_session_required");
            if((int)profile>=(int)SceneProfile.CompleteGame&&npcCount!=3)errors.Add("three_npc_required");
            if((int)profile>=(int)SceneProfile.InteractionSlice&&!hasCoffee)errors.Add("coffee_required");
            if(profile==SceneProfile.Release&&!releaseEvidencePresent)errors.Add("external_release_evidence_required");
            return errors.ToArray();
        }
    }
    // A generation fence is independent of UI and cancels delayed callbacks after reset.
    public sealed class RecoverableProp
    {
        private SessionToken epoch;private readonly Session session;
        public bool Held {get;private set;}public bool TrackingValid {get;private set;}
        public long ReturnCount {get;private set;}
        public RecoverableProp(Session session){this.session=session??throw new ArgumentNullException(nameof(session));Reset();}
        public void Reset(){epoch=session.Token;Held=false;TrackingValid=true;ReturnCount=0;}
        public bool Hold(SessionToken token){if(!session.Valid(token)||!token.Equals(epoch)||session.Paused||Held)return false;Held=true;TrackingValid=true;return true;}
        public void LostTracking(){TrackingValid=false;}
        // Ownership must be explicitly released by the adapter, never inferred as a throw.
        public bool Release(SessionToken token){if(!session.Valid(token)||!token.Equals(epoch)||!Held)return false;Held=false;return true;}
        public bool TryReturn(bool destinationClear,SessionToken token)
        {if(!session.Valid(token)||!token.Equals(epoch)||session.Paused||Held||!destinationClear)return false;ReturnCount++;TrackingValid=true;return true;}
    }
    public sealed class GuideProject
    {
        private readonly Session session;private readonly Topology topology;private readonly string origin;
        private SessionToken epoch;private readonly CompiledGraph[] graphs=new CompiledGraph[3];
        private readonly long[] revisions=new long[3],verified=new long[3];
        public GuideProject(Session session,Topology topology,string origin)
        {this.session=session??throw new ArgumentNullException(nameof(session));this.topology=topology??throw new ArgumentNullException(nameof(topology));this.origin=Guard.Id(origin);Reset();}
        public void Reset(){epoch=session.Token;for(int i=0;i<3;i++){graphs[i]=GraphCompiler.Compile(GraphCompiler.Template((Challenge)i,false));revisions[i]=1;verified[i]=0;}session.InvalidateFrom(Challenge.C01);}
        private bool Active(SessionToken token){return token.Equals(epoch)&&session.Valid(token)&&!session.Paused;}
        public bool SetGraph(GraphDefinition definition,SessionToken token)
        {if(!Active(token))return false;var next=GraphCompiler.Compile(definition);int i=(int)next.Challenge;graphs[i]=next;revisions[i]++;for(int j=i;j<3;j++)verified[j]=0;session.InvalidateFrom(next.Challenge);return true;}
        public bool Check(Challenge challenge,SessionToken token,string destinationA=null,string destinationB=null,bool assisted=false)
        {
            if(!Enum.IsDefined(typeof(Challenge),challenge))throw new ArgumentException("Unknown challenge");
            if(!Active(token))return false;int i=(int)challenge;
            for(int k=0;k<i;k++)if(verified[k]!=revisions[k])return false;
            bool pass=false;
            if(challenge==Challenge.C01)
            {
                if(destinationA==destinationB||destinationA==null||destinationB==null)return false;
                var probe=new ButtonRouter(session,topology,origin,graphs[0]);
                bool a=probe.Activate(destinationA,1,token);var pa=probe.RouteSnapshot();
                bool b=probe.Activate(destinationB,2,token);var pb=probe.RouteSnapshot();
                bool c=probe.Activate(destinationA,3,token);var pc=probe.RouteSnapshot();
                pass=a&&b&&c&&pa.Length>0&&pb.Length>0&&pc.Length>0&&pa[pa.Length-1]==destinationA&&pb[pb.Length-1]==destinationB&&pc[pc.Length-1]==destinationA;
            }
            else if(challenge==Challenge.C02)
            {
                var probe=new Guide(session,new[]{"probe_a","probe_b"},graphs[1]);
                probe.Step(true,false,true,token);bool waits=probe.State==GuideState.Waiting;
                probe.Step(false,false,true,token);bool stays=probe.State==GuideState.Waiting&&probe.Index==0;
                probe.Step(false,true,true,token);probe.Step(false,false,true,token);
                pass=waits&&stays&&probe.Index==1;
            }
            else pass=AuthSandbox.Verify(graphs[2]);
            verified[i]=pass?revisions[i]:0;
            if(!pass){for(int j=i;j<3;j++)verified[j]=0;session.InvalidateFrom(challenge);return false;}
            return session.Complete(challenge,token,assisted);
        }
        public bool Verified(Challenge c){if(!Enum.IsDefined(typeof(Challenge),c))return false;int i=(int)c;return session.Valid(epoch)&&verified[i]==revisions[i];}
        public CompiledGuideConfig Seal(SessionToken token)
        {if(!Active(token)||!session.Ready||!Verified(Challenge.C01)||!Verified(Challenge.C02)||!Verified(Challenge.C03))throw new InvalidOperationException("Configuration tests stale or incomplete");return CompiledGuideConfig.Seal(graphs[0],graphs[1],graphs[2]);}
    }
}
