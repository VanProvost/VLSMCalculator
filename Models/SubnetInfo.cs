namespace VLSMCalculator.Models;

public class SubnetInfo
{
    public string Network { get; set; } = "";
    public string NetworkAddress { get; set; } = "";
    public int CIDR { get; set; }
    public string SubnetMask { get; set; } = "";
    public string Wildcard { get; set; } = "";
    public string FirstHost { get; set; } = "";
    public string LastHost { get; set; } = "";
    public string Broadcast { get; set; } = "";
    public int TotalHosts { get; set; }
    public int UsableHosts { get; set; }
    public int RequiredHosts { get; set; }
    public uint SubnetSize { get; set; }
}
