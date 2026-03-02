namespace VBNetTweaks.Utils;

public static class PerformanceMonitor
{
    private struct Sample
    {
        public string Name;
        public float TotalTime;
        public int Count;
        public float LastLogTime;
    }

    private static Dictionary<string, Sample> _samples = new();
    private const float LOG_INTERVAL = 5f;

    public static void Track(string name, Action action)
    {
        if (!VBNetTweaks.DebugEnabled.Value)
        {
            action();
            return;
        }

        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();

        if (!_samples.TryGetValue(name, out var sample))
        {
            sample = new Sample { Name = name };
        }

        sample.TotalTime += sw.ElapsedMilliseconds;
        sample.Count++;
        
        float now = Time.time;
        if (now - sample.LastLogTime > LOG_INTERVAL)
        {
            float avg = sample.TotalTime / sample.Count;
            VBNetTweaks.LogDebug($"{name}: avg={avg:F2}ms over {sample.Count} samples");
            
            sample.TotalTime = 0;
            sample.Count = 0;
            sample.LastLogTime = now;
        }

        _samples[name] = sample;
    }
}