namespace Portalum.Id003
{
    /// <summary>
    /// Device Command
    /// </summary>
    public enum DeviceCommand : byte
    {
        /// <summary>
        /// Status request
        /// </summary>
        GetStatus = 0x11,

        /// <summary>
        /// Reset
        /// </summary>
        Reset = 0x40,

        /// <summary>
        /// Get Version
        /// </summary>
        GetVersion = 0x88,

        /// <summary>
        /// Get Boot Version
        /// </summary>
        GetBootVersion = 0x89,
    }
}
