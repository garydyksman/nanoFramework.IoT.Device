// Copyright (c) 2024 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Threading;

using Iot.Device.LoRa;

namespace Iot.Device.LoRa.Drivers.Sx1262
{
    /// <summary>
    /// <para>Low-level SX1262 LoRa radio driver.</para>
    /// <para>Heltec Vision Master E213 (HT-VME213) default pin mapping: NSS = 8, SCK = 9, MOSI = 10, MISO = 11, RST = 12, BUSY = 13, DIO1 = 14.</para>
    /// <para>Supports reset, initialization, TX, RX polling, and buffer access.</para>
    /// </summary>
    public class Sx1262 : ILoRaDevice
    {
        // ---------------------------------------------------------------
        // Op-codes (datasheet section 11.1)
        // ---------------------------------------------------------------
        private const byte OpGetStatus = 0xC0;
        private const byte OpSetStandby = 0x80;
        private const byte OpSetSleep = 0x84;
        private const byte OpSetPacketType = 0x8A;
        private const byte OpSetRfFrequency = 0x86;
        private const byte OpSetTxParams = 0x8E;
        private const byte OpSetPaConfig = 0x95;
        private const byte OpSetModulationParams = 0x8B;
        private const byte OpSetPacketParams = 0x8C;
        private const byte OpSetBufferBaseAddr = 0x98;
        private const byte OpSetDioIrqParams = 0x08;
        private const byte OpGetIrqStatus = 0x12;
        private const byte OpClearIrqStatus = 0x02;
        private const byte OpSetRx = 0x82;
        private const byte OpSetTx = 0x83;
        private const byte OpWriteBuffer = 0x0E;
        private const byte OpReadBuffer = 0x1E;
        private const byte OpGetRxBufferStatus = 0x13;
        private const byte OpGetPacketStatus = 0x14;
        private const byte OpSetDio3AsTcxoCtrl = 0x97;
        private const byte OpSetDio2AsRfSwCtrl = 0x9D;
        private const byte OpSetRegulatorMode = 0x96;
        private const byte OpCalibrate = 0x89;

        // ---------------------------------------------------------------
        // IRQ bit masks (datasheet section 13.3.2)
        // ---------------------------------------------------------------
        private const ushort IrqTxDone = 0x0001;
        private const ushort IrqRxDone = 0x0002;
        private const ushort IrqCrcErr = 0x0040;
        private const ushort IrqTimeout = 0x0200;

        // ---------------------------------------------------------------
        // Hardware
        // ---------------------------------------------------------------
        private readonly SpiDevice _spi;
        private readonly GpioController _gpio;
        private readonly bool _shouldDispose;

        private GpioPin _resetPin;
        private GpioPin _busyPin;
        private GpioPin _dio1Pin;

        private bool _disposed;

        // ---------------------------------------------------------------
        // RX poll thread
        // ---------------------------------------------------------------
        private Thread _pollThread;
        private bool _stopPolling;

        // ---------------------------------------------------------------
        // Static helpers (SA1204: public static before non-static public)
        //// ---------------------------------------------------------------

        /// <summary>
        /// Decodes chip mode bits [6:4] from a raw status byte.
        /// </summary>
        /// <param name="status">The status byte returned by the chip.</param>
        /// <returns>A short label describing the chip mode.</returns>
        public static string DecodeChipMode(byte status)
        {
            switch ((byte)((status >> 4) & 0x07))
            {
                case 0x02:
                {
                    return "STDBY_RC";
                }

                case 0x03:
                {
                    return "STDBY_XOSC";
                }

                case 0x04:
                {
                    return "FS";
                }

                case 0x05:
                {
                    return "RX";
                }

                case 0x06:
                {
                    return "TX";
                }

                default:
                {
                    return "UNKNOWN";
                }
            }
        }

        // ---------------------------------------------------------------
        // Construction
        //// ---------------------------------------------------------------

