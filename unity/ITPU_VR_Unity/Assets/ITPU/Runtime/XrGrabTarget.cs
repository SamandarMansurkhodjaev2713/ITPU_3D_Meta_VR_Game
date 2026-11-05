using UnityEngine;

namespace ITPU.XR
{
    public abstract class XrGrabTarget : MonoBehaviour
    {
        public abstract string Owner { get; }
        public abstract bool Held { get; }
        public virtual bool TryPrimaryActivate(string source) => false;
        public abstract bool TryBeginGrab(string source, Pose gripPose);
        public abstract bool UpdateGrab(string source, Pose gripPose);
        public abstract bool TryRelease(string source, bool intentional, Vector3 linearVelocity,
            Vector3 angularVelocity);
        public abstract bool MarkTrackingLost(string source);
    }
}
