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
    public readonly partial struct MaterialImportSystem : ISystem
    {
        private readonly Dictionary<ShaderKey, Shader> cachedShaders;
        private readonly Stack<Operation> operations;

        public MaterialImportSystem()
        {
            cachedShaders = new(4);
            operations = new(4);
        }

        public readonly void Dispose()
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Dispose();
            }

            operations.Dispose();
            cachedShaders.Dispose();
        }

        void ISystem.Start(in SystemContext context, in World world)
        {
        }

        void ISystem.Update(in SystemContext context, in World world, in TimeSpan delta)
        {
            LoadMaterials(world, context, delta);
            PerformInstructions(world);
        }

        void ISystem.Finish(in SystemContext context, in World world)
        {
        }

        private readonly void PerformInstructions(World world)
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        private readonly void LoadMaterials(World world, SystemContext context, TimeSpan delta)
        {
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
                            if (TryLoadMaterial(material, dataRequest, context))
                            {
                                Trace.WriteLine($"Material `{material}` has been loaded");

                                //todo: being done this way because reference to the request may have shifted
                                material.SetComponent(dataRequest.BecomeLoaded());
                            }
                            else
                            {
                                request.duration += delta;
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

        private readonly bool TryLoadMaterial(Entity material, IsMaterialRequest request, SystemContext context)
        {
            World world = material.world;
            LoadData message = new(world, request.address);
            if (context.TryHandleMessage(ref message) != default)
            {
                if (message.IsLoaded)
                {
                    //todo: handle different formats, especially gltf
                    const string VertexProperty = "vertex";
                    const string FragmentProperty = "fragment";
                    using ByteReader reader = new(message.Bytes);
                    using JSONObject jsonObject = reader.ReadObject<JSONObject>();
                    bool hasVertexProperty = jsonObject.Contains(VertexProperty);
                    bool hasFragmentProperty = jsonObject.Contains(FragmentProperty);
                    message.Dispose();
                    if (hasVertexProperty && hasFragmentProperty)
                    {
                        Address vertexAddress = new(jsonObject.GetText(VertexProperty));
                        Address fragmentAddress = new(jsonObject.GetText(FragmentProperty));
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

                        //todo: this material import operation isnt built properly, some operations
                        //are immediate others are deferred and it should all be deferred
                        Operation operation = new();
                        operation.SelectEntity(material);
                        rint vertexShaderReference = material.AddReference(vertexShader);
                        rint fragmentShaderReference = material.AddReference(fragmentShader);

                        material.TryGetComponent(out IsMaterial component);

                        //todo: the flags and depth compare op are always assigned even if material component is already present
                        MaterialFlags flags = MaterialFlags.DepthTest | MaterialFlags.DepthWrite;
                        CompareOperation depthCompareOperation = CompareOperation.LessOrEqual;
                        component = component.WithFlags(flags);
                        component = component.WithDepthCompareOperation(depthCompareOperation);

                        operation.AddOrSetComponent(component.WithShaderReferences(vertexShaderReference, fragmentShaderReference));

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
                        throw new InvalidOperationException($"JSON data for material `{material}` has neither `{VertexProperty}` or `{FragmentProperty}` properties");
                    }
                    else if (!hasVertexProperty)
                    {
                        throw new InvalidOperationException($"JSON data for material `{material}` has no `{VertexProperty}` property");
                    }
                    else
                    {
                        throw new InvalidOperationException($"JSON data for material `{material}` has no `{FragmentProperty}` property");
                    }

                    return true;
                }
            }

            return false;
        }
    }
}