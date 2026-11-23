# Видеостример с веб-камеры (C# / .NET 10)

Решение состоит из:
- **Streamer** (`src/Streamer`) — консольное приложение: захватывает видео с веб-камеры и звук с микрофона и передаёт их по сети (TCP).
- **Player** (`src/Player`) — исходный плеер (WinForms, только Windows). Оставлен без изменений.
- **MauiPlayer** (`src/MauiPlayer`) — новый плеер на .NET MAUI для Android и Windows: видео + звук из того же потока.
- **StreamingPlayerLib** (`src/StreamingPlayerLib`) — библиотека с переиспользуемым WinForms-компонентом `StreamingPlayerLib.VideoPlayerControl`, который принимает и воспроизводит поток. Его можно встроить в другие программы (например, в SCADA/WinCC-хост на WinForms).

Протокол: TCP, заголовок `VS1\0`, далее пакеты `[тип:1 байт][длина:4][данные]` — JPEG-кадры (видео) и PCM16 (аудио) в одном соединении. Порт по умолчанию: `5000`.

## Требования

- Windows (захват камеры/микрофона через DirectShow, воспроизведение аудио через WaveOut) — только для Streamer и старого Player.
- .NET 10 SDK для сборки.
- Для MauiPlayer: workload `maui-android` и/или `maui-windows` (`dotnet workload install maui-android maui-windows`).
- Веб-камера и (опционально) микрофон на машине стримера.
- Сеть между машинами (открыт TCP-порт).

## Структура проекта

```
VideoStreaming.slnx
src/
  StreamingPlayerLib/   # библиотека: StreamProtocol.cs, VideoPlayerControl.cs
  Streamer/             # консольный стример (камера+микрофон -> TCP)
  Player/               # WinForms-плеер, использует VideoPlayerControl
  MauiPlayer/           # MAUI-плеер для Android и Windows
```

## Сборка

Для сборки всего решения нужны workload MAUI (см. выше); без них собирайте `Streamer`, `Player` и `StreamingPlayerLib` по отдельности.

```powershell
dotnet restore VideoStreaming.slnx
dotnet build VideoStreaming.slnx -c Release
```

Отдельно MAUI-плеер (нужны соответствующие workload):

```powershell
dotnet build src/MauiPlayer/MauiPlayer.csproj -c Release -f net10.0-android
dotnet build src/MauiPlayer/MauiPlayer.csproj -c Release -f net10.0-windows10.0.19041.0
```

Артефакты:
- `src/Streamer/bin/Release/net10.0-windows/Streamer.exe`
- `src/Player/bin/Release/net10.0-windows/Player.exe`
- `src/StreamingPlayerLib/bin/Release/net10.0-windows/StreamingPlayerLib.dll`
- `src/MauiPlayer/bin/Release/net10.0-android/.../MauiPlayer.apk`
- `src/MauiPlayer/bin/Release/net10.0-windows10.0.19041.0/.../MauiPlayer.exe`

## Запуск стримера (компьютер с камерой)

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

## Запуск плеера (компьютер просмотра)

1. Запустите `Player.exe`.
2. Укажите `Host` (IP/имя машины со стримером) и `Port` (по умолчанию `5000`).
3. Нажмите **Connect**. Статус и формат потока отображаются в панели. Повторное нажатие — отключение.

Плеер — тонкая оболочка над компонентом `VideoPlayerControl` из `StreamingPlayerLib`.

## Запуск MAUI-плеера (Android / Windows)

1. Установите и запустите приложение `MauiPlayer` на устройстве/эмуляторе Android или под Windows.
2. Укажите те же `Host` и `Port`, что и для старого плеера, и нажмите **Connect**.
3. Повторное нажатие отключает поток. Статус и формат потока показываются рядом.

Видео выводится через `Image` с MJPEG-кадрами (15 FPS), звук — через `AudioTrack` на Android и NAudio/WaveOut на Windows. Константы протокола (`src/StreamingPlayerLib/StreamProtocol.cs`) подключены в проект напрямую, чтобы старый плеер остался неизменным.

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
=======
# VideoStreamer
Stream video from one computer to other

