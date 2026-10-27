using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using YHPCReviewer.Models;

namespace YHPCReviewer.Diagnostics;

/// <summary>Temel donanım ve işletim sistemi bilgisini toplar. Hiçbir ayarı değiştirmez.</summary>
public static partial class SystemInfoCollector
{
    [GeneratedRegex(@"-\d{4}$")]
    private static partial Regex PseudoAdapterRegex();

    public static SystemInfo Collect()
    {
        var info = new SystemInfo
        {
            ComputerName = Environment.MachineName,
            UserName = Environment.UserName,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            CpuThreads = Environment.ProcessorCount,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };

        ReadOs(info);
        ReadCpu(info);
        ReadMemory(info);
        ReadGpus(info);
        ReadMotherboard(info);
        info.Disks = ReadDisks();
        info.Adapters = ReadAdapters();
        return info;
    }

    private static void ReadOs(SystemInfo info)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var build = key?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
        var ubr = key?.GetValue("UBR");
        var display = key?.GetValue("DisplayVersion") as string;

        // Windows 11, kayıt defterinde hâlâ "Windows 10" yazar; derleme numarasıyla düzelt.
        if (int.TryParse(build, out var b) && b >= 22000)
            product = product.Replace("Windows 10", "Windows 11");

        info.OsName = product;
        info.OsBuild = $"{display} (Derleme {build}{(ubr is null ? "" : "." + ubr)})".Trim();
    }

    private static void ReadCpu(SystemInfo info)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        info.Cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Bilinmiyor";

        TryWmi("SELECT NumberOfCores FROM Win32_Processor", o =>
            info.CpuCores += Convert.ToInt32(o["NumberOfCores"]));
    }

    private static void ReadMemory(SystemInfo info)
    {
        var status = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(status))
        {
            info.RamTotalGb = status.ullTotalPhys / 1024.0 / 1024 / 1024;
            info.RamAvailableGb = status.ullAvailPhys / 1024.0 / 1024 / 1024;
        }
    }

    private static void ReadGpus(SystemInfo info)
    {
        TryWmi("SELECT Name, DriverVersion FROM Win32_VideoController", o =>
        {
            var name = o["Name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                info.Gpus.Add($"{name} (sürücü {o["DriverVersion"]})");
        });
    }

    private static void ReadMotherboard(SystemInfo info)
    {
        TryWmi("SELECT Manufacturer, Product FROM Win32_BaseBoard", o =>
            info.Motherboard = $"{o["Manufacturer"]} {o["Product"]}".Trim());
    }

    private static List<DiskInfo> ReadDisks()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var list = new List<DiskInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
            list.Add(new DiskInfo
            {
                Name = d.Name.TrimEnd('\\'),
                Label = d.VolumeLabel,
                Format = d.DriveFormat,
                TotalBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace,
                IsSystem = string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase)
            });
        }
        return list;
    }

    private static List<AdapterInfo> ReadAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            // "WAN Miniport" ve "…-QoS Packet Scheduler-0000" gibi sanal filtre katmanları gerçek ağ kartı değildir.
            if (nic.Description.Contains("Miniport", StringComparison.OrdinalIgnoreCase) || PseudoAdapterRegex().IsMatch(nic.Description)) continue;

            var props = nic.GetIPProperties();
            var mac = nic.GetPhysicalAddress().ToString();
            list.Add(new AdapterInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                Type = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "Kablolu (Ethernet)",
                    _ => nic.NetworkInterfaceType.ToString()
                },
                Status = nic.OperationalStatus == OperationalStatus.Up ? "Bağlı" : "Bağlı değil",
                SpeedMbps = nic.OperationalStatus == OperationalStatus.Up && nic.Speed > 0 ? nic.Speed / 1_000_000 : 0,
                Addresses = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString()).ToList(),
                Gateways = props.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .Where(g => g != "0.0.0.0" && g != "::").ToList(),
                DnsServers = props.DnsAddresses.Select(a => a.ToString()).ToList(),
                Mac = string.IsNullOrEmpty(mac) ? null : string.Join(":", Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2)))
            });
        }
        return list;
    }

    internal static void TryWmi(string query, Action<ManagementBaseObject> onRow, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(10);
            foreach (var o in searcher.Get())
                using (o) onRow(o);
        }
        catch
        {
            // WMI bazı sistemlerde bozuk ya da kısıtlı olabilir; bilgi eksik kalır ama analiz sürer.
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
