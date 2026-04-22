// Copyright (c) 2024 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.

using System;

namespace Iot.Device.LoRa
{
    /// <summary>
    /// <para>Abstraction for a LoRa radio transceiver.</para>
    /// <para>Implement this interface for each chip variant (SX1262, SX1276, RFM95, and similar).</para>
    /// </summary>
    public interface ILoRaDevice : IDisposable
    {
        /// <summary>
        /// <para>Performs a hardware reset and waits for the chip to become ready.</para>
        /// <para>Must be the first call after construction.</para>
        /// </summary>
        void Reset();

        /// <summary>
        /// Applies the full initialization sequence. Call once after <see cref="ILoRaDevice.Reset" />.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Sets the RF carrier frequency in Hz (for example 868000000).
        /// </summary>
        /// <param name="frequencyHz">Carrier frequency in hertz.</param>
        void SetRfFrequency(uint frequencyHz);

        /// <summary>
        /// <para>Sends <paramref name="payload" /> over LoRa, blocking until TxDone or until <paramref name="timeoutMs" /> elapses.</para>
        /// <para>Do not call from <see cref="ILoRaDevice.PacketReceived" /> (the poll thread); implementations throw <see cref="InvalidOperationException" /> if invoked on that thread.</para>
        /// </summary>
        /// <param name="payload">The bytes to transmit.</param>
        /// <param name="timeoutMs">Maximum time to wait for completion, in milliseconds.</param>
        /// <exception cref="TimeoutException">Thrown when TX does not complete in time.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called from the RX poll thread.</exception>
        void Send(byte[] payload, int timeoutMs);

        /// <summary>
        /// <para>Starts a background thread that polls for incoming packets and raises <see cref="ILoRaDevice.PacketReceived" /> for each valid frame.</para>
        /// <para>Wire up <see cref="ILoRaDevice.PacketReceived" /> before calling this method.</para>
        /// </summary>
        void StartPolling();

        /// <summary>
        /// Signals the poll thread to stop and blocks until it exits.
        /// </summary>
        void StopPolling();

        /// <summary>
        /// Raised on the poll thread when a valid packet is received.
        /// </summary>
        event PacketReceivedHandler PacketReceived;
    }
}
