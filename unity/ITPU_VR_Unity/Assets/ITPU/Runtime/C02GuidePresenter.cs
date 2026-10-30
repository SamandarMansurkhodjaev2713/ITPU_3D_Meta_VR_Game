using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class C02GuidePresenter : MonoBehaviour
    {
        [SerializeField] private Transform guideMarker;
        [SerializeField] private Transform guestMarker;
        [SerializeField] private Transform startAnchor;
        [SerializeField] private Transform nextAnchor;

        public Vector3 GuidePosition => guideMarker == null ? Vector3.zero : guideMarker.position;
        public bool GuestVisible => guestMarker != null && guestMarker.gameObject.activeSelf;
        public int PresentationRevision { get; private set; }

        public void Configure(Transform guide, Transform guest, Transform start, Transform next)
        {
            guideMarker = guide;
            guestMarker = guest;
            startAnchor = start;
            nextAnchor = next;
            ResetPresentation();
        }

        public void Present(ITPU.Domain.C02Stage stage)
        {
            if (guideMarker == null || guestMarker == null || startAnchor == null || nextAnchor == null) return;
            guideMarker.position = stage == ITPU.Domain.C02Stage.BrokenGuideLeft ||
                                   stage == ITPU.Domain.C02Stage.Verified
                ? nextAnchor.position
                : startAnchor.position;
            guestMarker.position = startAnchor.position + new Vector3(0f, -0.16f, 0f);
            guestMarker.gameObject.SetActive(stage == ITPU.Domain.C02Stage.GuestConfirmed ||
                                             stage == ITPU.Domain.C02Stage.Verified);
            PresentationRevision++;
        }

        public void ResetPresentation()
        {
            if (guideMarker != null && startAnchor != null) guideMarker.position = startAnchor.position;
            if (guestMarker != null) guestMarker.gameObject.SetActive(false);
            PresentationRevision = 0;
        }
    }
}
