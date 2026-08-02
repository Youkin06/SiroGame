using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// WebGLでも利用できる、ドット化・アウトライン用のRenderer Feature。
/// URP標準Full Screen Passの画面コピーを使わず、加工後のテクスチャを
/// Camera Colorとして後続パスへ引き渡す。
/// </summary>
public sealed class DotOutlineRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Material material;
    [SerializeField] private RenderPassEvent injectionPoint =
        RenderPassEvent.AfterRenderingPostProcessing;

    private DotOutlineRenderPass renderPass;

    public override void Create()
    {
        renderPass = new DotOutlineRenderPass
        {
            renderPassEvent = injectionPoint
        };
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview ||
            cameraType == CameraType.Reflection ||
            material == null)
        {
            return;
        }

        renderPass.Setup(material);
        renderer.EnqueuePass(renderPass);
    }

    private sealed class DotOutlineRenderPass : ScriptableRenderPass
    {
        private const string PassName = "Dot Outline Fullscreen";
        private readonly MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        private Material passMaterial;

        public void Setup(Material targetMaterial)
        {
            passMaterial = targetMaterial;

            // Camera Colorを読み込めるよう、URPに中間バッファを要求する。
            requiresIntermediateTexture = true;
            ConfigureInput(
                ScriptableRenderPassInput.Depth |
                ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            UniversalResourceData resourceData =
                frameData.Get<UniversalResourceData>();

            // BackBufferはテクスチャとして同時に読み込めない。
            // requiresIntermediateTextureにより通常はここへ到達しない。
            if (resourceData.isActiveTargetBackBuffer || passMaterial == null)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc destinationDescriptor = renderGraph.GetTextureDesc(source);
            destinationDescriptor.name = "_CameraColorDotOutline";
            destinationDescriptor.clearBuffer = false;
            TextureHandle destination =
                renderGraph.CreateTexture(destinationDescriptor);

            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<PassData>(
                    PassName,
                    out PassData passData,
                    profilingSampler))
            {
                passData.source = source;
                passData.material = passMaterial;
                passData.propertyBlock = propertyBlock;

                builder.UseTexture(source, AccessFlags.Read);

                if (resourceData.cameraDepthTexture.IsValid())
                {
                    builder.UseTexture(
                        resourceData.cameraDepthTexture,
                        AccessFlags.Read);
                }

                if (resourceData.cameraNormalsTexture.IsValid())
                {
                    builder.UseTexture(
                        resourceData.cameraNormalsTexture,
                        AccessFlags.Read);
                }

                builder.SetRenderAttachment(
                    destination,
                    0,
                    AccessFlags.Write);

                builder.SetRenderFunc<PassData>(static (data, context) =>
                {
                    data.propertyBlock.Clear();
                    data.propertyBlock.SetTexture(
                        Shader.PropertyToID("_BlitTexture"),
                        data.source);
                    data.propertyBlock.SetVector(
                        Shader.PropertyToID("_BlitScaleBias"),
                        new Vector4(1f, 1f, 0f, 0f));

                    context.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.material,
                        0,
                        MeshTopology.Triangles,
                        3,
                        1,
                        data.propertyBlock);
                });
            }

            // 後続パスとFinal Blitは加工後のCamera Colorを使用する。
            resourceData.cameraColor = destination;
        }

        private sealed class PassData
        {
            internal TextureHandle source;
            internal Material material;
            internal MaterialPropertyBlock propertyBlock;
        }
    }
}
