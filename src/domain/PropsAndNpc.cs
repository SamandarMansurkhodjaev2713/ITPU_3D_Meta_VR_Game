using System;
namespace ITPU.Domain
{
    public enum CoffeeState { Idle,CupReady,Brewing,Paused,Ready }
    public sealed class CoffeeMachine
    {
        private readonly double seconds;public bool CupPresent{get;private set;}public double Fill{get;private set;}public CoffeeState State{get;private set;}
        public CoffeeMachine(double seconds=6){Guard.Finite(seconds,"seconds");if(seconds<=0)throw new ArgumentOutOfRangeException(nameof(seconds));this.seconds=seconds;Reset();}
        public void Reset(){CupPresent=false;Fill=0;State=CoffeeState.Idle;}
        public void Dock(){CupPresent=true;if(Fill>=1)State=CoffeeState.Ready;else if(State!=CoffeeState.Brewing)State=CoffeeState.CupReady;}
        public void Remove(){CupPresent=false;if(State==CoffeeState.Brewing)State=CoffeeState.Paused;else if(State!=CoffeeState.Paused)State=CoffeeState.Idle;}
        public bool Press(){if(!CupPresent||Fill>=1||State==CoffeeState.Brewing)return false;State=CoffeeState.Brewing;return true;}
        public void Tick(double dt,bool paused=false)
        {Guard.NonNegative(dt,"dt");if(paused||!CupPresent||State!=CoffeeState.Brewing)return;Fill=Math.Min(1,Fill+Math.Min(dt,.25)/seconds);if(Fill>1-1e-9){Fill=1;State=CoffeeState.Ready;}}
        public bool Empty(bool held){if(held)return false;Fill=0;State=CupPresent?CoffeeState.CupReady:CoffeeState.Idle;return true;}
    }
    public readonly struct ReleaseResult
    {public readonly bool Released,MayThrow;public ReleaseResult(bool released,bool mayThrow){Released=released;MayThrow=mayThrow;}}
    public sealed class PropOwnership
    {
        public string Owner{get;private set;}public bool Tracked{get;private set;}
        public bool Grab(string owner){Guard.Id(owner);if(Owner!=null)return false;Owner=owner;Tracked=true;return true;}
        public ReleaseResult Release(string owner,bool intentional){if(Owner!=owner)return new ReleaseResult(false,false);bool canThrow=intentional&&Tracked;Owner=null;Tracked=false;return new ReleaseResult(true,canThrow);}
        public void TrackingLost(){Owner=null;Tracked=false;}
        public void Reset(){Owner=null;Tracked=false;}
    }
    public enum NpcGesture { Idle,Greet,Explain,Point,Acknowledge,Humor }
    public sealed class NpcStation
    {
        public Vec3 Anchor{get;}private readonly double bodyLimit,headLimit,speed;public double BodyYaw{get;private set;}public double HeadYaw{get;private set;}public NpcGesture Gesture{get;private set;}
        public NpcStation(Vec3 anchor,double bodyLimit=25,double headLimit=35,double speed=70)
        {Anchor=anchor;this.bodyLimit=Guard.NonNegative(bodyLimit,"bodyLimit");this.headLimit=Guard.NonNegative(headLimit,"headLimit");Guard.Finite(speed,"speed");if(speed<=0)throw new ArgumentException("Speed must be positive");this.speed=speed;Reset();}
        public void Reset(){BodyYaw=HeadYaw=0;Gesture=NpcGesture.Idle;}
        public void Look(double relativeYaw,double dt,bool tracked=true,bool paused=false)
        {Guard.Finite(relativeYaw,"yaw");Guard.NonNegative(dt,"dt");if(paused)return;double y=tracked&&Math.Abs(relativeYaw)<=110?relativeYaw:0;double body=Guard.Clamp(y*.45,-bodyLimit,bodyLimit),head=Guard.Clamp(y-body,-headLimit,headLimit),step=speed*Math.Min(dt,.1);BodyYaw+=Guard.Clamp(body-BodyYaw,-step,step);HeadYaw+=Guard.Clamp(head-HeadYaw,-step,step);}
        public void React(NpcGesture gesture){if(!Enum.IsDefined(typeof(NpcGesture),gesture))throw new ArgumentException("Unknown gesture");Gesture=gesture;}
    }
    public sealed class ReactionGate
    {
        private readonly double cooldown;private double last=double.NegativeInfinity;
        public ReactionGate(double cooldown=12){this.cooldown=Guard.NonNegative(cooldown,"cooldown");}
        public bool TryReact(double now,bool mainSpeaking=false,bool visible=true){Guard.Finite(now,"now");if(mainSpeaking||!visible||now-last<cooldown)return false;last=now;return true;}
        public void Reset(){last=double.NegativeInfinity;}
    }
}
