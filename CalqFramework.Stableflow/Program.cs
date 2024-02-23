using CalqFramework.Cli;
using CalqFramework.Shell;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using static CalqFramework.Shell.ShellUtil;

namespace Ghbvft6.Calq.Dvo;

public partial class Program {
    private void Clean() {
        CMD("git reset --hard");
        CMD("git clean -d -x --force");
    }

    private bool IsInSameOrSubdirectory(string filePath, IEnumerable<string> files) {
        string fileDirectory = Path.GetDirectoryName(filePath)!;

        return files.Any(file =>
            fileDirectory.Equals(Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase) ||
            fileDirectory.StartsWith($"{Path.GetDirectoryName(file)}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        );
    }

    private bool IsInSameOrParentDirectory(string filePath, IEnumerable<string> files) {
        string fileDirectory = Path.GetDirectoryName(filePath)!;

        return files.Any(file =>
            fileDirectory.Equals(Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase) ||
            Path.GetDirectoryName(file)!.StartsWith($"{fileDirectory}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        );
    }

    private ICollection<string> GetChangedProjectFiles(string commitHash) {
        CMD($"git fetch --depth 1 origin {commitHash}");

        var projectFiles = GetProjectFiles();
        var changedFiles = CMD($"git diff {commitHash} --name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => $".{Path.DirectorySeparatorChar}{x}");

        var changedProjects = new List<string>();
        foreach (var projectFile in projectFiles) {
            if (IsInSameOrParentDirectory(projectFile, changedFiles)) {
                changedProjects.Add(projectFile);
            }
        }

        return changedProjects;
    }

    private IEnumerable<string> GetProjectFiles() {
        IEnumerable<string> projectFiles = Directory.GetFiles(".", "*.*proj", SearchOption.AllDirectories);
        IEnumerable<string> testProjectFiles = Directory.GetFiles(".", "*Test.*proj", SearchOption.AllDirectories);
        var examplesDir = Directory.EnumerateDirectories(".", "Examples", SearchOption.AllDirectories)
            .Union(Directory.EnumerateDirectories(".", "Example", SearchOption.AllDirectories)).OrderBy(x => x.Length).FirstOrDefault();
        IEnumerable<string> exampleProjectFiles = Directory.GetFiles(".", "*Example.*proj", SearchOption.AllDirectories);
        exampleProjectFiles = examplesDir != null ? exampleProjectFiles.Union(Directory.GetFiles(examplesDir, "*.*proj", SearchOption.AllDirectories)) : exampleProjectFiles;

        projectFiles = projectFiles.Where(file => !IsInSameOrSubdirectory(file, testProjectFiles));
        projectFiles = projectFiles.Where(file => !IsInSameOrSubdirectory(file, exampleProjectFiles));
        var projectFilesList = projectFiles.ToList();
        // TODO validate this logic
        projectFiles = projectFiles.Where(file => !IsInSameOrSubdirectory(file, projectFilesList.Except(new[] { file }))); // filter out nested projects

        return projectFiles;
    }

    private string GetProjectFile() {
        var currentProjectFiles = Directory.GetFiles(".", "*.*proj", SearchOption.AllDirectories).Where(file => !Path.GetFileNameWithoutExtension(file).EndsWith("Test")).ToArray();
        return currentProjectFiles.FirstOrDefault()!;
    }

    private string? GetTestProject(string projectFile) {
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        var projectFileName = Path.GetFileNameWithoutExtension(projectFile);
        var testProjectFiles = Directory.GetFiles(Path.Combine(projectDirectory, ".."), $"{projectFileName}Test.*proj", SearchOption.AllDirectories);
        return testProjectFiles.FirstOrDefault();
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

    private string GetPackageId(string projectFile) {
        var content = File.ReadAllText(projectFile);
        var packageIdPattern = "<PackageId>(.*?)</PackageId>";
        var match = Regex.Match(content, packageIdPattern);

        if (match.Success) {
            return match.Groups[1].Value;
        }

        return GetAssemblyName(projectFile);
    }
    private void BuildPush(string projectFile, Version version, bool test) {
        // TODO use XmlDocument
        var projectContent = File.ReadAllText(projectFile);
        var buildOptions = projectContent.Contains("Include=\"Microsoft.SourceLink.GitHub\"")
            ? "-p:EmbedUntrackedSources=true -p:DebugType=embedded"
            : "";

        // TODO build specific project and all test project that ref this project
        CMD($"dotnet restore \"{projectFile}\" --locked-mode -p:ContinuousIntegrationBuild=true");
        CMD($"dotnet build \"{projectFile}\" --no-restore --configuration Release -p:ContinuousIntegrationBuild=true -p:Version={version} {buildOptions}");

        if (test) {
            var testProjectFile = GetTestProject(projectFile);
            if (testProjectFile != null) {
                CMD($"dotnet test \"{testProjectFile}\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
            }
        }

        // TODO use XmlDocument
        var packOptions = projectContent.Contains("Include=\"Microsoft.SourceLink.GitHub\"")
            ? "-p:PublishRepositoryUrl=true"
            : $"-p:RepositoryUrl={CMD("git config --get remote.origin.url").Trim()}";

        CMD($"dotnet pack \"{projectFile}\" --no-restore --no-build --output . --configuration Release -p:ContinuousIntegrationBuild=true -p:Version={version} {buildOptions} {packOptions}");
        var nupkg = $"./{GetPackageId(projectFile)}.{version}.nupkg";

        CMD($"dotnet nuget push {nupkg} --source main");
    }

    private void BuildPushTag(IEnumerable<string> projectFiles, Version version, bool test) {
        foreach (var projectFile in projectFiles) {
            BuildPush(projectFile, version, true);
        }
        // FIXME push here instead - after everything was built
        Tag(version);
        TagAsLatest();
    }

    private void Tag(Version version) {
        CMD($"git tag v{version}");
        CMD($"git push origin v{version}");
    }

    private void TagAsLatest() {
        CMD($"git tag latest --force");
        CMD($"git push origin latest --force");
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
        ShellUtil.SetShell(new Bash());
        var result = CommandLineInterface.Execute(new Program());
        if (result != null) {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
