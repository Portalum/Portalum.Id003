using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nager.DataFragmentationHandler;
using Nito.HashAlgorithms;
using Portalum.Id003.DeviceEvents;
using Portalum.Id003.Helpers;
using System.Text;

namespace Portalum.Id003
{
    public class Id003Client : IDisposable
    {
        private readonly ILogger<Id003Client> _logger;
        private readonly IDeviceCommunication _deviceCommunication;
        private readonly CRC16 _crcCalculator;
        private readonly Dictionary<byte, DeviceEventBase> _statusDeviceEvents;
        private readonly DataPackageHandler _dataPackageHandler;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);

        //Sync (1 byte) + PackageLength (1 byte) + Checksum (2 byte)
        private readonly int _packageFramingSize = 4;
        private readonly int _checksumLength = 2;

        private readonly byte _syncByte = 0xFC;
        private readonly byte _acknowledgeByte = 0x50;
        private int _nextAmount = 0;

        public event Action<string> StatusChanged;
        public event Action<string> OperationStatusChanged;
        public event Action<int> NewAmountInCashBox;
        public event Action CashBoxReset;

        public Id003Client(
            IDeviceCommunication deviceCommunication,
            ILogger<Id003Client> logger = default)
        {
            if (logger == null)
            {
                logger = new NullLogger<Id003Client>();
            }
            this._logger = logger;

            this._cancellationTokenSource = new CancellationTokenSource();
            this._crcCalculator = new CRC16(CRC16.Definition.Ccitt);
            this._statusDeviceEvents = new Dictionary<byte, DeviceEventBase>();

            var statusDeviceEvents = new List<DeviceEventBase>
            {
                //new InvalidCommandEvent(),

                //Boot status
                new PowerUpDeviceEvent(),
                new InitializeDeviceEvent(),

                //Accept money status
                new DisableDeviceEvent(),
                new IdlingDeviceEvent(),

                //Device Info status
                new StackerOpenDeviceEvent(),

                //Money status
                new AcceptingDeviceEvent(),
                new EscrowDeviceEvent(),
                new StackedDeviceEvent(),
                new StackingDeviceEvent(),
                new VendValidDeviceEvent(),
                new RejectingDeviceEvent()
            };

            foreach (var deviceEvent in statusDeviceEvents)
            {
                this._statusDeviceEvents.Add(deviceEvent.Key, deviceEvent);
            }

            this._dataPackageHandler = new DataPackageHandler(
                dataPackageAnalyzer: new StartTokenWithLengthInfoDataPackageAnalyzer(this._syncByte),
                logger: logger);

            this._deviceCommunication = deviceCommunication;
            this._deviceCommunication.DataReceived += this._dataPackageHandler.AddData;

            _ = Task.Run(async () =>
            {
                while (!this._cancellationTokenSource.IsCancellationRequested)
                {
                    if (!this._deviceCommunication.IsConnected)
                    {
                        await Task.Delay(1000, this._cancellationTokenSource.Token).ContinueWith(task => { });
                        continue;
                    }

                    var receivedData = await this.SendAndReceiveAsync(new byte[] { (byte)DeviceCommand.GetStatus });
                    var deviceEvent = this.AnalyzeStatusData(receivedData);
                    if (deviceEvent == null)
                    {
                        await Task.Delay(100, this._cancellationTokenSource.Token).ContinueWith(task => { });
                        continue;
                    }

                    if (deviceEvent is StackerOpenDeviceEvent)
                    {
                        this.CashBoxReset?.Invoke();
                    }

                    if (deviceEvent is EscrowDeviceEvent)
                    {
                        var data = new byte[] { (byte)EscrowCommand.Stack1 };
                        var receivedData1 = await this.SendAndReceiveAsync(data);
                        this._nextAmount = this.GetAmount(receivedData[1]);
                    }

                    if (deviceEvent is VendValidDeviceEvent)
                    {
                        await this.SendAckowledgeAsync();
                    }


                    if (deviceEvent is StackedDeviceEvent)
                    {
                        if (this._nextAmount > 0)
                        {
                            this.NewAmountInCashBox?.Invoke(this._nextAmount);
                            this.StatusChanged?.Invoke($"New amount {this._nextAmount}");
                            this._nextAmount = 0;
                        }
                    }
                }
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (disposing)
            {
                this._cancellationTokenSource?.Cancel();
                this._cancellationTokenSource?.Dispose();

                this._deviceCommunication.DataReceived -= this._dataPackageHandler.AddData;
                this._crcCalculator.Dispose();

                this._semaphoreSlim.Dispose();
            }
        }


        private int GetAmount(byte amountByte)
        {
            switch (amountByte)
            {
                case 0x62:
                    return 5;
                case 0x63:
                    return 10;
                case 0x64:
                    return 20;
                case 0x65:
                    return 50;
                case 0x66:
                    return 100;
                case 0x67:
                    return 200;
            }

            return 0;
        }

        public async Task<string> GetVersionAsync()
        {
            var receivedData = await this.SendAndReceiveAsync(new byte[] { (byte)DeviceCommand.GetVersion });
            return Encoding.ASCII.GetString(receivedData);
        }

        public async Task<string> GetBootVersionAsync()
        {
            var receivedData = await this.SendAndReceiveAsync(new byte[] { (byte)DeviceCommand.GetBootVersion });
            return Encoding.ASCII.GetString(receivedData);
        }

        public async Task InitializeAsync()
        {
            await this.ResetAsync();
        }

        private async Task SendAckowledgeAsync()
        {
            var data = new byte[] { this._acknowledgeByte };
            await this.SendAsync(data);
        }

        private async Task<bool> SendDataWithChecksumAsync(byte[] data)
        {
            if (!this._deviceCommunication.IsConnected)
            {
                return false;
            }

            using var memoryStream = new MemoryStream();
            memoryStream.WriteByte(this._syncByte);

            //Calculate PackageLength
            memoryStream.WriteByte((byte)(data.Length + this._packageFramingSize));

            await memoryStream.WriteAsync(data.AsMemory());

            memoryStream.Seek(0, SeekOrigin.Begin);
            var checksumData = await this._crcCalculator.ComputeHashAsync(memoryStream);
            await memoryStream.WriteAsync(checksumData.AsMemory());

            await this._deviceCommunication.SendAsync(memoryStream.ToArray());

            return true;
        }

        private async Task<bool> SendAsync(byte[] sendData)
        {
            try
            {
                await this._semaphoreSlim.WaitAsync();
                return await this.SendDataWithChecksumAsync(sendData);
            }
            finally
            {
                this._semaphoreSlim.Release();
            }
        }

        private async Task<byte[]> SendAndReceiveAsync(
            byte[] sendData,
            CancellationToken cancellationToken = default)
        {
            await this._semaphoreSlim.WaitAsync();

            byte[] buffer = null;

            using var receiveCancellationTokenSource = new CancellationTokenSource();
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, receiveCancellationTokenSource.Token);

            void NewDataPackage(DataPackage dataPackage)
            {
                if (!this.CheckChecksum(dataPackage))
                {
                    return;
                }

                buffer = dataPackage.Data.ToArray();

                receiveCancellationTokenSource.Cancel();
            }

            try
            {
                this._dataPackageHandler.NewDataPackage += NewDataPackage;

                await this.SendDataWithChecksumAsync(sendData);
                var isDataReceived = await Task.Delay(TimeSpan.FromMilliseconds(2000), linkedCancellationTokenSource.Token)
                    .ContinueWith(task =>
                    {
                        if (task.IsCanceled)
                        {
                            if (receiveCancellationTokenSource.IsCancellationRequested)
                            {
                                return true;
                            }
                        }

                        return false;
                    });

                if (!isDataReceived)
                {
                    return Array.Empty<byte>();
                }

                return buffer;
            }
            finally
            {
                this._dataPackageHandler.NewDataPackage -= NewDataPackage;
                this._semaphoreSlim.Release();
            }
        }

        private bool CheckChecksum(DataPackage dataPackage)
        {
            var rawDataSpan = dataPackage.RawData.AsSpan();

            var rawDataLengthWithoutChecksum = rawDataSpan.Length - this._checksumLength;

            var dataWithoutChecksum = rawDataSpan.Slice(0, rawDataLengthWithoutChecksum);
            var checksumData = this._crcCalculator.ComputeHash(dataWithoutChecksum.ToArray());

            if (!checksumData.AsSpan().SequenceEqual(rawDataSpan.Slice(rawDataLengthWithoutChecksum)))
            {
                this._logger.LogError($"{nameof(CheckChecksum)} - Invalid checksum {BitConverter.ToString(dataPackage.RawData)}");
                return false;
            }

            return true;
        }

        private DeviceEventBase AnalyzeStatusData(Span<byte> data)
        {
            var eventKey = data[0];

            if (this._statusDeviceEvents.TryGetValue(eventKey, out var deviceEvent))
            {
                this.OperationStatusChanged?.Invoke(deviceEvent.GetType().Name);
                return deviceEvent;
            }

            this.OperationStatusChanged?.Invoke($"Unknown status {eventKey:X2}");
            return null;
        }

        public async Task<bool> EnableAsync()
        {
            var data = new byte[] { 0xC3, 0x00 };
            var receivedData = await this.SendAndReceiveAsync(data);

            return true;
        }

        public async Task<bool> DisableAsync()
        {
            var configByte = new byte();
            configByte = BitHelper.SetBit(configByte, 0);

            var data = new byte[] { 0xC3, configByte };
            var receivedData = await this.SendAndReceiveAsync(data);

            return true;
        }

        public async Task ResetAsync()
        {
            await this.SendAsync(new byte[] { (byte)DeviceCommand.Reset });

            await Task.Delay(2000);

            //Setting commands

            var setDisable = new byte[] { 0xC0, 0x00, 0x00 }; //SET_DENOM
            await this.SendAsync(setDisable);

            await Task.Delay(500);

            var setSecurity = new byte[] { 0xC1, 0x00, 0x00 }; //SET_SECURITY
            await this.SendAsync(setSecurity);

            await Task.Delay(500);

            var configByte = new byte();
            configByte = BitHelper.SetBit(configByte, 0);

            var setInhibit = new byte[] { 0xC3, configByte }; //SET_INHIBIT
            await this.SendAsync(setInhibit);

            await Task.Delay(500);

            var setDirection = new byte[] { 0xC4, 0x00 }; //SET_DIRECTION
            await this.SendAsync(setDirection);

            await Task.Delay(500);
            
            var setFunction = new byte[] { 0xC5, 0x00, 0x00 }; //SET_OPT_FUNC
            await this.SendAsync(setFunction);

            await Task.Delay(500);

            var setMode = new byte[] { 0xC2, 0x00 }; //SET_MODE
            await this.SendAsync(setMode);

            await Task.Delay(500);
        }
    }
}
