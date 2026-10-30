using System;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DemoPressInteractable))]
    public sealed class SessionResetButton : MonoBehaviour
    {
        [SerializeField] private DemoPressInteractable interactable;
        [SerializeField] private VerticalSliceReset reset;
        private bool subscribed;
        private Func<double> clock;

        public void Configure(DemoPressInteractable source, VerticalSliceReset sessionReset)
        {
            Unsubscribe();
            interactable = source;
            reset = sessionReset;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            interactable ??= GetComponent<DemoPressInteractable>();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (subscribed || interactable == null) return;
            interactable.Activated += ActivateReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (interactable != null) interactable.Activated -= ActivateReset;
            subscribed = false;
        }

        private void ActivateReset()
        {
            reset?.ResetSession(clock?.Invoke() ?? Time.unscaledTimeAsDouble);
        }

        public void SetClockProviderForTests(Func<double> provider) => clock = provider;
    }
}
