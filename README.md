# Видеостример с веб-камеры (C# / .NET 10, Windows)

Решение из двух частей:
- **Streamer** (`src/Streamer`) — консольное приложение: захватывает видео с веб-камеры и звук с микрофона и передаёт их по сети (TCP).
- **Player** (`src/Player`) — отдельное приложение-плеер (WinForms) для просмотра потока.
- **StreamingPlayerLib** (`src/StreamingPlayerLib`) — библиотека с переиспользуемым WinForms-компонентом `StreamingPlayerLib.VideoPlayerControl`, который принимает и воспроизводит поток. Его можно встроить в другие программы (например, в SCADA/WinCC-хост на WinForms).

Протокол: TCP, заголовок `VS1\0`, далее пакеты `[тип:1 байт][длина:4][данные]` — JPEG-кадры (видео) и PCM16 (аудио) в одном соединении. Порт по умолчанию: `5000`.

## Требования

- Windows (захват камеры/микрофона через DirectShow, воспроизведение аудио через WaveOut).
- .NET 10 SDK для сборки.
- Веб-камера и (опционально) микрофон на машине стримера.
- Сеть между машинами (открыт TCP-порт).

## Структура проекта

```
VideoStreaming.slnx
src/
  StreamingPlayerLib/   # библиотека: StreamProtocol.cs, VideoPlayerControl.cs
  Streamer/             # консольный стример (камера+микрофон -> TCP)
  Player/               # WinForms-плеер, использует VideoPlayerControl
```

## Сборка

```powershell
dotnet restore VideoStreaming.slnx
dotnet build VideoStreaming.slnx -c Release
```

Артефакты:
- `src/Streamer/bin/Release/net10.0-windows/Streamer.exe`
- `src/Player/bin/Release/net10.0-windows/Player.exe`
- `src/StreamingPlayerLib/bin/Release/net10.0-windows/StreamingPlayerLib.dll`

## Запуск стримера (машина с камерой)

```powershell
Streamer.exe [порт] [индекс_камеры] [индекс_микрофона|-1]
```

Параметры (все необязательные, по умолчанию `5000 0 0`):

| Параметр | По умолчанию | Описание |
|---|---:|---|
| `порт` | 5000 | TCP-порт прослушивания |
| `индекс_камеры` | 0 | Номер камеры в системе (0 — первая) |
| `индекс_микрофона` | 0 | Номер устройства записи; `-1` — без звука (только видео) |

Примеры:

```powershell
Streamer.exe                 # порт 5000, камера 0, микрофон 0
Streamer.exe 6000 0 0        # свой порт
Streamer.exe 5000 1 0        # вторая камера
Streamer.exe 5000 0 -1       # только видео
```

Стример ждёт подключение клиента и начинает передачу. После отключения клиента снова ждёт следующего (по одному клиенту за раз). Остановить — `Ctrl+C`.

> На машине стримера разрешите входящие подключения к выбранному порту в брандмауэре Windows.

## Запуск плеера (машина просмотра)

1. Запустите `Player.exe`.
2. Укажите `Host` (IP/имя машины со стримером) и `Port` (по умолчанию `5000`).
3. Нажмите **Connect**. Статус и формат потока отображаются в панели. Повторное нажатие — отключение.

Плеер — тонкая оболочка над компонентом `VideoPlayerControl` из `StreamingPlayerLib`.

## Встраивание компонента в другое WinForms-приложение (например, WinCC)

Компонент воспроизведения вынесен в библиотеку `StreamingPlayerLib` и может использоваться независимо от `Player`.

1. Добавьте ссылку на проект/сборку `StreamingPlayerLib` (целевая платформа `net10.0-windows`, WinForms). NuGet-зависимость: `NAudio` (подтянется транзитивно).
2. Разместите контрол и подключитесь к стримеру:

```csharp
using StreamingPlayerLib;

// создание и размещение (в WinForms-контейнере/форме)
var view = new VideoPlayerControl { Dock = DockStyle.Fill };
hostForm.Controls.Add(view);

// подключение (async, вызывать из UI-контекста формы)
await view.ConnectAsync("192.168.1.10", StreamProtocol.DefaultPort);

// события (маршалятся через UI при необходимости внутри контрола)
view.StatusChanged += (_, status) => { /* обновить статус */ };
view.ErrorOccurred += (_, ex) => { /* обработать ошибку */ };

// отключение
view.Disconnect();

// при уничтожении хоста контрол освобождает ресурсы (Dispose/Disconnect)
```

Полезные члены: `IsConnected`, `ConnectAsync(host, port, ct)`, `Disconnect()`, `StatusChanged`, `ErrorOccurred`, `StreamProtocol.DefaultPort`.

## Настройка формата потока

Константы — в `src/StreamingPlayerLib/StreamProtocol.cs`: `DefaultPort` (5000), `VideoFps` (15), `JpegQuality` (70), `AudioSampleRate` (16000), `AudioChannels` (1), `AudioBits` (16). При изменении пересоберите стример и все клиенты/библиотеки одинаково.

## Ограничения

- Один клиент одновременно (последовательный приём подключений).
- Рассчитано на LAN: без шифрования и аутентификации.
- Формат фиксированный (MJPEG + PCM16 16 kHz mono), синхронизация аудио/видео best-effort (возможен небольшой дрейф).
- Требуется DirectShow-совместимая камера/микрофон на Windows. Для захвата используется `OpenCvSharp4` + `OpenCvSharp4.runtime.win`.
