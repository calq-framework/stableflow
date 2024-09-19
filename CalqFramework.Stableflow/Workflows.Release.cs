namespace CalqFramework.Stableflow;

partial class Workflows {
    public void Release() {
        var latestTagHash = GetLatestTagHash();
        if (string.IsNullOrEmpty(latestTagHash)) {
            BuildPushTag(GetProjectFiles(), GetHighestHardcodedVersion(), true);
            return;
        }
        var modifiedProjectFiles = GetChangedProjectFiles(latestTagHash);
        if (TryGetBumpedVersion(modifiedProjectFiles, latestTagHash, out var version)) {
            BuildPushTag(modifiedProjectFiles, version, true);
        } else {
            // assembly files haven't changed
            TagAsLatest();
        }
        // return version;
    }
}
