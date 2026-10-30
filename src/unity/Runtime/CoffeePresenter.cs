using UnityEngine;
using ITPU.Domain;
namespace ITPU.Unity
{
    // Connect CupDocked/CupRemoved/Activate to exactly one interaction system.
    [DisallowMultipleComponent]
    public sealed class CoffeePresenter : MonoBehaviour
    {
        [SerializeField] private SessionHost session;
        [SerializeField] private Transform liquidSurface;
        [SerializeField] private GameObject streamVisual;
        [SerializeField] private AudioSource brewAudio;
        [SerializeField] private float brewSeconds=6f;
        [SerializeField] private float emptyHeight=.01f,fullHeight=.09f;
        private CoffeeMachine model;private Vector3 liquidStart;
        public CoffeeMachine Model {get{return model;}}
        private void Awake(){model=new CoffeeMachine(Mathf.Max(.1f,brewSeconds));if(liquidSurface!=null)liquidStart=liquidSurface.localPosition;ApplyVisuals();}
        private void OnEnable(){if(session!=null)session.ResetOccurred+=ResetProp;}
        private void OnDisable(){if(session!=null)session.ResetOccurred-=ResetProp;if(brewAudio!=null)brewAudio.Stop();if(streamVisual!=null)streamVisual.SetActive(false);}
        public void CupDocked(){if(model==null)return;model.Dock();ApplyVisuals();}
        public void CupRemoved(){if(model==null)return;model.Remove();ApplyVisuals();}
        public void Activate(){if(model==null||(session!=null&&session.Model.Paused))return;model.Press();ApplyVisuals();}
        public void ResetProp(){if(model==null)return;model.Reset();ApplyVisuals();}
        private void Update(){if(model==null)return;model.Tick(Time.unscaledDeltaTime,session!=null&&session.Model.Paused);ApplyVisuals();}
        private void ApplyVisuals()
        {
            if(model==null)return;bool flowing=model.State==CoffeeState.Brewing&&model.CupPresent&&(session==null||!session.Model.Paused);
            if(streamVisual!=null&&streamVisual.activeSelf!=flowing)streamVisual.SetActive(flowing);
            if(brewAudio!=null){if(flowing&&!brewAudio.isPlaying)brewAudio.Play();else if(!flowing&&brewAudio.isPlaying)brewAudio.Stop();}
            if(liquidSurface!=null){Vector3 v=liquidStart;v.y=Mathf.Lerp(emptyHeight,fullHeight,(float)model.Fill);liquidSurface.localPosition=v;}
        }
    }
}
