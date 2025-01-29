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
            // BuildPushTag(modifiedProjectFiles, version, true);
            // FIXME dotnet pack -p:Version={version} also sets version for subprojects so when when a package is referrenced the transition packages are required to be of the same version
            // logic to determine all transition packages of modified packages is TODO so for now push everything
            BuildPushTag(GetProjectFiles(), version, true);
        } else {
            // assembly files haven't changed
            TagAsLatest();
        }
        // return version;
    }
}
