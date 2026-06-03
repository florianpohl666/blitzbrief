namespace Blitztext.Core.Models;

public enum WorkflowPhaseKind
{
    Idle,
    Recording,
    Processing,
    Done,
    Error
}

public sealed record WorkflowPhase(WorkflowPhaseKind Kind, string Message)
{
    public static WorkflowPhase Idle { get; } = new(WorkflowPhaseKind.Idle, "Bereit");
}
