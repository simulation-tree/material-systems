using Collections.Generic;
using Data;
using Data.Messages;
using JSON;
using Materials.Components;
using Shaders;
using Simulation;
using System;
using System.Diagnostics;
using Unmanaged;
using Worlds;

namespace Materials.Systems
{
    using static Constants;

    public partial class MaterialImportSystem : SystemBase, IListener<DataUpdate>
    {
        private readonly World world;
        private readonly Dictionary<ShaderKey, uint> cachedShaders;
        private readonly Operation operation;
        private readonly int requestType;
        private readonly int materialType;

        public MaterialImportSystem(Simulator simulator, World world) : base(simulator)
        {
            this.world = world;
            cachedShaders = new(4);
            operation = new(world);

            Schema schema = world.Schema;
            requestType = schema.GetComponentType<IsMaterialRequest>();
            materialType = schema.GetComponentType<IsMaterial>();
        }

        public override void Dispose()
        {
            operation.Dispose();
            cachedShaders.Dispose();
        }

        void IListener<DataUpdate>.Receive(ref DataUpdate message)
        {
            ReadOnlySpan<Chunk> chunks = world.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                Chunk chunk = chunks[c];
                if (chunk.Definition.ContainsComponent(requestType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsMaterialRequest> components = chunk.GetComponents<IsMaterialRequest>(requestType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsMaterialRequest request = ref components[i];
                        uint material = entities[i];
                        if (request.status == IsMaterialRequest.Status.Submitted)
                        {
                            request.status = IsMaterialRequest.Status.Loading;
                            Trace.WriteLine($"Started searching data for material `{material}` with address `{request.address}`");
                        }

                        if (request.status == IsMaterialRequest.Status.Loading)
                        {
                            if (TryLoadMaterial(material, request))
                            {
                                Trace.WriteLine($"Material `{material}` has been loaded");
                                request.status = IsMaterialRequest.Status.Loaded;

                                //reset iteration loop because entities were created, meaning chunks have changed
                                c = -1;
                                chunks = world.Chunks;
                                break;
                            }
                            else
                            {
                                request.duration += message.deltaTime;
                                if (request.duration >= request.timeout)
                                {
                                    Trace.TraceError($"Material `{material}` could not be loaded");
                                    request.status = IsMaterialRequest.Status.NotFound;
                                }
                            }
                        }
                    }
                }
            }

            if (operation.TryPerform())
            {
                operation.Reset();
            }
        }

        private bool TryLoadMaterial(uint materialEntity, IsMaterialRequest request)
        {
            LoadData message = new(request.address);
            simulator.Broadcast(ref message);
            if (message.TryConsume(out ByteReader data))
            {
                //todo: handle different formats, especially gltf
                const string Vertex = "vertex";
                const string Fragment = "fragment";
                using JSONObject jsonObject = data.ReadObject<JSONObject>();
                bool hasVertexProperty = jsonObject.TryGetText(Vertex, out ReadOnlySpan<char> vertexText);
                bool hasFragmentProperty = jsonObject.TryGetText(Fragment, out ReadOnlySpan<char> fragmentText);
                data.Dispose();
                if (hasVertexProperty && hasFragmentProperty)
                {
                    Address vertexAddress = new(vertexText);
                    Address fragmentAddress = new(fragmentText);
                    ShaderKey vertexKey = new(world, vertexAddress);
                    if (!cachedShaders.TryGetValue(vertexKey, out uint vertexShader))
                    {
                        vertexShader = new Shader(world, vertexAddress, ShaderType.Vertex).GetEntityValue();
                        cachedShaders.Add(vertexKey, vertexShader);
                    }

                    ShaderKey fragmentKey = new(world, fragmentAddress);
                    if (!cachedShaders.TryGetValue(fragmentKey, out uint fragmentShader))
                    {
                        fragmentShader = new Shader(world, fragmentAddress, ShaderType.Fragment).GetEntityValue();
                        cachedShaders.Add(fragmentKey, fragmentShader);
                    }

                    operation.SetSelectedEntity(materialEntity);
                    rint vertexShaderReference = world.AddReference(materialEntity, vertexShader);
                    rint fragmentShaderReference = world.AddReference(materialEntity, fragmentShader);

                    world.TryGetComponent(materialEntity, materialType, out IsMaterial component);

                    const string RenderOrder = "renderOrder";
                    if (jsonObject.TryGetNumber(RenderOrder, out double renderOrder))
                    {
                        component.renderGroup = (sbyte)renderOrder;
                    }

                    component.vertexShaderReference = vertexShaderReference;
                    component.fragmentShaderReference = fragmentShaderReference;
                    component.blendSettings = GetBlendSettings(jsonObject);
                    component.depthSettings = GetDepthSettings(jsonObject);
                    operation.AddOrSetComponent(component);

                    if (!world.ContainsArray<PushConstantBinding>(materialEntity))
                    {
                        operation.CreateArray<PushConstantBinding>();
                    }

                    if (!world.ContainsArray<EntityComponentBinding>(materialEntity))
                    {
                        operation.CreateArray<EntityComponentBinding>();
                    }

                    if (!world.ContainsArray<TextureBinding>(materialEntity))
                    {
                        operation.CreateArray<TextureBinding>();
                    }

                    return true;
                }
                else if (!hasVertexProperty && !hasFragmentProperty)
                {
                    throw new InvalidOperationException($"JSON data for material `{materialEntity}` has neither `{Vertex}` or `{Fragment}` properties");
                }
                else if (!hasVertexProperty)
                {
                    throw new InvalidOperationException($"JSON data for material `{materialEntity}` has no `{Vertex}` property");
                }
                else
                {
                    throw new InvalidOperationException($"JSON data for material `{materialEntity}` has no `{Fragment}` property");
                }
            }

            return false;
        }

