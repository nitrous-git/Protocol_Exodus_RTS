using UnityEngine;

public class MoveState : UnitState<UnitBase>
{
    private readonly Vector3 destination;
    private bool pathRequested;

    public MoveState(Vector3 destination)
    {
        this.destination = destination;
    }

    protected override void OnEnterTyped(UnitBase unit)
    {
        if (unit.Motor == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        pathRequested = unit.Motor.MoveTo(destination);
        //Debug.Log("pathRequested is " + pathRequested);

        if (!pathRequested)
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }

    protected override void TickTyped(UnitBase unit)
    {
        if (!pathRequested)
            return;

        if (unit.Motor == null || unit.Motor.HasArrived)
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }
}
