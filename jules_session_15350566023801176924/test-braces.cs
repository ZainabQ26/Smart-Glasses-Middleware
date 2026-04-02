using System;
using System.IO;

class TestBraces {
    static void Main(string[] args) {
        string text = File.ReadAllText("src/SmartGlassesMiddleware/Program.cs");
        int count = 0;
        for (int i=0; i < text.Length; i++) {
            if (text[i] == '{') count++;
            if (text[i] == '}') count--;
            if (count < 0) {
                Console.WriteLine($"Mismatched braces at index {i}");
                return;
            }
        }
        Console.WriteLine($"Final brace count: {count}");
    }
}
