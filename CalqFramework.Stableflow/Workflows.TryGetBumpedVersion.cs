using System.Text.RegularExpressions;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Stableflow;

partial class Workflows {

    private Version GetHighestHardcodedVersion() {
        var projectFiles = GetProjectFiles();
        var highestHardcodedVersion = projectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First()!;
        return highestHardcodedVersion;
    }

    private string? GetLatestTagHash() {
        var tags = CMD("git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]*").Value.Split('\n', StringSplitOptions.RemoveEmptyEntries); // outputs "e466424416af589cdb7b8a23258ade4f23a0c3ed refs/tags/v0.0.0"

        var latestTagDescription = tags.FirstOrDefault(); // TODO if minor branch then get latest on that minor branch instead (also don't allow for minor bumps) - var branchName = CMD("git branch --show-current").Trim();

        if (string.IsNullOrEmpty(latestTagDescription)) {
            return null;
        }

        var latestTagHash = Regex.Split(latestTagDescription, @"\s+")[0]; // TODO re-validate with regex
        return latestTagHash;
    }

    private bool TryGetBumpedVersion(ICollection<string> projectFiles, string commitHash, out Version result) {
        Clean();

        var TMPDIR = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(TMPDIR);

        RUN($"git fetch --depth 1 origin {commitHash}");

        if (projectFiles.Any() == false) {
            var latestTagNamex = CMD($"git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]* | grep {commitHash}").Value.Split('/')?[^1]!; // TODO re-validate with regex
            var latestVersionx = GetVersionFromTagName(latestTagNamex);
            result = latestVersionx;
            return false;
        }

        var highestModifiedVersion = projectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First();
        var assembliesByModifiedProjectFiles = projectFiles.ToDictionary(x => x, x => GetAssemblyName(x));

        RUN($"git checkout {commitHash}");

        var baseProjectFiles = GetProjectFiles().Where(x => projectFiles.Contains(x));
        var baseProjectFilesByAssemblies = baseProjectFiles.ToDictionary(x => GetAssemblyName(x), x => x);

        // TODO confirm modified version resolving logic
        var highestBaseVersion = baseProjectFiles.Select(x => GetVersion(x)).OrderByDescending(x => x).First()!;

        if (highestModifiedVersion != highestBaseVersion) {
            RUN($"git switch -");
            result = GetHighestHardcodedVersion(); // ensure the modified version isn't lower than any other hardcoded version
            // FIXME ensure modified version isn't lower than any tag
            return true;
        }

        var hasNewAssembly = false;
        foreach (var modifiedAssembly in assembliesByModifiedProjectFiles.Values) {
            if (!baseProjectFilesByAssemblies.ContainsKey(modifiedAssembly)) {
                hasNewAssembly = true;
                break;
            }
        }

        var versionBump = new Version();
        if (hasNewAssembly) {
            versionBump = new Version(0, 1, 0);
            RUN($"git switch -");
        } else {
            var baseDllByModifiedProjectFile = new Dictionary<string, string>();
            foreach (var modifiedProjectFile in projectFiles) {
                // TODO cache latest build so that this building step can be omitted
                var assemblyName = assembliesByModifiedProjectFiles[modifiedProjectFile];
                var projectFile = baseProjectFilesByAssemblies[assemblyName];
                var outputDir = $"{TMPDIR}/publish_base/{assemblyName}";
                RUN($"dotnet restore \"{projectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
                RUN($"dotnet build \"{projectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
                RUN($"dotnet publish \"{projectFile}\" --output \"{LocalTerminal.Shell.GetInternalPath(outputDir)}\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
                baseDllByModifiedProjectFile[modifiedProjectFile] = $"{outputDir}/{assemblyName}.dll";
            }

            Clean();
            RUN($"git switch -");

            var modifiedDllByModifiedProjectFile = new Dictionary<string, string>();
            foreach (var modifiedProjectFile in projectFiles) {
                var assemblyName = assembliesByModifiedProjectFiles[modifiedProjectFile];
                var projectFile = modifiedProjectFile;
                var outputDir = $"{TMPDIR}/publish_release/{assemblyName}";
                RUN($"dotnet restore \"{projectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
                RUN($"dotnet build \"{projectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
                RUN($"dotnet publish \"{projectFile}\" --output \"{LocalTerminal.Shell.GetInternalPath(outputDir)}\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
                modifiedDllByModifiedProjectFile[modifiedProjectFile] = $"{outputDir}/{assemblyName}.dll";
            }

            foreach (var modifiedProjectFile in projectFiles) {
                CalqFramework.Stableflow.Synver.Configuration.version = "0.0.0"; // TODO remove it
                var synverVersionBump = CalqFramework.Stableflow.Synver.CompareAssemblies(baseDllByModifiedProjectFile[modifiedProjectFile], modifiedDllByModifiedProjectFile[modifiedProjectFile]);
                versionBump = new Version(synverVersionBump.major, synverVersionBump.minor, synverVersionBump.patch) > versionBump ? new Version(synverVersionBump.major, synverVersionBump.minor, synverVersionBump.patch) : versionBump;
            }
        }

        var latestTagName = CMD($"git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]* | grep {commitHash}").Value.Split('/')?[^1]!; // TODO re-validate with regex
        var latestVersion = GetVersionFromTagName(latestTagName);
        // TODO get bumped version directly from synver output
        if (versionBump.Major != 0 || versionBump.Minor != 0) {
            var bumpedVersion = new Version(latestVersion.Major + versionBump.Major, latestVersion.Major == versionBump.Major ? latestVersion.Minor + versionBump.Minor : versionBump.Minor, versionBump.Build);
            result = bumpedVersion;
            return true;
        } else if (versionBump.Build != 0) {
            var bumpedVersion = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build + versionBump.Build);
            result = bumpedVersion;
            return true;
        } else {
            // assembly files haven't changed
            result = latestVersion;
            return false;
        }
    }
}
