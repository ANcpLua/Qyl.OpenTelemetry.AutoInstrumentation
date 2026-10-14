namespace Qyl.Build;

sealed partial class QylBuild
{
    /// <summary>release.sh's detached verification, rerunnable by hand after a patch release: wait until
    /// nuget.org serves every packable project at the version (ReleaseIndex, up to 45 minutes), then the
    /// package smoke in published mode, which restores from nuget.org into its own packages folder with
    /// --no-cache.</summary>
    Target VerifyRelease => definition => definition
        .Description("Wait until nuget.org serves --published-version, then run the package smoke against it")
        .Requires(() => PublishedVersion)
        .Executes(() =>
        {
            var version = PublishedVersion!.Trim();
            ReleaseIndex.Wait(
                Repo.Artifacts / $"release-{version}-index.status",
                TimeSpan.FromMinutes(45),
                [.. Repo.PackableProjects.Select(project => ReleaseIndex.NuGet(project.NameWithoutExtension, version))]);
            SmokeGate.Run(version);
        });
}
