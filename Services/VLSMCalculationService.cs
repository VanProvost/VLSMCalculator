using VLSMCalculator.Models;
namespace VLSMCalculator.Services;
public class VLSMCalculationService
{
    public static List<SubnetInfo> GetVLSMAllocation(string baseNetwork, int baseCIDR, int[] requirements)
    {
        // Sort requirements in descending order for efficient allocation
        var sortedRequirements = requirements.OrderByDescending(x => x).ToArray();

        // Convert base network to integer
        var baseNetworkInt = NetworkCalculationService.ConvertIPToUInt32(baseNetwork);

        // Calculate available address space
        var availableHostBits = 32 - baseCIDR;
        var totalAvailableAddresses = (uint)Math.Pow(2, availableHostBits);

        var allocatedSubnets = new List<SubnetInfo>();
        var currentNetworkInt = baseNetworkInt;
        var remainingSpace = totalAvailableAddresses;

        foreach (var hostReq in sortedRequirements)
        {
            // Calculate required subnet bits
            var requiredHostBits = NetworkCalculationService.GetRequiredSubnetBits(hostReq);
            var subnetBits = availableHostBits - requiredHostBits;

            if (subnetBits < 0)
                throw new InvalidOperationException($"Cannot allocate subnet for {hostReq} hosts - insufficient address space");

            // Calculate subnet size
            var subnetSize = (uint)Math.Pow(2, requiredHostBits);

            if (subnetSize > remainingSpace)
                throw new InvalidOperationException($"Insufficient remaining address space for subnet requiring {hostReq} hosts");

            // Create subnet info
            var subnetInfo = GetSubnetInfo(currentNetworkInt, subnetBits, baseCIDR);
            subnetInfo.RequiredHosts = hostReq;

            allocatedSubnets.Add(subnetInfo);

            // Move to next available network address
            currentNetworkInt += subnetSize;
            remainingSpace -= subnetSize;
        }

        return allocatedSubnets;
    }

    private static SubnetInfo GetSubnetInfo(uint networkInt, int subnetBits, int originalCIDR)
    {
        var newCIDR = originalCIDR + subnetBits;
        if (newCIDR > 30)
            throw new InvalidOperationException("Cannot create subnet - would result in CIDR > 30");

        // Calculate subnet size
        var hostBits = 32 - newCIDR;
        var subnetSize = (uint)Math.Pow(2, hostBits);
        var usableHosts = (int)(subnetSize - 2);

        // Calculate addresses
        var networkAddress = NetworkCalculationService.ConvertUInt32ToIP(networkInt);
        var broadcastInt = networkInt + subnetSize - 1;
        var broadcastAddress = NetworkCalculationService.ConvertUInt32ToIP(broadcastInt);
        var firstHostAddress = NetworkCalculationService.ConvertUInt32ToIP(networkInt + 1);
        var lastHostAddress = NetworkCalculationService.ConvertUInt32ToIP(broadcastInt - 1);

        return new SubnetInfo
        {
            Network = $"{networkAddress}/{newCIDR}",
            NetworkAddress = networkAddress,
            CIDR = newCIDR,
            SubnetMask = NetworkCalculationService.ConvertCIDRToMask(newCIDR),
            Wildcard = NetworkCalculationService.GetWildcardMask(newCIDR),
            FirstHost = usableHosts > 0 ? firstHostAddress : "N/A",
            LastHost = usableHosts > 0 ? lastHostAddress : "N/A",
            Broadcast = broadcastAddress,
            TotalHosts = (int)subnetSize,
            UsableHosts = usableHosts,
            SubnetSize = subnetSize
        };
    }
}
