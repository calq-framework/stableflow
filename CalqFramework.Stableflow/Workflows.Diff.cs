namespace CalqFramework.Stableflow;

partial class Workflows {
    public Version Diff() {
        var latestTagHash = GetLatestTagHash();
        if (string.IsNullOrEmpty(latestTagHash)) {
            return GetHighestHardcodedVersion();
        }
        var modifiedProjectFiles = GetChangedProjectFiles(latestTagHash);
        TryGetBumpedVersion(modifiedProjectFiles, latestTagHash, out var version);
        return version;
    }
}
