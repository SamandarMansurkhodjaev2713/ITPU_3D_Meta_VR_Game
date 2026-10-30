using System;
using UnityEngine;
using ITPU.Domain;
namespace ITPU.Unity
{
    // XR lifecycle adapter must call TrackingPause from the actually selected SDK events.
    public sealed class SessionHost : MonoBehaviour
    {
        public Session Model {get;private set;}=new Session();
        public event Action ResetOccurred;
        private bool systemPaused,trackingPaused,operatorPaused,focusPaused,resetInProgress;
        public void SystemPause(bool paused){systemPaused=paused;Model.SetPause("system",paused);}
        public void TrackingPause(bool paused){trackingPaused=paused;Model.SetPause("tracking",paused);}
        public void OperatorPause(bool paused){operatorPaused=paused;Model.SetPause("operator",paused);}
        private void OnApplicationPause(bool paused){SystemPause(paused);}
        // Focus is a separate reason: a focus-gained callback must not override a system pause.
        private void OnApplicationFocus(bool focus){focusPaused=!focus;Model.SetPause("focus",!focus);}
        public void ResetSession()
        {
            if(resetInProgress)return;
            resetInProgress=true;
            try
            {
                Model.Reset();
                // A reset cannot cancel a still-active headset/focus/tracking pause.
                Model.SetPause("system",systemPaused);Model.SetPause("tracking",trackingPaused);
                Model.SetPause("operator",operatorPaused);Model.SetPause("focus",focusPaused);
                var handlers=ResetOccurred;
                if(handlers!=null)foreach(Action handler in handlers.GetInvocationList())
                {try{handler();}catch(Exception e){Debug.LogException(e);Model.SetPause("reset_fault",true);}}
            }
            finally{resetInProgress=false;}
        }
    }
}
