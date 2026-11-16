using System.Globalization;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Lt2.Tests.TestOrder.Collections))]
[assembly: TestClassOrderer(typeof(Lt2.Tests.TestOrder.Classes))]
[assembly: TestMethodOrderer(typeof(Lt2.Tests.TestOrder.Methods))]
[assembly: TestCaseOrderer(typeof(Lt2.Tests.TestOrder.Cases))]

namespace Lt2.Tests;

/// <summary>
/// The order the tests of this assembly run in, chosen by the environment variable <c>LT2_TEST_ORDER</c>, so that a run can
/// prove the tests do not depend on each other (AGENTS.md, "Writing tests"). xUnit v3's own orders are stable for one build
/// in one folder, and its <c>--seed</c> does not change them, so the orders are written here:
/// <list type="bullet">
/// <item><c>default</c> (or unset): xUnit's own order.</item>
/// <item><c>forward</c>: by name (collections, classes, methods, cases).</item>
/// <item><c>reverse</c>: by name, backwards, at every level.</item>
/// <item><c>random</c> or <c>random:SEED</c>: shuffled at every level from the seed (a new one when none is given).</item>
/// </list>
/// With <c>LT2_TEST_ORDER_LOG</c> set to a file, the order of the collections (in these projects one collection is one
/// class) and the seed are written to it, so a failing order can be run again.
/// </summary>
public static class TestOrder
{
    private static readonly Lazy<(string Mode, int Seed)> Chosen = new(Choose);

    /// <summary>The mode in use (<c>default</c>, <c>forward</c>, <c>reverse</c> or <c>random</c>) and the seed of a random order.</summary>
    public static (string Mode, int Seed) Current => Chosen.Value;

    /// <summary>Orders <paramref name="items"/> by <paramref name="key"/> as the chosen mode says; null for xUnit's own order.</summary>
    public static IReadOnlyCollection<T>? Apply<T>(IReadOnlyCollection<T> items, Func<T, string> key, string level)
    {
        var (mode, seed) = Current;
        switch (mode)
        {
            case "forward":
                return items.OrderBy(key, StringComparer.Ordinal).ToList();
            case "reverse":
                return items.OrderByDescending(key, StringComparer.Ordinal).ToList();
            case "random":
                // Sorted first, so the shuffle depends only on the names and the seed, not on the order xUnit handed in.
                var sorted = items.OrderBy(key, StringComparer.Ordinal).ToList();
                var random = new Random(unchecked(seed ^ StableHash(level + "|" + string.Join("|", sorted.Select(key)))));
                for (var i = sorted.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (sorted[i], sorted[j]) = (sorted[j], sorted[i]);
                }

                return sorted;
            default:
                return null;
        }
    }

    private static (string Mode, int Seed) Choose()
    {
        var raw = (Environment.GetEnvironmentVariable("LT2_TEST_ORDER") ?? "default").Trim().ToLowerInvariant();
        var parts = raw.Split(':', 2);
        var mode = parts[0] switch
        {
            "" or "default" => "default",
            "forward" or "reverse" or "random" => parts[0],
            _ => throw new InvalidOperationException($"LT2_TEST_ORDER '{raw}' is not one of default, forward, reverse, random, random:SEED."),
        };
        var seed = mode == "random" && parts.Length == 2
            ? int.Parse(parts[1], CultureInfo.InvariantCulture)
            : mode == "random" ? Environment.TickCount & 0x7FFFFFFF : 0;
        return (mode, seed);
    }

    /// <summary>FNV-1a: the same in every process (<see cref="string.GetHashCode()"/> is not).</summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    private static void Log(IEnumerable<string> collections)
    {
        var file = Environment.GetEnvironmentVariable("LT2_TEST_ORDER_LOG");
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        var (mode, seed) = Current;
        try
        {
            File.AppendAllLines(file, [$"order {mode}{(mode == "random" ? ":" + seed.ToString(CultureInfo.InvariantCulture) : "")}", .. collections.Select(c => "  " + c)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is a convenience: a run never fails because it cannot be written.
            Console.Error.WriteLine($"LT2_TEST_ORDER_LOG {file} could not be written: {ex.Message}");
        }
    }

    /// <summary>A collection's name without the id xUnit adds (the id depends on the folder the tests were built in).</summary>
    private static string CollectionName(ITestCollection collection) =>
        System.Text.RegularExpressions.Regex.Replace(collection.TestCollectionDisplayName, @"\s*\(id: [0-9a-f]+\)$", "");

    /// <summary>The collections (here: the classes) of the assembly.</summary>
    public sealed class Collections : ITestCollectionOrderer
    {
        public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
            where TTestCollection : ITestCollection
        {
            var ordered = Apply(testCollections, c => CollectionName(c), "collections")
                ?? DefaultTestCollectionOrderer.Instance.OrderTestCollections(testCollections);
            Log(ordered.Select(c => CollectionName(c)));
            return ordered;
        }
    }

    /// <summary>The classes of one collection.</summary>
    public sealed class Classes : ITestClassOrderer
    {
        public IReadOnlyCollection<TTestClass?> OrderTestClasses<TTestClass>(IReadOnlyCollection<TTestClass?> testClasses)
            where TTestClass : ITestClass =>
            Apply(testClasses, c => c?.TestClassName ?? "", "classes") ?? DefaultTestClassOrderer.Instance.OrderTestClasses(testClasses);
    }

    /// <summary>The methods of one class.</summary>
    public sealed class Methods : ITestMethodOrderer
    {
        public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods)
            where TTestMethod : ITestMethod =>
            Apply(testMethods, m => m?.MethodName ?? "", "methods") ?? DefaultTestMethodOrderer.Instance.OrderTestMethods(testMethods);
    }

    /// <summary>The cases of one method (a theory's rows).</summary>
    public sealed class Cases : ITestCaseOrderer
    {
        public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
            where TTestCase : notnull, ITestCase =>
            Apply(testCases, c => c.TestCaseDisplayName, "cases") ?? DefaultTestCaseOrderer.Instance.OrderTestCases(testCases);
    }
}