        private static DepthSettings GetDepthSettings(JSONObject jsonObject)
        {
            const string DepthTest = "depthTest";
            const string DepthWrite = "depthWrite";
            const string DepthBoundsTest = "depthBoundsTest";
            const string StencilTest = "stencilTest";
            const string CompareOperation = "compareOperation";
            const string MinDepthBounds = "minDepthBounds";
            const string MaxDepthBounds = "maxDepthBounds";
            const string Front = "front";
            const string Back = "back";

            DepthSettings settings = DepthSettings.Default;
            if (jsonObject.TryGetBoolean(DepthTest, out bool depthTest) && depthTest)
            {
                settings.flags |= DepthSettings.Flags.DepthTest;
            }

            if (jsonObject.TryGetBoolean(DepthWrite, out bool depthWrite) && depthWrite)
            {
                settings.flags |= DepthSettings.Flags.DepthWrite;
            }

            if (jsonObject.TryGetBoolean(DepthBoundsTest, out bool depthBoundsTest) && depthBoundsTest)
            {
                settings.flags |= DepthSettings.Flags.DepthBoundsTest;
            }

            if (jsonObject.TryGetBoolean(StencilTest, out bool stencilTest) && stencilTest)
            {
                settings.flags |= DepthSettings.Flags.StencilTest;
            }

            if (jsonObject.TryGetText(CompareOperation, out ReadOnlySpan<char> compareOperation))
            {
                settings.compareOperation = GetCompareOperation(compareOperation) ?? throw new($"Unrecognized compare operation value `{compareOperation.ToString()}`");
            }

            if (jsonObject.TryGetNumber(MinDepthBounds, out double minDepthBounds))
            {
                settings.minDepth = (float)minDepthBounds;
            }

            if (jsonObject.TryGetNumber(MaxDepthBounds, out double maxDepthBounds))
            {
                settings.maxDepth = (float)maxDepthBounds;
            }

            if (jsonObject.TryGetObject(Front, out JSONObject front))
            {
                settings.front = GetStencilSettings(front);
            }

            if (jsonObject.TryGetObject(Back, out JSONObject back))
            {
                settings.back = GetStencilSettings(back);
            }

            return settings;
        }

