using System;
namespace ITPU.Domain
{
    // This is a semantic gate. No SDK gesture recognition, floor tests, or XR Origin movement.
    public enum TeleportIntentState { Idle, Aim, Armed, Pending, Cooldown, Neutral }
    public sealed class TeleportFacts
    {
        public readonly bool Tracked, HeadTracked, Focused, Paused, UiBusy, Holding, SystemGesture;
        public readonly bool PathClear, LandingClear, HeadClear, TargetEnabled;
        public readonly string Anchor, Source, Revision;
        public readonly double Distance;
        public TeleportFacts(string anchor, string source, string revision, double distance,
            bool tracked, bool headTracked, bool focused, bool paused, bool uiBusy, bool holding,
            bool systemGesture, bool pathClear, bool landingClear, bool headClear, bool targetEnabled)
        { Anchor=anchor;Source=source;Revision=revision;Distance=distance;Tracked=tracked;HeadTracked=headTracked;
          Focused=focused;Paused=paused;UiBusy=uiBusy;Holding=holding;SystemGesture=systemGesture;
          PathClear=pathClear;LandingClear=landingClear;HeadClear=headClear;TargetEnabled=targetEnabled; }
    }
    public sealed class TeleportIntent
    {
        internal readonly Guid Session;
        internal readonly int Epoch;
        public readonly long Sequence;
        public readonly string Anchor, Source, Revision;
        internal TeleportIntent(Guid session,int epoch,long sequence,TeleportFacts f)
        {Session=session;Epoch=epoch;Sequence=sequence;Anchor=f.Anchor;Source=f.Source;Revision=f.Revision;}
    }
    public sealed class TeleportIntentGate
    {
        private readonly Guid session=Guid.NewGuid();
        private int epoch;private long sequence;
        private readonly double minHold,stableAim,cooldown,maxRange,maxPending;
        private double lastTime=double.NegativeInfinity,lastCommit=double.NegativeInfinity,aimSince,pressSince,pendingSince;
        private string anchor,owner,revision;private TeleportIntent pending;
        public TeleportIntentState State{get;private set;}
        public string Reason{get;private set;}
        public TeleportIntentGate(double minHold=.12,double stableAim=.15,double cooldown=.3,double maxRange=6,double maxPending=1)
        {
            if(!Finite(minHold)||!Finite(stableAim)||!Finite(cooldown)||!Finite(maxRange)||minHold<0||stableAim<0||cooldown<0||maxRange<=0||!Finite(maxPending)||maxPending<=0)
                throw new ArgumentOutOfRangeException(nameof(minHold));
            this.minHold=minHold;this.stableAim=stableAim;this.cooldown=cooldown;this.maxRange=maxRange;this.maxPending=maxPending;
            State=TeleportIntentState.Idle;Reason="";
        }
        static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        void Clock(double now)
        {
            if(!Finite(now)||now<0||now<lastTime){Cancel("INVALID_TIME");throw new ArgumentOutOfRangeException(nameof(now));}
            lastTime=now;
        }
        public bool Valid(TeleportFacts f)
        {return f!=null&&f.Tracked&&f.HeadTracked&&f.Focused&&!f.Paused&&!f.UiBusy&&!f.Holding&&!f.SystemGesture&&
            f.PathClear&&f.LandingClear&&f.HeadClear&&f.TargetEnabled&&!string.IsNullOrEmpty(f.Anchor)&&
            !string.IsNullOrEmpty(f.Source)&&!string.IsNullOrEmpty(f.Revision)&&Finite(f.Distance)&&f.Distance>=.4&&f.Distance<=maxRange;}
        bool Same(TeleportFacts f){return Valid(f)&&f.Anchor==anchor&&f.Source==owner&&f.Revision==revision;}
        public void Cancel(string reason="CANCELLED")
        {State=TeleportIntentState.Neutral;anchor=owner=revision=null;pending=null;Reason=reason;}
        public bool Neutral(double now)
        {Clock(now);if(now-lastCommit<cooldown)return false;State=TeleportIntentState.Idle;anchor=owner=revision=null;pending=null;Reason="";return true;}
        public void Reset(double now){Clock(now);epoch++;Cancel("RESET");}
        public bool Tick(double now,TeleportFacts f)
        {Clock(now);if(State==TeleportIntentState.Pending&&now-pendingSince>maxPending){Cancel("EXPIRED_INTENT");return false;}if((State==TeleportIntentState.Aim||State==TeleportIntentState.Armed||State==TeleportIntentState.Pending)&&!Same(f)){Cancel("CONTEXT_CHANGED");return false;}return true;}
        public bool Aim(double now,TeleportFacts f)
        {Clock(now);if(!Valid(f)){if(State!=TeleportIntentState.Idle)Cancel("INVALID_TARGET");return false;}
         if(State==TeleportIntentState.Neutral||State==TeleportIntentState.Cooldown||State==TeleportIntentState.Pending||State==TeleportIntentState.Armed)return false;
         if(State!=TeleportIntentState.Aim||!Same(f)){State=TeleportIntentState.Aim;anchor=f.Anchor;owner=f.Source;revision=f.Revision;aimSince=now;}return true;}
        public bool Press(double now,TeleportFacts f)
        {Clock(now);if(State!=TeleportIntentState.Aim||!Same(f)||now-aimSince+1e-9<stableAim){Cancel("UNARMED_PRESS");return false;}
         State=TeleportIntentState.Armed;pressSince=now;return true;}
        public TeleportIntent Release(double now,TeleportFacts f)
        {Clock(now);if(State!=TeleportIntentState.Armed||!Same(f)||now-pressSince+1e-9<minHold){Cancel("INVALID_RELEASE");return null;}
         pending=new TeleportIntent(session,epoch,++sequence,f);pendingSince=now;State=TeleportIntentState.Pending;return pending;}
        public bool Commit(double now,TeleportIntent intent,TeleportFacts f)
        {Clock(now);if(State!=TeleportIntentState.Pending||intent==null||!ReferenceEquals(intent,pending)||now-pendingSince>maxPending||!Same(f)||intent.Session!=session||intent.Epoch!=epoch)
          {Cancel("INVALID_COMMIT");return false;}
         pending=null;State=TeleportIntentState.Cooldown;lastCommit=now;return true;}
    }
}
