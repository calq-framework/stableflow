using CalqFramework.Cli;
using CalqFramework.Cmd;
using CalqFramework.Cmd.Shells;
using System.Text.Json;

namespace CalqFramework.Stableflow;

public class Program {
    static void Main(string[] args) {
        Terminal.LocalTerminal.Shell = new Bash();
        var result = new CommandLineInterface().Execute(new Workflows());
        if (result is not ResultVoid) {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
