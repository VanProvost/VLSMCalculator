namespace VLSMCalculator.Models;

public class AddressBlock
{
    public uint NetworkInt { get; set; }
    public int CIDR { get; set; }
    public string NetworkAddress { get; set; } = "";
    public uint Size { get; set; }
    public bool IsAllocated { get; set; }
}
