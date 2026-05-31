using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;

namespace lucia.EvalHarness.Reports;

/// <summary>
/// Generates a self-contained HTML report from evaluation results.
/// Loads the HTML template from disk (deployed alongside the assembly under
/// <c>Reports/Templates/report-template.html</c>), injects eval data as JSON,
/// and embeds an offline stylesheet so the generated report works without network access.
/// </summary>
public static class HtmlReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Generates and writes an HTML report file.
    /// Returns the path of the written file.
    /// </summary>
    public static string Generate(EvalRunResult result, GpuInfo gpuInfo, string reportDir)
    {
        Directory.CreateDirectory(reportDir);

        var timestamp = result.StartedAt.ToString("yyyyMMdd_HHmmss");
        var htmlPath = Path.Combine(reportDir, $"eval-{timestamp}.html");

        var data = HtmlReportData.FromEvalResult(result, gpuInfo);
        var dataJson = JsonSerializer.Serialize(data, JsonOptions);

        var template = LoadTemplate();
        var html = template
            .Replace("<!--__TAILWIND_CSS__-->", $"<style>{TailwindStaticCss.Css}</style>")
            .Replace("/*__EVAL_DATA__*/{}", dataJson);

        File.WriteAllText(htmlPath, html);
        return htmlPath;
    }

    private static string LoadTemplate()
    {
        // Try loading from the Templates directory relative to the assembly
        var assemblyDir = Path.GetDirectoryName(typeof(HtmlReportGenerator).Assembly.Location)!;
        var templatePath = Path.Combine(assemblyDir, "Reports", "Templates", "report-template.html");

        if (File.Exists(templatePath))
            return File.ReadAllText(templatePath);

        // Fallback: look relative to CWD
        var cwdPath = Path.Combine("Reports", "Templates", "report-template.html");
        if (File.Exists(cwdPath))
            return File.ReadAllText(cwdPath);

        // Fallback: look relative to the project source (for development)
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Reports", "Templates", "report-template.html");
        if (File.Exists(sourcePath))
            return File.ReadAllText(sourcePath);

        throw new FileNotFoundException(
            "HTML report template not found. Expected at: " + templatePath);
    }
}
