namespace IziProxy;

public interface IRemoteClient
{
    bool TestConnection(ServerConfig serverConfig);
    bool UploadTestScript(ServerConfig serverConfig);
    bool UploadFile(string localFilePath, string remoteFileName, ServerConfig serverConfig);
    bool RunTestScript(ServerConfig serverConfig);
    IRemoteCommand RunSudoCommand(ServerConfig serverConfig, string command);
}
