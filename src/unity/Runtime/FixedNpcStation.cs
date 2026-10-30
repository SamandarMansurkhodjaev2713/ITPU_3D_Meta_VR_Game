using UnityEngine;
using ITPU.Domain;
namespace ITPU.Unity
{
    [DisallowMultipleComponent]
    public sealed class FixedNpcStation : MonoBehaviour
    {
        [SerializeField] private SessionHost session;
        [SerializeField] private Transform viewer;
        [SerializeField] private Transform torsoAimPivot;
        [SerializeField] private Transform headAimPivot;
        [SerializeField] private Animator animator;
        private Vector3 anchor;
        private Quaternion anchorRotation,torsoRest,headRest;
        private NpcStation model;
        private bool faultLogged;
        private void Awake()
        {
            anchor=transform.position;anchorRotation=transform.rotation;
            model=new NpcStation(new Vec3(anchor.x,anchor.y,anchor.z));
            if(animator!=null)animator.applyRootMotion=false;
            if(torsoAimPivot!=null)torsoRest=torsoAimPivot.localRotation;
            if(headAimPivot!=null)headRest=headAimPivot.localRotation;
        }
        private void OnEnable(){if(session!=null)session.ResetOccurred+=ResetStation;}
        private void OnDisable(){if(session!=null)session.ResetOccurred-=ResetStation;}
        public void ResetStation()
        {if(model==null)return;model.Reset();transform.SetPositionAndRotation(anchor,anchorRotation);if(torsoAimPivot!=null)torsoAimPivot.localRotation=torsoRest;if(headAimPivot!=null)headAimPivot.localRotation=headRest;faultLogged=false;}
        private void LateUpdate()
        {
            if(model==null)return;
            // A drift is a defect, not a navigation feature. Clamp and report once.
            if((transform.position-anchor).sqrMagnitude>0.000001f||Quaternion.Angle(transform.rotation,anchorRotation)>.1f)
            {if(!faultLogged){Debug.LogError("NPC station root drifted; root-motion/navigation is forbidden",this);faultLogged=true;}transform.SetPositionAndRotation(anchor,anchorRotation);}
            bool paused=session!=null&&session.Model.Paused;
            float yaw=0;bool valid=viewer!=null;
            if(valid){Vector3 direction=Quaternion.Inverse(anchorRotation)*(viewer.position-anchor);yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;}
            model.Look(yaw,Time.unscaledDeltaTime,valid,paused);
            // Dedicated aim pivots must NOT be the same bones driven by an animation track.
            if(torsoAimPivot!=null)torsoAimPivot.localRotation=torsoRest*Quaternion.Euler(0,(float)model.BodyYaw,0);
            if(headAimPivot!=null)headAimPivot.localRotation=headRest*Quaternion.Euler(0,(float)model.HeadYaw,0);
        }
    }
}
