namespace AiLimit.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "ailimit-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => System.IO.Directory.CreateDirectory(Path);
    public void Dispose() { try { System.IO.Directory.Delete(Path, true); } catch { } }
}
