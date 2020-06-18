﻿//
// ShadowBuffer.cs
//
// Projector For LWRP
//
// Copyright (c) 2020 NYAHOON GAMES PTE. LTD.
//

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;
using Unity.Collections;

namespace ProjectorForLWRP
{
    [ExecuteInEditMode]
    public class ShadowBuffer : MonoBehaviour, System.IComparable<ShadowBuffer>
    {
        public enum ShadowColor
        {
            Monochrome = 0,
            Colored
        };
        public enum ApplyMethod
        {
            ByShadowProjectors = 0,
            ByLitShaders = 1,
            ByLightProjectors = 2,
        };

        [SerializeField][HideInInspector]
        private Material m_material;
        [SerializeField][HideInInspector]
        private string m_shadowTextureName = "_ShadowTex";
        [SerializeField][HideInInspector]
        private ShadowColor m_shadowColor = ShadowColor.Monochrome;
        [SerializeField][HideInInspector]
        private ApplyMethod m_applyMethod = ApplyMethod.ByShadowProjectors;
        [SerializeField][HideInInspector]
        private RenderPassEvent m_renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        [SerializeField][HideInInspector]
        private PerObjectData m_perObjectData = PerObjectData.None;
        [SerializeField][HideInInspector]
        private LayerMask m_shadowReceiverLayers = -1;
        [SerializeField][HideInInspector]
        private bool m_collectRealtimeShadows = true;

        public Material material { get { return m_material; } }
        public ShadowColor shadowColor { get { return m_shadowColor; } }
        public ApplyMethod applyMethod { get { return m_applyMethod; } }
        public RenderPassEvent renderPassEvent { get { return m_renderPassEvent; } }
        private PerObjectData perObjectData { get { return m_perObjectData; } }
        private LayerMask shadowReceiverLayers { get { return m_shadowReceiverLayers; } }
        public bool collectRealtimeShadows { get { return m_applyMethod == ApplyMethod.ByLitShaders && m_collectRealtimeShadows && realtimeShadowsEnabled && shadowReceiverLayers != 0; } }

