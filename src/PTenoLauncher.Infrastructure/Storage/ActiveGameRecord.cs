using System.Diagnostics;
namespace PTenoLauncher.Infrastructure.Storage;
public sealed record ActiveGameRecord(int Pid, long StartedTicks, string JavaPath)
{
    public static bool IsRunning(ActiveGameRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartedTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}