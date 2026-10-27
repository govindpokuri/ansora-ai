using System;
using System.Linq;

namespace OverlayCopilot.Services;
public static class CallMinutes
{
    public static string Estimate(CallReport report,long? available,DateTimeOffset now)
    {
        if(available is null)return "Sign in to estimate time";
        if(available<=0)return "No minutes available";
        var minutes=report.CapturePeriods.Sum(p=>Math.Max(0,((p.EndedAt??now)-p.StartedAt).TotalMinutes));
        var completed=report.Answers.Where(a=>a.CompletedAt is not null).ToArray();
        if(minutes<2 || completed.Length<3 || report.ReportedUsage.Total<=0)return "Estimating minutes left…";
        if(completed.Any(a=>a.Usage is null || a.UsageIncomplete))return "Minutes estimate unavailable";
        var left=available.Value/(report.ReportedUsage.Total/minutes);
        return left<1?"Less than 1 minute left":$"About {Math.Floor(left):N0} minutes left";
    }
}
