// Copyright (c) 2024 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Drawing;
using System.Threading;
using Iot.Device.EPaper;
using Iot.Device.EPaper.Buffers;
using Iot.Device.EPaper.Enums;
using Iot.Device.EPaper.Utilities;

namespace Iot.Device.EPaper.Drivers.Jd796xx.LcmEn2r13
{
    /// <summary>
    /// Driver for the LCMEN2R13EFC1 2.13" black-and-white e-paper display.
    /// </summary>
    /// <remarks>
    /// The visible panel is 122x250 pixels, while the controller RAM is byte-aligned to 128x250 pixels.
    /// This driver exposes the native controller RAM geometry so drawing and frame buffer layout remain consistent.
    /// </remarks>
    public class LcmEn2r13 : IEPaperDisplay
    {
        /// <summary>
        /// Native controller RAM width in pixels.
        /// </summary>
        public const int PanelWidth = 128;

        /// <summary>
        /// Native controller RAM height in pixels.
        /// </summary>
        public const int PanelHeight = 250;

        /// <summary>
        /// Frame buffer size in bytes for a 1-bit-per-pixel image.
        /// </summary>
        public const int FrameBufferSize = PanelWidth * PanelHeight / 8;

        /// <summary>
        /// Maximum SPI clock frequency in hertz.
        /// </summary>
        public const int SpiClockFrequency = 6_000_000;

        /// <summary>
        /// SPI mode used by the controller.
        /// </summary>
        public const SpiMode SpiMode = System.Device.Spi.SpiMode.Mode0;

        private readonly SpiDevice _spiDevice;
        private readonly GpioController _gpioController;
        private readonly bool _shouldDispose;
        private readonly bool _useBusyPin;
        private readonly byte[] _whiteFrame;

        private GpioPin _resetPin;
        private GpioPin _busyPin;
        private GpioPin _dataCommandPin;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="LcmEn2r13"/> class.
        /// </summary>
        /// <param name="spiDevice">SPI device used to communicate with the display.</param>
        /// <param name="resetPin">GPIO pin number for reset, or -1 if reset is not used.</param>
        /// <param name="busyPin">GPIO pin number for busy, or -1 if busy monitoring is not available.</param>
        /// <param name="dataCommandPin">GPIO pin number for data/command selection.</param>
        /// <param name="gpioController">GPIO controller instance, or null to create a new controller.</param>
        /// <param name="shouldDispose">True to dispose the provided GPIO controller when the driver is disposed.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="spiDevice"/> is null.</exception>
        public LcmEn2r13(
            SpiDevice spiDevice,
            int resetPin,
            int busyPin,
            int dataCommandPin,
            GpioController gpioController = null,
            bool shouldDispose = true)
        {
            if (spiDevice == null)
            {
                throw new ArgumentNullException(nameof(spiDevice));
            }

            _spiDevice = spiDevice;
            _gpioController = gpioController ?? new GpioController();
            _shouldDispose = shouldDispose || gpioController is null;

            if (resetPin >= 0)
            {
                _resetPin = _gpioController.OpenPin(resetPin, PinMode.Output);
                _resetPin.Write(PinValue.High);
            }

            _dataCommandPin = _gpioController.OpenPin(dataCommandPin, PinMode.Output);
            _dataCommandPin.Write(PinValue.Low);

            _useBusyPin = busyPin >= 0;
            if (_useBusyPin)
            {
                _busyPin = _gpioController.OpenPin(busyPin, PinMode.Input);
            }

            _whiteFrame = new byte[FrameBufferSize];
            for (int i = 0; i < _whiteFrame.Length; i++)
            {
                _whiteFrame[i] = 0xFF;
            }

            FrameBuffer1bpp = new FrameBuffer1BitPerPixel(PanelHeight, PanelWidth);
            FrameBuffer1bpp.Clear(Color.White);
        }

        /// <summary>
        /// Gets the native panel width in pixels.
        /// </summary>
        public int Width => PanelWidth;

        /// <summary>
        /// Gets the native panel height in pixels.
        /// </summary>
        public int Height => PanelHeight;

        /// <summary>
        /// Gets the active frame buffer used for drawing.
        /// </summary>
        public IFrameBuffer FrameBuffer => FrameBuffer1bpp;

