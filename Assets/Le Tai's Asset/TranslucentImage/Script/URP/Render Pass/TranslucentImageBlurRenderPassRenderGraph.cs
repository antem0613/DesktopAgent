#if UNITY_2023_3_OR_NEWER
#define HAS_RENDERGRAPH
#endif

#if HAS_RENDERGRAPH
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#endif

namespace LeTai.Asset.TranslucentImage.UniversalRP
{
public partial class TranslucentImageBlurRenderPass
{
#if HAS_RENDERGRAPH
    class BlurRGPassData
    {
        public TextureHandle          sourceTex;
        public TextureHandle[]        scratches;
        public TranslucentImageSource blurSource;
        public IBlurAlgorithm         blurAlgorithm;
        public int                    scratchesCount;
    }

    class PreviewRGPassData
    {
        public TranslucentImageSource blurSource;
        public TextureHandle          previewTarget;
        public Material               previewMaterial;
    }

    readonly Dictionary<RenderTexture, RTHandle> blurredScreenHdlDict = new();

    List<TextureHandle[]> scratchesList;
    string[]              scratchNames;

    void RenderGraphInit()
    {
        scratchesList = new List<TextureHandle[]>(2);
        AddScratchList();
        scratchNames = new string[14];
        for (var i = 0; i < scratchNames.Length; i++)
        {
            scratchNames[i] = $"TI_intermediate_rt_{i}";
        }
    }

    void AddScratchList()
    {
        scratchesList.Add(new TextureHandle[14]);
    }

    void RenderGraphDispose()
    {
        foreach (var (_, hdl) in blurredScreenHdlDict)
        {
            hdl?.Release();
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var blurSources   = currentPassData.blurSources;
        var blurAlgorithm = currentPassData.blurAlgorithm;

        for (var i = 0; i < blurSources.Count; i++)
        {
            var blurSource = blurSources[i];
            if (!blurSource
             || !blurSource.BlurConfig)
                continue;
            if ((currentPassData.shouldUpdateBlurMask & (1 << i)) == 0)
                continue;

            blurSource.ReallocateBlurTexIfNeeded(currentPassData.camPixelRect);

            var blurredScreen = blurSource.BlurredScreen;
            blurredScreenHdlDict.TryGetValue(blurredScreen, out var blurredScreenHdl);
            if (blurredScreenHdl == null || blurredScreenHdl.rt != blurredScreen)
            {
                blurredScreenHdl?.Release();
                blurredScreenHdl = RTHandles.Alloc(blurredScreen);

                blurredScreenHdlDict[blurredScreen] = blurredScreenHdl;
            }

            // ReSharper disable once ConvertToUsingDeclaration
            using (var builder = renderGraph.AddUnsafePass<BlurRGPassData>(PROFILER_TAG, out var data))
            {
                blurAlgorithm.Init(blurSource.BlurConfig, false);

                var desc           = blurSource.BlurredScreen.descriptor;
                var cropRegionSize = blurSource.BlurRegion.size;
                var scratchesCount = blurAlgorithm.GetScratchesCount(desc.width / cropRegionSize.x,
                                                                     desc.height / cropRegionSize.y);

                var resourceData = frameData.Get<UniversalResourceData>();
                data.sourceTex = resourceData.activeColorTexture;
                if (scratchesList.Count <= i)
                    AddScratchList();
                data.scratches      = scratchesList[i];
                data.blurSource     = blurSource;
                data.blurAlgorithm  = blurAlgorithm;
                data.scratchesCount = scratchesCount;

                builder.UseTexture(data.sourceTex, AccessFlags.Read);


                for (int j = 0; j < scratchesCount; j++)
                {
                    blurAlgorithm.GetNextScratchDescriptor(ref desc);
                    data.scratches[j] = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                                                                                   desc,
                                                                                   scratchNames[j] + "--" + i,
                                                                                   false,
                                                                                   FilterMode.Bilinear,
                                                                                   TextureWrapMode.Clamp);
                    builder.UseTexture(data.scratches[j], AccessFlags.ReadWrite);
                }

                var blurredScreenTHdl = renderGraph.ImportTexture(blurredScreenHdl);
                builder.UseTexture(blurredScreenTHdl, AccessFlags.Write);

                builder.SetRenderFunc(static (BlurRGPassData data, UnsafeGraphContext context) =>
                {
                    data.blurAlgorithm.Init(data.blurSource.BlurConfig, true);
                    for (int j = 0; j < data.scratchesCount; j++)
                        data.blurAlgorithm.SetScratch(j, data.scratches[j]);

                    var blurExecData = new BlurExecutor.BlurExecutionData(
                        data.sourceTex,
                        data.blurSource,
                        data.blurAlgorithm
                    );
                    BlurExecutor.ExecuteBlur(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), ref blurExecData);
                });
            }
        }

        if (currentPassData.previewIndex != -1)
        {
            // ReSharper disable once ConvertToUsingDeclaration
            using (var builder = renderGraph.AddUnsafePass<PreviewRGPassData>(PROFILER_TAG, out var data))
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                data.blurSource      = blurSources[currentPassData.previewIndex];
                data.previewMaterial = currentPassData.previewMaterial;
                data.previewTarget   = resourceData.activeColorTexture;

                builder.UseTexture(data.previewTarget, AccessFlags.Write);

                builder.SetRenderFunc(static (PreviewRGPassData data, UnsafeGraphContext context) =>
                {
                    var previewExecData = new PreviewExecutionData(
                        data.blurSource,
                        data.previewTarget,
                        data.previewMaterial
                    );
                    ExecutePreview(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), ref previewExecData);
                });
            }
        }
    }

#else
    void RenderGraphInit()    { }
    void RenderGraphDispose() { }

#endif
}
}
