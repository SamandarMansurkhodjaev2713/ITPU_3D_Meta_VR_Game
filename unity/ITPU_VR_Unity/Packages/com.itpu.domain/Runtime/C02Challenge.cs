using System;

namespace ITPU.Domain
{
    public enum C02Stage { BrokenReady, BrokenGuideLeft, WaitRuleInstalled, WaitingForGuest, GuestConfirmed, Verified }

    public sealed class C02WaitChallenge
    {
        private readonly Session session;private readonly string[] waypoints;private readonly TraceBuffer trace=new TraceBuffer();
        private SessionToken epoch;private Guide guide;
        public C02Stage Stage {get;private set;}public bool WaitRuleInstalled {get;private set;}public bool Completed {get;private set;}
        public bool PrerequisiteMet {get{return session.IsCompleted(Challenge.C01);}}public GuideState GuideState {get{return guide.State;}}public int WaypointIndex {get{return guide.Index;}}
        public C02WaitChallenge(Session session,string[] waypoints)
        {this.session=session??throw new ArgumentNullException(nameof(session));if(waypoints==null||waypoints.Length==0)throw new ArgumentException("Waypoints required");this.waypoints=(string[])waypoints.Clone();foreach(var waypoint in this.waypoints)Guard.Id(waypoint);ResetForCurrentSession();}
        public TraceEntry[] TraceSnapshot(){return trace.Snapshot();}
        public bool Step(bool markerArrived,bool guestArrived,bool tracking,SessionToken token)
        {
            if(!Active(token)||!PrerequisiteMet||!tracking||Completed)return false;if(!WaitRuleInstalled&&Stage==C02Stage.BrokenGuideLeft)return false;
            var before=guide.State;var after=guide.Step(markerArrived,guestArrived,tracking,token);
            if(!WaitRuleInstalled){if(before==GuideState.Moving&&after==GuideState.Advance&&markerArrived&&!guestArrived){Stage=C02Stage.BrokenGuideLeft;trace.Add(token,1,"move","GuideLeftWithoutGuest",waypoints[guide.Index]);return true;}return before!=after;}
            if(before==GuideState.Moving&&after==GuideState.Waiting){Stage=C02Stage.WaitingForGuest;trace.Add(token,2,"wait","WaitForGuest","holding");return true;}
            if(before==GuideState.Waiting&&after==GuideState.Waiting&&!guestArrived){Stage=C02Stage.WaitingForGuest;trace.Add(token,2,"wait","WaitForGuest","still-waiting");return true;}
            if(before==GuideState.Waiting&&after==GuideState.Advance&&guestArrived){Stage=C02Stage.GuestConfirmed;trace.Add(token,2,"guest","GuestArrived",waypoints[guide.Index]);return true;}
            if(after==GuideState.Complete){Completed=session.Complete(Challenge.C02,token);if(Completed){Stage=C02Stage.Verified;trace.Add(token,2,"advance","C02Verified","complete");}return Completed;}
            return before!=after;
        }
        public bool InstallWaitRule(SessionToken token){if(!Active(token)||!PrerequisiteMet||Stage!=C02Stage.BrokenGuideLeft||Completed)return false;session.InvalidateFrom(Challenge.C02);guide=NewGuide(true);WaitRuleInstalled=true;Stage=C02Stage.WaitRuleInstalled;trace.Add(token,2,"wait","WaitForGuestInstalled","ready-to-replay");return true;}
        public bool ConfirmedTeleport(string waypoint,bool tracking,SessionToken token){if(!Active(token)||!PrerequisiteMet||!WaitRuleInstalled||!tracking||Completed)return false;if(!guide.ConfirmedTeleport(Guard.Id(waypoint),token))return false;Stage=C02Stage.WaitingForGuest;trace.Add(token,2,"move","TeleportResync",waypoint);return true;}
        public void ResetForCurrentSession(){epoch=session.Token;session.InvalidateFrom(Challenge.C02);guide=NewGuide(false);WaitRuleInstalled=false;Completed=false;Stage=C02Stage.BrokenReady;trace.Clear();}
        private Guide NewGuide(bool correct){return new Guide(session,waypoints,GraphCompiler.Compile(GraphCompiler.Template(Challenge.C02,correct)));}
        private bool Active(SessionToken token){return token.Equals(epoch)&&session.Valid(token)&&!session.Paused;}
    }
}
