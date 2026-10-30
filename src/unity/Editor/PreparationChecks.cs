using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using ITPU.Unity;
using ITPU.Domain;
namespace ITPU.Editor
{
    public static class PreparationChecks
    {
        [Serializable] private sealed class Report {public string unityVersion,profile,status;public string[] errors,notes;}
        [MenuItem("ITPU/Validate/Environment (no NPC)")] public static void ValidateEnvironment(){Validate(SceneProfile.Environment);}
        [MenuItem("ITPU/Validate/Interaction slice")] public static void ValidateInteractionSlice(){Validate(SceneProfile.InteractionSlice);}
        [MenuItem("ITPU/Validate/Complete game (static checks only)")] public static void ValidateCompleteGame(){Validate(SceneProfile.CompleteGame);}
        // Compatibility entry now checks only the environment, never auto-selects an easier profile.
        public static void ValidateOpenScene(){ValidateEnvironment();}
        public static void Validate(SceneProfile profile)
        {
            var errors=new List<string>();var notes=new List<string>();var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            int npcs=0,sessions=0,coffees=0;bool materials=true;
            foreach(var root in roots)
            {
                npcs+=root.GetComponentsInChildren<FixedNpcStation>(true).Length;sessions+=root.GetComponentsInChildren<SessionHost>(true).Length;coffees+=root.GetComponentsInChildren<CoffeePresenter>(true).Length;
                foreach(var station in root.GetComponentsInChildren<FixedNpcStation>(true))
                {
                    foreach(var animator in station.GetComponentsInChildren<Animator>(true))if(animator.applyRootMotion)errors.Add("Root motion: "+animator.name);
                    foreach(var c in station.GetComponentsInChildren<Component>(true))if(c!=null&&c.GetType().Name=="NavMeshAgent")errors.Add("Walking component: "+c.name);
                }
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)if(m==null||m.shader==null)materials=false;
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)errors.Add("Missing script: "+t.name);
                    var p=t.localScale;if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z))errors.Add("Invalid scale: "+t.name);
                }
            }
            errors.AddRange(ScenePolicy.Validate(profile,npcs,sessions,coffees>0,materials,false));
            notes.Add("Only active scene inspected. No FPS, clearance, tracking, localization or release approval proven.");
            notes.Add("Release always requires separate signed-off device/localization/operational evidence; this checker cannot grant it.");
            Directory.CreateDirectory("ITPUReports");var report=new Report{unityVersion=Application.unityVersion,profile=profile.ToString(),status=errors.Count==0?"STATIC_PASS":"STATIC_FAIL",errors=errors.ToArray(),notes=notes.ToArray()};
            File.WriteAllText("ITPUReports/scene-"+profile.ToString()+".json",JsonUtility.ToJson(report,true));
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));Debug.Log("ITPU static check completed: "+profile);
        }
    }
}
