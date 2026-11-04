namespace T2.ACRemote.Common
{
    public interface IIoController
    {
        bool ReadBridgeRunning();
        bool ReadOutput(int outputIndex);
        void WriteOutput(int outputIndex, bool closed);
    }
}
