namespace GridScout2
{
    public record GameClient
    {
        public string? mainWindowTitle;
        public required int processId;
        public required long mainWindowId;
        public ulong uiRootAddress;
    }
}
