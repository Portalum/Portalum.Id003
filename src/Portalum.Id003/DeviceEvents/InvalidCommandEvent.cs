namespace Portalum.Id003.DeviceEvents
{
    /// <summary>
    /// Invalid Command
    /// </summary>
    public class InvalidCommandEvent : DeviceEventBase
    {
        public InvalidCommandEvent() : base(0x4B)
        { }
    }
}
