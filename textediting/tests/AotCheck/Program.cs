using System;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

// Touches the model so the publish has something to link; the check is the publish itself.
var clusters = GraphemeClusters.Starts("áb");
Console.WriteLine(clusters.Count == 2 ? "ok" : "unexpected");

// Runs the Markdown converter every way (MarkdownCheck.cs), so Markdig's parser, tables and our extension are linked and executed under AOT.
{
    var failures = MarkdownCheck.Run();
    foreach (var failure in failures) Console.WriteLine("md FAILED: " + failure);
    Console.WriteLine(failures.Count == 0 ? "md ok" : "md unexpected");
    if (failures.Count > 0) return 1;
}

// Runs the record frame (RecordCheck.cs): YamlDotNet's parser layer, the schema-typed reader, the writer and the JSON Schema, under AOT.
{
    var failures = RecordCheck.Run();
    foreach (var failure in failures) Console.WriteLine("record FAILED: " + failure);
    Console.WriteLine(failures.Count == 0 ? "record ok" : "record unexpected");
    if (failures.Count > 0) return 1;
}

// Runs the libpalaso path under AOT: SLDR offline init (reads langtags JSON with Newtonsoft) and a definition.
try
{
    var registry = new WritingSystemRegistry().Add(WritingSystemRegistry.Define("ar", "Segoe UI", 18, true, "lnum=1, ss01"));
    var info = registry.Get("ar");
    bool good = info.Rtl && info.Font == "Segoe UI" && info.Size == 18 && info.Features == "lnum=1, ss01";
    Console.WriteLine(good ? "ws ok" : "ws unexpected");
    return good ? 0 : 1;
}
catch (Exception e)
{
    Console.WriteLine("ws FAILED: " + e.GetType().Name + ": " + e.Message);
    return 1;
}