        private Dictionary<Camera, List<ShadowProjectorForLWRP>> m_projectors = new Dictionary<Camera, List<ShadowProjectorForLWRP>>();
        private ApplyShadowBufferPass m_applyPass;
        private ShadowMaterialProperties m_shadowMaterialProperties;
        private bool m_useShadowMaterialProperties;
        private int m_shadowTextureId;
        private void Initialize()
        {
            m_applyPass = new ApplyShadowBufferPass(this);
            m_shadowTextureId = Shader.PropertyToID(m_shadowTextureName);
            m_shadowMaterialProperties = GetComponent<ShadowMaterialProperties>();
            m_useShadowMaterialProperties = IsShadowMaterial();
        }
        ApplyShadowBufferPass applyPass
        {
            get
            {
                if (m_applyPass == null)
                {
                    Initialize();
                }
                return m_applyPass;
            }
        }
        public ShadowMaterialProperties shadowMaterialProperties
        {
            get
            {
                if (m_shadowMaterialProperties == null)
                {
                    Initialize();
                }
                return m_useShadowMaterialProperties ? m_shadowMaterialProperties : null;
            }
        }
        private void OnBeginFrameRendering(ScriptableRenderContext context, Camera[] cameras)
        {
            m_shadowTextureRef = null; // no longer valid.
            if (applyMethod == ApplyMethod.ByLitShaders && collectRealtimeShadows && isActiveAndEnabled)
            {
                for (int i = 0, count = cameras.Length; i < count; ++i)
                {
                    if ((cameras[i].cullingMask & (1 << gameObject.layer)) != 0)
                    {
                        ProjectorRendererFeature.AddShadowBuffer(this, cameras[i]);
                    }
                }
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            ClearProjectosForCamera(camera);
            if (m_shadowTextureRef != null)
            {
                m_shadowTextureRef.Release(m_shadowTextureColorWriteMask);
            }
        }

        private void OnEnable()
		{
            RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginFrameRendering -= OnBeginFrameRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        public bool IsShadowMaterial()
        {
            return material != null && (material.GetTag("P4LWRPApplyShadowBufferType", false) == "Shadow");
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_material == null && applyMethod == ApplyMethod.ByShadowProjectors)
            {
                m_material = HelperFunctions.FindMaterial("Projector For LWRP/ShadowBuffer/Apply Shadow Buffer");
            }
            if (applyMethod == ApplyMethod.ByLitShaders || (IsShadowMaterial() && applyMethod == ApplyMethod.ByShadowProjectors))
            {
                if (GetComponent<ShadowMaterialProperties>() == null)
                {
                    m_shadowMaterialProperties = gameObject.AddComponent<ShadowMaterialProperties>();
                    m_useShadowMaterialProperties = true;
                }
            }
            m_shadowTextureId = Shader.PropertyToID(m_shadowTextureName);
        }
#endif

        // use IComparable for sorting shadow buffer list because lambda expression cannot use 'ref'.
        // This can avoid making a copy of RenderingData.
        private int m_sortIndex;
        public int CompareTo(ShadowBuffer rhs)
        {
            return m_sortIndex - rhs.m_sortIndex;
        }

        public bool realtimeShadowsEnabled
        {
            get
            {
                if (shadowMaterialProperties == null)
                {
                    return false;
                }
                Light light = shadowMaterialProperties.lightSource;
                if (light == null)
                {
                    return false;
                }
                if (light.shadows == LightShadows.None)
                {
                    return false;
                }
                if (light.type == LightType.Rectangle || light.type == LightType.Disc)
                {
                    // no area light shadows
                    return false;
                }
                if (light.type == LightType.Point)
                {
                    // LWRP does not support point light shadows
                    return false;
                }
                if (light.bakingOutput.isBaked && light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                {
                    return false;
                }
                return true;
            }
        }
        private int m_additionalLightIndex;
        internal int visibleLightIndex { get; private set; }
        internal int additionalLightIndex { get { return m_additionalLightIndex; } }
        internal bool isMainLight { get; private set; }
        internal bool isVisible
        {
            get
            {
                if (applyMethod == ApplyMethod.ByLightProjectors)
                {
                    return true;
                }
                if (applyMethod == ApplyMethod.ByLitShaders && shadowMaterialProperties == null)
                {
                    return false;
                }
                if (applyMethod == ApplyMethod.ByShadowProjectors && shadowMaterialProperties == null)
                {
                    return true;
                }
                return visibleLightIndex != -1 || additionalLightIndex != -1;
            }
        }
        internal void SetupLightSource(ref RenderingData renderingData, ref NativeArray<int> lightIndexMap)
        {
            if (shadowMaterialProperties != null)
            {
                visibleLightIndex = shadowMaterialProperties.FindLightSourceIndex(ref renderingData, ref lightIndexMap, out m_additionalLightIndex);
            }
            else
            {
                visibleLightIndex = m_additionalLightIndex = -1;
            }
            isMainLight = (visibleLightIndex != -1 && visibleLightIndex == renderingData.lightData.mainLightIndex);
            m_sortIndex = CalculateSortIndex(ref renderingData);
        }

        private int CalculateSortIndex(ref RenderingData renderingData)
        {
            if (applyMethod == ApplyMethod.ByLitShaders && shadowColor == ShadowColor.Monochrome && shadowMaterialProperties != null)
            {
                // shadow buffers that collect realtime shadows have the highest priority.
                // it is better to combine them into a single texture.
                // main light shadows must be written into alpha channel.
                if (collectRealtimeShadows)
                {
                    if (0 <= additionalLightIndex)
                    {
                        return additionalLightIndex + 1;
                    }
                    else if (isMainLight)
                    {
                        return 0;
                    }
                }
                // then, shadow buffers used by lit shaders but doesn't collect realtime shadows come next.
                // if the number of the additional light shadow buffers is less than 5, they must be combined together.
                // additional light shadow buffers must come first.
                else
                {
                    if (0 <= additionalLightIndex)
                    {
                        return additionalLightIndex + renderingData.lightData.additionalLightsCount + 1;
                    }
                    else if (isMainLight)
                    {
                        return 2 * renderingData.lightData.additionalLightsCount + 1;
                    }
                }
            }
            // for others, don't care order but need to minimize the number of texture.
            // to prevent combining monochrome shadow buffers together and leaving a color shadow buffer alone,
            // sort color shadow buffers first.
            int index = 2 * renderingData.lightData.additionalLightsCount + 1;
            if (shadowColor == ShadowColor.Monochrome)
            {
                index += 1;
            }
            return index;
        }

		internal void RegisterProjector(Camera cam, ShadowProjectorForLWRP projector)
        {
            if (material == null)
            {
                return;
            }
            List<ShadowProjectorForLWRP> projectors;
            if (!m_projectors.TryGetValue(cam, out projectors))
            {
                projectors = new List<ShadowProjectorForLWRP>();
                m_projectors.Add(cam, projectors);
            }
            projectors.Add(projector);
        }

        internal void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData, out int applyPassCount)
        {
            List<ShadowProjectorForLWRP> projectors;
            applyPassCount = 0;
            if (applyMethod != ApplyMethod.ByShadowProjectors)
            {
                return;
            }
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (0 < projectors.Count)
                {
                    applyPass.renderPassEvent = renderPassEvent;
                    renderer.EnqueuePass(applyPass);
                    ++applyPassCount;
                }
            }
        }
        private bool m_appliedToLightPass = false;
        private CollectShadowBufferPass.RenderTextureRef m_shadowTextureRef = null;
        private ColorWriteMask m_shadowTextureColorWriteMask = 0;
        internal void CollectShadowBuffer(ScriptableRenderContext context, ref RenderingData renderingData, CollectShadowBufferPass.RenderTextureRef textureRef, ColorWriteMask writeMask)
        {
            m_shadowTextureRef = textureRef;
            m_shadowTextureColorWriteMask = writeMask;
            textureRef.Retain(writeMask);
            List <ShadowProjectorForLWRP> projectors;
            bool collectedProjectorShadows = false;
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (projectors != null)
                {
                    for (int i = 0; i < projectors.Count; ++i)
                    {
                        projectors[i].CollectShadows(context, ref renderingData);
                    }
                    collectedProjectorShadows = 0 < projectors.Count;
                }
            }
            m_appliedToLightPass = false;
            if (applyMethod == ApplyMethod.ByLitShaders && shadowColor == ShadowColor.Monochrome && (collectedProjectorShadows || collectRealtimeShadows))
            {
                if (0 <= visibleLightIndex)
                {
                    bool collect = collectRealtimeShadows;
                    if (collect)
                    {
                        Light light = renderingData.lightData.visibleLights[visibleLightIndex].light;
                        if (light.shadows == LightShadows.None)
                        {
                            collect = false;
                        }
                        else if (light.bakingOutput.isBaked && light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                        {
                            collect = false;
                        }
                    }
                    if (collectedProjectorShadows || collect)
                    {
                        if (isMainLight)
                        {
                            LitShaderState.SetMainLightShadow(m_shadowTextureRef.renderTexture, shadowReceiverLayers);
                            m_appliedToLightPass = true;
                        }
                        else if (0 <= additionalLightIndex)
                        {
                            int channelIndex = 0;
                            for (int i = 0; i < 4; ++i)
                            {
                                if ((writeMask & (ColorWriteMask)(1 << i)) != 0)
                                {
                                    channelIndex = i;
                                    break;
                                }
                            }
                            m_appliedToLightPass = LitShaderState.SetAdditionalLightShadow(additionalLightIndex, m_shadowTextureRef.renderTexture, channelIndex, shadowReceiverLayers);
                        }
                    }
                }
            }
            if (applyMethod != ApplyMethod.ByShadowProjectors && m_appliedToLightPass)
            {
                ClearProjectosForCamera(renderingData.cameraData.camera);
            }
        }
        static readonly string[] KEYWORD_SHADOWTEX_CHANNELS = { "P4LWRP_SHADOWTEX_CHANNEL_A", "P4LWRP_SHADOWTEX_CHANNEL_B", "P4LWRP_SHADOWTEX_CHANNEL_G", "P4LWRP_SHADOWTEX_CHANNEL_R", "P4LWRP_SHADOWTEX_CHANNEL_RGB" };
#if UNITY_EDITOR
        Material m_copiedMaterial = null;
#endif
        internal void ApplyShadowBuffer(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            PerObjectData requiredPerObjectData = PerObjectData.None;

            bool appliedToLightPass = m_appliedToLightPass;
            m_appliedToLightPass = false;
            if (appliedToLightPass && shadowReceiverLayers == -1)
            {
                return;
            }
            Material applyShadowMaterial = material;
#if UNITY_EDITOR
            // do not use the original material so as not to make it dirty.
            if (m_copiedMaterial == null)
            {
                m_copiedMaterial = new Material(material);
            }
            applyShadowMaterial = m_copiedMaterial;
#endif
            if (shadowMaterialProperties != null && !shadowMaterialProperties.UpdateMaterialProperties(applyShadowMaterial, ref renderingData, out requiredPerObjectData))
            {
                return;
            }
            for (int i = 0; i < 4; ++i)
            {
                if (m_shadowTextureColorWriteMask == (ColorWriteMask)(1 << i))
                {
                    applyShadowMaterial.EnableKeyword(KEYWORD_SHADOWTEX_CHANNELS[i]);
                }
                else
                {
                    applyShadowMaterial.DisableKeyword(KEYWORD_SHADOWTEX_CHANNELS[i]);
                }
            }
            if (shadowColor == ShadowColor.Colored)
            {
                applyShadowMaterial.EnableKeyword(KEYWORD_SHADOWTEX_CHANNELS[4]);
            }
            else
            {
                applyShadowMaterial.DisableKeyword(KEYWORD_SHADOWTEX_CHANNELS[4]);
            }
            requiredPerObjectData |= perObjectData;
            List<ShadowProjectorForLWRP> projectors;
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (projectors != null && 0 < projectors.Count)
                {
                    int stencilMask = StencilMaskAllocator.AllocateSingleBit();
                    if (stencilMask == 0)
                    {
#if UNITY_EDITOR
                        Debug.LogError("No more available stencil bit. Skip shadow projector rendering.");
#endif
                        return;
                    }
                    applyShadowMaterial.SetTexture(m_shadowTextureId, GetTemporaryShadowTexture());
                    for (int i = 0; i < projectors.Count; ++i)
                    {
                        projectors[i].ApplyShadowBuffer(context, ref renderingData, applyShadowMaterial, requiredPerObjectData, appliedToLightPass ? (int)shadowReceiverLayers : 0, stencilMask);
                    }
                }
            }
        }
        internal void ClearProjectosForCamera(Camera camera)
        {
            List<ShadowProjectorForLWRP> projectors;
            if (m_projectors.TryGetValue(camera, out projectors))
            {
                if (projectors != null)
                {
                    projectors.Clear();
                }
            }
        }
        internal int colorWriteMask
        {
            get { return (int)m_shadowTextureColorWriteMask; }
        }
        internal Texture GetTemporaryShadowTexture()
        {
            if (m_shadowTextureRef == null)
            {
                return null;
            }
            return m_shadowTextureRef.renderTexture;
        }
        internal void ReleaseTemporaryShadowTexture()
        {
            if (m_shadowTextureRef != null)
            {
                m_shadowTextureRef.Release((ColorWriteMask)colorWriteMask);
            }
        }
    }
}
