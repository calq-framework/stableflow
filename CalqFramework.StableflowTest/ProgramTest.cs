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

    static List<byte> GetDirMd5(string dir) {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
        var allBytes = new List<byte>();
        foreach ( var file in files) {
            var ignoreFilesAndDirs = new[] {
                "./.git/FETCH_HEAD",
                "./.git/ORIG_HEAD",
                "./.git/refs/tags/latest",
                "./.git/refs/tags/v0.0.0",
                "./ClqFramework.StableflowTestReleaseClasslib/bin",
                "./ClqFramework.StableflowTestReleaseClasslib/obj",
                "./ClqFramework.StableflowTestReleaseClasslibTest/bin",
                "./ClqFramework.StableflowTestReleaseClasslibTest/obj",
                "./ClqFramework.StableflowTestReleaseClasslib.0.0.0.nupkg"
            };
            if (ignoreFilesAndDirs.Any(prefix => file.StartsWith(prefix))) {
                continue;
            }
            var md5hash = GetFileMd5(file);
            allBytes.AddRange(md5hash);
        }
        return allBytes;
    }

    string GetPackageVersionId(string versionName) {
        var versionList = CMD(@$"
            curl -L \
                -H 'Accept: application/vnd.github+json' \
                -H 'Authorization: Bearer {Environment.GetEnvironmentVariable("NUGET_DELETE_PAT")}' \
                -H 'X-GitHub-Api-Version: 2022-11-28' \
                https://api.github.com/orgs/calq-framework/packages/nuget/ClqFramework.StableflowTestReleaseClasslib/versions
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

    void RemovePackage(string versionName) {
        var versionId = GetPackageVersionId(versionName);
        CMD(@$"
            curl -L \
                -X DELETE \
                -H 'Accept: application/vnd.github+json' \
                -H 'Authorization: Bearer {Environment.GetEnvironmentVariable("NUGET_DELETE_PAT")}' \
                -H 'X-GitHub-Api-Version: 2022-11-28' \
                https://api.github.com/orgs/calq-framework/packages/nuget/ClqFramework.StableflowTestReleaseClasslib/versions/{versionId}
        ");
        RemoveTag(versionName);
    }

    [Fact]
    public void Test1() {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Directory.CreateDirectory(tmpDir);
            Environment.CurrentDirectory = tmpDir;
            CMD("git clone --depth 1 https://github.com/calq-framework/stableflow-test-release-classlib.git"); // { token_of_StableflowReleaseTest if present} // todo pull to temp file within constructor and each unit test should use their own tmp folder
            Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "stableflow-test-release-classlib");
            var commitHashBefore = CMD("git rev-parse HEAD").Trim();
            var md5Before = GetDirMd5(".");
            new Program().release();
            var commitHashAfter = CMD("git rev-parse HEAD").Trim();
            var md5After = GetDirMd5(".");
            Assert.Equal(commitHashBefore, commitHashAfter);
            Assert.True(md5Before.SequenceEqual(md5After));
            GetPackageVersionId("0.0.0"); // assert package exists
        } finally {
            RemovePackage("0.0.0");
            Directory.Delete(tmpDir, true);
        }
    }
}
