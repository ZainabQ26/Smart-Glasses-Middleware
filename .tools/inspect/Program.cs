using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "microsoft.semantickernel", "1.70.0", "lib", "net10.0", "Microsoft.SemanticKernel.dll");
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine("DLL not found: " + path);
            return;
        }

        var asm = Assembly.LoadFrom(path);
        var found = false;
        foreach (var t in asm.GetTypes())
        {
            if (t.Name.Contains("Embedding") || t.Name.Contains("IEmbedding") || t.Name.Contains("OpenAI") || t.Name.Contains("OpenAi") || t.Name.Contains("OpenAI"))
            {
                found = true;
                Console.WriteLine("Type: " + t.FullName);
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    Console.WriteLine("  Method: " + m.ToString());
                }
                Console.WriteLine("---");
            }
        }
        if (!found) Console.WriteLine("No matching types found.");
    }
}
