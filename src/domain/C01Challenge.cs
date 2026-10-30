using System;

namespace ITPU.Domain
{
    public enum C01Stage { MissingHandler, HandlerLinked, FirstRoute, SecondRoute, Verified }

    public readonly struct C01PressResult
    {
        public readonly bool Accepted,NoHandler,Completed;
        public readonly string Destination;
        public readonly string[] Route;
        public readonly C01Stage Stage;
        public C01PressResult(bool accepted,bool noHandler,bool completed,string destination,
            string[] route,C01Stage stage)
        {Accepted=accepted;NoHandler=noHandler;Completed=completed;Destination=destination;Route=route??new string[0];Stage=stage;}
    }

    // Bounded C01 state machine. It composes the allow-listed graph/router and owns
    // actual A-B-A acceptance; presentation may only display this state.
    public sealed class C01RouteChallenge
    {
        private readonly Session session;
        private readonly Topology topology;
        private readonly string origin,firstDestination,secondDestination;
        private SessionToken epoch;
        private ButtonRouter router;
        private int progress;

        public bool HandlerLinked {get;private set;}
        public bool Completed {get;private set;}
        public string SelectedDestination {get;private set;}
        public C01Stage Stage {get;private set;}

        public C01RouteChallenge(Session session,Topology topology,string origin,
            string firstDestination,string secondDestination)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            this.topology=topology??throw new ArgumentNullException(nameof(topology));
            this.origin=Guard.Id(origin);this.firstDestination=Guard.Id(firstDestination);
            this.secondDestination=Guard.Id(secondDestination);
            if(this.firstDestination==this.secondDestination)throw new ArgumentException("Distinct destinations required");
            topology.Route(this.origin,this.firstDestination);topology.Route(this.origin,this.secondDestination);
            ResetForCurrentSession();
        }

        public TraceEntry[] TraceSnapshot(){return router.Trace.Snapshot();}
        public string[] RouteSnapshot(){return router.RouteSnapshot();}

        public bool SelectDestination(string destination,SessionToken token)
        {
            if(!Active(token))return false;destination=Guard.Id(destination);
            if(destination!=firstDestination&&destination!=secondDestination)return false;
            SelectedDestination=destination;return true;
        }

        public bool AttachHandler(SessionToken token)
        {
            if(!Active(token)||HandlerLinked)return false;
            session.InvalidateFrom(Challenge.C01);
            if(!router.SetGraph(GraphCompiler.Compile(GraphCompiler.Template(Challenge.C01,true)),token))return false;
            HandlerLinked=true;Completed=false;progress=0;Stage=C01Stage.HandlerLinked;return true;
        }

        public C01PressResult Press(long sequence,SessionToken token)
        {
            if(!Active(token))return Snapshot(false,false);
            bool accepted=router.Activate(SelectedDestination,sequence,token);
            if(accepted&&!Completed)ObserveAcceptedDestination(SelectedDestination,token);
            return Snapshot(accepted,!HandlerLinked);
        }

        public void ResetForCurrentSession()
        {
            epoch=session.Token;router=new ButtonRouter(session,topology,origin,
                GraphCompiler.Compile(GraphCompiler.Template(Challenge.C01,false)));
            HandlerLinked=false;Completed=false;progress=0;SelectedDestination=firstDestination;
            Stage=C01Stage.MissingHandler;session.InvalidateFrom(Challenge.C01);
        }

        private bool Active(SessionToken token){return token.Equals(epoch)&&session.Valid(token)&&!session.Paused;}

        private void ObserveAcceptedDestination(string destination,SessionToken token)
        {
            string expected=progress==1?secondDestination:firstDestination;
            if(destination==expected)progress++;
            else progress=destination==firstDestination?1:0;
            if(progress>=3)
            {
                Completed=session.Complete(Challenge.C01,token);
                Stage=Completed?C01Stage.Verified:C01Stage.SecondRoute;
            }
            else Stage=progress==2?C01Stage.SecondRoute:progress==1?C01Stage.FirstRoute:C01Stage.HandlerLinked;
        }

        private C01PressResult Snapshot(bool accepted,bool noHandler)
        {return new C01PressResult(accepted,noHandler,Completed,SelectedDestination,RouteSnapshot(),Stage);}
    }
}
