using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class StableId : MonoBehaviour
    {
        [SerializeField] private string value = "";
        public string Value => value;

        public void Set(string newValue)
        {
            if (string.IsNullOrWhiteSpace(newValue))
                throw new System.ArgumentException("Stable id is required", nameof(newValue));
            value = newValue;
        }
    }
}