        /// <summary>
        /// Gets a value indicating whether paged frame drawing is supported.
        /// </summary>
        public bool PagedFrameDrawEnabled => false;

        /// <summary>
        /// Gets the internal 1-bit-per-pixel frame buffer.
        /// </summary>
        protected FrameBuffer1BitPerPixel FrameBuffer1bpp { get; private set; }

        /// <summary>
        /// Begins a new frame draw operation by clearing the frame buffer to white.
        /// </summary>
        public void BeginFrameDraw()
        {
            FrameBuffer1bpp.Clear(Color.White);
        }

        /// <summary>
        /// Advances to the next frame page.
        /// </summary>
        /// <returns>Always returns false because paged drawing is not supported.</returns>
        public bool NextFramePage()
        {
            return false;
        }

        /// <summary>
        /// Ends the frame draw operation by flushing the frame buffer and refreshing the panel.
        /// </summary>
        public void EndFrameDraw()
        {
            Flush();
            PerformFullRefresh();
        }

        /// <summary>
        /// Draws a single pixel into the internal frame buffer using native panel coordinates.
        /// </summary>
        /// <param name="x">The horizontal pixel coordinate.</param>
        /// <param name="y">The vertical pixel coordinate.</param>
        /// <param name="color">The pixel color.</param>
        public void DrawPixel(int x, int y, Color color)
        {
            if (x < 0 || x >= PanelWidth || y < 0 || y >= PanelHeight)
            {
                return;
            }

            int bytesPerRow = PanelWidth / 8;
            int byteIndex = (y * bytesPerRow) + (x / 8);
            int bitMask = 0x80 >> (x & 7);

            if (color == Color.Black)
            {
                FrameBuffer1bpp.Buffer[byteIndex] &= (byte)~bitMask;
            }
            else
            {
                FrameBuffer1bpp.Buffer[byteIndex] |= (byte)bitMask;
            }
        }

        /// <summary>
        /// Flushes the internal frame buffer to display RAM.
        /// </summary>
        public void Flush()
        {
            PrepareForWrite();

            SendCommand(0x4E);
            SendData(0x01);

            SendCommand(0x4F);
            SendData(0xF9, 0x00);

            SendCommand(0x13);
            SendData(FrameBuffer1bpp.Buffer);

            SendCommand(0x10);
            SendData(_whiteFrame);
        }

        /// <summary>
        /// Sets the current drawing position.
        /// </summary>
        /// <param name="x">The horizontal position.</param>
        /// <param name="y">The vertical position.</param>
        /// <exception cref="NotImplementedException">Always thrown because positioned drawing is not implemented.</exception>
        public void SetPosition(int x, int y)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Performs a partial refresh of the display.
        /// </summary>
        /// <returns>True if the refresh command sequence was issued successfully.</returns>
        public bool PerformPartialRefresh()
        {
            return PerformFullRefresh();
        }

        /// <summary>
        /// Performs a hardware reset of the display.
        /// </summary>
        public void HardwareReset()
        {
            if (_resetPin == null)
            {
                return;
            }

            _resetPin.Write(PinValue.High);
            WaitMs(20);
            _resetPin.Write(PinValue.Low);
            WaitMs(20);
            _resetPin.Write(PinValue.High);
            WaitMs(100);
        }

        /// <summary>
        /// Powers on and initializes the display controller.
        /// </summary>
        public void PowerOn()
        {
            HardwareReset();

            SendCommand(0x12);
            WaitReady();

            SendCommand(0x00);
            SendData(0xDF);

            SendCommand(0x4D);
            SendData(0x55, 0x00, 0x00);

            SendCommand(0xA9);
            SendData(0x25, 0x00, 0x00);

            SendCommand(0xF3);
            SendData(0x0A, 0x00, 0x00);

            SendCommand(0x44);
            SendData(0x01, 0x0F);

            SendCommand(0x45);
            SendData(0xF9, 0x00, 0x00, 0x00);

            SendCommand(0x3C);
            SendData(0x01);

            SendCommand(0x18);
            SendData(0x80);

            SendCommand(0x4E);
            SendData(0x01);

            SendCommand(0x4F);
            SendData(0xF9, 0x00);

            WaitReady();
        }

