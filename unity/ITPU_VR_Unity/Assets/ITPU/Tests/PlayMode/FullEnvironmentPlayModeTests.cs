using System.Collections;
using System.Linq;
using ITPU.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ITPU.Tests.PlayMode
{
    public sealed class FullEnvironmentPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedScene_HasExactFullEnvironmentContract()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();

            var environmentId = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Value == "environment.full.v005");
            var environment = environmentId.gameObject;
            Assert.That(environment.name, Is.EqualTo("ENVIRONMENT_FULL_REFINED_005"));
            Assert.That(environment.GetComponentsInChildren<StableId>(true), Has.Length.EqualTo(381));
            Assert.That(environment.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(626));
            Assert.That(environment.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(549));
            Assert.That(environment.GetComponentsInChildren<MeshCollider>(true), Has.Length.EqualTo(4));
            Assert.That(environment.GetComponentsInChildren<CapsuleCollider>(true), Has.Length.EqualTo(1));
            Assert.That(environment.GetComponentsInChildren<TeleportSurface>(true), Has.Length.EqualTo(28));

            var renderers = environment.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Assert.That(bounds.size.x, Is.EqualTo(21.743f).Within(0.15f));
            Assert.That(bounds.size.y, Is.EqualTo(3.18f).Within(0.08f));
            Assert.That(bounds.size.z, Is.EqualTo(22.0815f).Within(0.15f));

            var ids = environment.GetComponentsInChildren<StableId>(true)
                .Select(item => item.Value).ToArray();
            foreach (var required in new[]
                     {
                         "FLOOR_0_2", "FLOOR_8_4", "RECEPTION", "ARCH_412", "ARCH_413",
                         "ARCH_414", "DOOR_401", "DOOR_423", "K_COFFEE", "K_CUP",
                     })
                Assert.That(ids, Does.Contain(required));
            Assert.That(ids.Any(item => item.Contains("430") || item.Contains("436")), Is.False);
            foreach (var movable in new[] { "MARKER", "ERASER", "K_CUP" })
                Assert.That(environment.GetComponentsInChildren<StableId>(true)
                    .Single(item => item.Value == movable).gameObject.isStatic, Is.False);

            foreach (var number in new[] { "412", "413", "414" })
            {
                var seat = environment.GetComponentsInChildren<StableId>(true)
                    .Single(item => item.Value == "SEAT_" + number);
                var table = environment.GetComponentsInChildren<StableId>(true)
                    .Single(item => item.Value == "TABLE_" + number);
                var seatBounds = seat.GetComponentsInChildren<Renderer>(true)[0].bounds;
                foreach (var renderer in seat.GetComponentsInChildren<Renderer>(true).Skip(1))
                    seatBounds.Encapsulate(renderer.bounds);
                var tableBounds = table.GetComponentsInChildren<Renderer>(true)[0].bounds;
                foreach (var renderer in table.GetComponentsInChildren<Renderer>(true).Skip(1))
                    tableBounds.Encapsulate(renderer.bounds);
                Assert.That(seatBounds.size.z, Is.GreaterThan(seatBounds.size.x),
                    "The niche seat must run along the side wall, not across the rear wall.");
                Assert.That(Mathf.Abs(seatBounds.center.x - tableBounds.center.x), Is.GreaterThan(0.55f),
                    "The niche seat must be laterally beside the table.");
            }

            var key = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.type == LightType.Directional);
            Assert.That(key.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(key.shadowStrength, Is.EqualTo(0f).Within(0.001f));
            var emissive = environment.GetComponentsInChildren<Renderer>(true)
                .Count(item => item.sharedMaterials.Any(material => material != null &&
                    material.name == "ITPU_fixture_emissive" && material.IsKeywordEnabled("_EMISSION")));
            Assert.That(emissive, Is.EqualTo(21));
            Assert.That(Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Any(item => item.enabled && item.gameObject.activeInHierarchy &&
                        (item.type == LightType.Point || item.type == LightType.Spot)), Is.False);
        }

        [UnityTest]
        public IEnumerator SavedScene_PreservesGameplayAndCorrectedC03Placement()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();

            Assert.That(Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            var panel = GameObject.Find("C03_INFORMATION_PANEL").GetComponent<Renderer>();
            var floorColliders = new[] { "FLOOR_0_2", "FLOOR_0_3" }
                .Select(id => Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == id))
                .SelectMany(item => item.GetComponentsInChildren<BoxCollider>(true)).ToArray();
            var floorBounds = floorColliders[0].bounds;
            floorBounds.Encapsulate(floorColliders[1].bounds);
            Assert.That(panel.bounds.min.x, Is.GreaterThan(floorBounds.min.x));
            Assert.That(panel.bounds.max.x, Is.LessThan(floorBounds.max.x));
            Assert.That(panel.bounds.min.z, Is.GreaterThan(floorBounds.min.z));
            Assert.That(panel.bounds.max.z, Is.LessThan(floorBounds.max.z));
            var wall = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == "WOOD_WALL_403")
                .GetComponentInChildren<BoxCollider>(true);
            Assert.That(panel.bounds.min.z, Is.GreaterThan(wall.bounds.max.z));
        }
    }
}
