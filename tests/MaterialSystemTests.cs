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
            Simulator.Add(new DataImportSystem());
            Simulator.Add(new MaterialImportSystem());
            Simulator.Add(new ShaderImportSystem());
        }

        protected override void TearDown()
        {
            Simulator.Remove<ShaderImportSystem>();
            Simulator.Remove<MaterialImportSystem>();
            Simulator.Remove<DataImportSystem>();
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
