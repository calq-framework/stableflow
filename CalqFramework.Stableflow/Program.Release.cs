﻿namespace Ghbvft6.Calq.Dvo;

partial class Program {
    public void release() {
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
