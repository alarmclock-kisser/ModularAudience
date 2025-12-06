#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

extern "C" __global__ void timestretch01(
    const float2* input,
    float2* output,
    const int chunkSize,
    const int overlapSize,
    const int samplerate,
    const double factor)
{
    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    int chunk = blockIdx.y * blockDim.y + threadIdx.y;

    int hopIn = chunkSize - overlapSize;
    int idx = chunk * chunkSize + bin;
    int prevIdx = (chunk > 0) ? (chunk - 1) * chunkSize + bin : idx;

    if (bin >= chunkSize) return;
    
    if (chunk == 0) {
        output[idx] = input[idx];
        return;
    }

    float2 cur = input[idx];
    float2 prev = input[prevIdx];

    // Eigenimplementierung von atan2f
    float phaseCur, phasePrev;
    
    // atan2f f�r cur
    if (cur.x == 0.0f) {
        phaseCur = (cur.y > 0.0f) ? M_PI / 2.0f : -M_PI / 2.0f;
    } else {
        phaseCur = atan(cur.y / cur.x);
        if (cur.x < 0.0f) phaseCur += M_PI;
    }
    
    // atan2f f�r prev
    if (prev.x == 0.0f) {
        phasePrev = (prev.y > 0.0f) ? M_PI / 2.0f : -M_PI / 2.0f;
    } else {
        phasePrev = atan(prev.y / prev.x);
        if (prev.x < 0.0f) phasePrev += M_PI;
    }

    float mag = sqrt(cur.x * cur.x + cur.y * cur.y);
    float deltaPhase = phaseCur - phasePrev;
    float freqPerBin = (float)samplerate / (float)chunkSize;
    float expectedPhaseAdv = 2.0f * M_PI * freqPerBin * bin * hopIn / (float)samplerate;

    float delta = deltaPhase - expectedPhaseAdv;
    delta = fmod(delta + M_PI, 2.0f * M_PI) - M_PI;

    float phaseOut = phasePrev + expectedPhaseAdv + (float)((double)delta * factor);

    output[idx].x = mag * cos(phaseOut);
    output[idx].y = mag * sin(phaseOut);
}