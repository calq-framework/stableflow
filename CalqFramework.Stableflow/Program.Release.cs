using System.Text.RegularExpressions;
using static Ghbvft6.Calq.Terminal.BashUtil;

namespace Ghbvft6.Calq.Dvo;

partial class Program {
    public void release() {
        var TMPDIR = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(TMPDIR);

        var tags = CMD("git ls-remote --tags --sort -version:refname origin v[0-9]*.[0-9]*.[0-9]*").Split('\n', StringSplitOptions.RemoveEmptyEntries); // outputs "e466424416af589cdb7b8a23258ade4f23a0c3ed refs/tags/v0.0.0"

        var latestTagDescription = tags.FirstOrDefault(); // TODO if minor branch then get latest on that minor branch instead (also don't allow for minor bumps) - var branchName = CMD("git branch --show-current").Trim();

        var modifiedProjectFile = GetProjectFile();
        var modifiedVersion = GetVersion(modifiedProjectFile);
        var modifiedAssemblyName = GetAssemblyName(modifiedProjectFile);

        if (string.IsNullOrEmpty(latestTagDescription)) {
            BuildPushTag(modifiedProjectFile, modifiedVersion, true);
            return;
        }

        var latestTagHash = Regex.Split(latestTagDescription, @"\s+")[0]; // TODO re-validate with regex

        Clean();
        CMD($"git fetch --depth 1 origin {latestTagHash}");
        CMD($"git checkout {latestTagHash}");

        var baseProjectFile = GetProjectFile();
        var baseVersion = GetVersion(baseProjectFile);
        var baseAssemblyName = GetAssemblyName(baseProjectFile);

        if (modifiedVersion != baseVersion) {
            CMD($"git switch -");
            BuildPushTag(modifiedProjectFile, modifiedVersion, true);
            return;
        }

        // TODO cache latest build so that this building step can be omitted
        CMD($"dotnet restore \"{baseProjectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet build \"{baseProjectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet publish \"{baseProjectFile}\" --output \"{TMPDIR}/publish_base\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
        var baseDll = $"{TMPDIR}/publish_base/{baseAssemblyName}.dll";

        Clean();
        CMD($"git switch -");

        CMD($"dotnet restore \"{modifiedProjectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet build \"{modifiedProjectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet publish \"{modifiedProjectFile}\" --output \"{TMPDIR}/publish_modified\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
        var modifiedDll = $"{TMPDIR}/publish_modified/{modifiedAssemblyName}.dll";

        var versioningToolOutput = CMD($"synver \"{baseDll}\" \"{modifiedDll}\" 0.0.0");

        var versionBumpString = Regex.Match(versioningToolOutput, @"([0-9]+\.[0-9]+\.[0-9]+)").Groups[1].Value;
        var versionBump = new Version(versionBumpString);

        var latestTagName = latestTagDescription.Split('/')?[^1]!; // TODO re-validate with regex
        var latestVersion = GetVersionFromTagName(latestTagName);
        // TODO get bumped version directly from synver output
        if (versionBump.Major != 0 || versionBump.Minor != 0) {
            var bumpedVersion = new Version(latestVersion.Major + versionBump.Major, latestVersion.Major == versionBump.Major ? latestVersion.Minor + versionBump.Minor : versionBump.Minor, versionBump.Build);
            BuildPushTag(modifiedProjectFile, bumpedVersion, true); // TODO do pr instead
        } else if (versionBump.Build != 0) {
            var bumpedVersion = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build + versionBump.Build);
            Console.WriteLine(bumpedVersion);
            BuildPushTag(modifiedProjectFile, bumpedVersion, true);
        } else {
            // assembly file hasn't changed
        }
    }
}
