using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OverlayCopilot.Services;
public sealed class CallAutoSave
{
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1,1);
    private readonly Dictionary<Guid,long> saved = new();
    public string LastPath { get; private set; } = "";
    public CallAutoSave(string? directory = null) => this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Ansora AI Community","Calls");
    public bool IsSaved(Guid id,long revision) { lock(saved) return saved.TryGetValue(id,out var value) && value>=revision; }
    public async Task SaveAsync(CallReport report,long revision)
    {
        await gate.WaitAsync();
        try
        {
            if(IsSaved(report.Id,revision))return;
            var path=Path.Combine(directory,$"call-{report.Id:N}");
            await Task.Run(async () => {
                Directory.CreateDirectory(directory);
                await CallExporter.WriteAtomicAsync(path+".json",CallExporter.Json(report));
                await CallExporter.WriteAtomicAsync(path+".html",CallExporter.Html(report));
            });
            LastPath=path+".html"; lock(saved) saved[report.Id]=revision;
        }
        finally { gate.Release(); }
    }
}
