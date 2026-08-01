#version 450

// Einfaches GLSL Shader für Vulkan
// Input: float[] Buffer als Inputptr
// Arg: float level

layout(std140, binding = 0) uniform UArgs {
    float level;
};

layout(location = 0) in float input;
out float fragColor;

void main() {
    fragColor = input / level;
}
