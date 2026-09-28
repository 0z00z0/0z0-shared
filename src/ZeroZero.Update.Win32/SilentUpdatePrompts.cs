namespace ZeroZero.Update.Win32;

/// <summary>Prompts that answer themselves and draw nothing. Every question is answered with the
/// step that carries on, every statement returns a completed task, and no window exists to
/// dismiss — so a flow wired to these cannot reach a screen whatever its outcome.</summary>
public sealed class SilentUpdatePrompts : IUpdatePrompts
{
    public static readonly SilentUpdatePrompts Instance = new();

    public Task<InstallChoice> AskToInstallAsync(ReleaseInfo release, Version runningVersion) =>
        Task.FromResult(InstallChoice.Install);

    /// <summary>Reports nowhere and never cancels.</summary>
    public DownloadSurface BeginDownload(ReleaseInfo release) => default;

    public Task SayUpToDateAsync(Version runningVersion) => Task.CompletedTask;

    public Task SayNothingReleasedAsync() => Task.CompletedTask;

    public Task SayCheckFailedAsync(UpdateCheckResult result) => Task.CompletedTask;

    public Task SayCannotInstallAsync(PreparedUpdate update) => Task.CompletedTask;

    public Task SayLaunchFailedAsync(PreparedUpdate update, LaunchResult result) => Task.CompletedTask;

    public void Dismiss() { }
}
