using System.Text.Json;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Loads trace-derived and user-issue evaluation scenarios from JSON test data files.
/// Supports filtering by failure type, model, category, and scenario name.
/// Designed for use with xUnit <c>[MemberData]</c> for data-driven tests.
/// </summary>
public static class TraceScenarioLoader
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<TraceScenarioCollection> s_traceScenarios = new(LoadTraceScenarios);
    private static readonly Lazy<UserIssueScenarioCollection> s_issueScenarios = new(LoadIssueScenarios);

    /// <summary>
    /// Gets all trace-derived scenarios.
    /// </summary>
    public static IReadOnlyList<TraceScenario> AllTraceScenarios => s_traceScenarios.Value.Scenarios;

    /// <summary>
    /// Gets all user-issue scenarios.
    /// </summary>
    public static IReadOnlyList<UserIssueScenario> AllIssueScenarios => s_issueScenarios.Value.Scenarios;

    /// <summary>
    /// Returns trace scenarios as xUnit <c>[MemberData]</c> rows.
    /// Each row contains a single <see cref="TraceScenario"/> instance.
    /// </summary>
    public static IEnumerable<object[]> TraceScenarioData()
    {
        return AllTraceScenarios.Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns user-issue scenarios as xUnit <c>[MemberData]</c> rows.
    /// Each row contains a single <see cref="UserIssueScenario"/> instance.
    /// </summary>
    public static IEnumerable<object[]> IssueScenarioData()
    {
        return AllIssueScenarios.Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns trace scenarios filtered by failure type as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    /// <param name="failureType">Failure type to filter by (e.g., WRONG_TOOL, NO_TOOL_CALL, STATE_ERROR, CORRECT).</param>
    public static IEnumerable<object[]> TracesByFailureType(string failureType)
    {
        return AllTraceScenarios
            .Where(s => s.FailureType.Equals(failureType, StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns trace scenarios filtered by source model as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    /// <param name="model">Model name to filter by (e.g., "granite4:350m", "gemma3:270m").</param>
    public static IEnumerable<object[]> TracesByModel(string model)
    {
        return AllTraceScenarios
            .Where(s => s.SourceModel.Equals(model, StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns trace scenarios filtered by category as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    /// <param name="category">Category to filter by (e.g., "control", "query", "stt-robustness").</param>
    public static IEnumerable<object[]> TracesByCategory(string category)
    {
        return AllTraceScenarios
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns only regression scenarios (consistent failures across runs) as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    public static IEnumerable<object[]> RegressionScenarios()
    {
        return AllTraceScenarios
            .Where(s => s.IsRegression)
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns only scenarios that passed (positive examples) as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    public static IEnumerable<object[]> PassingScenarios()
    {
        return AllTraceScenarios
            .Where(s => s.FailureType.Equals("CORRECT", StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns user-issue scenarios filtered by category as xUnit <c>[MemberData]</c> rows.
    /// </summary>
    public static IEnumerable<object[]> IssuesByCategory(string category)
    {
        return AllIssueScenarios
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });
    }

    /// <summary>
    /// Returns a specific trace scenario by name, or null if not found.
    /// </summary>
    public static TraceScenario? GetTraceByName(string name)
    {
        return AllTraceScenarios.FirstOrDefault(
            s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a specific user-issue scenario by issue number, or null if not found.
    /// </summary>
    public static UserIssueScenario? GetIssueByNumber(int issueNumber)
    {
        return AllIssueScenarios.FirstOrDefault(s => s.SourceIssue == issueNumber);
    }

    private static TraceScenarioCollection LoadTraceScenarios()
    {
        var path = ResolveTestDataPath("light-agent-traces.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TraceScenarioCollection>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize trace scenarios from {path}");
    }

    private static UserIssueScenarioCollection LoadIssueScenarios()
    {
        var path = ResolveTestDataPath("light-agent-user-issues.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UserIssueScenarioCollection>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize issue scenarios from {path}");
    }

    private static string ResolveTestDataPath(string fileName)
    {
        // When running from test output directory, TestData is copied alongside the assembly
        var assemblyDir = Path.GetDirectoryName(typeof(TraceScenarioLoader).Assembly.Location)!;
        var outputPath = Path.Combine(assemblyDir, "TestData", fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        // Fallback: resolve from project root (e.g., during development or IDE test runner)
        var projectRoot = FindProjectRoot(assemblyDir);
        if (projectRoot is not null)
        {
            var projectPath = Path.Combine(projectRoot, "lucia.Tests", "TestData", fileName);
            if (File.Exists(projectPath))
            {
                return projectPath;
            }
        }

        throw new FileNotFoundException(
            $"Test data file '{fileName}' not found. Searched in '{outputPath}' and project root.",
            fileName);
    }

    private static string? FindProjectRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lucia-dotnet.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
