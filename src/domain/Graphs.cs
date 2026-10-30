using System;
using System.Collections.Generic;
using System.Linq;
namespace ITPU.Domain
{
    public readonly struct Edge
    {
        public readonly string From,Output,To,Input;
        public Edge(string from,string output,string to,string input){From=Guard.Id(from);Output=Guard.Id(output);To=Guard.Id(to);Input=Guard.Id(input);}
        internal string Key {get{return From+"|"+Output+"|"+To+"|"+Input;}}
    }
    public readonly struct PermissionFlags
    {
        public readonly bool GuestView,GuestEdit,TeamEdit;
        public PermissionFlags(bool guestView,bool guestEdit,bool teamEdit){GuestView=guestView;GuestEdit=guestEdit;TeamEdit=teamEdit;}
        public static PermissionFlags Correct {get{return new PermissionFlags(true,false,true);}}
    }
    public sealed class GraphDefinition
    {
        public readonly Challenge Challenge;public readonly Dictionary<string,string> Nodes;public readonly List<Edge> Edges;public PermissionFlags Permissions;
        public GraphDefinition(Challenge challenge,Dictionary<string,string> nodes,List<Edge> edges,PermissionFlags permissions)
        {Challenge=challenge;Nodes=nodes??throw new ArgumentNullException(nameof(nodes));Edges=edges??throw new ArgumentNullException(nameof(edges));Permissions=permissions;}
    }
    // A sealed immutable instruction program. Never accepts executable strings or arbitrary nodes.
    public sealed class CompiledGraph
    {
        public Challenge Challenge {get;}public bool Baseline {get;}public bool Linked {get;}public bool Wait {get;}public bool RequiresPermission {get;}public PermissionFlags Permissions {get;}
        private readonly Edge[] edges;
        internal CompiledGraph(Challenge c,bool baseline,PermissionFlags p,Edge[] edges)
        {Challenge=c;Baseline=baseline;Linked=c==ITPU.Domain.Challenge.C01&&!baseline;Wait=c==ITPU.Domain.Challenge.C02&&!baseline;RequiresPermission=c==ITPU.Domain.Challenge.C03&&!baseline;Permissions=p;this.edges=(Edge[])edges.Clone();}
        public Edge[] EdgesSnapshot(){return (Edge[])edges.Clone();}
    }
    public static class GraphCompiler
    {
        public static GraphDefinition Template(Challenge c,bool correct=true)
        {
            var n=new Dictionary<string,string>();var e=new List<Edge>();
            switch(c)
            {
                case Challenge.C01:n.Add("press","OnPressed");n.Add("selected","SelectedLocation");n.Add("route","ShowRoute");e.Add(new Edge("selected","location","route","location"));if(correct)e.Add(new Edge("press","event","route","trigger"));break;
                case Challenge.C02:n.Add("move","MoveToWaypoint");n.Add("advance","AdvanceWaypoint");if(correct){n.Add("wait","WaitForGuest");e.Add(new Edge("move","flow","wait","flow"));e.Add(new Edge("wait","flow","advance","flow"));}else e.Add(new Edge("move","flow","advance","flow"));break;
                case Challenge.C03:n.Add("receive","ReceiveDemoRequest");n.Add("execute","ExecuteDemoOperation");if(correct){n.Add("check","RequirePermission");n.Add("reject","RejectDemoOperation");e.Add(new Edge("receive","request","check","request"));e.Add(new Edge("check","allow","execute","decision"));e.Add(new Edge("check","deny","reject","decision"));}else e.Add(new Edge("receive","request","execute","request"));break;
                default:throw new ArgumentException("Unknown challenge");
            }
            return new GraphDefinition(c,n,e,PermissionFlags.Correct);
        }
        private static bool Matches(GraphDefinition a,GraphDefinition b)
        {
            if(a.Nodes.Count!=b.Nodes.Count||a.Edges.Count!=b.Edges.Count)return false;
            foreach(var pair in a.Nodes){string value;if(!b.Nodes.TryGetValue(pair.Key,out value)||value!=pair.Value)return false;}
            var keys=new HashSet<string>(a.Edges.Select(x=>x.Key),StringComparer.Ordinal);
            return keys.Count==a.Edges.Count && keys.SetEquals(b.Edges.Select(x=>x.Key));
        }
        public static CompiledGraph Compile(GraphDefinition definition)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));if(definition.Nodes.Count>12||definition.Edges.Count>16)throw new ArgumentException("Graph budget exceeded");
            // Only the exact current exercise programs are valid; arbitrary loops/dangling ports are rejected.
            var baseline=Template(definition.Challenge,false);bool isBaseline=Matches(definition,baseline);
            if(!isBaseline&&!Matches(definition,Template(definition.Challenge,true)))throw new ArgumentException("Unsupported graph, duplicate edges, cycle or wrong port");
            return new CompiledGraph(definition.Challenge,isBaseline,definition.Permissions,definition.Edges.ToArray());
        }
    }
}
