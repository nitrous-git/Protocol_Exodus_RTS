public interface IControllable
{
    CommandType CurrentCommand { get; }
    bool CanReceiveCommands { get; }

    void IssueCommand(CommandType commandType, CommandContext context);
}