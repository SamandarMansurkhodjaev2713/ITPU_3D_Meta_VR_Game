using UnityEngine;
using UnityEngine.Events;
using ITPU.Domain;
namespace ITPU.Unity
{
    // XR bridge calls ConfirmGrab / ConfirmRelease / LostTracking; one bridge only.
    [DisallowMultipleComponent]
    public sealed class RecoverablePropAdapter : MonoBehaviour
    {
        [SerializeField] private SessionHost session;
        [SerializeField] private Transform home;
        [SerializeField] private Rigidbody body;
        [SerializeField] private UnityEvent forceReleaseForReset;
        private RecoverableProp model;private Vector3 start;private Quaternion rotation;
        private void Awake(){start=transform.position;rotation=transform.rotation;if(session!=null)model=new RecoverableProp(session.Model);}
        private void OnEnable(){if(session!=null)session.ResetOccurred+=ResetProp;}
        private void OnDisable(){if(session!=null)session.ResetOccurred-=ResetProp;}
        public bool ConfirmGrab(){return model!=null&&model.Hold(session.Model.Token);}
        public void ConfirmRelease(){if(model!=null)model.Release(session.Model.Token);}
        public void LostTracking(){if(model!=null)model.LostTracking();}
        public bool RequestReturn(bool destinationClear)
        {if(model==null||!model.TryReturn(destinationClear,session.Model.Token))return false;MoveHome();return true;}
        private void MoveHome(){if(body!=null){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}transform.SetPositionAndRotation(home!=null?home.position:start,home!=null?home.rotation:rotation);}
        public void ResetProp(){if(model==null)return;forceReleaseForReset?.Invoke();model.Reset();MoveHome();}
    }
}
