using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class CoffeeInteractionController : MonoBehaviour
    {
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private DemoPressInteractable brewButton;
        [SerializeField] private CoffeeCupGrabAdapter cup;
        [SerializeField] private GameObject streamVisual;
        [SerializeField] private Transform liquidSurface;
        [SerializeField] private Renderer buttonRenderer;
        [SerializeField] private float brewSeconds = 6f;
        [SerializeField] private Vector3 emptyLiquidLocalPosition;
        [SerializeField] private Vector3 fullLiquidLocalPosition = new(0f, 0.075f, 0f);

        private CoffeeMachine machine;
        private bool subscribed;
        private CoffeeState visualState = (CoffeeState)(-1);
        private float visualFill = -1f;
        private Color initialButtonColor = Color.white;
        private MaterialPropertyBlock buttonProperties;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public CoffeeState State => Machine.State;
        public float Fill => (float)Machine.Fill;
        public bool CupPresent => Machine.CupPresent;
        public CoffeeMachine Machine
        {
            get
            {
                EnsureReady();
                return machine;
            }
        }

        public void Configure(ChallengeSessionAuthority authority, DemoPressInteractable button,
            GameObject streams, Transform liquid, Renderer feedback, Vector3 emptyLocal,
            Vector3 fullLocal)
        {
            Unsubscribe();
            sessionAuthority = authority;
            brewButton = button;
            streamVisual = streams;
            liquidSurface = liquid;
            buttonRenderer = feedback;
            emptyLiquidLocalPosition = emptyLocal;
            fullLiquidLocalPosition = fullLocal;
            EnsureReady();
            Subscribe();
            ApplyVisuals(true);
        }

        public void SetCup(CoffeeCupGrabAdapter adapter) => cup = adapter;

        private void Awake()
        {
            EnsureReady();
            ApplyVisuals(true);
        }

        private void OnEnable()
        {
            EnsureReady();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (streamVisual != null) streamVisual.SetActive(false);
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        public bool TryActivate()
        {
            EnsureReady();
            if (sessionAuthority == null || sessionAuthority.Session.Paused) return false;
            var accepted = machine.Press();
            ApplyVisuals(true);
            return accepted;
        }

        public void TickForTests(float unscaledDeltaTime) => Tick(unscaledDeltaTime);

        public void NotifyCupDocked()
        {
            EnsureReady();
            machine.Dock();
            ApplyVisuals(true);
        }

        public void NotifyCupRemoved()
        {
            EnsureReady();
            machine.Remove();
            ApplyVisuals(true);
        }

        public void ResetInteraction()
        {
            EnsureReady();
            machine.Reset();
            ApplyVisuals(true);
        }

        private void Tick(float deltaTime)
        {
            EnsureReady();
            var paused = sessionAuthority == null || sessionAuthority.Session.Paused;
            machine.Tick(Mathf.Max(0f, deltaTime), paused);
            ApplyVisuals(false);
        }

        private void EnsureReady()
        {
            if (machine == null) machine = new CoffeeMachine(Mathf.Max(0.1f, brewSeconds));
            if (sessionAuthority == null)
                sessionAuthority = FindAnyObjectByType<ChallengeSessionAuthority>(FindObjectsInactive.Include);
            if (buttonRenderer != null && visualState == (CoffeeState)(-1))
                initialButtonColor = buttonRenderer.sharedMaterial == null
                    ? Color.white
                    : buttonRenderer.sharedMaterial.color;
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            if (brewButton != null) brewButton.Activated += OnButtonActivated;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred += OnSessionReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (brewButton != null) brewButton.Activated -= OnButtonActivated;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= OnSessionReset;
            subscribed = false;
        }

        private void OnButtonActivated() => TryActivate();

        private void OnSessionReset()
        {
            ResetInteraction();
            cup?.ResetToHome();
        }

        private void ApplyVisuals(bool force)
        {
            if (machine == null) return;
            var state = machine.State;
            var fill = (float)machine.Fill;
            if (!force && state == visualState && Mathf.Abs(fill - visualFill) < 0.001f) return;
            visualState = state;
            visualFill = fill;
            var flowing = state == CoffeeState.Brewing && machine.CupPresent &&
                          sessionAuthority != null && !sessionAuthority.Session.Paused;
            if (streamVisual != null && streamVisual.activeSelf != flowing) streamVisual.SetActive(flowing);
            if (liquidSurface != null)
            {
                var local = Vector3.Lerp(emptyLiquidLocalPosition, fullLiquidLocalPosition, fill);
                if (cup != null)
                    liquidSurface.SetPositionAndRotation(cup.transform.TransformPoint(local),
                        cup.transform.rotation);
                liquidSurface.gameObject.SetActive(fill > 0.002f);
            }
            if (buttonRenderer == null) return;
            var color = state switch
            {
                CoffeeState.Idle => new Color(0.24f, 0.26f, 0.28f, 1f),
                CoffeeState.CupReady => new Color(0.16f, 0.72f, 0.42f, 1f),
                CoffeeState.Brewing => new Color(0.24f, 0.72f, 1f, 1f),
                CoffeeState.Paused => new Color(1f, 0.58f, 0.12f, 1f),
                CoffeeState.Ready => new Color(0.96f, 0.82f, 0.22f, 1f),
                _ => initialButtonColor,
            };
            buttonProperties ??= new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(buttonProperties);
            buttonProperties.SetColor(BaseColorId, color);
            buttonProperties.SetColor(ColorId, color);
            buttonRenderer.SetPropertyBlock(buttonProperties);
        }
    }
}
