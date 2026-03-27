using System.Reflection;

// Compare old vs new A2A
var oldPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget/packages/a2a/0.3.4-preview/lib/net9.0/A2A.dll");
var newPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget/packages/a2a/1.0.0-preview/lib/net10.0/A2A.dll");

void ListTypes(string path, string label) {
    Console.WriteLine($"=== {label} ===");
    try {
        var asm = Assembly.LoadFrom(path);
        foreach (var t in asm.GetExportedTypes().OrderBy(t => t.FullName)) {
            var kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : "class";
            Console.WriteLine($"  [{kind}] {t.FullName}");
        }
    } catch (Exception ex) {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
}

ListTypes(oldPath, "A2A 0.3.4 (OLD)");
Console.WriteLine();
ListTypes(newPath, "A2A 1.0.0 (NEW)");
