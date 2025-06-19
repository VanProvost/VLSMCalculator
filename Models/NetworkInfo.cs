namespace VLSMCalculator.Models;

public class NetworkInfo
{
    public string Address { get; set; } = "";
    public string Netmask { get; set; } = "";
    public string Wildcard { get; set; } = "";
    public string Network { get; set; } = "";
    public string HostMin { get; set; } = "";
    public string HostMax { get; set; } = "";
    public string Broadcast { get; set; } = "";
    public int Hosts { get; set; }
    public string Class { get; set; } = "";
    public string CIDR { get; set; } = "";
}
