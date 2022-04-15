namespace Portalum.Id003.DeviceEvents
{
    /// <summary>
    /// Escrow
    /// </summary>
    public class EscrowDeviceEvent : DeviceEventBase
    {
        public string Denom { get; private set; }

        public EscrowDeviceEvent() : base(0x13)
        { }

        public override void ParseData(Span<byte> data)
        {
            this.Denom = data[1].ToString("X2");
        }
    }
}
