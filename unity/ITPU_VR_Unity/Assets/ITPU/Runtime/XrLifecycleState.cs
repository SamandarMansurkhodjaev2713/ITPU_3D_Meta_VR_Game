using System;
using UnityEngine;
using UnityEngine.XR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class XrLifecycleState : MonoBehaviour
    {
        [SerializeField] private bool focused = true;
        [SerializeField] private bool paused;
        [SerializeField] private bool headTracked;
        [SerializeField] private bool moveControllerTracked;
        [SerializeField] private bool turnControllerTracked;
        [SerializeField] private bool pollDevices = true;

        public bool Focused => focused;
        public bool Paused => paused;
        public bool HeadTracked => headTracked;
        public bool MoveControllerTracked => moveControllerTracked;
        public bool TurnControllerTracked => turnControllerTracked;
        public bool ContextSafe => focused && !paused && headTracked;
        public event Action Changed;

        public void SetFocus(bool value) => Set(ref focused, value);
        public void SetPaused(bool value) => Set(ref paused, value);

        public void SetTracking(bool head, bool moveController, bool turnController)
        {
            var changed = headTracked != head || moveControllerTracked != moveController ||
                          turnControllerTracked != turnController;
            headTracked = head;
            moveControllerTracked = moveController;
            turnControllerTracked = turnController;
            if (changed) Changed?.Invoke();
        }

        public void SetDevicePolling(bool enabled) => pollDevices = enabled;

        private void Update()
        {
            if (!pollDevices) return;
            SetTracking(IsTracked(XRNode.Head), IsTracked(XRNode.LeftHand), IsTracked(XRNode.RightHand));
        }

        private static bool IsTracked(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && tracked;
        }

        private void OnApplicationFocus(bool value) => SetFocus(value);
        private void OnApplicationPause(bool value) => SetPaused(value);

        private void Set(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            Changed?.Invoke();
        }
    }
}
