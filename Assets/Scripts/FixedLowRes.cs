using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class FixedLowRes : MonoBehaviour
{
    public int targetHeight = 180;

    void Update()
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
            urp.renderScale = Mathf.Clamp((float)targetHeight / Screen.height, 0.1f, 1f);
    }
}