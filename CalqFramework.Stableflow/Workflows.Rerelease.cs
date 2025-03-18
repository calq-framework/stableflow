namespace CalqFramework.Stableflow;
using static CalqFramework.Cmd.Terminal;

partial class Workflows {
    public void Rerelease() {
        var latestTagHash = GetLatestTagHash();
        // var modifiedProjectFiles = GetChangedProjectFiles(latestTagHash); // FIXME explained why in release command
        var latestTagName = CMD($"git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]* | grep {latestTagHash}").Split('/')?[^1]!; // TODO re-validate with regex
        var latestVersion = GetVersionFromTagName(latestTagName);
        foreach (var projectFile in GetProjectFiles()) {
            BuildPush(projectFile, latestVersion, false, true);
        }
    }
}
