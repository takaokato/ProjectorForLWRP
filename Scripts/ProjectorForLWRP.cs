﻿//
// ProjectorForLWRP.cs
//
// Projector For LWRP
//
// Copyright (c) 2019 NYAHOON GAMES PTE. LTD.
//

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;
using System.Collections.Generic;

namespace ProjectorForLWRP
{
	[ExecuteInEditMode]
	[RequireComponent(typeof(Projector))]
	public class ProjectorForLWRP : ProjectorForSRP.ProjectorForSRP, ICustomRenderer
	{
		// serialize field
		[Header("Receiver Object Filter")]
		[SerializeField]
		private string[] m_shaderTagList = new string[] { "LightweightForward", "SRPDefaultUnlit" };
		[SerializeField]
		private int m_renderQueueLowerBound = RenderQueueRange.opaque.lowerBound;
		[SerializeField]
		private int m_renderQueueUpperBound = RenderQueueRange.opaque.upperBound;

		[Header("Projector Rendering")]
		[SerializeField]
		private RenderPassEvent m_renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
		[SerializeField]
		private PerObjectData m_perObjectData = PerObjectData.None;
		[SerializeField]
		[HideInInspector]
		private Material m_stencilPass = null;

		// public properties
		public int renderQueueLowerBound
		{
			get { return m_renderQueueLowerBound; }
			set { m_renderQueueLowerBound = value; }
		}
		public int renderQueueUpperBound
		{
			get { return m_renderQueueUpperBound; }
			set { m_renderQueueUpperBound = value; }
		}
		public RenderPassEvent renderPassEvent
		{
			get { return m_renderPassEvent; }
			set { m_renderPassEvent = value; }
		}
		public PerObjectData perObjectData
		{
			get { return m_perObjectData; }
			set { m_perObjectData = value; }
		}
		public bool useStencilTest
		{
			get { return m_stencilPass != null; }
		}
		public Material stencilPassMaterial
		{
			get { return m_stencilPass; }
			set {
				bool wasNull = (m_stencilPass == null);
				m_stencilPass = value;
				if (value != null && m_meshFrustum == null)
				{
					m_meshFrustum = new Mesh();
					m_meshFrustum.hideFlags = HideFlags.HideAndDontSave;
				}
				if (wasNull && value != null)
				{
					SetProjectorFrustumVerticesToMesh(m_meshFrustum);
				}
			}
		}

		protected virtual void OnValidate()
		{
			if (useStencilTest)
			{
				if (m_meshFrustum == null)
				{
					m_meshFrustum = new Mesh();
					m_meshFrustum.hideFlags = HideFlags.HideAndDontSave;
				}
				SetProjectorFrustumVerticesToMesh(m_meshFrustum);
			}
		}

		protected override void OnProjectorFrustumChanged()
		{
			if (useStencilTest)
			{
				SetProjectorFrustumVerticesToMesh(m_meshFrustum);
			}
		}

		public void CopySerializedPropertiesFrom(ProjectorForLWRP src)
		{
			renderPassEvent = src.renderPassEvent;
			renderQueueLowerBound = src.renderQueueLowerBound;
			renderQueueUpperBound = src.renderQueueUpperBound;
			perObjectData = src.perObjectData;
			stencilPassMaterial = src.stencilPassMaterial;
		}

		private Mesh m_meshFrustum;

		private ShaderTagId[] m_shaderTagIdList;
		public void UpdateShaderTagIdList()
		{
			if (m_shaderTagList == null || m_shaderTagList.Length == 0)
			{
				if (m_shaderTagIdList == null || m_shaderTagIdList.Length != 1)
				{
					m_shaderTagIdList = new ShaderTagId[1];
				}
				m_shaderTagIdList[0] = ShaderTagId.none;
			}
			else
			{
				if (m_shaderTagIdList == null || m_shaderTagIdList.Length != m_shaderTagList.Length)
				{
					m_shaderTagIdList = new ShaderTagId[m_shaderTagList.Length];
				}
				for (int i = 0; i < m_shaderTagList.Length; ++i)
				{
					m_shaderTagIdList[i] = new ShaderTagId(m_shaderTagList[i]);
				}
			}
		}
		private static bool s_isInitialized = false;
		static protected new void StaticInitialize()
		{
			if (!s_isInitialized)
			{
				ProjectorForSRP.ProjectorForSRP.StaticInitialize();
				s_isInitialized = true;
			}
		}

		static ProjectorForLWRP()
		{
			StaticInitialize();
		}

		protected override void Initialize()
		{
			if (useStencilTest && m_meshFrustum == null)
			{
				m_meshFrustum = new Mesh();
				m_meshFrustum.hideFlags = HideFlags.HideAndDontSave;
			}
			base.Initialize();
			if (m_shaderTagIdList == null)
			{
				UpdateShaderTagIdList();
			}
		}

