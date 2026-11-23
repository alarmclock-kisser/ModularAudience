#nullable enable
using System.Collections.Generic;

namespace ModularAudience.Cuda
{
    public enum UiLanguage
    {
        Deutsch,
        English
    }

    public static partial class NvencVideoRenderer
    {
        public static UiLanguage CurrentUiLanguage { get; set; } = UiLanguage.English;

        // ============================================================
        // Public API – UI benutzt NUR DAS hier
        // ============================================================
        public static bool TryGetPresetDescription(
            string presetKey,
            out PresetDescription description)
        {
            if (!_presetBase.TryGetValue(presetKey, out var baseDesc))
            {
                description = default!;
                return false;
            }

            description = new PresetDescription(
                HardwareTech: baseDesc.GetHardwareText(CurrentUiLanguage),
                QualityScore: baseDesc.QualityScore,
                SpeedScore: baseDesc.SpeedScore
            );

            return true;
        }

        // ============================================================
        // Internal storage (sprachneutral)
        // ============================================================
        private static readonly IReadOnlyDictionary<string, PresetDescriptionBase> _presetBase
            = new Dictionary<string, PresetDescriptionBase>
            {
                // ------------------------------------------------------------
                // AV1 (CPU – SVT-AV1)
                // ------------------------------------------------------------
                ["AV1_SVT_DEFAULT"] = new(
                HardwareTech_EN: "CPU-based AV1 encoder (SVT-AV1) with very modern compression and no GPU acceleration.",
                HardwareTech_DE: "CPU-basierter AV1-Encoder (SVT-AV1) mit sehr moderner Kompression ohne GPU-Beschleunigung.",
                QualityScore: 92,
                SpeedScore: 15
            ),

                // ------------------------------------------------------------
                // H.264 CPU (x264)
                // ------------------------------------------------------------
                ["X264_DEFAULT"] = new(
                HardwareTech_EN: "CPU-based libx264 encoder, highly optimized and widely compatible.",
                HardwareTech_DE: "CPU-basierter libx264-Encoder, hochoptimiert und sehr kompatibel.",
                QualityScore: 85,
                SpeedScore: 55
            ),

                ["X264_SMALLER"] = new(
                HardwareTech_EN: "CPU-based libx264 using a slower preset for improved compression efficiency.",
                HardwareTech_DE: "CPU-basierter libx264 mit langsamerem Preset für bessere Kompression.",
                QualityScore: 88,
                SpeedScore: 35
            ),

                ["X264_ULTRAFASTPREVIEW"] = new(
                HardwareTech_EN: "CPU-based libx264 in ultrafast mode with minimal analysis for maximum speed.",
                HardwareTech_DE: "CPU-basierter libx264 im Ultrafast-Modus mit minimaler Analyse für maximale Geschwindigkeit.",
                QualityScore: 60,
                SpeedScore: 90
            ),

                // ------------------------------------------------------------
                // H.265 CPU (x265)
                // ------------------------------------------------------------
                ["X265_DEFAULT"] = new(
                HardwareTech_EN: "CPU-based libx265 (HEVC) encoder offering high efficiency at higher computational cost.",
                HardwareTech_DE: "CPU-basierter libx265-Encoder (HEVC) mit hoher Effizienz und höherem Rechenaufwand.",
                QualityScore: 90,
                SpeedScore: 30
            ),

                ["X265_SMALLER"] = new(
                HardwareTech_EN: "CPU-based libx265 using slower presets for maximum compression efficiency.",
                HardwareTech_DE: "CPU-basierter libx265 mit langsameren Presets für maximale Kompressionseffizienz.",
                QualityScore: 93,
                SpeedScore: 18
            ),

                // ------------------------------------------------------------
                // DNxHD (Editing)
                // ------------------------------------------------------------
                ["DNXHD_145_1080P"] = new(
                HardwareTech_EN: "CPU-based DNxHD intraframe encoder optimized for professional video editing.",
                HardwareTech_DE: "CPU-basierter DNxHD-Intraframe-Encoder, optimiert für professionellen Videoschnitt.",
                QualityScore: 78,
                SpeedScore: 70
            ),

                // ------------------------------------------------------------
                // Apple ProRes
                // ------------------------------------------------------------
                ["PRORES_422HQ"] = new(
                HardwareTech_EN: "CPU-based ProRes 422 HQ intraframe encoder for high-quality editing workflows.",
                HardwareTech_DE: "CPU-basierter ProRes-422-HQ-Intraframe-Encoder für hochwertige Schnitt-Workflows.",
                QualityScore: 88,
                SpeedScore: 65
            ),

                ["PRORES_422LT"] = new(
                HardwareTech_EN: "CPU-based ProRes 422 LT intraframe encoder with reduced data rate.",
                HardwareTech_DE: "CPU-basierter ProRes-422-LT-Intraframe-Encoder mit reduzierter Datenrate.",
                QualityScore: 82,
                SpeedScore: 70
            ),

                ["PRORES_4444"] = new(
                HardwareTech_EN: "CPU-based ProRes 4444 encoder with full color precision and alpha channel support.",
                HardwareTech_DE: "CPU-basierter ProRes-4444-Encoder mit voller Farbauflösung und Alpha-Kanal.",
                QualityScore: 95,
                SpeedScore: 55
            ),

                // ------------------------------------------------------------
                // NVIDIA NVENC (GPU)
                // ------------------------------------------------------------
                ["H264DEFAULT"] = new(
                HardwareTech_EN: "NVIDIA NVENC H.264 hardware encoder running on the GPU with minimal CPU usage.",
                HardwareTech_DE: "NVIDIA-NVENC-H.264-Hardwareencoder auf der GPU mit minimaler CPU-Last.",
                QualityScore: 80,
                SpeedScore: 95
            ),

                ["HEVCDEFAULT"] = new(
                HardwareTech_EN: "NVIDIA NVENC HEVC (H.265) hardware encoder with good efficiency and very high speed.",
                HardwareTech_DE: "NVIDIA-NVENC-HEVC-(H.265)-Hardwareencoder mit guter Effizienz und sehr hoher Geschwindigkeit.",
                QualityScore: 85,
                SpeedScore: 90
            ),
            };

        // ============================================================
        // Records
        // ============================================================
        private sealed record PresetDescriptionBase(
            string HardwareTech_EN,
            string HardwareTech_DE,
            int QualityScore,
            int SpeedScore)
        {
            public string GetHardwareText(UiLanguage lang)
                => lang == UiLanguage.Deutsch ? this.HardwareTech_DE : this.HardwareTech_EN;
        }

        public sealed record PresetDescription(
            string HardwareTech,
            int QualityScore,
            int SpeedScore
        );
    }
}
