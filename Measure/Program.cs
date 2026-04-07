using System;
using System.Diagnostics;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Warmup...");
        await RunTest(true);
        await RunTest(false);

        Console.WriteLine("\nMeasuring...");
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++)
        {
            await RunTest(true);
        }
        sw1.Stop();
        Console.WriteLine($"Baseline (.Result): {sw1.ElapsedMilliseconds} ms");

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++)
        {
            await RunTest(false);
        }
        sw2.Stop();
        Console.WriteLine($"Optimized (await): {sw2.ElapsedMilliseconds} ms");
    }

    static async Task RunTest(bool useResult)
    {
        var t1 = Task.FromResult(1);
        var t2 = Task.FromResult(2);
        var t3 = Task.FromResult(3);

        var all = Task.WhenAll(t1, t2, t3);
        await Task.WhenAny(all, Task.Delay(5000));

        if (useResult)
        {
            var r1 = t1.Result;
            var r2 = t2.Result;
            var r3 = t3.Result;
        }
        else
        {
            var r1 = await t1;
            var r2 = await t2;
            var r3 = await t3;
        }
    }
}
