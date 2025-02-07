using Collections;
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

        private MaterialImportSystem(Dictionary<ShaderKey, Shader> cachedShaders, Stack<Operation> operations)
        {
            this.cachedShaders = cachedShaders;
            this.operations = operations;
        }

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                systemContainer.Write(new MaterialImportSystem(new(), new()));
            }
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            LoadMaterials(world, systemContainer.simulator, delta);
            PerformInstructions(world);
        }

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                while (operations.TryPop(out Operation operation))
                {
                    operation.Dispose();
                }

                operations.Dispose();
                cachedShaders.Dispose();
            }
        }

        private readonly void PerformInstructions(World world)
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        private readonly void LoadMaterials(World world, Simulator simulator, TimeSpan delta)
        {
            ComponentType componentType = world.Schema.GetComponent<IsMaterialRequest>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.Contains(componentType))
                {
                    USpan<uint> entities = chunk.Entities;
                    USpan<IsMaterialRequest> components = chunk.GetComponents<IsMaterialRequest>(componentType);
                    for (uint i = 0; i < entities.Length; i++)
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

        private readonly bool TryLoadMaterial(Entity material, IsMaterialRequest request, Simulator simulator)
        {
            World world = material.world;
            LoadData message = new(world, request.address);
            if (simulator.TryHandleMessage(ref message))
            {
                if (message.IsLoaded)
                {

                    //todo: handle different formats, especially gltf
                    const string VertexProperty = "vertex";
                    const string FragmentProperty = "fragment";
                    using BinaryReader reader = new(message.Bytes);
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
                        operation.AddOrSetComponent(new IsMaterial(component.version + 1, vertexShaderReference, fragmentShaderReference));

                        if (!material.ContainsArray<PushBinding>())
                        {
                            operation.CreateArray<PushBinding>();
                        }

                        if (!material.ContainsArray<ComponentBinding>())
                        {
                            operation.CreateArray<ComponentBinding>();
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