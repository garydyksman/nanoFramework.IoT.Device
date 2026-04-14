using Iot.Device.EPaper;
using Iot.Device.EPaper.Drivers.LcmEn2r13;
using Iot.Device.EPaper.Enums;
using Iot.Device.EPaper.Fonts;
using nanoFramework.Hardware.Esp32;
using System.Device.Gpio;
using System.Device.Spi;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace LcmEn2r13Sample
{
    /// <summary>
    /// Sample program for the Heltec Vision Master E213 e-paper display.
    /// </summary>
    public class Program
    {
        private const int PinMosi = 6;
        private const int PinClk = 4;
        private const int PinCs = 5;
        private const int PinDc = 2;
        private const int PinRst = 3;
        private const int PinVext = 18;
        private const int PinBusy = 1;
        /// <summary>
        /// Application entry point.
        /// </summary>
        public static void Main()
        {
            var gpio = new GpioController();

            gpio.OpenPin(PinVext, PinMode.Output);
            gpio.Write(PinVext, PinValue.High);
            Thread.Sleep(100);
            Debug.WriteLine("VEXT on");

            Configuration.SetPinFunction(43, DeviceFunction.COM1_TX);
            Configuration.SetPinFunction(44, DeviceFunction.COM1_RX);
            Configuration.SetPinFunction(PinMosi, DeviceFunction.SPI2_MOSI);
            Configuration.SetPinFunction(PinClk, DeviceFunction.SPI2_CLOCK);

            var spiSettings = new SpiConnectionSettings(2, PinCs)
            {
                ClockFrequency = LcmEn2r13.SpiClockFrequency,
                Mode = LcmEn2r13.SpiMode,
                ChipSelectLineActiveState = false,
                Configuration = SpiBusConfiguration.HalfDuplex,
                DataFlow = DataFlow.MsbFirst
            };

            using SpiDevice spiDevice = SpiDevice.Create(spiSettings);
            using var display = new LcmEn2r13(
                spiDevice,
                resetPin: PinRst,
                busyPin: PinBusy,
                dataCommandPin: PinDc,
                gpioController: gpio,
                shouldDispose: false);

            display.PowerOn();
            Debug.WriteLine("Power on done");

            display.Clear(triggerPageRefresh: true);
            Debug.WriteLine("Clear done");

            var font = new Font8x12();

            using var gfx = new Graphics(display)
            {
                DisplayRotation = Rotation.Degrees270Clockwise,
                FlipGlyphsHorizontally = true
            };

            bool fillFirstShape = false;
            bool firstFrame = true;
            while (true)
            {
                display.BeginFrameDraw();

                gfx.DrawText("HELLO E213", font, 4, 6, Color.Black);
                gfx.DrawText("nanoFramework", font, 4, 22, Color.Black);
                gfx.DrawLine(0, 38, 120, 38, Color.Black);

                gfx.DrawRectangle(4, 46, 30, 18, Color.Black, fillFirstShape);
                gfx.DrawRectangle(40, 46, 30, 18, Color.Black, !fillFirstShape);
                gfx.DrawCircle(20, 92, 12, Color.Black, fillFirstShape);
                gfx.DrawCircle(55, 92, 12, Color.Black, !fillFirstShape);

                display.Flush();
                if (firstFrame)
                {
                    display.PerformFullRefresh();
                    firstFrame = false;
                    Debug.WriteLine("Full refresh done");
                }
                else
                {
                    display.PerformPartialRefresh();
                    Debug.WriteLine("Partial refresh done");
                }

                fillFirstShape = !fillFirstShape;
                Thread.Sleep(1000);
            }
        }
    }
}
