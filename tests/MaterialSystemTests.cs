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
            simulator.Add(new DataImportSystem());
            simulator.Add(new MaterialImportSystem());
            simulator.Add(new ShaderImportSystem());
        }

        protected override void TearDown()
        {
            simulator.Remove<ShaderImportSystem>();
            simulator.Remove<MaterialImportSystem>();
            simulator.Remove<DataImportSystem>();
            base.TearDown();
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
