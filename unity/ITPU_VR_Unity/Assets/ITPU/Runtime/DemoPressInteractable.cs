using System;
using UnityEngine;
using UnityEngine.Events;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class DemoPressInteractable : MonoBehaviour
    {
        [SerializeField] private UnityEvent activated = new();
        [SerializeField] private Renderer feedbackRenderer;
        private Color initialColor = Color.white;
        private int lastActivationFrame = -1;

        public int ActivationCount { get; private set; }
        public string LastSource { get; private set; } = "";
        public event Action Activated;

        private void Awake()
        {
            if (feedbackRenderer != null) initialColor = feedbackRenderer.material.color;
        }

        public bool TryActivate(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (lastActivationFrame == Time.frameCount) return false;
            lastActivationFrame = Time.frameCount;
            ActivationCount++;
            LastSource = source;
            if (feedbackRenderer != null) feedbackRenderer.material.color = new Color(0.15f, 0.78f, 0.72f, 1f);
            activated.Invoke();
            Activated?.Invoke();
            return true;
        }

        public void ResetState()
        {
            ActivationCount = 0;
            LastSource = "";
            lastActivationFrame = -1;
            if (feedbackRenderer != null) feedbackRenderer.material.color = initialColor;
        }

        public void Configure(Renderer renderer) => feedbackRenderer = renderer;
    }
}
