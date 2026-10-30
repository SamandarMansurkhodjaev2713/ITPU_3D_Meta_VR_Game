using UnityEngine;
using ITPU.Domain;
namespace ITPU.Unity
{
    // Dedicated board surface UV0 is normalized; front orientation is validated by the export probe, not inferred from a name.
    // Adapter input is normalized board UV, not raw world coordinates. Pen contact must validate plane/distance.
    [DisallowMultipleComponent]
    public sealed class DrawingPresenter : MonoBehaviour
    {
        [SerializeField] private SessionHost session;
        [SerializeField] private Renderer surface;
        [SerializeField] private float uploadHz=20;
        private DrawingSurface model;private Texture2D texture;private Color32[] buffer;
        private byte[] pixels;private MaterialPropertyBlock properties;private Texture originalTexture;private int uploaded=-1;private float nextUpload;
        private static readonly Color32[] Palette={new Color32(244,241,233,255),new Color32(27,47,57,255),new Color32(35,122,116,255),new Color32(185,75,51,255),new Color32(93,76,154,255),new Color32(170,131,29,255)};
        private void Awake()
        {model=new DrawingSurface();buffer=new Color32[model.Width*model.Height];pixels=new byte[buffer.Length];texture=new Texture2D(model.Width,model.Height,TextureFormat.RGBA32,false,false);texture.wrapMode=TextureWrapMode.Clamp;properties=new MaterialPropertyBlock();if(surface!=null){surface.GetPropertyBlock(properties);originalTexture=properties.GetTexture("_BaseMap");properties.SetTexture("_BaseMap",texture);surface.SetPropertyBlock(properties);}Upload();}
        private void OnEnable(){if(session!=null)session.ResetOccurred+=ResetBoard;}
        private void OnDisable(){if(session!=null)session.ResetOccurred-=ResetBoard;model?.Cancel();}
        private void OnDestroy(){if(surface!=null){surface.GetPropertyBlock(properties);properties.SetTexture("_BaseMap",originalTexture);surface.SetPropertyBlock(properties);}if(texture!=null)Destroy(texture);}
        private bool CanEdit(){return model!=null&&(session==null||!session.Model.Paused);}
        public void BeginStroke(){if(CanEdit())model.Begin();}
        public void DrawSegment(Vector2 from,Vector2 to,float radius,byte color){if(CanEdit())model.Line(from.x,from.y,to.x,to.y,radius,color);}
        public void EndStroke(){model?.End();}
        public void CancelOnTrackingLoss(){model?.Cancel();}
        public void UndoStroke(){if(CanEdit())model.Undo();}
        public byte[] FinalCardPixels(){return model.Snapshot();}
        public void ResetBoard(){model?.Reset();}
        private void Update(){if(model!=null&&model.Revision!=uploaded&&Time.unscaledTime>=nextUpload){Upload();nextUpload=Time.unscaledTime+1/Mathf.Clamp(uploadHz,1,30);}}
        private void Upload(){model.CopyPixels(pixels);for(int i=0;i<buffer.Length;i++)buffer[i]=Palette[pixels[i]];texture.SetPixels32(buffer);texture.Apply(false,false);uploaded=model.Revision;}
    }
}
