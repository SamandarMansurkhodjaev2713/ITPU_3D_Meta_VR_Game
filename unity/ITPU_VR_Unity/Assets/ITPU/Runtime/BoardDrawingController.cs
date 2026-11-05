using System;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    public enum BoardTool
    {
        Marker,
        Eraser,
    }

    [DisallowMultipleComponent]
    public sealed class BoardDrawingController : MonoBehaviour
    {
        public const int RasterWidth = 256;
        public const int RasterHeight = 128;
        public const int UndoCapacity = 12;
        public const float MaximumUploadHz = 20f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color32[] Palette =
        {
            new Color32(236, 238, 232, 255),
            new Color32(20, 28, 38, 255),
            new Color32(38, 112, 190, 255),
            new Color32(196, 55, 55, 255),
            new Color32(50, 142, 78, 255),
            new Color32(126, 73, 170, 255),
        };

        [SerializeField] private ChallengeSessionAuthority authority;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private BoxCollider surfaceCollider;
        [SerializeField, Range(1f, MaximumUploadHz)] private float uploadHz = MaximumUploadHz;
        [SerializeField, Range(0.5f, 8f)] private float markerRadiusPixels = 2.25f;
        [SerializeField, Range(2f, 32f)] private float eraserRadiusPixels = 9f;

        private MaterialPropertyBlock propertyBlock;
        private DrawingSurface model;
        private Texture2D texture;
        private byte[] pixelIndices;
        private Color32[] colorBuffer;
        private string owner = "";
        private Vector2 previousUv;
        private int uploadedRevision = -1;
        private float nextUploadAt;
        private bool subscribed;

        public BoardTool Tool { get; private set; } = BoardTool.Marker;
        public byte MarkerColorIndex { get; private set; } = 1;
        public string Owner => owner;
        public bool StrokeActive => owner.Length != 0;
        public int NonEmptyPixels => model?.NonEmptyPixels() ?? 0;
        public int Revision => model?.Revision ?? 0;
        public Texture2D RasterTexture => texture;
        public BoxCollider SurfaceCollider => surfaceCollider;

        public void Configure(ChallengeSessionAuthority sessionAuthority, InteractionPriority interactionPriority,
            Renderer renderer, BoxCollider collider)
        {
            Unsubscribe();
            authority = sessionAuthority;
            priority = interactionPriority;
            surfaceRenderer = renderer;
            surfaceCollider = collider;
            EnsureReady();
            Subscribe();
            UploadNow();
        }

        private void Awake() => EnsureReady();

        private void OnEnable()
        {
            EnsureReady();
            Subscribe();
        }

        private void OnDisable()
        {
            CancelActiveStroke();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            CancelActiveStroke();
            Unsubscribe();
            if (texture == null) return;
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
            texture = null;
        }

        private void Update()
        {
            if (model == null || uploadedRevision == model.Revision || Time.unscaledTime < nextUploadAt) return;
            UploadNow();
        }

        public bool TryBeginStroke(string source, Ray ray)
        {
            EnsureReady();
            if (!CanEdit() || string.IsNullOrWhiteSpace(source) || StrokeActive || !TryMapRay(ray, out var uv))
                return false;
            if (!model.Begin()) return false;
            owner = source;
            previousUv = uv;
            priority?.SetPrecisionBusy(true);
            DrawSegment(uv, uv);
            return true;
        }

        public bool UpdateStroke(string source, Ray ray)
        {
            if (!CanEdit() || source != owner || !TryMapRay(ray, out var uv)) return false;
            DrawSegment(previousUv, uv);
            previousUv = uv;
            return true;
        }

        public bool EndStroke(string source)
        {
            if (source != owner) return false;
            var changed = model.End();
            ReleaseOwner();
            return changed;
        }

        public bool CancelStroke(string source)
        {
            if (source != owner) return false;
            var cancelled = model.Cancel();
            ReleaseOwner();
            if (cancelled) UploadNow();
            return cancelled;
        }

        public bool SelectTool(BoardTool tool)
        {
            if (!Enum.IsDefined(typeof(BoardTool), tool) || StrokeActive || !CanEdit()) return false;
            Tool = tool;
            return true;
        }

        public bool Undo()
        {
            if (StrokeActive || !CanEdit() || !model.Undo()) return false;
            UploadNow();
            return true;
        }

        public bool CycleMarkerColor()
        {
            if (StrokeActive || !CanEdit()) return false;
            MarkerColorIndex = (byte)(MarkerColorIndex >= 5 ? 1 : MarkerColorIndex + 1);
            Tool = BoardTool.Marker;
            return true;
        }

        public void ResetBoard()
        {
            CancelActiveStroke();
            EnsureReady();
            model.Reset();
            Tool = BoardTool.Marker;
            MarkerColorIndex = 1;
            UploadNow();
        }

        public byte[] CaptureFinalCardSnapshot()
        {
            EnsureReady();
            return model.Snapshot();
        }

        public void UploadNowForTests() => UploadNow();

        private void EnsureReady()
        {
            model ??= new DrawingSurface(RasterWidth, RasterHeight, UndoCapacity);
            pixelIndices ??= new byte[RasterWidth * RasterHeight];
            colorBuffer ??= new Color32[RasterWidth * RasterHeight];
            propertyBlock ??= new MaterialPropertyBlock();
            if (texture == null)
            {
                texture = new Texture2D(RasterWidth, RasterHeight, TextureFormat.RGBA32, false, false)
                {
                    name = "ITPU_BoardRaster_Runtime",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                uploadedRevision = -1;
            }
            surfaceRenderer ??= GetComponent<Renderer>();
            surfaceCollider ??= GetComponent<BoxCollider>();
        }

        private bool CanEdit() => authority == null || !authority.Session.Paused;

        private void DrawSegment(Vector2 from, Vector2 to)
        {
            var color = Tool == BoardTool.Eraser ? (byte)0 : MarkerColorIndex;
            var radius = Tool == BoardTool.Eraser ? eraserRadiusPixels : markerRadiusPixels;
            model.Line(from.x, from.y, to.x, to.y, radius, color);
        }

        private bool TryMapRay(Ray ray, out Vector2 uv)
        {
            uv = default;
            if (surfaceCollider == null || ray.direction.sqrMagnitude < 0.9f ||
                !surfaceCollider.Raycast(ray, out var hit, 8f)) return false;
            var local = surfaceCollider.transform.InverseTransformPoint(hit.point) - surfaceCollider.center;
            var size = surfaceCollider.size;
            if (Mathf.Abs(size.x) < 0.0001f || Mathf.Abs(size.y) < 0.0001f) return false;
            uv = new Vector2(Mathf.Clamp01(local.x / size.x + 0.5f),
                Mathf.Clamp01(local.y / size.y + 0.5f));
            return true;
        }

        private void UploadNow()
        {
            EnsureReady();
            model.CopyPixels(pixelIndices);
            for (var index = 0; index < pixelIndices.Length; index++)
                colorBuffer[index] = Palette[pixelIndices[index]];
            texture.SetPixelData(colorBuffer, 0);
            texture.Apply(false, false);
            uploadedRevision = model.Revision;
            nextUploadAt = Time.unscaledTime + 1f / Mathf.Clamp(uploadHz, 1f, MaximumUploadHz);
            if (surfaceRenderer == null) return;
            surfaceRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMapId, texture);
            propertyBlock.SetColor(BaseColorId, Color.white);
            surfaceRenderer.SetPropertyBlock(propertyBlock);
        }

        private void CancelActiveStroke()
        {
            if (!StrokeActive) return;
            model?.Cancel();
            ReleaseOwner();
        }

        private void ReleaseOwner()
        {
            owner = "";
            priority?.SetPrecisionBusy(false);
        }

        private void Subscribe()
        {
            if (subscribed || authority == null || !isActiveAndEnabled) return;
            authority.ResetOccurred += ResetBoard;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (authority != null) authority.ResetOccurred -= ResetBoard;
            subscribed = false;
        }
    }
}
