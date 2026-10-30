using System;
using System.Collections.Generic;
using System.Diagnostics;
namespace ITPU.Domain
{
    public static class Guard
    {
        public static double Finite(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); return value; }
        public static double NonNegative(double value, string name)
        { Finite(value,name); if(value<0) throw new ArgumentOutOfRangeException(name);return value; }
        public static double Clamp(double value, double low, double high) { return Math.Min(high,Math.Max(low,value)); }
        public static string Id(string value)
        {
            if(string.IsNullOrEmpty(value)||value.Length>80)throw new ArgumentException("Invalid identifier");
            foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'||c=='.'))throw new ArgumentException("Invalid identifier");
            return value;
        }
    }
    public readonly struct Vec3
    {
        public readonly double X,Y,Z;
        public Vec3(double x,double y,double z){X=Guard.Finite(x,"x");Y=Guard.Finite(y,"y");Z=Guard.Finite(z,"z");}
    }
    public readonly struct SessionToken : IEquatable<SessionToken>
    {
        public readonly Guid SessionId; public readonly long Epoch;
        public SessionToken(Guid sessionId,long epoch){SessionId=sessionId;Epoch=epoch;}
        public bool Equals(SessionToken other){return SessionId==other.SessionId&&Epoch==other.Epoch;}
        public override bool Equals(object other){return other is SessionToken && Equals((SessionToken)other);}
        public override int GetHashCode(){return SessionId.GetHashCode()^Epoch.GetHashCode();}
    }
    public readonly struct TraceEntry
    {
        public readonly long Sequence,RunId,RelativeTimeMs;public readonly SessionToken Token;public readonly string Node,Event,Outcome;
        public TraceEntry(long sequence,long run,long relativeTimeMs,SessionToken token,string node,string evt,string outcome)
        {Sequence=sequence;RunId=run;RelativeTimeMs=relativeTimeMs;Token=token;Node=node;Event=evt;Outcome=outcome;}
    }
    public sealed class TraceBuffer
    {
        private readonly TraceEntry[] entries;private readonly Func<long> clock;private int next,count;private long sequence,origin,lastTime;
        public TraceBuffer(int capacity=512,Func<long> clockMilliseconds=null)
        {if(capacity<1||capacity>4096)throw new ArgumentOutOfRangeException(nameof(capacity));entries=new TraceEntry[capacity];clock=clockMilliseconds??MonotonicMilliseconds;origin=clock();}
        public int Count {get{return count;}}
        public void Add(SessionToken token,long run,string node,string evt,string outcome)
        {
            Guard.Id(node);Guard.Id(evt);if(outcome==null)outcome="";if(outcome.Length>100)outcome=outcome.Substring(0,100);
            lastTime=Math.Max(lastTime,Math.Max(0,clock()-origin));entries[next]=new TraceEntry(++sequence,run,lastTime,token,node,evt,outcome);next=(next+1)%entries.Length;count=Math.Min(count+1,entries.Length);
        }
        public TraceEntry[] Snapshot(){var r=new TraceEntry[count];int start=(next-count+entries.Length)%entries.Length;for(int i=0;i<count;i++)r[i]=entries[(start+i)%entries.Length];return r;}
        public void Clear(){Array.Clear(entries,0,entries.Length);count=next=0;sequence=lastTime=0;origin=clock();}
        private static long MonotonicMilliseconds(){return (long)(Stopwatch.GetTimestamp()*(1000.0/Stopwatch.Frequency));}
    }
    public enum Challenge { C01,C02,C03 }
    public sealed class Session
    {
        private Guid id=Guid.NewGuid();private long epoch=1;private readonly HashSet<string> pauses=new HashSet<string>();private readonly HashSet<Challenge> completed=new HashSet<Challenge>();
        public SessionToken Token {get{return new SessionToken(id,epoch);}}
        public bool Paused {get{return pauses.Count>0;}}
        public bool Assisted {get;private set;}
        public bool Ready {get{return completed.Contains(Challenge.C01)&&completed.Contains(Challenge.C02)&&completed.Contains(Challenge.C03);}}
        public bool IsCompleted(Challenge challenge)
        {if(!Enum.IsDefined(typeof(Challenge),challenge))throw new ArgumentException("Unknown challenge");return completed.Contains(challenge);}
        public bool Valid(SessionToken token){return token.Equals(Token);}
        public void SetPause(string reason,bool active){Guard.Id(reason);if(active)pauses.Add(reason);else pauses.Remove(reason);}
        public bool Complete(Challenge challenge,SessionToken token,bool assisted=false)
        {if(!Enum.IsDefined(typeof(Challenge),challenge))throw new ArgumentException("Unknown challenge");if(!Valid(token)||Paused)return false;if(challenge==Challenge.C02&&!completed.Contains(Challenge.C01))return false;if(challenge==Challenge.C03&&(!completed.Contains(Challenge.C01)||!completed.Contains(Challenge.C02)))return false;completed.Add(challenge);Assisted|=assisted;return true;}
        public void InvalidateFrom(Challenge challenge)
        {if(!Enum.IsDefined(typeof(Challenge),challenge))throw new ArgumentException("Unknown challenge");for(int i=(int)challenge;i<=2;i++)completed.Remove((Challenge)i);}
        public void Reset(){checked{epoch++;}id=Guid.NewGuid();pauses.Clear();completed.Clear();Assisted=false;}
    }
}
