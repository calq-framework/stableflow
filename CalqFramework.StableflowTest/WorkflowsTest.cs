namespace CalqFramework.StableflowTest;

using CalqFramework.Cmd.Shells;
using CalqFramework.Stableflow;
using System.Security.Cryptography;
using System.Text.Json;
using static CalqFramework.Cmd.Terminal;

public class WorkflowsTest {

    public WorkflowsTest() {
        LocalTerminal.Shell = new Bash();
    }

    static byte[] GetFileMd5(string filePath) {
        using (var md5 = MD5.Create()) {
            using (var stream = File.OpenRead(filePath)) {
                return md5.ComputeHash(stream);
            }
        }

    }

    static bool Md5sEqual(Dictionary<string, byte[]> a, Dictionary<string, byte[]> b) {
        if (a.Count != b.Count) {
            return false;
        }

        foreach (var key in a.Keys) {
            if (!b.ContainsKey(key)) {
                return false;
            }

            if (!a[key].SequenceEqual(b[key])) {
                return false;
            }
        }

        return true;
    }

    static Dictionary<string, byte[]> GetDirMd5s(string dir, string projectName) {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
        var coreFiles = new List<string>();
        foreach (var file in files) {
            var ignoreFilesAndDirs = new[] {
                "./.git/index",
                "./.git/shallow",
                "./.git/objects",
                "./.git/logs",

                "./.git/FETCH_HEAD",
                "./.git/ORIG_HEAD",
                "./.git/refs/tags/latest",
                "./.git/refs/tags/v0.0.0",
                "./.git/refs/tags/v0.0.1",
                "./.git/refs/tags/v0.1.0",
                $"./{projectName}/bin",
                $"./{projectName}/obj",
                $"./{projectName}Test/bin",
                $"./{projectName}Test/obj",
                $"./{projectName}.0.0.0.nupkg",
                $"./{projectName}.0.0.1.nupkg",
                $"./{projectName}.0.1.0.nupkg"
            };
            ignoreFilesAndDirs = ignoreFilesAndDirs.Concat(ignoreFilesAndDirs.Select(x => x.Replace('/', '\\'))).ToArray();
            if (ignoreFilesAndDirs.Any(prefix => file.StartsWith(prefix))) {
                continue;
            }
            coreFiles.Add(file);
        }
        var allBytes = new Dictionary<string, byte[]>();
        foreach (var file in coreFiles) {
            var md5hash = GetFileMd5(file);
            allBytes[file] = md5hash;
        }
        return allBytes;
    }
    string GetPackageVersionId(string versionName, string projectName) {
        var versionList = CMD(@$"
            curl -L \
                -H 'Accept: application/vnd.github+json' \
                -H 'Authorization: Bearer {Environment.GetEnvironmentVariable("NUGET_DELETE_PAT")}' \
                -H 'X-GitHub-Api-Version: 2022-11-28' \
                https://api.github.com/orgs/calq-framework/packages/nuget/{projectName}/versions
        ");
        var versions = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(versionList) ?? new List<Dictionary<string, object>>();
        foreach (var version in versions) {
            if (version["name"].ToString()! == versionName) {
                return version["id"].ToString()!;
            }
        }
        throw new Exception("version doesn't exist");
    }

    void RemoveTag(string versionName) {
        RUN($"git push --delete origin v{versionName}");
    }

    void RemovePackage(string versionName, string projectName) {
        var versionId = GetPackageVersionId(versionName, projectName);
        RUN(@$"
            curl -L \
                -X DELETE \
                -H 'Accept: application/vnd.github+json' \
                -H 'Authorization: Bearer {Environment.GetEnvironmentVariable("NUGET_DELETE_PAT")}' \
                -H 'X-GitHub-Api-Version: 2022-11-28' \
                https://api.github.com/orgs/calq-framework/packages/nuget/{projectName}/versions/{versionId}
        ");
        RemoveTag(versionName);
    }

    [Fact]
    public void Release_ClasslibInit_PublishesInitPackage() {
        var projectName = "CalqFramework.StableflowTestClasslibInit";
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            CD(tmpDir);
            RUN("git clone --depth 1 https://github.com/calq-framework/stableflow-test-classlib-init.git"); // { token_of_StableflowReleaseTest if present} // todo pull to temp file within constructor and each unit test should use their own tmp folder
            Environment.CurrentDirectory = Path.Combine(tmpDir, "stableflow-test-classlib-init");
            CD("stableflow-test-classlib-init");
            string commitHashBefore = CMD("git rev-parse HEAD");
            var md5Before = GetDirMd5s(".", projectName);
            new Workflows().Release();
            string commitHashAfter = CMD("git rev-parse HEAD");
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.0.0", projectName); // assert package exists
        } finally {
            try {
                RemovePackage("0.0.0", projectName);
                Directory.Delete(tmpDir, true);
            } catch { }
        }
    }

    [Fact]
    public void Release_MethodAddition_PublishesPatchPackage() {
        var projectName = "CalqFramework.StableflowTestMethodAddition";
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            CD(tmpDir);
            Environment.CurrentDirectory = tmpDir;
            RUN("git clone --depth 1 https://github.com/calq-framework/stableflow-test-method-addition.git");
            Environment.CurrentDirectory = Path.Combine(tmpDir, "stableflow-test-method-addition");
            CD("stableflow-test-method-addition");
            string commitHashBefore = CMD("git rev-parse HEAD");
            var md5Before = GetDirMd5s(".", projectName);
            new Workflows().Release();
            string commitHashAfter = CMD("git rev-parse HEAD");
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.0.1", projectName); // assert package exists
        } finally {
            try {
                RemovePackage("0.0.1", projectName);
                Directory.Delete(tmpDir, true);
            } catch { }
        }
    }

    [Fact]
    public void Release_MethodRemoval_PublishesMinorPackage() {
        var projectName = "CalqFramework.StableflowTestMethodRemoval";
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            CD(tmpDir);
            RUN("git clone --depth 1 https://github.com/calq-framework/stableflow-test-method-removal.git");
            Environment.CurrentDirectory = Path.Combine(tmpDir, "stableflow-test-method-removal");
            CD("stableflow-test-method-removal");
            string commitHashBefore = CMD("git rev-parse HEAD");
            var md5Before = GetDirMd5s(".", projectName);
            new Workflows().Release();
            string commitHashAfter = CMD("git rev-parse HEAD");
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.1.0", projectName); // assert package exists
        } finally {
            try {
                RemovePackage("0.1.0", projectName);
                Directory.Delete(tmpDir, true);
            } catch { }
        }
    }
}
