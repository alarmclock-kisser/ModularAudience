extern "C"
__global__ void ApplyGain(float* __restrict data, int n, float gain)
{
    // Grid-stride loop für robustes Scheduling über große Arrays
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    int stride = blockDim.x * gridDim.x;

    // Unrolling-Hinweis: NVRTC/PTX-Compiler wird oft selbst optimieren;
    // wir halten den Code bewusst minimal.
    for (int i = tid; i < n; i += stride)
    {
        // einfache skalierung; __restrict hilft dem Compiler bei Alias-Analyse
        float v = data[i];
        data[i] = v * gain;
    }
}

// Optionaler, separater Kernel zum Clipping in [-1, 1]
extern "C"
__global__ void HardClip(
float* __restrict data,
int n)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    int stride = blockDim.x * gridDim.x;

    for (int i = tid; i < n; i += stride)
    {
        float v = data[i];
        // branchless clamp mit fminf/fmaxf vermeiden wir absichtl., da NVRTC Math-Funktionen unterschiedlich handhabt.
        // Einfache ifs sind auf modernen GPUs sehr effizient.
        if (v > 1.0f) v = 1.0f;
        else if (v < -1.0f) v = -1.0f;
        data[i] = v;
    }
}