        /// <summary>
        /// Clears the display buffer to white and optionally refreshes the panel.
        /// </summary>
        /// <param name="triggerPageRefresh">True to trigger a full panel refresh after clearing.</param>
        public void Clear(bool triggerPageRefresh = false)
        {
            FrameBuffer1bpp.Clear(Color.White);

            PrepareForWrite();

            SendCommand(0x4E);
            SendData(0x01);

            SendCommand(0x4F);
            SendData(0xF9, 0x00);

            SendCommand(0x13);
            SendData(_whiteFrame);

            SendCommand(0x10);
            SendData(_whiteFrame);

            if (triggerPageRefresh)
            {
                PerformFullRefresh();
            }
        }

        /// <summary>
        /// Performs a full display refresh.
        /// </summary>
        /// <returns>True if the refresh command sequence was issued successfully.</returns>
        public bool PerformFullRefresh()
        {
            SendCommand(0x04);
            WaitReady();
            WaitMs(100);

            SendCommand(0x12);
            WaitMs(100);
            WaitReady();

            SendCommand(0x02);
            WaitReady();
            WaitMs(100);

            return true;
        }

        /// <summary>
        /// Powers down the display and enters deep sleep mode.
        /// </summary>
        public void PowerDown()
        {
            SendCommand(0x02);
            WaitReady();

            SendCommand(0x07);
            SendData(0xA5);
        }

        /// <summary>
        /// Sends one or more command bytes to the display.
        /// </summary>
        /// <param name="command">The command bytes to send.</param>
        public void SendCommand(params byte[] command)
        {
            _dataCommandPin.Write(PinValue.Low);
            _spiDevice.Write(command);
        }

        /// <summary>
        /// Sends one or more data bytes to the display.
        /// </summary>
        /// <param name="data">The data bytes to send.</param>
        public void SendData(params byte[] data)
        {
            _dataCommandPin.Write(PinValue.High);
            _spiDevice.Write(data);
            _dataCommandPin.Write(PinValue.Low);
        }

        /// <summary>
        /// Sends one or more 16-bit data values to the display.
        /// </summary>
        /// <param name="data">The 16-bit data values to send.</param>
        public void SendData(params ushort[] data)
        {
            _dataCommandPin.Write(PinValue.High);
            _spiDevice.Write(data);
            _dataCommandPin.Write(PinValue.Low);
        }

        /// <summary>
        /// Waits until the display controller reports ready.
        /// If no busy pin is available, a fixed delay is used instead.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token for the wait operation.</param>
        /// <returns>True when the display is ready.</returns>
        public bool WaitReady(CancellationToken cancellationToken = default)
        {
            if (!_useBusyPin)
            {
                WaitMs(1500);
                return true;
            }

            return _busyPin.WaitUntilPinValueEquals(PinValue.High, cancellationToken);
        }

        /// <summary>
        /// Releases the resources used by the display driver.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Sends controller settings required before writing image data.
        /// </summary>
        private void PrepareForWrite()
        {
            SendCommand(0x50);
            SendData(0x97);

            SendCommand(0x60);
            SendData(0x22);

            SendCommand(0x61);
            SendData(0x80, 0xFA);

            SendCommand(0x82);
            SendData(0x13);

            SendCommand(0x30);
            SendData(0x1A);

            SendCommand(0xE3);
            SendData(0x88);

            SendCommand(0xF8);
            SendData(0x80);

            SendCommand(0xB3);
            SendData(0x42);

            SendCommand(0xB4);
            SendData(0x28);

            SendCommand(0xAA);
            SendData(0xB7);

            SendCommand(0xA8);
            SendData(0x3D);
        }

        /// <summary>
        /// Waits for the specified number of milliseconds.
        /// </summary>
        /// <param name="milliseconds">The delay in milliseconds.</param>
        protected void WaitMs(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }

        /// <summary>
        /// Releases managed resources used by the display driver.
        /// </summary>
        /// <param name="disposing">True to dispose managed resources.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _resetPin?.Dispose();
                    _resetPin = null;

                    _busyPin?.Dispose();
                    _busyPin = null;

                    _dataCommandPin?.Dispose();
                    _dataCommandPin = null;

                    _spiDevice?.Dispose();

                    if (_shouldDispose)
                    {
                        _gpioController?.Dispose();
                    }
                }

                _disposed = true;
            }
        }
    }
}