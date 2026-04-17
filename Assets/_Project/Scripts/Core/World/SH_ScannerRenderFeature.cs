using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace TerrainScanner
{
    public class ScannerRenderFeature : ScriptableRendererFeature
    {
        private class ScannerRenderPass : ScriptableRenderPass
        {
            private Material _material;

            private class EffectPassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            private class CopyPassData
            {
                public TextureHandle Source;
            }

            public void Setup(Material material)
            {
                _material = material;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle cameraColor = resourceData.activeColorTexture;

                // Create an intermediate texture with the same descriptor as the camera color.
                var desc = renderGraph.GetTextureDesc(cameraColor);
                desc.name = "ScannerTempColor";
                desc.clearBuffer = false;
                TextureHandle tempColor = renderGraph.CreateTexture(desc);

                // ── Pass 1: Apply scanner shader → tempColor ──────────────
                using (var builder = renderGraph.AddRasterRenderPass<EffectPassData>(
                           "Scanner Effect Pass", out var passData))
                {
                    passData.Source = cameraColor;
                    passData.Material = _material;

                    builder.UseTexture(passData.Source, AccessFlags.Read);
                    builder.SetRenderAttachment(tempColor, 0);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((EffectPassData data, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(
                            ctx.cmd,
                            data.Source,
                            new Vector4(1f, 1f, 0f, 0f),
                            data.Material,
                            0);
                    });
                }

                // ── Pass 2: Copy tempColor back to cameraColor (no shader) ─
                using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>(
                           "Scanner Copy Back", out var passData))
                {
                    passData.Source = tempColor;

                    builder.UseTexture(passData.Source, AccessFlags.Read);
                    builder.SetRenderAttachment(cameraColor, 0);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((CopyPassData data, RasterGraphContext ctx) =>
                    {
                        // Simple copy using URP's internal blit — no custom material.
                        Blitter.BlitTexture(
                            ctx.cmd,
                            data.Source,
                            new Vector4(1f, 1f, 0f, 0f),
                            mipLevel: 0,
                            bilinear: false);
                    });
                }
            }
        }

        [Tooltip("Material using Hidden/ScannerWorld shader.")]
        public Material material;

        [Tooltip("When to inject the pass in the render pipeline.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        private ScannerRenderPass _pass;

        public override void Create()
        {
            _pass = new ScannerRenderPass();
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null) return;

            _pass.renderPassEvent = renderPassEvent;
            _pass.Setup(material);
            renderer.EnqueuePass(_pass);
        }
    }
}