using read_memory_64_bit;
using System.Diagnostics;

namespace GridScout2
{
  public class Eve
  {

    static Process[] GetWindowsProcessesLookingLikeEVEOnlineClient() =>
        Process.GetProcessesByName("exefile");

    public static IEnumerable<GameClient> ListGameClientProcesses()
    {
      var allWindowHandlesInZOrder = WinApi.ListWindowHandlesInZOrder();

      int? zIndexFromWindowHandle(IntPtr windowHandleToSearch) =>
          allWindowHandlesInZOrder
          .Select((windowHandle, index) => (windowHandle, index: (int?)index))
          .FirstOrDefault(handleAndIndex => handleAndIndex.windowHandle == windowHandleToSearch)
          .index;

      var processes =
          GetWindowsProcessesLookingLikeEVEOnlineClient()
          .Select(process =>
          {
            return new GameClient
            {
              processId = process.Id,
              mainWindowId = process.MainWindowHandle,
              mainWindowTitle = process.MainWindowTitle,
              mainWindowZIndex = zIndexFromWindowHandle(process.MainWindowHandle) ?? 9999,
            };
          });

      return processes;
    }

  }
}
