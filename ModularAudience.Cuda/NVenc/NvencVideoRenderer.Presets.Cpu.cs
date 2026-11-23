#nullable enable
using System;

namespace ModularAudience.Cuda
{
    public static partial class NvencVideoRenderer
    {
        public static class CpuPresets
        {
            // ------------------------------------------------------------
            // H.264 CPU (libx264)
            // ------------------------------------------------------------

            // Sehr schnell, große Files, gut für Preview/Debug
            public static NvencOptions X264_UltrafastPreview => new(
                VideoCodec: "libx264",
                Preset: "ultrafast",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 23,
                Profile: "high",
                FastStart: true
            );

            // “Guter Standard” für Qualität/Speed
            public static NvencOptions X264_Default => new(
                VideoCodec: "libx264",
                Preset: "medium",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 20,
                Profile: "high",
                FastStart: true
            );

            // Kleinere Dateien (mehr CPU)
            public static NvencOptions X264_Smaller => new(
                VideoCodec: "libx264",
                Preset: "slow",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 22,
                Profile: "high",
                FastStart: true
            );

            // ------------------------------------------------------------
            // H.265 CPU (libx265)
            // ------------------------------------------------------------

            // Guter Standard (H.265 braucht mehr CPU)
            public static NvencOptions X265_Default => new(
                VideoCodec: "libx265",
                Preset: "medium",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 26,
                Profile: "main",
                FastStart: true
            );

            // Besser komprimiert, langsamer
            public static NvencOptions X265_Smaller => new(
                VideoCodec: "libx265",
                Preset: "slow",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 28,
                Profile: "main",
                FastStart: true
            );

            // ------------------------------------------------------------
            // AV1 CPU (SVT-AV1) – wenn dein ffmpeg das drin hat
            // ------------------------------------------------------------

            // Hinweis: SVT-AV1 Preset ist in ffmpeg i.d.R. ein "0..13" (string ok).
            // 8 ist oft ein brauchbarer Speed/Quali-Tradeoff.
            public static NvencOptions Av1_Svt_Default => new(
                VideoCodec: "libsvtav1",
                Preset: "8",
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: null,
                Crf: 35,
                Profile: null,
                FastStart: true
            );
        }
    }
}
