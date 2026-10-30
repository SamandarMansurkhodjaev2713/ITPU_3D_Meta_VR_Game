using UnityEngine;
using UnityEngine.Events;
namespace ITPU.Unity
{
    // All excluded-room doors remain closed. This component never opens a door.
    public sealed class ClosedFacade : MonoBehaviour
    {
        [SerializeField] private UnityEvent onUnavailable;
        [SerializeField] private float cooldownSeconds=3f;
        private float last=-100f;
        public void ActivateHandle(){if(Time.unscaledTime-last<cooldownSeconds)return;last=Time.unscaledTime;onUnavailable?.Invoke();}
    }
}
