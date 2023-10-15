using Ghbvft6.Calq.Terminal;
using Ghbvft6.Calq.Tooler;
using System.Text.RegularExpressions;
using System.Xml;
using static Ghbvft6.Calq.Terminal.BashUtil;

namespace Ghbvft6.Calq.Dvo;

class Program {

    public void Merge(bool release) {
        var TMPDIR = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(TMPDIR);

        var mainBranchName = CMD("git branch --show-current").Trim();
        var commitsCount = int.Parse(CMD($"git rev-list --count {mainBranchName}").Trim());

        if (commitsCount == 1) {
            return;
        }

        var branches = CMD("git branch --remotes --list origin/v[0-9]*.[0-9]* --sort -version:refname").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var latestBranchFullName = branches.FirstOrDefault();
        var latestBranchName = latestBranchFullName?.Replace("origin/", "");

        if (string.IsNullOrEmpty(latestBranchName)) {
            var currentVersion = GetVersion(GetProjectFile());
            latestBranchName = $"v{currentVersion.Major}.{currentVersion.Minor}";
            Clean();
            CMD($"git switch --create {latestBranchName}");
            MergeBumpPush(mainBranchName, latestBranchName, 1, release);
            return;
        }

        Clean();
        CMD($"git switch --orphan {latestBranchName}");
        CMD($"git pull --depth=1 origin {latestBranchName}");

        var projectFile = GetProjectFile();
        var baseVersion = GetVersion(projectFile);
        var assemblyName = GetAssembly(projectFile);

        // TODO cache latest build so that this building step can be omitted
        CMD($"dotnet publish \"{projectFile}\" --output \"{TMPDIR}/publish_base\" --configuration Release -p:ContinuousIntegrationBuild=true");
        var baseDll = $"{TMPDIR}/publish_base/{assemblyName}.dll";

        Clean();
        MergeBranch(mainBranchName);

        projectFile = GetProjectFile();
        var modifiedVersion = GetVersion(projectFile);
        assemblyName = GetAssembly(projectFile);

        CMD($"dotnet publish \"{projectFile}\" --output \"{TMPDIR}/publish_modified\" --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
        var modifiedDll = $"{TMPDIR}/publish_modified/{assemblyName}.dll";

        if (modifiedVersion != baseVersion) {
            var minorBranchName = $"v{modifiedVersion.Major}.{modifiedVersion.Minor}";
            try {
                CMD($"git switch --create {minorBranchName}");
            } catch (CommandExecutionException ex) {
                if (minorBranchName != latestBranchName) {
                    throw ex;
                }
            }
            UpdateVersion(projectFile, modifiedVersion);
            Push(latestBranchName, release);

            return;
        }

        var versioningToolOutput = CMD($"synver \"{baseDll}\" \"{modifiedDll}\" 0.0.0");

        var versionBumpString = Regex.Match(versioningToolOutput, @"([0-9]+\.[0-9]+\.[0-9]+)").Groups[1].Value;
        var versionBump = new Version(versionBumpString);

        if (versionBump.Major != 0 || versionBump.Minor != 0) {
            var latestVersion = GetVersionFromBranchName(latestBranchName);
            var bumpedVersion = new Version($"{latestVersion.Major + versionBump.Major}.{latestVersion.Minor + versionBump.Minor}.0");
            var minorBranchName = $"v{bumpedVersion.Major}.{bumpedVersion.Minor}";
            CMD($"git switch --create {minorBranchName}");
            UpdateVersion(projectFile, bumpedVersion);
            Push(latestBranchName, release);
        } else if (versionBump.Revision != 0) {
            Clean();
            CMD($"git switch {latestBranchName}");
            BumpVersion(latestBranchName, versionBump.Revision);
            Push(latestBranchName, release);
            //IterateMinorBranches(mainBranchName, minorBranchName => {
            //    MergeAndBump(mainBranchName, minorBranchName, patchVersionBump, release);
            //});
        } else {
        }
    }

    private void Clean() {
        CMD("git reset --hard");
        CMD("git clean -d -x --force");
    }

    private string GetProjectFile() {
        var currentProjectFiles = Directory.GetFiles(".", "*.*proj").Where(file => !file.EndsWith("Test")).ToArray();
        return currentProjectFiles.FirstOrDefault()!;
    }

    private Version GetVersionFromBranchName(string branchName) {
        var versionString = Regex.Match(branchName, @"v([0-9]+\.[0-9]+)").Groups[1].Value;
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

    private string GetAssembly(string projectFile) {
        var content = File.ReadAllText(projectFile);
        var assemblyNamePattern = "<AssemblyName>(.*?)</AssemblyName>";
        var match = Regex.Match(content, assemblyNamePattern);

        if (match.Success) {
            return match.Groups[1].Value;
        }

        var projectFileName = Path.GetFileName(projectFile);
        return Path.GetFileNameWithoutExtension(projectFileName);
    }


    private void ReleaseCore(bool doBuild) {
        var projectFiles = Directory.GetFiles(".", "*.csproj").Where(file => !file.EndsWith("Test"));

        foreach (var projectFile in projectFiles) {
            var projectContent = File.ReadAllText(projectFile);
            var version = GetVersion(projectFile);

            var buildOptions = doBuild ? "" : "--no-restore --no-build";

            // TODO use XmlDocument
            var packOptions = projectContent.Contains("Include=\"Microsoft.SourceLink.GitHub\"")
                ? "-p:EmbedUntrackedSources=true -p:DebugType=embedded -p:PublishRepositoryUrl=true"
                : "";

            var nupkg = CMD($"dotnet pack \"{projectFile}\" {buildOptions} --configuration Release -p:ContinuousIntegrationBuild=true {packOptions}")
                .Split('\n')
                .FirstOrDefault(line => line.EndsWith(".nupkg"));

            CMD($"dotnet nuget push {nupkg} --source main");

            CMD($"git tag v{version}"); // might be already tagged by other project, TODO version inheritance
            CMD($"git push origin v{version}");
        }
    }

    public void release() {
        ReleaseCore(false);
    }

    class MergeException : Exception {
        public MergeException(string? message, Exception? innerException) : base(message, innerException) {
        }
    }

    private void MergeBranch(string mainBranchName) {
        try {
            CMD($"git -c user.name='stableflow[action]' -c user.email='' merge -X theirs --no-ff {mainBranchName}");
        } catch (CommandExecutionException e) {
            CMD("git -c user.name='stableflow[action]' -c user.email='' merge --abort || true");
            throw new MergeException("", e);
        }

        try {
            Directory.Delete(".github", true);
        } catch (DirectoryNotFoundException) {
        }

        CMD("dotnet restore --use-lock-file --force-evaluate -p:ContinuousIntegrationBuild=true");
        CMD("dotnet build --no-restore --configuration Release -p:ContinuousIntegrationBuild=true");
        try {
            CMD("dotnet test --no-restore --no-build --configuration Release -p:ContinuousIntegrationBuild=true");
        } catch (CommandExecutionException e) {
            throw new MergeException("", e);
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
        var newReleaseVersion = new Version($"{branchVersion.Major}.{branchVersion.Minor}.{baseVersion.Revision + patchVersionBump}");

        UpdateVersion(projectFile, newReleaseVersion);
    }

    private void Push(string branchName, bool doRelease) {
        CMD("git add '*/packages.lock.json'");
        CMD("git add .github || true");
        CMD("git -c user.name='stableflow[action]' -c user.email='' commit --amend --no-edit");
        CMD($"git push origin {branchName}");

        if (doRelease) {
            ReleaseCore(false);
        }
    }

    private void MergeBumpPush(string mainBranchName, string branchName, int patchVersionBump, bool doRelease) {
        MergeBranch(mainBranchName);
        BumpVersion(branchName, patchVersionBump);
        Push(branchName, doRelease);
    }

    private void IterateMinorBranches(string mainBranchName, Action<string> action) {
        var minorBranches = CMD("git branch --remotes --list origin/v[0-9]*.[0-9]* --sort -version:refname").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        minorBranches = minorBranches[1..];

        foreach (var branchFullName in minorBranches) {
            var branchName = branchFullName.Replace("origin/", "");
            Clean();
            CMD($"git switch --orphan {branchName}");
            CMD($"git pull --depth=1 origin {branchName}");

            try {
                action(branchName);
            } catch (MergeException) {
                Console.WriteLine($"Failed merge at {branchName}");
                break;
            }
        }

        Clean();
        CMD($"git switch {mainBranchName}");
    }

    static void Main(string[] args) {
        Tool.Execute(new Program(), args);
    }
}
