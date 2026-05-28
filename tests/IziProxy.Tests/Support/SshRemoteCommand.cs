using Renci.SshNet;

namespace IziProxy;

public sealed class SshRemoteCommand : IRemoteCommand
{
    public SshRemoteCommand(SshCommand command)
    {
        Command = command;
    }

    public SshCommand Command { get; }

    public string Result => Command.Result;
}
