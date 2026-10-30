using System;
using System.Collections.Generic;
using UnityEngine;

namespace ITPU.XR
{
    [Serializable]
    public struct C01WaypointBinding
    {
        public string id;
        public Transform marker;

        public C01WaypointBinding(string waypointId, Transform waypointMarker)
        {
            id = waypointId;
            marker = waypointMarker;
        }
    }

    [DisallowMultipleComponent]
    public sealed class C01RoutePresenter : MonoBehaviour
    {
        [SerializeField] private LineRenderer routeLine;
        [SerializeField] private C01WaypointBinding[] waypoints;
        private readonly List<Transform> visibleMarkers = new();

        public int VisiblePointCount => routeLine != null && routeLine.enabled ? routeLine.positionCount : 0;
        public int PresentationCount { get; private set; }
        public string CurrentDestination { get; private set; } = "";
        public LineRenderer RouteLine => routeLine;

        public void Configure(LineRenderer line, C01WaypointBinding[] bindings)
        {
            routeLine = line;
            waypoints = bindings ?? Array.Empty<C01WaypointBinding>();
            Clear();
        }

        public bool Present(string[] route, string destination)
        {
            ClearVisibleOnly();
            if (routeLine == null || route == null || route.Length == 0) return false;

            var positions = new Vector3[route.Length];
            for (var index = 0; index < route.Length; index++)
            {
                var marker = Find(route[index]);
                if (marker == null)
                {
                    Clear();
                    return false;
                }
                positions[index] = marker.position;
                marker.gameObject.SetActive(true);
                visibleMarkers.Add(marker);
            }

            routeLine.positionCount = positions.Length;
            routeLine.SetPositions(positions);
            routeLine.enabled = true;
            CurrentDestination = destination ?? "";
            PresentationCount++;
            return true;
        }

        public void Clear()
        {
            ClearVisibleOnly();
            CurrentDestination = "";
            PresentationCount = 0;
        }

        private void ClearVisibleOnly()
        {
            foreach (var marker in visibleMarkers)
                if (marker != null) marker.gameObject.SetActive(false);
            visibleMarkers.Clear();
            if (routeLine == null) return;
            routeLine.positionCount = 0;
            routeLine.enabled = false;
        }

        private Transform Find(string id)
        {
            foreach (var binding in waypoints ?? Array.Empty<C01WaypointBinding>())
                if (binding.id == id) return binding.marker;
            return null;
        }
    }
}
