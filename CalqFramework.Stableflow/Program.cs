using Ghbvft6.Calq.Terminal;
using Ghbvft6.Calq.Tooler;
using System.Text.RegularExpressions;
using System.Xml;
using static Ghbvft6.Calq.Terminal.BashUtil;

namespace Ghbvft6.Calq.Dvo;

class Program {
    private void Clean() {
        CMD("git reset --hard");
        CMD("git clean -d -x --force");
    }

    private string GetProjectFile() {
        var currentProjectFiles = Directory.GetFiles(".", "*.*proj", SearchOption.AllDirectories).Where(file => !Path.GetFileNameWithoutExtension(file).EndsWith("Test")).ToArray();
        return currentProjectFiles.FirstOrDefault()!;
    }

    private Version GetVersionFromBranchName(string branchName) {
        var versionString = Regex.Match(branchName, @"v([0-9]+\.[0-9]+)").Groups[1].Value;
        return new Version(versionString);
    }

    private Version GetVersionFromTagName(string branchName) {
        var versionString = Regex.Match(branchName, @"v([0-9]+\.[0-9]+\.[0-9]+)").Groups[1].Value;
        return new Version(versionString);
    }

    private Version GetVersion(string projectFile) {
        var content = File.ReadAllText(projectFile);
        var versionPattern = "<Version>(.*?)</Version>"; // TODO use XmlDocument
        var match = Regex.Match(content, versionPattern);

        if (match.Success) {
            return new Version(match.Groups[1].Value);
        }

        throw new Exception($"Version is not defined in project: {projectFile}");
    }

    private string GetAssemblyName(string projectFile) {
        var content = File.ReadAllText(projectFile);
        var assemblyNamePattern = "<AssemblyName>(.*?)</AssemblyName>";
        var match = Regex.Match(content, assemblyNamePattern);

        if (match.Success) {
            return match.Groups[1].Value;
        }

        var projectFileName = Path.GetFileName(projectFile);
        return Path.GetFileNameWithoutExtension(projectFileName);
    }

    private void BuildPushTag(string projectFile, Version version, bool test) {
        var projectContent = File.ReadAllText(projectFile);

        // TODO build specific project and all test project that ref this project
        CMD("dotnet restore --locked-mode -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet build --no-restore --configuration Release -p:ContinuousIntegrationBuild=true -p:Version={version}");

        if (test) {
            CMD("dotnet test --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
        }

        // TODO use XmlDocument
        var packOptions = projectContent.Contains("Include=\"Microsoft.SourceLink.GitHub\"")
            ? "-p:EmbedUntrackedSources=true -p:DebugType=embedded -p:PublishRepositoryUrl=true"
            : $"-p:RepositoryUrl={CMD("git config --get remote.origin.url").Trim()}";

        CMD($"dotnet pack \"{projectFile}\" --no-restore --no-build --output . --configuration Release -p:ContinuousIntegrationBuild=true -p:Version={version} {packOptions}");
        var nupkg = $"./{GetAssemblyName(projectFile)}.{version}.nupkg";

        CMD($"dotnet nuget push {nupkg} --source main");

        CMD($"git tag v{version}");
        CMD($"git push origin v{version}");
    }

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

    private void UpdateVersion(string projectFile, Version version) {
        //var content = File.ReadAllText(projectFile);
        //var versionPattern = "<Version>(.*)</Version>";
        //var newContent = Regex.Replace(content, versionPattern, $"<Version>{version}</Version>");
        //File.WriteAllText(projectFile, newContent);

        var xmlDoc = new XmlDocument();
        xmlDoc.Load(projectFile);
        (xmlDoc.SelectSingleNode("/Project/PropertyGroup/Version") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("Version"))!).InnerText = version.ToString();
        xmlDoc.Save(projectFile);
        CMD($"git add {projectFile}");
    }

    private void BumpVersion(string branchName, int patchVersionBump) {
        var projectFile = GetProjectFile();
        var baseVersion = GetVersion(projectFile);
        var branchVersion = GetVersionFromBranchName(branchName);
        var newReleaseVersion = new Version($"{branchVersion.Major}.{branchVersion.Minor}.{baseVersion.Build + patchVersionBump}");

        UpdateVersion(projectFile, newReleaseVersion);
    }

    private void CommitLockFile() {
        CMD("git add '**/packages.lock.json'");
        CMD("git -c user.name='Stableflow[action]' -c user.email='' commit -m 'update packages.lock.json'");
        CMD($"git push origin {CMD("git branch --show-current").Trim()}");
    }

    static void Main(string[] args) {
        Tool.Execute(new Program(), args);
    }
}
