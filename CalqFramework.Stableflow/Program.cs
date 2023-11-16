﻿using Ghbvft6.Calq.Tooler;
using System.Text.RegularExpressions;
using System.Xml;
using static Ghbvft6.Calq.Terminal.BashUtil;

namespace Ghbvft6.Calq.Dvo;

partial class Program {
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
