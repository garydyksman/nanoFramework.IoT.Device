# LoRa (SX1262)

This folder contains the **Iot.Device.LoRa** binding for .NET nanoFramework and a sample for the Semtech SX1262 LoRa transceiver.

## Layout

- **`LoRa/`** — Library (`Iot.Device.LoRa`): `ILoRaDevice`, `Sx1262` driver, and related types.
- **`Sx1262Sample/`** — Sample app (ESP32 SPI wiring for Heltec Vision Master E213 style boards).
- **`LoraTests/`** — `LoRaTests` project: runs **hardware-free** checks (for example `Sx1262.DecodeChipMode`).

## Usage notes

After **`Reset()`**, call **`Initialize()`** once to run the full radio setup sequence (frequency, modulation, IRQ masks, and so on), then start receive polling or transmit as needed.
