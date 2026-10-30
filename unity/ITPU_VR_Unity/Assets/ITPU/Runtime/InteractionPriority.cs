using System;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class InteractionPriority : MonoBehaviour
    {
        private bool uiBusy;
        private bool precisionBusy;
        private bool holdingObject;
        private bool teleportActive;
        private bool modal;

        public bool UiBusy => uiBusy;
        public bool PrecisionBusy => precisionBusy;
        public bool HoldingObject => holdingObject;
        public bool TeleportActive => teleportActive;
        public bool Modal => modal;
        public event Action Changed;

        public void SetUiBusy(bool value) => Set(ref uiBusy, value);
        public void SetPrecisionBusy(bool value) => Set(ref precisionBusy, value);
        public void SetHoldingObject(bool value) => Set(ref holdingObject, value);
        public void SetTeleportActive(bool value) => Set(ref teleportActive, value);
        public void SetModal(bool value) => Set(ref modal, value);

        public void ResetTransient()
        {
            uiBusy = false;
            precisionBusy = false;
            holdingObject = false;
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