        private static StencilSettings GetStencilSettings(JSONObject jsonObject)
        {
            const string FailOperation = "failOperation";
            const string PassOperation = "passOperation";
            const string DepthFailOperation = "depthFailOperation";
            const string CompareOperation = "compareOperation";
            const string CompareMask = "compareMask";
            const string WriteMask = "writeMask";
            const string ReferenceMask = "referenceMask";

            StencilSettings settings = StencilSettings.Default;
            if (jsonObject.TryGetText(FailOperation, out ReadOnlySpan<char> failOperation))
            {
                settings.failOperation = GetStencilOperation(failOperation) ?? throw new($"Unrecognized fail operation value `{failOperation.ToString()}`");
            }

            if (jsonObject.TryGetText(PassOperation, out ReadOnlySpan<char> passOperation))
            {
                settings.passOperation = GetStencilOperation(passOperation) ?? throw new($"Unrecognized pass operation value `{failOperation.ToString()}`");
            }

            if (jsonObject.TryGetText(DepthFailOperation, out ReadOnlySpan<char> depthFailOperation))
            {
                settings.depthFailOperation = GetStencilOperation(depthFailOperation) ?? throw new($"Unrecognized depth fail operation value `{failOperation.ToString()}`");
            }

            if (jsonObject.TryGetText(CompareOperation, out ReadOnlySpan<char> compareOperation))
            {
                settings.compareOperation = GetCompareOperation(compareOperation) ?? throw new($"Unrecognized compare operation value `{failOperation.ToString()}`");
            }

            if (jsonObject.TryGetNumber(CompareMask, out double compareMask))
            {
                settings.compareMask = (uint)compareMask;
            }

            if (jsonObject.TryGetNumber(WriteMask, out double writeMask))
            {
                settings.writeMask = (uint)writeMask;
            }

            if (jsonObject.TryGetNumber(ReferenceMask, out double referenceMask))
            {
                settings.compareMask = (uint)referenceMask;
            }

            return settings;
        }

        private static BlendSettings GetBlendSettings(JSONObject jsonObject)
        {
            const string BlendEnable = "blendEnable";
            const string SourceColorBlend = "sourceColorBlend";
            const string DestinationColorBlend = "destinationColorBlend";
            const string ColorBlendOperation = "colorBlendOperation";
            const string SourceAlphaBlend = "sourceAlphaBlend";
            const string DestinationAlphaBlend = "destinationAlphaBlend";
            const string AlphaBlendOperation = "alphaBlendOperation";

            BlendSettings settings = BlendSettings.Opaque;
            if (jsonObject.TryGetBoolean(BlendEnable, out bool blendEnable))
            {
                settings.blendEnable = blendEnable;
            }

            if (jsonObject.TryGetText(SourceColorBlend, out ReadOnlySpan<char> sourceColorBlend))
            {
                settings.sourceColorBlend = GetBlendFactor(sourceColorBlend) ?? throw new($"Unrecognized blend factor value `{sourceColorBlend.ToString()}`");
            }

            if (jsonObject.TryGetText(DestinationColorBlend, out ReadOnlySpan<char> destinationColorBlend))
            {
                settings.destinationColorBlend = GetBlendFactor(destinationColorBlend) ?? throw new($"Unrecognized blend factor value `{destinationColorBlend.ToString()}`");
            }

            if (jsonObject.TryGetText(ColorBlendOperation, out ReadOnlySpan<char> colorBlendOperation))
            {
                settings.colorBlendOperation = GetBlendOperation(colorBlendOperation) ?? throw new($"Unrecognized blend operation value `{colorBlendOperation.ToString()}`");
            }

            if (jsonObject.TryGetText(SourceAlphaBlend, out ReadOnlySpan<char> sourceAlphaBlend))
            {
                settings.sourceAlphaBlend = GetBlendFactor(sourceAlphaBlend) ?? throw new($"Unrecognized blend factor value `{sourceAlphaBlend.ToString()}`");
            }

            if (jsonObject.TryGetText(DestinationAlphaBlend, out ReadOnlySpan<char> destinationAlphaBlend))
            {
                settings.destinationAlphaBlend = GetBlendFactor(destinationAlphaBlend) ?? throw new($"Unrecognized blend factor value `{destinationAlphaBlend.ToString()}`");
            }

            if (jsonObject.TryGetText(AlphaBlendOperation, out ReadOnlySpan<char> alphaBlendOperation))
            {
                settings.alphaBlendOperation = GetBlendOperation(alphaBlendOperation) ?? throw new($"Unrecognized blend operation value `{alphaBlendOperation.ToString()}`");
            }

            return settings;
        }
    }
}