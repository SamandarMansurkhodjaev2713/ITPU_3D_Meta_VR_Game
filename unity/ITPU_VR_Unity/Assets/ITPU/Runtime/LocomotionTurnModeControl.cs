using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DemoPressInteractable))]
    public sealed class LocomotionTurnModeControl : MonoBehaviour
    {
        private const string SnapLabel = "TURN\nSNAP 30";
        private const string SmoothLabel = "TURN\nSMOOTH 45/s";

        [SerializeField] private DemoPressInteractable interactable;
        [SerializeField] private XrLocomotionDriver locomotion;
        [SerializeField] private TextMesh label;
        private bool subscribed;

        public bool SmoothTurnEnabled => locomotion != null && locomotion.SmoothTurnEnabled;

        public void Configure(DemoPressInteractable source, XrLocomotionDriver driver, TextMesh modeLabel)
        {
            Unsubscribe();
            interactable = source;
            locomotion = driver;
            label = modeLabel;
            Subscribe();
            RefreshLabel();
        }

        private void OnEnable()
        {
            Subscribe();
            RefreshLabel();
        }

        private void OnDisable() => Unsubscribe();

        public void ResetToDefault()
        {
            locomotion?.SetSmoothTurnEnabled(false);
            RefreshLabel();
        }

        private void Toggle()
        {
            if (locomotion == null) return;
            locomotion.SetSmoothTurnEnabled(!locomotion.SmoothTurnEnabled);
            RefreshLabel();
        }

        private void Subscribe()
        {
            if (subscribed || interactable == null) return;
            interactable.Activated += Toggle;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (interactable != null) interactable.Activated -= Toggle;
            subscribed = false;
        }

        private void RefreshLabel()
        {
            if (label != null) label.text = SmoothTurnEnabled ? SmoothLabel : SnapLabel;
        }
    }
}
