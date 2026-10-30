using UnityEngine;

namespace ITPU.XR
{
    public enum XrAimTargetKind
    {
        None,
        Invalid,
        Teleport,
        Interactable
    }

    [DisallowMultipleComponent]
    public sealed class XrPointerVisual : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private Transform hitMarker;
        [SerializeField] private Renderer hitMarkerRenderer;
        [SerializeField, Min(0f)] private float startOffset = 0.035f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.006f;
        [SerializeField] private Color neutralColor = new(0.22f, 0.82f, 1f, 0.85f);
        [SerializeField] private Color invalidColor = new(1f, 0.48f, 0.24f, 0.9f);
        [SerializeField] private Color teleportColor = new(0.18f, 0.78f, 1f, 1f);
        [SerializeField] private Color interactableColor = new(0.2f, 1f, 0.55f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock propertyBlock;

        public LineRenderer Line => line;
        public Transform HitMarker => hitMarker;
        public Renderer HitMarkerRenderer => hitMarkerRenderer;
        public bool LineVisible => line != null && line.enabled;
        public bool MarkerVisible => hitMarker != null && hitMarker.gameObject.activeSelf;
        public XrAimTargetKind LastTargetKind { get; private set; }
        public Vector3 LastEndPoint { get; private set; }

        public void Configure(LineRenderer aimLine, Transform marker, Renderer markerRenderer)
        {
            line = aimLine;
            hitMarker = marker;
            hitMarkerRenderer = markerRenderer;
            Hide();
        }

        private void Awake()
        {
            line ??= GetComponentInChildren<LineRenderer>(true);
            if (hitMarker == null)
            {
                var candidate = transform.Find("Aim Hit");
                if (candidate != null) hitMarker = candidate;
            }
            hitMarkerRenderer ??= hitMarker != null ? hitMarker.GetComponent<Renderer>() : null;
            Hide();
        }

        private void OnDisable() => Hide();

        public void Present(bool active, Ray ray, float maxDistance, bool hasHit,
            Vector3 hitPoint, Vector3 hitNormal, XrAimTargetKind targetKind)
        {
            if (!active || line == null || ray.direction.sqrMagnitude < 0.9f || maxDistance <= 0f)
            {
                Hide();
                return;
            }

            var direction = ray.direction.normalized;
            var start = ray.origin + direction * startOffset;
            var end = hasHit ? hitPoint : ray.origin + direction * maxDistance;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.enabled = true;

            LastEndPoint = end;
            LastTargetKind = hasHit ? targetKind : XrAimTargetKind.None;
            var color = ColorFor(LastTargetKind);
            ApplyColor(line, color);

            if (hitMarker == null) return;
            hitMarker.gameObject.SetActive(hasHit);
            if (!hasHit) return;

            var normal = hitNormal.sqrMagnitude < 0.5f ? -direction : hitNormal.normalized;
            hitMarker.SetPositionAndRotation(hitPoint + normal * surfaceOffset,
                Quaternion.FromToRotation(Vector3.forward, normal));
            var distance = Vector3.Distance(ray.origin, hitPoint);
            var diameter = Mathf.Clamp(distance * 0.012f, 0.018f, 0.038f);
            hitMarker.localScale = Vector3.one * diameter;
            ApplyColor(hitMarkerRenderer, color);
        }

        public void Hide()
        {
            if (line != null) line.enabled = false;
            if (hitMarker != null) hitMarker.gameObject.SetActive(false);
            LastTargetKind = XrAimTargetKind.None;
            LastEndPoint = default;
        }

        private Color ColorFor(XrAimTargetKind kind) => kind switch
        {
            XrAimTargetKind.Interactable => interactableColor,
            XrAimTargetKind.Teleport => teleportColor,
            XrAimTargetKind.Invalid => invalidColor,
            _ => neutralColor
        };

        private void ApplyColor(Renderer target, Color color)
        {
            if (target == null) return;
            propertyBlock ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }
    }
}