        /// <summary>
        /// Initializes a new instance of the <see cref="Sx1262" /> class.
        /// </summary>
        /// <param name="spiDevice">The SPI device for the radio.</param>
        /// <param name="resetPin">GPIO pin number for reset (active low).</param>
        /// <param name="busyPin">GPIO pin number for the BUSY line.</param>
        /// <param name="dio1Pin">GPIO pin number for DIO1 (IRQ).</param>
        /// <param name="gpioController">Optional shared GPIO controller; a new instance is created when null.</param>
        /// <param name="shouldDispose">True to dispose the GPIO controller when this instance is disposed.</param>
        public Sx1262(
            SpiDevice spiDevice,
            int resetPin,
            int busyPin,
            int dio1Pin,
            GpioController gpioController = null,
            bool shouldDispose = true)
        {
            if (spiDevice == null)
            {
                throw new ArgumentNullException(nameof(spiDevice));
            }

            _spi = spiDevice;
            _gpio = gpioController == null ? new GpioController() : gpioController;
            _shouldDispose = shouldDispose || gpioController == null;

            _resetPin = _gpio.OpenPin(resetPin, PinMode.Output);
            _busyPin = _gpio.OpenPin(busyPin, PinMode.Input);
            _dio1Pin = _gpio.OpenPin(dio1Pin, PinMode.Input);

            _resetPin.Write(PinValue.High);
        }

        // ---------------------------------------------------------------
        // Events
        //// ---------------------------------------------------------------

        /// <inheritdoc/>
        public event PacketReceivedHandler PacketReceived;

        // ---------------------------------------------------------------
        // Step 1 — Reset + BUSY + GetStatus
        //// ---------------------------------------------------------------

        /// <inheritdoc/>
        public void Reset()
        {
            _resetPin.Write(PinValue.High);
            Thread.Sleep(10);
            _resetPin.Write(PinValue.Low);
            Thread.Sleep(100);
            _resetPin.Write(PinValue.High);
            Thread.Sleep(10);
            WaitBusy(5000);
        }

        /// <summary>
        /// Blocks until BUSY goes low or the timeout expires.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        public void WaitBusy(int timeoutMs)
        {
            int elapsed = 0;
            while (_busyPin.Read() == PinValue.High)
            {
                Thread.Sleep(1);
                if (++elapsed >= timeoutMs)
                {
                    throw new TimeoutException("SX1262 BUSY timeout");
                }
            }
        }

        /// <summary>
        /// Reads the chip status byte.
        /// </summary>
        /// <returns>The second byte of the status SPI transaction.</returns>
        public byte GetStatus()
        {
            WaitBusy(5000);
            byte[] tx = new byte[] { OpGetStatus, 0x00 };
            byte[] rx = new byte[2];
            _spi.TransferFullDuplex(tx, rx);
            return rx[1];
        }

        // ---------------------------------------------------------------
        // Step 2 — Full init sequence
        //// ---------------------------------------------------------------

        /// <inheritdoc/>
        public void Initialize()
        {
            WriteCommand(OpSetDio3AsTcxoCtrl, new byte[] { 0x02, 0x00, 0x01, 0x40 });
            WriteCommand(OpCalibrate, new byte[] { 0x7F });
            WaitBusy(3000);
            WriteCommand(OpSetDio2AsRfSwCtrl, new byte[] { 0x01 });
            WriteCommand(OpSetRegulatorMode, new byte[] { 0x01 });
            WriteCommand(OpSetStandby, new byte[] { 0x01 });
            WriteCommand(OpSetPacketType, new byte[] { 0x01 });
            SetRfFrequency(868000000);
            WriteCommand(OpSetPaConfig, new byte[] { 0x04, 0x07, 0x00, 0x01 });
            WriteCommand(OpSetTxParams, new byte[] { 0x0E, 0x04 });
            WriteCommand(OpSetModulationParams, new byte[] { 0x07, 0x04, 0x01, 0x00 });
            WriteCommand(OpSetPacketParams, new byte[] { 0x00, 0x08, 0x00, 0xFF, 0x01, 0x00 });
            WriteCommand(OpSetBufferBaseAddr, new byte[] { 0x00, 0x00 });
            byte[] irqParams = new byte[] { 0x02, 0x03, 0x02, 0x03, 0x00, 0x00, 0x00, 0x00 };
            WriteCommand(OpSetDioIrqParams, irqParams);
        }

        /// <inheritdoc/>
        public void SetRfFrequency(uint frequencyHz)
        {
            ulong frf = ((ulong)frequencyHz << 25) / 32000000UL;
            byte[] rfFreqBytes = new byte[]
            {
                (byte)(frf >> 24),
                (byte)(frf >> 16),
                (byte)(frf >> 8),
                (byte)frf
            };
            WriteCommand(OpSetRfFrequency, rfFreqBytes);
        }

