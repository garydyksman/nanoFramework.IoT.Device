# LoRa (Semtech SX1262)

The [Semtech SX1262](https://www.semtech.com/products/wireless-rf/lora-connect/sx1262) is a long-range LoRa transceiver with sub-GHz RF front end, used on many LoRaWAN and point-to-point modules.

This folder contains the **Iot.Device.LoRa** binding for .NET nanoFramework: **`ILoRaDevice`** for common LoRa operations and **`Sx1262`** for SPI-connected SX1262 radios (reset, BUSY, DIO1 lines).

Official reference documentation: [SX1261/2 datasheet and application notes](https://www.semtech.com/products/wireless-rf/lora-connect/sx1262#documentation).

## Layout

- **`LoRa/`** — Library (`Iot.Device.LoRa`): `ILoRaDevice`, `Sx1262` driver, `LoRaMessage`, and related types.
- **`samples/Sx1262Sample/`** — ESP32 sample using SPI1 (Heltec Vision Master E213 style pinout). See [samples/Sx1262Sample](samples/Sx1262Sample).
- **`tests/`** — `LoRaTests` project: hardware-free unit tests (for example `Sx1262.DecodeChipMode`, `LoRaMessage` behavior). Build and run this project in the nanoFramework test runner.

## Wiring (sample / HT-VME213 style)

| SX1262 signal | ESP32 function (sample) | GPIO (sample) |
|---------------|-------------------------|-----------------|
| MOSI          | SPI1 MOSI               | 10              |
| MISO          | SPI1 MISO               | 11              |
| SCK           | SPI1 CLOCK              | 9               |
| NSS / CS      | SPI1 chip select        | 8               |
| RESET         | GPIO output             | 12              |
| BUSY          | GPIO input              | 13              |
| DIO1          | GPIO input (IRQ)        | 14              |

Use 3.3 V logic levels. Connect module GND and VCC per your board’s requirements.

## Minimal usage

1. Configure the MCU SPI pins (on ESP32, `Configuration.SetPinFunction` for the chosen bus).
2. Create an `SpiDevice` with the correct chip select.
3. Open **`Sx1262`**, then call **`Reset()`**, **`Initialize()`**, wire **`PacketReceived`**, and **`StartPolling()`** for receive; call **`Send(byte[], int)`** from a thread other than the poll thread.

```csharp
using Iot.Device.LoRa;
using Iot.Device.LoRa.Drivers.Sx1262;

// After SPI pin configuration and SpiDevice.Create(...):
using (Sx1262 lora = new Sx1262(spi, resetPin: 12, busyPin: 13, dio1Pin: 14, gpioController: gpio, shouldDispose: false))
{
    lora.Reset();
    lora.Initialize();
    lora.PacketReceived += (s, msg) => { /* handle msg.Payload, msg.Rssi, msg.Snr */ };
    lora.StartPolling();
    lora.Send(System.Text.Encoding.UTF8.GetBytes("Hello"), timeoutMs: 3000);
}
```

See **`samples/Sx1262Sample/Program.cs`** for a full ESP32 SPI1 example with periodic transmit and logging.

## Usage notes

After **`Reset()`**, call **`Initialize()`** once to run the full radio setup sequence (frequency, modulation, IRQ masks, and so on), then start receive polling or transmit as needed.
