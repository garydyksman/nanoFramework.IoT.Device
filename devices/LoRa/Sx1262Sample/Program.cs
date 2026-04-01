using Iot.Device.LoRa;
using Iot.Device.LoRa.Drivers.Sx1262;
using nanoFramework.Hardware.Esp32;
using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Diagnostics;
using System.Threading;
using System.Text;

namespace Sx1262Sample
{
    public class Program
    {
        // ---- SX1262 pin mapping (HT-VME213) — SPI1 ----
        private const int PinLoraMosi = 10;
        private const int PinLoraClk = 9;
        private const int PinLoraMiso = 11;
        private const int PinLoraCs = 8;
        private const int PinLoraRst = 12;
        private const int PinLoraBusy = 13;
        private const int PinLoraDio1 = 14;

        private static ILoRaDevice _lora;

        private static string _lastRx = "No RX yet";
        private static int _txCount = 0;
        private static string _statusMsg = "Ready";

        public static void Main()
        {
            var gpio = new GpioController();

            // ---- LoRa ----
            Configuration.SetPinFunction(PinLoraMosi, DeviceFunction.SPI1_MOSI);
            Configuration.SetPinFunction(PinLoraClk, DeviceFunction.SPI1_CLOCK);
            Configuration.SetPinFunction(PinLoraMiso, DeviceFunction.SPI1_MISO);

            var loraSpi = SpiDevice.Create(new SpiConnectionSettings(1, PinLoraCs)
            {
                ClockFrequency = 1000000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8
            });

            _lora = new Sx1262(
                loraSpi,
                resetPin: PinLoraRst,
                busyPin: PinLoraBusy,
                dio1Pin: PinLoraDio1,
                gpioController: gpio,
                shouldDispose: false);

            Debug.WriteLine("LoRa SX1262 sample starting...");
            _lora.Reset();

            Debug.WriteLine("Initialising LoRa...");
            _lora.Initialise();

            // PacketReceived is raised from a background thread,
            // so work can continue here without blocking the main thread.
            _lora.PacketReceived += OnPacketReceived;

            Debug.WriteLine("Starting LoRa receive polling...");
            _lora.StartPolling();

            // Send a message every 10 seconds from the main thread only,
            // to avoid concurrency issues with the SX1262 driver.
            while (true)
            {
                DoSend();
                Thread.Sleep(10000);
            }
        }

        // Called from main thread only
        private static void DoSend()
        {
            try
            {
                _txCount++;
                byte[] payload = Encoding.UTF8.GetBytes($"Hello from the .Net nanoFramework: {DateTime.UtcNow}");
                Debug.WriteLine("Sending: '" + Encoding.UTF8.GetString(payload, 0, payload.Length) + "'");

                _lora.Send(payload, 3000);

                Debug.WriteLine("Message sent. Whoo Hoo");
            }
            catch (Exception ex)
            {
                _statusMsg = "TX FAIL";
                Debug.WriteLine("TX failed: " + ex.Message);
            }
        }

        private static void OnPacketReceived(object sender, LoRaMessage msg)
        {
            string text = Encoding.UTF8.GetString(msg.Payload, 0, msg.Payload.Length);
            _lastRx = text + " (" + msg.Rssi + "dBm)";
            Debug.WriteLine("RX: '" + text + "' RSSI=" + msg.Rssi + "dBm SNR=" + msg.Snr + "dB");
        }
    }
}