        /// <summary>
        /// Puts the chip into continuous RX mode (timeout = 0xFFFFFF).
        /// </summary>
        /// <remarks>Called automatically by <see cref="Sx1262.StartPolling" /> and after transmit.</remarks>
        public void StartReceiving()
        {
            WriteCommand(OpSetRx, new byte[] { 0xFF, 0xFF, 0xFF });
        }

        // ---------------------------------------------------------------
        // Step 3 — TX
        //// ---------------------------------------------------------------

        /// <inheritdoc/>
        public void Send(byte[] payload, int timeoutMs)
        {
            if (payload == null || payload.Length == 0)
            {
                throw new ArgumentException("Payload cannot be null or empty");
            }

            if (payload.Length > 255)
            {
                throw new ArgumentException("Payload exceeds 255 bytes");
            }

            // Take SPI ownership away from the poll thread.
            StopPolling();

            try
            {
                byte[] packetParams = new byte[]
                {
                    0x00, 0x08, 0x00,
                    (byte)payload.Length,
                    0x01, 0x00
                };
                WriteCommand(OpSetPacketParams, packetParams);

                WriteBuffer(0x00, payload);
                WriteCommand(OpSetTx, new byte[] { 0x00, 0x00, 0x00 });

                int elapsed = 0;
                while (!IsDio1High)
                {
                    Thread.Sleep(1);
                    if (++elapsed >= timeoutMs)
                    {
                        throw new TimeoutException("SX1262 TxDone timeout");
                    }
                }

                ushort irq = GetIrqStatus();
                ClearIrqStatus(0xFFFF);

                if ((irq & IrqTimeout) != 0)
                {
                    throw new TimeoutException("SX1262 TX timeout IRQ");
                }

                if ((irq & IrqTxDone) == 0)
                {
                    throw new InvalidOperationException("Unexpected IRQ after TX");
                }
            }
            finally
            {
                // Always restart RX polling, even if TX threw.
                StartPolling();
            }
        }

        /// <summary>
        /// Writes bytes into the SX1262 data buffer at the given offset.
        /// </summary>
        /// <param name="offset">Start offset in the chip buffer.</param>
        /// <param name="data">Payload bytes to write.</param>
        public void WriteBuffer(byte offset, byte[] data)
        {
            WaitBusy(5000);
            byte[] tx = new byte[2 + data.Length];
            tx[0] = OpWriteBuffer;
            tx[1] = offset;
            Array.Copy(data, 0, tx, 2, data.Length);
            _spi.Write(tx);
        }

        // ---------------------------------------------------------------
        // Step 4 — RX
        //// ---------------------------------------------------------------

        /// <summary>
        /// Gets the RX buffer status after RxDone fires on DIO1.
        /// </summary>
        /// <param name="payloadLength">Receives the length of the received payload.</param>
        /// <param name="bufferOffset">Receives the buffer offset of the payload.</param>
        public void GetRxBufferStatus(out byte payloadLength, out byte bufferOffset)
        {
            byte[] r = ReadCommand(OpGetRxBufferStatus, 2);
            payloadLength = r[0];
            bufferOffset = r[1];
        }

        /// <summary>
        /// Reads <paramref name="length" /> bytes from the chip RX buffer starting at <paramref name="offset" />.
        /// </summary>
        /// <param name="offset">Offset in the RX buffer.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>A copy of the received bytes.</returns>
        public byte[] ReadBuffer(byte offset, byte length)
        {
            WaitBusy(5000);
            byte[] tx = new byte[3 + length];
            byte[] rx = new byte[3 + length];
            tx[0] = OpReadBuffer;
            tx[1] = offset;
            _spi.TransferFullDuplex(tx, rx);
            byte[] result = new byte[length];
            Array.Copy(rx, 3, result, 0, length);
            return result;
        }

        /// <summary>
        /// Gets the signal quality for the last received packet.
        /// </summary>
        /// <param name="rssi">Receives RSSI in dBm.</param>
        /// <param name="snr">Receives SNR in dB.</param>
        public void GetPacketStatus(out int rssi, out float snr)
        {
            byte[] r = ReadCommand(OpGetPacketStatus, 3);
            rssi = -(r[0] / 2);
            snr = ((sbyte)r[1]) / 4.0f;
        }

