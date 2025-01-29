using CalqFramework.Cli;
using CalqFramework.Shell;
using System.Text.Json;

namespace CalqFramework.Stableflow;

public class Program {
    static void Main(string[] args) {
        ShellUtil.SetShell(new Bash());
        var result = new CommandLineInterface().Execute(new Workflows());
        if (result != null) {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
