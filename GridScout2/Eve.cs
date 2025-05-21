using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GridScout2
{
    public class Eve
    { 

        public class GameClient : GameClientProcessSummaryStruct
        {
            public ulong? UIRootAddress;
        }

        public class GameClientProcessSummaryStruct
        {
            public int processId;

            public required string mainWindowId;

            public required string mainWindowTitle;

            public int mainWindowZIndex;
        }

        static Process[] GetWindowsProcessesLookingLikeEVEOnlineClient() =>
            Process.GetProcessesByName("exefile");

        public static IEnumerable<GameClientProcessSummaryStruct> ListGameClientProcesses()
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
                    return new GameClientProcessSummaryStruct
                    {
                        processId = process.Id,
                        mainWindowId = process.MainWindowHandle.ToInt64().ToString(),
                        mainWindowTitle = process.MainWindowTitle,
                        mainWindowZIndex = zIndexFromWindowHandle(process.MainWindowHandle) ?? 9999,
                    };
                });

            return processes;
        }

    }
}
