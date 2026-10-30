using System;
namespace ITPU.Domain
{
    // Policy only. Hook outputs to ONE verified collision-aware Meta locomotor.
    // This file does not move cameras, read InputDevices, resolve walls, or prove comfort.
    public sealed class WalkSample
    {
        public double Time, X, Y, Turn, Yaw;
        public bool HeadTracked, MoveTracked, TurnTracked, Focused, Paused, Modal, PrecisionBusy, Teleporting, Carrying;
    }
    public readonly struct WalkCommand
    {
        internal readonly Guid Session;
        internal readonly int Epoch;
        public readonly long Sequence;
        public readonly double Time, Dx, Dz, TurnDegrees, Speed;
        internal WalkCommand(Guid session,int epoch,long seq,double time,double dx,double dz,double turn,double speed)
        {Session=session;Epoch=epoch;Sequence=seq;Time=time;Dx=dx;Dz=dz;TurnDegrees=turn;Speed=speed;}
    }
    public sealed class ControllerWalkPolicy
    {
        readonly Guid session=Guid.NewGuid(); int epoch;long sequence;
        double lastNow=double.NegativeInfinity,lastSample=double.NegativeInfinity,lastTurn=double.NegativeInfinity,speed;
        bool ready,turnReady,controllers=true,smooth;
        WalkCommand? pending;
        public string Reason{get;private set;}
        public ControllerWalkPolicy(){Cancel("INITIAL_NEUTRAL_REQUIRED");}
        static bool F(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
        public void Cancel(string reason){ready=false;turnReady=false;speed=0;pending=null;Reason=reason;}
        public void Reset(){epoch++;lastSample=double.NegativeInfinity;Cancel("RESET");}
        public void SetMode(bool controllersEnabled,bool smoothTurn){if(controllers!=controllersEnabled||smooth!=smoothTurn){controllers=controllersEnabled;smooth=smoothTurn;Reset();}}
        public WalkCommand? Update(WalkSample i,double dt,double now)
        {
            pending=null;
            if(i==null||!F(dt)||!F(now)||!F(i.Time)||!F(i.X)||!F(i.Y)||!F(i.Turn)||!F(i.Yaw)||dt<=0||dt>ControllerWalkDefaults.MaxDt||now<0||now<lastNow||i.Time>now||now-i.Time>ControllerWalkDefaults.MaxAge||i.Time<lastSample||Math.Abs(i.X)>1||Math.Abs(i.Y)>1||Math.Abs(i.Turn)>1)
            {Cancel("INVALID_OR_STALE_INPUT");return null;}
            lastNow=now;lastSample=i.Time;
            if(!controllers||!i.HeadTracked||!i.MoveTracked||!i.TurnTracked||!i.Focused||i.Paused||i.Modal||i.PrecisionBusy||i.Teleporting)
            {Cancel("CONTEXT_BLOCKED");return null;}
            double m=Math.Sqrt(i.X*i.X+i.Y*i.Y);
            if(!F(m)){Cancel("INVALID_OR_STALE_INPUT");return null;}
            if(!ready){if(m<=ControllerWalkDefaults.Deadzone&&Math.Abs(i.Turn)<=ControllerWalkDefaults.TurnOff){ready=true;turnReady=true;Reason="READY";}return null;}
            double a=m>ControllerWalkDefaults.Deadzone?(Math.Min(m,1)-ControllerWalkDefaults.Deadzone)/(1-ControllerWalkDefaults.Deadzone):0;
            double sx=m>0?i.X/m*a*ControllerWalkDefaults.Strafe:0,sz=m>0?i.Y/m*a*(i.Y<0?ControllerWalkDefaults.Backward:1):0,mag=Math.Sqrt(sx*sx+sz*sz);
            double target=(i.Carrying?ControllerWalkDefaults.CarrySpeed:ControllerWalkDefaults.Speed)*mag;
            speed=target<=0?0:Math.Min(target,speed+ControllerWalkDefaults.Acceleration*dt);
            double x=mag>0?sx/mag*speed:0,z=mag>0?sz/mag*speed:0,yaw=(i.Yaw%360)*Math.PI/180;
            double vx=x*Math.Cos(yaw)+z*Math.Sin(yaw),vz=z*Math.Cos(yaw)-x*Math.Sin(yaw),turn=0;
            if(Math.Abs(i.Turn)<=ControllerWalkDefaults.TurnOff)turnReady=true;
            if(!smooth){if(Math.Abs(i.Turn)>=ControllerWalkDefaults.TurnOn&&turnReady&&now-lastTurn>=ControllerWalkDefaults.TurnCooldown){turn=Math.Sign(i.Turn)*ControllerWalkDefaults.Snap;turnReady=false;lastTurn=now;}}
            else {double t=Math.Max(-1,Math.Min(1,i.Turn));if(Math.Abs(t)>ControllerWalkDefaults.TurnOff)turn=Math.Sign(t)*(Math.Abs(t)-ControllerWalkDefaults.TurnOff)/(1-ControllerWalkDefaults.TurnOff)*ControllerWalkDefaults.SmoothDps*dt;}
            Reason="READY";if(speed==0&&turn==0)return null;
            pending=new WalkCommand(session,epoch,++sequence,now,vx*dt,vz*dt,turn,speed);return pending;
        }
        public WalkCommand? Consume(WalkCommand? command,double now,bool contextSafe)
        {
            if(!command.HasValue||!pending.HasValue||command.Value.Sequence!=pending.Value.Sequence||command.Value.Session!=session||command.Value.Epoch!=epoch||!F(now)||now<command.Value.Time||now-command.Value.Time>ControllerWalkDefaults.MaxAge||!contextSafe)
            {Cancel("REJECTED_COMMIT");return null;}
            pending=null;return command.Value;
        }
    }
}
