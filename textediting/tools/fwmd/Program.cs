using System;

namespace SIL.TextEditing.CommandLine;

internal static class Program
{
    static int Main(string[] args) => new CliApplication().Run(args, Console.In, Console.Out, Console.Error);
}
