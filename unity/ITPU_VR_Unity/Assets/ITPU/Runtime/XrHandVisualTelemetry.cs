using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class XrHandVisualTelemetry : MonoBehaviour
    {
        [SerializeField] private XRHandMeshController leftVisual;
        [SerializeField] private XRHandMeshController rightVisual;

        private readonly List<XRHandSubsystem> subsystems = new();
        private bool stateObserved;
        private bool lastRunning;
        private bool lastLeftTracked;
        private bool lastRightTracked;
        private HandTrackingDataSource lastLeftSource;
        private HandTrackingDataSource lastRightSource;
        private bool lastLeftSourceKnown;
        private bool lastRightSourceKnown;

        public XRHandMeshController LeftVisual => leftVisual;
        public XRHandMeshController RightVisual => rightVisual;

        public void Configure(XRHandMeshController left, XRHandMeshController right)
        {
            leftVisual = left;
            rightVisual = right;
            stateObserved = false;
        }

        private void LateUpdate()
        {
            subsystems.Clear();
            SubsystemManager.GetSubsystems(subsystems);
            XRHandSubsystem running = null;
            foreach (var subsystem in subsystems)
            {
                if (!subsystem.running) continue;
                running = subsystem;
                break;
            }

            if (running == null)
            {
                Observe(false, false, false, false, default, false, default);
                return;
            }

            var leftSourceKnown = running.TryGetExtendedData(
                Handedness.Left, out HandTrackingDataSource leftSource);
            var rightSourceKnown = running.TryGetExtendedData(
                Handedness.Right, out HandTrackingDataSource rightSource);
            Observe(true, running.leftHand.isTracked, running.rightHand.isTracked,
                leftSourceKnown, leftSource, rightSourceKnown, rightSource);
        }

        public void Observe(bool running, bool leftTracked, bool rightTracked,
            bool leftSourceKnown, HandTrackingDataSource leftSource,
            bool rightSourceKnown, HandTrackingDataSource rightSource)
        {
            if (stateObserved && lastRunning == running && lastLeftTracked == leftTracked &&
                lastRightTracked == rightTracked && lastLeftSourceKnown == leftSourceKnown &&
                lastRightSourceKnown == rightSourceKnown &&
                (!leftSourceKnown || lastLeftSource == leftSource) &&
                (!rightSourceKnown || lastRightSource == rightSource))
                return;

            stateObserved = true;
            lastRunning = running;
            lastLeftTracked = leftTracked;
            lastRightTracked = rightTracked;
            lastLeftSourceKnown = leftSourceKnown;
            lastRightSourceKnown = rightSourceKnown;
            lastLeftSource = leftSource;
            lastRightSource = rightSource;

            var leftRenderer = leftVisual?.handMeshRenderer;
            var rightRenderer = rightVisual?.handMeshRenderer;
            Debug.Log("ITPU_XR_HAND_VISUAL " +
                      $"running={running} leftTracked={leftTracked} " +
                      $"leftSource={(leftSourceKnown ? leftSource.ToString() : "DefaultOrUnknown")} " +
                      $"leftRenderer={(leftRenderer != null && leftRenderer.enabled)} " +
                      $"rightTracked={rightTracked} " +
                      $"rightSource={(rightSourceKnown ? rightSource.ToString() : "DefaultOrUnknown")} " +
                      $"rightRenderer={(rightRenderer != null && rightRenderer.enabled)}");
        }
    }
}
