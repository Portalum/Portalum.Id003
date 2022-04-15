using Microsoft.Extensions.Logging;
using System;
using System.IO.Ports;
using System.Windows;

namespace Portalum.Id003.ControlPanel
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDeviceCommunication _deviceCommunication;
        private readonly Id003Client _client;
        private int _amount;

        public MainWindow()
        {
            this._loggerFactory = LoggerFactory.Create(builder =>
                builder.AddFile("default.log", LogLevel.Trace, outputTemplate: "{Timestamp:HH:mm:ss.fff} {Level:u3} {SourceContext} {Message:lj}{NewLine}{Exception}")
                .SetMinimumLevel(LogLevel.Trace));

            this.InitializeComponent();

            this.ButtonConnect.IsEnabled = true;
            this.ButtonDisconnect.IsEnabled = false;

            var loggerDeviceCommunication = this._loggerFactory.CreateLogger<SerialPortDeviceCommunication>();
            var loggerEbaClient = this._loggerFactory.CreateLogger<Id003Client>();

            this._deviceCommunication = new SerialPortDeviceCommunication("COM4", 9600, Parity.Even, 8, StopBits.One, loggerDeviceCommunication);

            this._client = new Id003Client(this._deviceCommunication, loggerEbaClient);
            this._client.NewAmountInCashBox += this.NewAmountInCashBox;
            this._client.CashBoxReset += this.CashBoxReset;
            this._client.StatusChanged += this.StatusChanged;
            this._client.OperationStatusChanged += this.OperationStatusChanged;

            this.TextBoxStatus.Text = string.Empty;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this._client.NewAmountInCashBox -= this.NewAmountInCashBox;
            this._client.CashBoxReset -= this.CashBoxReset;
            this._client.StatusChanged -= this.StatusChanged;
            this._client.OperationStatusChanged -= this.OperationStatusChanged;

            this._client.Dispose();

            if (this._deviceCommunication.IsConnected)
            {
                this._deviceCommunication.DisconnectAsync().GetAwaiter();
            }
            this._deviceCommunication.Dispose();
        }

        private void NewAmountInCashBox(int amount)
        {
            this._amount += amount;
            this.UpdateAmount();
        }

        private void CashBoxReset()
        {
            this._amount = 0;
            this.UpdateAmount();
        }

        private void OperationStatusChanged(string status)
        {
            this.TextBoxOperationStatus.Dispatcher.BeginInvoke(() =>
            {
                this.TextBoxOperationStatus.Text = status;
            });
        }

        private void StatusChanged(string status)
        {
            this.TextBoxStatus.Dispatcher.BeginInvoke(() =>
            {
                this.TextBoxStatus.Text += $"{DateTime.Now:HH:mm:ss.fff} {status}\r\n";
            });
        }

        private void UpdateAmount()
        {
            this.TextBoxAmount.Dispatcher.BeginInvoke(() =>
            {
                this.TextBoxAmount.Text = $"{this._amount}";
            });
        }

        private async void ButtonInitialize_Click(object sender, RoutedEventArgs e)
        {
            await this._client.InitializeAsync();
        }

        private async void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            var successful = await this._deviceCommunication.ConnectAsync();
            if (successful)
            {
                this.ButtonConnect.IsEnabled = false;
                this.ButtonDisconnect.IsEnabled = true;
                this.StatusChanged("Connected");
                return;
            }
        }

        private async void ButtonDisconnect_Click(object sender, RoutedEventArgs e)
        {
            var successful = await this._deviceCommunication.DisconnectAsync();
            if (successful)
            {
                this.ButtonConnect.IsEnabled = true;
                this.ButtonDisconnect.IsEnabled = false;

                this.StatusChanged("Disconnected");
                return;
            }
        }

        private async void ButtonGetVersion_Click(object sender, RoutedEventArgs e)
        {
            var version = await this._client.GetVersionAsync();
            this.StatusChanged($"Version:{version}");
        }

        private async void ButtonGetBootVersion_Click(object sender, RoutedEventArgs e)
        {
            var version = await this._client.GetBootVersionAsync();
            this.StatusChanged($"BootVersion:{version}");
        }

        private async void ButtonEnable_Click(object sender, RoutedEventArgs e)
        {
            if (!await this._client.EnableAsync())
            {
                this.StatusChanged("Failure on enable");
            }
        }

        private async void ButtonDisable_Click(object sender, RoutedEventArgs e)
        {
            if (!await this._client.DisableAsync())
            {
                this.StatusChanged("Failure on disable");
            }
        }
    }
}
