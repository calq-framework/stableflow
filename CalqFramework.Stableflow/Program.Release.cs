using System.Text.RegularExpressions;
using static Ghbvft6.Calq.Terminal.BashUtil;

namespace Ghbvft6.Calq.Dvo;

partial class Program {
    public void release() {
        Clean();

        var TMPDIR = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(TMPDIR);

        var tags = CMD("git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]*").Split('\n', StringSplitOptions.RemoveEmptyEntries); // outputs "e466424416af589cdb7b8a23258ade4f23a0c3ed refs/tags/v0.0.0"

        var latestTagDescription = tags.FirstOrDefault(); // TODO if minor branch then get latest on that minor branch instead (also don't allow for minor bumps) - var branchName = CMD("git branch --show-current").Trim();

        var projectFiles = GetProjectFiles();
        var highestHardcodedVersion = projectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First()!;

        if (string.IsNullOrEmpty(latestTagDescription)) {
            BuildPushTag(projectFiles, highestHardcodedVersion, true);
            return;
        }

        var latestTagHash = Regex.Split(latestTagDescription, @"\s+")[0]; // TODO re-validate with regex
        CMD($"git fetch --depth 1 origin {latestTagHash}");

        var modifiedProjectFiles = GetChangedProjectFiles(latestTagHash);
        var highestModifiedVersion = modifiedProjectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First()!;
        var assembliesByModifiedProjectFiles = modifiedProjectFiles.ToDictionary(x => x, x => GetAssemblyName(x));

        CMD($"git checkout {latestTagHash}");

        var baseProjectFiles = GetProjectFiles();
        var baseProjectFilesByAssemblies = baseProjectFiles.ToDictionary(x => GetAssemblyName(x), x => x);

        // TODO confirm modified version resolving logic
        var highestBaseVersion = baseProjectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First()!;

        if (highestModifiedVersion != highestBaseVersion) {
            CMD($"git switch -");
            BuildPushTag(modifiedProjectFiles, highestHardcodedVersion, true);
            return;
        }

        var hasNewAssembly = false;
        foreach (var modifiedAssembly in assembliesByModifiedProjectFiles.Values) {
            if (!baseProjectFilesByAssemblies.ContainsKey(modifiedAssembly)) {
                hasNewAssembly = true;
            }
        }

        var versionBump = new Version();
        if (hasNewAssembly) {
            versionBump = new Version(0, 1, 0);
        } else {
            var baseDllByModifiedProjectFile = new Dictionary<string, string>();
            foreach (var modifiedProjectFile in modifiedProjectFiles) {
                // TODO cache latest build so that this building step can be omitted
                var assemblyName = assembliesByModifiedProjectFiles[modifiedProjectFile];
                var projectFile = baseProjectFilesByAssemblies[assemblyName];
                var outputDir = $"{TMPDIR}/publish_base/{assemblyName}";
                CMD($"dotnet restore \"{projectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
                CMD($"dotnet build \"{projectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
                CMD($"dotnet publish \"{projectFile}\" --output \"{outputDir}\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
                baseDllByModifiedProjectFile[modifiedProjectFile] = $"{outputDir}/{assemblyName}.dll";
            }

            Clean();
            CMD($"git switch -");

            var modifiedDllByModifiedProjectFile = new Dictionary<string, string>();
            foreach (var modifiedProjectFile in modifiedProjectFiles) {
                var assemblyName = assembliesByModifiedProjectFiles[modifiedProjectFile];
                var projectFile = modifiedProjectFile;
                var outputDir = $"{TMPDIR}/publish_release/{assemblyName}";
                CMD($"dotnet restore \"{projectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
                CMD($"dotnet build \"{projectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
                CMD($"dotnet publish \"{projectFile}\" --output \"{outputDir}\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
                modifiedDllByModifiedProjectFile[modifiedProjectFile] = $"{outputDir}/{assemblyName}.dll";
            }

            foreach (var modifiedProjectFile in modifiedProjectFiles) {
                var versioningToolOutput = CMD($"synver \"{baseDllByModifiedProjectFile[modifiedProjectFile]}\" \"{modifiedDllByModifiedProjectFile[modifiedProjectFile]}\" 0.0.0");

                var versionBumpString = Regex.Match(versioningToolOutput, @"([0-9]+\.[0-9]+\.[0-9]+)").Groups[1].Value;
                versionBump = new Version(versionBumpString) > versionBump ? new Version(versionBumpString) : versionBump;
            }
        }

        var latestTagName = latestTagDescription.Split('/')?[^1]!; // TODO re-validate with regex
        var latestVersion = GetVersionFromTagName(latestTagName);
        // TODO get bumped version directly from synver output
        if (versionBump.Major != 0 || versionBump.Minor != 0) {
            var bumpedVersion = new Version(latestVersion.Major + versionBump.Major, latestVersion.Major == versionBump.Major ? latestVersion.Minor + versionBump.Minor : versionBump.Minor, versionBump.Build);
            BuildPushTag(modifiedProjectFiles, bumpedVersion, true); // TODO do pr instead
        } else if (versionBump.Build != 0) {
            var bumpedVersion = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build + versionBump.Build);
            BuildPushTag(modifiedProjectFiles, bumpedVersion, true);
        } else {
            // assembly files haven't changed
            TagAsLatest();
        }
    }
}