        /// <summary>
        /// Reads IRQ flags, pulls the packet from the buffer, raises <see cref="Sx1262.PacketReceived" />, then returns to RX mode.
        /// </summary>
        /// <returns>A <see cref="LoRaMessage" /> on success; null on CRC error or timeout.</returns>
        public LoRaMessage HandleRxDone()
        {
            ushort irq = GetIrqStatus();
            ClearIrqStatus(0xFFFF);

            if ((irq & IrqTimeout) != 0)
            {
                return null;
            }

            if ((irq & IrqCrcErr) != 0)
            {
                return null;
            }

            if ((irq & IrqRxDone) == 0)
            {
                return null;
            }

            GetRxBufferStatus(out byte length, out byte offset);
            byte[] payload = ReadBuffer(offset, length);

            GetPacketStatus(out int rssi, out float snr);
            LoRaMessage msg = new LoRaMessage(payload, rssi, snr);

            StartReceiving();

            if (PacketReceived != null)
            {
                PacketReceived(this, msg);
            }

            return msg;
        }

        // ---------------------------------------------------------------
        // RX poll thread (nanoFramework safe)
        //// ---------------------------------------------------------------

        /// <inheritdoc/>
        public void StartPolling()
        {
            if (_pollThread != null)
            {
                return;
            }

            _stopPolling = false;
            StartReceiving();

            _pollThread = new Thread(PollLoop);
            _pollThread.Start();
        }

        /// <inheritdoc/>
        public void StopPolling()
        {
            _stopPolling = true;
            Thread worker = _pollThread;
            if (worker == null)
            {
                return;
            }

            if (Thread.CurrentThread != worker)
            {
                worker.Join();
            }

            if (Thread.CurrentThread != worker && _pollThread == worker)
            {
                _pollThread = null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether DIO1 is high (an IRQ is pending).
        /// </summary>
        public bool IsDio1High => _dio1Pin.Read() == PinValue.High;

        /// <summary>
        /// Gets the current IRQ status flags from the chip.
        /// </summary>
        /// <returns>The 16-bit IRQ status flags.</returns>
        public ushort GetIrqStatus()
        {
            byte[] r = ReadCommand(OpGetIrqStatus, 2);
            return (ushort)((r[0] << 8) | r[1]);
        }

        /// <summary>
        /// Clears IRQ flags after handling.
        /// </summary>
        /// <param name="mask">Bits to clear in the IRQ status register.</param>
        public void ClearIrqStatus(ushort mask)
        {
            byte[] clearBytes = new byte[]
            {
                (byte)(mask >> 8),
                (byte)mask
            };
            WriteCommand(OpClearIrqStatus, clearBytes);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopPolling();

            if (_resetPin != null)
            {
                _resetPin.Dispose();
                _resetPin = null;
            }

            if (_busyPin != null)
            {
                _busyPin.Dispose();
                _busyPin = null;
            }

            if (_dio1Pin != null)
            {
                _dio1Pin.Dispose();
                _dio1Pin = null;
            }

            if (_shouldDispose && _gpio != null)
            {
                _gpio.Dispose();
            }

            _disposed = true;
        }

        private void PollLoop()
        {
            try
            {
                while (!_stopPolling)
                {
                    if (IsDio1High)
                    {
                        HandleRxDone();
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }
            }
            finally
            {
                if (_pollThread == Thread.CurrentThread)
                {
                    _pollThread = null;
                }
            }
        }

        internal void WriteCommand(byte opCode, byte[] data)
        {
            WaitBusy(5000);
            byte[] tx = new byte[1 + data.Length];
            tx[0] = opCode;
            Array.Copy(data, 0, tx, 1, data.Length);
            _spi.Write(tx);
        }

        internal byte[] ReadCommand(byte opCode, int responseLen)
        {
            WaitBusy(5000);
            byte[] tx = new byte[2 + responseLen];
            byte[] rx = new byte[2 + responseLen];
            tx[0] = opCode;
            _spi.TransferFullDuplex(tx, rx);
            byte[] result = new byte[responseLen];
            Array.Copy(rx, 2, result, 0, responseLen);
            return result;
        }
    }
}
