using Collections.Generic;
using Data;
using Data.Messages;
using Materials.Components;
using Serialization.JSON;
using Shaders;
using Simulation;
using System;
using System.Diagnostics;
using Unmanaged;
using Worlds;

namespace Materials.Systems
{
    public class MaterialImportSystem : ISystem, IDisposable
    {
        private static readonly BlendFactor[] blendFactors = Enum.GetValues<BlendFactor>();
        private static readonly BlendOperation[] blendOperations = Enum.GetValues<BlendOperation>();
        private static readonly CompareOperation[] compareOperations = Enum.GetValues<CompareOperation>();
        private static readonly StencilOperation[] stencilOperations = Enum.GetValues<StencilOperation>();
        private static readonly string[] blendFactorOptions;
        private static readonly string[] blendOperationOptions;
        private static readonly string[] compareOperationOptions;
        private static readonly string[] stencilOperationOptions;

        static MaterialImportSystem()
        {
            blendFactorOptions = new string[blendFactors.Length];
            for (int i = 0; i < blendFactors.Length; i++)
            {
                blendFactorOptions[i] = blendFactors[i].ToString();
            }

            blendOperationOptions = new string[blendOperations.Length];
            for (int i = 0; i < blendOperations.Length; i++)
            {
                blendOperationOptions[i] = blendOperations[i].ToString();
            }

            compareOperationOptions = new string[compareOperations.Length];
            for (int i = 0; i < compareOperations.Length; i++)
            {
                compareOperationOptions[i] = compareOperations[i].ToString();
            }

            stencilOperationOptions = new string[stencilOperations.Length];
            for (int i = 0; i < stencilOperations.Length; i++)
            {
                stencilOperationOptions[i] = stencilOperations[i].ToString();
            }
        }

        private readonly Dictionary<ShaderKey, Shader> cachedShaders;
        private readonly Stack<Operation> operations;

        public MaterialImportSystem()
        {
            cachedShaders = new(4);
            operations = new(4);
        }

        public void Dispose()
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Dispose();
            }

            operations.Dispose();
            cachedShaders.Dispose();
        }

        void ISystem.Update(Simulator simulator, double deltaTime)
        {
            LoadMaterials(simulator, deltaTime);
            PerformInstructions(simulator.world);
        }

        private void PerformInstructions(World world)
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        private void LoadMaterials(Simulator simulator, double deltaTime)
        {
            World world = simulator.world;
            int componentType = world.Schema.GetComponentType<IsMaterialRequest>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(componentType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsMaterialRequest> components = chunk.GetComponents<IsMaterialRequest>(componentType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsMaterialRequest request = ref components[i];
                        Entity material = new(world, entities[i]);
                        if (request.status == IsMaterialRequest.Status.Submitted)
                        {
                            request.status = IsMaterialRequest.Status.Loading;
                            Trace.WriteLine($"Started searching data for material `{material}` with address `{request.address}`");
                        }

                        if (request.status == IsMaterialRequest.Status.Loading)
                        {
                            IsMaterialRequest dataRequest = request;
                            if (TryLoadMaterial(material, dataRequest, simulator))
                            {
                                Trace.WriteLine($"Material `{material}` has been loaded");

                                //todo: being done this way because reference to the request may have shifted
                                material.SetComponent(dataRequest.BecomeLoaded());
                            }
                            else
                            {
                                request.duration += deltaTime;
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
        }

        private bool TryLoadMaterial(Entity material, IsMaterialRequest request, Simulator simulator)
        {
            World world = material.world;
            LoadData message = new(world, request.address);
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
                    if (!cachedShaders.TryGetValue(vertexKey, out Shader vertexShader))
                    {
                        vertexShader = new(world, vertexAddress, ShaderType.Vertex);
                        cachedShaders.Add(vertexKey, vertexShader);
                    }

                    ShaderKey fragmentKey = new(world, fragmentAddress);
                    if (!cachedShaders.TryGetValue(fragmentKey, out Shader fragmentShader))
                    {
                        fragmentShader = new(world, fragmentAddress, ShaderType.Fragment);
                        cachedShaders.Add(fragmentKey, fragmentShader);
                    }

                    Operation operation = new();
                    operation.SelectEntity(material);
                    rint vertexShaderReference = material.AddReference(vertexShader);
                    rint fragmentShaderReference = material.AddReference(fragmentShader);

                    material.TryGetComponent(out IsMaterial component);

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

                    if (!material.ContainsArray<InstanceDataBinding>())
                    {
                        operation.CreateArray<InstanceDataBinding>();
                    }

                    if (!material.ContainsArray<EntityComponentBinding>())
                    {
                        operation.CreateArray<EntityComponentBinding>();
                    }

                    if (!material.ContainsArray<TextureBinding>())
                    {
                        operation.CreateArray<TextureBinding>();
                    }

                    operations.Push(operation);
                }
                else if (!hasVertexProperty && !hasFragmentProperty)
                {
                    throw new InvalidOperationException($"JSON data for material `{material}` has neither `{Vertex}` or `{Fragment}` properties");
                }
                else if (!hasVertexProperty)
                {
                    throw new InvalidOperationException($"JSON data for material `{material}` has no `{Vertex}` property");
                }
                else
                {
                    throw new InvalidOperationException($"JSON data for material `{material}` has no `{Fragment}` property");
                }

                return true;
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

        private static BlendFactor? GetBlendFactor(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < blendFactorOptions.Length; i++)
            {
                if (text.Equals(blendFactorOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return blendFactors[i];
                }
            }

            return null;
        }

        private static BlendOperation? GetBlendOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < blendOperationOptions.Length; i++)
            {
                if (text.Equals(blendOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return blendOperations[i];
                }
            }

            return null;
        }

        private static CompareOperation? GetCompareOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < compareOperationOptions.Length; i++)
            {
                if (text.Equals(compareOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return compareOperations[i];
                }
            }

            return null;
        }

        private static StencilOperation? GetStencilOperation(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < stencilOperationOptions.Length; i++)
            {
                if (text.Equals(stencilOperationOptions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return stencilOperations[i];
                }
            }

            return null;
        }
    }
}