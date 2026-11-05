using System;
using System.Collections.Generic;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class InteractionPriority : MonoBehaviour
    {
        private bool uiBusy;
        private bool precisionBusy;
        private bool legacyHoldingObject;
        private readonly HashSet<string> holdingSources = new HashSet<string>(StringComparer.Ordinal);
        private bool teleportActive;
        private bool modal;

        public bool UiBusy => uiBusy;
        public bool PrecisionBusy => precisionBusy;
        public bool HoldingObject => legacyHoldingObject || holdingSources.Count > 0;
        public int HoldingSourceCount => holdingSources.Count + (legacyHoldingObject ? 1 : 0);
        public bool TeleportActive => teleportActive;
        public bool Modal => modal;
        public event Action Changed;

        public void SetUiBusy(bool value) => Set(ref uiBusy, value);
        public void SetPrecisionBusy(bool value) => Set(ref precisionBusy, value);
        public void SetHoldingObject(bool value) => Set(ref legacyHoldingObject, value);

        public void SetHoldingObject(string source, bool value)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            var changed = value ? holdingSources.Add(source) : holdingSources.Remove(source);
            if (changed) Changed?.Invoke();
        }
        public void SetTeleportActive(bool value) => Set(ref teleportActive, value);
        public void SetModal(bool value) => Set(ref modal, value);

        public void ResetTransient()
        {
            uiBusy = false;
            precisionBusy = false;
            legacyHoldingObject = false;
            holdingSources.Clear();
            teleportActive = false;
            modal = false;
            Changed?.Invoke();
        }

        private void Set(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            Changed?.Invoke();
        }
    }
}
