using Microsoft.ML.OnnxRuntime;

namespace ModularAudience.Onnx;

public sealed class DemucsLoadOptions
{
    public bool UseDirectML { get; set; } = true;
    public int DeviceId { get; set; }
    public bool AllowCpuFallback { get; set; } = true;
    public bool AllowCpuOperators { get; set; } = true;
    public bool ValidateDirectML { get; set; } = true;
    public bool DisableDirectMLGraphFusion { get; set; } = true;
    public int FourierWorkers { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public double ChunkOverlap { get; set; } = 0.25;
    public int CudaDeviceId { get; set; }
    public GraphOptimizationLevel GraphOptimization { get; set; } = GraphOptimizationLevel.ORT_ENABLE_BASIC;
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.ORT_SEQUENTIAL;
    public bool EnableMemoryPattern { get; set; }
    public bool EnableCpuMemoryArena { get; set; } = true;
    public int IntraOpThreads { get; set; }
    public int InterOpThreads { get; set; }
    public bool EnableProfiling { get; set; }
    public string ProfileOutputPathPrefix { get; set; } = "demucs-profile";
    public bool LogSession { get; set; }
    public bool DisableSpinning { get; set; }
    public bool DisablePrepacking { get; set; }
    public string OptimizedModelPath { get; set; } = "";
    public Dictionary<string, string> SessionConfigEntries { get; set; } = [];

    internal SessionOptions Create(bool directML)
    {
        this.NormalizeForProvider(directML);
        var options = new SessionOptions
        {
            GraphOptimizationLevel = this.GraphOptimization,
            ExecutionMode = directML ? ExecutionMode.ORT_SEQUENTIAL : this.ExecutionMode,
            EnableMemoryPattern = !directML && this.EnableMemoryPattern,
            EnableCpuMemArena = this.EnableCpuMemoryArena,
            IntraOpNumThreads = this.IntraOpThreads,
            InterOpNumThreads = this.InterOpThreads,
            EnableProfiling = this.EnableProfiling,
            ProfileOutputPathPrefix = this.ProfileOutputPathPrefix,
            LogSeverityLevel = this.LogSession ? OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE : OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING
        };
        try
        {
            if (directML && this.DisableDirectMLGraphFusion)
                options.AddSessionConfigEntry("ep.dml.disable_graph_fusion", "1");
            if (this.DisableSpinning)
            {
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
            }
            if (this.DisablePrepacking) options.AddSessionConfigEntry("session.disable_prepacking", "1");
            foreach (var (key, value) in this.SessionConfigEntries) options.AddSessionConfigEntry(key, value);
            if (!string.IsNullOrWhiteSpace(this.OptimizedModelPath)) options.OptimizedModelFilePath = this.OptimizedModelPath;
            if (!this.AllowCpuOperators && directML) options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            if (directML) options.AppendExecutionProvider_DML(this.DeviceId);
            return options;
        }
        catch { options.Dispose(); throw; }
    }

    public void NormalizeWorkers()
    {
        this.FourierWorkers = Math.Clamp(this.FourierWorkers, 1, Environment.ProcessorCount);
        this.IntraOpThreads = Math.Clamp(this.IntraOpThreads, 0, Environment.ProcessorCount);
        this.InterOpThreads = Math.Clamp(this.InterOpThreads, 0, Environment.ProcessorCount);
        this.ChunkOverlap = double.IsFinite(this.ChunkOverlap) ? Math.Clamp(this.ChunkOverlap, 0, 0.75) : 0.25;
    }

    public void NormalizeForProvider(bool directML)
    {
        this.NormalizeWorkers();
        if (!directML) return;
        this.GraphOptimization = GraphOptimizationLevel.ORT_ENABLE_BASIC;
        this.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        this.EnableMemoryPattern = false;
    }
}
