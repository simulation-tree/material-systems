#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUv;

layout(push_constant) uniform EntityData {
    vec4 color;
	mat4 model;
} entity;

layout(binding = 2) uniform CameraInfo {
	mat4 proj;
    mat4 view;
} cameraInfo;

layout(location = 0) out vec4 fragColor;
layout(location = 1) out vec2 uv;

out gl_PerVertex 
{
    vec4 gl_Position;   
};

void main() {
    gl_Position = cameraInfo.proj * cameraInfo.view * entity.model * vec4(inPosition, 1.0);
    fragColor = entity.color;
    uv = inUv;
}