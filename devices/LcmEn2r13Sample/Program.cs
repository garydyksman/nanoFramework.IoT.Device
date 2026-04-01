using Iot.Device.EPaper;
using Iot.Device.EPaper.Drivers.Jd796xx.LcmEn2r13;
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
        private const int PinDisplayMosi = 6;
        private const int PinDisplayClk = 4;
        private const int PinDisplayMiso = 7;   // not used (half-duplex) but must be assigned
        private const int PinDisplayCs = 5;
        private const int PinDisplayDc = 2;
        private const int PinDisplayRst = 3;
        private const int PinDisplayBusy = 1;
        private const int PinVext = 18;

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

            Configuration.SetPinFunction(PinDisplayMosi, DeviceFunction.SPI2_MOSI);
            Configuration.SetPinFunction(PinDisplayClk, DeviceFunction.SPI2_CLOCK);
            Configuration.SetPinFunction(PinDisplayMiso, DeviceFunction.SPI2_MISO);

            var displaySpi = SpiDevice.Create(new SpiConnectionSettings(2, PinDisplayCs)
            {
                ClockFrequency = LcmEn2r13.SpiClockFrequency,
                Mode = LcmEn2r13.SpiMode,
                ChipSelectLineActiveState = false,
                Configuration = SpiBusConfiguration.HalfDuplex,
                DataFlow = DataFlow.MsbFirst
            });

            var display = new LcmEn2r13(
                displaySpi,
                resetPin: PinDisplayRst,
                busyPin: PinDisplayBusy,
                dataCommandPin: PinDisplayDc,
                gpioController: gpio,
                shouldDispose: false);

            display.PowerOn();
            Debug.WriteLine("Power on done");

            display.Clear(triggerPageRefresh: true);
            Debug.WriteLine("Clear done");

            var font = new Font8x12();

            using var gfx = new Graphics(display)
            {
                DisplayRotation = Rotation.Degrees90Clockwise,
                FlipGlyphsHorizontally = true
            };

            display.BeginFrameDraw();

            gfx.DrawText("HELLO E213", font, 4, 6, Color.Black);
            gfx.DrawText("nanoFramework", font, 4, 22, Color.Black);
            gfx.DrawLine(0, 38, 120, 38, Color.Black);
            gfx.DrawRectangle(4, 46, 30, 18, Color.Black, false);
            gfx.DrawRectangle(40, 46, 30, 18, Color.Black, true);
            gfx.DrawCircle(20, 92, 12, Color.Black, false);
            gfx.DrawCircle(55, 92, 12, Color.Black, true);

            display.EndFrameDraw();
            Debug.WriteLine("Refresh done");

            Thread.Sleep(3000);

            // Clear only the regions that will change
            gfx.DrawRectangle(4, 46, 30, 18, Color.White, true);
            gfx.DrawRectangle(40, 46, 30, 18, Color.White, true);
            gfx.DrawRectangle(8, 80, 24, 24, Color.White, true);
            gfx.DrawRectangle(43, 80, 24, 24, Color.White, true);

            // Draw new state
            gfx.DrawRectangle(4, 46, 30, 18, Color.Black, true);
            gfx.DrawRectangle(40, 46, 30, 18, Color.Black, false);
            gfx.DrawCircle(20, 92, 12, Color.Black, true);
            gfx.DrawCircle(55, 92, 12, Color.Black, false);

            display.PerformPartialRefresh();
            Debug.WriteLine("Partial Refresh done");

            display.PowerDown();

            Thread.Sleep(Timeout.Infinite);
        }
    }
}