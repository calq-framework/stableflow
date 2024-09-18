namespace CalqFramework.StableflowTest;

using CalqFramework.Shell;
using Ghbvft6.Calq.Dvo;
using System.Security.Cryptography;
using System.Text.Json;
using static CalqFramework.Shell.ShellUtil;

public class ProgramTest {

    public ProgramTest() {
        ShellUtil.SetShell(new Bash());
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

        foreach (var key in a.Keys)
        {
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
        var allBytes = new Dictionary<string, byte[]>();
        foreach ( var file in files) {
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
            if (ignoreFilesAndDirs.Any(prefix => file.StartsWith(prefix))) {
                continue;
            }
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
        CMD($"git push --delete origin v{versionName}");
    }

    void RemovePackage(string versionName, string projectName) {
        var versionId = GetPackageVersionId(versionName, projectName);
        CMD(@$"
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
            Environment.CurrentDirectory = tmpDir;
            CMD("git clone --depth 1 https://github.com/calq-framework/stableflow-test-classlib-init.git"); // { token_of_StableflowReleaseTest if present} // todo pull to temp file within constructor and each unit test should use their own tmp folder
            Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "stableflow-test-classlib-init");
            var commitHashBefore = CMD("git rev-parse HEAD").Trim();
            var md5Before = GetDirMd5s(".", projectName);
            new Program().release();
            var commitHashAfter = CMD("git rev-parse HEAD").Trim();
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.0.0", projectName); // assert package exists
        } finally {
            RemovePackage("0.0.0", projectName);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Release_MethodAddition_PublishesPatchPackage() {
        var projectName = "CalqFramework.StableflowTestMethodAddition";
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            Environment.CurrentDirectory = tmpDir;
            CMD("git clone --depth 1 https://github.com/calq-framework/stableflow-test-method-addition.git");
            Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "stableflow-test-method-addition");
            var commitHashBefore = CMD("git rev-parse HEAD").Trim();
            var md5Before = GetDirMd5s(".", projectName);
            new Program().release();
            var commitHashAfter = CMD("git rev-parse HEAD").Trim();
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.0.1", projectName); // assert package exists
        } finally {
            RemovePackage("0.0.1", projectName);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Release_MethodRemoval_PublishesMinorPackage() {
        var projectName = "CalqFramework.StableflowTestMethodRemoval";
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            Environment.CurrentDirectory = tmpDir;
            CMD("git clone --depth 1 https://github.com/calq-framework/stableflow-test-method-removal.git");
            Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "stableflow-test-method-removal");
            var commitHashBefore = CMD("git rev-parse HEAD").Trim();
            var md5Before = GetDirMd5s(".", projectName);
            new Program().release();
            var commitHashAfter = CMD("git rev-parse HEAD").Trim();
            var md5After = GetDirMd5s(".", projectName);
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(Md5sEqual(md5Before, md5After));
            GetPackageVersionId("0.1.0", projectName); // assert package exists
        } finally {
            RemovePackage("0.1.0", projectName);
            Directory.Delete(tmpDir, true);
        }
    }
}
