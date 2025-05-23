using Data;
using Shaders;

namespace Materials.Systems.Tests
{
    public class ImportTests : MaterialSystemTests
    {
        [Test, CancelAfter(4000)]
        public async Task Import(CancellationToken cancellation)
        {
            const string MaterialJSON =
                @"
                {
                    ""vertex"": ""Assets/vertexShader.glsl"",
                    ""fragment"": ""Assets/fragmentShader.glsl""
                }";

            DataSource testMaterial = new(world, "Assets/testMaterial.json");
            testMaterial.WriteUTF8(MaterialJSON);

            Material material = new(world, "Assets/testMaterial.json");

            await material.UntilCompliant(Simulator.Update, cancellation);

            Assert.That(material.IsCompliant, Is.True);
            Shader vertexShader = material.VertexShader;
            Shader fragmentShader = material.FragmentShader;
            Assert.That(vertexShader.IsCompliant, Is.True);
            Assert.That(fragmentShader.IsCompliant, Is.True);
        }
    }
}
