#version 450

layout(location = 0) in vec4 fragColor;
layout(location = 1) in vec2 uv;
layout(binding = 3) uniform sampler2D mainTexture;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel = texture(mainTexture, uv);
    outColor = fragColor * texel.a;
}