using UnityEngine;

namespace ITPU.XR
{
    public enum PaperBasketSensorKind
    {
        Mouth,
        Inside,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PaperBasketSensor : MonoBehaviour
    {
        [SerializeField] private PaperBasketSensorKind kind;

        public PaperBasketSensorKind Kind => kind;

        public void Configure(PaperBasketSensorKind sensorKind)
        {
            kind = sensorKind;
            var box = GetComponent<BoxCollider>();
            if (box != null) box.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var paper = FindPaper(other);
            if (paper == null) return;
            if (kind == PaperBasketSensorKind.Mouth)
                paper.NotifyBasketMouth(paper.Body != null && paper.Body.linearVelocity.y < -0.05f);
            else paper.TickBasketInside(true, 0f);
        }

        private void OnTriggerStay(Collider other)
        {
            if (kind != PaperBasketSensorKind.Inside) return;
            FindPaper(other)?.TickBasketInside(true, Time.fixedDeltaTime);
        }

        private void OnTriggerExit(Collider other)
        {
            if (kind != PaperBasketSensorKind.Inside) return;
            FindPaper(other)?.TickBasketInside(false, 0f);
        }

        private static PaperPropGrabAdapter FindPaper(Collider other)
        {
            if (other == null) return null;
            if (other.attachedRigidbody != null)
                return other.attachedRigidbody.GetComponent<PaperPropGrabAdapter>();
            return other.GetComponentInParent<PaperPropGrabAdapter>();
        }
    }
}
