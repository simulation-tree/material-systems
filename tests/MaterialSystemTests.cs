using Data;
using Data.Systems;
using Shaders;
using Shaders.Systems;
using Simulation.Tests;
using Types;
using Worlds;

namespace Materials.Systems.Tests
{
    public abstract class MaterialSystemTests : SimulationTests
    {
        static MaterialSystemTests()
        {
            MetadataRegistry.Load<DataMetadataBank>();
            MetadataRegistry.Load<MaterialsMetadataBank>();
            MetadataRegistry.Load<ShadersMetadataBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.AddSystem(new DataImportSystem());
            simulator.AddSystem(new MaterialImportSystem());
            simulator.AddSystem(new ShaderImportSystem());
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<DataSchemaBank>();
            schema.Load<MaterialsSchemaBank>();
            schema.Load<ShadersSchemaBank>();
            return schema;
        }
    }
}
