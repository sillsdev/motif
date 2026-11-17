using System;
using System.IO;
using System.Text;

namespace SIL.TextEditing.CommandLine;

internal static class Program
{
    static int Main(string[] args)
    {
        // UTF-8 without a byte order mark on every stream, whatever the console's code page is.
        var utf8 = new UTF8Encoding(false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        using var error = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
        return new CliApplication().Run(args, input, output, error);
    }
}
