using System.CommandLine;
using System.Xml.Linq;
using System.Text.RegularExpressions;

var fileOption = new Option<FileInfo>(
    name: "--file",
    description: "The xml file produced by a single run of the whole test suite") {
    IsRequired = true
};
var parallelizationOption = new Option<int[]>(
    name: "--parallelization",
    description: "The number of parts to split the result into") {
        AllowMultipleArgumentsPerToken = true,
    };
var maxFilterOption = new Option<int?>(
    name: "--max-filters",
    description: "The maximum number of test filters to produce per split");
var verbosityOption = new Option<int?>(
    name: "--console-verbosity",
    description: "The console output verbosity");

var rootCommand = new RootCommand("Generate splits for parallelize of test runs based on the time each test took");
rootCommand.AddOption(fileOption);
rootCommand.AddOption(parallelizationOption);
rootCommand.AddOption(maxFilterOption);
rootCommand.AddOption(verbosityOption);

rootCommand.SetHandler((file, parallelization, maxFilters, verbosityA) =>
{
    if (!parallelization.Any()) {
        parallelization = new[] { 2 };
    }
    var verbosity = verbosityA ?? 0;
    using var fileStream = file.Open(FileMode.Open, FileAccess.Read);
    var xmlDoc = XDocument.Load(fileStream);
    var xmlNamespace = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
    var testResultsXml = xmlDoc.Root!.Descendants(xmlNamespace+"UnitTestResult");
    Log(verbosity, 3, "DC: " + xmlDoc.Root.Descendants().Count());
    Log(verbosity, 3, "UTRC: " + testResultsXml.Count());
    var testResult = new Dictionary<string,TimeSpan>();
    var testResultUp = new Dictionary<string,TimeSpan>();
    foreach(var testX in testResultsXml) {
        var testName = testX.Attribute("testName")!.Value;
        var flattenTestName = deparameterizeKey(testName);
        var upTestName = upKey(flattenTestName);
        var duration = TimeSpan.Parse(testX.Attribute("duration")!.Value);
        foreach(var (key,dict) in new[] { (upTestName, testResultUp), (flattenTestName, testResult) }) {
            var keyDuration = duration;
            if (dict.TryGetValue(key, out var existingDur)) {
                keyDuration = keyDuration + existingDur;
            }
            dict[key] = keyDuration;
        }
    }
    Log(verbosity, 2, $"TRC: {testResult.Count()}");
    Log(verbosity, 2, $"TRUC: {testResultUp.Count()}");
    if (maxFilters != null) {
        var maxCount = parallelization.Min() * maxFilters;
        while(testResult.Count > maxCount) {
            var shortestUpKey = testResultUp.OrderBy(i => i.Value).First().Key;
            Log(verbosity, 4, $"Coalescing {shortestUpKey}: {testResultUp[shortestUpKey]}");
            {
                var duration = TimeSpan.Zero;
                foreach(var testR in testResult.Keys.Where(i => i.StartsWith(shortestUpKey)).ToArray()) {
                    testResult.Remove(testR, out var thisDur);
                    duration += thisDur;
                }
                testResult[shortestUpKey] = duration;
                testResultUp.Remove(shortestUpKey);
            }

            // handle multiple level flattening
            var nextUpKey = upKey(shortestUpKey);
            if (testResult.Where(i => i.Key.StartsWith(nextUpKey)).All(i => upKey(i.Key) == nextUpKey)) {
                var duration = TimeSpan.Zero;
                foreach(var testR in testResult.Where(i => i.Key.StartsWith(nextUpKey)).ToArray()) {
                    duration += testR.Value;
                }
                testResultUp[nextUpKey] = duration;
            }
        }
    }
    Log(verbosity, 2, $"TRC: {testResult.Count()}");
    Log(verbosity, 2, $"TRUC: {testResultUp.Count()}");
    Log(verbosity, 3, "TR:\n"+ J(testResult));
    Log(verbosity, 3, "TRU:\n"+ J(testResultUp));

    var result = new Dictionary<int,List<string>[]>();

    foreach(var thisParallelization in parallelization) {
        var parts = Enumerable.Range(0, thisParallelization).Select(i => new List<string>()).ToArray();
        var partsDur = Enumerable.Range(0, thisParallelization).Select(i => TimeSpan.Zero).ToArray();

        var orderedTestResults = new Stack<KeyValuePair<string,TimeSpan>>(testResult.OrderBy(i => i.Value).ToList());

        while(orderedTestResults.Any()) {
            var smallestIndex = partsDur.Select((i,j) => (i,j)).OrderBy(i => i.i).Select(i => i.j).First();
            var largest = orderedTestResults.Pop();
            Log(verbosity, 3, $"SI: {J(smallestIndex)}");
            Log(verbosity, 3, $"L: {J(largest)}");
            Log(verbosity, 3, $"SD: {J(partsDur[smallestIndex])}");
            Log(verbosity, 4, $"SL: {J(parts[smallestIndex])}");
            partsDur[smallestIndex] += largest.Value;
            parts[smallestIndex].Add(largest.Key);
            Log(verbosity, 3, $"SD2: {J(partsDur[smallestIndex])}");
            Log(verbosity, 4, $"SL2: {J(parts[smallestIndex])}");
        }
        Log(verbosity, 1, "FR (CD):\n"+ J(partsDur));
        Log(verbosity, 2, "FR (TD):\n"+ J(parts.Select(i => i.Select(i => (i, testResult[i]))).ToArray()));

        result[thisParallelization] = parts;
    }

    Console.WriteLine(J(result));
},
fileOption, parallelizationOption, maxFilterOption, verbosityOption);

return await rootCommand.InvokeAsync(args);

public partial class Program
{
    [GeneratedRegex("[.+][^.]*$", RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.ExplicitCapture)]
    private static partial Regex UP_REGEX();
    [GeneratedRegex("^[.+]*[.]", RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.ExplicitCapture)]
    private static partial Regex LEAF_REGEX();
    [GeneratedRegex("[(].*$", RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.ExplicitCapture)]
    private static partial Regex DEPARAMETERIZE_REGEX();
    static string deparameterizeKey(string key) // cspell:ignore deparameterize
        => DEPARAMETERIZE_REGEX().Replace(key, "");
    static string upKey(string key)
        => UP_REGEX().Replace(key, "");
    static string leafKey(string key)
        => LEAF_REGEX().Replace(key, "");

    static void Log(int verbosity, int thisVerbosity, string message) {
        if (thisVerbosity <= verbosity) {
            Console.WriteLine(message);
        }
    }

    static string J(object? i)
        => System.Text.Json.JsonSerializer.Serialize(i, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
}
