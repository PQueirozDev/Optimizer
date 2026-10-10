using System.ComponentModel;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

internal static class RecoveryTests
{
    public static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        var plan = Guid.NewGuid().ToString();
        var session = new GamingService.GameSession
        { StoppedServices = new() { "BITS", "WSearch" }, PreviousPowerPlan = plan };
        var saves = 0;
        var count = GamingService.RestoreSession(session, name =>
        { if (name == "WSearch") throw new Win32Exception("Simulated failure"); }, _ => false, _ => saves++);
        Check(count == 1 && session.StoppedServices.SequenceEqual(new[] { "WSearch" }) &&
            session.PreviousPowerPlan == plan && session.RecoveryPending && saves == 2,
            "Partial recovery preserves failures and checkpoints successful items");
        var called = new List<string>();
        count = GamingService.RestoreSession(session, called.Add, _ => true, _ => saves++);
        Check(count == 1 && called.SequenceEqual(new[] { "WSearch" }) && session.StoppedServices.Count == 0 &&
            session.PreviousPowerPlan == null, "Retry only restores pending items");
        session.PreviousPowerPlan = plan;
        GamingService.RestoreSession(session, _ => throw new Exception("Unexpected service"),
            _ => throw new Win32Exception("Simulated power failure"), _ => { });
        Check(session.PreviousPowerPlan == plan, "Power command exceptions preserve the original plan");
        session.StoppedServices.Add("UntrustedService");
        session.PreviousPowerPlan = "invalid";
        GamingService.RestoreSession(session, _ => throw new Exception("Untrusted service executed"),
            _ => throw new Exception("Invalid plan executed"), _ => { });
        Check(session.StoppedServices.Count == 1 && session.PreviousPowerPlan == "invalid",
            "Invalid recovery entries are never executed or silently discarded");
        var entry = ActivityEntry.Parse("[10:00] [ERROR] Falha no serviço");
        Check(entry.Matches("SERVIÇO", "ERROR") && !entry.Matches("", "SUCCESS"), "History searches ignore case and respect severity");
        Check(ActivityEntry.Parse("Plain message").Matches("Plain", "INFO"), "Unstructured log lines remain searchable as information");
    }
}
