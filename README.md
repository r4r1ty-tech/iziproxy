# IziProxy

Кроссплатформенный инструмент на C# для автоматического развёртывания и управления прокси-серверами **Xray (VLESS + XHTTP + REALITY)** на чистых VDS/VPS (Ubuntu/Debian).

Графический интерфейс построен на **Avalonia UI** и запускается на Windows, Linux и Android из единой кодовой базы.

---

## Что умеет

1. **Подключается к VPS по SSH/SFTP** и загружает скрипты развёртывания.
2. **Запускает bash-скрипт** (`Deploy.sh`), который устанавливает Xray, включает TCP BBR, подбирает SNI-домены для маскировки прямо с сервера, настраивает firewall, генерирует пару x25519 и UUID.
3. **Генерирует `vless://`-ссылки и QR-коды** для каждого inbound, чтобы настроить клиент за секунды.
4. **Мониторит работающий сервер**: статус сервиса Xray, валидация конфига через `xray -test -config`, статистика трафика по inbound'ам через gRPC API Xray.

---

## Архитектура

```
IziProxy/
├── IziProxy.Core/               # Библиотека — вся доменная логика
│   ├── SSH.cs                   # SSH/SFTP-обёртка (Renci.SshNet)
│   ├── DeploySckripts.cs        # Загрузка скриптов, запуск деплоя, парсинг вывода
│   ├── XrayMonitor.cs           # Статус сервиса, проверка конфига, статистика трафика
│   ├── XrayConfigParams.cs      # Генерация ключей/UUID, модель SNI и портов
│   ├── VlessLinkGenerator.cs    # Сборка vless://-ссылок
│   ├── ServerConfig.cs          # Модель параметров подключения
│   └── VDS_setup/               # Deploy.sh, MainInstall.sh, шаблон config.json
│
├── IziProxy.GUI/
│   ├── IziProxy.GUI/            # Общая Avalonia UI (Views, ViewModels, ресурсы)
│   │   ├── ViewModels/          # Реактивные ViewModel на CommunityToolkit.Mvvm
│   │   ├── Views/               # AXAML-представления (Deploy, Dashboard, Logs)
│   │   └── VdsProfileService.cs # Сохранение профилей серверов (JSON, %localappdata%)
│   ├── IziProxy.GUI.Desktop/    # Точка входа — Windows / Linux / macOS
│   ├── IziProxy.GUI.Android/    # Точка входа — Android
│   └── IziProxy.GUI.Browser/    # Точка входа — WebAssembly (эксперимент)
│
├── IziProxy/                    # CLI-точка входа (консольный деплой)
└── tests/IziProxy.Tests/        # Тесты (xUnit)
```

### Основные зависимости

| Пакет | Назначение |
|---|---|
| `Avalonia` 11 | Кроссплатформенный UI-фреймворк |
| `CommunityToolkit.Mvvm` 8 | Source-генераторы для MVVM |
| `SSH.NET` 2025.1 | SSH/SFTP-клиент |
| `QRCoder` 1.8 | Офлайн-генерация QR-кодов |
| `Material.Icons.Avalonia` | Набор Material Design иконок |

---

## Требования

- **.NET 10 preview SDK** на машине для сборки.
- VPS на **Ubuntu 20.04+** или **Debian 11+** с доступом по SSH.

---

## Запуск

**Графическая версия (Desktop):**
```bash
dotnet run --project IziProxy.GUI/IziProxy.GUI.Desktop/IziProxy.GUI.Desktop.csproj
```

**Консольная версия (CLI):**
```bash
dotnet run --project IziProxy/IziProxy.csproj
```

---

## Руководство по экранам

### Вкладка Deploy

1. Введите IP сервера, SSH-пользователя и пароль, либо укажите путь к приватному ключу.
2. При желании сохраните профиль — данные записываются в `%localappdata%/IziProxy/profiles.json`.
3. Нажмите **Проверить соединение**, чтобы убедиться в доступности сервера до запуска тяжёлых скриптов.
4. Нажмите **Установить**. Вывод скрипта идёт в вкладку Logs. По завершении внизу появятся `vless://`-ссылки и QR-коды.

### Вкладка Dashboard

- Показывает статус сервиса Xray в реальном времени (`systemctl is-active xray`).
- Кнопка **Перезапустить**: `systemctl restart xray`.
- Кнопка **Проверить конфиг**: запускает `xray -test -config /etc/xray/config.json` и показывает ошибки.
- Секция **Трафик**: запрашивает `xray api statsquery` через gRPC и выводит счётчики uplink/downlink по каждому inbound.

---

## Тесты

```bash
dotnet test
```

Покрытие в [`tests/IziProxy.Tests/`](tests/IziProxy.Tests/):

| Файл | Что проверяется |
|---|---|
| `VlessLinkGeneratorTests.cs` | Корректная сборка `vless://`-ссылки |
| `ServerConfigTests.cs` | Валидация модели `ServerConfig` |
| `SshTests.cs` | Failure-пути SSH — возврат `false` / исключение при отсутствии подключения |
| `DeployScriptsTests.cs` | Поведение деплоя без активного соединения |
| `XrayConfigParamsTests.cs` | Модель параметров Xray |

---

## CI/CD

Сборки в GitHub Actions запускаются вручную (`workflow_dispatch`):

| Workflow | Runner | Результат |
|---|---|---|
| [linux-build.yml](.github/workflows/linux-build.yml) | `ubuntu-latest` | `IziProxy-Linux-x86_64.AppImage` → загружается в релиз `v1.0.0` |
| [android-build.yml](.github/workflows/android-build.yml) | `windows-latest` | Подписанный `.apk` → загружается в релиз `v1.0.0` |

Оба workflow используют `dotnet publish --self-contained` и публикуют артефакты через `gh release upload`.

---

## Про протокол

- **VLESS + XHTTP + REALITY** — трафик выглядит как обычная TLS HTTPS-сессия к реальному домену (SNI), что делает его трудно обнаруживаемым.
- Скрипт сам проверяет список доменов с сервера и выбирает лучший по задержке и параметрам TLS.
- Настраиваются три inbound (порты 443, 8443 и случайный) с разными SNI-доменами — у клиента три независимых варианта подключения.

---

## Лицензия

MIT — для образовательного и личного использования.
