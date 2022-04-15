namespace Portalum.Id003.DeviceEvents
{
    public abstract class DeviceEventBase
    {
        public byte Key { get; private set; }

        public DeviceEventBase(byte key)
        {
            this.Key = key;
        }

        public virtual void ParseData(Span<byte> data)
        {

        }
    }
}