		protected override void AddProjectorToRenderer(Camera camera)
		{
			CustomRendererPassManager.staticInstance.AddCustomRenderer(camera, this);
		}

		private static new void DestroyObject(Object obj)
		{
			if (obj != null)
			{
#if UNITY_EDITOR
				DestroyImmediate(obj);
#else
				Destroy(obj);
#endif
			}
		}

		protected override void Cleanup()
		{
			if (m_meshFrustum != null)
			{
				DestroyObject(m_meshFrustum);
				m_meshFrustum = null;
			}
		}

		CommandBuffer m_stencilPassCommands = null;
		private MaterialPropertyBlock m_stencilProperties = null;
		protected void WriteFrustumStencil(ScriptableRenderContext context)
		{
			int stencilMask = StencilMaskAllocator.GetTemporaryBit();
			if (stencilMask == 0)
			{
				return;
			}
			if (m_stencilProperties == null)
			{
				m_stencilProperties = new MaterialPropertyBlock();
			}
			P4LWRPShaderProperties.p4lwrp_StencilRef.Set(m_stencilProperties, stencilMask);
			P4LWRPShaderProperties.p4lwrp_StencilMask.Set(m_stencilProperties, stencilMask);
			if (m_stencilPassCommands == null)
			{
				m_stencilPassCommands = new CommandBuffer();
			}
			m_stencilPassCommands.Clear();
			m_stencilPassCommands.DrawMesh(m_meshFrustum, transform.localToWorldMatrix, stencilPassMaterial, 0, 0, m_stencilProperties);
			m_stencilPassCommands.DrawMesh(m_meshFrustum, transform.localToWorldMatrix, stencilPassMaterial, 0, 1, m_stencilProperties);
			context.ExecuteCommandBuffer(m_stencilPassCommands);
		}
		protected static void SetupCullingResultsForRendering(ref RenderingData renderingData, ref CullingResults cullingResults, PerObjectData perObjectData)
		{
			if (0 < renderingData.lightData.additionalLightsCount && (perObjectData & PerObjectData.LightIndices) != 0)
			{
				cullingResults.visibleLights.CopyFrom(renderingData.cullResults.visibleLights);
				var lightIndexMap = renderingData.cullResults.GetLightIndexMap(Unity.Collections.Allocator.Temp);
				cullingResults.SetLightIndexMap(lightIndexMap);
				lightIndexMap.Dispose();
			}
			if ((perObjectData & PerObjectData.ReflectionProbes) != 0)
			{
				cullingResults.visibleReflectionProbes.CopyFrom(renderingData.cullResults.visibleReflectionProbes);
				var indexMap = renderingData.cullResults.GetReflectionProbeIndexMap(Unity.Collections.Allocator.Temp);
				cullingResults.SetReflectionProbeIndexMap(indexMap);
				indexMap.Dispose();
			}
		}
		protected void GetDefaultDrawSettings(ref RenderingData renderingData, Material material, out DrawingSettings drawingSettings, out FilteringSettings filteringSettings, out RenderStateBlock renderStateBlock)
		{
			base.GetDefaultDrawSettings(renderingData.cameraData.camera, material, m_shaderTagIdList, out drawingSettings, out filteringSettings);
			drawingSettings.enableDynamicBatching = renderingData.supportsDynamicBatching;
			drawingSettings.perObjectData = perObjectData;

			filteringSettings.renderQueueRange = new RenderQueueRange(m_renderQueueLowerBound, m_renderQueueUpperBound);
			renderStateBlock = new RenderStateBlock();
			if (useStencilTest)
			{
				int stencilMask = StencilMaskAllocator.GetTemporaryBit();
				if (stencilMask != 0)
				{
					renderStateBlock.mask = RenderStateMask.Stencil;
					renderStateBlock.stencilReference = (int)stencilMask;
					renderStateBlock.stencilState = new StencilState(true, (byte)stencilMask, (byte)stencilMask, CompareFunction.Equal, StencilOp.Zero, StencilOp.Keep, StencilOp.Keep);
				}
			}
		}
		public virtual void Render(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			CullingResults cullingResults;
			if (!TryGetCullingResults(renderingData.cameraData.camera, out cullingResults))
			{
				return;
			}
			Material material = GetDuplicatedProjectorMaterial();
			EnableProjectorForLWRPKeyword(material);
			SetupProjectorMatrix(material);

			if (useStencilTest)
			{
				WriteFrustumStencil(context);
			}

			SetupCullingResultsForRendering(ref renderingData, ref cullingResults, perObjectData);
			DrawingSettings drawingSettings;
			FilteringSettings filteringSettings;
			RenderStateBlock renderStateBlock;
			GetDefaultDrawSettings(ref renderingData, material, out drawingSettings, out filteringSettings, out renderStateBlock);
			context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
		}
	}
}
