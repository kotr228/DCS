using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для визначення процесу з мережевого з'єднання
/// Використовує Windows API для отримання таблиці TCP/UDP з'єднань
/// </summary>
public class ProcessLookupService
{
    // Windows API structures and functions for TCP connection table
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, TCP_TABLE_CLASS tblClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, UDP_TABLE_CLASS tblClass, int reserved);

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_OWNER_PID_ALL = 5
    }

    private enum UDP_TABLE_CLASS
    {
        UDP_TABLE_OWNER_PID = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public byte localPort1;
        public byte localPort2;
        public byte localPort3;
        public byte localPort4;
        public uint remoteAddr;
        public byte remotePort1;
        public byte remotePort2;
        public byte remotePort3;
        public byte remotePort4;
        public int owningPid;

        public ushort LocalPort => (ushort)((localPort1 << 8) + localPort2);
        public ushort RemotePort => (ushort)((remotePort1 << 8) + remotePort2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public byte localPort1;
        public byte localPort2;
        public byte localPort3;
        public byte localPort4;
        public int owningPid;

        public ushort LocalPort => (ushort)((localPort1 << 8) + localPort2);
    }

    /// <summary>
    /// Знайти процес, що використовує заданий TCP порт
    /// </summary>
    public ProcessInfo? FindProcessByTcpPort(int localPort, string? remoteIP = null)
    {
        try
        {
            int bufferSize = 0;
            // Перший виклик для отримання розміру
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, 2, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, 2,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

                if (result != 0)
                    return null;

                int rowCount = Marshal.ReadInt32(tcpTablePtr);
                IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + 4);

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);

                    if (row.LocalPort == localPort)
                    {
                        // Якщо вказано remoteIP, перевіряємо його теж
                        if (remoteIP != null)
                        {
                            var remoteAddr = new IPAddress(row.remoteAddr);
                            if (remoteAddr.ToString() != remoteIP)
                            {
                                rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                                continue;
                            }
                        }

                        return GetProcessInfo(row.owningPid);
                    }

                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }
        }
        catch
        {
            // Fallback
        }

        return null;
    }

    /// <summary>
    /// Знайти процес, що використовує заданий UDP порт
    /// </summary>
    public ProcessInfo? FindProcessByUdpPort(int localPort)
    {
        try
        {
            int bufferSize = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, false, 2, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            IntPtr udpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                uint result = GetExtendedUdpTable(udpTablePtr, ref bufferSize, false, 2,
                    UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

                if (result != 0)
                    return null;

                int rowCount = Marshal.ReadInt32(udpTablePtr);
                IntPtr rowPtr = (IntPtr)((long)udpTablePtr + 4);

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);

                    if (row.LocalPort == localPort)
                    {
                        return GetProcessInfo(row.owningPid);
                    }

                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_UDPROW_OWNER_PID>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(udpTablePtr);
            }
        }
        catch
        {
            // Fallback
        }

        return null;
    }

    /// <summary>
    /// Отримати інформацію про процес за PID
    /// </summary>
    private ProcessInfo? GetProcessInfo(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return new ProcessInfo
            {
                ProcessId = pid,
                ProcessName = process.ProcessName,
                ApplicationPath = process.MainModule?.FileName ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Отримати всі активні TCP з'єднання з інформацією про процеси
    /// </summary>
    public List<ConnectionInfo> GetActiveTcpConnections()
    {
        var connections = new List<ConnectionInfo>();

        try
        {
            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, 2, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, 2,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

                if (result != 0)
                    return connections;

                int rowCount = Marshal.ReadInt32(tcpTablePtr);
                IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + 4);

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);

                    var localAddr = new IPAddress(row.localAddr);
                    var remoteAddr = new IPAddress(row.remoteAddr);

                    var processInfo = GetProcessInfo(row.owningPid);

                    connections.Add(new ConnectionInfo
                    {
                        LocalAddress = localAddr.ToString(),
                        LocalPort = row.LocalPort,
                        RemoteAddress = remoteAddr.ToString(),
                        RemotePort = row.RemotePort,
                        Protocol = "TCP",
                        State = ((TcpState)row.state).ToString(),
                        ProcessId = row.owningPid,
                        ProcessName = processInfo?.ProcessName ?? "Unknown",
                        ApplicationPath = processInfo?.ApplicationPath ?? string.Empty
                    });

                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }
        }
        catch
        {
            // Fallback
        }

        return connections;
    }
}

/// <summary>
/// Інформація про процес
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ApplicationPath { get; set; } = string.Empty;
}

/// <summary>
/// Інформація про з'єднання з процесом
/// </summary>
public class ConnectionInfo
{
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ApplicationPath { get; set; } = string.Empty;
}
