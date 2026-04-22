// Copyright (c) 2024 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.

namespace Iot.Device.LoRa
{
    /// <summary>
    /// <para>Represents the method that will handle packet received events.</para>
    /// <para>.NET nanoFramework does not support generic delegates such as EventHandler of T.</para>
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="message">The received message.</param>
    public delegate void PacketReceivedHandler(object sender, LoRaMessage message);
}
