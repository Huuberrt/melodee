using BenchmarkDotNet.Running;

namespace Melodee.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Melodee Benchmarks");
            Console.WriteLine("==================");
            Console.WriteLine();
            Console.WriteLine("Available benchmark categories:");
            Console.WriteLine("  streaming  - API streaming performance benchmarks");
            Console.WriteLine("  database   - Database query performance benchmarks");
            Console.WriteLine("  cache      - Cache performance benchmarks");
            Console.WriteLine("  collection - Collection operation benchmarks");
            Console.WriteLine("  all        - Run all benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet run -c Release --project benchmarks/Melodee.Benchmarks [category]");
            Console.WriteLine("Example: dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming");
            return;
        }

        var category = args[0].ToLower();

        switch (category)
        {
            case "streaming":
                BenchmarkRunner.Run<StreamingBenchmarks>();
                break;
            case "database":
                BenchmarkRunner.Run<DatabaseQueryBenchmarks>();
                break;
            case "cache":
                BenchmarkRunner.Run<CacheBenchmarks>();
                break;
            case "collection":
                BenchmarkRunner.Run<CollectionOperationBenchmarks>();
                break;
            case "all":
                BenchmarkRunner.Run<StreamingBenchmarks>();
                BenchmarkRunner.Run<DatabaseQueryBenchmarks>();
                BenchmarkRunner.Run<CacheBenchmarks>();
                BenchmarkRunner.Run<CollectionOperationBenchmarks>();
                break;
            default:
                Console.WriteLine($"Unknown benchmark category: {category}");
                Console.WriteLine("Available categories: streaming, database, cache, collection, all");
                break;
        }
    }
}