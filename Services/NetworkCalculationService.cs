using System.Net;
using VLSMCalculator.Models;
namespace VLSMCalculator.Services;

public class NetworkCalculationService
{
    public static uint ConvertIPToUInt32(string ip)
    {
        var ipObj = IPAddress.Parse(ip);
        var bytes = ipObj.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    public static string ConvertUInt32ToIP(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    public static string ConvertCIDRToMask(int cidr)
    {
        if (cidr < 0 || cidr > 32)
            throw new ArgumentException("Invalid CIDR notation. Must be between 0 and 32.");

        uint mask = 0;
        for (int i = 0; i < cidr; i++)
        {
            mask |= (uint)(1 << (31 - i));
        }

        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    public static int GetRequiredSubnetBits(int hostCount)
    {
        // Add 2 for network and broadcast addresses
        int totalAddresses = hostCount + 2;
        
        // Calculate required bits (power of 2)
        int bits = (int)Math.Ceiling(Math.Log(totalAddresses, 2));
        
        return bits;
    }

    public static string GetWildcardMask(int cidr)
    {
        var subnetMask = ConvertCIDRToMask(cidr);
        var subnetMaskInt = ConvertIPToUInt32(subnetMask);
        var wildcardMaskInt = subnetMaskInt ^ 0xFFFFFFFF;
        
        return ConvertUInt32ToIP(wildcardMaskInt);
    }

    public static NetworkInfo GetNetworkInfo(string ip, int cidrBits)
    {
        // Validate IP address
        if (!IPAddress.TryParse(ip, out _))
            throw new ArgumentException($"Invalid IP address: {ip}");

        // Validate CIDR
        if (cidrBits < 0 || cidrBits > 32)
            throw new ArgumentException($"Invalid CIDR notation: {cidrBits}. Must be between 0 and 32.");

        // Convert IP to 32-bit integer
        var ipInt = ConvertIPToUInt32(ip);

        // Calculate subnet mask
        var subnetMask = ConvertCIDRToMask(cidrBits);
        var subnetMaskInt = ConvertIPToUInt32(subnetMask);

        // Calculate network address
        var networkInt = ipInt & subnetMaskInt;
        var networkAddress = ConvertUInt32ToIP(networkInt);

        // Calculate broadcast address
        var wildcardMaskInt = subnetMaskInt ^ 0xFFFFFFFF;
        var broadcastInt = networkInt | wildcardMaskInt;
        var broadcastAddress = ConvertUInt32ToIP(broadcastInt);

        // Calculate first and last host addresses
        var firstHostInt = networkInt + 1;
        var lastHostInt = broadcastInt - 1;
        var firstHostAddress = ConvertUInt32ToIP(firstHostInt);
        var lastHostAddress = ConvertUInt32ToIP(lastHostInt);

        // Calculate number of hosts
        var totalHosts = Math.Pow(2, (32 - cidrBits));
        var usableHosts = (int)(totalHosts - 2);

        // Determine network class
        var firstOctet = int.Parse(ip.Split('.')[0]);
        var networkClass = firstOctet switch
        {
            >= 1 and <= 126 => "A",
            >= 128 and <= 191 => "B",
            >= 192 and <= 223 => "C",
            >= 224 and <= 239 => "D (Multicast)",
            >= 240 and <= 255 => "E (Reserved)",
            _ => "Classless"
        };

        return new NetworkInfo
        {
            Address = ip,
            Netmask = subnetMask,
            Wildcard = ConvertUInt32ToIP(wildcardMaskInt),
            Network = $"{networkAddress}/{cidrBits}",
            HostMin = usableHosts > 0 ? firstHostAddress : "N/A",
            HostMax = usableHosts > 0 ? lastHostAddress : "N/A",
            Broadcast = broadcastAddress,
            Hosts = usableHosts,
            Class = networkClass,
            CIDR = $"/{cidrBits}"
        };
    }
}
