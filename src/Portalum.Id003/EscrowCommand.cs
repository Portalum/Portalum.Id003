namespace Portalum.Id003
{
    public enum EscrowCommand : byte
    {
        /// <summary>
        /// Stack 1
        /// </summary>
        Stack1 = 0x41,

        /// <summary>
        /// Stack 2
        /// </summary>
        Stack2 = 0x42,

        /// <summary>
        /// Return
        /// </summary>
        Return = 0x43,

        /// <summary>
        /// Hold
        /// </summary>
        Hold = 0x44,

        /// <summary>
        /// Wait
        /// </summary>
        Wait = 0x45,
    }
}
