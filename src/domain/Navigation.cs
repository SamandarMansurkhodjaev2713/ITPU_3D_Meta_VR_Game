using System;
using System.Collections.Generic;
namespace ITPU.Domain
{
    public sealed class Topology
    {
        private readonly Dictionary<string,List<string>> links=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        public Topology(IEnumerable<string> nodes,IEnumerable<Tuple<string,string>> edges)
        {
            if(nodes==null||edges==null)throw new ArgumentNullException();foreach(var n in nodes)links.Add(Guard.Id(n),new List<string>());
            foreach(var e in edges){if(e==null||e.Item1==e.Item2||!links.ContainsKey(e.Item1)||!links.ContainsKey(e.Item2)||links[e.Item1].Contains(e.Item2))throw new ArgumentException("Invalid topology edge");links[e.Item1].Add(e.Item2);links[e.Item2].Add(e.Item1);}
        }
        public string[] Route(string from,string to)
        {
            if(!links.ContainsKey(from)||!links.ContainsKey(to))throw new ArgumentException("Destination outside scope");var q=new Queue<string>();var previous=new Dictionary<string,string>();previous[from]=null;q.Enqueue(from);
            while(q.Count>0){var n=q.Dequeue();if(n==to){var path=new List<string>();for(var at=to;at!=null;at=previous[at])path.Add(at);path.Reverse();return path.ToArray();}foreach(var next in links[n])if(!previous.ContainsKey(next)){previous[next]=n;q.Enqueue(next);}}
            throw new InvalidOperationException("No permitted route");
        }
    }
    public sealed class ButtonRouter
    {
        private readonly Session session;private readonly Topology topology;private readonly string origin;private CompiledGraph graph;private SessionToken epoch;private long last=-1,run;
        private string[] current=new string[0];public readonly TraceBuffer Trace=new TraceBuffer();
        public ButtonRouter(Session session,Topology topology,string origin,CompiledGraph graph)
        {this.session=session??throw new ArgumentNullException(nameof(session));this.topology=topology??throw new ArgumentNullException(nameof(topology));this.origin=Guard.Id(origin);this.graph=graph??throw new ArgumentNullException(nameof(graph));if(graph.Challenge!=Challenge.C01)throw new ArgumentException("Wrong graph");epoch=session.Token;}
        public bool SetGraph(CompiledGraph next,SessionToken token)
        {
            if(next==null||next.Challenge!=Challenge.C01)throw new ArgumentException("Wrong graph");
            if(!session.Valid(token)||!epoch.Equals(token)||session.Paused)return false;
            graph=next;current=new string[0];Trace.Add(token,0,"link","HandlerAttached",graph.Linked?"linked":"unlinked");return true;
        }
        public string[] RouteSnapshot(){return (string[])current.Clone();}
        public bool Activate(string destination,long sequence,SessionToken token)
        {
            if(sequence<0)throw new ArgumentOutOfRangeException(nameof(sequence));if(!session.Valid(token)||session.Paused)return false;
            if(!epoch.Equals(token)){epoch=token;last=-1;current=new string[0];Trace.Clear();}
            if(sequence<=last)return false;last=sequence;run++;Trace.Add(token,run,"press","OnPressed","accepted");
            if(!graph.Linked){Trace.Add(token,run,"route","NoHandler","no route");return false;}
            current=topology.Route(origin,destination);Trace.Add(token,run,"selected","SelectedLocation",destination);Trace.Add(token,run,"route","ShowRoute",destination);return true;
        }
    }
    public enum GuideState { Moving,Waiting,Advance,Complete }
    public sealed class Guide
    {
        private readonly Session session;private readonly string[] points;private readonly bool wait;private SessionToken epoch;
        public GuideState State {get;private set;}public int Index {get;private set;}public readonly TraceBuffer Trace=new TraceBuffer();
        public Guide(Session session,string[] points,CompiledGraph compiled)
        {this.session=session??throw new ArgumentNullException(nameof(session));if(points==null||points.Length==0)throw new ArgumentException("Points required");this.points=(string[])points.Clone();foreach(var p in this.points)Guard.Id(p);if(compiled==null||compiled.Challenge!=Challenge.C02)throw new ArgumentException("Wrong graph");wait=compiled.Wait;Reset();}
        public void Reset(){epoch=session.Token;Index=0;State=GuideState.Moving;Trace.Clear();}
        public GuideState Step(bool markerArrived,bool guestArrived,bool tracking,SessionToken token)
        {
            if(!session.Valid(token)||!epoch.Equals(token)||session.Paused||!tracking)return State;
            switch(State){case GuideState.Moving:if(markerArrived){Trace.Add(token,1,"move","MoveToWaypoint",points[Index]);State=wait?GuideState.Waiting:GuideState.Advance;}break;
                case GuideState.Waiting:if(guestArrived){Trace.Add(token,1,"wait","WaitForGuest","arrived");State=GuideState.Advance;}break;
                case GuideState.Advance:Trace.Add(token,1,"advance","AdvanceWaypoint",points[Index]);if(Index+1>=points.Length)State=GuideState.Complete;else{Index++;State=GuideState.Moving;}break;}
            return State;
        }
        public bool ConfirmedTeleport(string point,SessionToken token)
        {
            if(!session.Valid(token)||!epoch.Equals(token)||session.Paused)return false;int i=Array.IndexOf(points,point);if(i<0||i<Index)return false;Index=i;State=wait?GuideState.Waiting:GuideState.Advance;Trace.Add(token,1,"move","TeleportResync",point);return true;
        }
    }
    public sealed class ArrivalSensor
    {
        private readonly double enter,exit,dwell;private double time;public bool Inside{get;private set;}
        public ArrivalSensor(double enter=1,double exit=1.4,double dwell=.2){Guard.NonNegative(dwell,"dwell");Guard.Finite(enter,"enter");Guard.Finite(exit,"exit");if(enter<=0||exit<=enter)throw new ArgumentException("Bad radii");this.enter=enter;this.exit=exit;this.dwell=dwell;}
        public void Reset(){time=0;Inside=false;}
        public bool Sample(double distance,double dt,bool tracking=true,bool paused=false)
        {Guard.NonNegative(distance,"distance");Guard.NonNegative(dt,"dt");if(!tracking){Reset();return false;}if(paused)return Inside;if(distance>=exit){Reset();return false;}if(Inside)return true;if(distance<=enter){time+=Math.Min(dt,.25);if(time+1e-9>=dwell)Inside=true;}else time=0;return Inside;}
    }
}
