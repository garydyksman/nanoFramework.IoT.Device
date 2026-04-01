// Copyright (c) 2024 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.

using System;

namespace Iot.Device.LoRa
{
    /// <summary>Received LoRa packet.</summary>
    public sealed class LoRaMessage
    {
        /// <summary>Raw payload bytes.</summary>
        public byte[] Payload { get; }

        /// <summary>Signal RSSI in dBm (typically -30 to -120).</summary>
        public int Rssi { get; }

        /// <summary>Signal-to-noise ratio in dB.</summary>
        public float Snr { get; }

        /// <summary>Creates a new LoRaMessage.</summary>
        public LoRaMessage(byte[] payload, int rssi, float snr)
        {
            Payload = payload;
            Rssi = rssi;
            Snr = snr;
        }
    }

    /// <summary>
    /// Delegate for packet received events.
    /// nanoFramework does not support generic delegates (EventHandler&lt;T&gt;).
    /// </summary>
    public delegate void PacketReceivedHandler(object sender, LoRaMessage message);

    /// <summary>
    /// Abstraction for a LoRa radio transceiver.
    /// Implement this interface for each chip variant (SX1262, SX1276, RFM95, …).
    /// </summary>
    public interface ILoRaDevice : IDisposable
    {
        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Performs a hardware reset and waits for the chip to become ready.
        /// Must be the first call after construction.
        /// </summary>
        void Reset();

        /// <summary>
        /// Applies the full initialisation sequence.
        /// Call once after <see cref="Reset"/>.
        /// </summary>
        void Initialise();

        // ------------------------------------------------------------------
        // Radio configuration
        // ------------------------------------------------------------------

        /// <summary>Sets the RF carrier frequency in Hz (e.g. 868_000_000).</summary>
        void SetRfFrequency(uint frequencyHz);

        // ------------------------------------------------------------------
        // Transmit
        // ------------------------------------------------------------------

        /// <summary>
        /// Sends <paramref name="payload"/> over LoRa.
        /// Blocks until TxDone or <paramref name="timeoutMs"/> elapses.
        /// </summary>
        /// <exception cref="TimeoutException">TX did not complete in time.</exception>
        void Send(byte[] payload, int timeoutMs);

        // ------------------------------------------------------------------
        // Receive
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts a background thread that polls for incoming packets and
        /// raises <see cref="PacketReceived"/> for each valid frame.
        /// Wire up <see cref="PacketReceived"/> before calling this.
        /// </summary>
        void StartPolling();

        /// <summary>Signals the poll thread to stop and blocks until it exits.</summary>
        void StopPolling();

        /// <summary>
        /// Raised on the poll thread when a valid packet is received.
        /// </summary>
        event PacketReceivedHandler PacketReceived;
    }
}
