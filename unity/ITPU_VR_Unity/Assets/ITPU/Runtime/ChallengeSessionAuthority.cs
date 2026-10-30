using System;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class ChallengeSessionAuthority : MonoBehaviour
    {
        [SerializeField] private VerticalSliceReset sessionReset;
        [SerializeField] private XrLifecycleState lifecycle;

        private Session session;
        private bool subscribed;

        public Session Session
        {
            get
            {
                EnsureSession();
                return session;
            }
        }

        public SessionToken Token => Session.Token;
        public int ResetRevision { get; private set; }
        public event Action ResetOccurred;

        public void Configure(VerticalSliceReset reset, XrLifecycleState lifecycleState)
        {
            Unsubscribe();
            sessionReset = reset;
            lifecycle = lifecycleState;
            EnsureSession();
            SyncLifecyclePause();
            Subscribe();
        }

        private void Awake()
        {
            EnsureSession();
            SyncLifecyclePause();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void EnsureSession()
        {
            session ??= new Session();
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || sessionReset == null) return;
            sessionReset.ResetPerformed += OnSessionReset;
            if (lifecycle != null) lifecycle.Changed += SyncLifecyclePause;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (sessionReset != null) sessionReset.ResetPerformed -= OnSessionReset;
            if (lifecycle != null) lifecycle.Changed -= SyncLifecyclePause;
            subscribed = false;
        }

        private void OnSessionReset()
        {
            EnsureSession();
            session.Reset();
            SyncLifecyclePause();
            checked { ResetRevision++; }
            Debug.Log("ITPU_SESSION reset_revision=" + ResetRevision + " epoch=" + session.Token.Epoch);
            ResetOccurred?.Invoke();
        }

        private void SyncLifecyclePause()
        {
            EnsureSession();
            var blocked = lifecycle == null || !lifecycle.Focused || lifecycle.Paused || !lifecycle.HeadTracked;
            session.SetPause("xr.lifecycle", blocked);
        }
    }
}
